function bindButton(documentRef, id, handler, cleanup) {
    const button = documentRef.getElementById(id);
    if (!button || button.dataset.cvCommandBound) {
        return;
    }

    button.dataset.cvCommandBound = 'true';
    const listener = (event) => {
        event.preventDefault();
        void Promise.resolve(handler(event)).catch(error => {
            console.error(`[CommandHandlers] ${id} failed`, error);
        });
    };

    button.addEventListener('click', listener);
    cleanup.push(() => {
        button.removeEventListener('click', listener);
        delete button.dataset.cvCommandBound;
    });
}

function validateFlowBeforeAction(propertyPanel, flowCanvas, action) {
    if (propertyPanel?.validateFlowForAction?.(flowCanvas, { action, showToast: true }) === false) {
        return false;
    }

    if (propertyPanel?.currentOperator && propertyPanel.applyChanges?.({ showToast: false }) === false) {
        return false;
    }

    return true;
}

function syncDraftBeforeSave(propertyPanel) {
    if (!propertyPanel?.currentOperator) {
        return true;
    }

    if (typeof propertyPanel.syncDraftChanges === 'function') {
        return propertyPanel.syncDraftChanges({ showToast: false }) !== false;
    }

    if (typeof propertyPanel.applyChanges === 'function') {
        return propertyPanel.applyChanges({ showToast: false }) !== false;
    }

    return true;
}

function tryOpenFinalDecisionFromError(error) {
    const payload = error?.payload || {};
    const code = payload.code || payload.Code || '';
    const action = payload.action || payload.Action || '';
    if (action !== 'ConfigureFinalDecision' && !String(code).includes('DECISION')) {
        return false;
    }

    window.dispatchEvent(new CustomEvent('clearvision:open-final-decision', {
        detail: {
            code,
            violations: payload.violations || payload.Violations || [],
            message: payload.error || payload.Error || error?.message || ''
        }
    }));
    return true;
}

/**
 * @typedef {Object} ToolbarCommandOptions
 * @property {Document} documentRef
 * @property {{ get?: Function }} serviceRegistry
 * @property {() => any} getPropertyPanel
 * @property {() => any} getCurrentProject
 * @property {() => any} getFlowCanvas
 * @property {() => any} getImageViewer
 * @property {{ saveProject?: Function, updateFlow?: Function, getCurrentProject?: Function }} projectManager
 * @property {{ setProject?: Function, executeSingle?: Function }} inspectionController
 * @property {(message: string, type?: string) => void} showToast
 * @property {(options?: any) => void} handleNewProject
 * @property {() => boolean} [canEditProject]
 * @property {(view: string) => void} setCurrentView
 * @property {(view: string) => void} syncActiveNavButton
 * @property {(view: string) => Promise<void>} switchView
 * @property {() => Promise<any>} ensureInspectionPanelReady
 * @property {() => void} initializeInspectionImageViewer
 * @property {() => Promise<void>} logout
 */

/**
 * Binds toolbar buttons to command workflows.
 *
 * @param {ToolbarCommandOptions} options
 * @returns {() => void}
 */
export function bindToolbarCommands(options) {
    const {
        documentRef = document,
        serviceRegistry,
        getPropertyPanel,
        getCurrentProject,
        getFlowCanvas,
        getImageViewer,
        projectManager,
        inspectionController,
        showToast,
        handleNewProject,
        canEditProject = () => true,
        setCurrentView,
        syncActiveNavButton,
        switchView,
        ensureInspectionPanelReady,
        initializeInspectionImageViewer,
        logout
    } = options;

    const cleanup = [];

    bindButton(documentRef, 'btn-save', async () => {
        try {
            if (!canEditProject()) {
                showToast('保存工程需要工程师或管理员权限。', 'warning');
                return;
            }

            const propertyPanel = getPropertyPanel?.() || serviceRegistry?.get?.('propertyPanel');
            const flowCanvas = getFlowCanvas();
            if (!syncDraftBeforeSave(propertyPanel)) {
                return;
            }

            const project = getCurrentProject();
            if (project) {
                if (flowCanvas) {
                    const flow = flowCanvas.serialize();
                    if (typeof projectManager?.updateFlow === 'function') {
                        projectManager.updateFlow(flow);
                    } else {
                        project.flow = flow;
                    }
                }

                await projectManager.saveProject(projectManager?.getCurrentProject?.() || project);
                const storage = typeof localStorage !== 'undefined' ? localStorage : null;
                if (storage) {
                    try {
                        const rawBackup = storage.getItem('cv_autosave_backup');
                        const backup = rawBackup ? JSON.parse(rawBackup) : null;
                        if (!backup?.projectId || backup.projectId === project.id) {
                            storage.removeItem('cv_autosave_backup');
                        }
                    } catch {
                        try {
                            storage.removeItem('cv_autosave_backup');
                        } catch {
                            // Saving already succeeded; ignore unavailable backup storage.
                        }
                    }
                }

                showToast(`工程已保存到服务端工程库（版本 v${project.version || '1.0.0'}）`, 'success');
                return;
            }

            if (flowCanvas?.nodes?.size > 0) {
                handleNewProject({ preserveCanvas: true });
                return;
            }

            showToast('请先创建或打开工程', 'warning');
        } catch (error) {
            console.error('[CommandHandlers] save failed', error);
            showToast(`保存失败: ${error?.message || error}`, 'error');
        }
    }, cleanup);

    bindButton(documentRef, 'btn-run', async () => {
        try {
            const project = getCurrentProject();
            if (!project) {
                showToast('请先创建或打开工程', 'warning');
                return;
            }

            const flowCanvas = getFlowCanvas();
            if (!flowCanvas || flowCanvas.nodes?.size === 0) {
                showToast('请先在流程中添加至少一个算子', 'warning');
                return;
            }

            const propertyPanel = getPropertyPanel?.() || serviceRegistry?.get?.('propertyPanel');
            if (!validateFlowBeforeAction(propertyPanel, flowCanvas, '运行')) {
                return;
            }

            setCurrentView('inspection');
            syncActiveNavButton('inspection');
            await switchView('inspection');
            inspectionController.setProject(project.id);

            const panel = await ensureInspectionPanelReady();
            initializeInspectionImageViewer();
            panel?.updateStatus?.('running', '运行中...');
            panel?.setButtonsState?.(true);

            const testImage = getImageViewer()?.currentTestImage;
            if (testImage) {
                await inspectionController.executeSingle(testImage);
                return;
            }

            await inspectionController.executeSingle();
        } catch (error) {
            console.error('[CommandHandlers] run failed', error);
            if (tryOpenFinalDecisionFromError(error)) {
                showToast('正式运行被最终判定配置阻断，请按定位信息修复', 'warning');
                return;
            }
            showToast(`检测失败: ${error?.message || error}`, 'error');
        }
    }, cleanup);

    bindButton(documentRef, 'btn-logout', async () => {
        await logout();
    }, cleanup);

    return () => cleanup.splice(0).forEach(dispose => dispose());
}
