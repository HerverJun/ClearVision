export function validateIntegerValue(rawValue, label, { min = Number.MIN_SAFE_INTEGER, max = Number.MAX_SAFE_INTEGER } = {}) {
    const raw = String(rawValue ?? '').trim();
    if (!/^-?\d+$/.test(raw)) {
        return { ok: false, message: `${label}必须填写整数。` };
    }

    const value = Number.parseInt(raw, 10);
    if (!Number.isSafeInteger(value) || value < min || value > max) {
        return { ok: false, message: `${label}必须在 ${min} - ${max} 范围内。` };
    }

    return { ok: true, value };
}

export function validateFloatValue(rawValue, label, { min = Number.NEGATIVE_INFINITY, max = Number.POSITIVE_INFINITY } = {}) {
    const raw = String(rawValue ?? '').trim();
    if (!raw) {
        return { ok: false, message: `${label}不能为空。` };
    }

    const value = Number.parseFloat(raw);
    if (!Number.isFinite(value) || value < min || value > max) {
        return { ok: false, message: `${label}必须在 ${min} - ${max} 范围内。` };
    }

    return { ok: true, value };
}

export function assertValidation(result) {
    if (!result?.ok) {
        throw new Error(result?.message || '配置校验失败。');
    }

    return result.value;
}

export function validateAiModelDraftPayload(payload) {
    if (!String(payload.name || '').trim()) {
        return { ok: false, message: '请填写模型昵称。' };
    }

    if (!String(payload.model || '').trim()) {
        return { ok: false, message: '请填写模型标识。' };
    }

    const timeoutMs = Number(payload.timeoutMs);
    if (!Number.isInteger(timeoutMs) || timeoutMs < 1000 || timeoutMs > 600000) {
        return { ok: false, message: 'AI 请求超时必须是 1000 - 600000 ms 之间的整数。' };
    }

    const baseUrl = String(payload.baseUrl || '').trim();
    if (baseUrl) {
        try {
            const parsed = new URL(baseUrl);
            if (!['http:', 'https:'].includes(parsed.protocol)) {
                return { ok: false, message: 'API Endpoint 必须是有效的 http/https URL，或留空使用默认地址。' };
            }
        } catch {
            return { ok: false, message: 'API Endpoint 必须是有效的 http/https URL，或留空使用默认地址。' };
        }
    }

    return { ok: true, value: payload };
}
