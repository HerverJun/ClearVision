import { showToast, createModal, closeModal } from '../../shared/components/uiComponents.js';
import { applyFeatureToButton } from '../../shared/featureRegistry.js';
import { LifecycleRegistry } from './lifecycleRegistry.js';
import settingsApi from './settingsApi.js';
import { installSettingsNormalizers } from './settingsNormalizers.js';
import { installAiTab } from './tabs/aiTab.js';
import { installCameraTab } from './tabs/cameraTab.js';
import { installPlcTab } from './tabs/plcTab.js';
import { installRuntimePreviewPilotConsole } from './tabs/runtimePreviewPilotConsole.js';
import { installStationTab } from './tabs/stationTab.js';
import { installSystemTabs } from './tabs/systemTabs.js';
import { installTcpTab } from './tabs/tcpTab.js';

export class SettingsView {
    constructor(containerId) {
        this.containerId = containerId;
        this.container = document.getElementById(containerId);
        
        this.config = null;
        this.users = [];
        this.cameraBindings = [];
        this.selectedCameraBindingId = null;
        
        const currentUser = window.currentUser || {};
        this.currentUser = currentUser || {};
        this.isAdmin = currentUser?.role === 'Admin';
        
        this.aiModels = [];
        this.activeAiModelId = null;
        this.editingAiModelId = null;
        this._pendingFormEdits = {}; // 暂存表单中的未保存修改
        this.aiReasoningSupportPreview = null;
        this._aiReasoningSupportRequestId = 0;
        this._aiReasoningSupportDebounce = null;
        this.diskUsage = null;
        this.databaseStatus = null;
        this.plcMappings = [];
        this.plcConnectionStatus = 'unknown';
        this.plcValidationErrors = [];
        this.plcSettingsLoaded = false;
        this.tcpProfiles = [];
        this.selectedTcpProfileId = null;
        this.tcpSettingsLoaded = false;
        this.tcpDraftDirty = false;
        this.tcpStatusById = {};
        this.tcpFramesById = {};
        this.savedCommunicationConfig = null;
        this.stationCommunicationSettings = null;
        this.stationCommunicationLoaded = false;
        this.stationTokenVisible = false;
        this.stationTokenValue = '';
        this.activeTab = null;
        this.lastCameraBindingSaveError = null;
        this.serialPhotoelectricPorts = [];
        this.serialPhotoelectricPortsLoaded = false;
        this.lifecycle = new LifecycleRegistry();
        this._trackedModals = new Set();
        this._trackedTimeouts = new Set();
        this._stationTokenHideTimer = null;
        this._diskUsageRequestId = 0;
        this._refreshRequestId = 0;
    }

    createTrackedModal(options = {}) {
        const modal = createModal({
            ...options,
            onClose: () => {
                try {
                    const result = options.onClose?.();
                    if (result && typeof result.finally === 'function') {
                        result
                            .catch(error => console.warn('[SettingsView] Modal close cleanup failed:', error))
                            .finally(() => {
                                this._trackedModals.delete(modal);
                                this.lifecycle.untrackModal(modal);
                            });
                        return;
                    }
                } finally {
                    this._trackedModals.delete(modal);
                    this.lifecycle.untrackModal(modal);
                }
            },
            onDispose: () => {
                try {
                    options.onDispose?.();
                } finally {
                    this._trackedModals.delete(modal);
                    this.lifecycle.untrackModal(modal);
                }
            }
        });
        this._trackedModals.add(modal);
        this.lifecycle.trackModal(modal);
        return modal;
    }

    setTrackedTimeout(callback, delay = 0) {
        const timeoutId = this.lifecycle.setTimeout(() => {
            this._trackedTimeouts.delete(timeoutId);
            callback();
        }, delay);
        this._trackedTimeouts.add(timeoutId);
        return timeoutId;
    }

    clearTrackedTimeout(timeoutId) {
        if (!timeoutId) return;
        this.lifecycle.clearTimeout(timeoutId);
        this._trackedTimeouts.delete(timeoutId);
    }

