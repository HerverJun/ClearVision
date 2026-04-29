/**
 * 检测控制模块
 * 负责单次检测、实时检测、相机控制
 * 【架构修复 v2】支持 SSE + WebMessage 双栈
 */

import httpClient from '../../core/messaging/httpClient.js';
import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import { createSignal } from '../../core/state/store.js';
import { getStoredToken } from '../auth/authStorage.js';
import { buildSseHeaders, parseSseFrame } from './inspectionSseClient.mjs';

// 检测状态
const [getInspectionState, setInspectionState, subscribeInspectionState] = createSignal({
    isRunning: false,
    isRealtime: false,
    progress: 0,
    currentOperator: null,
    status: 'idle' // idle, running, completed, error
});

const [getLastResult, setLastResult, subscribeLastResult] = createSignal(null);

class InspectionController {
    constructor() {
        this.projectId = null;
        this.cameraId = null;
        this.abortController = null;
        
        // 【架构修复 v2】SSE 相关
        this.eventSource = null;
        this.isSseSupported = typeof fetch !== 'undefined'
            && typeof ReadableStream !== 'undefined'
            && typeof TextDecoder !== 'undefined';
        this.useSse = false;  // 是否使用 SSE（根据连接成功与否动态决定）
        this.lastSseEventId = null;
        this.sseProjectId = null;
        this.sseConnectionId = 0;
        this.sseReconnectTimer = null;
        this.sseReconnectAttempt = 0;
        this.sseReconnectBaseDelayMs = 1000;
        this.sseReconnectMaxDelayMs = 10000;
        
        // 初始化监听
        this.initializeWebMessage();
    }

    /**
     * 设置当前工程
     */
    setProject(projectId) {
        this.projectId = projectId;
    }

    /**
     * 设置相机
     */
    setCamera(cameraId) {
        this.cameraId = cameraId;
    }

    getCurrentFlowData() {
        if (window.flowCanvas && typeof window.flowCanvas.serialize === 'function') {
            return window.flowCanvas.serialize();
        }

        return null;
    }

    /**
     * 初始化 WebMessage 监听（降级方案）
     */
    initializeWebMessage() {
        // 监听算子执行事件
        webMessageBridge.on('operatorExecuted', (data) => {
            console.log('[InspectionController] 算子执行完成:', data);
            this.updateProgress(data);
        });

        // 【架构修复 v2】监听状态变更事件
        webMessageBridge.on('stateChanged', (data) => {
            console.log('[InspectionController] 状态变更:', data);
            this.handleStateChanged(data);
        });

        // 【架构修复 v2】监听检测结果事件
        webMessageBridge.on('resultProduced', (data) => {
            console.log('[InspectionController] 检测结果:', data);
            this.handleResultEvent(data);
        });

        // 【架构修复 v2】监听进度事件
        webMessageBridge.on('progressChanged', (data) => {
            console.log('[InspectionController] 进度更新:', data);
            this.updateProgress(data);
        });

        // 监听检测完成事件（兼容旧版）
        webMessageBridge.on('faulted', (data) => {
            console.error('[InspectionController] faulted:', data);
            this.handleInspectionError(new Error(data.errorMessage || 'Realtime inspection faulted'));
        });

        webMessageBridge.on('inspectionCompleted', (data) => {
            console.log('[InspectionController] 检测完成:', data);
            this.handleInspectionCompleted(data);
        });

        // 监听进度通知
        webMessageBridge.on('progressNotification', (data) => {
            this.updateProgress(data);
        });
    }

    /**
     * 【架构修复 v2】订阅 SSE 事件流
     */
    subscribeToSseEvents(projectId) {
        if (!this.isSseSupported) {
            console.log('[InspectionController] 浏览器不支持 SSE，使用 WebMessage');
            return false;
        }

        // 关闭已有连接
        this.unsubscribeFromSseEvents();

        try {
            console.log('[InspectionController] 连接 SSE:', projectId);
            
            const token = getStoredToken();
            const eventUrl = `${httpClient.baseUrl}/inspection/realtime/${projectId}/events`;
            if (this.sseProjectId !== projectId) {
                this.lastSseEventId = null;
                this.sseProjectId = projectId;
            }

            const controller = new AbortController();
            const connectionId = ++this.sseConnectionId;
            this.sseReconnectAttempt = 0;
            this.eventSource = {
                connectionId,
                close: () => controller.abort()
            };

            this.runSseStreamWithReconnect(eventUrl, token, controller.signal, connectionId);

            return true;
        } catch (error) {
            console.error('[InspectionController] SSE 连接失败:', error);
            this.useSse = false;
            return false;
        }
    }

