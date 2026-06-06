function readValue(source, names) {
    if (!source || typeof source !== 'object') {
        return undefined;
    }

    for (const name of names) {
        if (Object.prototype.hasOwnProperty.call(source, name)) {
            return source[name];
        }
    }

    const keys = Object.keys(source);
    const matched = keys.find(key => names.some(name => key.toLowerCase() === String(name).toLowerCase()));
    return matched ? source[matched] : undefined;
}

function readArray(source, names) {
    const value = readValue(source, names);
    return Array.isArray(value) ? value : [];
}

function readBoolean(source, names) {
    const value = readValue(source, names);
    if (typeof value === 'boolean') {
        return value;
    }
    if (typeof value === 'string') {
        const normalized = value.trim().toLowerCase();
        if (normalized === 'true') return true;
        if (normalized === 'false') return false;
    }
    return null;
}

function redactDisplayValue(value) {
    const text = String(value ?? '').trim();
    if (!text) {
        return '';
    }

    if (/base64|data:image|authorization|bearer|x-api-key|api[_-]?key|token=/i.test(text)) {
        return '<redacted>';
    }

    if (/(^|[^0-9])(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?:\/\S*)?/i.test(text)) {
        return '<redacted>';
    }

    if (/[A-Za-z]:[\\/]|(^|[\s=:])\/[\w.-]|\\|https?:\/\//i.test(text)) {
        return '<redacted>';
    }

    return text;
}

function normalizeArtifact(item) {
    return {
        artifactId: redactDisplayValue(readValue(item, ['artifactId', 'ArtifactId'])),
        artifactType: redactDisplayValue(readValue(item, ['artifactType', 'ArtifactType']) ?? 'artifact'),
        metadataOnly: readBoolean(item, ['metadataOnly', 'MetadataOnly']) !== false,
        binaryIncluded: readBoolean(item, ['binaryIncluded', 'BinaryIncluded']) === true,
        byteLength: Number(readValue(item, ['byteLength', 'ByteLength']) ?? 0) || 0
    };
}

function normalizePermissionDecision(item) {
    const counts = readValue(item, ['allowlistCounts', 'AllowlistCounts']) || {};
    return {
        allowed: readBoolean(item, ['allowed', 'Allowed']),
        reasonCode: redactDisplayValue(readValue(item, ['reasonCode', 'ReasonCode', 'reason', 'Reason'])),
        reason: redactDisplayValue(readValue(item, ['reason', 'Reason'])),
        runtimePreviewConsent: readBoolean(item, ['runtimePreviewConsent', 'RuntimePreviewConsent']),
        pilotEnabled: readBoolean(item, ['pilotEnabled', 'PilotEnabled']),
        metadataOnly: readBoolean(item, ['metadataOnly', 'MetadataOnly']) !== false,
        effectiveAdapterName: redactDisplayValue(readValue(item, ['effectiveAdapterName', 'EffectiveAdapterName'])),
        allowlistCounts: {
            camera: Number(readValue(counts, ['camera', 'Camera']) ?? 0) || 0,
            model: Number(readValue(counts, ['model', 'Model']) ?? 0) || 0,
            template: Number(readValue(counts, ['template', 'Template']) ?? 0) || 0,
            flow: Number(readValue(counts, ['flow', 'Flow']) ?? 0) || 0,
            resourceRoot: Number(readValue(counts, ['resourceRoot', 'ResourceRoot']) ?? 0) || 0
        }
    };
}

function normalizeResourceTrace(item) {
    return {
        allowed: readBoolean(item, ['allowed', 'Allowed']),
        reasonCode: redactDisplayValue(readValue(item, ['reasonCode', 'ReasonCode'])),
        resourceType: redactDisplayValue(readValue(item, ['resourceType', 'ResourceType'])),
        resourceId: redactDisplayValue(readValue(item, ['resourceId', 'ResourceId'])),
        normalizedKey: redactDisplayValue(readValue(item, ['normalizedKey', 'NormalizedKey'])),
        missingResources: readArray(item, ['missingResources', 'MissingResources']).map(normalizeIssueItem),
        trace: readArray(item, ['trace', 'Trace'])
    };
}

