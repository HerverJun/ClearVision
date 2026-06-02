function countFlowItems(items) {
    if (Array.isArray(items)) {
        return items.length;
    }

    if (items && typeof items === 'object') {
        return Object.keys(items).length;
    }

    return 0;
}

export function getFlowNodeCount(flow) {
    const candidates = [
        flow?.operators,
        flow?.Operators,
        flow?.nodes,
        flow?.Nodes
    ];

    for (const candidate of candidates) {
        const count = countFlowItems(candidate);
        if (count > 0) {
            return count;
        }
    }

    return 0;
}
