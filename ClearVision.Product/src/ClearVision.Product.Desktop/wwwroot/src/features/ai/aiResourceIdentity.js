const clean = value => String(value ?? '').trim();
const lower = value => clean(value).toLowerCase();
const token = value => lower(value).replace(/[^a-z0-9_]+/g, '');
const asArray = value => Array.isArray(value) ? value : [];
const asObject = value => value && typeof value === 'object' && !Array.isArray(value) ? value : {};

export function normalizeResourceType(value) {
    const normalized = token(value);
    if (normalized.includes('camera')) return 'camera_binding';
    if (normalized.includes('model')) return 'model_resource';
    if (normalized.includes('template')) return 'template_artifact';
    if (normalized.includes('calibration') || normalized.includes('measurement')) return 'calibration_resource';
    if (normalized.includes('plc')) return 'plc_output';
    if (normalized.includes('output')) return 'output_channel';
    return normalized || 'resource';
}

export function normalizeResourceParameter(value) {
    const normalized = token(value);
    return ['cameraid', 'camerabindingid', 'camera_binding_id'].includes(normalized)
        ? 'camera_binding_id'
        : normalized;
}

function normalizeOperatorKey(value) {
    const text = lower(value);
    if (!text) return '';
    const match = text.match(/^(.*)#(\d+)$/);
    return match ? `${token(match[1])}#${Math.max(1, Number(match[2]) || 1)}` : token(text);
}

function inferParameter(resourceKey, resourceType) {
    const key = clean(resourceKey);
    const candidate = key.includes('.') ? key.slice(key.lastIndexOf('.') + 1) : '';
    if (candidate) return candidate;
    if (resourceType === 'camera_binding') return 'CameraBindingId';
    if (resourceType === 'model_resource') return 'ModelPath';
    if (resourceType === 'template_artifact') return 'Template';
    if (resourceType === 'calibration_resource') return 'Scale';
    if (resourceType === 'plc_output' || resourceType === 'output_channel') return 'OutputChannel';
    return '';
}

function inferOperatorKey(item, resourceType, resourceKey) {
    const explicit = normalizeOperatorKey(item.operatorKey ?? item.OperatorKey);
    if (explicit) return explicit;
    const operatorType = clean(item.operatorType ?? item.OperatorType);
    const operatorIndex = Number(item.operatorIndex ?? item.OperatorIndex);
    if (operatorType && Number.isFinite(operatorIndex) && operatorIndex >= 0) {
        return `${token(operatorType)}#${operatorIndex + 1}`;
    }
    if (resourceType === 'camera_binding') return 'imageacquisition#1';
    const operatorId = clean(item.operatorId ?? item.OperatorId ?? item.actualOperatorId ?? item.ActualOperatorId);
    if (operatorId) return `id_${token(operatorId)}`;
    const fromKey = clean(resourceKey).includes('.') ? clean(resourceKey).slice(0, clean(resourceKey).lastIndexOf('.')) : '';
    return fromKey ? `id_${token(fromKey)}` : '';
}

export function createCanonicalResourceId(resourceType, operatorKey, parameterName, fallbackScope = '') {
    return `resource:v1|${normalizeResourceType(resourceType)}|${normalizeOperatorKey(operatorKey) || token(fallbackScope) || 'global'}|${normalizeResourceParameter(parameterName) || 'resource'}`;
}

export function normalizeCanonicalResource(value, { source = '' } = {}) {
    const raw = asObject(value);
    const nested = asObject(raw.resource ?? raw.Resource);
    const item = { ...raw, ...nested };
    const category = lower(item.category ?? item.Category);
    const blockerId = clean(item.id ?? item.Id);
    const legacyKey = blockerId.replace(/^resource_pending:/i, '').replace(/^resource:/i, '');
    const resourceKey = clean(item.resourceKey ?? item.ResourceKey ?? legacyKey);
    const resourceType = normalizeResourceType(
        item.resourceType ?? item.ResourceType ?? item.type ?? item.Type ?? item.field ?? item.Field ?? legacyKey);
    const parameterName = clean(item.parameterName ?? item.ParameterName) || inferParameter(resourceKey, resourceType);
    const operatorKey = inferOperatorKey(item, resourceType, resourceKey);
    const canonicalId = clean(item.canonicalId ?? item.CanonicalId) ||
        createCanonicalResourceId(resourceType, operatorKey, parameterName, legacyKey || resourceKey);
    const resourceName = clean(item.resourceName ?? item.ResourceName) || ({
        camera_binding: '相机绑定', model_resource: '模型资源', template_artifact: '模板资源',
        calibration_resource: '标定参数', plc_output: '外部输出资源', output_channel: '输出通道'
    }[resourceType] || '工程资源');
    const aliases = [...new Set([
        canonicalId,
        resourceKey,
        blockerId,
        ...asArray(item.aliases ?? item.Aliases)
    ].map(clean).filter(Boolean))];
    return {
        kind: 'resource',
        id: canonicalId,
        canonicalId,
        aliases,
        resourceKey,
        resourceType,
        resourceName,
        operatorKey,
        operatorId: clean(item.operatorId ?? item.OperatorId ?? item.actualOperatorId ?? item.ActualOperatorId),
        actualOperatorId: clean(item.actualOperatorId ?? item.ActualOperatorId),
        operatorType: clean(item.operatorType ?? item.OperatorType),
        operatorIndex: Number.isFinite(Number(item.operatorIndex ?? item.OperatorIndex)) ? Number(item.operatorIndex ?? item.OperatorIndex) : -1,
        parameterName,
        status: lower(item.status ?? item.Status) || 'pending',
        blockingScope: lower(item.blockingScope ?? item.BlockingScope) || (item.blocksBuild === false ? 'deploy_run' : 'build'),
        draftPolicy: lower(item.draftPolicy ?? item.DraftPolicy) || 'draft_allowed',
        resolutionTarget: clean(item.resolutionTarget ?? item.ResolutionTarget) || 'plan_workbench',
        source: clean(source || item.source || item.Source || (category === 'resource_pending' ? 'build_readiness' : 'missing_resource')),
        sources: [...new Set([clean(source || item.source || item.Source), ...asArray(item.sources ?? item.Sources)].filter(Boolean))],
        description: clean(item.description ?? item.Description ?? item.publicLabel ?? item.PublicLabel),
        title: clean(item.resourceName ?? item.ResourceName) || resourceName,
        field: clean(item.field ?? item.Field),
        questionId: clean(item.questionId ?? item.QuestionId),
        category: category || 'resource_pending',
        resolutionMode: lower(item.resolutionMode ?? item.ResolutionMode) || 'provide_resource',
        blocksBuild: item.blocksBuild === true || item.BlocksBuild === true,
        raw
    };
}

export function mergeCanonicalResources(values = []) {
    const byId = new Map();
    asArray(values).forEach((candidate, index) => {
        const item = candidate?.canonicalId ? candidate : normalizeCanonicalResource(candidate, { source: candidate?.source });
        const id = item.canonicalId || `resource:v1|resource|unknown#${index + 1}|resource`;
        const existing = byId.get(id);
        if (!existing) {
            byId.set(id, item);
            return;
        }
        const prefer = (left, right) => clean(left) || clean(right);
        byId.set(id, {
            ...existing,
            ...item,
            canonicalId: id,
            id,
            resourceKey: prefer(existing.resourceKey, item.resourceKey),
            resourceName: prefer(existing.resourceName, item.resourceName),
            operatorKey: prefer(existing.operatorKey, item.operatorKey),
            operatorId: prefer(existing.operatorId, item.operatorId),
            actualOperatorId: prefer(existing.actualOperatorId, item.actualOperatorId),
            operatorType: prefer(existing.operatorType, item.operatorType),
            parameterName: prefer(existing.parameterName, item.parameterName),
            description: prefer(existing.description, item.description),
            resolutionTarget: prefer(existing.resolutionTarget, item.resolutionTarget),
            blocksBuild: existing.blocksBuild === true || item.blocksBuild === true,
            blockingScope: existing.blocksBuild === true ? existing.blockingScope : item.blockingScope,
            aliases: [...new Set([...asArray(existing.aliases), ...asArray(item.aliases)])],
            sources: [...new Set([...asArray(existing.sources), ...asArray(item.sources), existing.source, item.source].filter(Boolean))]
        });
    });
    return [...byId.values()];
}

export function serializeResourceDecision(resource, decision = {}) {
    const item = normalizeCanonicalResource(resource, { source: resource?.source });
    return {
        canonicalId: item.canonicalId,
        status: lower(decision.status) || 'pending',
        resourceKey: item.resourceKey,
        resourceType: item.resourceType,
        operatorKey: item.operatorKey,
        operatorId: item.operatorId,
        operatorType: item.operatorType,
        operatorIndex: item.operatorIndex,
        parameterName: item.parameterName,
        valueSummary: clean(decision.valueSummary ?? decision.value),
        source: clean(decision.source)
    };
}

export function serializeCanonicalResource(resource, { source = '' } = {}) {
    const item = normalizeCanonicalResource(resource, { source: source || resource?.source });
    return {
        canonicalId: item.canonicalId,
        aliases: item.aliases,
        resourceKey: item.resourceKey,
        resourceType: item.resourceType,
        resourceName: item.resourceName,
        operatorKey: item.operatorKey,
        operatorId: item.operatorId,
        actualOperatorId: item.actualOperatorId,
        operatorType: item.operatorType,
        operatorIndex: item.operatorIndex,
        parameterName: item.parameterName,
        status: item.status,
        blockingScope: item.blockingScope,
        draftPolicy: item.draftPolicy,
        resolutionTarget: item.resolutionTarget,
        source: clean(source || item.source),
        sources: item.sources,
        description: item.description,
        field: item.field,
        questionId: item.questionId,
        category: item.category,
        resolutionMode: item.resolutionMode,
        blocksBuild: item.blocksBuild
    };
}
