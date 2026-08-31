/**
 * 检测控制模块
 * 负责单次检测、实时检测、相机控制
 * 【架构修复 v2】支持 SSE + WebMessage 双栈
 */

import httpClient from '../../core/messaging/httpClient.js';
import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import { createSignal } from '../../core/state/store.js';
import { getStoredToken } from '../auth/authStorage.js';
import { getCurrentProject } from '../project/projectManager.js';
import { buildSseHeaders, buildSseUrl, parseSseFrame } from './inspectionSseClient.mjs';
import { normalizeCanonicalOutcome } from './canonicalOutcome.mjs';

// 检测状态
const [getInspectionState, setInspectionState, subscribeInspectionState] = createSignal({
    projectId: null,
    isRunning: false,
    isRealtime: false,
    progress: 0,
    currentOperator: null,
    status: 'idle' // idle, running, completed, error
});

const [getLastResult, setLastResult, subscribeLastResult] = createSignal(null);

const INLINE_RESULT_IMAGE_KEYS = [
    'imageData',
    'ImageData',
    'outputImage',
    'OutputImage',
    'outputImageBase64',
    'OutputImageBase64',
    'resultImageBase64',
    'ResultImageBase64'
];

const MAX_PREVIEW_INPUT_IMAGE_BYTES = 18 * 1024 * 1024;
const DEFAULT_SSE_MAX_FRAME_CHARS = 2 * 1024 * 1024;
const DEFAULT_SSE_MAX_BUFFER_CHARS = 4 * 1024 * 1024;
const LIGHTWEIGHT_RESULT_ARRAY_LIMIT = 24;
const LIGHTWEIGHT_RESULT_OBJECT_FIELD_LIMIT = 48;
const LIGHTWEIGHT_RESULT_STRING_LIMIT = 512;
const LIGHTWEIGHT_RESULT_MAX_DEPTH = 3;
const LIGHTWEIGHT_RESULT_IMAGE_KEY_PATTERN = /(image|bitmap|preview|thumbnail|base64|mask)/i;
const LOCKED_RUNTIME_STATES_FOR_SNAPSHOT = new Set(['starting', 'running', 'stopping']);

function readProjectId(payload, fallback = null) {
    return payload?.projectId ?? payload?.ProjectId ?? fallback ?? null;
}

function notifyDecisionAdmissionFailure(error) {
    const payload = error?.payload || {};
    const code = payload.code || payload.Code || '';
    const action = payload.action || payload.Action || '';
    if (action !== 'ConfigureFinalDecision' && !String(code).includes('DECISION')) {
        return;
    }
    window.dispatchEvent(new CustomEvent('clearvision:open-final-decision', {
        detail: {
            code,
            violations: payload.violations || payload.Violations || [],
            message: payload.error || payload.Error || error?.message || ''
        }
    }));
}

function getInlineResultImageBase64(result) {
    if (!result || typeof result !== 'object') {
        return null;
    }

    for (const key of INLINE_RESULT_IMAGE_KEYS) {
        const value = result[key];
        if (typeof value === 'string' && value.length > 0) {
            return value;
        }
    }

    return null;
}

function isLightweightImageLikeValue(key, value) {
    if (typeof value !== 'string') {
        return false;
    }

    const text = value.trim();
    if (text.startsWith('data:image/')) {
        return true;
    }

    return LIGHTWEIGHT_RESULT_IMAGE_KEY_PATTERN.test(String(key || '')) && text.length > 120;
}

function compactLightweightResultString(key, value) {
    if (isLightweightImageLikeValue(key, value)) {
        return '[image omitted]';
    }

    const text = String(value ?? '');
    return text.length > LIGHTWEIGHT_RESULT_STRING_LIMIT
        ? `${text.slice(0, LIGHTWEIGHT_RESULT_STRING_LIMIT)}...`
        : text;
}

function compactLightweightResultValue(value, depth = 0, seen = new WeakSet(), sourceKey = '') {
    if (typeof value === 'string') {
        return compactLightweightResultString(sourceKey, value);
    }

    if (value === null || value === undefined || typeof value !== 'object') {
        return value;
    }

    if (seen.has(value)) {
        return '[circular]';
    }

    if (depth >= LIGHTWEIGHT_RESULT_MAX_DEPTH) {
        return Array.isArray(value)
            ? `${value.length} items`
            : `${Object.keys(value).length} fields`;
    }

    seen.add(value);

    if (Array.isArray(value)) {
        const visibleItems = value
            .slice(0, LIGHTWEIGHT_RESULT_ARRAY_LIMIT)
            .map(item => compactLightweightResultValue(item, depth + 1, seen, sourceKey));
        if (value.length > visibleItems.length) {
            visibleItems.push(`+${value.length - visibleItems.length} more`);
        }
        return visibleItems;
    }

    const compact = {};
    const entries = Object.entries(value);
    let visibleCount = 0;
    let omittedImageCount = 0;
    for (const [key, entryValue] of entries) {
        if (isLightweightImageLikeValue(key, entryValue)) {
            omittedImageCount += 1;
            continue;
        }

        if (visibleCount >= LIGHTWEIGHT_RESULT_OBJECT_FIELD_LIMIT) {
            break;
        }

        compact[key] = compactLightweightResultValue(entryValue, depth + 1, seen, key);
        visibleCount += 1;
    }

    const hiddenCount = Math.max(0, entries.length - visibleCount - omittedImageCount);
    if (hiddenCount > 0) {
        compact.__hiddenFieldCount = hiddenCount;
    }
    if (omittedImageCount > 0) {
        compact.__omittedImageFieldCount = omittedImageCount;
    }
    return compact;
}

