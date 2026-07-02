import {
    getNodePreviewIdentitySignature,
    normalizeNodePreviewIdentity
} from './nodePreviewSelectionStore.js';

const DEFAULT_ROW_LIMIT = 80;
const ROW_LIMIT_INCREMENT = 80;
const MAX_SEARCH_NODES = 2048;
const MAX_DISPLAY_CHARS = 512;
export const MAX_ARTIFACT_TEXT_PREVIEW_BYTES = 64 * 1024;
export const MAX_ARTIFACT_TEXT_DISPLAY_CHARS = 4096;
const ARTIFACT_UNAVAILABLE_TEXT = '资源已过期或不可用';
const ARTIFACT_TEXT_TOO_LARGE_TEXT = '内容过大，仅展示元数据';

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

function asString(value, fallback = '') {
    if (value === undefined || value === null) {
        return fallback;
    }

    return String(value);
}

function clipForDisplay(value, maxChars = MAX_DISPLAY_CHARS) {
    const text = asString(value);
    return text.length <= maxChars
        ? text
        : `${text.slice(0, maxChars)}...`;
}

function normalizeBool(value) {
    return value === true || value === 'true' || value === 1;
}

function normalizeKindKey(value) {
    return asString(value, 'unknown').trim().toLowerCase();
}

function getKindKey(node) {
    return normalizeKindKey(readOwn(node, 'kind', 'Kind'));
}

function getOriginalTypeKey(node) {
    return asString(readOwn(node, 'originalType', 'OriginalType')).trim().toLowerCase();
}

function getObservationFromState(state) {
    const observation = readOwn(state, 'observation', 'Observation');
    return observation && typeof observation === 'object' ? observation : null;
}

function getObservationIdentityFromState(state) {
    const observation = getObservationFromState(state);
    const identity = readOwn(observation, 'identity', 'Identity');
    return identity && typeof identity === 'object' ? identity : null;
}

function getObservationIdentitySignatureFromState(state) {
    return getNodePreviewIdentitySignature(getObservationIdentityFromState(state));
}

function getObservationOutcome(observation) {
    const outcome = readOwn(observation, 'outcome', 'Outcome');
    return outcome && typeof outcome === 'object' ? outcome : {};
}

function getObservationSummary(observation) {
    const summary = readOwn(observation, 'summary', 'Summary');
    return Array.isArray(summary) ? summary : [];
}

function getObservationDiagnostics(observation) {
    const diagnostics = readOwn(observation, 'diagnostics', 'Diagnostics');
    return Array.isArray(diagnostics) ? diagnostics : [];
}

function getObservationLimits(observation) {
    const limits = readOwn(observation, 'limits', 'Limits');
    return limits && typeof limits === 'object' ? limits : {};
}

function getObservationDetail(observation) {
    const detail = readOwn(observation, 'detail', 'Detail');
    return detail && typeof detail === 'object' ? detail : null;
}

function normalizeArtifactReference(artifact) {
    if (!artifact || typeof artifact !== 'object') {
        return null;
    }

    const artifactId = readOwn(artifact, 'artifactId', 'ArtifactId');
    if (!artifactId || typeof artifactId !== 'string') {
        return null;
    }

    return {
        artifactId,
        kind: asString(readOwn(artifact, 'kind', 'Kind')),
        role: asString(readOwn(artifact, 'role', 'Role')),
        pathHint: asString(readOwn(artifact, 'pathHint', 'PathHint'), '$'),
        contentType: asString(readOwn(artifact, 'contentType', 'ContentType'), 'application/octet-stream'),
        length: Number(readOwn(artifact, 'length', 'Length') || 0),
        sha256: asString(readOwn(artifact, 'sha256', 'Sha256')),
        width: readOwn(artifact, 'width', 'Width') ?? null,
        height: readOwn(artifact, 'height', 'Height') ?? null,
        channels: readOwn(artifact, 'channels', 'Channels') ?? null,
        createdAtUtc: readOwn(artifact, 'createdAtUtc', 'CreatedAtUtc') ?? null,
        expiresAtUtc: readOwn(artifact, 'expiresAtUtc', 'ExpiresAtUtc') ?? null
    };
}

function normalizeObservationNode(node) {
    if (!node || typeof node !== 'object') {
        return {
            kind: 'unknown',
            displayValue: '',
            originalType: null,
            name: null,
            pathHint: '$',
            addressable: false,
            truncated: false,
            artifact: null,
            outputPortId: null,
            outputPortName: null,
            resultPathVersion: null,
            resultPath: null,
            bindableVariableTypes: [],
            childCount: 0
        };
    }

    const children = readOwn(node, 'children', 'Children');
    const outputPortId = readOwn(node, 'outputPortId', 'OutputPortId');
    const outputPortName = readOwn(node, 'outputPortName', 'OutputPortName');
    const resultPathVersion = readOwn(node, 'resultPathVersion', 'ResultPathVersion');
    const resultPath = readOwn(node, 'resultPath', 'ResultPath');
    return {
        kind: asString(readOwn(node, 'kind', 'Kind'), 'unknown'),
        displayValue: readOwn(node, 'displayValue', 'DisplayValue') === undefined
            ? ''
            : asString(readOwn(node, 'displayValue', 'DisplayValue')),
        originalType: readOwn(node, 'originalType', 'OriginalType') === undefined
            ? null
            : asString(readOwn(node, 'originalType', 'OriginalType')),
        name: readOwn(node, 'name', 'Name') === undefined
            ? null
            : asString(readOwn(node, 'name', 'Name')),
        pathHint: asString(readOwn(node, 'pathHint', 'PathHint'), '$'),
        addressable: normalizeBool(readOwn(node, 'addressable', 'Addressable')),
        truncated: normalizeBool(readOwn(node, 'truncated', 'Truncated')),
        artifact: normalizeArtifactReference(readOwn(node, 'artifact', 'Artifact')),
        outputPortId: outputPortId === undefined || outputPortId === null ? null : asString(outputPortId),
        outputPortName: outputPortName === undefined || outputPortName === null ? null : asString(outputPortName),
        resultPathVersion: resultPathVersion === undefined || resultPathVersion === null ? null : resultPathVersion,
        resultPath: resultPath === undefined || resultPath === null ? null : asString(resultPath),
        bindableVariableTypes: normalizeBindableVariableTypes(readOwn(node, 'bindableVariableTypes', 'BindableVariableTypes')),
        childCount: Array.isArray(children) ? children.length : 0
    };
}

