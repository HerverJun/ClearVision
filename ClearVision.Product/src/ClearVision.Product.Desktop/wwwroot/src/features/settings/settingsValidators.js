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

export function validatePlcConnectionDraft(payload) {
    const protocol = String(payload?.protocol || 'S7').trim().toUpperCase();
    const ipAddress = String(payload?.ipAddress || '').trim();
    if (!ipAddress) {
        return { ok: false, message: '请填写 PLC IP 地址。' };
    }

    const port = validateIntegerValue(payload?.port, 'PLC 端口', { min: 1, max: 65535 });
    if (!port.ok) {
        return port;
    }

    const value = { ipAddress, port: port.value };
    if (protocol === 'S7') {
        const rack = validateIntegerValue(payload?.rack, 'S7 Rack', { min: 0, max: 15 });
        if (!rack.ok) {
            return rack;
        }

        const slot = validateIntegerValue(payload?.slot, 'S7 Slot', { min: 0, max: 15 });
        if (!slot.ok) {
            return slot;
        }

        value.rack = rack.value;
        value.slot = slot.value;
    }

    return { ok: true, value };
}

export function validateStationCommunicationDraft(payload) {
    const mode = String(payload?.mode || 'Disabled');
    const parsedPort = Number.parseInt(`${payload?.port ?? ''}`, 10);
    if (!Number.isFinite(parsedPort) || parsedPort < 1 || parsedPort > 65535) {
        return { ok: false, message: '端口必须是 1-65535 之间的整数' };
    }

    const lanHost = String(payload?.lanHost || '').trim();
    if (mode !== 'Disabled' && lanHost && (
        /^https?:\/\//i.test(lanHost) ||
        /\s/.test(lanHost) ||
        /[\\/?#@]/.test(lanHost)
    )) {
        return { ok: false, message: 'LAN 主机名/IP 只能填写主机名或 IP，不要包含 http://、路径、空格或特殊符号。' };
    }

    return { ok: true, value: { mode, port: parsedPort, lanHost } };
}

export function validateTcpProfileDraft(profile) {
    const mode = String(profile?.mode || 'Client').trim();
    if (!['Client', 'Server'].includes(mode)) {
        return { ok: false, message: 'TCP 模式必须是 Client 或 Server。' };
    }

    const validateIp = (value, label) => {
        const text = String(value || '').trim();
        if (!text || text.toLowerCase() === 'localhost') {
            return text ? { ok: true, value: text } : { ok: false, message: `${label}不能为空。` };
        }

        if (!/^(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}$/.test(text)) {
            return { ok: false, message: `${label}必须是有效 IP 地址。` };
        }

        return { ok: true, value: text };
    };

    const name = String(profile?.name || '').trim();
    if (!name) {
        return { ok: false, message: 'TCP Profile 名称不能为空。' };
    }

    const host = mode === 'Server'
        ? validateIp(profile?.localHost, '本地监听 IP')
        : validateIp(profile?.remoteHost, '远端 IP');
    if (!host.ok) {
        return host;
    }

    const port = validateIntegerValue(
        mode === 'Server' ? profile?.localPort : profile?.remotePort,
        mode === 'Server' ? '本地监听端口' : '远端端口',
        { min: 1, max: 65535 });
    if (!port.ok) {
        return port;
    }

    const timeout = validateIntegerValue(profile?.timeoutMs, '超时时间', { min: 100, max: 600000 });
    if (!timeout.ok) {
        return timeout;
    }

    const encoding = String(profile?.encoding || 'UTF8').trim().toUpperCase();
    if (!['UTF8', 'ASCII', 'GBK', 'HEX'].includes(encoding)) {
        return { ok: false, message: '编码必须是 UTF8、ASCII、GBK 或 HEX。' };
    }

    const frameMode = String(profile?.frameMode || 'Raw').trim();
    if (!['Raw', 'Line', 'FixedLength', 'Hex'].includes(frameMode)) {
        return { ok: false, message: '报文模式必须是 Raw、Line、FixedLength 或 Hex。' };
    }

    if (frameMode === 'FixedLength') {
        const fixedLength = validateIntegerValue(profile?.fixedLength, '固定长度', { min: 1, max: 1048576 });
        if (!fixedLength.ok) {
            return fixedLength;
        }
    }

    const lineEnding = String(profile?.lineEnding || 'None').trim();
    if (!['None', 'CR', 'LF', 'CRLF'].includes(lineEnding)) {
        return { ok: false, message: '行结束符必须是 None、CR、LF 或 CRLF。' };
    }

    return { ok: true, value: profile };
}

export function validateCameraParameterDraft(payload) {
    if (!Number.isFinite(payload?.exposureTimeUs) || payload.exposureTimeUs < 10 || payload.exposureTimeUs > 1000000) {
        return { ok: false, message: '曝光时间需在 10 - 1000000 µs 范围内' };
    }

    if (!Number.isFinite(payload?.gainDb) || payload.gainDb < 0 || payload.gainDb > 24) {
        return { ok: false, message: '增益需在 0.0 - 24.0 dB 范围内' };
    }

    if (!Number.isInteger(payload?.enterPhotoelectricDebounceMs) || payload.enterPhotoelectricDebounceMs < 0 || payload.enterPhotoelectricDebounceMs > 5000) {
        return { ok: false, message: '回车防抖时间需在 0 - 5000 ms 范围内' };
    }

    if (!Number.isInteger(payload?.enterPhotoelectricTimeoutMs) || payload.enterPhotoelectricTimeoutMs < 100 || payload.enterPhotoelectricTimeoutMs > 600000) {
        return { ok: false, message: '回车等待超时需在 100 - 600000 ms 范围内' };
    }

    if (!Number.isInteger(payload?.serialPhotoelectricBaudRate) || payload.serialPhotoelectricBaudRate <= 0) {
        return { ok: false, message: '串口波特率必须是大于 0 的整数' };
    }

    if (!Number.isInteger(payload?.serialPhotoelectricDebounceMs) || payload.serialPhotoelectricDebounceMs < 0 || payload.serialPhotoelectricDebounceMs > 5000) {
        return { ok: false, message: '串口防抖时间需在 0 - 5000 ms 范围内' };
    }

    if (!Number.isInteger(payload?.serialPhotoelectricTimeoutMs) || payload.serialPhotoelectricTimeoutMs < 100 || payload.serialPhotoelectricTimeoutMs > 600000) {
        return { ok: false, message: '串口等待超时需在 100 - 600000 ms 范围内' };
    }

    if (payload?.softwareTriggerSource === 'SerialPhotoelectric' && !/^COM\d+$/i.test(String(payload?.serialPhotoelectricPortName || '').trim())) {
        return { ok: false, message: '串口光电触发需要填写类似 COM3 的串口号' };
    }

    if (payload?.triggerMode !== 'Software' && (!Number.isInteger(payload?.targetFrameRateFps) || payload.targetFrameRateFps < 1 || payload.targetFrameRateFps > 120)) {
        return { ok: false, message: '采集帧率需在 1 - 120 fps 范围内' };
    }

    return { ok: true, value: payload };
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

    const protocol = String(payload.protocol || 'openai_compatible').trim().toLowerCase();
    if (!['openai_compatible', 'anthropic', 'azure_openai', 'ollama_native'].includes(protocol)) {
        return { ok: false, message: 'AI Protocol must be openai_compatible, anthropic, azure_openai, or ollama_native.' };
    }

    const wireApi = String(payload.wireApi || 'chat_completions').trim().toLowerCase();
    if (!['chat_completions', 'responses'].includes(wireApi)) {
        return { ok: false, message: 'WireApi must be chat_completions or responses.' };
    }

    const authMode = String(payload.authMode || 'bearer').trim().toLowerCase();
    if (!['bearer', 'header_key', 'none'].includes(authMode)) {
        return { ok: false, message: 'AuthMode must be bearer, header_key, or none.' };
    }

    const apiKeyOperation = String(payload.apiKeyOperation || 'keep').trim().toLowerCase().replace(/_/g, '-');
    if (!['keep', 'replace', 'clear', 'new'].includes(apiKeyOperation)) {
        return { ok: false, message: 'ApiKey operation must be keep, replace, clear, or new.' };
    }

    if ((apiKeyOperation === 'replace' || apiKeyOperation === 'new') && !String(payload.apiKey || '').trim()) {
        return { ok: false, message: 'A replacement API key is required for this key operation.' };
    }

    const roles = Array.isArray(payload.roleBindings) ? payload.roleBindings : [];
    const allowedRoles = ['generation', 'planner', 'vision-agent-shadow-eval', 'reasoning', 'fallback', 'validation', 'vision'];
    if (roles.some(role => !allowedRoles.includes(String(role || '').trim().toLowerCase().replace(/_/g, '-')))) {
        return { ok: false, message: 'ModelRole contains an unsupported role binding.' };
    }

    if (payload.priority !== undefined && payload.priority !== null) {
        const priority = Number(payload.priority);
        if (!Number.isInteger(priority) || priority < 1 || priority > 10000) {
            return { ok: false, message: 'Priority must be an integer from 1 to 10000.' };
        }
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
