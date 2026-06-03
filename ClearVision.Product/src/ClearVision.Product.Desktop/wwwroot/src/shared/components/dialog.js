/**
 * 对话框组件
 * 模态对话框、提示框、确认框
 */

class Dialog {
    constructor() {
        this.overlay = null;
        this.dialog = null;
    }

    /**
     * 创建对话框
     */
    create(title, content, buttons = []) {
        // 创建遮罩层
        this.overlay = document.createElement('div');
        this.overlay.className = 'cv-modal-overlay';
        
        // 创建对话框
        this.dialog = document.createElement('div');
        this.dialog.className = 'cv-modal';
        
        // 标题
        const header = document.createElement('div');
        header.className = 'cv-modal-header';
        header.innerHTML = `
            <h3 class="cv-modal-title"></h3>
            <button class="cv-modal-close">&times;</button>
        `;
        header.querySelector('.cv-modal-title').textContent = String(title ?? '');
        
        // 内容
        const body = document.createElement('div');
        body.className = 'cv-modal-body';
        if (typeof content === 'string') {
            body.innerHTML = content;
        } else {
            body.appendChild(content);
        }
        
        // 按钮
        const footer = document.createElement('div');
        footer.className = 'cv-modal-footer';
        
        buttons.forEach(btn => {
            const button = document.createElement('button');
            button.className = `cv-btn ${btn.className || ''}`;
            button.textContent = btn.text;
            button.onclick = () => {
                if (btn.onClick) {
                    const result = btn.onClick();
                    if (result !== false) {
                        this.close();
                    }
                } else {
                    this.close();
                }
            };
            footer.appendChild(button);
        });
        
        // 组装
        this.dialog.appendChild(header);
        this.dialog.appendChild(body);
        this.dialog.appendChild(footer);
        this.overlay.appendChild(this.dialog);
        document.body.appendChild(this.overlay);
        
        // 绑定关闭事件
        header.querySelector('.cv-modal-close').onclick = () => this.close();
        this.overlay.onclick = (e) => {
            if (e.target === this.overlay) this.close();
        };
        
        // 动画
        setTimeout(() => {
            this.overlay.classList.add('show');
            this.dialog.classList.add('show');
        }, 10);
        
        return this;
    }

    /**
     * 关闭对话框
     */
    close() {
        if (this.overlay) {
            this.overlay.classList.remove('show');
            this.dialog.classList.remove('show');
            setTimeout(() => {
                this.overlay?.remove();
                this.overlay = null;
                this.dialog = null;
            }, 300);
        }
    }

    /**
     * 确认对话框
     */
    static confirm(title, message, onConfirm, onCancel) {
        const dialog = new Dialog();
        dialog.create(title, `
            <p class="dialog-message">${Dialog.escapeHtml(message)}</p>
        `, [
            {
                text: '取消',
                className: '',
                onClick: () => {
                    onCancel?.();
                    return true;
                }
            },
            {
                text: '确认',
                className: 'cv-btn-primary',
                onClick: () => {
                    onConfirm?.();
                    return true;
                }
            }
        ]);
        return dialog;
    }

    /**
     * 提示对话框
     */
    static alert(title, message, onClose) {
        const dialog = new Dialog();
        dialog.create(title, `
            <p class="dialog-message">${Dialog.escapeHtml(message)}</p>
        `, [
            {
                text: '确定',
                className: 'cv-btn-primary',
                onClick: () => {
                    onClose?.();
                    return true;
                }
            }
        ]);
        return dialog;
    }