function normalizeBindableVariableTypes(value) {
    return Array.isArray(value)
        ? Array.from(new Set(value
            .map(item => String(item || '').trim())
            .filter(Boolean)))
        : [];
}

function getNodeChildren(node) {
    const children = readOwn(node, 'children', 'Children');
    return Array.isArray(children) ? children : [];
}

function hasExplicitKind(node, ...kindKeys) {
    return kindKeys.includes(getKindKey(node));
}

function hasControlledOriginalType(node, ...typeNames) {
    const originalType = getOriginalTypeKey(node);
    if (!originalType) {
        return false;
    }

    return typeNames.some(typeName => {
        const normalizedTypeName = String(typeName).toLowerCase();
        return originalType === normalizedTypeName ||
            originalType.endsWith(`.${normalizedTypeName}`) ||
            originalType.endsWith(`+${normalizedTypeName}`);
    });
}

const SCALAR_KIND_KEYS = new Set([
    'null',
    'boolean',
    'number',
    'string',
    'enum',
    'guid',
    'datetime',
    'duration',
    'nonfinitenumber'
]);

const RESOURCE_KIND_KEYS = new Set([
    'matrix',
    'image',
    'mask',
    'binary',
    'stream',
    'resource',
    'profile',
    'pointset'
]);

const CONTAINER_KIND_KEYS = new Set(['dictionary', 'object', 'array']);
const BOUNDED_KIND_KEYS = new Set(['truncated', 'unsupportedenumerable', 'objectdescriptor', 'circular']);
const NON_BINDABLE_SCALAR_KIND_KEYS = new Set(['nonfinitenumber']);

const rendererDefinitions = [
    {
        name: 'scalar',
        matches: node => SCALAR_KIND_KEYS.has(getKindKey(node)),
        render: node => {
            const normalized = normalizeObservationNode(node);
            return {
                renderer: 'scalar',
                label: normalized.kind,
                value: normalized.displayValue,
                meta: normalized.originalType
            };
        }
    },
    {
        name: 'point',
        matches: node => hasExplicitKind(node, 'point') ||
            hasControlledOriginalType(node, 'point', 'pointf', 'point2d', 'point2f', 'point3d', 'point3f'),
        render: node => renderKnownShapeNode(node, 'Point')
    },
    {
        name: 'circle',
        matches: node => hasExplicitKind(node, 'circle') ||
            hasControlledOriginalType(node, 'circle', 'circle2d', 'circlef'),
        render: node => renderKnownShapeNode(node, 'Circle')
    },
    {
        name: 'line',
        matches: node => hasExplicitKind(node, 'line') ||
            hasControlledOriginalType(node, 'line', 'line2d', 'linef'),
        render: node => renderKnownShapeNode(node, 'Line')
    },
    {
        name: 'rectangle',
        matches: node => hasExplicitKind(node, 'rectangle', 'rect') ||
            hasControlledOriginalType(node, 'rectangle', 'rectanglef', 'rect'),
        render: node => renderKnownShapeNode(node, 'Rectangle')
    },
    {
        name: 'resource',
        matches: node => RESOURCE_KIND_KEYS.has(getKindKey(node)),
        render: node => {
            const normalized = normalizeObservationNode(node);
            return {
                renderer: 'resource',
                label: normalized.kind,
                value: normalized.displayValue || 'descriptor',
                meta: normalized.artifact ? 'artifact' : normalized.originalType
            };
        }
    },
    {
        name: 'detectionList',
        matches: node => hasExplicitKind(node, 'detectionlist'),
        render: node => renderKnownShapeNode(node, 'Detection List', 'detectionList')
    },
    {
        name: 'detection',
        matches: node => hasExplicitKind(node, 'detection'),
        render: node => renderKnownShapeNode(node, 'Detection')
    },
    {
        name: 'calibrationQuality',
        matches: node => hasExplicitKind(node, 'calibrationquality') ||
            hasControlledOriginalType(node, 'calibrationquality'),
        render: node => renderKnownShapeNode(node, 'Calibration Quality', 'calibrationQuality')
    },
    {
        name: 'container',
        matches: node => CONTAINER_KIND_KEYS.has(getKindKey(node)),
        render: node => {
            const normalized = normalizeObservationNode(node);
            return {
                renderer: 'container',
                label: normalized.kind,
                value: normalized.displayValue || `${normalized.childCount} children`,
                meta: normalized.originalType
            };
        }
    },
    {
        name: 'bounded',
        matches: node => BOUNDED_KIND_KEYS.has(getKindKey(node)),
        render: node => {
            const normalized = normalizeObservationNode(node);
            return {
                renderer: 'bounded',
                label: normalized.kind,
                value: normalized.displayValue,
                meta: normalized.originalType || `${normalized.childCount} children`
            };
        }
    }
];

