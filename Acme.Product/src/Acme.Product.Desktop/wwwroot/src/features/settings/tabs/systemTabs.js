import settingsApi from '../settingsApi.js';
import { validateIntegerValue, validateFloatValue, assertValidation } from '../settingsValidators.js';
import { showToast, closeModal } from '../../../shared/components/uiComponents.js';
import { applyTheme, getAppliedTheme, normalizeTheme } from '../../../core/theme/theme.js';
import { applyFeatureToButton, getFeatureButtonLabel, getFeatureMeta, isFeatureEnabled } from '../../../shared/featureRegistry.js';

export function installSystemTabs(SettingsView) {
    Object.assign(SettingsView.prototype, {
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
                ${this.renderScopeNotice('general')}

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
        ,
        renderStorageTab() {
            const storage = this.config?.storage || this.getDefaultConfig().storage;
            const pathPickerFeature = getFeatureMeta('storage.pathPicker');
            const immediateCleanupFeature = getFeatureMeta('storage.immediateCleanup');
            return `
                <div class="settings-section-title">
                    <h2>文件与存储管理</h2>
                    <p>配置图像数据保存路径、清理策略与磁盘容量预警。</p>
                </div>
                ${this.renderScopeNotice('storage')}

                <div class="settings-modern-card" style="margin-bottom: 24px;">
                    <div class="settings-card-header">
                        <div class="settings-header-left">
                            <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M2 20h20v-4H2v4zm2-3h2v2H4v-2zM2 4v4h20V4H2zm4 3H4V5h2v2zm-4 7h20v-4H2v4zm2-3h2v2H4v-2z"/></svg>
                            <span>存储路径配置</span>
                        </div>
                    </div>
                    <div class="settings-card-body">
                        <div class="settings-fieldset">
                            <label>默认图像保存路径</label>
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
                                    <label>图像保存策略</label>
                                    <select class="cv-input" id="cfg-savePolicy">
                                        <option value="All" ${storage.savePolicy === 'All' ? 'selected' : ''}>保存所有图像</option>
                                        <option value="NgOnly" ${storage.savePolicy === 'NgOnly' ? 'selected' : ''}>仅保存 NG 图像</option>
                                        <option value="None" ${storage.savePolicy === 'None' ? 'selected' : ''}>不保存图像</option>
                                    </select>
                                </div>
                                <div class="settings-fieldset" style="flex:1;">
                                    <label>自动清理阈值 (天)</label>
                                    <div class="input-with-suffix" style="position:relative;">
                                        <input type="number" class="cv-input" id="cfg-retentionDays" value="${storage.retentionDays ?? 30}" style="padding-right:36px;">
                                        <span style="position:absolute; right:12px; top:50%; transform:translateY(-50%); color:#94a3b8; font-size:13px;">天</span>
                                    </div>
                                </div>
                            </div>
                            <div class="settings-fieldset">
                                <label>磁盘低空间预警 (GB)</label>
                                <div class="input-with-suffix" style="position:relative; max-width: 200px;">
                                    <input type="number" class="cv-input" id="cfg-minFreeSpaceGb" value="${storage.minFreeSpaceGb ?? 5}" style="padding-right:36px;">
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
        ,
        renderRuntimeTab() {
            const runtime = this.config?.runtime || this.getDefaultConfig().runtime;
            return `
                <div class="settings-section-title" style="display:flex; justify-content:space-between; align-items:flex-end;">
                    <div>
                        <h2>生产运行保护</h2>
                        <p>配置自动运行逻辑、异常停机条件及硬件联动保护。</p>
                    </div>
                    <div class="settings-status-badge status-connected" style="background:rgba(232, 85, 78, 0.08); color:#c0392b; border-color:rgba(232, 85, 78, 0.25);">
                        <span class="status-dot" style="background:#e74c3c;"></span> 保护机制已启用
                    </div>
                </div>
                ${this.renderScopeNotice('runtime')}

                <div class="settings-modern-card">
                    <div class="settings-card-header">
                        <div class="settings-header-left">
                            <svg viewBox="0 0 24 24" class="settings-header-icon" style="fill:#e74c3c;"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>
                            <span>生产保护机制</span>
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
                                <span class="settings-field-hint" style="margin-left: 24px;">该配置会随“保存运行保护”一起持久化，并作为运行保护的开关。</span>
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
        ,
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
                ${this.renderScopeNotice('users')}

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
        ,
        async refreshUserTable() {
            if (!this.isAdmin) return;
            const tbody = this.container.querySelector('#settings-user-table tbody');
            if (!tbody) return;

            try {
                this.users = await settingsApi.loadUsers();

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
        ,
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
                    if (confirm(`确定要删除用户“${user.username}”吗？\n\n该账号将无法登录，历史审计记录仍会保留用户名。`)) {
                        try {
                            await settingsApi.deleteUser(id);
                            showToast('用户已删除', 'success');
                            this.refreshUserTable();
                        } catch(err) {
                            showToast('删除失败: ' + err.message, 'error');
                        }
                    }
                } else if (action === 'toggle-status') {
                    const nextAction = user.isActive ? '禁用' : '启用';
                    if (!confirm(`确定要${nextAction}用户“${user.username}”吗？${user.isActive ? '\n\n禁用后该账号将无法登录。' : ''}`)) {
                        return;
                    }
                    try {
                        await settingsApi.updateUser(id, {
                            displayName: user.displayName,
                            role: user.role,
                            isActive: !user.isActive
                        });
                        showToast(`用户已${nextAction}`, 'success');
                        this.refreshUserTable();
                    } catch(err) {
                        showToast('操作失败: ' + err.message, 'error');
                    }
                } else if (action === 'reset-pwd') {
                    const newPwd = prompt(`请输入为 ${user.username} 重置的新密码 (至少6位):`);
                    if (newPwd) {
                        if (!confirm(`确定要重置用户“${user.username}”的密码吗？\n\n保存后旧密码立即失效，请通过安全渠道告知新密码。`)) {
                            return;
                        }
                        try {
                            await settingsApi.resetUserPassword(id, { newPassword: newPwd });
                            showToast('密码重置成功', 'success');
                        } catch(err) {
                            showToast('密码重置失败: ' + err.message, 'error');
                        }
                    }
                }
            });
        }
        ,
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

            const eventCleanups = [];
            const cleanupModalEvents = () => {
                eventCleanups.splice(0).forEach(cleanup => cleanup());
            };

            const modal = this.createTrackedModal({
                title,
                content,
                width: '520px',
                onClose: cleanupModalEvents
            });

            eventCleanups.push(this.lifecycle.trackEvent(content.querySelector('#btn-cancel-usermodal'), 'click', () => closeModal(modal)));
            eventCleanups.push(this.lifecycle.trackEvent(content.querySelector('#btn-save-usermodal'), 'click', async () => {
                const displayName = content.querySelector('#modal-user-displayname').value;
                const role = parseInt(content.querySelector('#modal-user-role').value, 10);

                try {
                    if (mode === 'create') {
                        const username = content.querySelector('#modal-user-username').value;
                        const password = content.querySelector('#modal-user-password').value;
                        await settingsApi.createUser({ username, password, displayName, role });
                        showToast('用户创建成功', 'success');
                    } else {
                        await settingsApi.updateUser(user.id, {
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
            }));
        }

        ,
        async loadDiskUsage() {
            if (!this.container) return;
            const requestId = ++this._diskUsageRequestId;

            try {
                const pathInput = this.container.querySelector('#cfg-imageSavePath');
                const sourcePath = pathInput?.value || this.config?.storage?.imageSavePath || '';
                const usage = await settingsApi.getDiskUsage(sourcePath);
                if (requestId !== this._diskUsageRequestId) {
                    return;
                }
                this.diskUsage = usage;
                this.updateDiskUsageCard();
            } catch (error) {
                if (requestId !== this._diskUsageRequestId) {
                    return;
                }
                console.warn('[SettingsView] 加载磁盘容量失败:', error);
            }
        }
        ,
        async validateStoragePathBeforeSave(path) {
            const normalizedPath = String(path || '').trim();
            if (!normalizedPath) {
                showToast('请先填写默认图像保存路径。', 'warning');
                return false;
            }

            try {
                const usage = await settingsApi.getDiskUsage(normalizedPath);
                if (usage?.isAccessible === false || usage?.canWrite === false) {
                    showToast('保存路径不可访问或不可写，请联系管理员确认目录权限。', 'error');
                    return false;
                }

                this.diskUsage = usage;
                this.updateDiskUsageCard();
                return true;
            } catch (error) {
                console.warn('[SettingsView] 保存路径校验失败:', error);
                showToast(`保存路径校验失败，请确认目录存在且当前账号可写: ${error.message}`, 'error');
                return false;
            }
        }
        ,
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
        ,
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
                await settingsApi.changePassword({
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
        ,
        async resetSettings() {
            const resetFeature = getFeatureMeta('settings.reset');
            const resetLabel = getFeatureButtonLabel('settings.reset', '恢复默认设置');
            if (!confirm(`确定要${resetLabel}吗？\n\n将重置普通系统配置、PLC 配置、相机绑定和 AI 模型配置；Station token 不会写入普通配置，但现场连接可能需要重新检查。${resetFeature.description}`)) {
                return;
            }

            if (!confirm('请再次确认：恢复默认设置会立即写入本机配置文件，当前未保存的设置页修改会丢失。继续执行吗？')) {
                return;
            }

            try {
                const result = await settingsApi.resetSettings();
                const message = result?.message
                    || result?.Message
                    || `${resetLabel}已执行`;
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
        ,
        readIntegerSetting(selector, label, { min = Number.MIN_SAFE_INTEGER, max = Number.MAX_SAFE_INTEGER } = {}) {
            const raw = String(this.container?.querySelector(selector)?.value ?? '').trim();
            return assertValidation(validateIntegerValue(raw, label, { min, max }));
        }
        ,
        readFloatSetting(selector, label, { min = Number.NEGATIVE_INFINITY, max = Number.POSITIVE_INFINITY } = {}) {
            const raw = String(this.container?.querySelector(selector)?.value ?? '').trim();
            return assertValidation(validateFloatValue(raw, label, { min, max }));
        }
        ,
        collectGeneralConfigForSave() {
            const softwareTitle = String(this.container?.querySelector('#cfg-softwareTitle')?.value || '').trim();
            if (!softwareTitle) {
                throw new Error('软件标题不能为空。');
            }
            if (softwareTitle.length > 80) {
                throw new Error('软件标题不能超过 80 个字符。');
            }

            const theme = normalizeTheme(this.container?.querySelector('#cfg-theme')?.value, null);
            if (!theme) {
                throw new Error('系统主题只能选择浅色或深色。');
            }

            return {
                softwareTitle,
                theme,
                autoStart: this.container?.querySelector('#cfg-autoStart')?.checked || false
            };
        }
        ,
        collectStorageConfigForSave() {
            const imageSavePath = String(this.container?.querySelector('#cfg-imageSavePath')?.value || '').trim();
            if (!imageSavePath) {
                throw new Error('默认图像保存路径不能为空。');
            }

            const savePolicy = this.container?.querySelector('#cfg-savePolicy')?.value || 'NgOnly';
            if (!['All', 'NgOnly', 'None'].includes(savePolicy)) {
                throw new Error('图像保存策略无效，请重新选择。');
            }

            return {
                imageSavePath,
                savePolicy,
                retentionDays: this.readIntegerSetting('#cfg-retentionDays', '自动清理阈值', { min: 0, max: 3650 }),
                minFreeSpaceGb: this.readFloatSetting('#cfg-minFreeSpaceGb', '磁盘低空间预警', { min: 0, max: 1024 })
            };
        }
        ,
        collectRuntimeConfigForSave() {
            return {
                autoRun: this.container?.querySelector('#cfg-autoRun')?.checked || false,
                stopOnConsecutiveNg: this.readIntegerSetting('#cfg-stopOnConsecutiveNg', '连续 NG 停机报警阈值', { min: 0, max: 100000 }),
                missingMaterialTimeoutSeconds: this.readIntegerSetting('#cfg-missingMaterialTimeoutSeconds', '缺料等待超时', { min: 0, max: 86400 }),
                applyProtectionRules: this.container?.querySelector('#cfg-applyProtectionRules')?.checked ?? true
            };
        }
        ,
        collectSecurityConfigForSave() {
            return {
                passwordMinLength: this.readIntegerSetting('#cfg-passwordMinLength', '密码最小长度', { min: 6, max: 128 }),
                sessionTimeoutMinutes: this.readIntegerSetting('#cfg-sessionTimeoutMinutes', '会话自动超时', { min: 1, max: 1440 }),
                loginFailureLockoutCount: this.readIntegerSetting('#cfg-loginFailureLockoutCount', '登录失败锁定次数', { min: 1, max: 100 })
            };
        }
        ,
        buildAppConfigForSave(activeTabName) {
            const nextConfig = this.normalizeAppConfig(this.config || this.getDefaultConfig());
            nextConfig.communication = this.cloneCommunicationConfig(
                this.savedCommunicationConfig || this.config?.communication || nextConfig.communication
            );
            nextConfig.cameras = Array.isArray(this.config?.cameras) ? [...this.config.cameras] : [];
            nextConfig.activeCameraId = this.config?.activeCameraId || '';

            if (activeTabName === 'general') {
                nextConfig.general = this.collectGeneralConfigForSave();
            } else if (activeTabName === 'storage') {
                nextConfig.storage = this.collectStorageConfigForSave();
            } else if (activeTabName === 'runtime') {
                nextConfig.runtime = this.collectRuntimeConfigForSave();
            } else if (activeTabName === 'users') {
                nextConfig.security = this.collectSecurityConfigForSave();
            }

            return nextConfig;
        }
        ,
        async saveAppSettingsForTab(activeTabName) {
            let config;
            try {
                config = this.buildAppConfigForSave(activeTabName);
            } catch (error) {
                showToast(error.message, 'warning');
                return;
            }

            if (activeTabName === 'storage') {
                const storagePathValid = await this.validateStoragePathBeforeSave(config.storage.imageSavePath);
                if (!storagePathValid) {
                    return;
                }
            }

            try {
                await settingsApi.saveSettings(config);
                this.config = this.normalizeAppConfig(config);
                this.savedCommunicationConfig = this.cloneCommunicationConfig(this.config.communication);
                this.syncPlcMappingsFromActiveProfile();

                if (activeTabName === 'general') {
                    applyTheme(this.config.general.theme, { persist: true });
                }
                if (activeTabName === 'storage') {
                    await this.loadDiskUsage();
                }

                showToast(`${this.getSaveScopeMeta(activeTabName).button}已完成。`, 'success');
            } catch (error) {
                console.error('[SettingsView] Failed to save app config:', error);
                showToast('保存设置失败: ' + error.message, 'error');
            }
        }

    });
}
