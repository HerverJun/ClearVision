import serviceRegistry from './serviceRegistry.js';

const LEGACY_SERVICE_GLOBALS = {
    flowCanvas: 'flowCanvas',
    flowCanvasAdapter: 'flowCanvasAdapter',
    flowEditorInteraction: 'flowEditorInteraction',
    imageViewer: 'imageViewer',
    inspectionImageViewer: 'inspectionImageViewer',
    inspectionPanel: 'inspectionPanel',
    resultPanel: 'resultPanel',
    propertyPanel: 'propertyPanel',
    operatorLibraryPanel: 'operatorLibraryPanel',
    aiPanel: 'aiPanel',
    nodePreviewCoordinator: 'nodePreviewCoordinator',
    nodePreviewOverlay: 'nodePreviewOverlay',
    nodePreviewInspector: 'nodePreviewInspector',
    nodePreviewSelectionStore: 'nodePreviewSelectionStore',
    cvSettingsView: 'settingsView'
};

let installed = false;

function installLegacyGlobalAccessors(targetWindow = window) {
    if (installed || !targetWindow) {
        return;
    }

    Object.entries(LEGACY_SERVICE_GLOBALS).forEach(([globalName, serviceKey]) => {
        const descriptor = Object.getOwnPropertyDescriptor(targetWindow, globalName);
        if (descriptor && descriptor.configurable === false) {
            return;
        }

        Object.defineProperty(targetWindow, globalName, {
            configurable: true,
            enumerable: false,
            get() {
                return serviceRegistry.get(serviceKey);
            },
            set(value) {
                if (value === null || value === undefined) {
                    serviceRegistry.unregister(serviceKey);
                    return;
                }

                serviceRegistry.register(serviceKey, value);
            }
        });
    });

    installed = true;
}

function exposeLegacyGlobal(globalName, serviceKey, targetWindow = window) {
    if (!targetWindow || !globalName || !serviceKey) {
        return;
    }

    Object.defineProperty(targetWindow, globalName, {
        configurable: true,
        enumerable: false,
        get() {
            return serviceRegistry.get(serviceKey);
        },
        set(value) {
            if (value === null || value === undefined) {
                serviceRegistry.unregister(serviceKey);
                return;
            }

            serviceRegistry.register(serviceKey, value);
        }
    });
}

export {
    LEGACY_SERVICE_GLOBALS,
    exposeLegacyGlobal,
    installLegacyGlobalAccessors
};