    clearTransientResources() {
        this.clearTrackedTimeout(this._aiReasoningSupportDebounce);
        this._aiReasoningSupportDebounce = null;
        this.clearTrackedTimeout(this._stationTokenHideTimer);
        this._stationTokenHideTimer = null;

        this._trackedTimeouts.forEach(timeoutId => this.lifecycle.clearTimeout(timeoutId));
        this._trackedTimeouts.clear();
        this.lifecycle.clearTransient();
        this._aiReasoningSupportRequestId += 1;
        this._diskUsageRequestId += 1;
    }

    clearSensitiveUiState({ refreshStationPanel = false } = {}) {
        this.stationTokenVisible = false;
        this.stationTokenValue = '';
        this.clearAiSecretInputs();
        if (refreshStationPanel && this.getActiveTabName() === 'station') {
            this.refreshStationCommunicationPanel();
        }
    }

    deactivate() {
        this._refreshRequestId += 1;
        this.clearTransientResources();
        this.clearSensitiveUiState();
        [...this._trackedModals].forEach(modal => closeModal(modal));
    }

    destroy() {
        this.deactivate();
        if (this.container) {
            this.container.innerHTML = '';
        }
    }

    async refresh() {
        if (!this.container) {
            console.error('[SettingsView] Container not found:', this.containerId);
            return;
        }

        this.deactivate();
        const refreshRequestId = ++this._refreshRequestId;
        this.plcConnectionStatus = 'unknown';
        
        // 可选：添加统一骨架屏或加载提示
        this.container.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-muted);">正在加载设置...</div>';
        
        // 获取配置信息
        try {
            this.config = this.normalizeAppConfig(await settingsApi.loadSettings());
            if (refreshRequestId !== this._refreshRequestId) return;
            this.cameraBindings = this.config.cameras || [];
            this.syncActiveCameraSelection();
            this.savedCommunicationConfig = this.cloneCommunicationConfig(this.config.communication);
            this.syncPlcMappingsFromActiveProfile();
            this.plcSettingsLoaded = false;
            this.plcValidationErrors = [];
            this.tcpSettingsLoaded = false;
            this.tcpDraftDirty = false;
            this.tcpStatusById = {};
            this.tcpFramesById = {};
            this.stationCommunicationLoaded = false;
            this.stationTokenVisible = false;
            this.stationTokenValue = '';
            
            if (this.isAdmin) {
                this.users = await settingsApi.loadUsers();
                if (refreshRequestId !== this._refreshRequestId) return;
            }
        } catch (error) {
            console.error('[SettingsView] Failed to load data:', error);
            showToast('加载系统配置失败: ' + error.message, 'error');
            this.config = this.normalizeAppConfig(this.getDefaultConfig());
            this.syncActiveCameraSelection();
            this.savedCommunicationConfig = this.cloneCommunicationConfig(this.config.communication);
            this.syncPlcMappingsFromActiveProfile();
            this.plcSettingsLoaded = false;
            this.plcValidationErrors = [];
            this.tcpSettingsLoaded = false;
            this.tcpDraftDirty = false;
            this.tcpStatusById = {};
            this.tcpFramesById = {};
            this.stationCommunicationLoaded = false;
            this.stationTokenVisible = false;
            this.stationTokenValue = '';
        }
        
        await this.loadAiModels();
        if (refreshRequestId !== this._refreshRequestId) return;
        
        // 构建全屏两栏布局 DOM
        this.renderLayout();
        
        // 绑定整个容器内的事件
        this.bindEvents();

        // 加载磁盘容量真实数据
        await this.loadDiskUsage();
        
