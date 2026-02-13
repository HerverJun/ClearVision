import httpClient from '../../core/messaging/httpClient.js';
import { createModal, closeModal, showToast } from '../../shared/components/uiComponents.js';

/**
 * 设置模态框管理器
 */
class SettingsModal {
    constructor() {
        this.config = null;
        this.modalOverlay = null;
        this.activeTab = 'general';
    }
    
    /**
     * 打开设置模态框
     */
    async open() {
        console.log('[SettingsModal] === open() START ===');
        
        // 如果已有模态框打开，先关闭
        if (this.modalOverlay) {
            console.log('[SettingsModal] Closing existing modal');
            closeModal(this.modalOverlay);
            this.modalOverlay = null;
        }
        
        // 加载配置
        try {
            console.log('[SettingsModal] Fetching config from /settings...');
            this.config = await httpClient.get('/settings');
            console.log('[SettingsModal] Config loaded:', JSON.stringify(this.config, null, 2));
        } catch (error) {
            console.error('[SettingsModal] Failed to load config:', error);
            showToast('加载配置失败: ' + error.message, 'error');
            
            // 使用默认配置继续
            console.log('[SettingsModal] Using default config');
            this.config = this.getDefaultConfig();
        }
        
        // 创建模态框内容
        console.log('[SettingsModal] Creating modal content...');
        const content = this.createContent();
        console.log('[SettingsModal] Content created');
        
        // 创建底部按钮
        console.log('[SettingsModal] Creating footer...');
        const footer = this.createFooter();
        console.log('[SettingsModal] Footer created');
        
        console.log('[SettingsModal] Calling createModal...');
        try {
            this.modalOverlay = createModal({
                title: '⚙️ 系统设置',
                content: content,
                footer: footer,
                width: '600px'
            });
            console.log('[SettingsModal] Modal created, overlay:', this.modalOverlay);
            
            // 立即强制设置样式，确保显示（解决CSS缓存问题）
            if (this.modalOverlay) {
                console.log('[SettingsModal] Applying forced styles...');
                this.modalOverlay.style.cssText = `
                    position: fixed !important;
                    top: 0 !important;
                    left: 0 !important;
                    width: 100% !important;
                    height: 100% !important;
                    background: rgba(13, 27, 42, 0.9) !important;
                    display: flex !important;
                    justify-content: center !important;
                    align-items: center !important;
                    z-index: 99999 !important;
                    opacity: 1 !important;
                    visibility: visible !important;
                `;
                
                // 同时添加 show 类以触发 CSS 动画
                this.modalOverlay.classList.add('show');
                
                // 确保模态框内容也正确显示
                const modal = this.modalOverlay.querySelector('.cv-modal');
                if (modal) {
                    modal.style.cssText = `
                        background: var(--glass-panel, rgba(15, 36, 53, 0.95)) !important;
                        border: 1px solid var(--glass-border, rgba(255,255,255,0.1)) !important;
                        border-radius: 16px !important;
                        width: 90% !important;
                        max-width: 600px !important;
                        max-height: 85vh !important;
                        display: flex !important;
                        flex-direction: column !important;
                        overflow: hidden !important;
                        box-shadow: 0 20px 60px rgba(0,0,0,0.5) !important;
                    `;
                }
                
                console.log('[SettingsModal] Forced styles applied');
            } else {
                console.error('[SettingsModal] Modal overlay is null!');
                showToast('创建模态框失败', 'error');
                return;
            }
        } catch (error) {
            console.error('[SettingsModal] Error creating modal:', error);
            showToast('创建设置窗口失败: ' + error.message, 'error');
            return;
        }
        
        // 绑定事件
        try {
            this.bindEvents();
            console.log('[SettingsModal] Events bound successfully');
        } catch (error) {
            console.error('[SettingsModal] Error binding events:', error);
        }
        
        console.log('[SettingsModal] === open() END - Modal should be visible ===');
    }
    
    /**
     * 获取默认配置
     */
    getDefaultConfig() {
        return {
            general: {
                softwareTitle: 'ClearVision 检测站',
                theme: 'dark',
                autoStart: false
            },
            communication: {
                plcIpAddress: '192.168.1.100',
                plcPort: 502,
                protocol: 'ModbusTcp',
                heartbeatIntervalMs: 1000
            },
            storage: {
                imageSavePath: 'D:\\VisionData\\Images',
                savePolicy: 'NgOnly',
                retentionDays: 30,
                minFreeSpaceGb: 5
            },
            runtime: {
                autoRun: false,
                stopOnConsecutiveNg: 0
            }
        };
    }
    
    /**
     * 创建内容区域
     */
    createContent() {
        console.log('[SettingsModal] createContent() called');
        const container = document.createElement('div');
        container.className = 'settings-container';
        
        // 标签页导航
        container.innerHTML = `
            <div class="settings-tabs">
                <button class="settings-tab active" data-tab="general">常规</button>
                <button class="settings-tab" data-tab="communication">通讯</button>
                <button class="settings-tab" data-tab="storage">存储</button>
                <button class="settings-tab" data-tab="runtime">运行</button>
            </div>
            <div class="settings-content">
                ${this.renderGeneralTab()}
                ${this.renderCommunicationTab()}
                ${this.renderStorageTab()}
                ${this.renderRuntimeTab()}
            </div>
        `;
        
        return container;
    }
    
