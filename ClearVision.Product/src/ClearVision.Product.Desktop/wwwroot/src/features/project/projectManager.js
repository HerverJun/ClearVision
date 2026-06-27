/**
 * 宸ョ▼绠＄悊妯″潡
 * 璐熻矗宸ョ▼鐨勫垱寤恒€佹墦寮€銆佷繚瀛樸€佸垪琛ㄧ鐞?
 */

import httpClient from '../../core/messaging/httpClient.js';
import { createSignal } from '../../core/state/store.js';

// 宸ョ▼鐘舵€?
const [getCurrentProject, setCurrentProject, subscribeProject] = createSignal(null);
const [getProjectList, setProjectList, subscribeProjectList] = createSignal([]);
const [getRecentProjects, setRecentProjects, subscribeRecentProjects] = createSignal([]);

class ProjectManager {
    constructor() {
        this.currentProject = null;
        this.unsavedChanges = false;
        this.openProjectRequestId = 0;
        this.savedGlobalVariablesSignature = '';
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

        const shouldSave = window.confirm('褰撳墠宸ョ▼鏈夋湭淇濆瓨鐨勬洿鏀癸紝鏄惁鍏堜繚瀛橈紵');
        if (shouldSave) {
            await this.saveProject();
        }

        return true;
    }

    /**
     * 鑾峰彇宸ョ▼鍒楄〃
     */
    async getProjectList() {
        try {
            const projects = await httpClient.get('/projects');
            setProjectList(projects);
            return projects;
        } catch (error) {
            console.error('[ProjectManager] 鑾峰彇宸ョ▼鍒楄〃澶辫触:', error);
            throw error;
        }
    }

    /**
     * 鑾峰彇鏈€杩戞墦寮€鐨勫伐绋?
     */
    async getRecentProjects(count = 10) {
        try {
            const projects = await httpClient.get(`/projects/recent?count=${count}`);
            setRecentProjects(projects);
            return projects;
        } catch (error) {
            console.error('[ProjectManager] 鑾峰彇鏈€杩戝伐绋嬪け璐?', error);
            throw error;
        }
    }

    /**
     * 鎼滅储宸ョ▼
     */
    async searchProjects(keyword) {
        try {
            const projects = await httpClient.get(`/projects/search?keyword=${encodeURIComponent(keyword)}`);
            return projects;
        } catch (error) {
            console.error('[ProjectManager] 鎼滅储宸ョ▼澶辫触:', error);
            throw error;
        }
    }

    /**
     * 鍒涘缓鏂板伐绋?
     */
    async createProject(name, description = '') {
        try {
            this.invalidateOpenProjectRequests();
            await this.prepareForProjectSwitch();
            const project = await httpClient.post('/projects', {
                name,
                description
            });
            
            this.currentProject = project;
            this.rememberGlobalVariableBaseline(project);
            setCurrentProject(project);
            this.unsavedChanges = false;
            this.updateStatusBar(project);
            this.rememberProjectInCaches(project);
            
            console.log('[ProjectManager] 宸ョ▼鍒涘缓鎴愬姛:', project.id);
            return project;
        } catch (error) {
            console.error('[ProjectManager] 鍒涘缓宸ョ▼澶辫触:', error);
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

            console.log('[ProjectManager] 绀轰緥宸ョ▼鍒涘缓鎴愬姛:', project.id, '| mode:', mode);
            return project;
        } catch (error) {
            console.error('[ProjectManager] 鍒涘缓绀轰緥宸ョ▼澶辫触:', error);
            throw error;
        }
    }

    async getDemoGuide() {
        try {
            return await httpClient.get('/demo/guide');
        } catch (error) {
            console.error('[ProjectManager] 鑾峰彇绀轰緥宸ョ▼寮曞澶辫触:', error);
            throw error;
        }
    }

