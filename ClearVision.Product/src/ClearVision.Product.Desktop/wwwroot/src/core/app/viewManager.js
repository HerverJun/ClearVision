const VIEW_CONTAINER_IDS = {
    flow: 'flow-editor',
    image: 'image-viewer',
    inspection: 'inspection-view',
    results: 'results-view',
    stations: 'stations-view',
    project: 'project-view',
    ai: 'ai-view',
    settings: 'settings-view'
};

function scheduleFrame(callback) {
    if (typeof requestAnimationFrame === 'function') {
        requestAnimationFrame(callback);
        return;
    }

    setTimeout(callback, 0);
}

function getViewContainers(documentRef) {
    return Object.fromEntries(
        Object.entries(VIEW_CONTAINER_IDS).map(([view, id]) => [view, documentRef.getElementById(id)])
    );
}

function hideAllViews(containers) {
    Object.values(containers).forEach(container => container?.classList.add('hidden'));
}

function getInlineResultImageBase64(result) {
    if (!result || typeof result !== 'object') {
        return null;
    }

    return result.imageData
        || result.ImageData
        || result.outputImage
        || result.OutputImage
        || result.outputImageBase64
        || result.OutputImageBase64
        || result.resultImageBase64
        || result.ResultImageBase64
        || null;
}

/**
 * @typedef {Object} ViewManagerOptions
 * @property {Document} documentRef
 * @property {{ emit?: Function }} eventBus
 * @property {{ get?: Function }} serviceRegistry
 * @property {(view: string) => void} setCurrentView
 * @property {(featureName: string, error: unknown) => void} onFeatureLoadError
 * @property {() => any} getFlowCanvas
 * @property {() => any} getPropertySidebarController
 * @property {() => Promise<any>} ensureInspectionPanelReady
 * @property {() => void} initializeInspectionImageViewer
 * @property {(viewer: any) => void} restoreInspectionImageViewer
 * @property {() => Promise<any>} ensureResultPanel
 * @property {() => Promise<void>} loadInspectionHistory
 * @property {() => Promise<any>} ensureStationMonitorView
 * @property {() => Promise<any>} ensureProjectView
 * @property {() => Promise<any>} ensureAiPanel
 * @property {() => Promise<any>} ensureSettingsView
 * @property {() => any} getSettingsView
 */

/**
 * Keeps routing and view visibility out of the app bootstrap.
 *
 * @param {ViewManagerOptions} options
 */
