import {
    createCategoryIconElement,
    createOperatorIconElement,
    createPathIconElement
} from '../../shared/operatorIconRenderer.js';
import {
    buildOperatorSearchText,
    searchOperators
} from '../../shared/operatorSearch.js';

export const GLOBAL_SEARCH_GROUP_KEY = 'global-search';

export const OPERATOR_CATEGORY_ORDER = Object.freeze([
    '采集',
    '图像预处理',
    '分割与区域',
    '特征提取',
    '匹配与定位',
    '缺陷检测',
    '测量',
    '标定与坐标',
    'AI推理',
    '3D点云',
    '数据处理',
    '流程控制',
    '通信',
    '输出与辅助'
]);

const CATEGORY_ORDER_BY_NAME = new Map(
    OPERATOR_CATEGORY_ORDER.map((name, index) => [name, index + 1]));

const SEARCH_ICON_PATH =
    'M9.5 3a6.5 6.5 0 0 1 5.17 10.44l4.45 4.45-1.41 1.41-4.45-4.45A6.5 6.5 0 1 1 9.5 3zm0 2a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9z';

const GLOBAL_SEARCH_GROUP = {
    key: GLOBAL_SEARCH_GROUP_KEY,
    label: '搜索',
    kind: 'global-search',
    operators: []
};

const STATIC_GROUPS = [
    {
        key: 'recent',
        label: '最近',
        emptyTitle: '暂无最近使用',
        emptyText: '添加算子后，这里会显示最近使用的入口。'
    },
    {
        key: 'favorite',
        label: '收藏',
        emptyTitle: '暂无收藏算子',
        emptyText: '当前没有收藏数据源，可先从分组中选择算子。'
    }
];

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function normalizeText(value) {
    return String(value ?? '').trim();
}

function getOperatorTitle(operator) {
    return normalizeText(
        operator?.displayName ||
        operator?.DisplayName ||
        operator?.title ||
        operator?.name ||
        operator?.Name ||
        operator?.type ||
        operator?.Type ||
        '未命名算子');
}

function getOperatorType(operator) {
    return normalizeText(operator?.type || operator?.Type);
}

function getOperatorCategory(operator) {
    return normalizeText(operator?.category || operator?.Category || '其他') || '其他';
}

function getOperatorCategoryOrder(operator) {
    const explicitOrder = Number(operator?.categoryOrder ?? operator?.CategoryOrder);
    if (Number.isFinite(explicitOrder) && explicitOrder > 0) {
        return explicitOrder;
    }

    return CATEGORY_ORDER_BY_NAME.get(getOperatorCategory(operator)) || Number.MAX_SAFE_INTEGER;
}

function getOperatorLifecycle(operator) {
    return normalizeText(operator?.lifecycle || operator?.Lifecycle || 'Stable') || 'Stable';
}

function getLifecycleLabel(lifecycle) {
    return ({
        Stable: '稳定',
        Experimental: '实验',
        Reference: '参考',
        Legacy: '旧版',
        Deprecated: '已弃用'
    })[lifecycle] || lifecycle;
}

export { buildOperatorSearchText };

export function buildOperatorGroups(operators = []) {
    const buckets = new Map();
    for (const operator of operators) {
        const category = getOperatorCategory(operator);
        if (!buckets.has(category)) {
            buckets.set(category, {
                order: getOperatorCategoryOrder(operator),
                operators: []
            });
        }
        const bucket = buckets.get(category);
        bucket.order = Math.min(bucket.order, getOperatorCategoryOrder(operator));
        bucket.operators.push(operator);
    }

    return Array.from(buckets.entries())
        .sort(([leftLabel, left], [rightLabel, right]) =>
            left.order - right.order || leftLabel.localeCompare(rightLabel, 'zh-Hans-CN'))
        .map(([label, bucket]) => ({
            key: `category:${label}`,
            label,
            kind: 'category',
            categoryOrder: bucket.order,
            operators: bucket.operators.slice().sort((left, right) =>
                getOperatorTitle(left).localeCompare(getOperatorTitle(right), 'zh-Hans-CN'))
        }));
}

export function filterOperatorsForFlyout(operators = [], keyword = '') {
    return searchOperators(operators, keyword);
}

export function buildPaletteGroupsFromCategories(categoryGroups = []) {
    return [
        { ...GLOBAL_SEARCH_GROUP },
        ...STATIC_GROUPS.map(group => ({
            ...group,
            kind: 'static',
            operators: []
        })),
        ...(Array.isArray(categoryGroups) ? categoryGroups : [])
    ];
}

