import settingsApi from '../settingsApi.js';
import { assertValidation, validateTcpProfileDraft } from '../settingsValidators.js';
import { showToast } from '../../../shared/components/uiComponents.js';

export function installTcpTab(SettingsView) {
    Object.assign(SettingsView.prototype, {
        getSelectedTcpProfile() {
            if (!Array.isArray(this.tcpProfiles) || this.tcpProfiles.length === 0) {
                return null;
            }

            return this.tcpProfiles.find(profile => profile.id === this.selectedTcpProfileId) || this.tcpProfiles[0];
        }
        ,
        createDefaultTcpProfile() {
            const id = `tcp_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 6)}`;
            return this.normalizeTcpProfile({
                id,
                name: `TCP Profile ${Array.isArray(this.tcpProfiles) ? this.tcpProfiles.length + 1 : 1}`,
                enabled: true,
                mode: 'Client',
                remoteHost: '127.0.0.1',
                remotePort: 9000,
                localHost: '127.0.0.1',
                localPort: 9001,
                encoding: 'UTF8',
                frameMode: 'Raw',
                lineEnding: 'None',
                timeoutMs: 5000,
                keepAlive: false,
                reconnect: true,
                connectOnStartup: false,
                description: ''
            });
        }
        ,
        bindTcpSettingsEvents() {
            const tcpTab = this.container?.querySelector('[data-section="tcp"]');
            if (!tcpTab || tcpTab.dataset.boundTcpEvents === 'true') return;

            tcpTab.dataset.boundTcpEvents = 'true';
            tcpTab.addEventListener('click', async (e) => {
                const profileItem = e.target.closest('[data-tcp-profile-id]');
                const button = e.target.closest('button');
                if (profileItem && !button) {
                    this.selectTcpProfile(profileItem.dataset.tcpProfileId);
                    return;
                }

                if (!button) return;

                if (button.dataset.action === 'select-tcp-profile') {
                    this.selectTcpProfile(button.dataset.profileId);
                    return;
                }

                switch (button.id) {
                    case 'btn-add-tcp-profile':
                        this.addTcpProfile();
                        return;
                    case 'btn-delete-tcp-profile':
                        this.deleteSelectedTcpProfile();
                        return;
                    case 'btn-save-tcp':
                        await this.saveTcpSettings();
                        return;
                    case 'btn-tcp-connect':
                        await this.connectSelectedTcpProfile();
                        return;
                    case 'btn-tcp-disconnect':
                        await this.disconnectSelectedTcpProfile();
                        return;
                    case 'btn-tcp-server-start':
                        await this.startSelectedTcpServer();
                        return;
                    case 'btn-tcp-server-stop':
                        await this.stopSelectedTcpServer();
                        return;
                    case 'btn-tcp-send-text':
                        await this.sendTcpPayload(false);
                        return;
                    case 'btn-tcp-send-hex':
                        await this.sendTcpPayload(true);
                        return;
                    case 'btn-tcp-clear-frames':
                        await this.clearSelectedTcpFrames();
                        return;
                    case 'btn-tcp-refresh':
                        await this.refreshSelectedTcpRuntimeState();
                        return;
                    default:
                        break;
                }
            });

            const updateDraft = (target) => {
                if (!target?.dataset?.tcpField) return;
                this.updateSelectedTcpProfileDraft(target.dataset.tcpField, target);
            };

            tcpTab.addEventListener('input', (e) => updateDraft(e.target));
            tcpTab.addEventListener('change', (e) => {
                const target = e.target;
                updateDraft(target);
                if (target?.dataset?.tcpField === 'mode' || target?.dataset?.tcpField === 'frameMode') {
                    this.refreshTcpPanel();
                }
            });
        }
        ,
        async loadTcpSettings({ force = false } = {}) {
            if (!force && this.tcpSettingsLoaded) {
                this.refreshTcpPanel();
                await this.refreshSelectedTcpRuntimeState({ silent: true });
                return;
            }

            try {
                const result = await settingsApi.listTcpProfiles();
                const profiles = Array.isArray(result?.profiles) ? result.profiles : [];
                this.tcpProfiles = profiles.map(profile => this.normalizeTcpProfile(profile));
                this.selectedTcpProfileId = this.tcpProfiles[0]?.id || null;
                this.tcpSettingsLoaded = true;
                this.tcpDraftDirty = false;
                this.refreshTcpPanel();
                await this.refreshSelectedTcpRuntimeState({ silent: true });
            } catch (error) {
                console.error('[SettingsView] Failed to load TCP profiles:', error);
                showToast('加载 TCP Profile 失败: ' + error.message, 'error');
            }
        }
        ,
        refreshTcpPanel() {
            const tcpPanel = this.container?.querySelector('[data-section="tcp"]');
            if (!tcpPanel) return;
            tcpPanel.innerHTML = this.renderTcpTab();
        }
        ,
        selectTcpProfile(profileId) {
            this.syncSelectedTcpProfileDraft();
            this.selectedTcpProfileId = profileId;
            this.refreshTcpPanel();
            this.refreshSelectedTcpRuntimeState({ silent: true });
        }
        ,
        addTcpProfile() {
            if (!Array.isArray(this.tcpProfiles)) {
                this.tcpProfiles = [];
            }

            const profile = this.createDefaultTcpProfile();
            this.tcpProfiles.push(profile);
            this.selectedTcpProfileId = profile.id;
            this.tcpDraftDirty = true;
            this.refreshTcpPanel();
        }
        ,
        deleteSelectedTcpProfile() {
            const selected = this.getSelectedTcpProfile();
            if (!selected || !Array.isArray(this.tcpProfiles)) return;

            this.tcpProfiles = this.tcpProfiles.filter(profile => profile.id !== selected.id);
            delete this.tcpStatusById[selected.id];
            delete this.tcpFramesById[selected.id];
            this.selectedTcpProfileId = this.tcpProfiles[0]?.id || null;
            this.tcpDraftDirty = true;
            this.refreshTcpPanel();
        }
        ,
        updateSelectedTcpProfileDraft(field, element) {
            const profile = this.getSelectedTcpProfile();
            if (!profile) return;

            this.tcpDraftDirty = true;
            if (['enabled', 'keepAlive', 'reconnect', 'connectOnStartup'].includes(field)) {
                profile[field] = !!element.checked;
                return;
            }

            if (['remotePort', 'localPort', 'timeoutMs', 'fixedLength'].includes(field)) {
                profile[field] = Number.parseInt(element.value || '0', 10) || 0;
                return;
            }

            profile[field] = element.value || '';
            if (field === 'name') {
                profile.name = profile.name.trimStart();
            }
        }
        ,
        syncSelectedTcpProfileDraft() {
            const profile = this.getSelectedTcpProfile();
            const panel = this.container?.querySelector('[data-section="tcp"]');
            if (!profile || !panel) return;

            panel.querySelectorAll('[data-tcp-field]').forEach(element => {
                this.updateSelectedTcpProfileDraft(element.dataset.tcpField, element);
            });
        }
        ,
        buildTcpProfilesPayload() {
            this.syncSelectedTcpProfileDraft();
            return (Array.isArray(this.tcpProfiles) ? this.tcpProfiles : [])
                .map(profile => this.normalizeTcpProfile(profile));
        }
        ,
        validateTcpProfilesForSave(profiles) {
            profiles.forEach(profile => {
                assertValidation(validateTcpProfileDraft(profile));
            });

            const seen = new Set();
            profiles.forEach(profile => {
                const key = String(profile.id || '').trim().toLowerCase();
                if (seen.has(key)) {
                    throw new Error('TCP Profile Id 不能重复。');
                }
                seen.add(key);
            });
        }
        ,
        async saveTcpSettings({ silent = false } = {}) {
            let payload;
            try {
                payload = this.buildTcpProfilesPayload();
                this.validateTcpProfilesForSave(payload);
            } catch (error) {
                if (!silent) {
                    showToast(error.message, 'warning');
                }
                return { success: false, profiles: null };
            }

            try {
                const result = await settingsApi.saveTcpProfiles(payload);
                const success = !!result?.success;
                const profiles = Array.isArray(result?.profiles) ? result.profiles : payload;
                this.tcpProfiles = profiles.map(profile => this.normalizeTcpProfile(profile));
                if (!this.tcpProfiles.some(profile => profile.id === this.selectedTcpProfileId)) {
                    this.selectedTcpProfileId = this.tcpProfiles[0]?.id || null;
                }
                this.tcpDraftDirty = !success;
                this.tcpSettingsLoaded = true;
                this.refreshTcpPanel();

                if (!success) {
                    if (!silent) {
                        showToast(result?.message || 'TCP Profile 校验失败', 'error');
                    }
                    return { success: false, profiles: this.tcpProfiles };
                }

                if (!silent) {
                    showToast(result?.message || 'TCP Profile 已保存', 'success');
                }

                return { success: true, profiles: this.tcpProfiles };
            } catch (error) {
                console.error('[SettingsView] Failed to save TCP profiles:', error);
                if (!silent) {
                    showToast('保存 TCP Profile 失败: ' + error.message, 'error');
                }
                return { success: false, profiles: null };
            }
        }
        ,
        async ensureTcpProfileSavedForOperation() {
            const result = await this.saveTcpSettings({ silent: true });
            if (!result.success) {
                showToast('请先修正并保存当前 TCP Profile。', 'warning');
                return null;
            }

            const selected = this.getSelectedTcpProfile();
            if (!selected) {
                showToast('请先创建 TCP Profile。', 'warning');
                return null;
            }

            return selected;
        }
        ,
        async connectSelectedTcpProfile() {
            const selected = await this.ensureTcpProfileSavedForOperation();
            if (!selected) return;

            try {
                const result = await settingsApi.connectTcpProfile(selected.id);
                showToast(result?.message || 'TCP 已连接', 'success');
                await this.refreshSelectedTcpRuntimeState({ forceProfileId: selected.id, silent: true });
            } catch (error) {
                showToast('TCP 连接失败: ' + error.message, 'error');
                await this.refreshSelectedTcpRuntimeState({ forceProfileId: selected.id, silent: true });
            }
        }
        ,
        async disconnectSelectedTcpProfile() {
            const selected = this.getSelectedTcpProfile();
            if (!selected) return;

            try {
                const result = await settingsApi.disconnectTcpProfile(selected.id);
                showToast(result?.message || 'TCP 已断开', 'success');
                await this.refreshSelectedTcpRuntimeState({ forceProfileId: selected.id, silent: true });
            } catch (error) {
                showToast('断开 TCP 失败: ' + error.message, 'error');
            }
        }
        ,
        async startSelectedTcpServer() {
            const selected = await this.ensureTcpProfileSavedForOperation();
            if (!selected) return;

            try {
                const result = await settingsApi.startTcpServer(selected.id);
                showToast(result?.message || 'TCP Server 已启动', 'success');
                await this.refreshSelectedTcpRuntimeState({ forceProfileId: selected.id, silent: true });
            } catch (error) {
                showToast('启动 TCP Server 失败: ' + error.message, 'error');
                await this.refreshSelectedTcpRuntimeState({ forceProfileId: selected.id, silent: true });
            }
        }
        ,
        async stopSelectedTcpServer() {
            const selected = this.getSelectedTcpProfile();
            if (!selected) return;

            try {
                const result = await settingsApi.stopTcpServer(selected.id);
                showToast(result?.message || 'TCP Server 已停止', 'success');
                await this.refreshSelectedTcpRuntimeState({ forceProfileId: selected.id, silent: true });
            } catch (error) {
                showToast('停止 TCP Server 失败: ' + error.message, 'error');
            }
        }
        ,
        async sendTcpPayload(isHex) {
            const payload = this.container?.querySelector(isHex ? '#tcp-send-hex' : '#tcp-send-text')?.value || '';
            const waitResponse = this.container?.querySelector('#tcp-wait-response')?.checked !== false;
            const selected = await this.ensureTcpProfileSavedForOperation();
            if (!selected) return;

            try {
                const result = await settingsApi.sendTcpProfile(selected.id, {
                    payload,
                    isHex,
                    waitResponse,
                    responseTimeoutMs: selected.timeoutMs
                });
                const responseText = result?.response || '';
                const responseBox = this.container?.querySelector('#tcp-last-response');
                if (responseBox) {
                    responseBox.textContent = responseText || result?.message || '';
                }
                showToast(result?.message || 'TCP 发送成功', 'success');
                await this.refreshSelectedTcpRuntimeState({ forceProfileId: selected.id, silent: true });
            } catch (error) {
                showToast('TCP 发送失败: ' + error.message, 'error');
                await this.refreshSelectedTcpRuntimeState({ forceProfileId: selected.id, silent: true });
            }
        }
        ,
        async clearSelectedTcpFrames() {
            const selected = this.getSelectedTcpProfile();
            if (!selected) return;

            try {
                await settingsApi.clearTcpProfileFrames(selected.id);
                this.tcpFramesById[selected.id] = [];
                this.refreshTcpPanel();
            } catch (error) {
                showToast('清空 TCP 日志失败: ' + error.message, 'error');
            }
        }
        ,
        async refreshSelectedTcpRuntimeState({ forceProfileId = null, silent = false } = {}) {
            const selected = forceProfileId
                ? this.tcpProfiles.find(profile => profile.id === forceProfileId)
                : this.getSelectedTcpProfile();
            if (!selected) return;

            try {
                const [statusResult, framesResult] = await Promise.all([
                    settingsApi.getTcpProfileStatus(selected.id),
                    settingsApi.getTcpProfileFrames(selected.id)
                ]);
                this.tcpStatusById[selected.id] = statusResult?.status || null;
                this.tcpFramesById[selected.id] = Array.isArray(framesResult?.frames) ? framesResult.frames : [];
                this.refreshTcpPanel();
            } catch (error) {
                if (!silent) {
                    showToast('刷新 TCP 状态失败: ' + error.message, 'error');
                }
            }
        }
        ,
        getTcpStatusMeta(profile) {
            const status = this.tcpStatusById?.[profile?.id];
            if (status?.isListening) {
                return { className: 'status-connected', text: `监听中 ${status.connectedClients || 0}` };
            }
            if (status?.isConnected) {
                return { className: 'status-connected', text: '已连接' };
            }
            if (status?.lastError) {
                return { className: 'status-error', text: '异常' };
            }
            return { className: 'status-disconnected', text: '未连接' };
        }
        ,
        renderTcpProfileList() {
            if (!Array.isArray(this.tcpProfiles) || this.tcpProfiles.length === 0) {
                return `
                    <div style="padding: 24px; color: var(--text-muted); text-align: center;">
                        暂无 TCP Profile
                    </div>
                `;
            }

            return this.tcpProfiles.map(profile => {
                const active = profile.id === this.getSelectedTcpProfile()?.id;
                const statusMeta = this.getTcpStatusMeta(profile);
                return `
                    <button type="button" class="settings-list-item ${active ? 'active' : ''}" data-action="select-tcp-profile" data-profile-id="${this.escapeHtml(profile.id)}" data-tcp-profile-id="${this.escapeHtml(profile.id)}" style="width:100%; text-align:left;">
                        <span style="display:flex; justify-content:space-between; gap:8px; align-items:center;">
                            <strong>${this.escapeHtml(profile.name)}</strong>
                            <span class="settings-status-badge ${statusMeta.className}" style="white-space:nowrap;"><span class="status-dot"></span> ${statusMeta.text}</span>
                        </span>
                        <span style="display:block; margin-top:6px; color:var(--text-muted); font-size:12px;">
                            ${this.escapeHtml(profile.mode)} · ${this.escapeHtml(profile.mode === 'Server' ? `${profile.localHost}:${profile.localPort}` : `${profile.remoteHost}:${profile.remotePort}`)}
                        </span>
                    </button>
                `;
            }).join('');
        }
        ,
        renderTcpFrames(profile) {
            const frames = this.tcpFramesById?.[profile?.id] || [];
            if (!Array.isArray(frames) || frames.length === 0) {
                return `
                    <tr>
                        <td colspan="5" style="text-align:center; padding: 20px; color: var(--text-muted);">暂无收发日志</td>
                    </tr>
                `;
            }

            return frames.slice(-80).reverse().map(frame => {
                const when = frame?.timestampUtc ? new Date(frame.timestampUtc).toLocaleTimeString() : '';
                const text = String(frame?.text || '');
                const hex = String(frame?.hex || '');
                return `
                    <tr>
                        <td>${this.escapeHtml(when)}</td>
                        <td>${this.escapeHtml(frame?.direction || '')}</td>
                        <td>${Number.parseInt(`${frame?.byteCount ?? 0}`, 10) || 0}</td>
                        <td style="max-width:360px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;" title="${this.escapeHtml(text)}">${this.escapeHtml(text)}</td>
                        <td style="max-width:360px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;" title="${this.escapeHtml(hex)}">${this.escapeHtml(hex)}</td>
                    </tr>
                `;
            }).join('');
        }
        ,
        renderTcpTab() {
            const selected = this.getSelectedTcpProfile();
            if (!selected) {
                return `
                    <div class="settings-section-title">
                        <h2>TCP 通讯</h2>
                        <p>维护全局机器人/通用 TCP 调试 Profile。</p>
                    </div>
                    ${this.renderScopeNotice('tcp')}
                    <div class="settings-modern-card">
                        <div class="settings-card-body" style="display:flex; align-items:center; justify-content:center; min-height:260px;">
                            <button class="cv-btn cv-btn-primary" id="btn-add-tcp-profile">新增 Profile</button>
                        </div>
                    </div>
                `;
            }

            const statusMeta = this.getTcpStatusMeta(selected);
            const isServer = selected.mode === 'Server';
            const frameMode = this.normalizeTcpFrameMode(selected.frameMode);
            const status = this.tcpStatusById?.[selected.id] || {};
            const textValue = selected.encoding === 'HEX' || frameMode === 'Hex' ? '' : 'PING';
            const hexValue = selected.encoding === 'HEX' || frameMode === 'Hex' ? '50494E47' : '';

            return `
                <div class="settings-section-title">
                    <h2>TCP 通讯</h2>
                    <p>全局 TCP / 机器人通讯 Profile 与收发调试。</p>
                </div>
                ${this.renderScopeNotice('tcp')}

                <div style="display:grid; grid-template-columns:minmax(220px, 280px) minmax(0, 1fr); gap:20px; align-items:start;">
                    <div class="settings-modern-card">
                        <div class="settings-card-header">
                            <div class="settings-header-left">
                                <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M4 7h16v2H4V7zm0 4h16v2H4v-2zm0 4h16v2H4v-2z"/></svg>
                                <span>Profile</span>
                            </div>
                            <button class="cv-btn settings-btn-light" id="btn-add-tcp-profile" style="padding:4px 10px;">新增</button>
                        </div>
                        <div class="settings-card-body" style="display:flex; flex-direction:column; gap:8px;">
                            ${this.renderTcpProfileList()}
                        </div>
                    </div>

                    <div style="display:flex; flex-direction:column; gap:20px;">
                        <div class="settings-modern-card">
                            <div class="settings-card-header has-badge">
                                <div class="settings-header-left">
                                    <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M3 5h18v4H3V5zm2 2v0h14V7H5zm-2 4h8v8H3v-8zm2 2v4h4v-4H5zm8-2h8v8h-8v-8zm2 2v4h4v-4h-4z"/></svg>
                                    <span>${this.escapeHtml(selected.name)}</span>
                                </div>
                                <div style="display:flex; gap:8px; align-items:center;">
                                    <div class="settings-status-badge ${statusMeta.className}" title="${this.escapeHtml(status?.lastError || '')}">
                                        <span class="status-dot"></span> ${statusMeta.text}
                                    </div>
                                    <button class="cv-btn settings-btn-light" id="btn-tcp-refresh">刷新</button>
                                </div>
                            </div>
                            <div class="settings-card-body">
                                <div class="horizontal-flex">
                                    <div class="settings-fieldset" style="flex:2;">
                                        <label>名称</label>
                                        <input class="cv-input" data-tcp-field="name" value="${this.escapeHtml(selected.name)}">
                                    </div>
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>启用</label>
                                        <label class="settings-toggle-row">
                                            <input type="checkbox" data-tcp-field="enabled" ${selected.enabled ? 'checked' : ''}>
                                            <span>Enabled</span>
                                        </label>
                                    </div>
                                    <div class="settings-fieldset" style="flex:1.2;">
                                        <label>模式</label>
                                        <select class="cv-input" data-tcp-field="mode">
                                            <option value="Client" ${selected.mode === 'Client' ? 'selected' : ''}>Client</option>
                                            <option value="Server" ${selected.mode === 'Server' ? 'selected' : ''}>Server</option>
                                        </select>
                                    </div>
                                </div>

                                <div class="horizontal-flex" style="margin-top:16px;">
                                    <div class="settings-fieldset" style="flex:1.6;">
                                        <label>远端 IP</label>
                                        <input class="cv-input" data-tcp-field="remoteHost" value="${this.escapeHtml(selected.remoteHost)}">
                                    </div>
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>远端端口</label>
                                        <input type="number" class="cv-input" data-tcp-field="remotePort" value="${selected.remotePort || ''}">
                                    </div>
                                    <div class="settings-fieldset" style="flex:1.6;">
                                        <label>本地监听 IP</label>
                                        <input class="cv-input" data-tcp-field="localHost" value="${this.escapeHtml(selected.localHost)}">
                                    </div>
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>本地监听端口</label>
                                        <input type="number" class="cv-input" data-tcp-field="localPort" value="${selected.localPort || ''}">
                                    </div>
                                </div>

                                <div class="horizontal-flex" style="margin-top:16px;">
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>编码</label>
                                        <select class="cv-input" data-tcp-field="encoding">
                                            ${['UTF8', 'ASCII', 'GBK', 'HEX'].map(item => `<option value="${item}" ${selected.encoding === item ? 'selected' : ''}>${item}</option>`).join('')}
                                        </select>
                                    </div>
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>报文模式</label>
                                        <select class="cv-input" data-tcp-field="frameMode">
                                            ${['Raw', 'Line', 'Hex', 'FixedLength'].map(item => `<option value="${item}" ${frameMode === item ? 'selected' : ''}>${item}</option>`).join('')}
                                        </select>
                                    </div>
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>行结束符</label>
                                        <select class="cv-input" data-tcp-field="lineEnding" ${frameMode === 'Line' ? '' : 'disabled'}>
                                            ${['None', 'CR', 'LF', 'CRLF'].map(item => `<option value="${item}" ${selected.lineEnding === item ? 'selected' : ''}>${item}</option>`).join('')}
                                        </select>
                                    </div>
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>固定长度</label>
                                        <input type="number" class="cv-input" data-tcp-field="fixedLength" value="${selected.fixedLength || ''}" ${frameMode === 'FixedLength' ? '' : 'disabled'}>
                                    </div>
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>超时(ms)</label>
                                        <input type="number" class="cv-input" data-tcp-field="timeoutMs" value="${selected.timeoutMs || 5000}">
                                    </div>
                                </div>

                                <div class="horizontal-flex" style="margin-top:16px; align-items:center;">
                                    <label class="settings-toggle-row"><input type="checkbox" data-tcp-field="keepAlive" ${selected.keepAlive ? 'checked' : ''}> <span>KeepAlive</span></label>
                                    <label class="settings-toggle-row"><input type="checkbox" data-tcp-field="reconnect" ${selected.reconnect ? 'checked' : ''}> <span>Reconnect</span></label>
                                    <label class="settings-toggle-row"><input type="checkbox" data-tcp-field="connectOnStartup" ${selected.connectOnStartup ? 'checked' : ''}> <span>ConnectOnStartup</span></label>
                                </div>

                                <div class="settings-fieldset" style="margin-top:16px;">
                                    <label>说明</label>
                                    <input class="cv-input" data-tcp-field="description" value="${this.escapeHtml(selected.description)}">
                                </div>

                                <div style="display:flex; justify-content:space-between; gap:12px; margin-top:18px;">
                                    <div style="display:flex; gap:8px; flex-wrap:wrap;">
                                        ${isServer ? `
                                            <button class="cv-btn settings-btn-dark" id="btn-tcp-server-start">开始监听</button>
                                            <button class="cv-btn settings-btn-light" id="btn-tcp-server-stop">停止监听</button>
                                        ` : `
                                            <button class="cv-btn settings-btn-dark" id="btn-tcp-connect">连接</button>
                                            <button class="cv-btn settings-btn-light" id="btn-tcp-disconnect">断开</button>
                                        `}
                                    </div>
                                    <div style="display:flex; gap:8px;">
                                        <button class="cv-btn settings-btn-light" id="btn-delete-tcp-profile">删除</button>
                                        <button class="cv-btn cv-btn-primary" id="btn-save-tcp">保存 Profile</button>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div class="settings-modern-card">
                            <div class="settings-card-header">
                                <div class="settings-header-left">
                                    <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M2 12l7-7v4h7v6H9v4l-7-7zm20 0l-7 7v-4H8V9h7V5l7 7z"/></svg>
                                    <span>收发调试</span>
                                </div>
                                <label class="settings-toggle-row"><input type="checkbox" id="tcp-wait-response" checked> <span>WaitResponse</span></label>
                            </div>
                            <div class="settings-card-body">
                                <div class="horizontal-flex">
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>发送文本</label>
                                        <textarea class="cv-input" id="tcp-send-text" rows="3">${this.escapeHtml(textValue)}</textarea>
                                        <button class="cv-btn settings-btn-dark" id="btn-tcp-send-text" style="margin-top:8px;">发送文本</button>
                                    </div>
                                    <div class="settings-fieldset" style="flex:1;">
                                        <label>发送 HEX</label>
                                        <textarea class="cv-input" id="tcp-send-hex" rows="3" placeholder="50494E47">${this.escapeHtml(hexValue)}</textarea>
                                        <button class="cv-btn settings-btn-dark" id="btn-tcp-send-hex" style="margin-top:8px;">发送 HEX</button>
                                    </div>
                                </div>
                                <div class="settings-fieldset" style="margin-top:12px;">
                                    <label>最近响应</label>
                                    <pre id="tcp-last-response" style="min-height:42px; max-height:120px; overflow:auto; padding:10px; background:rgba(15,23,42,0.08); border-radius:6px; white-space:pre-wrap;"></pre>
                                </div>
                            </div>
                        </div>

                        <div class="settings-modern-card">
                            <div class="settings-card-header">
                                <div class="settings-header-left">
                                    <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M4 4h16v16H4V4zm2 4h12V6H6v2zm0 5h12v-3H6v3zm0 5h12v-3H6v3z"/></svg>
                                    <span>收发日志</span>
                                </div>
                                <button class="cv-btn settings-btn-light" id="btn-tcp-clear-frames">清空日志</button>
                            </div>
                            <div class="settings-card-table-wrapper">
                                <table class="settings-modern-table">
                                    <thead>
                                        <tr>
                                            <th>时间</th>
                                            <th>方向</th>
                                            <th>字节</th>
                                            <th>文本</th>
                                            <th>HEX</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        ${this.renderTcpFrames(selected)}
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    </div>
                </div>
            `;
        }
    });
}
