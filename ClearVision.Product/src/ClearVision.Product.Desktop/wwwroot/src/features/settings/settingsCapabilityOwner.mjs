import settingsApi from './settingsApi.js';

export const SETTINGS_CAPABILITY_OWNER_ID = 'settings-capability-v2';

const SETTINGS_TABS = Object.freeze([
    { id: 'system', label: '系统', configKey: 'system' },
    { id: 'plc', label: 'PLC', configKey: 'plc' },
    { id: 'station', label: 'Station', configKey: 'station' },
    { id: 'ai', label: 'AI', configKey: 'ai' },
    { id: 'camera', label: '相机', configKey: 'cameras' }
]);

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function deepClone(value) {
    if (value === null || value === undefined) {
        return value;
    }

    return typeof structuredClone === 'function'
        ? structuredClone(value)
        : JSON.parse(JSON.stringify(value));
}

function findTab(tabId) {
    return SETTINGS_TABS.find(tab => tab.id === tabId) || SETTINGS_TABS[0];
}

export class SettingsCapabilityAdapter {
    constructor({ settingsApiRef = settingsApi } = {}) {
        this.settingsApi = settingsApiRef;
    }

    async loadSettings() {
        return await this.settingsApi.loadSettings();
    }

    async saveSettings(config) {
        return await this.settingsApi.saveSettings(config);
    }
}

export function createSettingsCapabilityAdapter(options = {}) {
    return new SettingsCapabilityAdapter(options);
}

export class SettingsCapabilityOwner {
    constructor(container, {
        adapter,
        showToast = () => {}
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('SettingsCapabilityOwner requires a container.');
        }
        if (!adapter) {
            throw new Error('SettingsCapabilityOwner requires an adapter.');
        }

        this.adapter = adapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.config = null;
        this.activeTabId = SETTINGS_TABS[0].id;
        this.draftTextByTab = new Map();
        this.dirtyTabs = new Set();
        this.loading = false;
        this.errorMessage = '';
        this.statusMessage = '';
        this.disposed = false;
        this.refreshRequestId = 0;

        this.handleClick = this.handleClick.bind(this);
        this.handleInput = this.handleInput.bind(this);
        this.container.dataset.settingsOwner = SETTINGS_CAPABILITY_OWNER_ID;
        this.container.addEventListener('click', this.handleClick);
        this.container.addEventListener('input', this.handleInput);
        this.render();
    }

    async refresh() {
        if (this.disposed) {
            return;
        }

        const requestId = ++this.refreshRequestId;
        this.loading = true;
        this.errorMessage = '';
        this.statusMessage = '正在加载设置';
        this.render();

        try {
            const config = await this.adapter.loadSettings();
            if (this.disposed || requestId !== this.refreshRequestId) {
                return;
            }

            this.config = config || {};
            this.draftTextByTab.clear();
            this.dirtyTabs.clear();
            SETTINGS_TABS.forEach(tab => {
                this.draftTextByTab.set(tab.id, this.stringifyTabConfig(tab));
            });
            this.statusMessage = '设置已加载';
        } catch (error) {
            if (requestId === this.refreshRequestId) {
                this.errorMessage = error?.message || '设置加载失败';
                this.statusMessage = this.errorMessage;
            }
        } finally {
            if (requestId === this.refreshRequestId) {
                this.loading = false;
                this.render();
            }
        }
    }

    deactivate() {
        this.refreshRequestId += 1;
        this.statusMessage = '';
    }

    getActiveTab() {
        return findTab(this.activeTabId);
    }

    getTabValue(tab = this.getActiveTab()) {
        if (!this.config || typeof this.config !== 'object') {
            return {};
        }

        return this.config[tab.configKey] ?? this.config[tab.id] ?? {};
    }

    stringifyTabConfig(tab) {
        return JSON.stringify(this.getTabValue(tab), null, 2);
    }

    handleInput(event) {
        const textarea = event.target?.closest?.('[data-settings-tab-editor]');
        if (!textarea || this.disposed) {
            return;
        }

        const tabId = textarea.dataset.settingsTabEditor;
        this.draftTextByTab.set(tabId, textarea.value);
        this.dirtyTabs.add(tabId);
        this.statusMessage = '当前页有未保存修改';
        this.updateStatus();
    }

    handleClick(event) {
        const actionEl = event.target?.closest?.('[data-settings-action]');
        if (!actionEl || this.disposed) {
            return;
        }

        event.preventDefault();
        const action = actionEl.dataset.settingsAction;
        if (action === 'tab') {
            this.switchTab(actionEl.dataset.settingsTab || SETTINGS_TABS[0].id);
        } else if (action === 'save') {
            void this.saveCurrentTab();
        } else if (action === 'reload') {
            void this.refresh();
        } else if (action === 'discard') {
            this.discardCurrentTab();
        }
    }

