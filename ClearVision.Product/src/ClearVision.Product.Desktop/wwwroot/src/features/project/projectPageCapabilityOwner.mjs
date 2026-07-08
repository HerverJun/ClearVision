import projectManager from './projectManager.js';
import {
    closeModal as closeSharedModal,
    createButton as createSharedButton,
    createModal as createSharedModal,
    showToast as showSharedToast
} from '../../shared/components/uiComponents.js';

export const PROJECT_PAGE_CAPABILITY_OWNER_ID = 'project-page-capability-v2';

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function formatDate(value) {
    const timestamp = new Date(value).getTime();
    return Number.isFinite(timestamp) && timestamp > 0
        ? new Date(timestamp).toLocaleDateString('zh-CN')
        : '-';
}

export class ProjectPageCapabilityAdapter {
    constructor({ projectManagerRef = projectManager } = {}) {
        this.projectManager = projectManagerRef;
    }

    getCurrentProject() {
        return this.projectManager.getCurrentProject?.() || null;
    }

    async listProjects({ recent = false, search = '' } = {}) {
        if (search.trim()) {
            return await this.projectManager.searchProjects(search.trim());
        }

        return recent
            ? await this.projectManager.getRecentProjects(10)
            : await this.projectManager.getProjectList();
    }

    async createProject(name, description = '') {
        return await this.projectManager.createProject(name, description);
    }

    async openProject(projectId) {
        return await this.projectManager.openProject(projectId);
    }

    async saveCurrentProject(project = this.getCurrentProject()) {
        if (!project) {
            throw new Error('没有可保存的工程');
        }

        return await this.projectManager.saveProject(project);
    }

    async deleteProject(projectId) {
        return await this.projectManager.deleteProject(projectId);
    }
}

export function createProjectPageCapabilityAdapter(options = {}) {
    return new ProjectPageCapabilityAdapter(options);
}

export class ProjectPageCapabilityOwner {
    constructor(container, {
        adapter,
        showToast = showSharedToast,
        createModal = createSharedModal,
        closeModal = closeSharedModal,
        createButton = createSharedButton
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('ProjectPageCapabilityOwner requires a container.');
        }
        if (!adapter) {
            throw new Error('ProjectPageCapabilityOwner requires an adapter.');
        }

        this.adapter = adapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.createModal = typeof createModal === 'function' ? createModal : createSharedModal;
        this.closeModal = typeof closeModal === 'function' ? closeModal : closeSharedModal;
        this.createButton = typeof createButton === 'function' ? createButton : createSharedButton;
        this.projects = [];
        this.search = '';
        this.currentTab = 'all';
        this.sortBy = 'modifiedAt';
        this.loading = false;
        this.errorMessage = '';
        this.disposed = false;
        this.refreshRequestId = 0;
        this.modalSequence = 0;

        this.handleClick = this.handleClick.bind(this);
        this.handleInput = this.handleInput.bind(this);
        this.handleChange = this.handleChange.bind(this);
        this.container.dataset.projectPageOwner = PROJECT_PAGE_CAPABILITY_OWNER_ID;
        this.container.addEventListener('click', this.handleClick);
        this.container.addEventListener('input', this.handleInput);
        this.container.addEventListener('change', this.handleChange);
        this.render();
    }

    async refresh() {
        if (this.disposed) {
            return;
        }

        const requestId = ++this.refreshRequestId;
        this.loading = true;
        this.errorMessage = '';
        this.renderList();

        try {
            const projects = await this.adapter.listProjects({
                recent: this.currentTab === 'recent',
                search: this.search
            });
            if (this.disposed || requestId !== this.refreshRequestId) {
                return;
            }

            this.projects = Array.isArray(projects) ? projects : [];
            this.sortProjects();
        } catch (error) {
            if (requestId === this.refreshRequestId) {
                this.errorMessage = error?.message || '工程列表加载失败';
                this.projects = [];
            }
        } finally {
            if (requestId === this.refreshRequestId) {
                this.loading = false;
                this.render();
            }
        }
    }

