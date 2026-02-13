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
            window.chrome.webview.addEventListener('sharedbufferreceived', this.handleSharedBuffer.bind(this));
            console.log('[WebMessageBridge] WebView2 环境已初始化');
        } else {
            console.warn('[WebMessageBridge] 不在 WebView2 环境中，使用模拟模式');
            this.enableMockMode();
        }
    }

    /**
     * 处理共享缓冲区数据
     */
    handleSharedBuffer(event) {
        try {
            if (event.additionalData) {
                const metadata = JSON.parse(event.additionalData);
                const buffer = event.getBuffer();
                
                // 将 SharedBuffer 转换为消息分发
                // 注意：buffer 是 SharedBuffer对象，需要在使用处读取
                const handler = this.messageHandlers.get('image.stream.shared');
                if (handler) {
                    handler({
                        buffer,
                        width: metadata.width,
                        height: metadata.height
                    });
                }
            }
        } catch (error) {
            console.error('[WebMessageBridge] 处理共享缓冲区失败:', error);
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
        
        // 安全获取消息类型 (兼容后端可能的不同命名)
        // 关键修复：先检查 message 是否存在，再访问属性
        const messageType = message ? (message.type || message.messageType || message.MessageType) : null;
        
        if (!message || !messageType) {
            console.warn('[WebMessageBridge] 收到无效消息:', message);
            return;
        }

        console.log('[WebMessageBridge] 收到消息:', messageType, message);

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
        const handler = this.messageHandlers.get(messageType);
        if (handler) {
            try {
                // 将完整消息对象传给handler，确保能访问到 data 以外的属性（如 FilePickedEvent 的 filePath 可能在根对象上，取决于后端序列化）
                // 后端 EventBase 序列化通常是扁平的，所以 message 本身就是数据
                // 之前的代码传的是 message.data，这可能是不对的，除非后端包了一层 "data": { ... }
                // 对于 Command/Event，通常整个 JSON 就是对象。
                // 检查原代码： const result = handler(message.data);
                // 如果后端直接发的是 EventBase JSON，那么 message.data 将是 undefined！
                // 除非 postMessage({ type: '...', data: ... })。
                // 我刚刚确认 WebMessageHandler.cs 是 postWebMessageAsJson(json)。没有包 data。
                // 所以原有的 handler(message.data) 只有在前端发请求得到的响应结构中才有效？
                // 或者之前所有的 Event 都没工作？
                // 无论如何，对于 FilePickedEvent，数据在根上。
                // 我将传递 message 本身。为了兼容性，如果 message.data 存在，也可以考虑传 data?
                // 不，为了 FilePickedEvent，我必须传 message。
                
                const result = handler(message); 
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
            console.warn('[WebMessageBridge] 未找到消息处理器:', messageType);
        }
    }

    /**
     * 发送消息到 .NET
     */
    async sendMessage(type, data = null, expectResponse = false) {
        // 【修复】构建与后端期望格式一致的消息
        // 后端 MessageBase 期望 messageType 字段，而不是 type
        // 命令参数应该扁平化，而不是嵌套在 data 对象中
        const message = {
            ...data,             // 展开数据到根级别（先展开，后面覆盖）
            messageType: type,  // 使用 messageType 而不是 type，确保不被 data 覆盖
            timestamp: new Date().toISOString()  // 确保时间戳不被覆盖
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
