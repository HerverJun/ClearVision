import { renderDiagnosticsCardsHtml } from '../inspection/analysisCardsPanel.js';
import {
    BLOB_PREVIEW_SEMANTICS_MESSAGE,
    formatPreviewOutputValue,
    getPreviewResultLabel,
    isPreviewImageLikePayload
} from './previewOutputFormatter.mjs';
import {
    MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS,
    MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES,
    STALE_PREVIEW_MESSAGE,
    buildOperatorResultViewModel,
    buildSafeJsonPreview,
    formatByteLength,
    formatResultArtifactMetadata,
    isTextArtifactForResultPanel,
    normalizeArtifactReference,
    redactLocalAbsolutePaths
} from './operatorResultViewModel.mjs';
import { buildRegionInputGuidance } from './regionInputGuidance.mjs';

const MAX_ANALYSIS_IMAGE_BASE64_CHARS = 24 * 1024 * 1024;

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function toPreviewDataUrl(imageBase64, maxChars = MAX_ANALYSIS_IMAGE_BASE64_CHARS) {
    if (typeof imageBase64 !== 'string' || imageBase64.length === 0) {
        return null;
    }

    if (Number.isFinite(maxChars) && maxChars > 0 && imageBase64.length > maxChars) {
        return null;
    }

    return `data:image/png;base64,${imageBase64}`;
}

export class PreviewPanel {
    constructor(container, options = {}) {
        this.container = container;
        this.getOperator = options.getOperator ?? (() => null);
        this.previewCoordinator = options.previewCoordinator ?? null;
        this.onOpenImage = options.onOpenImage ?? (() => {});
        this.onAnalyzePreview = options.onAnalyzePreview ?? null;
        this.onAutoTune = options.onAutoTune ?? null;
        this.validateBeforePreview = options.validateBeforePreview ?? (() => true);
        this.getFlowRevision = options.getFlowRevision ?? (() => this.state?.request?.flowRevision ?? 0);
        this.getNodes = options.getNodes ?? (() => []);
        this.getLiveNode = options.getLiveNode ?? (() => null);
        this.hasInputConnection = options.hasInputConnection ?? (() => false);
        this.onSelectNode = options.onSelectNode ?? null;
        this.debounceMs = options.debounceMs ?? 500;
        this.maxAnalysisImageBase64Chars = Number.isFinite(options.maxAnalysisImageBase64Chars)
            ? options.maxAnalysisImageBase64Chars
            : MAX_ANALYSIS_IMAGE_BASE64_CHARS;

        this.autoPreviewEnabled = true;
        this.collapsed = false;
        this.analysisResult = null;
        this.isAnalyzing = false;
        this.isAutoTuning = false;
        this.artifactReadAbort = null;
        this.artifactReadToken = 0;
        this.artifactReadState = new Map();
        this.resultIdentitySignature = '';
        this.state = this.previewCoordinator?.getState?.() ?? null;
        this.resultIdentitySignature = this._getStateIdentitySignature(this.state);
        this.unsubscribePreview = this.previewCoordinator?.subscribe?.(state => {
            this.state = state;
            this.applyPreviewState();
        }) || null;
        this.unsubscribeStructure = typeof options.subscribeStructureState === 'function'
            ? options.subscribeStructureState(() => {
                this.applyPreviewState();
            })
            : null;

        this.render();
        this.applyPreviewState();
    }

    destroy() {
        this.cancelArtifactRead();
        this.unsubscribePreview?.();
        this.unsubscribeStructure?.();
        this.unsubscribePreview = null;
        this.unsubscribeStructure = null;
        this.analysisResult = null;
        this.artifactReadState.clear();
        this._setOutputImage(null);
    }

