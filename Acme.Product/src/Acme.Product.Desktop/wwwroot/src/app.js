/**
 * 涓诲簲鐢ㄥ叆鍙?- S4-006: 绔埌绔泦鎴?
 * Sprint 4: 鍓嶅悗绔泦鎴愪笌鐢ㄦ埛浣撻獙闂幆
 */

import { Dialog } from './shared/components/dialog.js';
import { buildOperatorNodeConfig } from './shared/operatorVisuals.js';
import { createOperatorIconElement } from './shared/operatorIconRenderer.js';
import eventBus from './core/app/eventBus.js';
import serviceRegistry from './core/app/serviceRegistry.js';
import { installLegacyGlobalAccessors } from './core/app/legacyGlobals.js';
import { createViewManager } from './core/app/viewManager.js';
import { bindToolbarCommands } from './core/app/commandHandlers.js';
import { createFlowCanvasAdapter } from './core/canvas/flowCanvasAdapter.js';
import { createAiGenerationController } from './features/ai/aiGenerationController.js';
import debugLogger, { installConsoleGate } from './core/logging/debugLogger.js';
import { t } from './core/i18n/resources.js';

installConsoleGate();

// ============================================
// 全局错误捕获 - 用于调试
// ============================================
window._errorLogs = [];

const MAX_ERROR_LOGS = 100;
function addErrorLog(logEntry) {
    window._errorLogs.push(logEntry);
    if (window._errorLogs.length > MAX_ERROR_LOGS) {
        window._errorLogs.shift();
    }
}

window.onerror = function(message, source, lineno, colno, error) {
    const errorInfo = `[Global Error] ${message} at ${source}:${lineno}`;
    console.error(errorInfo);
    addErrorLog({
        type: 'Error',
        message,
        source,
        line: lineno,
        column: colno,
        time: new Date().toLocaleTimeString()
    });
    return false;
};

window.addEventListener('unhandledrejection', function(event) {
    const errorMsg = event.reason?.message || event.reason;
    console.error('[Unhandled Promise Rejection]', errorMsg);
    addErrorLog({
        type: 'Promise',
        message: errorMsg,
        time: new Date().toLocaleTimeString()
    });
});

debugLogger.debug(`[App] ${t('app.startingImports', 'Starting module imports')}...`);

// ============================================
// 璁よ瘉妫€鏌?- 鏈櫥褰曞垯璺宠浆
// ============================================
import { bootstrapAuthSession, logout } from './features/auth/auth.js';

import httpClient from './core/messaging/httpClient.js';
import { createSignal } from './core/state/store.js';
import FlowCanvas from './core/canvas/flowCanvas.js';
import { FlowEditorInteraction } from './features/flow-editor/flowEditorInteraction.js';
import { ImageViewerComponent } from './features/image-viewer/imageViewer.js';
import { OperatorLibraryPanel } from './features/operator-library/operatorLibrary.js';
import inspectionController from './features/inspection/inspectionController.js';
import { showToast, createModal, closeModal, createInput, createLabeledInput, createButton } from './shared/components/uiComponents.js';
import {
    applyTheme,
    bindThemeToggle,
    bootstrapTheme,
    syncThemeWithSettings
} from './core/theme/theme.js';
import { PropertyPanel } from './features/flow-editor/propertyPanel.js';
import PropertySidebarController from './features/flow-editor/propertySidebarController.mjs';
import { NodePreviewCoordinator, resolvePreviewInputImageBase64 } from './features/flow-editor/previewCoordinator.js';
import NodePreviewOverlay from './features/flow-editor/nodePreviewOverlay.js';
import projectManager, {
    getCurrentProject,
    subscribeProject
} from './features/project/projectManager.js';

// 鍏ㄥ眬鐘舵€?
const [getCurrentView, setCurrentView, subscribeView] = createSignal('flow');
const [getSelectedOperator, setSelectedOperator, subscribeSelectedOperator] = createSignal(null);
const [getOperatorLibrary, setOperatorLibrary, subscribeOperatorLibrary] = createSignal([]);

installLegacyGlobalAccessors();

// 订阅管理器，防止内存泄漏
const subscriptions = [];
function trackedSubscribe(subscribeFn, callback) {
    const unsubscribe = subscribeFn(callback);
    subscriptions.push(unsubscribe);
    return unsubscribe;
}

// 缁勪欢瀹炰緥
let imageViewer = null;
let operatorLibraryPanel = null;
let flowCanvas = null;
let flowEditorInteraction = null;
let propertyPanel = null;
let propertySidebarController = null;
let nodePreviewCoordinator = null;
let nodePreviewOverlay = null;
let projectView = null;
let resultPanel = null;
let inspectionPanel = null;
let stationMonitorView = null;
let aiPanel = null;
let viewManager = null;
let toolbarCommandDisposer = null;
let aiGenerationController = null;
let appInitialized = false;
let appBootstrapPromise = null;
let statusBarStarted = false;
let themeUpdateInFlight = false;
let projectFlowSyncSuppressionDepth = 0;
let studioPerformanceGuardsInitialized = false;

let projectViewModulePromise = null;
let resultPanelModulePromise = null;
let inspectionPanelModulePromise = null;
let stationMonitorModulePromise = null;
let resultPanelAnalyticsRefreshTimer = null;
let resultPanelAnalyticsRefreshProjectId = null;
const RESULT_PANEL_ANALYTICS_REFRESH_DELAY_MS = 5000;

function scheduleResultPanelAnalyticsRefresh(panel, projectId, isRealtimeResult) {
    if (!panel || !projectId || typeof panel.loadServerAnalytics !== 'function') {
        return;
    }

    if (isRealtimeResult) {
        return;
    }

    resultPanelAnalyticsRefreshProjectId = projectId;
    if (resultPanelAnalyticsRefreshTimer !== null) {
        clearTimeout(resultPanelAnalyticsRefreshTimer);
    }

    resultPanelAnalyticsRefreshTimer = setTimeout(() => {
        resultPanelAnalyticsRefreshTimer = null;
        panel.loadServerAnalytics(resultPanelAnalyticsRefreshProjectId).catch(error => {
            debugLogger.warn('[App] 刷新结果页服务端分析失败:', error);
        });
    }, RESULT_PANEL_ANALYTICS_REFRESH_DELAY_MS);
}

function isResultPanelVisible() {
    const container = document.getElementById('results-list-container');
    if (!container) {
        return false;
    }

    return !container.closest('.hidden');
}

function isInspectionActiveForBackgroundWork() {
    const state = inspectionController.getState?.();
    return state?.isRealtime === true || state?.isRunning === true || state?.status === 'running';
}
let aiPanelModulePromise = null;

// 鏈満鑽夌澶囦唤瀹氭椂鍣?
let autoSaveInterval = null;
const AUTO_SAVE_DELAY = 5 * 60 * 1000;
const LOCAL_DRAFT_BACKUP_KEY = 'cv_autosave_backup';
const promptedLocalDraftKeys = new Set();
let lastLocalDraftBackupSignature = null;

function getFlowNodeCount(flow) {
    const nodes = flow?.nodes || flow?.Nodes || [];
    if (Array.isArray(nodes)) {
        return nodes.length;
    }

    if (nodes && typeof nodes === 'object') {
        return Object.keys(nodes).length;
    }

    return 0;
}

function getLocalDraftBackupSignature(project, flow) {
    const projectId = project?.id || '';
    const modifiedAt = project?.modifiedAt || project?.ModifiedAt || '';
    const flowRevision = flow?.flowRevision ?? flow?.FlowRevision ?? '';
    return `${projectId}:${modifiedAt}:${flowRevision}`;
}

function readLocalDraftBackup() {
    try {
        const raw = localStorage.getItem(LOCAL_DRAFT_BACKUP_KEY);
        return raw ? JSON.parse(raw) : null;
    } catch (error) {
        debugLogger.warn('[LocalDraftBackup] 本机草稿读取失败:', error);
        return null;
    }
}

function saveLocalDraftBackup(project, flow, source = 'timer') {
    if (!project || !flow) {
        return null;
    }

    const backup = {
        projectId: project.id,
        projectName: project.name || '',
        timestamp: new Date().toISOString(),
        source,
        nodeCount: getFlowNodeCount(flow),
        flow
    };

    localStorage.setItem(LOCAL_DRAFT_BACKUP_KEY, JSON.stringify(backup));
    lastLocalDraftBackupSignature = getLocalDraftBackupSignature(project, flow);
    return backup;
}