    /**
     * 输入对话框
     */
    static prompt(title, message, defaultValue = '', onConfirm, onCancel) {
        const dialog = new Dialog();
        const inputId = `prompt-input-${Date.now()}`;
        
        dialog.create(title, `
            <p class="dialog-message">${Dialog.escapeHtml(message)}</p>
            <input type="text" 
                   id="${inputId}" 
                   class="cv-input" 
                   value="${Dialog.escapeHtml(defaultValue)}"
                   placeholder="请输入...">
        `, [
            {
                text: '取消',
                className: '',
                onClick: () => {
                    onCancel?.();
                    return true;
                }
            },
            {
                text: '确定',
                className: 'cv-btn-primary',
                onClick: () => {
                    const input = document.getElementById(inputId);
                    onConfirm?.(input.value);
                    return true;
                }
            }
        ]);
        
        // 自动聚焦
        setTimeout(() => {
            document.getElementById(inputId)?.focus();
        }, 100);
        
        return dialog;
    }

    /**
     * 新建工程对话框
     */
    static createProject(onConfirm, onCancel) {
        const dialog = new Dialog();
        const nameId = `project-name-${Date.now()}`;
        const descId = `project-desc-${Date.now()}`;
        
        dialog.create('新建工程', `
            <div class="form-group">
                <label for="${nameId}">工程名称 *</label>
                <input type="text" 
                       id="${nameId}" 
                       class="cv-input" 
                       placeholder="请输入工程名称">
            </div>
            <div class="form-group">
                <label for="${descId}">工程描述</label>
                <textarea id="${descId}" 
                          class="cv-input" 
                          rows="3"
                          placeholder="请输入工程描述（可选）"></textarea>
            </div>
        `, [
            {
                text: '取消',
                className: '',
                onClick: () => {
                    onCancel?.();
                    return true;
                }
            },
            {
                text: '创建',
                className: 'cv-btn-primary',
                onClick: () => {
                    const name = document.getElementById(nameId).value.trim();
                    const description = document.getElementById(descId).value.trim();
                    
                    if (!name) {
                        alert('请输入工程名称');
                        return false;
                    }
                    
                    onConfirm?.({ name, description });
                    return true;
                }
            }
        ]);
        
        setTimeout(() => {
            document.getElementById(nameId)?.focus();
        }, 100);
        
        return dialog;
    }

    /**
     * 工程列表对话框
     */
    static projectList(projects, onSelect, onDelete) {
        const dialog = new Dialog();
        
        const listHtml = projects.length === 0 
            ? '<p class="empty-text">暂无工程</p>'
            : `
                <div class="project-list">
                    ${projects.map(p => `
                        <div class="project-list-item" data-id="${Dialog.escapeHtml(p.id)}">
                            <div class="project-info">
                                <span class="project-name">${Dialog.escapeHtml(p.name)}</span>
                                <span class="project-date">${new Date(p.modifiedAt || p.createdAt).toLocaleDateString()}</span>
                            </div>
                            ${onDelete ? `<button class="cv-btn cv-btn-icon btn-delete" data-id="${Dialog.escapeHtml(p.id)}">🗑️</button>` : ''}
                        </div>
                    `).join('')}
                </div>
            `;
        
        dialog.create('打开工程', listHtml, [
            {
                text: '关闭',
                className: '',
                onClick: () => true
            }
        ]);
        
        // 绑定选择事件
        const items = dialog.dialog.querySelectorAll('.project-list-item');
        items.forEach(item => {
            item.addEventListener('click', (e) => {
                if (!e.target.classList.contains('btn-delete')) {
                    const id = item.dataset.id;
                    const project = projects.find(p => p.id === id);
                    onSelect?.(project);
                    dialog.close();
                }
            });
        });
        
        // 绑定删除事件
        if (onDelete) {
            const deleteBtns = dialog.dialog.querySelectorAll('.btn-delete');
            deleteBtns.forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const id = btn.dataset.id;
                    const project = projects.find(p => p.id === id);
                    
                    Dialog.confirm('确认删除', `确定要删除工程 "${project?.name ?? ''}" 吗？`, () => {
                        onDelete(project);
                        dialog.close();
                    });
                });
            });
        }
        
        return dialog;
    }

    static escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }
}

export default Dialog;
export { Dialog };
