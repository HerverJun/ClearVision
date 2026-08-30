import httpClient from '../../core/messaging/httpClient.js';
import ImageCanvas from '../../core/canvas/imageCanvas.js';
import {
    hitTestPointSequencePoint,
    normalizePointSequenceGeometry,
    parsePointPairs
} from './roiGeometry.mjs';

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
const SAMPLE_SOURCES = ['ManualClick', 'UpstreamPoint', 'UpstreamCircleCenter', 'Template', 'Imported'];

function createElement(tag, className = '', text = '') {
    const element = document.createElement(tag);
    if (className) {
        element.className = className;
    }
    if (text) {
        element.textContent = text;
    }
    return element;
}

function readOwn(object, ...names) {
    if (!object || typeof object !== 'object') {
        return undefined;
    }

    for (const name of names) {
        if (Object.prototype.hasOwnProperty.call(object, name)) {
            return object[name];
        }
    }

    return undefined;
}

function readParameterValue(operator, name, fallback = '') {
    const parameter = (operator?.parameters || []).find(item =>
        String(item?.name || item?.Name || '').toLowerCase() === String(name).toLowerCase());
    const value = parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue;
    return value == null ? fallback : value;
}

function readNumber(value, fallback = null) {
    if (value === '' || value === null || value === undefined) {
        return fallback;
    }

    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
}

function formatNumber(value, digits = 4) {
    const number = Number(value);
    if (!Number.isFinite(number)) {
        return '';
    }

    return number.toFixed(digits).replace(/\.?0+$/u, '');
}