function createLightweightInspectionResult(result) {
    if (!result || typeof result !== 'object') {
        return result ?? null;
    }

    const lightweight = { ...result };
    for (const key of INLINE_RESULT_IMAGE_KEYS) {
        if (Object.prototype.hasOwnProperty.call(lightweight, key)) {
            lightweight[key] = null;
        }
    }

    if (Object.prototype.hasOwnProperty.call(lightweight, 'outputData')) {
        lightweight.outputData = compactLightweightResultValue(lightweight.outputData);
    }
    if (Object.prototype.hasOwnProperty.call(lightweight, 'OutputData')) {
        lightweight.OutputData = compactLightweightResultValue(lightweight.OutputData);
    }
    if (Object.prototype.hasOwnProperty.call(lightweight, 'analysisData')) {
        lightweight.analysisData = compactLightweightResultValue(lightweight.analysisData);
    }
    if (Object.prototype.hasOwnProperty.call(lightweight, 'AnalysisData')) {
        lightweight.AnalysisData = compactLightweightResultValue(lightweight.AnalysisData);
    }
    if (Array.isArray(lightweight.defects) && lightweight.defects.length > LIGHTWEIGHT_RESULT_ARRAY_LIMIT) {
        lightweight.defects = [
            ...lightweight.defects.slice(0, LIGHTWEIGHT_RESULT_ARRAY_LIMIT).map(defect => compactLightweightResultValue(defect)),
            `+${lightweight.defects.length - LIGHTWEIGHT_RESULT_ARRAY_LIMIT} more`
        ];
    }
    if (Array.isArray(lightweight.Defects) && lightweight.Defects.length > LIGHTWEIGHT_RESULT_ARRAY_LIMIT) {
        lightweight.Defects = [
            ...lightweight.Defects.slice(0, LIGHTWEIGHT_RESULT_ARRAY_LIMIT).map(defect => compactLightweightResultValue(defect)),
            `+${lightweight.Defects.length - LIGHTWEIGHT_RESULT_ARRAY_LIMIT} more`
        ];
    }

    return lightweight;
}

function getResultImageUrl(result) {
    const imageReference = result?.imageReference ?? result?.ImageReference;
    if (typeof imageReference !== 'string' || !imageReference.trim()) {
        return null;
    }

    return httpClient.buildRequestUrl(imageReference.trim());
}

function encodeBytesToBase64(bytes) {
    if (typeof Buffer !== 'undefined') {
        return Buffer.from(bytes).toString('base64');
    }

    let binary = '';
    const chunkSize = 0x8000;
    for (let index = 0; index < bytes.length; index += chunkSize) {
        const chunk = bytes.subarray(index, index + chunkSize);
        binary += String.fromCharCode(...chunk);
    }

    return btoa(binary);
}

async function loadImageUrlAsBase64(imageUrl, options = {}) {
    if (!imageUrl) {
        return null;
    }

    const configuredMaxBytes = Number(options.maxBytes ?? MAX_PREVIEW_INPUT_IMAGE_BYTES);
    const maxBytes = Number.isFinite(configuredMaxBytes) && configuredMaxBytes > 0
        ? configuredMaxBytes
        : MAX_PREVIEW_INPUT_IMAGE_BYTES;
    const requestUrl = httpClient.buildRequestUrl(imageUrl);

    try {
        const response = await fetch(requestUrl, {
            method: 'GET',
            headers: httpClient.defaultHeaders,
            signal: options.signal
        });
        if (!response.ok) {
            return null;
        }

        const contentLength = Number(response.headers?.get?.('content-length') ?? 0);
        if (Number.isFinite(contentLength) && contentLength > maxBytes) {
            return null;
        }

        const blob = await response.blob();
        if (Number.isFinite(maxBytes) && blob.size > maxBytes) {
            return null;
        }

        return encodeBytesToBase64(new Uint8Array(await blob.arrayBuffer()));
    } catch (error) {
        console.warn('[InspectionController] Failed to load cached inspection image:', error);
        return null;
    }
}

async function loadImageUrlAsBlob(imageUrl, options = {}) {
    if (!imageUrl) {
        throw new Error('Inspection image URL is empty.');
    }

    const requestUrl = httpClient.buildRequestUrl(imageUrl);
    const response = await fetch(requestUrl, {
        method: 'GET',
        headers: httpClient.defaultHeaders,
        signal: options.signal
    });

    if (!response.ok) {
        throw new Error(`Inspection image request failed: HTTP ${response.status}.`);
    }

    const blob = await response.blob();
    if (!blob || blob.size <= 0) {
        throw new Error('Inspection image response was empty.');
    }

    return blob;
}

function isAbortError(error) {
    return error?.name === 'AbortError';
}

function debugInspectionLog(...args) {
    if (globalThis.CV_DEBUG_INSPECTION === true) {
        console.debug(...args);
    }
}