function normalizeIssueItem(item) {
    if (typeof item === 'string') {
        return redactDisplayValue(item);
    }

    if (!item || typeof item !== 'object') {
        return item;
    }

    return {
        ...item,
        message: redactDisplayValue(readValue(item, ['message', 'Message'])),
        Message: redactDisplayValue(readValue(item, ['Message', 'message'])),
        summary: redactDisplayValue(readValue(item, ['summary', 'Summary'])),
        Summary: redactDisplayValue(readValue(item, ['Summary', 'summary'])),
        description: redactDisplayValue(readValue(item, ['description', 'Description'])),
        Description: redactDisplayValue(readValue(item, ['Description', 'description'])),
        resourceKey: redactDisplayValue(readValue(item, ['resourceKey', 'ResourceKey'])),
        ResourceKey: redactDisplayValue(readValue(item, ['ResourceKey', 'resourceKey'])),
        operatorId: redactDisplayValue(readValue(item, ['operatorId', 'OperatorId', 'tempId', 'TempId'])),
        OperatorId: redactDisplayValue(readValue(item, ['OperatorId', 'operatorId', 'tempId', 'TempId']))
    };
}

function normalizePendingActions(items) {
    return (Array.isArray(items) ? items : [])
        .map(item => ({
            actionType: redactDisplayValue(readValue(item, ['actionType', 'ActionType', 'type', 'Type'])),
            summary: redactDisplayValue(readValue(item, ['summary', 'Summary', 'message', 'Message'])),
            title: redactDisplayValue(readValue(item, ['title', 'Title']))
        }))
        .filter(item => item.actionType || item.summary || item.title);
}

function normalizeReadiness(item) {
    if (!item || typeof item !== 'object') {
        return null;
    }

    const coverage = readValue(item, ['allowlistCoverage', 'AllowlistCoverage']) || {};
    return {
        status: redactDisplayValue(readValue(item, ['status', 'Status'])),
        canRunMetadataPilot: readBoolean(item, ['canRunMetadataPilot', 'CanRunMetadataPilot']),
        workflowDraftAllowed: readBoolean(item, ['workflowDraftAllowed', 'WorkflowDraftAllowed']),
        issues: readArray(item, ['issues', 'Issues']).map(normalizeIssueItem),
        blockingIssues: readArray(item, ['blockingIssues', 'BlockingIssues']).map(normalizeIssueItem),
        missingResources: readArray(item, ['missingResources', 'MissingResources']).map(normalizeIssueItem),
        unsafeFindings: readArray(item, ['unsafeFindings', 'UnsafeFindings']).map(normalizeIssueItem),
        resourceTrace: normalizeResourceTrace(readValue(item, ['resourceTrace', 'ResourceTrace']) || {}),
        pendingActions: normalizePendingActions(readArray(item, ['pendingActions', 'PendingActions'])),
        allowlistCoverage: redactDisplayValue(JSON.stringify(coverage || {}))
    };
}

