import { createSignal } from '../../core/state/store.js';

function normalizeIdentityString(value) {
    return value === undefined || value === null
        ? null
        : String(value).trim().toLowerCase();
}

function normalizeIdentityNumber(value) {
    if (value === undefined || value === null) {
        return null;
    }

    const numberValue = Number(value);
    return Number.isSafeInteger(numberValue) && numberValue >= 0
        ? numberValue
        : null;
}

function readIdentityField(identity, camelName, pascalName) {
    if (!identity || typeof identity !== 'object') {
        return null;
    }

    if (Object.prototype.hasOwnProperty.call(identity, camelName)) {
        return identity[camelName];
    }

    if (Object.prototype.hasOwnProperty.call(identity, pascalName)) {
        return identity[pascalName];
    }

    return null;
}

export function normalizeNodePreviewIdentity(identity) {
    if (!identity || typeof identity !== 'object') {
        return null;
    }

    const normalized = {
        projectId: normalizeIdentityString(readIdentityField(identity, 'projectId', 'ProjectId')),
        targetNodeId: normalizeIdentityString(readIdentityField(identity, 'targetNodeId', 'TargetNodeId')),
        debugSessionId: normalizeIdentityString(readIdentityField(identity, 'debugSessionId', 'DebugSessionId')),
        clientRequestSequence: normalizeIdentityNumber(readIdentityField(identity, 'clientRequestSequence', 'ClientRequestSequence')),
        flowRevision: normalizeIdentityNumber(readIdentityField(identity, 'flowRevision', 'FlowRevision'))
    };

    return normalized.projectId &&
        normalized.targetNodeId &&
        normalized.debugSessionId &&
        normalized.clientRequestSequence !== null &&
        normalized.flowRevision !== null
        ? normalized
        : null;
}

export function getNodePreviewIdentitySignature(identity) {
    const normalized = normalizeNodePreviewIdentity(identity);
    if (!normalized) {
        return '';
    }

    return [
        normalized.projectId,
        normalized.targetNodeId,
        normalized.debugSessionId,
        normalized.clientRequestSequence,
        normalized.flowRevision
    ].join('|');
}

function cloneArtifactMetadata(artifact) {
    if (!artifact || typeof artifact !== 'object') {
        return null;
    }

    return {
        artifactId: typeof artifact.artifactId === 'string' ? artifact.artifactId : null,
        kind: typeof artifact.kind === 'string' ? artifact.kind : '',
        role: typeof artifact.role === 'string' ? artifact.role : '',
        pathHint: typeof artifact.pathHint === 'string' ? artifact.pathHint : '$',
        contentType: typeof artifact.contentType === 'string' ? artifact.contentType : 'application/octet-stream',
        length: Number.isFinite(Number(artifact.length)) ? Number(artifact.length) : 0,
        sha256: typeof artifact.sha256 === 'string' ? artifact.sha256 : '',
        width: artifact.width ?? null,
        height: artifact.height ?? null,
        channels: artifact.channels ?? null,
        expiresAtUtc: artifact.expiresAtUtc ?? null
    };
}

function normalizeOptionalString(value) {
    return value === undefined || value === null
        ? null
        : String(value);
}

function normalizeBindableVariableTypes(value) {
    return Array.isArray(value)
        ? Array.from(new Set(value
            .map(item => String(item || '').trim())
            .filter(Boolean)))
        : [];
}

export function createNodePreviewSelectionStore() {
    const [getSelection, setSelection, subscribe] = createSignal(null);

    const clear = () => {
        setSelection(null);
    };

    return {
        getSelection,
        subscribe,
        select(selection) {
            const identity = normalizeNodePreviewIdentity(selection?.identity);
            if (!identity) {
                clear();
                return null;
            }

            const nextSelection = {
                identity,
                identitySignature: getNodePreviewIdentitySignature(identity),
                nodeName: String(selection?.nodeName ?? ''),
                nodeKind: String(selection?.nodeKind ?? ''),
                outputPortId: normalizeOptionalString(selection?.outputPortId),
                outputPortName: normalizeOptionalString(selection?.outputPortName),
                resultPathVersion: selection?.resultPathVersion ?? null,
                resultPath: normalizeOptionalString(selection?.resultPath),
                kind: selection?.kind === undefined || selection?.kind === null
                    ? null
                    : String(selection.kind),
                displayValue: selection?.displayValue === undefined || selection?.displayValue === null
                    ? ''
                    : String(selection.displayValue),
                originalType: selection?.originalType === undefined || selection?.originalType === null
                    ? null
                    : String(selection.originalType),
                pathHint: selection?.pathHint === undefined || selection?.pathHint === null
                    ? '$'
                    : String(selection.pathHint),
                addressable: selection?.addressable === true,
                truncated: selection?.truncated === true,
                bindableVariableTypes: normalizeBindableVariableTypes(selection?.bindableVariableTypes),
                artifact: cloneArtifactMetadata(selection?.artifact)
            };

            setSelection(nextSelection);
            return nextSelection;
        },
        clear,
        clearIfIdentityChanged(identity) {
            const current = getSelection();
            if (!current) {
                return;
            }

            if (current.identitySignature !== getNodePreviewIdentitySignature(identity)) {
                clear();
            }
        },
        destroy() {
            clear();
        }
    };
}

export default createNodePreviewSelectionStore;
