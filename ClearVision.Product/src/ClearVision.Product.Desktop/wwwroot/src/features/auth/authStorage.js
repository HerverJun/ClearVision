const TOKEN_KEY = 'cv_auth_token';
const USER_KEY = 'cv_current_user';
const AUTH_BINDING_CHANGED_EVENT = 'clearvision:auth-binding-changed';

function notifyAuthBindingChanged() {
    try {
        window.dispatchEvent(new CustomEvent(AUTH_BINDING_CHANGED_EVENT));
    } catch {
        // Storage remains authoritative when DOM events are unavailable.
    }
}

function safeParseJson(value, fallback = null) {
    if (!value) {
        return fallback;
    }

    try {
        return JSON.parse(value);
    } catch {
        const sessionStore = getSessionStore();
        const localStore = getLocalStore();
        sessionStore?.removeItem(USER_KEY);
        localStore?.removeItem(USER_KEY);
        return fallback;
    }
}

function getSessionStore() {
    try {
        return window.sessionStorage;
    } catch {
        return null;
    }
}

function getLocalStore() {
    try {
        return window.localStorage;
    } catch {
        return null;
    }
}

function migrateLegacyValue(key) {
    const sessionStore = getSessionStore();
    const localStore = getLocalStore();

    const sessionValue = sessionStore?.getItem(key);
    if (sessionValue) {
        return sessionValue;
    }

    const legacyValue = localStore?.getItem(key);
    if (!legacyValue) {
        return null;
    }

    sessionStore?.setItem(key, legacyValue);
    localStore?.removeItem(key);
    return legacyValue;
}

export function getStoredToken() {
    return migrateLegacyValue(TOKEN_KEY);
}

export function getStoredUser() {
    const userJson = migrateLegacyValue(USER_KEY);
    return safeParseJson(userJson);
}

export function storeAuthSession(token, user) {
    const sessionStore = getSessionStore();
    const localStore = getLocalStore();

    if (token) {
        sessionStore?.setItem(TOKEN_KEY, token);
        localStore?.removeItem(TOKEN_KEY);
    }

    if (user) {
        sessionStore?.setItem(USER_KEY, JSON.stringify(user));
        localStore?.removeItem(USER_KEY);
    }

    notifyAuthBindingChanged();
}

export function clearAuthSession() {
    const sessionStore = getSessionStore();
    const localStore = getLocalStore();

    sessionStore?.removeItem(TOKEN_KEY);
    sessionStore?.removeItem(USER_KEY);
    localStore?.removeItem(TOKEN_KEY);
    localStore?.removeItem(USER_KEY);
    notifyAuthBindingChanged();
}

