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

/**
 * @typedef {Object} ToolbarCommandOptions
 * @property {Document} documentRef
 * @property {{ get?: Function }} serviceRegistry
 * @property {() => any} getPropertyPanel
 * @property {() => any} getCurrentProject
 * @property {() => any} getFlowCanvas
 * @property {() => any} getImageViewer
 * @property {{ saveProject?: Function }} projectManager
 * @property {{ setProject?: Function, executeSingle?: Function }} inspectionController
 * @property {(message: string, type?: string) => void} showToast
 * @property {(options?: any) => void} handleNewProject
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
            const propertyPanel = getPropertyPanel?.() || serviceRegistry?.get?.('propertyPanel');
            if (propertyPanel?.currentOperator) {
                propertyPanel.applyChanges();
            }

            const project = getCurrentProject();
            const flowCanvas = getFlowCanvas();
            if (project) {
                if (flowCanvas) {
                    project.flow = flowCanvas.serialize();
                }

                await projectManager.saveProject(project);
                showToast('Project saved', 'success');
                return;
            }

            if (flowCanvas?.nodes?.size > 0) {
                handleNewProject({ preserveCanvas: true });
                return;
            }

            showToast('Create or open a project first', 'warning');
        } catch (error) {
            console.error('[CommandHandlers] save failed', error);
            showToast(`Save failed: ${error?.message || error}`, 'error');
        }
    }, cleanup);

    bindButton(documentRef, 'btn-run', async () => {
        try {
            const project = getCurrentProject();
            if (!project) {
                showToast('Create or open a project first', 'warning');
                return;
            }

            const flowCanvas = getFlowCanvas();
            if (!flowCanvas || flowCanvas.nodes?.size === 0) {
                showToast('Add at least one operator to the flow', 'warning');
                return;
            }

            setCurrentView('inspection');
            syncActiveNavButton('inspection');
            await switchView('inspection');
            inspectionController.setProject(project.id);

            const panel = await ensureInspectionPanelReady();
            initializeInspectionImageViewer();
            panel?.updateStatus?.('running', 'Running...');
            panel?.setButtonsState?.(true);

            const testImage = getImageViewer()?.currentTestImage;
            if (testImage) {
                await inspectionController.executeSingle(testImage);
                return;
            }

            await inspectionController.executeSingle();
        } catch (error) {
            console.error('[CommandHandlers] run failed', error);
            showToast(`Inspection failed: ${error?.message || error}`, 'error');
        }
    }, cleanup);

    bindButton(documentRef, 'btn-logout', async () => {
        await logout();
    }, cleanup);

    return () => cleanup.splice(0).forEach(dispose => dispose());
}
