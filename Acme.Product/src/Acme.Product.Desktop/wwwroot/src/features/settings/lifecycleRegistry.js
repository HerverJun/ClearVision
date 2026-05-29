export class LifecycleRegistry {
    constructor() {
        this.timeouts = new Set();
        this.modals = new Set();
        this.abortControllers = new Set();
        this.objectUrls = new Set();
        this.eventCleanups = new Set();
        this.cleanupCallbacks = new Set();
    }

    trackModal(modal) {
        if (modal) {
            this.modals.add(modal);
        }
        return modal;
    }

    untrackModal(modal) {
        this.modals.delete(modal);
    }

    setTimeout(callback, delay = 0) {
        const timeoutId = window.setTimeout(() => {
            this.timeouts.delete(timeoutId);
            callback();
        }, delay);
        this.timeouts.add(timeoutId);
        return timeoutId;
    }

    clearTimeout(timeoutId) {
        if (!timeoutId) return;
        window.clearTimeout(timeoutId);
        this.timeouts.delete(timeoutId);
    }

    trackAbortController(controller) {
        if (controller) {
            this.abortControllers.add(controller);
        }
        return controller;
    }

    untrackAbortController(controller) {
        this.abortControllers.delete(controller);
    }

    trackObjectUrl(url) {
        if (url) {
            this.objectUrls.add(url);
        }
        return url;
    }

    revokeObjectUrl(url) {
        if (!url) return;
        URL.revokeObjectURL(url);
        this.objectUrls.delete(url);
    }

    trackEvent(target, type, handler, options = undefined) {
        if (!target || typeof target.addEventListener !== 'function' || typeof handler !== 'function') {
            return () => {};
        }

        target.addEventListener(type, handler, options);
        const cleanup = () => {
            target.removeEventListener(type, handler, options);
            this.eventCleanups.delete(cleanup);
        };
        this.eventCleanups.add(cleanup);
        return cleanup;
    }

    onCleanup(callback) {
        if (typeof callback === 'function') {
            this.cleanupCallbacks.add(callback);
        }
        return () => this.cleanupCallbacks.delete(callback);
    }

    clearTransient() {
        this.timeouts.forEach(timeoutId => window.clearTimeout(timeoutId));
        this.timeouts.clear();

        this.abortControllers.forEach(controller => controller.abort());
        this.abortControllers.clear();

        this.objectUrls.forEach(url => URL.revokeObjectURL(url));
        this.objectUrls.clear();

        this.eventCleanups.forEach(cleanup => {
            try {
                cleanup();
            } catch (error) {
                console.warn('[SettingsLifecycle] Event cleanup failed:', error);
            }
        });
        this.eventCleanups.clear();

        this.cleanupCallbacks.forEach(callback => {
            try {
                callback();
            } catch (error) {
                console.warn('[SettingsLifecycle] Cleanup callback failed:', error);
            }
        });
        this.cleanupCallbacks.clear();
    }
}