export function buildPaletteGroups(operators = []) {
    return buildPaletteGroupsFromCategories(buildOperatorGroups(operators));
}

export function isGlobalSearchGroupKey(groupKey) {
    return groupKey === GLOBAL_SEARCH_GROUP_KEY;
}

export function clampScrollValue(value = 0, scrollSize = 0, clientSize = 0) {
    const numericValue = Number.isFinite(Number(value)) ? Number(value) : 0;
    const max = Math.max(0, Number(scrollSize || 0) - Number(clientSize || 0));
    return Math.min(Math.max(0, numericValue), max);
}

export function clampScrollState(scrollState = {}, metrics = {}) {
    return {
        scrollTop: clampScrollValue(scrollState.scrollTop, metrics.scrollHeight, metrics.clientHeight),
        scrollLeft: clampScrollValue(scrollState.scrollLeft, metrics.scrollWidth, metrics.clientWidth)
    };
}

export function createFlyoutViewModel({
    activeGroup = null,
    allOperators = [],
    searchTerm = ''
} = {}) {
    const term = normalizeText(searchTerm);
    const globalMode = isGlobalSearchGroupKey(activeGroup?.key);
    const sourceOperators = globalMode
        ? (Array.isArray(allOperators) ? allOperators : [])
        : (Array.isArray(activeGroup?.operators) ? activeGroup.operators : []);
    const operators = filterOperatorsForFlyout(sourceOperators, term);
    const hasSearch = term.length > 0;
    const scopeLabel = globalMode ? '全部算子' : (activeGroup?.label || '当前分组');
    const title = globalMode ? '全部算子' : (activeGroup?.label || '算子');
    const subtitle = globalMode
        ? `搜索范围：全部算子 · ${hasSearch ? `${operators.length} 个匹配结果` : `${sourceOperators.length} 个算子`}`
        : activeGroup?.kind === 'category'
            ? (hasSearch
                ? `搜索范围：${scopeLabel} · ${operators.length} 个匹配结果`
                : `${sourceOperators.length} 个算子`)
            : (activeGroup?.kind === 'static'
                ? '静态入口'
                : `搜索范围：${scopeLabel}`);
    const emptyTitle = hasSearch
        ? '未找到匹配算子'
        : (activeGroup?.emptyTitle || '该分组暂无算子');
    const emptyText = hasSearch
        ? '未找到匹配算子，可尝试输入算子名称、类型、端口或参数。'
        : globalMode
            ? '请输入关键词搜索全部算子，或直接浏览全部算子。'
            : (activeGroup?.emptyText || '该分组当前没有可添加的算子。');

    return {
        mode: globalMode ? 'global' : 'category',
        globalMode,
        hasSearch,
        sourceOperators,
        operators,
        title,
        subtitle,
        emptyTitle,
        emptyText,
        placeholder: globalMode
            ? '搜索全部算子：名称、类型、端口、参数'
            : '搜索本分类算子',
        showCategoryLabels: globalMode
    };
}

export function createOperatorPayload(operator) {
    return operator && typeof operator === 'object' ? { ...operator } : null;
}

export class OperatorPaletteShell {
    constructor({
        rail,
        flyout,
        libraryPanel = null,
        onOperatorAdd = () => {},
        onOperatorDragStart = () => {}
    } = {}) {
        this.rail = typeof rail === 'string' ? document.querySelector(rail) : rail;
        this.flyout = typeof flyout === 'string' ? document.querySelector(flyout) : flyout;
        this.libraryPanel = libraryPanel;
        this.onOperatorAdd = typeof onOperatorAdd === 'function' ? onOperatorAdd : () => {};
        this.onOperatorDragStart = typeof onOperatorDragStart === 'function' ? onOperatorDragStart : () => {};
        this.activeGroupKey = null;
        this.searchTerm = '';
        this.groups = [];
        this.renderedOperators = [];
        this.disposed = false;

        this.handleRailClick = this.handleRailClick.bind(this);
        this.handleFlyoutClick = this.handleFlyoutClick.bind(this);
        this.handleFlyoutInput = this.handleFlyoutInput.bind(this);
        this.handleFlyoutDragStart = this.handleFlyoutDragStart.bind(this);
        this.handleDocumentClick = this.handleDocumentClick.bind(this);
        this.handleKeyDown = this.handleKeyDown.bind(this);
        this.handleLibraryUpdated = this.handleLibraryUpdated.bind(this);

        this.rail?.addEventListener('click', this.handleRailClick);
        this.flyout?.addEventListener('click', this.handleFlyoutClick);
        this.flyout?.addEventListener('input', this.handleFlyoutInput);
        this.flyout?.addEventListener('dragstart', this.handleFlyoutDragStart);
        document.addEventListener('click', this.handleDocumentClick);
        document.addEventListener('keydown', this.handleKeyDown);
        this.libraryPanel?.container?.addEventListener?.('operator-library:updated', this.handleLibraryUpdated);

        this.syncFromLibrary();
    }