    switchTab(tabId) {
        this.activeTabId = findTab(tabId).id;
        this.errorMessage = '';
        this.render();
    }

    discardCurrentTab() {
        const tab = this.getActiveTab();
        this.draftTextByTab.set(tab.id, this.stringifyTabConfig(tab));
        this.dirtyTabs.delete(tab.id);
        this.statusMessage = '已放弃当前页修改';
        this.render();
    }

    async saveCurrentTab() {
        if (this.loading || !this.config) {
            return false;
        }

        const tab = this.getActiveTab();
        const raw = this.draftTextByTab.get(tab.id) ?? '{}';
        let parsed;
        try {
            parsed = raw.trim() ? JSON.parse(raw) : {};
        } catch (error) {
            this.errorMessage = `当前页 JSON 无效：${error.message}`;
            this.statusMessage = '设置校验失败';
            this.render();
            return false;
        }

        const nextConfig = deepClone(this.config) || {};
        nextConfig[tab.configKey] = parsed;
        this.loading = true;
        this.errorMessage = '';
        this.statusMessage = `正在保存${tab.label}设置`;
        this.render();

        try {
            const saved = await this.adapter.saveSettings(nextConfig);
            this.config = saved || nextConfig;
            this.draftTextByTab.set(tab.id, this.stringifyTabConfig(tab));
            this.dirtyTabs.delete(tab.id);
            this.statusMessage = `${tab.label}设置已保存`;
            this.showToast(this.statusMessage, 'success');
            this.render();
            return true;
        } catch (error) {
            this.errorMessage = error?.message || '设置保存失败';
            this.statusMessage = this.errorMessage;
            this.showToast(this.errorMessage, 'error');
            this.render();
            return false;
        } finally {
            this.loading = false;
        }
    }

    render() {
        if (this.disposed || !this.container) {
            return;
        }

        const activeTab = this.getActiveTab();
        const draftText = this.draftTextByTab.get(activeTab.id) ?? this.stringifyTabConfig(activeTab);
        this.container.innerHTML = `
            <section class="settings-layout settings-capability-owner" data-owner="${SETTINGS_CAPABILITY_OWNER_ID}">
                <aside class="settings-sidebar">
                    <h2 class="settings-sidebar-title">系统配置</h2>
                    <nav class="settings-menu">
                        ${SETTINGS_TABS.map(tab => `
                            <button type="button"
                                    class="settings-menu-item ${tab.id === this.activeTabId ? 'active' : ''}"
                                    data-settings-action="tab"
                                    data-settings-tab="${tab.id}">
                                ${escapeHtml(tab.label)}
                                ${this.dirtyTabs.has(tab.id) ? '<span class="type-badge">未保存</span>' : ''}
                            </button>
                        `).join('')}
                    </nav>
                </aside>
                <main class="settings-content">
                    <header class="settings-content-header">
                        <div>
                            <h2>${escapeHtml(activeTab.label)}设置</h2>
                            <p id="settings-save-scope">仅保存当前页配置，不会提交其它设置页草稿。</p>
                        </div>
                        <div class="settings-actions">
                            <button type="button" class="btn btn-secondary" data-settings-action="reload" ${this.loading ? 'disabled' : ''}>重新加载</button>
                            <button type="button" class="btn btn-secondary" data-settings-action="discard" ${this.loading || !this.dirtyTabs.has(activeTab.id) ? 'disabled' : ''}>放弃当前页</button>
                            <button type="button" class="btn btn-primary" id="btn-save-settings" data-settings-action="save" ${this.loading ? 'disabled' : ''}>保存当前页</button>
                        </div>
                    </header>
                    ${this.errorMessage ? `<div class="settings-error" role="alert">${escapeHtml(this.errorMessage)}</div>` : ''}
                    <div class="settings-scope-notice">
                        <strong>当前页保存</strong>
                        <span>${escapeHtml(activeTab.label)}页会通过既有 settings API 写回原配置契约。</span>
                    </div>
                    <textarea class="form-input settings-json-editor"
                              data-settings-tab-editor="${activeTab.id}"
                              spellcheck="false"
                              ${this.loading || !this.config ? 'disabled' : ''}>${escapeHtml(draftText)}</textarea>
                    <div class="settings-status" data-settings-status aria-live="polite">${escapeHtml(this.statusMessage)}</div>
                </main>
            </section>
        `;
    }

    updateStatus() {
        const status = this.container?.querySelector?.('[data-settings-status]');
        if (status) {
            status.textContent = this.statusMessage || '';
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
        this.refreshRequestId += 1;
        this.container.removeEventListener('click', this.handleClick);
        this.container.removeEventListener('input', this.handleInput);
        delete this.container.dataset.settingsOwner;
        this.container.innerHTML = '';
    }
}

export default SettingsCapabilityOwner;