    renderGeneralTab() {
        const general = this.config?.general || this.getDefaultConfig().general;
        return `
            <div class="settings-section active" data-section="general">
                <div class="settings-group">
                    <div class="settings-group-title">界面设置</div>
                    <div class="settings-row">
                        <div>
                            <div class="settings-label">软件标题</div>
                            <div class="settings-hint">显示在顶部的名称</div>
                        </div>
                        <input type="text" class="settings-input" 
                               id="cfg-softwareTitle" 
                               value="${general.softwareTitle || ''}">
                    </div>
                    <div class="settings-row">
                        <div>
                            <div class="settings-label">界面主题</div>
                        </div>
                        <select class="settings-select" id="cfg-theme">
                            <option value="dark" ${general.theme === 'dark' ? 'selected' : ''}>暗色模式</option>
                            <option value="light" ${general.theme === 'light' ? 'selected' : ''}>亮色模式</option>
                        </select>
                    </div>
                    <div class="settings-row">
                        <div>
                            <div class="settings-label">皮肤风格</div>
                            <div class="settings-hint">切换整套 UI 皮肤 (需重新加载)</div>
                        </div>
                        <button id="btn-open-launcher" class="cv-btn cv-btn-secondary" style="width: auto; padding: 4px 12px;">切换皮肤</button>
                    </div>
                </div>
            </div>
        `;
    }
    
    renderCommunicationTab() {
        const communication = this.config?.communication || this.getDefaultConfig().communication;
        return `
            <div class="settings-section" data-section="communication">
                <div class="settings-group">
                    <div class="settings-group-title">PLC 连接</div>
                    <div class="settings-row">
                        <div class="settings-label">IP 地址</div>
                        <input type="text" class="settings-input" 
                               id="cfg-plcIpAddress" 
                               value="${communication.plcIpAddress || ''}">
                    </div>
                    <div class="settings-row">
                        <div class="settings-label">端口号</div>
                        <input type="number" class="settings-input" 
                               id="cfg-plcPort" 
                               value="${communication.plcPort || 502}">
                    </div>
                    <div class="settings-row">
                        <div class="settings-label">通讯协议</div>
                        <select class="settings-select" id="cfg-protocol">
                            <option value="ModbusTcp" ${communication.protocol === 'ModbusTcp' ? 'selected' : ''}>Modbus TCP</option>
                            <option value="TcpSocket" ${communication.protocol === 'TcpSocket' ? 'selected' : ''}>TCP Socket</option>
                        </select>
                    </div>
                    <div class="settings-row">
                        <div class="settings-label">心跳间隔 (ms)</div>
                        <input type="number" class="settings-input" 
                               id="cfg-heartbeatIntervalMs" 
                               value="${communication.heartbeatIntervalMs || 1000}">
                    </div>
                </div>
            </div>
        `;
    }
    
    renderStorageTab() {
        const storage = this.config?.storage || this.getDefaultConfig().storage;
        return `
            <div class="settings-section" data-section="storage">
                <div class="settings-group">
                    <div class="settings-group-title">图片存储</div>
                    <div class="settings-row">
                        <div class="settings-label">保存路径</div>
                        <input type="text" class="settings-input" 
                               id="cfg-imageSavePath" 
                               value="${storage.imageSavePath || ''}"
                               style="width: 300px;">
                    </div>
                    <div class="settings-row">
                        <div class="settings-label">保存策略</div>
                        <select class="settings-select" id="cfg-savePolicy">
                            <option value="All" ${storage.savePolicy === 'All' ? 'selected' : ''}>保存全部</option>
                            <option value="NgOnly" ${storage.savePolicy === 'NgOnly' ? 'selected' : ''}>仅保存 NG</option>
                            <option value="None" ${storage.savePolicy === 'None' ? 'selected' : ''}>不保存</option>
                        </select>
                    </div>
                    <div class="settings-row">
                        <div class="settings-label">保留天数</div>
                        <input type="number" class="settings-input" 
                               id="cfg-retentionDays" 
                               value="${storage.retentionDays || 30}">
                    </div>
                    <div class="settings-row">
                        <div class="settings-label">最小剩余空间 (GB)</div>
                        <input type="number" class="settings-input" 
                               id="cfg-minFreeSpaceGb" 
                               value="${storage.minFreeSpaceGb || 5}">
                    </div>
                </div>
            </div>
        `;
    }
    
