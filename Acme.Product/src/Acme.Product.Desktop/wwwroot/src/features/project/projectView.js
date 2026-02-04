/**
 * 工程视图组件
 * 展示工程列表，支持搜索、打开、删除
 */

import projectManager from './projectManager.js';
import { showToast, createModal, closeModal, createButton } from '../../shared/components/uiComponents.js';

export class ProjectView {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.currentTab = 'all'; // 'all' | 'recent'
        this.projects = [];
        
        if (!this.container) {
            console.error('[ProjectView] 容器未找到:', containerId);
            return;
        }
        
        this.init();
    }
    
    async init() {
        this.bindEvents();
        await this.loadProjects();
    }
    
    bindEvents() {
        // 搜索按钮
        const searchBtn = document.getElementById('btn-search-project');
        const searchInput = document.getElementById('project-search-input');
        
        if (searchBtn && searchInput) {
            searchBtn.addEventListener('click', () => this.handleSearch(searchInput.value));
            searchInput.addEventListener('keypress', (e) => {
                if (e.key === 'Enter') this.handleSearch(searchInput.value);
            });
        }
        
        // 新建工程按钮（工程分页内）
        const newProjectBtn = document.getElementById('btn-new-project-inline');
        if (newProjectBtn) {
            newProjectBtn.addEventListener('click', () => this.showNewProjectDialog());
        }
        
        // Tab 切换
        const tabs = this.container.querySelectorAll('.tab-btn');
        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                tabs.forEach(t => t.classList.remove('active'));
                tab.classList.add('active');
                this.currentTab = tab.dataset.tab;
                this.loadProjects();
            });
        });
    }
    
    async loadProjects() {
        const listContainer = document.getElementById('project-list');
        if (!listContainer) return;
        
        listContainer.innerHTML = '<p class="loading-text">加载中...</p>';
        
        try {
            if (this.currentTab === 'recent') {
                this.projects = await projectManager.getRecentProjects(10);
            } else {
                this.projects = await projectManager.getProjectList();
            }
            
            this.renderProjects(listContainer);
        } catch (error) {
            console.error('[ProjectView] 加载工程列表失败:', error);
            listContainer.innerHTML = '<p class="error-text">加载失败，请重试</p>';
        }
    }
    
    renderProjects(container) {
        if (!this.projects || this.projects.length === 0) {
            container.innerHTML = '<p class="empty-text">暂无工程，点击"新建"创建第一个工程</p>';
            return;
        }
        
        container.innerHTML = this.projects.map(project => this.createProjectCard(project)).join('');
        
        // 绑定卡片事件
        container.querySelectorAll('.project-card').forEach(card => {
            const projectId = card.dataset.id;
            
            // 双击打开
            card.addEventListener('dblclick', () => this.openProject(projectId));
            
            // 打开按钮
            card.querySelector('.btn-open')?.addEventListener('click', (e) => {
                e.stopPropagation();
                this.openProject(projectId);
            });
            
            // 删除按钮
            card.querySelector('.btn-delete')?.addEventListener('click', (e) => {
                e.stopPropagation();
                this.confirmDelete(projectId, card.querySelector('.project-card-title').textContent);
            });
        });
    }
    
    createProjectCard(project) {
        const createdDate = new Date(project.createdAt).toLocaleDateString('zh-CN');
        const modifiedDate = new Date(project.modifiedAt).toLocaleDateString('zh-CN');
        
        return `
            <div class="project-card" data-id="${project.id}">
                <div class="project-card-title">${this.escapeHtml(project.name)}</div>
                <div class="project-card-desc">${this.escapeHtml(project.description || '暂无描述')}</div>
                <div class="project-card-meta">
                    <span>创建: ${createdDate}</span>
                    <span>修改: ${modifiedDate}</span>
                </div>
                <div class="project-card-actions">
                    <button class="btn btn-primary btn-open">打开</button>
                    <button class="btn btn-danger btn-delete">删除</button>
                </div>
            </div>
        `;
    }
    
    async openProject(projectId) {
        try {
            showToast('正在打开工程...', 'info');
            const project = await projectManager.openProject(projectId);
            
            if (project) {
                showToast(`工程 "${project.name}" 已打开`, 'success');
                
                // 触发自定义事件，通知 app.js 切换到流程视图
                window.dispatchEvent(new CustomEvent('projectOpened', { detail: project }));
            }
        } catch (error) {
            console.error('[ProjectView] 打开工程失败:', error);
            showToast('打开工程失败: ' + error.message, 'error');
        }
    }
    
    confirmDelete(projectId, projectName) {
        const content = document.createElement('p');
        content.textContent = `确定要删除工程 "${projectName}" 吗？此操作无法撤销。`;
        
        let modalOverlay = null;
        
        const btnCancel = createButton({
            text: '取消',
            type: 'secondary',
            onClick: () => closeModal(modalOverlay)
        });
        
        const btnDelete = createButton({
            text: '删除',
            type: 'danger',
            onClick: async () => {
                try {
                    await projectManager.deleteProject(projectId);
                    showToast('工程已删除', 'success');
                    closeModal(modalOverlay);
                    await this.loadProjects();
                } catch (error) {
                    showToast('删除失败: ' + error.message, 'error');
                }
            }
        });
        
        modalOverlay = createModal({
            title: '确认删除',
            content,
            footer: [btnCancel, btnDelete],
            width: '400px'
        });
    }
    
    async handleSearch(keyword) {
        if (!keyword.trim()) {
            await this.loadProjects();
            return;
        }
        
        const listContainer = document.getElementById('project-list');
        if (!listContainer) return;
        
        listContainer.innerHTML = '<p class="loading-text">搜索中...</p>';
        
        try {
            this.projects = await projectManager.searchProjects(keyword);
            this.renderProjects(listContainer);
        } catch (error) {
            console.error('[ProjectView] 搜索失败:', error);
            listContainer.innerHTML = '<p class="error-text">搜索失败，请重试</p>';
        }
    }
    
    // 刷新列表（外部调用）
    async refresh() {
        await this.loadProjects();
    }
    
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
    
    /**
     * 显示新建工程对话框
     */
    showNewProjectDialog() {
        const content = document.createElement('div');
        content.className = 'new-project-form';
        content.innerHTML = `
            <div class="form-group">
                <label for="new-project-name">工程名称 <span class="required">*</span></label>
                <input type="text" id="new-project-name" class="cv-input" placeholder="请输入工程名称" />
            </div>
            <div class="form-group">
                <label for="new-project-desc">描述</label>
                <input type="text" id="new-project-desc" class="cv-input" placeholder="可选描述" />
            </div>
        `;
        
        let modalOverlay = null;
        
        const btnCancel = createButton({
            text: '取消',
            type: 'secondary',
            onClick: () => closeModal(modalOverlay)
        });
        
        const btnCreate = createButton({
            text: '创建',
            onClick: async () => {
                const nameInput = document.getElementById('new-project-name');
                const descInput = document.getElementById('new-project-desc');
                const name = nameInput?.value?.trim();
                const desc = descInput?.value?.trim() || '';
                
                if (!name) {
                    showToast('请输入工程名称', 'warning');
                    nameInput?.focus();
                    return;
                }
                
                try {
                    const project = await projectManager.createProject(name, desc);
                    showToast(`工程 "${name}" 已创建`, 'success');
                    closeModal(modalOverlay);
                    
                    // 触发工程打开事件，切换到流程视图
                    window.dispatchEvent(new CustomEvent('projectOpened', { detail: project }));
                } catch (error) {
                    console.error('[ProjectView] 创建工程失败:', error);
                    showToast('创建失败: ' + error.message, 'error');
                }
            }
        });
        
        modalOverlay = createModal({
            title: '新建工程',
            content,
            footer: [btnCancel, btnCreate],
            width: '400px'
        });
        
        // 自动聚焦到名称输入框
        setTimeout(() => {
            document.getElementById('new-project-name')?.focus();
        }, 100);
    }
}

export default ProjectView;
