/**
 * HTTP API 客户端
 * 用于与后端 Minimal APIs 通信
 */

import {
    API_PORT_CANDIDATES,
    DEFAULT_API_PORT,
    buildLocalApiBaseUrl,
    getSavedApiPort,
    isHostInjectedEnvironment,
    saveApiPort
} from './apiConfig.js';
import { getStoredToken } from '../../features/auth/authStorage.js';

class HttpError extends Error {
    constructor(message, { status = 0, statusText = '', payload = null, rawBody = '', response = null } = {}) {
        super(message || `HTTP ${status}`);
        this.name = 'HttpError';
        this.status = status;
        this.statusCode = status;
        this.statusText = statusText;
        this.payload = payload;
        this.rawBody = rawBody;
        this.response = response;
    }
}

class HttpClient {
    constructor(baseUrl = null) {
        this._baseUrl = baseUrl;
        this._defaultHeaders = {
            'Content-Type': 'application/json'
        };
        this._discoveredPort = null;
        this._lastSuccessfulConnectionAt = null;
    }

    /**
     * 获取请求头（自动附加认证 Token）
     * 每次请求时动态读取当前会话中的 Token
     */
    get defaultHeaders() {
        const headers = { ...this._defaultHeaders };
        const token = getStoredToken();
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }
        return headers;
    }

    set defaultHeaders(value) {
        this._defaultHeaders = value;
    }

    /**
     * 获取基础 URL
     * 优先级：1. 构造参数 2. window.__API_BASE_URL__ 注入 3. 自动探测
     */
    get baseUrl() {
        if (this._baseUrl) return this._baseUrl;
        if (window.__API_BASE_URL__) return window.__API_BASE_URL__;

        const { protocol, hostname, port } = window.location;

        // 如果是在 WebView2 (file://) 或 Electron 环境下运行
        // 或者使用虚拟主机 app.local
        if (isHostInjectedEnvironment()) {
            // 尝试从本地存储获取上次成功连接的端口
            const savedPort = getSavedApiPort();
            if (savedPort) {
                console.log(`[HttpClient] 使用本地存储的端口: ${savedPort}`);
                return buildLocalApiBaseUrl(savedPort);
            }

            // 如果已经发现过端口，使用发现的端口
            if (this._discoveredPort) {
                return buildLocalApiBaseUrl(this._discoveredPort);
            }

            // 默认回退到 localhost:5000
            console.warn('[HttpClient] 警告: 未检测到 API 配置，将尝试自动发现端口');
            return buildLocalApiBaseUrl(DEFAULT_API_PORT);
        }

        // 浏览器环境：使用当前页面端口
        return `${protocol}//${hostname}:${port || DEFAULT_API_PORT}/api`;
    }

    /**
     * 自动发现可用端口
     * 尝试连接与宿主一致的 5000-5010 端口范围
     */
    async discoverPort() {
        if (this._discoveredPort) return this._discoveredPort;

        for (const port of API_PORT_CANDIDATES) {
            try {
                const controller = new AbortController();
                const timeoutId = setTimeout(() => controller.abort(), 500);

                const response = await fetch(`http://localhost:${port}/health`, {
                    method: 'GET',
                    signal: controller.signal
                });

                clearTimeout(timeoutId);

                if (response.ok) {
                    console.log(`[HttpClient] 发现后端服务运行在端口: ${port}`);
                    this._discoveredPort = port;
                    saveApiPort(port);
                    return port;
                }
            } catch (e) {
                // 端口不可用，继续尝试下一个
            }
        }

        return null;
    }

    /**
     * 保存成功连接的端口
     */
    saveSuccessfulPort(url) {
        try {
            this._lastSuccessfulConnectionAt = new Date();
            const match = url.match(/:(\d+)\/api/);
            if (match) {
                saveApiPort(Number.parseInt(match[1], 10));
                console.log(`[HttpClient] 已保存 API 端口: ${match[1]}`);
            }
        } catch (e) {
            // 忽略存储错误
        }
    }

    getLastSuccessfulConnectionText() {
        if (!this._lastSuccessfulConnectionAt) {
            return '本次会话尚未成功连接';
        }

        return this._lastSuccessfulConnectionAt.toLocaleString();
    }

    isDeveloperDiagnosticsEnabled() {
        try {
            return localStorage.getItem('cv_developer_diagnostics') === 'true';
        } catch {
            return false;
        }
    }

    buildFieldNetworkErrorMessage(apiUrl, error) {
        const requestId = globalThis.crypto?.randomUUID?.() || `${Date.now()}`;
        const diagnosticInfo = [
            `requestId=${requestId}`,
            `time=${new Date().toISOString()}`,
            `service=${apiUrl.origin}`,
            `path=${apiUrl.pathname}`,
            `lastSuccess=${this.getLastSuccessfulConnectionText()}`,
            `error=${error?.message || error?.name || 'NetworkError'}`
        ].join('; ');

        return `
无法连接到 ClearVision 服务 (${apiUrl.host})

现场检查步骤：
1. 确认本机 ClearVision 服务窗口仍在运行，托盘或任务管理器中没有异常退出。
2. 检查工控机网线、交换机和防火墙策略，确认当前电脑可以访问 ${apiUrl.hostname}:${apiUrl.port || '(默认端口)'}。
3. 如果刚修改过服务端口或部署包，请重启 ClearVision 后再刷新页面。
4. 仍无法恢复时，将下方诊断信息发给设备维护或售后人员。

上次成功连接：${this.getLastSuccessfulConnectionText()}
诊断信息：${diagnosticInfo}
        `.trim();
    }

    buildDeveloperNetworkErrorMessage(apiUrl) {
        return `

开发诊断：
- 当前尝试端口: ${apiUrl.port || '(默认端口)'}
- 可用端口探测范围: ${API_PORT_CANDIDATES.join(', ')}
- 如需临时指定端口，可在浏览器控制台设置 cv_api_port 后刷新。
        `.trimEnd();
    }

    get rootBaseUrl() {
        return this.baseUrl.replace(/\/api\/?$/i, '');
    }

    normalizePath(url) {
        const raw = String(url ?? '').trim();
        if (!raw) {
            return '/';
        }

        if (/^https?:\/\//i.test(raw)) {
            return raw;
        }

        if (raw.startsWith('//')) {
            return `${window.location.protocol}${raw}`;
        }

        let normalized = raw;
        if (/^\/api(\/|$)/i.test(normalized)) {
            normalized = normalized.replace(/^\/api(?=\/|$)/i, '') || '/';
        }

        if (!normalized.startsWith('/')) {
            normalized = `/${normalized}`;
        }

        return normalized;
    }

    appendQueryString(url, queryString) {
        if (!queryString) {
            return url;
        }

        return `${url}${url.includes('?') ? '&' : '?'}${queryString}`;
    }

    buildRequestUrl(url, params = null, baseUrl = this.baseUrl) {
        const normalizedPath = this.normalizePath(url);
        const queryString = params ? new URLSearchParams(params).toString() : '';

        if (/^https?:\/\//i.test(normalizedPath)) {
            return this.appendQueryString(normalizedPath, queryString);
        }

        return this.appendQueryString(`${baseUrl}${normalizedPath}`, queryString);
    }

    buildRootRequestUrl(url, params = null, baseUrl = this.rootBaseUrl) {
        const normalizedPath = this.normalizePath(url);
        const queryString = params ? new URLSearchParams(params).toString() : '';

        if (/^https?:\/\//i.test(normalizedPath)) {
            return this.appendQueryString(normalizedPath, queryString);
        }

        return this.appendQueryString(`${baseUrl}${normalizedPath}`, queryString);
    }

    /**
     * 发送 GET 请求
     */
    async get(url, params = null) {
        let fullUrl = this.buildRequestUrl(url, params);

        console.log(`[HttpClient] GET ${fullUrl}`);

        try {
            const response = await fetch(fullUrl, {
                method: 'GET',
                headers: this.defaultHeaders
            });
            this.saveSuccessfulPort(fullUrl);
            return this.handleResponse(response);
        } catch (error) {
            // 如果是连接错误，尝试自动发现端口并重试
            if (error.message?.includes('Failed to fetch') || error.name === 'TypeError') {
                const discoveredPort = await this.discoverPort();
                if (discoveredPort && discoveredPort !== DEFAULT_API_PORT) {
                    console.log(`[HttpClient] 尝试使用发现的端口 ${discoveredPort} 重试...`);
                    fullUrl = this.buildRequestUrl(url, params, buildLocalApiBaseUrl(discoveredPort));
                    const response = await fetch(fullUrl, {
                        method: 'GET',
                        headers: this.defaultHeaders
                    });
                    this.saveSuccessfulPort(fullUrl);
                    return this.handleResponse(response);
                }
            }
            throw this.handleNetworkError(error, fullUrl);
        }
    }

    async getRoot(url, params = null) {
        let fullUrl = this.buildRootRequestUrl(url, params);

        console.log(`[HttpClient] GET ${fullUrl}`);

        try {
            const response = await fetch(fullUrl, {
                method: 'GET',
                headers: this.defaultHeaders
            });
            this.saveSuccessfulPort(this.buildRequestUrl('/health'));
            return this.handleResponse(response);
        } catch (error) {
            if (error.message?.includes('Failed to fetch') || error.name === 'TypeError') {
                const discoveredPort = await this.discoverPort();
                if (discoveredPort && discoveredPort !== DEFAULT_API_PORT) {
                    const discoveredRootBaseUrl = buildLocalApiBaseUrl(discoveredPort).replace(/\/api\/?$/i, '');
                    console.log(`[HttpClient] 尝试使用发现的端口 ${discoveredPort} 重试根路径请求...`);
                    fullUrl = this.buildRootRequestUrl(url, params, discoveredRootBaseUrl);
                    const response = await fetch(fullUrl, {
                        method: 'GET',
                        headers: this.defaultHeaders
                    });
                    this.saveSuccessfulPort(buildLocalApiBaseUrl(discoveredPort));
                    return this.handleResponse(response);
                }
            }

            throw this.handleNetworkError(error, fullUrl);
        }
    }

    /**
     * 发送 POST 请求
     */
    async post(url, data = null, options = {}) {
        let fullUrl = this.buildRequestUrl(url);
        console.log(`[HttpClient] POST ${fullUrl}`);
        const signal = options?.signal;

        try {
            const response = await fetch(fullUrl, {
                method: 'POST',
                headers: this.defaultHeaders,
                body: data ? JSON.stringify(data) : null,
                signal
            });
            this.saveSuccessfulPort(fullUrl);
            return this.handleResponse(response);
        } catch (error) {
            // 如果是连接错误，尝试自动发现端口并重试
            if (error.message?.includes('Failed to fetch') || error.name === 'TypeError') {
                const discoveredPort = await this.discoverPort();
                if (discoveredPort && discoveredPort !== DEFAULT_API_PORT) {
                    console.log(`[HttpClient] 尝试使用发现的端口 ${discoveredPort} 重试...`);
                    fullUrl = this.buildRequestUrl(url, null, buildLocalApiBaseUrl(discoveredPort));
                    const response = await fetch(fullUrl, {
                        method: 'POST',
                        headers: this.defaultHeaders,
                        body: data ? JSON.stringify(data) : null,
                        signal
                    });
                    this.saveSuccessfulPort(fullUrl);
                    return this.handleResponse(response);
                }
            }
            throw this.handleNetworkError(error, fullUrl);
        }
    }

    /**
     * 发送 POST 请求并接收 Blob 响应
     */
    async postForBlob(url, data = null, options = {}) {
        let fullUrl = this.buildRequestUrl(url);
        console.log(`[HttpClient] POST (blob) ${fullUrl}`);
        const signal = options?.signal;

        try {
            const response = await fetch(fullUrl, {
                method: 'POST',
                headers: this.defaultHeaders,
                body: data ? JSON.stringify(data) : null,
                signal
            });
            this.saveSuccessfulPort(fullUrl);
            return this.handleBlobResponse(response);
        } catch (error) {
            // 如果是连接错误，尝试自动发现端口并重试
            if (error.message?.includes('Failed to fetch') || error.name === 'TypeError') {
                const discoveredPort = await this.discoverPort();
                if (discoveredPort && discoveredPort !== DEFAULT_API_PORT) {
                    console.log(`[HttpClient] 尝试使用发现的端口 ${discoveredPort} 重试...`);
                    fullUrl = this.buildRequestUrl(url, null, buildLocalApiBaseUrl(discoveredPort));
                    const response = await fetch(fullUrl, {
                        method: 'POST',
                        headers: this.defaultHeaders,
                        body: data ? JSON.stringify(data) : null,
                        signal
                    });
                    this.saveSuccessfulPort(fullUrl);
                    return this.handleBlobResponse(response);
                }
            }
            throw this.handleNetworkError(error, fullUrl);
        }
    }

    /**
     * 发送 PUT 请求
     */
    async getForBlob(url, options = {}) {
        let fullUrl = this.buildRequestUrl(url);
        console.log(`[HttpClient] GET (blob) ${fullUrl}`);
        const signal = options?.signal;

        try {
            const response = await fetch(fullUrl, {
                method: 'GET',
                headers: this.defaultHeaders,
                cache: options?.cache || 'no-store',
                signal
            });
            this.saveSuccessfulPort(fullUrl);
            return this.handleBlobResponse(response);
        } catch (error) {
            if (error.message?.includes('Failed to fetch') || error.name === 'TypeError') {
                const discoveredPort = await this.discoverPort();
                if (discoveredPort && discoveredPort !== DEFAULT_API_PORT) {
                    console.log(`[HttpClient] 尝试使用发现的端口 ${discoveredPort} 重试 blob GET...`);
                    fullUrl = this.buildRequestUrl(url, null, buildLocalApiBaseUrl(discoveredPort));
                    const response = await fetch(fullUrl, {
                        method: 'GET',
                        headers: this.defaultHeaders,
                        cache: options?.cache || 'no-store',
                        signal
                    });
                    this.saveSuccessfulPort(fullUrl);
                    return this.handleBlobResponse(response);
                }
            }

            throw this.handleNetworkError(error, fullUrl);
        }
    }

    async put(url, data = null) {
        const fullUrl = this.buildRequestUrl(url);
        const response = await fetch(fullUrl, {
            method: 'PUT',
            headers: this.defaultHeaders,
            body: data ? JSON.stringify(data) : null
        });
        return this.handleResponse(response);
    }

    /**
     * 发送 DELETE 请求
     */
    async delete(url) {
        const fullUrl = this.buildRequestUrl(url);
        const response = await fetch(fullUrl, {
            method: 'DELETE',
            headers: this.defaultHeaders
        });
        return this.handleResponse(response);
    }

    /**
     * 处理网络错误
     * 提供清晰的错误提示
     */
    handleNetworkError(error, url) {
        if (error.name === 'TypeError' && error.message.includes('Failed to fetch')) {
            const apiUrl = new URL(url, window.location.href);
            let errorMessage = this.buildFieldNetworkErrorMessage(apiUrl, error);
            if (this.isDeveloperDiagnosticsEnabled()) {
                errorMessage += this.buildDeveloperNetworkErrorMessage(apiUrl);
            }

            console.error('[HttpClient] 连接失败:', errorMessage);
            const wrappedError = new Error(errorMessage);
            wrappedError.diagnosticInfo = errorMessage;
            return wrappedError;
        }
        return error;
    }

    /**
     * 处理 Blob 响应
     */
    async handleBlobResponse(response) {
        if (!response.ok) {
            throw await this.buildHttpError(response);
        }

        return {
            blob: await response.blob(),
            headers: response.headers
        };
    }

    /**
     * 处理响应
     */
    async handleResponse(response) {
        if (!response.ok) {
            throw await this.buildHttpError(response);
        }

        const contentType = response.headers.get('content-type');
        if (contentType && contentType.includes('application/json')) {
            return await response.json();
        }

        return await response.text();
    }

    async buildHttpError(response) {
        const rawBody = (await response.text()).trim();
        let payload = null;
        let message = rawBody || `HTTP ${response.status}`;
        const contentType = response.headers.get('content-type') || '';
        if (rawBody && contentType.includes('application/json')) {
            try {
                payload = JSON.parse(rawBody);
                if (typeof payload === 'string' && payload.trim()) {
                    message = payload.trim();
                } else if (payload && typeof payload === 'object') {
                    const candidate = payload.error
                        || payload.Error
                        || payload.message
                        || payload.Message
                        || payload.publicMessage
                        || payload.PublicMessage
                        || payload.errorCode
                        || payload.ErrorCode;
                    if (typeof candidate === 'string' && candidate.trim()) {
                        message = candidate.trim();
                    }
                }
            } catch (error) {
                console.warn('[HttpClient] Failed to parse JSON error payload:', error);
            }
        }

        return new HttpError(message, {
            status: response.status,
            statusText: response.statusText,
            payload,
            rawBody,
            response
        });
    }

    async extractErrorMessage(response) {
        const rawBody = (await response.text()).trim();
        if (!rawBody) {
            return `HTTP ${response.status}`;
        }

        const contentType = response.headers.get('content-type') || '';
        if (contentType.includes('application/json')) {
            try {
                const payload = JSON.parse(rawBody);
                if (typeof payload === 'string' && payload.trim()) {
                    return payload.trim();
                }

                if (payload && typeof payload === 'object') {
                    const candidate = payload.error
                        || payload.Error
                        || payload.message
                        || payload.Message;
                    if (typeof candidate === 'string' && candidate.trim()) {
                        return candidate.trim();
                    }
                }
            } catch (error) {
                console.warn('[HttpClient] Failed to parse JSON error payload:', error);
            }
        }

        return rawBody;
    }
}

// 创建默认实例
const httpClient = new HttpClient();

export default httpClient;
export { HttpClient, HttpError };