    render() {
        if (!this.container) {
            return;
        }

        const operator = this.getOperator();
        const showWireSequenceActions = operator?.type === 'DetectionSequenceJudge';
        const analyzeLabel = this.isAnalyzing ? '分析中...' : '预览并分析';
        const autoTuneLabel = this.isAutoTuning ? '调参中...' : '一键自动调参';

        this.container.innerHTML = `
            <section class="operator-preview-panel ${this.collapsed ? 'collapsed' : ''}">
                <header class="operator-preview-header">
                    <button type="button" class="operator-preview-toggle" id="btn-preview-toggle">
                        ${this.collapsed ? '▶' : '▼'} 预览
                    </button>
                    <div class="operator-preview-actions">
                        <label class="operator-preview-auto">
                            <input type="checkbox" id="preview-auto-toggle" ${this.autoPreviewEnabled ? 'checked' : ''}/>
                            自动预览
                        </label>
                        ${showWireSequenceActions ? `
                            <button type="button" class="btn btn-secondary btn-preview-analyze" id="btn-preview-analyze" ${this.isAnalyzing || this.isAutoTuning ? 'disabled' : ''}>
                                ${analyzeLabel}
                            </button>
                            <button type="button" class="btn btn-secondary btn-preview-autotune" id="btn-preview-autotune" ${this.isAnalyzing || this.isAutoTuning ? 'disabled' : ''}>
                                ${autoTuneLabel}
                            </button>
                        ` : ''}
                        <button type="button" class="btn btn-secondary btn-preview-refresh" id="btn-preview-refresh">
                            刷新预览
                        </button>
                    </div>
                </header>

                <div class="operator-preview-body" id="operator-preview-body">
                    <div class="operator-preview-main-image" data-role="preview-main-image-container">
                        <div class="operator-preview-image-toolbar">
                            <span class="title">输出图像</span>
                            <button type="button" class="btn btn-secondary btn-sm" id="btn-preview-open-output" disabled>
                                打开大图
                            </button>
                        </div>
                        <img id="preview-output-image" alt="当前算子输出图像预览" data-role="preview-output-image" />
                        <div class="placeholder" id="preview-output-placeholder">暂无输出图像 / 该算子无图像输出</div>
                    </div>

                    <div class="operator-preview-region-guidance" id="preview-region-guidance" role="alert" hidden></div>

                    <div class="operator-preview-meta">
                        <div class="operator-preview-status" id="preview-status-text">等待预览</div>
                        <div class="operator-preview-outputs" id="preview-output-list">暂无输出摘要</div>
                        <div class="blob-preview-semantics" id="blob-preview-semantics" role="note" hidden></div>
                        <div class="operator-preview-diagnostics" id="preview-diagnostics-panel"></div>
                    </div>

                    <div class="operator-result-panel" id="operator-result-panel"></div>
                </div>
            </section>
        `;

        const toggleBtn = this.container.querySelector('#btn-preview-toggle');
        toggleBtn?.addEventListener('click', () => {
            this.collapsed = !this.collapsed;
            this.render();
            this.applyPreviewState();
        });

        const autoToggle = this.container.querySelector('#preview-auto-toggle');
        autoToggle?.addEventListener('change', event => {
            this.autoPreviewEnabled = Boolean(event.target.checked);
        });

        const refreshBtn = this.container.querySelector('#btn-preview-refresh');
        refreshBtn?.addEventListener('click', () => {
            this.refresh();
        });

        const analyzeBtn = this.container.querySelector('#btn-preview-analyze');
        analyzeBtn?.addEventListener('click', () => {
            this._handleAnalyzePreview();
        });

        const autoTuneBtn = this.container.querySelector('#btn-preview-autotune');
        autoTuneBtn?.addEventListener('click', () => {
            this._handleAutoTune();
        });

        const outputImage = this.container.querySelector('[data-role="preview-output-image"]');
        outputImage?.addEventListener('click', event => {
            const source = event.currentTarget.getAttribute('src');
            if (source) {
                this.onOpenImage(source);
            }
        });

        const openOutputBtn = this.container.querySelector('#btn-preview-open-output');
        openOutputBtn?.addEventListener('click', () => {
            const source = this.container?.querySelector('[data-role="preview-output-image"]')?.getAttribute('src');
            if (source) {
                this.onOpenImage(source);
            }
        });

        this._bindResultPanelEvents();
    }

    scheduleAutoPreview(options = {}) {
        if (!this.autoPreviewEnabled || this.collapsed) {
            return;
        }

        if (this._isCurrentOperatorDisabled()) {
            return;
        }

        if (this._getRegionInputGuidance(this.getOperator())) {
            this.applyPreviewState();
            return;
        }

        if (this.validateBeforePreview({ trigger: 'auto', showToast: false }) === false) {
            return;
        }

        const debounceMs = options.debounceMs ?? this.debounceMs;
        const force = Boolean(options.force);
        this.previewCoordinator?.requestActivePreview?.({
            immediate: false,
            force,
            debounceMs,
            trigger: 'auto'
        });
    }

    refresh() {
        if (this._isCurrentOperatorDisabled()) {
            this.applyPreviewState();
            return;
        }

        if (this._getRegionInputGuidance(this.getOperator())) {
            this.applyPreviewState();
            return;
        }

        if (this.validateBeforePreview({ trigger: 'manual', showToast: true }) === false) {
            return;
        }

        this.analysisResult = null;
        this.previewCoordinator?.requestActivePreview?.({
            immediate: true,
            force: true,
            trigger: 'manual'
        });
    }

