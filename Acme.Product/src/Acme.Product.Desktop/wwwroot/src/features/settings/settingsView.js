import httpClient from '../../core/messaging/httpClient.js';
import { showToast, createModal, closeModal } from '../../shared/components/uiComponents.js';
import { applyTheme, getAppliedTheme, normalizeTheme } from '../../core/theme/theme.js';
import inspectionController from '../inspection/inspectionController.js';
import {
    applyFeatureToButton,
    getFeatureButtonLabel,
    getFeatureMeta,
    isFeatureEnabled
} from '../../shared/featureRegistry.js';

class SettingsView {
    constructor(containerId) {
        this.containerId = containerId;
        this.container = document.getElementById(containerId);
        
        this.config = null;
        this.users = [];
        this.cameraBindings = [];
        this.selectedCameraBindingId = null;
        
        // 尝试从本地存储或全局对象中获取当前用户信息
        const storedUser = localStorage.getItem('cv_current_user');
        const currentUser = window.currentUser || (storedUser ? JSON.parse(storedUser) : {});
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
        this.plcMappings = [];
        this.plcConnectionStatus = 'unknown';
        this.plcValidationErrors = [];
        this.plcSettingsLoaded = false;
        this.savedCommunicationConfig = null;
        this.stationCommunicationSettings = null;
        this.stationCommunicationLoaded = false;
        this.stationTokenVisible = false;
        this.stationTokenValue = '';
        this.activeTab = null;
        this.lastCameraBindingSaveError = null;
        this.serialPhotoelectricPorts = [];
        this.serialPhotoelectricPortsLoaded = false;

        console.log('[SettingsView] Initialized for container:', containerId, '| isAdmin:', this.isAdmin);
    }

    normalizeCameraTriggerMode(value) {
        const normalized = String(value || '').trim().toLowerCase();
        if (normalized === 'continuous') return 'Continuous';
        if (normalized === 'external' || normalized === 'hardware' || normalized === 'externalsignal') return 'External';
        return 'Software';
    }

    normalizeHardwareTriggerSource(value) {
        const normalized = String(value || '').trim();
        return normalized || 'Line0';
    }

    normalizeSoftwareTriggerSource(value) {
        const normalized = String(value || '').trim().toLowerCase();
        if (['enterphotoelectric', 'keyboardenter', 'usbenter', 'enter', 'photoelectricenter'].includes(normalized)) {
            return 'EnterPhotoelectric';
        }
        if (['serialphotoelectric', 'comphotoelectric', 'serial', 'com'].includes(normalized)) {
            return 'SerialPhotoelectric';
        }
        return 'Manual';
    }

    normalizeCameraTargetFrameRate(value) {
        const parsed = Number.parseInt(String(value ?? ''), 10);
        if (!Number.isFinite(parsed) || parsed <= 0) {
            return 10;
        }

        return Math.min(120, Math.max(1, parsed));
    }

    normalizeCameraPixelFormat(value) {
        const normalized = String(value || '')
            .trim()
            .replace(/[-_\s]/g, '')
            .toLowerCase();
        if (['rgb', 'rgb8'].includes(normalized)) return 'RGB8';
        if (['bgr', 'bgr8'].includes(normalized)) return 'BGR8';
        if (['bayerrg', 'bayerrg8'].includes(normalized)) return 'BayerRG8';
        if (['bayergb', 'bayergb8'].includes(normalized)) return 'BayerGB8';
        if (['bayergr', 'bayergr8'].includes(normalized)) return 'BayerGR8';
        if (['bayerbg', 'bayerbg8'].includes(normalized)) return 'BayerBG8';
        if (['mono', 'mono8', 'monochrome', 'gray', 'gray8', 'grey', 'grey8', 'moon'].includes(normalized)) return 'Mono8';
        return 'Mono8';
    }

    getCameraPixelFormatLabel(value) {
        const format = this.normalizeCameraPixelFormat(value);
        const labels = {
            Mono8: 'Mono8',
            RGB8: 'RGB8',
            BGR8: 'BGR8',
            BayerRG8: 'Bayer RG8',
            BayerGB8: 'Bayer GB8',
            BayerGR8: 'Bayer GR8',
            BayerBG8: 'Bayer BG8'
        };
        return labels[format] || 'Mono8';
    }

    normalizeEnterDebounceMs(value) {
        const parsed = Number.parseInt(String(value ?? ''), 10);
        if (!Number.isFinite(parsed)) {
            return 200;
        }

        return Math.min(5000, Math.max(0, parsed));
    }

    normalizeEnterTimeoutMs(value) {
        const parsed = Number.parseInt(String(value ?? ''), 10);
        if (!Number.isFinite(parsed) || parsed <= 0) {
            return 30000;
        }

        return Math.min(600000, Math.max(100, parsed));
    }

    normalizeSerialBaudRate(value) {
        const parsed = Number.parseInt(String(value ?? ''), 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : 9600;
    }

    normalizeSerialDebounceMs(value) {
        return this.normalizeEnterDebounceMs(value);
    }

    normalizeSerialTimeoutMs(value) {
        return this.normalizeEnterTimeoutMs(value);
    }

    normalizeSerialPhotoelectricPortInfo(port) {
        const portName = String(port?.portName ?? port?.PortName ?? '').trim().toUpperCase();
        if (!portName) {
            return null;
        }

        const displayName = String(port?.displayName ?? port?.DisplayName ?? portName).trim() || portName;
        const isRecommended = (port?.isRecommended ?? port?.IsRecommended) === true;
        return { portName, displayName, isRecommended };
    }

    getRecommendedSerialPhotoelectricPort() {
        return this.serialPhotoelectricPorts.find(port => port.isRecommended)
            || this.serialPhotoelectricPorts.find(port => !/蓝牙|bluetooth/i.test(port.displayName))
            || this.serialPhotoelectricPorts[0]
            || null;
    }

    updateSerialPhotoelectricPortHint() {
        const hintEl = this.container?.querySelector('#cam-param-serial-port-hint');
        if (!hintEl) {
            return;
        }

        if (!this.serialPhotoelectricPortsLoaded) {
            hintEl.textContent = '进入本页后会自动识别 USB 串口。';
            return;
        }

        if (this.serialPhotoelectricPorts.length === 0) {
            hintEl.textContent = '未发现串口设备；插入传感器后点“识别”。';
            return;
        }

        const recommended = this.getRecommendedSerialPhotoelectricPort();
        hintEl.textContent = recommended
            ? `自动识别: ${recommended.displayName}`
            : `已发现: ${this.serialPhotoelectricPorts.map(port => port.displayName).join('、')}`;
    }

    applySerialPhotoelectricPort(portName, { persistSelected = true } = {}) {
        const normalizedPortName = String(portName || '').trim().toUpperCase();
        if (!normalizedPortName) {
            return;
        }

        const input = this.container?.querySelector('#cam-param-serial-port-name');
        if (input) {
            input.value = normalizedPortName;
        }

        if (persistSelected && this.selectedCameraBindingId) {
            const binding = this.cameraBindings.find(item => item.id === this.selectedCameraBindingId);
            if (binding) {
                binding.serialPhotoelectricPortName = normalizedPortName;
            }
        }
    }

    async loadSerialPhotoelectricPorts({ silent = false, applyRecommended = false } = {}) {
        const refreshBtn = this.container?.querySelector('#btn-refresh-serial-photoelectric-port');
        const portInput = this.container?.querySelector('#cam-param-serial-port-name');
        const previousText = refreshBtn?.textContent;

        if (refreshBtn) {
            refreshBtn.disabled = true;
            refreshBtn.textContent = '识别中';
        }

        try {
            const response = await httpClient.get('/trigger-input/serial-photoelectric-ports');
            const rawPorts = Array.isArray(response)
                ? response
                : (response?.ports || response?.Ports || []);
            this.serialPhotoelectricPorts = rawPorts
                .map(port => this.normalizeSerialPhotoelectricPortInfo(port))
                .filter(Boolean);
            this.serialPhotoelectricPortsLoaded = true;

            const recommended = this.getRecommendedSerialPhotoelectricPort();
            const currentPort = String(portInput?.value || '').trim();
            const currentPortDetected = this.serialPhotoelectricPorts.some(port =>
                port.portName.toLowerCase() === currentPort.toLowerCase());
            if (recommended && (!currentPort || (applyRecommended && !currentPortDetected))) {
                this.applySerialPhotoelectricPort(recommended.portName);
            }

            this.updateSerialPhotoelectricPortHint();

            if (!silent) {
                if (recommended) {
                    showToast(`已识别串口光电: ${recommended.displayName}`, 'success');
                } else {
                    showToast('未发现可用串口设备', 'warning');
                }
            }
        } catch (error) {
            this.serialPhotoelectricPortsLoaded = true;
            this.serialPhotoelectricPorts = [];
            this.updateSerialPhotoelectricPortHint();
            if (!silent) {
                showToast('串口自动识别失败: ' + (error?.message || error), 'error');
            }
        } finally {
            if (refreshBtn) {
                refreshBtn.textContent = previousText || '识别';
                refreshBtn.disabled = false;
            }
        }
    }

    isEnterPhotoelectricBinding(binding) {
        return this.normalizeCameraTriggerMode(binding?.triggerMode) === 'Software'
            && this.normalizeSoftwareTriggerSource(binding?.softwareTriggerSource) === 'EnterPhotoelectric';
    }

    isSerialPhotoelectricBinding(binding) {
        return this.normalizeCameraTriggerMode(binding?.triggerMode) === 'Software'
            && this.normalizeSoftwareTriggerSource(binding?.softwareTriggerSource) === 'SerialPhotoelectric';
    }

    getTriggerSourceLabel(binding) {
        if (this.isEnterPhotoelectricBinding(binding)) {
            return '回车光电触发';
        }
        if (this.isSerialPhotoelectricBinding(binding)) {
            return '串口光电触发';
        }

        const mode = this.normalizeCameraTriggerMode(binding?.triggerMode);
        if (mode === 'External') return '相机 IO 外触发';
        if (mode === 'Continuous') return '连续采集';
        return '软件触发';
    }

    /**
     * 初始化或重新加载视图
     * app.js 在切换到 settings 视图时调用
     */
    async refresh() {
        console.log('[SettingsView] === refresh() START ===');
        if (!this.container) {
            console.error('[SettingsView] Container not found:', this.containerId);
            return;
        }
        this.plcConnectionStatus = 'unknown';
        
        // 可选：添加统一骨架屏或加载提示
        this.container.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-muted);">正在加载设置...</div>';
        
        // 获取配置信息
        try {
            console.log('[SettingsView] Fetching main config...');
            this.config = this.normalizeAppConfig(await httpClient.get('/settings'));
            this.cameraBindings = this.config.cameras || [];
            this.syncActiveCameraSelection();
            this.savedCommunicationConfig = this.cloneCommunicationConfig(this.config.communication);
            this.syncPlcMappingsFromActiveProfile();
            this.plcSettingsLoaded = false;
            this.plcValidationErrors = [];
            this.stationCommunicationLoaded = false;
            this.stationTokenVisible = false;
            this.stationTokenValue = '';
            
            if (this.isAdmin) {
                console.log('[SettingsView] Fetching users list...');
                this.users = await httpClient.get('/users');
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
            this.stationCommunicationLoaded = false;
            this.stationTokenVisible = false;
            this.stationTokenValue = '';
        }
        
        await this.loadAiModels();
        
        // 构建全屏两栏布局 DOM
        this.renderLayout();
        
        // 绑定整个容器内的事件
        this.bindEvents();

        // 加载磁盘容量真实数据
        await this.loadDiskUsage();
        
        // 默认激活第一个 Tab
        this.activateTab('general');
        
        console.log('[SettingsView] === refresh() END ===');
    }

    cloneCommunicationConfig(config) {
        return JSON.parse(JSON.stringify(config || this.getDefaultConfig().communication));
    }

    normalizePlcProtocol(protocol) {
        const candidate = `${protocol || ''}`.trim().toUpperCase();
        if (candidate === 'MC' || candidate === 'MITSUBISHIMC') return 'MC';
        if (candidate === 'FINS' || candidate === 'OMRONFINS') return 'FINS';
        return 'S7';
    }

    getPlcProfileKey(protocol = null) {
        const normalized = this.normalizePlcProtocol(protocol || this.config?.communication?.activeProtocol);
        if (normalized === 'MC') return 'mc';
        if (normalized === 'FINS') return 'fins';
        return 's7';
    }

    normalizePlcMappings(mappings) {
        if (!Array.isArray(mappings)) return [];
        return mappings
            .map(item => ({
                name: item?.name || '',
                address: item?.address || '',
                dataType: item?.dataType || 'Bool',
                description: item?.description || '',
                canWrite: !!item?.canWrite
            }))
            .filter(item => item.name || item.address || item.description);
    }

    normalizePlcProfile(profile, defaults, includeS7Fields = false) {
        const normalized = {
            ipAddress: `${profile?.ipAddress || ''}`.trim(),
            port: Number.isFinite(Number.parseInt(`${profile?.port ?? ''}`, 10))
                ? Number.parseInt(`${profile?.port ?? ''}`, 10)
                : defaults.port,
            mappings: this.normalizePlcMappings(profile?.mappings ?? defaults.mappings)
        };

        if (includeS7Fields) {
            normalized.cpuType = `${profile?.cpuType || defaults.cpuType || 'S7-1200'}`.trim() || 'S7-1200';
            normalized.rack = Number.isFinite(Number.parseInt(`${profile?.rack ?? ''}`, 10))
                ? Number.parseInt(`${profile?.rack ?? ''}`, 10)
                : defaults.rack;
            normalized.slot = Number.isFinite(Number.parseInt(`${profile?.slot ?? ''}`, 10))
                ? Number.parseInt(`${profile?.slot ?? ''}`, 10)
                : defaults.slot;
        }

        return normalized;
    }

    normalizeCommunicationConfig(communication) {
        const defaults = this.getDefaultConfig().communication;
        const normalized = {
            activeProtocol: this.normalizePlcProtocol(communication?.activeProtocol || communication?.protocol || defaults.activeProtocol),
            heartbeatIntervalMs: Number.isFinite(Number.parseInt(`${communication?.heartbeatIntervalMs ?? ''}`, 10))
                && Number.parseInt(`${communication?.heartbeatIntervalMs ?? ''}`, 10) > 0
                ? Number.parseInt(`${communication?.heartbeatIntervalMs ?? ''}`, 10)
                : defaults.heartbeatIntervalMs,
            s7: this.normalizePlcProfile(communication?.s7 || {}, defaults.s7, true),
            mc: this.normalizePlcProfile(communication?.mc || {}, defaults.mc),
            fins: this.normalizePlcProfile(communication?.fins || {}, defaults.fins)
        };

        const hasProtocolProfiles = !!communication?.s7 || !!communication?.mc || !!communication?.fins;
        const legacyIp = `${communication?.plcIpAddress || communication?.ipAddress || ''}`.trim();
        const legacyPort = Number.parseInt(`${communication?.plcPort ?? communication?.port ?? ''}`, 10);
        const legacyMappings = this.normalizePlcMappings(communication?.mappings);
        if (!hasProtocolProfiles && (legacyIp || Number.isFinite(legacyPort) || legacyMappings.length > 0)) {
            const profileKey = this.getPlcProfileKey(normalized.activeProtocol);
            normalized[profileKey] = {
                ...normalized[profileKey],
                ipAddress: legacyIp || normalized[profileKey].ipAddress,
                port: Number.isFinite(legacyPort) ? legacyPort : normalized[profileKey].port,
                mappings: legacyMappings.length > 0 ? legacyMappings : normalized[profileKey].mappings
            };
        }

        return normalized;
    }

    normalizeAppConfig(config) {
        const defaults = this.getDefaultConfig();
        return {
            ...defaults,
            ...config,
            general: {
                ...defaults.general,
                ...(config?.general || {}),
                theme: normalizeTheme(config?.general?.theme, defaults.general.theme)
            },
            communication: this.normalizeCommunicationConfig(config?.communication),
            storage: { ...defaults.storage, ...(config?.storage || {}) },
            runtime: { ...defaults.runtime, ...(config?.runtime || {}) },
            security: { ...defaults.security, ...(config?.security || {}) },
            cameras: (Array.isArray(config?.cameras) ? config.cameras : (defaults.cameras || [])).map(binding => ({
                ...binding,
                pixelFormat: this.normalizeCameraPixelFormat(binding?.pixelFormat ?? binding?.PixelFormat),
                triggerMode: this.normalizeCameraTriggerMode(binding?.triggerMode ?? binding?.TriggerMode),
                hardwareTriggerSource: this.normalizeHardwareTriggerSource(binding?.hardwareTriggerSource ?? binding?.HardwareTriggerSource),
                softwareTriggerSource: this.normalizeSoftwareTriggerSource(binding?.softwareTriggerSource ?? binding?.SoftwareTriggerSource),
                enterPhotoelectricDebounceMs: this.normalizeEnterDebounceMs(binding?.enterPhotoelectricDebounceMs ?? binding?.EnterPhotoelectricDebounceMs),
                enterPhotoelectricTimeoutMs: this.normalizeEnterTimeoutMs(binding?.enterPhotoelectricTimeoutMs ?? binding?.EnterPhotoelectricTimeoutMs),
                ignoreEnterTriggerWhileBusy: (binding?.ignoreEnterTriggerWhileBusy ?? binding?.IgnoreEnterTriggerWhileBusy) !== false,
                enterPhotoelectricDeviceId: String(binding?.enterPhotoelectricDeviceId ?? binding?.EnterPhotoelectricDeviceId ?? '').trim(),
                serialPhotoelectricPortName: String(binding?.serialPhotoelectricPortName ?? binding?.SerialPhotoelectricPortName ?? '').trim(),
                serialPhotoelectricBaudRate: this.normalizeSerialBaudRate(binding?.serialPhotoelectricBaudRate ?? binding?.SerialPhotoelectricBaudRate),
                serialPhotoelectricDebounceMs: this.normalizeSerialDebounceMs(binding?.serialPhotoelectricDebounceMs ?? binding?.SerialPhotoelectricDebounceMs),
                serialPhotoelectricTimeoutMs: this.normalizeSerialTimeoutMs(binding?.serialPhotoelectricTimeoutMs ?? binding?.SerialPhotoelectricTimeoutMs),
                ignoreSerialPhotoelectricTriggerWhileBusy: (binding?.ignoreSerialPhotoelectricTriggerWhileBusy ?? binding?.IgnoreSerialPhotoelectricTriggerWhileBusy) !== false,
                targetFrameRateFps: this.normalizeCameraTargetFrameRate(binding?.targetFrameRateFps ?? binding?.TargetFrameRateFps)
            })),
            activeCameraId: config?.activeCameraId || defaults.activeCameraId || ''
        };
    }

    getActivePlcProtocol() {
        return this.normalizePlcProtocol(this.config?.communication?.activeProtocol);
    }

    getActivePlcProfile() {
        const communication = this.normalizeCommunicationConfig(this.config?.communication);
        return communication[this.getPlcProfileKey(communication.activeProtocol)];
    }

    syncPlcMappingsFromActiveProfile() {
        const profile = this.getActivePlcProfile();
        this.plcMappings = this.normalizePlcMappings(profile?.mappings);
    }
    
    async loadAiModels({ preserveEditingId = false } = {}) {
        const previousEditingId = this.editingAiModelId;
        try {
            const models = await httpClient.get('/ai/models');
            this.aiModels = models || [];
        } catch (e) {
            console.warn('[SettingsView] Failed to load AI models from backend:', e);
            this.aiModels = [];
        }
        
        if (this.aiModels.length > 0) {
            const active = this.aiModels.find(m => m.isActive);
            this.activeAiModelId = active ? active.id : this.aiModels[0].id;
        } else {
            this.activeAiModelId = null;
        }
        if (preserveEditingId && previousEditingId && this.aiModels.some(m => m.id === previousEditingId)) {
            this.editingAiModelId = previousEditingId;
        } else {
            this.editingAiModelId = this.activeAiModelId;
        }
        this._pendingFormEdits = {};
        this.aiReasoningSupportPreview = null;
        this._aiReasoningSupportRequestId += 1;
    }

    /**
     * 基于两栏结构生成主 HTML
     */
    renderLayout() {
        const userManagementTab = this.isAdmin ? `<div class="settings-menu-item" data-tab="users">
            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/></svg> 
            用户管理
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
                        <div class="settings-menu-item" data-tab="station">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M4 5h16v6H4V5zm2 2v2h12V7H6zm-2 6h7v6H4v-6zm2 2v2h3v-2H6zm7-2h7v6h-7v-6zm2 2v2h3v-2h-3z"/></svg>
                            Station 设置
                        </div>
                        <div class="settings-menu-item" data-tab="storage">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M2 20h20v-4H2v4zm2-3h2v2H4v-2zM2 4v4h20V4H2zm4 3H4V5h2v2zm-4 7h20v-4H2v4zm2-3h2v2H4v-2z"/></svg>
                            文件存储
                        </div>
                        <div class="settings-menu-item" data-tab="runtime">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 14h-2v-2h2v2zm0-4h-2V7h2v5z"/></svg>
                            执行与运行
                        </div>
                        <div class="settings-menu-item" data-tab="cameras">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="3.2"/><path d="M9 2L7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z"/></svg> 
                            相机管理
                        </div>
                        <div class="settings-menu-item" data-tab="ai">
                            <svg class="settings-menu-icon" viewBox="0 0 24 24"><path d="M21 16.5c0 .38-.21.71-.53.88l-7.9 4.44c-.16.12-.36.18-.57.18-.21 0-.41-.06-.57-.18l-7.9-4.44A.991.991 0 013 16.5v-9c0-.38.21-.71.53-.88l7.9-4.44c.16-.12.36-.18.57-.18.21 0 .41.06.57.18l7.9 4.44c.32.17.53.5.53.88v9zM12 4.15L6.04 7.5 12 10.85l5.96-3.35L12 4.15zM5 15.91l6 3.38v-6.71L5 9.21v6.7zM19 15.91v-6.7l-6 3.37v6.71l6-3.38z"/></svg> 
                            AI 大模型
                        </div>
                        ${userManagementTab}
                    </nav>
                </aside>
                <div class="settings-content-area">
                    <div class="settings-header-banner">
                        <h1 class="settings-main-title">常量预设</h1>
                        <div class="settings-actions">
                            <button class="cv-btn cv-btn-primary" id="btn-save-settings">保存所有更改</button>
                        </div>
                    </div>
                    <div class="settings-tab-panels">
                        <div class="settings-panel active" data-section="general">${this.renderGeneralTab()}</div>
                        <div class="settings-panel" data-section="communication">${this.renderCommunicationTab()}</div>
                        <div class="settings-panel" data-section="station">${this.renderStationCommunicationTab()}</div>
                        <div class="settings-panel" data-section="storage">${this.renderStorageTab()}</div>
                        <div class="settings-panel" data-section="runtime">${this.renderRuntimeTab()}</div>
                        <div class="settings-panel" data-section="cameras">${this.renderCameraTab()}</div>
                        <div class="settings-panel" data-section="ai">${this.renderAiTab()}</div>
                        ${this.isAdmin ? `<div class="settings-panel" data-section="users">${this.renderUserManagementTab()}</div>` : ''}
                    </div>
                </div>
            </div>
        `;
    }

    activateTab(tabName) {
        if (!this.container) return;
        this.activeTab = tabName;
        
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
        } else if (tabName === 'station') {
            this.loadStationCommunicationSettings();
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
        this.bindPlcSettingsEvents();
        this.bindStationCommunicationEvents();

        // 存储路径变化后刷新磁盘容量卡片
        const imageSavePathInput = this.container.querySelector('#cfg-imageSavePath');
        if (imageSavePathInput) {
            const refreshDiskUsage = () => this.loadDiskUsage();
            imageSavePathInput.addEventListener('change', refreshDiskUsage);
            imageSavePathInput.addEventListener('blur', refreshDiskUsage);
        }

        this.container.querySelector('#btn-change-password')?.addEventListener('click', () => this.changePassword());
        this.container.querySelector('#btn-reset-settings')?.addEventListener('click', () => this.resetSettings());
        this.container.querySelector('#btn-apply-protection-rules')?.addEventListener('click', () => this.save());
        this.container.querySelector('#btn-save-security-policy')?.addEventListener('click', () => this.save());

        applyFeatureToButton(this.container.querySelector('#btn-change-image-save-path'), 'storage.pathPicker', { fallbackLabel: '更改目录' });
        applyFeatureToButton(this.container.querySelector('#btn-clean-expired-files'), 'storage.immediateCleanup', { fallbackLabel: '立即清理过期文件' });
        applyFeatureToButton(this.container.querySelector('#btn-reset-settings'), 'settings.reset', { fallbackLabel: '恢复默认设置' });
    }
    
    bindPlcSettingsEvents() {
        const communicationTab = this.container?.querySelector('[data-section="communication"]');
        if (!communicationTab || communicationTab.dataset.boundPlcEvents === 'true') return;

        communicationTab.dataset.boundPlcEvents = 'true';

        communicationTab.addEventListener('click', async (e) => {
            const button = e.target.closest('button');
            if (!button) return;

            if (button.id === 'btn-plc-test') {
                await this.testPlcConnection();
                return;
            }

            if (button.id === 'btn-add-plc-mapping') {
                this.addPlcMapping();
                return;
            }

            if (button.id === 'btn-save-plc') {
                await this.savePlcSettings();
                return;
            }

            if (button.id === 'btn-reset-plc') {
                await this.loadPlcSettings({ force: true });
                return;
            }

            if (button.dataset.action === 'delete-mapping') {
                const index = Number.parseInt(button.dataset.index || '-1', 10);
                if (index >= 0) {
                    this.deletePlcMapping(index);
                }
            }
        });

        const updateField = (target) => {
            const row = target.closest('tr.plc-mapping-row');
            if (!row) return;
            const field = target.dataset.field;
            if (!field) return;
            const index = Number.parseInt(row.dataset.index || '-1', 10);
            if (index < 0) return;
            this.updatePlcMappingField(index, field, target);
        };

        communicationTab.addEventListener('input', (e) => {
            const target = e.target;
            if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement)) return;
            updateField(target);
        });

