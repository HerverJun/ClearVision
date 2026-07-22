/**
 * 工程管理模块
 * 负责工程的创建、打开、保存、列表管理
 */

import httpClient from '../../core/messaging/httpClient.js';
import { createSignal } from '../../core/state/store.js';
import { saveGlobalVariableSchema } from '../global-variables/globalVariableStore.js';

// 工程状态
const [getCurrentProject, setCurrentProject, subscribeProject] = createSignal(null);
const [getProjectList, setProjectList, subscribeProjectList] = createSignal([]);
const [getRecentProjects, setRecentProjects, subscribeRecentProjects] = createSignal([]);

class ProjectManager {
    constructor() {
        this.currentProject = null;
        this.unsavedChanges = false;
        this.openProjectRequestId = 0;
        this.savedGlobalVariablesSignature = '';
        this.flowSnapshotProvider = null;
    }

    setFlowSnapshotProvider(provider) {
        this.flowSnapshotProvider = typeof provider === 'function' ? provider : null;
    }

    syncFlowSnapshotForPersistence() {
        if (!this.currentProject || !this.flowSnapshotProvider) {
            return null;
        }

        const flow = this.flowSnapshotProvider(this.currentProject);
        if (!flow) {
            return null;
        }

        this.currentProject.flow = flow;
        return flow;
    }

    invalidateOpenProjectRequests() {
        this.openProjectRequestId += 1;
        return this.openProjectRequestId;
    }

    rememberProjectInCaches(project) {
        if (!project?.id) {
            return;
        }

        const upsert = (projects) => {
            if (!Array.isArray(projects)) {
                return [project];
            }

            return [
                project,
                ...projects.filter(item => item?.id !== project.id)
            ];
        };

        setProjectList(upsert(getProjectList()));
        setRecentProjects(upsert(getRecentProjects()));
    }

    forgetProjectFromCaches(projectId) {
        if (!projectId) {
            return;
        }

        const remove = (projects) => Array.isArray(projects)
            ? projects.filter(item => item?.id !== projectId)
            : [];

        setProjectList(remove(getProjectList()));
        setRecentProjects(remove(getRecentProjects()));
    }

    async prepareForProjectSwitch() {
        if (!this.currentProject || !this.unsavedChanges) {
            return true;
        }

        const shouldSave = window.confirm('当前工程有未保存的更改，是否先保存？');
        if (shouldSave) {
            await this.saveProject();
        }

        return true;
    }

    /**
     * 获取工程列表
     */
    async getProjectList() {
        try {
            const projects = await httpClient.get('/projects');
            setProjectList(projects);
            return projects;
        } catch (error) {
            console.error('[ProjectManager] 获取工程列表失败:', error);
            throw error;
        }
    }

    /**
     * 获取最近打开的工程
     */
    async getRecentProjects(count = 10) {
        try {
            const projects = await httpClient.get(`/projects/recent?count=${count}`);
            setRecentProjects(projects);
            return projects;
        } catch (error) {
            console.error('[ProjectManager] 获取最近工程失败:', error);
            throw error;
        }
    }

    /**
     * 搜索工程
     */
    async searchProjects(keyword) {
        try {
            const projects = await httpClient.get(`/projects/search?keyword=${encodeURIComponent(keyword)}`);
            return projects;
        } catch (error) {
            console.error('[ProjectManager] 搜索工程失败:', error);
            throw error;
        }
    }

    /**
     * 创建新工程
     */
    async createProject(name, description = '', purpose = 'Inspection') {
        try {
            this.invalidateOpenProjectRequests();
            await this.prepareForProjectSwitch();
            const project = await httpClient.post('/projects', {
                name,
                description,
                flow: {
                    name: 'MainFlow',
                    purpose: purpose === 'Commissioning' ? 'Commissioning' : 'Inspection',
                    operators: [],
                    connections: []
                }
            });
            
            this.currentProject = project;
            this.rememberGlobalVariableBaseline(project);
            setCurrentProject(project);
            this.unsavedChanges = false;
            this.updateStatusBar(project);
            this.rememberProjectInCaches(project);
            
            console.log('[ProjectManager] 工程创建成功:', project.id);
            return project;
        } catch (error) {
            console.error('[ProjectManager] 创建工程失败:', error);
            throw error;
        }
    }