    applyPreviewState() {
        this._resetArtifactReadsIfIdentityChanged();
        const operator = this.getOperator();
        const regionInputGuidance = this._getRegionInputGuidance(operator);
        this._setRegionInputGuidance(regionInputGuidance);
        if (regionInputGuidance) {
            this._setStatus(regionInputGuidance.title);
            this._setOutputImage(null, regionInputGuidance.summary);
            this._renderOutputs(null);
            this._renderOperatorResultPanel();
            return;
        }

        if (!operator || !this.state || this.state.activeNodeId !== operator.id) {
            this._setStatus(operator ? '该算子暂无预览结果' : '请选择一个算子节点查看模块结果');
            this._setOutputImage(null, operator ? '暂无输出图像 / 该算子无图像输出' : '请选择一个算子');
            this._renderOutputs(null);
            this._renderOperatorResultPanel();
            return;
        }

        if (this._isCurrentOperatorDisabled()) {
            this._setStatus('节点已禁用');
            this._setOutputImage(this.state.presenter?.outputImageSrc || null);
            this._renderOutputs(this.state.outputData);
            this._renderOperatorResultPanel();
            return;
        }

        const analysisResult = this.analysisResult?.targetNodeId === operator.id
            ? this.analysisResult
            : null;

        if (analysisResult) {
            const statusText = analysisResult.success
                ? (this.isAutoTuning ? '线序自动调参已完成' : '线序分析已完成')
                : (analysisResult.errorMessage || '线序分析未完成');
            this._setStatus(statusText);
            this._setOutputImage(analysisResult.previewImageSrc || this.state.presenter.outputImageSrc || null);
            this._renderOutputs(analysisResult.outputs || this.state.outputData);
            this._renderOperatorResultPanel();
            return;
        }

        const presenter = this.state.presenter;
        const resultViewModel = this._buildResultViewModel();
        this._setStatus(resultViewModel?.stale ? STALE_PREVIEW_MESSAGE : presenter.statusText);
        this._setOutputImage(presenter.outputImageSrc || null);
        this._renderOutputs(this.state.status === 'loading' ? null : this.state.outputData);
        this._setBlobPreviewSemantics(operator, this.state.status);
        this._renderOperatorResultPanel(resultViewModel);
    }

    _setStatus(text) {
        const statusElement = this.container?.querySelector('#preview-status-text');
        if (statusElement) {
            statusElement.textContent = text;
        }
    }

    _setBlobPreviewSemantics(operator, status) {
        const container = this.container?.querySelector('#blob-preview-semantics');
        if (!container) {
            return;
        }

        const isBlobPreview = operator?.type === 'BlobAnalysis' && status === 'success';
        container.hidden = !isBlobPreview;
        container.textContent = isBlobPreview
            ? BLOB_PREVIEW_SEMANTICS_MESSAGE
            : '';
    }

    _getRegionInputGuidance(operator = this.getOperator()) {
        const candidate = buildRegionInputGuidance(operator);
        if (!candidate || !operator?.id) {
            return candidate;
        }

        return this.hasInputConnection(operator.id, candidate.portIndex) ? null : candidate;
    }

    _setRegionInputGuidance(guidance) {
        const guidanceContainer = this.container?.querySelector('#preview-region-guidance');
        const refreshButton = this.container?.querySelector('#btn-preview-refresh');
        if (refreshButton) {
            refreshButton.disabled = Boolean(guidance);
            refreshButton.setAttribute?.('aria-disabled', guidance ? 'true' : 'false');
        }
        if (!guidanceContainer) {
            return;
        }

        guidanceContainer.hidden = !guidance;
        guidanceContainer.innerHTML = guidance
            ? `<strong>${escapeHtml(guidance.title)}</strong><ul>${guidance.lines.map(line => `<li>${escapeHtml(line)}</li>`).join('')}</ul>`
            : '';
    }

    _setImage(type, imageSource) {
        if (type === 'after' || type === 'output') {
            this._setOutputImage(imageSource);
        }
    }

    _setOutputImage(imageSource, placeholderText = '暂无输出图像 / 该算子无图像输出') {
        const image = this.container?.querySelector('#preview-output-image');
        const placeholder = this.container?.querySelector('#preview-output-placeholder');
        const openButton = this.container?.querySelector('#btn-preview-open-output');

        if (!image || !placeholder) {
            return;
        }

        if (!imageSource) {
            image.removeAttribute('src');
            image.style.display = 'none';
            placeholder.style.display = 'flex';
            placeholder.textContent = placeholderText;
            if (openButton) {
                openButton.disabled = true;
            }
            return;
        }

        if (image.getAttribute('src') !== imageSource) {
            image.src = imageSource;
        }
        image.style.display = 'block';
        placeholder.style.display = 'none';
        if (openButton) {
            openButton.disabled = false;
        }
    }