export function normalizeRuntimePreviewSummary(runtimePreview) {
    if (!runtimePreview || typeof runtimePreview !== 'object') {
        return null;
    }

    const fallback = readValue(runtimePreview, ['fallback', 'Fallback']) || {};
    const permissionDecision = normalizePermissionDecision(readValue(runtimePreview, ['permissionDecision', 'PermissionDecision']) || {});
    const resourceTrace = normalizeResourceTrace(readValue(runtimePreview, ['resourceTrace', 'ResourceTrace']) || {});
    const readiness = normalizeReadiness(readValue(runtimePreview, ['readiness', 'Readiness']));
    return {
        previewReady: readBoolean(runtimePreview, ['previewReady', 'PreviewReady']),
        adapterName: redactDisplayValue(readValue(runtimePreview, ['adapterName', 'AdapterName'])),
        previewMode: redactDisplayValue(readValue(runtimePreview, ['previewMode', 'PreviewMode'])),
        permissionDecision,
        resourceTrace,
        readiness,
        artifacts: readArray(runtimePreview, ['artifacts', 'Artifacts']).map(normalizeArtifact),
        blockingIssues: readArray(runtimePreview, ['blockingIssues', 'BlockingIssues']).map(normalizeIssueItem),
        warnings: readArray(runtimePreview, ['warnings', 'Warnings']).map(normalizeIssueItem),
        missingResources: readArray(runtimePreview, ['missingResources', 'MissingResources']).map(normalizeIssueItem),
        issues: readArray(runtimePreview, ['issues', 'Issues']).map(normalizeIssueItem),
        pendingActions: normalizePendingActions(readArray(runtimePreview, ['pendingActions', 'PendingActions'])),
        fallback: {
            used: readBoolean(runtimePreview, ['fallbackUsed', 'FallbackUsed']) === true ||
                readBoolean(fallback, ['used', 'Used']) === true,
            adapterName: redactDisplayValue(readValue(runtimePreview, ['fallbackAdapterName', 'FallbackAdapterName']) ??
                readValue(fallback, ['fallbackAdapterName', 'FallbackAdapterName'])),
            reason: redactDisplayValue(readValue(runtimePreview, ['fallbackReason', 'FallbackReason']) ??
                readValue(fallback, ['reason', 'Reason'])),
            errorCode: redactDisplayValue(readValue(runtimePreview, ['errorCode', 'ErrorCode']) ??
                readValue(fallback, ['errorCode', 'ErrorCode', 'reasonCode', 'ReasonCode']))
        }
    };
}