    sortProjects() {
        this.projects.sort((left, right) => {
            const read = project => {
                if (this.sortBy === 'name') {
                    return String(project.name || '').toLowerCase();
                }

                return new Date(project[this.sortBy] || project.modifiedAt || project.createdAt).getTime() || 0;
            };
            const leftValue = read(left);
            const rightValue = read(right);
            return leftValue < rightValue ? 1 : leftValue > rightValue ? -1 : 0;
        });
    }

    handleInput(event) {
        const input = event.target?.closest?.('[data-project-search]');
        if (!input || this.disposed) {
            return;
        }

        this.search = input.value || '';
    }

    handleChange(event) {
        const sort = event.target?.closest?.('[data-project-sort]');
        if (!sort || this.disposed) {
            return;
        }

        this.sortBy = sort.value || 'modifiedAt';
        this.sortProjects();
        this.renderList();
    }

    handleClick(event) {
        const actionEl = event.target?.closest?.('[data-project-action]');
        if (!actionEl || this.disposed) {
            return;
        }

        event.preventDefault();
        const action = actionEl.dataset.projectAction;
        if (action === 'search') {
            void this.refresh();
        } else if (action === 'tab') {
            this.currentTab = actionEl.dataset.projectTab || 'all';
            void this.refresh();
        } else if (action === 'new') {
            void this.createProject();
        } else if (action === 'open') {
            void this.openProject(actionEl.closest('[data-project-id]')?.dataset?.projectId || '');
        } else if (action === 'save-current') {
            void this.saveCurrentProject();
        } else if (action === 'delete') {
            void this.deleteProject(actionEl.closest('[data-project-id]')?.dataset?.projectId || '');
        } else if (action === 'import') {
            globalThis.showImportDialog?.();
        } else if (action === 'export') {
            globalThis.showProjectExportDialog?.();
        }
    }

    createProject() {
        if (this.disposed) {
            return null;
        }

        const formId = `project-page-create-${++this.modalSequence}`;
        const content = document.createElement('div');
        content.className = 'new-project-form project-page-create-form';
        content.dataset.projectCreateForm = '';

        const nameGroup = document.createElement('div');
        nameGroup.className = 'form-group';

        const nameLabel = document.createElement('label');
        nameLabel.setAttribute('for', `${formId}-name`);
        nameLabel.textContent = '工程名称 ';

        const requiredMark = document.createElement('span');
        requiredMark.className = 'required';
        requiredMark.textContent = '*';
        nameLabel.appendChild(requiredMark);

        const nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.id = `${formId}-name`;
        nameInput.className = 'cv-input';
        nameInput.placeholder = '请输入工程名称';
        nameInput.dataset.projectNameInput = '';
        nameInput.setAttribute('autocomplete', 'off');

        const nameError = document.createElement('div');
        nameError.className = 'cv-field-error';
        nameError.dataset.projectNameError = '';
        nameError.setAttribute('role', 'alert');
        nameError.hidden = true;

        nameGroup.appendChild(nameLabel);
        nameGroup.appendChild(nameInput);
        nameGroup.appendChild(nameError);

        const descGroup = document.createElement('div');
        descGroup.className = 'form-group';

        const descLabel = document.createElement('label');
        descLabel.setAttribute('for', `${formId}-desc`);
        descLabel.textContent = '描述';

        const descInput = document.createElement('input');
        descInput.type = 'text';
        descInput.id = `${formId}-desc`;
        descInput.className = 'cv-input';
        descInput.placeholder = '可选描述';
        descInput.dataset.projectDescInput = '';
        descInput.setAttribute('autocomplete', 'off');

        descGroup.appendChild(descLabel);
        descGroup.appendChild(descInput);

        content.appendChild(nameGroup);
        content.appendChild(descGroup);

        let modalOverlay = null;
        let createInFlight = false;

        const clearNameError = () => {
            nameError.textContent = '';
            nameError.hidden = true;
            nameGroup.classList?.remove('invalid');
            nameInput.removeAttribute?.('aria-invalid');
        };

        const showNameError = () => {
            nameError.textContent = '请输入工程名称';
            nameError.hidden = false;
            nameGroup.classList?.add('invalid');
            nameInput.setAttribute('aria-invalid', 'true');
            this.showToast('请输入工程名称', 'warning');
            nameInput.focus?.();
        };

        nameInput.addEventListener('input', () => {
            if (nameInput.value.trim()) {
                clearNameError();
            }
        });

        const btnCancel = this.createButton({
            text: '取消',
            type: 'secondary',
            onClick: () => this.closeModal(modalOverlay)
        });
        btnCancel.dataset.projectModalAction = 'cancel';

        const btnCreate = this.createButton({
            text: '创建',
            onClick: async () => {
                if (createInFlight) {
                    return;
                }

                const name = nameInput.value.trim();
                const description = descInput.value.trim();
                if (!name) {
                    showNameError();
                    return;
                }

                clearNameError();
                createInFlight = true;
                btnCreate.disabled = true;

                try {
                    const project = await this.adapter.createProject(name, description);
                    this.showToast(`工程 "${project?.name || name}" 已创建`, 'success');
                    this.closeModal(modalOverlay);
                    await this.refresh();
                } catch (error) {
                    createInFlight = false;
                    btnCreate.disabled = false;
                    this.showToast(`创建失败: ${error?.message || error}`, 'error');
                }
            }
        });
        btnCreate.dataset.projectModalAction = 'create';

        nameInput.addEventListener('keydown', (event) => {
            if (event.key === 'Enter') {
                event.preventDefault();
                btnCreate.click?.();
            }
        });

        modalOverlay = this.createModal({
            title: '新建工程',
            content,
            footer: [btnCancel, btnCreate],
            width: '400px'
        });
        modalOverlay?.setAttribute?.('data-project-create-modal', '');
        modalOverlay?.querySelector?.('.cv-modal')?.setAttribute?.('data-project-create-dialog', '');

        setTimeout(() => nameInput.focus?.(), 0);
        return modalOverlay;
    }

