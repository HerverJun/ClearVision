function clean(value) {
    return String(value ?? '').trim();
}

function toArray(value) {
    return Array.isArray(value) ? value.filter(Boolean) : [];
}

function read(source, ...names) {
    if (!source || typeof source !== 'object') return undefined;
    for (const name of names) {
        if (Object.prototype.hasOwnProperty.call(source, name)) return source[name];
    }
    return undefined;
}

function normalize(value) {
    return clean(value).toLowerCase();
}

export function getPendingParameterNames(item) {
    const ids = getOperatorIds(item);
    // Older Build results leaked this internal policy into operator work items.
    const legacyPlanDefault = ids.length > 0 && ids.every(id => id === 'plan_default');
    return toArray(read(item, 'parameterNames', 'ParameterNames'))
        .map(clean)
        .filter(name => name && !(legacyPlanDefault && normalize(name) === 'resource_policy'));
}

function getOperatorIds(item) {
    return [
        read(item, 'operatorId', 'OperatorId', 'tempId', 'TempId'),
        read(item, 'actualOperatorId', 'ActualOperatorId')
    ].map(normalize).filter(Boolean);
}

function getResourceParameterName(resource) {
    const direct = clean(read(resource, 'parameterName', 'ParameterName'));
    if (direct) return direct;
    const resourceKey = clean(read(resource, 'resourceKey', 'ResourceKey'));
    const dotIndex = resourceKey.lastIndexOf('.');
    return dotIndex >= 0 && dotIndex < resourceKey.length - 1
        ? resourceKey.slice(dotIndex + 1).trim()
        : '';
}

function getResourceOperatorIds(resource) {
    const ids = getOperatorIds(resource);
    const resourceKey = clean(read(resource, 'resourceKey', 'ResourceKey'));
    const dotIndex = resourceKey.indexOf('.');
    if (dotIndex > 0) ids.push(normalize(resourceKey.slice(0, dotIndex)));
    return [...new Set(ids.filter(Boolean))];
}

function matchesResource(item, parameterName, resource) {
    const normalizedName = normalize(parameterName);
    if (!normalizedName || normalize(getResourceParameterName(resource)) !== normalizedName) return false;

    const itemIds = getOperatorIds(item);
    const resourceIds = getResourceOperatorIds(resource);
    if (itemIds.length === 0 || resourceIds.length === 0) return true;
    return itemIds.some(id => resourceIds.includes(id));
}

export function partitionPendingParameters(pendingParameters = [], missingResources = []) {
    const pending = toArray(pendingParameters);
    const resources = toArray(missingResources);
    const ordinaryPendingParameters = [];
    const resourceBackedPendingParameters = [];
    const resourceBackedFields = [];

    pending.forEach(item => {
        const ordinaryNames = [];
        const resourceNames = [];
        getPendingParameterNames(item).forEach(parameterName => {
            const resource = resources.find(candidate => matchesResource(item, parameterName, candidate));
            if (resource) {
                resourceNames.push(parameterName);
                resourceBackedFields.push({
                    operatorId: clean(read(item, 'operatorId', 'OperatorId', 'tempId', 'TempId')),
                    actualOperatorId: clean(read(item, 'actualOperatorId', 'ActualOperatorId')),
                    parameterName,
                    resource
                });
            } else {
                ordinaryNames.push(parameterName);
            }
        });

        if (ordinaryNames.length > 0) {
            ordinaryPendingParameters.push({ ...item, parameterNames: ordinaryNames });
        }
        if (resourceNames.length > 0) {
            resourceBackedPendingParameters.push({ ...item, parameterNames: resourceNames });
        }
    });

    return {
        ordinaryPendingParameters,
        resourceBackedPendingParameters,
        resourceBackedFields,
        resourceBackedFieldCount: resourceBackedFields.length,
        resources
    };
}

export const aiPendingParameterPartitionTestApi = {
    getResourceParameterName,
    getResourceOperatorIds,
    matchesResource
};
