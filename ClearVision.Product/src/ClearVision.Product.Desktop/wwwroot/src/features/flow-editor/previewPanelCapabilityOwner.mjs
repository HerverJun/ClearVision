import {
    buildPreviewSummaryItems,
    formatPreviewOutputValue
} from './previewOutputFormatter.mjs';
import {
    MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS,
    MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES,
    buildOperatorResultViewModel,
    buildSafeJsonPreview,
    formatByteLength,
    formatResultArtifactMetadata,
    isTextArtifactForResultPanel,
    normalizeArtifactReference,
    redactLocalAbsolutePaths
} from './operatorResultViewModel.mjs';

export const PREVIEW_PANEL_CAPABILITY_OWNER_ID = 'preview-panel-capability-v2';

// Preview request and artifact-read route: PreviewPanelCapabilityOwner -> PreviewPanelCapabilityAdapter -> NodePreviewCoordinator.

function deepClone(value) {
    if (value === null || value === undefined) {
        return value;
    }

    if (typeof structuredClone === 'function') {
        return structuredClone(value);
    }

    return JSON.parse(JSON.stringify(value));
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function escapeAttribute(value) {
    return escapeHtml(value);
}

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function getParameterName(parameter) {
    return String(parameter?.name ?? parameter?.Name ?? '').trim();
}

function getParameterValue(parameter) {
    return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue ?? null;
}

function mergeParameters(definitionParameters = [], nodeParameters = []) {
    const nodeList = Array.isArray(nodeParameters) ? nodeParameters : [];
    const definitionList = Array.isArray(definitionParameters) ? definitionParameters : [];
    const usedNodeParameters = new Set();

    const merged = definitionList.map(definition => {
        const definitionName = getParameterName(definition);
        const nodeParameter = nodeList.find(parameter =>
            getParameterName(parameter).toLowerCase() === definitionName.toLowerCase());
        if (nodeParameter) {
            usedNodeParameters.add(nodeParameter);
        }

        return {
            ...deepClone(definition),
            value: nodeParameter ? getParameterValue(nodeParameter) : getParameterValue(definition)
        };
    });

    nodeList.forEach(parameter => {
        if (!usedNodeParameters.has(parameter)) {
            merged.push(deepClone(parameter));
        }
    });

    return merged;
}

function readOwn(source, ...keys) {
    if (!source || typeof source !== 'object') {
        return undefined;
    }

    for (const key of keys) {
        if (Object.prototype.hasOwnProperty.call(source, key)) {
            return source[key];
        }
    }

    return undefined;
}

function normalizeBool(value) {
    return value === true || value === 'true' || value === 1;
}

function getObservationIdentity(state) {
    const observation = readOwn(state, 'observation', 'Observation');
    const identity = readOwn(observation, 'identity', 'Identity');
    return identity && typeof identity === 'object' ? identity : null;
}

function getStateIdentitySignature(state) {
    const identity = getObservationIdentity(state) || {};
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

function getStatusLabel(state, belongsToSelectedNode, nodeDeleted) {
    if (nodeDeleted) {
        return {
            kind: 'deleted',
            label: '节点已删除',
            message: '节点已删除'
        };
    }

    if (!belongsToSelectedNode) {
        return {
            kind: 'idle',
            label: '等待预览',
            message: '请选择一个算子'
        };
    }

    if (state?.status === 'loading') {
        return {
            kind: 'loading',
            label: '预览中',
            message: '预览中'
        };
    }

    if (state?.status === 'success') {
        return {
            kind: 'success',
            label: '预览完成',
            message: '预览完成'
        };
    }

    if (state?.status === 'error') {
        return {
            kind: 'error',
            label: '预览失败',
            message: state?.errorMessage || '预览失败'
        };
    }

    if (state?.status === 'canceled') {
        return {
            kind: 'canceled',
            label: '预览已取消',
            message: '预览已取消'
        };
    }

    return {
        kind: 'idle',
        label: '等待预览',
        message: '等待预览'
    };
}

function normalizeOperatorTitle(operator, liveNode = null) {
    return String(
        operator?.title ||
        operator?.displayName ||
        liveNode?.title ||
        liveNode?.displayName ||
        operator?.type ||
        liveNode?.type ||
        '未命名算子');
}

export class PreviewPanelCapabilityAdapter {
    constructor({
        flowCanvasAdapter,
        previewCoordinator,
        getOperatorMetadata = () => null
    } = {}) {
        if (!flowCanvasAdapter) {
            throw new Error('PreviewPanelCapabilityAdapter requires a FlowCanvasAdapter.');
        }
        if (!previewCoordinator) {
            throw new Error('PreviewPanelCapabilityAdapter requires a NodePreviewCoordinator.');
        }

        this.flowCanvasAdapter = flowCanvasAdapter;
        this.previewCoordinator = previewCoordinator;
        this.getOperatorMetadata = typeof getOperatorMetadata === 'function'
            ? getOperatorMetadata
            : () => null;
    }

    getSelectedNodeId() {
        return this.flowCanvasAdapter?.selectedNode || null;
    }

    getNode(nodeId) {
        if (!nodeId) {
            return null;
        }

        return this.flowCanvasAdapter?.nodes?.get?.(nodeId) || null;
    }

    getSelectedOperatorSnapshot(nodeId = this.getSelectedNodeId()) {
        const node = this.getNode(nodeId);
        if (!node) {
            return null;
        }

        const metadata = this.getOperatorMetadata(node.type) || {};
        const displayName = metadata.displayName || metadata.DisplayName || node.title || node.type || '算子';

        return {
            id: node.id,
            type: node.type,
            title: node.title || displayName,
            displayName,
            disabled: normalizeBool(node.disabled ?? node.Disabled),
            inputPorts: node.inputs || metadata.inputPorts || metadata.InputPorts || [],
            outputPorts: node.outputs || metadata.outputPorts || metadata.OutputPorts || [],
            parameters: mergeParameters(metadata.parameters || metadata.Parameters || [], node.parameters || [])
        };
    }

    subscribeSelectedNode(listener) {
        if (typeof listener !== 'function') {
            return () => {};
        }

        return this.flowCanvasAdapter?.subscribeSelection?.((state = {}) => {
            listener(this.getSelectedOperatorSnapshot(state.selectedNodeId), state);
        }) || (() => {});
    }

    subscribeFlowChanges(listener) {
        if (typeof listener !== 'function') {
            return () => {};
        }

        return this.flowCanvasAdapter?.subscribeStructureState?.(listener) || (() => {});
    }

    subscribePreviewState(listener) {
        return this.previewCoordinator?.subscribe?.(listener) || (() => {});
    }

    getPreviewState() {
        return this.previewCoordinator?.getState?.() || null;
    }

    setActiveNode(nodeId, options = {}) {
        const node = this.getNode(nodeId);
        if (!node) {
            this.previewCoordinator?.setActiveNode?.(null, options);
            return false;
        }

        this.previewCoordinator?.setActiveNode?.(node, options);
        return true;
    }

    clearActiveNode() {
        this.previewCoordinator?.setActiveNode?.(null, { autoPreview: false });
    }

    requestPreview(options = {}) {
        this.previewCoordinator?.requestActivePreview?.(options);
    }

    cancelPreview() {
        if (typeof this.previewCoordinator?.cancelPreview === 'function') {
            this.previewCoordinator.cancelPreview();
            return;
        }

        this.previewCoordinator?.cancelActivePreviewRequest?.();
    }

    readArtifactForCurrentState(artifactId, expectedIdentity, options = {}) {
        return this.previewCoordinator?.readArtifactForCurrentState?.(artifactId, expectedIdentity, options);
    }

    selectNode(nodeId) {
        return this.flowCanvasAdapter?.selectNode?.(nodeId) === true;
    }

    getFlowRevision() {
        return this.flowCanvasAdapter?.getFlowRevision?.() ?? this.flowCanvasAdapter?.getRevision?.() ?? 0;
    }

    getNodes() {
        const nodes = this.flowCanvasAdapter?.nodes;
        if (nodes instanceof Map) {
            return Array.from(nodes.values());
        }
        if (Array.isArray(nodes)) {
            return nodes;
        }
        if (nodes?.values && typeof nodes.values === 'function') {
            return Array.from(nodes.values());
        }

        return [];
    }
}

export class PreviewPanelCapabilityOwner {
    constructor(container, {
        previewAdapter,
        showToast = () => {}
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('PreviewPanelCapabilityOwner requires a container.');
        }
        if (!previewAdapter) {
            throw new Error('PreviewPanelCapabilityOwner requires a preview adapter.');
        }

        this.previewAdapter = previewAdapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.currentOperator = null;
        this.currentNodeId = null;
        this.nodeDeleted = false;
        this.previewState = this.previewAdapter.getPreviewState();
        this.autoPreviewEnabled = true;
        this.disposed = false;
        this.unsubscribes = [];
        this.artifactReadAbort = null;
        this.artifactReadToken = 0;
        this.artifactReadState = new Map();
        this.resultIdentitySignature = getStateIdentitySignature(this.previewState);

        this.handleClick = this.handleClick.bind(this);
        this.handleChange = this.handleChange.bind(this);

        this.container.dataset.previewPanelOwner = PREVIEW_PANEL_CAPABILITY_OWNER_ID;
        this.container.addEventListener('click', this.handleClick);
        this.container.addEventListener('change', this.handleChange);

        this.unsubscribes.push(
            this.previewAdapter.subscribePreviewState(state => this.handlePreviewStateChanged(state)),
            this.previewAdapter.subscribeSelectedNode((operator, state) => this.handleSelectedNodeChanged(operator, state)),
            this.previewAdapter.subscribeFlowChanges(state => this.handleFlowChanged(state))
        );

        this.render();
    }

    handleSelectedNodeChanged(operator) {
        if (this.disposed) {
            return;
        }

        this.currentOperator = operator || null;
        this.currentNodeId = operator?.id || null;
        this.nodeDeleted = false;
        this.cancelArtifactRead();
        this.artifactReadState.clear();

        if (!this.currentNodeId) {
            this.previewAdapter.clearActiveNode();
            this.render();
            return;
        }

        this.previewAdapter.setActiveNode(this.currentNodeId, {
            autoPreview: this.autoPreviewEnabled
        });
        this.render();
    }

    handleFlowChanged() {
        if (this.disposed || !this.currentNodeId) {
            return;
        }

        const liveOperator = this.previewAdapter.getSelectedOperatorSnapshot(this.currentNodeId);
        if (!liveOperator) {
            this.nodeDeleted = true;
            this.currentOperator = null;
            this.previewAdapter.clearActiveNode();
            this.render();
            return;
        }

        this.currentOperator = liveOperator;
        this.render();
    }

    handlePreviewStateChanged(state) {
        if (this.disposed) {
            return;
        }

        this.previewState = state || null;
        this.resetArtifactReadsIfIdentityChanged();
        this.render();
    }

    handleClick(event) {
        const target = event.target?.closest?.('[data-preview-action]') || event.target;
        const action = target?.dataset?.previewAction;
        if (!action || this.disposed) {
            return;
        }

        event.preventDefault?.();

        if (action === 'manual-preview') {
            this.requestManualPreview();
            return;
        }

        if (action === 'cancel-preview') {
            this.previewAdapter.cancelPreview();
            return;
        }

        if (action === 'select-node') {
            const nodeId = target.dataset.nodeId;
            if (nodeId) {
                this.previewAdapter.selectNode(nodeId);
            }
            return;
        }

        if (action === 'read-artifact') {
            const artifactId = target.dataset.artifactId;
            if (artifactId) {
                void this.readArtifactPreview(artifactId);
            }
        }
    }

    handleChange(event) {
        const target = event.target;
        if (target?.dataset?.previewAuto !== 'true' || this.disposed) {
            return;
        }

        this.autoPreviewEnabled = Boolean(target.checked);
        if (!this.autoPreviewEnabled) {
            this.previewAdapter.cancelPreview();
            this.render();
            return;
        }

        if (this.autoPreviewEnabled && this.currentNodeId) {
            this.previewAdapter.setActiveNode(this.currentNodeId, {
                autoPreview: true
            });
        }
        this.render();
    }

    requestManualPreview() {
        if (!this.currentNodeId) {
            this.showToast('请选择一个算子', 'warning');
            return;
        }

        if (!this.previewAdapter.getNode(this.currentNodeId)) {
            this.nodeDeleted = true;
            this.previewAdapter.clearActiveNode();
            this.render();
            return;
        }

        this.previewAdapter.setActiveNode(this.currentNodeId, {
            autoPreview: false
        });
        this.previewAdapter.requestPreview({
            immediate: true,
            force: true,
            trigger: 'manual'
        });
    }

    resetArtifactReadsIfIdentityChanged() {
        const nextSignature = getStateIdentitySignature(this.previewState);
        if (nextSignature === this.resultIdentitySignature) {
            return;
        }

        this.resultIdentitySignature = nextSignature;
        this.artifactReadState.clear();
        this.cancelArtifactRead();
    }

    render() {
        if (this.disposed || !this.container) {
            return;
        }

        const selectedNodeId = this.currentNodeId;
        const belongsToSelectedNode = Boolean(
            selectedNodeId &&
            this.previewState?.activeNodeId === selectedNodeId);
        const liveNode = selectedNodeId ? this.previewAdapter.getNode(selectedNodeId) : null;
        const statusInfo = getStatusLabel(this.previewState, belongsToSelectedNode, this.nodeDeleted);
        const title = this.currentOperator
            ? normalizeOperatorTitle(this.currentOperator, liveNode)
            : '请选择一个算子';
        const type = this.currentOperator?.type || liveNode?.type || '-';

        this.container.innerHTML = `
            <section class="preview-capability-owner" data-owner="${PREVIEW_PANEL_CAPABILITY_OWNER_ID}" data-status="${escapeAttribute(statusInfo.kind)}">
                <header class="preview-capability-header">
                    <div class="preview-capability-title-group">
                        <div class="preview-capability-title">预览面板</div>
                        <div class="preview-capability-status" data-status="${escapeAttribute(statusInfo.kind)}">${escapeHtml(statusInfo.label)}</div>
                    </div>
                    <div class="preview-capability-actions">
                        <label class="preview-capability-auto">
                            <input type="checkbox" data-preview-auto="true" ${this.autoPreviewEnabled ? 'checked' : ''}>
                            <span>自动预览</span>
                        </label>
                        <button type="button" class="btn btn-secondary btn-sm" data-preview-action="manual-preview" ${selectedNodeId ? '' : 'disabled'}>手动预览</button>
                        <button type="button" class="btn btn-secondary btn-sm" data-preview-action="cancel-preview" ${belongsToSelectedNode && this.previewState?.status === 'loading' ? '' : 'disabled'}>取消预览</button>
                    </div>
                </header>
                <div class="preview-capability-scroll" data-low-height-scroll="true">
                    <section class="preview-capability-current">
                        <h5>当前算子</h5>
                        <div class="preview-capability-current-title">${escapeHtml(title)}</div>
                        <div class="preview-capability-current-type">${escapeHtml(type)}</div>
                        <p class="preview-capability-message">${escapeHtml(statusInfo.message)}</p>
                    </section>
                    ${this.renderPreviewMedia(belongsToSelectedNode)}
                    ${this.renderPreviewSummary(belongsToSelectedNode, statusInfo)}
                    ${this.renderModuleResult(belongsToSelectedNode, liveNode)}
                </div>
            </section>
        `;
    }

    renderPreviewMedia(belongsToSelectedNode) {
        const presenter = belongsToSelectedNode ? (this.previewState?.presenter || {}) : {};
        return `
            <section class="preview-capability-media">
                <div class="preview-capability-image">
                    <div class="preview-capability-image-title">输入</div>
                    ${presenter.inputImageSrc
                        ? `<img src="${escapeAttribute(presenter.inputImageSrc)}" alt="输入图像预览">`
                        : '<div class="preview-capability-placeholder">暂无输入图像</div>'}
                </div>
                <div class="preview-capability-image">
                    <div class="preview-capability-image-title">输出</div>
                    ${presenter.outputImageSrc
                        ? `<img src="${escapeAttribute(presenter.outputImageSrc)}" alt="输出图像预览">`
                        : '<div class="preview-capability-placeholder">暂无输出图像</div>'}
                </div>
            </section>
        `;
    }

    renderPreviewSummary(belongsToSelectedNode, statusInfo) {
        if (this.nodeDeleted) {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">节点已删除</div>
                </section>
            `;
        }

        if (!this.currentNodeId) {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">请选择一个算子</div>
                </section>
            `;
        }

        if (!belongsToSelectedNode || !this.previewState) {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">等待预览</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'loading') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">预览中</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'error') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty error">预览失败：${escapeHtml(statusInfo.message)}</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'canceled') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">预览已取消</div>
                </section>
            `;
        }

        const summaryItems = buildPreviewSummaryItems(this.previewState.outputData, {
            maxItems: 8,
            stringMaxLength: 64,
            skipImageLikeValues: true
        });

        if (summaryItems.length === 0) {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">暂无输出摘要</div>
                </section>
            `;
        }

        return `
            <section class="preview-capability-section">
                <h5>预览结果</h5>
                <div class="preview-capability-summary">
                    ${summaryItems.map(item => `
                        <div class="preview-capability-summary-row" data-output-kind="${escapeAttribute(item.kind || 'value')}">
                            <span>${escapeHtml(item.key)}</span>
                            <strong title="${escapeAttribute(item.title || item.value || '')}">${escapeHtml(item.value)}</strong>
                        </div>
                    `).join('')}
                </div>
            </section>
        `;
    }

    renderModuleResult(belongsToSelectedNode, liveNode) {
        const model = buildOperatorResultViewModel(this.currentOperator, this.previewState, {
            liveNode,
            flowRevision: this.previewAdapter.getFlowRevision(),
            getNodes: () => this.previewAdapter.getNodes()
        });
        const stateMessage = this.nodeDeleted ? '节点已删除' : model.stateMessage;
        const outputSections = belongsToSelectedNode ? this.renderOutputSections(model) : '';

        return `
            <section class="preview-capability-section preview-capability-module-result" data-result-status="${escapeAttribute(this.nodeDeleted ? 'deleted' : model.status)}">
                <div class="preview-capability-section-header">
                    <h5>模块结果</h5>
                    <span>${escapeHtml(this.nodeDeleted ? '节点已删除' : model.statusText)}</span>
                </div>
                <div class="preview-capability-empty">${escapeHtml(stateMessage)}</div>
                ${this.renderNodeResultList(model)}
                ${this.renderOverview(model)}
                ${outputSections}
                ${this.renderArtifacts(model)}
                ${this.renderDiagnostics(model)}
            </section>
        `;
    }

    renderNodeResultList(model) {
        if (!Array.isArray(model.nodeResults) || model.nodeResults.length === 0) {
            return '';
        }

        return `
            <div class="preview-capability-node-list">
                ${model.nodeResults.slice(0, 32).map(item => `
                    <button type="button"
                            class="preview-capability-node-item"
                            data-preview-action="select-node"
                            data-node-id="${escapeAttribute(item.nodeId)}"
                            data-selected="${item.selected ? 'true' : 'false'}"
                            data-status="${escapeAttribute(item.statusKind)}">
                        <span>${String(item.index + 1).padStart(2, '0')}</span>
                        <strong>${escapeHtml(item.title)}</strong>
                        <em>${escapeHtml(item.statusText)}</em>
                    </button>
                `).join('')}
            </div>
        `;
    }

    renderOverview(model) {
        const rows = (model.overviewItems || [])
            .filter(([, value]) => value !== null && value !== undefined && value !== '')
            .slice(0, 8)
            .map(([label, value]) => `
                <div class="preview-capability-kv">
                    <span>${escapeHtml(label)}</span>
                    <strong>${escapeHtml(value)}</strong>
                </div>
            `)
            .join('');

        return rows ? `<div class="preview-capability-kv-grid">${rows}</div>` : '';
    }

    renderOutputSections(model) {
        if (!Array.isArray(model.outputSections) || model.outputSections.length === 0) {
            return '';
        }

        return `
            <div class="preview-capability-output-groups">
                ${model.outputSections.map(section => `
                    <div class="preview-capability-output-group" data-output-group="${escapeAttribute(section.kind)}">
                        <div class="preview-capability-output-heading">${escapeHtml(section.kind)}</div>
                        ${section.items.slice(0, 8).map(item => `
                            <div class="preview-capability-output-row">
                                <span>${escapeHtml(item.key || item.pathHint || '-')}</span>
                                <strong>${escapeHtml(item.value || '-')}</strong>
                                <em>${escapeHtml(item.resultPath || item.meta || item.pathHint || '')}</em>
                            </div>
                        `).join('')}
                    </div>
                `).join('')}
            </div>
        `;
    }

    renderArtifacts(model) {
        if (!Array.isArray(model.artifacts) || model.artifacts.length === 0) {
            return '';
        }

        return `
            <div class="preview-capability-artifacts">
                ${model.artifacts.map(artifact => {
                    const readState = this.artifactReadState.get(artifact.artifactId);
                    const formatted = formatPreviewOutputValue(artifact.role || artifact.kind || 'Artifact', formatByteLength(artifact.length), {
                        stringMaxLength: 80
                    });
                    return `
                        <div class="preview-capability-artifact" data-artifact-id="${escapeAttribute(artifact.artifactId)}">
                            <div>
                                <strong>${escapeHtml(artifact.role || artifact.kind || 'Artifact')}</strong>
                                <span>${escapeHtml(artifact.contentType || '-')} · ${escapeHtml(formatted.text)}</span>
                            </div>
                            <button type="button"
                                    class="btn btn-secondary btn-sm"
                                    data-preview-action="read-artifact"
                                    data-artifact-id="${escapeAttribute(artifact.artifactId)}"
                                    ${readState?.status === 'loading' ? 'disabled' : ''}>
                                ${readState?.status === 'loading' ? '读取中' : '查看摘要'}
                            </button>
                            ${readState ? `<pre class="preview-capability-artifact-preview ${escapeAttribute(readState.status)}">${escapeHtml(readState.text)}</pre>` : ''}
                        </div>
                    `;
                }).join('')}
            </div>
        `;
    }

    renderDiagnostics(model) {
        if (!Array.isArray(model.diagnostics) || model.diagnostics.length === 0) {
            return '';
        }

        return `
            <div class="preview-capability-diagnostics">
                ${model.diagnostics.slice(0, 8).map(item => `
                    <div class="preview-capability-diagnostic">
                        <span>${escapeHtml(item.code || item.source || 'diagnostic')}</span>
                        <strong>${escapeHtml(item.message || '')}</strong>
                    </div>
                `).join('')}
            </div>
        `;
    }

    findArtifact(artifactId) {
        const safeArtifactId = String(artifactId || '');
        const artifacts = Array.isArray(this.previewState?.artifacts) ? this.previewState.artifacts : [];
        return artifacts.find(artifact => artifact?.artifactId === safeArtifactId) || null;
    }

    cancelArtifactRead() {
        this.artifactReadAbort?.abort?.();
        this.artifactReadAbort = null;
        this.artifactReadToken += 1;
    }

    isArtifactReadCurrent(token, identity, abortController = null) {
        return token === this.artifactReadToken &&
            abortController?.signal?.aborted !== true &&
            JSON.stringify(getObservationIdentity(this.previewState) || {}) === JSON.stringify(identity || {});
    }

    isArtifactUnavailableError(error) {
        return error?.status === 404 ||
            error?.statusCode === 404 ||
            error?.name === 'PreviewArtifactUnavailableError' ||
            /过期|不可用|stale|missing|not found|404/i.test(String(error?.message || ''));
    }

    async readArtifactPreview(artifactId) {
        const artifact = normalizeArtifactReference(this.findArtifact(artifactId));
        const identity = getObservationIdentity(this.previewState);
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
            this.render();
            return;
        }

        if (artifact.length > MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES) {
            this.artifactReadState.set(artifact.artifactId, {
                status: 'success',
                text: formatResultArtifactMetadata(artifact, '内容过大，仅展示元数据')
            });
            this.render();
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
        this.render();

        try {
            const result = await this.previewAdapter.readArtifactForCurrentState(
                artifact.artifactId,
                identity,
                { signal: abortController?.signal });
            if (!this.isArtifactReadCurrent(token, identity, abortController)) {
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
            if (!this.isArtifactReadCurrent(token, identity, abortController)) {
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
            this.render();
        } catch (error) {
            if (!this.isArtifactReadCurrent(token, identity, abortController) || error?.name === 'AbortError') {
                return;
            }

            this.artifactReadState.set(artifact.artifactId, {
                status: 'error',
                text: this.isArtifactUnavailableError(error)
                    ? '资源已过期或不可用'
                    : (error?.message || 'Artifact 读取失败')
            });
            this.render();
        } finally {
            if (this.artifactReadAbort === abortController) {
                this.artifactReadAbort = null;
            }
        }
    }

    destroy() {
        this.dispose();
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        this.cancelArtifactRead();
        this.unsubscribes.forEach(unsubscribe => {
            try {
                unsubscribe?.();
            } catch {
                // Best-effort cleanup for external subscriptions.
            }
        });
        this.unsubscribes = [];
        this.artifactReadState.clear();
        this.container.removeEventListener('click', this.handleClick);
        this.container.removeEventListener('change', this.handleChange);
        delete this.container.dataset.previewPanelOwner;
        this.container.innerHTML = '';
        this.currentOperator = null;
        this.currentNodeId = null;
        this.previewState = null;
    }
}

export function createPreviewPanelCapabilityAdapter(options = {}) {
    return new PreviewPanelCapabilityAdapter(options);
}

export default PreviewPanelCapabilityOwner;
