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
        .map(item => `${item.toolName}:${item.permission || '--'}:${item.adapterName || '--'}:${item.success ? 'ok' : 'failed'}:${item.durationMs}ms${item.errorCode ? `:${item.errorCode}` : ''}`)
        .join(' | ');

    return `
        <details class="ai-agent-tool-trace" data-agent-artifact="toolTrace">
            <summary>toolTrace (${toolTrace.length}) ${panel._escapeHtml(toolSummary)}</summary>
            <div class="ai-agent-tool-trace-list">
                ${toolTrace.map(item => `
                    <div class="ai-agent-tool-trace-row">
                        <span>${panel._escapeHtml(item.toolName)}</span>
                        <span>${panel._escapeHtml(item.permission || '--')}</span>
                        <span>${panel._escapeHtml(item.adapterName || '--')}</span>
                        <span>${item.success ? 'success' : 'failed'}</span>
                        <span>${panel._escapeHtml(String(item.durationMs))}ms</span>
                        <span>${panel._escapeHtml(item.errorCode || '--')}</span>
                    </div>
                `).join('')}
            </div>
        </details>
    `;
}
