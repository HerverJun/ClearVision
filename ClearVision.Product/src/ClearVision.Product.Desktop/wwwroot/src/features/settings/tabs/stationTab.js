import settingsApi from '../settingsApi.js';
import { validateStationCommunicationDraft, assertValidation } from '../settingsValidators.js';
import { showToast } from '../../../shared/components/uiComponents.js';

export function installStationTab(SettingsView) {
    Object.assign(SettingsView.prototype, {
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
        ,
        normalizeStationMode(value) {
            const mode = String(value || '').trim();
            if (/^lancontroller$/i.test(mode)) return 'LanController';
            if (/^localloopback$/i.test(mode)) return 'LocalLoopback';
            return 'Disabled';
        }
        ,
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
        ,
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
        ,
        async loadStationCommunicationSettings({ force = false } = {}) {
            if (!force && this.stationCommunicationLoaded) {
                this.refreshStationCommunicationPanel();
                return;
            }

            try {
                const result = await settingsApi.loadStationCommunicationSettings();
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
        ,
        refreshStationCommunicationPanel() {
            const stationPanel = this.container?.querySelector('[data-section="station"]');
            if (!stationPanel) return;

            stationPanel.innerHTML = this.renderStationCommunicationTab();
        }
        ,
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
        ,
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
        ,
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
        ,
        buildStationCommunicationPayload() {
            this.syncStationCommunicationDraftFromForm();
            const settings = this.normalizeStationCommunicationSettings(this.stationCommunicationSettings);
            const portInput = this.container?.querySelector('#cfg-station-port')?.value;
            const lanHost = this.container?.querySelector('#cfg-station-lan-host')?.value?.trim() || settings.lanHost;
            const validated = assertValidation(validateStationCommunicationDraft({
                mode: settings.mode,
                port: portInput ?? settings.port,
                lanHost
            }));

            return {
                mode: settings.mode,
                port: validated.port,
                lanHost: validated.lanHost,
                localStationSyncEnabled: settings.mode !== 'Disabled'
                    && (this.container?.querySelector('#cfg-station-local-sync')?.checked ?? settings.localStationSyncEnabled)
            };
        }
        ,
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
                const result = await settingsApi.saveStationCommunicationSettings(payload);
                this.stationCommunicationSettings = this.normalizeStationCommunicationSettings(result);
                this.stationCommunicationLoaded = true;
                this.stationTokenVisible = false;
                this.stationTokenValue = '';
                this.refreshStationCommunicationPanel();
                if (!silent) {
                    showToast(this.getStationCommunicationSaveMessage(this.stationCommunicationSettings), 'success');
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
        ,
        getStationCommunicationSaveMessage(settings) {
            const restart = settings?.requiresRestart || {};
            if (restart.studio && restart.localStation) {
                return 'Station 设置已保存。请重启本机 Studio 和本机 Station 后生效。';
            }

            if (restart.studio) {
                return 'Station 设置已保存。请重启本机 Studio 后生效。';
            }

            if (restart.localStation) {
                return 'Station 设置已保存。请重启本机 Station 后生效。';
            }

            return 'Station 设置已保存，当前本机已按这些设置运行。';
        }
        ,
        scheduleStationTokenAutoHide() {
            this.clearTrackedTimeout(this._stationTokenHideTimer);
            this._stationTokenHideTimer = this.setTrackedTimeout(() => {
                this._stationTokenHideTimer = null;
                this.stationTokenVisible = false;
                this.stationTokenValue = '';
                if (this.getActiveTabName() === 'station') {
                    this.refreshStationCommunicationPanel();
                    showToast('Station token 已自动隐藏。', 'info');
                }
            }, 60000);
        }
        ,
        async revealStationToken() {
            if (!this.isAdmin) {
                showToast('只有管理员可以显示 Station token', 'warning');
                return '';
            }

            try {
                const result = await settingsApi.revealStationToken();
                this.stationTokenValue = String(result?.token || '');
                this.stationTokenVisible = !!this.stationTokenValue;
                if (result?.settings) {
                    this.stationCommunicationSettings = this.normalizeStationCommunicationSettings(result.settings);
                    this.stationCommunicationLoaded = true;
                }
                this.refreshStationCommunicationPanel();
                this.scheduleStationTokenAutoHide();
                return this.stationTokenValue;
            } catch (error) {
                showToast('显示 Station token 失败: ' + error.message, 'error');
                return '';
            }
        }
        ,
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
            this.scheduleStationTokenAutoHide();
            showToast('Station token 已复制', 'success');
        }
        ,
        async regenerateStationToken() {
            if (!this.isAdmin) {
                showToast('只有管理员可以重新生成 Station token', 'warning');
                return;
            }

            if (!window.confirm('重新生成 token 后，已配置旧 token 的远端 Station 需要同步更新。继续吗？')) {
                return;
            }

            try {
                const result = await settingsApi.regenerateStationToken();
                this.stationTokenValue = String(result?.token || '');
                this.stationTokenVisible = !!this.stationTokenValue;
                if (result?.settings) {
                    this.stationCommunicationSettings = this.normalizeStationCommunicationSettings(result.settings);
                    this.stationCommunicationLoaded = true;
                }
                this.refreshStationCommunicationPanel();
                this.scheduleStationTokenAutoHide();
                showToast('Station token 已重新生成，重启后生效。', 'success');
            } catch (error) {
                showToast('重新生成 Station token 失败: ' + error.message, 'error');
            }
        }
        ,
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
        ,
        openStationMonitor() {
            const stationButton = document.querySelector('.nav-btn[data-view="stations"]');
            if (stationButton) {
                stationButton.click();
                return;
            }

            showToast('未找到 Station 监控入口', 'warning');
        }
        ,
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
                restartMessages.push('需要重启本机 Studio：新的监听模式、端口或 token 才会生效。');
            }
            if (settings.requiresRestart?.localStation) {
                restartMessages.push('需要重启本机 Station：它才会重新读取本机 Station 配置文件。');
            }
            if (restartMessages.length === 0) {
                restartMessages.push('本机 Studio 已按当前保存的设置运行，不需要重启。');
                restartMessages.push('如果是另一台电脑的 Station，请在那台电脑填右侧“远端 Station”片段并重启 Station。');
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
                    <p>本机 Studio 作为监控服务端；另一台电脑的 Station 主动连接这里。保存后只按下方明确提示重启。</p>
                </div>
                ${this.renderScopeNotice('station')}

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

    });
}
