export const aiPanelTopologySummaryMixin = {
    _extractTopologySummary(flow) {
        if (!flow) return '';
        const ops = this._extractOperators(flow);
        const connections = this._extractConnections(flow);
        if (ops.length === 0) return '';

        // Build adjacency from connections
        const adj = new Map();
        const inDegree = new Map();
        ops.forEach(op => {
            const tid = op.tempId || op.TempId || '';
            if (!adj.has(tid)) adj.set(tid, []);
            if (!inDegree.has(tid)) inDegree.set(tid, 0);
        });
        connections.forEach(conn => {
            const src = conn.sourceTempId || conn.SourceTempId || '';
            const tgt = conn.targetTempId || conn.TargetTempId || '';
            if (adj.has(src)) adj.get(src).push(tgt);
            inDegree.set(tgt, (inDegree.get(tgt) || 0) + 1);
        });

        // Topological sort with cycle detection
        const queue = [];
        for (const [tid, deg] of inDegree) {
            if (deg === 0) queue.push(tid);
        }
        const sorted = [];
        while (queue.length > 0) {
            const tid = queue.shift();
            sorted.push(tid);
            for (const next of (adj.get(tid) || [])) {
                inDegree.set(next, inDegree.get(next) - 1);
                if (inDegree.get(next) === 0) queue.push(next);
            }
        }

        // Cycle detection: append remaining nodes (in cycles) to avoid silent loss
        if (sorted.length < ops.length) {
            for (const [tid, deg] of inDegree) {
                if (deg > 0 && !sorted.includes(tid)) {
                    sorted.push(tid);
                }
            }
        }

        const opMap = new Map();
        ops.forEach(op => {
            const tid = op.tempId || op.TempId || '';
            opMap.set(tid, op);
        });

        return sorted
            .map(tid => opMap.get(tid))
            .filter(Boolean)
            .map(op => {
                const operatorType = op.operatorType || op.OperatorType || op.type || op.Type || '';
                return getOperatorTypeDisplayName(operatorType) || op.displayName || op.DisplayName || '?';
            })
            .join(' -> ');
    }

    // ── 路径脱敏 ─────────────────────────────────────────────
};
