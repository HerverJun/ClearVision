import settingsApi from '../settingsApi.js';
import { validatePlcConnectionDraft, assertValidation } from '../settingsValidators.js';
import { showToast } from '../../../shared/components/uiComponents.js';

export function installPlcTab(SettingsView) {
    Object.assign(SettingsView.prototype, {
        getActivePlcProtocol() {
            return this.normalizePlcProtocol(this.config?.communication?.activeProtocol);
        }
        ,
        getActivePlcProfile() {
            const communication = this.normalizeCommunicationConfig(this.config?.communication);
            return communication[this.getPlcProfileKey(communication.activeProtocol)];
        }
        ,
        syncPlcMappingsFromActiveProfile() {
            const profile = this.getActivePlcProfile();
            this.plcMappings = this.normalizePlcMappings(profile?.mappings);
        }
        ,
        bindPlcSettingsEvents() {
            const communicationTab = this.container?.querySelector('[data-section="communication"]');
            if (!communicationTab || communicationTab.dataset.boundPlcEvents === 'true') return;

            communicationTab.dataset.boundPlcEvents = 'true';
            const connectionFieldIds = new Set([
                'cfg-plcIpAddress',
                'cfg-plcPort',
                'cfg-s7-cpuType',
                'cfg-s7-rack',
                'cfg-s7-slot'
            ]);

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
                if (connectionFieldIds.has(target.id)) {
                    this.plcDraftDirty = true;
                    this.syncActivePlcProfileDraft(this.getActivePlcProtocol());
                    return;
                }
                updateField(target);
            });

            communicationTab.addEventListener('change', (e) => {
                const target = e.target;
                if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement)) return;
                if (target.id === 'cfg-protocol') {
                    const previousProtocol = this.getActivePlcProtocol();
                    this.plcDraftDirty = true;
                    this.syncActivePlcProfileDraft(previousProtocol);
                    this.config.communication.activeProtocol = this.normalizePlcProtocol(target.value);
                    this.syncPlcMappingsFromActiveProfile();
                    this.plcValidationErrors = [];
                    this.plcConnectionStatus = 'unknown';
                    this.refreshCommunicationPanel();
                    return;
                }
                if (connectionFieldIds.has(target.id)) {
                    this.plcDraftDirty = true;
                    this.syncActivePlcProfileDraft(this.getActivePlcProtocol());
                    return;
                }
                updateField(target);
            });
        }
        ,
        refreshCommunicationPanel() {
            const communicationPanel = this.container?.querySelector('[data-section="communication"]');
            if (!communicationPanel) return;

            this.syncPlcMappingsFromActiveProfile();
            communicationPanel.innerHTML = this.renderCommunicationTab();
            this.renderPlcMappingsTable();
            this.updatePlcConnectionBadge(this.plcConnectionStatus);
        }
        ,
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

            const normalizedProfile = this.normalizePlcProfile(nextProfile, defaults, protocol === 'S7');
            communication.activeProtocol = this.normalizePlcProtocol(protocol);
            communication[profileKey] = normalizedProfile;
            this.plcProfileDrafts = {
                ...(this.plcProfileDrafts || {}),
                [profileKey]: this.cloneCommunicationConfig(normalizedProfile)
            };
            this.config.communication = communication;
            this.syncPlcMappingsFromActiveProfile();
        }
        ,
        mergePlcProfileDrafts(communication) {
            const merged = this.cloneCommunicationConfig(communication);
            const drafts = this.plcProfileDrafts || {};
            ['s7', 'mc', 'fins'].forEach(key => {
                if (drafts[key]) {
                    merged[key] = this.cloneCommunicationConfig(drafts[key]);
                }
            });
            return merged;
        }
        ,
        buildPlcSettingsPayload({ persistAllProfiles = false } = {}) {
            this.syncActivePlcProfileDraft(this.getActivePlcProtocol());
            const workingCommunication = this.mergePlcProfileDrafts(this.config.communication);
            if (persistAllProfiles) {
                return workingCommunication;
            }

            const savedCommunication = this.mergePlcProfileDrafts(this.savedCommunicationConfig || this.getDefaultConfig().communication);
            const activeProtocol = this.getActivePlcProtocol();
            const profileKey = this.getPlcProfileKey(activeProtocol);
            savedCommunication.activeProtocol = activeProtocol;
            savedCommunication.heartbeatIntervalMs = workingCommunication.heartbeatIntervalMs;
            savedCommunication[profileKey] = this.cloneCommunicationConfig(workingCommunication[profileKey]);
            return savedCommunication;
        }
        ,
        areCommunicationConfigsEqual(left, right) {
            const normalizedLeft = this.normalizeCommunicationConfig(left);
            const normalizedRight = this.normalizeCommunicationConfig(right);
            return JSON.stringify(normalizedLeft) === JSON.stringify(normalizedRight);
        }
        ,
        async loadPlcSettings({ force = false } = {}) {
            if (!force && this.plcSettingsLoaded) {
                this.refreshCommunicationPanel();
                return;
            }

            try {
                const result = await settingsApi.loadPlcSettings();
                const settings = this.normalizeCommunicationConfig(result?.settings || result);
                if (!force && this.plcDraftDirty) {
                    return;
                }
                this.savedCommunicationConfig = this.cloneCommunicationConfig(settings);
                this.config.communication = this.cloneCommunicationConfig(settings);
                this.plcProfileDrafts = {};
                this.plcDraftDirty = false;
                this.plcValidationErrors = [];
                this.plcSettingsLoaded = true;
                this.syncPlcMappingsFromActiveProfile();
                this.refreshCommunicationPanel();
            } catch (error) {
                console.error('[SettingsView] Failed to load PLC settings:', error);
                showToast('加载PLC配置失败: ' + error.message, 'error');
            }
        }
        ,
        getCurrentProtocolValidationErrors() {
            const protocol = this.getActivePlcProtocol();
            return (this.plcValidationErrors || []).filter(error => this.normalizePlcProtocol(error?.protocol) === protocol);
        }
        ,
        getPlcFieldErrors(section, field, index = null) {
            return this.getCurrentProtocolValidationErrors().filter(error => {
                if (`${error?.section || ''}` !== section) return false;
                if (`${error?.field || ''}` !== field) return false;
                if (index === null) return error?.index === undefined || error?.index === null;
                return Number.parseInt(`${error?.index ?? ''}`, 10) === index;
            });
        }
        ,
        renderPlcErrorText(errors) {
            if (!Array.isArray(errors) || errors.length === 0) return '';
            return `<div class="plc-field-error">${errors.map(error => this.escapeHtml(error?.message || '')).join('<br>')}</div>`;
        }
        ,
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
        ,
        addPlcMapping() {
            if (!Array.isArray(this.plcMappings)) {
                this.plcMappings = [];
            }

            this.plcDraftDirty = true;
            this.plcMappings.push({
                name: '',
                address: '',
                dataType: 'Bool',
                description: '',
                canWrite: false
            });

            this.renderPlcMappingsTable();
        }
        ,
        deletePlcMapping(index) {
            if (!Array.isArray(this.plcMappings)) return;
            if (index < 0 || index >= this.plcMappings.length) return;
            this.plcDraftDirty = true;
            this.plcMappings.splice(index, 1);
            this.renderPlcMappingsTable();
        }
        ,
        updatePlcMappingField(index, field, element) {
            if (!Array.isArray(this.plcMappings)) return;
            if (!this.plcMappings[index]) return;

            this.plcDraftDirty = true;
            if (field === 'canWrite') {
                this.plcMappings[index].canWrite = `${element.value}` === 'true';
                return;
            }

            this.plcMappings[index][field] = element.value || '';
        }
        ,
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
        ,
        validateActivePlcConnectionForm() {
            const protocol = this.getActivePlcProtocol();
            const ipAddress = String(this.container?.querySelector('#cfg-plcIpAddress')?.value || '').trim();
            assertValidation(validatePlcConnectionDraft({
                protocol,
                ipAddress,
                port: this.container?.querySelector('#cfg-plcPort')?.value || '',
                rack: this.container?.querySelector('#cfg-s7-rack')?.value || '',
                slot: this.container?.querySelector('#cfg-s7-slot')?.value || ''
            }));
        }
        ,
        async savePlcSettings({ silent = false, persistAllProfiles = false } = {}) {
            let payload;
            try {
                this.validateActivePlcConnectionForm();
                payload = this.buildPlcSettingsPayload({ persistAllProfiles });
            } catch (error) {
                if (!silent) {
                    showToast(error.message, 'warning');
                }
                return { success: false, settings: null };
            }

            try {
                const result = await settingsApi.savePlcSettings(payload);
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
                this.plcDraftDirty = false;
                this.plcProfileDrafts = {};
                if (this.buildAppConfigForSave) {
                    const appConfig = this.buildAppConfigForSave('communication');
                    await settingsApi.saveSettings(appConfig);
                    this.config = this.normalizeAppConfig(appConfig);
                    this.savedCommunicationConfig = this.cloneCommunicationConfig(this.config.communication);
                }
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
        ,
        async testPlcConnection() {
            try {
                this.validateActivePlcConnectionForm();
            } catch (error) {
                showToast(error.message, 'warning');
                return;
            }

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
                const result = await settingsApi.testPlcConnection(payload);
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
        ,
        getPlcConnectionBadgeMeta(status) {
            if (status === 'connected') {
                return { className: 'status-connected', text: '连接正常' };
            }

            if (status === 'failed') {
                return { className: 'status-disconnected', text: '连接失败' };
            }

            return { className: 'status-disconnected', text: '未测试' };
        }
        ,
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
        ,
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
                ${this.renderScopeNotice('communication')}

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

    });
}