function clearLocalDraftBackup(projectId = null) {
    const backup = readLocalDraftBackup();
    if (!backup) {
        return;
    }

    if (!projectId || backup.projectId === projectId) {
        localStorage.removeItem(LOCAL_DRAFT_BACKUP_KEY);
    }
}

function loadProjectViewModule() {
    if (!projectViewModulePromise) {
        projectViewModulePromise = import('./features/project/projectView.js');
    }

    return projectViewModulePromise;
}

function loadResultPanelModule() {
    if (!resultPanelModulePromise) {
        resultPanelModulePromise = import('./features/results/resultPanel.js');
    }

    return resultPanelModulePromise;
}

function loadStationMonitorModule() {
    if (!stationMonitorModulePromise) {
        stationMonitorModulePromise = import('./features/stations/stationMonitorView.js');
    }

    return stationMonitorModulePromise;
}

function loadInspectionPanelModule() {
    if (!inspectionPanelModulePromise) {
        inspectionPanelModulePromise = import('./features/inspection/inspectionPanel.js');
    }

    return inspectionPanelModulePromise;
}

function loadAiPanelModule() {
    if (!aiPanelModulePromise) {
        aiPanelModulePromise = import('./features/ai/aiPanel.js');
    }

    return aiPanelModulePromise;
}

function updateAuthenticatedUserDisplay() {
    const userNameEl = document.getElementById('user-display-name');
    if (userNameEl && window.currentUser) {
        userNameEl.textContent = window.currentUser.displayName || window.currentUser.username || '--';
    }
}

function syncActiveNavButton(view) {
    getViewManager().syncActiveNavButton(view);
}

function getViewManager() {
    if (!viewManager) {
        viewManager = createViewManager({
            documentRef: document,
            eventBus,
            serviceRegistry,
            setCurrentView,
            onFeatureLoadError: handleFeatureLoadError,
            getFlowCanvas: () => serviceRegistry.get('flowCanvasAdapter') || flowCanvas,
            getPropertySidebarController: () => propertySidebarController,
            ensureInspectionPanelReady,
            initializeInspectionImageViewer,
            ensureResultPanel,
            loadInspectionHistory,
            ensureStationMonitorView,
            ensureProjectView,
            ensureAiPanel
        });
    }

    return viewManager;
}

function getAiGenerationController() {
    if (!aiGenerationController) {
        aiGenerationController = createAiGenerationController({
            eventBus,
            serviceRegistry,
            ensureAiPanel,
            switchView,
            setCurrentView,
            syncActiveNavButton
        });
        serviceRegistry.register('aiGenerationController', aiGenerationController);
    }

    return aiGenerationController;
}

function handleFeatureLoadError(featureName, error) {
    console.error(`[App] ${featureName} 初始化失败:`, error);
    showToast(`${featureName} 初始化失败，请刷新后重试`, 'error');
}

function withProjectFlowSyncSuppressed(action) {
    projectFlowSyncSuppressionDepth += 1;
    try {
        return action();
    } finally {
        projectFlowSyncSuppressionDepth = Math.max(0, projectFlowSyncSuppressionDepth - 1);
    }
}

function promptLocalDraftRestore(project) {
    if (!project || !flowCanvas) {
        return;
    }

    const backup = readLocalDraftBackup();
    const promptKey = `${backup?.projectId || ''}:${backup?.timestamp || ''}`;
    if (!backup || backup.projectId !== project.id || promptedLocalDraftKeys.has(promptKey)) {
        return;
    }

    const currentFlow = typeof flowCanvas.serialize === 'function' ? flowCanvas.serialize() : project.flow;
    if (JSON.stringify(currentFlow || null) === JSON.stringify(backup.flow || null)) {
        return;
    }

    promptedLocalDraftKeys.add(promptKey);

    const backupTime = backup.timestamp ? new Date(backup.timestamp).toLocaleString() : '未知时间';
    const currentNodeCount = getFlowNodeCount(currentFlow);
    const backupNodeCount = Number.isFinite(backup.nodeCount) ? backup.nodeCount : getFlowNodeCount(backup.flow);
    const shouldRestore = window.confirm([
        '检测到本机草稿备份。',
        '',
        `工程：${backup.projectName || project.name || project.id}`,
        `备份时间：${backupTime}`,
        `当前节点数：${currentNodeCount}`,
        `草稿节点数：${backupNodeCount}`,
        '',
        '本机草稿仅保存在当前电脑浏览器缓存中，不等同于正式工程保存。',
        '是否恢复这份草稿到当前流程画布？'
    ].join('\n'));

    if (!shouldRestore) {
        return;
    }

    withProjectFlowSyncSuppressed(() => {
        flowCanvas.deserialize(backup.flow);
    });
    projectManager.updateFlow(backup.flow);
    showToast('已恢复本机草稿；请点击“保存工程”写入正式工程库。', 'warning');
}

function initializeProjectFlowCanvasSync() {
    if (!flowCanvas || typeof flowCanvas.subscribeStructureState !== 'function') {
        return;
    }

    const unsubscribe = flowCanvas.subscribeStructureState((payload = {}) => {
        if (payload.reason === 'initial') {
            return;
        }

        syncCurrentProjectFlowFromCanvas();
    });
    subscriptions.push(unsubscribe);
}

function initializeStudioPerformanceGuards() {
    if (studioPerformanceGuardsInitialized) {
        return;
    }

    studioPerformanceGuardsInitialized = true;
    const unsubscribe = eventBus.on('view:changed', ({ view } = {}) => {
        if (view !== 'stations') {
            stationMonitorView?.deactivate?.();
        }

        if (view !== 'results') {
            resultPanel?.disconnectResultsStream?.();
        }
    });

    subscriptions.push(unsubscribe);
}

function tryParseJsonPayload(payload) {
    if (typeof payload !== 'string' || payload.trim().length === 0) {
        return null;
    }

    try {
        return JSON.parse(payload);
    } catch (error) {
        debugLogger.warn('[App] JSON payload 解析失败:', error);
        return null;
    }
}

function normalizeAnalysisData(result) {
    if (!result || typeof result !== 'object') {
        return null;
    }

    return result.analysisData
        || result.AnalysisData
        || tryParseJsonPayload(result.analysisDataJson)
        || tryParseJsonPayload(result.AnalysisDataJson)
        || null;
}

