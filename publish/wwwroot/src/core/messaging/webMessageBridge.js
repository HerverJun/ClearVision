/**
 * WebMessage 通信桥接
 * 负责与 .NET WebView2 宿主通信
 */

class WebMessageBridge {
    constructor() {
        this.messageHandlers = new Map();
        this.pendingRequests = new Map();
        this.requestId = 0;
        
        // 初始化消息监听
        this.initialize();
    }

    /**
     * 初始化 WebMessage 监听
     */
    initialize() {
        // 检查是否在 WebView2 环境中
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', this.handleMessage.bind(this));
            console.log('[WebMessageBridge] WebView2 环境已初始化');
        } else {
            console.warn('[WebMessageBridge] 不在 WebView2 环境中，使用模拟模式');
            this.enableMockMode();
        }
    }

    /**
     * 启用模拟模式（用于开发测试）
     */
    enableMockMode() {
        this.mockMode = true;
        
        // 模拟响应
        window.mockWebViewResponse = (message) => {
            this.handleMessage({ data: message });
        };
    }

    /**
     * 处理来自 .NET 的消息
     */
    handleMessage(event) {
        const message = event.data;
        
        if (!message || !message.type) {
            console.warn('[WebMessageBridge] 收到无效消息:', message);
            return;
        }

        console.log('[WebMessageBridge] 收到消息:', message.type, message);

        // 处理响应
        if (message.requestId && this.pendingRequests.has(message.requestId)) {
            const { resolve, reject } = this.pendingRequests.get(message.requestId);
            this.pendingRequests.delete(message.requestId);

            if (message.error) {
                reject(new Error(message.error));
            } else {
                resolve(message.data);
            }
            return;
        }

        // 处理命令
        const handler = this.messageHandlers.get(message.type);
        if (handler) {
            try {
                const result = handler(message.data);
                if (message.requestId) {
                    this.sendResponse(message.requestId, result);
                }
            } catch (error) {
                console.error('[WebMessageBridge] 处理消息失败:', error);
                if (message.requestId) {
                    this.sendError(message.requestId, error.message);
                }
            }
        } else {
            console.warn('[WebMessageBridge] 未找到消息处理器:', message.type);
        }
    }

    /**
     * 发送消息到 .NET
     */
    async sendMessage(type, data = null, expectResponse = false) {
        const message = {
            type,
            data,
            timestamp: Date.now()
        };

        if (expectResponse) {
            message.requestId = ++this.requestId;
            
            return new Promise((resolve, reject) => {
                this.pendingRequests.set(message.requestId, { resolve, reject });
                
                // 设置超时
                setTimeout(() => {
                    if (this.pendingRequests.has(message.requestId)) {
                        this.pendingRequests.delete(message.requestId);
                        reject(new Error('请求超时'));
                    }
                }, 30000);

                this.postMessage(message);
            });
        } else {
            this.postMessage(message);
            return Promise.resolve();
        }
    }

    /**
     * 实际发送消息
     */
    postMessage(message) {
        if (this.mockMode) {
            console.log('[WebMessageBridge] 模拟发送:', message);
        } else if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(message);
        } else {
            console.error('[WebMessageBridge] 无法发送消息，WebView2 未初始化');
        }
    }

    /**
     * 发送响应
     */
    sendResponse(requestId, data) {
        this.postMessage({
            type: 'response',
            requestId,
            data,
            timestamp: Date.now()
        });
    }

    /**
     * 发送错误响应
     */
    sendError(requestId, error) {
        this.postMessage({
            type: 'response',
            requestId,
            error,
            timestamp: Date.now()
        });
    }

    /**
     * 注册消息处理器
     */
    on(type, handler) {
        this.messageHandlers.set(type, handler);
    }

    /**
     * 注销消息处理器
     */
    off(type) {
        this.messageHandlers.delete(type);
    }
}

// 创建单例实例
const webMessageBridge = new WebMessageBridge();

export default webMessageBridge;
export { WebMessageBridge };