class InspectionController {
    constructor() {
        this.projectId = null;
        this.cameraId = null;
        this.abortController = null;
        this.flowProvider = null;
        this.imageSinks = [];
        this.webMessageUnsubscribers = [];
        this.webMessageInitialized = false;
        this._onCompletedCallbacks = new Set();
        this._onErrorCallbacks = new Set();
        this._onImageStateCallbacks = new Set();
        
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
        this.sseMaxFrameChars = DEFAULT_SSE_MAX_FRAME_CHARS;
        this.sseMaxBufferChars = DEFAULT_SSE_MAX_BUFFER_CHARS;
        this.recentCompletedResultKeys = new Map();
        this.resultDedupeWindowMs = 5000;
        this.resultDedupeMaxEntries = 1000;
        this.lastResultImageBase64 = null;
        this.lastResultImageUrl = null;
        this.lastResultImageBlob = null;
        this.lastResultImageState = {
            status: 'idle',
            imageId: null,
            resultId: null,
            message: null
        };
        this._lastResultImageLoad = null;
        
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

    setFlowProvider(provider) {
        this.flowProvider = typeof provider === 'function' ? provider : null;
    }

    setImageSinks(sinks) {
        this.imageSinks = Array.isArray(sinks)
            ? sinks.filter(sink => typeof sink === 'function')
            : [];
    }

    getCurrentFlowData() {
        if (this.flowProvider) {
            return this.flowProvider();
        }

        return null;
    }

    publishImageData(imageData) {
        if (!imageData) {
            return;
        }

        this.imageSinks.forEach((sink) => {
            try {
                sink(imageData);
            } catch (error) {
                console.error('[InspectionController] Image sink failed:', error);
            }
        });
    }

    publishBase64Image(imageBase64) {
        if (!imageBase64) {
            return;
        }

        this.publishImageData(`data:image/png;base64,${imageBase64}`);
    }

    updateLatestResultImage(result) {
        this.cancelLastResultImageLoad();

        const inlineImage = getInlineResultImageBase64(result);
        const imageId = result?.imageId ?? result?.ImageId ?? null;
        const resultId = result?.id ?? result?.resultId ?? result?.Id ?? result?.ResultId ?? null;

        this.lastResultImageBase64 = inlineImage || null;
        this.lastResultImageUrl = inlineImage ? null : getResultImageUrl(result);
        this.lastResultImageBlob = null;

        if (inlineImage) {
            this.setLastResultImageState({
                status: 'ready',
                source: 'inline',
                imageId,
                resultId,
                message: null
            });
            this.publishBase64Image(inlineImage);
            return;
        }

        if (this.lastResultImageUrl) {
            void this.ensureLastResultImageLoaded();
            return;
        }

        this.setLastResultImageState({
            status: 'empty',
            imageId: null,
            resultId,
            message: '检测结果未提供可展示的图像。'
        });
    }

    async ensureLastResultImageLoaded(options = {}) {
        if (this.lastResultImageBase64) {
            return this.lastResultImageBase64;
        }

        if (this.lastResultImageBlob && options.force !== true) {
            return this.lastResultImageBlob;
        }

        const imageUrl = this.lastResultImageUrl;
        if (!imageUrl) {
            return null;
        }

        const result = getLastResult();
        const imageId = result?.imageId ?? result?.ImageId ?? null;
        const resultId = result?.id ?? result?.resultId ?? result?.Id ?? result?.ResultId ?? null;
        const requestKey = `${resultId ?? 'result'}:${imageId ?? imageUrl}`;

        if (options.force !== true && this._lastResultImageLoad?.key === requestKey) {
            return this._lastResultImageLoad.promise;
        }

        this.cancelLastResultImageLoad();
        const abortController = new AbortController();
        this.setLastResultImageState({
            status: 'loading',
            imageId,
            resultId,
            message: '正在加载检测图像。'
        });

        const promise = (async () => {
            try {
                const blob = await loadImageUrlAsBlob(imageUrl, { signal: abortController.signal });
                if (!this.isLatestResultImageLoad(requestKey, abortController)) {
                    return null;
                }

                this.lastResultImageBlob = blob;
                this.setLastResultImageState({
                    status: 'ready',
                    source: 'cache',
                    imageId,
                    resultId,
                    message: null
                });
                this.publishImageData(blob);
                return blob;
            } catch (error) {
                if (isAbortError(error) || !this.isLatestResultImageLoad(requestKey, abortController)) {
                    return null;
                }

                const message = error?.message || '检测图像加载失败。';
                console.warn('[InspectionController] Failed to load inspection result image:', message);
                this.setLastResultImageState({
                    status: 'error',
                    imageId,
                    resultId,
                    message
                });
                return null;
            } finally {
                if (this.isLatestResultImageLoad(requestKey, abortController)) {
                    this._lastResultImageLoad = null;
                }
            }
        })();

        this._lastResultImageLoad = { key: requestKey, abortController, promise };
        return promise;
    }

    retryLastResultImage() {
        return this.ensureLastResultImageLoaded({ force: true });
    }

    cancelLastResultImageLoad() {
        this._lastResultImageLoad?.abortController?.abort?.();
        this._lastResultImageLoad = null;
    }

    isLatestResultImageLoad(requestKey, abortController) {
        return this._lastResultImageLoad?.key === requestKey &&
            this._lastResultImageLoad?.abortController === abortController;
    }

    setLastResultImageState(nextState) {
        this.lastResultImageState = {
            status: nextState?.status || 'idle',
            imageId: nextState?.imageId ?? null,
            resultId: nextState?.resultId ?? null,
            source: nextState?.source ?? null,
            message: nextState?.message ?? null
        };

        [...this._onImageStateCallbacks].forEach(callback => {
            try {
                callback(this.lastResultImageState);
            } catch (callbackError) {
                console.error('[InspectionController] Image state callback failed:', callbackError);
            }
        });
    }

    /**
     * 初始化 WebMessage 监听（降级方案）
     */
    initializeWebMessage() {
        if (this.webMessageInitialized) {
            return;
        }

        const register = (type, handler) => {
            this.webMessageUnsubscribers.push(webMessageBridge.on(type, handler));
        };

        // 监听算子执行事件
        register('operatorExecuted', (data) => {
            debugInspectionLog('[InspectionController] 算子执行完成:', data);
            this.updateProgress(data);
        });

        // 【架构修复 v2】监听状态变更事件
        register('stateChanged', (data) => {
            debugInspectionLog('[InspectionController] 状态变更:', data);
            this.handleStateChanged(data);
        });

        // 【架构修复 v2】监听检测结果事件
        register('resultProduced', (data) => {
            debugInspectionLog('[InspectionController] 检测结果:', data);
            this.handleResultEvent(data);
        });

        // 【架构修复 v2】监听进度事件
        register('progressChanged', (data) => {
            debugInspectionLog('[InspectionController] 进度更新:', data);
            this.updateProgress(data);
        });

        // 监听检测完成事件（兼容旧版）
        register('faulted', (data) => {
            console.error('[InspectionController] faulted:', data);
            this.handleInspectionError(new Error(data.errorMessage || 'Realtime inspection faulted'));
        });

        register('inspectionCompleted', (data) => {
            debugInspectionLog('[InspectionController] 检测完成:', data);
            this.handleInspectionCompleted(data);
        });

        // 监听进度通知
        register('progressNotification', (data) => {
            this.updateProgress(data);
        });

        this.webMessageInitialized = true;
    }

    disposeWebMessage() {
        const unsubscribers = this.webMessageUnsubscribers.splice(0);
        unsubscribers.forEach((unsubscribe) => {
            try {
                unsubscribe();
            } catch (error) {
                console.warn('[InspectionController] WebMessage unsubscribe failed:', error);
            }
        });

        this.webMessageInitialized = false;
    }

    /**
     * 【架构修复 v2】订阅 SSE 事件流
     */
    subscribeToSseEvents(projectId) {
        if (!this.isSseSupported) {
            debugInspectionLog('[InspectionController] 浏览器不支持 SSE，使用 WebMessage');
            return false;
        }

        // 关闭已有连接
        this.unsubscribeFromSseEvents();

        try {
            debugInspectionLog('[InspectionController] 连接 SSE:', projectId);
            
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

        const response = await fetch(buildSseUrl(eventUrl, this.lastSseEventId), {
            method: 'GET',
            headers,
            signal
        });

        if (!response.ok || !response.body) {
            throw new Error(`SSE connection failed: HTTP ${response.status}`);
        }

        debugInspectionLog('[InspectionController] SSE 连接已建立');
        this.useSse = true;
        this.sseReconnectAttempt = 0;

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        try {
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
                    this.dispatchBoundedSseFrame(frame);
                    separatorIndex = buffer.indexOf('\n\n');
                }

                this.assertSseBufferWithinLimit(buffer);
            }
        } finally {
            try {
                reader.releaseLock?.();
            } catch (error) {
                debugInspectionLog('[InspectionController] SSE reader release failed:', error);
            }

            this.useSse = false;
        }
    }

