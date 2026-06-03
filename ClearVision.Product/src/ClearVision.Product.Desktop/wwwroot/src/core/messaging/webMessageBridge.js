/**
 * WebMessage bridge for WebView2 host communication.
 */
const canCaptureEarlyWindowErrors = typeof window !== 'undefined' && typeof window.addEventListener === 'function';

if (typeof window !== 'undefined' && !window._errorLogs) {
    window._errorLogs = [];
}

if (canCaptureEarlyWindowErrors && !window.__cvEarlyErrorCaptureInstalled) {
    window.__cvEarlyErrorCaptureInstalled = true;
    window.addEventListener('error', (event) => {
        const errorRecord = {
            type: 'Error',
            message: event?.message || event?.error?.message || 'Unknown error',
            source: event?.filename || event?.error?.fileName || '',
            line: event?.lineno || event?.error?.lineNumber || 0,
            column: event?.colno || event?.error?.columnNumber || 0,
            stack: event?.error?.stack || '',
            time: new Date().toLocaleTimeString()
        };
        window._errorLogs.push(errorRecord);
        if (window._errorLogs.length > 100) {
            window._errorLogs.shift();
        }
        console.error('[WebMessageBridge] Early error capture:', JSON.stringify(errorRecord));
    });
    window.addEventListener('unhandledrejection', (event) => {
        const reason = event?.reason;
        const errorRecord = {
            type: 'Promise',
            message: reason?.message || String(reason || 'Unknown rejection'),
            stack: reason?.stack || '',
            time: new Date().toLocaleTimeString()
        };
        window._errorLogs.push(errorRecord);
        if (window._errorLogs.length > 100) {
            window._errorLogs.shift();
        }
        console.error('[WebMessageBridge] Early rejection capture:', JSON.stringify(errorRecord));
    });
}

function debugWebMessageLog(...args) {
    if (globalThis.CV_DEBUG_WEBMESSAGE === true || globalThis.CV_DEBUG_INSPECTION === true) {
        console.debug(...args);
    }
}

const DEFAULT_MAX_PENDING_REQUESTS = 256;
const REQUEST_TIMEOUT_MS = 30000;

class WebMessageBridge {
    constructor() {
        // messageType -> Set<handler>
        this.messageHandlers = new Map();
        this.pendingRequests = new Map();
        this.pendingRequestTimeouts = new Map();
        this.requestId = 0;
        this.mockMode = false;
        this.maxPendingRequests = DEFAULT_MAX_PENDING_REQUESTS;
        this._boundHandleMessage = this.handleMessage.bind(this);
        this._boundHandleSharedBuffer = this.handleSharedBuffer.bind(this);
        this._isInitialized = false;

        this.initialize();
    }