    _renderOutputs(outputs) {
        const outputContainer = this.container?.querySelector('#preview-output-list');
        const diagnosticsContainer = this.container?.querySelector('#preview-diagnostics-panel');
        if (!outputContainer) {
            return;
        }

        if (!outputs || typeof outputs !== 'object' || Object.keys(outputs).length === 0) {
            outputContainer.textContent = '暂无输出摘要';
            if (diagnosticsContainer) {
                diagnosticsContainer.innerHTML = '';
            }
            return;
        }

        const items = Object.entries(outputs)
            .filter(([key, value]) => ![
                'image',
                'originalimage',
                'diagnostics',
                'data',
                'output',
                'filepath',
                'text',
                'detectionlist',
                'objects',
                'defects',
                'rawcandidatecount',
                'visualizationdetectioncount',
                'internalnmsenabled'
            ].includes(String(key).toLowerCase()) && !(typeof value === 'string' && isPreviewImageLikePayload(value)))
            .slice(0, 8)
            .map(([key, value]) => {
            const formattedValue = formatPreviewOutputValue(key, value, {
                stringMaxLength: 48
            });
            const titleAttribute = formattedValue.title
                ? ` title="${escapeHtml(formattedValue.title)}"`
                : '';

            return `
                <div class="operator-preview-output-item" data-output-kind="${formattedValue.kind}">
                    <span class="key">${escapeHtml(getPreviewResultLabel(key))}</span>
                    <span class="value"${titleAttribute}>${escapeHtml(formattedValue.text)}</span>
                </div>
            `;
        });

        outputContainer.innerHTML = items.length > 0 ? items.join('') : '暂无输出摘要';

        if (diagnosticsContainer) {
            diagnosticsContainer.innerHTML = renderDiagnosticsCardsHtml(outputs, 'OK', {
                compact: true,
                containerClass: 'analysis-cards-container ac-diagnostics-inline ac-diagnostics-preview'
            });
        }
    }

    _buildResultViewModel() {
        const operator = this.getOperator();
        const liveNode = operator?.id ? this.getLiveNode(operator.id) : null;
        return buildOperatorResultViewModel(operator, this.state, {
            liveNode,
            flowRevision: this.getFlowRevision(),
            getNodes: () => this._getFlowNodes()
        });
    }

    _getFlowNodes() {
        const nodes = this.getNodes();
        if (Array.isArray(nodes)) {
            return nodes;
        }

        if (nodes instanceof Map) {
            return Array.from(nodes.values());
        }

        if (nodes?.values && typeof nodes.values === 'function') {
            return Array.from(nodes.values());
        }

        return [];
    }

    _isCurrentOperatorDisabled() {
        const operator = this.getOperator();
        const liveNode = operator?.id ? this.getLiveNode(operator.id) : null;
        return Boolean(liveNode?.disabled ?? liveNode?.Disabled ?? operator?.disabled ?? operator?.Disabled);
    }

    _renderOperatorResultPanel(viewModel = null) {
        const container = this.container?.querySelector('#operator-result-panel');
        if (!container) {
            return;
        }

        const model = viewModel || this._buildResultViewModel();
        container.innerHTML = `
            <section class="operator-result-surface" data-status="${escapeHtml(model.status)}">
                <header class="operator-result-surface-header">
                    <div>
                        <div class="operator-result-title">模块结果</div>
                        <div class="operator-result-subtitle">${escapeHtml(model.operatorName || '未选择算子')}</div>
                    </div>
                    <span class="operator-result-status" data-status="${escapeHtml(model.status)}">${escapeHtml(model.statusText)}</span>
                </header>
                <div class="operator-result-state" data-status="${escapeHtml(model.status)}">${escapeHtml(model.stateMessage)}</div>
                ${this._renderOverview(model)}
                ${this._renderOutputSections(model)}
                ${this._renderSceneSection(model)}
                ${this._renderDiagnostics(model)}
            </section>
        `;
        this._bindResultPanelEvents();
    }