function renderKnownShapeNode(node, label, rendererName = null) {
    const normalized = normalizeObservationNode(node);
    return {
        renderer: rendererName || label.replace(/\s+/g, '').toLowerCase(),
        label,
        value: normalized.displayValue || `${normalized.childCount} fields`,
        meta: normalized.originalType
    };
}

function renderUnknownNode(node) {
    const normalized = normalizeObservationNode(node);
    return {
        renderer: 'unknown',
        label: normalized.kind || 'unknown',
        value: normalized.displayValue,
        meta: normalized.originalType || `${normalized.childCount} children`
    };
}

export const nodePreviewRendererRegistry = Object.freeze({
    render(node) {
        const renderer = rendererDefinitions.find(definition => definition.matches(node));
        return renderer ? renderer.render(node) : renderUnknownNode(node);
    },
    coverage() {
        return rendererDefinitions.map(definition => definition.name).concat('unknown');
    }
});

function createNodeKey(node, indexPath) {
    const normalized = normalizeObservationNode(node);
    return `${normalized.pathHint}|${normalized.name || ''}|${indexPath}`;
}

function rowMatchesSearch(row, query) {
    if (!query) {
        return true;
    }

    const normalized = row.normalized;
    const haystack = [
        normalized.name,
        normalized.displayValue,
        normalized.originalType,
        normalized.pathHint
    ].filter(Boolean).join('\n').toLowerCase();
    return haystack.includes(query.toLowerCase());
}

export function buildVisibleObservationRows(root, options = {}) {
    if (!root || typeof root !== 'object') {
        return {
            rows: [],
            totalVisited: 0,
            hasMore: false,
            searchTruncated: false
        };
    }

    const limit = Math.max(1, Math.floor(Number(options.limit || DEFAULT_ROW_LIMIT)));
    const expandedKeys = options.expandedKeys instanceof Set ? options.expandedKeys : new Set();
    const query = String(options.searchQuery || '').trim();
    const rows = [];
    const stack = [{ node: root, depth: 0, indexPath: '0' }];
    let totalVisited = 0;
    let searchTruncated = false;

    while (stack.length > 0) {
        const current = stack.pop();
        totalVisited += 1;
        if (totalVisited > MAX_SEARCH_NODES) {
            searchTruncated = Boolean(query);
            break;
        }

        const normalized = normalizeObservationNode(current.node);
        const key = createNodeKey(current.node, current.indexPath);
        const rendered = nodePreviewRendererRegistry.render(current.node);
        const childCount = normalized.childCount;
        const expanded = current.depth === 0 || expandedKeys.has(key);
        const row = {
            key,
            node: current.node,
            normalized,
            rendered,
            depth: current.depth,
            childCount,
            expandable: childCount > 0,
            expanded
        };

        if (!query || rowMatchesSearch(row, query)) {
            rows.push(row);
            if (rows.length > limit) {
                break;
            }
        }

        if (query || expanded) {
            const children = getNodeChildren(current.node);
            for (let index = children.length - 1; index >= 0; index -= 1) {
                stack.push({
                    node: children[index],
                    depth: current.depth + 1,
                    indexPath: `${current.indexPath}.${index}`
                });
            }
        }
    }

    return {
        rows: rows.slice(0, limit),
        totalVisited,
        hasMore: rows.length > limit || stack.length > 0,
        searchTruncated
    };
}

export function searchObservationRows(root, query, limit = DEFAULT_ROW_LIMIT) {
    return buildVisibleObservationRows(root, {
        searchQuery: query,
        limit,
        expandedKeys: new Set()
    });
}

function createElement(tagName, className = '', text = null) {
    const element = document.createElement(tagName);
    if (className) {
        element.className = className;
    }
    if (text !== null && text !== undefined) {
        element.textContent = String(text);
    }
    return element;
}

function makeButton(className, label, onClick) {
    const button = createElement('button', className, label);
    button.type = 'button';
    button.addEventListener('click', event => {
        event.preventDefault();
        event.stopPropagation();
        onClick?.(event);
    });
    return button;
}

function appendMetric(container, label, value, className = 'node-preview-inspector-metric') {
    const item = createElement('div', className);
    item.appendChild(createElement('span', `${className}-label`, label));
    item.appendChild(createElement('span', `${className}-value`, value ?? '-'));
    container.appendChild(item);
}

function getPrimaryImageSource(state, artifact = null) {
    if (!artifact) {
        return state?.outputImageBase64 || state?.inputImageBase64 || null;
    }

    const role = String(artifact.role || '').toLowerCase();
    if (role === 'inputimage') {
        return state?.inputImageBase64 || null;
    }

    if (role === 'outputimage' || role === 'image') {
        return state?.outputImageBase64 || null;
    }

    return null;
}

function isImageArtifact(artifact) {
    return String(artifact?.contentType || '').toLowerCase().startsWith('image/') ||
        String(artifact?.kind || '').toLowerCase() === 'image';
}

function isTextArtifact(artifact) {
    const contentType = String(artifact?.contentType || '').toLowerCase();
    return contentType.startsWith('text/') || contentType.includes('json');
}

function isArtifactDeclaredTooLargeForTextPreview(artifact) {
    const length = Number(artifact?.length ?? 0);
    return Number.isFinite(length) && length > MAX_ARTIFACT_TEXT_PREVIEW_BYTES;
}