    async runSseStreamWithReconnect(eventUrl, token, signal, connectionId) {
        while (!signal.aborted && this.isActiveSseConnection(connectionId)) {
            try {
                await this.openSseStream(eventUrl, token, signal);
                if (signal.aborted || !this.isActiveSseConnection(connectionId)) {
                    return;
                }

                console.warn('[InspectionController] SSE stream ended, reconnecting');
            } catch (error) {
                if (error?.name === 'AbortError' ||
                    signal.aborted ||
                    !this.isActiveSseConnection(connectionId)) {
                    return;
                }

                console.error('[InspectionController] SSE 错误:', error);
            }

            this.useSse = false;
            this.sseReconnectAttempt += 1;

            try {
                await this.waitForSseReconnect(signal, connectionId);
            } catch (error) {
                if (error?.name !== 'AbortError') {
                    console.error('[InspectionController] SSE reconnect wait failed:', error);
                }
                return;
            }
        }
    }

    async openSseStream(eventUrl, token, signal) {
        const headers = buildSseHeaders(token, this.lastSseEventId);

        const response = await fetch(eventUrl, {
            method: 'GET',
            headers,
            signal
        });

        if (!response.ok || !response.body) {
            throw new Error(`SSE connection failed: HTTP ${response.status}`);
        }

        console.log('[InspectionController] SSE 连接已建立');
        this.useSse = true;
        this.sseReconnectAttempt = 0;

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { value, done } = await reader.read();
            if (done) {
                break;
            }

            buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');

            let separatorIndex = buffer.indexOf('\n\n');
            while (separatorIndex >= 0) {
                const frame = buffer.slice(0, separatorIndex);
                buffer = buffer.slice(separatorIndex + 2);
                this.dispatchSseFrame(frame);
                separatorIndex = buffer.indexOf('\n\n');
            }
        }

