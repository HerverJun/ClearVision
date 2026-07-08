/**
 * 认证服务 - Token 管理和权限检查
 */

import {
    API_PORT_CANDIDATES,
    DEFAULT_API_PORT,
    buildLocalApiBaseUrl,
    getSavedApiPort,
    isHostInjectedEnvironment,
    saveApiPort
} from '../../core/messaging/apiConfig.js';
import httpClient from '../../core/messaging/httpClient.js';
import { clearAuthSession, getStoredToken, storeAuthSession } from './authStorage.js';

function getAuthWindow() {
    if (typeof window !== 'undefined') {
        return window;
    }

    if (!globalThis.__clearVisionAuthWindow) {
        globalThis.__clearVisionAuthWindow = {
            currentUser: null,
            location: {
                href: 'http://localhost/index.html',
                pathname: '/index.html'
            },
            sessionStorage: null
        };
    }

    return globalThis.__clearVisionAuthWindow;
}

function buildAppUrl(relativePath) {
    return new URL(relativePath, getAuthWindow().location?.href || 'http://localhost/index.html').toString();
}

function isLoginPage() {
    return String(getAuthWindow().location?.pathname || '').includes('/login.html');
}

function applyCurrentUser(user) {
    getAuthWindow().currentUser = user || null;
}

function normalizeServerUser(payload) {
    const source = payload?.user || payload?.User || payload || {};
    const userId = source.userId || source.UserId || source.id || source.Id || '';
    const username = source.username || source.Username || '';
    const displayName = source.displayName || source.DisplayName || username;
    const role = source.role || source.Role || '';

    if (!userId || !username || !role) {
        return null;
    }

    return {
        id: userId,
        userId,
        username,
        displayName,
        role
    };
}

async function applyAuthenticatedUserResponse(response, token) {
    if (!response.ok) {
        return null;
    }

    const payload = await response.json();
    const user = normalizeServerUser(payload);
    if (!user) {
        return null;
    }

    storeAuthSession(token, user);
    applyCurrentUser(user);
    return user;
}

function clearCurrentUser() {
    applyCurrentUser(null);
}

const LOGOUT_NOTICE_KEY = 'cv_logout_notice';

function writeLogoutNotice(message) {
    const authWindow = getAuthWindow();
    try {
        if (!message) {
            authWindow.sessionStorage?.removeItem(LOGOUT_NOTICE_KEY);
            return;
        }

        authWindow.sessionStorage?.setItem(LOGOUT_NOTICE_KEY, message);
    } catch {
        // Ignore storage failures and continue logout flow.
    }
}

function redirectToLogin() {
    if (!isLoginPage()) {
        getAuthWindow().location.href = buildAppUrl('./login.html');
    }
}

function resetAuthState() {
    clearAuthSession();
    clearCurrentUser();
}

export function getToken() {
    return getStoredToken();
}

export function getCurrentUser() {
    return getAuthWindow().currentUser || null;
}

export function isAuthenticated() {
    return !!getToken();
}

export function hasRole(role) {
    const user = getCurrentUser();
    return user && user.role === role;
}

export function isAdmin() {
    return hasRole('Admin');
}

export function isEngineer() {
    return hasRole('Engineer') || hasRole('Admin');
}

export function isOperator() {
    const user = getCurrentUser();
    return user && (user.role === 'Operator' || user.role === 'Engineer' || user.role === 'Admin');
}

export async function logout() {
    try {
        if (getToken()) {
            await httpClient.post('/auth/logout');
        }
    } catch (error) {
        console.warn('[Auth] 服务端登出失败，将继续清理本地会话。', error);
        writeLogoutNotice('服务端登出失败，但本地会话已清理。若其他终端仍在线，请稍后确认会话状态。');
    } finally {
        clearAuthSession();
        getAuthWindow().location.href = buildAppUrl('./login.html');
    }
}

export function consumeLogoutNotice() {
    const authWindow = getAuthWindow();
    try {
        const message = authWindow.sessionStorage?.getItem(LOGOUT_NOTICE_KEY) || '';
        authWindow.sessionStorage?.removeItem(LOGOUT_NOTICE_KEY);
        return message;
    } catch {
        return '';
    }
}