    attachLibraryPanel(libraryPanel) {
        this.libraryPanel?.container?.removeEventListener?.('operator-library:updated', this.handleLibraryUpdated);
        this.libraryPanel = libraryPanel;
        this.libraryPanel?.container?.addEventListener?.('operator-library:updated', this.handleLibraryUpdated);
        this.syncFromLibrary();
    }

    handleLibraryUpdated() {
        this.syncFromLibrary();
    }

    getOperators() {
        return this.libraryPanel?.getOperators?.() || [];
    }

    syncFromLibrary() {
        if (this.disposed) {
            return;
        }

        this.groups = buildOperatorGroups(this.getOperators());
        if (!this.activeGroupKey && this.groups.length > 0) {
            this.activeGroupKey = this.groups[0].key;
        }
        this.renderRail();
        if (this.isFlyoutOpen()) {
            this.renderFlyout();
        }
    }

    getAllGroups() {
        return buildPaletteGroupsFromCategories(this.groups);
    }

    getActiveGroup() {
        const allGroups = this.getAllGroups();
        return allGroups.find(group => group.key === this.activeGroupKey) || allGroups[0] || null;
    }

    isGlobalSearchActive() {
        return isGlobalSearchGroupKey(this.activeGroupKey);
    }

    getRailScroller() {
        return this.rail?.querySelector?.('.operator-rail-inner') || this.rail || null;
    }

    getFlyoutListScroller() {
        return this.flyout?.querySelector?.('[data-palette-list="true"]') || null;
    }

    captureScrollState(element) {
        if (!element) {
            return null;
        }

        return {
            scrollTop: element.scrollTop || 0,
            scrollLeft: element.scrollLeft || 0
        };
    }

    restoreScrollState(getElement, scrollState) {
        if (!scrollState || typeof getElement !== 'function') {
            return;
        }

        const restore = () => {
            const element = getElement();
            if (!element) {
                return;
            }

            const next = clampScrollState(scrollState, {
                scrollHeight: element.scrollHeight,
                clientHeight: element.clientHeight,
                scrollWidth: element.scrollWidth,
                clientWidth: element.clientWidth
            });
            element.scrollTop = next.scrollTop;
            element.scrollLeft = next.scrollLeft;
        };

        const win = globalThis.window;
        if (typeof win?.requestAnimationFrame === 'function') {
            win.requestAnimationFrame(restore);
        } else {
            restore();
        }
    }

    renderRail() {
        if (!this.rail) {
            return;
        }

        const railScroll = this.captureScrollState(this.getRailScroller());
        const groups = this.getAllGroups();
        this.rail.innerHTML = `
            <div class="operator-rail-inner" data-testid="operator-rail">
                ${groups.map(group => `
                    <button type="button"
                            class="operator-rail-item"
                            data-palette-group="${escapeHtml(group.key)}"
                            data-palette-kind="${escapeHtml(group.kind || '')}"
                            aria-pressed="${group.key === this.activeGroupKey ? 'true' : 'false'}"
                            title="${escapeHtml(group.label)}">
                        <span class="operator-rail-icon" data-rail-icon="${escapeHtml(group.key)}"></span>
                        <span class="operator-rail-label">${escapeHtml(group.label)}</span>
                    </button>
                `).join('')}
            </div>
        `;

        groups.forEach(group => {
            const iconHost = Array.from(this.rail.querySelectorAll('[data-rail-icon]'))
                .find(element => element.dataset.railIcon === group.key);
            if (!iconHost) {
                return;
            }

            if (group.kind === 'global-search') {
                iconHost.replaceChildren(createPathIconElement(SEARCH_ICON_PATH, 'operator-rail-svg', '#d4a853'));
                return;
            }

            if (group.kind === 'static') {
                iconHost.textContent = group.key === 'recent' ? '近' : '藏';
                return;
            }

            iconHost.replaceChildren(createCategoryIconElement(group.label, 'operator-rail-svg'));
        });

        this.restoreScrollState(() => this.getRailScroller(), railScroll);
    }

