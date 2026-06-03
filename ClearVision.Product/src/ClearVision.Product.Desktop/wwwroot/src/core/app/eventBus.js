/**
 * Lightweight in-process event bus for frontend feature coordination.
 *
 * Event names are intentionally plain strings such as:
 * - view:changed
 * - project:changed
 * - inspection:result
 * - inspection:error
 * - flow:changed
 * - ai:applied
 */
class EventBus {
    constructor() {
        this.handlers = new Map();
    }

    on(eventName, handler) {
        if (!eventName || typeof handler !== 'function') {
            return () => {};
        }

        let handlersForEvent = this.handlers.get(eventName);
        if (!handlersForEvent) {
            handlersForEvent = new Set();
            this.handlers.set(eventName, handlersForEvent);
        }

        handlersForEvent.add(handler);
        return () => this.off(eventName, handler);
    }

    once(eventName, handler) {
        if (!eventName || typeof handler !== 'function') {
            return () => {};
        }

        const unsubscribe = this.on(eventName, (payload) => {
            unsubscribe();
            handler(payload);
        });

        return unsubscribe;
    }

    off(eventName, handler = null) {
        const handlersForEvent = this.handlers.get(eventName);
        if (!handlersForEvent) {
            return;
        }

        if (!handler) {
            this.handlers.delete(eventName);
            return;
        }

        handlersForEvent.delete(handler);
        if (handlersForEvent.size === 0) {
            this.handlers.delete(eventName);
        }
    }

    emit(eventName, payload = null) {
        const handlersForEvent = this.handlers.get(eventName);
        if (!handlersForEvent || handlersForEvent.size === 0) {
            return;
        }

        [...handlersForEvent].forEach((handler) => {
            try {
                handler(payload);
            } catch (error) {
                console.error(`[EventBus] Handler failed for ${eventName}:`, error);
            }
        });
    }

    clear() {
        this.handlers.clear();
    }
}

const eventBus = new EventBus();

export { EventBus };
export default eventBus;