    async createDemoProject(mode = 'full') {
        const endpoint = mode === 'simple' ? '/demo/create-simple' : '/demo/create';

        try {
            this.invalidateOpenProjectRequests();
            await this.prepareForProjectSwitch();
            const project = await httpClient.post(endpoint);

            this.currentProject = project;
            this.rememberGlobalVariableBaseline(project);
            setCurrentProject(project);
            this.unsavedChanges = false;
            this.updateStatusBar(project);
            this.rememberProjectInCaches(project);

            console.log('[ProjectManager] 示例工程创建成功:', project.id, '| mode:', mode);
            return project;
        } catch (error) {
            console.error('[ProjectManager] 创建示例工程失败:', error);
            throw error;
        }
    }

    async getDemoGuide() {
        try {
            return await httpClient.get('/demo/guide');
        } catch (error) {
            console.error('[ProjectManager] 获取示例工程引导失败:', error);
            throw error;
        }
    }

    /**
     * 打开工程
     */
    async openProject(projectId) {
        try {
            if (this.currentProject?.id === projectId) {
                if (this.unsavedChanges) {
                    await this.prepareForProjectSwitch();
                }

                return this.currentProject;
            }

            const requestId = this.invalidateOpenProjectRequests();
            await this.prepareForProjectSwitch();
            const project = await httpClient.get(`/projects/${projectId}`);
            if (requestId !== this.openProjectRequestId) {
                console.warn('[ProjectManager] 忽略过期的工程打开结果:', projectId);
                return null;
            }
            
            this.currentProject = project;
            this.rememberGlobalVariableBaseline(project);
            setCurrentProject(project);
            this.unsavedChanges = false;
            
            // 更新状态栏
            this.updateStatusBar(project);
            
            console.log('[ProjectManager] 工程打开成功:', project.id);
            return project;
        } catch (error) {
            console.error('[ProjectManager] 打开工程失败:', error);
            throw error;
        }
    }

    /**
     * 保存工程
     */
    async saveProject(projectData = null) {
        if (!this.currentProject) {
            throw new Error('No project is open.');
        }

        const syncedFlow = this.syncFlowSnapshotForPersistence();
        const targetProjectId = this.currentProject.id;
        const data = projectData || this.currentProject;
        const flow = syncedFlow || data.flow || data.Flow || this.currentProject.flow || this.currentProject.Flow || null;
        const globalVariables = data.globalVariables || data.GlobalVariables || this.currentProject.globalVariables || this.currentProject.GlobalVariables;
        const globalVariablesChanged = this.haveGlobalVariablesChanged(globalVariables);

        try {
            const expectedRevision = readPersistenceRevision(this.currentProject);
            const updatePayload = {
                name: data.name,
                description: data.description
            };
            if (Number.isFinite(expectedRevision)) {
                updatePayload.expectedPersistenceRevision = expectedRevision;
            }

            if (globalVariablesChanged) {
                updatePayload.globalVariables = globalVariables;
            }

            const saved = await httpClient.put(`/projects/${targetProjectId}`, updatePayload);
            const savedProject = isProjectPayload(saved) ? saved : {};
            let flowExpectedRevision = readPersistenceRevision(savedProject);
            if (!Number.isFinite(flowExpectedRevision)) {
                flowExpectedRevision = expectedRevision;
            }

            let savedFlow = null;
            if (flow) {
                savedFlow = await httpClient.put(
                    `/projects/${targetProjectId}/flow`,
                    toUpdateFlowRequest(flow, flowExpectedRevision));
                const flowRevision = readPersistenceRevision(savedFlow);
                if (Number.isFinite(flowRevision)) {
                    savedProject.persistenceRevision = flowRevision;
                    savedProject.PersistenceRevision = flowRevision;
                }
            }

            if (!this.currentProject || this.currentProject.id !== targetProjectId) {
                return true;
            }

            const flowFromResponse = savedFlow?.flow || savedFlow?.Flow;
            Object.assign(this.currentProject, savedProject);
            this.currentProject.name = savedProject.name ?? savedProject.Name ?? data.name ?? this.currentProject.name;
            this.currentProject.description = savedProject.description ?? savedProject.Description ?? data.description ?? this.currentProject.description;
            this.currentProject.flow = flowFromResponse || savedProject.flow || savedProject.Flow || flow || this.currentProject.flow;
            const savedGlobalVariables = savedProject.globalVariables || savedProject.GlobalVariables || globalVariables || this.currentProject.globalVariables;
            if (savedGlobalVariables) {
                this.currentProject.globalVariables = savedGlobalVariables;
            }
            setCurrentProject(this.currentProject);
            this.currentProject.modifiedAt = new Date().toISOString();
            this.unsavedChanges = false;
            this.rememberGlobalVariableBaseline(this.currentProject);
            this.rememberProjectInCaches(this.currentProject);
            this.updateStatusBar(this.currentProject);
            this.updateTitle();
            try {
                localStorage.removeItem(`cv.flow-draft.v2:${targetProjectId}`);
            } catch {
                // Local draft cleanup is best-effort after the authoritative save succeeds.
            }

            console.log('[ProjectManager] 工程保存成功:', targetProjectId);
            return true;
        } catch (error) {
            console.error('[ProjectManager] 保存工程失败:', error);
            throw error;
        }
    }
    /**
     * 删除工程
     */
    async deleteProject(projectId) {
        try {
            if (this.currentProject?.id === projectId) {
                this.invalidateOpenProjectRequests();
            }
            await httpClient.delete(`/projects/${projectId}`);
            
            // 如果删除的是当前工程，清空当前工程
            if (this.currentProject && this.currentProject.id === projectId) {
                await this.closeProject({ promptToSave: false });
            }
            this.forgetProjectFromCaches(projectId);
            
            console.log('[ProjectManager] 工程删除成功:', projectId);
            return true;
        } catch (error) {
            console.error('[ProjectManager] 删除工程失败:', error);
            throw error;
        }
    }

