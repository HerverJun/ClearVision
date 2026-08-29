export const LOCAL_DRAFT_SCHEMA = 'clearvision.local-project-draft';
export const LOCAL_DRAFT_VERSION = 1;
export const LEGACY_LOCAL_DRAFT_KEY = 'cv_autosave_backup';

const LOCAL_DRAFT_KEY_PREFIX = `cv_local_draft:v${LOCAL_DRAFT_VERSION}`;

function normalizeStableId(value) {
    if (typeof value !== 'string') {
        return null;
    }

    const normalized = value.trim();
    return normalized && normalized.length <= 256 ? normalized : null;
}

function resolveUserId(user) {
    return normalizeStableId(user?.userId ?? user?.id ?? null);
}

function defaultStorageProvider() {
    try {
        return globalThis.window?.localStorage ?? globalThis.localStorage ?? null;
    } catch {
        return null;
    }
}

function defaultUserProvider() {
    return globalThis.window?.currentUser ?? null;
}

function isObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isValidTimestamp(value) {
    return typeof value === 'string' &&
        value.length > 0 &&
        value.length <= 128 &&
        Number.isFinite(Date.parse(value));
}

export function buildLocalDraftStorageKey(userId, projectId) {
    const owner = normalizeStableId(userId);
    const project = normalizeStableId(projectId);
    if (!owner || !project) {
        return null;
    }

    return `${LOCAL_DRAFT_KEY_PREFIX}:${encodeURIComponent(owner)}:${encodeURIComponent(project)}`;
}

export class LocalDraftStorage {
    constructor({
        storageProvider = defaultStorageProvider,
        userProvider = defaultUserProvider,
        nowProvider = () => new Date().toISOString()
    } = {}) {
        this.storageProvider = storageProvider;
        this.userProvider = userProvider;
        this.nowProvider = nowProvider;
    }

    purgeOwnerlessLegacyDraft() {
        const storage = this.#getStorage();
        if (!storage) {
            return false;
        }

        try {
            const existed = storage.getItem(LEGACY_LOCAL_DRAFT_KEY) !== null;
            storage.removeItem(LEGACY_LOCAL_DRAFT_KEY);
            return existed;
        } catch {
            return false;
        }
    }

    read(projectId) {
        const context = this.#resolveContext(projectId);
        this.purgeOwnerlessLegacyDraft();
        if (!context) {
            return null;
        }

        const { storage, userId, normalizedProjectId, key } = context;
        try {
            const raw = storage.getItem(key);
            if (!raw) {
                return null;
            }

            const payload = JSON.parse(raw);
            if (!this.#isValidPayload(payload, userId, normalizedProjectId)) {
                storage.removeItem(key);
                return null;
            }

            return payload;
        } catch {
            try {
                storage.removeItem(key);
            } catch {
                // Storage may have become unavailable; reading still fails closed.
            }
            return null;
        }
    }

    write(project, flow, { source = 'timer', nodeCount = null } = {}) {
        const projectId = normalizeStableId(project?.id ?? project?.projectId ?? null);
        const context = this.#resolveContext(projectId);
        this.purgeOwnerlessLegacyDraft();
        if (!context || !isObject(flow)) {
            return null;
        }

        let timestamp;
        try {
            timestamp = this.nowProvider?.();
        } catch {
            return null;
        }

        if (!isValidTimestamp(timestamp)) {
            return null;
        }

        const payload = {
            schema: LOCAL_DRAFT_SCHEMA,
            version: LOCAL_DRAFT_VERSION,
            userId: context.userId,
            projectId: context.normalizedProjectId,
            projectName: typeof project?.name === 'string' ? project.name : '',
            timestamp,
            source: typeof source === 'string' ? source : 'timer',
            nodeCount: Number.isFinite(nodeCount) ? nodeCount : null,
            flow
        };

        try {
            context.storage.setItem(context.key, JSON.stringify(payload));
            return payload;
        } catch {
            return null;
        }
    }

    clear(projectId) {
        const context = this.#resolveContext(projectId);
        this.purgeOwnerlessLegacyDraft();
        if (!context) {
            return false;
        }

        try {
            const existed = context.storage.getItem(context.key) !== null;
            context.storage.removeItem(context.key);
            return existed;
        } catch {
            return false;
        }
    }

    #getStorage() {
        try {
            const storage = this.storageProvider?.();
            return storage &&
                typeof storage.getItem === 'function' &&
                typeof storage.setItem === 'function' &&
                typeof storage.removeItem === 'function'
                ? storage
                : null;
        } catch {
            return null;
        }
    }

    #resolveContext(projectId) {
        const storage = this.#getStorage();
        let user;
        try {
            user = this.userProvider?.();
        } catch {
            return null;
        }

        const userId = resolveUserId(user);
        const normalizedProjectId = normalizeStableId(projectId);
        const key = buildLocalDraftStorageKey(userId, normalizedProjectId);
        if (!storage || !userId || !normalizedProjectId || !key) {
            return null;
        }

        return { storage, userId, normalizedProjectId, key };
    }

    #isValidPayload(payload, userId, projectId) {
        return isObject(payload) &&
            payload.schema === LOCAL_DRAFT_SCHEMA &&
            payload.version === LOCAL_DRAFT_VERSION &&
            payload.userId === userId &&
            payload.projectId === projectId &&
            isObject(payload.flow) &&
            isValidTimestamp(payload.timestamp);
    }
}

export const localDraftStorage = new LocalDraftStorage();

export default localDraftStorage;