    initialize() {
        if (this._isInitialized) {
            return;
        }

        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', this._boundHandleMessage);
            window.chrome.webview.addEventListener('sharedbufferreceived', this._boundHandleSharedBuffer);
            this._isInitialized = true;
            debugWebMessageLog('[WebMessageBridge] Initialized in WebView2');
        } else {
            console.warn('[WebMessageBridge] Not in WebView2, using mock mode');
            this.enableMockMode();
            this._isInitialized = true;
        }
    }

    handleSharedBuffer(event) {
        try {
            if (!event.additionalData) {
                return;
            }

            const metadata = JSON.parse(event.additionalData);
            const payload = {
                buffer: event.getBuffer(),
                width: metadata.width,
                height: metadata.height
            };

            const handlers = this.messageHandlers.get('image.stream.shared');
            if (!handlers || handlers.size === 0) {
                return;
            }

            [...handlers].forEach((handler) => {
                try {
                    handler(payload);
                } catch (error) {
                    console.error('[WebMessageBridge] Shared buffer handler failed:', error);
                }
            });
        } catch (error) {
            console.error('[WebMessageBridge] Failed to process shared buffer:', error);
        }
    }

    enableMockMode() {
        this.mockMode = true;
        window.mockWebViewResponse = (message) => {
            this.handleMessage({ data: message });
        };
    }

    handleMessage(event) {
        const message = event?.data;
        const messageType = message ? (message.type || message.messageType || message.MessageType) : null;

        if (!message || !messageType) {
            console.warn('[WebMessageBridge] Invalid message:', message);
            return;
        }

        debugWebMessageLog('[WebMessageBridge] Received message:', messageType, message);

        if (message.requestId && this.pendingRequests.has(message.requestId)) {
            if (message.error) {
                this.rejectPendingRequest(message.requestId, new Error(message.error));
            } else {
                this.resolvePendingRequest(message.requestId, message.data ?? message.payload ?? message);
            }
            return;
        }

        const handlers = this.messageHandlers.get(messageType);
        if (!handlers || handlers.size === 0) {
            debugWebMessageLog('[WebMessageBridge] No handler for message type:', messageType);
            return;
        }

        let firstResult;
        let hasResult = false;
        let firstError = null;

        const payload = message.payload ?? message.data ?? message;

        [...handlers].forEach((handler) => {
            try {
                const result = handler(payload);
                if (!hasResult) {
                    firstResult = result;
                    hasResult = true;
                }
            } catch (error) {
                if (!firstError) {
                    firstError = error;
                }
                console.error('[WebMessageBridge] Handler failed:', error);
            }
        });

        if (message.requestId) {
            if (firstError) {
                this.sendError(message.requestId, firstError.message || 'Unknown handler error');
            } else {
                this.sendResponse(message.requestId, firstResult);
            }
        }
    }

    async sendMessage(type, data = null, expectResponse = false) {
        const message = {
            ...(data || {}),
            messageType: type,
            timestamp: new Date().toISOString()
        };

        if (expectResponse) {
            message.requestId = ++this.requestId;

            return new Promise((resolve, reject) => {
                this.prunePendingRequestsForNewRequest();

                const timeoutId = setTimeout(() => {
                    this.rejectPendingRequest(message.requestId, new Error('Request timeout'));
                }, REQUEST_TIMEOUT_MS);

                this.pendingRequests.set(message.requestId, { resolve, reject });
                this.pendingRequestTimeouts.set(message.requestId, timeoutId);

                this.postMessage(message);
            });
        }

        this.postMessage(message);
        return Promise.resolve();
    }

    postMessage(message) {
        if (this.mockMode) {
            debugWebMessageLog('[WebMessageBridge] Mock post:', message);
            return;
        }

        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(message);
            return;
        }

        console.error('[WebMessageBridge] Unable to post message, WebView2 unavailable');
    }

    sendResponse(requestId, data) {
        this.postMessage({
            type: 'response',
            requestId,
            data,
            timestamp: Date.now()
        });
    }

    sendError(requestId, error) {
        this.postMessage({
            type: 'response',
            requestId,
            error,
            timestamp: Date.now()
        });
    }

    on(type, handler) {
        if (!type || typeof handler !== 'function') {
            return () => {};
        }

        let handlers = this.messageHandlers.get(type);
        if (!handlers) {
            handlers = new Set();
            this.messageHandlers.set(type, handlers);
        }

        handlers.add(handler);
        return () => this.off(type, handler);
    }

    off(type, handler = null) {
        const handlers = this.messageHandlers.get(type);
        if (!handlers) {
            return;
        }

        if (!handler) {
            this.messageHandlers.delete(type);
            return;
        }

        handlers.delete(handler);
        if (handlers.size === 0) {
            this.messageHandlers.delete(type);
        }
    }

    prunePendingRequestsForNewRequest() {
        const maxPendingRequests = Number(this.maxPendingRequests);
        if (!Number.isFinite(maxPendingRequests) || maxPendingRequests <= 0) {
            return;
        }

        while (this.pendingRequests.size >= maxPendingRequests) {
            const oldestRequestId = this.pendingRequests.keys().next().value;
            if (oldestRequestId === undefined) {
                break;
            }

            this.rejectPendingRequest(
                oldestRequestId,
                new Error(`Pending WebMessage request limit exceeded (${maxPendingRequests})`)
            );
        }
    }

    resolvePendingRequest(requestId, data) {
        const pendingRequest = this.pendingRequests.get(requestId);
        if (!pendingRequest) {
            return false;
        }

        this.clearPendingRequest(requestId);
        pendingRequest.resolve(data);
        return true;
    }

    rejectPendingRequest(requestId, error) {
        const pendingRequest = this.pendingRequests.get(requestId);
        if (!pendingRequest) {
            return false;
        }

        this.clearPendingRequest(requestId);
        pendingRequest.reject(error);
        return true;
    }

    clearPendingRequest(requestId) {
        const timeoutId = this.pendingRequestTimeouts.get(requestId);
        if (timeoutId !== undefined) {
            clearTimeout(timeoutId);
            this.pendingRequestTimeouts.delete(requestId);
        }

        this.pendingRequests.delete(requestId);
    }

    clearPendingRequests(error = new Error('WebMessage bridge disposed')) {
        const pendingRequestIds = [...this.pendingRequests.keys()];
        pendingRequestIds.forEach((requestId) => {
            this.rejectPendingRequest(requestId, error);
        });
    }

    dispose() {
        if (this._isInitialized && window.chrome && window.chrome.webview) {
            window.chrome.webview.removeEventListener?.('message', this._boundHandleMessage);
            window.chrome.webview.removeEventListener?.('sharedbufferreceived', this._boundHandleSharedBuffer);
        }

        this.clearPendingRequests();
        this.messageHandlers.clear();
        this._isInitialized = false;
    }
}

const webMessageBridge = new WebMessageBridge();

export default webMessageBridge;
export { WebMessageBridge };