        this.useSse = false;
    }

    isActiveSseConnection(connectionId) {
        return this.eventSource?.connectionId === connectionId;
    }

    waitForSseReconnect(signal, connectionId) {
        if (signal.aborted || !this.isActiveSseConnection(connectionId)) {
            return Promise.reject(this.createSseAbortError());
        }

        const delayMs = this.getSseReconnectDelayMs();

        return new Promise((resolve, reject) => {
            let timer = null;
            const cleanup = () => {
                if (timer !== null) {
                    clearTimeout(timer);
                }
                if (this.sseReconnectTimer === timer) {
                    this.sseReconnectTimer = null;
                }
                signal.removeEventListener('abort', onAbort);
            };
            const onAbort = () => {
                cleanup();
                reject(this.createSseAbortError());
            };

            this.clearSseReconnectTimer();
            timer = setTimeout(() => {
                cleanup();
                if (this.isActiveSseConnection(connectionId)) {
                    resolve();
                } else {
                    reject(this.createSseAbortError());
                }
            }, delayMs);
            this.sseReconnectTimer = timer;
            signal.addEventListener('abort', onAbort, { once: true });
        });
    }

    getSseReconnectDelayMs() {
        const attempt = Math.max(0, this.sseReconnectAttempt - 1);
        const backoffMultiplier = 2 ** Math.min(attempt, 4);
        return Math.min(
            this.sseReconnectMaxDelayMs,
            this.sseReconnectBaseDelayMs * backoffMultiplier
        );
    }

    clearSseReconnectTimer() {
        if (this.sseReconnectTimer !== null) {
            clearTimeout(this.sseReconnectTimer);
            this.sseReconnectTimer = null;
        }
    }

    createSseAbortError() {
        const error = new Error('SSE reconnect aborted');
        error.name = 'AbortError';
        return error;
    }

    dispatchSseFrame(frame) {
        const parsed = parseSseFrame(frame);
        if (parsed === null) {
            return;
        }

        const { eventName, eventId, payload } = parsed;
        if (eventId) {
            this.lastSseEventId = eventId;
        }

        switch (eventName) {
            case 'initialState':
                console.log('[InspectionController] SSE 初始状态:', payload);
                setInspectionState({
                    ...getInspectionState(),
                    isRealtime: payload.status === 'Running' || payload.status === 'Starting',
                    status: payload.status === 'Running' ? 'running' : 'idle'
                });
                break;
            case 'stateChanged':
                console.log('[InspectionController] SSE 状态变更:', payload);
                this.handleStateChanged(payload);
                break;
            case 'resultProduced':
                console.log('[InspectionController] SSE 检测结果:', payload);
                this.handleResultEvent(payload);
                break;
            case 'progressChanged':
                console.log('[InspectionController] SSE 进度:', payload);
                this.updateProgress(payload);
                break;
            case 'faulted':
                console.error('[InspectionController] SSE faulted:', payload);
                this.handleInspectionError(new Error(payload.errorMessage || 'Realtime inspection faulted'));
                break;
            case 'heartbeat':
                console.debug('[InspectionController] SSE 心跳');
                break;
            default:
                console.debug('[InspectionController] 未处理的 SSE 事件:', eventName);
                break;
        }
    }

    /**
     * 【架构修复 v2】取消 SSE 订阅
     */
    unsubscribeFromSseEvents() {
        this.clearSseReconnectTimer();
        if (this.eventSource) {
            console.log('[InspectionController] 关闭 SSE 连接');
            this.eventSource.close();
            this.eventSource = null;
        }

        this.sseConnectionId += 1;
        this.useSse = false;
    }

    /**
     * 【架构修复 v2】处理状态变更
     */
    handleStateChanged(data) {
        const statusMap = {
            'Starting': 'running',
            'Running': 'running',
            'Stopping': 'running',
            'Stopped': 'idle',
            'Faulted': 'error'
        };

        setInspectionState({
            ...getInspectionState(),
            isRealtime: data.newState === 'Running' || data.newState === 'Starting',
            status: statusMap[data.newState] || 'idle'
        });

        if (data.newState === 'Stopped' || data.newState === 'Faulted') {
            console.error('[InspectionController] 检测故障:', data.errorMessage);
        }
    }

    /**
     * 【架构修复 v2】处理结果事件
     */
    handleResultEvent(data) {
        const result = this.normalizeResultPayload({
            id: data.resultId ?? data.ResultId ?? data.id ?? data.Id,
            projectId: data.projectId ?? data.ProjectId,
            imageId: data.imageId ?? data.ImageId,
            status: data.status ?? data.Status,
            errorMessage: data.errorMessage ?? data.ErrorMessage,
            defects: data.defects ?? data.Defects ?? [],
            defectCount: data.defectCount ?? data.DefectCount,
            processingTimeMs: data.processingTimeMs ?? data.ProcessingTimeMs,
            timestamp: data.timestamp ?? data.Timestamp,
            outputData: data.outputData ?? data.OutputData,
            outputDataJson: data.outputDataJson ?? data.OutputDataJson,
            analysisData: data.analysisData ?? data.AnalysisData,
            analysisDataJson: data.analysisDataJson ?? data.AnalysisDataJson,
            outputImageBase64: data.outputImageBase64 ?? data.OutputImageBase64
        });

        setLastResult(result);

        // 如果有输出图像，显示它
        if (result.outputImageBase64) {
            const imageData = `data:image/png;base64,${result.outputImageBase64}`;
            if (window.inspectionImageViewer) {
                window.inspectionImageViewer.loadImage(imageData);
            }
            if (window.imageViewer) {
                window.imageViewer.loadImage(imageData);
            }
        }

        this.notifyInspectionCompleted(result);
    }

    /**
     * 执行单次检测
     */
    async executeSingle(imageData = null) {
        if (!this.projectId) {
            throw new Error('未选择工程');
        }

        setInspectionState({
            ...getInspectionState(),
            isRunning: true,
            progress: 0,
            status: 'running'
        });

        try {
            let result;
            const flowData = this.getCurrentFlowData();

            if (imageData) {
                const base64Data = imageData instanceof Uint8Array 
                    ? btoa(String.fromCharCode(...imageData))
                    : imageData;

                result = await httpClient.post('/inspection/execute', {
                    projectId: this.projectId,
                    imageBase64: base64Data,
                    flowData
                });
            } else if (this.cameraId) {
                result = await httpClient.post('/inspection/execute', {
                    projectId: this.projectId,
                    cameraId: this.cameraId,
                    flowData
                });
            } else {
                result = await httpClient.post('/inspection/execute', {
                    projectId: this.projectId,
                    flowData
                });
            }

            this.handleInspectionCompleted(result);
            return result;

        } catch (error) {
            console.error('[InspectionController] 检测执行失败:', error);
            this.handleInspectionError(error);
            throw error;
        }
    }

    /**
     * 开始实时检测
     */
    async startRealtime() {
        if (!this.projectId) {
            throw new Error('未选择工程');
        }

        if (!this.cameraId) {
            throw new Error('未选择相机');
        }

        // 【架构修复 v2】先订阅 SSE 事件
        this.subscribeToSseEvents(this.projectId);

        try {
            this.abortController = new AbortController();

            const flowData = this.getCurrentFlowData();
            
            await httpClient.post('/inspection/realtime/start', {
                projectId: this.projectId,
                cameraId: this.cameraId,
                runMode: 'camera',
                flowData: flowData
            });

            console.log('[InspectionController] 实时检测已启动');

        } catch (error) {
            console.error('[InspectionController] 启动实时检测失败:', error);
            this.unsubscribeFromSseEvents();
            throw error;
        }
    }

    /**
     * 开始实时检测（流程驱动模式）
     */
    async startRealtimeFlowMode() {
        if (!this.projectId) {
            throw new Error('未选择工程');
        }

        // 【架构修复 v2】先订阅 SSE 事件
        this.subscribeToSseEvents(this.projectId);

        try {
            this.abortController = new AbortController();

            const flowData = this.getCurrentFlowData();
            if (!flowData) {
                throw new Error('无法获取流程数据');
            }

            await httpClient.post('/inspection/realtime/start', {
                projectId: this.projectId,
                cameraId: this.cameraId || null,
                runMode: 'flow',
                flowData: flowData
            });

            console.log('[InspectionController] 实时检测已启动 (流程驱动)');

        } catch (error) {
            console.error('[InspectionController] 启动失败:', error);
            this.unsubscribeFromSseEvents();
            throw error;
        }
    }

    /**
     * 停止实时检测
     */
    async stopRealtime() {
        try {
            await httpClient.post('/inspection/realtime/stop', { projectId: this.projectId });
            
            if (this.abortController) {
                this.abortController.abort();
                this.abortController = null;
            }

            // 【架构修复 v2】取消 SSE 订阅
            this.unsubscribeFromSseEvents();

            console.log('[InspectionController] 实时检测已停止');

        } catch (error) {
            console.error('[InspectionController] 停止实时检测失败:', error);
        }
    }

    /**
     * 【Phase 3】预览工作流中指定节点的输出
     * 复用调试缓存机制，执行上游子图到目标节点
     * 
     * @param {Guid} targetNodeId - 目标节点ID
     * @param {Object} options - 预览选项
     * @param {string} options.debugSessionId - 调试会话ID（用于缓存复用）
     * @param {string} options.inputImageBase64 - 输入图像（可选）
     * @param {Object} options.parameters - 覆盖参数（可选）
     */
    async previewNode(targetNodeId, options = {}) {
        if (!this.projectId) {
            throw new Error('未选择工程');
        }

        try {
            const flowData = this.getCurrentFlowData();
            if (!flowData) {
                throw new Error('无法获取流程数据');
            }

            console.log('[InspectionController] 请求预览节点:', targetNodeId);

            const result = await httpClient.post('/flows/preview-node', {
                projectId: this.projectId,
                targetNodeId: targetNodeId,
                debugSessionId: options.debugSessionId || this.generateSessionId(),
                flowData: flowData,
                inputImageBase64: options.inputImageBase64,
                parameters: options.parameters,
                imageFormat: options.imageFormat || '.png'
            });

            console.log('[InspectionController] 预览完成:', result);

            // 显示预览结果
            if (result.outputImageBase64) {
                const imageData = `data:image/png;base64,${result.outputImageBase64}`;
                if (window.inspectionImageViewer) {
                    window.inspectionImageViewer.loadImage(imageData);
                }
                if (window.imageViewer) {
                    window.imageViewer.loadImage(imageData);
                }
            }

            return result;

        } catch (error) {
            console.error('[InspectionController] 预览节点失败:', error);
            throw error;
        }
    }

    async previewFlowNodeWithMetrics(targetNodeId, options = {}) {
        try {
            const flowData = this.getCurrentFlowData();
            if (!flowData) {
                throw new Error('无法获取流程数据');
            }

            const result = await httpClient.post('/autotune/flow-node/preview', {
                flowId: flowData.id || this.projectId || this.generateSessionId(),
                targetNodeId,
                flowData,
                inputImageBase64: options.inputImageBase64 || null,
                goal: options.goal || null
            });

            if (result?.previewImageBase64) {
                const imageData = `data:image/png;base64,${result.previewImageBase64}`;
                if (window.inspectionImageViewer) {
                    window.inspectionImageViewer.loadImage(imageData);
                }
                if (window.imageViewer) {
                    window.imageViewer.loadImage(imageData);
                }
            }

            return result;
        } catch (error) {
            console.error('[InspectionController] 线序预览分析失败:', error);
            throw error;
        }
    }

    async autoTuneWireSequenceScenario(options = {}) {
        try {
            const flowData = this.getCurrentFlowData();
            if (!flowData) {
                throw new Error('无法获取流程数据');
            }

            const result = await httpClient.post('/autotune/scenario', {
                scenarioKey: options.scenarioKey || 'wire-sequence-terminal',
                flowData,
                inputImageBase64: options.inputImageBase64 || null,
                goal: options.goal || null,
                maxIterations: options.maxIterations || 5
            });

            const finalPreview = result?.finalPreview || null;
            if (finalPreview?.previewImageBase64) {
                const imageData = `data:image/png;base64,${finalPreview.previewImageBase64}`;
                if (window.inspectionImageViewer) {
                    window.inspectionImageViewer.loadImage(imageData);
                }
                if (window.imageViewer) {
                    window.imageViewer.loadImage(imageData);
                }
            }

            return result;
        } catch (error) {
            console.error('[InspectionController] 线序场景自动调参失败:', error);
            throw error;
        }
    }

    /**
     * 【Phase 3】生成调试会话ID
     */
    generateSessionId() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            const r = Math.random() * 16 | 0;
            const v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    /**
     * 处理检测完成
     */
    handleInspectionCompleted(result) {
        const normalizedResult = this.normalizeResultPayload(result);

        setLastResult(normalizedResult);

        setInspectionState({
            ...getInspectionState(),
            isRunning: false,
            progress: 100,
            status: normalizedResult.status === 'Error' ? 'error' : 'completed'
        });

        const outputImage = normalizedResult.outputImage
            || normalizedResult.resultImageBase64
            || normalizedResult.outputImageBase64;
        if (outputImage) {
            const imageData = `data:image/png;base64,${outputImage}`;

            if (window.inspectionImageViewer) {
                window.inspectionImageViewer.loadImage(imageData);
            }

            if (window.imageViewer) {
                window.imageViewer.loadImage(imageData);
            }
        }

        this.notifyInspectionCompleted(normalizedResult);
    }

    /**
     * 处理检测错误
     */
    handleInspectionError(error) {
        setInspectionState({
            ...getInspectionState(),
            isRunning: false,
            isRealtime: false,
            status: 'error'
        });

        if (this._onErrorCallbacks) {
            this._onErrorCallbacks.forEach(cb => {
                try {
                    cb(error);
                } catch (callbackError) {
                    console.error('[InspectionController] 错误回调执行失败:', callbackError);
                }
            });
        }
    }

    /**
     * 更新进度
     */
    updateProgress(data) {
        setInspectionState({
            ...getInspectionState(),
            progress: data.progress || data.progressPercentage || 0,
            currentOperator: data.operatorName || data.currentOperator || null
        });
    }

    /**
     * 获取检测历史
     */
    async getInspectionHistory(startTime, endTime, pageIndex = 0, pageSize = 20) {
        if (!this.projectId) {
            throw new Error('未选择工程');
        }

        try {
            const params = {
                startTime: startTime?.toISOString(),
                endTime: endTime?.toISOString(),
                pageIndex,
                pageSize
            };

            const results = await httpClient.get(
                `/inspection/history/${this.projectId}`,
                params
            );

            return results;
        } catch (error) {
            console.error('[InspectionController] 获取检测历史失败:', error);
            throw error;
        }
    }

    /**
     * 获取统计信息
     */
    async getStatistics(startTime, endTime) {
        if (!this.projectId) {
            throw new Error('未选择工程');
        }

        try {
            const params = {
                startTime: startTime?.toISOString(),
                endTime: endTime?.toISOString()
            };

            const stats = await httpClient.get(
                `/inspection/statistics/${this.projectId}`,
                params
            );

            return stats;
        } catch (error) {
            console.error('[InspectionController] 获取统计信息失败:', error);
            throw error;
        }
    }

    /**
     * 设置检测完成回调
     */
    onInspectionCompleted(callback) {
        if (!this._onCompletedCallbacks) {
            this._onCompletedCallbacks = [];
        }
        this._onCompletedCallbacks.push(callback);
        
        return () => {
            if (this._onCompletedCallbacks) {
                this._onCompletedCallbacks = this._onCompletedCallbacks.filter(cb => cb !== callback);
            }
        };
    }

    /**
     * 设置检测错误回调
     */
    onInspectionError(callback) {
        if (!this._onErrorCallbacks) {
            this._onErrorCallbacks = [];
        }
        this._onErrorCallbacks.push(callback);
        
        return () => {
            if (this._onErrorCallbacks) {
                this._onErrorCallbacks = this._onErrorCallbacks.filter(cb => cb !== callback);
            }
        };
    }

    /**
     * 获取当前状态
     */
    getState() {
        return getInspectionState();
    }

    /**
     * 获取最新结果
     */
    getLastResult() {
        return getLastResult();
    }

    /**
     * 是否正在运行
     */
    isRunning() {
        return getInspectionState().isRunning;
    }

    /**
     * 是否实时检测模式
     */
    isRealtime() {
        return getInspectionState().isRealtime;
    }

    normalizeResultPayload(result) {
        const normalized = { ...(result || {}) };

        normalized.id = normalized.id ?? normalized.Id;
        normalized.projectId = normalized.projectId ?? normalized.ProjectId ?? this.projectId ?? null;
        normalized.status = this.normalizeInspectionStatus(normalized.status ?? normalized.Status);
        normalized.confidenceScore = normalized.confidenceScore ?? normalized.ConfidenceScore;
        normalized.errorMessage = normalized.errorMessage ?? normalized.ErrorMessage;

        const outputData = this.parseJsonField(
            this.readFirstDefined(normalized.outputData, normalized.OutputData),
            normalized.outputDataJson || normalized.OutputDataJson,
            'outputDataJson'
        );
        if (outputData) {
            normalized.outputData = outputData;
        }

        const analysisData = this.parseJsonField(
            this.readFirstDefined(normalized.analysisData, normalized.AnalysisData),
            normalized.analysisDataJson || normalized.AnalysisDataJson,
            'analysisDataJson'
        );
        if (analysisData) {
            normalized.analysisData = analysisData;
        }

        normalized.defects = Array.isArray(normalized.defects)
            ? normalized.defects
            : (Array.isArray(normalized.Defects) ? normalized.Defects : []);
        normalized.defectCount = this.readFirstDefined(
            normalized.defectCount,
            normalized.DefectCount,
            normalized.defects?.length,
            normalized.Defects?.length
        ) ?? 0;
        normalized.processingTimeMs = normalized.processingTimeMs
            ?? normalized.ProcessingTimeMs
            ?? normalized.processingTime
            ?? normalized.executionTimeMs
            ?? normalized.ExecutionTimeMs;
        normalized.timestamp = normalized.timestamp
            ?? normalized.Timestamp
            ?? normalized.inspectionTime
            ?? normalized.InspectionTime;

        normalized.outputImage = normalized.outputImage || normalized.OutputImage;
        normalized.outputImageBase64 = normalized.outputImageBase64 || normalized.OutputImageBase64;
        normalized.resultImageBase64 = normalized.resultImageBase64 || normalized.ResultImageBase64;
        normalized.imageData = normalized.imageData
            || normalized.ImageData
            || normalized.outputImage
            || normalized.resultImageBase64
            || normalized.outputImageBase64;
        normalized.imageId = normalized.imageId || normalized.ImageId;

        return normalized;
    }

    normalizeInspectionStatus(status) {
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

    parseJsonField(directValue, serializedValue, fieldName) {
        if (this.hasMeaningfulStructuredValue(directValue)) {
            return directValue;
        }

        if (typeof serializedValue !== 'string' || serializedValue.trim().length === 0) {
            return directValue || null;
        }

        try {
            return JSON.parse(serializedValue);
        } catch (error) {
            console.warn(`[InspectionController] 解析 ${fieldName} 失败:`, error);
            return directValue || null;
        }
    }

    hasMeaningfulStructuredValue(value) {
        if (!value || typeof value !== 'object') {
            return false;
        }

        if (Array.isArray(value)) {
            return value.length > 0;
        }

        return Object.keys(value).length > 0;
    }

    readFirstDefined(...values) {
        return values.find(value => value !== undefined && value !== null);
    }

    notifyInspectionCompleted(result) {
        if (!this._onCompletedCallbacks) {
            return;
        }

        this._onCompletedCallbacks.forEach(cb => {
            try {
                cb(result);
            } catch (callbackError) {
                console.error('[InspectionController] 完成回调执行失败:', callbackError);
            }
        });
    }
}

// 创建单例
const inspectionController = new InspectionController();

export default inspectionController;
export { 
    inspectionController,
    getInspectionState,
    getLastResult
};