    renderFlyout({ preserveListScroll = true } = {}) {
        if (!this.flyout) {
            return;
        }

        const listScroll = preserveListScroll
            ? this.captureScrollState(this.getFlyoutListScroller())
            : null;
        const activeGroup = this.getActiveGroup();
        const allOperators = this.getOperators();
        const viewModel = createFlyoutViewModel({
            activeGroup,
            allOperators,
            searchTerm: this.searchTerm
        });
        const operators = viewModel.operators;
        this.renderedOperators = operators;

        this.flyout.innerHTML = `
            <section class="operator-group-flyout-panel" data-testid="operator-group-flyout" data-palette-mode="${escapeHtml(viewModel.mode)}">
                <header class="operator-flyout-header">
                    <div>
                        <div class="operator-flyout-eyebrow">算子组</div>
                        <h3>${escapeHtml(viewModel.title)}</h3>
                        <p>${escapeHtml(viewModel.subtitle)}</p>
                    </div>
                    <button type="button" class="operator-flyout-close" data-palette-close="true" aria-label="关闭算子组">×</button>
                </header>
                <label class="operator-flyout-search">
                    <span>搜索</span>
                    <input type="search"
                           class="cv-input"
                           data-palette-search="true"
                           placeholder="${escapeHtml(viewModel.placeholder)}"
                           autocomplete="off"
                           value="${escapeHtml(this.searchTerm)}">
                </label>
                <label class="operator-flyout-compatibility">
                    <input type="checkbox"
                           data-palette-compatibility="true"
                           ${this.libraryPanel?.getIncludeCompatibility?.() ? 'checked' : ''}>
                    <span>显示兼容算子（旧版/已弃用）</span>
                </label>
                <div class="operator-flyout-list" data-palette-list="true">
                    ${operators.length > 0
                        ? operators.map((operator, index) =>
                            this.renderOperatorItem(operator, index, {
                                showCategory: viewModel.showCategoryLabels
                            })).join('')
                        : `
                            <div class="operator-flyout-empty">
                                <strong>${escapeHtml(viewModel.emptyTitle)}</strong>
                                <span>${escapeHtml(viewModel.emptyText)}</span>
                            </div>
                        `}
                </div>
            </section>
        `;

        operators.forEach((operator, index) => {
            const iconHost = this.flyout.querySelector(`[data-operator-icon="${index}"]`);
            iconHost?.replaceChildren(createOperatorIconElement(operator, 'operator-flyout-svg'));
        });

        this.restoreScrollState(() => this.getFlyoutListScroller(), listScroll);
    }

    renderOperatorItem(operator, index, { showCategory = false } = {}) {
        const title = getOperatorTitle(operator);
        const type = getOperatorType(operator);
        const category = getOperatorCategory(operator);
        const description = operator?.description || operator?.Description || '暂无说明';
        const lifecycle = getOperatorLifecycle(operator);
        const lifecycleNote = operator?.lifecycleNote || operator?.LifecycleNote || '';
        const lifecycleLabel = getLifecycleLabel(lifecycle);
        const lifecycleDescription = lifecycleNote
            ? `${lifecycleLabel}：${lifecycleNote}`
            : `生命周期：${lifecycleLabel}`;
        const lifecycleDescriptionId = `operator-lifecycle-note-${index}`;
        const inputCount = (operator?.inputPorts || operator?.InputPorts || []).length;
        const outputCount = (operator?.outputPorts || operator?.OutputPorts || []).length;

        return `
            <button type="button"
                    class="operator-flyout-item"
                    draggable="true"
                    data-operator-index="${index}"
                    data-operator-type="${escapeHtml(type)}"
                    ${lifecycle !== 'Stable' ? `aria-describedby="${escapeHtml(lifecycleDescriptionId)}"` : ''}
                    title="添加 ${escapeHtml(title)}">
                <span class="operator-flyout-drag">⋮⋮</span>
                <span class="operator-flyout-icon" data-operator-icon="${index}"></span>
                <span class="operator-flyout-main">
                    <span class="operator-flyout-title">
                        <strong>${escapeHtml(title)}</strong>
                        ${lifecycle !== 'Stable'
                            ? `<span class="operator-lifecycle-badge operator-lifecycle-${escapeHtml(lifecycle.toLowerCase())}" title="${escapeHtml(lifecycleDescription)}" aria-hidden="true">${escapeHtml(lifecycleLabel)}</span>`
                            : ''}
                    </span>
                    <span class="operator-flyout-detail">
                        ${showCategory ? `<span class="operator-flyout-category">${escapeHtml(category)}</span>` : ''}
                        <em>${escapeHtml(description)}</em>
                    </span>
                </span>
                <span class="operator-flyout-meta">
                    ${escapeHtml(inputCount)} 入 / ${escapeHtml(outputCount)} 出
                </span>
                ${lifecycle !== 'Stable'
                    ? `<span id="${escapeHtml(lifecycleDescriptionId)}" class="operator-flyout-lifecycle-note">${escapeHtml(lifecycleDescription)}</span>`
                    : ''}
            </button>
        `;
    }