export function renderRuntimePreviewSection(panel, runtimePreview) {
    const summary = normalizeRuntimePreviewSummary(runtimePreview);
    if (!summary) {
        return '';
    }

    const countLabel = [
        summary.blockingIssues.length ? `${summary.blockingIssues.length} blocking` : '',
        summary.warnings.length ? `${summary.warnings.length} warning` : '',
        summary.missingResources.length ? `${summary.missingResources.length} missing` : '',
        summary.pendingActions.length ? `${summary.pendingActions.length} pending` : '',
        summary.readiness?.status ? `readiness=${summary.readiness.status}` : '',
        summary.artifacts.length ? `${summary.artifacts.length} artifact` : ''
    ].filter(Boolean).join(' / ') || 'metadata-only';

    const permission = summary.permissionDecision.allowed === null
        ? ''
        : `permission=${summary.permissionDecision.allowed ? 'allowed' : 'denied'}`;
    const traceText = [
        summary.resourceTrace.resourceType,
        summary.resourceTrace.reasonCode,
        summary.resourceTrace.normalizedKey
    ].filter(Boolean).join(' / ');
    const counts = summary.permissionDecision.allowlistCounts;
    const countText = `camera=${counts.camera} / model=${counts.model} / template=${counts.template} / flow=${counts.flow} / root=${counts.resourceRoot}`;

    return `
        <details class="ai-agent-preview-section" data-validation-preview-section="runtimePreview">
            <summary class="ai-agent-preview-header">
                <span>runtimePreview</span>
                <small>${panel._escapeHtml(countLabel)}</small>
            </summary>
            <div class="ai-agent-preview-body">
                <div class="ai-agent-preview-state-row">
                    ${summary.previewReady !== null ? `<span>previewReady=${summary.previewReady ? 'true' : 'false'}</span>` : ''}
                    ${summary.adapterName ? `<span>adapterName=${panel._escapeHtml(summary.adapterName)}</span>` : ''}
                    ${summary.previewMode ? `<span>previewMode=${panel._escapeHtml(summary.previewMode)}</span>` : ''}
                    ${permission ? `<span>${panel._escapeHtml(permission)}</span>` : ''}
                    ${summary.permissionDecision.reasonCode ? `<span>permissionReason=${panel._escapeHtml(summary.permissionDecision.reasonCode)}</span>` : ''}
                    ${summary.fallback.used ? `<span>fallback=true</span>` : ''}
                    ${summary.fallback.adapterName ? `<span>fallbackAdapterName=${panel._escapeHtml(summary.fallback.adapterName)}</span>` : ''}
                    ${summary.fallback.reason ? `<span>fallbackReason=${panel._escapeHtml(summary.fallback.reason)}</span>` : ''}
                    ${summary.fallback.errorCode ? `<span>errorCode=${panel._escapeHtml(summary.fallback.errorCode)}</span>` : ''}
                </div>
                ${summary.readiness ? `
                    <div class="ai-agent-preview-state-row" data-runtime-preview-readiness="true">
                        ${summary.readiness.status ? `<span>readiness=${panel._escapeHtml(summary.readiness.status)}</span>` : ''}
                        ${summary.readiness.canRunMetadataPilot !== null ? `<span>canRunMetadataPilot=${summary.readiness.canRunMetadataPilot ? 'true' : 'false'}</span>` : ''}
                        ${summary.readiness.workflowDraftAllowed !== null ? `<span>workflowDraftAllowed=${summary.readiness.workflowDraftAllowed ? 'true' : 'false'}</span>` : ''}
                    </div>
                ` : ''}
                ${(traceText || summary.resourceTrace.allowed !== null) ? `
                    <div class="ai-agent-preview-state-row" data-runtime-preview-resource-trace="true">
                        ${summary.resourceTrace.allowed !== null ? `<span>resourceAllowed=${summary.resourceTrace.allowed ? 'true' : 'false'}</span>` : ''}
                        ${traceText ? `<span>resourceTrace=${panel._escapeHtml(traceText)}</span>` : ''}
                    </div>
                ` : ''}
                ${panel._renderAgentPreviewIssueList(summary.blockingIssues, 'blocking')}
                ${summary.readiness ? panel._renderAgentPreviewIssueList(summary.readiness.blockingIssues, 'blocking') : ''}
                ${panel._renderAgentPreviewIssueList(summary.warnings, 'warning')}
                ${panel._renderAgentPreviewIssueList(summary.missingResources, 'warning')}
                ${summary.readiness ? panel._renderAgentPreviewIssueList(summary.readiness.missingResources, 'warning') : ''}
                ${summary.readiness ? panel._renderAgentPreviewIssueList(summary.readiness.unsafeFindings, 'warning') : ''}
                ${panel._renderAgentPreviewIssueList(summary.resourceTrace.missingResources, 'warning')}
                ${panel._renderAgentPreviewIssueList(summary.issues, 'warning')}
                ${summary.pendingActions.length > 0 ? `
                    <div class="ai-agent-artifact-list" data-runtime-preview-pending-actions="true">
                        ${summary.pendingActions.slice(0, 6).map(item => `
                            <div class="ai-agent-artifact-row">
                                <span>${panel._escapeHtml(item.actionType || 'pending')}</span>
                                <small>${panel._escapeHtml(item.summary || item.title || '')}</small>
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
                ${summary.artifacts.length > 0 ? `
                    <div class="ai-agent-artifact-list">
                        ${summary.artifacts.slice(0, 6).map(item => `
                            <div class="ai-agent-artifact-row">
                                <span>${panel._escapeHtml(item.artifactType)}</span>
                                <small>${panel._escapeHtml([
                                    item.artifactId,
                                    `metadataOnly=${item.metadataOnly}`,
                                    `binaryIncluded=${item.binaryIncluded}`,
                                    `byteLength=${item.byteLength}`
                                ].filter(Boolean).join(' / '))}</small>
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
                <details class="ai-agent-runtime-preview-dev" data-runtime-preview-pilot-status="true" ${panel.isVisionAgentDeveloperUiEnabled ? '' : 'hidden'}>
                    <summary>RuntimePreview Pilot status</summary>
                    <div class="ai-agent-preview-state-row">
                        <span>pilotEnabled=${summary.permissionDecision.pilotEnabled === true ? 'true' : 'false'}</span>
                        <span>metadataOnly=${summary.permissionDecision.metadataOnly ? 'true' : 'false'}</span>
                        <span>allowlistCounts=${panel._escapeHtml(countText)}</span>
                        ${summary.readiness?.allowlistCoverage ? `<span>readinessCoverage=${panel._escapeHtml(summary.readiness.allowlistCoverage)}</span>` : ''}
                        <span>realResourcesTouched=false</span>
                    </div>
                </details>
            </div>
        </details>
    `;
}