    async openProject(projectId) {
        if (!projectId) {
            return null;
        }

        try {
            const project = await this.adapter.openProject(projectId);
            this.showToast(`工程 "${project?.name || projectId}" 已打开`, 'success');
            return project;
        } catch (error) {
            this.showToast(`打开失败: ${error.message}`, 'error');
            return null;
        }
    }

    async saveCurrentProject() {
        try {
            await this.adapter.saveCurrentProject();
            this.showToast('当前工程已保存', 'success');
            await this.refresh();
            return true;
        } catch (error) {
            this.showToast(`保存失败: ${error.message}`, 'error');
            return false;
        }
    }

    deleteProject(projectId) {
        if (!projectId || this.disposed) {
            return null;
        }

        const project = this.projects.find(item => item?.id === projectId) || null;
        const projectName = project?.name || '未命名工程';

        const content = document.createElement('div');
        content.className = 'cv-modal-form project-page-delete-confirm';
        content.dataset.projectDeleteConfirm = '';

        const message = document.createElement('p');
        message.className = 'project-page-delete-message';
        message.textContent = '确定要删除此工程吗？此操作无法撤销。';

        const details = document.createElement('dl');
        details.className = 'project-page-delete-details';

        const nameTerm = document.createElement('dt');
        nameTerm.textContent = '工程名称';
        const nameValue = document.createElement('dd');
        nameValue.dataset.projectDeleteName = '';
        nameValue.textContent = projectName;

        const idTerm = document.createElement('dt');
        idTerm.textContent = '工程 ID';
        const idValue = document.createElement('dd');
        idValue.dataset.projectDeleteId = '';
        idValue.textContent = projectId;

        details.appendChild(nameTerm);
        details.appendChild(nameValue);
        details.appendChild(idTerm);
        details.appendChild(idValue);
        content.appendChild(message);
        content.appendChild(details);

        let modalOverlay = null;
        let deleteInFlight = false;

        const btnCancel = this.createButton({
            text: '取消',
            type: 'secondary',
            onClick: () => this.closeModal(modalOverlay)
        });
        btnCancel.dataset.projectModalAction = 'cancel-delete';

        const btnDelete = this.createButton({
            text: '删除',
            type: 'danger',
            onClick: async () => {
                if (deleteInFlight) {
                    return;
                }

                deleteInFlight = true;
                btnDelete.disabled = true;

                try {
                    await this.adapter.deleteProject(projectId);
                    this.showToast(`工程 "${projectName}" 已删除`, 'success');
                    this.closeModal(modalOverlay);
                    await this.refresh();
                } catch (error) {
                    deleteInFlight = false;
                    btnDelete.disabled = false;
                    this.showToast(`删除失败: ${error?.message || error}`, 'error');
                }
            }
        });
        btnDelete.dataset.projectModalAction = 'confirm-delete';

        modalOverlay = this.createModal({
            title: '确认删除',
            content,
            footer: [btnCancel, btnDelete],
            width: '420px'
        });
        modalOverlay?.setAttribute?.('data-project-delete-modal', '');
        modalOverlay?.querySelector?.('.cv-modal')?.setAttribute?.('data-project-delete-dialog', '');

        return modalOverlay;
    }

