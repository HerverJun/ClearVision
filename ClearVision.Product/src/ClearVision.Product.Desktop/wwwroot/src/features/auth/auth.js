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

const authContextSubscribers = new Set();

function getStableUserId(user) {
    const value = user?.userId ?? user?.id ?? null;
    if (typeof value !== 'string') {
        return null;
    }

    const normalized = value.trim();
    return normalized || null;
}

function applyCurrentUser(user) {
    const authWindow = getAuthWindow();
    const previousUser = authWindow.currentUser || null;
    const nextUser = user || null;
    const previousUserId = getStableUserId(previousUser);
    const nextUserId = getStableUserId(nextUser);

    authWindow.currentUser = nextUser;
    if (previousUserId === nextUserId) {
        return;
    }

    for (const subscriber of [...authContextSubscribers]) {
        try {
            subscriber(nextUser, previousUser);
        } catch (error) {
            console.error('[Auth] 认证上下文订阅回调失败:', error);
        }
    }
}

export const Capabilities = Object.freeze({
    PROJECT_EDIT: 'project.edit',
    INSPECTION_RESULTS_READ: 'inspection.results.read',
    STATION_COMMANDS_CREATE: 'station.commands.create',
    STATION_PACKAGES_READ: 'station.packages.read',
    STATION_PACKAGES_DEPLOY: 'station.packages.deploy',
    STATION_TEST_PACKAGES_CREATE: 'station.test-packages.create',
    SETTINGS_UPDATE: 'settings.update',
    SETTINGS_RESET: 'settings.reset',
    PLC_SETTINGS_UPDATE: 'plc.settings.update',
    PLC_MAPPINGS_UPDATE: 'plc.mappings.update',
    PLC_CONNECTION_TEST: 'plc.connection.test',
    TCP_PROFILES_UPDATE: 'tcp.profiles.update',
    TCP_CONNECTIONS_OPERATE: 'tcp.connections.operate',
    STATION_COMMUNICATION_UPDATE: 'station.communication.update',
    STATION_COMMUNICATION_TOKEN_MANAGE: 'station.communication-token.manage',
    CAMERA_BINDINGS_UPDATE: 'cameras.bindings.update',
    CAMERA_CAPTURE: 'cameras.capture',
    CAMERA_PREVIEW_OPERATE: 'cameras.preview.operate',
    TRIGGER_INPUT_OPERATE: 'trigger-input.operate',
    AI_MODELS_CREATE: 'ai.models.create',
    AI_MODELS_UPDATE: 'ai.models.update',
    AI_MODELS_DELETE: 'ai.models.delete',
    AI_MODELS_ACTIVATE: 'ai.models.activate',
    AI_MODELS_SET_DEFAULT: 'ai.models.set-default',
    AI_MODELS_TEST: 'ai.models.test',
    DATABASE_STATUS_READ: 'database.status.read',
    DATABASE_BACKUP: 'database.backup',
    DATABASE_REPAIR: 'database.repair',
    DATABASE_RESTORE: 'database.restore',
    DATABASE_CLEANUP: 'database.cleanup',
    USERS_READ: 'users.read',
    USERS_CREATE: 'users.create',
    USERS_UPDATE: 'users.update',
    USERS_DELETE: 'users.delete',
    USERS_RESET_PASSWORD: 'users.reset-password'
});

function normalizeCapabilities(value) {
    if (!Array.isArray(value)) {
        return [];
    }

    return [...new Set(value
        .filter(item => typeof item === 'string')
        .map(item => item.trim())
        .filter(Boolean))]
        .sort((left, right) => left.localeCompare(right, 'en'));
}

function normalizePasswordPolicy(value) {
    const source = value && typeof value === 'object' ? value : {};
    const minimumLength = Number(source.minimumLength ?? source.MinimumLength);
    return {
        minimumLength: Number.isInteger(minimumLength) && minimumLength > 0
            ? minimumLength
            : null
    };
}

export function normalizeAuthenticatedContext(payload) {
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
        role,
        capabilities: normalizeCapabilities(source.capabilities ?? source.Capabilities),
        passwordPolicy: normalizePasswordPolicy(source.passwordPolicy ?? source.PasswordPolicy)
    };
}

async function applyAuthenticatedUserResponse(response, token) {
    if (!response.ok) {
        return null;
    }

    const payload = await response.json();
    const user = normalizeAuthenticatedContext(payload);
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

export function subscribeAuthContext(callback) {
    if (typeof callback !== 'function') {
        throw new TypeError('认证上下文订阅者必须是函数。');
    }

    authContextSubscribers.add(callback);
    return () => authContextSubscribers.delete(callback);
}

export function isAuthenticated() {
    return !!getToken();
}

export function hasRole(role) {
    const user = getCurrentUser();
    return user && user.role === role;
}

export function hasCapability(capability) {
    if (typeof capability !== 'string' || !capability) {
        return false;
    }

    const capabilities = getCurrentUser()?.capabilities;
    return Array.isArray(capabilities) && capabilities.includes(capability);
}

export function isAdmin() {
    return hasCapability(Capabilities.USERS_READ);
}

export function isEngineer() {
    return hasCapability(Capabilities.PROJECT_EDIT);
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
        resetAuthState();
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
    has(capability) {
        return hasCapability(capability);
    },

    canEdit() {
        return hasCapability(Capabilities.PROJECT_EDIT);
    },

    canManageUsers() {
        return hasCapability(Capabilities.USERS_READ);
    },

    canViewSettings() {
        return hasCapability(Capabilities.SETTINGS_UPDATE) ||
            hasCapability(Capabilities.PLC_CONNECTION_TEST) ||
            hasCapability(Capabilities.CAMERA_BINDINGS_UPDATE);
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