    handleRailClick(event) {
        const button = event.target?.closest?.('[data-palette-group]');
        if (!button) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        this.openGroup(button.dataset.paletteGroup);
    }

    handleFlyoutClick(event) {
        event.stopPropagation();
        if (event.target?.closest?.('[data-palette-close]')) {
            event.preventDefault();
            this.closeFlyout();
            return;
        }

        const item = event.target?.closest?.('[data-operator-index]');
        if (!item) {
            return;
        }

        event.preventDefault();
        const operator = this.renderedOperators[Number(item.dataset.operatorIndex)];
        if (!operator) {
            return;
        }

        const payload = createOperatorPayload(operator);
        if (!payload) {
            return;
        }

        this.onOperatorAdd(payload);
        this.closeFlyout();
    }

    handleFlyoutInput(event) {
        if (event.target?.dataset?.paletteCompatibility === 'true') {
            void this.libraryPanel?.setIncludeCompatibility?.(event.target.checked);
            return;
        }

        if (event.target?.dataset?.paletteSearch !== 'true') {
            return;
        }

        this.searchTerm = event.target.value || '';
        this.renderFlyout({ preserveListScroll: true });
        const searchInput = this.flyout?.querySelector('[data-palette-search="true"]');
        searchInput?.focus();
        try {
            const cursor = searchInput?.value?.length ?? 0;
            searchInput?.setSelectionRange?.(cursor, cursor);
        } catch {
            // Some browser search inputs do not support selection ranges.
        }
    }

    handleFlyoutDragStart(event) {
        const item = event.target?.closest?.('[data-operator-index]');
        if (!item) {
            return;
        }

        const operator = this.renderedOperators[Number(item.dataset.operatorIndex)];
        if (!operator) {
            return;
        }

        const payload = createOperatorPayload(operator);
        if (!payload) {
            return;
        }

        event.dataTransfer?.setData('application/json', JSON.stringify(payload));
        if (event.dataTransfer) {
            event.dataTransfer.effectAllowed = 'copy';
        }
        window.__draggingOperatorData = payload;
        this.onOperatorDragStart(payload);

        const clearDragData = () => {
            window.setTimeout(() => {
                if (window.__draggingOperatorData === payload) {
                    window.__draggingOperatorData = null;
                }
            }, 500);
            item.removeEventListener('dragend', clearDragData);
        };
        item.addEventListener('dragend', clearDragData);
    }

    handleDocumentClick(event) {
        if (!this.isFlyoutOpen()) {
            return;
        }

        if (this.flyout?.contains(event.target) || this.rail?.contains(event.target)) {
            return;
        }

        this.closeFlyout();
    }

    handleKeyDown(event) {
        if (event.key === 'Escape' && this.isFlyoutOpen()) {
            this.closeFlyout();
        }
    }

    openGroup(groupKey) {
        const isSameGroup = this.activeGroupKey === groupKey;
        this.activeGroupKey = groupKey;
        if (!isSameGroup) {
            this.searchTerm = '';
        }
        this.renderRail();
        this.renderFlyout({ preserveListScroll: isSameGroup });
        this.flyout?.classList.remove('hidden');
        this.flyout?.setAttribute('aria-hidden', 'false');
        this.flyout?.querySelector('[data-palette-search="true"]')?.focus();
    }

    closeFlyout() {
        this.flyout?.classList.add('hidden');
        this.flyout?.setAttribute('aria-hidden', 'true');
    }

    isFlyoutOpen() {
        return Boolean(this.flyout && !this.flyout.classList.contains('hidden'));
    }

    dispose() {
        this.disposed = true;
        this.rail?.removeEventListener('click', this.handleRailClick);
        this.flyout?.removeEventListener('click', this.handleFlyoutClick);
        this.flyout?.removeEventListener('input', this.handleFlyoutInput);
        this.flyout?.removeEventListener('dragstart', this.handleFlyoutDragStart);
        document.removeEventListener('click', this.handleDocumentClick);
        document.removeEventListener('keydown', this.handleKeyDown);
        this.libraryPanel?.container?.removeEventListener?.('operator-library:updated', this.handleLibraryUpdated);
    }
}

export default OperatorPaletteShell;