    dispatchBoundedSseFrame(frame) {
        const maxFrameChars = Number(this.sseMaxFrameChars);
        if (Number.isFinite(maxFrameChars) && maxFrameChars > 0 && frame.length > maxFrameChars) {
            console.warn('[InspectionController] Dropping oversized SSE frame.', {
                length: frame.length,
                maxFrameChars
            });
            return false;
        }

        this.dispatchSseFrame(frame);
        return true;
    }

    assertSseBufferWithinLimit(buffer) {
        const maxBufferChars = Number(this.sseMaxBufferChars);
        if (Number.isFinite(maxBufferChars) && maxBufferChars > 0 && buffer.length > maxBufferChars) {
            throw new Error(`SSE buffer exceeded ${maxBufferChars} characters without a frame boundary`);
        }
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
                debugInspectionLog('[InspectionController] SSE 初始状态:', payload);
                this.applyRuntimeStateSnapshot(payload);
                break;
            case 'stateChanged':
                debugInspectionLog('[InspectionController] SSE 状态变更:', payload);
                this.handleStateChanged(payload);
                break;
            case 'resultProduced':
                debugInspectionLog('[InspectionController] SSE 检测结果:', payload);
                this.handleResultEvent(payload);
                break;
            case 'progressChanged':
                debugInspectionLog('[InspectionController] SSE 进度:', payload);
                this.updateProgress(payload);
                break;
            case 'faulted':
                console.error('[InspectionController] SSE faulted:', payload);
                this.handleInspectionError(new Error(payload.errorMessage || 'Realtime inspection faulted'));
                break;
            case 'heartbeat':
                debugInspectionLog('[InspectionController] SSE 心跳');
                break;
            default:
                debugInspectionLog('[InspectionController] 未处理的 SSE 事件:', eventName);
                break;
        }
    }

    /**
     * 【架构修复 v2】取消 SSE 订阅
     */
    unsubscribeFromSseEvents() {
        this.clearSseReconnectTimer();
        if (this.eventSource) {
            debugInspectionLog('[InspectionController] 关闭 SSE 连接');
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
        this.applyRuntimeStateSnapshot({
            ...data,
            status: data.status ?? data.Status ?? data.newState ?? data.NewState
        });

        if (data.newState === 'Faulted') {
            console.error('[InspectionController] 检测故障:', data.errorMessage);
        } else if (data.errorMessage) {
            debugInspectionLog('[InspectionController] 检测状态附带消息:', data.errorMessage);
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
            imageReference: data.imageReference ?? data.ImageReference,
            status: data.status ?? data.Status,
            executionOutcome: data.executionOutcome ?? data.ExecutionOutcome,
            decisionOutcome: data.decisionOutcome ?? data.DecisionOutcome,
            decisionSource: data.decisionSource ?? data.DecisionSource,
            reasonCode: data.reasonCode ?? data.ReasonCode,
            hasJudgmentSignal: data.hasJudgmentSignal ?? data.HasJudgmentSignal,
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

        if (!this.markResultAsHandled(result)) {
            debugInspectionLog('[InspectionController] 忽略重复检测结果:', this.getResultDedupeKey(result));
            return;
        }

        setLastResult(createLightweightInspectionResult(result));
        this.updateLatestResultImage(result);

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
            projectId: this.projectId,
            isRunning: true,
            progress: 0,
            status: 'running'
        });

        try {
            let result;
            const flowData = this.getCurrentFlowData();
            const draftAuthority = flowData
                ? this.createDraftExecutionAuthority(flowData, {
                    externalCameraBindingId: imageData ? null : this.cameraId
                })
                : {};

            if (imageData) {
                const base64Data = imageData instanceof Uint8Array
                    ? encodeBytesToBase64(imageData)
                    : imageData;

                result = await httpClient.post('/inspection/execute', {
                    ...draftAuthority,
                    projectId: this.projectId,
                    imageBase64: base64Data,
                    flowData
                });
            } else if (this.cameraId) {
                result = await httpClient.post('/inspection/execute', {
                    ...draftAuthority,
                    projectId: this.projectId,
                    cameraId: this.cameraId,
                    flowData
                });
            } else {
                result = await httpClient.post('/inspection/execute', {
                    ...draftAuthority,
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
            const draftAuthority = flowData
                ? this.createDraftExecutionAuthority(flowData, { externalCameraBindingId: this.cameraId })
                : {};
            
            await httpClient.post('/inspection/realtime/start', {
                ...draftAuthority,
                projectId: this.projectId,
                cameraId: this.cameraId,
                runMode: 'camera',
                flowData: flowData
            });

            debugInspectionLog('[InspectionController] 实时检测已启动');

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
                ...this.createDraftExecutionAuthority(flowData, {
                    externalCameraBindingId: this.cameraId || null
                }),
                projectId: this.projectId,
                cameraId: this.cameraId || null,
                runMode: 'flow',
                flowData: flowData
            });

            debugInspectionLog('[InspectionController] 实时检测已启动 (流程驱动)');

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
        const stoppedProjectId = this.projectId;
        try {
            await httpClient.post('/inspection/realtime/stop', { projectId: stoppedProjectId });
            this.applyRuntimeStateSnapshot({
                projectId: stoppedProjectId,
                status: 'Stopped',
                isBusy: false,
                stoppedAt: new Date().toISOString()
            });
            
            if (this.abortController) {
                this.abortController.abort();
                this.abortController = null;
            }

            // 【架构修复 v2】取消 SSE 订阅
            this.unsubscribeFromSseEvents();

            debugInspectionLog('[InspectionController] 实时检测已停止');

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
     * @param {string} options.inputImageSourceNodeId - 显式单帧所替代的相机采集节点（可选）
     * @param {Object} options.parameters - 覆盖参数（可选）
     * @param {AbortSignal} options.signal - 取消信号（可选）
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

            debugInspectionLog('[InspectionController] 请求预览节点:', targetNodeId);

            const result = await httpClient.post('/flows/preview-node', {
                ...this.createDraftExecutionAuthority(flowData),
                projectId: this.projectId,
                targetNodeId: targetNodeId,
                debugSessionId: options.debugSessionId || this.generateSessionId(),
                clientRequestSequence: options.clientRequestSequence,
                flowRevision: options.flowRevision,
                flowData: flowData,
                inputImageBase64: options.inputImageBase64,
                inputImageSourceNodeId: options.inputImageSourceNodeId,
                parameters: options.parameters,
                imageFormat: options.imageFormat || '.png',
                timeoutMs: options.timeoutMs,
                artifactMode: options.artifactMode || 'references'
            }, {
                signal: options.signal
            });

            debugInspectionLog('[InspectionController] 预览完成:', result);

            // 显示预览结果
            if (result.outputImageBase64) {
                this.publishBase64Image(result.outputImageBase64);
            }

            return result;

        } catch (error) {
            if (error?.name === 'AbortError') {
                throw error;
            }

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
            const authority = this.createAutoTuneDraftAuthority();

            const result = await httpClient.post('/autotune/flow-node/preview', {
                ...authority,
                flowId: flowData.id || this.projectId || this.generateSessionId(),
                targetNodeId,
                flowData,
                inputImageBase64: options.inputImageBase64 || null,
                goal: options.goal || null
            });

            if (result?.previewImageBase64) {
                this.publishBase64Image(result.previewImageBase64);
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
            const authority = this.createAutoTuneDraftAuthority();

            const result = await httpClient.post('/autotune/scenario', {
                ...authority,
                scenarioKey: options.scenarioKey || 'wire-sequence-terminal',
                flowData,
                inputImageBase64: options.inputImageBase64 || null,
                goal: options.goal || null,
                maxIterations: options.maxIterations || 5
            });

            const finalPreview = result?.finalPreview || null;
            if (finalPreview?.previewImageBase64) {
                this.publishBase64Image(finalPreview.previewImageBase64);
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
    createAutoTuneDraftAuthority() {
        const project = getCurrentProject();
        const projectId = project?.id ?? project?.Id ?? this.projectId;
        const expectedProjectRevision = Number(project?.persistenceRevision ?? project?.PersistenceRevision);
        if (!projectId || !Number.isInteger(expectedProjectRevision) || expectedProjectRevision < 0) {
            throw new Error('自动调参需要已保存工程及其当前版本，请先保存工程');
        }

        const confirmationId = this.generateSessionId();
        let auditId = this.generateSessionId();
        while (auditId === confirmationId) {
            auditId = this.generateSessionId();
        }

        return {
            projectId,
            expectedProjectRevision,
            declaredCapabilities: 0,
            confirmationId,
            auditId
        };
    }

    createDraftExecutionAuthority(flowData, { externalCameraBindingId = null } = {}) {
        const project = getCurrentProject();
        const projectId = project?.id ?? project?.Id ?? this.projectId;
        const expectedProjectRevision = Number(project?.persistenceRevision ?? project?.PersistenceRevision);
        if (!projectId || !Number.isInteger(expectedProjectRevision) || expectedProjectRevision < 0) {
            throw new Error('执行草稿需要已保存工程及其当前版本，请先保存工程');
        }

        const confirmationId = this.generateSessionId();
        let auditId = this.generateSessionId();
        while (auditId === confirmationId) {
            auditId = this.generateSessionId();
        }

        const capabilityManifest = this.deriveExecutionCapabilities(flowData);
        if (externalCameraBindingId && !this.shouldBypassExternalCameraInput(flowData) &&
            !capabilityManifest.includes('DeviceRead')) {
            capabilityManifest.push('DeviceRead');
            capabilityManifest.sort();
        }

        return {
            expectedProjectRevision,
            capabilityManifest,
            confirmationId,
            auditId
        };
    }

    shouldBypassExternalCameraInput(flowData) {
        const operators = Array.isArray(flowData?.operators)
            ? flowData.operators
            : (Array.isArray(flowData?.Operators) ? flowData.Operators : []);
        let hasExplicitFileSource = false;
        const readParameter = (operator, name) => {
            const parameters = Array.isArray(operator?.parameters)
                ? operator.parameters
                : (Array.isArray(operator?.Parameters) ? operator.Parameters : []);
            const parameter = parameters.find(item =>
                String(item?.name ?? item?.Name ?? '').toLowerCase() === name.toLowerCase());
            return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue;
        };

        for (const operator of operators) {
            if (String(operator?.type ?? operator?.Type ?? '') !== 'ImageAcquisition') continue;
            const sourceType = String(readParameter(operator, 'SourceType') ?? '').split('|')[0].trim().toLowerCase();
            const cameraBindingId = String(readParameter(operator, 'CameraId') ?? '').trim();
            if (sourceType === 'camera' || (!sourceType && cameraBindingId)) return false;
            const filePath = String(readParameter(operator, 'FilePath') ?? '').trim();
            if ((!sourceType || sourceType === 'file') && filePath) hasExplicitFileSource = true;
        }

        return hasExplicitFileSource;
    }

    deriveExecutionCapabilities(flowData) {
        const capabilities = new Set();
        const operators = Array.isArray(flowData?.operators)
            ? flowData.operators
            : (Array.isArray(flowData?.Operators) ? flowData.Operators : []);
        const networkTypes = new Set([
            'HttpRequest', 'TcpCommunication', 'SerialCommunication', 'ModbusCommunication',
            'ModbusRtuCommunication', 'SiemensS7Communication', 'MitsubishiMcCommunication',
            'OmronFinsCommunication', 'MqttPublish', 'DatabaseWrite'
        ]);
        const deviceWriteTypes = new Set([
            'CameraCalibration', 'FisheyeCalibration', 'StereoCalibration', 'NPointCalibration',
            'TranslationRotationCalibration', 'HandEyeCalibration', 'CalibrationLoader', 'TriggerModule'
        ]);
        const readParameter = (operator, name) => {
            const parameters = Array.isArray(operator?.parameters)
                ? operator.parameters
                : (Array.isArray(operator?.Parameters) ? operator.Parameters : []);
            const parameter = parameters.find(item =>
                String(item?.name ?? item?.Name ?? '').toLowerCase() === name.toLowerCase());
            return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue;
        };

        operators.forEach(operator => {
            if ((operator?.isEnabled ?? operator?.IsEnabled) === false) return;
            const type = String(operator?.type ?? operator?.Type ?? '');
            if (networkTypes.has(type)) capabilities.add('NetworkWrite');
            if (deviceWriteTypes.has(type)) capabilities.add('DeviceWrite');
            if (type === 'ImageSave' || type === 'TextSave' ||
                (type === 'ResultOutput' && String(readParameter(operator, 'SaveToFile')).toLowerCase() === 'true')) {
                capabilities.add('FileWrite');
            }
            if (type === 'ImageAcquisition') {
                const sourceType = String(readParameter(operator, 'SourceType') ?? '').split('|')[0].trim().toLowerCase();
                if (sourceType === 'camera') capabilities.add('DeviceRead');
                else if (String(readParameter(operator, 'FilePath') ?? '').trim()) capabilities.add('FileRead');
            }
            if (type === 'VariableWrite' || type === 'VariableIncrement') capabilities.add('StateWrite');
        });

        return Array.from(capabilities).sort();
    }

    generateSessionId() {
        const cryptoRef = globalThis.crypto;
        if (cryptoRef && typeof cryptoRef.randomUUID === 'function') {
            return cryptoRef.randomUUID();
        }

        if (cryptoRef && typeof cryptoRef.getRandomValues === 'function') {
            const bytes = new Uint8Array(16);
            cryptoRef.getRandomValues(bytes);
            bytes[6] = (bytes[6] & 0x0f) | 0x40;
            bytes[8] = (bytes[8] & 0x3f) | 0x80;
            const hex = Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
            return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
        }

        throw new Error('Secure random generator is not available.');
    }

    /**
     * 处理检测完成
     */
    handleInspectionCompleted(result) {
        const normalizedResult = this.normalizeResultPayload(result);
        const isDuplicate = !this.markResultAsHandled(normalizedResult);

        setInspectionState({
            ...getInspectionState(),
            projectId: normalizedResult.projectId ?? this.projectId,
            isRunning: false,
            isRealtime: false,
            progress: 100,
            status: ['failed', 'timedOut', 'invalid'].includes(normalizedResult.outcomeCategory)
                ? 'error'
                : 'completed'
        });

        if (isDuplicate) {
            debugInspectionLog('[InspectionController] 忽略重复检测完成:', this.getResultDedupeKey(normalizedResult));
            return;
        }

        setLastResult(createLightweightInspectionResult(normalizedResult));
        this.updateLatestResultImage(normalizedResult);

        this.notifyInspectionCompleted(normalizedResult);
    }

    /**
     * 处理检测错误
     */
    handleInspectionError(error) {
        notifyDecisionAdmissionFailure(error);
        setInspectionState({
            ...getInspectionState(),
            projectId: this.projectId,
            isRunning: false,
            isRealtime: false,
            status: 'error'
        });

        [...this._onErrorCallbacks].forEach(cb => {
            try {
                cb(error);
            } catch (callbackError) {
                console.error('[InspectionController] 错误回调执行失败:', callbackError);
            }
        });
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
        if (typeof callback !== 'function') {
            return () => {};
        }
        this._onCompletedCallbacks.add(callback);
        
        return () => {
            this._onCompletedCallbacks.delete(callback);
        };
    }

    /**
     * 设置检测错误回调
     */
    onInspectionError(callback) {
        if (typeof callback !== 'function') {
            return () => {};
        }
        this._onErrorCallbacks.add(callback);
        
        return () => {
            this._onErrorCallbacks.delete(callback);
        };
    }

    onInspectionImageState(callback) {
        if (typeof callback !== 'function') {
            return () => {};
        }

        this._onImageStateCallbacks.add(callback);
        callback(this.lastResultImageState);
        return () => this._onImageStateCallbacks.delete(callback);
    }

    /**
     * 获取当前状态
     */
    getState() {
        return getInspectionState();
    }

    async fetchRuntimeState(projectId = this.projectId) {
        if (!projectId) {
            return this.normalizeRuntimeStateSnapshot(null, projectId);
        }

        const payload = await httpClient.get(`/inspection/realtime/${projectId}/state`);
        return this.normalizeRuntimeStateSnapshot(payload, projectId);
    }

    subscribeState(callback) {
        return subscribeInspectionState(callback);
    }

    /**
     * 获取最新结果
     */
    getLastResult() {
        return getLastResult();
    }

    getLastResultImageBase64() {
        return this.lastResultImageBase64;
    }

    getLastResultImageUrl() {
        return this.lastResultImageUrl;
    }

    getLastResultImageBlob() {
        return this.lastResultImageBlob;
    }

    getLastResultImageState() {
        return this.lastResultImageState;
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

    normalizeRuntimeState(status) {
        const normalized = String(status || '').trim().toLowerCase();
        switch (normalized) {
            case 'starting':
                return 'starting';
            case 'running':
                return 'running';
            case 'stopping':
                return 'stopping';
            case 'completed':
                return 'completed';
            case 'faulted':
            case 'error':
                return 'error';
            case 'stopped':
            case 'idle':
            case '':
                return 'idle';
            default:
                return normalized;
        }
    }

    applyRuntimeStateSnapshot(payload) {
        const snapshot = this.normalizeRuntimeStateSnapshot(payload, readProjectId(payload, this.projectId));
        setInspectionState({
            ...getInspectionState(),
            ...snapshot
        });
        return snapshot;
    }

    normalizeRuntimeStateSnapshot(payload, projectId = null) {
        const status = this.normalizeRuntimeState(payload?.status ?? payload?.Status ?? 'Idle');
        const isBusy = Boolean(payload?.isBusy ?? payload?.IsBusy ?? LOCKED_RUNTIME_STATES_FOR_SNAPSHOT.has(status));
        return {
            projectId: readProjectId(payload, projectId),
            status,
            isBusy,
            isRunning: isBusy,
            isRealtime: isBusy,
            sessionId: payload?.sessionId ?? payload?.SessionId ?? null,
            startedAt: payload?.startedAt ?? payload?.StartedAt ?? null,
            stoppedAt: payload?.stoppedAt ?? payload?.StoppedAt ?? null
        };
    }

    normalizeResultPayload(result) {
        const normalized = { ...(result || {}) };

        normalized.id = normalized.id ?? normalized.Id;
        normalized.projectId = normalized.projectId ?? normalized.ProjectId ?? this.projectId ?? null;
        normalized.status = this.normalizeInspectionStatus(normalized.status ?? normalized.Status);
        normalized.executionOutcome = normalized.executionOutcome ?? normalized.ExecutionOutcome;
        normalized.decisionOutcome = normalized.decisionOutcome ?? normalized.DecisionOutcome;
        normalized.decisionSource = normalized.decisionSource ?? normalized.DecisionSource;
        normalized.reasonCode = normalized.reasonCode ?? normalized.ReasonCode;
        normalized.hasJudgmentSignal = normalized.hasJudgmentSignal ?? normalized.HasJudgmentSignal ?? false;
        const canonicalOutcome = normalizeCanonicalOutcome(normalized);
        normalized.executionOutcome = canonicalOutcome.executionOutcome;
        normalized.decisionOutcome = canonicalOutcome.decisionOutcome;
        normalized.outcomeCategory = canonicalOutcome.category;
        normalized.outcomeLabel = canonicalOutcome.label;
        normalized.outcomeTone = canonicalOutcome.tone;
        normalized.isLegacyOutcomeProjection = canonicalOutcome.isLegacyProjection;
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
        normalized.imageReference = normalized.imageReference ?? normalized.ImageReference ?? null;

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

    getResultDedupeKey(result) {
        return result?.id
            ?? result?.resultId
            ?? result?.ResultId
            ?? result?.Id
            ?? null;
    }

    markResultAsHandled(result) {
        const key = this.getResultDedupeKey(result);
        if (!key) {
            return true;
        }

        const now = Date.now();
        for (const [storedKey, timestamp] of this.recentCompletedResultKeys.entries()) {
            if (now - timestamp > this.resultDedupeWindowMs) {
                this.recentCompletedResultKeys.delete(storedKey);
            }
        }

        if (this.recentCompletedResultKeys.has(key)) {
            return false;
        }

        this.recentCompletedResultKeys.set(key, now);
        this.pruneResultDedupeEntries();
        return true;
    }

    pruneResultDedupeEntries() {
        const maxEntries = Number(this.resultDedupeMaxEntries);
        if (!Number.isFinite(maxEntries) || maxEntries <= 0) {
            return;
        }

        while (this.recentCompletedResultKeys.size > maxEntries) {
            const oldestKey = this.recentCompletedResultKeys.keys().next().value;
            if (oldestKey === undefined) {
                break;
            }

            this.recentCompletedResultKeys.delete(oldestKey);
        }
    }

    notifyInspectionCompleted(result) {
        [...this._onCompletedCallbacks].forEach(cb => {
            try {
                cb(result);
            } catch (callbackError) {
                console.error('[InspectionController] 完成回调执行失败:', callbackError);
            }
        });
    }

    dispose() {
        this.abortController?.abort?.();
        this.abortController = null;
        this.unsubscribeFromSseEvents();
        this.disposeWebMessage();
        this.setImageSinks([]);
        this.setFlowProvider(null);
        this._onCompletedCallbacks.clear();
        this._onErrorCallbacks.clear();
        this._onImageStateCallbacks.clear();
        this.recentCompletedResultKeys.clear();
        this.cancelLastResultImageLoad();
        this.lastResultImageBase64 = null;
        this.lastResultImageUrl = null;
        this.lastResultImageBlob = null;
        this.lastResultImageState = {
            status: 'idle',
            imageId: null,
            resultId: null,
            message: null
        };
    }
}

// 创建单例
const inspectionController = new InspectionController();

export default inspectionController;
export { 
    inspectionController,
    getInspectionState,
    getLastResult,
    getInlineResultImageBase64,
    createLightweightInspectionResult,
    getResultImageUrl,
    loadImageUrlAsBase64,
    loadImageUrlAsBlob
};
