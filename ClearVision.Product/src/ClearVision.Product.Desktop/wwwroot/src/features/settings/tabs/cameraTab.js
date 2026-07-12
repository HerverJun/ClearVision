import settingsApi from '../settingsApi.js';
import { validateCameraParameterDraft } from '../settingsValidators.js';
import { showToast, closeModal } from '../../../shared/components/uiComponents.js';
import inspectionController from '../../inspection/inspectionController.js';

export function installCameraTab(SettingsView) {
    Object.assign(SettingsView.prototype, {
        getRecommendedSerialPhotoelectricPort() {
            return this.serialPhotoelectricPorts.find(port => port.isRecommended)
                || this.serialPhotoelectricPorts.find(port => !/蓝牙|bluetooth/i.test(port.displayName))
                || this.serialPhotoelectricPorts[0]
                || null;
        }
        ,
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
        ,
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
        ,
        async loadSerialPhotoelectricPorts({ silent = false, applyRecommended = false } = {}) {
            const refreshBtn = this.container?.querySelector('#btn-refresh-serial-photoelectric-port');
            const portInput = this.container?.querySelector('#cam-param-serial-port-name');
            const previousText = refreshBtn?.textContent;

            if (refreshBtn) {
                refreshBtn.disabled = true;
                refreshBtn.textContent = '识别中';
            }

            try {
                const response = await settingsApi.listSerialPhotoelectricPorts();
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
        ,
        isEnterPhotoelectricBinding(binding) {
            return this.normalizeCameraTriggerMode(binding?.triggerMode) === 'Software'
                && this.normalizeSoftwareTriggerSource(binding?.softwareTriggerSource) === 'EnterPhotoelectric';
        }
        ,
        isSerialPhotoelectricBinding(binding) {
            return this.normalizeCameraTriggerMode(binding?.triggerMode) === 'Software'
                && this.normalizeSoftwareTriggerSource(binding?.softwareTriggerSource) === 'SerialPhotoelectric';
        }
        ,
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

        ,
        bindCameraManagementEvents() {
            const section = this.container.querySelector('[data-section="cameras"]');
            if (!section) return;

            const discoverHuarayBtn = section.querySelector('#btn-discover-huaray-cameras');
            discoverHuarayBtn?.addEventListener('click', () => this.discoverCameras('huaray', discoverHuarayBtn));

            const discoverHikvisionBtn = section.querySelector('#btn-discover-hikvision-cameras');
            discoverHikvisionBtn?.addEventListener('click', () => this.discoverCameras('hikvision', discoverHikvisionBtn));

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

                        const module = await import('../../../core/calibration/planarScaleOffsetCalibWizard.js');
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
                        const binding = this.cameraBindings.find(item => item.id === id);
                        const label = binding?.displayName || binding?.serialNumber || id;
                        if (confirm(`确定要删除相机绑定“${label}”吗？\n\n删除后会立即保存相机绑定列表；如果相机流正在运行，后端可能拒绝本次操作。`)) {
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
        ,
        async loadCameraBindings() {
            const tbody = this.container.querySelector('#camera-bindings-table tbody');
            if (tbody) {
                tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; padding: 24px;"><div class="cv-spinner" style="margin-right:8px; display:inline-block;"></div>正在加载相机配置...</td></tr>`;
            }

            try {
                const bindings = await settingsApi.listCameraBindings();
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
        ,
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
                const response = await settingsApi.discoverCameras(meta.endpoint);
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
        ,
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

            const eventCleanups = [];
            const cleanupModalEvents = () => {
                eventCleanups.splice(0).forEach(cleanup => cleanup());
            };

            const modal = this.createTrackedModal({
                title: `配置向导：发现${vendorText}相机`,
                content: contentDiv,
                width: '920px',
                onClose: cleanupModalEvents
            });
            const bindBtns = contentDiv.querySelectorAll('.btn-bind-camera');
            bindBtns.forEach(btn => {
                eventCleanups.push(this.lifecycle.trackEvent(btn, 'click', async () => {
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
                        targetFrameRateFps: 30
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
                }));
            });
        }
        ,
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
        ,
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
        ,
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
        ,
        syncCameraFrameRateInputState(triggerMode = null, forceDisabled = false) {
            const frameRateInput = this.container?.querySelector('#cam-param-target-frame-rate');
            const hintEl = this.container?.querySelector('#cam-param-target-frame-rate-hint');
            if (!frameRateInput) return;

            const effectiveTriggerMode = this.normalizeCameraTriggerMode(
                triggerMode ?? this.container?.querySelector('#cam-param-trigger-mode')?.value
            );
            const disabled = forceDisabled || (effectiveTriggerMode !== 'Continuous' && effectiveTriggerMode !== 'External');
            frameRateInput.disabled = disabled;
            frameRateInput.readOnly = disabled;
            frameRateInput.setAttribute('aria-disabled', disabled ? 'true' : 'false');

            if (hintEl) {
                hintEl.textContent = disabled
                    ? '仅帧驱动模式下可编辑；当前值会保留。'
                    : '帧驱动模式下按该目标 fps 在应用侧节流。';
            }
        }
        ,
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
        ,
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
        ,
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
                const result = await settingsApi.learnEnterTriggerDevice({ timeoutMs: 10000 });
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
        ,
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
                const result = await settingsApi.testSerialPhotoelectric({
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
        ,
        getSelectedCameraBinding() {
            if (!this.selectedCameraBindingId) {
                return null;
            }

            return this.cameraBindings.find(binding => binding.id === this.selectedCameraBindingId) || null;
        }
        ,
        async startContinuousPreviewSession(cameraBindingId) {
            return await settingsApi.startContinuousPreview({
                cameraBindingId
            });
        }
        ,
        async stopContinuousPreviewSession(sessionId) {
            if (!sessionId) return;
            try {
                await settingsApi.stopContinuousPreview({ sessionId });
            } catch (error) {
                console.warn('[SettingsView] Failed to stop continuous preview session:', error);
            }
        }
        ,
        async fetchContinuousPreviewFrame(sessionId, options = {}) {
            const cacheKey = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
            const { blob, headers } = await settingsApi.fetchContinuousPreviewFrame(sessionId, cacheKey, {
                signal: options.signal
            });
            if (!blob || blob.size === 0) {
                throw new Error('连续预览未返回图像数据');
            }

            const imageUrl = this.lifecycle.trackObjectUrl(URL.createObjectURL(blob));
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
        ,
        async captureSharedFrame(cameraBindingId, options = {}) {
            const session = await this.startContinuousPreviewSession(cameraBindingId);
            try {
                const preview = await this.fetchContinuousPreviewFrame(session.sessionId || session.SessionId, {
                    signal: options.signal
                });
                return {
                    ...preview,
                    triggerMode: this.normalizeCameraTriggerMode(session.triggerMode || session.TriggerMode),
                    targetFrameRateFps: this.normalizeCameraTargetFrameRate(
                        session.targetFrameRateFps || session.TargetFrameRateFps || preview.targetFrameRateFps
                    ),
                    cameraBindingId
                };
            } finally {
                await this.stopContinuousPreviewSession(session.sessionId || session.SessionId);
            }
        }
        ,
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

            const { blob, headers } = await settingsApi.softTriggerCapture(request, {
                signal: options.signal
            });

            if (!blob || blob.size === 0) {
                throw new Error('预览接口未返回图像数据');
            }

            const imageUrl = this.lifecycle.trackObjectUrl(URL.createObjectURL(blob));
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
        ,
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
                    this.lifecycle.revokeObjectUrl(currentPreviewUrl);
                    currentPreviewUrl = null;
                }
            };

            const cancelActivePreviewRequest = () => {
                if (!activePreviewAbortController) {
                    return;
                }

                activePreviewAbortController.abort();
                this.lifecycle.untrackAbortController(activePreviewAbortController);
                activePreviewAbortController = null;
            };

            const stopPreview = async () => {
                previewActive = false;
                previewLoopToken += 1;
                cancelActivePreviewRequest();
                await this.stopContinuousPreviewSession(sessionId);
                sessionId = null;
            };

            const eventCleanups = [];
            const cleanupPreviewEvents = () => {
                eventCleanups.splice(0).forEach(cleanup => cleanup());
            };

            const modal = this.createTrackedModal({
                title: `相机预览 - ${binding.displayName || binding.serialNumber || binding.id}`,
                content,
                width: '960px',
                onClose: async () => {
                    cleanupPreviewEvents();
                    await stopPreview();
                    cleanupPreviewUrl();
                }
            });
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
                const targetFpsText = preview.targetFrameRateFps ?? '--';
                const actualFpsText = Number.isFinite(preview.actualFrameRateFps)
                    ? preview.actualFrameRateFps.toFixed(1)
                    : '--';
                metaEl.textContent = `触发模式: ${triggerModeLabel} · 目标帧率: ${targetFpsText} fps · 实际帧率: ${actualFpsText} fps · 分辨率: ${preview.width ?? '--'} x ${preview.height ?? '--'}${preview.sequence ? ` · 序号: ${preview.sequence}` : ''}`;
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
                    const sessionTargetFrameRateFps = this.normalizeCameraTargetFrameRate(
                        session.targetFrameRateFps || session.TargetFrameRateFps || binding.targetFrameRateFps
                    );
                    const previewFrameTimes = [];

                    while (previewActive && sessionId && loopToken === previewLoopToken) {
                        const abortController = typeof AbortController !== 'undefined'
                            ? this.lifecycle.trackAbortController(new AbortController())
                            : null;
                        activePreviewAbortController = abortController;
                        const preview = await this.fetchContinuousPreviewFrame(sessionId, {
                            signal: abortController?.signal
                        });
                        if (activePreviewAbortController === abortController) {
                            this.lifecycle.untrackAbortController(abortController);
                            activePreviewAbortController = null;
                        }
                        if (!previewActive || loopToken !== previewLoopToken) {
                            this.lifecycle.revokeObjectUrl(preview.imageUrl);
                            break;
                        }

                        const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
                        previewFrameTimes.push(now);
                        while (previewFrameTimes.length > 1 && now - previewFrameTimes[0] > 3000) {
                            previewFrameTimes.shift();
                        }
                        const elapsedMs = previewFrameTimes.length > 1
                            ? previewFrameTimes[previewFrameTimes.length - 1] - previewFrameTimes[0]
                            : 0;
                        const actualFrameRateFps = elapsedMs > 0
                            ? ((previewFrameTimes.length - 1) * 1000) / elapsedMs
                            : null;

                        renderFrame({
                            ...preview,
                            targetFrameRateFps: sessionTargetFrameRateFps,
                            actualFrameRateFps
                        });
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

            eventCleanups.push(this.lifecycle.trackEvent(toggleBtn, 'click', async () => {
                if (previewActive) {
                    await stopPreview();
                    toggleBtn.textContent = '继续预览';
                    placeholderEl.style.display = 'block';
                    placeholderEl.textContent = stoppedText;
                    imageEl.style.display = currentPreviewUrl ? 'block' : 'none';
                    return;
                }

                await startPreview();
            }));

            await startPreview();
        }
        ,
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
                    this.lifecycle.revokeObjectUrl(currentPreviewUrl);
                    currentPreviewUrl = null;
                }
            };

            const cancelActivePreviewRequest = () => {
                if (!activePreviewAbortController) {
                    return;
                }

                activePreviewAbortController.abort();
                this.lifecycle.untrackAbortController(activePreviewAbortController);
                activePreviewAbortController = null;
            };

            const eventCleanups = [];
            const cleanupPreviewEvents = () => {
                eventCleanups.splice(0).forEach(cleanup => cleanup());
            };

            const modal = this.createTrackedModal({
                title: `相机预览 - ${binding.displayName || binding.serialNumber || binding.id}`,
                content,
                width: '960px',
                onClose: () => {
                    cleanupPreviewEvents();
                    previewClosed = true;
                    previewRequestId += 1;
                    cancelActivePreviewRequest();
                    if (autoRearmTimer) {
                        this.clearTrackedTimeout(autoRearmTimer);
                        autoRearmTimer = null;
                    }
                    cleanupPreviewUrl();
                }
            });
            const refreshBtn = content.querySelector('#btn-refresh-camera-preview');
            const previewSurfaceEl = content.querySelector('#camera-preview-surface');
            const imageEl = content.querySelector('#camera-preview-image');
            const placeholderEl = content.querySelector('#camera-preview-placeholder');
            const metaEl = content.querySelector('#camera-preview-meta');

            const focusPreviewSurface = () => {
                previewSurfaceEl?.focus({ preventScroll: true });
            };

            if (isEnterPhotoelectric) {
                eventCleanups.push(this.lifecycle.trackEvent(content, 'keydown', event => {
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
                }, true));
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

            const rearmNextPreview = () => {
                if (!isPhotoelectricTrigger || previewClosed) {
                    return;
                }

                if (autoRearmTimer) {
                    this.clearTrackedTimeout(autoRearmTimer);
                }

                autoRearmTimer = this.setTrackedTimeout(() => {
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
                    ? this.lifecycle.trackAbortController(new AbortController())
                    : null;
                activePreviewAbortController = abortController;

                try {
                    const preview = await this.captureCameraPreview(binding.id, {
                        acceptPendingEnterSignalAfterUtc,
                        signal: abortController?.signal
                    });
                    if (previewClosed || requestId !== previewRequestId) {
                        this.lifecycle.revokeObjectUrl(preview.imageUrl);
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
                        this.lifecycle.untrackAbortController(abortController);
                        activePreviewAbortController = null;
                    }

                    if (shouldAutoRearm) {
                        rearmNextPreview();
                    }
                }
            };

            eventCleanups.push(this.lifecycle.trackEvent(refreshBtn, 'click', event => {
                event.preventDefault();
                focusPreviewSurface();
                void loadPreview();
            }));

            focusPreviewSurface();
            void loadPreview();
        }
        ,
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
            const enterPhotoelectricDebounceMs = Number.parseInt(String(enterDebounceInput.value ?? ''), 10);
            const enterPhotoelectricTimeoutMs = Number.parseInt(String(enterTimeoutInput.value ?? ''), 10);
            const enterPhotoelectricDeviceId = String(enterDeviceInput.value || '').trim();
            const ignoreEnterTriggerWhileBusy = ignoreBusyInput.checked !== false;
            const serialPhotoelectricPortName = String(serialPortInput.value || '').trim();
            const serialPhotoelectricBaudRate = Number.parseInt(String(serialBaudInput.value ?? ''), 10);
            const serialPhotoelectricDebounceMs = Number.parseInt(String(serialDebounceInput.value ?? ''), 10);
            const serialPhotoelectricTimeoutMs = Number.parseInt(String(serialTimeoutInput.value ?? ''), 10);
            const ignoreSerialPhotoelectricTriggerWhileBusy = ignoreSerialBusyInput.checked !== false;
            const targetFrameRateFps = Number.parseInt(String(frameRateInput.value ?? ''), 10);

            const validation = validateCameraParameterDraft({
                exposureTimeUs,
                gainDb,
                enterPhotoelectricDebounceMs,
                enterPhotoelectricTimeoutMs,
                serialPhotoelectricBaudRate,
                serialPhotoelectricDebounceMs,
                serialPhotoelectricTimeoutMs,
                softwareTriggerSource,
                serialPhotoelectricPortName,
                triggerMode,
                targetFrameRateFps
            });
            if (!validation.ok) {
                showToast(validation.message, 'warning');
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
            binding.targetFrameRateFps = triggerMode === 'Software'
                ? this.normalizeCameraTargetFrameRate(binding.targetFrameRateFps)
                : targetFrameRateFps;

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

        ,
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
                ${this.renderScopeNotice('cameras')}

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
                    <div class="settings-card-header" style="background:var(--bg-surface); display:flex; justify-content:space-between;">
                        <div class="settings-header-left">
                            <svg viewBox="0 0 24 24" class="settings-header-icon" style="fill:#94a3b8;"><path d="M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z"/></svg>
                            <span>参数配置: <span id="current-cam-name">未选择相机</span></span>
                        </div>
                    </div>
                    <div class="settings-card-body">
                        <div id="camera-selection-hint" class="settings-field-hint" style="display:block; margin-bottom:16px;">
                            请先在上方绑定列表中选择一台相机，再进行预览、二维平面标定或参数保存。
                        </div>
                        <div style="display:grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap:24px; margin-bottom: 16px;">
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
                        <div style="display:grid; grid-template-columns:minmax(280px, 1fr); gap:24px; margin-bottom: 24px;">
                            <div class="settings-fieldset">
                                <label>采集帧率 (Frame Rate)</label>
                                <div class="input-with-suffix" style="position:relative;">
                                    <input type="number" class="cv-input" id="cam-param-target-frame-rate" value="" min="1" max="120" style="padding-right:36px;" disabled readonly aria-disabled="true">
                                    <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">fps</span>
                                </div>
                                <span class="settings-field-hint" id="cam-param-target-frame-rate-hint">帧驱动模式（Continuous / External）下可编辑；默认 30 fps，范围 1 - 120。</span>
                            </div>
                        </div>
                        <div style="border-top:1px solid var(--border-color); padding-top:20px; margin-bottom:24px;">
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
                        <div style="display:grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap:24px;">
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
                    <div class="settings-card-body" style="border-top:1px solid var(--border-color); display:flex; justify-content:flex-end; gap:12px; padding:16px 24px;">
                        <button class="cv-btn settings-btn-light" id="btn-reset-camera-params">重置当前值</button>
                        <button class="cv-btn settings-btn-danger" id="btn-save-camera-params">
                            <svg viewBox="0 0 24 24" style="width:16px; height:16px; margin-right:6px; fill:currentColor;"><path d="M17 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z"/></svg>
                            保存当前相机参数
                        </button>
                    </div>
                </div>
            `;
        }
        ,
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
        ,
        syncActiveCameraSelection() {
            inspectionController.setCamera(this.resolveActiveCameraId() || null);
        }
        ,
        resolveActiveCameraId() {
            const preferredActiveId = this.config?.activeCameraId || '';
            if (this.cameraBindings.some(b => b.id === preferredActiveId)) {
                return preferredActiveId;
            }
            return this.cameraBindings[0]?.id || '';
        }
        ,
        async saveCameraBindings({ silent = false } = {}) {
            const activeCameraId = this.resolveActiveCameraId();
            const bindingsPayload = this.collectCameraBindings();
            this.lastCameraBindingSaveError = null;

            try {
                await settingsApi.saveCameraBindings({
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
        ,
        async saveCameraSettingsFromTop() {
            if (this.selectedCameraBindingId) {
                const saved = await this.saveSelectedCameraParameters();
                if (saved) {
                    await this.saveAppSettingsForTab('cameras');
                }
                return;
            }

            const saved = await this.saveCameraBindings();
            if (saved) {
                await this.saveAppSettingsForTab('cameras');
            }
            if (saved) {
                showToast('相机绑定配置已保存。', 'success');
            }
        }

    });
}