    renderRuntimeTab() {
        const runtime = this.config?.runtime || this.getDefaultConfig().runtime;
        return `
            <div class="settings-section" data-section="runtime">
                <div class="settings-group">
                    <div class="settings-group-title">运行参数</div>
                    <div class="settings-row">
                        <div>
                            <div class="settings-label">自动运行</div>
                            <div class="settings-hint">软件启动后自动开始检测</div>
                        </div>
                        <label class="switch">
                            <input type="checkbox" id="cfg-autoRun" ${runtime.autoRun ? 'checked' : ''}>
                            <span class="slider"></span>
                        </label>
                    </div>
                    <div class="settings-row">
                        <div>
                            <div class="settings-label">连续 NG 停机</div>
                            <div class="settings-hint">检测到连续 N 个 NG 时暂停（0=禁用）</div>
                        </div>
                        <input type="number" class="settings-input" 
                               id="cfg-stopOnConsecutiveNg" 
                               value="${runtime.stopOnConsecutiveNg || 0}">
                    </div>
                </div>
            </div>
        `;
    }
    
    createFooter() {
        const footer = document.createElement('div');
        footer.style.cssText = 'display: flex; gap: 12px; justify-content: flex-end;';
        
        const cancelBtn = document.createElement('button');
        cancelBtn.className = 'cv-btn cv-btn-secondary';
        cancelBtn.textContent = '取消';
        cancelBtn.onclick = () => {
            console.log('[SettingsModal] Cancel clicked');
            closeModal(this.modalOverlay);
            this.modalOverlay = null;
        };
        
        const saveBtn = document.createElement('button');
        saveBtn.className = 'cv-btn cv-btn-primary';
        saveBtn.textContent = '保存';
        saveBtn.onclick = () => {
            console.log('[SettingsModal] Save clicked');
            this.save();
        };
        
        footer.appendChild(cancelBtn);
        footer.appendChild(saveBtn);
        return footer;
    }
    
    bindEvents() {
        if (!this.modalOverlay) {
            console.error('[SettingsModal] Cannot bind events: modalOverlay is null');
            return;
        }
        
        // 标签页切换
        const tabs = this.modalOverlay.querySelectorAll('.settings-tab');
        const sections = this.modalOverlay.querySelectorAll('.settings-section');
        
        console.log(`[SettingsModal] Binding ${tabs.length} tabs and ${sections.length} sections`);
        
        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                const targetTab = tab.dataset.tab;
                console.log(`[SettingsModal] Tab clicked: ${targetTab}`);
                
                tabs.forEach(t => t.classList.remove('active'));
                sections.forEach(s => s.classList.remove('active'));
                
                tab.classList.add('active');
                const targetSection = this.modalOverlay.querySelector(`[data-section="${targetTab}"]`);
                if (targetSection) {
                    targetSection.classList.add('active');
                }
            });
        });

        // 绑定切换皮肤按钮
        const launcherBtn = this.modalOverlay.querySelector('#btn-open-launcher');
        if (launcherBtn) {
            launcherBtn.addEventListener('click', () => {
                if (confirm('切换皮肤需要重新加载页面，确定继续吗？')) {
                    window.location.href = 'launcher.html';
                }
            });
        }
    }
    
    /**
     * 保存配置
     */
    async save() {
        console.log('[SettingsModal] Saving config...');
        
        // 收集表单数据
        const config = {
            general: {
                softwareTitle: document.getElementById('cfg-softwareTitle')?.value || '',
                theme: document.getElementById('cfg-theme')?.value || 'dark',
                autoStart: false
            },
            communication: {
                plcIpAddress: document.getElementById('cfg-plcIpAddress')?.value || '',
                plcPort: parseInt(document.getElementById('cfg-plcPort')?.value || '502', 10),
                protocol: document.getElementById('cfg-protocol')?.value || 'ModbusTcp',
                heartbeatIntervalMs: parseInt(document.getElementById('cfg-heartbeatIntervalMs')?.value || '1000', 10)
            },
            storage: {
                imageSavePath: document.getElementById('cfg-imageSavePath')?.value || '',
                savePolicy: document.getElementById('cfg-savePolicy')?.value || 'NgOnly',
                retentionDays: parseInt(document.getElementById('cfg-retentionDays')?.value || '30', 10),
                minFreeSpaceGb: parseInt(document.getElementById('cfg-minFreeSpaceGb')?.value || '5', 10)
            },
            runtime: {
                autoRun: document.getElementById('cfg-autoRun')?.checked || false,
                stopOnConsecutiveNg: parseInt(document.getElementById('cfg-stopOnConsecutiveNg')?.value || '0', 10)
            }
        };
        
        console.log('[SettingsModal] Config to save:', JSON.stringify(config, null, 2));
        
        try {
            await httpClient.put('/settings', config);
            console.log('[SettingsModal] Config saved successfully');
            showToast('设置已保存', 'success');
            closeModal(this.modalOverlay);
            this.modalOverlay = null;
            
            // 立即应用主题
            if (config.general.theme) {
                document.documentElement.dataset.theme = config.general.theme;
            }
        } catch (error) {
            console.error('[SettingsModal] Failed to save config:', error);
            showToast('保存失败: ' + error.message, 'error');
        }
    }
}

// 导出单例
console.log('[SettingsModal] Module loaded, creating singleton');
const settingsModal = new SettingsModal();
console.log('[SettingsModal] Singleton created:', settingsModal);
export default settingsModal;