function buildArtifactMetadataPreview(artifact, message) {
    return [
        message,
        `Length: ${formatByteLength(artifact?.length)}`,
        `Content type: ${artifact?.contentType || 'application/octet-stream'}`,
        `SHA-256: ${artifact?.sha256 || '-'}`,
        `Expires: ${formatDateTime(artifact?.expiresAtUtc)}`
    ].join('\n');
}

function formatByteLength(value) {
    const numberValue = Number(value);
    if (!Number.isFinite(numberValue) || numberValue < 0) {
        return '-';
    }

    if (numberValue < 1024) {
        return `${numberValue} B`;
    }

    if (numberValue < 1024 * 1024) {
        return `${(numberValue / 1024).toFixed(1)} KB`;
    }

    return `${(numberValue / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDateTime(value) {
    if (!value) {
        return '-';
    }

    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

function normalizeArtifactList(state) {
    const artifacts = readOwn(state, 'artifacts', 'Artifacts');
    return Array.isArray(artifacts)
        ? artifacts.map(normalizeArtifactReference).filter(Boolean)
        : [];
}

function isArtifactUnavailableError(error) {
    return error?.status === 404 ||
        error?.statusCode === 404 ||
        String(error?.message || '').includes('404') ||
        String(error?.message || '').includes('expired');
}

async function copyText(value) {
    const text = asString(value);
    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        return;
    }

    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.setAttribute('readonly', 'readonly');
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    document.execCommand?.('copy');
    textarea.remove();
}

export class NodePreviewInspector {
    constructor(container, flowCanvas, previewCoordinator, options = {}) {
        this.container = container;
        this.flowCanvas = flowCanvas;
        this.previewCoordinator = previewCoordinator;
        this.selectionStore = options.selectionStore ?? null;
        this.onOpenImage = options.onOpenImage ?? (() => {});
        this.onBindGlobalVariable = options.onBindGlobalVariable ?? (() => {});
        this.state = this.previewCoordinator?.getState?.() ?? null;
        this.activeTab = 'summary';
        this.expandedKeys = new Set();
        this.rowLimit = DEFAULT_ROW_LIMIT;
        this.searchQuery = '';
        this.pendingSearchValue = '';
        this.searchTimer = null;
        this.artifactReadAbort = null;
        this.artifactReadToken = 0;
        this.artifactReadState = new Map();
        this.identitySignature = getObservationIdentitySignatureFromState(this.state);
        this.dismissedNodeId = null;
        this.destroyed = false;

        this.root = createElement('div', 'node-preview-inspector-root');
        this.container?.appendChild(this.root);

        this.unsubscribePreview = this.previewCoordinator?.subscribe?.(state => {
            const previousNodeId = this.state?.activeNodeId || null;
            const currentNodeId = state?.activeNodeId || null;
            if (previousNodeId !== currentNodeId) {
                this.dismissedNodeId = null;
            }

            this.handleStateChange(state);
            this.state = state;
            this.render();
        }) || null;

        this.unsubscribeView = this.flowCanvas?.subscribeViewState?.(() => {
            this.updatePosition();
        }) || null;

        this.render();
    }

    destroy() {
        this.destroyed = true;
        this.cancelSearch();
        this.cancelArtifactRead();
        this.unsubscribePreview?.();
        this.unsubscribeView?.();
        this.selectionStore?.clear?.();
        this.root?.remove();
    }

    isVisible() {
        const activeNodeId = this.state?.activeNodeId || null;
        return Boolean(activeNodeId && this.dismissedNodeId !== activeNodeId);
    }

    handleStateChange(nextState) {
        const nextSignature = getObservationIdentitySignatureFromState(nextState);
        const status = String(nextState?.status || 'idle');
        if (nextSignature !== this.identitySignature) {
            this.identitySignature = nextSignature;
            this.expandedKeys.clear();
            this.rowLimit = DEFAULT_ROW_LIMIT;
            this.searchQuery = '';
            this.pendingSearchValue = '';
            this.artifactReadState.clear();
            this.cancelSearch();
            this.cancelArtifactRead();
            this.selectionStore?.clear?.();
        }

        if (!nextSignature || status === 'error' || status === 'idle') {
            this.selectionStore?.clear?.();
        } else {
            this.selectionStore?.clearIfIdentityChanged?.(getObservationIdentityFromState(nextState));
        }
    }

    render() {
        if (!this.root) {
            return;
        }

        this.root.replaceChildren();
        if (!this.isVisible()) {
            this.root.classList.add('hidden');
            return;
        }

        this.root.classList.remove('hidden');
        const card = createElement('section', 'node-preview-inspector-card');
        card.dataset.nodePreviewInspector = 'true';
        card.appendChild(this.renderHeader());
        card.appendChild(this.renderTabs());
        card.appendChild(this.renderBody());
        this.root.appendChild(card);
        this.updatePosition();
    }

    renderHeader() {
        const header = createElement('header', 'node-preview-inspector-header');
        const titleBlock = createElement('div', 'node-preview-inspector-title-group');
        titleBlock.appendChild(createElement('div', 'node-preview-inspector-title', this.state?.title || '节点预览'));
        titleBlock.appendChild(createElement('div', 'node-preview-inspector-status', this.getStatusText()));
        header.appendChild(titleBlock);

        const actions = createElement('div', 'node-preview-inspector-actions');
        const imageSource = getPrimaryImageSource(this.state);
        if (imageSource) {
            actions.appendChild(makeButton('node-preview-inspector-action-btn', '打开图像', () => {
                this.onOpenImage(imageSource);
            }));
        }
        actions.appendChild(makeButton('node-preview-inspector-action-btn', '刷新', () => {
            this.previewCoordinator?.requestActivePreview?.({
                force: true,
                immediate: true
            });
        }));
        actions.appendChild(makeButton('node-preview-inspector-action-btn', '关闭', () => {
            this.dismissedNodeId = this.state?.activeNodeId || null;
            this.selectionStore?.clear?.();
            this.render();
        }));
        header.appendChild(actions);
        return header;
    }

    renderTabs() {
        const tabs = createElement('div', 'node-preview-inspector-tabs');
        [
            ['summary', 'Summary'],
            ['detail', 'Detail'],
            ['artifacts', 'Artifact']
        ].forEach(([tabId, label]) => {
            const button = makeButton('node-preview-inspector-tab', label, () => {
                this.activeTab = tabId;
                this.render();
            });
            button.dataset.active = this.activeTab === tabId ? 'true' : 'false';
            tabs.appendChild(button);
        });
        return tabs;
    }

    renderBody() {
        const body = createElement('div', 'node-preview-inspector-body');
        const status = String(this.state?.status || 'idle');
        const observation = getObservationFromState(this.state);

        if (status === 'loading') {
            body.appendChild(createElement('div', 'node-preview-inspector-state', '预览生成中...'));
            return body;
        }

        if (status === 'error') {
            body.appendChild(createElement('div', 'node-preview-inspector-state error', this.state?.errorMessage || '预览请求失败'));
            return body;
        }

        if (!this.state?.activeNodeId || !observation) {
            body.appendChild(createElement('div', 'node-preview-inspector-state', '暂无可查看的节点预览结果'));
            return body;
        }

        if (this.activeTab === 'detail') {
            body.appendChild(this.renderDetail(observation));
        } else if (this.activeTab === 'artifacts') {
            body.appendChild(this.renderArtifacts());
        } else {
            body.appendChild(this.renderSummary(observation));
        }

        return body;
    }

    getStatusText() {
        const status = String(this.state?.status || 'idle');
        if (status === 'loading') {
            return 'loading';
        }
        if (status === 'error') {
            return `error: ${this.state?.errorMessage || 'unknown'}`;
        }
        if (status === 'success') {
            return typeof this.state?.executionTimeMs === 'number'
                ? `success (${this.state.executionTimeMs} ms)`
                : 'success';
        }

        return 'empty';
    }

    renderSummary(observation) {
        const panel = createElement('div', 'node-preview-inspector-panel summary');
        const outcome = getObservationOutcome(observation);
        const diagnostics = getObservationDiagnostics(observation);
        const truncated = normalizeBool(readOwn(observation, 'truncated', 'Truncated'));
        const metrics = createElement('div', 'node-preview-inspector-metrics');

        appendMetric(metrics, 'Outcome', normalizeBool(readOwn(outcome, 'success', 'Success')) ? 'success' : 'failure');
        appendMetric(metrics, 'Time', `${readOwn(outcome, 'executionTimeMs', 'ExecutionTimeMs') ?? this.state?.executionTimeMs ?? '-'} ms`);
        appendMetric(metrics, 'Executed', readOwn(outcome, 'executedOperatorCount', 'ExecutedOperatorCount') ?? '-');
        appendMetric(metrics, 'Failed Node', readOwn(outcome, 'failedOperatorName', 'FailedOperatorName') || '-');
        appendMetric(metrics, 'Truncated', truncated ? 'yes' : 'no');
        appendMetric(metrics, 'Diagnostics', diagnostics.length);
        panel.appendChild(metrics);

        const summaryItems = getObservationSummary(observation);
        const list = createElement('div', 'node-preview-inspector-summary-list');
        if (summaryItems.length === 0) {
            list.appendChild(createElement('div', 'node-preview-inspector-empty-line', '暂无 summary items'));
        } else {
            summaryItems.forEach(item => {
                const row = createElement('div', 'node-preview-inspector-summary-row');
                appendMetric(row, 'Key', readOwn(item, 'key', 'Key') || '-', 'node-preview-inspector-inline-metric');
                appendMetric(row, 'Value', clipForDisplay(readOwn(item, 'displayValue', 'DisplayValue')), 'node-preview-inspector-inline-metric');
                appendMetric(row, 'Type', readOwn(item, 'originalType', 'OriginalType') || '-', 'node-preview-inspector-inline-metric');
                appendMetric(row, 'Path', readOwn(item, 'pathHint', 'PathHint') || '$', 'node-preview-inspector-inline-metric');
                appendMetric(row, 'Addressable', normalizeBool(readOwn(item, 'addressable', 'Addressable')) ? 'yes' : 'no', 'node-preview-inspector-inline-metric');
                list.appendChild(row);
            });
        }
        panel.appendChild(list);

        if (diagnostics.length > 0) {
            const diagnosticList = createElement('div', 'node-preview-inspector-diagnostics');
            diagnostics.forEach(item => {
                const row = createElement('div', 'node-preview-inspector-diagnostic');
                row.appendChild(createElement('span', 'code', readOwn(item, 'code', 'Code') || 'diagnostic'));
                row.appendChild(createElement('span', 'message', readOwn(item, 'message', 'Message') || ''));
                row.appendChild(createElement('span', 'path', readOwn(item, 'pathHint', 'PathHint') || '$'));
                diagnosticList.appendChild(row);
            });
            panel.appendChild(diagnosticList);
        }

        return panel;
    }

    renderDetail(observation) {
        const panel = createElement('div', 'node-preview-inspector-panel detail');
        const toolbar = createElement('div', 'node-preview-inspector-detail-toolbar');
        const search = createElement('input', 'node-preview-inspector-search');
        search.type = 'search';
        search.placeholder = 'Search';
        search.value = this.pendingSearchValue || this.searchQuery;
        search.addEventListener('input', event => {
            this.scheduleSearch(event.target.value);
        });
        toolbar.appendChild(search);

        const limits = getObservationLimits(observation);
        toolbar.appendChild(createElement('div', 'node-preview-inspector-limit-note', `row limit ${this.rowLimit}, DTO nodes ${readOwn(limits, 'maxNodes', 'MaxNodes') ?? MAX_SEARCH_NODES}`));
        panel.appendChild(toolbar);

        const detail = getObservationDetail(observation);
        const rowsResult = buildVisibleObservationRows(detail, {
            expandedKeys: this.expandedKeys,
            searchQuery: this.searchQuery,
            limit: this.rowLimit
        });
        const tree = createElement('div', 'node-preview-inspector-tree');
        tree.dataset.rowCount = String(rowsResult.rows.length);

        rowsResult.rows.forEach(row => {
            tree.appendChild(this.renderDetailRow(row));
        });
        if (rowsResult.rows.length === 0) {
            tree.appendChild(createElement('div', 'node-preview-inspector-empty-line', '无匹配字段'));
        }
        panel.appendChild(tree);

        const footer = createElement('div', 'node-preview-inspector-tree-footer');
        if (rowsResult.searchTruncated) {
            footer.appendChild(createElement('span', 'node-preview-inspector-warning', '搜索已按 Observation 节点上限截断'));
        }
        if (rowsResult.hasMore) {
            footer.appendChild(makeButton('node-preview-inspector-action-btn', '显示更多', () => {
                this.rowLimit += ROW_LIMIT_INCREMENT;
                this.render();
            }));
        }
        panel.appendChild(footer);
        return panel;
    }

    renderDetailRow(row) {
        const rowElement = createElement('div', 'node-preview-inspector-tree-row');
        rowElement.dataset.renderer = row.rendered.renderer;
        rowElement.dataset.addressable = row.normalized.addressable ? 'true' : 'false';
        rowElement.style.setProperty('--node-preview-depth', String(Math.min(row.depth, 8)));

        const expander = makeButton('node-preview-inspector-expander', row.expandable ? (row.expanded ? '-' : '+') : '', () => {
            if (!row.expandable) {
                return;
            }
            if (this.expandedKeys.has(row.key)) {
                this.expandedKeys.delete(row.key);
            } else {
                this.expandedKeys.add(row.key);
            }
            this.render();
        });
        expander.disabled = !row.expandable;
        rowElement.appendChild(expander);

        const content = createElement('button', 'node-preview-inspector-tree-content');
        content.type = 'button';
        content.addEventListener('click', event => {
            event.preventDefault();
            this.selectDetailRow(row);
        });
        content.appendChild(createElement('span', 'node-preview-inspector-node-name', row.normalized.name || row.rendered.label));
        content.appendChild(createElement('span', 'node-preview-inspector-node-kind', row.rendered.label));
        content.appendChild(createElement('span', 'node-preview-inspector-node-value', clipForDisplay(row.rendered.value)));
        content.appendChild(createElement('span', 'node-preview-inspector-node-path', row.normalized.pathHint));
        if (!row.normalized.addressable) {
            content.appendChild(createElement('span', 'node-preview-inspector-node-badge', 'not addressable'));
        }
        if (row.normalized.truncated) {
            content.appendChild(createElement('span', 'node-preview-inspector-node-badge warning', 'truncated'));
        }
        rowElement.appendChild(content);

        const actions = createElement('div', 'node-preview-inspector-row-actions');
        actions.appendChild(makeButton('node-preview-inspector-row-action', 'Copy value', () => {
            void copyText(row.normalized.displayValue);
        }));
        actions.appendChild(makeButton('node-preview-inspector-row-action', 'Copy path', () => {
            void copyText(row.normalized.pathHint);
        }));
        if (this.canBindGlobalVariable(row)) {
            const bindButton = makeButton('node-preview-inspector-row-action bind-global-variable', '绑定到全局变量', () => {
                const descriptor = this.selectDetailRow(row);
                if (descriptor && this.isBindableDescriptorCurrent(descriptor)) {
                    this.onBindGlobalVariable(descriptor);
                } else {
                    this.selectionStore?.clear?.();
                }
            });
            bindButton.dataset.action = 'bind-global-variable';
            actions.appendChild(bindButton);
        }
        rowElement.appendChild(actions);
        return rowElement;
    }

    selectDetailRow(row) {
        const descriptor = this.createSelectionDescriptor(row);
        if (!descriptor) {
            this.selectionStore?.clear?.();
            return null;
        }

        return this.selectionStore?.select?.(descriptor) || descriptor;
    }

    createSelectionDescriptor(row) {
        const identity = normalizeNodePreviewIdentity(getObservationIdentityFromState(this.state));
        if (!identity) {
            return;
        }

        return {
            identity,
            nodeName: this.state?.title || '',
            nodeKind: this.state?.nodeType || '',
            outputPortId: row.normalized.outputPortId,
            outputPortName: row.normalized.outputPortName,
            resultPathVersion: row.normalized.resultPathVersion,
            resultPath: row.normalized.resultPath,
            kind: row.normalized.kind,
            displayValue: row.normalized.displayValue,
            originalType: row.normalized.originalType,
            pathHint: row.normalized.pathHint,
            addressable: row.normalized.addressable,
            truncated: row.normalized.truncated,
            bindableVariableTypes: row.normalized.bindableVariableTypes,
            artifact: row.normalized.artifact
        };
    }

    canBindGlobalVariable(row) {
        const descriptor = this.createSelectionDescriptor(row);
        return Boolean(descriptor && this.isBindableDescriptorCurrent(descriptor));
    }

    isBindableDescriptorCurrent(descriptor) {
        if (!descriptor?.identity || !this.isCurrentObservationIdentity(descriptor.identity)) {
            return false;
        }

        const kindKey = normalizeKindKey(descriptor.kind);
        return SCALAR_KIND_KEYS.has(kindKey) &&
            !NON_BINDABLE_SCALAR_KIND_KEYS.has(kindKey) &&
            descriptor.addressable === true &&
            Boolean(descriptor.outputPortId) &&
            Boolean(descriptor.outputPortName) &&
            descriptor.resultPathVersion === 1 &&
            Boolean(descriptor.resultPath) &&
            descriptor.truncated !== true &&
            !descriptor.artifact &&
            Array.isArray(descriptor.bindableVariableTypes) &&
            descriptor.bindableVariableTypes.length > 0;
    }

    isCurrentObservationIdentity(identity) {
        const signature = getNodePreviewIdentitySignature(identity);
        if (!signature) {
            return false;
        }

        const currentState = this.previewCoordinator?.getState?.() ?? this.state;
        return signature === getObservationIdentitySignatureFromState(currentState) &&
            signature === getObservationIdentitySignatureFromState(this.state);
    }

    renderArtifacts() {
        const panel = createElement('div', 'node-preview-inspector-panel artifacts');
        const artifacts = normalizeArtifactList(this.state);
        if (artifacts.length === 0) {
            panel.appendChild(createElement('div', 'node-preview-inspector-empty-line', '暂无 Artifact 引用'));
            return panel;
        }

        artifacts.forEach(artifact => {
            panel.appendChild(this.renderArtifact(artifact));
        });
        return panel;
    }

    renderArtifact(artifact) {
        const item = createElement('article', 'node-preview-inspector-artifact');
        const title = createElement('div', 'node-preview-inspector-artifact-title');
        title.appendChild(createElement('span', 'kind', artifact.kind || 'artifact'));
        title.appendChild(createElement('span', 'role', artifact.role || '-'));
        item.appendChild(title);

        const grid = createElement('div', 'node-preview-inspector-artifact-grid');
        appendMetric(grid, 'Content type', artifact.contentType, 'node-preview-inspector-inline-metric');
        appendMetric(grid, 'Length', formatByteLength(artifact.length), 'node-preview-inspector-inline-metric');
        appendMetric(grid, 'SHA-256', artifact.sha256 || '-', 'node-preview-inspector-inline-metric');
        appendMetric(grid, 'Size', artifact.width && artifact.height ? `${artifact.width} x ${artifact.height}` : '-', 'node-preview-inspector-inline-metric');
        appendMetric(grid, 'Expires', formatDateTime(artifact.expiresAtUtc), 'node-preview-inspector-inline-metric');
        appendMetric(grid, 'Path', artifact.pathHint || '$', 'node-preview-inspector-inline-metric');
        item.appendChild(grid);

        const actions = createElement('div', 'node-preview-inspector-artifact-actions');
        if (isImageArtifact(artifact)) {
            actions.appendChild(makeButton('node-preview-inspector-action-btn', '打开图像', () => {
                this.openArtifactImage(artifact);
            }));
        } else {
            actions.appendChild(makeButton('node-preview-inspector-action-btn', '按需读取', () => {
                this.readArtifactOnDemand(artifact);
            }));
        }
        item.appendChild(actions);

        const readState = this.artifactReadState.get(artifact.artifactId);
        if (readState) {
            const status = createElement('pre', `node-preview-inspector-artifact-read ${readState.status}`);
            status.textContent = readState.text;
            item.appendChild(status);
        }

        return item;
    }

    scheduleSearch(value) {
        this.pendingSearchValue = String(value || '');
        this.cancelSearch();
        this.searchTimer = setTimeout(() => {
            this.searchTimer = null;
            this.searchQuery = this.pendingSearchValue.trim();
            this.rowLimit = DEFAULT_ROW_LIMIT;
            this.render();
        }, 160);
    }

    cancelSearch() {
        if (this.searchTimer) {
            clearTimeout(this.searchTimer);
            this.searchTimer = null;
        }
    }

    isArtifactReadCurrent(token, identity, abortController = null) {
        return !this.destroyed &&
            token === this.artifactReadToken &&
            abortController?.signal?.aborted !== true &&
            getObservationIdentitySignatureFromState(this.state) === getNodePreviewIdentitySignature(identity);
    }

    cancelArtifactRead() {
        this.artifactReadAbort?.abort?.();
        this.artifactReadAbort = null;
        this.artifactReadToken += 1;
    }

    async readArtifactOnDemand(artifact) {
        await this.startArtifactRead(artifact, 'text');
    }

    async openArtifactImage(artifact) {
        const existingSource = getPrimaryImageSource(this.state, artifact);
        if (existingSource) {
            this.onOpenImage(existingSource);
            return;
        }

        await this.startArtifactRead(artifact, 'image');
    }

    async startArtifactRead(artifact, mode) {
        const identity = normalizeNodePreviewIdentity(getObservationIdentityFromState(this.state));
        if (!identity || !artifact?.artifactId) {
            return;
        }

        this.cancelArtifactRead();
        const token = this.artifactReadToken;
        const artifactForRead = normalizeArtifactReference(artifact);
        if (!artifactForRead) {
            return;
        }

        if (mode !== 'image') {
            if (!isTextArtifact(artifactForRead)) {
                if (!this.isArtifactReadCurrent(token, identity)) {
                    return;
                }

                this.artifactReadState.set(artifactForRead.artifactId, {
                    status: 'success',
                    text: buildArtifactMetadataPreview(artifactForRead, '非文本 Artifact，仅展示元数据')
                });
                this.render();
                return;
            }

            if (isArtifactDeclaredTooLargeForTextPreview(artifactForRead)) {
                if (!this.isArtifactReadCurrent(token, identity)) {
                    return;
                }

                this.artifactReadState.set(artifactForRead.artifactId, {
                    status: 'success',
                    text: buildArtifactMetadataPreview(artifactForRead, ARTIFACT_TEXT_TOO_LARGE_TEXT)
                });
                this.render();
                return;
            }
        }

        const abortController = typeof AbortController !== 'undefined'
            ? new AbortController()
            : null;
        this.artifactReadAbort = abortController;
        this.artifactReadState.set(artifactForRead.artifactId, {
            status: 'loading',
            text: mode === 'image' ? '正在读取图像 Artifact...' : '正在按需读取 Artifact...'
        });
        this.render();

        try {
            if (!this.isArtifactReadCurrent(token, identity, abortController)) {
                return;
            }

            const result = await this.previewCoordinator?.readArtifactForCurrentState?.(
                artifactForRead.artifactId,
                identity,
                { signal: abortController?.signal, objectUrl: mode === 'image' });
            if (!this.isArtifactReadCurrent(token, identity, abortController)) {
                return;
            }

            if (mode === 'image') {
                if (result?.objectUrl) {
                    this.onOpenImage(result.objectUrl);
                    this.artifactReadState.set(artifactForRead.artifactId, {
                        status: 'success',
                        text: '图像 Artifact 已打开。'
                    });
                } else {
                    this.artifactReadState.set(artifactForRead.artifactId, {
                        status: 'error',
                        text: ARTIFACT_UNAVAILABLE_TEXT
                    });
                }
                this.render();
                return;
            }

            const blob = result?.blob;
            const artifactMetadata = normalizeArtifactReference(result?.artifact) || artifactForRead;
            const contentType = String(artifactMetadata.contentType || '').toLowerCase();
            let text = `已读取 ${formatByteLength(blob?.size ?? artifactMetadata.length)}。`;
            if (blob && (contentType.includes('json') || contentType.startsWith('text/'))) {
                if (typeof blob.slice !== 'function') {
                    throw new Error('Artifact Blob 不支持有界文本预览');
                }

                const actualSize = Number(blob.size ?? 0);
                const actualTextTooLarge = Number.isFinite(actualSize) && actualSize > MAX_ARTIFACT_TEXT_PREVIEW_BYTES;
                const previewBlob = blob.slice(0, MAX_ARTIFACT_TEXT_PREVIEW_BYTES);
                const rawText = await previewBlob.text();
                if (!this.isArtifactReadCurrent(token, identity, abortController)) {
                    return;
                }
                const displayText = clipForDisplay(rawText, MAX_ARTIFACT_TEXT_DISPLAY_CHARS);
                const displayTruncated = actualTextTooLarge || rawText.length > MAX_ARTIFACT_TEXT_DISPLAY_CHARS;
                text = displayTruncated
                    ? `${displayText}\n已截断。`
                    : displayText;
            }

            if (!this.isArtifactReadCurrent(token, identity, abortController)) {
                return;
            }

            this.artifactReadState.set(artifactForRead.artifactId, {
                status: 'success',
                text
            });
            this.render();
        } catch (error) {
            if (!this.isArtifactReadCurrent(token, identity, abortController) || error?.name === 'AbortError') {
                return;
            }

            this.artifactReadState.set(artifactForRead.artifactId, {
                status: 'error',
                text: isArtifactUnavailableError(error)
                    ? ARTIFACT_UNAVAILABLE_TEXT
                    : (error?.message || 'Artifact 读取失败')
            });
            this.render();
        } finally {
            if (this.artifactReadAbort === abortController) {
                this.artifactReadAbort = null;
            }
        }
    }

    updatePosition() {
        if (!this.isVisible() || !this.root) {
            return;
        }

        const card = this.root.querySelector('[data-node-preview-inspector="true"]');
        const activeNodeId = this.state?.activeNodeId || null;
        if (!card || !activeNodeId) {
            return;
        }

        const nodeRect = this.flowCanvas?.getNodeScreenRect?.(activeNodeId);
        const containerRect = this.container?.getBoundingClientRect?.();
        if (!nodeRect || !containerRect) {
            card.style.right = '16px';
            card.style.top = '16px';
            return;
        }

        const cardWidth = card.offsetWidth || 520;
        const cardHeight = card.offsetHeight || 420;
        const gap = 14;
        const padding = 12;
        const containerWidth = containerRect.width;
        const containerHeight = containerRect.height;

        let left = nodeRect.x + nodeRect.width + gap;
        if (left + cardWidth > containerWidth - padding) {
            left = nodeRect.x - cardWidth - gap;
        }
        if (left < padding) {
            left = Math.max(padding, containerWidth - cardWidth - padding);
        }

        let top = nodeRect.y;
        if (top + cardHeight > containerHeight - padding) {
            top = containerHeight - cardHeight - padding;
        }
        if (top < padding) {
            top = padding;
        }

        card.style.left = `${Math.round(left)}px`;
        card.style.top = `${Math.round(top)}px`;
    }
}

export default NodePreviewInspector;
