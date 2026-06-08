export function normalizeAgentToolTrace(items) {
    if (!Array.isArray(items)) return [];

    return items
        .map(item => {
            const duration = Number(item?.durationMs ?? item?.DurationMs ?? 0);
            const successValue = item?.success ?? item?.Success;
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
                success: typeof successValue === 'boolean' ? successValue : String(successValue ?? '').toLowerCase() === 'true',
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

    const toolSummary = toolTrace
        .map(item => `${getToolDisplayName(item.toolName, { fallback: '工具' })} ${item.success ? '已通过' : '失败'} ${item.durationMs}ms`)
        .join(' · ');

    return `
        <details class="ai-agent-tool-trace" data-agent-artifact="toolTrace">
            <summary title="${panel._escapeHtml(toolTrace.map(item => item.toolName).join(' | '))}">工具轨迹（${toolTrace.length}）${panel._escapeHtml(toolSummary ? ` ${toolSummary}` : '')}</summary>
            <div class="ai-agent-tool-trace-list">
                ${toolTrace.map(item => `
                    <div class="ai-agent-tool-trace-row">
                        <span title="${panel._escapeHtml(item.toolName)}">${panel._escapeHtml(getToolDisplayName(item.toolName, { fallback: item.toolName }))}</span>
                        <span title="${panel._escapeHtml(item.permission || '--')}">${panel._escapeHtml(getStatusDisplayName(item.permission, { fallback: item.permission || '--' }))}</span>
                        <span title="${panel._escapeHtml(item.adapterName || '--')}">${panel._escapeHtml(getStatusDisplayName(item.adapterName, { fallback: item.adapterName ? '元数据适配器' : '--' }))}</span>
                        <span title="${item.success ? 'success' : 'failed'}">${item.success ? '成功' : '失败'}</span>
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