    /**
     * 鎵撳紑宸ョ▼
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
                console.warn('[ProjectManager] 蹇界暐杩囨湡鐨勫伐绋嬫墦寮€缁撴灉:', projectId);
                return null;
            }
            
            this.currentProject = project;
            this.rememberGlobalVariableBaseline(project);
            setCurrentProject(project);
            this.unsavedChanges = false;
            
            // 鏇存柊鐘舵€佹爮
            this.updateStatusBar(project);
            
            console.log('[ProjectManager] 宸ョ▼鎵撳紑鎴愬姛:', project.id);
            return project;
        } catch (error) {
            console.error('[ProjectManager] 鎵撳紑宸ョ▼澶辫触:', error);
            throw error;
        }
    }

    /**
     * 淇濆瓨宸ョ▼
     */
    async saveProject(projectData = null) {
        if (!this.currentProject) {
            throw new Error('No project is open.');
        }

        const targetProjectId = this.currentProject.id;
        const data = projectData || this.currentProject;
        const flow = data.flow || data.Flow || this.currentProject.flow || this.currentProject.Flow || null;
        const globalVariables = data.globalVariables || data.GlobalVariables || this.currentProject.globalVariables || this.currentProject.GlobalVariables;
        const globalVariablesChanged = this.haveGlobalVariablesChanged(globalVariables);

        try {
            const updatePayload = {
                name: data.name,
                description: data.description
            };
            if (globalVariablesChanged) {
                updatePayload.globalVariables = globalVariables;
            }

            const saved = await httpClient.put(`/projects/${targetProjectId}`, updatePayload);
            if (flow) {
                await httpClient.put(`/projects/${targetProjectId}/flow`, toUpdateFlowRequest(flow));
            }

            if (!this.currentProject || this.currentProject.id !== targetProjectId) {
                return true;
            }

            const savedProject = isProjectPayload(saved) ? saved : {};
            Object.assign(this.currentProject, savedProject);
            this.currentProject.name = savedProject.name ?? savedProject.Name ?? data.name ?? this.currentProject.name;
            this.currentProject.description = savedProject.description ?? savedProject.Description ?? data.description ?? this.currentProject.description;
            this.currentProject.flow = savedProject.flow || savedProject.Flow || flow || this.currentProject.flow;
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

            console.log('[ProjectManager] 宸ョ▼淇濆瓨鎴愬姛:', targetProjectId);
            return true;
        } catch (error) {
            console.error('[ProjectManager] 淇濆瓨宸ョ▼澶辫触:', error);
            throw error;
        }
    }
    /**
     * 鍒犻櫎宸ョ▼
     */
    async deleteProject(projectId) {
        try {
            if (this.currentProject?.id === projectId) {
                this.invalidateOpenProjectRequests();
            }
            await httpClient.delete(`/projects/${projectId}`);
            
            // 濡傛灉鍒犻櫎鐨勬槸褰撳墠宸ョ▼锛屾竻绌哄綋鍓嶅伐绋?
            if (this.currentProject && this.currentProject.id === projectId) {
                await this.closeProject({ promptToSave: false });
            }
            this.forgetProjectFromCaches(projectId);
            
            console.log('[ProjectManager] 宸ョ▼鍒犻櫎鎴愬姛:', projectId);
            return true;
        } catch (error) {
            console.error('[ProjectManager] 鍒犻櫎宸ョ▼澶辫触:', error);
            throw error;
        }
    }

    /**
     * 鍏抽棴褰撳墠宸ョ▼
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
     * 鏇存柊褰撳墠宸ョ▼鏁版嵁
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
     * 鏇存柊娴佺▼
     */
    updateFlow(flowData) {
        if (!this.currentProject) return;

        this.currentProject.flow = flowData;
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

    async saveGlobalVariables(globalVariables = null) {
        if (!this.currentProject) {
            throw new Error('No project is open.');
        }

        const schema = globalVariables || this.currentProject.globalVariables || {
            schemaVersion: '1.0',
            variables: [],
            sourceBindings: [],
            targetBindings: []
        };
        const savedProject = await httpClient.put(`/projects/${this.currentProject.id}`, {
            name: this.currentProject.name,
            description: this.currentProject.description,
            flow: this.currentProject.flow || this.currentProject.Flow || null,
            globalVariables: schema
        });
        const saved = savedProject.globalVariables || savedProject.GlobalVariables || schema;
        this.currentProject = {
            ...this.currentProject,
            ...savedProject,
            flow: savedProject.flow || this.currentProject.flow,
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
     * 妫€鏌ユ槸鍚︽湁鏈繚瀛樼殑鏇存敼
     */
    hasUnsavedChanges() {
        return this.unsavedChanges;
    }

    /**
     * 鑾峰彇褰撳墠宸ョ▼
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
     * 鏇存柊鐘舵€佹爮
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
     * 鏇存柊绐楀彛鏍囬
     */
    updateTitle() {
        const unsavedMark = this.unsavedChanges ? ' *' : '';
        const projectName = this.currentProject ? this.currentProject.name : 'Untitled';
        document.title = `${projectName}${unsavedMark} - ClearVision`;
    }

    /**
     * 瀵煎嚭宸ョ▼
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
                    throw new Error(`涓嶆敮鎸佺殑瀵煎嚭鏍煎紡: ${format}`);
            }

            // 涓嬭浇鏂囦欢
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
            console.error('[ProjectManager] 瀵煎嚭宸ョ▼澶辫触:', error);
            throw error;
        }
    }

    /**
     * 瀵煎叆宸ョ▼
     */
    async importProject(file) {
        try {
            const content = await file.text();
            const projectData = JSON.parse(content);
            
            // 鍒涘缓鏂板伐绋?
            const project = await this.createProject(
                projectData.name || 'Imported Project',
                projectData.description || ''
            );

            // 瀵煎叆娴佺▼鏁版嵁
            if (projectData.flow) {
                await this.updateFlow(projectData.flow);
                await this.saveProject();
            }

            return project;
        } catch (error) {
            console.error('[ProjectManager] 瀵煎叆宸ョ▼澶辫触:', error);
            throw error;
        }
    }
}

function toUpdateFlowRequest(flow) {
    return {
        operators: Array.isArray(flow?.operators) ? flow.operators : (flow?.Operators || []),
        connections: Array.isArray(flow?.connections) ? flow.connections : (flow?.Connections || [])
    };
}

function getGlobalVariablesSignature(globalVariables) {
    if (!globalVariables) {
        return '';
    }

    return JSON.stringify(globalVariables);
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

// 鍒涘缓鍗曚緥
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
