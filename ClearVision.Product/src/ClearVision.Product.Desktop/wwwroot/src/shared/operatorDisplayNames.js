export const OPERATOR_DISPLAY_NAMES = Object.freeze({
    ImageAcquisition: '图像采集',
    TemplateMatching: '模板匹配',
    DeepLearning: '深度学习检测',
    CircleMeasurement: '圆测量',
    MeasureDistance: '距离测量',
    ResultJudgment: '结果判定',
    ResultOutput: '结果输出',
    ImageCompose: '图像合成',
    RoiManager: 'ROI管理',
    BlobAnalysis: '斑点分析',
    Thresholding: '阈值分割',
    Filtering: '滤波处理'
});

export function getOperatorTypeDisplayName(operatorType, options = {}) {
    const rawType = String(operatorType || '').trim();
    if (!rawType) {
        return options.fallback || '';
    }

    const displayName = OPERATOR_DISPLAY_NAMES[rawType] || '';
    if (!displayName) {
        return rawType;
    }

    return options.includeType === true ? `${displayName}（${rawType}）` : displayName;
}