    _renderOverview(model) {
        const rows = (model.executionSummaryItems || [])
            .filter(item => item.value !== null && item.value !== undefined && item.value !== '')
            .map(item => `
                <div class="operator-result-kv">
                    <span>${escapeHtml(item.label)}</span>
                    <strong>${escapeHtml(item.value)}</strong>
                </div>
            `)
            .join('');

        return `
            <section class="operator-result-section">
                <h5>结果摘要</h5>
                <div class="operator-result-kv-grid">${rows}</div>
            </section>
        `;
    }

    _renderNodeResultList(model) {
        if (!Array.isArray(model.nodeResults) || model.nodeResults.length === 0) {
            return `
                <section class="operator-result-section">
                    <h5>节点结果</h5>
                    <div class="operator-result-empty">暂无流程节点</div>
                </section>
            `;
        }

        const items = model.nodeResults.map(item => `
            <button type="button"
                    class="operator-result-node-item"
                    data-node-select="${escapeHtml(item.nodeId)}"
                    data-selected="${item.selected ? 'true' : 'false'}"
                    data-status="${escapeHtml(item.statusKind)}">
                <span class="operator-result-node-index">${String(item.index + 1).padStart(2, '0')}</span>
                <span class="operator-result-node-main">
                    <span class="operator-result-node-title">${escapeHtml(item.title)}</span>
                    <span class="operator-result-node-type">${escapeHtml(item.type || '-')}</span>
                </span>
                <span class="operator-result-node-status">${escapeHtml(item.statusText)}</span>
            </button>
        `).join('');

        return `
            <section class="operator-result-section">
                <h5>节点结果</h5>
                <div class="operator-result-node-list">${items}</div>
            </section>
        `;
    }

    _renderOutputSections(model) {
        if (model.status === 'loading') {
            return `
                <section class="operator-result-section">
                    <h5>关键输出</h5>
                    <div class="operator-result-empty">预览运行中...</div>
                </section>
            `;
        }

        if (!Array.isArray(model.keyOutputs) || model.keyOutputs.length === 0) {
            return `
                <section class="operator-result-section">
                    <h5>关键输出</h5>
                    <div class="operator-result-empty">${model.status === 'success'
                        ? '执行成功，但没有可展示的关键输出；可在高级诊断中查看原始结果。'
                        : '暂无可展示的关键输出'}</div>
                </section>
            `;
        }

        const rows = model.keyOutputs.map(item => `
            <div class="operator-result-output-row" data-output-kind="${escapeHtml(item.kind || 'value')}">
                <span class="operator-result-output-key">${escapeHtml(item.label || item.key || '-')}</span>
                <span class="operator-result-output-value" title="${escapeHtml(item.title || item.value || '')}">${escapeHtml(item.value || '-')}</span>
                <span class="operator-result-output-meta">${escapeHtml(item.meta || (item.declared ? '声明输出' : item.resultPath || ''))}</span>
            </div>
        `).join('');

        return `
            <section class="operator-result-section">
                <h5>关键输出</h5>
                <div class="operator-result-output-group" data-output-group="key-output">${rows}</div>
            </section>
        `;
    }

    _renderSceneSection(model) {
        const scene = model.sceneSummary || {};
        const imageItems = Array.isArray(model.imageSummaries) ? model.imageSummaries : [];
        if (!scene.available && imageItems.length === 0) {
            return `
                <section class="operator-result-section">
                    <h5>图像与附件</h5>
                    <div class="operator-result-empty">${escapeHtml(scene.message || '暂无图像/区域附件')}</div>
                </section>
            `;
        }

        const attachments = imageItems.map(item => {
            const readState = item.artifact?.artifactId
                ? this.artifactReadState.get(item.artifact.artifactId)
                : null;
            return `
                <div class="operator-result-artifact" data-artifact-id="${escapeHtml(item.artifact?.artifactId || '')}">
                    <div class="operator-result-artifact-main">
                        <strong>${escapeHtml(item.label || '图像/附件')}</strong>
                        <span>${escapeHtml(item.summary || '图像内容已省略')}</span>
                        <span>${escapeHtml(item.contentType || item.kind || '')}</span>
                    </div>
                    ${item.artifact?.artifactId ? `
                        <button type="button"
                                class="operator-result-artifact-read"
                                data-artifact-read="${escapeHtml(item.artifact.artifactId)}"
                                ${readState?.status === 'loading' ? 'disabled' : ''}>
                            ${readState?.status === 'loading' ? '读取中' : '查看摘要'}
                        </button>
                    ` : ''}
                    ${readState ? `<pre class="operator-result-artifact-preview ${escapeHtml(readState.status)}">${escapeHtml(readState.text)}</pre>` : ''}
                </div>
            `;
        }).join('');

        const primitives = (scene.primitives || []).map(item => `
            <div class="operator-result-scene-row">
                <span>${escapeHtml(item.label || item.primitiveId || item.kind)}</span>
                <span>${escapeHtml(item.kind || '区域')}</span>
                <span>${escapeHtml(item.resultPath || item.layer || '')}</span>
            </div>
        `).join('');
        const sceneSummary = scene.available
            ? `<div class="operator-result-output-heading">区域/叠加：${escapeHtml(scene.primitiveCount ?? scene.primitives?.length ?? 0)} 项${scene.imageSize ? ` · ${escapeHtml(scene.imageSize)}` : ''}</div>`
            : '';

        return `
            <section class="operator-result-section">
                <h5>图像与附件</h5>
                ${attachments ? `<div class="operator-result-artifact-list">${attachments}</div>` : ''}
                ${sceneSummary}
                ${primitives ? `<div class="operator-result-scene-list">${primitives}</div>` : ''}
            </section>
        `;
    }

