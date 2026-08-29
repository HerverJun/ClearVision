/**
 * 主应用入口 - S4-006: 端到端集成
 * Sprint 4: 前后端集成与用户体验闭环
 */

import { Dialog } from './shared/components/dialog.js';
import { buildOperatorNodeConfig } from './shared/operatorVisuals.js';
import { createOperatorIconElement } from './shared/operatorIconRenderer.js';
import eventBus from './core/app/eventBus.js';
import serviceRegistry from './core/app/serviceRegistry.js';
import { installLegacyGlobalAccessors } from './core/app/legacyGlobals.js';
import { createViewManager } from './core/app/viewManager.js';
import { buildResultDefects as buildBoundedResultDefects, getResultDefectCount } from './core/app/resultDefects.js';
import { getFlowNodeCount } from './core/app/flowData.js';
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
// Auth session bootstrap
// ============================================
import {
    PermissionGuard,
    bootstrapAuthSession,
    getCurrentUser,
    installUnauthorizedHandler,
    logout,
    subscribeAuthContext
} from './features/auth/auth.js';

import httpClient from './core/messaging/httpClient.js';
import { createSignal } from './core/state/store.js';
import FlowCanvas from './core/canvas/flowCanvas.js';
import { FlowEditorInteraction } from './features/flow-editor/flowEditorInteraction.js';
import FinalDecisionPanel from './features/flow-editor/finalDecisionPanel.js';
import { ImageViewerComponent } from './features/image-viewer/imageViewer.js';
import { OperatorLibraryPanel } from './features/operator-library/operatorLibrary.js';
import OperatorPaletteShell from './features/flow-editor/operatorPaletteShell.js';
import inspectionController, {
    createLightweightInspectionResult,
    getResultImageUrl,
    loadImageUrlAsBase64
} from './features/inspection/inspectionController.js';
import { showToast, createModal, closeModal, createInput, createLabeledInput, createButton } from './shared/components/uiComponents.js';
import {
    applyTheme,
    bindThemeToggle,
    bootstrapTheme,
    syncThemeWithSettings
} from './core/theme/theme.js';
import PropertySidebarController, {
    createPropertyPanelCapabilityAdapter
} from './features/flow-editor/propertySidebarController.mjs';
import PropertyPanelCapabilityOwner from './features/flow-editor/propertyPanelCapabilityOwner.mjs';
import {
    NodePreviewCoordinator,
    resolveCameraPreviewInputFrame,
    resolvePreviewInputImageBase64
} from './features/flow-editor/previewCoordinator.js';
import PreviewPanelCapabilityOwner, {
    createPreviewPanelCapabilityAdapter
} from './features/flow-editor/previewPanelCapabilityOwner.mjs';
import GlobalVariablesCapabilityOwner, {
    createGlobalVariablesCapabilityAdapter
} from './features/global-variables/globalVariablesCapabilityOwner.mjs';
import ProjectPageCapabilityOwner, {
    createProjectPageCapabilityAdapter
} from './features/project/projectPageCapabilityOwner.mjs';
import ResultsReviewCapabilityOwner, {
    createResultsReviewCapabilityAdapter
} from './features/results/resultsReviewCapabilityOwner.mjs';
import projectManager, {
    getCurrentProject,
    subscribeProject
} from './features/project/projectManager.js';
import localDraftStorage from './features/project/localDraftStorage.js';

const NODE_PREVIEW_INSPECTOR_FLAG_KEY = 'Studio:NodePreviewInspectorEnabled';
const PROPERTY_PANEL_CAPABILITY_FLAG_KEY = 'Studio2.PropertyPanel';
const PREVIEW_PANEL_CAPABILITY_FLAG_KEY = 'Studio2.PreviewPanel';
const GLOBAL_VARIABLES_CAPABILITY_FLAG_KEY = 'Studio2.GlobalVariables';
const PROJECT_PAGE_CAPABILITY_FLAG_KEY = 'Studio2.ProjectPage';
const RESULTS_REVIEW_CAPABILITY_FLAG_KEY = 'Studio2.ResultsReview';

function readStartupFeatureFlagOnce(flagKey) {
    const startup = window.__CLEARVISION_STARTUP__;
    const featureFlags = startup && typeof startup === 'object' &&
        startup.featureFlags && typeof startup.featureFlags === 'object'
        ? startup.featureFlags
        : null;

    return featureFlags?.[flagKey] === true;
}

function readNodePreviewInspectorFlagOnce() {
    const startup = window.__CLEARVISION_STARTUP__;
    const featureFlags = startup && typeof startup === 'object' &&
        startup.featureFlags && typeof startup.featureFlags === 'object'
        ? startup.featureFlags
        : null;

    return featureFlags?.[NODE_PREVIEW_INSPECTOR_FLAG_KEY] === true;
}

function readPropertyPanelCapabilityFlagOnce() {
    return readStartupFeatureFlagOnce(PROPERTY_PANEL_CAPABILITY_FLAG_KEY);
}

function readPreviewPanelCapabilityFlagOnce() {
    return readStartupFeatureFlagOnce(PREVIEW_PANEL_CAPABILITY_FLAG_KEY);
}

function readGlobalVariablesCapabilityFlagOnce() {
    return readStartupFeatureFlagOnce(GLOBAL_VARIABLES_CAPABILITY_FLAG_KEY);
}

function readProjectPageCapabilityFlagOnce() {
    return readStartupFeatureFlagOnce(PROJECT_PAGE_CAPABILITY_FLAG_KEY);
}

function readResultsReviewCapabilityFlagOnce() {
    return readStartupFeatureFlagOnce(RESULTS_REVIEW_CAPABILITY_FLAG_KEY);
}

const NODE_PREVIEW_INSPECTOR_ENABLED = readNodePreviewInspectorFlagOnce();
const PROPERTY_PANEL_CAPABILITY_ENABLED = readPropertyPanelCapabilityFlagOnce();
const PREVIEW_PANEL_CAPABILITY_ENABLED = readPreviewPanelCapabilityFlagOnce();
const GLOBAL_VARIABLES_CAPABILITY_ENABLED = readGlobalVariablesCapabilityFlagOnce();
const PROJECT_PAGE_CAPABILITY_ENABLED = readProjectPageCapabilityFlagOnce();
const RESULTS_REVIEW_CAPABILITY_ENABLED = readResultsReviewCapabilityFlagOnce();

// 全局状态
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

// Encoding cleanup: previous comment text was unreadable.
let imageViewer = null;
let operatorLibraryPanel = null;
let operatorPaletteShell = null;
let flowCanvas = null;
let flowEditorInteraction = null;
let finalDecisionPanel = null;
let propertyPanel = null;
let propertyPanelOwner = null;
let propertyPanelCapabilityAdapter = null;
let previewPanelCapabilityOwner = null;
let previewPanelCapabilityAdapter = null;
let globalVariablesCapabilityAdapter = null;
let projectPageCapabilityAdapter = null;
let resultsReviewCapabilityAdapter = null;
let propertySidebarController = null;
let nodePreviewCoordinator = null;
let nodePreviewOverlay = null;
let nodePreviewInspector = null;
let nodePreviewSelectionStore = null;
let nodePreviewProjectId = null;
let projectView = null;
let resultPanel = null;
let inspectionPanel = null;
let stationMonitorView = null;
let aiPanel = null;
let globalVariablePanel = null;
let settingsView = null;
let viewManager = null;
let toolbarCommandDisposer = null;
let aiGenerationController = null;
let appInitialized = false;
let appBootstrapPromise = null;
let statusBarStarted = false;
let themeUpdateInFlight = false;
let projectFlowSyncSuppressionDepth = 0;
let studioPerformanceGuardsInitialized = false;
let activeSubgraphNodeId = null;
let subgraphBreadcrumbBound = false;

let projectViewModulePromise = null;
let resultPanelModulePromise = null;
let inspectionPanelModulePromise = null;
let stationMonitorModulePromise = null;
let globalVariablePanelModulePromise = null;
let settingsViewModulePromise = null;
let legacyPropertyPanelModulePromise = null;
let nodePreviewInspectorModulePromise = null;
let nodePreviewOverlayModulePromise = null;
let nodePreviewSelectionStoreModulePromise = null;
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

// Encoding cleanup: previous comment text was unreadable.
let autoSaveInterval = null;
const AUTO_SAVE_DELAY = 5 * 60 * 1000;
const PROJECT_FLOW_SYNC_DEBOUNCE_MS = 250;
const PROJECT_FLOW_SYNC_IDLE_TIMEOUT_MS = 1500;
const promptedLocalDraftKeys = new Set();
let lastLocalDraftBackupSignature = null;
let pendingProjectFlowSyncTimer = null;
let pendingProjectFlowSyncIdleCancel = null;
let pendingProjectFlowSyncProjectId = null;
let pendingProjectFlowSyncRevision = null;
let pendingProjectFlowSyncReason = null;

function getAuthenticatedUserId() {
    const value = getCurrentUser()?.userId ?? getCurrentUser()?.id ?? null;
    if (typeof value !== 'string') {
        return null;
    }

    const normalized = value.trim();
    return normalized || null;
}

function resetLocalDraftSessionState() {
    promptedLocalDraftKeys.clear();
    lastLocalDraftBackupSignature = null;
}

function getLocalDraftBackupSignature(project, flow, userId = getAuthenticatedUserId()) {
    if (!userId) {
        return null;
    }

    const projectId = project?.id || '';
    const modifiedAt = project?.modifiedAt || project?.ModifiedAt || '';
    const flowRevision = flow?.flowRevision ?? flow?.FlowRevision ?? '';
    return `${userId}:${projectId}:${modifiedAt}:${flowRevision}`;
}