        // 默认激活第一个 Tab
        this.activateTab('general');
    }

    getSaveScopeMeta(tabName = this.getActiveTabName()) {
        const scopes = {
            general: {
                button: '保存常规设置',
                title: '保存常规设置',
                body: '保存软件标题、主题和开机启动。主题立即生效。'
            },
            communication: {
                button: '保存 PLC 设置',
                title: '保存 PLC 设置',
                body: '保存 PLC 协议连接和映射配置。'
            },
            tcp: {
                button: '保存 TCP Profile',
                title: '保存 TCP 通讯',
                body: '保存机器人/通用 TCP Client 与 Server 调试 Profile。'
            },
            station: {
                button: '保存 Station 设置',
                title: '保存 Station 设置',
                body: '保存监听模式、端口和本机 Station 同步配置。'
            },
            storage: {
                button: '保存存储设置',
                title: '保存文件与存储',
                body: '保存图像路径、清理天数和低空间阈值。'
            },
            database: {
                button: '刷新数据库状态',
                title: '刷新数据库状态',
                body: '查看 SQLite schema、迁移、备份、恢复、历史清理和健康检查状态。'
            },
            runtime: {
                button: '保存运行保护',
                title: '保存运行保护',
                body: '保存连续 NG、缺料超时和自动运行保护规则。'
            },
            cameras: {
                button: '保存当前相机',
                title: '保存相机管理',
                body: '保存当前相机绑定和参数。'
            },
            ai: {
                button: '保存 AI 模型',
                title: '保存 AI 模型',
                body: '保存当前 AI 模型配置。API Key 保存后清空输入框。'
            },
            'runtime-preview-pilot': {
                button: '刷新 Pilot Console',
                title: 'RuntimePreview Pilot Console',
                body: '查看 metadata-only session、scenario corpus、package readiness、audit/export，不保存普通设置。'
            },
            users: {
                button: '保存安全策略',
                title: '保存安全策略',
                body: '保存密码长度、会话超时和登录失败锁定策略；用户新增、删除和重置密码仍走各自按钮。'
            }
        };

        return scopes[tabName] || scopes.general;
    }

    renderScopeNotice(tabName) {
        const meta = this.getSaveScopeMeta(tabName);
        return `
            <div class="settings-scope-notice">
                <strong>${this.escapeHtml(meta.title)}</strong>
                <span>${this.escapeHtml(meta.body)}</span>
            </div>
        `;
    }

    updateSaveActionState(tabName = this.getActiveTabName()) {
        const meta = this.getSaveScopeMeta(tabName);
        const saveBtn = this.container?.querySelector('#btn-save-settings');
        const scopeText = this.container?.querySelector('#settings-save-scope');
        if (saveBtn) {
            saveBtn.textContent = meta.button;
            saveBtn.title = meta.body;
        }
        if (scopeText) {
            scopeText.textContent = meta.body;
        }
    }

    /**
     * 基于两栏结构生成主 HTML
     */
    renderLayout() {
        const userManagementTab = this.isAdmin ? `<div class="settings-menu-item" data-tab="users">
            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/></svg> 
            用户管理
        </div>` : '';
        const runtimePreviewPilotTab = this.isAdmin && this.isRuntimePreviewPilotDeveloperUiEnabled?.() ? `<div class="settings-menu-item" data-tab="runtime-preview-pilot">
            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M4 4h16v4H4V4zm0 6h7v10H4V10zm9 0h7v10h-7V10zm2 2v2h3v-2h-3zm0 4v2h3v-2h-3z"/></svg>
            RuntimePreview Pilot
        </div>` : '';
        
        this.container.innerHTML = `
            <div class="settings-layout">
                <aside class="settings-sidebar">
                    <h2 class="settings-sidebar-title">系统配置</h2>
                    <nav class="settings-menu">
                        <div class="settings-menu-item active" data-tab="general">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M19.43 12.98c.04-.32.07-.64.07-.98s-.03-.66-.07-.98l2.11-1.65c.19-.15.24-.42.12-.64l-2-3.46c-.12-.22-.39-.3-.61-.22l-2.49 1c-.52-.4-1.08-.73-1.69-.98l-.38-2.65C14.46 2.18 14.25 2 14 2h-4c-.25 0-.46.18-.49.42l-.38 2.65c-.61.25-1.17.59-1.69.98l-2.49-1c-.23-.09-.49 0-.61.22l-2 3.46c-.13.22-.07.49.12.64l2.11 1.65c-.04.32-.07.65-.07.98s.03.66.07.98l-2.11 1.65c-.19.15-.24.42-.12.64l2 3.46c.12.22.39.3.61.22l2.49-1c.52.4 1.08.73 1.69.98l.38 2.65c.03.24.24.42.49.42h4c.25 0 .46-.18.49-.42l.38-2.65c.61-.25 1.17-.59 1.69-.98l2.49 1c.23.09.49 0 .61-.22l2-3.46c.12-.22.07-.49-.12-.64l-2.11-1.65zM12 15.5c-1.93 0-3.5-1.57-3.5-3.5s1.57-3.5 3.5-3.5 3.5 1.57 3.5 3.5-1.57 3.5-3.5 3.5z"/></svg> 
                            常规设置
                        </div>
                        <div class="settings-menu-item" data-tab="communication">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z"/></svg>
                            PLC 通讯
                        </div>
                        <div class="settings-menu-item" data-tab="tcp">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M4 7h5v2H6v6h3v2H4V7zm7 3h2v4h-2v-4zm4-3h5v10h-5v-2h3V9h-3V7zM8 11h8v2H8v-2z"/></svg>
                            TCP 通讯
                        </div>
                        <div class="settings-menu-item" data-tab="station">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M4 5h16v6H4V5zm2 2v2h12V7H6zm-2 6h7v6H4v-6zm2 2v2h3v-2H6zm7-2h7v6h-7v-6zm2 2v2h3v-2h-3z"/></svg>
                            工站通讯
                        </div>
                        <div class="settings-menu-item" data-tab="storage">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M2 20h20v-4H2v4zm2-3h2v2H4v-2zM2 4v4h20V4H2zm4 3H4V5h2v2zm-4 7h20v-4H2v4zm2-3h2v2H4v-2z"/></svg>
                            文件存储
                        </div>
                        <div class="settings-menu-item" data-tab="database">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M12 3C7.58 3 4 4.34 4 6v12c0 1.66 3.58 3 8 3s8-1.34 8-3V6c0-1.66-3.58-3-8-3zm0 2c3.31 0 6 .67 6 1s-2.69 1-6 1-6-.67-6-1 2.69-1 6-1zm0 14c-3.31 0-6-.67-6-1v-2.08C7.46 16.58 9.6 17 12 17s4.54-.42 6-1.08V18c0 .33-2.69 1-6 1zm0-4c-3.31 0-6-.67-6-1v-2.08C7.46 12.58 9.6 13 12 13s4.54-.42 6-1.08V14c0 .33-2.69 1-6 1zm0-4c-3.31 0-6-.67-6-1V7.92C7.46 8.58 9.6 9 12 9s4.54-.42 6-1.08V10c0 .33-2.69 1-6 1z"/></svg>
                            数据库维护
                        </div>
                        <div class="settings-menu-item" data-tab="runtime">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 14h-2v-2h2v2zm0-4h-2V7h2v5z"/></svg>
                            生产运行保护
                        </div>
                        <div class="settings-menu-item" data-tab="cameras">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="3.2"/><path d="M9 2L7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z"/></svg> 
                            相机管理
                        </div>
                        <div class="settings-menu-item" data-tab="ai">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M21 16.5c0 .38-.21.71-.53.88l-7.9 4.44c-.16.12-.36.18-.57.18-.21 0-.41-.06-.57-.18l-7.9-4.44A.991.991 0 013 16.5v-9c0-.38.21-.71.53-.88l7.9-4.44c.16-.12.36-.18.57-.18.21 0 .41.06.57.18l7.9 4.44c.32.17.53.5.53.88v9zM12 4.15L6.04 7.5 12 10.85l5.96-3.35L12 4.15zM5 15.91l6 3.38v-6.71L5 9.21v6.7zM19 15.91v-6.7l-6 3.37v6.71l6-3.38z"/></svg> 
                            AI 大模型
                        </div>
                        ${runtimePreviewPilotTab}
                        ${userManagementTab}
                    </nav>
                </aside>
                <div class="settings-content-area">
                    <div class="settings-header-banner">
                        <h1 class="settings-main-title">生产参数</h1>
                        <div class="settings-actions">
                            <span class="settings-save-scope" id="settings-save-scope"></span>
                            <button class="cv-btn cv-btn-primary" id="btn-save-settings">保存当前页</button>
                        </div>
                    </div>
                    <div class="settings-tab-panels">
                        <div class="settings-panel active" data-section="general">${this.renderGeneralTab()}</div>
                        <div class="settings-panel" data-section="communication">${this.renderCommunicationTab()}</div>
                        <div class="settings-panel" data-section="tcp">${this.renderTcpTab()}</div>
                        <div class="settings-panel" data-section="station">${this.renderStationCommunicationTab()}</div>
                        <div class="settings-panel" data-section="storage">${this.renderStorageTab()}</div>
                        <div class="settings-panel" data-section="database">${this.renderDatabaseTab()}</div>
                        <div class="settings-panel" data-section="runtime">${this.renderRuntimeTab()}</div>
                        <div class="settings-panel" data-section="cameras">${this.renderCameraTab()}</div>
                        <div class="settings-panel" data-section="ai">${this.renderAiTab()}</div>
                        ${this.isAdmin && this.isRuntimePreviewPilotDeveloperUiEnabled?.() ? `<div class="settings-panel" data-section="runtime-preview-pilot">${this.renderRuntimePreviewPilotConsoleTab()}</div>` : ''}
                        ${this.isAdmin ? `<div class="settings-panel" data-section="users">${this.renderUserManagementTab()}</div>` : ''}
                    </div>
                </div>
            </div>
        `;
    }

    activateTab(tabName) {
        if (!this.container) return;
        const previousTab = this.activeTab;
        if (previousTab && previousTab !== tabName) {
            if (previousTab === 'cameras') {
                [...this._trackedModals].forEach(modal => closeModal(modal));
            }
            if (previousTab === 'station') {
                this.clearSensitiveUiState({ refreshStationPanel: true });
            }
            if (previousTab === 'ai') {
                this.clearAiSecretInputs();
            }
        }
        this.activeTab = tabName;
        this.updateSaveActionState(tabName);
        
        // 侧边栏高亮
        const menuItems = this.container.querySelectorAll('.settings-menu-item');
        menuItems.forEach(item => {
            if (item.dataset.tab === tabName) {
                item.classList.add('active');
                // 同步更新右侧大标题
                const headerTitle = this.container.querySelector('.settings-main-title');
                if (headerTitle) headerTitle.textContent = item.textContent.trim();
            } else {
                item.classList.remove('active');
            }
        });

        // 切换内容面板
        const panels = this.container.querySelectorAll('.settings-panel');
        panels.forEach(panel => {
            if (panel.dataset.section === tabName) {
                panel.classList.add('active');
            } else {
                panel.classList.remove('active');
            }
        });
        
        // 如果是切换到用户管理且是管理员，需要刷新表格
        if (tabName === 'users' && this.isAdmin) {
            this.refreshUserTable();
        } else if (tabName === 'cameras') {
            this.loadCameraBindings()
                .then(() => this.loadSerialPhotoelectricPorts({ silent: true, applyRecommended: true }))
                .catch(error => console.warn('[SettingsView] Camera tab load failed:', error));
        } else if (tabName === 'communication') {
            this.loadPlcSettings();
        } else if (tabName === 'tcp') {
            this.loadTcpSettings();
        } else if (tabName === 'station') {
            this.loadStationCommunicationSettings();
        } else if (tabName === 'database') {
            this.refreshDatabaseStatus();
        } else if (tabName === 'runtime-preview-pilot') {
            this.loadRuntimePreviewPilotState?.()
                .then(() => this.refreshRuntimePreviewPilotPanel?.())
                .catch(error => console.warn('[SettingsView] RuntimePreview Pilot console load failed:', error));
        }
    }

    bindEvents() {
        if (!this.container) return;

        // 左侧菜单切换
        const menu = this.container.querySelector('.settings-menu');
        if (menu) {
            menu.addEventListener('click', (e) => {
                const menuItem = e.target.closest('.settings-menu-item');
                if (menuItem) {
                    this.activateTab(menuItem.dataset.tab);
                }
            });
        }

        // 保存按钮
        const saveBtn = this.container.querySelector('#btn-save-settings');
        if (saveBtn) {
            saveBtn.addEventListener('click', () => this.save());
        }

        // 绑定相机管理相关事件
        this.bindCameraManagementEvents();

        // 绑定用户管理事件（仅管理员）
        if (this.isAdmin) {
            this.bindUserManagementEvents();
        }

        // 绑定 AI 设置事件
        this.bindAiSettingsEvents();
        this.bindRuntimePreviewPilotConsoleEvents?.();
        this.bindPlcSettingsEvents();
        this.bindTcpSettingsEvents();
        this.bindStationCommunicationEvents();

        // 存储路径变化后刷新磁盘容量卡片
        const imageSavePathInput = this.container.querySelector('#cfg-imageSavePath');
        if (imageSavePathInput) {
            const refreshDiskUsage = () => this.loadDiskUsage(imageSavePathInput.value);
            imageSavePathInput.addEventListener('change', refreshDiskUsage);
            imageSavePathInput.addEventListener('blur', refreshDiskUsage);
        }

        this.container.querySelector('#btn-change-password')?.addEventListener('click', () => this.changePassword());
        this.container.querySelector('#btn-reset-settings')?.addEventListener('click', () => this.resetSettings());
        this.container.querySelector('#btn-apply-protection-rules')?.addEventListener('click', () => this.save());
        this.container.querySelector('#btn-save-security-policy')?.addEventListener('click', () => this.save());
        this.bindDatabaseMaintenanceEvents();

        applyFeatureToButton(this.container.querySelector('#btn-change-image-save-path'), 'storage.pathPicker', { fallbackLabel: '更改目录' });
        applyFeatureToButton(this.container.querySelector('#btn-clean-expired-files'), 'storage.immediateCleanup', { fallbackLabel: '立即清理过期文件' });
        applyFeatureToButton(this.container.querySelector('#btn-reset-settings'), 'settings.reset', { fallbackLabel: '恢复默认设置' });
    }
    
    getActiveTabName() {
        if (this.activeTab) {
            return this.activeTab;
        }

        const activePanel = this.container?.querySelector('.settings-panel.active');
        return activePanel?.dataset.section || null;
    }

    async save() {
        const activeTabName = this.getActiveTabName();

        if (activeTabName === 'station') {
            await this.saveStationCommunicationSettings();
            return;
        }

        if (activeTabName === 'communication') {
            await this.savePlcSettings({ persistAllProfiles: true });
            return;
        }

        if (activeTabName === 'tcp') {
            await this.saveTcpSettings();
            return;
        }

        if (activeTabName === 'cameras') {
            await this.saveCameraSettingsFromTop();
            return;
        }

        if (activeTabName === 'ai') {
            await this.saveAiDraftFromTop();
            return;
        }

        if (activeTabName === 'database') {
            await this.refreshDatabaseStatus();
            return;
        }

        if (activeTabName === 'runtime-preview-pilot') {
            await this.loadRuntimePreviewPilotState?.();
            this.refreshRuntimePreviewPilotPanel?.();
            return;
        }

        await this.saveAppSettingsForTab(activeTabName);
    }
}

installSettingsNormalizers(SettingsView);
installPlcTab(SettingsView);
installTcpTab(SettingsView);
installStationTab(SettingsView);
installAiTab(SettingsView);
installRuntimePreviewPilotConsole(SettingsView);
installCameraTab(SettingsView);
installSystemTabs(SettingsView);

// 暴露出初始化方法给外界（比如 app.js）
window.initializeSettingsView = function() {
    window.cvSettingsView?.destroy?.();
    window.cvSettingsView = new SettingsView('settings-view');
    window.cvSettingsView.refresh();
};

export function createLegacySettingsView(containerId = 'settings-view') {
    return new SettingsView(containerId);
}