    render() {
        if (this.disposed || !this.container) {
            return;
        }

        this.container.innerHTML = `
            <div class="project-view-header project-page-capability-owner" data-owner="${PROJECT_PAGE_CAPABILITY_OWNER_ID}">
                <div class="project-search">
                    <input type="text" class="cv-input" data-project-search value="${escapeHtml(this.search)}" placeholder="搜索工程...">
                    <button type="button" class="btn btn-secondary" data-project-action="search">搜索</button>
                    <button type="button" class="btn btn-primary" data-project-action="new">新建工程</button>
                    <button type="button" class="btn btn-secondary" data-project-action="save-current">保存当前工程</button>
                    <button type="button" class="btn btn-secondary" data-project-action="import">导入</button>
                    <button type="button" class="btn btn-secondary" data-project-action="export">导出</button>
                </div>
                <div class="project-view-controls">
                    <select class="sort-select" data-project-sort>
                        <option value="modifiedAt" ${this.sortBy === 'modifiedAt' ? 'selected' : ''}>最近修改</option>
                        <option value="createdAt" ${this.sortBy === 'createdAt' ? 'selected' : ''}>创建时间</option>
                        <option value="name" ${this.sortBy === 'name' ? 'selected' : ''}>名称</option>
                    </select>
                    <div class="project-tabs">
                        <button type="button" class="tab-btn ${this.currentTab === 'all' ? 'active' : ''}" data-project-action="tab" data-project-tab="all">全部工程</button>
                        <button type="button" class="tab-btn ${this.currentTab === 'recent' ? 'active' : ''}" data-project-action="tab" data-project-tab="recent">最近打开</button>
                    </div>
                </div>
            </div>
            <div class="project-list" id="project-list" data-project-list></div>
        `;
        this.renderList();
    }

    renderList() {
        const list = this.container?.querySelector?.('[data-project-list]');
        if (!list) {
            return;
        }

        if (this.loading) {
            list.innerHTML = '<p class="empty-text">加载中...</p>';
            return;
        }

        if (this.errorMessage) {
            list.innerHTML = `<p class="empty-text">${escapeHtml(this.errorMessage)}</p>`;
            return;
        }

        if (this.projects.length === 0) {
            list.innerHTML = '<div class="empty-state"><h3 class="empty-state-title">还没有工程</h3><p class="empty-state-desc">创建或导入工程后即可开始检测。</p></div>';
            return;
        }

        list.innerHTML = `
            <div class="projects-list">
                ${this.projects.map(project => `
                    <article class="project-list-item" data-project-id="${escapeHtml(project.id)}">
                        <div class="project-list-info">
                            <div class="project-list-title">${escapeHtml(project.name || '未命名工程')}</div>
                            <div class="project-list-desc">${escapeHtml(project.description || '暂无描述')}</div>
                        </div>
                        <div class="project-list-meta">
                            <span>${formatDate(project.modifiedAt || project.createdAt)}</span>
                            <span>${escapeHtml(project.id || '')}</span>
                        </div>
                        <div class="project-list-actions">
                            <button type="button" class="action-btn btn-open" title="打开" data-project-action="open">打开</button>
                            <button type="button" class="action-btn btn-delete" title="删除" data-project-action="delete">删除</button>
                        </div>
                    </article>
                `).join('')}
            </div>
        `;
    }

    destroy() {
        this.dispose();
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        this.refreshRequestId += 1;
        this.container.removeEventListener('click', this.handleClick);
        this.container.removeEventListener('input', this.handleInput);
        this.container.removeEventListener('change', this.handleChange);
        delete this.container.dataset.projectPageOwner;
        this.container.innerHTML = '';
    }
}

export default ProjectPageCapabilityOwner;