    _renderArtifacts(model) {
        if (!Array.isArray(model.artifacts) || model.artifacts.length === 0) {
            return `
                <section class="operator-result-section">
                    <h5>证据</h5>
                    <div class="operator-result-empty">暂无 Artifact</div>
                </section>
            `;
        }

        const rows = model.artifacts.map(artifact => {
            const readState = this.artifactReadState.get(artifact.artifactId);
            const dimensions = artifact.width && artifact.height
                ? `${artifact.width} x ${artifact.height}${artifact.channels ? ` x ${artifact.channels}` : ''}`
                : '';
            return `
                <div class="operator-result-artifact" data-artifact-id="${escapeHtml(artifact.artifactId)}">
                    <div class="operator-result-artifact-main">
                        <strong>${escapeHtml(artifact.role || artifact.kind || 'artifact')}</strong>
                        <span>${escapeHtml(artifact.kind || '-')} · ${escapeHtml(artifact.contentType || '-')} · ${escapeHtml(formatByteLength(artifact.length))}</span>
                        <span>${escapeHtml(dimensions || artifact.createdAtUtc || artifact.expiresAtUtc || '')}</span>
                    </div>
                    <button type="button"
                            class="operator-result-artifact-read"
                            data-artifact-read="${escapeHtml(artifact.artifactId)}"
                            ${readState?.status === 'loading' ? 'disabled' : ''}>
                        ${readState?.status === 'loading' ? '读取中' : '查看摘要'}
                    </button>
                    ${readState ? `<pre class="operator-result-artifact-preview ${escapeHtml(readState.status)}">${escapeHtml(readState.text)}</pre>` : ''}
                </div>
            `;
        }).join('');

        return `
            <section class="operator-result-section">
                <h5>证据</h5>
                <div class="operator-result-artifact-list">${rows}</div>
            </section>
        `;
    }

    _renderDiagnostics(model) {
        const diagnostics = Array.isArray(model.advancedDiagnostics) ? model.advancedDiagnostics : [];
        const diagnosticRows = diagnostics.map(item => `
            <div class="operator-result-diagnostic">
                <span>${escapeHtml(item.label || item.code || item.source || '诊断')}</span>
                <strong>${escapeHtml(item.message || '')}</strong>
                <small>${escapeHtml(item.pathHint || item.source || '')}</small>
            </div>
        `).join('');
        const rawSections = (model.rawDataSections || []).map(section => `
            <div class="operator-result-output-group" data-output-group="${escapeHtml(section.kind)}">
                <div class="operator-result-output-heading">${escapeHtml(section.label)}${section.omittedCount > 0 ? ` · 已折叠 ${escapeHtml(section.omittedCount)} 项` : ''}</div>
                ${section.items.map(item => `
                    <div class="operator-result-output-row">
                        <span class="operator-result-output-key">${escapeHtml(item.label || '-')}</span>
                        <span class="operator-result-output-value">${escapeHtml(item.value || '-')}</span>
                        <span class="operator-result-output-meta">${escapeHtml(item.meta || '')}</span>
                    </div>
                `).join('')}
            </div>
        `).join('');
        const rawJson = model.rawJsonPreview?.text
            ? `<pre class="operator-result-raw-json">${escapeHtml(model.rawJsonPreview.text)}</pre>`
            : '<div class="operator-result-empty">暂无原始 JSON</div>';

        return `
            <details class="operator-result-section operator-result-advanced">
                <summary>
                    <span>高级诊断</span>
                    <em>${escapeHtml(diagnostics.length)} 条诊断 · ${escapeHtml((model.rawDataSections || []).length)} 组原始数据</em>
                </summary>
                ${diagnosticRows
                    ? `<div class="operator-result-diagnostic-list">${diagnosticRows}</div>`
                    : '<div class="operator-result-empty">暂无诊断信息</div>'}
                ${rawSections}
                <div class="operator-result-output-heading">原始数据摘要</div>
                ${rawJson}
            </details>
        `;
    }