export function createViewManager(options) {
    const {
        documentRef = document,
        eventBus,
        serviceRegistry,
        setCurrentView,
        onFeatureLoadError,
        getFlowCanvas,
        getPropertySidebarController,
        ensureInspectionPanelReady,
        initializeInspectionImageViewer,
        restoreInspectionImageViewer,
        ensureResultPanel,
        loadInspectionHistory,
        ensureStationMonitorView,
        ensureProjectView,
        ensureAiPanel,
        ensureSettingsView,
        getSettingsView
    } = options;

    function syncActiveNavButton(view) {
        documentRef.querySelectorAll('.nav-btn').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.view === view);
        });
    }

    function bindNavigation() {
        documentRef.querySelectorAll('.nav-btn').forEach(btn => {
            if (btn.dataset.cvViewBound) {
                return;
            }

            btn.dataset.cvViewBound = 'true';
            btn.addEventListener('click', () => {
                const view = btn.dataset.view;
                setCurrentView(view);
                syncActiveNavButton(view);
                void switchView(view).catch(error => onFeatureLoadError?.('view switch', error));
            });
        });
    }

    async function switchView(view) {
        console.log(`[ViewManager] switch view: ${view}`);
        eventBus?.emit?.('view:changed', { view });

        const containers = getViewContainers(documentRef);
        if (view !== 'settings') {
            getSettingsView?.()?.deactivate?.();
        }
        hideAllViews(containers);

        const leftSidebar = documentRef.querySelector('.sidebar.left');
        const rightSidebar = documentRef.querySelector('.sidebar.right');
        const operatorRail = documentRef.querySelector('.operator-rail');
        const operatorFlyout = documentRef.querySelector('.operator-group-flyout');
        const mainContent = documentRef.getElementById('main-content');
        const sidebarResizer = documentRef.querySelector('[data-sidebar-resizer="property"]');
        const isFlowView = view === 'flow';
        mainContent?.classList.toggle('flow-editor-shell', isFlowView);
        leftSidebar?.classList.toggle('hidden', !isFlowView);
        rightSidebar?.classList.toggle('hidden', !isFlowView);
        operatorRail?.classList.toggle('hidden', !isFlowView);
        sidebarResizer?.classList.toggle('hidden', !isFlowView);
        if (!isFlowView) {
            operatorFlyout?.classList.add('hidden');
            operatorFlyout?.setAttribute('aria-hidden', 'true');
            sidebarResizer?.setAttribute('aria-hidden', 'true');
        } else {
            sidebarResizer?.removeAttribute('aria-hidden');
        }

        getPropertySidebarController()?.sync?.(view);

        switch (view) {
            case 'flow':
                containers.flow?.classList.remove('hidden');
                scheduleFrame(() => getFlowCanvas()?.resize?.());
                break;
            case 'image':
                containers.image?.classList.remove('hidden');
                scheduleFrame(() => {
                    const imageViewer = serviceRegistry?.get?.('imageViewer');
                    imageViewer?.imageCanvas?.resize?.();
                    if (typeof restoreInspectionImageViewer === 'function') {
                        restoreInspectionImageViewer(imageViewer);
                    } else {
                        const inspectionImage = serviceRegistry?.get?.('lastInspectionImageBlob');
                        const inspectionImageBase64 = serviceRegistry?.get?.('lastInspectionImageBase64');
                        const source = inspectionImage || (inspectionImageBase64
                            ? `data:image/png;base64,${inspectionImageBase64}`
                            : null);
                        if (source && imageViewer) {
                            imageViewer.loadImage(source, { silent: true });
                        }
                    }
                });
                break;
            case 'inspection': {
                containers.inspection?.classList.remove('hidden');
                const panel = await ensureInspectionPanelReady();
                initializeInspectionImageViewer();
                scheduleFrame(() => {
                    const inspectionImageViewer = serviceRegistry?.get?.('inspectionImageViewer');
                    inspectionImageViewer?.imageCanvas?.resize?.();

                    const lastInspectionResult = serviceRegistry?.get?.('lastInspectionResult');
                    const lastInspectionImageBase64 = serviceRegistry?.get?.('lastInspectionImageBase64')
                        || getInlineResultImageBase64(lastInspectionResult);
                    const lastInspectionImage = serviceRegistry?.get?.('lastInspectionImageBlob')
                        || (lastInspectionImageBase64
                            ? `data:image/png;base64,${lastInspectionImageBase64}`
                            : null);
                    if (lastInspectionImage && inspectionImageViewer) {
                        inspectionImageViewer.loadImage(lastInspectionImage, { silent: true });
                    }

                    panel?.refresh?.();
                });
                break;
            }
            case 'results': {
                containers.results?.classList.remove('hidden');
                const panel = await ensureResultPanel();
                await loadInspectionHistory?.();
                panel?.render?.();
                break;
            }
            case 'stations': {
                containers.stations?.classList.remove('hidden');
                const monitorView = await ensureStationMonitorView();
                await monitorView?.activate?.();
                break;
            }
            case 'project': {
                containers.project?.classList.remove('hidden');
                const viewInstance = await ensureProjectView();
                viewInstance?.refresh?.();
                break;
            }
            case 'ai': {
                containers.ai?.classList.remove('hidden');
                const panel = await ensureAiPanel();
                panel?.activate?.();
                break;
            }
            case 'settings':
                containers.settings?.classList.remove('hidden');
                {
                    const viewInstance = await ensureSettingsView?.();
                    await viewInstance?.refresh?.();
                }
                break;
            default:
                containers.flow?.classList.remove('hidden');
                break;
        }
    }

    return {
        bindNavigation,
        switchView,
        syncActiveNavButton
    };
}
