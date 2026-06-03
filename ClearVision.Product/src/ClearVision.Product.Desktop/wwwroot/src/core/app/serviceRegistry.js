/**
 * Small runtime registry for long-lived UI services and feature instances.
 *
 * This replaces ad hoc window.* discovery while keeping modules framework-free.
 */
class ServiceRegistry {
    constructor() {
        this.services = new Map();
        this.subscribers = new Map();
    }

    register(key, service) {
        if (!key) {
            throw new Error('Service key is required.');
        }

        this.services.set(key, service);
        this.notify(key, service);
        return service;
    }

    get(key) {
        return this.services.get(key) ?? null;
    }

    require(key) {
        const service = this.get(key);
        if (!service) {
            throw new Error(`Service is not registered: ${key}`);
        }

        return service;
    }

    has(key) {
        return this.services.has(key);
    }

    unregister(key, expectedService = undefined) {
        if (!this.services.has(key)) {
            return false;
        }

        if (expectedService !== undefined && this.services.get(key) !== expectedService) {
            return false;
        }

        this.services.delete(key);
        this.notify(key, null);
        return true;
    }

    subscribe(key, handler, options = {}) {
        if (!key || typeof handler !== 'function') {
            return () => {};
        }

        let subscribersForKey = this.subscribers.get(key);
        if (!subscribersForKey) {
            subscribersForKey = new Set();
            this.subscribers.set(key, subscribersForKey);
        }

        subscribersForKey.add(handler);

        if (options.immediate) {
            handler(this.get(key));
        }

        return () => {
            subscribersForKey.delete(handler);
            if (subscribersForKey.size === 0) {
                this.subscribers.delete(key);
            }
        };
    }

    notify(key, service) {
        const subscribersForKey = this.subscribers.get(key);
        if (!subscribersForKey || subscribersForKey.size === 0) {
            return;
        }

        [...subscribersForKey].forEach((handler) => {
            try {
                handler(service);
            } catch (error) {
                console.error(`[ServiceRegistry] Subscriber failed for ${key}:`, error);
            }
        });
    }

    clear() {
        this.services.clear();
        this.subscribers.clear();
    }
}

const serviceRegistry = new ServiceRegistry();

export { ServiceRegistry };
export default serviceRegistry;