export function getAuthHeaders() {
    const token = getToken();
    return token ? { Authorization: `Bearer ${token}` } : {};
}

const SESSION_INVALID_NOTICE = '登录状态无效，请重新登录。';
let unauthorizedHandlerInstalled = false;
let unauthorizedHandling = false;

/**
 * 处理全局未授权（401）信号：清理本地会话，写入提示，并引导用户重新登录。
 * 通过 guard 防止并发的多个 401 触发重复跳转，也避免在登录页上再次跳转造成循环。
 */
export function handleUnauthorized() {
    if (unauthorizedHandling || isLoginPage()) {
        return;
    }

    unauthorizedHandling = true;
    resetAuthState();
    writeLogoutNotice(SESSION_INVALID_NOTICE);
    redirectToLogin();
}

/**
 * 安装全局未授权监听器（幂等）。应在应用启动时调用一次。
 */
export function installUnauthorizedHandler() {
    if (unauthorizedHandlerInstalled) {
        return;
    }

    const authWindow = getAuthWindow();
    if (typeof authWindow.addEventListener !== 'function') {
        return;
    }

    authWindow.addEventListener('clearvision:auth-unauthorized', () => handleUnauthorized());
    unauthorizedHandlerInstalled = true;
}

export const PermissionGuard = {
    canEdit() {
        return isEngineer();
    },

    canManageUsers() {
        return isAdmin();
    },

    canViewSettings() {
        return isEngineer();
    },

    canRunInspection() {
        return isOperator();
    }
};

export function initAuth() {
    const token = getToken();

    if (!token) {
        resetAuthState();
        redirectToLogin();
        return null;
    }

    return true;
}

export async function bootstrapAuthSession({ redirectOnFailure = true } = {}) {
    const token = getToken();

    if (!token) {
        resetAuthState();
        if (redirectOnFailure) {
            redirectToLogin();
        }

        return {
            ok: false,
            reason: 'missing-session',
            user: null
        };
    }

    const refreshedUser = await refreshAuthenticatedUserAsync();
    if (!refreshedUser) {
        resetAuthState();
        if (redirectOnFailure) {
            redirectToLogin();
        }

        return {
            ok: false,
            reason: 'invalid-session',
            user: null
        };
    }

    return {
        ok: true,
        reason: 'authenticated',
        user: refreshedUser
    };
}

export async function validateTokenAsync() {
    return !!(await refreshAuthenticatedUserAsync());
}

async function refreshAuthenticatedUserAsync() {
    const token = getToken();
    if (!token) return null;

    try {
        const authWindow = getAuthWindow();
        if (authWindow.__API_BASE_URL__) {
            const response = await fetch(`${authWindow.__API_BASE_URL__}/auth/me`, {
                method: 'GET',
                headers: { Authorization: `Bearer ${token}` }
            });
            return await applyAuthenticatedUserResponse(response, token);
        }

        if (isHostInjectedEnvironment()) {
            const candidatePorts = [];
            const savedPort = getSavedApiPort();

            if (savedPort) {
                candidatePorts.push(savedPort);
            }

            API_PORT_CANDIDATES
                .filter(port => port !== savedPort)
                .forEach(port => candidatePorts.push(port));

            for (const port of candidatePorts) {
                try {
                    const response = await fetch(`${buildLocalApiBaseUrl(port)}/auth/me`, {
                        method: 'GET',
                        headers: { Authorization: `Bearer ${token}` }
                    });

                    if (response.ok) {
                        saveApiPort(port);
                        return await applyAuthenticatedUserResponse(response, token);
                    }
                } catch {
                    // Try the next candidate port.
                }
            }

            return null;
        }

        const { protocol, hostname, port } = authWindow.location;
        const response = await fetch(`${protocol}//${hostname}:${port || DEFAULT_API_PORT}/api/auth/me`, {
            method: 'GET',
            headers: { Authorization: `Bearer ${token}` }
        });
        return await applyAuthenticatedUserResponse(response, token);
    } catch (e) {
        console.warn('[Auth] Token 验证请求失败:', e.message);
        return null;
    }
}

applyCurrentUser(null);
