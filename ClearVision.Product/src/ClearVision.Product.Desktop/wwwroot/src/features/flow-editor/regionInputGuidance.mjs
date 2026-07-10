const REGION_MORPHOLOGY_TYPES = new Set([
    'RegionErosion',
    'RegionDilation',
    'RegionOpening',
    'RegionClosing'
]);

export function buildRegionInputGuidance(operator, options = {}) {
    const operatorType = String(operator?.type || operator?.Type || operator?.operatorType || operator?.OperatorType || '').trim();
    if (!REGION_MORPHOLOGY_TYPES.has(operatorType) || options.hasRegionInputConnection === true) {
        return null;
    }

    const inputPorts = Array.isArray(operator?.inputPorts)
        ? operator.inputPorts
        : (Array.isArray(operator?.InputPorts)
            ? operator.InputPorts
            : (Array.isArray(operator?.inputs) ? operator.inputs : []));
    const portIndex = inputPorts.findIndex(port => {
        const name = String(port?.name || port?.Name || '').trim().toLowerCase();
        const type = port?.dataType ?? port?.DataType ?? port?.type ?? port?.Type;
        return name === 'region' || String(type).trim().toLowerCase() === 'region' || Number(type) === 13;
    });
    if (portIndex < 0) {
        return null;
    }

    const lines = [
        'Image/Contour 不能直接替代。',
        '推荐 BinaryImageToRegion 或区域生成算子。',
        '可选 Image 输入仅用于参考图和可视化，不是主输入。'
    ];
    return {
        code: 'missing-region-input',
        title: '当前缺少 Region',
        portIndex,
        lines,
        summary: `当前缺少 Region；${lines.join('')}`
    };
}
