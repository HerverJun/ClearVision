/**
 * HTTP API 客户端
 * 用于与后端 Minimal APIs 通信
 */

class HttpClient {
    constructor(baseUrl = '') {
        this.baseUrl = baseUrl;
        this.defaultHeaders = {
            'Content-Type': 'application/json'
        };
    }

    /**
     * 发送 GET 请求
     */
    async get(url, params = null) {
        const queryString = params ? '?' + new URLSearchParams(params).toString() : '';
        const response = await fetch(this.baseUrl + url + queryString, {
            method: 'GET',
            headers: this.defaultHeaders
        });
        return this.handleResponse(response);
    }

    /**
     * 发送 POST 请求
     */
    async post(url, data = null) {
        const response = await fetch(this.baseUrl + url, {
            method: 'POST',
            headers: this.defaultHeaders,
            body: data ? JSON.stringify(data) : null
        });
        return this.handleResponse(response);
    }

    /**
     * 发送 PUT 请求
     */
    async put(url, data = null) {
        const response = await fetch(this.baseUrl + url, {
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
        const response = await fetch(this.baseUrl + url, {
            method: 'DELETE',
            headers: this.defaultHeaders
        });
        return this.handleResponse(response);
    }

    /**
     * 处理响应
     */
    async handleResponse(response) {
        if (!response.ok) {
            const error = await response.text();
            throw new Error(`HTTP ${response.status}: ${error}`);
        }

        const contentType = response.headers.get('content-type');
        if (contentType && contentType.includes('application/json')) {
            return await response.json();
        }

        return await response.text();
    }
}

// 创建默认实例
const httpClient = new HttpClient('/api');

export default httpClient;
export { HttpClient };