    /**
     * 关闭当前工程
     */
    async closeProject(options = {}) {
        const { promptToSave = true } = options;
        this.invalidateOpenProjectRequests();
        if (this.unsavedChanges && promptToSave) {
            const confirm = window.confirm('Project has unsaved changes. Save now?');
            if (confirm) {
                await this.saveProject();
            }
        }

        this.currentProject = null;
        this.savedGlobalVariablesSignature = '';
        setCurrentProject(null);
        this.unsavedChanges = false;
        this.updateStatusBar(null);
        return true;
    }

    /**
     * 更新当前工程数据
     */
    updateProject(updates) {
        if (!this.currentProject) return;

        this.currentProject = {
            ...this.currentProject,
            ...updates,
            modifiedAt: new Date().toISOString()
        };

        setCurrentProject(this.currentProject);
        this.unsavedChanges = true;
        this.updateTitle();
    }

    /**
     * 更新流程
     */
    updateFlow(flowData) {
        if (!this.currentProject) return;

        this.currentProject.flow = flowData;
        this.unsavedChanges = true;
        this.updateTitle();
    }

    markFlowDirty() {
        if (!this.currentProject) return;

        this.unsavedChanges = true;
        this.updateTitle();
    }

    updateGlobalVariables(globalVariables) {
        if (!this.currentProject) return;

        this.currentProject.globalVariables = globalVariables || {
            schemaVersion: '1.0',
            variables: [],
            sourceBindings: [],
            targetBindings: []
        };
        this.unsavedChanges = true;
        this.updateTitle();
        setCurrentProject(this.currentProject);
    }

    async saveGlobalVariables(globalVariables = null, expectedProjectId = null) {
        if (!this.currentProject) {
            throw new Error('No project is open.');
        }

        const targetProject = this.currentProject;
        const targetProjectId = targetProject.id;
        if (expectedProjectId !== null && expectedProjectId !== undefined && !sameProjectId(expectedProjectId, targetProjectId)) {
            throw new Error('Global variable save target does not match the current project.');
        }

        const schema = globalVariables || targetProject.globalVariables || {
            schemaVersion: '1.0',
            variables: [],
            sourceBindings: [],
            targetBindings: []
        };
        const saved = await saveGlobalVariableSchema(targetProjectId, schema);
        if (!this.currentProject || !sameProjectId(this.currentProject.id, targetProjectId)) {
            return saved;
        }

        this.currentProject = {
            ...this.currentProject,
            globalVariables: saved
        };
        this.rememberGlobalVariableBaseline(this.currentProject);
        setCurrentProject(this.currentProject);
        this.unsavedChanges = false;
        this.rememberProjectInCaches(this.currentProject);
        this.updateTitle();
        return saved;
    }
    /**
     * 检查是否有未保存的更改
     */
    hasUnsavedChanges() {
        return this.unsavedChanges;
    }

    /**
     * 获取当前工程
     */
    getCurrentProject() {
        return this.currentProject;
    }

