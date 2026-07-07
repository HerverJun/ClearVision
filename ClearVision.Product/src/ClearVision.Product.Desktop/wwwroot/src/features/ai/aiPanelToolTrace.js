function normalizeToolStatus(item) {
    const statusValue = item?.status ?? item?.Status;
    const normalizedStatus = String(statusValue ?? '').trim().toLowerCase();
    if (normalizedStatus) {
        switch (normalizedStatus) {
            case 'completed':
            case 'succeeded':
            case 'success':
                return { kind: 'success', label: '已完成', title: normalizedStatus, success: true };
            case 'warning':
                return { kind: 'warning', label: '警告', title: normalizedStatus, success: null };
            case 'skipped':
                return { kind: 'skipped', label: '已跳过', title: normalizedStatus, success: null };
            case 'failed':
            case 'error':
                return { kind: 'failed', label: '失败', title: normalizedStatus, success: false };
            case 'denied':
                return { kind: 'denied', label: '拒绝', title: normalizedStatus, success: false };
            case 'running':
                return { kind: 'running', label: '执行中', title: normalizedStatus, success: null };
            default:
                return {
                    kind: 'unknown',
                    label: getStatusDisplayName(normalizedStatus, { fallback: '已记录/状态不可用' }),
                    title: normalizedStatus,
                    success: null
                };
        }
    }

    const successValue = item?.success ?? item?.Success;
    if (typeof successValue === 'boolean') {
        return successValue
            ? { kind: 'success', label: '已通过', title: 'success=true', success: true }
            : { kind: 'failed', label: '失败', title: 'success=false', success: false };
    }

    const successText = String(successValue ?? '').trim().toLowerCase();
    if (successText === 'true') {
        return { kind: 'success', label: '已通过', title: 'success=true', success: true };
    }
    if (successText === 'false') {
        return { kind: 'failed', label: '失败', title: 'success=false', success: false };
    }

    return { kind: 'unknown', label: '已记录/状态不可用', title: 'status-unavailable', success: null };
}

function sanitizeToolTraceDisplay(panel, value, maxChars = 160) {
    const text = String(value ?? '').trim();
    if (!text) return '';
    return panel?._sanitizeValidationPreviewText?.(text, maxChars) ||
        panel?._sanitizeAssistantFailureText?.(text, maxChars) ||
        panel?._redactPublicDiagnosticText?.(text)?.slice(0, maxChars) ||
        text.slice(0, maxChars);
}

export function normalizeAgentToolTrace(items) {
    if (!Array.isArray(items)) return [];

    return items
        .map(item => {
            const duration = Number(item?.durationMs ?? item?.DurationMs ?? 0);
            const status = normalizeToolStatus(item);
            return {
                toolName: String(item?.toolName ?? item?.ToolName ?? item?.name ?? item?.Name ?? '').trim(),
                permission: String(item?.permission ?? item?.Permission ?? '').trim(),
                adapterName: String(item?.adapterName ?? item?.AdapterName ?? '').trim(),
                permissionReason: String(item?.permissionDecision?.reasonCode ??
                    item?.permissionDecision?.ReasonCode ??
                    item?.permissionDecision?.reason ??
                    item?.permissionDecision?.Reason ??
                    item?.PermissionDecision?.reasonCode ??
                    item?.PermissionDecision?.ReasonCode ??
                    item?.PermissionDecision?.reason ??
                    item?.PermissionDecision?.Reason ?? '').trim(),
                success: status.success,
                statusKind: status.kind,
                statusLabel: status.label,
                statusTitle: status.title,
                errorCode: String(item?.errorCode ?? item?.ErrorCode ?? '').trim(),
                durationMs: Number.isFinite(duration) ? Math.max(0, Math.round(duration)) : 0
            };
        })
        .filter(item => item.toolName);
}

export function renderAgentToolTrace(panel, toolTrace) {
    if (!Array.isArray(toolTrace) || toolTrace.length === 0) {
        return '';
    }

    const safeItems = toolTrace.map(item => {
        const rawToolName = String(item?.toolName ?? '').trim();
        const rawToolSummaryLabel = getToolDisplayName(rawToolName, { fallback: '工具' });
        const rawToolLabel = getToolDisplayName(rawToolName, { fallback: rawToolName || '工具' });
        return {
            ...item,
            toolName: sanitizeToolTraceDisplay(panel, rawToolName, 160),
            toolSummaryLabel: sanitizeToolTraceDisplay(panel, rawToolSummaryLabel, 160) || '工具',
            toolLabel: sanitizeToolTraceDisplay(panel, rawToolLabel, 160) || '工具',
            permission: sanitizeToolTraceDisplay(panel, item?.permission, 120),
            adapterName: sanitizeToolTraceDisplay(panel, item?.adapterName, 120),
            permissionReason: sanitizeToolTraceDisplay(panel, item?.permissionReason, 160),
            statusLabel: sanitizeToolTraceDisplay(panel, item?.statusLabel, 120),
            statusTitle: sanitizeToolTraceDisplay(panel, item?.statusTitle || item?.statusKind || 'status-unavailable', 160),
            errorCode: sanitizeToolTraceDisplay(panel, item?.errorCode, 160)
        };
    });

    const toolSummary = safeItems
        .map(item => `${item.toolSummaryLabel} ${item.statusLabel || '已记录/状态不可用'} ${item.durationMs}ms`)
        .join(' · ');

    return `
        <details class="ai-agent-tool-trace" data-agent-artifact="toolTrace">
            <summary title="${panel._escapeHtml(safeItems.map(item => item.toolName).join(' | '))}">工具轨迹（${safeItems.length}）${panel._escapeHtml(toolSummary ? ` ${toolSummary}` : '')}</summary>
            <div class="ai-agent-tool-trace-list">
                ${safeItems.map(item => `
                    <div class="ai-agent-tool-trace-row">
                        <span title="${panel._escapeHtml(item.toolName)}">${panel._escapeHtml(item.toolLabel)}</span>
                        <span title="${panel._escapeHtml(item.permission || '--')}">${panel._escapeHtml(getStatusDisplayName(item.permission, { fallback: item.permission || '--' }))}</span>
                        <span title="${panel._escapeHtml(item.adapterName || '--')}">${panel._escapeHtml(getStatusDisplayName(item.adapterName, { fallback: item.adapterName ? '元数据适配器' : '--' }))}</span>
                        <span title="${panel._escapeHtml(item.statusTitle || 'status-unavailable')}">${panel._escapeHtml(item.statusLabel || '已记录/状态不可用')}</span>
                        <span>${panel._escapeHtml(String(item.durationMs))}ms</span>
                        <span title="${panel._escapeHtml(item.errorCode || '--')}">${panel._escapeHtml(item.errorCode ? getStatusDisplayName(item.errorCode, { fallback: '错误码' }) : '--')}</span>
                        <span title="${panel._escapeHtml(item.permissionReason || '--')}">${panel._escapeHtml(item.permissionReason ? getStatusDisplayName(item.permissionReason, { fallback: '权限原因' }) : '--')}</span>
                    </div>
                `).join('')}
            </div>
        </details>
    `;
}
import {
    getStatusDisplayName,
    getToolDisplayName
} from '../../shared/operatorDisplayNames.js';