function safeId(prefix = 'draft') {
    return `${prefix}-${globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`}`;
}

function clampImagePoint(point, bounds) {
    if (!bounds) {
        return point;
    }

    return {
        x: Math.max(0, Math.min(Number(bounds.width) - 1, Number(point.x))),
        y: Math.max(0, Math.min(Number(bounds.height) - 1, Number(point.y)))
    };
}

function toImageSource(base64OrSrc) {
    if (!base64OrSrc) {
        return null;
    }

    const text = String(base64OrSrc);
    return text.startsWith('data:') ? text : `data:image/png;base64,${text}`;
}

function createDefaultOptions() {
    return {
        ransacReprojectionThreshold: 3,
        ransacMaxIterations: 3000,
        ransacConfidence: 0.995,
        maxAcceptedReprojectionError: 3,
        minInlierCount: 0,
        minInlierRatio: 0.5
    };
}

function getOperatorMode(operator) {
    const value = String(readParameterValue(operator, 'CalibrationMode', 'Affine'));
    return value.toLowerCase() === 'perspective' ? 'Perspective' : 'Affine';
}

function getOperatorUnit(operator) {
    const value = String(readParameterValue(operator, 'CalibrationUnit', 'mm')).trim();
    return value || 'mm';
}

function getOperatorOptions(operator) {
    return {
        ransacReprojectionThreshold: readNumber(readParameterValue(operator, 'RansacReprojectionThreshold', 3), 3),
        ransacMaxIterations: Math.max(1, Math.round(readNumber(readParameterValue(operator, 'RansacMaxIterations', 3000), 3000))),
        ransacConfidence: readNumber(readParameterValue(operator, 'RansacConfidence', 0.995), 0.995),
        maxAcceptedReprojectionError: readNumber(readParameterValue(operator, 'MaxAcceptedReprojectionError', 3), 3),
        minInlierCount: Math.max(0, Math.round(readNumber(readParameterValue(operator, 'MinInlierCount', 0), 0))),
        minInlierRatio: readNumber(readParameterValue(operator, 'MinInlierRatio', 0.5), 0.5)
    };
}

function normalizeSample(raw, index = 0, source = 'Imported') {
    const now = new Date().toISOString();
    return {
        sampleId: raw.sampleId || raw.SampleId || safeId('sample'),
        order: Number(raw.order ?? raw.Order ?? index + 1),
        pixelX: readNumber(raw.pixelX ?? raw.PixelX ?? raw.x ?? raw.X),
        pixelY: readNumber(raw.pixelY ?? raw.PixelY ?? raw.y ?? raw.Y),
        worldX: readNumber(raw.worldX ?? raw.WorldX),
        worldY: readNumber(raw.worldY ?? raw.WorldY),
        source: SAMPLE_SOURCES.includes(raw.source || raw.Source) ? (raw.source || raw.Source) : source,
        enabled: readOwn(raw, 'enabled', 'Enabled') !== false,
        inlier: readOwn(raw, 'inlier', 'Inlier') ?? null,
        reprojectionX: readNumber(raw.reprojectionX ?? raw.ReprojectionX),
        reprojectionY: readNumber(raw.reprojectionY ?? raw.ReprojectionY),
        error: readNumber(raw.error ?? raw.Error),
        note: String(raw.note ?? raw.Note ?? ''),
        createdAtUtc: raw.createdAtUtc || raw.CreatedAtUtc || now
    };
}

function sessionToGeometry(session) {
    return normalizePointSequenceGeometry({
        kind: 'pointSequence',
        points: session.samples.map(sample => ({
            x: sample.pixelX ?? 0,
            y: sample.pixelY ?? 0,
            worldX: sample.worldX ?? 0,
            worldY: sample.worldY ?? 0,
            enabled: sample.enabled !== false
        }))
    });
}

function isSampleValid(sample) {
    return Number.isFinite(Number(sample.pixelX)) &&
        Number.isFinite(Number(sample.pixelY)) &&
        Number.isFinite(Number(sample.worldX)) &&
        Number.isFinite(Number(sample.worldY));
}

function extractPointCandidates(outputData) {
    const candidates = [];
    const visit = (value, source, depth = 0) => {
        if (!value || depth > 3) {
            return;
        }

        if (Array.isArray(value)) {
            value.slice(0, 128).forEach(item => visit(item, source, depth + 1));
            return;
        }

        if (typeof value !== 'object') {
            return;
        }

        const x = readNumber(readOwn(value, 'x', 'X', 'imageX', 'ImageX', 'pixelX', 'PixelX', 'centerX', 'CenterX'));
        const y = readNumber(readOwn(value, 'y', 'Y', 'imageY', 'ImageY', 'pixelY', 'PixelY', 'centerY', 'CenterY'));
        if (Number.isFinite(x) && Number.isFinite(y)) {
            candidates.push({ x, y, source });
            return;
        }

        const center = readOwn(value, 'center', 'Center', 'circle', 'Circle');
        if (center) {
            visit(center, 'UpstreamCircleCenter', depth + 1);
        }

        ['Point', 'Points', 'PointList', 'Circle', 'Circles', 'CircleDataList'].forEach(key => {
            const child = readOwn(value, key);
            if (child) {
                visit(child, key.toLowerCase().includes('circle') ? 'UpstreamCircleCenter' : 'UpstreamPoint', depth + 1);
            }
        });
    };

    visit(outputData, 'UpstreamPoint');
    return candidates;
}

export class CalibrationDraftWorkbench {
    constructor(container, options = {}) {
        this.container = container;
        this.getOperator = options.getOperator ?? (() => null);
        this.previewCoordinator = options.previewCoordinator ?? null;
        this.getProject = options.getProject ?? (() => null);
        this.getProjectId = options.getProjectId ?? (() => null);
        this.getFlowRevision = options.getFlowRevision ?? (() => 0);
        this.getDebugSessionId = options.getDebugSessionId ?? (() => null);
        this.onFormalSaveSuccess = options.onFormalSaveSuccess ?? (() => {});
        this.canvasId = `calibration-draft-canvas-${Math.random().toString(36).slice(2)}`;
        this.imageCanvas = null;
        this.unsubscribePreview = null;
        this.previewState = this.previewCoordinator?.getState?.() ?? null;
        this.currentImageSource = null;
        this.imageBounds = null;
        this.solveAbort = null;
        this.solveSequence = 0;
        this.formalSaveInProgress = false;
        this.selectedSampleId = null;
        this.session = this.createSessionFromOperator();
        this.render();
        this.initializeCanvas();
        this.bindPreview();
        this.applyPreviewState();
    }

    destroy() {
        this.solveAbort?.abort?.();
        this.solveAbort = null;
        this.unsubscribePreview?.();
        this.unsubscribePreview = null;
        if (this.canvasClickHandler) {
            this.imageCanvas?.canvas?.removeEventListener?.('click', this.canvasClickHandler);
            this.canvasClickHandler = null;
        }
        this.imageCanvas?.destroy?.();
        this.imageCanvas = null;
        this.container = null;
    }

    createSessionFromOperator() {
        const operator = this.getOperator();
        const sequence = parsePointPairs(readParameterValue(operator, 'PointPairs', '[]')) ||
            { kind: 'pointSequence', points: [] };
        return {
            sessionId: safeId('calibration-draft'),
            projectId: this.getProjectId() || EMPTY_GUID,
            targetNodeId: operator?.id || EMPTY_GUID,
            imageIdentity: '',
            createdAtUtc: new Date().toISOString(),
            updatedAtUtc: new Date().toISOString(),
            mode: getOperatorMode(operator),
            unit: getOperatorUnit(operator),
            samples: (sequence.points || []).map((point, index) => normalizeSample({
                sampleId: safeId('sample'),
                order: index + 1,
                pixelX: point.x,
                pixelY: point.y,
                worldX: point.worldX,
                worldY: point.worldY,
                enabled: point.enabled
            }, index, 'Imported')),
            solverOptions: { ...createDefaultOptions(), ...getOperatorOptions(operator) },
            lastSolveResult: null,
            candidateBundle: null,
            candidateBundleJson: null,
            solveArtifactId: null,
            formalAssetId: null,
            formalAssetRevision: null,
            formalAssetHash: null,
            artifacts: [],
            diagnostics: [],
            dirty: false,
            status: 'Draft'
        };
    }

    bindPreview() {
        if (!this.previewCoordinator?.subscribe) {
            return;
        }

        this.unsubscribePreview = this.previewCoordinator.subscribe(state => {
            this.previewState = state;
            this.applyPreviewState();
        });
    }

    initializeCanvas() {
        if (!this.container) {
            return;
        }

        this.imageCanvas = new ImageCanvas(this.canvasId, {
            interactionMode: 'roi-rect',
            onOverlayChanged: (geometry, phase) => this.handleOverlayChanged(geometry, phase)
        });
        this.canvasClickHandler = event => this.handleCanvasClick(event);
        this.imageCanvas.canvas?.addEventListener('click', this.canvasClickHandler);
        this.renderOverlay();
    }

    async applyPreviewState() {
        const source = this.resolveInputImageSource();
        if (!source || source === this.currentImageSource || !this.imageCanvas) {
            return;
        }

        this.currentImageSource = source;
        this.session.imageIdentity = this.previewState?.request?.inputImageHash || source.slice(0, 48);
        try {
            await this.imageCanvas.loadImage(source);
            this.imageBounds = this.imageCanvas.image
                ? { width: this.imageCanvas.image.width, height: this.imageCanvas.image.height }
                : null;
            this.renderOverlay();
            this.renderStatus();
        } catch {
            this.currentImageSource = null;
            this.imageBounds = null;
            this.renderStatus('Input image could not be loaded.');
        }
    }

    resolveInputImageSource() {
        const activeNodeId = this.previewState?.activeNodeId;
        const operatorId = this.getOperator()?.id || null;
        if (activeNodeId === operatorId && this.previewState?.presenter?.inputImageSrc) {
            return this.previewState.presenter.inputImageSrc;
        }

        return toImageSource(this.previewState?.inputImageBase64);
    }

    render() {
        if (!this.container) {
            return;
        }

        this.container.innerHTML = `
            <section class="calibration-draft-workbench" data-testid="npoint-calibration-workbench">
                <div class="calibration-draft-header">
                    <div>
                        <div class="calibration-draft-title">N Point Calibration Draft</div>
                        <div class="calibration-draft-subtitle">Draft candidate only / 未正式保存到工程资产</div>
                    </div>
                    <div class="calibration-draft-actions">
                        <button type="button" class="btn btn-secondary btn-sm" data-action="fit">Fit</button>
                        <button type="button" class="btn btn-secondary btn-sm" data-action="template9">9-Point</button>
                        <button type="button" class="btn btn-secondary btn-sm" data-action="upstream">Upstream</button>
                        <button type="button" class="btn btn-primary btn-sm" data-action="solve">Recalc Draft</button>
                        <button type="button" class="btn btn-secondary btn-sm" data-action="export" disabled>Export</button>
                    </div>
                </div>
                <div class="calibration-draft-options"></div>
                <div class="calibration-draft-stage">
                    <canvas id="${this.canvasId}" class="calibration-draft-canvas"></canvas>
                    <div class="calibration-draft-empty">Run node preview once to load the input image.</div>
                </div>
                <div class="calibration-draft-status"></div>
                <div class="calibration-draft-table-wrap"></div>
                <div class="calibration-draft-import">
                    <textarea rows="3" placeholder="Paste CSV: pixelX,pixelY,worldX,worldY or JSON sample array"></textarea>
                    <div class="calibration-draft-actions">
                        <button type="button" class="btn btn-secondary btn-sm" data-action="apply-paste">Apply Paste</button>
                        <button type="button" class="btn btn-secondary btn-sm" data-action="copy">Copy Samples</button>
                        <button type="button" class="btn btn-primary btn-sm" data-action="formal-save" disabled title="Save candidate as a Project asset">Formal Save</button>
                    </div>
                </div>
            </section>
        `;

        this.container.querySelector('[data-action="fit"]')?.addEventListener('click', () => this.imageCanvas?.fitToWindow());
        this.container.querySelector('[data-action="template9"]')?.addEventListener('click', () => this.applyNinePointTemplate());
        this.container.querySelector('[data-action="upstream"]')?.addEventListener('click', () => this.importUpstreamCandidates());
        this.container.querySelector('[data-action="solve"]')?.addEventListener('click', () => this.solveDraft());
        this.container.querySelector('[data-action="export"]')?.addEventListener('click', () => this.exportCandidateBundle());
        this.container.querySelector('[data-action="apply-paste"]')?.addEventListener('click', () => this.applyPastedSamples());
        this.container.querySelector('[data-action="copy"]')?.addEventListener('click', () => this.copySamples());
        this.container.querySelector('[data-action="formal-save"]')?.addEventListener('click', () => this.formalSaveCandidate());
        this.renderOptions();
        this.renderStatus();
        this.renderTable();
    }

    renderOptions() {
        const host = this.container?.querySelector('.calibration-draft-options');
        if (!host) {
            return;
        }

        host.replaceChildren();
        const mode = createElement('select', 'calibration-draft-input');
        ['Affine', 'Perspective'].forEach(value => {
            const option = createElement('option', '', value);
            option.value = value;
            option.selected = this.session.mode === value;
            mode.appendChild(option);
        });
        mode.addEventListener('change', () => this.updateSession({ mode: mode.value, dirty: true, status: 'Draft' }));
        host.appendChild(this.wrapControl('Mode', mode));

        host.appendChild(this.numberOption('Unit', 'unit', this.session.unit, 'text'));
        Object.entries({
            ransacReprojectionThreshold: 'RANSAC threshold',
            ransacMaxIterations: 'Iterations',
            ransacConfidence: 'Confidence',
            maxAcceptedReprojectionError: 'Max error',
            minInlierCount: 'Min inliers',
            minInlierRatio: 'Min ratio'
        }).forEach(([key, label]) => {
            host.appendChild(this.numberOption(label, key, this.session.solverOptions[key], 'number'));
        });
    }

    wrapControl(label, control) {
        const wrapper = createElement('label', 'calibration-draft-control');
        wrapper.appendChild(createElement('span', '', label));
        wrapper.appendChild(control);
        return wrapper;
    }

    numberOption(label, key, value, type) {
        const input = createElement('input', 'calibration-draft-input');
        input.type = type;
        input.value = value ?? '';
        input.addEventListener('change', () => {
            if (key === 'unit') {
                this.updateSession({ unit: input.value.trim() || 'mm', dirty: true, status: 'Draft' });
                return;
            }

            this.session.solverOptions[key] = type === 'number' ? Number(input.value) : input.value;
            this.updateSession({ dirty: true, status: 'Draft' }, false);
        });
        return this.wrapControl(label, input);
    }

    renderStatus(message = null) {
        const host = this.container?.querySelector('.calibration-draft-status');
        if (!host) {
            return;
        }

        host.replaceChildren();
        const metrics = [
            ['Status', message || this.session.status],
            ['Samples', `${this.session.samples.length}`],
            ['Enabled', `${this.session.samples.filter(sample => sample.enabled !== false).length}`],
            ['Accepted', this.session.lastSolveResult ? String(this.session.lastSolveResult.accepted === true) : ''],
            ['Mean', formatNumber(this.session.lastSolveResult?.meanError)],
            ['Max', formatNumber(this.session.lastSolveResult?.maxError)],
            ['Asset', this.session.formalAssetId ? `${this.session.formalAssetId} / r${this.session.formalAssetRevision ?? '-'}` : '']
        ];
        metrics.forEach(([label, value]) => {
            const item = createElement('div', 'calibration-draft-metric');
            item.appendChild(createElement('span', 'calibration-draft-metric-label', label));
            item.appendChild(createElement('strong', 'calibration-draft-metric-value', value || '-'));
            host.appendChild(item);
        });

        const exportButton = this.container?.querySelector('[data-action="export"]');
        if (exportButton) {
            exportButton.disabled = !this.session.candidateBundleJson;
        }
        const formalSaveButton = this.container?.querySelector('[data-action="formal-save"]');
        if (formalSaveButton) {
            const hasProjectContext = Boolean(this.getProjectId());
            const project = this.getProject?.() || {};
            const revision = Number(project.persistenceRevision ?? project.PersistenceRevision);
            const hasRevision = Number.isSafeInteger(revision) && revision >= 0;
            formalSaveButton.disabled = this.formalSaveInProgress ||
                !hasProjectContext ||
                !hasRevision ||
                !this.session.solveArtifactId;
            formalSaveButton.title = !hasProjectContext
                ? 'Open a saved Project before Formal Save'
                : !hasRevision
                    ? 'Reload the Project revision before Formal Save'
                    : 'Save the server solve artifact as a Project asset';
            formalSaveButton.textContent = this.formalSaveInProgress ? 'Saving...' : 'Formal Save';
        }
        const empty = this.container?.querySelector('.calibration-draft-empty');
        if (empty) {
            empty.style.display = this.currentImageSource ? 'none' : 'flex';
        }
    }

    renderTable() {
        const host = this.container?.querySelector('.calibration-draft-table-wrap');
        if (!host) {
            return;
        }

        const table = createElement('table', 'calibration-draft-table');
        table.innerHTML = `
            <thead>
                <tr>
                    <th>#</th><th>Use</th><th>Pixel X</th><th>Pixel Y</th><th>World X</th><th>World Y</th>
                    <th>Src</th><th>Inlier</th><th>Error</th><th>Note</th><th></th>
                </tr>
            </thead>
            <tbody></tbody>
        `;
        const body = table.querySelector('tbody');
        this.session.samples.forEach((sample, index) => {
            body.appendChild(this.renderSampleRow(sample, index));
        });
        host.replaceChildren(table);
    }

    renderSampleRow(sample, index) {
        const row = createElement('tr', isSampleValid(sample) ? '' : 'invalid');
        row.dataset.sampleId = sample.sampleId;
        row.dataset.selected = sample.sampleId === this.selectedSampleId ? 'true' : 'false';
        row.appendChild(createElement('td', '', String(index + 1)));
        const enabled = createElement('input');
        enabled.type = 'checkbox';
        enabled.checked = sample.enabled !== false;
        enabled.addEventListener('change', () => {
            sample.enabled = enabled.checked;
            this.markDirty();
        });
        const enabledCell = createElement('td');
        enabledCell.appendChild(enabled);
        row.appendChild(enabledCell);
        ['pixelX', 'pixelY', 'worldX', 'worldY'].forEach(key => {
            const input = createElement('input', 'calibration-draft-cell-input');
            input.type = 'number';
            input.step = 'any';
            input.value = sample[key] ?? '';
            input.addEventListener('change', () => {
                sample[key] = readNumber(input.value);
                this.markDirty();
            });
            const cell = createElement('td');
            cell.appendChild(input);
            row.appendChild(cell);
        });
        row.appendChild(createElement('td', '', sample.source || 'ManualClick'));
        row.appendChild(createElement('td', '', sample.inlier === null || sample.inlier === undefined ? '-' : (sample.inlier ? 'yes' : 'no')));
        row.appendChild(createElement('td', '', formatNumber(sample.error)));
        const note = createElement('input', 'calibration-draft-cell-input');
        note.value = sample.note || '';
        note.addEventListener('change', () => {
            sample.note = note.value;
            this.markDirty(false);
        });
        const noteCell = createElement('td');
        noteCell.appendChild(note);
        row.appendChild(noteCell);
        const actions = createElement('td', 'calibration-draft-row-actions');
        [
            ['↑', () => this.moveSample(index, -1)],
            ['↓', () => this.moveSample(index, 1)],
            ['×', () => this.deleteSample(index)]
        ].forEach(([label, handler]) => {
            const button = createElement('button', 'btn btn-secondary btn-xs', label);
            button.type = 'button';
            button.addEventListener('click', handler);
            actions.appendChild(button);
        });
        row.appendChild(actions);
        row.addEventListener('click', event => {
            if (event.target?.tagName === 'INPUT' || event.target?.tagName === 'BUTTON') {
                return;
            }

            this.selectedSampleId = sample.sampleId;
            this.renderOverlay();
            this.renderTable();
        });
        return row;
    }

    updateSession(patch, renderAll = true) {
        Object.assign(this.session, patch, { updatedAtUtc: new Date().toISOString() });
        if (renderAll) {
            this.renderOptions();
            this.renderStatus();
            this.renderTable();
            this.renderOverlay();
        }
    }

    markDirty(renderTable = true) {
        this.session.dirty = true;
        this.session.status = 'Draft';
        this.session.lastSolveResult = null;
        this.session.candidateBundle = null;
        this.session.candidateBundleJson = null;
        this.session.solveArtifactId = null;
        this.session.formalAssetId = null;
        this.session.formalAssetRevision = null;
        this.session.formalAssetHash = null;
        this.session.samples.forEach(sample => {
            sample.inlier = null;
            sample.reprojectionX = null;
            sample.reprojectionY = null;
            sample.error = null;
        });
        this.renderStatus();
        if (renderTable) {
            this.renderTable();
        }
        this.renderOverlay();
    }

    handleCanvasClick(event) {
        if (!this.imageCanvas?.image || event.defaultPrevented) {
            return;
        }

        const point = this.imageCanvas.getImagePointFromEvent(event);
        const geometry = sessionToGeometry(this.session);
        const hit = hitTestPointSequencePoint(point, geometry, { scale: this.imageCanvas.scale, offset: this.imageCanvas.offset }, this.imageCanvas.handleSize);
        if (hit) {
            const index = Number(hit.split(':')[1]);
            if (Number.isInteger(index) && this.session.samples[index]) {
                this.selectedSampleId = this.session.samples[index].sampleId;
                this.renderOverlay();
                this.renderTable();
            }
            return;
        }

        const imagePoint = clampImagePoint(point, this.imageBounds);
        this.session.samples.push(normalizeSample({
            order: this.session.samples.length + 1,
            pixelX: imagePoint.x,
            pixelY: imagePoint.y,
            worldX: null,
            worldY: null,
            source: 'ManualClick'
        }, this.session.samples.length, 'ManualClick'));
        this.selectedSampleId = this.session.samples.at(-1)?.sampleId || null;
        this.markDirty();
    }

    handleOverlayChanged(geometry, phase) {
        if (!geometry?.points || phase !== 'commit') {
            return;
        }

        geometry.points.forEach((point, index) => {
            const sample = this.session.samples[index];
            if (!sample) {
                return;
            }

            sample.pixelX = point.x;
            sample.pixelY = point.y;
        });
        this.markDirty();
    }

    renderOverlay() {
        if (!this.imageCanvas) {
            return;
        }

        this.imageCanvas.setEditableGeometry(sessionToGeometry(this.session), {
            resetDraft: false,
            color: '#16a34a',
            fill: false
        });
        const overlay = this.imageCanvas.getPrimaryEditableOverlay?.();
        if (overlay) {
            overlay.selectedPointIndex = this.session.samples.findIndex(sample => sample.sampleId === this.selectedSampleId);
        }

        const overlays = [];
        this.session.samples.forEach((sample, index) => {
            if (!Number.isFinite(Number(sample.reprojectionX)) || !Number.isFinite(Number(sample.reprojectionY))) {
                return;
            }

            overlays.push({
                id: `calibration-reprojection:${sample.sampleId}`,
                type: 'point',
                x: sample.reprojectionX,
                y: sample.reprojectionY,
                width: 1,
                height: 1,
                radius: 3,
                color: sample.inlier === false ? '#ef4444' : '#2563eb',
                fillColor: sample.inlier === false ? 'rgba(239,68,68,0.72)' : 'rgba(37,99,235,0.72)',
                fill: true,
                selectable: false,
                zOrder: 300 + index
            });
            overlays.push({
                id: `calibration-error-vector:${sample.sampleId}`,
                type: 'polyline',
                x: 0,
                y: 0,
                width: 1,
                height: 1,
                points: [
                    { x: sample.pixelX, y: sample.pixelY },
                    { x: sample.reprojectionX, y: sample.reprojectionY }
                ],
                color: sample.inlier === false ? '#dc2626' : '#0ea5e9',
                lineWidth: 1,
                selectable: false,
                zOrder: 260 + index
            });
        });
        this.imageCanvas.setOverlayGroup('calibration-draft-result', overlays);
    }

    applyNinePointTemplate() {
        const bounds = this.imageBounds || { width: 300, height: 300 };
        const xs = [0.2, 0.5, 0.8].map(value => (Number(bounds.width) - 1) * value);
        const ys = [0.2, 0.5, 0.8].map(value => (Number(bounds.height) - 1) * value);
        const samples = [];
        ys.forEach(y => {
            xs.forEach(x => {
                samples.push(normalizeSample({
                    order: samples.length + 1,
                    pixelX: x,
                    pixelY: y,
                    worldX: null,
                    worldY: null,
                    source: 'Template'
                }, samples.length, 'Template'));
            });
        });
        this.session.samples = samples;
        this.selectedSampleId = samples[0]?.sampleId || null;
        this.markDirty();
    }

    importUpstreamCandidates() {
        const candidates = extractPointCandidates(this.previewState?.outputData || {});
        candidates.slice(0, 64).forEach(candidate => {
            const point = clampImagePoint(candidate, this.imageBounds);
            this.session.samples.push(normalizeSample({
                order: this.session.samples.length + 1,
                pixelX: point.x,
                pixelY: point.y,
                worldX: null,
                worldY: null,
                source: candidate.source
            }, this.session.samples.length, candidate.source));
        });
        this.markDirty();
    }

    moveSample(index, direction) {
        const next = index + direction;
        if (next < 0 || next >= this.session.samples.length) {
            return;
        }

        const samples = this.session.samples;
        [samples[index], samples[next]] = [samples[next], samples[index]];
        samples.forEach((sample, orderIndex) => {
            sample.order = orderIndex + 1;
        });
        this.markDirty();
    }

    deleteSample(index) {
        this.session.samples.splice(index, 1);
        this.session.samples.forEach((sample, orderIndex) => {
            sample.order = orderIndex + 1;
        });
        this.markDirty();
    }

    async solveDraft() {
        const operator = this.getOperator();
        const projectId = this.getProjectId() || EMPTY_GUID;
        const targetNodeId = operator?.id || EMPTY_GUID;
        const clientRequestSequence = ++this.solveSequence;
        this.solveAbort?.abort?.();
        this.solveAbort = new AbortController();
        this.session.status = 'Solving';
        this.renderStatus();

        try {
            const response = await httpClient.post('/calibration/npoint-draft/solve', {
                sessionId: this.session.sessionId,
                projectId,
                targetNodeId,
                debugSessionId: this.getDebugSessionId(projectId, targetNodeId) || EMPTY_GUID,
                clientRequestSequence,
                flowRevision: Number(this.getFlowRevision() || 0),
                imageIdentity: this.session.imageIdentity,
                mode: this.session.mode,
                unit: this.session.unit,
                solverOptions: this.session.solverOptions,
                samples: this.session.samples
            }, { signal: this.solveAbort.signal });

            if (clientRequestSequence !== this.solveSequence) {
                return;
            }

            this.session.status = response.status || (response.success ? 'Solved' : 'Failed');
            this.session.samples = (response.samples || []).map((sample, index) => normalizeSample(sample, index, sample.source || 'Imported'));
            this.session.lastSolveResult = response.lastSolveResult || null;
            this.session.candidateBundle = response.candidateBundle || null;
            this.session.candidateBundleJson = response.candidateBundleJson || null;
            this.session.formalAssetId = null;
            this.session.formalAssetRevision = null;
            this.session.formalAssetHash = null;
            this.session.artifacts = response.artifacts || [];
            this.session.solveArtifactId = this.session.artifacts.find((artifact) =>
                artifact?.kind === 'calibrationSolveBundle')?.artifactId || null;
            this.session.diagnostics = response.diagnostics || [];
            this.session.dirty = response.success !== true;
            this.renderStatus(response.success ? null : response.errorMessage);
            this.renderTable();
            this.renderOverlay();
        } catch (error) {
            if (error?.name === 'AbortError') {
                return;
            }

            this.session.status = 'Failed';
            this.session.diagnostics = [error?.message || 'Draft solve failed.'];
            this.renderStatus(error?.message || 'Draft solve failed.');
        }
    }

    async formalSaveCandidate() {
        if (!this.session.solveArtifactId) {
            this.renderStatus('Recalc a server-backed draft before Formal Save.');
            return;
        }

        const projectId = this.getProjectId() || EMPTY_GUID;
        if (!projectId || projectId === EMPTY_GUID) {
            this.renderStatus('Open a saved Project before Formal Save; this draft remains display-only.');
            return;
        }

        const project = this.getProject?.() || {};
        const expectedRevision = Number(project.persistenceRevision ?? project.PersistenceRevision);
        if (!Number.isSafeInteger(expectedRevision) || expectedRevision < 0) {
            this.renderStatus('Reload the Project revision before Formal Save.');
            return;
        }
        this.formalSaveInProgress = true;
        this.session.status = 'Saving';
        this.renderStatus('Saving formal asset...');

        try {
            const response = await httpClient.post(`/projects/${projectId}/calibration-assets/from-draft`, {
                expectedPersistenceRevision: expectedRevision,
                sessionId: this.session.sessionId,
                targetNodeId: this.getOperator()?.id || EMPTY_GUID,
                imageIdentity: this.session.imageIdentity,
                solveArtifactId: this.session.solveArtifactId
            });
            const asset = response?.asset || response?.Asset || {};
            const revision = response?.persistenceRevision ?? response?.PersistenceRevision ?? asset.projectRevision ?? asset.ProjectRevision;
            this.session.status = 'FormalSaved';
            this.session.dirty = false;
            this.session.formalAssetId = asset.assetId || asset.AssetId || '';
            this.session.formalAssetRevision = revision ?? null;
            this.session.formalAssetHash = asset.contentHash || asset.ContentHash || response?.assetsHash || response?.AssetsHash || '';
            this.onFormalSaveSuccess?.(response);
            this.renderStatus(`Saved ${this.session.formalAssetId || 'asset'} at revision ${this.session.formalAssetRevision ?? '-'}.`);
        } catch (error) {
            this.session.status = 'FormalSaveFailed';
            this.session.diagnostics = [error?.message || 'Formal Save failed.'];
            this.renderStatus(this.session.diagnostics[0]);
        } finally {
            this.formalSaveInProgress = false;
            this.renderStatus(this.session.status === 'FormalSaveFailed'
                ? (this.session.diagnostics?.[0] || 'Formal Save failed.')
                : null);
        }
    }

    exportCandidateBundle() {
        if (!this.session.candidateBundleJson) {
            return;
        }

        const project = this.getProjectId() || 'no-project';
        const node = this.getOperator()?.id || 'no-node';
        const timestamp = new Date().toISOString().replace(/[:.]/gu, '-');
        const payload = {
            draftNotice: 'Draft candidate only / 未正式保存到工程资产',
            schemaVersion: 'calibration-candidate-bundle.v1',
            sessionId: this.session.sessionId,
            projectId: project,
            targetNodeId: node,
            candidateBundle: JSON.parse(this.session.candidateBundleJson)
        };
        const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `calibration-candidate-${project}-${node}-${this.session.sessionId}-${timestamp}.json`;
        link.click();
        URL.revokeObjectURL(url);
    }

    applyPastedSamples() {
        const textarea = this.container?.querySelector('.calibration-draft-import textarea');
        const text = textarea?.value?.trim();
        if (!text) {
            return;
        }

        let imported = [];
        try {
            const parsed = JSON.parse(text);
            imported = Array.isArray(parsed) ? parsed : [];
        } catch {
            imported = text.split(/\r?\n/u)
                .map(line => line.split(/[,\t]/u).map(item => item.trim()))
                .filter(parts => parts.length >= 4)
                .map(parts => ({
                    pixelX: Number(parts[0]),
                    pixelY: Number(parts[1]),
                    worldX: Number(parts[2]),
                    worldY: Number(parts[3])
                }));
        }

        imported.forEach(item => {
            this.session.samples.push(normalizeSample({
                ...item,
                order: this.session.samples.length + 1,
                source: 'Imported'
            }, this.session.samples.length, 'Imported'));
        });
        this.markDirty();
    }

    async copySamples() {
        const payload = JSON.stringify(this.session.samples.map(sample => ({
            sampleId: sample.sampleId,
            order: sample.order,
            pixelX: sample.pixelX,
            pixelY: sample.pixelY,
            worldX: sample.worldX,
            worldY: sample.worldY,
            source: sample.source,
            enabled: sample.enabled,
            note: sample.note
        })), null, 2);
        try {
            await navigator.clipboard?.writeText(payload);
        } catch {
            const textarea = this.container?.querySelector('.calibration-draft-import textarea');
            if (textarea) {
                textarea.value = payload;
            }
        }
    }
}

export default CalibrationDraftWorkbench;
