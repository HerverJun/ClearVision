export const OPERATOR_DISPLAY_NAMES = Object.freeze({
    ImageAcquisition: '图像采集',
    BlobLabeling: 'Blob分类标注',
    PointAlignment: '点位偏差计算',
    RoiTransform: 'ROI位姿变换',
    PositionCorrection: 'ROI位姿补偿（像素）',
    PointCorrection: '点位刚性补偿',
    EdgePairDefect: '边缘间距缺陷检测',
    StatisticalOutlierRemoval: '点云统计离群点去除（SOR）',
    PPFMatch: 'PPF点云粗匹配',
    PlanarMatching: '平面特征匹配',
    ColorDetection: '颜色分析',
    GeometricTolerance: '二维几何公差判定',
    DetectionSequenceJudge: '检测顺序判定',
    ImageDiff: '图像差异率分析',
    RectangleRegion: '矩形框定义',
    CoordinateTransform: '像素到物理坐标（单点）',
    ROIManager: 'ROI裁剪与掩膜',
    RoiManager: 'ROI裁剪与掩膜',
    TryCatch: 'Try分支透传',
    ModbusCommunication: 'Modbus TCP通信',
    Threshold: '全局阈值处理',
    Thresholding: '全局阈值处理',
    FFT1D: '信号/图像傅里叶变换（FFT）',
    InverseFFT1D: '信号/图像逆傅里叶变换（IFFT）',
    PhaseClosure: '相位解缠绕',
    TemplateMatching: '模板匹配',
    TemplateMatch: '模板匹配',
    DeepLearning: '深度学习检测',
    CircleMeasurement: '圆测量',
    GeoMeasurement: '几何距离测量',
    Measurement: '几何测量',
    MeasureDistance: '距离测量',
    UnitConvert: '单位换算',
    ConditionJudge: '条件判断',
    ResultJudgment: '结果判定',
    ResultOutput: '结果输出',
    TcpCommunication: 'TCP通讯',
    SurfaceDefectDetection: '表面缺陷检测',
    ImageAdd: '图像叠加',
    ImageCompose: '图像合成',
    BlobAnalysis: '斑点分析',
    BinaryImageToRegion: '二值图转区域',
    RegionClosing: '区域闭运算',
    Grayscale: '灰度化',
    GaussianBlur: '高斯滤波',
    Filtering: '滤波处理'
});

export const PARAMETER_DISPLAY_NAMES = Object.freeze({
    ModelId: '模型资源',
    ModelPath: '模型资源',
    ModelCatalogPath: '模型资源',
    TemplateId: '模板资源',
    TemplatePath: '模板文件',
    Template: '模板资源',
    CameraId: '相机绑定',
    CameraBindingId: '相机绑定',
    SourceType: '采集源',
    FilePath: '图像文件',
    OutputChannelId: '输出通道',
    OutputChannel: '输出通道',
    Channel: '输出通道',
    Unit: '测量单位',
    PixelScale: '像素比例',
    Scale: '像素比例',
    CalibrationScale: '标定比例',
    Tolerance: '容差阈值',
    FieldName: '判定字段',
    Condition: '判定条件',
    ExpectedLabels: '期望标签',
    ExpectedCount: '期望数量',
    Value: '输入值',
    JudgmentResult: '判定结果',
    PlcAddress: 'PLC 地址',
    PLCParameters: 'PLC 参数',
    KernelSize: '滤波核尺寸',
    Mode: '处理模式',
    Rule: '判定规则',
    Input: '输入端口',
    Output: '输出端口'
});

export const RESOURCE_DISPLAY_NAMES = Object.freeze({
    model: '模型资源',
    model_resource: '模型资源',
    ModelId: '模型资源',
    ModelPath: '模型资源',
    template: '模板资源',
    template_artifact: '模板资源',
    template_resource: '模板资源',
    measurement_parameter: '测量参数/标定',
    calibration_parameter: '测量参数/标定',
    camera: '相机绑定',
    camera_binding: '相机绑定',
    output_channel: '输出通道',
    plc: 'PLC 参数',
    plc_parameter: 'PLC 参数',
    plc_address: 'PLC 地址',
    missingResources: '缺失资源',
    pendingActions: '待处理动作',
    structuralValidation: '结构校验',
    dryRun: '元数据预演',
    deploymentPrecheck: '部署预检',
    runtimePreview: '运行预演',
    toolTrace: '工具轨迹',
    operator_result_metadata: '算子结果元数据',
    frame_metadata: '帧元数据',
    artifact: '产物'
});

export const TOOL_DISPLAY_NAMES = Object.freeze({
    validate_flow: '流程校验工具',
    dry_run_flow: '元数据预演工具'
});

export const STATUS_DISPLAY_NAMES = Object.freeze({
    allowed: '允许',
    denied: '拒绝',
    ready: '就绪',
    not_ready: '未就绪',
    enabled: '启用',
    disabled: '禁用',
    success: '成功',
    failed: '失败',
    ok: '通过',
    metadata_only: '仅元数据',
    offline_runtime_preview: '离线元数据适配器',
    pilot_runtime_preview: '试点预演适配器',
    runtime_preview_camera_not_allowlisted: '运行预演相机未加入白名单',
    runtime_preview_external_path_denied: '运行预演外部路径被拒绝',
    offline_metadata_fallback_retained: '已保留离线元数据兜底',
    'offline metadata fallback retained': '已保留离线元数据兜底',
    RuntimePreviewPilotReadinessReview: '运行预演就绪复核',
    ProvideModelPath: '补齐模型资源',
    pending: '待处理'
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

export function getParameterDisplayName(parameterName, options = {}) {
    return getMappedDisplayName(PARAMETER_DISPLAY_NAMES, parameterName, options);
}

export function getResourceDisplayName(resourceType, options = {}) {
    return getMappedDisplayName(RESOURCE_DISPLAY_NAMES, resourceType, options);
}

export function getToolDisplayName(toolName, options = {}) {
    return getMappedDisplayName(TOOL_DISPLAY_NAMES, toolName, options);
}

export function getStatusDisplayName(status, options = {}) {
    return getMappedDisplayName(STATUS_DISPLAY_NAMES, status, options);
}

function getMappedDisplayName(map, value, options = {}) {
    const rawValue = String(value || '').trim();
    if (!rawValue) {
        return options.fallback || '';
    }

    const displayName = map[rawValue] || map[rawValue.toLowerCase()] || '';
    if (!displayName) {
        return options.fallback || rawValue;
    }

    return options.includeType === true ? `${displayName}（${rawValue}）` : displayName;
}
