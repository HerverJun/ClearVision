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

function normalizeArtifact(item) {
    return {
        artifactId: String(readValue(item, ['artifactId', 'ArtifactId']) ?? '').trim(),
        artifactType: String(readValue(item, ['artifactType', 'ArtifactType']) ?? 'artifact').trim(),
        metadataOnly: readBoolean(item, ['metadataOnly', 'MetadataOnly']) !== false,
        binaryIncluded: readBoolean(item, ['binaryIncluded', 'BinaryIncluded']) === true,
        byteLength: Number(readValue(item, ['byteLength', 'ByteLength']) ?? 0) || 0
    };
}

export function normalizeRuntimePreviewSummary(runtimePreview) {
    if (!runtimePreview || typeof runtimePreview !== 'object') {
        return null;
    }

    const fallback = readValue(runtimePreview, ['fallback', 'Fallback']) || {};
    return {
        previewReady: readBoolean(runtimePreview, ['previewReady', 'PreviewReady']),
        adapterName: String(readValue(runtimePreview, ['adapterName', 'AdapterName']) ?? '').trim(),
        previewMode: String(readValue(runtimePreview, ['previewMode', 'PreviewMode']) ?? '').trim(),
        artifacts: readArray(runtimePreview, ['artifacts', 'Artifacts']).map(normalizeArtifact),
        blockingIssues: readArray(runtimePreview, ['blockingIssues', 'BlockingIssues']),
        warnings: readArray(runtimePreview, ['warnings', 'Warnings']),
        missingResources: readArray(runtimePreview, ['missingResources', 'MissingResources']),
        fallback: {
            used: readBoolean(runtimePreview, ['fallbackUsed', 'FallbackUsed']) === true ||
                readBoolean(fallback, ['used', 'Used']) === true,
            reason: String(readValue(runtimePreview, ['fallbackReason', 'FallbackReason']) ??
                readValue(fallback, ['reason', 'Reason']) ?? '').trim(),
            errorCode: String(readValue(runtimePreview, ['errorCode', 'ErrorCode']) ??
                readValue(fallback, ['errorCode', 'ErrorCode']) ?? '').trim()
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
        summary.artifacts.length ? `${summary.artifacts.length} artifact` : ''
    ].filter(Boolean).join(' / ') || 'metadata-only';

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
                    ${summary.fallback.used ? `<span>fallback=true</span>` : ''}
                    ${summary.fallback.reason ? `<span>fallbackReason=${panel._escapeHtml(summary.fallback.reason)}</span>` : ''}
                    ${summary.fallback.errorCode ? `<span>errorCode=${panel._escapeHtml(summary.fallback.errorCode)}</span>` : ''}
                </div>
                ${panel._renderAgentPreviewIssueList(summary.blockingIssues, 'blocking')}
                ${panel._renderAgentPreviewIssueList(summary.warnings, 'warning')}
                ${panel._renderAgentPreviewIssueList(summary.missingResources, 'warning')}
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
            </div>
        </details>
    `;
}
