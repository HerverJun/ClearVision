export function buildSseHeaders(token, lastEventId = null) {
    const headers = token ? { Authorization: `Bearer ${token}` } : {};
    if (lastEventId) {
        headers['Last-Event-ID'] = String(lastEventId);
    }

    return headers;
}

export function buildSseUrl(url, lastEventId = null) {
    const cursor = String(lastEventId ?? '').trim();
    if (!cursor) {
        return url;
    }

    const separator = String(url || '').includes('?') ? '&' : '?';
    return `${url}${separator}lastEventId=${encodeURIComponent(cursor)}`;
}

export function parseSseFrame(frame) {
    if (!frame || !frame.trim()) {
        return null;
    }

    let eventName = 'message';
    let eventId = null;
    const dataLines = [];

    frame.split('\n').forEach((line) => {
        if (!line || line.startsWith(':')) {
            return;
        }

        const colonIndex = line.indexOf(':');
        const field = colonIndex >= 0 ? line.slice(0, colonIndex) : line;
        const rawValue = colonIndex >= 0 ? line.slice(colonIndex + 1) : '';
        const value = rawValue.startsWith(' ') ? rawValue.slice(1) : rawValue;

        if (field === 'event') {
            eventName = value;
        } else if (field === 'id') {
            eventId = value;
        } else if (field === 'data') {
            dataLines.push(value);
        }
    });

    if (dataLines.length === 0) {
        return null;
    }

    return {
        eventName,
        eventId,
        payload: JSON.parse(dataLines.join('\n'))
    };
}