function writeLocalDraftBackup(project, flow, source = 'timer') {
    if (!project || !flow) {
        return null;
    }

    const backup = localDraftStorage.write(project, flow, {
        source,
        nodeCount: getFlowNodeCount(flow)
    });
    if (!backup) {
        return null;
    }

    lastLocalDraftBackupSignature = getLocalDraftBackupSignature(project, flow, backup.userId);
    return backup;
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

function loadGlobalVariablePanelModule() {
    if (!globalVariablePanelModulePromise) {
        globalVariablePanelModulePromise = import('./features/global-variables/globalVariablePanel.js');
    }

    return globalVariablePanelModulePromise;
}

function loadSettingsViewModule() {
    if (!settingsViewModulePromise) {
        settingsViewModulePromise = import('./features/settings/settingsView.js');
    }

    return settingsViewModulePromise;
}

function loadLegacyPropertyPanelModule() {
    if (!legacyPropertyPanelModulePromise) {
        legacyPropertyPanelModulePromise = import('./features/flow-editor/propertyPanel.js');
    }

    return legacyPropertyPanelModulePromise;
}

function loadNodePreviewInspectorModule() {
    if (!nodePreviewInspectorModulePromise) {
        nodePreviewInspectorModulePromise = import('./features/flow-editor/nodePreviewInspector.js');
    }

    return nodePreviewInspectorModulePromise;
}

function loadNodePreviewOverlayModule() {
    if (!nodePreviewOverlayModulePromise) {
        nodePreviewOverlayModulePromise = import('./features/flow-editor/nodePreviewOverlay.js');
    }

    return nodePreviewOverlayModulePromise;
}

function loadNodePreviewSelectionStoreModule() {
    if (!nodePreviewSelectionStoreModulePromise) {
        nodePreviewSelectionStoreModulePromise = import('./features/flow-editor/nodePreviewSelectionStore.js');
    }

    return nodePreviewSelectionStoreModulePromise;
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

function canEditProject() {
    return PermissionGuard.canEdit();
}

function canExportRuntimePackage() {
    return PermissionGuard.canManageUsers();
}

function showProjectEditPermissionHint(action = '执行该操作') {
    showToast(`${action}需要工程师或管理员权限。`, 'warning');
}

function showRuntimePackageExportPermissionHint() {
    showToast('导出运行包需要管理员权限。', 'warning');
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
            restoreInspectionImageViewer,
            ensureResultPanel,
            loadInspectionHistory,
            ensureStationMonitorView,
            ensureProjectView,
            ensureAiPanel,
            ensureSettingsView,
            getSettingsView: () => settingsView
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

function isSubgraphHostNode(node) {
    return String(node?.type || '').toLowerCase() === 'foreach';
}

function bindSubgraphBreadcrumb() {
    if (subgraphBreadcrumbBound) {
        return;
    }

    const rootLink = document.getElementById('breadcrumb-root');
    const exitButton = document.getElementById('btn-exit-subgraph');
    const exitSubgraph = () => closeSubgraphBreadcrumb();

    rootLink?.addEventListener('click', exitSubgraph);
    exitButton?.addEventListener('click', exitSubgraph);
    subgraphBreadcrumbBound = true;
}

function openSubgraphBreadcrumb(node) {
    if (!isSubgraphHostNode(node)) {
        return false;
    }

    activeSubgraphNodeId = node.id;
    const breadcrumb = document.getElementById('subgraph-breadcrumb');
    const current = document.getElementById('breadcrumb-current');
    if (current) {
        current.textContent = node.title || node.displayName || node.type || 'ForEach';
    }
    breadcrumb?.classList.remove('hidden');
    return true;
}

function closeSubgraphBreadcrumb() {
    activeSubgraphNodeId = null;
    document.getElementById('subgraph-breadcrumb')?.classList.add('hidden');
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

    const backup = localDraftStorage.read(project.id);
    const promptKey = `${backup?.userId || ''}:${backup?.projectId || ''}:${backup?.timestamp || ''}`;
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

function scheduleIdleWork(callback, timeout = PROJECT_FLOW_SYNC_IDLE_TIMEOUT_MS) {
    if (typeof window.requestIdleCallback === 'function') {
        const handle = window.requestIdleCallback(callback, { timeout });
        return () => window.cancelIdleCallback?.(handle);
    }

    const handle = window.setTimeout(callback, 0);
    return () => window.clearTimeout(handle);
}

function clearPendingProjectFlowSyncSchedule() {
    if (pendingProjectFlowSyncTimer !== null) {
        window.clearTimeout(pendingProjectFlowSyncTimer);
        pendingProjectFlowSyncTimer = null;
    }

    if (pendingProjectFlowSyncIdleCancel) {
        pendingProjectFlowSyncIdleCancel();
        pendingProjectFlowSyncIdleCancel = null;
    }
}

function resetPendingProjectFlowSync() {
    clearPendingProjectFlowSyncSchedule();
    pendingProjectFlowSyncProjectId = null;
    pendingProjectFlowSyncRevision = null;
    pendingProjectFlowSyncReason = null;
}

function runPendingProjectFlowSyncWhenIdle() {
    pendingProjectFlowSyncTimer = null;
    pendingProjectFlowSyncIdleCancel = scheduleIdleWork(() => {
        pendingProjectFlowSyncIdleCancel = null;
        flushPendingProjectFlowSync();
    });
}

function scheduleProjectFlowSyncFromCanvas(payload = {}) {
    if (projectFlowSyncSuppressionDepth > 0) {
        return;
    }

    const project = getCurrentProject();
    if (!project || !flowCanvas || typeof flowCanvas.serialize !== 'function') {
        return;
    }

    pendingProjectFlowSyncProjectId = project.id || null;
    pendingProjectFlowSyncRevision = payload.flowRevision ?? flowCanvas.getFlowRevision?.() ?? null;
    pendingProjectFlowSyncReason = payload.reason || 'structure-change';
    projectManager.markFlowDirty?.();

    clearPendingProjectFlowSyncSchedule();
    pendingProjectFlowSyncTimer = window.setTimeout(
        runPendingProjectFlowSyncWhenIdle,
        PROJECT_FLOW_SYNC_DEBOUNCE_MS);
}

function flushPendingProjectFlowSync() {
    if (pendingProjectFlowSyncProjectId === null && pendingProjectFlowSyncRevision === null) {
        return null;
    }

    const expectedProjectId = pendingProjectFlowSyncProjectId;
    const reason = pendingProjectFlowSyncReason || 'flush';
    clearPendingProjectFlowSyncSchedule();
    return syncCurrentProjectFlowFromCanvas({
        expectedProjectId,
        force: true,
        reason
    });
}

function initializeProjectFlowCanvasSync() {
    if (!flowCanvas || typeof flowCanvas.subscribeStructureState !== 'function') {
        return;
    }

    const unsubscribe = flowCanvas.subscribeStructureState((payload = {}) => {
        if (payload.reason === 'initial') {
            return;
        }

        scheduleProjectFlowSyncFromCanvas(payload);
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

function getPreviewValue(preview) {
    if (!preview || typeof preview !== 'object') {
        return null;
    }

    if (Object.prototype.hasOwnProperty.call(preview, 'value')) {
        return preview.value;
    }

    if (Object.prototype.hasOwnProperty.call(preview, 'Value')) {
        return preview.Value;
    }

    return null;
}

function normalizeHistoryImageUrl(result) {
    const reference = result?.imageReference ?? result?.ImageReference;
    if (typeof reference === 'string' && reference.trim().length > 0) {
        return httpClient.buildRequestUrl(reference);
    }

    return null;
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

function getLatestInspectionImageBase64() {
    return serviceRegistry.get('lastInspectionImageBase64')
        || inspectionController.getLastResultImageBase64?.()
        || getInlineResultImageBase64(serviceRegistry.get('lastInspectionResult'))
        || getInlineResultImageBase64(inspectionController.getLastResult?.());
}

function clearStudioPreviewInputFrame(message = '') {
    const frame = serviceRegistry.get('studioPreviewInputFrame');
    if (!frame) {
        return;
    }

    serviceRegistry.unregister('studioPreviewInputFrame', frame);
    if (message) {
        showToast(message, 'warning');
    }
}

function getStudioPreviewInputFrame(targetNodeId = null) {
    const frame = serviceRegistry.get('studioPreviewInputFrame');
    if (!frame?.imageBase64) {
        return null;
    }

    const currentProjectId = getCurrentProject()?.id || null;
    const sourceNode = flowCanvas?.nodes?.get?.(frame.sourceNodeId) || null;
    const resolution = resolveCameraPreviewInputFrame({
        frame,
        currentProjectId,
        sourceNode,
        targetNodeId,
        connections: flowCanvas?.connections || []
    });
    if (resolution.shouldInvalidate) {
        clearStudioPreviewInputFrame(resolution.message);
    }

    return resolution.frame;
}

function getStudioPreviewInputImageSource(targetNodeId = null) {
    const imageBase64 = getStudioPreviewInputFrame(targetNodeId)?.imageBase64 || null;
    return imageBase64 ? `data:image/png;base64,${imageBase64}` : null;
}

function getLatestInspectionImageSource() {
    const latestBlob = inspectionController.getLastResultImageBlob?.();
    if (latestBlob) {
        return latestBlob;
    }

    const latestBase64 = getLatestInspectionImageBase64();
    if (latestBase64) {
        return `data:image/png;base64,${latestBase64}`;
    }

    return null;
}

function getLatestInspectionImageUrl() {
    return serviceRegistry.get('lastInspectionImageUrl')
        || inspectionController.getLastResultImageUrl?.()
        || getResultImageUrl(serviceRegistry.get('lastInspectionResult'))
        || getResultImageUrl(inspectionController.getLastResult?.());
}

async function getLatestInspectionPreviewInput(targetNodeId = null) {
    const studioFrame = getStudioPreviewInputFrame(targetNodeId);
    if (studioFrame?.imageBase64) {
        return {
            imageBase64: studioFrame.imageBase64,
            sourceNodeId: studioFrame.sourceNodeId,
            frameId: studioFrame.frameId
        };
    }

    const latestImage = getLatestInspectionImageBase64();
    if (latestImage) {
        return { imageBase64: latestImage, sourceNodeId: null, frameId: null };
    }

    const inspectionResult = serviceRegistry.get('lastInspectionResult') || inspectionController.getLastResult?.();
    const inlineImage = resolvePreviewInputImageBase64(inspectionResult);
    if (inlineImage) {
        return { imageBase64: inlineImage, sourceNodeId: null, frameId: null };
    }

    return {
        imageBase64: await loadImageUrlAsBase64(getLatestInspectionImageUrl()),
        sourceNodeId: null,
        frameId: null
    };
}

async function getLatestInspectionInputImageBase64(targetNodeId = null) {
    return (await getLatestInspectionPreviewInput(targetNodeId)).imageBase64;
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

function applyInspectionImageLoadState(state) {
    const currentView = getCurrentView();
    const viewer = currentView === 'inspection'
        ? serviceRegistry.get('inspectionImageViewer')
        : currentView === 'image'
            ? serviceRegistry.get('imageViewer')
            : null;
    if (!viewer) {
        return;
    }

    if (state?.status === 'loading') {
        viewer.clearImage?.('正在加载最新检测图像');
    } else if (state?.status === 'error') {
        viewer.clearImage?.(state.message || '检测图像加载失败', {
            retryLabel: '重试加载',
            onRetry: () => void inspectionController.retryLastResultImage?.()
        });
    } else if (state?.status === 'empty') {
        viewer.clearImage?.(state.message || '检测结果未提供可展示的图像');
    }
}

function restoreInspectionImageViewer(viewer) {
    if (!viewer) {
        return;
    }

    const lastImageSource = getLatestInspectionImageSource();
    if (lastImageSource) {
        loadViewerImageSilently(viewer, lastImageSource);
        return;
    }

    const imageState = inspectionController.getLastResultImageState?.();
    if (imageState?.status === 'loading') {
        viewer.clearImage?.('正在加载最新检测图像');
        void inspectionController.ensureLastResultImageLoaded?.();
    } else if (imageState?.status === 'error') {
        viewer.clearImage?.(imageState.message || '检测图像加载失败', {
            retryLabel: '重试加载',
            onRetry: () => void inspectionController.retryLastResultImage?.()
        });
    } else if (imageState?.status === 'empty') {
        viewer.clearImage?.(imageState.message || '检测结果未提供可展示的图像');
    }
}

function buildResultDefects(result) {
    return buildBoundedResultDefects(result);

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
        type: `Target ${index + 1}`,
        description: 'Result did not include defect details.'
        /*
        type: `目标 ${index + 1}`,
        description: '实时结果未携带缺陷详情'
        */
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

function normalizeStationResultStatus(outcome, inspectionStatus = null) {
    const normalizedOutcome = String(outcome ?? '').trim().toUpperCase();
    if (normalizedOutcome === 'OK' || normalizedOutcome === '0') {
        return 'OK';
    }

    if (normalizedOutcome === 'NG' || normalizedOutcome === '1') {
        return 'NG';
    }

    if (normalizedOutcome === 'ERROR' || normalizedOutcome === '2') {
        return 'Error';
    }

    if (normalizedOutcome === 'CANCELED' || normalizedOutcome === 'CANCELLED' || normalizedOutcome === '3') {
        return 'Error';
    }

    return normalizeInspectionStatus(inspectionStatus || outcome || 'Unknown');
}

function normalizeStationTraceResultRecord(result) {
    if (!result || typeof result !== 'object') {
        return null;
    }

    const stationId = result.stationId ?? result.StationId ?? '';
    const sequenceId = result.sequenceId ?? result.SequenceId ?? 0;
    const diagnosticCode = result.diagnosticCode ?? result.DiagnosticCode ?? '';
    const diagnosticMessage = result.diagnosticMessage ?? result.DiagnosticMessage ?? '';
    const status = normalizeStationResultStatus(
        result.outcome ?? result.Outcome,
        result.inspectionStatus ?? result.InspectionStatus);
    const primaryOutputsPreview = result.primaryOutputsPreview ?? result.PrimaryOutputsPreview ?? {};
    const outputData = {
        ...(primaryOutputsPreview && typeof primaryOutputsPreview === 'object' ? primaryOutputsPreview : {}),
        stationId,
        sequenceId,
        runId: result.runId ?? result.RunId ?? '',
        packageId: result.packageId ?? result.PackageId ?? '',
        packageName: result.packageName ?? result.PackageName ?? '',
        imageId: result.imageId ?? result.ImageId ?? '',
        diagnosticCode,
        diagnosticMessage
    };
    const defects = status === 'OK'
        ? []
        : [{
            type: diagnosticCode || status,
            description: diagnosticMessage || diagnosticCode || status
        }];

    return {
        id: `${stationId || 'station'}:${sequenceId}:${result.messageId ?? result.MessageId ?? ''}`,
        projectId: null,
        stationId,
        status,
        defects,
        defectCount: defects.length,
        processingTime: result.executionTimeMs ?? result.ExecutionTimeMs ?? 0,
        processingTimeMs: result.executionTimeMs ?? result.ExecutionTimeMs ?? 0,
        timestamp: result.completedAtUtc ?? result.CompletedAtUtc ?? result.createdAtUtc ?? result.CreatedAtUtc ?? new Date().toISOString(),
        confidenceScore: null,
        imageId: null,
        outputData,
        analysisData: null,
        errorMessage: status === 'Error' ? diagnosticMessage : ''
    };
}

function normalizeInspectionResultRecord(result, fallbackProjectId = null) {
    if (!result || typeof result !== 'object') {
        return null;
    }

    const normalized = { ...result };
    normalized.id = normalized.id ?? normalized.Id;
    normalized.projectId = normalized.projectId ?? normalized.ProjectId ?? fallbackProjectId ?? null;
    normalized.status = normalizeInspectionStatus(normalized.status ?? normalized.Status);
    const actualDefectCount = getResultDefectCount(normalized);
    normalized.defects = buildBoundedResultDefects(normalized);
    normalized.defectCount = normalized.defectCount
        ?? normalized.DefectCount
        ?? actualDefectCount
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
    normalized.imageReference = normalized.imageReference ?? normalized.ImageReference ?? null;
    normalized.imageUrl = normalized.imageUrl || normalizeHistoryImageUrl(normalized);
    normalized.hasImage = normalized.hasImage ?? normalized.HasImage ?? !!normalized.imageId;
    normalized.imageMissing = normalized.imageMissing ?? normalized.ImageMissing ?? false;
    normalized.imageMissingMessage = normalized.imageMissingMessage
        ?? normalized.ImageMissingMessage
        ?? (normalized.imageMissing ? '图像文件不存在或已清理' : '');
    normalized.imageData = getInlineResultImageBase64(normalized);
    normalized.ImageData = null;
    normalized.outputImage = null;
    normalized.OutputImage = null;
    normalized.outputImageBase64 = null;
    normalized.OutputImageBase64 = null;
    normalized.resultImageBase64 = null;
    normalized.ResultImageBase64 = null;
    normalized.outputDataPreview = normalized.outputDataPreview ?? normalized.OutputDataPreview ?? null;
    normalized.analysisDataPreview = normalized.analysisDataPreview ?? normalized.AnalysisDataPreview ?? null;
    normalized.outputData = getPreviewValue(normalized.outputDataPreview) || normalizeOutputData(normalized);
    normalized.analysisData = getPreviewValue(normalized.analysisDataPreview) || normalizeAnalysisData(normalized);
    normalized.hasOutputData = normalized.hasOutputData ?? normalized.HasOutputData ?? Object.keys(normalized.outputData || {}).length > 0;
    normalized.hasAnalysisData = normalized.hasAnalysisData ?? normalized.HasAnalysisData ?? !!normalized.analysisData;
    normalized.flowVersionHash = normalized.flowVersionHash
        ?? normalized.FlowVersionHash
        ?? normalized.traceability?.flowVersionHash
        ?? normalized.Traceability?.FlowVersionHash
        ?? null;
    normalized.calibrationBundleId = normalized.calibrationBundleId
        ?? normalized.CalibrationBundleId
        ?? normalized.traceability?.calibrationBundleId
        ?? normalized.Traceability?.CalibrationBundleId
        ?? null;
    normalized.sessionId = normalized.sessionId
        ?? normalized.SessionId
        ?? normalized.runId
        ?? normalized.RunId
        ?? normalized.traceability?.sessionId
        ?? normalized.Traceability?.SessionId
        ?? null;
    normalized.runId = normalized.runId ?? normalized.RunId ?? normalized.sessionId;
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
            isLibrarySelection: true,
            title: operatorData.title || operatorData.displayName || operatorData.type,
            parameters: operatorData.parameters ? operatorData.parameters.map(p => ({ ...p })) : []
        };
        if (isPropertyPanelCapabilityEnabled() || isPreviewPanelCapabilityEnabled()) {
            const flowCanvasAdapter = serviceRegistry.get('flowCanvasAdapter');
            if (flowCanvasAdapter?.selectNode) {
                flowCanvasAdapter.selectNode(null);
            } else if (flowCanvas) {
                flowCanvas.selectedNode = null;
                flowCanvas.selectedConnection = null;
                flowCanvas.markSelectionChanged?.('operator-library-selection');
                flowCanvas.onNodeSelected?.(null);
                flowCanvas.render?.();
            }
        }
        setSelectedOperator(operatorCopy);
        if (isPropertyPanelCapabilityEnabled()) {
            propertyPanel?.setOperator?.(operatorCopy);
        }
    };

    initializeOperatorPaletteShell();
    debugLogger.debug('[App] 算子库面板初始化完成');
}

function initializeOperatorPaletteShell() {
    const rail = document.getElementById('operator-rail');
    const flyout = document.getElementById('operator-group-flyout');
    if (!rail || !flyout) {
        debugLogger.warn('[App] Operator rail/flyout host not found');
        return;
    }

    operatorPaletteShell?.dispose?.();
    operatorPaletteShell = new OperatorPaletteShell({
        rail,
        flyout,
        libraryPanel: operatorLibraryPanel,
        onOperatorDragStart: (operatorData) => {
            debugLogger.debug('[App] 从算子组拖拽算子:', operatorData.type);
            operatorLibraryPanel?.onOperatorDragStart?.(operatorData);
        },
        onOperatorAdd: (operatorData) => {
            addOperatorFromPalette(operatorData);
        }
    });
    serviceRegistry.register('operatorPaletteShell', operatorPaletteShell);
}

function addOperatorFromPalette(operatorData) {
    if (!operatorData?.type) {
        showToast('算子数据不完整，无法添加', 'error');
        return null;
    }

    if (!flowCanvas || !flowEditorInteraction) {
        const operatorCopy = {
            ...operatorData,
            isLibrarySelection: true,
            title: operatorData.title || operatorData.displayName || operatorData.type,
            parameters: operatorData.parameters ? operatorData.parameters.map(parameter => ({ ...parameter })) : []
        };
        setSelectedOperator(operatorCopy);
        propertyPanel?.setOperator?.(operatorCopy);
        showToast('画布尚未就绪，已显示算子属性', 'warning');
        return null;
    }

    const rect = flowCanvas.canvas.getBoundingClientRect();
    const scale = Number.isFinite(flowCanvas.scale) && flowCanvas.scale > 0 ? flowCanvas.scale : 1;
    const offset = flowCanvas.offset || { x: 0, y: 0 };
    const existingCount = flowCanvas.nodes?.size || 0;
    const stagger = (existingCount % 6) * 28;
    const x = (rect.width * 0.5) / scale + offset.x + stagger;
    const y = (rect.height * 0.42) / scale + offset.y + stagger;
    const operatorTitle = operatorData.displayName || operatorData.name || operatorData.title || operatorData.type;
    const node = flowEditorInteraction.addOperatorNode(operatorData.type, x, y, operatorData);

    flowEditorInteraction.saveState?.();
    const flowCanvasAdapter = serviceRegistry.get('flowCanvasAdapter');
    if (node?.id && flowCanvasAdapter?.selectNode) {
        flowCanvasAdapter.selectNode(node.id);
    } else if (node?.id) {
        flowCanvas.selectedNode = node.id;
        flowCanvas.selectedConnection = null;
        flowCanvas.markSelectionChanged?.('operator-palette-add');
        flowCanvas.onNodeSelected?.(node);
        flowCanvas.render?.();
    }
    syncCurrentProjectFlowFromCanvas();
    showToast(`已添加算子: ${operatorTitle}`, 'success');
    return node;
}

function initializeImageViewer() {
    const container = document.getElementById('image-viewer');
    if (!container) {
        console.error('[App] 找不到图像查看器容器');
        return;
    }

    const existingImageViewer = serviceRegistry.get('imageViewer');
    if (existingImageViewer) {
        if (typeof existingImageViewer.isAttachedTo === 'function' && existingImageViewer.isAttachedTo(container)) {
            imageViewer = existingImageViewer;
            requestAnimationFrame(() => {
                existingImageViewer.imageCanvas?.resize?.();
            });
            return;
        }

        if (typeof existingImageViewer.destroy === 'function') {
            existingImageViewer.destroy();
        } else {
            existingImageViewer.dispose?.();
        }
        serviceRegistry.unregister('imageViewer', existingImageViewer);
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
        if (typeof existingInspectionImageViewer.isAttachedTo === 'function' && existingInspectionImageViewer.isAttachedTo(container)) {
            requestAnimationFrame(() => {
                existingInspectionImageViewer.imageCanvas?.resize();
            });
            restoreInspectionImageViewer(existingInspectionImageViewer);
            return;
        }

        if (typeof existingInspectionImageViewer.destroy === 'function') {
            existingInspectionImageViewer.destroy();
        } else {
            existingInspectionImageViewer.dispose?.();
        }
        serviceRegistry.unregister('inspectionImageViewer', existingInspectionImageViewer);
    }

    try {
        const inspectionImageViewer = new ImageViewerComponent('inspection-image-area');
        serviceRegistry.register('inspectionImageViewer', inspectionImageViewer);
        restoreInspectionImageViewer(inspectionImageViewer);

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

function isNodePreviewInspectorEnabled() {
    return NODE_PREVIEW_INSPECTOR_ENABLED;
}

function isPreviewPanelCapabilityEnabled() {
    return PREVIEW_PANEL_CAPABILITY_ENABLED;
}

function disposeLegacyNodePreviewSurfaces() {
    nodePreviewOverlay?.destroy?.();
    nodePreviewInspector?.destroy?.();
    nodePreviewSelectionStore?.clear?.();
    serviceRegistry.unregister('nodePreviewOverlay', nodePreviewOverlay);
    serviceRegistry.unregister('nodePreviewInspector', nodePreviewInspector);
    serviceRegistry.unregister('nodePreviewSelectionStore', nodePreviewSelectionStore);
    nodePreviewOverlay = null;
    nodePreviewInspector = null;
    nodePreviewSelectionStore = null;
}

async function initializeNodePreviewExperience() {
    if (!flowCanvas) {
        return;
    }

    if (!nodePreviewCoordinator) {
        nodePreviewCoordinator = new NodePreviewCoordinator({
            getProjectId: () => getCurrentProject()?.id || null,
            getFlowRevision: () => flowCanvas.getFlowRevision?.() || 0,
            getNodeById: nodeId => flowCanvas.nodes.get(nodeId) || null,
            getOperatorMetadata: type => findOperatorDefinition(type),
            getInputImageBase64: () => getLatestInspectionInputImageBase64(),
            getInputImageContext: node => getLatestInspectionPreviewInput(node?.id || null),
            previewExecutor: (nodeId, options) => inspectionController.previewNode(nodeId, options),
            subscribeStructureState: listener => flowCanvas.subscribeStructureState(state => {
                getStudioPreviewInputFrame();
                listener(state);
            }),
            debounceMs: 500
        });
        serviceRegistry.register('nodePreviewCoordinator', nodePreviewCoordinator);
    }

    if (isPreviewPanelCapabilityEnabled()) {
        disposeLegacyNodePreviewSurfaces();
        return;
    }

    const inspectorEnabled = isNodePreviewInspectorEnabled();
    if (inspectorEnabled) {
        if (!nodePreviewSelectionStore) {
            const { createNodePreviewSelectionStore } = await loadNodePreviewSelectionStoreModule();
            nodePreviewSelectionStore = createNodePreviewSelectionStore();
            serviceRegistry.register('nodePreviewSelectionStore', nodePreviewSelectionStore);
        }

        if (!nodePreviewInspector) {
            const container = document.querySelector('.flow-editor-container');
            if (container) {
                const { default: NodePreviewInspector } = await loadNodePreviewInspectorModule();
                nodePreviewInspector = new NodePreviewInspector(container, flowCanvas, nodePreviewCoordinator, {
                    selectionStore: nodePreviewSelectionStore,
                    onOpenImage: openImageViewerFromPreview,
                    onBindGlobalVariable: descriptor => {
                        const panel = globalVariablePanel || serviceRegistry.get('globalVariablePanel');
                        void panel?.bindPreviewField?.(descriptor);
                    }
                });
                serviceRegistry.register('nodePreviewInspector', nodePreviewInspector);
            }
        }
        return;
    }

    if (!nodePreviewOverlay) {
        const container = document.querySelector('.flow-editor-container');
        if (container) {
            const { default: NodePreviewOverlay } = await loadNodePreviewOverlayModule();
            nodePreviewOverlay = new NodePreviewOverlay(container, flowCanvas, nodePreviewCoordinator, {
                onOpenImage: openImageViewerFromPreview
            });
            serviceRegistry.register('nodePreviewOverlay', nodePreviewOverlay);
        }
    }
}

function initializeInspectionController() {
    inspectionController.onInspectionImageState((state) => {
        if (state?.status === 'loading' || state?.status === 'empty') {
            serviceRegistry.unregister('lastInspectionImageBlob');
        }
        applyInspectionImageLoadState(state);
    });

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

        const inlineResultImage = getInlineResultImageBase64(normalizedResult);
        const resultImageUrl = inlineResultImage ? null : getResultImageUrl(normalizedResult);
        const lightweightResult = createLightweightInspectionResult(normalizedResult);
        eventBus.emit('inspection:result', lightweightResult);
        serviceRegistry.register('lastInspectionResult', lightweightResult);
        if (inlineResultImage) {
            serviceRegistry.register('lastInspectionImageBase64', inlineResultImage);
            serviceRegistry.unregister('lastInspectionImageUrl');
            serviceRegistry.unregister('lastInspectionImageBlob');
        } else if (resultImageUrl) {
            serviceRegistry.unregister('lastInspectionImageBase64');
            serviceRegistry.register('lastInspectionImageUrl', resultImageUrl);
            serviceRegistry.unregister('lastInspectionImageBlob');
        } else {
            serviceRegistry.unregister('lastInspectionImageBase64');
            serviceRegistry.unregister('lastInspectionImageUrl');
            serviceRegistry.unregister('lastInspectionImageBlob');
        }
        window._lastInspectionResult = lightweightResult;

        const isRealtimeResult = inspectionController.getState?.().isRealtime === true;

        if (getCurrentView() === 'inspection') {
            const inspectionImageViewerService = serviceRegistry.get('inspectionImageViewer');
            if (inlineResultImage && inspectionImageViewerService) {
                loadViewerImageSilently(inspectionImageViewerService, `data:image/png;base64,${inlineResultImage}`);
            }

            updateInspectionResultsPanel(normalizedResult);
        }

        if (resultPanel && isResultPanelVisible()) {
            resultPanel.setProjectContext(currentProjectId);
            const normalizedDefects = buildBoundedResultDefects(normalizedResult);
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
                imageData: inlineResultImage,
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

function isPropertyPanelCapabilityEnabled() {
    return PROPERTY_PANEL_CAPABILITY_ENABLED;
}

function shouldPreviewPanelCapabilityOwnSidebarPreview() {
    return isPreviewPanelCapabilityEnabled();
}

function shouldLegacyPropertyPanelOwnSidebarPreview() {
    return !isPropertyPanelCapabilityEnabled() && !isPreviewPanelCapabilityEnabled();
}

function shouldHideUnownedSidebarPreviewHost() {
    return isPropertyPanelCapabilityEnabled() && !isPreviewPanelCapabilityEnabled();
}

function isGlobalVariablesCapabilityEnabled() {
    return GLOBAL_VARIABLES_CAPABILITY_ENABLED;
}

function isProjectPageCapabilityEnabled() {
    return PROJECT_PAGE_CAPABILITY_ENABLED;
}

function isResultsReviewCapabilityEnabled() {
    return RESULTS_REVIEW_CAPABILITY_ENABLED;
}

function disposePreviewPanelCapabilityOwner() {
    previewPanelCapabilityOwner?.dispose?.();
    serviceRegistry.unregister('previewPanelCapabilityOwner', previewPanelCapabilityOwner);
    serviceRegistry.unregister('previewPanelCapabilityAdapter', previewPanelCapabilityAdapter);
    previewPanelCapabilityOwner = null;
    previewPanelCapabilityAdapter = null;
}

function initializePreviewPanelCapability() {
    const hostPanel = document.querySelector('[data-preview-panel-host]');
    const container = document.getElementById('preview-panel');
    if (!container) {
        debugLogger.warn('[App] Preview Panel capability host not found');
        return;
    }

    disposePreviewPanelCapabilityOwner();

    if (shouldHideUnownedSidebarPreviewHost()) {
        hostPanel?.classList.add('hidden');
        container.innerHTML = '<p class="empty-text">预览面板未启用</p>';
        return;
    }

    if (!shouldPreviewPanelCapabilityOwnSidebarPreview()) {
        hostPanel?.classList.remove('hidden');
        return;
    }

    const flowCanvasAdapter = serviceRegistry.get('flowCanvasAdapter');
    previewPanelCapabilityAdapter = createPreviewPanelCapabilityAdapter({
        flowCanvasAdapter,
        previewCoordinator: nodePreviewCoordinator,
        getOperatorMetadata: type => findOperatorDefinition(type),
        getProjectId: () => getCurrentProject()?.id || null,
        getInputImageBase64: () => getLatestInspectionInputImageBase64(),
        onOpenPreviewImage: openImageViewerFromPreview
    });
    previewPanelCapabilityOwner = new PreviewPanelCapabilityOwner(container, {
        previewAdapter: previewPanelCapabilityAdapter,
        showToast
    });

    hostPanel?.classList.remove('hidden');
    serviceRegistry.register('previewPanelCapabilityAdapter', previewPanelCapabilityAdapter);
    serviceRegistry.register('previewPanelCapabilityOwner', previewPanelCapabilityOwner);
    debugLogger.debug('[App] Preview Panel capability owner 初始化完成');
}

function disposePropertyPanelOwner() {
    const owner = propertyPanelOwner || propertyPanel;
    const capabilityOwner = propertyPanelOwner?.panel || null;
    if (owner) {
        if (typeof owner.dispose === 'function') {
            owner.dispose();
        } else {
            owner.destroy?.();
        }
    }

    serviceRegistry.unregister('propertyPanelCapabilityOwner', capabilityOwner);
    serviceRegistry.unregister('propertyPanelCapabilityAdapter', propertyPanelCapabilityAdapter);
    serviceRegistry.unregister('propertyPanel', propertyPanel);
    propertyPanelOwner = null;
    propertyPanelCapabilityAdapter = null;
    propertyPanel = null;
}

async function createLegacyPropertyPanelOwner() {
    const { PropertyPanel } = await loadLegacyPropertyPanelModule();
    const ownsPreviewSidebar = shouldLegacyPropertyPanelOwnSidebarPreview() && !isNodePreviewInspectorEnabled();
    const flowCanvasAdapter = serviceRegistry.get('flowCanvasAdapter');
    propertyPanelCapabilityAdapter = flowCanvasAdapter && isPropertyPanelCapabilityEnabled()
        ? createPropertyPanelCapabilityAdapter({
            flowCanvasAdapter,
            getOperatorMetadata: type => findOperatorDefinition(type)
        })
        : null;
    const auxiliaryWorkbenchesEnabled = !isPreviewPanelCapabilityEnabled();
    const panel = new PropertyPanel('property-panel', {
        previewCoordinator: nodePreviewCoordinator,
        onOpenPreviewImage: openImageViewerFromPreview,
        previewResourcesEnabled: !isPreviewPanelCapabilityEnabled(),
        previewPanelEnabled: ownsPreviewSidebar,
        auxiliaryWorkbenchesEnabled,
        previewContainer: ownsPreviewSidebar
            ? document.getElementById('preview-panel')
            : null
    });
    let disposed = false;

    const syncSelectedCanvasEntity = (state = {}) => {
        if (disposed) {
            return;
        }

        if (state.selectedNodeId && propertyPanelCapabilityAdapter) {
            const operator = propertyPanelCapabilityAdapter.getSelectedOperatorSnapshot(state.selectedNodeId);
            if (!operator) {
                panel.clear();
                return;
            }
            debugLogger.debug('[App] 选中算子变化:', operator.title || operator.type);
            panel.setOperator(operator);
            return;
        }

        if (state.selectedConnectionId && propertyPanelCapabilityAdapter) {
            const connection = propertyPanelCapabilityAdapter.getSelectedConnectionSnapshot(state.selectedConnectionId);
            if (connection) {
                panel.setConnection?.(connection);
                return;
            }
        }

        panel.clear();
    };

    const unsubscribeCanvasSelection = propertyPanelCapabilityAdapter
        ? propertyPanelCapabilityAdapter.flowCanvasAdapter?.subscribeSelection?.(syncSelectedCanvasEntity)
        : null;

    const unsubscribeSelectedOperator = subscribeSelectedOperator((operator) => {
        if (disposed || (propertyPanelCapabilityAdapter && !operator?.isLibrarySelection)) {
            return;
        }

        if (operator) {
            debugLogger.debug('[App] 选中算子变化:', operator.title || operator.type);
            panel.setOperator(operator);
        } else {
            panel.clear();
        }
    });

    panel.onChange((values) => {
        if (disposed) {
            return;
        }

        debugLogger.debug('[App] 算子参数变更:', values);
        const operator = panel.currentOperator || getSelectedOperator();
        if (operator && flowCanvas) {
            const node = flowCanvas.nodes.get(operator.id);
            if (node) {
                node.parameters = operator.parameters;
                flowCanvas.markFlowStructureChanged?.('parameter-change');
                syncCurrentProjectFlowFromCanvas();
            }
        }
    });

    return {
        kind: 'legacy-property-panel',
        panel,
        dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            unsubscribeCanvasSelection?.();
            unsubscribeSelectedOperator?.();
            panel.destroy?.();
        }
    };
}

function createPropertyPanelCapabilityOwner() {
    const flowCanvasAdapter = serviceRegistry.get('flowCanvasAdapter');
    propertyPanelCapabilityAdapter = flowCanvasAdapter
        ? createPropertyPanelCapabilityAdapter({
            flowCanvasAdapter,
            getOperatorMetadata: type => findOperatorDefinition(type)
        })
        : null;

    if (!propertyPanelCapabilityAdapter) {
        debugLogger.warn('[App] Property Panel capability adapter not available; falling back to legacy owner');
        return null;
    }

    const owner = new PropertyPanelCapabilityOwner('property-panel', {
        propertyAdapter: propertyPanelCapabilityAdapter,
        previewCoordinator: nodePreviewCoordinator,
        previewResourcesEnabled: !isPreviewPanelCapabilityEnabled(),
        onOpenPreviewImage: openImageViewerFromPreview,
        onCapturePreviewInput: frame => {
            serviceRegistry.register('studioPreviewInputFrame', {
                ...frame,
                projectId: getCurrentProject()?.id || null
            });
        },
        getPreviewInputImageSource: nodeId => getStudioPreviewInputImageSource(nodeId),
        circleSearchV2ToolEnabled: readStartupFeatureFlagOnce('Studio:CircleSearchV2ToolEnabled'),
        nPointCalibrationWorkbenchEnabled: readStartupFeatureFlagOnce('Studio:NPointCalibrationWorkbenchEnabled'),
        showToast
    });

    return {
        kind: 'property-panel-capability',
        panel: owner,
        dispose() {
            owner.dispose?.();
        }
    };
}

async function initializePropertyPanel() {
    const container = document.getElementById('property-panel');
    if (!container) {
        console.error('[App] 找不到属性面板容器');
        return;
    }

    disposePropertyPanelOwner();

    propertyPanelOwner = isPropertyPanelCapabilityEnabled()
        ? createPropertyPanelCapabilityOwner()
        : null;
    if (!propertyPanelOwner) {
        propertyPanelOwner = await createLegacyPropertyPanelOwner();
    }
    propertyPanel = propertyPanelOwner.panel;

    if (propertyPanelCapabilityAdapter) {
        serviceRegistry.register('propertyPanelCapabilityAdapter', propertyPanelCapabilityAdapter);
    }

    if (isPropertyPanelCapabilityEnabled()) {
        serviceRegistry.register('propertyPanelCapabilityOwner', propertyPanelOwner.panel);
    }

    serviceRegistry.register('propertyPanel', propertyPanel);

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

function disposeGlobalVariablePanelOwner() {
    if (globalVariablePanel) {
        if (typeof globalVariablePanel.dispose === 'function') {
            globalVariablePanel.dispose();
        } else {
            globalVariablePanel.destroy?.();
        }
    }

    serviceRegistry.unregister('globalVariablesCapabilityOwner', globalVariablePanel);
    serviceRegistry.unregister('globalVariablesCapabilityAdapter', globalVariablesCapabilityAdapter);
    serviceRegistry.unregister('globalVariablePanel', globalVariablePanel);
    globalVariablePanel = null;
    globalVariablesCapabilityAdapter = null;
}

async function initializeGlobalVariablePanel() {
    const container = document.getElementById('global-variable-panel');
    if (!container) {
        return;
    }

    disposeGlobalVariablePanelOwner();

    if (isGlobalVariablesCapabilityEnabled()) {
        globalVariablesCapabilityAdapter = createGlobalVariablesCapabilityAdapter({
            projectManagerRef: projectManager,
            inspectionControllerRef: inspectionController
        });
        globalVariablePanel = new GlobalVariablesCapabilityOwner(container, {
            adapter: globalVariablesCapabilityAdapter,
            showToast
        });
        serviceRegistry.register('globalVariablesCapabilityAdapter', globalVariablesCapabilityAdapter);
        serviceRegistry.register('globalVariablesCapabilityOwner', globalVariablePanel);
        serviceRegistry.register('globalVariablePanel', globalVariablePanel);
        await globalVariablePanel.setProject(getCurrentProject());
        return;
    }

    const module = await loadGlobalVariablePanelModule();
    globalVariablePanel = new module.default('global-variable-panel');
    serviceRegistry.register('globalVariablePanel', globalVariablePanel);
    await globalVariablePanel.setProject(getCurrentProject());
}

async function loadInspectionHistory({
    pageIndex = 0,
    pageSize = resultPanel?.pageSize ?? 12,
    startTime = resultPanel?.getAnalyticsQueryParams?.().startTime,
    endTime = resultPanel?.getAnalyticsQueryParams?.().endTime,
    status = resultPanel?.getAnalyticsQueryParams?.().status,
    defectType = resultPanel?.getAnalyticsQueryParams?.().defectType,
    dataSource = resultPanel?.dataSource ?? 'inspection'
} = {}) {
    const project = getCurrentProject();
    if (dataSource === 'station' || !project) {
        return loadStationResultHistory({ pageIndex, pageSize, startTime, endTime, status, defectType });
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

async function loadInspectionHistoryDetail(result) {
    const project = getCurrentProject();
    const resultId = result?.id ?? result?.resultId ?? result?.Id ?? result?.ResultId;
    if (!project || !resultId) {
        throw new Error('缺少历史详情上下文');
    }

    const response = await httpClient.get(`/inspection/history/${project.id}/${resultId}`);
    return normalizeInspectionResultRecord({
        ...result,
        ...response,
        historyDetailLoaded: true
    }, project.id);
}

async function loadInspectionHistoryComparison({ left, right, leftId, rightId } = {}) {
    const project = getCurrentProject();
    const resolvedLeftId = leftId ?? left?.resultId ?? left?.id ?? left?.ResultId ?? left?.Id;
    const resolvedRightId = rightId ?? right?.resultId ?? right?.id ?? right?.ResultId ?? right?.Id;
    if (!project || !resolvedLeftId || !resolvedRightId) {
        throw new Error('缺少结果对比上下文');
    }

    return await httpClient.get(`/inspection/history/${project.id}/compare`, {
        leftId: resolvedLeftId,
        rightId: resolvedRightId
    });
}

async function loadInspectionPreviousSuccess(result, { limit = 50 } = {}) {
    const project = getCurrentProject();
    const resultId = result?.id ?? result?.resultId ?? result?.Id ?? result?.ResultId;
    if (!project || !resultId) {
        throw new Error('缺少失败前成功查询上下文');
    }

    return await httpClient.get(`/inspection/history/${project.id}/${resultId}/previous-success`, {
        limit
    });
}

async function exportInspectionEvidence(result) {
    const project = getCurrentProject();
    const resultId = result?.id ?? result?.resultId ?? result?.Id ?? result?.ResultId;
    if (!project || !resultId) {
        throw new Error('缺少证据导出上下文');
    }

    const response = await httpClient.getForBlob(`/inspection/history/${project.id}/${resultId}/evidence/export`);
    const disposition = response?.headers?.get?.('content-disposition') || '';
    const fileNameMatch = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(disposition);
    const filename = fileNameMatch
        ? decodeURIComponent(fileNameMatch[1].replace(/"/g, ''))
        : `inspection-evidence-${resultId}.json`;

    return {
        blob: response.blob,
        filename,
        sha256: response?.headers?.get?.('x-evidence-export-sha256') || null
    };
}

async function loadStationResultHistory({
    pageIndex = 0,
    pageSize = resultPanel?.pageSize ?? 12,
    startTime = resultPanel?.getAnalyticsQueryParams?.().startTime,
    endTime = resultPanel?.getAnalyticsQueryParams?.().endTime,
    status = resultPanel?.getAnalyticsQueryParams?.().status,
    defectType = resultPanel?.getAnalyticsQueryParams?.().defectType
} = {}) {
    if (!resultPanel) {
        return false;
    }

    try {
        debugLogger.debug('[App] 正在加载 Station 采集追溯数据...');
        const response = await httpClient.get('/stations/results', {
            pageIndex,
            pageSize,
            ...(startTime ? { from: startTime } : {}),
            ...(endTime ? { to: endTime } : {}),
            ...(status ? { status } : {}),
            ...(defectType ? { diagnosticCode: defectType } : {})
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

        resultPanel.dataSource = 'station';
        const dataSourceFilter = document.getElementById('filter-data-source');
        if (dataSourceFilter) {
            dataSourceFilter.value = 'station';
        }

        resultPanel.disconnectResultsStream?.();
        resultPanel.loadResults(
            results.map(normalizeStationTraceResultRecord).filter(Boolean),
            {
                totalCount,
                pageIndex: resolvedPageIndex,
                pageSize: resolvedPageSize,
                serverPaged: true
            });

        if (typeof resultPanel.loadServerAnalytics === 'function') {
            await resultPanel.loadServerAnalytics();
        }

        return true;
    } catch (error) {
        console.error('[App] 加载 Station 采集追溯数据失败:', error);
        return false;
    }
}

async function initializeFlowEditor() {
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
            if (typeof Blob !== 'undefined' && imageData instanceof Blob) {
                serviceRegistry.register('lastInspectionImageBlob', imageData);
                serviceRegistry.unregister('lastInspectionImageBase64');
            }

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
    await initializeNodePreviewExperience();
    bindSubgraphBreadcrumb();

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
                disabled: node.disabled === true,
                inputPorts: node.inputs || operatorDef?.inputPorts || [],
                outputPorts: node.outputs || operatorDef?.outputPorts || [],
                parameters: mergeParameters(operatorDef?.parameters, node.parameters)
            });
            if (!isPreviewPanelCapabilityEnabled()) {
                nodePreviewCoordinator?.setActiveNode(node);
            }
        } else {
            setSelectedOperator(null);
            if (!isPreviewPanelCapabilityEnabled()) {
                nodePreviewCoordinator?.setActiveNode(null);
            }
        }
    };

    flowCanvas.onNodeDoubleClicked = (node) => {
        if (node) {
            if (!isPreviewPanelCapabilityEnabled()) {
                nodePreviewCoordinator?.setActiveNode(node);
            }
            openSubgraphBreadcrumb(node);
        }
    };

    flowEditorInteraction = new FlowEditorInteraction(flowCanvas, { projectManager });
    serviceRegistry.register('flowEditorInteraction', flowEditorInteraction);
    finalDecisionPanel = new FinalDecisionPanel(flowCanvas);
    serviceRegistry.register('finalDecisionPanel', finalDecisionPanel);
    startAutoSave();

    debugLogger.debug('[App] 流程编辑器初始化完成');
}

function handleNewProject(options = {}) {
    if (!canEditProject()) {
        showProjectEditPermissionHint(options.preserveCanvas ? '保存为新工程' : '新建工程');
        return;
    }

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

    let createInFlight = false;
    const btnCreate = createButton({
        text: preserveCanvas ? '保存' : '创建',
        onClick: async () => {
            if (createInFlight) {
                return;
            }

            const name = nameInput.querySelector('input').value.trim();
            const desc = descInput.querySelector('input').value.trim();

            if (!name) {
                showToast('请输入工程名称', 'warning');
                return;
            }

            createInFlight = true;
            btnCreate.disabled = true;
            try {
                await createProject(name, desc, preserveCanvas);
                closeModal(modalOverlay);
                await switchView('flow');
            } catch {
                createInFlight = false;
                btnCreate.disabled = false;
            }
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
    if (!canEditProject()) {
        showProjectEditPermissionHint(preserveCanvas ? '保存为新工程' : '创建工程');
        throw new Error('ProjectEditPermissionRequired');
    }

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
    if (!PermissionGuard.canManageUsers()) {
        return theme;
    }

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
    void switchView(getCurrentView()).catch(error => handleFeatureLoadError('视图切换', error));
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

    if (isProjectPageCapabilityEnabled()) {
        projectPageCapabilityAdapter = createProjectPageCapabilityAdapter({
            projectManagerRef: projectManager
        });
        projectView = new ProjectPageCapabilityOwner(container, {
            adapter: projectPageCapabilityAdapter,
            showToast
        });
        serviceRegistry.register('projectPageCapabilityAdapter', projectPageCapabilityAdapter);
        serviceRegistry.register('projectPageCapabilityOwner', projectView);
        serviceRegistry.register('projectView', projectView);
        debugLogger.debug('[App] Project Page capability owner 初始化完成');
        return projectView;
    }

    const { ProjectView } = await loadProjectViewModule();
    projectView = new ProjectView('project-view');
    serviceRegistry.register('projectView', projectView);

    debugLogger.debug('[App] 工程视图初始化完成');
    return projectView;
}

function disposeSettingsViewOwner() {
    if (settingsView) {
        if (typeof settingsView.dispose === 'function') {
            settingsView.dispose();
        } else {
            settingsView.destroy?.();
        }
    }

    serviceRegistry.unregister('settingsView', settingsView);
    settingsView = null;
}

async function ensureSettingsView() {
    if (settingsView) {
        return settingsView;
    }

    const container = document.getElementById('settings-view');
    if (!container) {
        debugLogger.warn('[App] 设置视图容器未找到');
        return null;
    }

    disposeSettingsViewOwner();

    const { createLegacySettingsView } = await loadSettingsViewModule();
    settingsView = createLegacySettingsView('settings-view');
    serviceRegistry.register('settingsView', settingsView);
    debugLogger.debug('[App] 设置视图初始化完成');
    return settingsView;
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

    if (isResultsReviewCapabilityEnabled()) {
        const existingResultPanel = serviceRegistry.get('resultPanel');
        existingResultPanel?.dispose?.();
        resultsReviewCapabilityAdapter = createResultsReviewCapabilityAdapter({
            loadHistory: loadInspectionHistory,
            loadDetail: loadInspectionHistoryDetail,
            loadComparison: loadInspectionHistoryComparison,
            loadPreviousSuccess: loadInspectionPreviousSuccess,
            exportEvidence: exportInspectionEvidence
        });
        resultPanel = new ResultsReviewCapabilityOwner(container, {
            adapter: resultsReviewCapabilityAdapter,
            showToast
        });
        serviceRegistry.register('resultsReviewCapabilityAdapter', resultsReviewCapabilityAdapter);
        serviceRegistry.register('resultsReviewCapabilityOwner', resultPanel);
        serviceRegistry.register('resultPanel', resultPanel);
        resultPanel.setProjectContext(getCurrentProject()?.id || null);
        debugLogger.debug('[App] Results/Review capability owner 初始化完成');
        return resultPanel;
    }

    const { ResultPanel } = await loadResultPanelModule();

    const existingResultPanel = serviceRegistry.get('resultPanel');
    if (existingResultPanel && typeof existingResultPanel.dispose === 'function') {
        debugLogger.warn('[App] Found stale ResultPanel instance; disposing before recreation.');
        existingResultPanel.dispose();
    }

    resultPanel = new ResultPanel('results-list-container');
    serviceRegistry.register('resultPanel', resultPanel);
    resultPanel.setProjectContext(getCurrentProject()?.id || null);
    resultPanel.setHistoryLoader(loadInspectionHistory);
    resultPanel.setHistoryDetailLoader(loadInspectionHistoryDetail);
    resultPanel.setComparisonLoader(loadInspectionHistoryComparison);
    resultPanel.setPreviousSuccessLoader(loadInspectionPreviousSuccess);
    resultPanel.setEvidenceExportLoader(exportInspectionEvidence);

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
            void ensureProjectForAppliedFlow(syncedFlow, {
                projectName: `AI生成工程_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}`,
                description: '由 AI 生成流程自动创建。'
            });
        },
        onCanvasChanged: ({ flow } = {}) => {
            const syncedFlow = syncCurrentProjectFlowFromCanvas() || flow || null;
            if (syncedFlow) {
                getAiGenerationController().publishApplied(syncedFlow);
                void ensureProjectForAppliedFlow(syncedFlow, {
                    projectName: `AI生成工程_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}`,
                    description: '由 AI 生成流程自动创建。'
                });
            }
        }
    });
    serviceRegistry.register('aiPanel', aiPanel);
    debugLogger.debug('[App] AI 面板初始化完成');
    return aiPanel;
}

window.addEventListener('pagehide', () => {
    aiPanel?.dispose?.();
    aiPanel = null;
}, { once: true });

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
        canEditProject,
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

    // 安装全局 401 处理：会话在运行期间失效时清理本地会话并引导用户重新登录，
    // 而不是让各功能把 Unauthorized 当作普通错误反复弹出。
    installUnauthorizedHandler();

    const authState = await bootstrapAuthSession();
    if (!authState.ok) {
        debugLogger.warn(`[App] 认证启动失败: ${authState.reason}`);
        return false;
    }

    localDraftStorage.purgeOwnerlessLegacyDraft();
    trackedSubscribe(subscribeAuthContext, resetLocalDraftSessionState);
    resetLocalDraftSessionState();
    updateAuthenticatedUserDisplay();
    initializeNavigation();
    initializeOperatorLibraryPanel();
    await initializeFlowEditor();
    initializeImageViewer();
    initializeInspectionController();
    await initializePropertyPanel();
    initializePreviewPanelCapability();
    initializePropertySidebarController();
    await initializeGlobalVariablePanel();
    initializeTheme();
    initializeToolbar();
    startStatusBarUpdates();
    initializeStudioPerformanceGuards();
    trackedSubscribe(subscribeProject, (project) => {
        if (globalVariablePanel) {
            void globalVariablePanel.setProject(project).catch(error => {
                handleFeatureLoadError('全局变量', error);
            });
        }

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
    const nextProjectId = project?.id || null;
    if (nodePreviewProjectId !== nextProjectId) {
        clearStudioPreviewInputFrame();
    }
    const shouldClearPreview = nextProjectId === null ||
        (nodePreviewProjectId !== null && nodePreviewProjectId !== nextProjectId);
    nodePreviewProjectId = nextProjectId;
    if (shouldClearPreview) {
        nodePreviewSelectionStore?.clear?.();
        nodePreviewCoordinator?.setActiveNode?.(null);
    }
    if (!project?.id) {
        flowCanvas?.setGlobalVariableSchema?.(null);
        inspectionController.setProject(null);
        inspectionPanel?.setProjectContext?.(null);
        resultPanel?.setProjectContext?.(null);
        resultPanel?.clear?.();
        return;
    }

    inspectionController.setProject(project.id);
    inspectionPanel?.setProjectContext?.(project.id);
    flowCanvas?.setGlobalVariableSchema?.(project.globalVariables || project.GlobalVariables);

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

function syncCurrentProjectFlowFromCanvas(options = {}) {
    if (projectFlowSyncSuppressionDepth > 0 && options.force !== true) {
        return null;
    }

    const project = getCurrentProject();
    if (!project || !flowCanvas || typeof flowCanvas.serialize !== 'function') {
        return null;
    }

    if (options.expectedProjectId &&
        String(project.id || '').toLowerCase() !== String(options.expectedProjectId).toLowerCase()) {
        resetPendingProjectFlowSync();
        return null;
    }

    const flow = flowCanvas.serialize();
    projectManager.updateFlow(flow);
    if (!options.expectedProjectId ||
        String(project.id || '').toLowerCase() === String(options.expectedProjectId).toLowerCase()) {
        resetPendingProjectFlowSync();
    }
    return flow;
}

function getCurrentFlowSnapshotForPersistence() {
    return flushPendingProjectFlowSync() ||
        syncCurrentProjectFlowFromCanvas({ force: true, reason: 'persistence' }) ||
        getCurrentProject()?.flow ||
        null;
}

projectManager.setFlowSnapshotProvider?.(() => getCurrentFlowSnapshotForPersistence());

async function ensureProjectForAppliedFlow(flow, options = {}) {
    if (getCurrentProject()?.id || getFlowNodeCount(flow) === 0) {
        return null;
    }

    if (!canEditProject()) {
        showProjectEditPermissionHint('保存 AI 生成工程');
        return null;
    }

    const projectName = options.projectName || `AI生成工程_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}`;
    const projectDescription = options.description || '由 AI 生成流程自动创建。';

    try {
        const project = await projectManager.createProject(projectName, projectDescription);
        projectManager.updateFlow(flow);
        await projectManager.saveProject(projectManager.getCurrentProject?.() || project);
        showToast(`已创建工程：${project?.name || projectName}`, 'success');
        return project;
    } catch (error) {
        console.error('[App] 应用流程后自动创建工程失败:', error);
        showToast(`流程已应用，但创建工程失败: ${error?.message || error}`, 'warning');
        return null;
    }
}

function validateCurrentFlowForAction(action) {
    const panel = propertyPanel || serviceRegistry.get('propertyPanel');
    if (panel?.validateFlowForAction?.(flowCanvas, { action, showToast: true }) === false) {
        return false;
    }

    if (panel?.currentOperator && panel.applyChanges?.({ showToast: false }) === false) {
        return false;
    }

    return true;
}

function syncCurrentPropertyDraftForPersistence() {
    const panel = propertyPanel || serviceRegistry.get('propertyPanel');
    if (!panel?.currentOperator) {
        return true;
    }

    if (typeof panel.syncDraftChanges === 'function') {
        return panel.syncDraftChanges({ showToast: false }) !== false;
    }

    if (typeof panel.applyChanges === 'function') {
        return panel.applyChanges({ showToast: false }) !== false;
    }

    return true;
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
                const flow = flushPendingProjectFlowSync() ||
                    project.flow ||
                    (flowCanvas && typeof flowCanvas.serialize === 'function' ? flowCanvas.serialize() : null);
                if (!flow) {
                    return;
                }

                const signature = getLocalDraftBackupSignature(project, flow);
                if (!signature || signature === lastLocalDraftBackupSignature) {
                    return;
                }

                const backup = writeLocalDraftBackup(project, flow, 'timer');
                if (!backup) {
                    debugLogger.warn('[LocalDraftBackup] 缺少已完成认证的用户上下文，本轮草稿未写入。');
                    return;
                }

                debugLogger.debug('[LocalDraftBackup] 本机草稿备份完成:', new Date().toLocaleTimeString());
            } catch (err) {
                console.error('[LocalDraftBackup] 本机草稿备份失败:', err);
            }
        }
    }, AUTO_SAVE_DELAY);

    debugLogger.debug('[LocalDraftBackup] 本机草稿备份已启动，间隔:', AUTO_SAVE_DELAY / 1000 / 60, '分钟');
}

/**
 * Encoding cleanup: previous comment text was unreadable.
 */
function stopAutoSave() {
    if (autoSaveInterval) {
        clearInterval(autoSaveInterval);
        autoSaveInterval = null;
        debugLogger.debug('[LocalDraftBackup] Local draft backup event.');
    }
}

/**
 * Encoding cleanup: previous comment text was unreadable.
 */
async function triggerAutoSave() {
    const project = getCurrentProject();
    if (project && flowCanvas) {
        try {
            syncCurrentPropertyDraftForPersistence();
            project.flow = getCurrentFlowSnapshotForPersistence();
            if (!project.flow) {
                return;
            }

            const backup = writeLocalDraftBackup(project, project.flow, 'manual');
            if (!backup) {
                debugLogger.warn('[LocalDraftBackup] 缺少已完成认证的用户上下文，手动草稿未写入。');
                showToast('本机草稿备份失败：请重新登录后再试', 'error');
                return;
            }

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
            flow: getCurrentFlowSnapshotForPersistence(),
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
    const currentProject = getCurrentProject();
    const targetProjectId = projectId || currentProject?.id || null;
    if (currentProject && currentProject.id === targetProjectId && !syncCurrentPropertyDraftForPersistence()) {
        return;
    }

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
    if (!canExportRuntimePackage()) {
        showRuntimePackageExportPermissionHint();
        return;
    }

    const currentProject = getCurrentProject();
    const targetProjectId = projectId || currentProject?.id || null;
    if (!targetProjectId) {
        showToast('没有可导出的工程', 'warning');
        return;
    }
    if (currentProject && currentProject.id === targetProjectId && !validateCurrentFlowForAction('导出运行包')) {
        return;
    }
    try {
        const requestBody = {};
        if (currentProject && currentProject.id === targetProjectId) {
            const flow = getCurrentFlowSnapshotForPersistence();
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
            if (!canExportRuntimePackage()) {
                showRuntimePackageExportPermissionHint();
                return;
            }

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
        btnRuntime.disabled = !enabled || !canExportRuntimePackage();
        btnRuntime.title = canExportRuntimePackage()
            ? ''
            : '只有管理员可以导出运行包';
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
 * Encoding cleanup: previous comment text was unreadable.
 * @param {string} type - 算子类型
 * @returns {Object|null} 算子定义数据
 */
function findOperatorDefinition(type) {
    if (!operatorLibraryPanel) return null;
    const operators = operatorLibraryPanel.getOperators ? operatorLibraryPanel.getOperators() : [];
    return operators.find(op => op.type === type) || null;
}

/**
 * Encoding cleanup: previous comment text was unreadable.
 * Encoding cleanup: previous comment text was unreadable.
 * @param {Array} nodeParams - 画布节点保存的参数值
 * Encoding cleanup: previous comment text was unreadable.
 */
function mergeParameters(defParams, nodeParams) {
    if (!defParams || defParams.length === 0) return nodeParams || [];
    
    return defParams.map(defP => {
        // Encoding cleanup: previous comment text was unreadable.
        const nodeP = (nodeParams || []).find(np => 
            (np.name && defP.name && np.name.toLowerCase() === defP.name.toLowerCase()) ||
            (np.Name && defP.name && np.Name.toLowerCase() === defP.name.toLowerCase())
        );
        
        const mergedParam = { 
            ...defP,
            // 优先使用节点保存的值(Value 或 value)
            value: nodeP !== undefined ? (nodeP.value ?? nodeP.Value ?? nodeP.defaultValue ?? nodeP.DefaultValue) : defP.defaultValue
        };
        
        return mergedParam;
    });
}

/**
 * 【阶段B-B5】从JSON文件导入工程
 * Encoding cleanup: previous comment text was unreadable.
 */
async function importProjectFromJson(file) {
    if (!file) return;

    if (!canEditProject()) {
        showProjectEditPermissionHint('导入工程');
        return;
    }
    
    try {
        const content = await file.text();
        const importData = JSON.parse(content);
        
        // 验证文件格式
        if (!importData.project || !importData.project.flow) {
            throw new Error('无效的工程文件格式');
        }
        
        // 确认导入
        const confirmed = confirm(`确定要导入工程 "${importData.project.name || '未命名'}" 吗？\n如果当前工程有未保存的更改，系统会先询问是否保存。`);
        if (!confirmed) return;
        
        // Encoding cleanup: previous comment text was unreadable.
        const importName = (importData.project.name || '未命名') + ' (导入)';
        const importDesc = importData.project.description || '';
        const project = await projectManager.createProject(importName, importDesc);
        
        // 加载流程到画布
        if (flowCanvas && importData.project.flow) {
            withProjectFlowSyncSuppressed(() => flowCanvas.deserialize(importData.project.flow));
            // Refresh project flow after import.
            projectManager.updateFlow(flowCanvas.serialize());
            await projectManager.saveProject(projectManager.getCurrentProject?.() || project);
        }
        
        // Bind the inspection controller to the current project.
        inspectionController.setProject(project.id);
        
        // Switch back to the flow editor.
        switchView('flow');
        document.querySelectorAll('.nav-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.dataset.view === 'flow') btn.classList.add('active');
        });
        
        showToast('工程导入成功', 'success');
        debugLogger.debug('[Import] 工程已导入', project.name);
        
        // Encoding cleanup: previous comment text was unreadable.
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
// 阶段五：状态栏更新功能
// ==========================================================================

/**
 * 更新状态栏指标
 */
function updateStatusBar() {
    if (window.performance?.memory) {
        const memoryMB = Math.round(window.performance.memory.usedJSHeapSize / 1024 / 1024);
        const memoryEl = getStatusBarMetricElement('#memory-usage .metric-value', 'memory');
        if (memoryEl) memoryEl.textContent = `${memoryMB} MB`;
    }
}

/**
 * FPS 计数器
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

// Encoding cleanup: previous comment text was unreadable.
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
// Encoding cleanup: previous comment text was unreadable.
// ==========================================================================

/**
 * Encoding cleanup: previous comment text was unreadable.
 */
function switchViewWithTransition(view) {
    const views = ['flow', 'inspection', 'results', 'project'];
    const currentView = getCurrentView();
    
    if (currentView === view) return;
    
    // 获取当前显示的视图容器
    const currentContainer = document.getElementById(`${currentView}-view`) || 
                            document.getElementById(`${currentView}-editor`) ||
                            document.getElementById('flow-editor');
    
    if (currentContainer) {
        // Encoding cleanup: previous comment text was unreadable.
        currentContainer.classList.add('view-exit');
        
        setTimeout(() => {
            currentContainer.classList.remove('view-exit');
            currentContainer.classList.add('hidden');
            
    // 显示新视图
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
        // 无动画直接切换
        switchView(view);
    }
}

// ==========================================================================
// 阶段五：加载骨架层
// ==========================================================================

/**
 * 显示加载骨架层
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
 * 隐藏加载骨架层
 */
function hideLoadingScreen() {
    const loadingScreen = document.getElementById('loading-screen');
    if (loadingScreen) {
        loadingScreen.classList.add('hidden');
        setTimeout(() => loadingScreen.remove(), 500);
    }
}

// ==========================================================================
// 阶段五：欢迎/引导页
// ==========================================================================

/**
 * 显示欢迎页
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