    rememberGlobalVariableBaseline(project = this.currentProject) {
        this.savedGlobalVariablesSignature = getGlobalVariablesSignature(project?.globalVariables || project?.GlobalVariables);
    }

    haveGlobalVariablesChanged(globalVariables) {
        const signature = getGlobalVariablesSignature(globalVariables);
        return Boolean(signature) && signature !== this.savedGlobalVariablesSignature;
    }

    /**
     * 更新状态栏
     */
    updateStatusBar(project) {
        const projectNameEl = document.getElementById('project-name');
        const versionEl = document.getElementById('version');
        
        if (projectNameEl) {
            projectNameEl.textContent = project ? project.name : 'Untitled Project';
        }
        
        if (versionEl && project) {
            versionEl.textContent = `v${project.version || '1.0.0'}`;
        }
    }

    /**
     * 更新窗口标题
     */
    updateTitle() {
        const unsavedMark = this.unsavedChanges ? ' *' : '';
        const projectName = this.currentProject ? this.currentProject.name : 'Untitled';
        document.title = `${projectName}${unsavedMark} - ClearVision`;
    }

    /**
     * 导出工程
     */
    async exportProject(projectId, format = 'json') {
        try {
            const project = await httpClient.get(`/projects/${projectId}`);
            
            let content, filename, mimeType;
            
            switch (format) {
                case 'json':
                    content = JSON.stringify(project, null, 2);
                    filename = `${project.name}.json`;
                    mimeType = 'application/json';
                    break;
                default:
                    throw new Error(`不支持的导出格式: ${format}`);
            }

            // 下载文件
            const blob = new Blob([content], { type: mimeType });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
            
            return true;
        } catch (error) {
            console.error('[ProjectManager] 导出工程失败:', error);
            throw error;
        }
    }

    /**
     * 导入工程
     */
    async importProject(file) {
        try {
            const content = await file.text();
            const projectData = JSON.parse(content);
            
            // 创建新工程
            const project = await this.createProject(
                projectData.name || 'Imported Project',
                projectData.description || ''
            );

            // 导入流程数据
            if (projectData.flow) {
                await this.updateFlow(projectData.flow);
                await this.saveProject();
            }

            return project;
        } catch (error) {
            console.error('[ProjectManager] 导入工程失败:', error);
            throw error;
        }
    }
}

function toUpdateFlowRequest(flow, expectedPersistenceRevision = null) {
    const request = {
        operators: Array.isArray(flow?.operators) ? flow.operators : (flow?.Operators || []),
        connections: Array.isArray(flow?.connections) ? flow.connections : (flow?.Connections || []),
        decisionConfiguration: flow?.decisionConfiguration ?? flow?.DecisionConfiguration ?? null
    };
    const name = flow?.name ?? flow?.Name;
    if (typeof name === 'string' && name.trim()) {
        request.name = name.trim();
    }

    if (expectedPersistenceRevision !== null && expectedPersistenceRevision !== undefined) {
        const revision = Number(expectedPersistenceRevision);
        if (Number.isFinite(revision)) {
            request.expectedPersistenceRevision = revision;
        }
    }

    return request;
}

function readPersistenceRevision(source) {
    const value = source?.persistenceRevision ?? source?.PersistenceRevision;
    const revision = Number(value);
    return Number.isFinite(revision) ? revision : null;
}

function getGlobalVariablesSignature(globalVariables) {
    if (!globalVariables) {
        return '';
    }

    return JSON.stringify(globalVariables);
}

function sameProjectId(left, right) {
    if (left === null || left === undefined || right === null || right === undefined) {
        return false;
    }

    return String(left).trim().toLowerCase() === String(right).trim().toLowerCase();
}

function isProjectPayload(value) {
    if (!value || typeof value !== 'object') {
        return false;
    }

    return Boolean(
        value.id ||
        value.Id ||
        value.name ||
        value.Name ||
        Object.prototype.hasOwnProperty.call(value, 'description') ||
        Object.prototype.hasOwnProperty.call(value, 'Description') ||
        value.flow ||
        value.Flow ||
        value.globalVariables ||
        value.GlobalVariables
    );
}

// 创建单例
const projectManager = new ProjectManager();

export default projectManager;
export { 
    projectManager, 
    getCurrentProject, 
    setCurrentProject,
    subscribeProject,
    getProjectList,
    getRecentProjects
};