function normalizeOutputData(result) {
    if (!result || typeof result !== 'object') {
        return {};
    }

    return result.outputData
        || result.OutputData
        || tryParseJsonPayload(result.outputDataJson)
        || tryParseJsonPayload(result.OutputDataJson)
        || {};
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

function loadViewerImageSilently(viewer, imageData) {
    if (!viewer || !imageData) {
        return;
    }

    try {
        const maybePromise = viewer.loadImage?.(imageData, { silent: true });
        if (maybePromise && typeof maybePromise.catch === 'function') {
            maybePromise.catch(error => {
                debugLogger.warn('[App] 静默加载检测图像失败:', error);
            });
        }
    } catch (error) {
        debugLogger.warn('[App] 静默加载检测图像失败:', error);
    }
}

function buildResultDefects(result) {
    const actualDefects = result?.defects || result?.Defects;
    if (Array.isArray(actualDefects) && actualDefects.length > 0) {
        return actualDefects;
    }

    const defectCount = Number(
        result?.defectCount
        ?? result?.DefectCount
        ?? 0
    );
    if (!Number.isFinite(defectCount) || defectCount <= 0) {
        return [];
    }

    return Array.from({ length: defectCount }, (_, index) => ({
        type: `目标 ${index + 1}`,
        description: '实时结果未携带缺陷详情'
    }));
}

function normalizeInspectionStatus(status) {
    const normalized = String(status || '').trim().toUpperCase();
    if (normalized === 'OK') {
        return 'OK';
    }

    if (normalized === 'NG') {
        return 'NG';
    }

    if (normalized === 'ERROR') {
        return 'Error';
    }

    return status || 'Unknown';
}

function normalizeInspectionResultRecord(result, fallbackProjectId = null) {
    if (!result || typeof result !== 'object') {
        return null;
    }

    const normalized = { ...result };
    normalized.id = normalized.id ?? normalized.Id;
    normalized.projectId = normalized.projectId ?? normalized.ProjectId ?? fallbackProjectId ?? null;
    normalized.status = normalizeInspectionStatus(normalized.status ?? normalized.Status);
    normalized.defects = buildResultDefects(normalized);
    normalized.defectCount = normalized.defectCount
        ?? normalized.DefectCount
        ?? normalized.defects.length;
    normalized.processingTime = normalized.processingTime
        ?? normalized.processingTimeMs
        ?? normalized.ProcessingTimeMs
        ?? normalized.executionTimeMs
        ?? normalized.ExecutionTimeMs
        ?? null;
    normalized.processingTimeMs = normalized.processingTimeMs ?? normalized.processingTime;
    normalized.timestamp = normalized.timestamp
        ?? normalized.Timestamp
        ?? normalized.inspectionTime
        ?? normalized.InspectionTime
        ?? new Date().toISOString();
    normalized.confidenceScore = normalized.confidenceScore ?? normalized.ConfidenceScore;
    normalized.imageId = normalized.imageId || normalized.ImageId;
    normalized.imageData = getInlineResultImageBase64(normalized);
    normalized.outputImage = normalized.outputImage || normalized.OutputImage || null;
    normalized.outputImageBase64 = normalized.outputImageBase64 || normalized.OutputImageBase64 || null;
    normalized.resultImageBase64 = normalized.resultImageBase64 || normalized.ResultImageBase64 || null;
    normalized.outputData = normalizeOutputData(normalized);
    normalized.analysisData = normalizeAnalysisData(normalized);
    normalized.errorMessage = normalized.errorMessage ?? normalized.ErrorMessage ?? '';

    return normalized;
}

function updateInspectionResultsPanel(result) {
    return result;
}

function initializeOperatorLibraryPanel() {
    const container = document.getElementById('operator-library');
    if (!container) {
        console.error('[App] 找不到算子库容器');
        return;
    }

    operatorLibraryPanel = new OperatorLibraryPanel('operator-library');
    serviceRegistry.register('operatorLibraryPanel', operatorLibraryPanel);

    operatorLibraryPanel.onOperatorDragStart = (operatorData) => {
        debugLogger.debug('[App] 开始拖拽算子:', operatorData.type);
    };

    operatorLibraryPanel.onOperatorSelected = (operatorData) => {
        debugLogger.debug('[App] 选中算子:', operatorData.type);
        const operatorCopy = {
            ...operatorData,
            title: operatorData.title || operatorData.displayName || operatorData.type,
            parameters: operatorData.parameters ? operatorData.parameters.map(p => ({ ...p })) : []
        };
        setSelectedOperator(operatorCopy);
    };

    debugLogger.debug('[App] 算子库面板初始化完成');
}

function initializeImageViewer() {
    const container = document.getElementById('image-viewer');
    if (!container) {
        console.error('[App] 找不到图像查看器容器');
        return;
    }

    imageViewer = new ImageViewerComponent('image-viewer');
    serviceRegistry.register('imageViewer', imageViewer);

    imageViewer.onImageLoaded = (img) => {
        debugLogger.debug('[App] 图像已加载:', img.width, 'x', img.height);
    };

    imageViewer.onAnnotationClicked = (annotation) => {
        debugLogger.debug('[App] 点击标注:', annotation);
    };

    debugLogger.debug('[App] 图像查看器初始化完成');
}

function initializeInspectionImageViewer() {
    const container = document.getElementById('inspection-image-area');
    if (!container) {
        debugLogger.warn('[App] 检测图像查看器容器未找到');
        return;
    }

    const existingInspectionImageViewer = serviceRegistry.get('inspectionImageViewer');
    if (existingInspectionImageViewer) {
        requestAnimationFrame(() => {
            existingInspectionImageViewer.imageCanvas?.resize();
        });
        return;
    }

    try {
        const inspectionImageViewer = new ImageViewerComponent('inspection-image-area');
        serviceRegistry.register('inspectionImageViewer', inspectionImageViewer);

        const lastResult = inspectionController.getLastResult?.();
        const lastImage = getInlineResultImageBase64(lastResult);
        if (lastImage) {
            loadViewerImageSilently(inspectionImageViewer, `data:image/png;base64,${lastImage}`);
        }

        debugLogger.debug('[App] 检测图像查看器初始化完成');
    } catch (error) {
        console.error('[App] 检测图像查看器初始化失败:', error);
    }
}

function openImageViewerFromPreview(imageSource) {
    const viewer = serviceRegistry.get('imageViewer');
    if (!imageSource || !viewer) {
        return;
    }

    void viewer.loadImage(imageSource)
        .then(() => {
            setCurrentView('image');
            syncActiveNavButton('image');
            return switchView('image');
        })
        .catch(error => {
            console.error('[App] 打开预览大图失败:', error);
            showToast(`打开预览大图失败: ${error.message}`, 'error');
        });
}

function initializeNodePreviewExperience() {
    if (!flowCanvas) {
        return;
    }

    if (!nodePreviewCoordinator) {
        nodePreviewCoordinator = new NodePreviewCoordinator({
            getProjectId: () => getCurrentProject()?.id || null,
            getFlowRevision: () => flowCanvas.getFlowRevision?.() || 0,
            getNodeById: nodeId => flowCanvas.nodes.get(nodeId) || null,
            getOperatorMetadata: type => findOperatorDefinition(type),
            getInputImageBase64: () => {
                const inspectionResult = serviceRegistry.get('lastInspectionResult') || inspectionController.getLastResult?.();
                return resolvePreviewInputImageBase64(inspectionResult);
            },
            previewExecutor: (nodeId, options) => inspectionController.previewNode(nodeId, options),
            subscribeStructureState: listener => flowCanvas.subscribeStructureState(listener),
            debounceMs: 500
        });
        serviceRegistry.register('nodePreviewCoordinator', nodePreviewCoordinator);
    }

    if (!nodePreviewOverlay) {
        const container = document.querySelector('.flow-editor-container');
        if (container) {
            nodePreviewOverlay = new NodePreviewOverlay(container, flowCanvas, nodePreviewCoordinator, {
                onOpenImage: openImageViewerFromPreview
            });
            serviceRegistry.register('nodePreviewOverlay', nodePreviewOverlay);
        }
    }
}

function initializeInspectionController() {
    const unsubscribeCompleted = inspectionController.onInspectionCompleted((result) => {
        const currentProjectId = getCurrentProject()?.id || null;
        const normalizedResult = normalizeInspectionResultRecord(result, currentProjectId);
        if (!normalizedResult) {
            return;
        }

        if (normalizedResult.projectId && normalizedResult.projectId !== currentProjectId) {
            debugLogger.warn('[App] Ignore stale inspection result from another project.', {
                activeProjectId: currentProjectId,
                resultProjectId: normalizedResult.projectId
            });
            return;
        }

        eventBus.emit('inspection:result', normalizedResult);
        serviceRegistry.register('lastInspectionResult', normalizedResult);
        window._lastInspectionResult = normalizedResult;

        const isRealtimeResult = inspectionController.getState?.().isRealtime === true;

        if (getCurrentView() === 'inspection') {
            const outputImage = getInlineResultImageBase64(normalizedResult);
            const inspectionImageViewerService = serviceRegistry.get('inspectionImageViewer');
            if (outputImage && inspectionImageViewerService) {
                loadViewerImageSilently(inspectionImageViewerService, `data:image/png;base64,${outputImage}`);
            }

            updateInspectionResultsPanel(normalizedResult);
        }

        if (resultPanel && isResultPanelVisible()) {
            resultPanel.setProjectContext(currentProjectId);
            const normalizedDefects = buildResultDefects(normalizedResult);
            resultPanel.addResult({
                id: normalizedResult.id,
                projectId: normalizedResult.projectId,
                status: normalizedResult.status,
                defects: normalizedDefects,
                defectCount: normalizedResult.defectCount,
                processingTime: normalizedResult.processingTime ?? normalizedResult.processingTimeMs,
                processingTimeMs: normalizedResult.processingTimeMs,
                timestamp: normalizedResult.timestamp || new Date().toISOString(),
                confidenceScore: normalizedResult.confidenceScore,
                imageId: normalizedResult.imageId,
                imageData: getInlineResultImageBase64(normalizedResult),
                outputImage: normalizedResult.outputImage || null,
                outputImageBase64: normalizedResult.outputImageBase64 || null,
                resultImageBase64: normalizedResult.resultImageBase64 || null,
                outputData: normalizedResult.outputData || {},
                analysisData: normalizedResult.analysisData || null,
                errorMessage: normalizedResult.errorMessage
            }, {
                isRealtime: isRealtimeResult
            });

            if (!resultPanel.serverPaged) {
                scheduleResultPanelAnalyticsRefresh(resultPanel, currentProjectId, isRealtimeResult);
            }
        }

        if (normalizedResult.status === 'Error') {
            showToast(`检测错误: ${normalizedResult.errorMessage || '未知错误'}`, 'error', {
                minIntervalMs: 5000,
                key: 'inspection-result-error'
            });
        }
    });

    const unsubscribeError = inspectionController.onInspectionError((error) => {
        eventBus.emit('inspection:error', error);
        console.error('[App] 检测错误:', error);
        showToast('检测失败: ' + error.message, 'error');
    });

    subscriptions.push(unsubscribeCompleted, unsubscribeError);
    debugLogger.debug('[App] 检测控制器初始化完成');
}

function initializePropertyPanel() {
    const container = document.getElementById('property-panel');
    if (!container) {
        console.error('[App] 找不到属性面板容器');
        return;
    }

    propertyPanel = new PropertyPanel('property-panel', {
        previewCoordinator: nodePreviewCoordinator,
        onOpenPreviewImage: openImageViewerFromPreview
    });
    serviceRegistry.register('propertyPanel', propertyPanel);

    trackedSubscribe(subscribeSelectedOperator, (operator) => {
        if (operator) {
            debugLogger.debug('[App] 选中算子变化:', operator.title || operator.type);
            propertyPanel.setOperator(operator);
        } else {
            propertyPanel.clear();
        }
    });

    propertyPanel.onChange((values) => {
        debugLogger.debug('[App] 算子参数变更:', values);
        const operator = getSelectedOperator();
        if (operator && flowCanvas) {
            const node = flowCanvas.nodes.get(operator.id);
            if (node) {
                node.parameters = operator.parameters;
                flowCanvas.markFlowStructureChanged?.('parameter-change');
                syncCurrentProjectFlowFromCanvas();
            }
        }
    });

    debugLogger.debug('[App] 属性面板初始化完成');
}

function initializePropertySidebarController() {
    const handle = document.querySelector('[data-sidebar-resizer="property"]');
    if (!handle) {
        debugLogger.warn('[App] Property sidebar resizer not found');
        return;
    }

    propertySidebarController?.destroy?.();
    propertySidebarController = new PropertySidebarController({
        handle,
        root: document.documentElement,
        getCurrentView
    });
}

async function loadInspectionHistory({
    pageIndex = 0,
    pageSize = resultPanel?.pageSize ?? 12,
    startTime = resultPanel?.getAnalyticsQueryParams?.().startTime,
    endTime = resultPanel?.getAnalyticsQueryParams?.().endTime,
    status = resultPanel?.getAnalyticsQueryParams?.().status,
    defectType = resultPanel?.getAnalyticsQueryParams?.().defectType
} = {}) {
    const project = getCurrentProject();
    if (!project) {
        debugLogger.debug('[App] 没有打开的工程，跳过加载历史数据');
        return false;
    }

    try {
        debugLogger.debug('[App] 正在加载检测历史数据...');
        const response = await httpClient.get(`/inspection/history/${project.id}`, {
            pageIndex,
            pageSize,
            ...(startTime ? { startTime } : {}),
            ...(endTime ? { endTime } : {}),
            ...(status ? { status } : {}),
            ...(defectType ? { defectType } : {})
        });

        const results = Array.isArray(response)
            ? response
            : (response?.items || response?.Items || []);
        const totalCount = Array.isArray(response)
            ? results.length
            : (response?.totalCount ?? response?.TotalCount ?? results.length);
        const resolvedPageIndex = Array.isArray(response)
            ? pageIndex
            : (response?.pageIndex ?? response?.PageIndex ?? pageIndex);
        const resolvedPageSize = Array.isArray(response)
            ? pageSize
            : (response?.pageSize ?? response?.PageSize ?? pageSize);

        if (resultPanel) {
            resultPanel.setProjectContext(project.id);
        }

        if (Array.isArray(results) && resultPanel) {
            const normalizedResults = results
                .map(result => normalizeInspectionResultRecord(result, project.id))
                .filter(Boolean);

            resultPanel.loadResults(normalizedResults, {
                totalCount,
                pageIndex: resolvedPageIndex,
                pageSize: resolvedPageSize,
                serverPaged: true
            });

            if (typeof resultPanel.loadServerAnalytics === 'function') {
                await resultPanel.loadServerAnalytics();
            }

            debugLogger.debug(`[App] 已加载 ${normalizedResults.length} 条历史检测记录`);
        }

        return true;
    } catch (error) {
        console.error('[App] 加载检测历史数据失败:', error);
        return false;
    }
}

function initializeFlowEditor() {
    const canvas = document.getElementById('flow-canvas');
    if (!canvas) {
        console.error('[App] 找不到流程编辑器画布');
        return;
    }

    flowCanvas = new FlowCanvas('flow-canvas');
    const flowCanvasAdapter = createFlowCanvasAdapter(flowCanvas, { eventBus });
    serviceRegistry.register('flowCanvas', flowCanvas);
    serviceRegistry.register('flowCanvasAdapter', flowCanvasAdapter);

    initializeProjectFlowCanvasSync();
    inspectionController.setFlowProvider?.(() => flowCanvasAdapter.serialize());
    inspectionController.setImageSinks?.([
        (imageData) => {
            if (getCurrentView() !== 'inspection') {
                return;
            }

            loadViewerImageSilently(serviceRegistry.get('inspectionImageViewer'), imageData);
        },
        (imageData) => {
            if (getCurrentView() !== 'image') {
                return;
            }

            loadViewerImageSilently(serviceRegistry.get('imageViewer'), imageData);
        }
    ]);
    initializeNodePreviewExperience();

    flowCanvas.onNodeSelected = (node) => {
        if (node) {
            const operatorDef = findOperatorDefinition(node.type);
            setSelectedOperator({
                id: node.id,
                type: node.type,
                title: node.title || operatorDef?.displayName || node.type,
                displayName: operatorDef?.displayName || node.title || node.type,
                iconPath: node.iconPath || operatorDef?.iconPath || null,
                color: node.color || null,
                inputPorts: node.inputs || operatorDef?.inputPorts || [],
                outputPorts: node.outputs || operatorDef?.outputPorts || [],
                parameters: mergeParameters(operatorDef?.parameters, node.parameters)
            });
            nodePreviewCoordinator?.setActiveNode(node);
        } else {
            setSelectedOperator(null);
            nodePreviewCoordinator?.setActiveNode(null);
        }
    };

    flowCanvas.onNodeDoubleClicked = (node) => {
        if (node) {
            nodePreviewCoordinator?.setActiveNode(node);
        }
    };

    flowEditorInteraction = new FlowEditorInteraction(flowCanvas, { projectManager });
    serviceRegistry.register('flowEditorInteraction', flowEditorInteraction);
    startAutoSave();

    debugLogger.debug('[App] 流程编辑器初始化完成');
}

function handleNewProject(options = {}) {
    const { preserveCanvas = false } = options;
    const nameInput = createLabeledInput({ label: '工程名称', required: true, placeholder: `Project_${Date.now()}` });
    const descInput = createLabeledInput({ label: '描述', placeholder: '工程描述...' });

    const content = document.createElement('div');
    content.appendChild(nameInput);
    content.appendChild(descInput);

    let modalOverlay = null;
    const btnCancel = createButton({
        text: '取消',
        type: 'secondary',
        onClick: () => closeModal(modalOverlay)
    });

    const btnCreate = createButton({
        text: preserveCanvas ? '保存' : '创建',
        onClick: () => {
            const name = nameInput.querySelector('input').value.trim();
            const desc = descInput.querySelector('input').value.trim();

            if (!name) {
                showToast('请输入工程名称', 'warning');
                return;
            }

            void createProject(name, desc, preserveCanvas).then(() => {
                closeModal(modalOverlay);
                void switchView('flow');
            }).catch(() => {});
        }
    });

    modalOverlay = createModal({
        title: preserveCanvas ? '保存为新工程' : '新建工程',
        content,
        footer: [btnCancel, btnCreate],
        width: '400px'
    });
}

async function loadProject(projectId) {
    try {
        const project = await projectManager.openProject(projectId);
        showToast(`工程 "${project.name}" 已加载`, 'success');
        return project;
    } catch (error) {
        console.error('[App] 加载工程失败:', error);
        showToast('加载工程失败: ' + error.message, 'error');
        throw error;
    }
}

async function createProject(name, description = '', preserveCanvas = false) {
    try {
        const preservedFlow = preserveCanvas && flowCanvas && typeof flowCanvas.serialize === 'function'
            ? flowCanvas.serialize()
            : null;
        const project = await projectManager.createProject(name, description);

        if (preserveCanvas && preservedFlow && flowCanvas) {
            withProjectFlowSyncSuppressed(() => flowCanvas.deserialize(preservedFlow));
            projectManager.updateFlow(flowCanvas.serialize());
            await projectManager.saveProject(projectManager.getCurrentProject?.() || project);
            debugLogger.debug('[App] 画布内容已保存到新工程:', project.name);
        } else if (flowCanvas && !preserveCanvas) {
            withProjectFlowSyncSuppressed(() => flowCanvas.clear());
        }

        showToast(`工程 "${name}" 已创建`, 'success');
        return project;
    } catch (error) {
        console.error('[App] 创建工程失败:', error);
        const errorMsg = error?.message || String(error);
        if (errorMsg.includes('无法连接到后端服务')) {
            Dialog.alert('连接失败', errorMsg.replace(/\n/g, '<br>'), null);
        } else {
            showToast('创建工程失败: ' + errorMsg, 'error');
        }
        throw error;
    }
}

function initializeTheme() {
    const initialTheme = bootstrapTheme();
    const themeToggle = document.getElementById('btn-theme-toggle');
    bindThemeToggle(themeToggle, handleThemeChanged);

    void syncThemeWithSettings(
        () => httpClient.get('/settings'),
        { expectedTheme: initialTheme }
    );
}

async function persistThemePreference(theme) {
    const result = await httpClient.put('/settings/theme', { theme });
    return result?.theme || theme;
}

async function handleThemeChanged({ previousTheme, nextTheme }) {
    if (themeUpdateInFlight) {
        return;
    }

    themeUpdateInFlight = true;
    const themeToggle = document.getElementById('btn-theme-toggle');
    if (themeToggle) {
        themeToggle.disabled = true;
    }

    applyTheme(nextTheme, { persist: true });

    let persistedTheme = nextTheme;
    try {
        persistedTheme = await persistThemePreference(nextTheme);
        applyTheme(persistedTheme, { persist: true });
    } catch (error) {
        applyTheme(previousTheme, { persist: true });
        showToast(`主题保存失败: ${error.message}`, 'error');
        themeUpdateInFlight = false;
        if (themeToggle) {
            themeToggle.disabled = false;
        }
        return;
    }

    const message = persistedTheme === 'dark' ? '已切换到暗色模式' : '已切换到亮色模式';
    showToast(message, 'info');
    themeUpdateInFlight = false;
    if (themeToggle) {
        themeToggle.disabled = false;
    }
}

function initializeNavigation() {
    getViewManager().bindNavigation();
    document.querySelectorAll('[data-open-view]').forEach((button) => {
        if (button.dataset.cvOpenViewBound) {
            return;
        }

        button.dataset.cvOpenViewBound = 'true';
        button.addEventListener('click', () => {
            const view = button.dataset.openView;
            if (!view) {
                return;
            }

            setCurrentView(view);
            syncActiveNavButton(view);
            void switchView(view).catch(error => handleFeatureLoadError('视图切换', error));
        });
    });

    syncActiveNavButton(getCurrentView());
}

async function ensureProjectView() {
    if (projectView) {
        return projectView;
    }

    const container = document.getElementById('project-view');
    if (!container) {
        debugLogger.warn('[App] 工程视图容器未找到，将在首次切换到工程视图时初始化');
        return null;
    }

    const { ProjectView } = await loadProjectViewModule();
    projectView = new ProjectView('project-view');

    debugLogger.debug('[App] 工程视图初始化完成');
    return projectView;
}

async function ensureResultPanel() {
    if (resultPanel) {
        return resultPanel;
    }

    const container = document.getElementById('results-list-container');
    if (!container) {
        debugLogger.warn('[App] 结果视图容器未找到');
        return null;
    }

    const { ResultPanel } = await loadResultPanelModule();
    resultPanel = new ResultPanel('results-list-container');
    serviceRegistry.register('resultPanel', resultPanel);
    resultPanel.setProjectContext(getCurrentProject()?.id || null);
    resultPanel.setHistoryLoader(loadInspectionHistory);

    resultPanel.onResultClick = (result) => {
        debugLogger.debug('[App] 点击结果:', result);
        if (resultPanel && result) {
            resultPanel.showResultDetail(result);
        }
    };

    const clearBtn = document.getElementById('btn-clear-results');
    if (clearBtn && !clearBtn.dataset.cvBound) {
        clearBtn.dataset.cvBound = 'true';
        clearBtn.addEventListener('click', () => {
            if (confirm('确定要清空当前结果视图吗？此操作不会删除后端历史记录。')) {
                resultPanel.clear();
                showToast('当前结果视图已清空，历史记录未删除', 'success');
            }
        });
    }

    debugLogger.debug('[App] 结果面板初始化完成（现代化仪表盘）');
    return resultPanel;
}

async function ensureInspectionPanelReady() {
    const container = document.getElementById('inspection-control-panel');
    if (!container) {
        debugLogger.warn('[App] 检测控制面板容器未找到');
        return null;
    }

    if (inspectionPanel) {
        inspectionPanel.setProjectContext(getCurrentProject()?.id || null);
        return inspectionPanel;
    }

    const { InspectionPanel } = await loadInspectionPanelModule();

    const existingInspectionPanel = serviceRegistry.get('inspectionPanel');
    if (existingInspectionPanel && typeof existingInspectionPanel.dispose === 'function') {
        debugLogger.warn('[App] 发现残留的 InspectionPanel 实例，正在销毁...');
        existingInspectionPanel.dispose();
    }

    inspectionPanel = new InspectionPanel('inspection-control-panel');
    serviceRegistry.register('inspectionPanel', inspectionPanel);
    inspectionPanel.setProjectContext(getCurrentProject()?.id || null);

    const lastResult = inspectionController.getLastResult?.();
    if (lastResult) {
        inspectionPanel.handleInspectionResult(lastResult);
    }

    debugLogger.debug('[App] 检测控制面板初始化完成');
    return inspectionPanel;
}

async function ensureStationMonitorView() {
    if (stationMonitorView) {
        return stationMonitorView;
    }

    const container = document.getElementById('stations-view');
    if (!container) {
        debugLogger.warn('[App] Station monitor container not found.');
        return null;
    }

    const { StationMonitorView } = await loadStationMonitorModule();
    stationMonitorView = new StationMonitorView('stations-view');
    serviceRegistry.register('stationMonitorView', stationMonitorView);
    debugLogger.debug('[App] Station monitor view initialized.');
    return stationMonitorView;
}

async function ensureAiPanel() {
    if (aiPanel) {
        return aiPanel;
    }

    const flowCanvasService = serviceRegistry.get('flowCanvasAdapter') || serviceRegistry.get('flowCanvas');
    if (!flowCanvasService) {
        debugLogger.warn('[App] FlowCanvas 未就绪，无法初始化 AI 面板');
        return null;
    }

    const { AiPanel } = await loadAiPanelModule();
    aiPanel = new AiPanel('ai-view', flowCanvasService, {
        getOperators: () => operatorLibraryPanel?.getOperators?.() || [],
        showToast,
        onApplied: (flow) => {
            const syncedFlow = syncCurrentProjectFlowFromCanvas() || flow;
            getAiGenerationController().publishApplied(syncedFlow);
        },
        onCanvasChanged: ({ flow } = {}) => {
            const syncedFlow = syncCurrentProjectFlowFromCanvas() || flow || null;
            if (syncedFlow) {
                getAiGenerationController().publishApplied(syncedFlow);
            }
        }
    });
    serviceRegistry.register('aiPanel', aiPanel);
    debugLogger.debug('[App] AI 面板初始化完成');
    return aiPanel;
}

async function switchView(view) {
    return getViewManager().switchView(view);
}

function initializeToolbar() {
    if (toolbarCommandDisposer) {
        return;
    }

    toolbarCommandDisposer = bindToolbarCommands({
        documentRef: document,
        serviceRegistry,
        getPropertyPanel: () => propertyPanel,
        getCurrentProject,
        getFlowCanvas: () => flowCanvas,
        getImageViewer: () => imageViewer,
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
    });
}

function initializeAiGeneration() {
    getAiGenerationController();
    debugLogger.debug('[App] AI 生成功能已升级为独立面板');
}

async function initializeApp() {
    if (appInitialized) {
        return true;
    }

    debugLogger.debug('[App] 初始化应用...');
    showLoadingScreen();

    const authState = await bootstrapAuthSession();
    if (!authState.ok) {
        debugLogger.warn(`[App] 认证启动失败: ${authState.reason}`);
        return false;
    }

    updateAuthenticatedUserDisplay();
    initializeNavigation();
    initializeOperatorLibraryPanel();
    initializeFlowEditor();
    initializeImageViewer();
    initializeInspectionController();
    initializePropertyPanel();
    initializePropertySidebarController();
    initializeTheme();
    initializeToolbar();
    startStatusBarUpdates();
    initializeStudioPerformanceGuards();
    trackedSubscribe(subscribeProject, (project) => {
        window.setTimeout(() => {
            void handleProjectChange(project).catch(error => {
                handleFeatureLoadError('工程切换', error);
            });
        }, 0);
    });

    appInitialized = true;

    debugLogger.debug('[App] 应用初始化完成');
    showToast('ClearVision 已就绪', 'success');
    return true;
}

async function bootstrapApp() {
    if (appBootstrapPromise) {
        return appBootstrapPromise;
    }

    appBootstrapPromise = (async () => {
        const initialized = await initializeApp();
        if (!initialized) {
            hideLoadingScreen();
            return false;
        }

        setTimeout(() => {
            hideLoadingScreen();
            showWelcomeScreen();
        }, 500);

        return true;
    })();

    return appBootstrapPromise;
}

async function handleProjectChange(project) {
    eventBus.emit('project:changed', { project });
    if (!project?.id) {
        inspectionController.setProject(null);
        inspectionPanel?.setProjectContext?.(null);
        resultPanel?.setProjectContext?.(null);
        resultPanel?.clear?.();
        return;
    }

    inspectionController.setProject(project.id);
    inspectionPanel?.setProjectContext?.(project.id);

    if (flowCanvas) {
        withProjectFlowSyncSuppressed(() => {
            if (project.flow) {
                debugLogger.debug('[App] 当前工程已切换，加载流程数据:', project.flow);
                flowCanvas.deserialize(project.flow);
            } else {
                debugLogger.debug('[App] 当前工程没有流程数据，清空画布');
                flowCanvas.clear();
            }
        });
    }

    resultPanel?.setProjectContext?.(project.id);
    resultPanel?.clear?.();

    promptLocalDraftRestore(project);

    setCurrentView('flow');
    syncActiveNavButton('flow');
    await switchView('flow');
}

function syncCurrentProjectFlowFromCanvas() {
    if (projectFlowSyncSuppressionDepth > 0) {
        return null;
    }

    const project = getCurrentProject();
    if (!project || !flowCanvas || typeof flowCanvas.serialize !== 'function') {
        return null;
    }

    const flow = flowCanvas.serialize();
    projectManager.updateFlow(flow);
    return flow;
}

function startAutoSave() {
    stopAutoSave();

    autoSaveInterval = setInterval(async () => {
        if (isInspectionActiveForBackgroundWork()) {
            debugLogger.debug('[LocalDraftBackup] 检测运行中，跳过本轮本机草稿备份');
            return;
        }

        const project = getCurrentProject();
        if (project && projectManager.hasUnsavedChanges?.()) {
            try {
                const flow = project.flow || (flowCanvas && typeof flowCanvas.serialize === 'function' ? flowCanvas.serialize() : null);
                if (!flow) {
                    return;
                }

                const signature = getLocalDraftBackupSignature(project, flow);
                if (signature === lastLocalDraftBackupSignature) {
                    return;
                }

                saveLocalDraftBackup(project, flow, 'timer');
                debugLogger.debug('[LocalDraftBackup] 本机草稿备份完成:', new Date().toLocaleTimeString());
            } catch (err) {
                console.error('[LocalDraftBackup] 本机草稿备份失败:', err);
            }
        }
    }, AUTO_SAVE_DELAY);

    debugLogger.debug('[LocalDraftBackup] 本机草稿备份已启动，间隔:', AUTO_SAVE_DELAY / 1000 / 60, '分钟');
}

/**
 * 銆愰樁娈礏-B4銆戝仠姝㈡湰鏈鸿崏绋垮浠?
 */
function stopAutoSave() {
    if (autoSaveInterval) {
        clearInterval(autoSaveInterval);
        autoSaveInterval = null;
        debugLogger.debug('[LocalDraftBackup] 鏈満鑽夌澶囦唤宸插仠姝?');
    }
}

/**
 * 銆愰樁娈礏-B4銆戠珛鍗虫墽琛屾湰鏈鸿崏绋垮浠?
 */
async function triggerAutoSave() {
    const project = getCurrentProject();
    if (project && flowCanvas) {
        try {
            project.flow = flowCanvas.serialize();
            saveLocalDraftBackup(project, project.flow, 'manual');
            debugLogger.debug('[LocalDraftBackup] 手动触发本机草稿备份完成');
            showToast('本机草稿已更新；正式工程仍需点击“保存工程”。', 'success');
        } catch (err) {
            console.error('[LocalDraftBackup] 手动备份失败:', err);
            showToast('本机草稿备份失败', 'error');
        }
    }
}

/**
 * 【阶段B-B5】导出工程为JSON文件
 */
function createProjectExportFilename(projectName, extension = '.cvproj.json') {
    const normalizedName = String(projectName || 'project')
        .trim()
        .replace(/[\\/:*?"<>|]/g, '_');
    const safeName = normalizedName || 'project';
    const dateStamp = new Date().toISOString().slice(0, 10);
    return `${safeName}_${dateStamp}${extension}`;
}

function triggerDownload(content, filename, mimeType) {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

async function resolveProjectExportSource(projectId = null) {
    const currentProject = getCurrentProject();
    const targetProjectId = projectId || currentProject?.id || null;

    if (!targetProjectId) {
        return null;
    }

    if (currentProject && currentProject.id === targetProjectId) {
        return {
            project: currentProject,
            flow: flowCanvas ? flowCanvas.serialize() : currentProject.flow,
            isCurrentProject: true
        };
    }

    const project = await httpClient.get(`/projects/${targetProjectId}`);
    return {
        project,
        flow: project.flow,
        isCurrentProject: false
    };
}

async function exportProjectToJson(projectId = null) {
    const exportSource = await resolveProjectExportSource(projectId);
    if (!exportSource?.project) {
        showToast('没有可导出的工程', 'warning');
        return;
    }
    
    try {
        const { project, flow } = exportSource;
        const exportData = {
            version: '1.0',
            exportTime: new Date().toISOString(),
            project: {
                id: project.id,
                name: project.name,
                description: project.description,
                createdAt: project.createdAt,
                updatedAt: new Date().toISOString(),
                flow
            }
        };
        
        triggerDownload(
            JSON.stringify(exportData, null, 2),
            createProjectExportFilename(project.name),
            'application/json'
        );
        
        showToast('工程导出成功', 'success');
        debugLogger.debug('[Export] 工程已导出', project.name);
    } catch (err) {
        console.error('[Export] 导出失败:', err);
        showToast('工程导出失败', 'error');
    }
}

async function exportRuntimePackage(projectId = null) {
    const currentProject = getCurrentProject();
    const targetProjectId = projectId || currentProject?.id || null;
    if (!targetProjectId) {
        showToast('没有可导出的工程', 'warning');
        return;
    }
    try {
        const requestBody = {};
        if (currentProject && currentProject.id === targetProjectId) {
            const flow = flowCanvas ? flowCanvas.serialize() : currentProject.flow;
            await projectManager.saveProject({
                ...currentProject,
                flow
            });
            requestBody.flow = flow;
        }

        const response = await httpClient.post(`/projects/${targetProjectId}/runtime-package/export`, requestBody);

        showToast('运行包导出成功', 'success');
        Dialog.alert(
            '运行包已导出',
            `路径: ${response.packageRootPath || '-'}<br>FlowHash: ${response.flowHash || '-'}`,
            null
        );
    } catch (err) {
        console.error('[Export] 运行包导出失败:', err);
        const msg = err.message || '未知错误';
        if (msg.includes('\n')) {
            Dialog.alert('导出失败', msg.replace(/\n/g, '<br>'), null);
        } else {
            showToast(`运行包导出失败: ${msg}`, 'error');
        }
    }
}

function showProjectExportDialog() {
    const content = document.createElement('div');
    content.innerHTML = `
        <div style="display:flex; flex-direction:column; gap:12px;">
            <div style="display:flex; flex-direction:column; gap:6px;">
                <label for="project-export-select" style="font-weight:600;">选择工程</label>
                <select id="project-export-select" class="cv-input" disabled>
                    <option value="">正在加载工程库...</option>
                </select>
                <div id="project-export-hint" style="color:var(--text-muted); font-size:12px;">
                    可直接从工程库选择已有工程，无需先打开。
                </div>
            </div>
            <div style="padding:12px; border:1px solid var(--border-color); border-radius:6px;">
                <div style="font-weight:600; margin-bottom:4px;">工程文件</div>
                <div style="color:var(--text-muted); font-size:12px;">导出可继续在 Studio 中编辑的工程快照。</div>
            </div>
            <div style="padding:12px; border:1px solid var(--border-color); border-radius:6px;">
                <div style="font-weight:600; margin-bottom:4px;">运行包</div>
                <div style="color:var(--text-muted); font-size:12px;">导出可供工站使用的运行包；选择当前打开工程时会带上画布上的最新修改。</div>
            </div>
        </div>
    `;

    const projectSelect = content.querySelector('#project-export-select');
    const projectExportHint = content.querySelector('#project-export-hint');
    const currentProject = getCurrentProject();
    const preferredProjectId = currentProject?.id || null;

    let modalOverlay = null;
    const btnCancel = createButton({
        text: '取消',
        type: 'secondary',
        onClick: () => closeModal(modalOverlay)
    });
    const btnJson = createButton({
        text: '导出工程文件',
        type: 'secondary',
        disabled: true,
        onClick: async () => {
            const selectedProjectId = projectSelect.value;
            if (!selectedProjectId) {
                showToast('请选择要导出的工程', 'warning');
                return;
            }

            closeModal(modalOverlay);
            await exportProjectToJson(selectedProjectId);
        }
    });
    const btnRuntime = createButton({
        text: '导出运行包',
        disabled: true,
        onClick: async () => {
            const selectedProjectId = projectSelect.value;
            if (!selectedProjectId) {
                showToast('请选择要导出的工程', 'warning');
                return;
            }

            closeModal(modalOverlay);
            await exportRuntimePackage(selectedProjectId);
        }
    });

    const setExportActionsEnabled = (enabled) => {
        btnJson.disabled = !enabled;
        btnRuntime.disabled = !enabled;
    };

    const populateProjectOptions = (projects) => {
        projectSelect.innerHTML = '';

        projects.forEach((project, index) => {
            const option = document.createElement('option');
            const fallbackName = `未命名工程 ${index + 1}`;
            const modifiedAt = project.modifiedAt || project.updatedAt || project.createdAt;
            const dateLabel = modifiedAt
                ? new Date(modifiedAt).toLocaleDateString('zh-CN')
                : null;

            option.value = project.id;
            option.textContent = dateLabel
                ? `${project.name || fallbackName} (${dateLabel})`
                : (project.name || fallbackName);
            projectSelect.appendChild(option);
        });

        const hasPreferredProject = preferredProjectId
            && projects.some(project => project.id === preferredProjectId);
        projectSelect.value = hasPreferredProject
            ? preferredProjectId
            : projects[0]?.id || '';
        projectSelect.disabled = !projectSelect.value;
        setExportActionsEnabled(Boolean(projectSelect.value));
    };

    modalOverlay = createModal({
        title: '导出',
        content,
        footer: [btnCancel, btnJson, btnRuntime],
        width: '420px'
    });

    (async () => {
        try {
            const projects = await projectManager.getProjectList();
            const exportableProjects = Array.isArray(projects)
                ? projects.filter(project => Boolean(project?.id))
                : [];

            if (exportableProjects.length === 0) {
                projectSelect.innerHTML = '<option value="">工程库暂无可导出工程</option>';
                projectSelect.disabled = true;
                projectExportHint.textContent = '工程库里还没有工程，请先创建工程。';
                setExportActionsEnabled(false);
                return;
            }

            populateProjectOptions(exportableProjects);
            projectExportHint.textContent = preferredProjectId
                ? '已默认选中当前打开工程，你也可以切换为工程库里的其他工程。'
                : '请选择工程库中的已有工程进行导出。';
        } catch (error) {
            console.error('[Export] 加载工程库失败', error);

            if (currentProject?.id) {
                populateProjectOptions([currentProject]);
                projectExportHint.textContent = '工程库加载失败，已回退为当前打开工程。';
                return;
            }

            projectSelect.innerHTML = '<option value="">工程库加载失败</option>';
            projectSelect.disabled = true;
            projectExportHint.textContent = '暂时无法读取工程库，请稍后重试。';
            setExportActionsEnabled(false);
        }
    })();
}

/**
 * 銆愪慨澶嶃€戞牴鎹畻瀛愮被鍨嬫煡鎵剧畻瀛愬簱涓殑瀹氫箟鏁版嵁
 * @param {string} type - 算子类型
 * @returns {Object|null} 算子定义数据
 */
function findOperatorDefinition(type) {
    if (!operatorLibraryPanel) return null;
    const operators = operatorLibraryPanel.getOperators ? operatorLibraryPanel.getOperators() : [];
    return operators.find(op => op.type === type) || null;
}

/**
 * 銆愪慨澶嶃€戝悎骞跺弬鏁板畾涔変笌鍙傛暟鍊?
 * @param {Array} defParams - 绠楀瓙搴撲腑鐨勫弬鏁板畾涔夛紙鍩哄噯锛?
 * @param {Array} nodeParams - 鐢诲竷鑺傜偣淇濆瓨鐨勫弬鏁板€?
 * @returns {Array} 鍚堝苟鍚庣殑鍙傛暟鍒楄〃
 */
function mergeParameters(defParams, nodeParams) {
    if (!defParams || defParams.length === 0) return nodeParams || [];
    
    return defParams.map(defP => {
        // [淇] 涓嶅尯鍒嗗ぇ灏忓啓鍖归厤锛岃В鍐冲墠绔?(camelCase) 涓庡悗绔?(PascalCase) 鐨勫樊寮?
        const nodeP = (nodeParams || []).find(np => 
            (np.name && defP.name && np.name.toLowerCase() === defP.name.toLowerCase()) ||
            (np.Name && defP.name && np.Name.toLowerCase() === defP.name.toLowerCase())
        );
        
        const mergedParam = { 
            ...defP,
            // 浼樺厛浣跨敤鑺傜偣淇濆瓨鐨勫€?(Value 鎴?value)
            value: nodeP !== undefined ? (nodeP.value ?? nodeP.Value ?? nodeP.defaultValue ?? nodeP.DefaultValue) : defP.defaultValue
        };
        
        return mergedParam;
    });
}

/**
 * 【阶段B-B5】从JSON文件导入工程
 * @param {File} file - 鐢ㄦ埛閫夋嫨鐨勬枃浠?
 */
async function importProjectFromJson(file) {
    if (!file) return;
    
    try {
        const content = await file.text();
        const importData = JSON.parse(content);
        
        // 楠岃瘉鏂囦欢鏍煎紡
        if (!importData.project || !importData.project.flow) {
            throw new Error('无效的工程文件格式');
        }
        
        // 纭瀵煎叆
        const confirmed = confirm(`确定要导入工程 "${importData.project.name || '未命名'}" 吗？\n当前未保存的更改将会丢失。`);
        if (!confirmed) return;
        
        // 閫氳繃 projectManager 鍒涘缓鏂板伐绋嬶紙鐢卞悗绔敓鎴?ID锛?
        const importName = (importData.project.name || '未命名') + ' (导入)';
        const importDesc = importData.project.description || '';
        const project = await projectManager.createProject(importName, importDesc);
        
        // 鍔犺浇娴佺▼鍒扮敾甯?
        if (flowCanvas && importData.project.flow) {
            withProjectFlowSyncSuppressed(() => flowCanvas.deserialize(importData.project.flow));
            // 灏嗘祦绋嬫暟鎹繚瀛樺埌鍚庣
            projectManager.updateFlow(flowCanvas.serialize());
            await projectManager.saveProject(projectManager.getCurrentProject?.() || project);
        }
        
        // 璁剧疆妫€娴嬫帶鍒跺櫒鐨勫伐绋?
        inspectionController.setProject(project.id);
        
        // 鍒囨崲鍒版祦绋嬭鍥?
        switchView('flow');
        document.querySelectorAll('.nav-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.dataset.view === 'flow') btn.classList.add('active');
        });
        
        showToast('工程导入成功', 'success');
        debugLogger.debug('[Import] 工程已导入', project.name);
        
        // 鍒锋柊宸ョ▼鍒楄〃
        if (projectView) {
            projectView.refresh();
        }
    } catch (err) {
        console.error('[Import] 导入失败:', err);
        showToast('工程导入失败: ' + err.message, 'error');
    }
}

/**
 * 【阶段B-B5】显示导入对话框
 */
function showImportDialog() {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.cvproj.json,.json';
    input.onchange = (e) => {
        const file = e.target.files[0];
        if (file) {
            importProjectFromJson(file);
        }
    };
    input.click();
}

// Expose import/export functions globally for projectView.js
window.showImportDialog = showImportDialog;
window.exportProjectToJson = exportProjectToJson;
window.exportRuntimePackage = exportRuntimePackage;
window.showProjectExportDialog = showProjectExportDialog;

// ==========================================================================
// 闃舵浜旓細鐘舵€佹爮鏇存柊鍔熻兘
// ==========================================================================

/**
 * 鏇存柊鐘舵€佹爮鎸囨爣
 */
function updateStatusBar() {
    if (window.performance?.memory) {
        const memoryMB = Math.round(window.performance.memory.usedJSHeapSize / 1024 / 1024);
        const memoryEl = getStatusBarMetricElement('#memory-usage .metric-value', 'memory');
        if (memoryEl) memoryEl.textContent = `${memoryMB} MB`;
    }
}

/**
 * FPS 璁℃暟鍣?
 */
let fpsCounter = {
    frames: 0,
    lastTime: performance.now()
};
let statusBarInterval = null;
let fpsFrameTrackerInstalled = false;
let memoryMetricElement = null;
let fpsMetricElement = null;

function installFpsFrameTracker() {
    if (fpsFrameTrackerInstalled || typeof window === 'undefined' || typeof window.requestAnimationFrame !== 'function') {
        return;
    }

    const originalRequestAnimationFrame = window.requestAnimationFrame.bind(window);
    window.requestAnimationFrame = (callback) => originalRequestAnimationFrame((timestamp) => {
        fpsCounter.frames++;
        callback(timestamp);
    });
    fpsFrameTrackerInstalled = true;
}

function getStatusBarMetricElement(selector, cacheName) {
    if (cacheName === 'memory') {
        if (!memoryMetricElement) {
            memoryMetricElement = document.querySelector(selector);
        }
        return memoryMetricElement;
    }

    if (!fpsMetricElement) {
        fpsMetricElement = document.querySelector(selector);
    }
    return fpsMetricElement;
}

function updateFPS() {
    if (!statusBarStarted) {
        return;
    }

    const now = performance.now();
    const elapsed = now - fpsCounter.lastTime;

    if (elapsed >= 1000) {
        const fps = Math.round(fpsCounter.frames * 1000 / elapsed);
        const fpsEl = getStatusBarMetricElement('#fps-counter .metric-value', 'fps');
        if (fpsEl) fpsEl.textContent = `${fps} FPS`;
        
        fpsCounter.frames = 0;
        fpsCounter.lastTime = now;
    }
}

// 鍚姩鐘舵€佹爮鏇存柊
function startStatusBarUpdates() {
    if (statusBarStarted) {
        return;
    }

    statusBarStarted = true;
    installFpsFrameTracker();
    memoryMetricElement = document.querySelector('#memory-usage .metric-value');
    fpsMetricElement = document.querySelector('#fps-counter .metric-value');
    fpsCounter.frames = 0;
    fpsCounter.lastTime = performance.now();
    if (fpsMetricElement) {
        fpsMetricElement.textContent = '0 FPS';
    }

    statusBarInterval = setInterval(() => {
        updateStatusBar();
        updateFPS();
    }, 1000);
    updateStatusBar();
    updateFPS();
}

// ==========================================================================
// 闃舵浜旓細瑙嗗浘鍒囨崲杩囨浮鍔ㄧ敾
// ==========================================================================

/**
 * 鍒囨崲瑙嗗浘锛堝甫杩囨浮鍔ㄧ敾锛?
 */
function switchViewWithTransition(view) {
    const views = ['flow', 'inspection', 'results', 'project'];
    const currentView = getCurrentView();
    
    if (currentView === view) return;
    
    // 鑾峰彇褰撳墠鏄剧ず鐨勮鍥惧鍣?
    const currentContainer = document.getElementById(`${currentView}-view`) || 
                            document.getElementById(`${currentView}-editor`) ||
                            document.getElementById('flow-editor');
    
    if (currentContainer) {
        // 娣诲姞閫€鍑哄姩鐢?
        currentContainer.classList.add('view-exit');
        
        setTimeout(() => {
            currentContainer.classList.remove('view-exit');
            currentContainer.classList.add('hidden');
            
            // 鏄剧ず鏂拌鍥?
            switchView(view);
            
            const newContainer = document.getElementById(`${view}-view`) || 
                               document.getElementById(`${view}-editor`) ||
                               document.getElementById('flow-editor');
            
            if (newContainer) {
                newContainer.classList.remove('hidden');
                newContainer.classList.add('view-enter');
                
                setTimeout(() => {
                    newContainer.classList.remove('view-enter');
                }, 300);
            }
        }, 300);
    } else {
        // 鏃犲姩鐢荤洿鎺ュ垏鎹?
        switchView(view);
    }
}

// ==========================================================================
// 闃舵浜旓細鍔犺浇楠ㄦ灦灞?
// ==========================================================================

/**
 * 鏄剧ず鍔犺浇楠ㄦ灦灞?
 */
function showLoadingScreen() {
    if (document.getElementById('loading-screen')) {
        return;
    }

    const loadingScreen = document.createElement('div');
    loadingScreen.id = 'loading-screen';
    loadingScreen.className = 'loading-screen';
    loadingScreen.innerHTML = `
        <div class="loading-logo">ClearVision</div>
        <div class="loading-spinner"></div>
        <div class="loading-text">正在加载...</div>
    `;
    document.body.appendChild(loadingScreen);
}

/**
 * 闅愯棌鍔犺浇楠ㄦ灦灞?
 */
function hideLoadingScreen() {
    const loadingScreen = document.getElementById('loading-screen');
    if (loadingScreen) {
        loadingScreen.classList.add('hidden');
        setTimeout(() => loadingScreen.remove(), 500);
    }
}

// ==========================================================================
// 闃舵浜旓細娆㈣繋/寮曞椤?
// ==========================================================================

/**
 * 鏄剧ず娆㈣繋椤?
 */
function showWelcomeScreen() {
    // 检查是否首次运行
    const hasSeenWelcome = localStorage.getItem('cv_welcome_shown');
    if (hasSeenWelcome) return;
    
    const welcomeOverlay = document.createElement('div');
    welcomeOverlay.className = 'welcome-overlay';
    welcomeOverlay.innerHTML = `
        <div class="welcome-content">
            <h2 class="welcome-title">欢迎使用 ClearVision</h2>
            <p class="welcome-desc">打开最近工程，确认设备连接，继续现场检测任务。</p>
            <div class="welcome-features">
                <div class="welcome-feature">
                    <div class="welcome-feature-icon">1</div>
                    <div class="welcome-feature-title">打开或创建工程</div>
                </div>
                <div class="welcome-feature">
                    <div class="welcome-feature-icon">2</div>
                    <div class="welcome-feature-title">确认设备连接</div>
                </div>
                <div class="welcome-feature">
                    <div class="welcome-feature-icon">3</div>
                    <div class="welcome-feature-title">查看最近异常</div>
                </div>
            </div>
            <button class="btn btn-primary" id="btn-welcome-start">进入工作台</button>
        </div>
    `;
    
    document.body.appendChild(welcomeOverlay);
    
    document.getElementById('btn-welcome-start').addEventListener('click', () => {
        localStorage.setItem('cv_welcome_shown', 'true');
        welcomeOverlay.style.opacity = '0';
        setTimeout(() => welcomeOverlay.remove(), 300);
    });
}

// 启动应用
document.addEventListener('DOMContentLoaded', () => {
    bootstrapApp().catch(error => {
        console.error('[App] 应用启动失败:', error);
        hideLoadingScreen();
        showToast(`应用启动失败: ${error.message}`, 'error');
    });
});

export { 
    getCurrentView, 
    setCurrentView, 
    getSelectedOperator, 
    setSelectedOperator,
    imageViewer,
    operatorLibraryPanel,
    flowCanvas,
    flowEditorInteraction,
    exportProjectToJson,
    exportRuntimePackage,
    importProjectFromJson,
    showImportDialog,
    showProjectExportDialog,
    triggerAutoSave
};

