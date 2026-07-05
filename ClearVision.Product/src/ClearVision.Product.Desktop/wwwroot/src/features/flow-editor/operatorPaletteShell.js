import {
    createCategoryIconElement,
    createOperatorIconElement
} from '../../shared/operatorIconRenderer.js';

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

function buildSearchText(operator) {
    const ports = [
        ...(Array.isArray(operator?.inputPorts) ? operator.inputPorts : []),
        ...(Array.isArray(operator?.InputPorts) ? operator.InputPorts : []),
        ...(Array.isArray(operator?.outputPorts) ? operator.outputPorts : []),
        ...(Array.isArray(operator?.OutputPorts) ? operator.OutputPorts : [])
    ];
    const parameters = [
        ...(Array.isArray(operator?.parameters) ? operator.parameters : []),
        ...(Array.isArray(operator?.Parameters) ? operator.Parameters : [])
    ];
    const tags = [
        ...(Array.isArray(operator?.tags) ? operator.tags : []),
        ...(Array.isArray(operator?.Tags) ? operator.Tags : []),
        ...(Array.isArray(operator?.keywords) ? operator.keywords : []),
        ...(Array.isArray(operator?.Keywords) ? operator.Keywords : [])
    ];

    return [
        getOperatorTitle(operator),
        getOperatorType(operator),
        getOperatorCategory(operator),
        operator?.description,
        operator?.Description,
        ...tags,
        ...ports.flatMap(port => [
            port?.name,
            port?.Name,
            port?.displayName,
            port?.DisplayName,
            port?.dataType,
            port?.DataType,
            port?.type,
            port?.Type
        ]),
        ...parameters.flatMap(parameter => [
            parameter?.name,
            parameter?.Name,
            parameter?.displayName,
            parameter?.DisplayName,
            parameter?.description,
            parameter?.Description,
            parameter?.dataType,
            parameter?.DataType,
            parameter?.type,
            parameter?.Type
        ])
    ].filter(Boolean).join(' ').toLowerCase();
}

export function buildOperatorGroups(operators = []) {
    const buckets = new Map();
    for (const operator of operators) {
        const category = getOperatorCategory(operator);
        if (!buckets.has(category)) {
            buckets.set(category, []);
        }
        buckets.get(category).push(operator);
    }

    return Array.from(buckets.entries())
        .sort(([left], [right]) => left.localeCompare(right, 'zh-Hans-CN'))
        .map(([label, items]) => ({
            key: `category:${label}`,
            label,
            kind: 'category',
            operators: items.slice().sort((left, right) =>
                getOperatorTitle(left).localeCompare(getOperatorTitle(right), 'zh-Hans-CN'))
        }));
}