    _renderRawJson(model) {
        const text = model.rawJsonPreview?.text || '';
        if (!text) {
            return `
                <section class="operator-result-section">
                    <h5>Raw JSON</h5>
                    <div class="operator-result-empty">暂无 raw JSON</div>
                </section>
            `;
        }

        return `
            <section class="operator-result-section">
                <h5>Raw JSON</h5>
                <pre class="operator-result-raw-json">${escapeHtml(text)}</pre>
            </section>
        `;
    }

    _bindResultPanelEvents() {
        const container = this.container?.querySelector('#operator-result-panel');
        if (!container) {
            return;
        }

        container.querySelectorAll('[data-node-select]').forEach(button => {
            button.addEventListener('click', event => {
                event.preventDefault();
                const nodeId = event.currentTarget.getAttribute('data-node-select');
                if (nodeId && typeof this.onSelectNode === 'function') {
                    this.onSelectNode(nodeId);
                }
            });
        });

        container.querySelectorAll('[data-artifact-read]').forEach(button => {
            button.addEventListener('click', event => {
                event.preventDefault();
                const artifactId = event.currentTarget.getAttribute('data-artifact-read');
                void this.readArtifactPreview(artifactId);
            });
        });
    }

    _findArtifact(artifactId) {
        const safeArtifactId = String(artifactId || '');
        const artifacts = Array.isArray(this.state?.artifacts) ? this.state.artifacts : [];
        return artifacts.find(artifact => artifact?.artifactId === safeArtifactId) || null;
    }

    _getObservationIdentity() {
        const observation = this.state?.observation;
        const identity = observation?.identity || observation?.Identity;
        return identity && typeof identity === 'object' ? identity : null;
    }

    _getStateIdentitySignature(state) {
        const observation = state?.observation;
        const identity = observation?.identity || observation?.Identity || {};
        return [
            state?.activeNodeId || '',
            state?.status || '',
            state?.request?.requestKey || '',
            identity.projectId || identity.ProjectId || '',
            identity.targetNodeId || identity.TargetNodeId || '',
            identity.debugSessionId || identity.DebugSessionId || '',
            identity.clientRequestSequence || identity.ClientRequestSequence || '',
            identity.flowRevision || identity.FlowRevision || ''
        ].join('|');
    }

    _resetArtifactReadsIfIdentityChanged() {
        const nextSignature = this._getStateIdentitySignature(this.state);
        if (nextSignature === this.resultIdentitySignature) {
            return;
        }

        this.resultIdentitySignature = nextSignature;
        this.artifactReadState.clear();
        this.cancelArtifactRead();
    }

    cancelArtifactRead() {
        this.artifactReadAbort?.abort?.();
        this.artifactReadAbort = null;
        this.artifactReadToken += 1;
    }

    _isArtifactReadCurrent(token, identity, abortController = null) {
        return token === this.artifactReadToken &&
            abortController?.signal?.aborted !== true &&
            JSON.stringify(this._getObservationIdentity() || {}) === JSON.stringify(identity || {});
    }

    _isArtifactUnavailableError(error) {
        return error?.status === 404 ||
            error?.statusCode === 404 ||
            error?.name === 'PreviewArtifactUnavailableError' ||
            /过期|不可用|stale|missing|not found|404/i.test(String(error?.message || ''));
    }

