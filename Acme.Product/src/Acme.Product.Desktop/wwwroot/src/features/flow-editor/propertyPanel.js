import webMessageBridge from '../../core/messaging/webMessageBridge.js';

class PropertyPanel {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.currentOperator = null;
        this.onChangeCallback = null;
        this.bindGlobalEvents();
    }

    /**
     * 绑定全局事件
     */
    bindGlobalEvents() {
        webMessageBridge.on('FilePickedEvent', (event) => {
            // 兼容 PascalCase 和 camelCase
            const isCancelled = event.IsCancelled || event.isCancelled;
            if (isCancelled) return;

            // 兼容 PascalCase 和 camelCase
            const parameterName = event.ParameterName || event.parameterName;
            const filePath = event.FilePath || event.filePath;

            console.log('[PropertyPanel] 收到文件选择事件:', parameterName, filePath);

            const input = this.container.querySelector(`#param-${parameterName}`);
            if (input) {
                input.value = filePath || '';
                // 触发 change 事件以更新状态
                input.dispatchEvent(new Event('change'));

                // 自动应用更改
                this.applyChanges();
            }
        });
    }

    /**
     * 设置算子
     */
    setOperator(operator) {
        this.currentOperator = operator;
        this.render();
    }

    /**
     * 清空面板
     */
    clear() {
        this.currentOperator = null;
        this.container.innerHTML = '<p class="empty-text">选择一个算子查看属性</p>';
    }

    /**
     * 渲染面板
     */
    render() {
        if (!this.currentOperator) {
            this.clear();
            return;
        }

        // 兼容 title (画布节点) 和 displayName (算子库)
        const title = this.currentOperator.title || this.currentOperator.displayName || this.currentOperator.type;
        const { type, parameters = [] } = this.currentOperator;
        
        let html = `
            <div class="property-header">
                <h4>${title}</h4>
                <span class="property-type">${type}</span>
            </div>
            <div class="property-content">
        `;

        if (parameters.length === 0) {
            html += '<p class="empty-text">该算子没有可配置参数</p>';
        } else {
            html += '<form class="property-form" id="property-form">';
            
            parameters.forEach(param => {
                html += this.renderParameter(param);
            });
            
            html += `
                </div>
                <div class="property-actions">
                    <button type="button" class="btn btn-primary" id="btn-apply">应用</button>
                    <button type="button" class="btn" id="btn-reset">重置</button>
                </div>
            </form>
            `;
        }

        html += '</div>';
        this.container.innerHTML = html;

        // 绑定事件
        this.bindEvents();
    }

    /**
     * 渲染参数控件
     */
    renderParameter(param) {
        const { name, displayName, description, dataType, value, defaultValue, min, max, isRequired } = param;
        
        let inputHtml = '';
        const requiredMark = isRequired ? '<span class="required">*</span>' : '';
        
        switch (dataType) {
            case 'int':
            case 'double':
            case 'float':
                inputHtml = `
                    <input type="number" 
                           id="param-${name}" 
                           name="${name}" 
                           value="${value !== undefined ? value : defaultValue}"
                           ${min !== undefined ? `min="${min}"` : ''}
                           ${max !== undefined ? `max="${max}"` : ''}
                           step="${dataType === 'int' ? 1 : 0.1}"
                           class="form-input"
                           data-type="${dataType}">
                `;
                break;
                
            case 'string':
                inputHtml = `
                    <input type="text" 
                           id="param-${name}" 
                           name="${name}" 
                           value="${value !== undefined ? value : defaultValue || ''}"
                           class="form-input"
                           data-type="string">
                `;
                break;
                
            case 'bool':
            case 'boolean':
                const checked = (value !== undefined ? value : defaultValue) ? 'checked' : '';
                inputHtml = `
                    <label class="switch">
                        <input type="checkbox" 
                               id="param-${name}" 
                               name="${name}" 
                               ${checked}
                               data-type="boolean">
                        <span class="slider"></span>
                    </label>
                `;
                break;
                
            case 'enum':
            case 'select':
                const options = param.options || [];
                inputHtml = `
                    <select id="param-${name}" 
                            name="${name}" 
                            class="form-select"
                            data-type="enum">
                        ${options.map(opt => {
                            const label = typeof opt === 'string' ? opt : (opt.label || opt.Label || 'undefined');
                            const val = typeof opt === 'string' ? opt : (opt.value ?? opt.Value);
                            const currentVal = value !== undefined ? value : defaultValue;
                            return `
                                <option value="${val}" ${val === currentVal ? 'selected' : ''}>
                                    ${label}
                                </option>
                            `;
                        }).join('')}
                    </select>
                `;
                break;
                
            case 'color':
                inputHtml = `
                    <input type="color" 
                           id="param-${name}" 
                           name="${name}" 
                           value="${value !== undefined ? value : defaultValue || '#000000'}"
                           class="form-color"
                           data-type="color">
                `;
                break;
                
            case 'file':
                inputHtml = `
                    <div style="display: flex; gap: 5px;">
                        <input type="text" 
                               id="param-${name}" 
                               name="${name}" 
                               value="${value !== undefined ? value : defaultValue || ''}"
                               class="form-input"
                               readonly
                               style="flex: 1;"
                               data-type="file">
                        <button type="button" class="btn btn-sm btn-secondary btn-pick-file" data-param="${name}">...</button>
                    </div>
                `;
                break;
                
            default:
                inputHtml = `
                    <input type="text" 
                           id="param-${name}" 
                           name="${name}" 
                           value="${value !== undefined ? value : defaultValue || ''}"
                           class="form-input"
                           data-type="${dataType}">
                `;
        }

        return `
            <div class="form-group">
                <label for="param-${name}" class="form-label">
                    ${displayName || name} ${requiredMark}
                </label>
                ${inputHtml}
                ${description ? `<p class="form-description">${description}</p>` : ''}
            </div>
        `;
    }

    /**
     * 绑定事件
     */
    bindEvents() {
        const form = document.getElementById('property-form');
        if (!form) return;

        // 应用按钮
        const applyBtn = document.getElementById('btn-apply');
        if (applyBtn) {
            applyBtn.addEventListener('click', () => this.applyChanges());
        }

        // 重置按钮
        const resetBtn = document.getElementById('btn-reset');
        if (resetBtn) {
            resetBtn.addEventListener('click', () => this.resetChanges());
        }

        // 实时更新（可选）
        const inputs = form.querySelectorAll('input, select');
        inputs.forEach(input => {
            input.addEventListener('change', () => {
                if (this.onChangeCallback) {
                    this.onChangeCallback(this.getValues());
                }
            });
        });

        // 文件选择按钮
        const fileBtns = form.querySelectorAll('.btn-pick-file');
        fileBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const paramName = btn.dataset.param;
                webMessageBridge.sendMessage('PickFileCommand', {
                    parameterName: paramName,
                    filter: 'Image Files|*.bmp;*.jpg;*.png;*.jpeg|All Files|*.*'
                });
            });
        });
    }

    /**
     * 获取当前值
     */
    getValues() {
        const form = document.getElementById('property-form');
        if (!form) return {};

        const values = {};
        const inputs = form.querySelectorAll('input, select');
        
        inputs.forEach(input => {
            const name = input.name;
            const type = input.dataset.type;
            
            switch (type) {
                case 'int':
                    values[name] = parseInt(input.value, 10);
                    break;
                case 'double':
                case 'float':
                    values[name] = parseFloat(input.value);
                    break;
                case 'boolean':
                    values[name] = input.checked;
                    break;
                default:
                    values[name] = input.value;
            }
        });

        return values;
    }

    /**
     * 应用更改
     */
    applyChanges() {
        const values = this.getValues();
        
        if (this.currentOperator) {
            // 更新算子参数
            this.currentOperator.parameters.forEach(param => {
                if (values[param.name] !== undefined) {
                    param.value = values[param.name];
                }
            });
        }

        if (this.onChangeCallback) {
            this.onChangeCallback(values);
        }

        // 显示成功提示
        this.showToast('参数已应用', 'success');
    }

    /**
     * 重置更改
     */
    resetChanges() {
        if (this.currentOperator) {
            this.currentOperator.parameters.forEach(param => {
                param.value = param.defaultValue;
            });
        }
        
        this.render();
        
        if (this.onChangeCallback) {
            this.onChangeCallback(this.getValues());
        }

        this.showToast('参数已重置', 'info');
    }

    /**
     * 设置变更回调
     */
    onChange(callback) {
        this.onChangeCallback = callback;
    }

    /**
     * 显示提示
     */
    showToast(message, type = 'info') {
        // 创建提示元素
        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;
        toast.textContent = message;
        
        document.body.appendChild(toast);
        
        // 动画显示
        setTimeout(() => toast.classList.add('show'), 10);
        
        // 自动隐藏
        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), 300);
        }, 2000);
    }
}

export default PropertyPanel;
export { PropertyPanel };