export function filterOperatorsForFlyout(operators = [], keyword = '') {
    const term = normalizeText(keyword).toLowerCase();
    if (!term) {
        return operators.slice();
    }

    return operators.filter(operator => buildSearchText(operator).includes(term));
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
        return [
            ...STATIC_GROUPS.map(group => ({
                ...group,
                kind: 'static',
                operators: []
            })),
            ...this.groups
        ];
    }

    getActiveGroup() {
        const allGroups = this.getAllGroups();
        return allGroups.find(group => group.key === this.activeGroupKey) || allGroups[0] || null;
    }

    renderRail() {
        if (!this.rail) {
            return;
        }

        const groups = this.getAllGroups();
        this.rail.innerHTML = `
            <div class="operator-rail-inner" data-testid="operator-rail">
                ${groups.map(group => `
                    <button type="button"
                            class="operator-rail-item"
                            data-palette-group="${escapeHtml(group.key)}"
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

            if (group.kind === 'static') {
                iconHost.textContent = group.key === 'recent' ? '近' : '藏';
                return;
            }

            iconHost.replaceChildren(createCategoryIconElement(group.label, 'operator-rail-svg'));
        });
    }

    renderFlyout() {
        if (!this.flyout) {
            return;
        }

        const activeGroup = this.getActiveGroup();
        const allOperators = this.getOperators();
        const searching = normalizeText(this.searchTerm).length > 0;
        const sourceOperators = searching
            ? allOperators
            : (activeGroup?.operators || []);
        const operators = filterOperatorsForFlyout(sourceOperators, this.searchTerm);
        this.renderedOperators = operators;

        const title = searching ? '搜索算子' : (activeGroup?.label || '算子');
        const subtitle = searching
            ? `共 ${operators.length} 个匹配结果`
            : activeGroup?.kind === 'category'
                ? `${operators.length} 个算子`
                : '静态入口';
        const emptyTitle = searching
            ? '未找到匹配算子'
            : (activeGroup?.emptyTitle || '该分组暂无算子');
        const emptyText = searching
            ? '请换一个关键词，或从左侧分组浏览。'
            : (activeGroup?.emptyText || '该分组当前没有可添加的算子。');

        this.flyout.innerHTML = `
            <section class="operator-group-flyout-panel" data-testid="operator-group-flyout">
                <header class="operator-flyout-header">
                    <div>
                        <div class="operator-flyout-eyebrow">算子组</div>
                        <h3>${escapeHtml(title)}</h3>
                        <p>${escapeHtml(subtitle)}</p>
                    </div>
                    <button type="button" class="operator-flyout-close" data-palette-close="true" aria-label="关闭算子组">×</button>
                </header>
                <label class="operator-flyout-search">
                    <span>搜索</span>
                    <input type="search"
                           class="cv-input"
                           data-palette-search="true"
                           placeholder="搜索算子名称、类型、端口"
                           autocomplete="off"
                           value="${escapeHtml(this.searchTerm)}">
                </label>
                <div class="operator-flyout-list" data-palette-list="true">
                    ${operators.length > 0
                        ? operators.map((operator, index) => this.renderOperatorItem(operator, index)).join('')
                        : `
                            <div class="operator-flyout-empty">
                                <strong>${escapeHtml(emptyTitle)}</strong>
                                <span>${escapeHtml(emptyText)}</span>
                            </div>
                        `}
                </div>
            </section>
        `;

        operators.forEach((operator, index) => {
            const iconHost = this.flyout.querySelector(`[data-operator-icon="${index}"]`);
            iconHost?.replaceChildren(createOperatorIconElement(operator, 'operator-flyout-svg'));
        });
    }

    renderOperatorItem(operator, index) {
        const title = getOperatorTitle(operator);
        const type = getOperatorType(operator);
        const description = operator?.description || operator?.Description || '暂无说明';
        const inputCount = (operator?.inputPorts || operator?.InputPorts || []).length;
        const outputCount = (operator?.outputPorts || operator?.OutputPorts || []).length;

        return `
            <button type="button"
                    class="operator-flyout-item"
                    draggable="true"
                    data-operator-index="${index}"
                    data-operator-type="${escapeHtml(type)}"
                    title="添加 ${escapeHtml(title)}">
                <span class="operator-flyout-drag">⋮⋮</span>
                <span class="operator-flyout-icon" data-operator-icon="${index}"></span>
                <span class="operator-flyout-main">
                    <strong>${escapeHtml(title)}</strong>
                    <em>${escapeHtml(description)}</em>
                </span>
                <span class="operator-flyout-meta">
                    ${escapeHtml(inputCount)} 入 / ${escapeHtml(outputCount)} 出
                </span>
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

        this.onOperatorAdd({ ...operator });
        this.closeFlyout();
    }

    handleFlyoutInput(event) {
        if (event.target?.dataset?.paletteSearch !== 'true') {
            return;
        }

        this.searchTerm = event.target.value || '';
        this.renderFlyout();
        this.flyout?.querySelector('[data-palette-search="true"]')?.focus();
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

        const payload = { ...operator };
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
        this.activeGroupKey = groupKey;
        this.searchTerm = '';
        this.renderRail();
        this.renderFlyout();
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
