export const AGENT_ACTION_EVENT = 'clearvision:agent-action';

export function dispatchAgentAction(actionType, payload = null, options = {}) {
    const normalizedType = String(actionType || '').trim();
    if (!normalizedType) {
        return { accepted: false, detail: null, eventName: AGENT_ACTION_EVENT };
    }

    const detail = {
        actionType: normalizedType,
        payload,
        title: String(options.title || '').trim(),
        summary: String(options.summary || '').trim(),
        source: String(options.source || 'aiPanel').trim(),
        requiresUserConfirmation: Boolean(options.requiresUserConfirmation ?? true),
        timestampUtc: new Date().toISOString()
    };

    const event = new CustomEvent(AGENT_ACTION_EVENT, {
        detail,
        bubbles: false,
        cancelable: true
    });

    return {
        accepted: window.dispatchEvent(event),
        detail,
        eventName: AGENT_ACTION_EVENT
    };
}

export function onAgentAction(handler) {
    if (typeof handler !== 'function') {
        return () => {};
    }

    const listener = (event) => handler(event.detail, event);
    window.addEventListener(AGENT_ACTION_EVENT, listener);
    return () => window.removeEventListener(AGENT_ACTION_EVENT, listener);
}