        communicationTab.addEventListener('change', (e) => {
            const target = e.target;
            if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement)) return;
            if (target.id === 'cfg-protocol') {
                const previousProtocol = this.getActivePlcProtocol();
                this.syncActivePlcProfileDraft(previousProtocol);
                this.config.communication.activeProtocol = this.normalizePlcProtocol(target.value);
                this.syncPlcMappingsFromActiveProfile();
                this.plcValidationErrors = [];
                this.plcConnectionStatus = 'unknown';
                this.refreshCommunicationPanel();
                return;
            }
            updateField(target);
        });
    }

    refreshCommunicationPanel() {
        const communicationPanel = this.container?.querySelector('[data-section="communication"]');
        if (!communicationPanel) return;

        this.syncPlcMappingsFromActiveProfile();
        communicationPanel.innerHTML = this.renderCommunicationTab();
        this.renderPlcMappingsTable();
        this.updatePlcConnectionBadge(this.plcConnectionStatus);
    }

    syncActivePlcProfileDraft(protocol = this.getActivePlcProtocol()) {
        if (!this.config?.communication) {
            this.config = this.normalizeAppConfig(this.getDefaultConfig());
        }

        const communication = this.normalizeCommunicationConfig(this.config.communication);
        const profileKey = this.getPlcProfileKey(protocol);
        const defaults = this.getDefaultConfig().communication[profileKey];
        const currentProfile = communication[profileKey] || defaults;
        const nextProfile = {
            ...currentProfile,
            ipAddress: this.container?.querySelector('#cfg-plcIpAddress')?.value?.trim() ?? currentProfile.ipAddress ?? '',
            port: Number.parseInt(this.container?.querySelector('#cfg-plcPort')?.value || `${currentProfile.port || defaults.port}`, 10),
            mappings: this.collectPlcMappingsFromTable()
        };

        if (protocol === 'S7') {
            nextProfile.cpuType = this.container?.querySelector('#cfg-s7-cpuType')?.value || currentProfile.cpuType || defaults.cpuType;
            nextProfile.rack = Number.parseInt(this.container?.querySelector('#cfg-s7-rack')?.value || `${currentProfile.rack ?? defaults.rack}`, 10);
            nextProfile.slot = Number.parseInt(this.container?.querySelector('#cfg-s7-slot')?.value || `${currentProfile.slot ?? defaults.slot}`, 10);
        }

        communication.activeProtocol = this.normalizePlcProtocol(protocol);
        communication[profileKey] = this.normalizePlcProfile(nextProfile, defaults, protocol === 'S7');
        this.config.communication = communication;
        this.syncPlcMappingsFromActiveProfile();
    }

    buildPlcSettingsPayload({ persistAllProfiles = false } = {}) {
        this.syncActivePlcProfileDraft(this.getActivePlcProtocol());
        const workingCommunication = this.cloneCommunicationConfig(this.config.communication);
        if (persistAllProfiles) {
            return workingCommunication;
        }

        const savedCommunication = this.cloneCommunicationConfig(this.savedCommunicationConfig || this.getDefaultConfig().communication);
        const activeProtocol = this.getActivePlcProtocol();
        const profileKey = this.getPlcProfileKey(activeProtocol);
        savedCommunication.activeProtocol = activeProtocol;
        savedCommunication.heartbeatIntervalMs = workingCommunication.heartbeatIntervalMs;
        savedCommunication[profileKey] = this.cloneCommunicationConfig(workingCommunication[profileKey]);
        return savedCommunication;
    }

    areCommunicationConfigsEqual(left, right) {
        const normalizedLeft = this.normalizeCommunicationConfig(left);
        const normalizedRight = this.normalizeCommunicationConfig(right);
        return JSON.stringify(normalizedLeft) === JSON.stringify(normalizedRight);
    }

    async loadPlcSettings({ force = false } = {}) {
        if (!force && this.plcSettingsLoaded) {
            this.refreshCommunicationPanel();
            return;
        }

        try {
            const result = await httpClient.get('/plc/settings');
            const settings = this.normalizeCommunicationConfig(result?.settings || result);
            this.savedCommunicationConfig = this.cloneCommunicationConfig(settings);
            this.config.communication = this.cloneCommunicationConfig(settings);
            this.plcValidationErrors = [];
            this.plcSettingsLoaded = true;
            this.syncPlcMappingsFromActiveProfile();
            this.refreshCommunicationPanel();
        } catch (error) {
            console.error('[SettingsView] Failed to load PLC settings:', error);
            showToast('加载PLC配置失败: ' + error.message, 'error');
        }
    }

    getCurrentProtocolValidationErrors() {
        const protocol = this.getActivePlcProtocol();
        return (this.plcValidationErrors || []).filter(error => this.normalizePlcProtocol(error?.protocol) === protocol);
    }

    getPlcFieldErrors(section, field, index = null) {
        return this.getCurrentProtocolValidationErrors().filter(error => {
            if (`${error?.section || ''}` !== section) return false;
            if (`${error?.field || ''}` !== field) return false;
            if (index === null) return error?.index === undefined || error?.index === null;
            return Number.parseInt(`${error?.index ?? ''}`, 10) === index;
        });
    }

    renderPlcErrorText(errors) {
        if (!Array.isArray(errors) || errors.length === 0) return '';
        return `<div class="plc-field-error">${errors.map(error => this.escapeHtml(error?.message || '')).join('<br>')}</div>`;
    }

    renderPlcMappingsTable() {
        const tbody = this.container?.querySelector('#plc-mapping-tbody');
        if (!tbody) return;

        if (!Array.isArray(this.plcMappings) || this.plcMappings.length === 0) {
            tbody.innerHTML = `
                <tr>
                    <td colspan="6" style="text-align:center; padding: 24px; color: #94a3b8;">
                        暂无映射，点击“添加变量”创建首个 PLC 地址映射。
                    </td>
                </tr>
            `;
            return;
        }

        const dataTypeOptions = ['Bool', 'Byte', 'Int16', 'Int32', 'Float', 'Double', 'String', 'Word', 'DWord'];
        const rowsHtml = this.plcMappings.map((mapping, index) => {
            const name = this.escapeHtml(mapping?.name || '');
            const address = this.escapeHtml(mapping?.address || '');
            const description = this.escapeHtml(mapping?.description || '');
            const dataType = (mapping?.dataType || 'Bool').trim();
            const canWrite = !!mapping?.canWrite;
            const nameErrors = this.getPlcFieldErrors('mapping', 'name', index);
            const addressErrors = this.getPlcFieldErrors('mapping', 'address', index);
            const dataTypeErrors = this.getPlcFieldErrors('mapping', 'dataType', index);
            const optionsHtml = dataTypeOptions.map(type =>
                `<option value="${type}" ${dataType === type ? 'selected' : ''}>${type}</option>`
            ).join('');

            return `
                <tr class="plc-mapping-row" data-index="${index}">
                    <td>
                        <input type="text" class="cv-input ${nameErrors.length ? 'plc-invalid-input' : ''}" data-field="name" value="${name}" placeholder="变量名">
                        ${this.renderPlcErrorText(nameErrors)}
                    </td>
                    <td>
                        <input type="text" class="cv-input ${addressErrors.length ? 'plc-invalid-input' : ''}" data-field="address" value="${address}" placeholder="${this.getActivePlcProtocol() === 'S7' ? '如 DB1.DBX0.0' : this.getActivePlcProtocol() === 'MC' ? '如 D100' : '如 DM100'}">
                        ${this.renderPlcErrorText(addressErrors)}
                    </td>
                    <td>
                        <select class="cv-input ${dataTypeErrors.length ? 'plc-invalid-input' : ''}" data-field="dataType">
                            ${optionsHtml}
                        </select>
                        ${this.renderPlcErrorText(dataTypeErrors)}
                    </td>
                    <td>
                        <select class="cv-input" data-field="canWrite">
                            <option value="false" ${canWrite ? '' : 'selected'}>R</option>
                            <option value="true" ${canWrite ? 'selected' : ''}>W</option>
                        </select>
                    </td>
                    <td><input type="text" class="cv-input" data-field="description" value="${description}" placeholder="说明"></td>
                    <td>
                        <button class="action-icon-btn" data-action="delete-mapping" data-index="${index}" title="删除">
                            <svg viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg>
                        </button>
                    </td>
                </tr>
            `;
        }).join('');

        tbody.innerHTML = rowsHtml;
    }

    addPlcMapping() {
        if (!Array.isArray(this.plcMappings)) {
            this.plcMappings = [];
        }

        this.plcMappings.push({
            name: '',
            address: '',
            dataType: 'Bool',
            description: '',
            canWrite: false
        });

        this.renderPlcMappingsTable();
    }

    deletePlcMapping(index) {
        if (!Array.isArray(this.plcMappings)) return;
        if (index < 0 || index >= this.plcMappings.length) return;
        this.plcMappings.splice(index, 1);
        this.renderPlcMappingsTable();
    }

    updatePlcMappingField(index, field, element) {
        if (!Array.isArray(this.plcMappings)) return;
        if (!this.plcMappings[index]) return;

        if (field === 'canWrite') {
            this.plcMappings[index].canWrite = `${element.value}` === 'true';
            return;
        }

        this.plcMappings[index][field] = element.value || '';
    }

    collectPlcMappingsFromTable() {
        const rows = this.container?.querySelectorAll('#plc-mapping-tbody tr.plc-mapping-row') || [];
        if (rows.length === 0) {
            return this.normalizePlcMappings(this.plcMappings);
        }

        return Array.from(rows).map(row => {
            const name = row.querySelector('[data-field="name"]')?.value?.trim() || '';
            const address = row.querySelector('[data-field="address"]')?.value?.trim() || '';
            const dataType = row.querySelector('[data-field="dataType"]')?.value || 'Bool';
            const description = row.querySelector('[data-field="description"]')?.value?.trim() || '';
            const canWrite = row.querySelector('[data-field="canWrite"]')?.value === 'true';
            return { name, address, dataType, description, canWrite };
        }).filter(item => item.name || item.address || item.description);
    }

    async savePlcSettings({ silent = false, persistAllProfiles = false } = {}) {
        const payload = this.buildPlcSettingsPayload({ persistAllProfiles });
        try {
            const result = await httpClient.put('/plc/settings', payload);
            const success = !!result?.success;
            const normalizedSettings = this.normalizeCommunicationConfig(result?.settings || payload);

            this.plcValidationErrors = Array.isArray(result?.errors) ? result.errors : [];
            this.config.communication = this.cloneCommunicationConfig(normalizedSettings);
            this.syncPlcMappingsFromActiveProfile();
            this.refreshCommunicationPanel();

            if (!success) {
                if (!silent) {
                    showToast(result?.message || 'PLC 配置校验失败', 'error');
                }
                return { success: false, settings: normalizedSettings };
            }

            this.savedCommunicationConfig = this.cloneCommunicationConfig(normalizedSettings);
            this.config.communication = this.cloneCommunicationConfig(normalizedSettings);
            this.plcValidationErrors = [];
            this.plcSettingsLoaded = true;
            this.syncPlcMappingsFromActiveProfile();
            this.refreshCommunicationPanel();

            if (!silent) {
                showToast(result?.message || 'PLC 配置已保存', 'success');
            }

            return { success: true, settings: normalizedSettings };
        } catch (error) {
            console.error('[SettingsView] Failed to save PLC settings:', error);
            if (!silent) {
                showToast('保存PLC配置失败: ' + error.message, 'error');
            }
            return { success: false, settings: null };
        }
    }

    async testPlcConnection() {
        this.syncActivePlcProfileDraft(this.getActivePlcProtocol());

        const protocol = this.getActivePlcProtocol();
        const profile = this.getActivePlcProfile();
        const testButton = this.container?.querySelector('#btn-plc-test');
        const payload = {
            protocol,
            ipAddress: profile?.ipAddress || '',
            port: Number.parseInt(`${profile?.port ?? 0}`, 10) || 0,
            cpuType: protocol === 'S7' ? (profile?.cpuType || 'S7-1200') : null,
            rack: protocol === 'S7' ? Number.parseInt(`${profile?.rack ?? 0}`, 10) : null,
            slot: protocol === 'S7' ? Number.parseInt(`${profile?.slot ?? 1}`, 10) : null
        };

        if (!payload.ipAddress) {
            showToast('请先填写 PLC IP 地址', 'warning');
            return;
        }

        if (!Number.isFinite(payload.port) || payload.port <= 0 || payload.port > 65535) {
            showToast('端口必须是 1-65535 之间的整数', 'warning');
            return;
        }

        if (testButton) {
            testButton.disabled = true;
        }

        try {
            const result = await httpClient.post('/plc/test-connection', payload);
            const isSuccess = !!result?.success;
            const message = result?.message || (isSuccess ? '连接成功' : '连接失败');
            this.updatePlcConnectionBadge(isSuccess ? 'connected' : 'failed', message);
            showToast(message, isSuccess ? 'success' : 'error');
        } catch (error) {
            this.updatePlcConnectionBadge('failed', error.message);
            showToast('连接测试失败: ' + error.message, 'error');
        } finally {
            if (testButton) {
                testButton.disabled = false;
            }
        }
    }

    getPlcConnectionBadgeMeta(status) {
        if (status === 'connected') {
            return { className: 'status-connected', text: '连接正常' };
        }

        if (status === 'failed') {
            return { className: 'status-disconnected', text: '连接失败' };
        }

        return { className: 'status-disconnected', text: '未测试' };
    }

    updatePlcConnectionBadge(status, message = '') {
        this.plcConnectionStatus = status;
        const badge = this.container?.querySelector('#plc-connection-badge');
        if (!badge) return;

        const meta = this.getPlcConnectionBadgeMeta(status);
        badge.classList.remove('status-connected', 'status-disconnected', 'status-error');
        badge.classList.add(meta.className);
        badge.innerHTML = `<span class="status-dot"></span> ${meta.text}`;
        badge.title = message || '';
    }

    getDefaultStationCommunicationSettings() {
        return {
            success: true,
            message: '',
            mode: 'Disabled',
            port: 5000,
            lanHost: '',
            lanAddresses: [],
            localStationSyncEnabled: false,
            token: { hasToken: false, mask: '', last4: '' },
            paths: { studio: '', localStation: '' },
            currentRunning: {
                studioEnabled: false,
                studioListenMode: 'Loopback',
                studioPort: 5000,
                studioToken: { hasToken: false, mask: '', last4: '' }
            },
            requiresRestart: { studio: false, localStation: false },
            localStationBaseUrl: '',
            remoteStationBaseUrl: '',
            localStationHubUrl: '',
            remoteStationHubUrl: '',
            diagnostics: []
        };
    }

    normalizeStationMode(value) {
        const mode = String(value || '').trim();
        if (/^lancontroller$/i.test(mode)) return 'LanController';
        if (/^localloopback$/i.test(mode)) return 'LocalLoopback';
        return 'Disabled';
    }

    normalizeStationCommunicationSettings(settings) {
        const defaults = this.getDefaultStationCommunicationSettings();
        const source = settings?.settings || settings || {};
        const token = source.token || source.tokenInfo || defaults.token;
        const running = source.currentRunning || defaults.currentRunning;
        return {
            ...defaults,
            ...source,
            mode: this.normalizeStationMode(source.mode),
            port: Number.isFinite(Number.parseInt(`${source.port ?? ''}`, 10))
                ? Number.parseInt(`${source.port ?? ''}`, 10)
                : defaults.port,
            lanHost: String(source.lanHost || '').trim(),
            lanAddresses: Array.isArray(source.lanAddresses) ? source.lanAddresses : [],
            localStationSyncEnabled: !!source.localStationSyncEnabled,
            token: {
                hasToken: !!token?.hasToken,
                mask: String(token?.mask || ''),
                last4: String(token?.last4 || '')
            },
            paths: {
                studio: String(source.paths?.studio || ''),
                localStation: String(source.paths?.localStation || '')
            },
            currentRunning: {
                ...defaults.currentRunning,
                ...running,
                studioToken: running?.studioToken || defaults.currentRunning.studioToken
            },
            requiresRestart: {
                studio: !!source.requiresRestart?.studio,
                localStation: !!source.requiresRestart?.localStation
            },
            diagnostics: Array.isArray(source.diagnostics) ? source.diagnostics : []
        };
    }

    getStationModeMeta(mode) {
        const normalized = this.normalizeStationMode(mode);
        const meta = {
            Disabled: {
                label: '关闭',
                title: '不开放 Studio Station 入口，本机 Station 也不主动同步。',
                badge: '已关闭'
            },
            LocalLoopback: {
                label: '本机通讯',
                title: 'Studio 只监听 127.0.0.1，本机 Station 连接本机 Studio。',
                badge: '本机'
            },
            LanController: {
                label: '局域网总控',
                title: 'Studio 监听局域网地址，远端 Station 可使用页面给出的地址与 token。',
                badge: 'LAN'
            }
        };
        return meta[normalized] || meta.Disabled;
    }

    async loadStationCommunicationSettings({ force = false } = {}) {
        if (!force && this.stationCommunicationLoaded) {
            this.refreshStationCommunicationPanel();
            return;
        }

        try {
            const result = await httpClient.get('/station-communication/settings');
            this.stationCommunicationSettings = this.normalizeStationCommunicationSettings(result);
            this.stationCommunicationLoaded = true;
            this.refreshStationCommunicationPanel();
        } catch (error) {
            console.error('[SettingsView] Failed to load Station communication settings:', error);
            this.stationCommunicationSettings = this.normalizeStationCommunicationSettings(this.stationCommunicationSettings);
            showToast('加载 Station 设置失败: ' + error.message, 'error');
            this.refreshStationCommunicationPanel();
        }
    }

    refreshStationCommunicationPanel() {
        const stationPanel = this.container?.querySelector('[data-section="station"]');
        if (!stationPanel) return;

        stationPanel.innerHTML = this.renderStationCommunicationTab();
    }

    bindStationCommunicationEvents() {
        const stationPanel = this.container?.querySelector('[data-section="station"]');
        if (!stationPanel || stationPanel.dataset.boundStationEvents === 'true') return;

        stationPanel.dataset.boundStationEvents = 'true';
        stationPanel.addEventListener('click', async (e) => {
            const button = e.target.closest('button');
            if (!button) return;

            if (button.dataset.stationMode) {
                this.applyStationMode(button.dataset.stationMode);
                return;
            }

            if (button.id === 'btn-save-station-communication') {
                await this.saveStationCommunicationSettings();
                return;
            }

            if (button.id === 'btn-reset-station-communication') {
                await this.loadStationCommunicationSettings({ force: true });
                return;
            }

            if (button.id === 'btn-reveal-station-token') {
                await this.revealStationToken();
                return;
            }

            if (button.id === 'btn-copy-station-token') {
                await this.copyStationToken();
                return;
            }

            if (button.id === 'btn-regenerate-station-token') {
                await this.regenerateStationToken();
                return;
            }

            if (button.id === 'btn-open-station-monitor') {
                this.openStationMonitor();
            }
        });

        stationPanel.addEventListener('input', (e) => {
            const target = e.target;
            if (!(target instanceof HTMLInputElement)) return;
            if (target.id === 'cfg-station-port' || target.id === 'cfg-station-lan-host') {
                this.syncStationCommunicationDraftFromForm();
            }
        });

        stationPanel.addEventListener('change', (e) => {
            const target = e.target;
            if (!(target instanceof HTMLInputElement)) return;
            if (target.id === 'cfg-station-local-sync') {
                this.syncStationCommunicationDraftFromForm();
            }
        });
    }

    syncStationCommunicationDraftFromForm() {
        const current = this.normalizeStationCommunicationSettings(this.stationCommunicationSettings);
        const portValue = this.container?.querySelector('#cfg-station-port')?.value;
        const parsedPort = Number.parseInt(`${portValue ?? ''}`, 10);
        const lanHost = this.container?.querySelector('#cfg-station-lan-host')?.value?.trim();
        const localSync = this.container?.querySelector('#cfg-station-local-sync')?.checked;

        this.stationCommunicationSettings = {
            ...current,
            port: Number.isFinite(parsedPort) ? parsedPort : current.port,
            lanHost: lanHost ?? current.lanHost,
            localStationSyncEnabled: localSync ?? current.localStationSyncEnabled
        };
    }

    applyStationMode(mode) {
        this.syncStationCommunicationDraftFromForm();
        const normalizedMode = this.normalizeStationMode(mode);
        const current = this.normalizeStationCommunicationSettings(this.stationCommunicationSettings);
        this.stationCommunicationSettings = {
            ...current,
            mode: normalizedMode,
            localStationSyncEnabled: normalizedMode === 'Disabled'
                ? false
                : current.localStationSyncEnabled !== false,
            port: Number.isFinite(Number.parseInt(`${current.port}`, 10)) ? current.port : 5000
        };
        this.refreshStationCommunicationPanel();
    }

    buildStationCommunicationPayload() {
        this.syncStationCommunicationDraftFromForm();
        const settings = this.normalizeStationCommunicationSettings(this.stationCommunicationSettings);
        const portInput = this.container?.querySelector('#cfg-station-port')?.value;
        const parsedPort = Number.parseInt(`${portInput ?? settings.port}`, 10);
        if (!Number.isFinite(parsedPort) || parsedPort < 1 || parsedPort > 65535) {
            throw new Error('端口必须是 1-65535 之间的整数');
        }

        return {
            mode: settings.mode,
            port: parsedPort,
            lanHost: this.container?.querySelector('#cfg-station-lan-host')?.value?.trim() || settings.lanHost,
            localStationSyncEnabled: settings.mode !== 'Disabled'
                && (this.container?.querySelector('#cfg-station-local-sync')?.checked ?? settings.localStationSyncEnabled)
        };
    }

    async saveStationCommunicationSettings({ silent = false } = {}) {
        if (!this.isAdmin) {
            showToast('只有管理员可以保存 Station 设置', 'warning');
            return { success: false };
        }

        let payload;
        try {
            payload = this.buildStationCommunicationPayload();
        } catch (error) {
            showToast(error.message, 'warning');
            return { success: false };
        }

        const saveButton = this.container?.querySelector('#btn-save-station-communication');
        if (saveButton) {
            saveButton.disabled = true;
        }

        try {
            const result = await httpClient.put('/station-communication/settings', payload);
            this.stationCommunicationSettings = this.normalizeStationCommunicationSettings(result);
            this.stationCommunicationLoaded = true;
            this.stationTokenVisible = false;
            this.stationTokenValue = '';
            this.refreshStationCommunicationPanel();
            if (!silent) {
                showToast('Station 设置已保存，按提示重启后生效。', 'success');
            }
            return { success: true, settings: this.stationCommunicationSettings };
        } catch (error) {
            console.error('[SettingsView] Failed to save Station communication settings:', error);
            if (!silent) {
                showToast('保存 Station 设置失败: ' + error.message, 'error');
            }
            return { success: false };
        } finally {
            if (saveButton) {
                saveButton.disabled = false;
            }
        }
    }

    async revealStationToken() {
        if (!this.isAdmin) {
            showToast('只有管理员可以显示 Station token', 'warning');
            return '';
        }

        try {
            const result = await httpClient.post('/station-communication/token', { operation: 'reveal' });
            this.stationTokenValue = String(result?.token || '');
            this.stationTokenVisible = !!this.stationTokenValue;
            if (result?.settings) {
                this.stationCommunicationSettings = this.normalizeStationCommunicationSettings(result.settings);
                this.stationCommunicationLoaded = true;
            }
            this.refreshStationCommunicationPanel();
            return this.stationTokenValue;
        } catch (error) {
            showToast('显示 Station token 失败: ' + error.message, 'error');
            return '';
        }
    }

    async copyStationToken() {
        let token = this.stationTokenVisible ? this.stationTokenValue : '';
        if (!token) {
            token = await this.revealStationToken();
        }

        if (!token) {
            showToast('当前没有可复制的 Station token', 'warning');
            return;
        }

        await this.copyTextToClipboard(token);
        showToast('Station token 已复制', 'success');
    }

    async regenerateStationToken() {
        if (!this.isAdmin) {
            showToast('只有管理员可以重新生成 Station token', 'warning');
            return;
        }

        if (!window.confirm('重新生成 token 后，已配置旧 token 的远端 Station 需要同步更新。继续吗？')) {
            return;
        }

        try {
            const result = await httpClient.post('/station-communication/token', { operation: 'regenerate' });
            this.stationTokenValue = String(result?.token || '');
            this.stationTokenVisible = !!this.stationTokenValue;
            if (result?.settings) {
                this.stationCommunicationSettings = this.normalizeStationCommunicationSettings(result.settings);
                this.stationCommunicationLoaded = true;
            }
            this.refreshStationCommunicationPanel();
            showToast('Station token 已重新生成，重启后生效。', 'success');
        } catch (error) {
            showToast('重新生成 Station token 失败: ' + error.message, 'error');
        }
    }

    async copyTextToClipboard(text) {
        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(text);
            return;
        }

        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.setAttribute('readonly', 'readonly');
        textArea.style.position = 'fixed';
        textArea.style.left = '-9999px';
        document.body.appendChild(textArea);
        textArea.select();
        document.execCommand('copy');
        document.body.removeChild(textArea);
    }

    openStationMonitor() {
        const stationButton = document.querySelector('.nav-btn[data-view="stations"]');
        if (stationButton) {
            stationButton.click();
            return;
        }

        showToast('未找到 Station 监控入口', 'warning');
    }

    escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/\"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    normalizeAiReasoning(reasoning) {
        const mode = `${reasoning?.mode || 'auto'}`.toLowerCase();
        const effort = `${reasoning?.effort || 'medium'}`.toLowerCase();
        return {
            mode: ['auto', 'off', 'on'].includes(mode) ? mode : 'auto',
            effort: ['low', 'medium', 'high'].includes(effort) ? effort : 'medium'
        };
    }

    getDefaultAiReasoningSupport() {
        return {
            familyId: 'unknown',
            familyName: 'Unknown',
            allowedModes: ['auto'],
            allowedEfforts: ['medium'],
            supportsExplicitMode: false,
            supportsEffort: false,
            isModelLockedOn: false,
            helpText: '当前模型族未识别，建议保持 Auto，以免覆盖厂商默认行为。'
        };
    }

    normalizeAiReasoningSupport(support) {
        const fallback = this.getDefaultAiReasoningSupport();
        const allowedModes = Array.isArray(support?.allowedModes) ? support.allowedModes : fallback.allowedModes;
        const allowedEfforts = Array.isArray(support?.allowedEfforts) ? support.allowedEfforts : fallback.allowedEfforts;
        const normalizedModes = allowedModes
            .map(mode => `${mode || ''}`.toLowerCase())
            .filter(mode => ['auto', 'off', 'on'].includes(mode));
        const normalizedEfforts = allowedEfforts
            .map(effort => `${effort || ''}`.toLowerCase())
            .filter(effort => ['low', 'medium', 'high'].includes(effort));
        const finalModes = normalizedModes.length > 0 ? [...new Set(normalizedModes)] : fallback.allowedModes;
        const finalEfforts = normalizedEfforts.length > 0 ? [...new Set(normalizedEfforts)] : fallback.allowedEfforts;

        return {
            familyId: support?.familyId || fallback.familyId,
            familyName: support?.familyName || fallback.familyName,
            allowedModes: finalModes,
            allowedEfforts: finalEfforts,
            supportsExplicitMode: finalModes.some(mode => mode === 'on' || mode === 'off'),
            supportsEffort: finalEfforts.length > 1 || finalEfforts[0] !== 'medium',
            isModelLockedOn: finalModes.includes('on') && !finalModes.includes('off'),
            helpText: support?.helpText || fallback.helpText
        };
    }

    getAiReasoningNote(support) {
        const modeText = support.allowedModes.map(mode => mode === 'auto' ? 'Auto' : mode === 'off' ? 'Off' : 'On').join(' / ');
        const effortText = support.allowedEfforts.map(effort => effort === 'low' ? 'Low' : effort === 'high' ? 'High' : 'Medium').join(' / ');

        if (support.allowedModes.length === 1 && support.allowedModes[0] === 'auto') {
            return '当前模型族仅支持 Auto。';
        }

        if (support.allowedEfforts.length === 1) {
            return `可选模式：${modeText}；强度固定为 ${effortText}。`;
        }

        return `可选模式：${modeText}；可选强度：${effortText}。`;
    }

    scheduleAiReasoningSupportPreview() {
        if (this._aiReasoningSupportDebounce) {
            clearTimeout(this._aiReasoningSupportDebounce);
        }

        this._aiReasoningSupportDebounce = setTimeout(() => {
            this._aiReasoningSupportDebounce = null;
            this.refreshAiReasoningSupportPreview();
        }, 120);
    }

    async refreshAiReasoningSupportPreview() {
        const aiTab = this.container?.querySelector('[data-section="ai"]');
        const model = this.aiModels.find(x => x.id === this.editingAiModelId);
        if (!aiTab || !model) {
            this.aiReasoningSupportPreview = null;
            this.syncAiReasoningUiState();
            return;
        }

        const requestId = ++this._aiReasoningSupportRequestId;
        try {
            const support = await httpClient.post('/ai/reasoning-support', {
                provider: aiTab.querySelector('#cfg-ai-provider')?.value || model.provider || '',
                model: aiTab.querySelector('#cfg-ai-model')?.value || model.model || '',
                baseUrl: aiTab.querySelector('#cfg-ai-baseurl')?.value || model.baseUrl || '',
                protocol: null
            });

            if (requestId !== this._aiReasoningSupportRequestId) return;
            this.aiReasoningSupportPreview = this.normalizeAiReasoningSupport(support);
        } catch (error) {
            if (requestId !== this._aiReasoningSupportRequestId) return;
            console.warn('[SettingsView] Failed to refresh AI reasoning support preview:', error);
            this.aiReasoningSupportPreview = null;
        }

        this.syncAiReasoningUiState();
    }

    getCurrentAiReasoningSupport() {
        const model = this.aiModels.find(x => x.id === this.editingAiModelId);
        if (this.aiReasoningSupportPreview) {
            return this.normalizeAiReasoningSupport(this.aiReasoningSupportPreview);
        }

        return this.normalizeAiReasoningSupport(model?.reasoningSupport);
    }

    syncAiReasoningUiState() {
        const aiTab = this.container?.querySelector('[data-section="ai"]');
        if (!aiTab) return;

        const modeEl = aiTab.querySelector('#cfg-ai-reasoning-mode');
        const effortEl = aiTab.querySelector('#cfg-ai-reasoning-effort');
        const familyEl = aiTab.querySelector('#ai-reasoning-family');
        const helpEl = aiTab.querySelector('#ai-reasoning-help');
        const noteEl = aiTab.querySelector('#ai-reasoning-note');
        if (!modeEl || !effortEl || !familyEl || !helpEl || !noteEl) return;

        const support = this.getCurrentAiReasoningSupport();
        const allowedModes = support.allowedModes || ['auto'];
        const allowedEfforts = support.allowedEfforts || ['medium'];
        const modeOptions = Array.from(modeEl.options || []);
        const effortOptions = Array.from(effortEl.options || []);

        modeOptions.forEach(option => {
            option.disabled = !allowedModes.includes(option.value);
        });
        effortOptions.forEach(option => {
            option.disabled = !allowedEfforts.includes(option.value);
        });

        if (!allowedModes.includes(modeEl.value)) {
            modeEl.value = allowedModes[0] || 'auto';
        }
        if (!allowedEfforts.includes(effortEl.value)) {
            effortEl.value = allowedEfforts[0] || 'medium';
        }

        modeEl.disabled = allowedModes.length <= 1;
        effortEl.disabled = modeEl.value === 'off' || allowedEfforts.length <= 1;
        familyEl.textContent = `${support.familyName} (${support.familyId})`;
        helpEl.textContent = support.helpText || '';
        noteEl.textContent = this.getAiReasoningNote(support);
    }

    bindAiSettingsEvents() {
        const aiTab = this.container.querySelector('[data-section="ai"]');
        if (!aiTab) return;
        
        aiTab.addEventListener('click', async (e) => {
            const btn = e.target.closest('button');
            if (!btn) return;
            
            if (btn.id === 'btn-toggle-apikey') {
                const input = aiTab.querySelector('#cfg-ai-apikey');
                if (input) {
                    input.type = input.type === 'password' ? 'text' : 'password';
                    btn.textContent = input.type === 'password' ? '👁' : '🔒';
                }
            } else if (btn.id === 'btn-add-llm') {
                try {
                    const result = await httpClient.post('/ai/models', {
                        name: '新建模型',
                        provider: 'OpenAI Compatible',
                        model: '',
                        baseUrl: '',
                        apiKey: '',
                        timeoutMs: 120000
                    });
                    await this.loadAiModels();
                    this.editingAiModelId = result.id;
                    this._pendingFormEdits = {};
                    this.refreshAiTableAndForm();
                    showToast('模型已创建', 'success');
                } catch (err) {
                    showToast('创建模型失败: ' + err.message, 'error');
                }
            } else if (btn.dataset.action === 'edit') {
                // 切换编辑前先保存当前编辑（如果有未保存的修改）
                this.editingAiModelId = btn.dataset.id;
                this.aiReasoningSupportPreview = null;
                this._aiReasoningSupportRequestId += 1;
                this._pendingFormEdits = {};
                this.refreshAiTableAndForm();
            } else if (btn.dataset.action === 'delete') {
                if (this.aiModels.length <= 1) {
                    showToast('至少需保留一个模型', 'warning');
                    return;
                }
                const id = btn.dataset.id;
                try {
                    await httpClient.delete(`/ai/models/${id}`);
                    await this.loadAiModels();
                    if (this.editingAiModelId === id) {
                        this.editingAiModelId = this.aiModels[0]?.id;
                        this._pendingFormEdits = {};
                    }
                    this.refreshAiTableAndForm();
                    showToast('模型已删除', 'success');
                } catch (err) {
                    showToast('删除失败: ' + err.message, 'error');
                }
            } else if (btn.dataset.action === 'activate') {
                const id = btn.dataset.id;
                try {
                    await httpClient.post(`/ai/models/${id}/activate`, {});
                    await this.loadAiModels();
                    this.refreshAiTableAndForm();
                    showToast('激活模型已切换', 'success');
                } catch (err) {
                    showToast('切换激活失败: ' + err.message, 'error');
                }
            } else if (btn.id === 'btn-ai-test') {
                const modelId = this.editingAiModelId;
                if (!modelId) return;
                const resultEl = aiTab.querySelector('#ai-test-result');
                btn.disabled = true;
                btn.textContent = '⏳ 测试中...';
                if (resultEl) resultEl.textContent = '';

                try {
                    // 先保存当前表单到后端，再测试（确保用的是最新配置）
                    await this._saveCurrentForm();
                    const result = await httpClient.post(`/ai/models/${modelId}/test`, {});
                    if (result.success) {
                        if (resultEl) { resultEl.textContent = '✅ ' + result.message; resultEl.style.color = '#4caf50'; }
                        showToast('AI 测试连接成功', 'success');
                    } else {
                        if (resultEl) { resultEl.textContent = '❌ ' + result.message; resultEl.style.color = 'var(--cinnabar)'; }
                        showToast('AI 连接失败: ' + result.message, 'error');
                    }
                } catch (err) {
                    if (resultEl) { resultEl.textContent = '❌ 请求失败: ' + err.message; resultEl.style.color = 'var(--cinnabar)'; }
                    showToast('AI 请求失败: ' + err.message, 'error');
                } finally {
                    btn.disabled = false;
                    btn.textContent = '🔗 测试连接';
                }
            } else if (btn.id === 'btn-ai-save') {
                const modelId = this.editingAiModelId;
                if (!modelId) return;
                try {
                    await this._saveCurrentForm();
                    await httpClient.post(`/ai/models/${modelId}/activate`, {});
                    await this.loadAiModels({ preserveEditingId: true });
                    this.refreshAiTableAndForm();
                    showToast('模型设置已保存', 'success');
                } catch(err) {
                    showToast('保存失败: ' + err.message, 'error');
                }
            }
        });
        
        const handleAiFieldChange = (e) => {
            const el = e.target;
            if (!el || !el.id) return;

            const fieldMap = {
                'cfg-ai-name': 'name',
                'cfg-ai-provider': 'provider',
                'cfg-ai-model': 'model',
                'cfg-ai-baseurl': 'baseUrl',
                'cfg-ai-apikey': 'apiKey',
                'cfg-ai-timeout': 'timeoutMs',
                'cfg-ai-reasoning-mode': 'reasoning.mode',
                'cfg-ai-reasoning-effort': 'reasoning.effort'
            };
            const field = fieldMap[el.id];
            if (!field) return;

            this._pendingFormEdits[field] = el.value;
            if (el.id === 'cfg-ai-name') {
                const m = this.aiModels.find(x => x.id === this.editingAiModelId);
                if (m) {
                    m.name = el.value;
                    this.refreshAiTableOnly();
                }
            }

            if (['cfg-ai-provider', 'cfg-ai-model', 'cfg-ai-baseurl'].includes(el.id)) {
                this.scheduleAiReasoningSupportPreview();
            }

            if (['cfg-ai-reasoning-mode', 'cfg-ai-reasoning-effort'].includes(el.id)) {
                this.syncAiReasoningUiState();
            }
        };

        aiTab.addEventListener('input', handleAiFieldChange);
        aiTab.addEventListener('change', handleAiFieldChange);
        
        setTimeout(() => {
            this.refreshAiTableAndForm();
            this.syncAiReasoningUiState();
        }, 0);
    }

    getActiveTabName() {
        if (this.activeTab) {
            return this.activeTab;
        }

        const activePanel = this.container?.querySelector('.settings-panel.active');
        return activePanel?.dataset.section || null;
    }

    hasPendingAiChanges() {
        const modelId = this.editingAiModelId;
        if (!modelId) {
            return false;
        }

        if (Object.keys(this._pendingFormEdits || {}).length > 0) {
            return true;
        }

        const aiTab = this.container?.querySelector('[data-section="ai"]');
        if (!aiTab) {
            return false;
        }

        const model = this.aiModels.find(x => x.id === modelId);
        if (!model) {
            return false;
        }

        const currentTimeout = parseInt(aiTab.querySelector('#cfg-ai-timeout')?.value || '120000', 10);
        const normalizedTimeout = Number.isFinite(currentTimeout) ? currentTimeout : 120000;
        const pendingApiKey = aiTab.querySelector('#cfg-ai-apikey')?.value || '';
        const currentReasoning = this.normalizeAiReasoning(model.reasoning);
        const draftReasoning = this.normalizeAiReasoning({
            mode: aiTab.querySelector('#cfg-ai-reasoning-mode')?.value || currentReasoning.mode,
            effort: aiTab.querySelector('#cfg-ai-reasoning-effort')?.value || currentReasoning.effort
        });

        return (aiTab.querySelector('#cfg-ai-name')?.value || '') !== (model.name || '')
            || (aiTab.querySelector('#cfg-ai-provider')?.value || 'OpenAI Compatible') !== (model.provider || 'OpenAI Compatible')
            || (aiTab.querySelector('#cfg-ai-model')?.value || '') !== (model.model || '')
            || (aiTab.querySelector('#cfg-ai-baseurl')?.value || '') !== (model.baseUrl || '')
            || normalizedTimeout !== (model.timeoutMs ?? 120000)
            || draftReasoning.mode !== currentReasoning.mode
            || draftReasoning.effort !== currentReasoning.effort
            || pendingApiKey.trim().length > 0;
    }

    /** 将当前编辑表单的值保存到后端 */
    async _saveCurrentForm() {
        const modelId = this.editingAiModelId;
        if (!modelId) return;
        const aiTab = this.container.querySelector('[data-section="ai"]');
        if (!aiTab) return;

        const payload = {
            name: aiTab.querySelector('#cfg-ai-name')?.value || '',
            provider: aiTab.querySelector('#cfg-ai-provider')?.value || 'OpenAI Compatible',
            model: aiTab.querySelector('#cfg-ai-model')?.value || '',
            baseUrl: aiTab.querySelector('#cfg-ai-baseurl')?.value || '',
            apiKey: aiTab.querySelector('#cfg-ai-apikey')?.value || '', // 空 → 后端保留原值
            timeoutMs: parseInt(aiTab.querySelector('#cfg-ai-timeout')?.value || '120000', 10),
            reasoning: {
                mode: aiTab.querySelector('#cfg-ai-reasoning-mode')?.value || 'auto',
                effort: aiTab.querySelector('#cfg-ai-reasoning-effort')?.value || 'medium'
            }
        };

        await httpClient.put(`/ai/models/${modelId}`, payload);
        await this.loadAiModels({ preserveEditingId: true });
        this.aiReasoningSupportPreview = null;
        this._pendingFormEdits = {};
        this.refreshAiTableOnly();
    }
    
    refreshAiTableOnly() {
        const tbody = this.container.querySelector('#ai-models-table tbody');
        if (!tbody) return;
        
        tbody.innerHTML = this.aiModels.map(m => {
            const isEditing = m.id === this.editingAiModelId;
            const id = this.escapeHtml(m.id || '');
            const name = this.escapeHtml(m.name || '-');
            const provider = String(m.provider || '');
            const providerHtml = this.escapeHtml(provider || '-');
            const model = this.escapeHtml(m.model || '-');
            const badgeBg = provider.includes('Anthropic') ? '#fce7f3' : (provider.includes('OpenAI API') ? '#e0e7ff' : '#f3f4f6');
            const badgeColor = provider.includes('Anthropic') ? '#db2777' : (provider.includes('OpenAI API') ? '#4338ca' : '#475569');
            
            return `
                <tr style="${isEditing ? 'background:#f8fafc;' : ''}">
                    <td class="font-bold">${name}</td>
                    <td><span class="type-badge" style="background:${badgeBg}; color:${badgeColor};">${providerHtml}</span></td>
                    <td class="font-mono">${model}</td>
                    <td>
                        ${m.isActive 
                            ? '<span class="settings-status-badge status-connected" style="background:#ecfdf5; padding:2px 8px;"><span class="status-dot"></span> Active</span>' 
                            : `<button class="cv-btn settings-btn-light" style="padding:2px 8px; font-size:12px; height:24px;" data-action="activate" data-id="${id}">设为激活</button>`}
                    </td>
                    <td>
                        <button class="action-icon-btn" data-action="edit" data-id="${id}" title="编辑">
                            <svg viewBox="0 0 24 24"><path d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25z"/></svg>
                        </button>
                        <button class="action-icon-btn" data-action="delete" data-id="${id}" title="删除" style="color:var(--cinnabar);">
                            <svg viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg>
                        </button>
                    </td>
                </tr>
            `;
        }).join('');
    }
    
    refreshAiTableAndForm() {
        this.refreshAiTableOnly();
        
        const formContainer = this.container.querySelector('#ai-detail-form');
        if (!formContainer) return;
        
        const m = this.aiModels.find(x => x.id === this.editingAiModelId);
        if (!m) {
            formContainer.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-muted);">请选择一个模型进行编辑</div>';
            return;
        }

        // apiKey 不再从后端获取真实值，用 placeholder 提示
        const apiKeyPlaceholder = m.hasApiKey ? '●●●●●●（已配置，留空则不修改）' : '请输入 API Key';
        const reasoning = this.normalizeAiReasoning(m.reasoning);
        const support = this.normalizeAiReasoningSupport(this.aiReasoningSupportPreview || m.reasoningSupport);
        const nameValue = this.escapeHtml(m.name || '');
        const modelValue = this.escapeHtml(m.model || '');
        const baseUrlValue = this.escapeHtml(m.baseUrl || '');
        const apiKeyPlaceholderValue = this.escapeHtml(apiKeyPlaceholder);
        const timeoutValue = Number.isFinite(Number(m.timeoutMs)) ? Number(m.timeoutMs) : 120000;
        
        formContainer.innerHTML = `
            <div style="display:flex; gap:16px; margin-bottom:16px;">
                 <div class="settings-fieldset" style="flex:1;">
                     <label>模型昵称</label>
                     <input type="text" class="cv-input" id="cfg-ai-name" value="${nameValue}" placeholder="本地别名">
                  </div>
                 <div class="settings-fieldset" style="flex:1;">
                     <label>API 协议</label>
                     <select class="cv-input" id="cfg-ai-provider">
                         <option value="OpenAI API" ${m.provider==='OpenAI API'?'selected':''}>OpenAI API</option>
                         <option value="Anthropic Claude" ${m.provider==='Anthropic Claude'?'selected':''}>Anthropic Claude</option>
                         <option value="OpenAI Compatible" ${m.provider==='OpenAI Compatible'?'selected':''}>本地模型 / OpenAI 兼容 (Ollama, vLLM, GLM等)</option>
                     </select>
                 </div>
                 <div class="settings-fieldset" style="flex:1;">
                     <label>模型选择</label>
                     <input type="text" class="cv-input" id="cfg-ai-model" value="${modelValue}" placeholder="如 deepseek-chat">
                  </div>
             </div>
             <div class="settings-fieldset" style="margin-bottom:16px;">
                 <label>API Endpoint (Host & Path)</label>
                 <div style="display:flex;">
                      <span style="padding:10px 12px; background:#f8fafc; border:1px solid #cbd5e1; border-right:none; border-radius:6px 0 0 6px; color:#64748b;">URL:</span>
                      <input type="text" class="cv-input" id="cfg-ai-baseurl" value="${baseUrlValue}" placeholder="如 https://api.deepseek.com/v1" style="border-radius:0 6px 6px 0;">
                  </div>
             </div>
              <div style="display:flex; gap:16px;">
                  <div class="settings-fieldset" style="flex:2;">
                      <label>API Key</label>
                     <div class="input-with-suffix" style="position:relative;">
                          <input type="password" class="cv-input" id="cfg-ai-apikey" value="" placeholder="${apiKeyPlaceholderValue}" style="padding-right:36px; font-family:monospace;">
                         <button class="icon-action-btn" id="btn-toggle-apikey" style="position:absolute; right:10px; top:50%; transform:translateY(-50%);">👁</button>
                     </div>
                 </div>
                  <div class="settings-fieldset" style="flex:1;">
                      <label>请求超时 (ms)</label>
                      <input type="number" class="cv-input" id="cfg-ai-timeout" value="${timeoutValue}">
                  </div>
              </div>
              <details class="settings-fieldset" style="margin-top:16px; border:1px solid #e2e8f0; border-radius:10px; padding:12px 14px; background:#fafcff;" open>
                  <summary style="cursor:pointer; font-weight:700; color:#1e293b;">推理 / Thinking</summary>
                  <div style="display:flex; gap:16px; margin-top:14px;">
                      <div class="settings-fieldset" style="flex:1;">
                          <label>推理模式</label>
                          <select class="cv-input" id="cfg-ai-reasoning-mode">
                              <option value="auto" ${reasoning.mode === 'auto' ? 'selected' : ''}>Auto</option>
                              <option value="off" ${reasoning.mode === 'off' ? 'selected' : ''}>Off</option>
                              <option value="on" ${reasoning.mode === 'on' ? 'selected' : ''}>On</option>
                          </select>
                      </div>
                      <div class="settings-fieldset" style="flex:1;">
                          <label>思考强度</label>
                          <select class="cv-input" id="cfg-ai-reasoning-effort">
                              <option value="low" ${reasoning.effort === 'low' ? 'selected' : ''}>Low</option>
                              <option value="medium" ${reasoning.effort === 'medium' ? 'selected' : ''}>Medium</option>
                              <option value="high" ${reasoning.effort === 'high' ? 'selected' : ''}>High</option>
                          </select>
                      </div>
                  </div>
                  <div style="display:flex; gap:10px; align-items:center; flex-wrap:wrap; margin-top:12px;">
                       <span style="font-size:12px; font-weight:700; color:#475569;">识别模型族</span>
                       <span id="ai-reasoning-family" style="font-size:12px; padding:4px 8px; border-radius:999px; background:#eef2ff; color:#3730a3;">${this.escapeHtml(`${support.familyName} (${support.familyId})`)}</span>
                       <span id="ai-reasoning-note" style="font-size:12px; color:#64748b;">${this.escapeHtml(this.getAiReasoningNote(support))}</span>
                   </div>
                   <div id="ai-reasoning-help" style="margin-top:8px; font-size:12px; line-height:1.6; color:#475569;">${this.escapeHtml(support.helpText || '')}</div>
               </details>
              <div style="display:flex; justify-content:flex-end; gap:12px; margin-top:24px;">
                  <button class="cv-btn settings-btn-light" id="btn-ai-test">🔗 测试连接</button>
                  <button class="cv-btn settings-btn-danger" id="btn-ai-save">💾 保存并应用该模型集</button>
             </div>
              <div id="ai-test-result" style="margin-top:10px; text-align:right; font-size:13px; font-weight:500;"></div>
         `;
        this.syncAiReasoningUiState();
    }
    
    bindCameraManagementEvents() {
        const section = this.container.querySelector('[data-section="cameras"]');
        if (!section) return;

        const discoverHuarayBtn = section.querySelector('#btn-discover-huaray-cameras');
        discoverHuarayBtn?.addEventListener('click', () => this.discoverCameras('huaray', discoverHuarayBtn));

        const discoverHikvisionBtn = section.querySelector('#btn-discover-hikvision-cameras');
        discoverHikvisionBtn?.addEventListener('click', () => this.discoverCameras('hikvision', discoverHikvisionBtn));

        // 兼容旧按钮 ID，避免历史页面缓存导致按钮失效。
        const discoverBtn = section.querySelector('#btn-discover-cameras');
        discoverBtn?.addEventListener('click', () => this.discoverCameras('all', discoverBtn));

        const calibBtn = section.querySelector('#btn-hand-eye-calib');
        const previewBtn = section.querySelector('#btn-camera-preview');
        previewBtn?.addEventListener('click', () => this.showSelectedCameraPreview());
        if (calibBtn) {
            calibBtn.addEventListener('click', async () => {
                try {
                    const binding = this.getSelectedCameraBinding();
                    if (!binding) {
                        showToast('请先在相机列表中明确选中一台相机，再启动二维平面标定向导', 'warning');
                        return;
                    }

                    const module = await import('../../core/calibration/planarScaleOffsetCalibWizard.js');
                    const wizard = new module.PlanarScaleOffsetCalibWizard(null, {
                        captureFrame: (cameraBindingId) => this.captureCameraPreview(cameraBindingId),
                        getCameraBindingId: () => binding.id
                    });
                    wizard.show();
                } catch (e) {
                    showToast('无法加载二维平面标定向导: ' + e.message, 'error');
                }
            });
        }

        const tbody = this.container.querySelector('#camera-bindings-table tbody');
        if (tbody) {
            tbody.addEventListener('click', async (e) => {
                const tr = e.target.closest('tr.camera-row');
                if (!tr) return;

                // 点击选中行，展示详情
                this.selectCameraRow(tr);

                // 删除按钮
                const deleteBtn = e.target.closest('.action-icon-btn');
                if (deleteBtn) {
                    const id = tr.dataset.id;
                    if (confirm('确定要删除此相机配置吗？')) {
                        const previousBindings = [...this.cameraBindings];
                        this.cameraBindings = this.cameraBindings.filter(b => b.id !== id);
                        if (this.selectedCameraBindingId === id) {
                            this.selectedCameraBindingId = null;
                        }
                        this.refreshCameraTable();

                        const saved = await this.saveCameraBindings({ silent: true });
                        if (!saved) {
                            this.cameraBindings = previousBindings;
                            this.refreshCameraTable();
                            showToast('删除相机配置失败，请重试', 'error');
                            return;
                        }

                        showToast('已移除相机配置', 'success');
                    }
                }
            });
        }

        const saveParamsBtn = section.querySelector('#btn-save-camera-params');
        saveParamsBtn?.addEventListener('click', () => this.saveSelectedCameraParameters());

        const resetParamsBtn = section.querySelector('#btn-reset-camera-params');
        resetParamsBtn?.addEventListener('click', () => {
            if (!this.selectedCameraBindingId) {
                this.updateCameraParameterPanel(null);
                return;
            }
            const row = this.container.querySelector(`#camera-bindings-table tr.camera-row[data-id="${this.selectedCameraBindingId}"]`);
            if (row) {
                this.selectCameraRow(row);
            } else {
                this.updateCameraParameterPanel(null);
            }
        });

        const triggerModeSelect = section.querySelector('#cam-param-trigger-mode');
        triggerModeSelect?.addEventListener('change', () => {
            this.syncCameraFrameRateInputState();
            this.syncCameraTriggerSourceInputState();
            this.syncCameraHardwareTriggerSourceInputState();
        });

        const triggerSourceSelect = section.querySelector('#cam-param-software-trigger-source');
        triggerSourceSelect?.addEventListener('change', () => {
            this.syncCameraTriggerSourceInputState();
            if (this.normalizeSoftwareTriggerSource(triggerSourceSelect.value) === 'SerialPhotoelectric') {
                this.loadSerialPhotoelectricPorts({ silent: false, applyRecommended: true });
            }
        });

        const learnEnterDeviceBtn = section.querySelector('#btn-learn-enter-trigger-device');
        learnEnterDeviceBtn?.addEventListener('click', () => this.learnEnterPhotoelectricDevice());

        const testSerialPhotoelectricBtn = section.querySelector('#btn-test-serial-photoelectric');
        testSerialPhotoelectricBtn?.addEventListener('click', () => this.testSerialPhotoelectricTrigger());

        const refreshSerialPhotoelectricBtn = section.querySelector('#btn-refresh-serial-photoelectric-port');
        refreshSerialPhotoelectricBtn?.addEventListener('click', () =>
            this.loadSerialPhotoelectricPorts({ silent: false, applyRecommended: true }));
    }

    async loadCameraBindings() {
        const tbody = this.container.querySelector('#camera-bindings-table tbody');
        if (tbody) {
            tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; padding: 24px;"><div class="cv-spinner" style="margin-right:8px; display:inline-block;"></div>正在加载相机配置...</td></tr>`;
        }

        try {
            const bindings = await httpClient.get('/cameras/bindings');
            this.cameraBindings = (bindings || []).map(binding => {
                const exposureRaw = binding.exposureTimeUs ?? binding.ExposureTimeUs;
                const gainRaw = binding.gainDb ?? binding.GainDb;
                const pixelFormatRaw = binding.pixelFormat ?? binding.PixelFormat;
                const triggerRaw = binding.triggerMode ?? binding.TriggerMode;
                const hardwareTriggerSourceRaw = binding.hardwareTriggerSource ?? binding.HardwareTriggerSource;
                const softwareTriggerSourceRaw = binding.softwareTriggerSource ?? binding.SoftwareTriggerSource;
                const enterDebounceRaw = binding.enterPhotoelectricDebounceMs ?? binding.EnterPhotoelectricDebounceMs;
                const enterTimeoutRaw = binding.enterPhotoelectricTimeoutMs ?? binding.EnterPhotoelectricTimeoutMs;
                const ignoreBusyRaw = binding.ignoreEnterTriggerWhileBusy ?? binding.IgnoreEnterTriggerWhileBusy;
                const enterDeviceRaw = binding.enterPhotoelectricDeviceId ?? binding.EnterPhotoelectricDeviceId ?? '';
                const serialPortRaw = binding.serialPhotoelectricPortName ?? binding.SerialPhotoelectricPortName ?? '';
                const serialBaudRaw = binding.serialPhotoelectricBaudRate ?? binding.SerialPhotoelectricBaudRate;
                const serialDebounceRaw = binding.serialPhotoelectricDebounceMs ?? binding.SerialPhotoelectricDebounceMs;
                const serialTimeoutRaw = binding.serialPhotoelectricTimeoutMs ?? binding.SerialPhotoelectricTimeoutMs;
                const ignoreSerialBusyRaw = binding.ignoreSerialPhotoelectricTriggerWhileBusy ?? binding.IgnoreSerialPhotoelectricTriggerWhileBusy;
                const targetFrameRateRaw = binding.targetFrameRateFps ?? binding.TargetFrameRateFps;
                const connectionStatus = binding.connectionStatus ?? binding.ConnectionStatus ?? binding.status ?? binding.Status ?? null;
                const serialNumber = binding.serialNumber ?? binding.SerialNumber ?? binding.deviceId ?? binding.DeviceId ?? '';
                const ipAddress = binding.ipAddress ?? binding.IpAddress ?? '';

                return {
                    ...binding,
                    serialNumber: typeof serialNumber === 'string' ? serialNumber.trim() : '',
                    ipAddress: typeof ipAddress === 'string' ? ipAddress.trim() : '',
                    exposureTimeUs: Number.isFinite(Number(exposureRaw)) ? Number(exposureRaw) : 5000,
                    gainDb: Number.isFinite(Number(gainRaw)) ? Number(gainRaw) : 1.0,
                    pixelFormat: this.normalizeCameraPixelFormat(pixelFormatRaw),
                    triggerMode: this.normalizeCameraTriggerMode(triggerRaw),
                    hardwareTriggerSource: this.normalizeHardwareTriggerSource(hardwareTriggerSourceRaw),
                    softwareTriggerSource: this.normalizeSoftwareTriggerSource(softwareTriggerSourceRaw),
                    enterPhotoelectricDebounceMs: this.normalizeEnterDebounceMs(enterDebounceRaw),
                    enterPhotoelectricTimeoutMs: this.normalizeEnterTimeoutMs(enterTimeoutRaw),
                    ignoreEnterTriggerWhileBusy: ignoreBusyRaw !== false,
                    enterPhotoelectricDeviceId: String(enterDeviceRaw || '').trim(),
                    serialPhotoelectricPortName: String(serialPortRaw || '').trim(),
                    serialPhotoelectricBaudRate: this.normalizeSerialBaudRate(serialBaudRaw),
                    serialPhotoelectricDebounceMs: this.normalizeSerialDebounceMs(serialDebounceRaw),
                    serialPhotoelectricTimeoutMs: this.normalizeSerialTimeoutMs(serialTimeoutRaw),
                    ignoreSerialPhotoelectricTriggerWhileBusy: ignoreSerialBusyRaw !== false,
                    targetFrameRateFps: this.normalizeCameraTargetFrameRate(targetFrameRateRaw),
                    connectionStatus: typeof connectionStatus === 'string' && connectionStatus.trim() ? connectionStatus.trim() : null
                };
            });

            if (this.selectedCameraBindingId && !this.cameraBindings.some(b => b.id === this.selectedCameraBindingId)) {
                this.selectedCameraBindingId = null;
            }
            this.refreshCameraTable();
            this.syncActiveCameraSelection();
        } catch (error) {
            console.error('Failed to load camera bindings:', error);
            if (tbody) {
                tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; padding: 20px; color:var(--accent);">加载配置失败: ${this.escapeHtml(error.message)}</td></tr>`;
            }
            this.updateCameraParameterPanel(null);
        }
    }

    async discoverCameras(vendor = 'all', sourceButton = null) {
        const vendorMeta = {
            huaray: { text: '华睿', endpoint: '/cameras/discover/huaray' },
            hikvision: { text: '海康', endpoint: '/cameras/discover/hikvision' },
            all: { text: '全部', endpoint: '/cameras/discover' }
        };
        const meta = vendorMeta[vendor] || vendorMeta.all;

        showToast(`正在搜索${meta.text}相机...`, 'info');
        if (sourceButton) sourceButton.disabled = true;

        try {
            const response = await httpClient.get(meta.endpoint);
            const devices = Array.isArray(response)
                ? response
                : (response?.devices || response?.Devices || []);
            const diagnostics = Array.isArray(response)
                ? null
                : (response?.diagnostics || response?.Diagnostics || null);

            if (diagnostics?.message) {
                const diagnosticsType = devices.length > 0 ? 'info' : 'warning';
                showToast(diagnostics.message, diagnosticsType);
                console.info('[SettingsView] Camera diagnostics:', diagnostics);
            }

            if (devices && devices.length > 0) {
                showToast(`找到 ${devices.length} 个${meta.text}相机设备`, 'success');
                this.showDiscoveryModal(devices, meta.text);
            } else {
                showToast(`未发现在线${meta.text}相机`, 'warning');
            }
        } catch (error) {
            showToast(`搜索${meta.text}相机失败: ${error.message}`, 'error');
        } finally {
            if (sourceButton) sourceButton.disabled = false;
        }
    }

    showDiscoveryModal(devices, vendorText = '在线') {
        const normalizedDevices = (devices || []).map(device => ({
            cameraId: device.cameraId ?? device.CameraId ?? '',
            manufacturer: device.manufacturer ?? device.Manufacturer ?? '',
            model: device.model ?? device.Model ?? '',
            connectionType: device.connectionType ?? device.ConnectionType ?? '',
            ipAddress: device.ipAddress ?? device.IpAddress ?? ''
        }));
        const vendorLabel = this.escapeHtml(vendorText);

        const contentDiv = document.createElement('div');
        contentDiv.innerHTML = `
            <div class="settings-card-table-wrapper" style="max-height: 420px; overflow: auto;">
                <table class="settings-modern-table" style="margin: 0; width: 100%;">
                    <thead>
                        <tr>
                            <th>序列号 (IP)</th>
                            <th>制造商</th>
                            <th>型号</th>
                            <th>驱动/协议</th>
                            <th>操作</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${normalizedDevices.map(d => `
                            <tr>
                                <td><code style="background:var(--panel-bg); padding:2px 6px; border-radius:4px; font-size:13px; font-family:var(--font-mono); word-break:break-all;">${this.escapeHtml(d.cameraId || '-')}</code></td>
                                <td>${this.escapeHtml(d.manufacturer || '-')}</td>
                                <td>${this.escapeHtml(d.model || '-')}</td>
                                <td>${this.escapeHtml(d.connectionType || '-')}</td>
                                <td>
                                    <button class="cv-btn cv-btn-primary btn-bind-camera"
                                            data-sn="${this.escapeHtml(d.cameraId || '')}"
                                            data-man="${this.escapeHtml(d.manufacturer)}"
                                            data-model="${this.escapeHtml(d.model)}"
                                            data-ip="${this.escapeHtml(d.ipAddress || d.IpAddress || '')}"
                                            style="padding:6px 12px; font-size:13px; border-radius:6px;">
                                        添加绑定
                                    </button>
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
            <p style="margin-top:16px; font-size:13px; color:var(--text-muted);">
                * 当前展示 ${vendorLabel} 搜索结果。点击“添加绑定”可将设备加入当前工程。
            </p>
        `;

        const modal = createModal({
            title: `配置向导：发现${vendorText}相机`,
            content: contentDiv,
            width: '920px'
        });
        modal.querySelector('.cv-modal')?.style.setProperty('max-width', '95vw');

        // 绑定点击事件
        const bindBtns = contentDiv.querySelectorAll('.btn-bind-camera');
        bindBtns.forEach(btn => {
            btn.addEventListener('click', async () => {
                const sn = btn.dataset.sn;
                const manufacturer = btn.dataset.man;
                const model = btn.dataset.model;

                // 检查是否已存在
                if (this.cameraBindings.find(b => String(b.serialNumber || b.deviceId || '').toLowerCase() === String(sn || '').toLowerCase())) {
                    showToast('该相机已在绑定列表中，无需重复添加', 'warning');
                    return;
                }

                const displayName = prompt('请输入该相机的逻辑命名 (如: Left_Camera_01):', `Cam_${this.cameraBindings.length + 1}`);
                if (!displayName) return;

                const newBinding = {
                    id: `cam_${Date.now().toString(36)}`,
                    displayName: displayName,
                    serialNumber: sn,
                    manufacturer: manufacturer,
                    modelName: model,
                    ipAddress: btn.dataset.ip || '',
                    isEnabled: true,
                    exposureTimeUs: 5000,
                    gainDb: 1.0,
                    pixelFormat: 'Mono8',
                    triggerMode: 'Software',
                    hardwareTriggerSource: 'Line0',
                    softwareTriggerSource: 'Manual',
                    enterPhotoelectricDebounceMs: 200,
                    enterPhotoelectricTimeoutMs: 30000,
                    ignoreEnterTriggerWhileBusy: true,
                    enterPhotoelectricDeviceId: '',
                    serialPhotoelectricPortName: '',
                    serialPhotoelectricBaudRate: 9600,
                    serialPhotoelectricDebounceMs: 200,
                    serialPhotoelectricTimeoutMs: 30000,
                    ignoreSerialPhotoelectricTriggerWhileBusy: true,
                    targetFrameRateFps: 10
                };

                this.cameraBindings.push(newBinding);
                const saved = await this.saveCameraBindings();
                if (!saved) {
                    this.cameraBindings = this.cameraBindings.filter(b => b.id !== newBinding.id);
                    this.refreshCameraTable();
                    return;
                }

                this.selectedCameraBindingId = newBinding.id;
                await this.loadCameraBindings();
                showToast(`已成功绑定逻辑相机: ${displayName}`, 'success');
                
                // 置灰当前按钮，防止重复点击
                btn.disabled = true;
                btn.textContent = '已绑定';
                btn.classList.add('settings-btn-light');
                btn.classList.remove('cv-btn-primary');

                const selectedRow = this.container.querySelector(`#camera-bindings-table tr.camera-row[data-id="${newBinding.id}"]`);
                if (selectedRow) {
                    this.selectCameraRow(selectedRow);
                }
            });
        });
    }

    refreshCameraTable() {
        const tbody = this.container.querySelector('#camera-bindings-table tbody');
        if (!tbody) return;

        if (this.selectedCameraBindingId && !this.cameraBindings.some(b => b.id === this.selectedCameraBindingId)) {
            this.selectedCameraBindingId = null;
        }

        if (!this.cameraBindings || this.cameraBindings.length === 0) {
            this.selectedCameraBindingId = null;
            this.updateCameraParameterPanel(null);
            tbody.innerHTML = '<tr><td colspan="6" style="text-align:center; color:var(--text-muted); padding:24px;">暂无绑定配置，请点击“华睿搜索”或“海康搜索”发现设备</td></tr>';
            return;
        }

        tbody.innerHTML = this.cameraBindings.map((b, index) => {
            const rawConnectionStatus = String(
                b.connectionStatus ?? b.ConnectionStatus ?? b.status ?? b.Status ?? ''
            ).trim();
            const normalizedStatus = rawConnectionStatus.toLowerCase();
            const isConnected = ['connected', 'online', 'ready', 'active', '已连接'].includes(normalizedStatus);
            const isDisconnected = ['disconnected', 'offline', 'error', 'disabled', 'unbound', '已断开'].includes(normalizedStatus);
            const statusClass = isConnected
                ? 'status-connected'
                : (isDisconnected ? 'status-error' : 'status-disconnected');
            const statusDotClass = isConnected
                ? 'status-dot'
                : (isDisconnected ? 'status-dot status-error' : 'status-dot');
            const statusText = this.escapeHtml(rawConnectionStatus || '未知');
            const bgClass = index === 0 ? '#fee2e2' : '#e0e7ff';
            const fgClass = index === 0 ? 'var(--cinnabar)' : 'var(--primary)';
            const isSelected = this.selectedCameraBindingId === b.id;
            const bindingId = this.escapeHtml(b.id || '');
            const displayName = this.escapeHtml(b.displayName || '未命名相机');
            const serialNumber = this.escapeHtml(b.serialNumber || '未知');
            const ipAddress = this.escapeHtml(b.ipAddress || b.IpAddress || '未知');
            const manufacturer = this.escapeHtml(b.manufacturer || '未知');
            const pixelFormat = this.escapeHtml(this.getCameraPixelFormatLabel(b.pixelFormat ?? b.PixelFormat));
            const triggerSourceLabel = this.escapeHtml(this.getTriggerSourceLabel(b));

            return `
            <tr class="camera-row" data-id="${bindingId}" style="cursor: pointer; background:${isSelected ? 'var(--panel-bg)' : 'transparent'};">
                <td>
                    <div style="display:flex; align-items:center; gap:12px;">
                        <div style="width:32px; height:32px; background:${bgClass}; border-radius:8px; display:flex; align-items:center; justify-content:center; color:${fgClass};">
                            <svg viewBox="0 0 24 24" style="width:18px;height:18px;fill:currentColor;"><path d="M12 4C7.58 4 4 7.58 4 12s3.58 8 8 8 8-3.58 8-8-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6s2.69-6 6-6 6 2.69 6 6-2.69 6-6 6zM12 7c-2.76 0-5 2.24-5 5s2.24 5 5 5 5-2.24 5-5-2.24-5-5-5zm0 8c-1.65 0-3-1.35-3-3s1.35-3 3-3 3 1.35 3 3-1.35 3-3 3z"/></svg>
                        </div>
                        <div>
                            <div class="font-bold">${displayName}</div>
                            <div class="text-muted" style="font-size:12px;">${serialNumber}</div>
                            <div class="text-muted" style="font-size:12px;">${triggerSourceLabel}</div>
                        </div>
                    </div>
                </td>
                <td><span class="font-mono">${ipAddress}</span></td>
                <td>${manufacturer}</td>
                <td><span class="font-mono">${pixelFormat}</span></td>
                <td><span class="settings-status-badge ${statusClass}"><span class="${statusDotClass}"></span> ${statusText}</span></td>
                <td><button class="action-icon-btn" title="删除" style="color:var(--cinnabar);"><svg viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg></button></td>
            </tr>
            `;
        }).join('');

        if (this.selectedCameraBindingId) {
            const selectedBinding = this.cameraBindings.find(b => b.id === this.selectedCameraBindingId);
            this.updateCameraParameterPanel(selectedBinding || null);
        } else {
            this.updateCameraParameterPanel(null);
        }
    }

    selectCameraRow(tr) {
        if (!tr) return;

        // 取消其他行高亮
        const allRows = this.container.querySelectorAll('tr.camera-row');
        allRows.forEach(r => {
            r.style.backgroundColor = '';
        });

        const id = tr.getAttribute('data-id');
        this.selectedCameraBindingId = id;
        if (this.config) {
            this.config.activeCameraId = id;
        }
        const cam = this.cameraBindings.find(b => b.id === id);
        tr.style.backgroundColor = 'var(--panel-bg)';

        this.updateCameraParameterPanel(cam || null);
        this.syncActiveCameraSelection();
    }

    updateCameraParameterPanel(cam) {
        const nameEl = this.container.querySelector('#current-cam-name');
        if (nameEl) {
            nameEl.textContent = cam?.displayName || '未选择相机';
        }

        const exposureInput = this.container.querySelector('#cam-param-exposure');
        const gainInput = this.container.querySelector('#cam-param-gain');
        const pixelFormatSelect = this.container.querySelector('#cam-param-pixel-format');
        const triggerModeSelect = this.container.querySelector('#cam-param-trigger-mode');
        const hardwareTriggerSourceSelect = this.container.querySelector('#cam-param-hardware-trigger-source');
        const triggerSourceSelect = this.container.querySelector('#cam-param-software-trigger-source');
        const enterDebounceInput = this.container.querySelector('#cam-param-enter-debounce');
        const enterTimeoutInput = this.container.querySelector('#cam-param-enter-timeout');
        const enterDeviceInput = this.container.querySelector('#cam-param-enter-device-id');
        const ignoreBusyInput = this.container.querySelector('#cam-param-ignore-enter-busy');
        const serialPortInput = this.container.querySelector('#cam-param-serial-port-name');
        const serialBaudInput = this.container.querySelector('#cam-param-serial-baud-rate');
        const serialDebounceInput = this.container.querySelector('#cam-param-serial-debounce');
        const serialTimeoutInput = this.container.querySelector('#cam-param-serial-timeout');
        const ignoreSerialBusyInput = this.container.querySelector('#cam-param-ignore-serial-busy');
        const frameRateInput = this.container.querySelector('#cam-param-target-frame-rate');

        if (exposureInput) {
            exposureInput.value = cam ? String(cam.exposureTimeUs ?? 5000) : '';
            exposureInput.disabled = !cam;
        }
        if (gainInput) {
            gainInput.value = cam ? String(cam.gainDb ?? 1.0) : '';
            gainInput.disabled = !cam;
        }
        if (pixelFormatSelect) {
            pixelFormatSelect.value = this.normalizeCameraPixelFormat(cam?.pixelFormat ?? cam?.PixelFormat);
            pixelFormatSelect.disabled = !cam;
        }
        if (triggerModeSelect) {
            triggerModeSelect.value = this.normalizeCameraTriggerMode(cam?.triggerMode);
            triggerModeSelect.disabled = !cam;
        }
        if (hardwareTriggerSourceSelect) {
            hardwareTriggerSourceSelect.value = this.normalizeHardwareTriggerSource(cam?.hardwareTriggerSource);
            hardwareTriggerSourceSelect.disabled = !cam;
        }
        if (triggerSourceSelect) {
            triggerSourceSelect.value = this.normalizeSoftwareTriggerSource(cam?.softwareTriggerSource);
            triggerSourceSelect.disabled = !cam;
        }
        if (enterDebounceInput) {
            enterDebounceInput.value = cam ? String(this.normalizeEnterDebounceMs(cam.enterPhotoelectricDebounceMs)) : '';
        }
        if (enterTimeoutInput) {
            enterTimeoutInput.value = cam ? String(this.normalizeEnterTimeoutMs(cam.enterPhotoelectricTimeoutMs)) : '';
        }
        if (enterDeviceInput) {
            enterDeviceInput.value = cam ? String(cam.enterPhotoelectricDeviceId || '') : '';
        }
        if (ignoreBusyInput) {
            ignoreBusyInput.checked = cam?.ignoreEnterTriggerWhileBusy !== false;
            ignoreBusyInput.disabled = !cam;
        }
        if (serialPortInput) {
            serialPortInput.value = cam ? String(cam.serialPhotoelectricPortName || '') : '';
        }
        if (serialBaudInput) {
            serialBaudInput.value = cam ? String(this.normalizeSerialBaudRate(cam.serialPhotoelectricBaudRate)) : '9600';
        }
        if (serialDebounceInput) {
            serialDebounceInput.value = cam ? String(this.normalizeSerialDebounceMs(cam.serialPhotoelectricDebounceMs)) : '';
        }
        if (serialTimeoutInput) {
            serialTimeoutInput.value = cam ? String(this.normalizeSerialTimeoutMs(cam.serialPhotoelectricTimeoutMs)) : '';
        }
        if (ignoreSerialBusyInput) {
            ignoreSerialBusyInput.checked = cam?.ignoreSerialPhotoelectricTriggerWhileBusy !== false;
            ignoreSerialBusyInput.disabled = !cam;
        }
        if (frameRateInput) {
            frameRateInput.value = cam ? String(this.normalizeCameraTargetFrameRate(cam.targetFrameRateFps)) : '';
        }
        this.updateSerialPhotoelectricPortHint();
        this.syncCameraFrameRateInputState(cam?.triggerMode, !cam);
        this.syncCameraTriggerSourceInputState(cam, !cam);
        this.syncCameraHardwareTriggerSourceInputState(cam, !cam);

        const saveBtn = this.container.querySelector('#btn-save-camera-params');
        if (saveBtn) {
            saveBtn.disabled = !cam;
        }

        const previewBtn = this.container.querySelector('#btn-camera-preview');
        if (previewBtn) {
            previewBtn.disabled = !cam;
            previewBtn.title = cam ? `预览 ${cam.displayName || cam.serialNumber || cam.id}` : '请先在列表中选择一台相机';
        }

        const calibBtn = this.container.querySelector('#btn-hand-eye-calib');
        if (calibBtn) {
            calibBtn.disabled = !cam;
            calibBtn.title = cam ? `对 ${cam.displayName || cam.serialNumber || cam.id} 启动二维平面标定` : '请先在列表中选择一台相机';
        }

        const selectionHint = this.container.querySelector('#camera-selection-hint');
        if (selectionHint) {
            selectionHint.textContent = cam
                ? `当前已选中：${cam.displayName || cam.serialNumber || cam.id}。像素格式：${this.getCameraPixelFormatLabel(cam.pixelFormat ?? cam.PixelFormat)}。触发方式：${this.getTriggerSourceLabel(cam)}。`
                : '请先在上方绑定列表中选择一台相机，再进行预览、二维平面标定或参数保存。';
        }
    }

    syncCameraFrameRateInputState(triggerMode = null, forceDisabled = false) {
        const frameRateInput = this.container?.querySelector('#cam-param-target-frame-rate');
        const hintEl = this.container?.querySelector('#cam-param-target-frame-rate-hint');
        if (!frameRateInput) return;

        const effectiveTriggerMode = this.normalizeCameraTriggerMode(
            triggerMode ?? this.container?.querySelector('#cam-param-trigger-mode')?.value
        );
        const disabled = forceDisabled || effectiveTriggerMode !== 'Continuous';
        frameRateInput.disabled = disabled;
        frameRateInput.readOnly = disabled;
        frameRateInput.setAttribute('aria-disabled', disabled ? 'true' : 'false');

        if (hintEl) {
            hintEl.textContent = disabled
                ? '仅 Continuous 模式下可编辑；当前值会保留。'
                : 'Continuous 模式下按该目标 fps 在应用侧节流。';
        }
    }

    syncCameraTriggerSourceInputState(cam = null, forceDisabled = false) {
        const triggerMode = this.normalizeCameraTriggerMode(
            this.container?.querySelector('#cam-param-trigger-mode')?.value ?? cam?.triggerMode
        );
        const triggerSourceSelect = this.container?.querySelector('#cam-param-software-trigger-source');
        const enterDebounceInput = this.container?.querySelector('#cam-param-enter-debounce');
        const enterTimeoutInput = this.container?.querySelector('#cam-param-enter-timeout');
        const enterDeviceInput = this.container?.querySelector('#cam-param-enter-device-id');
        const ignoreBusyInput = this.container?.querySelector('#cam-param-ignore-enter-busy');
        const serialPortInput = this.container?.querySelector('#cam-param-serial-port-name');
        const serialBaudInput = this.container?.querySelector('#cam-param-serial-baud-rate');
        const serialDebounceInput = this.container?.querySelector('#cam-param-serial-debounce');
        const serialTimeoutInput = this.container?.querySelector('#cam-param-serial-timeout');
        const ignoreSerialBusyInput = this.container?.querySelector('#cam-param-ignore-serial-busy');
        const learnBtn = this.container?.querySelector('#btn-learn-enter-trigger-device');
        const hintEl = this.container?.querySelector('#cam-param-enter-trigger-hint');

        const noCamera = forceDisabled || !this.selectedCameraBindingId;
        const canChooseSoftwareSource = !noCamera && triggerMode === 'Software';
        const selectedSource = this.normalizeSoftwareTriggerSource(triggerSourceSelect?.value ?? cam?.softwareTriggerSource);
        const enableEnterOptions = canChooseSoftwareSource && selectedSource === 'EnterPhotoelectric';
        const enableSerialOptions = canChooseSoftwareSource && selectedSource === 'SerialPhotoelectric';

        if (triggerSourceSelect) {
            triggerSourceSelect.disabled = !canChooseSoftwareSource;
        }

        [enterDebounceInput, enterTimeoutInput, enterDeviceInput].forEach(input => {
            if (!input) return;
            input.disabled = !enableEnterOptions;
            input.readOnly = !enableEnterOptions;
            input.setAttribute('aria-disabled', enableEnterOptions ? 'false' : 'true');
        });

        if (ignoreBusyInput) {
            ignoreBusyInput.disabled = !enableEnterOptions;
        }

        if (learnBtn) {
            learnBtn.disabled = !enableEnterOptions;
        }

        [serialPortInput, serialBaudInput, serialDebounceInput, serialTimeoutInput].forEach(input => {
            if (!input) return;
            input.disabled = !enableSerialOptions;
            input.readOnly = !enableSerialOptions;
            input.setAttribute('aria-disabled', enableSerialOptions ? 'false' : 'true');
        });

        if (ignoreSerialBusyInput) {
            ignoreSerialBusyInput.disabled = !enableSerialOptions;
        }

        if (hintEl) {
            if (noCamera) {
                hintEl.textContent = '请选择相机后配置触发来源。';
            } else if (triggerMode !== 'Software') {
                hintEl.textContent = '仅软件触发模式使用触发来源；相机 IO 外触发和连续采集由相机采集模式控制。';
            } else if (selectedSource === 'EnterPhotoelectric') {
                hintEl.textContent = '等待 USB 回车光电发送回车键后执行一次软件采图。';
            } else if (selectedSource === 'SerialPhotoelectric') {
                hintEl.textContent = '等待串口光电遮挡帧 01 11 后执行一次软件采图。';
            } else {
                hintEl.textContent = '普通软件触发由预览按钮、接口或流程运行请求发起。';
            }
        }
    }

    syncCameraHardwareTriggerSourceInputState(cam = null, forceDisabled = false) {
        const triggerMode = this.normalizeCameraTriggerMode(
            this.container?.querySelector('#cam-param-trigger-mode')?.value ?? cam?.triggerMode
        );
        const sourceSelect = this.container?.querySelector('#cam-param-hardware-trigger-source');
        const hintEl = this.container?.querySelector('#cam-param-hardware-trigger-source-hint');
        const noCamera = forceDisabled || !this.selectedCameraBindingId;
        const enabled = !noCamera && triggerMode === 'External';

        if (sourceSelect) {
            sourceSelect.disabled = !enabled;
            sourceSelect.setAttribute('aria-disabled', enabled ? 'false' : 'true');
        }

        if (hintEl) {
            hintEl.textContent = enabled
                ? 'External 模式下写入相机 SDK 的 TriggerSource。'
                : '仅 External 模式下生效；当前值会保留。';
        }
    }

    async learnEnterPhotoelectricDevice() {
        if (!this.selectedCameraBindingId) {
            showToast('请先选择一台相机', 'warning');
            return;
        }

        const button = this.container?.querySelector('#btn-learn-enter-trigger-device');
        const deviceInput = this.container?.querySelector('#cam-param-enter-device-id');
        const previousText = button?.textContent;
        if (button) {
            button.disabled = true;
            button.textContent = '等待回车...';
        }

        try {
            showToast('请触发一次 USB 回车光电传感器', 'info');
            const result = await httpClient.post('/trigger-input/learn-enter-device', { timeoutMs: 10000 });
            const deviceId = result?.deviceId || result?.DeviceId || '';
            if (!deviceId) {
                throw new Error('未获取到设备标识');
            }

            if (deviceInput) {
                deviceInput.value = deviceId;
            }

            const binding = this.cameraBindings.find(b => b.id === this.selectedCameraBindingId);
            if (binding) {
                binding.enterPhotoelectricDeviceId = deviceId;
            }

            showToast('已学习回车光电设备', 'success');
        } catch (error) {
            showToast('学习设备失败: ' + (error?.message || error), 'error');
        } finally {
            if (button) {
                button.textContent = previousText || '学习 USB 设备';
                this.syncCameraTriggerSourceInputState();
            }
        }
    }

    async testSerialPhotoelectricTrigger() {
        const button = this.container?.querySelector('#btn-test-serial-photoelectric');
        const portInput = this.container?.querySelector('#cam-param-serial-port-name');
        const baudInput = this.container?.querySelector('#cam-param-serial-baud-rate');
        const debounceInput = this.container?.querySelector('#cam-param-serial-debounce');
        const timeoutInput = this.container?.querySelector('#cam-param-serial-timeout');

        let portName = String(portInput?.value || '').trim().toUpperCase();
        if (!portName) {
            await this.loadSerialPhotoelectricPorts({ silent: true, applyRecommended: true });
            portName = String(portInput?.value || '').trim().toUpperCase();
        }

        if (!portName) {
            const prompted = window.prompt('请输入串口光电串口号', 'COM3');
            portName = String(prompted || '').trim().toUpperCase();
        }

        if (!/^COM\d+$/i.test(portName)) {
            showToast('串口号格式需要类似 COM3', 'warning');
            return;
        }

        const baudRate = this.normalizeSerialBaudRate(baudInput?.value);
        const debounceMs = this.normalizeSerialDebounceMs(debounceInput?.value);
        const rawTimeoutMs = Number.parseInt(String(timeoutInput?.value ?? ''), 10);
        const timeoutMs = Number.isFinite(rawTimeoutMs) && rawTimeoutMs > 0
            ? this.normalizeSerialTimeoutMs(rawTimeoutMs)
            : 10000;
        const previousText = button?.textContent;

        if (button) {
            button.disabled = true;
            button.textContent = '等待遮挡...';
        }

        try {
            showToast(`正在监听 ${portName} 串口光电，请遮挡一次传感器`, 'info');
            const result = await httpClient.post('/trigger-input/test-serial-photoelectric', {
                portName,
                baudRate,
                debounceMs,
                timeoutMs
            });
            const resultPort = result?.portName || result?.PortName || portName;
            showToast(`串口光电测试成功: ${resultPort} 收到遮挡帧 01 11`, 'success');
        } catch (error) {
            showToast('串口光电测试失败: ' + (error?.message || error), 'error');
        } finally {
            if (button) {
                button.textContent = previousText || 'Toast 测试';
                button.disabled = false;
            }
        }
    }

    getSelectedCameraBinding() {
        if (!this.selectedCameraBindingId) {
            return null;
        }

        return this.cameraBindings.find(binding => binding.id === this.selectedCameraBindingId) || null;
    }

    async startContinuousPreviewSession(cameraBindingId) {
        return await httpClient.post('/cameras/continuous-preview/start', {
            cameraBindingId
        });
    }

    async stopContinuousPreviewSession(sessionId) {
        if (!sessionId) return;
        try {
            await httpClient.post('/cameras/continuous-preview/stop', { sessionId });
        } catch (error) {
            console.warn('[SettingsView] Failed to stop continuous preview session:', error);
        }
    }

    async fetchContinuousPreviewFrame(sessionId, options = {}) {
        const cacheKey = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
        const { blob, headers } = await httpClient.getForBlob(`/cameras/continuous-preview/frame/${encodeURIComponent(sessionId)}?_=${cacheKey}`, {
            signal: options.signal
        });
        if (!blob || blob.size === 0) {
            throw new Error('连续预览未返回图像数据');
        }

        const imageUrl = URL.createObjectURL(blob);
        const widthHeader = headers.get('X-Image-Width');
        const heightHeader = headers.get('X-Image-Height');
        const sequenceHeader = headers.get('X-Frame-Sequence');
        const parsedWidth = widthHeader ? Number(widthHeader) : null;
        const parsedHeight = heightHeader ? Number(heightHeader) : null;
        const parsedSequence = sequenceHeader ? Number(sequenceHeader) : null;

        return {
            imageUrl,
            width: Number.isFinite(parsedWidth) ? parsedWidth : null,
            height: Number.isFinite(parsedHeight) ? parsedHeight : null,
            sequence: Number.isFinite(parsedSequence) ? parsedSequence : null
        };
    }

    async captureSharedFrame(cameraBindingId, options = {}) {
        const session = await this.startContinuousPreviewSession(cameraBindingId);
        try {
            const preview = await this.fetchContinuousPreviewFrame(session.sessionId || session.SessionId, {
                signal: options.signal
            });
            return {
                ...preview,
                triggerMode: this.normalizeCameraTriggerMode(session.triggerMode || session.TriggerMode),
                cameraBindingId
            };
        } finally {
            await this.stopContinuousPreviewSession(session.sessionId || session.SessionId);
        }
    }

    async captureCameraPreview(cameraBindingId = this.selectedCameraBindingId, options = {}) {
        if (!cameraBindingId) {
            throw new Error('请先在相机管理中选择一台相机');
        }

        const binding = this.cameraBindings.find(item => item.id === cameraBindingId) || this.getSelectedCameraBinding();
        const triggerMode = this.normalizeCameraTriggerMode(binding?.triggerMode);
        if (triggerMode === 'Continuous' || triggerMode === 'External') {
            return await this.captureSharedFrame(cameraBindingId, options);
        }

        const request = {
            cameraBindingId
        };
        if (options.acceptPendingEnterSignalAfterUtc) {
            request.acceptPendingEnterSignalAfterUtc = options.acceptPendingEnterSignalAfterUtc;
        }

        const { blob, headers } = await httpClient.postForBlob('/cameras/soft-trigger-capture', request, {
            signal: options.signal
        });

        if (!blob || blob.size === 0) {
            throw new Error('预览接口未返回图像数据');
        }

        const imageUrl = URL.createObjectURL(blob);
        const widthHeader = headers.get('X-Image-Width');
        const heightHeader = headers.get('X-Image-Height');
        const parsedWidth = widthHeader ? Number(widthHeader) : null;
        const parsedHeight = heightHeader ? Number(heightHeader) : null;

        return {
            imageUrl,
            cameraBindingId: headers.get('X-Camera-Id') || cameraBindingId,
            triggerMode: headers.get('X-Trigger-Mode') || 'Software',
            triggerSource: headers.get('X-Trigger-Source') || binding?.softwareTriggerSource || 'Manual',
            width: Number.isFinite(parsedWidth) ? parsedWidth : null,
            height: Number.isFinite(parsedHeight) ? parsedHeight : null
        };
    }

    async showContinuousCameraPreview(binding) {
        let currentPreviewUrl = null;
        let sessionId = null;
        let previewActive = false;
        let previewLoopToken = 0;
        let activePreviewAbortController = null;
        const triggerMode = this.normalizeCameraTriggerMode(binding?.triggerMode);
        const triggerModeLabel = triggerMode === 'External' ? 'External' : 'Continuous';
        const startupText = triggerMode === 'External'
            ? '正在启动外触发预览，等待相机 IO 触发...'
            : '正在启动连续预览...';
        const stoppedText = triggerMode === 'External'
            ? '外触发预览已停止'
            : '连续预览已停止';

        const content = document.createElement('div');
        const bindingLabel = this.escapeHtml(binding.displayName || binding.serialNumber || binding.id);
        content.innerHTML = `
            <div style="display:flex; flex-direction:column; gap:16px;">
                <div style="display:flex; justify-content:space-between; align-items:center; gap:12px;">
                    <div style="font-size:13px; color:var(--text-muted);">
                        当前相机: <strong style="color:var(--text-primary);">${bindingLabel}</strong>
                    </div>
                    <button class="cv-btn cv-btn-secondary" id="btn-toggle-camera-preview" type="button">停止预览</button>
                </div>
                <div style="background:#020617; border:1px solid var(--border-color); border-radius:12px; min-height:420px; display:flex; align-items:center; justify-content:center; overflow:hidden;">
                    <img id="camera-preview-image" alt="相机预览" style="max-width:100%; max-height:420px; display:none; object-fit:contain;">
                    <div id="camera-preview-placeholder" style="color:#94a3b8; font-size:14px; text-align:center; padding:24px;">${startupText}</div>
                </div>
                <div id="camera-preview-meta" style="font-size:13px; color:var(--text-muted); min-height:20px;"></div>
            </div>
        `;

        const cleanupPreviewUrl = () => {
            if (currentPreviewUrl) {
                URL.revokeObjectURL(currentPreviewUrl);
                currentPreviewUrl = null;
            }
        };

        const cancelActivePreviewRequest = () => {
            if (!activePreviewAbortController) {
                return;
            }

            activePreviewAbortController.abort();
            activePreviewAbortController = null;
        };

        const stopPreview = async () => {
            previewActive = false;
            previewLoopToken += 1;
            cancelActivePreviewRequest();
            await this.stopContinuousPreviewSession(sessionId);
            sessionId = null;
        };

        const modal = createModal({
            title: `相机预览 - ${binding.displayName || binding.serialNumber || binding.id}`,
            content,
            width: '960px',
            onClose: async () => {
                await stopPreview();
                cleanupPreviewUrl();
            }
        });
        modal.querySelector('.cv-modal')?.style.setProperty('max-width', '95vw');

        const toggleBtn = content.querySelector('#btn-toggle-camera-preview');
        const imageEl = content.querySelector('#camera-preview-image');
        const placeholderEl = content.querySelector('#camera-preview-placeholder');
        const metaEl = content.querySelector('#camera-preview-meta');

        const renderFrame = preview => {
            cleanupPreviewUrl();
            currentPreviewUrl = preview.imageUrl;
            imageEl.src = preview.imageUrl;
            imageEl.style.display = 'block';
            placeholderEl.style.display = 'none';
            metaEl.textContent = `触发模式: ${triggerModeLabel} · 分辨率: ${preview.width ?? '--'} x ${preview.height ?? '--'}${preview.sequence ? ` · 序号: ${preview.sequence}` : ''}`;
        };

        const startPreview = async () => {
            if (previewActive) return;
            previewActive = true;
            const loopToken = ++previewLoopToken;
            toggleBtn.textContent = '停止预览';
            placeholderEl.style.display = 'block';
            placeholderEl.textContent = startupText;
            imageEl.style.display = 'none';

            try {
                const session = await this.startContinuousPreviewSession(binding.id);
                sessionId = session.sessionId || session.SessionId;

                while (previewActive && sessionId && loopToken === previewLoopToken) {
                    const abortController = typeof AbortController !== 'undefined'
                        ? new AbortController()
                        : null;
                    activePreviewAbortController = abortController;
                    const preview = await this.fetchContinuousPreviewFrame(sessionId, {
                        signal: abortController?.signal
                    });
                    if (activePreviewAbortController === abortController) {
                        activePreviewAbortController = null;
                    }
                    if (!previewActive || loopToken !== previewLoopToken) {
                        URL.revokeObjectURL(preview.imageUrl);
                        break;
                    }

                    renderFrame(preview);
                }
            } catch (error) {
                if (error?.name === 'AbortError') {
                    return;
                }

                await this.stopContinuousPreviewSession(sessionId);
                sessionId = null;
                placeholderEl.style.display = 'block';
                placeholderEl.textContent = `${triggerModeLabel} 预览加载失败: ${error.message}`;
                metaEl.textContent = '';
                imageEl.style.display = 'none';
                previewActive = false;
                toggleBtn.textContent = '继续预览';
            } finally {
                activePreviewAbortController = null;
            }
        };

        toggleBtn.addEventListener('click', async () => {
            if (previewActive) {
                await stopPreview();
                toggleBtn.textContent = '继续预览';
                placeholderEl.style.display = 'block';
                placeholderEl.textContent = stoppedText;
                imageEl.style.display = currentPreviewUrl ? 'block' : 'none';
                return;
            }

            await startPreview();
        });

        await startPreview();
    }

    async showSelectedCameraPreview() {
        let binding = this.getSelectedCameraBinding();
        if (!binding) {
            showToast('请先在相机管理中选择一台相机，再打开相机预览', 'warning');
            return;
        }

        const saved = await this.saveSelectedCameraParameters({ silent: true });
        if (!saved) {
            if (this.lastCameraBindingSaveError) {
                showToast('保存相机参数失败: ' + this.lastCameraBindingSaveError, 'error');
            }

            return;
        }

        binding = this.getSelectedCameraBinding() || binding;
        const triggerMode = this.normalizeCameraTriggerMode(binding.triggerMode);
        if (triggerMode === 'Continuous' || triggerMode === 'External') {
            await this.showContinuousCameraPreview(binding);
            return;
        }

        const isEnterPhotoelectric = this.isEnterPhotoelectricBinding(binding);
        const isSerialPhotoelectric = this.isSerialPhotoelectricBinding(binding);
        const isPhotoelectricTrigger = isEnterPhotoelectric || isSerialPhotoelectric;
        const photoelectricLabel = isSerialPhotoelectric ? '串口光电触发' : '回车光电触发';
        let currentPreviewUrl = null;
        let currentPreviewMetaText = '';
        let previewClosed = false;
        let previewLoading = false;
        let previewRequestId = 0;
        let autoRearmTimer = null;
        let activePreviewAbortController = null;
        const content = document.createElement('div');
        const bindingLabel = this.escapeHtml(binding.displayName || binding.serialNumber || binding.id);
        content.innerHTML = `
            <div style="display:flex; flex-direction:column; gap:16px;">
                <div style="display:flex; justify-content:space-between; align-items:center; gap:12px;">
                    <div style="font-size:13px; color:var(--text-muted);">
                        当前相机: <strong style="color:var(--text-primary);">${bindingLabel}</strong>
                    </div>
                    <button class="cv-btn cv-btn-secondary" id="btn-refresh-camera-preview" type="button">${isPhotoelectricTrigger ? '重新布防' : '刷新预览'}</button>
                </div>
                <div id="camera-preview-surface" tabindex="-1" style="background:#020617; border:1px solid var(--border-color); border-radius:12px; min-height:420px; display:flex; align-items:center; justify-content:center; overflow:hidden; outline:none;">
                    <img id="camera-preview-image" alt="相机预览" style="max-width:100%; max-height:420px; display:none; object-fit:contain;">
                    <div id="camera-preview-placeholder" style="color:#94a3b8; font-size:14px; text-align:center; padding:24px;">正在加载相机预览...</div>
                </div>
                <div id="camera-preview-meta" style="font-size:13px; color:var(--text-muted); min-height:20px;"></div>
            </div>
        `;

        const cleanupPreviewUrl = () => {
            if (currentPreviewUrl) {
                URL.revokeObjectURL(currentPreviewUrl);
                currentPreviewUrl = null;
            }
        };

        const cancelActivePreviewRequest = () => {
            if (!activePreviewAbortController) {
                return;
            }

            activePreviewAbortController.abort();
            activePreviewAbortController = null;
        };

        const modal = createModal({
            title: `相机预览 - ${binding.displayName || binding.serialNumber || binding.id}`,
            content,
            width: '960px',
            onClose: () => {
                previewClosed = true;
                previewRequestId += 1;
                cancelActivePreviewRequest();
                if (autoRearmTimer) {
                    clearTimeout(autoRearmTimer);
                    autoRearmTimer = null;
                }
                cleanupPreviewUrl();
            }
        });
        modal.querySelector('.cv-modal')?.style.setProperty('max-width', '95vw');

        const refreshBtn = content.querySelector('#btn-refresh-camera-preview');
        const previewSurfaceEl = content.querySelector('#camera-preview-surface');
        const imageEl = content.querySelector('#camera-preview-image');
        const placeholderEl = content.querySelector('#camera-preview-placeholder');
        const metaEl = content.querySelector('#camera-preview-meta');

        const focusPreviewSurface = () => {
            previewSurfaceEl?.focus({ preventScroll: true });
        };

        if (isEnterPhotoelectric) {
            content.addEventListener('keydown', event => {
                if (event.key !== 'Enter') {
                    return;
                }

                const target = event.target;
                if (target instanceof HTMLElement &&
                    target.closest('input, textarea, select, [contenteditable="true"]')) {
                    return;
                }

                event.preventDefault();
                event.stopPropagation();
                focusPreviewSurface();
            }, true);
        }

        const buildPreviewMetaText = (preview, waitingForNext = false) => {
            const source = this.normalizeSoftwareTriggerSource(preview.triggerSource);
            const triggerLabel = source === 'EnterPhotoelectric'
                ? '回车光电触发'
                : source === 'SerialPhotoelectric'
                    ? '串口光电触发'
                    : preview.triggerMode;
            const waitSuffix = waitingForNext ? ` · 等待下一次${triggerLabel}...` : '';
            return `触发方式: ${triggerLabel} · 分辨率: ${preview.width ?? '--'} x ${preview.height ?? '--'}${waitSuffix}`;
        };

        const showWaitingState = () => {
            refreshBtn.disabled = true;
            refreshBtn.textContent = isPhotoelectricTrigger ? '等待触发中...' : '加载中...';

            if (isPhotoelectricTrigger && currentPreviewUrl) {
                placeholderEl.style.display = 'none';
                imageEl.style.display = 'block';
                metaEl.textContent = currentPreviewMetaText
                    ? `${currentPreviewMetaText} · 等待下一次${photoelectricLabel}...`
                    : `等待下一次${photoelectricLabel}...`;
                return;
            }

            placeholderEl.style.display = 'block';
            placeholderEl.textContent = isPhotoelectricTrigger
                ? `等待${photoelectricLabel}...`
                : '正在加载相机预览...';
            imageEl.style.display = 'none';
        };

        const queueAutoRearm = () => {
            if (!isPhotoelectricTrigger || previewClosed) {
                return;
            }

            if (autoRearmTimer) {
                clearTimeout(autoRearmTimer);
            }

            autoRearmTimer = setTimeout(() => {
                autoRearmTimer = null;
                void loadPreview();
            }, 0);
        };

        const loadPreview = async () => {
            if (previewClosed || previewLoading) {
                return;
            }

            previewLoading = true;
            const requestId = ++previewRequestId;
            const acceptPendingEnterSignalAfterUtc = isPhotoelectricTrigger
                ? new Date().toISOString()
                : null;
            focusPreviewSurface();
            showWaitingState();
            let shouldAutoRearm = false;
            const abortController = typeof AbortController !== 'undefined'
                ? new AbortController()
                : null;
            activePreviewAbortController = abortController;

            try {
                const preview = await this.captureCameraPreview(binding.id, {
                    acceptPendingEnterSignalAfterUtc,
                    signal: abortController?.signal
                });
                if (previewClosed || requestId !== previewRequestId) {
                    URL.revokeObjectURL(preview.imageUrl);
                    return;
                }

                cleanupPreviewUrl();
                currentPreviewUrl = preview.imageUrl;
                currentPreviewMetaText = buildPreviewMetaText(preview);
                imageEl.src = preview.imageUrl;
                imageEl.style.display = 'block';
                placeholderEl.style.display = 'none';
                metaEl.textContent = currentPreviewMetaText;
                shouldAutoRearm = isPhotoelectricTrigger;
            } catch (error) {
                if (error?.name === 'AbortError') {
                    return;
                }

                if (previewClosed || requestId !== previewRequestId) {
                    return;
                }

                if (currentPreviewUrl) {
                    placeholderEl.style.display = 'none';
                    imageEl.style.display = 'block';
                    metaEl.textContent = `${currentPreviewMetaText} · 布防失败: ${error.message}`;
                } else {
                    placeholderEl.style.display = 'block';
                    placeholderEl.textContent = `相机预览加载失败: ${error.message}`;
                    imageEl.style.display = 'none';
                    metaEl.textContent = '';
                }
            } finally {
                if (requestId === previewRequestId) {
                    previewLoading = false;
                    refreshBtn.disabled = false;
                    refreshBtn.textContent = isPhotoelectricTrigger ? '重新布防' : '刷新预览';
                }

                if (activePreviewAbortController === abortController) {
                    activePreviewAbortController = null;
                }

                if (shouldAutoRearm) {
                    queueAutoRearm();
                }
            }
        };

        refreshBtn.addEventListener('click', event => {
            event.preventDefault();
            focusPreviewSurface();
            void loadPreview();
        });

        focusPreviewSurface();
        void loadPreview();
    }

    async saveSelectedCameraParameters({ silent = false } = {}) {
        if (!this.selectedCameraBindingId) {
            showToast('请先在上方绑定列表中选择一台相机', 'warning');
            return false;
        }

        const binding = this.cameraBindings.find(b => b.id === this.selectedCameraBindingId);
        if (!binding) {
            showToast('未找到选中的相机绑定', 'error');
            return false;
        }

        const exposureInput = this.container.querySelector('#cam-param-exposure');
        const gainInput = this.container.querySelector('#cam-param-gain');
        const pixelFormatSelect = this.container.querySelector('#cam-param-pixel-format');
        const triggerModeSelect = this.container.querySelector('#cam-param-trigger-mode');
        const hardwareTriggerSourceSelect = this.container.querySelector('#cam-param-hardware-trigger-source');
        const triggerSourceSelect = this.container.querySelector('#cam-param-software-trigger-source');
        const enterDebounceInput = this.container.querySelector('#cam-param-enter-debounce');
        const enterTimeoutInput = this.container.querySelector('#cam-param-enter-timeout');
        const enterDeviceInput = this.container.querySelector('#cam-param-enter-device-id');
        const ignoreBusyInput = this.container.querySelector('#cam-param-ignore-enter-busy');
        const serialPortInput = this.container.querySelector('#cam-param-serial-port-name');
        const serialBaudInput = this.container.querySelector('#cam-param-serial-baud-rate');
        const serialDebounceInput = this.container.querySelector('#cam-param-serial-debounce');
        const serialTimeoutInput = this.container.querySelector('#cam-param-serial-timeout');
        const ignoreSerialBusyInput = this.container.querySelector('#cam-param-ignore-serial-busy');
        const frameRateInput = this.container.querySelector('#cam-param-target-frame-rate');
        if (!exposureInput || !gainInput || !pixelFormatSelect || !triggerModeSelect || !hardwareTriggerSourceSelect || !triggerSourceSelect || !enterDebounceInput || !enterTimeoutInput || !enterDeviceInput || !ignoreBusyInput || !serialPortInput || !serialBaudInput || !serialDebounceInput || !serialTimeoutInput || !ignoreSerialBusyInput || !frameRateInput) {
            showToast('参数面板控件缺失，请刷新后重试', 'error');
            return false;
        }

        const exposureTimeUs = Number.parseFloat(exposureInput.value);
        const gainDb = Number.parseFloat(gainInput.value);
        const pixelFormat = this.normalizeCameraPixelFormat(pixelFormatSelect.value || 'Mono8');
        const triggerMode = this.normalizeCameraTriggerMode(triggerModeSelect.value || 'Software');
        const hardwareTriggerSource = this.normalizeHardwareTriggerSource(hardwareTriggerSourceSelect.value || 'Line0');
        const softwareTriggerSource = this.normalizeSoftwareTriggerSource(triggerSourceSelect.value || 'Manual');
        const enterPhotoelectricDebounceMs = this.normalizeEnterDebounceMs(enterDebounceInput.value);
        const enterPhotoelectricTimeoutMs = this.normalizeEnterTimeoutMs(enterTimeoutInput.value);
        const enterPhotoelectricDeviceId = String(enterDeviceInput.value || '').trim();
        const ignoreEnterTriggerWhileBusy = ignoreBusyInput.checked !== false;
        const serialPhotoelectricPortName = String(serialPortInput.value || '').trim();
        const serialPhotoelectricBaudRate = this.normalizeSerialBaudRate(serialBaudInput.value);
        const serialPhotoelectricDebounceMs = this.normalizeSerialDebounceMs(serialDebounceInput.value);
        const serialPhotoelectricTimeoutMs = this.normalizeSerialTimeoutMs(serialTimeoutInput.value);
        const ignoreSerialPhotoelectricTriggerWhileBusy = ignoreSerialBusyInput.checked !== false;
        const targetFrameRateFps = this.normalizeCameraTargetFrameRate(frameRateInput.value);

        if (!Number.isFinite(exposureTimeUs) || exposureTimeUs < 10 || exposureTimeUs > 1000000) {
            showToast('曝光时间需在 10 - 1000000 µs 范围内', 'warning');
            return false;
        }
        if (!Number.isFinite(gainDb) || gainDb < 0 || gainDb > 24) {
            showToast('增益需在 0.0 - 24.0 dB 范围内', 'warning');
            return false;
        }

        binding.exposureTimeUs = exposureTimeUs;
        binding.gainDb = gainDb;
        binding.pixelFormat = pixelFormat;
        binding.triggerMode = triggerMode;
        binding.hardwareTriggerSource = hardwareTriggerSource;
        binding.softwareTriggerSource = softwareTriggerSource;
        binding.enterPhotoelectricDebounceMs = enterPhotoelectricDebounceMs;
        binding.enterPhotoelectricTimeoutMs = enterPhotoelectricTimeoutMs;
        binding.enterPhotoelectricDeviceId = enterPhotoelectricDeviceId;
        binding.ignoreEnterTriggerWhileBusy = ignoreEnterTriggerWhileBusy;
        binding.serialPhotoelectricPortName = serialPhotoelectricPortName;
        binding.serialPhotoelectricBaudRate = serialPhotoelectricBaudRate;
        binding.serialPhotoelectricDebounceMs = serialPhotoelectricDebounceMs;
        binding.serialPhotoelectricTimeoutMs = serialPhotoelectricTimeoutMs;
        binding.ignoreSerialPhotoelectricTriggerWhileBusy = ignoreSerialPhotoelectricTriggerWhileBusy;
        binding.targetFrameRateFps = targetFrameRateFps;

        const saved = await this.saveCameraBindings({ silent });
        if (!saved) {
            return false;
        }

        this.refreshCameraTable();
        const selectedRow = this.container.querySelector(`#camera-bindings-table tr.camera-row[data-id="${this.selectedCameraBindingId}"]`);
        if (selectedRow) {
            this.selectCameraRow(selectedRow);
        }
        if (!silent) {
            showToast(`已保存相机参数: ${binding.displayName || binding.serialNumber}`, 'success');
        }

        return true;
    }

    // （演示用空壳。需要配合 Modal使用，这里只挂载入口）
    // ----- UI 渲染块（为了测试顺利，简化版） -----
    getDefaultConfig() {
        return {
            general: { softwareTitle: 'ClearVision', theme: 'dark', autoStart: false },
            communication: {
                activeProtocol: 'S7',
                heartbeatIntervalMs: 1000,
                s7: {
                    ipAddress: '192.168.0.1',
                    port: 102,
                    cpuType: 'S7-1200',
                    rack: 0,
                    slot: 1,
                    mappings: []
                },
                mc: {
                    ipAddress: '192.168.3.1',
                    port: 5002,
                    mappings: []
                },
                fins: {
                    ipAddress: '192.168.250.1',
                    port: 9600,
                    mappings: []
                }
            },
            storage: { imageSavePath: 'D:\\VisionData\\Images', savePolicy: 'NgOnly', retentionDays: 30, minFreeSpaceGb: 5 },
            runtime: {
                autoRun: false,
                stopOnConsecutiveNg: 0,
                missingMaterialTimeoutSeconds: 120,
                applyProtectionRules: true
            },
            security: {
                passwordMinLength: 6,
                sessionTimeoutMinutes: 30,
                loginFailureLockoutCount: 5
            },
            cameras: [],
            activeCameraId: ''
        };
    }

    renderGeneralTab() {
        const general = this.config?.general || this.getDefaultConfig().general;
        const security = this.config?.security || this.getDefaultConfig().security;
        const runtimeTheme = normalizeTheme(general.theme, getAppliedTheme());
        const settingsResetFeature = getFeatureMeta('settings.reset');
        return `
            <div class="settings-section-title">
                <h2>常规设置</h2>
                <p>配置系统层面的基础选项，包括界面显示和启动行为。</p>
            </div>
            
            <div class="settings-modern-card">
                <div class="settings-card-header">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M19.43 12.98c.04-.32.07-.64.07-.98s-.03-.66-.07-.98l2.11-1.65c.19-.15.24-.42.12-.64l-2-3.46c-.12-.22-.39-.3-.61-.22l-2.49 1c-.52-.4-1.08-.73-1.69-.98l-.38-2.65C14.46 2.18 14.25 2 14 2h-4c-.25 0-.46.18-.49.42l-.38 2.65c-.61.25-1.17.59-1.69.98l-2.49-1c-.23-.09-.49 0-.61.22l-2 3.46c-.13.22-.07.49-.12-.64l2.11 1.65c-.04.32-.07.65-.07.98s.03.66.07.98l-2.11 1.65c-.19.15-.24.42-.12.64l2 3.46c.12.22.39.3.61.22l2.49-1c.52.4 1.08.73 1.69.98l.38 2.65c.03.24.24.42.49.42h4c.25 0 .46-.18.49-.42l.38-2.65c.61-.25 1.17-.59 1.69-.98l2.49 1c.23.09.49 0 .61-.22l2-3.46c.12-.22.07-.49-.12-.64l-2.11-1.65zM12 15.5c-1.93 0-3.5-1.57-3.5-3.5s1.57-3.5 3.5-3.5 3.5 1.57 3.5 3.5-1.57 3.5-3.5 3.5z"/></svg>
                        <span>基础配置</span>
                    </div>
                </div>
                <div class="settings-card-body">
                    <div class="settings-fieldset" style="max-width: 400px;">
                        <label>软件标题 (Software Title)</label>
                        <div class="input-with-icon">
                            <svg class="input-icon" viewBox="0 0 24 24" style="fill:#94a3b8;"><path d="M5 4v3h5.5v12h3V7H19V4z"/></svg>
                            <input type="text" class="cv-input" id="cfg-softwareTitle" value="${general.softwareTitle || ''}" placeholder="ClearVision">
                        </div>
                        <span class="settings-field-hint">将显示在系统顶部的全局标题栏上</span>
                    </div>
                    
                    <div style="margin-top:24px; display:flex; gap:24px;">
                        <div class="settings-fieldset" style="flex:1;">
                            <label>系统主题 (Theme)</label>
                            <select class="cv-input" id="cfg-theme">
                                <option value="light" ${runtimeTheme === 'light' ? 'selected' : ''}>浅色主题 (Light)</option>
                                <option value="dark" ${runtimeTheme === 'dark' ? 'selected' : ''}>深色主题 (Dark)</option>
                            </select>
                        </div>
                        <div class="settings-fieldset" style="flex:1; display:flex; flex-direction:column; justify-content:flex-end;">
                            <label style="display:flex; align-items:center; gap:8px; cursor:pointer; margin-bottom:12px;">
                                <input type="checkbox" id="cfg-autoStart" ${general.autoStart ? 'checked' : ''} style="width:16px; height:16px; accent-color:var(--cinnabar);">
                                开机自动启动软件
                            </label>
                        </div>
                    </div>
                </div>
            </div>

            <div class="settings-modern-card" style="margin-top:24px;">
                <div class="settings-card-header">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z"/></svg>
                        <span>账号与安全</span>
                    </div>
                </div>
                <div class="settings-card-body">
                    <div style="display:flex; gap:24px; flex-wrap:wrap;">
                        <div class="settings-fieldset" style="flex:1; min-width:220px;">
                            <label>当前密码</label>
                            <input type="password" class="cv-input" id="cfg-current-password" autocomplete="current-password" placeholder="请输入当前密码">
                        </div>
                        <div class="settings-fieldset" style="flex:1; min-width:220px;">
                            <label>新密码</label>
                            <input type="password" class="cv-input" id="cfg-new-password" autocomplete="new-password" placeholder="至少 ${security.passwordMinLength || 6} 位">
                        </div>
                        <div class="settings-fieldset" style="flex:1; min-width:220px;">
                            <label>确认新密码</label>
                            <input type="password" class="cv-input" id="cfg-confirm-password" autocomplete="new-password" placeholder="请再次输入新密码">
                        </div>
                    </div>
                    <div style="display:flex; justify-content:space-between; align-items:center; gap:16px; margin-top:20px; flex-wrap:wrap;">
                        <span class="settings-field-hint">当前密码策略：最少 ${security.passwordMinLength || 6} 位。</span>
                        <div style="display:flex; gap:12px; flex-wrap:wrap;">
                            <button class="cv-btn settings-btn-light" id="btn-reset-settings" title="${settingsResetFeature.title}">${getFeatureButtonLabel('settings.reset', '恢复默认设置')}</button>
                            <button class="cv-btn settings-btn-danger" id="btn-change-password">修改密码</button>
                        </div>
                    </div>
                    <div style="margin-top:12px;">
                        <span class="settings-field-hint">${settingsResetFeature.description}</span>
                    </div>
                </div>
            </div>
        `;
    }

    renderCommunicationTab() {
        const comm = this.normalizeCommunicationConfig(this.config?.communication);
        const activeProtocol = this.normalizePlcProtocol(comm.activeProtocol);
        const profileKey = this.getPlcProfileKey(activeProtocol);
        const profile = comm[profileKey];
        const badgeMeta = this.getPlcConnectionBadgeMeta(this.plcConnectionStatus);
        const connectionErrors = {
            ipAddress: this.getPlcFieldErrors('connection', 'ipAddress'),
            port: this.getPlcFieldErrors('connection', 'port'),
            cpuType: this.getPlcFieldErrors('connection', 'cpuType'),
            rack: this.getPlcFieldErrors('connection', 'rack'),
            slot: this.getPlcFieldErrors('connection', 'slot')
        };
        const activeErrors = this.getCurrentProtocolValidationErrors();
        const protocolLabel = activeProtocol === 'MC'
            ? '三菱 MC'
            : activeProtocol === 'FINS'
                ? '欧姆龙 FINS'
                : '西门子 S7';
        const addressPlaceholder = activeProtocol === 'MC'
            ? '如 D100 / X10 / M200'
            : activeProtocol === 'FINS'
                ? '如 DM100 / CIO10.3'
                : '如 DB1.DBX0.0 / MW100';
        const protocolHint = activeProtocol === 'MC'
            ? '使用 Mitsubishi MC 协议与 FX/Q/iQ 系列 PLC 通讯。'
            : activeProtocol === 'FINS'
                ? '使用 Omron FINS/TCP 与 CP/CJ/NJ/NX 系列 PLC 通讯。'
                : '使用 Siemens S7 协议与 S7-1200/1500 等 PLC 通讯。';

        return `
            <div class="settings-section-title">
                <h2>PLC 通讯配置</h2>
                <p>聚焦已落地的厂牌协议栈，配置连接参数与地址映射。</p>
            </div>

            <div class="settings-modern-card">
                <div class="settings-card-header has-badge">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M19 15v4H5v-4h14m1-2H4c-.55 0-1 .45-1 1v6c0 .55.45 1 1 1h16c.55 0 1-.45 1-1v-6c0-.55-.45-1-1-1zM7 18.5c-.82 0-1.5-.67-1.5-1.5s.68-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zM19 5v4H5V5h14m1-2H4c-.55 0-1 .45-1 1v6c0 .55.45 1 1 1h16c.55 0 1-.45 1-1V4c0-.55-.45-1-1-1zM7 8.5c-.82 0-1.5-.67-1.5-1.5S6.18 5.5 7 5.5s1.5.67 1.5 1.5S7.82 8.5 7 8.5z"/></svg>
                        <span>通讯连接设置</span>
                    </div>
                    <div style="display:flex; gap:8px; align-items:center;">
                        <div class="settings-status-badge ${badgeMeta.className}" id="plc-connection-badge">
                            <span class="status-dot"></span> ${badgeMeta.text}
                        </div>
                    </div>
                </div>
                
                <div class="settings-card-body">
                    <div class="settings-field-hint" style="margin-bottom: 16px;">${protocolHint}</div>
                    ${activeErrors.length > 0 ? `
                        <div class="plc-validation-summary">
                            <strong>${protocolLabel} 配置存在 ${activeErrors.length} 个问题</strong>
                            <span>请修正当前协议的连接参数或地址映射后再保存。</span>
                        </div>
                    ` : ''}
                    <div class="horizontal-flex">
                    <div class="settings-fieldset" style="flex:1.5;">
                        <label>通讯协议</label>
                        <select class="cv-input" id="cfg-protocol">
                            <option value="S7" ${activeProtocol === 'S7' ? 'selected' : ''}>Siemens S7</option>
                            <option value="MC" ${activeProtocol === 'MC' ? 'selected' : ''}>Mitsubishi MC</option>
                            <option value="FINS" ${activeProtocol === 'FINS' ? 'selected' : ''}>Omron FINS</option>
                        </select>
                    </div>
                    <div class="settings-fieldset" style="flex:2;">
                        <label>PLC IP地址</label>
                        <div class="input-with-icon">
                            <svg class="input-icon" viewBox="0 0 24 24"><path d="M4 6h16v2H4zm0 5h16v2H4zm0 5h16v2H4z"/></svg>
                            <input type="text" class="cv-input ${connectionErrors.ipAddress.length ? 'plc-invalid-input' : ''}" id="cfg-plcIpAddress" value="${this.escapeHtml(profile?.ipAddress || '')}" placeholder="192.168.0.10">
                        </div>
                        ${this.renderPlcErrorText(connectionErrors.ipAddress)}
                    </div>
                    <div class="settings-fieldset" style="flex:1;">
                        <label>端口号</label>
                        <input type="number" class="cv-input ${connectionErrors.port.length ? 'plc-invalid-input' : ''}" id="cfg-plcPort" value="${profile?.port || ''}">
                        ${this.renderPlcErrorText(connectionErrors.port)}
                    </div>
                    <div class="settings-fieldset-action">
                        <button class="cv-btn settings-btn-dark" id="btn-plc-test">
                            <svg viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>
                            连接测试
                        </button>
                    </div>
                    </div>
                    ${activeProtocol === 'S7' ? `
                        <div class="horizontal-flex" style="margin-top: 16px;">
                            <div class="settings-fieldset" style="flex:1.2;">
                                <label>CPU 类型</label>
                                <select class="cv-input ${connectionErrors.cpuType.length ? 'plc-invalid-input' : ''}" id="cfg-s7-cpuType">
                                    <option value="S7-1200" ${profile?.cpuType === 'S7-1200' ? 'selected' : ''}>S7-1200</option>
                                    <option value="S7-1500" ${profile?.cpuType === 'S7-1500' ? 'selected' : ''}>S7-1500</option>
                                    <option value="S7-300" ${profile?.cpuType === 'S7-300' ? 'selected' : ''}>S7-300</option>
                                    <option value="S7-400" ${profile?.cpuType === 'S7-400' ? 'selected' : ''}>S7-400</option>
                                    <option value="S7-200" ${profile?.cpuType === 'S7-200' ? 'selected' : ''}>S7-200</option>
                                    <option value="S7-200 Smart" ${profile?.cpuType === 'S7-200 Smart' ? 'selected' : ''}>S7-200 Smart</option>
                                </select>
                                ${this.renderPlcErrorText(connectionErrors.cpuType)}
                            </div>
                            <div class="settings-fieldset" style="flex:0.8;">
                                <label>Rack</label>
                                <input type="number" class="cv-input ${connectionErrors.rack.length ? 'plc-invalid-input' : ''}" id="cfg-s7-rack" value="${Number.isFinite(profile?.rack) ? profile.rack : 0}">
                                ${this.renderPlcErrorText(connectionErrors.rack)}
                            </div>
                            <div class="settings-fieldset" style="flex:0.8;">
                                <label>Slot</label>
                                <input type="number" class="cv-input ${connectionErrors.slot.length ? 'plc-invalid-input' : ''}" id="cfg-s7-slot" value="${Number.isFinite(profile?.slot) ? profile.slot : 1}">
                                ${this.renderPlcErrorText(connectionErrors.slot)}
                            </div>
                        </div>
                    ` : ''}
                </div>
            </div>

            <div class="settings-modern-card" style="margin-top: 24px;">
                <div class="settings-card-header">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5 14H7v-2h7v2zm3-4H7v-2h10v2zm0-4H7V7h10v2z"/></svg>
                        <span>${protocolLabel} 地址映射表</span>
                    </div>
                    <div class="settings-header-actions">
                        <button class="cv-btn settings-btn-light" style="padding: 4px 12px; margin-left: 8px;" id="btn-add-plc-mapping">
                            <span style="font-size: 16px; margin-right: 4px;">+</span> 添加变量
                        </button>
                    </div>
                </div>

                <div class="settings-card-body" style="padding-bottom: 0;">
                    <span class="settings-field-hint">地址格式示例：${addressPlaceholder}</span>
                </div>
                
                <div class="settings-card-table-wrapper">
                    <table class="settings-modern-table">
                        <thead>
                            <tr>
                                <th>变量名称</th>
                                <th>PLC地址</th>
                                <th>数据类型</th>
                                <th>读/写</th>
                                <th>注释</th>
                                <th>操作</th>
                            </tr>
                        </thead>
                        <tbody id="plc-mapping-tbody"></tbody>
                    </table>
                </div>
            </div>
            
            <div class="settings-floating-footer">
                <button class="cv-btn settings-btn-light" style="width: 100px;" id="btn-reset-plc">取消</button>
                <button class="cv-btn settings-btn-danger" style="width: 140px;" id="btn-save-plc">
                    <svg viewBox="0 0 24 24" style="width: 18px; height: 18px; margin-right: 6px; fill: currentColor;"><path d="M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46C19.54 15.03 20 13.57 20 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74C4.46 8.97 4 10.43 4 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z"/></svg> 
                    保存当前协议
                </button>
            </div>
        `;
    }

    renderStationCommunicationTab() {
        const settings = this.normalizeStationCommunicationSettings(this.stationCommunicationSettings);
        const mode = settings.mode;
        const modeMeta = this.getStationModeMeta(mode);
        const isDisabled = mode === 'Disabled';
        const isLan = mode === 'LanController';
        const isAdminDisabled = this.isAdmin ? '' : 'disabled';
        const tokenDisplay = this.stationTokenVisible && this.stationTokenValue
            ? this.stationTokenValue
            : (settings.token?.hasToken ? settings.token.mask : '未生成');
        const restartMessages = [];
        if (settings.requiresRestart?.studio) {
            restartMessages.push('Studio 需要重启后才会按新监听模式和端口启动。');
        }
        if (settings.requiresRestart?.localStation) {
            restartMessages.push('本机 Station 需要重启后才会读取新的同步地址和 token。');
        }
        if (restartMessages.length === 0) {
            restartMessages.push('当前保存值与已知运行值一致。');
        }

        const modeButton = (value) => {
            const meta = this.getStationModeMeta(value);
            const active = mode === value;
            return `
                <button type="button"
                    class="cv-btn station-mode-option"
                    data-station-mode="${value}"
                    aria-pressed="${active ? 'true' : 'false'}"
                    style="flex:1; min-width:140px; height:auto; min-height:74px; padding:12px 14px; border-radius:8px; border:1px solid ${active ? 'var(--cinnabar)' : '#cbd5e1'}; background:${active ? 'rgba(231, 76, 60, 0.08)' : '#fff'}; color:${active ? 'var(--cinnabar)' : 'var(--text-color)'}; text-align:left; display:flex; flex-direction:column; gap:6px; cursor:pointer;">
                    <strong style="font-size:14px;">${meta.label}</strong>
                    <span style="font-size:12px; line-height:1.35; color:var(--text-muted);">${meta.title}</span>
                </button>
            `;
        };

        const lanAddressOptions = settings.lanAddresses
            .map(address => `<option value="${this.escapeHtml(address)}"></option>`)
            .join('');
        const remoteSnippet = isLan
            ? [
                `StudioBaseUrl=${settings.remoteStationBaseUrl || `http://${settings.lanHost || '<LAN-IP>'}:${settings.port}`}`,
                `StudioHubUrl=${settings.remoteStationHubUrl || ''}`,
                `SharedToken=${settings.token?.hasToken ? settings.token.mask : '<generate-token>'}`
            ].join('\n')
            : '切换到“局域网总控”后显示远端 Station 连接片段。';
        const localSnippet = isDisabled
            ? '本机 Station 同步已关闭。'
            : [
                `StudioBaseUrl=${settings.localStationBaseUrl || `http://127.0.0.1:${settings.port}`}`,
                `StudioHubUrl=${settings.localStationHubUrl || ''}`,
                `SharedToken=${settings.token?.hasToken ? settings.token.mask : '<generate-token>'}`
            ].join('\n');
        const diagnosticsHtml = settings.diagnostics.length
            ? settings.diagnostics.map(item => `<li>${this.escapeHtml(item)}</li>`).join('')
            : '<li>暂无诊断提示。</li>';

        return `
            <div class="settings-section-title">
                <h2>Station 通讯设置</h2>
                <p>管理本机 Studio 与本机 Station 的同步入口，保存后按提示重启生效。</p>
            </div>

            <div class="settings-modern-card">
                <div class="settings-card-header has-badge">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M4 5h16v6H4V5zm2 2v2h12V7H6zm-2 6h7v6H4v-6zm2 2v2h3v-2H6zm7-2h7v6h-7v-6zm2 2v2h3v-2h-3z"/></svg>
                        <span>通讯模式</span>
                    </div>
                    <div class="settings-status-badge ${isDisabled ? 'status-disconnected' : 'status-connected'}" style="${isDisabled ? 'color:#64748b;' : ''}">
                        <span class="status-dot"></span> ${modeMeta.badge}
                    </div>
                </div>
                <div class="settings-card-body">
                    <div style="display:flex; gap:12px; flex-wrap:wrap; margin-bottom:22px;">
                        ${modeButton('Disabled')}
                        ${modeButton('LocalLoopback')}
                        ${modeButton('LanController')}
                    </div>
                    <div class="horizontal-flex" style="padding:0; align-items:flex-start; flex-wrap:wrap;">
                        <div class="settings-fieldset" style="flex:1; min-width:180px;">
                            <label>Studio 监听端口</label>
                            <input type="number" class="cv-input" id="cfg-station-port" min="1" max="65535" value="${settings.port || 5000}" ${isDisabled ? 'disabled' : ''}>
                            <span class="settings-field-hint">本机通讯默认使用 127.0.0.1:${settings.port || 5000}。</span>
                        </div>
                        <div class="settings-fieldset" style="flex:1.5; min-width:240px;">
                            <label>LAN 主机名/IP</label>
                            <input type="text" class="cv-input" id="cfg-station-lan-host" list="station-lan-addresses" value="${this.escapeHtml(settings.lanHost || settings.lanAddresses[0] || '')}" ${isDisabled ? 'disabled' : ''} placeholder="192.168.1.20">
                            <datalist id="station-lan-addresses">${lanAddressOptions}</datalist>
                            <span class="settings-field-hint">${settings.lanAddresses.length ? `已识别: ${settings.lanAddresses.map(item => this.escapeHtml(item)).join('、')}` : '未识别到非回环 IPv4，可手动填写。'}</span>
                        </div>
                        <div class="settings-fieldset" style="min-width:190px;">
                            <label>本机 Station 同步</label>
                            <label style="display:flex; align-items:center; gap:10px; height:40px; font-size:13px; color:var(--text-color);">
                                <input type="checkbox" id="cfg-station-local-sync" ${settings.localStationSyncEnabled ? 'checked' : ''} ${isDisabled ? 'disabled' : ''}>
                                写入本机 Station 配置
                            </label>
                            <span class="settings-field-hint">仅管理这台电脑上的 Station。</span>
                        </div>
                    </div>
                </div>
            </div>

            <div class="settings-modern-card">
                <div class="settings-card-header has-badge">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M12 1a5 5 0 00-5 5v3H6a2 2 0 00-2 2v9a2 2 0 002 2h12a2 2 0 002-2v-9a2 2 0 00-2-2h-1V6a5 5 0 00-5-5zm-3 8V6a3 3 0 116 0v3H9z"/></svg>
                        <span>共享 Token</span>
                    </div>
                    <div class="settings-status-badge ${settings.token?.hasToken ? 'status-connected' : 'status-disconnected'}" style="${settings.token?.hasToken ? '' : 'color:#b45309;'}">
                        <span class="status-dot"></span> ${settings.token?.hasToken ? '已生成' : '未生成'}
                    </div>
                </div>
                <div class="settings-card-body">
                    <div class="horizontal-flex" style="padding:0; align-items:flex-end; flex-wrap:wrap;">
                        <div class="settings-fieldset" style="flex:1; min-width:280px;">
                            <label>Station token</label>
                            <input type="text" class="cv-input font-mono" readonly value="${this.escapeHtml(tokenDisplay)}">
                            <span class="settings-field-hint">LAN 模式必须携带 token；保存启用通讯时会自动生成。</span>
                        </div>
                        <button class="cv-btn settings-btn-light" id="btn-reveal-station-token" ${isAdminDisabled} style="padding:0 16px;">显示</button>
                        <button class="cv-btn settings-btn-light" id="btn-copy-station-token" ${isAdminDisabled} style="padding:0 16px;">复制</button>
                        <button class="cv-btn settings-btn-dark" id="btn-regenerate-station-token" ${isAdminDisabled}>重新生成</button>
                    </div>
                </div>
            </div>

            <div class="settings-modern-card">
                <div class="settings-card-header">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"/></svg>
                        <span>连接片段与运行状态</span>
                    </div>
                </div>
                <div class="settings-card-body">
                    <div style="display:grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap:16px; margin-bottom:18px;">
                        <div style="border:1px solid #e2e8f0; border-radius:8px; padding:14px;">
                            <div style="font-size:13px; font-weight:600; margin-bottom:8px;">本机 Station</div>
                            <pre class="font-mono" style="white-space:pre-wrap; word-break:break-all; margin:0; font-size:12px; line-height:1.5;">${this.escapeHtml(localSnippet)}</pre>
                        </div>
                        <div style="border:1px solid #e2e8f0; border-radius:8px; padding:14px;">
                            <div style="font-size:13px; font-weight:600; margin-bottom:8px;">远端 Station</div>
                            <pre class="font-mono" style="white-space:pre-wrap; word-break:break-all; margin:0; font-size:12px; line-height:1.5;">${this.escapeHtml(remoteSnippet)}</pre>
                        </div>
                    </div>
                    <div style="display:grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap:16px;">
                        <div>
                            <label style="display:block; font-size:13px; font-weight:600; margin-bottom:8px;">配置路径</label>
                            <div class="font-mono" style="font-size:12px; line-height:1.6; word-break:break-all;">
                                Studio: ${this.escapeHtml(settings.paths?.studio || '')}<br>
                                Station: ${this.escapeHtml(settings.paths?.localStation || '')}
                            </div>
                        </div>
                        <div>
                            <label style="display:block; font-size:13px; font-weight:600; margin-bottom:8px;">重启提示</label>
                            <ul style="margin:0; padding-left:18px; color:var(--text-muted); font-size:13px; line-height:1.6;">
                                ${restartMessages.map(item => `<li>${this.escapeHtml(item)}</li>`).join('')}
                            </ul>
                        </div>
                    </div>
                    <div style="margin-top:18px; padding:12px 14px; border-radius:8px; background:#f8fafc; border:1px solid #e2e8f0;">
                        <ul style="margin:0; padding-left:18px; color:var(--text-muted); font-size:12px; line-height:1.6;">
                            ${diagnosticsHtml}
                        </ul>
                    </div>
                </div>
            </div>

            <div class="settings-floating-footer">
                <button class="cv-btn settings-btn-light" id="btn-open-station-monitor" style="padding:0 14px;">Station 监控</button>
                <button class="cv-btn settings-btn-light" id="btn-reset-station-communication" style="width:100px;">取消</button>
                <button class="cv-btn settings-btn-danger" id="btn-save-station-communication" ${isAdminDisabled} style="width:150px;">保存 Station 设置</button>
            </div>
        `;
    }

    renderStorageTab() {
        const storage = this.config?.storage || this.getDefaultConfig().storage;
        const pathPickerFeature = getFeatureMeta('storage.pathPicker');
        const immediateCleanupFeature = getFeatureMeta('storage.immediateCleanup');
        return `
            <div class="settings-section-title">
                <h2>文件与存储管理</h2>
                <p>配置图像数据保存路径、清理策略与磁盘容量预警。</p>
            </div>
            
            <div class="settings-modern-card" style="margin-bottom: 24px;">
                <div class="settings-card-header">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M2 20h20v-4H2v4zm2-3h2v2H4v-2zM2 4v4h20V4H2zm4 3H4V5h2v2zm-4 7h20v-4H2v4zm2-3h2v2H4v-2z"/></svg> 
                        <span>存储路径配置</span>
                    </div>
                </div>
                <div class="settings-card-body">
                    <div class="settings-fieldset">
                        <label>默认保存路径 (Image Save Path)</label>
                        <div style="display:flex; gap:12px;">
                            <div class="input-with-icon" style="flex:1;">
                                <svg class="input-icon" viewBox="0 0 24 24" style="fill:#fbbf24;"><path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></svg>
                                <input type="text" class="cv-input" id="cfg-imageSavePath" value="${storage.imageSavePath || 'D:\\VisionData\\Images'}">
                            </div>
                            <button class="cv-btn settings-btn-light" id="btn-change-image-save-path" style="padding:0 20px;" ${isFeatureEnabled('storage.pathPicker') ? '' : 'disabled'} title="${pathPickerFeature.title}">${getFeatureButtonLabel('storage.pathPicker', '更改目录')}</button>
                        </div>
                        <span class="settings-field-hint" style="display:block; margin-top:12px;">${pathPickerFeature.description}</span>
                    </div>
                </div>
            </div>

            <div style="display:flex; gap:24px;">
                <div class="settings-modern-card" style="flex:1.5;">
                    <div class="settings-card-header">
                        <div class="settings-header-left">
                           <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>
                           <span>清理策略与预警</span>
                        </div>
                    </div>
                    <div class="settings-card-body">
                        <div style="display:flex; gap:24px; margin-bottom: 20px;">
                            <div class="settings-fieldset" style="flex:1;">
                                <label>保存策略 (Save Policy)</label>
                                <select class="cv-input" id="cfg-savePolicy">
                                    <option value="All" ${storage.savePolicy === 'All' ? 'selected' : ''}>保存所有图像 (All)</option>
                                    <option value="NgOnly" ${storage.savePolicy === 'NgOnly' ? 'selected' : ''}>仅保存 NG 图像</option>
                                    <option value="None" ${storage.savePolicy === 'None' ? 'selected' : ''}>不保存 (None)</option>
                                </select>
                            </div>
                            <div class="settings-fieldset" style="flex:1;">
                                <label>自动清理阈值 (天)</label>
                                <div class="input-with-suffix" style="position:relative;">
                                    <input type="number" class="cv-input" id="cfg-retentionDays" value="${storage.retentionDays || 30}" style="padding-right:36px;">
                                    <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">Days</span>
                                </div>
                            </div>
                        </div>
                        <div class="settings-fieldset">
                            <label>磁盘低空间预警 (GB)</label>
                            <div class="input-with-suffix" style="position:relative; max-width: 200px;">
                                <input type="number" class="cv-input" id="cfg-minFreeSpaceGb" value="${storage.minFreeSpaceGb || 5}" style="padding-right:36px;">
                                <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">GB</span>
                            </div>
                            <span class="settings-field-hint">当磁盘剩余空间不足该值时，系统会报警并禁止生产启动。</span>
                        </div>
                    </div>
                </div>
                
                <div class="settings-modern-card" style="flex:1;">
                    <div class="settings-card-body" style="padding: 32px 24px;">
                        <div style="display:flex; justify-content:space-between; margin-bottom:12px;">
                            <span id="disk-drive-label" style="font-size:13px; font-weight:600; color:#475569;">-- 磁盘空间</span>
                            <span id="disk-used-percent" style="font-size:13px; font-weight:700; color:#0f172a;">--% 已用</span>
                        </div>
                        <div style="background:#e2e8f0; height:8px; border-radius:4px; overflow:hidden; margin-bottom:24px;">
                            <div id="disk-used-bar" style="background:var(--cinnabar); width:0%; height:100%;"></div>
                        </div>
                        
                        <div style="display:flex; justify-content:space-between; margin-bottom:8px; font-size:13px;">
                            <span class="text-muted">已用空间</span>
                            <span class="font-bold" id="disk-used-gb">-- GB</span>
                        </div>
                        <div style="display:flex; justify-content:space-between; font-size:13px;">
                            <span class="text-muted">可用空间</span>
                            <span class="font-bold" id="disk-free-gb" style="color:#059669;">-- GB</span>
                        </div>
                        
                        <button class="cv-btn settings-btn-light" id="btn-clean-expired-files" style="width:100%; margin-top:32px;" ${isFeatureEnabled('storage.immediateCleanup') ? '' : 'disabled'} title="${immediateCleanupFeature.title}">${getFeatureButtonLabel('storage.immediateCleanup', '立即清理过期文件')}</button>
                        <span class="settings-field-hint" style="display:block; margin-top:12px;">${immediateCleanupFeature.description}</span>
                    </div>
                </div>
            </div>
        `;
    }

    renderRuntimeTab() {
        const runtime = this.config?.runtime || this.getDefaultConfig().runtime;
        return `
            <div class="settings-section-title" style="display:flex; justify-content:space-between; align-items:flex-end;">
                <div>
                    <h2>执行与控制 (Runtime)</h2>
                    <p>配置自动运行逻辑、异常停机条件及硬件联动保护。</p>
                </div>
                <div class="settings-status-badge status-connected" style="background:rgba(232, 85, 78, 0.08); color:#c0392b; border-color:rgba(232, 85, 78, 0.25);">
                    <span class="status-dot" style="background:#e74c3c;"></span> 保护机制已启用
                </div>
            </div>

            <div class="settings-modern-card">
                <div class="settings-card-header">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon" style="fill:#e74c3c;"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>
                        <span>生产保护机制 (Production Guards)</span>
                    </div>
                </div>
                <div class="settings-card-body">
                    <div style="display:flex; gap:24px;">
                        <div class="settings-fieldset" style="flex:1;">
                            <label>连续 NG 停机报警阈值</label>
                            <div class="input-with-suffix" style="position:relative;">
                                <input type="number" class="cv-input" id="cfg-stopOnConsecutiveNg" value="${runtime.stopOnConsecutiveNg || 0}" style="padding-right:48px;">
                                <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">件/次</span>
                            </div>
                            <span class="settings-field-hint">设为 0 时关闭此功能。当连续检测失败次数达到设定值，系统将暂停并发送报警至 PLC。</span>
                        </div>
                        
                        <div class="settings-fieldset" style="flex:1;">
                            <label>缺料等待超时 (秒)</label>
                            <div class="input-with-suffix" style="position:relative;">
                                <input type="number" class="cv-input" id="cfg-missingMaterialTimeoutSeconds" value="${runtime.missingMaterialTimeoutSeconds ?? 120}" style="padding-right:36px;">
                                <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">s</span>
                            </div>
                            <span class="settings-field-hint">连续运行中超过该时间未收到下一次检测结果时自动保护性停止，现场默认 120 秒。</span>
                        </div>
                    </div>

                    <div style="margin-top: 24px;">
                        <div class="settings-fieldset">
                            <label style="display:flex; align-items:center; gap:8px; cursor:pointer;">
                                <input type="checkbox" id="cfg-autoRun" ${runtime.autoRun ? 'checked' : ''} style="width:16px; height:16px; accent-color:var(--cinnabar);">
                                软件就绪后自动进入“连续运行”状态
                            </label>
                            <span class="settings-field-hint" style="margin-left: 24px;">注意：启用此项可能导致系统启动即抛出触发信号需求，请确保外部环境安全。</span>
                        </div>
                        <div class="settings-fieldset" style="margin-top:12px;">
                            <label style="display:flex; align-items:center; gap:8px; cursor:pointer;">
                                <input type="checkbox" id="cfg-applyProtectionRules" ${runtime.applyProtectionRules !== false ? 'checked' : ''} style="width:16px; height:16px; accent-color:var(--cinnabar);">
                                保存后立即启用运行保护规则
                            </label>
                            <span class="settings-field-hint" style="margin-left: 24px;">该配置会随“保存所有更改”一起持久化，并作为运行保护的开关。</span>
                        </div>
                    </div>
                </div>
                <div class="settings-card-body" style="border-top:1px solid #e2e8f0; display:flex; justify-content:flex-end; padding:16px 24px;">
                    <button class="cv-btn settings-btn-danger" id="btn-apply-protection-rules">
                        <svg viewBox="0 0 24 24" style="width:16px; height:16px; margin-right:6px; fill:currentColor;"><path d="M17 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z"/></svg>
                        保存运行保护配置
                    </button>
                </div>
            </div>
        `;
    }
    
    renderCameraTab() {
        return `
            <div class="settings-section-title" style="display:flex; justify-content:space-between; align-items:flex-end;">
                <div>
                    <h2>相机管理</h2>
                    <p>配置和管理视觉系统连接的工业相机参数。</p>
                </div>
                <div class="settings-actions" style="display:flex; gap:12px; align-items:center; flex-wrap:wrap;">
                    <button class="cv-btn settings-btn-light" id="btn-discover-huaray-cameras">
                        <svg viewBox="0 0 24 24" style="width:16px; height:16px; margin-right:6px; fill:currentColor;"><path d="M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z"/></svg>
                        华睿搜索
                    </button>
                    <button class="cv-btn settings-btn-light" id="btn-discover-hikvision-cameras">
                        <svg viewBox="0 0 24 24" style="width:16px; height:16px; margin-right:6px; fill:currentColor;"><path d="M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z"/></svg>
                        海康搜索
                    </button>
                    <button class="cv-btn settings-btn-light" id="btn-camera-preview" disabled title="请先在列表中选择一台相机">
                        相机预览
                    </button>
                    <button class="cv-btn settings-btn-light" id="btn-hand-eye-calib" disabled title="请先在列表中选择一台相机">
                        二维平面标定向导
                    </button>
                </div>
            </div>

            <div class="settings-modern-card">
                <div class="settings-card-table-wrapper">
                    <table class="settings-modern-table" id="camera-bindings-table">
                        <thead>
                            <tr>
                                <th>名称</th>
                                <th>IP地址/序列号</th>
                                <th>驱动类型</th>
                                <th>像素格式</th>
                                <th>状态</th>
                                <th>操作</th>
                            </tr>
                        </thead>
                        <tbody>
                            <!-- 加载后端数据 -->
                            <tr><td colspan="6" style="text-align:center; padding: 24px;"><div class="cv-spinner" style="margin-right:8px; display:inline-block;"></div>正在加载相机配置...</td></tr>
                        </tbody>
                    </table>
                </div>
            </div>

            <!-- Parameters Card -->
            <div class="settings-modern-card" style="margin-top:24px; background:#fafbfc;">
                <div class="settings-card-header" style="background:#ffffff; display:flex; justify-content:space-between;">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon" style="fill:#94a3b8;"><path d="M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z"/></svg>
                        <span>参数配置: <span id="current-cam-name">未选择相机</span></span>
                    </div>
                </div>
                <div class="settings-card-body">
                    <div id="camera-selection-hint" class="settings-field-hint" style="display:block; margin-bottom:16px;">
                        请先在上方绑定列表中选择一台相机，再进行预览、二维平面标定或参数保存。
                    </div>
                    <div style="display:grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap:24px; margin-bottom: 24px;">
                        <div class="settings-fieldset">
                            <label>曝光时间 (Exposure Time)</label>
                            <div class="input-with-suffix" style="position:relative;">
                                <input type="number" class="cv-input" id="cam-param-exposure" value="" style="padding-right:36px;">
                                <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">µs</span>
                            </div>
                            <span class="settings-field-hint">范围: 10 - 1000000 µs</span>
                        </div>
                        <div class="settings-fieldset">
                            <label>增益 (Gain)</label>
                            <div class="input-with-suffix" style="position:relative;">
                                <input type="number" step="0.1" class="cv-input" id="cam-param-gain" value="" style="padding-right:36px;">
                                <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">dB</span>
                            </div>
                            <span class="settings-field-hint">范围: 0.0 - 24.0 dB</span>
                        </div>
                        <div class="settings-fieldset">
                            <label>像素格式 (Pixel Format)</label>
                            <select class="cv-input" id="cam-param-pixel-format">
                                <option value="Mono8">Mono8 / 黑白</option>
                                <option value="RGB8">RGB8 / 彩色 RGB</option>
                                <option value="BGR8">BGR8 / 彩色 BGR</option>
                                <option value="BayerRG8">BayerRG8</option>
                                <option value="BayerGB8">BayerGB8</option>
                                <option value="BayerGR8">BayerGR8</option>
                                <option value="BayerBG8">BayerBG8</option>
                            </select>
                            <span class="settings-field-hint">保存后写入相机 SDK 的 PixelFormat。</span>
                        </div>
                        <div class="settings-fieldset">
                            <label>触发模式 (Trigger Mode)</label>
                            <select class="cv-input" id="cam-param-trigger-mode">
                                <option value="Software">软件触发</option>
                                <option value="External">外部触发</option>
                                <option value="Continuous">连续采集</option>
                            </select>
                            <span class="settings-field-hint">仅作用于当前所选相机</span>
                        </div>
                    </div>
                    <div style="border-top:1px solid #e2e8f0; padding-top:20px; margin-bottom:24px;">
                        <div class="settings-header-left" style="margin-bottom:14px;">
                            <svg viewBox="0 0 24 24" class="settings-header-icon" style="fill:#64748b;"><path d="M7 2v11h3v9l7-12h-4l4-8H7z"/></svg>
                            <span>触发来源 / 触发方式</span>
                        </div>
                        <div style="display:grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap:24px;">
                            <div class="settings-fieldset">
                                <label>软件触发来源</label>
                                <select class="cv-input" id="cam-param-software-trigger-source">
                                    <option value="Manual">手动 / API 软件触发</option>
                                    <option value="EnterPhotoelectric">回车光电触发（USB）</option>
                                    <option value="SerialPhotoelectric">串口光电触发（COM）</option>
                                </select>
                                <span class="settings-field-hint" id="cam-param-enter-trigger-hint">仅软件触发模式下生效。</span>
                            </div>
                            <div class="settings-fieldset">
                                <label>外触发输入线</label>
                                <select class="cv-input" id="cam-param-hardware-trigger-source" disabled aria-disabled="true">
                                    <option value="Line0">Line0</option>
                                    <option value="Line1">Line1</option>
                                    <option value="Line2">Line2</option>
                                    <option value="Line3">Line3</option>
                                </select>
                                <span class="settings-field-hint" id="cam-param-hardware-trigger-source-hint">仅 External 模式下生效；当前值会保留。</span>
                            </div>
                            <div class="settings-fieldset">
                                <label>回车防抖时间</label>
                                <div class="input-with-suffix" style="position:relative;">
                                    <input type="number" class="cv-input" id="cam-param-enter-debounce" value="200" min="0" max="5000" style="padding-right:42px;" disabled readonly aria-disabled="true">
                                    <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">ms</span>
                                </div>
                                <span class="settings-field-hint">过滤 USB 回车重复触发。</span>
                            </div>
                            <div class="settings-fieldset">
                                <label>回车等待超时</label>
                                <div class="input-with-suffix" style="position:relative;">
                                    <input type="number" class="cv-input" id="cam-param-enter-timeout" value="30000" min="100" max="600000" style="padding-right:42px;" disabled readonly aria-disabled="true">
                                    <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">ms</span>
                                </div>
                                <span class="settings-field-hint">超过该时间未收到回车触发则报错。</span>
                            </div>
                        </div>
                        <div style="display:grid; grid-template-columns: 2fr 1fr; gap:24px; margin-top:16px; align-items:end;">
                            <div class="settings-fieldset">
                                <label>回车设备过滤</label>
                                <div style="display:flex; gap:8px;">
                                    <input type="text" class="cv-input" id="cam-param-enter-device-id" value="" placeholder="留空表示接受任意键盘类回车设备" disabled readonly aria-disabled="true">
                                    <button type="button" class="cv-btn settings-btn-light" id="btn-learn-enter-trigger-device" disabled>学习 USB 设备</button>
                                </div>
                                <span class="settings-field-hint">学习后只接受该 USB 回车光电设备，避免普通键盘误触。</span>
                            </div>
                            <div class="settings-fieldset">
                                <label style="display:flex; align-items:center; gap:8px; cursor:pointer;">
                                    <input type="checkbox" id="cam-param-ignore-enter-busy" checked style="width:16px; height:16px; accent-color:var(--cinnabar);" disabled>
                                    忙碌时忽略新触发
                                </label>
                                <span class="settings-field-hint" style="margin-left:24px;">防止同一工件重复拍照。</span>
                            </div>
                        </div>
                        <div style="display:grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap:24px; margin-top:16px;">
                            <div class="settings-fieldset">
                                <label>串口号</label>
                                <div style="display:flex; gap:8px;">
                                    <input type="text" class="cv-input" id="cam-param-serial-port-name" value="" placeholder="自动识别" style="min-width:0;" disabled readonly aria-disabled="true">
                                    <button type="button" class="cv-btn settings-btn-light" id="btn-refresh-serial-photoelectric-port">识别</button>
                                    <button type="button" class="cv-btn settings-btn-light" id="btn-test-serial-photoelectric">测试</button>
                                </div>
                                <span class="settings-field-hint" id="cam-param-serial-port-hint">进入本页后会自动识别 USB 串口。</span>
                            </div>
                            <div class="settings-fieldset">
                                <label>串口波特率</label>
                                <input type="number" class="cv-input" id="cam-param-serial-baud-rate" value="9600" min="1" disabled readonly aria-disabled="true">
                                <span class="settings-field-hint">厂家 demo 默认 9600。</span>
                            </div>
                            <div class="settings-fieldset">
                                <label>串口防抖时间</label>
                                <div class="input-with-suffix" style="position:relative;">
                                    <input type="number" class="cv-input" id="cam-param-serial-debounce" value="200" min="0" max="5000" style="padding-right:42px;" disabled readonly aria-disabled="true">
                                    <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">ms</span>
                                </div>
                                <span class="settings-field-hint">过滤遮挡帧重复触发。</span>
                            </div>
                            <div class="settings-fieldset">
                                <label>串口等待超时</label>
                                <div class="input-with-suffix" style="position:relative;">
                                    <input type="number" class="cv-input" id="cam-param-serial-timeout" value="30000" min="100" max="600000" style="padding-right:42px;" disabled readonly aria-disabled="true">
                                    <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">ms</span>
                                </div>
                                <span class="settings-field-hint">收到遮挡帧 01 11 后触发。</span>
                            </div>
                        </div>
                        <div class="settings-fieldset" style="margin-top:16px;">
                            <label style="display:flex; align-items:center; gap:8px; cursor:pointer;">
                                <input type="checkbox" id="cam-param-ignore-serial-busy" checked style="width:16px; height:16px; accent-color:var(--cinnabar);" disabled>
                                串口忙碌时忽略新触发
                            </label>
                            <span class="settings-field-hint" style="margin-left:24px;">仅串口光电触发来源下生效。</span>
                        </div>
                    </div>
                    <div style="display:grid; grid-template-columns: repeat(3, 1fr); gap:24px;">
                        <div class="settings-fieldset">
                            <label>采集帧率 (Frame Rate)</label>
                            <div class="input-with-suffix" style="position:relative;">
                                <input type="number" class="cv-input" id="cam-param-target-frame-rate" value="" min="1" max="120" style="padding-right:36px;" disabled readonly aria-disabled="true">
                                <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">fps</span>
                            </div>
                            <span class="settings-field-hint" id="cam-param-target-frame-rate-hint">仅 Continuous 模式下可编辑；当前值会保留。</span>
                        </div>
                        <div class="settings-fieldset">
                            <label>图像宽度 (Width)</label>
                            <div class="input-with-suffix" style="position:relative;">
                                <input type="number" class="cv-input" value="" style="padding-right:36px;" disabled readonly aria-disabled="true">
                                <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">px</span>
                            </div>
                            <span class="settings-field-hint">宽高参数暂未开放编辑，避免误以为已经保存生效。</span>
                        </div>
                        <div class="settings-fieldset">
                            <label>图像高度 (Height)</label>
                            <div class="input-with-suffix" style="position:relative;">
                                <input type="number" class="cv-input" value="" style="padding-right:36px;" disabled readonly aria-disabled="true">
                                <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">px</span>
                            </div>
                            <span class="settings-field-hint">宽高参数暂未开放编辑，避免误以为已经保存生效。</span>
                        </div>
                    </div>
                </div>
                <div class="settings-card-body" style="border-top:1px solid #e2e8f0; display:flex; justify-content:flex-end; gap:12px; padding:16px 24px;">
                    <button class="cv-btn settings-btn-light" id="btn-reset-camera-params">重置当前值</button>
                    <button class="cv-btn settings-btn-danger" id="btn-save-camera-params">
                        <svg viewBox="0 0 24 24" style="width:16px; height:16px; margin-right:6px; fill:currentColor;"><path d="M17 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z"/></svg>
                        保存当前相机参数
                    </button>
                </div>
            </div>
        `;
    }

    renderAiTab() {
        const aiInfo = "DeepSeek-V3";
        return `
            <div class="settings-section-title">
                <h2>AI & LLM 模型管理</h2>
                <p>集成深度学习本地模型与云端大语言模型 API 配置。</p>
            </div>

            <!-- Block 1: Model Tab & List -->
            <div class="settings-modern-card">
                <div class="settings-card-header" style="background:white; border-bottom:1px solid #e2e8f0; padding:0; display:flex;">
                    <div style="display:flex; padding-top:16px;">
                        <div style="padding:0 24px 12px; color:#94a3b8; font-weight:600; font-size:14px; cursor:pointer;">
                            <svg viewBox="0 0 24 24" style="width:16px; height:16px; vertical-align:text-bottom; margin-right:4px; fill:currentColor;"><path d="M19.14,12.94c0.04-0.3,0.06-0.61,0.06-0.94c0-0.32-0.02-0.64-0.06-0.94l2.03-1.58c0.18-0.14,0.23-0.41,0.12-0.61 l-1.92-3.32c-0.12-0.22-0.37-0.29-0.59-0.22l-2.39,0.96c-0.5-0.38-1.03-0.7-1.62-0.94L14.4,2.81c-0.04-0.24-0.24-0.41-0.48-0.41 h-3.84c-0.24,0-0.43,0.17-0.47,0.41L9.25,5.35C8.66,5.59,8.12,5.92,7.63,6.29L5.24,5.33c-0.22-0.08-0.47,0-0.59,0.22L2.73,8.87 C2.62,9.08,2.66,9.34,2.86,9.48l2.03,1.58C4.84,11.36,4.8,11.69,4.8,12s0.02,0.64,0.06,0.94l-2.03,1.58 c-0.18,0.14-0.23,0.41-0.12,0.61l1.92,3.32c0.12,0.22,0.37,0.29,0.59,0.22l2.39-0.96c0.5,0.38,1.03,0.7,1.62,0.94l0.36,2.54 c0.05,0.24,0.24,0.41,0.48,0.41h3.84c0.24,0,0.43-0.17,0.47-0.41l0.36-2.54c0.59-0.24,1.13-0.56,1.62-0.94l2.39,0.96 c0.22,0.08,0.47,0,0.59-0.22l1.92-3.32c0.12-0.22,0.07-0.49-0.12-0.61L19.14,12.94z M12,15.6c-1.98,0-3.6-1.62-3.6-3.6 s1.62-3.6,3.6-3.6s3.6,1.62,3.6,3.6S13.98,15.6,12,15.6z"/></svg> 本地模型
                        </div>
                        <div style="padding:0 24px 12px; color:var(--cinnabar); font-weight:600; font-size:14px; border-bottom:2px solid var(--cinnabar); cursor:pointer;">
                            <svg viewBox="0 0 24 24" style="width:16px; height:16px; vertical-align:text-bottom; margin-right:4px; fill:currentColor;"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13v4h-4v-4H7l5-5 5 5h-3z"/></svg> 大语言模型 (LLM)
                        </div>
                    </div>
                    <div style="margin-left:auto; padding:12px 24px;">
                        <button class="cv-btn settings-btn-light" style="height:32px;" id="btn-add-llm">+ 添加 LLM</button>
                    </div>
                </div>
                <div class="settings-card-table-wrapper">
                    <table class="settings-modern-table" id="ai-models-table">
                        <thead>
                            <tr>
                                <th>名称</th>
                                <th>协议</th>
                                <th>模型标识</th>
                                <th>状态 (Active)</th>
                                <th>操作</th>
                            </tr>
                        </thead>
                        <tbody>
                            <!-- Javascript Dynamically Inserted Here -->
                        </tbody>
                    </table>
                </div>
            </div>

            <!-- Block 2: 详情配置 -->
            <div style="display:flex; gap:24px;">
                <div class="settings-modern-card" style="flex:2;">
                    <div class="settings-card-header" style="background:#ffffff;">
                        <div class="settings-header-left">
                            <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13v4h-4v-4H7l5-5 5 5h-3z" fill="var(--text-muted)"/></svg>
                            <span>配置所选模型 (Editing)</span>
                        </div>
                    </div>
                    <div class="settings-card-body" id="ai-detail-form">
                        <!-- Loaded by JS -->
                    </div>
                </div>

                <div class="settings-modern-card" style="flex:1; border:2px solid #a7f3d0; box-shadow:0 10px 25px -5px rgba(16, 185, 129, 0.1);">
                    <div class="settings-card-body" style="text-align:center; padding:32px 24px;">
                        <h3 style="font-size:15px; color:#475569; margin:0 0 24px;">API 性能概览</h3>
                        
                        <div style="position:relative; width:140px; height:140px; margin:0 auto 24px;">
                            <!-- Fake Donut Chart SVG -->
                            <svg viewBox="0 0 36 36" style="width:100%; height:100%;">
                                <path d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831" fill="none" stroke="#f1f5f9" stroke-width="4"></path>
                                <path d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831" fill="none" stroke="#10b981" stroke-width="4" stroke-dasharray="80, 100" stroke-dashoffset="0" style="transition: stroke-dasharray 1s ease-out;"></path>
                            </svg>
                            <div style="position:absolute; top:50%; left:50%; transform:translate(-50%, -50%);">
                                <div style="font-size:32px; font-weight:700; color:#0f172a; line-height:1;">450</div>
                                <div style="font-size:12px; color:#94a3b8; font-weight:600; text-transform:uppercase;">ms</div>
                            </div>
                        </div>

                        <div style="font-size:13px; font-weight:600; color:#10b981; margin-bottom:24px;">● 网络延迟 (Latency)</div>

                        <div style="text-align:left; font-size:13px; border-top:1px dashed #e2e8f0; padding-top:16px;">
                            <div style="display:flex; justify-content:space-between; margin-bottom:8px;">
                                <span class="text-muted">Token 消耗 (Daily)</span>
                                <span class="font-bold">14.2K / 50K</span>
                            </div>
                            <div style="background:#e2e8f0; height:6px; border-radius:3px; overflow:hidden; margin-bottom:16px;">
                                <div style="background:#3b82f6; width:28%; height:100%;"></div>
                            </div>

                            <div style="display:flex; justify-content:space-between; margin-bottom:8px;">
                                <span class="text-muted">RPM (Requests/Min)</span>
                                <span class="font-bold">45 / 100</span>
                            </div>
                            <div style="background:#e2e8f0; height:6px; border-radius:3px; overflow:hidden;">
                                <div style="background:#ec4899; width:45%; height:100%;"></div>
                            </div>
                        </div>

                        <div style="background:#ecfdf5; border:1px solid #d1fae5; border-radius:8px; padding:12px; margin-top:24px; text-align:left; font-size:12px; color:#065f46; display:flex; gap:8px;">
                            <svg viewBox="0 0 24 24" style="width:16px; height:16px; fill:#10b981; flex-shrink:0;"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"/></svg>
                            DeepSeek-V3 连接稳定，当前延迟适合非实时分析任务。
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    renderUserManagementTab() {
        const security = this.config?.security || this.getDefaultConfig().security;
        return `
            <div class="settings-section-title" style="display:flex; justify-content:space-between; align-items:flex-end;">
                <div>
                    <h2>用户权限管理</h2>
                    <p>管理系统操作人员账号，分配不同级别的使用权限。</p>
                </div>
                <div class="settings-actions">
                    <button class="cv-btn settings-btn-danger" id="btn-add-user">
                        <span style="font-size:16px; margin-right:4px;">+</span> 新增用户
                    </button>
                </div>
            </div>

            <div class="settings-modern-card">
                <div class="settings-card-table-wrapper">
                    <table class="settings-modern-table" id="settings-user-table">
                        <thead>
                            <tr>
                                <th>用户名 (Username)</th>
                                <th>角色 (Role)</th>
                                <th>最后登录</th>
                                <th>状态</th>
                                <th>操作</th>
                            </tr>
                        </thead>
                        <tbody>
                            <!-- Loaded by JS -->
                        </tbody>
                    </table>
                </div>
            </div>
            
            <div class="settings-modern-card" style="margin-top:24px;">
                <div class="settings-card-header">
                    <div class="settings-header-left">
                        <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z"/></svg> 
                        <span>安全与密码策略</span>
                    </div>
                </div>
                <div class="settings-card-body" style="display:flex; gap:24px;">
                    <div class="settings-fieldset" style="flex:1;">
                        <label>密码最小长度</label>
                        <input type="number" class="cv-input" id="cfg-passwordMinLength" value="${security.passwordMinLength || 6}">
                    </div>
                    <div class="settings-fieldset" style="flex:1;">
                        <label>会话自动超时 (分钟)</label>
                        <input type="number" class="cv-input" id="cfg-sessionTimeoutMinutes" value="${security.sessionTimeoutMinutes || 30}">
                    </div>
                    <div class="settings-fieldset" style="flex:1;">
                        <label>登录失败锁定次数</label>
                        <input type="number" class="cv-input" id="cfg-loginFailureLockoutCount" value="${security.loginFailureLockoutCount || 5}">
                    </div>
                </div>
                <div class="settings-card-body" style="border-top:1px solid #e2e8f0; padding-top:16px;">
                    <div style="display:flex; justify-content:space-between; align-items:center; gap:16px; flex-wrap:wrap; width:100%;">
                        <span class="settings-field-hint">密码最小长度会立即应用到修改密码、创建用户和重置密码；其他策略会随认证链路逐步接入。</span>
                        <button class="cv-btn settings-btn-danger" id="btn-save-security-policy">保存安全策略</button>
                    </div>
                </div>
            </div>
            
            <!-- 模态框容器 -->
            <div id="user-modal-container"></div>
        `;
    }

    async refreshUserTable() {
        if (!this.isAdmin) return;
        const tbody = this.container.querySelector('#settings-user-table tbody');
        if (!tbody) return;
        
        try {
            this.users = await httpClient.get('/users');
            
            tbody.innerHTML = this.users.map(u => {
                let roleColor = '#475569';
                let roleBg = '#f1f5f9';
                let roleName = '角色未知';
                
                if (u.role === 'Admin' || u.role === 0) {
                    roleColor = '#c0392b'; roleBg = 'rgba(232, 85, 78, 0.08)'; roleName = '系统管理员';
                } else if (u.role === 'Engineer' || u.role === 1) {
                    roleColor = '#1d4ed8'; roleBg = '#dbeafe'; roleName = '工程师';
                } else if (u.role === 'Operator' || u.role === 2) {
                    roleColor = '#475569'; roleBg = '#f1f5f9'; roleName = '操作员';
                }

                const username = String(u.username || '');
                const usernameHtml = this.escapeHtml(username || '-');
                const userId = this.escapeHtml(u.id || '');
                const initial = this.escapeHtml((username.charAt(0) || '?').toUpperCase());
                // 如果最后登录时间为空，显示"从未登录"，否则格式化
                const lastLogin = this.escapeHtml(u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '从未登录');
                
                const statusHtml = u.isActive 
                    ? '<span class="settings-status-badge status-connected"><span class="status-dot"></span> 正常</span>'
                    : '<span class="settings-status-badge status-disconnected" style="color:#ef4444;"><span class="status-dot" style="background:#ef4444;"></span> 已禁用</span>';
                
                return `
                    <tr>
                        <td>
                            <div style="display:flex; align-items:center; gap:12px;">
                                <div style="width:32px; height:32px; background:${roleBg}; border-radius:50%; display:flex; align-items:center; justify-content:center; color:${roleColor}; font-weight:700; font-size:14px;">${initial}</div>
                                <div class="font-bold">${usernameHtml}</div>
                            </div>
                        </td>
                        <td><span class="type-badge" style="background:${roleBg}; color:${roleColor};">${roleName}</span></td>
                        <td class="text-muted">${lastLogin}</td>
                        <td>${statusHtml}</td>
                        <td>
                            <button class="action-icon-btn" data-action="edit" data-id="${userId}" title="编辑用户"><svg viewBox="0 0 24 24"><path d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25z"/></svg></button>
                            ${username.toLowerCase() !== 'admin' ? `<button class="action-icon-btn" data-action="reset-pwd" data-id="${userId}" title="重置密码"><svg viewBox="0 0 24 24"><path d="M12.65 10C11.83 7.67 9.61 6 7 6c-3.31 0-6 2.69-6 6s2.69 6 6 6c2.61 0 4.83-1.67 5.65-4h2.35l2-2 2 2 2-2 2 2V10h-6.35zM7 14c-1.1 0-2-.89-2-2s.9-2 2-2 2 .89 2 2-.9 2-2 2z"/></svg></button>` : ''}
                            ${username.toLowerCase() !== 'admin' ? `<button class="action-icon-btn" data-action="toggle-status" data-id="${userId}" title="${u.isActive?'禁用':'启用'}"><svg viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg></button>` : ''}
                            ${username.toLowerCase() !== 'admin' ? `<button class="action-icon-btn" data-action="delete" data-id="${userId}" title="删除用户" style="color:var(--cinnabar);"><svg viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg></button>` : ''}
                        </td>
                    </tr>
                `;
            }).join('');
        } catch (error) {
            console.error('[SettingsView] loadUsers err', error);
            tbody.innerHTML = `<tr><td colspan="5" style="text-align:center;color:var(--cinnabar);">加载失败: ${this.escapeHtml(error.message)}</td></tr>`;
        }
    }

    bindUserManagementEvents() {
        const tab = this.container.querySelector('[data-section="users"]');
        if (!tab) return;
        
        tab.addEventListener('click', async (e) => {
            const btn = e.target.closest('button');
            if (!btn) return;
            
            if (btn.id === 'btn-add-user') {
                this.showUserModal('create', null);
                return;
            }
            
            const action = btn.dataset.action;
            const id = btn.dataset.id;
            if (!action || !id) return;
            
            const user = this.users.find(u => u.id === id);
            if (!user) return;
            
            if (action === 'edit') {
                this.showUserModal('edit', user);
            } else if (action === 'delete') {
                if (confirm(`确定要删除用户 ${user.username} 吗？`)) {
                    try {
                        await httpClient.delete(`/users/${id}`);
                        showToast('用户已删除', 'success');
                        this.refreshUserTable();
                    } catch(err) {
                        showToast('删除失败: ' + err.message, 'error');
                    }
                }
            } else if (action === 'toggle-status') {
                try {
                    await httpClient.put(`/users/${id}`, {
                        displayName: user.displayName,
                        role: user.role,
                        isActive: !user.isActive
                    });
                    showToast(`用户已${user.isActive?'禁用':'启用'}`, 'success');
                    this.refreshUserTable();
                } catch(err) {
                    showToast('操作失败: ' + err.message, 'error');
                }
            } else if (action === 'reset-pwd') {
                const newPwd = prompt(`请输入为 ${user.username} 重置的新密码 (至少6位):`);
                if (newPwd) {
                    try {
                        await httpClient.post(`/users/${id}/reset-password`, { newPassword: newPwd });
                        showToast('密码重置成功', 'success');
                    } catch(err) {
                        showToast('密码重置失败: ' + err.message, 'error');
                    }
                }
            }
        });
    }

    showUserModal(mode, user) {
        const title = mode === 'create' ? '新增用户' : '编辑用户';
        let roleVal = user ? user.role : 2; // Default to Operator (2)
        if (roleVal === 'Admin') roleVal = 0;
        else if (roleVal === 'Engineer') roleVal = 1;
        else if (roleVal === 'Operator') roleVal = 2;
        const usernameValue = this.escapeHtml(user ? user.username : '');
        const displayNameValue = this.escapeHtml(user?.displayName || '');

        const content = document.createElement('div');
        content.innerHTML = `
            <div class="settings-fieldset" style="margin-bottom:16px;">
                <label>用户名 (登录账号)</label>
                <input type="text" class="cv-input" id="modal-user-username" value="${usernameValue}" ${mode === 'edit' ? 'disabled' : ''}>
            </div>
            ${mode === 'create' ? `
            <div class="settings-fieldset" style="margin-bottom:16px;">
                <label>初始密码 (至少6位)</label>
                <input type="password" class="cv-input" id="modal-user-password">
            </div>
            ` : ''}
            <div class="settings-fieldset" style="margin-bottom:16px;">
                <label>显示名称 (可选)</label>
                <input type="text" class="cv-input" id="modal-user-displayname" value="${displayNameValue}">
            </div>
            <div class="settings-fieldset" style="margin-bottom:16px;">
                <label>用户角色</label>
                <select class="cv-input" id="modal-user-role">
                    <option value="0" ${roleVal === 0 ? 'selected' : ''}>系统管理员</option>
                    <option value="1" ${roleVal === 1 ? 'selected' : ''}>工程师</option>
                    <option value="2" ${roleVal === 2 ? 'selected' : ''}>操作员</option>
                </select>
            </div>
            <div style="display:flex; justify-content:flex-end; gap:12px; margin-top:24px;">
                <button class="cv-btn cv-btn-secondary" id="btn-cancel-usermodal">取消</button>
                <button class="cv-btn cv-btn-primary" id="btn-save-usermodal">保存</button>
            </div>
        `;

        const modal = createModal({
            title,
            content,
            width: '520px'
        });

        content.querySelector('#btn-cancel-usermodal').addEventListener('click', () => closeModal(modal));
        content.querySelector('#btn-save-usermodal').addEventListener('click', async () => {
            const displayName = content.querySelector('#modal-user-displayname').value;
            const role = parseInt(content.querySelector('#modal-user-role').value, 10);
            
            try {
                if (mode === 'create') {
                    const username = content.querySelector('#modal-user-username').value;
                    const password = content.querySelector('#modal-user-password').value;
                    await httpClient.post('/users', { username, password, displayName, role });
                    showToast('用户创建成功', 'success');
                } else {
                    await httpClient.put(`/users/${user.id}`, {
                        displayName,
                        role,
                        isActive: user.isActive
                    });
                    showToast('用户信息已更新', 'success');
                }
                closeModal(modal);
                this.refreshUserTable();
            } catch (err) {
                showToast('保存失败: ' + err.message, 'error');
            }
        });
    }

    // saveAiConfig 已被 _saveCurrentForm() 替代，不再需要此方法

    async loadDiskUsage() {
        if (!this.container) return;

        try {
            const pathInput = this.container.querySelector('#cfg-imageSavePath');
            const sourcePath = pathInput?.value || this.config?.storage?.imageSavePath || '';
            const usage = await httpClient.get(`/settings/disk-usage?path=${encodeURIComponent(sourcePath)}`);
            this.diskUsage = usage;
            this.updateDiskUsageCard();
        } catch (error) {
            console.warn('[SettingsView] 加载磁盘容量失败:', error);
        }
    }

    updateDiskUsageCard() {
        if (!this.container || !this.diskUsage) return;

        const usage = this.diskUsage;
        const driveLabel = this.container.querySelector('#disk-drive-label');
        const usedPercent = this.container.querySelector('#disk-used-percent');
        const usedBar = this.container.querySelector('#disk-used-bar');
        const usedGb = this.container.querySelector('#disk-used-gb');
        const freeGb = this.container.querySelector('#disk-free-gb');

        if (driveLabel) driveLabel.textContent = `${usage.driveName} 磁盘空间`;
        if (usedPercent) usedPercent.textContent = `${usage.usedPercent}% 已用`;
        if (usedBar) usedBar.style.width = `${Math.min(100, Math.max(0, usage.usedPercent))}%`;
        if (usedGb) usedGb.textContent = `${usage.usedGb} GB`;
        if (freeGb) freeGb.textContent = `${usage.freeGb} GB`;
    }

    collectCameraBindings() {
        return this.cameraBindings.map(binding => ({
            ...binding,
            pixelFormat: this.normalizeCameraPixelFormat(binding.pixelFormat ?? binding.PixelFormat),
            triggerMode: this.normalizeCameraTriggerMode(binding.triggerMode),
            hardwareTriggerSource: this.normalizeHardwareTriggerSource(binding.hardwareTriggerSource),
            softwareTriggerSource: this.normalizeSoftwareTriggerSource(binding.softwareTriggerSource),
            enterPhotoelectricDebounceMs: this.normalizeEnterDebounceMs(binding.enterPhotoelectricDebounceMs),
            enterPhotoelectricTimeoutMs: this.normalizeEnterTimeoutMs(binding.enterPhotoelectricTimeoutMs),
            ignoreEnterTriggerWhileBusy: binding.ignoreEnterTriggerWhileBusy !== false,
            enterPhotoelectricDeviceId: String(binding.enterPhotoelectricDeviceId || '').trim(),
            serialPhotoelectricPortName: String(binding.serialPhotoelectricPortName || '').trim(),
            serialPhotoelectricBaudRate: this.normalizeSerialBaudRate(binding.serialPhotoelectricBaudRate),
            serialPhotoelectricDebounceMs: this.normalizeSerialDebounceMs(binding.serialPhotoelectricDebounceMs),
            serialPhotoelectricTimeoutMs: this.normalizeSerialTimeoutMs(binding.serialPhotoelectricTimeoutMs),
            ignoreSerialPhotoelectricTriggerWhileBusy: binding.ignoreSerialPhotoelectricTriggerWhileBusy !== false,
            targetFrameRateFps: this.normalizeCameraTargetFrameRate(binding.targetFrameRateFps)
        }));
    }

    syncActiveCameraSelection() {
        inspectionController.setCamera(this.resolveActiveCameraId() || null);
    }

    resolveActiveCameraId() {
        const preferredActiveId = this.config?.activeCameraId || '';
        if (this.cameraBindings.some(b => b.id === preferredActiveId)) {
            return preferredActiveId;
        }
        return this.cameraBindings[0]?.id || '';
    }

    async saveCameraBindings({ silent = false } = {}) {
        const activeCameraId = this.resolveActiveCameraId();
        const bindingsPayload = this.collectCameraBindings();
        this.lastCameraBindingSaveError = null;

        try {
            await httpClient.put('/cameras/bindings', {
                bindings: bindingsPayload,
                activeCameraId: activeCameraId
            });

            if (this.config) {
                this.config.cameras = [...bindingsPayload];
                this.config.activeCameraId = activeCameraId;
            }

            this.syncActiveCameraSelection();

            return true;
        } catch (error) {
            console.error('[SettingsView] Failed to save camera bindings:', error);
            this.lastCameraBindingSaveError = error.message;
            if (!silent) {
                showToast('保存相机绑定失败: ' + error.message, 'error');
            }
            return false;
        }
    }

    async changePassword() {
        const currentPassword = this.container?.querySelector('#cfg-current-password')?.value || '';
        const newPassword = this.container?.querySelector('#cfg-new-password')?.value || '';
        const confirmPassword = this.container?.querySelector('#cfg-confirm-password')?.value || '';
        const minLength = this.config?.security?.passwordMinLength
            ?? this.config?.security?.PasswordMinLength
            ?? this.getDefaultConfig().security.passwordMinLength;

        if (!currentPassword || !newPassword || !confirmPassword) {
            showToast('请完整填写当前密码、新密码和确认密码', 'warning');
            return;
        }

        if (newPassword !== confirmPassword) {
            showToast('两次输入的新密码不一致', 'warning');
            return;
        }

        if (newPassword.trim().length < minLength) {
            showToast(`新密码长度不能少于 ${minLength} 位`, 'warning');
            return;
        }

        try {
            await httpClient.post('/auth/change-password', {
                oldPassword: currentPassword,
                newPassword: newPassword
            });

            this.container.querySelector('#cfg-current-password').value = '';
            this.container.querySelector('#cfg-new-password').value = '';
            this.container.querySelector('#cfg-confirm-password').value = '';
            showToast('密码修改成功，请使用新密码继续登录', 'success');
        } catch (error) {
            showToast(`密码修改失败: ${error.message}`, 'error');
        }
    }

    async resetSettings() {
        const resetFeature = getFeatureMeta('settings.reset');
        if (!confirm(`确定要${getFeatureButtonLabel('settings.reset', '恢复默认设置')}吗？${resetFeature.description}`)) {
            return;
        }

        try {
            const result = await httpClient.post('/settings/reset');
            const message = result?.message
                || result?.Message
                || `${getFeatureButtonLabel('settings.reset', '恢复默认设置')}已执行`;
            applyTheme(
                normalizeTheme(result?.config?.general?.theme, this.getDefaultConfig().general.theme),
                { persist: true }
            );
            showToast(message, 'success');
            await this.refresh();
        } catch (error) {
            showToast(`恢复默认设置失败: ${error.message}`, 'error');
        }
    }

    /**
     * 收集输入并调用 API
     */
    async save() {
        console.log('[SettingsView] Saving config...');

        if (this.getActiveTabName() === 'station') {
            await this.saveStationCommunicationSettings();
            return;
        }

        const themeSelect = this.container?.querySelector('#cfg-theme');
        const selectedTheme = normalizeTheme(themeSelect?.value, null);
        const currentTheme = normalizeTheme(this.config?.general?.theme, getAppliedTheme());
        const effectiveTheme = selectedTheme || currentTheme;
        const defaultConfig = this.getDefaultConfig();
        const parsedHeartbeatIntervalMs = Number.parseInt(`${this.config?.communication?.heartbeatIntervalMs ?? ''}`, 10);
        const heartbeatIntervalMs = Number.isFinite(parsedHeartbeatIntervalMs) && parsedHeartbeatIntervalMs > 0
            ? parsedHeartbeatIntervalMs
            : defaultConfig.communication.heartbeatIntervalMs;
        const parsedRetentionDays = Number.parseInt(this.container?.querySelector('#cfg-retentionDays')?.value || '', 10);
        const retentionDays = Number.isFinite(parsedRetentionDays) && parsedRetentionDays >= 0
            ? parsedRetentionDays
            : (this.config?.storage?.retentionDays ?? defaultConfig.storage.retentionDays);
        const parsedMinFreeSpaceGb = Number.parseFloat(this.container?.querySelector('#cfg-minFreeSpaceGb')?.value || '');
        const minFreeSpaceGb = Number.isFinite(parsedMinFreeSpaceGb) && parsedMinFreeSpaceGb >= 0
            ? parsedMinFreeSpaceGb
            : (this.config?.storage?.minFreeSpaceGb ?? defaultConfig.storage.minFreeSpaceGb);
        const parsedMissingMaterialTimeoutSeconds = Number.parseInt(this.container?.querySelector('#cfg-missingMaterialTimeoutSeconds')?.value || '', 10);
        const missingMaterialTimeoutSeconds = Number.isFinite(parsedMissingMaterialTimeoutSeconds) && parsedMissingMaterialTimeoutSeconds >= 0
            ? parsedMissingMaterialTimeoutSeconds
            : (this.config?.runtime?.missingMaterialTimeoutSeconds ?? defaultConfig.runtime.missingMaterialTimeoutSeconds);
        const parsedPasswordMinLength = Number.parseInt(this.container?.querySelector('#cfg-passwordMinLength')?.value || '', 10);
        const passwordMinLength = Number.isFinite(parsedPasswordMinLength) && parsedPasswordMinLength >= 6
            ? parsedPasswordMinLength
            : (this.config?.security?.passwordMinLength ?? defaultConfig.security.passwordMinLength);
        const parsedSessionTimeoutMinutes = Number.parseInt(this.container?.querySelector('#cfg-sessionTimeoutMinutes')?.value || '', 10);
        const sessionTimeoutMinutes = Number.isFinite(parsedSessionTimeoutMinutes) && parsedSessionTimeoutMinutes > 0
            ? parsedSessionTimeoutMinutes
            : (this.config?.security?.sessionTimeoutMinutes ?? defaultConfig.security.sessionTimeoutMinutes);
        const parsedLoginFailureLockoutCount = Number.parseInt(this.container?.querySelector('#cfg-loginFailureLockoutCount')?.value || '', 10);
        const loginFailureLockoutCount = Number.isFinite(parsedLoginFailureLockoutCount) && parsedLoginFailureLockoutCount > 0
            ? parsedLoginFailureLockoutCount
            : (this.config?.security?.loginFailureLockoutCount ?? defaultConfig.security.loginFailureLockoutCount);
        const activeCameraId = this.resolveActiveCameraId();

        // 保证“保存所有更改”也会带上当前选中相机的参数修改。
        if (this.selectedCameraBindingId) {
            const selectedBinding = this.cameraBindings.find(b => b.id === this.selectedCameraBindingId);
            const exposureInput = this.container?.querySelector('#cam-param-exposure');
            const gainInput = this.container?.querySelector('#cam-param-gain');
            const pixelFormatSelect = this.container?.querySelector('#cam-param-pixel-format');
            const triggerModeSelect = this.container?.querySelector('#cam-param-trigger-mode');
            const hardwareTriggerSourceSelect = this.container?.querySelector('#cam-param-hardware-trigger-source');
            const triggerSourceSelect = this.container?.querySelector('#cam-param-software-trigger-source');
            const enterDebounceInput = this.container?.querySelector('#cam-param-enter-debounce');
            const enterTimeoutInput = this.container?.querySelector('#cam-param-enter-timeout');
            const enterDeviceInput = this.container?.querySelector('#cam-param-enter-device-id');
            const ignoreBusyInput = this.container?.querySelector('#cam-param-ignore-enter-busy');
            const serialPortInput = this.container?.querySelector('#cam-param-serial-port-name');
            const serialBaudInput = this.container?.querySelector('#cam-param-serial-baud-rate');
            const serialDebounceInput = this.container?.querySelector('#cam-param-serial-debounce');
            const serialTimeoutInput = this.container?.querySelector('#cam-param-serial-timeout');
            const ignoreSerialBusyInput = this.container?.querySelector('#cam-param-ignore-serial-busy');
            const frameRateInput = this.container?.querySelector('#cam-param-target-frame-rate');
            if (selectedBinding && exposureInput && gainInput && pixelFormatSelect && triggerModeSelect && hardwareTriggerSourceSelect && triggerSourceSelect && enterDebounceInput && enterTimeoutInput && enterDeviceInput && ignoreBusyInput && serialPortInput && serialBaudInput && serialDebounceInput && serialTimeoutInput && ignoreSerialBusyInput && frameRateInput) {
                const exposureTimeUs = Number.parseFloat(exposureInput.value);
                const gainDb = Number.parseFloat(gainInput.value);
                if (!Number.isFinite(exposureTimeUs) || exposureTimeUs < 10 || exposureTimeUs > 1000000) {
                    showToast('曝光时间需在 10 - 1000000 µs 范围内', 'warning');
                    return;
                }
                if (!Number.isFinite(gainDb) || gainDb < 0 || gainDb > 24) {
                    showToast('增益需在 0.0 - 24.0 dB 范围内', 'warning');
                    return;
                }
                selectedBinding.exposureTimeUs = exposureTimeUs;
                selectedBinding.gainDb = gainDb;
                selectedBinding.pixelFormat = this.normalizeCameraPixelFormat(pixelFormatSelect.value || 'Mono8');
                selectedBinding.triggerMode = this.normalizeCameraTriggerMode(triggerModeSelect.value || 'Software');
                selectedBinding.hardwareTriggerSource = this.normalizeHardwareTriggerSource(hardwareTriggerSourceSelect.value || 'Line0');
                selectedBinding.softwareTriggerSource = this.normalizeSoftwareTriggerSource(triggerSourceSelect.value || 'Manual');
                selectedBinding.enterPhotoelectricDebounceMs = this.normalizeEnterDebounceMs(enterDebounceInput.value);
                selectedBinding.enterPhotoelectricTimeoutMs = this.normalizeEnterTimeoutMs(enterTimeoutInput.value);
                selectedBinding.enterPhotoelectricDeviceId = String(enterDeviceInput.value || '').trim();
                selectedBinding.ignoreEnterTriggerWhileBusy = ignoreBusyInput.checked !== false;
                selectedBinding.serialPhotoelectricPortName = String(serialPortInput.value || '').trim();
                selectedBinding.serialPhotoelectricBaudRate = this.normalizeSerialBaudRate(serialBaudInput.value);
                selectedBinding.serialPhotoelectricDebounceMs = this.normalizeSerialDebounceMs(serialDebounceInput.value);
                selectedBinding.serialPhotoelectricTimeoutMs = this.normalizeSerialTimeoutMs(serialTimeoutInput.value);
                selectedBinding.ignoreSerialPhotoelectricTriggerWhileBusy = ignoreSerialBusyInput.checked !== false;
                selectedBinding.targetFrameRateFps = this.normalizeCameraTargetFrameRate(frameRateInput.value);
            }
        }
        
        const plcSettingsDraft = this.normalizeCommunicationConfig(
            this.buildPlcSettingsPayload({ persistAllProfiles: true })
        );
        const hasPendingPlcChanges = !this.areCommunicationConfigsEqual(
            plcSettingsDraft,
            this.savedCommunicationConfig || this.config?.communication
        );
        let communicationForSave = plcSettingsDraft;

        if (hasPendingPlcChanges) {
            const plcSaveResult = await this.savePlcSettings({ silent: true, persistAllProfiles: true });
            if (!plcSaveResult?.success) {
                showToast('PLC 配置校验未通过，请先修正当前协议配置。', 'error');
                return;
            }

            communicationForSave = this.normalizeCommunicationConfig(plcSaveResult.settings || plcSettingsDraft);
        } else {
            this.config.communication = this.cloneCommunicationConfig(plcSettingsDraft);
            this.syncPlcMappingsFromActiveProfile();
        }

        const config = {
            general: {
                softwareTitle: this.container?.querySelector('#cfg-softwareTitle')?.value || '',
                theme: effectiveTheme,
                autoStart: this.container?.querySelector('#cfg-autoStart')?.checked || false
            },
            communication: {
                ...this.normalizeCommunicationConfig(communicationForSave),
                heartbeatIntervalMs
            },
            storage: {
                imageSavePath: this.container?.querySelector('#cfg-imageSavePath')?.value || '',
                savePolicy: this.container?.querySelector('#cfg-savePolicy')?.value || 'NgOnly',
                retentionDays,
                minFreeSpaceGb
            },
            runtime: {
                autoRun: this.container?.querySelector('#cfg-autoRun')?.checked || false,
                stopOnConsecutiveNg: parseInt(this.container?.querySelector('#cfg-stopOnConsecutiveNg')?.value || '0', 10),
                missingMaterialTimeoutSeconds,
                applyProtectionRules: this.container?.querySelector('#cfg-applyProtectionRules')?.checked ?? true
            },
            security: {
                passwordMinLength,
                sessionTimeoutMinutes,
                loginFailureLockoutCount
            },
            cameras: this.collectCameraBindings(),
            activeCameraId
        };
        
        try {
            // 先保存相机绑定，避免运行中相机流拒绝参数变更时，全局配置已提前落盘。
            const bindingsSaved = await this.saveCameraBindings({ silent: true });
            if (!bindingsSaved) {
                throw new Error(this.lastCameraBindingSaveError || 'Camera bindings save failed');
            }

            config.cameras = this.collectCameraBindings();
            await httpClient.put('/settings', config);
            this.config = this.normalizeAppConfig(config);
            this.savedCommunicationConfig = this.cloneCommunicationConfig(this.config.communication);

            this.syncPlcMappingsFromActiveProfile();
            this.plcSettingsLoaded = true;

            // 仅在 AI 页显式保存时同步当前模型，避免全局保存触发隐式副作用。
            const activeTabName = this.getActiveTabName();
            const hasPendingAiChanges = this.hasPendingAiChanges();
            if (activeTabName === 'ai' && hasPendingAiChanges) {
                await this._saveCurrentForm();
            }

            console.log('[SettingsView] Config saved successfully');
            if (activeTabName !== 'ai' && hasPendingAiChanges) {
                showToast('系统设置已保存；AI 模型草稿仍保留在 AI 页，需要单独保存。', 'warning');
            } else {
                showToast('所有设置已生效并保存。', 'success');
            }
            await this.loadDiskUsage();
            
            applyTheme(effectiveTheme, { persist: true });
        } catch (error) {
            console.error('[SettingsView] Failed to save config:', error);
            showToast('保存设置通讯错误: ' + error.message, 'error');
        }
    }
}

// 暴露出初始化方法给外界（比如 app.js）
window.initializeSettingsView = function() {
    console.log('[SettingsView] Registering globally...');
    window.cvSettingsView = new SettingsView('settings-view');
    window.cvSettingsView.refresh();
};