    async readArtifactPreview(artifactId) {
        const artifact = normalizeArtifactReference(this._findArtifact(artifactId));
        const identity = this._getObservationIdentity();
        if (!artifact?.artifactId || !identity) {
            return;
        }

        this.cancelArtifactRead();
        const token = this.artifactReadToken;

        if (!isTextArtifactForResultPanel(artifact)) {
            this.artifactReadState.set(artifact.artifactId, {
                status: 'success',
                text: formatResultArtifactMetadata(artifact, '非文本 Artifact，仅展示元数据')
            });
            this._renderOperatorResultPanel();
            return;
        }

        if (artifact.length > MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES) {
            this.artifactReadState.set(artifact.artifactId, {
                status: 'success',
                text: formatResultArtifactMetadata(artifact, '内容过大，仅展示元数据')
            });
            this._renderOperatorResultPanel();
            return;
        }

        const abortController = typeof AbortController !== 'undefined'
            ? new AbortController()
            : null;
        this.artifactReadAbort = abortController;
        this.artifactReadState.set(artifact.artifactId, {
            status: 'loading',
            text: '正在按需读取 Artifact...'
        });
        this._renderOperatorResultPanel();

        try {
            const result = await this.previewCoordinator?.readArtifactForCurrentState?.(
                artifact.artifactId,
                identity,
                { signal: abortController?.signal });
            if (!this._isArtifactReadCurrent(token, identity, abortController)) {
                return;
            }

            const artifactMetadata = normalizeArtifactReference(result?.artifact) || artifact;
            const blob = result?.blob;
            if (!blob || typeof blob.slice !== 'function') {
                throw new Error('Artifact Blob 不支持有界文本预览');
            }

            const actualSize = Number(blob.size ?? artifactMetadata.length ?? 0);
            const actualTextTooLarge = Number.isFinite(actualSize) &&
                actualSize > MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES;
            const previewBlob = blob.slice(0, MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES);
            const rawText = await previewBlob.text();
            if (!this._isArtifactReadCurrent(token, identity, abortController)) {
                return;
            }

            let previewText = rawText;
            if (String(artifactMetadata.contentType || '').toLowerCase().includes('json')) {
                try {
                    previewText = buildSafeJsonPreview(JSON.parse(rawText), {
                        maxChars: MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS
                    }).text;
                } catch {
                    previewText = redactLocalAbsolutePaths(rawText);
                }
            } else {
                previewText = redactLocalAbsolutePaths(rawText);
            }

            const displayTruncated = actualTextTooLarge ||
                previewText.length > MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS;
            const boundedText = displayTruncated
                ? `${previewText.slice(0, MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS)}\n已截断。`
                : previewText;

            this.artifactReadState.set(artifact.artifactId, {
                status: 'success',
                text: boundedText || formatResultArtifactMetadata(artifactMetadata, `已读取 ${formatByteLength(actualSize)}。`)
            });
            this._renderOperatorResultPanel();
        } catch (error) {
            if (!this._isArtifactReadCurrent(token, identity, abortController) || error?.name === 'AbortError') {
                return;
            }

            this.artifactReadState.set(artifact.artifactId, {
                status: 'error',
                text: this._isArtifactUnavailableError(error)
                    ? '资源已过期或不可用'
                    : (error?.message || 'Artifact 读取失败')
            });
            this._renderOperatorResultPanel();
        } finally {
            if (this.artifactReadAbort === abortController) {
                this.artifactReadAbort = null;
            }
        }
    }

    async _handleAnalyzePreview() {
        if (typeof this.onAnalyzePreview !== 'function' || this.isAnalyzing || this.isAutoTuning) {
            return;
        }

        try {
            this.isAnalyzing = true;
            this.render();
            this.applyPreviewState();
            const result = await this.onAnalyzePreview({
                operator: this.getOperator(),
                previewState: this.state
            });
            this.analysisResult = this._normalizeAnalysisResult(result);
        } catch (error) {
            console.error('[PreviewPanel] 线序分析失败:', error);
        } finally {
            this.isAnalyzing = false;
            this.render();
            this.applyPreviewState();
        }
    }

    async _handleAutoTune() {
        if (typeof this.onAutoTune !== 'function' || this.isAnalyzing || this.isAutoTuning) {
            return;
        }

        try {
            this.isAutoTuning = true;
            this.render();
            this.applyPreviewState();
            const result = await this.onAutoTune({
                operator: this.getOperator(),
                previewState: this.state
            });
            this.analysisResult = this._normalizeAnalysisResult(result);
        } catch (error) {
            console.error('[PreviewPanel] 线序自动调参失败:', error);
        } finally {
            this.isAutoTuning = false;
            this.render();
            this.applyPreviewState();
        }
    }

    _normalizeAnalysisResult(result) {
        if (!result || typeof result !== 'object') {
            return null;
        }

        const previewImageBase64 = result.previewImageBase64 || result.PreviewImageBase64 || null;
        const outputs = result.outputs || result.Outputs || null;

        return {
            targetNodeId: result.targetNodeId || result.TargetNodeId || null,
            success: Boolean(result.success ?? result.Success),
            errorMessage: result.errorMessage || result.ErrorMessage || null,
            inputImageSrc: null,
            previewImageSrc: toPreviewDataUrl(previewImageBase64, this.maxAnalysisImageBase64Chars),
            outputs
        };
    }
}

export default PreviewPanel;
export { toPreviewDataUrl };
