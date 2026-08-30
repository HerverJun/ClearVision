import httpClient from '../../core/messaging/httpClient.js';
import { AiWorkbenchStates } from './aiPanelWorkbench.js';
import { AgentWorkspaceEventTypes } from './agentWorkspaceState.js';
import { isPendingParameterSentinel } from '../../shared/parameterDependencyRules.js';
import { normalizeCanonicalResource, serializeResourceDecision } from './aiResourceIdentity.js';

export const AgentWorkspaceModes = Object.freeze({
    PLAN: 'plan',
    BUILD: 'build',
    APPLIED: 'applied'
});

export const PLANNING_DEADLINE_FALLBACK = Object.freeze({
    contractVersion: 'v1',
    totalBudgetMs: 120000,
    clientNetworkMarginMs: 15000,
    minimumRepairBudgetMs: 5000
});

const BUILD_STAGE_ORDER = [
    'understand_requirement',
    'context_collection',
    'plan_generation',
    'assumption_confirmation',
    'requirement_parsing',
    'resolve_build_intent',
    'template_strategy',
    'operator_pipeline',
    'parameter_mapping',
    'planner',
    'workflow_draft',
    'validate_schema',
    'metadata_dry_run',
    'readiness',
    'manifest_dry_run',
    'package_readiness',
    'station_compatibility',
    'operator_contract',
    'release_review',
    'repair_loop',
    'workflow_diff',
    'apply_gate',
    'artifact',
    'run'
];

const BUILD_STAGE_LABELS = {
    understand_requirement: '理解需求',
    context_collection: '收集上下文',
    plan_generation: '生成计划',
    assumption_confirmation: '确认假设',
    requirement_parsing: '需求归一',
    resolve_build_intent: '构建意图',
    template_strategy: '模板策略',
    operator_pipeline: '算子链',
    parameter_mapping: '参数映射',
    planner: '规划与工具',
    tool_policy: '工具策略',
    workflow_draft: '流程草稿',
    validate_schema: '结构校验',
    metadata_dry_run: '元数据预演',
    readiness: '就绪检查',
    manifest_dry_run: '运行包预演',
    package_readiness: '运行包就绪',
    station_compatibility: '工站兼容',
    operator_contract: '算子契约',
    release_review: '发布复核',
    repair_loop: '自动修复',
    workflow_diff: '流程差异',
    apply_gate: '应用门禁',
    artifact: '结果产物',
    run: '运行',
    collecting_context: '收集上下文',
    planning_with_model: '模型规划',
    rule_fallback_used: '启用规则兜底',
    plan_ready: '规划就绪',
    validating_plan_contract: '校验规划契约',
    applying_safety_constraints: '应用安全约束'
};

const PLAN_PHASES = [
    { key: 'understand', label: '理解需求' },
    { key: 'context', label: '整理工程上下文' },
    { key: 'generate', label: '生成方案' },
    { key: 'validate', label: '校验方案' }
];

const PLAN_PENDING_STATUS = 'waiting';
const PLANNING_SLOW_FEEDBACK_MS = 6000;
const LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE = 'legacy_build_artifact_missing_canonical_flow';
const LEGACY_BUILD_MISSING_CANONICAL_FLOW_MESSAGE = '该构建结果不包含可验证的画布流程产物，无法直接应用。\n请基于原计划重新构建。';

const PLAN_ANSWER_FIELDS = Object.freeze({
    INSPECTION_OBJECT: 'inspection_object',
    TASK_TYPE: 'task_type',
    IMAGE_SOURCE: 'image_source',
    ACCEPTANCE_CRITERIA: 'acceptance_criteria',
    OUTPUT_TARGET: 'output_target',
    TARGET_ATTRIBUTE: 'target_attribute',
    DEFECT_TYPE: 'defect_type',
    MEASUREMENT_TARGET: 'measurement_target',
    ALGORITHM_STRATEGY: 'algorithm_strategy',
    ROI_STRATEGY: 'roi_strategy',
    TEMPLATE_STRATEGY: 'template_strategy'
});

const PLAN_ANSWER_ORIGINS = Object.freeze({
    EXPLICIT_USER_SELECTION: 'explicit_user_selection',
    ACCEPTED_RECOMMENDED_DEFAULT: 'accepted_recommended_default',
    EXPLICIT_USER_TEXT: 'explicit_user_text'
});

const PLAN_BUILD_BLOCKER_CATEGORIES = Object.freeze({
    HARD_REQUIREMENT: 'hard_requirement',
    STRATEGY_CONFIRMATION: 'strategy_confirmation',
    RESOURCE_PENDING: 'resource_pending',
    CONTRACT_WARNING: 'contract_warning',
    SAFETY_BLOCKER: 'safety_blocker'
});

const PLAN_BUILD_RESOLUTION_MODES = Object.freeze({
    ANSWER_QUESTION: 'answer_question',
    ACCEPT_RECOMMENDED: 'accept_recommended',
    PROVIDE_RESOURCE: 'provide_resource',
    NON_BLOCKING: 'non_blocking'
});

const PLAN_ANSWER_EFFECTS = Object.freeze({
    RESOLVE_FIELD: 'resolve_field',
    DEFER: 'defer',
    INFORMATIONAL: 'informational'
});

const PLAN_QUESTION_FIELD_BY_ID = Object.freeze({
    object_type: PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
    product_type: PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
    part_type: PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
    inspection_target: PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
    detection_target: PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
    task_category: PLAN_ANSWER_FIELDS.TASK_TYPE,
    detection_task: PLAN_ANSWER_FIELDS.TASK_TYPE,
    inspection_task: PLAN_ANSWER_FIELDS.TASK_TYPE,
    visual_task: PLAN_ANSWER_FIELDS.TASK_TYPE,
    medical_modality: PLAN_ANSWER_FIELDS.TASK_TYPE,
    lesion_type: PLAN_ANSWER_FIELDS.TASK_TYPE,
    medical_modality_and_lesion_type: PLAN_ANSWER_FIELDS.TASK_TYPE,
    image_input: PLAN_ANSWER_FIELDS.IMAGE_SOURCE,
    input_source: PLAN_ANSWER_FIELDS.IMAGE_SOURCE,
    source_image: PLAN_ANSWER_FIELDS.IMAGE_SOURCE,
    camera_source: PLAN_ANSWER_FIELDS.IMAGE_SOURCE,
    image_source_roi: PLAN_ANSWER_FIELDS.IMAGE_SOURCE,
    ok_condition: PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    ng_condition: PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    judgment_rule: PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    result_rule: PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    output_target: PLAN_ANSWER_FIELDS.OUTPUT_TARGET,
    output_goal: PLAN_ANSWER_FIELDS.OUTPUT_TARGET,
    output_destination: PLAN_ANSWER_FIELDS.OUTPUT_TARGET,
    result_output: PLAN_ANSWER_FIELDS.OUTPUT_TARGET,
    local_result_payload: PLAN_ANSWER_FIELDS.OUTPUT_TARGET,
    structured_result_output: PLAN_ANSWER_FIELDS.OUTPUT_TARGET,
    business_system_output: PLAN_ANSWER_FIELDS.OUTPUT_TARGET,
    classification_strategy: PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY,
    model_or_rule_strategy: PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY,
    algorithm_strategy: PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY,
    defect_definition: PLAN_ANSWER_FIELDS.DEFECT_TYPE,
    attribute_target: PLAN_ANSWER_FIELDS.TARGET_ATTRIBUTE,
    ok_ng_rule: PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    presence_judgment: PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    decode_policy: PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    sequence_rule: PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    measurement_target: PLAN_ANSWER_FIELDS.MEASUREMENT_TARGET,
    template_asset: PLAN_ANSWER_FIELDS.TEMPLATE_STRATEGY
});

const PLAN_CANONICAL_FIELDS = new Set(Object.values(PLAN_ANSWER_FIELDS));
const PLAN_STRICT_BUILD_FIELDS = new Set([
    PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
    PLAN_ANSWER_FIELDS.TASK_TYPE,
    PLAN_ANSWER_FIELDS.IMAGE_SOURCE,
    PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
    PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY
]);

const AI_DISPLAY_TEXT_MAP = {
    'Accept recommended defaults, then start Build.': '可按推荐项确认后开始构建。',
    'Accept recommended defaults or answer questions, then start Build.': '请确认推荐项或手动回答问题后开始构建。',
    'Accept recommended defaults and Build.': '按推荐项确认并构建。',
    'Describe the inspection target before Build can start.': '请先描述检测目标，再开始构建。',
    'Surface defect inspection route': '表面缺陷检测路线',
    'Detect visible scratches and blobs.': '检测可见划痕、斑点等表面缺陷。',
    'Inspection intent: surface defect inspection.': '需求意图：表面缺陷检测。',
    'What should count as a defect?': '缺陷判定标准是什么？',
    'Defect definition controls thresholds and judgment.': '缺陷定义会影响阈值和判定逻辑。',
    'Thresholds depend on defect definition.': '阈值取决于缺陷定义。',
    'Scratch/blob': '划痕/斑点',
    'Use general surface defect candidates.': '使用通用表面缺陷候选区域。',
    'Good first draft.': '适合作为初始草稿。',
    'Crack': '裂纹',
    'Dent/stain': '凹痕/污渍',
    'Emphasize thin dark/bright crack-like defects.': '重点检测细长明暗裂纹类缺陷。',
    'Look for dents, stain, or discoloration.': '关注凹痕、污渍或变色。',
    'Look for dents, stains, or discoloration.': '关注凹痕、污渍或变色。',
    'Needs contrast assumptions.': '需要确认对比度假设。',
    'Needs lighting/sample confirmation.': '需要确认光照和样品条件。',
    'Thresholds need sample confirmation.': '阈值需要结合样品确认。',
    'Thresholds need representative images.': '阈值需要代表性图像确认。',
    'Detect visible scratches/blobs and judge by area/contrast.': '检测可见划痕/斑点，并按面积和对比度判定。',
    'Use selected template first.': '优先使用已选模板。',
    'Missing resources stay pending': '缺失资源保持待确认',
    'No resource path is guessed.': '不会猜测资源路径。',
    'Workflow draft contains acquisition, inspection, judgment, and output stages.': '流程草稿包含采集、检测、判定和输出阶段。',
    'Map parameters and run readiness checks.': '映射参数并运行就绪检查。',
    'Context collected': '已收集工程上下文',
    'Collected public requirement, flow, template, attachment, operator, and Station boundary metadata.': '已收集公开需求、流程、模板、附件、算子和工站边界元数据。',
    'collecting_context completed': '上下文收集完成',
    'planning_with_model started': '模型规划已开始',
    'rule_fallback_used completed': '已启用规则兜底',
    'plan_ready completed': '规划已就绪',
    'Rule fallback used': '已启用规则兜底',
    'Fallback plan ready': '兜底规划已就绪',
    'Rule fallback PlanModeResult is ready for user confirmation.': '规则兜底规划已就绪，等待用户确认。',
    'Planner completion is disabled; using rule fallback plan.': '模型规划未启用，已使用规则兜底方案。',
    'Planner failed contract generation; using rule fallback plan.': '模型规划失败，已使用规则兜底方案。',
    'Planner timed out; using rule fallback plan.': '模型规划超时，已使用规则兜底方案。',
    '模型规划超时，已使用规则兜底方案。': '模型规划超时，已使用规则兜底方案。',
    'Plan ready': '规划已就绪',
    'Planner started': '规划器已开始',
    'Planning with model': '模型规划中',
    'Building a public execution plan.': '正在生成公开执行计划。',
    'Fetch streamed before terminal.': '事件流已在终态前返回。',
    'Replay completed.': '回放已完成。',
    'Replay mode completed.': '回放模式已完成。',
    'Already rendered.': '已渲染。',
    'Done.': '已完成。',
    'Tool completed: validate_flow': '工具完成：流程校验',
    'Release review completed': '发布复核已完成',
    'Release review completed.': '发布复核已完成',
    'Metadata-only release review passed.': '仅元数据发布复核已通过。',
    'No fix required.': '当前无需修复。',
    'missing threshold': '缺少阈值',
    'Stub dryrun completed.': '元数据预演已完成。',
    'Review readiness': '复核运行预演就绪条件。',
    'not allowlisted': '未加入白名单。',
    'offline metadata fallback retained': '已保留离线元数据兜底。',
    'RuntimePreviewPilotReadinessReview': '运行预演就绪复核',
    'ProvideModelPath': '补齐模型资源',
    'offline_runtime_preview': '离线元数据适配器',
    'pilot_runtime_preview': '试点预演适配器',
    'metadata_only': '仅元数据',
    'runtime_preview_camera_not_allowlisted': '运行预演相机未加入白名单',
    'runtime_preview_external_path_denied': '运行预演外部路径被拒绝',
    'Planner model is generating a structured PlanModeResult candidate.': '模型正在生成结构化规划候选。',
    'Planner candidate returned': '模型规划候选已返回',
    'Planner returned a public structured candidate for validation.': '模型已返回公开结构化候选，等待校验。',
    'Validating plan contract': '校验规划契约',
    'Validating JSON shape, question quality, operator catalog, and template constraints.': '正在校验 JSON 结构、问题质量、算子目录和模板约束。',
    'Plan contract valid': '规划契约已校验',
    'Planner plan was normalized to the public PlanModeResult contract.': '模型规划已归一到公开 PlanModeResult 契约。',
    'Safety constraints applied': '安全约束已应用',
    'Redaction, metadata-only boundaries, resource placeholders, and PLC safety policy were applied.': '已应用脱敏、元数据边界、资源占位和 PLC 安全策略。',
    'Planner-sourced PlanModeResult is ready for user confirmation.': '模型规划已就绪，等待用户确认。',
    'Run completed': '运行已完成',
    'Build completed.': '构建完成。',
    'Build completed with public metadata.': '已使用公开元数据完成构建。',
    'Validated draft metadata.': '流程草稿元数据已校验。',
    'Tool completed.': '工具执行完成。',
    'Editable draft ready': '可编辑草稿已就绪',
    'Deployment blocked': '部署已阻断',
    'Deployment blocked by missing resources.': '部署因缺失资源被阻断。',
    'Canvas: ready': '画布：可应用',
    'Runtime draft': '运行草稿',
    'Deployment: blocked': '部署：阻断',
    'invalid operator was repaired': '非法算子已修复',
    'NG when scratch candidate exceeds pending threshold.': '当划痕候选超过待确认阈值时判为 NG。',
    'Bind model_resource metadata before deployment.': '部署前绑定模型资源元数据。',
    'Bind missing model_resource metadata for op_detect.ModelId before deployment.': '部署前绑定 op_detect.模型资源 的模型资源元数据。',
    'LLM-requested tool completed with public metadata.': 'LLM 主动调用的工具已返回公开元数据。'
};

const AI_CODE_TEXT_MAP = {
    rule_fallback: '规则兜底',
    rule_baseline: '规则基线',
    planner: '模型规划',
    model_planner: '模型规划',
    llm_planner: '模型规划',
    backend_planner: '后端规划',
    planner_failed: '模型规划失败，已使用规则兜底方案',
    planner_disabled: '模型规划未启用，已使用规则兜底方案',
    planner_timeout: '模型规划超时，已使用规则兜底方案',
    planner_unauthorized: '模型规划鉴权失败，已使用规则兜底方案',
    completion_request_failed: '模型请求失败',
    completion_empty: '模型返回空内容',
    planner_json_parse_failed: 'JSON 解析失败',
    planner_contract_repair_failed: '契约修复失败',
    planner_unknown_error: '模型规划未知错误',
    completion_request: '模型请求阶段',
    completion_response: '模型返回阶段',
    json_parse: 'JSON 解析阶段',
    contract_repair: '契约修复阶段',
    contract_repaired: '契约已修复',
    high: '高',
    medium: '中',
    low: '低',
    started: '已开始',
    running: '执行中',
    completed: '已完成',
    blocked: '已阻断',
    failed: '失败',
    cancelled: '已取消',
    canceled: '已取消',
    pending: '等待中',
    file: '文件',
    camera: '相机',
    true: '是',
    false: '否',
    template_skeleton: '模板骨架',
    plan_route: '规划路线',
    catalog_required: '目录必需',
    accepted_default: '已接受默认值',
    selected: '已选择',
    plan_default: '规划默认值',
    missing_resource: '缺失资源',
    pending_parameters: '待确认参数',
    redacted_metadata: '脱敏元数据',
    allow_editable_draft_when_not_deploy_ready: '部署未就绪时仍允许编辑草稿',
    mapped: '已映射',
    template_fill: '模板填充',
    template_adapt: '模板适配',
    catalog_match: '目录匹配',
    no_template: '不使用模板',
    deployment_resource_pending: '部署资源待绑定',
    plan_hash_mismatch: '计划哈希不一致',
    invalid_operator_repaired: '非法算子已修复',
    missing_parameter: '参数缺失',
    schema_validation_warning: '结构校验警告',
    model_resource: '模型资源',
    template_artifact: '模板资源',
    measurement_parameter: '测量参数',
    camera_binding: '相机绑定',
    output_channel: '输出通道',
    plc_address: 'PLC 地址',
    fixed_build_orchestrator: '固定构建链路',
    fallback_build_orchestrator: '回退构建链路',
    mode_mismatch: 'mode 不匹配',
    not_enabled: '未启用',
    permission_denied: '权限拒绝',
    protocol_failed: '协议失败',
    max_tool_rounds_exceeded: 'MaxToolRounds 超限',
    template_not_found: '未找到匹配模板骨架，已改用算子链生成',
    required_template_missing: '必需模板骨架缺失',
    tool_permission_denied: '工具权限被拒绝，已回退稳定构建链路',
    unknown_tool: '未知工具被拒绝，已回退稳定构建链路',
    runtime_preview_consent_required: 'RuntimePreview 需要显式授权，已回退稳定构建链路',
};

const AI_OPERATOR_LABELS = {
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
    SurfaceDefectDetection: '表面缺陷检测',
    BlobAnalysis: 'Blob分析',
    BinaryImageToRegion: '二值图转区域',
    RegionClosing: '区域闭运算',
    Grayscale: '灰度化',
    GaussianBlur: '高斯滤波',
    DeepLearning: '深度学习',
    TemplateMatch: '模板匹配',
    TemplateMatching: '模板匹配',
    CircleMeasurement: '圆测量',
    GeoMeasurement: '几何测量',
    Measurement: '测量',
    MeasureDistance: '距离测量',
    UnitConvert: '单位换算',
    ConditionJudge: '条件判断',
    ImageAdd: '图像加法',
    ResultJudgment: '结果判定',
    ResultOutput: '结果输出',
    TcpCommunication: 'TCP通信'
};

const LEGACY_PORT_DATA_TYPES = Object.freeze({
    0: 'Image',
    1: 'Integer',
    2: 'Float',
    3: 'Boolean',
    4: 'String',
    5: 'Point',
    6: 'Rectangle',
    7: 'Contour',
    8: 'PointList',
    9: 'DetectionResult',
    10: 'DetectionList',
    11: 'CircleData',
    12: 'LineData',
    13: 'Region',
    14: 'BlobList',
    15: 'BlobFeatureList',
    99: 'Any'
});

const AI_PARAMETER_LABELS = {
    ModelId: '模型资源',
    ModelPath: '模型资源',
    ModelCatalogPath: '模型资源',
    Template: '模板资源',
    TemplatePath: '模板文件',
    TemplateId: '模板资源',
    Unit: '测量单位/像素比例',
    PixelScale: '像素比例',
    Scale: '像素比例',
    CalibrationScale: '标定比例',
    Tolerance: '容差阈值',
    Rule: '判定规则',
    FieldName: '判定字段',
    Condition: '判定条件',
    ExpectedLabels: '期望标签',
    ExpectedCount: '期望数量',
    Value: '输入值',
    JudgmentResult: '判定结果',
    CameraId: '相机绑定',
    CameraBindingId: '相机绑定',
    OutputChannelId: '输出通道',
    OutputChannel: '输出通道',
    Channel: '输出通道',
    PlcAddress: 'PLC 地址',
    PLCParameters: 'PLC 参数',
    SourceType: '采集源',
    FilePath: '图像文件'
};

export const aiPanelAgentWorkspaceMixin = {
    _getPlanningClientDeadlineMs() {
        const contract = this.planningDeadlineContract || PLANNING_DEADLINE_FALLBACK;
        const totalBudgetMs = Number(contract.totalBudgetMs);
        const clientNetworkMarginMs = Number(contract.clientNetworkMarginMs);
        if (!Number.isFinite(totalBudgetMs) || totalBudgetMs < 1000 ||
            !Number.isFinite(clientNetworkMarginMs) || clientNetworkMarginMs < 1000) {
            return PLANNING_DEADLINE_FALLBACK.totalBudgetMs + PLANNING_DEADLINE_FALLBACK.clientNetworkMarginMs;
        }
        return totalBudgetMs + clientNetworkMarginMs;
    },

    _getPlanningBackendBudgetMs() {
        const contract = this.planningDeadlineContract || PLANNING_DEADLINE_FALLBACK;
        const configuredBudgetMs = Number(contract.totalBudgetMs);
        return Number.isFinite(configuredBudgetMs) && configuredBudgetMs >= 1000
            ? configuredBudgetMs
            : PLANNING_DEADLINE_FALLBACK.totalBudgetMs;
    },

    _getPlanningBackendRemainingMs() {
        const totalBudgetMs = this._getPlanningBackendBudgetMs?.() || PLANNING_DEADLINE_FALLBACK.totalBudgetMs;
        const deadlineAt = Number(this.planningLifecycle?.backendDeadlineAt);
        if (!Number.isFinite(deadlineAt) || deadlineAt <= 0) return totalBudgetMs;
        return Math.max(1, Math.floor(deadlineAt - Date.now()));
    },

    async _refreshPlanningDeadlineContract() {
        try {
            const result = await httpClient.get('/ai/vision-agent/planning-deadline');
            const data = this._asObject?.(result) || result || {};
            const contractVersion = String(data.contractVersion || data.ContractVersion || '').trim();
            const totalBudgetMs = Number(data.totalBudgetMs ?? data.TotalBudgetMs);
            const clientNetworkMarginMs = Number(data.clientNetworkMarginMs ?? data.ClientNetworkMarginMs);
            const minimumRepairBudgetMs = Number(data.minimumRepairBudgetMs ?? data.MinimumRepairBudgetMs);
            if (contractVersion === PLANNING_DEADLINE_FALLBACK.contractVersion &&
                Number.isFinite(totalBudgetMs) && totalBudgetMs >= 1000 &&
                Number.isFinite(clientNetworkMarginMs) && clientNetworkMarginMs >= 1000) {
                this.planningDeadlineContract = {
                    contractVersion,
                    totalBudgetMs,
                    clientNetworkMarginMs,
                    minimumRepairBudgetMs: Number.isFinite(minimumRepairBudgetMs) && minimumRepairBudgetMs >= 500
                        ? minimumRepairBudgetMs
                        : PLANNING_DEADLINE_FALLBACK.minimumRepairBudgetMs
                };
            }
        } catch {
            this.planningDeadlineContract = this.planningDeadlineContract || { ...PLANNING_DEADLINE_FALLBACK };
        }
        return this.planningDeadlineContract;
    },

    _getPlanningDeadlineError(error) {
        const payload = this._asObject?.(error?.payload) || error?.payload || {};
        const errorCode = String(
            payload.errorCode || payload.ErrorCode || error?.errorCode || ''
        ).trim().toLowerCase();
        if (errorCode !== 'planning_deadline_exceeded') return null;
        return {
            errorCode,
            timeoutKind: String(
                payload.timeoutKind || payload.TimeoutKind || error?.timeoutKind || 'total_budget_exceeded'
            ).trim().toLowerCase(),
            stage: String(payload.stage || payload.Stage || error?.stage || '').trim()
        };
    },

    _handlePlanningDeadlineExceeded(error, turn = this.activeAssistantTurn) {
        const deadlineError = this._getPlanningDeadlineError?.(error);
        if (!deadlineError) return false;
        if (this.planningLifecycle) {
            this.planningLifecycle.timeoutKind = deadlineError.timeoutKind || 'total_budget_exceeded';
            this.planningLifecycle.timeoutStage = deadlineError.stage;
        }
        this._setGeneratingState?.(false);
        this.isCancellingGenerate = false;
        this._setWorkbenchState?.(AiWorkbenchStates.FAILED);
        this._setAssistantTurnStatus?.(turn, '规划超时', 'failed');
        this._setAssistantSectionText?.(turn, 'reply', '规划超过后端公布的总时间预算，请重试本次需求。');
        this._setResultStatusNote?.('规划超过后端公布的总时间预算，请重试。', 'warning');
        this._markPlanningLifecycleTerminal?.(
            'timeout',
            '规划超过后端公布的总时间预算，请重试本次需求。',
            { preserveReply: true });
        this._renderAgentWorkspaceOverview?.();
        this._renderPlanWorkspace?.(this.pendingVisionPlan);
        this._updatePlanBuildActionState?.();
        return true;
    },

    _clearPlanningLifecycleTimers() {
        const lifecycle = this.planningLifecycle;
        if (!lifecycle) return;
        if (lifecycle.slowTimer) window.clearTimeout?.(lifecycle.slowTimer);
        if (lifecycle.timeoutTimer) window.clearTimeout?.(lifecycle.timeoutTimer);
        lifecycle.slowTimer = null;
        lifecycle.timeoutTimer = null;
    },

    _beginPlanningLifecycle({ requestId, requestContext, turn, phase = 'understand' } = {}) {
        this._clearPlanningLifecycleTimers?.();
        this.lastPlanningRequestContext = requestContext || this.lastPlanningRequestContext || null;
        const startedAt = Date.now();
        const clientDeadlineMs = this._getPlanningClientDeadlineMs?.() ||
            (PLANNING_DEADLINE_FALLBACK.totalBudgetMs + PLANNING_DEADLINE_FALLBACK.clientNetworkMarginMs);
        const backendBudgetMs = this._getPlanningBackendBudgetMs?.() || PLANNING_DEADLINE_FALLBACK.totalBudgetMs;
        this.planningLifecycle = {
            requestId: String(requestId || '').trim(),
            timelineId: String(requestId || '').trim() || `planning-${Date.now()}`,
            status: 'running',
            phase,
            routerStatus: phase === 'understand' ? 'running' : 'waiting',
            routerSummary: phase === 'understand' ? '正在理解需求，尚未标记完成。' : '',
            currentSummary: phase === 'understand'
                ? '正在理解需求，等待 Intent Router 返回。'
                : '正在整理工程上下文。',
            slow: false,
            startedAt,
            backendDeadlineAt: startedAt + backendBudgetMs,
            clientDeadlineAt: startedAt + clientDeadlineMs,
            turn: turn || this.activeAssistantTurn || null,
            slowTimer: null,
            timeoutTimer: null
        };
        this._armPlanningLifecycleTimers?.(
            this.planningLifecycle.requestId,
            clientDeadlineMs);
        this._renderPlanningProgress?.(turn);
        return this.planningLifecycle;
    },

    _armPlanningLifecycleTimers(requestId, timeoutMs) {
        const lifecycle = this.planningLifecycle;
        if (!lifecycle || lifecycle.status !== 'running') return;
        this._clearPlanningLifecycleTimers?.();
        lifecycle.slowTimer = window.setTimeout?.(() => {
            const current = this.planningLifecycle;
            if (!current || current.status !== 'running' || current.requestId !== requestId) return;
            current.slow = true;
            current.currentSummary = current.phase === 'understand'
                ? '响应较慢，系统仍在理解需求；当前阶段尚未标记完成。'
                : '规划仍在进行，正在等待下一条公开事件。';
            this._renderPlanningProgress?.(current.turn);
        }, PLANNING_SLOW_FEEDBACK_MS);
        lifecycle.timeoutTimer = window.setTimeout?.(() => {
            const current = this.planningLifecycle;
            if (!current || current.status !== 'running' || current.requestId !== requestId) return;
            current.timeoutKind = 'client_deadline_exceeded';
            this._markPlanningLifecycleTerminal?.('timeout', '规划等待超时，未将未完成阶段标记为完成。可重试本次需求。');
            this.activeIntentRouterRequestId = null;
            this._clearActivePlanRequest?.();
            this._closeAgentRunEventSource?.();
            this._setGeneratingState?.(false);
            this.isCancellingGenerate = false;
            this._setWorkbenchState?.(AiWorkbenchStates.FAILED);
            this._renderAgentWorkspaceOverview?.();
            this._renderPlanWorkspace?.(this.pendingVisionPlan);
        }, timeoutMs);
    },

    _advancePlanningLifecycle(phase, summary, { routerStatus, routerSummary, requestId } = {}) {
        const lifecycle = this.planningLifecycle;
        if (!lifecycle || lifecycle.status !== 'running') return;
        this._clearPlanningLifecycleTimers?.();
        lifecycle.phase = phase || lifecycle.phase;
        lifecycle.currentSummary = String(summary || lifecycle.currentSummary || '').trim();
        lifecycle.slow = false;
        if (routerStatus) lifecycle.routerStatus = routerStatus;
        if (routerSummary) lifecycle.routerSummary = routerSummary;
        if (requestId) lifecycle.requestId = String(requestId).trim();
        const clientDeadlineAt = Number(lifecycle.clientDeadlineAt);
        const remainingClientMs = Number.isFinite(clientDeadlineAt) && clientDeadlineAt > 0
            ? Math.max(1, Math.floor(clientDeadlineAt - Date.now()))
            : this._getPlanningClientDeadlineMs?.() ||
                (PLANNING_DEADLINE_FALLBACK.totalBudgetMs + PLANNING_DEADLINE_FALLBACK.clientNetworkMarginMs);
        this._armPlanningLifecycleTimers?.(
            lifecycle.requestId,
            remainingClientMs);
        this._renderPlanningProgress?.(lifecycle.turn);
    },

    _markPlanningLifecycleTerminal(status, message, { preserveReply = false } = {}) {
        const lifecycle = this.planningLifecycle || {};
        this._clearPlanningLifecycleTimers?.();
        lifecycle.status = status;
        lifecycle.currentSummary = String(message || '').trim();
        lifecycle.slow = false;
        lifecycle.turn = lifecycle.turn || this.activeAssistantTurn || null;
        this.planningLifecycle = lifecycle;
        const tone = status === 'completed' ? 'success' : status === 'cancelled' ? 'cancelled' : 'failed';
        const label = status === 'completed' ? '规划完成' : status === 'cancelled' ? '已取消' : status === 'timeout' ? '规划超时' : '规划失败';
        this._setAssistantTurnStatus?.(lifecycle.turn, label, tone);
        if (lifecycle.currentSummary && !preserveReply) {
            this._setAssistantSectionText?.(lifecycle.turn, 'reply', lifecycle.currentSummary);
        }
        this._renderPlanningProgress?.(lifecycle.turn);
    },

    _renderPlanningProgress(turn = this.planningLifecycle?.turn || this.activeAssistantTurn) {
        this._renderPlanRunTimeline?.(turn);
        if (this.pendingVisionPlan) return;
        this._renderPlanWorkspace?.(null);
    },

    _retryPlanningLifecycle() {
        const context = this.lastPlanningRequestContext;
        if (!context?.description) return false;
        this._enterIntentRouterFromPrompt?.({
            ...context,
            clearInput: false,
            input: null,
            addUserMessage: false
        });
        return true;
    },

    _cancelPendingPlanningRequest() {
        const lifecycle = this.planningLifecycle;
        if (!lifecycle || lifecycle.status !== 'running') return false;
        this.activeIntentRouterRequestId = null;
        this._clearActivePlanRequest?.();
        this._closeAgentRunEventSource?.();
        this._setGeneratingState?.(false);
        this.isCancellingGenerate = false;
        this._setWorkbenchState?.(AiWorkbenchStates.CANCELLED);
        this._markPlanningLifecycleTerminal?.('cancelled', '规划已取消，未完成阶段保持未完成。可随时重试本次需求。');
        this._renderAgentWorkspaceOverview?.();
        this._renderPlanWorkspace?.(this.pendingVisionPlan);
        return true;
    },

    _resetAgentWorkspace({ preservePlan = false } = {}) {
        this._clearPlanningLifecycleTimers?.();
        this.planningLifecycle = null;
        this.activeIntentRouterRequestId = null;
        this.activePlanRequestId = null;
        this.activePlanRunId = null;
        this.activePlanRunRequestId = null;
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();
        this.activePlanRunCompletion = null;
        if (!preservePlan) {
            this.pendingClarificationPayload = null;
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.RESET,
                payload: { preserveSession: true, requirementMode: 'strict' }
            });
            this.planRequirementModes = new Map();
            this.currentPlanIdentity = '';
            this._resetPlanReadinessPreviewState?.({ abort: true });
        } else {
            this._activatePlanIdentity?.(this.pendingVisionPlan);
        }

        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.VIEW_CHANGED,
            payload: { mode: AgentWorkspaceModes.PLAN }
        });
        this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { persist: false, render: false });
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
        this._updatePlanBuildActionState();
    },

    _loadWorkspaceViewMode() {
        try {
            return this._normalizeWorkspaceViewMode(localStorage.getItem(this.workspaceViewStorageKey));
        } catch {
            return AgentWorkspaceModes.PLAN;
        }
    },

    _saveWorkspaceViewMode(mode) {
        try {
            localStorage.setItem(this.workspaceViewStorageKey, this._normalizeWorkspaceViewMode(mode));
        } catch {
            // ignore localStorage failures
        }
    },

    _normalizeWorkspaceViewMode(mode) {
        const normalized = String(mode || '').trim().toLowerCase();
        return normalized === AgentWorkspaceModes.BUILD ? AgentWorkspaceModes.BUILD : AgentWorkspaceModes.PLAN;
    },

    _canViewPlanWorkspace() {
        return Boolean(this.pendingVisionPlan || this.activePlanRunId || this.activePlanRunEvents?.length);
    },

    _canViewBuildWorkspace() {
        return Boolean(this.activeAgentRunId || this.activeAgentRunEvents?.length || this.currentResult?.buildResult || this.currentResult?.BuildResult);
    },

    _getWorkspaceViewMode() {
        const requested = this._normalizeWorkspaceViewMode(this.workspaceViewMode);
        if (requested === AgentWorkspaceModes.BUILD && !this._canViewBuildWorkspace()) {
            return AgentWorkspaceModes.PLAN;
        }
        return requested;
    },

    _setWorkspaceViewMode(mode, { persist = true, render = true } = {}) {
        const requested = this._normalizeWorkspaceViewMode(mode);
        this.workspaceViewMode = requested === AgentWorkspaceModes.BUILD && !this._canViewBuildWorkspace()
            ? AgentWorkspaceModes.PLAN
            : requested;

        if (persist) {
            this._saveWorkspaceViewMode(this.workspaceViewMode);
        }

        if (render) {
            this._renderAgentWorkspaceOverview();
            this._renderPlanWorkspace(this.pendingVisionPlan);
            this._renderBuildWorkspaceFromAgentRun();
        }
    },

    _isPlanSnapshotReadOnly() {
        return Boolean(
            String(this.workspaceBuildRunId || this.activeAgentRunId || '').trim() ||
            String(this.workspaceSubmittedBuildFingerprint || '').trim()
        );
    },

    _warnPlanReadOnly() {
        this._setResultStatusNote?.('Build 已提交，当前 Plan 快照只读。请新建 Plan 修订后再调整。', 'warning');
        this._addMessage?.('system', 'Build 已提交，当前 Plan 快照只读。请新建 Plan 修订后再调整。');
    },

    _buildWorkspaceSnapshotDelta() {
        const workspace = this._createAgentWorkspaceSnapshot?.() || {};
        return {
            lifecycleState: this._getAgentWorkspacePhase?.() || AgentWorkspaceModes.PLAN,
            planQuestionSelections: { ...(this.planQuestionSelections || {}) },
            confirmedPlanAnswers: this._toArray(workspace.confirmedAnswers),
            optimisticPlanAnswers: this._toArray(workspace.optimisticAnswers),
            answerRevision: Number(workspace.answerRevision) || 0,
            readinessPreview: workspace.readinessPreview || null,
            missingResources: workspace.missingResources || [],
            resourceDecisions: workspace.resourceDecisions || {},
            resourceRevision: Number(workspace.resourceRevision) || 0,
            requirementMode: this.requirementMode || 'strict',
            workspaceViewMode: this.workspaceViewMode || AgentWorkspaceModes.PLAN,
            planAcceptedRecommendedDefaults: this.planAcceptedRecommendedDefaults === true,
            submittedBuildFingerprint: this.workspaceSubmittedBuildFingerprint || ''
        };
    },

    _syncWorkspaceSnapshotDirty() {
        this.workspaceSnapshotDirty =
            Number(this.workspacePersistedGeneration || 0) < Number(this.workspaceMutationGeneration || 0);
        return this.workspaceSnapshotDirty;
    },

    _isCurrentWorkspaceSaveTask(task) {
        if (!task || this._disposed) return false;
        const currentPlanIdentity = this._getPlanIdentity?.(this.pendingVisionPlan) || '';
        return String(task.sessionId || '').trim().toLowerCase() === String(this.sessionId || '').trim().toLowerCase() &&
            Number(task.sessionNavigationEpoch || 0) === Number(this.sessionNavigationEpoch || 0) &&
            String(task.planIdentity || '') === String(currentPlanIdentity || '');
    },

    _isWorkspaceMutationBlocked() {
        if (this.workspaceBoundaryInProgress) {
            this._setResultStatusNote?.('Plan 修改正在保存，请稍后再编辑。', 'warning');
            return true;
        }
        if (this._isPlanSnapshotReadOnly()) {
            this._warnPlanReadOnly();
            return true;
        }
        return false;
    },

    _queueWorkspaceSnapshotFlush(reason = 'edit') {
        if (!this.sessionId || !this.pendingVisionPlan) {
            return Promise.resolve({ skipped: true });
        }
        if (!Number.isFinite(Number(this.workspaceSnapshotRevision)) || Number(this.workspaceSnapshotRevision) <= 0) {
            return Promise.resolve({ skipped: true, missingBaseline: true });
        }
        if (this._isPlanSnapshotReadOnly()) {
            return Promise.resolve({ skipped: true, readOnly: true });
        }
        if (this.workspaceBoundaryInProgress && reason !== 'boundary_retry') {
            return Promise.reject(new Error('workspace_boundary_in_progress'));
        }

        const mutationId = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
        const delta = this._buildWorkspaceSnapshotDelta();
        const planIdentity = this._getPlanIdentity?.(this.pendingVisionPlan) || '';
        const task = Object.freeze({
            sessionId: String(this.sessionId || '').trim(),
            sessionNavigationEpoch: Number(this.sessionNavigationEpoch || 0),
            planIdentity,
            generation: Number(this.workspaceMutationGeneration || 0) + 1,
            mutationId,
            delta: Object.freeze({ ...delta }),
            reason
        });
        this.workspaceMutationGeneration = task.generation;
        this.workspacePendingMutationCount = Number(this.workspacePendingMutationCount || 0) + 1;
        this._syncWorkspaceSnapshotDirty();
        const run = async () => {
            let expectedRevision = Number(this.workspaceSnapshotRevision || 0);
            let rebased = false;
            try {
                while (true) {
                    try {
                        const result = await httpClient.post(`/ai/sessions/${encodeURIComponent(task.sessionId)}/workspace-snapshot`, {
                            expectedRevision,
                            clientMutationId: task.mutationId,
                            ...task.delta
                        });
                        if (this._isCurrentWorkspaceSaveTask(task)) {
                            this._applyWorkspaceSnapshotSummary?.(result?.snapshot || result?.Snapshot || null);
                            this._handleWorkspacePersistenceStatus?.(result?.persistenceStatus || result?.PersistenceStatus || null);
                            this.workspacePersistedGeneration = Math.max(
                                Number(this.workspacePersistedGeneration || 0),
                                task.generation);
                            this._syncWorkspaceSnapshotDirty();
                        }
                        return result;
                    } catch (error) {
                        const payload = error?.payload || {};
                        const errorCode = String(payload?.errorCode ?? payload?.ErrorCode ?? '').trim();
                        const isConflict = error?.status === 409 ||
                            error?.statusCode === 409 ||
                            errorCode === 'workspace_revision_conflict';
                        if (!isConflict) {
                            throw error;
                        }

                        if (!this._isCurrentWorkspaceSaveTask(task)) {
                            return { ignored: true, stale: true };
                        }

                        this._applyWorkspaceSnapshotSummary?.(payload?.snapshot || payload?.Snapshot || null);
                        this._handleWorkspacePersistenceStatus?.(payload?.persistenceStatus || payload?.PersistenceStatus || null);
                        const currentPlanIdentity = this._getPlanIdentity?.(this.pendingVisionPlan) || '';
                        const samePlan = Boolean(task.planIdentity && currentPlanIdentity && task.planIdentity === currentPlanIdentity);
                        if (!rebased && samePlan && !this._isPlanSnapshotReadOnly()) {
                            rebased = true;
                            expectedRevision = Number(this.workspaceSnapshotRevision || 0);
                            continue;
                        }

                        this._syncWorkspaceSnapshotDirty();
                        this._setResultStatusNote?.('工作台状态已更新，当前未保存修改已保留，请确认后重试。', 'warning');
                        throw error;
                    }
                }
            } catch (error) {
                if (!this._isCurrentWorkspaceSaveTask(task)) {
                    return { ignored: true, stale: true };
                }
                this.workspaceSaveErrorGeneration = Math.max(
                    Number(this.workspaceSaveErrorGeneration || 0),
                    task.generation);
                this._syncWorkspaceSnapshotDirty();
                const payload = error?.payload || {};
                const errorCode = String(payload?.errorCode ?? payload?.ErrorCode ?? '').trim();
                const isConflict = error?.status === 409 ||
                    error?.statusCode === 409 ||
                    errorCode === 'workspace_revision_conflict';
                if (!isConflict) {
                    this._setResultStatusNote?.('Plan 修改尚未成功保存，切换前请重试。', 'warning');
                }
                throw error;
            } finally {
                this.workspacePendingMutationCount = Math.max(
                    0,
                    Number(this.workspacePendingMutationCount || 0) - 1);
            }
        };

        this.workspaceSnapshotSaveQueue = (this.workspaceSnapshotSaveQueue || Promise.resolve())
            .catch(() => undefined)
            .then(run);
        return this.workspaceSnapshotSaveQueue;
    },

    async _flushWorkspaceSnapshotBeforeBoundary(reason = 'boundary') {
        if (this._disposed) return false;
        if (!this.sessionId || !this.pendingVisionPlan || this._isPlanSnapshotReadOnly()) {
            return true;
        }
        this._syncWorkspaceSnapshotDirty();
        if (!this.workspaceSnapshotDirty) {
            return true;
        }

        const targetGeneration = Number(this.workspaceMutationGeneration || 0);
        this.workspaceBoundaryInProgress = true;
        try {
            await (this.workspaceSnapshotSaveQueue || Promise.resolve());
            return Number(this.workspacePersistedGeneration || 0) >= targetGeneration;
        } catch {
            return Number(this.workspacePersistedGeneration || 0) >= targetGeneration;
        } finally {
            this.workspaceBoundaryInProgress = false;
            this._syncWorkspaceSnapshotDirty();
        }
    },

    _clearPlanQuestionAnswers() {
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.ANSWERS_REPLACED,
            payload: { answers: {}, selections: {} },
            planId: this.agentWorkspaceState?.identity?.planId,
            planHash: this.agentWorkspaceState?.identity?.planHash
        });
        this.planAcceptedRecommendedDefaults = false;
    },

    _getPlanIdentity(plan) {
        const planId = String(plan?.planId || plan?.id || '').trim();
        const planHash = String(
            plan?.planHash ||
            plan?.rawPlanSnapshot?.planHash ||
            plan?.rawPlanSnapshot?.PlanHash ||
            ''
        ).trim();
        return planId && planHash ? `${planId}::${planHash}` : '';
    },

    _activatePlanIdentity(plan) {
        const identity = this._getPlanIdentity(plan);
        if (!identity) {
            this.requirementMode = 'strict';
            this.currentPlanIdentity = '';
            return;
        }

        const identityChanged = this.currentPlanIdentity !== identity;
        if (identityChanged) {
            this.activePlanReadinessPreviewController?.abort?.();
            this.activePlanReadinessPreviewController = null;
            this.activePlanReadinessPreviewRequest = null;
            this.lastPlanReadinessRequestFingerprint = '';
            this.currentPlanIdentity = identity;
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.READINESS_CLEARED,
                payload: {},
                planId: this.agentWorkspaceState?.identity?.planId,
                planHash: this.agentWorkspaceState?.identity?.planHash
            });
            this.lastPlanReadinessPreviewError = '';
        }

        if (!this.planRequirementModes) {
            this.planRequirementModes = new Map();
        }
        if (!this.planRequirementModes.has(identity)) {
            this.planRequirementModes.set(identity, 'strict');
        }

        this.requirementMode = this._normalizeRequirementMode?.(this.planRequirementModes.get(identity)) || 'strict';
    },

    _rememberRequirementModeForPlan(plan, mode) {
        const identity = this._getPlanIdentity(plan);
        if (!identity) return;
        if (!this.planRequirementModes) {
            this.planRequirementModes = new Map();
        }
        this.planRequirementModes.set(identity, this._normalizeRequirementMode?.(mode) || 'strict');
    },

    _resetPlanReadinessPreviewState({ abort = true } = {}) {
        if (this.activePlanReadinessPreviewTimeoutId) {
            window.clearTimeout?.(this.activePlanReadinessPreviewTimeoutId);
        }
        this.activePlanReadinessPreviewTimeoutId = null;
        if (abort) {
            this.activePlanReadinessPreviewController?.abort?.();
        }
        this.activePlanReadinessPreviewController = null;
        this.activePlanReadinessPreviewRequest = null;
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.READINESS_CLEARED,
            payload: {},
            planId: this.agentWorkspaceState?.identity?.planId,
            planHash: this.agentWorkspaceState?.identity?.planHash
        });
        this.lastPlanReadinessPreviewError = '';
    },

    _isCurrentPlanReadinessPreviewRequest(request) {
        if (!request || !this.pendingVisionPlan) return false;
        const plan = this.pendingVisionPlan;
        return String(request.planId || '') === String(plan.planId || plan.id || '') &&
            String(request.planHash || '') === String(plan.planHash || '') &&
            Number(request.answerRevision) === (Number(this.planAnswerRevision) || 0) &&
            Number(request.resourceRevision) === (Number(this.agentWorkspaceState?.resources?.revision) || 0) &&
            this._normalizeRequirementMode?.(request.requirementMode) ===
                (this._normalizeRequirementMode?.(this.requirementMode) || 'strict');
    },

    _normalizePlanReadinessPreviewResult(result) {
        const data = this._asObject?.(result) || result || {};
        const readiness = this._normalizePlanBuildReadiness(data.buildReadiness || data.BuildReadiness);
        const hasAuthoritativeCountPartition = [
            ['mustConfirmBeforeBuildCount', 'MustConfirmBeforeBuildCount'],
            ['fillLaterCount', 'FillLaterCount'],
            ['totalIncompleteCount', 'TotalIncompleteCount']
        ].every(([camel, pascal]) => Object.prototype.hasOwnProperty.call(data, camel) ||
            Object.prototype.hasOwnProperty.call(data, pascal));
        return {
            planId: String(data.planId || data.PlanId || '').trim(),
            planHash: String(data.planHash || data.PlanHash || '').trim(),
            requirementMode: this._normalizeRequirementMode?.(data.requirementMode || data.RequirementMode) || 'strict',
            answerRevision: Number(data.answerRevision ?? data.AnswerRevision ?? 0) || 0,
            resourceRevision: Number(data.resourceRevision ?? data.ResourceRevision ?? 0) || 0,
            acceptedAnswers: this._toArray(data.acceptedAnswers || data.AcceptedAnswers)
                .map(answer => this._normalizePlanAnswer(answer))
                .filter(Boolean),
            answerSetFingerprint: String(data.answerSetFingerprint || data.AnswerSetFingerprint || '').trim(),
            buildReadiness: readiness,
            deferredQuestionIds: this._toArray(data.deferredQuestionIds || data.DeferredQuestionIds)
                .map(item => String(item || '').trim())
                .filter(Boolean),
            pendingConfirmationCount: Number(data.pendingConfirmationCount ?? data.PendingConfirmationCount ?? 0) || 0,
            resourcePendingCount: Number(data.resourcePendingCount ?? data.ResourcePendingCount ?? 0) || 0,
            hardBlockerCount: Number(data.hardBlockerCount ?? data.HardBlockerCount ?? 0) || 0,
            buildBlockingConfirmationCount: Number(data.buildBlockingConfirmationCount ?? data.BuildBlockingConfirmationCount ?? 0) || 0,
            buildRequiredResourceCount: Number(data.buildRequiredResourceCount ?? data.BuildRequiredResourceCount ?? 0) || 0,
            deferredFieldCount: Number(data.deferredFieldCount ?? data.DeferredFieldCount ?? 0) || 0,
            draftAllowedResourceCount: Number(data.draftAllowedResourceCount ?? data.DraftAllowedResourceCount ?? 0) || 0,
            mustConfirmBeforeBuildCount: Number(data.mustConfirmBeforeBuildCount ?? data.MustConfirmBeforeBuildCount ?? 0) || 0,
            fillLaterCount: Number(data.fillLaterCount ?? data.FillLaterCount ?? 0) || 0,
            totalIncompleteCount: Number(data.totalIncompleteCount ?? data.TotalIncompleteCount ?? 0) || 0,
            hasAuthoritativeCountPartition,
            metadataOnly: (data.metadataOnly ?? data.MetadataOnly) === true,
            contractValid: (data.contractValid ?? data.ContractValid) !== false,
            failureCode: String(data.failureCode || data.FailureCode || '').trim(),
            failureMessage: this._localizeDisplayText(data.failureMessage || data.FailureMessage || '')
        };
    },

    _makeInitialPlanReadinessPreview(plan, readiness) {
        const normalizedReadiness = this._normalizePlanBuildReadiness(readiness) || readiness || null;
        if (!this._isUsableAuthoritativeReadiness(normalizedReadiness)) return null;
        const stats = this._getPlanReadinessStats({ ...plan, buildReadiness: normalizedReadiness });
        return {
            planId: String(plan?.planId || plan?.id || '').trim(),
            planHash: String(plan?.planHash || '').trim(),
            requirementMode: this._normalizeRequirementMode?.(plan?.requirementMode || this.requirementMode || 'strict') || 'strict',
            answerRevision: Number(this.planAnswerRevision) || 0,
            resourceRevision: Number(this.agentWorkspaceState?.resources?.revision) || 0,
            acceptedAnswers: this._toArray(plan?.rawPlanSnapshot?.confirmedPlanAnswers || plan?.rawPlanSnapshot?.ConfirmedPlanAnswers)
                .map(answer => this._normalizePlanAnswer(answer))
                .filter(Boolean),
            answerSetFingerprint: '',
            buildReadiness: normalizedReadiness,
            deferredQuestionIds: [],
            pendingConfirmationCount: stats.pendingConfirmationCount,
            resourcePendingCount: stats.resourcePendingCount,
            hardBlockerCount: this._toArray(normalizedReadiness.blockers).filter(blocker => blocker?.blocksBuild === true &&
                blocker?.category !== PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING).length,
            metadataOnly: true,
            contractValid: true,
            failureCode: '',
            failureMessage: ''
        };
    },

    _getCurrentCanonicalPreview(plan) {
        const preview = this.agentWorkspaceState?.readinessPreview || null;
        if (!preview || !preview.buildReadiness) return null;
        if (!['ready', 'blocked'].includes(this.agentWorkspaceState?.readinessStatus)) return null;
        const planId = String(plan?.planId || plan?.id || '').trim();
        const planHash = String(plan?.planHash || '').trim();
        if (String(preview.planId || '') !== planId || String(preview.planHash || '') !== planHash) return null;
        if (Number(preview.answerRevision) !== (Number(this.planAnswerRevision) || 0)) return null;
        if (Number(preview.resourceRevision) !== (Number(this.agentWorkspaceState?.resources?.revision) || 0)) return null;
        const mode = this._normalizeRequirementMode?.(this.requirementMode) || 'strict';
        if ((this._normalizeRequirementMode?.(preview.requirementMode) || 'strict') !== mode) return null;
        return preview;
    },

    _applyPlanReadinessPreviewResult(plan, result) {
        const preview = this._normalizePlanReadinessPreviewResult(result);
        if (!preview.buildReadiness || !this._isUsableAuthoritativeReadiness(preview.buildReadiness)) {
            return false;
        }
        if (preview.contractValid === false) {
            const message = this._sanitizePlanDiagnosticText(preview.failureMessage || '构建条件校验失败，请重试', 260) || '构建条件校验失败，请重试';
            this.lastPlanReadinessPreviewError = message;
            return false;
        }
        const request = {
            planId: preview.planId,
            planHash: preview.planHash,
            answerRevision: preview.answerRevision,
            resourceRevision: preview.resourceRevision,
            requirementMode: preview.requirementMode
        };
        if (!this._isCurrentPlanReadinessPreviewRequest(request)) {
            return false;
        }

        const nextPlan = {
            ...plan,
            requirementMode: preview.requirementMode,
            effectiveReadiness: preview,
            previewState: preview.buildReadiness.canBuild === true ? 'ready' : 'blocked',
            previewError: '',
            buildReadiness: preview.buildReadiness,
            executable: preview.buildReadiness.canBuild === true,
            resolvedPlanFields: this._toArray(preview.buildReadiness.resolvedFields),
            remainingPlanFields: this._toArray(preview.buildReadiness.remainingFields),
            blockingReasons: this._toArray(preview.buildReadiness.blockers)
            .filter(blocker => blocker?.blocksBuild === true)
            .map(blocker => blocker.publicLabel || blocker.field || blocker.questionId || blocker.id)
            .filter(Boolean)
        };
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.PLAN_RECEIVED,
            payload: { plan: nextPlan },
            planId: preview.planId,
            planHash: preview.planHash
        });
        if (preview.acceptedAnswers.length) {
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.ANSWERS_CONFIRMED,
                payload: { answers: preview.acceptedAnswers, preserveRevision: true },
                planId: preview.planId,
                planHash: preview.planHash
            });
        }
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.READINESS_RECEIVED,
            payload: preview,
            planId: preview.planId,
            planHash: preview.planHash
        });
        this.lastPlanReadinessPreviewError = '';
        if (this.pendingRequirementModeReadinessPersistence === preview.requirementMode) {
            this.pendingRequirementModeReadinessPersistence = '';
            this._queueWorkspaceSnapshotFlush?.('readiness_preview');
        }
        return true;
    },

    _buildPlanReadinessPreviewRequest(plan, { acceptedRecommended = false } = {}) {
        const request = this._buildStructuredBuildFromPlanRequest(plan, { acceptedRecommended });
        return {
            ...request,
            requirementMode: this._normalizeRequirementMode?.(this.requirementMode) || 'strict',
            answerRevision: Number(this.planAnswerRevision) || 0,
            resourceRevision: Number(this.agentWorkspaceState?.resources?.revision) || 0
        };
    },

    async _requestBackendPlanReadinessPreview(request, options = {}) {
        return await httpClient.post('/ai/agent-plan/readiness-preview', request, options);
    },

    _requestPlanReadinessPreview(plan = this.pendingVisionPlan, { acceptedRecommended = false, reason = '' } = {}) {
        if (!plan) return false;
        this._activatePlanIdentity?.(plan);
        const request = this._buildPlanReadinessPreviewRequest(plan, { acceptedRecommended });
        const requestFingerprint = [
            request.planId,
            request.planHash,
            Number(request.answerRevision) || 0,
            Number(request.resourceRevision) || 0,
            this._normalizeRequirementMode?.(request.requirementMode) || 'strict',
            acceptedRecommended === true ? 'recommended' : 'answers'
        ].join('::');
        if (reason !== 'retry' && this.lastPlanReadinessRequestFingerprint === requestFingerprint) {
            return false;
        }
        this.lastPlanReadinessRequestFingerprint = requestFingerprint;
        const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;
        if (this.activePlanReadinessPreviewTimeoutId) {
            window.clearTimeout?.(this.activePlanReadinessPreviewTimeoutId);
        }
        this.activePlanReadinessPreviewTimeoutId = null;
        this.activePlanReadinessPreviewController?.abort?.();
        this.activePlanReadinessPreviewController = controller;
        const previewRequest = {
            planId: String(request.planId || '').trim(),
            planHash: String(request.planHash || '').trim(),
            answerRevision: Number(request.answerRevision) || 0,
            resourceRevision: Number(request.resourceRevision) || 0,
            requirementMode: this._normalizeRequirementMode?.(request.requirementMode) || 'strict',
            reason,
            requestId: `readiness-${Date.now()}-${Number(this._planReadinessRequestSequence = (this._planReadinessRequestSequence || 0) + 1)}`,
            startedAt: Date.now(),
            timedOut: false
        };
        this.activePlanReadinessPreviewRequest = previewRequest;
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.READINESS_REQUESTED,
            payload: previewRequest,
            planId: previewRequest.planId,
            planHash: previewRequest.planHash
        });
        this.lastPlanReadinessPreviewError = '';
        this._updatePlanBuildActionState?.();

        const timeoutMs = Math.max(1000, Number(this.planReadinessTimeoutMs) || 15000);
        this.activePlanReadinessPreviewTimeoutId = window.setTimeout?.(() => {
            if (this.activePlanReadinessPreviewRequest !== previewRequest ||
                !this._isCurrentPlanReadinessPreviewRequest(previewRequest)) return;
            previewRequest.timedOut = true;
            controller?.abort?.();
            const message = `构建条件校验超过 ${Math.ceil(timeoutMs / 1000)} 秒，请重试。`;
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.READINESS_FAILED,
                payload: { message, status: 'timeout', requestId: previewRequest.requestId },
                planId: previewRequest.planId,
                planHash: previewRequest.planHash
            });
            this.lastPlanReadinessPreviewError = message;
            this.lastPlanReadinessRequestFingerprint = '';
            this.activePlanReadinessPreviewTimeoutId = null;
            this.activePlanReadinessPreviewController = null;
            this.activePlanReadinessPreviewRequest = null;
            this._renderPlanWorkspace?.(this.pendingVisionPlan);
            this._renderAgentWorkspaceOverview?.();
            this._updatePlanBuildActionState?.();
        }, timeoutMs);

        const clearRequestTimeout = () => {
            if (this.activePlanReadinessPreviewTimeoutId) {
                window.clearTimeout?.(this.activePlanReadinessPreviewTimeoutId);
            }
            this.activePlanReadinessPreviewTimeoutId = null;
        };

        this._requestBackendPlanReadinessPreview(request, controller?.signal ? { signal: controller.signal } : {})
            .then(result => {
                if (this.activePlanReadinessPreviewRequest !== previewRequest ||
                    !this._isCurrentPlanReadinessPreviewRequest(previewRequest)) {
                    return;
                }
                clearRequestTimeout();
                if (!this._applyPlanReadinessPreviewResult(plan, result)) {
                    const message = this._sanitizePlanDiagnosticText(this.lastPlanReadinessPreviewError || '构建条件校验失败，请重试', 260) || '构建条件校验失败，请重试';
                    this._dispatchAgentWorkspaceEvent?.({
                        type: AgentWorkspaceEventTypes.READINESS_FAILED,
                        payload: { message },
                        planId: previewRequest.planId,
                        planHash: previewRequest.planHash
                    });
                    this.lastPlanReadinessPreviewError = message;
                    this.lastPlanReadinessRequestFingerprint = '';
                    this.activePlanReadinessPreviewController = null;
                    this.activePlanReadinessPreviewRequest = null;
                    this._renderPlanWorkspace?.(plan);
                    this._renderAgentWorkspaceOverview?.();
                    this._updatePlanBuildActionState?.();
                    return;
                }
                this.activePlanReadinessPreviewController = null;
                this.activePlanReadinessPreviewRequest = null;
                this._renderPlanWorkspace?.(plan);
                this._renderAgentWorkspaceOverview?.();
                this._updatePlanBuildActionState?.();
            })
            .catch(error => {
                if (error?.name === 'AbortError') {
                    if (previewRequest.timedOut) return;
                    if (this.activePlanReadinessPreviewRequest === previewRequest) {
                        clearRequestTimeout();
                        this.activePlanReadinessPreviewController = null;
                        this.activePlanReadinessPreviewRequest = null;
                        this._dispatchAgentWorkspaceEvent?.({
                            type: AgentWorkspaceEventTypes.READINESS_STATUS_CHANGED,
                            payload: { status: 'idle', message: '校验已中止，可重试。' },
                            planId: previewRequest.planId,
                            planHash: previewRequest.planHash
                        });
                    }
                    return;
                }
                if (this.activePlanReadinessPreviewRequest !== previewRequest ||
                    !this._isCurrentPlanReadinessPreviewRequest(previewRequest)) {
                    return;
                }
                clearRequestTimeout();
                const message = this._sanitizePlanDiagnosticText(error?.message || '构建条件校验失败，请重试', 260) || '构建条件校验失败，请重试';
                this._dispatchAgentWorkspaceEvent?.({
                    type: AgentWorkspaceEventTypes.READINESS_FAILED,
                        payload: { message },
                        planId: previewRequest.planId,
                        planHash: previewRequest.planHash
                });
                this.lastPlanReadinessPreviewError = message;
                this.lastPlanReadinessRequestFingerprint = '';
                this.activePlanReadinessPreviewController = null;
                this.activePlanReadinessPreviewRequest = null;
                this._renderPlanWorkspace?.(plan);
                this._renderAgentWorkspaceOverview?.();
                this._updatePlanBuildActionState?.();
            });
        return true;
    },

    _mergeBackendPlanAnswers(plan) {
        if (!plan) return;
        const backendAnswers = this._toArray(
            plan.rawPlanSnapshot?.confirmedPlanAnswers ??
            plan.rawPlanSnapshot?.ConfirmedPlanAnswers ?? []
        ).map(a => this._normalizePlanAnswer(a)).filter(Boolean);
        if (!backendAnswers.length) return;

        const nextAnswers = { ...(this.planQuestionAnswers || {}) };
        const nextSelections = { ...(this.planQuestionSelections || {}) };
        let changed = false;

        const originPriority = {
            'explicit_user_text': 4,
            'explicit_user_selection': 4,
            'resource_bound': 3,
            'model_inferred': 2,
            'accepted_recommended_default': 1
        };

        backendAnswers.forEach(answer => {
            const key = answer.field;
            const existing = nextAnswers[key]
                ? this._normalizePlanAnswer(nextAnswers[key])
                : null;
            if (existing) {
                const existingPri = originPriority[existing.origin] || 0;
                const backendPri = originPriority[answer.origin] || 0;
                if (existingPri > backendPri) {
                    return;
                }
            }
            nextAnswers[key] = answer;
            if (answer.questionId) {
                nextSelections[answer.questionId] = answer.value;
            }
            changed = true;
        });

        if (changed) {
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.ANSWERS_CONFIRMED,
                payload: { answers: Object.values(nextAnswers) },
                planId: this.agentWorkspaceState?.identity?.planId,
                planHash: this.agentWorkspaceState?.identity?.planHash
            });
        }
    },

    _shouldOpenPlanModeBeforeBuild(args = {}) {
        if (this._isAllowedSkipPlanRequest(args)) return false;
        if (this.isGenerating) return false;
        if (args?.skipPlan) return true;

        const {
            explicitMode = '',
            description = '',
            hasCurrentFlowContext = false
        } = args || {};
        const mode = String(explicitMode || '').trim().toLowerCase();
        if (['modify', 'explain', 'review_pending_parameters'].includes(mode)) {
            return false;
        }

        if (mode && !['auto', 'new', 'build', 'stable', 'scripted'].includes(mode)) {
            return true;
        }

        if (hasCurrentFlowContext) {
            if (this._looksLikeExistingFlowEditRequest?.(description) ||
                this._looksLikeExplainRequest?.(description) ||
                (this._looksLikeModifyRequest?.(description) && !this._looksLikeExplicitNewFlowRequest?.(description))) {
                return false;
            }
        }

        return true;
    },

    _shouldRouteIntentBeforeGenerate(args = {}) {
        if (this._isAllowedSkipPlanRequest(args)) return false;
        if (this.isGenerating) return false;

        const mode = String(args?.explicitMode || '').trim().toLowerCase();
        if (mode === 'review_pending_parameters') {
            return false;
        }

        return true;
    },

    _isAllowedSkipPlanRequest({
        explicitMode = '',
        skipPlan = false,
        skipPlanSource = '',
        buildFromPlan = null
    } = {}) {
        if (!skipPlan) return false;

        const source = String(skipPlanSource || '').trim().toLowerCase();
        const mode = String(explicitMode || '').trim().toLowerCase();
        if (buildFromPlan && typeof buildFromPlan === 'object' && Object.keys(buildFromPlan).length > 0) {
            return true;
        }

        if (source === 'confirmed_plan') {
            return Boolean(buildFromPlan || this.pendingVisionPlan);
        }

        if (source === 'developer_direct_build_debug') {
            return this.isVisionAgentDeveloperUiEnabled === true;
        }

        if (source === 'pending_parameter_review') {
            return mode === 'review_pending_parameters' && this.currentResult?.flow;
        }

        if (source === 'intent_router_build') {
            return mode === 'modify' && this._hasCurrentFlowContext?.() === true;
        }

        return false;
    },

    _updatePlanBuildActionState() {
        const busy = Boolean(this.isGenerating);
        const hasPlan = Boolean(this.pendingVisionPlan);
        const actionState = this._getPlanBuildActionState(this.pendingVisionPlan);
        const canBuild = hasPlan && actionState.canStart;
        const blockedTitle = hasPlan
            ? actionState.statusText
            : '请先完成规划';
        const inlineBuildBtn = this.container?.querySelector('#ai-btn-start-build-inline');
        if (inlineBuildBtn) {
            inlineBuildBtn.disabled = busy || !canBuild;
            inlineBuildBtn.dataset.acceptRecommended = actionState.acceptedRecommended ? 'true' : 'false';
            if (actionState.label) {
                inlineBuildBtn.setAttribute?.('aria-label', actionState.label);
                inlineBuildBtn.textContent = actionState.label;
            }
            inlineBuildBtn.title = !hasPlan
                ? '请先完成规划'
                : canBuild
                    ? '按已确认计划开始构建'
                    : '当前计划仍需澄清，暂不可构建';
            if (hasPlan && !canBuild) {
                inlineBuildBtn.title = blockedTitle;
            }
            inlineBuildBtn.setAttribute?.('aria-disabled', inlineBuildBtn.disabled ? 'true' : 'false');
        }

        const buildStatus = this.container?.querySelector('#ai-plan-build-status');
        if (buildStatus) {
            buildStatus.textContent = !hasPlan
                ? ''
                : actionState.statusText || blockedTitle;
        }

        const mainBuildButtons = this.container?.querySelectorAll?.('#ai-btn-start-build') || [];
        mainBuildButtons.forEach(mainBuildBtn => {
            mainBuildBtn.disabled = busy || !canBuild;
            mainBuildBtn.textContent = actionState.label || 'Start Build';
            mainBuildBtn.dataset.acceptRecommended = actionState.acceptedRecommended ? 'true' : 'false';
            mainBuildBtn.title = hasPlan ? actionState.statusText : 'Finish the plan first';
            mainBuildBtn.setAttribute?.('aria-disabled', mainBuildBtn.disabled ? 'true' : 'false');
        });

        this.container?.querySelectorAll?.('.ai-plan-action').forEach(button => {
            button.disabled = busy || !canBuild;
            if (!canBuild && hasPlan) {
                button.title = blockedTitle;
            }
            button.dataset.acceptRecommended = actionState.acceptedRecommended ? 'true' : 'false';
            button.setAttribute?.('aria-disabled', button.disabled ? 'true' : 'false');
        });
    },

    _enterIntentRouterFromPrompt({
        description,
        hint = '',
        userMessage = '',
        attachmentPaths = [],
        templateSelection = null,
        clearInput = true,
        input = null,
        explicitMode = '',
        hasCurrentFlowContext = false,
        addUserMessage = true
    }) {
        const normalizedDescription = String(description || '').trim();
        if (!normalizedDescription) {
            this._addMessage('system', '请输入需求描述。');
            return false;
        }

        const routerRequestId = this._createIntentRouterRequestId();
        this.activeIntentRouterRequestId = routerRequestId;
        this.lastUserPrompt = String(userMessage || normalizedDescription).trim();
        this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
        this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
        this.pendingClarificationPayload = null;
        this.activePlanRequestId = null;
        this.activePlanRunId = null;
        this.activePlanRunRequestId = null;
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();
        this.activePlanRunCompletion = null;

        this._closeAgentRunEventSource?.();
        this._setGeneratingState?.(true);
        this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
        if (addUserMessage) {
            this._addMessage('user', userMessage || normalizedDescription);
        }
        const turn = this._startAssistantTurn({
            activate: true,
            statusText: '正在判断请求类型',
            statusTone: 'streaming',
            openReply: true
        });
        const requestContext = {
            description: normalizedDescription,
            hint,
            userMessage,
            attachmentPaths,
            templateSelection,
            explicitMode,
            hasCurrentFlowContext
        };
        this._beginPlanningLifecycle?.({
            requestId: routerRequestId,
            requestContext,
            turn,
            phase: 'understand'
        });
        this._setAssistantSectionText(turn, 'reply', '正在规划，详细阶段和当前工作见左侧工作台。');
        this._setResultStatusNote('正在理解需求。', 'info');
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
        this._updatePlanBuildActionState();

        if (clearInput && input) {
            input.value = '';
            input.style.height = 'auto';
        }

        const requestAnswerRevision = Number(this.planAnswerRevision) || 0;
        const routerRequest = this._buildIntentRouterRequest({
            description: normalizedDescription,
            hint,
            userMessage,
            attachmentPaths,
            templateSelection,
            explicitMode,
            hasCurrentFlowContext
        });

        this._requestBackendIntentRouterRun(routerRequest)
            .then(result => {
                if (!this._isActiveIntentRouterRequest(routerRequestId)) return;
                this._clearActiveIntentRouterRequest(routerRequestId);
                this._advancePlanningLifecycle?.(
                    'context',
                    '需求理解已返回，正在整理工程上下文。',
                    {
                        routerStatus: 'completed',
                        routerSummary: 'Intent Router 已返回真实结果。'
                    });
                this._handleIntentRouterResult(result, {
                    routerRequestId,
                    turn,
                    description: normalizedDescription,
                    hint,
                    userMessage,
                    attachmentPaths,
                    templateSelection,
                    explicitMode,
                    hasCurrentFlowContext,
                    planAnswerRevision: requestAnswerRevision
                });
            })
            .catch(error => {
                if (!this._isActiveIntentRouterRequest(routerRequestId)) return;
                this._clearActiveIntentRouterRequest(routerRequestId);
                if (this._handlePlanningDeadlineExceeded?.(error, turn)) {
                    this.activeAssistantTurn = null;
                    return;
                }
                this._advancePlanningLifecycle?.(
                    'context',
                    '路由服务未完成，已交由 Planner 继续整理工程上下文。',
                    {
                        routerStatus: 'failed',
                        routerSummary: this._sanitizePlanDiagnosticText(error?.message || 'intent_router_failed', 180)
                    });
                this._enterPlanModeFromPrompt({
                    description: normalizedDescription,
                    hint,
                    userMessage,
                    attachmentPaths,
                    templateSelection,
                    clearInput: false,
                    input,
                    turn,
                    addUserMessage: false,
                    clearPendingPlan: true
                });
            });

        return true;
    },

    _createIntentRouterRequestId() {
        const randomPart = Math.random().toString(36).slice(2, 8);
        return `intent-${Date.now()}-${randomPart}`;
    },

    _isActiveIntentRouterRequest(requestId) {
        return Boolean(this.activeIntentRouterRequestId) && requestId === this.activeIntentRouterRequestId;
    },

    _clearActiveIntentRouterRequest(requestId = null) {
        if (!requestId || this.activeIntentRouterRequestId === requestId) {
            this.activeIntentRouterRequestId = null;
        }
    },

    _buildIntentRouterRequest({
        description,
        hint = '',
        userMessage = '',
        attachmentPaths = [],
        templateSelection = null,
        explicitMode = ''
    }) {
        const planRequest = this._buildPlanModeRequest({
            description,
            hint,
            userMessage,
            attachmentPaths,
            templateSelection
        });
        return {
            ...planRequest,
            mode: String(explicitMode || this.agentGenerateFlowMode || 'auto').trim() || 'auto',
            hasPendingPlan: Boolean(this.pendingVisionPlan),
            pendingPlanSummary: this._buildPendingPlanIntentSummary(),
            pendingPlanHash: this.pendingVisionPlan?.planHash || '',
            confirmedPlanAnswers: this._buildConfirmedPlanAnswers(this.pendingVisionPlan),
            resolvedPlanFields: this._getResolvedPlanFields(this.pendingVisionPlan),
            remainingPlanFields: this._getRemainingPlanFields(this.pendingVisionPlan),
            requirementMode: this.requirementMode || 'strict',
            developerDirectBuildDebug: false,
            metadataOnly: true
        };
    },

    _buildPendingPlanIntentSummary() {
        if (!this.pendingVisionPlan) return null;
        const plan = this.pendingVisionPlan;
        const summary = {
            planId: plan.planId || plan.id || '',
            planHash: plan.planHash || '',
            goal: plan.goal || '',
            intent: plan.intent || '',
            canBuild: this.agentWorkspaceState?.projection?.readiness?.canBuild === true,
            route: plan.route || plan.recommendedRoute || plan.RecommendedRoute || null
        };
        try {
            return JSON.stringify(summary);
        } catch {
            return null;
        }
    },

    async _requestBackendIntentRouterRun(request) {
        return await httpClient.post('/ai/agent-intent-router-runs', request);
    },

    _handleIntentRouterResult(result, context) {
        if (context?.planAnswerRevision !== undefined &&
            Number(context.planAnswerRevision) !== (Number(this.planAnswerRevision) || 0)) {
            this._setGeneratingState?.(false);
            this.activeAssistantTurn = null;
            return false;
        }

        const route = this._normalizeIntentRouterResult(result);
        if (this.planningLifecycle) {
            this.planningLifecycle.routerPublicReason = route.publicReason || '';
        }
        this._renderPlanRunTimeline?.(context.turn);
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.INTENT_RESOLVED,
            payload: route,
            sessionId: this.sessionId,
            revision: result?.revision || result?.Revision
        });
        const isRuleFallback = /rule_fallback/i.test(String(route.routerSource || ''));
        const tone = route.needsClarification ? 'warning' : 'streaming';
        const visibleStatus = isRuleFallback
            ? '规则降级解析'
            : route.needsClarification || route.intent === 'ambiguous_vision_requirement'
            ? '需要补充信息'
            : '已理解请求';
        this._setAssistantTurnStatus(context.turn, visibleStatus, tone);
        this._setAssistantSectionText(context.turn, 'reply', this._formatIntentRouterReply(route));
        if (route.shouldMergeIntoPendingPlan && this.pendingVisionPlan) {
            this._mergePlanAnswerUpdates(this.pendingVisionPlan, route.planAnswerUpdates);
            const nextPlan = {
                ...this.pendingVisionPlan,
                resolvedPlanFields: route.resolvedPlanFields.length
                    ? route.resolvedPlanFields
                    : this._getResolvedPlanFields(this.pendingVisionPlan),
                remainingPlanFields: route.remainingPlanFields.length
                    ? route.remainingPlanFields
                    : this._getRemainingPlanFields(this.pendingVisionPlan)
            };
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.PLAN_RECEIVED,
                payload: { plan: nextPlan },
                planId: nextPlan.planId,
                planHash: nextPlan.planHash
            });
            this._resetPlanReadinessPreviewState?.({ abort: true });
            this._requestPlanReadinessPreview?.(this.pendingVisionPlan, { reason: 'intent_answer_merge' });

            if (route.intent === 'build_from_confirmed_plan' && this.agentWorkspaceState?.projection?.readiness?.canBuild === true) {
                this._setAssistantTurnStatus(context.turn, '进入构建', 'success');
                this.activeAssistantTurn = null;
                this._setGeneratingState?.(false);
                return this._startBuildFromCurrentPlan();
            }

            this.pendingClarificationPayload = null;
            this._setWorkbenchState(AiWorkbenchStates.IDLE);
            this._setResultStatusNote(route.publicReason || 'Plan answers updated.', 'info');
            this._setGeneratingState?.(false);
            this._renderAgentWorkspaceOverview();
            this._renderPlanWorkspace(this.pendingVisionPlan);
            this._renderBuildWorkspaceFromAgentRun();
            this._updatePlanBuildActionState();
            this.activeAssistantTurn = null;
            this._markPlanningLifecycleTerminal?.('completed', route.publicReason || '需求更新已完成。', { preserveReply: true });
            return true;
        }

        if (route.shouldOpenPlan === true) {
            return this._enterPlanModeFromPrompt({
                description: context.description,
                hint: context.hint,
                userMessage: context.userMessage,
                attachmentPaths: context.attachmentPaths,
                templateSelection: context.templateSelection,
                semanticExtraction: route.semanticExtraction,
                clearInput: false,
                input: null,
                turn: context.turn,
                addUserMessage: false,
                clearPendingPlan: route.shouldResetPendingPlan !== false
            });
        }

        if (route.intent === 'build_from_confirmed_plan' && this.pendingVisionPlan) {
            this._refreshPlanEffectiveBuildReadiness?.(this.pendingVisionPlan);
        }

        if (route.intent === 'build_from_confirmed_plan' && this.pendingVisionPlan && this.agentWorkspaceState?.projection?.readiness?.canBuild !== true) {
            route.intent = 'ambiguous_vision_requirement';
            route.needsClarification = true;
            route.publicReason = route.publicReason || this.pendingVisionPlan.requirementMaturity?.publicReason || '当前计划仍需澄清，暂不可构建。';
        }

        if (route.intent === 'build_from_confirmed_plan' && this.pendingVisionPlan?.executable === true) {
            this._setAssistantTurnStatus(context.turn, '进入构建', 'success');
            this.activeAssistantTurn = null;
            this._setGeneratingState?.(false);
            return this._startBuildFromCurrentPlan();
        }

        if (route.intent === 'modify_existing_flow' && this._hasCurrentFlowContext?.() === true) {
            this._setAssistantTurnStatus(context.turn, '进入构建', 'success');
            this.activeAssistantTurn = null;
            this._setGeneratingState?.(false);
            return this._dispatchGenerateRequest({
                description: context.description,
                hint: context.hint,
                userMessage: context.userMessage,
                attachmentPaths: context.attachmentPaths,
                explicitMode: 'modify',
                templateSelection: context.templateSelection,
                clearInput: false,
                skipPlan: true,
                skipPlanSource: 'intent_router_build',
                suppressUserMessage: true
            });
        }

        if (route.needsClarification || route.intent === 'ambiguous_vision_requirement') {
            if (this.pendingVisionPlan && route.shouldResetPendingPlan !== true) {
                this.pendingClarificationPayload = null;
                this._refreshPlanEffectiveBuildReadiness?.(this.pendingVisionPlan);
                this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
                this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
                this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
                this._setResultStatusNote(route.publicReason || 'Plan answers are still required before build.', 'warning');
                this._setGeneratingState?.(false);
                this._renderAgentWorkspaceOverview();
                this._renderPlanWorkspace(this.pendingVisionPlan);
                this._renderBuildWorkspaceFromAgentRun();
                this._updatePlanBuildActionState();
                this.activeAssistantTurn = null;
                this._markPlanningLifecycleTerminal?.('completed', route.publicReason || '当前计划仍需确认关键问题。', { preserveReply: true });
                return true;
            }

            this.pendingClarificationPayload = null;
            this._setWorkbenchState(AiWorkbenchStates.IDLE);
            this._setResultStatusNote('', '');
            this._setAssistantTurnStatus(context.turn, '已回复', 'success');
        } else {
            this.pendingClarificationPayload = null;
            this._setWorkbenchState(AiWorkbenchStates.IDLE);
            this._setResultStatusNote('', '');
            this._setAssistantTurnStatus(context.turn, '已回复', 'success');
        }

        this._setGeneratingState?.(false);
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
        this._updatePlanBuildActionState();
        this.activeAssistantTurn = null;
        this._markPlanningLifecycleTerminal?.('completed', route.publicReason || '请求已处理。', { preserveReply: true });
        return true;
    },

    _normalizeIntentRouterResult(result) {
        const item = this._asObject?.(result) || result || {};
        const intent = String(item.intent || item.Intent || 'ambiguous_vision_requirement').trim() || 'ambiguous_vision_requirement';
        const normalizeDisplayText = value => {
            const localized = this._localizeDisplayText(String(value || '').trim());
            return this._redactPublicDiagnosticText?.(localized) || localized;
        };
        const questions = [];
        const requirementMaturity = this._normalizeRequirementMaturity(item.requirementMaturity || item.RequirementMaturity);
        const semanticExtraction = this._normalizeSemanticExtraction(item.semanticExtraction || item.SemanticExtraction);
        const rawShouldResetPendingPlan = item.shouldResetPendingPlan ?? item.ShouldResetPendingPlan;
        return {
            intent,
            confidence: String(item.confidence || item.Confidence || 'low').trim() || 'low',
            shouldOpenPlan: Boolean(item.shouldOpenPlan ?? item.ShouldOpenPlan),
            shouldBuildDirectly: Boolean(item.shouldBuildDirectly ?? item.ShouldBuildDirectly),
            shouldMergeIntoPendingPlan: Boolean(item.shouldMergeIntoPendingPlan ?? item.ShouldMergeIntoPendingPlan),
            shouldResetPendingPlan: rawShouldResetPendingPlan === undefined ? false : Boolean(rawShouldResetPendingPlan),
            canPlan: (item.canPlan ?? item.CanPlan ?? requirementMaturity?.canPlan) === true,
            canBuild: Boolean(item.canBuild ?? item.CanBuild),
            needsClarification: Boolean(item.needsClarification ?? item.NeedsClarification),
            publicReason: normalizeDisplayText(item.publicReason || item.PublicReason || ''),
            assistantReply: normalizeDisplayText(item.assistantReply || item.AssistantReply || ''),
            clarificationQuestions: questions,
            fallbackReason: item.fallbackReason || item.FallbackReason || '',
            routerSource: item.routerSource || item.RouterSource || '',
            semanticExtraction,
            requirementMaturity,
            planAnswerUpdates: this._toArray(item.planAnswerUpdates || item.PlanAnswerUpdates)
                .map(answer => this._normalizePlanAnswer(answer))
                .filter(Boolean),
            resolvedPlanFields: this._toArray(item.resolvedPlanFields || item.ResolvedPlanFields)
                .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
                .filter(Boolean),
            remainingPlanFields: this._toArray(item.remainingPlanFields || item.RemainingPlanFields)
                .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
                .filter(Boolean),
            decisionTrace: this._normalizeDecisionTrace(item.decisionTrace || item.DecisionTrace)
        };
    },

    _mergePlanAnswerUpdates(plan, updates = []) {
        if (!plan || !updates.length) return false;
        const nextAnswers = { ...(this.planQuestionAnswers || {}) };
        const nextSelections = { ...(this.planQuestionSelections || {}) };
        let changed = false;

        const originPriority = {
            'explicit_user_text': 4,
            'explicit_user_selection': 4,
            'resource_bound': 3,
            'model_inferred': 2,
            'accepted_recommended_default': 1
        };

        updates.forEach(update => {
            const rawUpdate = this._asObject?.(update) || update || {};
            const answer = this._normalizePlanAnswer({
                origin: PLAN_ANSWER_ORIGINS.EXPLICIT_USER_TEXT,
                ...rawUpdate
            });
            if (!answer) return;
            const question = this._toArray(plan.questions)
                .find(item => String(item?.id || '').trim() === answer.questionId ||
                    this._inferPlanQuestionFieldForQuestion(item, plan) === answer.field);
            const merged = {
                ...answer,
                questionId: answer.questionId || String(question?.id || '').trim(),
                origin: answer.origin || PLAN_ANSWER_ORIGINS.EXPLICIT_USER_TEXT
            };
            const key = merged.field;
            const existing = nextAnswers[key]
                ? this._normalizePlanAnswer(nextAnswers[key])
                : null;
            if (existing) {
                const existingPri = originPriority[existing.origin] || 0;
                const newPri = originPriority[merged.origin] || 0;
                if (existingPri > newPri) {
                    return;
                }
            }
            nextAnswers[key] = merged;
            if (merged.questionId &&
                this._toArray(question?.options).some(option => String(option?.value || '').trim() === merged.value)) {
                nextSelections[merged.questionId] = merged.value;
            }
            changed = true;
        });

        if (changed) {
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.ANSWERS_REPLACED,
                payload: { answers: nextAnswers, selections: nextSelections },
                planId: this.agentWorkspaceState?.identity?.planId,
                planHash: this.agentWorkspaceState?.identity?.planHash
            });
        }

        return changed;
    },

    _formatIntentRouterIntentLabel(intent) {
        switch (String(intent || '').trim()) {
            case 'casual_chat':
                return '普通寒暄';
            case 'help':
                return '能力咨询';
            case 'actionable_vision_plan':
                return '可规划视觉需求';
            case 'modify_existing_flow':
                return '当前流程修改';
            case 'build_from_confirmed_plan':
                return '已确认计划构建';
            case 'direct_build_debug':
                return '直接 Build 调试';
            default:
                return '需求不足';
        }
    },

    _formatIntentRouterReply(route) {
        const getQuestionText = question => typeof question === 'string'
            ? question
            : String(question?.question ?? question?.Question ?? '').trim();
        if (route.assistantReply) {
            return route.assistantReply;
        }
        if (route.needsClarification || route.intent === 'ambiguous_vision_requirement') {
            const questions = route.clarificationQuestions.length
                ? route.clarificationQuestions
                : [
                    '请补充检测目标或产品对象。',
                    '请说明缺陷、测量项或识别内容。',
                    '请说明输入来源和 OK/NG 判定规则。'
                ];
            return getQuestionText(questions[0]) || '请补充检测目标、缺陷类型、输入来源和 OK/NG 规则。';
        }
        return '已理解。';
    },

    _updateIntentRouterTimeline(chainId, stepId, text, status = 'running', publicReason = '') {
        const node = this._updateThinkingStep(chainId || 'intent-router', stepId || 'intent-router-run', text);
        if (!node) return;
        node.className = `ai-agent-run-step is-${status === 'completed' ? 'success' : 'running'}`;
        node.dataset.stage = 'intent_router';
        node.dataset.eventType = stepId || '';
        if (publicReason) {
            node.dataset.publicReason = publicReason;
            node.title = publicReason;
        }
    },

    _looksLikeExplicitNewPlanRequest(description) {
        const text = String(description || '').trim().toLowerCase();
        if (!text) return false;
        return /\b(reset plan|restart plan|new task|new plan|replan|start over)\b/i.test(text) ||
            /重新规划|重新开始|新会话|新任务|重置计划|换一个需求/.test(text);
    },

    _enterPlanModeFromPrompt({
        description,
        hint = '',
        userMessage = '',
        attachmentPaths = [],
        templateSelection = null,
        semanticExtraction = null,
        clearInput = true,
        input = null,
        turn: existingTurn = null,
        addUserMessage = true,
        clearPendingPlan = true
    }) {
        const normalizedDescription = String(description || '').trim();
        if (!normalizedDescription) {
            this._addMessage('system', '请先输入检测目标，再进入规划模式。');
            return false;
        }

        this.lastUserPrompt = String(userMessage || normalizedDescription).trim();
        this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
        this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
        this.pendingClarificationPayload = null;
        if (clearPendingPlan) {
            this.pendingVisionPlan = null;
            this._clearPlanQuestionAnswers();
        }
        this.activePlanRunId = null;
        this.activePlanRunRequestId = null;
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();
        this.activePlanRunCompletion = null;
        this._resetPublicLiveEventState?.();
        const planRequestId = this._createPlanRequestId();
        this.activePlanRequestId = planRequestId;

        this._closeAgentRunEventSource?.();
        this._setGeneratingState?.(true);
        this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
        if (addUserMessage) {
            this._addMessage('user', userMessage || normalizedDescription);
        }
        const turn = existingTurn || this._startAssistantTurn({
            activate: true,
            statusText: '规划中',
            statusTone: 'warning',
            openReply: true
        });
        this.activeAssistantTurn = turn;
        const requestContext = {
            description: normalizedDescription,
            hint,
            userMessage,
            attachmentPaths,
            templateSelection,
            explicitMode: '',
            hasCurrentFlowContext: this._hasCurrentFlowContext?.() === true
        };
        if (this.planningLifecycle?.status === 'running') {
            this.planningLifecycle.turn = turn;
            this.lastPlanningRequestContext = requestContext;
            this._advancePlanningLifecycle?.(
                'context',
                '正在整理当前流程、模板、附件、算子目录和工站边界。',
                { requestId: planRequestId });
        } else {
            this._beginPlanningLifecycle?.({
                requestId: planRequestId,
                requestContext,
                turn,
                phase: 'context'
            });
        }
        this._setAssistantTurnStatus(turn, '规划中', 'warning');
        this._setAssistantSectionText(
            turn,
            'reply',
            '规划进行中，实时状态和当前工作见左侧工作台。'
        );

        this._setResultStatusNote('正在收集工程上下文。', 'info');
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
        this._updatePlanBuildActionState();
        if (clearInput && input) {
            input.value = '';
            input.style.height = 'auto';
        }

        const planRequest = this._buildPlanModeRequest({
            description: normalizedDescription,
            hint,
            userMessage,
            attachmentPaths,
            templateSelection,
            semanticExtraction,
            clientMutationId: planRequestId
        });
        this._requestBackendVisionPlanLive(planRequest, {
            planRequestId,
            turn,
            fallbackDescription: normalizedDescription
        })
            .then(result => {
                if (!this._isActivePlanRequest(planRequestId)) return;
                const normalizedPlan = this._normalizeBackendPlanResult(result, normalizedDescription);
                this._clearActivePlanRequest(planRequestId);
                this._setGeneratingState?.(false);
                this._dispatchAgentWorkspaceEvent?.({
                    type: AgentWorkspaceEventTypes.PLAN_RECEIVED,
                    payload: { plan: normalizedPlan },
                    sessionId: this.sessionId,
                    planId: normalizedPlan?.planId,
                    planHash: normalizedPlan?.planHash,
                    revision: result?.revision || result?.Revision
                });
                this._mergeBackendPlanAnswers(this.pendingVisionPlan);
                this._requestPlanReadinessPreview?.(this.pendingVisionPlan, { reason: 'plan_received' });
                const timeoutFallback = this._isPlannerTimeoutFallback(this.pendingVisionPlan);
                const fallbackUsed = this.pendingVisionPlan.planSource === 'rule_fallback';
                this._setAssistantTurnStatus(turn, '规划完成', 'success');
                this._setAssistantSectionText(
                    turn,
                    'reply',
                    timeoutFallback
                        ? '模型规划超时，已使用规则兜底方案。可先按兜底方案构建，或稍后重试深度规划。'
                        : fallbackUsed
                            ? '规划已完成，已使用规则兜底方案。请确认推荐项或手动回答后开始构建。'
                            : '规划已完成，请确认推荐项或手动回答后开始构建。'
                );
                const persistenceWarning = this._getPersistenceWarning?.(this.pendingVisionPlan?.rawPlanSnapshot || result);
                if (persistenceWarning) {
                    this._setResultStatusNote(persistenceWarning.message || '规划结果已生成，但本次 Plan 工作台状态未能保存。', 'warning');
                } else {
                    this._setResultStatusNote('规划模式等待确认，确认后进入构建模式。', 'info');
                }
                this._renderAgentWorkspaceOverview();
                this._renderPlanWorkspace(this.pendingVisionPlan);
                this._updatePlanBuildActionState();
                this._markPlanningLifecycleTerminal?.(
                    'completed',
                    timeoutFallback
                        ? '模型规划超时，规则兜底方案已通过校验并接管工作台。'
                        : '规划与校验已完成，请确认关键问题后进入构建。',
                    { preserveReply: true });
                this.activeAssistantTurn = null;
            })
            .catch(error => {
                if (!this._isActivePlanRequest(planRequestId)) return;
                this._clearActivePlanRequest(planRequestId);
                this._setGeneratingState?.(false);
                if (clearInput && input) {
                    input.value = userMessage || normalizedDescription;
                    input.style.height = 'auto';
                }
                if (this._handlePlanningDeadlineExceeded?.(error, turn)) {
                    this.activeAssistantTurn = null;
                    return;
                }
                const cancelled = this.planningLifecycle?.status === 'cancelled';
                this._setAssistantTurnStatus(turn, cancelled ? '已取消' : '规划失败', cancelled ? 'cancelled' : 'failed');
                const message = this._sanitizePlanDiagnosticText(error?.message || String(error || '未知错误'), 260) || '未知错误';
                this._setAssistantSectionText(
                    turn,
                    'reply',
                    cancelled ? '规划已取消。' : `规划模式失败：${message}`
                );
                this._setResultStatusNote(cancelled ? '规划已取消。' : (message || '规划模式失败，请检查后端连接后重试。'), 'warning');
                this._renderAgentWorkspaceOverview();
                this._renderPlanWorkspace(this.pendingVisionPlan);
                this._updatePlanBuildActionState();
                if (!cancelled) {
                    this._markPlanningLifecycleTerminal?.('failed', `规划失败：${message}。可重试本次需求。`);
                }
                this.activeAssistantTurn = null;
            });

        return true;
    },

    _createPlanRequestId() {
        const randomPart = Math.random().toString(36).slice(2, 8);
        return `plan-${Date.now()}-${randomPart}`;
    },

    _isActivePlanRequest(requestId) {
        return Boolean(this.activePlanRequestId) && requestId === this.activePlanRequestId;
    },

    _clearActivePlanRequest(requestId = null) {
        if (!requestId || this.activePlanRequestId === requestId) {
            this.activePlanRequestId = null;
        }
    },

    _buildPlanModeRequest({
        description,
        hint = '',
        userMessage = '',
        attachmentPaths = [],
        templateSelection = null,
        semanticExtraction = null,
        clientMutationId = null
    }) {
        const normalizedTemplateSelection = this._normalizeTemplateSelection?.(templateSelection) || null;
        const currentFlowSnapshot = this._hasCurrentFlowContext?.()
            ? this._stringifyPlanSnapshot(this._getCurrentFlowJson?.())
            : null;
        return {
            description: String(description || '').trim(),
            originalUserPrompt: String(userMessage || description || '').trim(),
            additionalContext: String(hint || '').trim() || null,
            sessionId: this.sessionId || null,
            mode: 'plan',
            requirementMode: this.requirementMode || 'strict',
            currentFlowSnapshot,
            currentResultSnapshot: this._buildCurrentResultPlanSnapshot(),
            templateSelection: normalizedTemplateSelection,
            attachmentSummary: this._buildPlanAttachmentSummary(attachmentPaths),
            historySummary: this._buildPlanHistorySummary(),
            semanticExtraction: semanticExtraction || null,
            confirmedPlanAnswers: this._buildConfirmedPlanAnswers(this.pendingVisionPlan),
            resolvedPlanFields: this._getResolvedPlanFields(this.pendingVisionPlan),
            remainingPlanFields: this._getRemainingPlanFields(this.pendingVisionPlan),
            workspaceExpectedRevision: Math.max(0, Number(this.workspaceSnapshotRevision) || 0),
            clientMutationId: String(clientMutationId || '').trim() || this._createPlanRequestId(),
            planningBudgetMs: this._getPlanningBackendRemainingMs?.() || PLANNING_DEADLINE_FALLBACK.totalBudgetMs
        };
    },

    _buildPlanAttachmentSummary(attachmentPaths = []) {
        const explicitCount = Array.isArray(attachmentPaths) ? attachmentPaths.length : 0;
        const attachmentCount = explicitCount > 0
            ? explicitCount
            : (Array.isArray(this.attachments) ? this.attachments.length : 0);
        const resourceKinds = attachmentCount > 0
            ? ['user_attachment_metadata']
            : [];

        return {
            count: attachmentCount,
            resourceKinds,
            pathsRedacted: true
        };
    },

    _stringifyPlanSnapshot(value) {
        if (value === null || value === undefined) return null;
        if (typeof value === 'string') return value.trim() || null;
        try {
            return JSON.stringify(value);
        } catch {
            return null;
        }
    },

    _buildCurrentResultPlanSnapshot() {
        if (!this.currentResult) return null;
        const flow = this.currentResult.flow || this.currentResult.Flow || null;
        const pending = this.currentResult.pendingParameters || this.currentResult.PendingParameters || [];
        const missing = this.currentResult.missingResources || this.currentResult.MissingResources || [];
        const snapshot = {
            hasFlow: Boolean(flow),
            operatorCount: flow ? this._extractOperators(flow).length : 0,
            connectionCount: flow ? this._extractConnections(flow).length : 0,
            pendingParameterCount: Array.isArray(pending) ? pending.length : 0,
            missingResourceCount: Array.isArray(missing) ? missing.length : 0,
            generationMode: this.currentResult.generationMode || this.currentResult.GenerationMode || ''
        };
        try {
            return JSON.stringify(snapshot);
        } catch {
            return null;
        }
    },

    _buildPlanHistorySummary() {
        const items = Array.isArray(this.history) ? this.history.slice(0, 3) : [];
        if (!items.length) return null;
        return items
            .map(item => String(item.lastMessage || '').trim())
            .filter(Boolean)
            .slice(0, 3)
            .join(' / ') || null;
    },

    async _requestBackendVisionPlanLive(request, { planRequestId, turn, fallbackDescription = '' } = {}) {
        return await this._requestBackendVisionPlanRun(request, {
            planRequestId,
            turn,
            fallbackDescription
        });
    },

    async _requestBackendVisionPlanRun(request, { planRequestId, turn, fallbackDescription = '' } = {}) {
        const createResult = await httpClient.post('/ai/agent-plan-runs', request);
        const runId = String(createResult?.runId || createResult?.RunId || '').trim();
        const sessionId = String(createResult?.sessionId || createResult?.SessionId || '').trim();
        if (sessionId) {
            this._adoptCanonicalSessionId?.(sessionId, { reason: 'plan_run_response' });
        }
        this._applyWorkspaceSnapshotSummary?.(createResult?.workspaceSnapshot || createResult?.WorkspaceSnapshot || null);
        this._handleWorkspacePersistenceStatus?.(createResult?.persistenceStatus || createResult?.PersistenceStatus || null);
        if (!runId) {
            throw new Error('Plan Run 创建接口没有返回 runId。');
        }

        if (!this._isActivePlanRequest(planRequestId)) {
            throw new Error('Plan Run 已过期。');
        }

        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.RUN_STARTED,
            payload: { kind: 'plan', runId },
            sessionId: this.sessionId,
            runId
        });
        this._advancePlanningLifecycle?.(
            'context',
            'Plan Run 已创建，等待公开阶段事件接管进度。',
            { requestId: runId });
        this.activePlanRunRequestId = planRequestId;
        this._resetPublicLiveEventState?.();
        this.activeAssistantTurn = turn;
        this._setAssistantTurnStatus(turn, '规划中', 'streaming');

        const completion = new Promise((resolve, reject) => {
            this.activePlanRunCompletion = {
                runId,
                planRequestId,
                fallbackDescription,
                resolve,
                reject
            };
        });

        const initialEvents = createResult?.events || createResult?.Events || [];
        initialEvents.forEach(evt => this._handleAgentRunEvent(evt));
        this._renderPlanRunTimeline(turn);

        const lastSequence = this._getPlanRunLastSequence();
        this._startAgentRunEventSource?.(runId, { lastSequence });
        return await completion;
    },

    _applyWorkspaceSnapshotSummary(snapshot) {
        if (!snapshot || typeof snapshot !== 'object') return;
        const revision = Number(snapshot.revision ?? snapshot.Revision);
        if (Number.isFinite(revision)) {
            this.workspaceSnapshotRevision = revision;
            this._syncWorkspaceSnapshotDirty?.();
        }
        const buildRunId = String(snapshot.buildRunId ?? snapshot.BuildRunId ?? '').trim();
        const submittedBuildFingerprint = String(snapshot.submittedBuildFingerprint ?? snapshot.SubmittedBuildFingerprint ?? '').trim();
        if (Object.prototype.hasOwnProperty.call(snapshot, 'buildRunId') ||
            Object.prototype.hasOwnProperty.call(snapshot, 'BuildRunId')) {
            this.workspaceBuildRunId = buildRunId;
        }
        if (Object.prototype.hasOwnProperty.call(snapshot, 'submittedBuildFingerprint') ||
            Object.prototype.hasOwnProperty.call(snapshot, 'SubmittedBuildFingerprint')) {
            this.workspaceSubmittedBuildFingerprint = submittedBuildFingerprint;
        }
    },

    _handleWorkspacePersistenceStatus(status) {
        if (!status || typeof status !== 'object') return;
        const primarySaved = status.primaryStoreSaved ?? status.PrimaryStoreSaved;
        const backupSaved = status.recoveryBackupSaved ?? status.RecoveryBackupSaved;
        const message = String(status.publicMessage ?? status.PublicMessage ?? '').trim();
        if (primarySaved === false) {
            this.workspacePersistenceWarning = {
                level: 'warning',
                message: message || '结果已生成，但本次会话尚未成功保存。'
            };
            this._workspacePersistenceStatusNoteActive = true;
            this._workspacePersistenceStatusNoteText = this.workspacePersistenceWarning.message;
            this._setResultStatusNote?.(this.workspacePersistenceWarning.message, 'warning');
            return;
        }
        if (primarySaved === true) {
            this.workspacePersistenceWarning = null;
            if (this._workspacePersistenceStatusNoteActive) {
                const note = this.container?.querySelector?.('#ai-result-status-note');
                if (!note || String(note.textContent || '').trim() === String(this._workspacePersistenceStatusNoteText || '').trim()) {
                    this._setResultStatusNote?.('', '');
                }
                this._workspacePersistenceStatusNoteActive = false;
                this._workspacePersistenceStatusNoteText = '';
            }
        }
        if (backupSaved === false && message) {
            this.workspacePersistenceWarning = {
                level: 'info',
                message
            };
            this._workspacePersistenceStatusNoteActive = true;
            this._workspacePersistenceStatusNoteText = message;
            this._setResultStatusNote?.(message, 'info');
        }
    },

    _isActivePlanRunEvent(evt) {
        const runId = String(evt?.runId || '').trim();
        return Boolean(this.activePlanRunId) && runId === this.activePlanRunId;
    },

    _getPlanRunLastSequence() {
        return (Array.isArray(this.activePlanRunEvents) ? this.activePlanRunEvents : [])
            .reduce((max, evt) => Math.max(max, Number(evt?.sequence || 0)), 0);
    },

    _handlePlanRunEvent(evt) {
        if (!evt) {
            this._recordPublicLiveEventDrop?.('dropped');
            return;
        }
        if (!this._isActivePlanRunEvent(evt)) {
            this._recordPublicLiveEventDrop?.('stale');
            return;
        }
        if (this.activePlanRunRequestId && !this._isActivePlanRequest(this.activePlanRunRequestId)) {
            this._recordPublicLiveEventDrop?.('stale');
            return;
        }

        const before = this.agentWorkspaceState;
        const next = this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED,
            payload: { kind: 'plan', event: evt },
            sessionId: this.sessionId,
            runId: evt.runId,
            planId: this.agentWorkspaceState?.identity?.planId,
            planHash: this.agentWorkspaceState?.identity?.planHash
        });
        if (!next || next === before) {
            this._recordPublicLiveEventDrop?.('duplicate');
            return;
        }
        this._routePublicLiveEvent?.(this._normalizePublicLiveEvent?.(evt, { source: 'plan-run' }));

        if (evt.eventType === 'assistant.brief') {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '规划中', 'streaming');
        } else {
            this._renderPlanRunTimeline(this.activeAssistantTurn);
        }

        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);

        if (evt.eventType === 'plan.completed' ||
            evt.eventType === 'plan.cancelled' ||
            evt.eventType === 'plan.failed') {
            return;
        }

        if (evt.eventType === 'run.completed') {
            this._resolveActivePlanRun(evt);
            return;
        }

        if (evt.eventType === 'run.cancelled') {
            const terminal = this._applyPlanRunTerminalPayload(evt);
            this._rejectActivePlanRun(
                new Error(this._buildPlanRunTerminalMessage(evt, terminal, '规划已取消。')),
                { cancelled: true });
            return;
        }

        if (evt.eventType === 'run.failed') {
            const terminal = this._applyPlanRunTerminalPayload(evt);
            const error = new Error(this._buildPlanRunTerminalMessage(evt, terminal, '规划失败。'));
            error.payload = terminal.payload;
            this._rejectActivePlanRun(error);
        }
    },

    _resolveActivePlanRun(evt) {
        const completion = this.activePlanRunCompletion;
        if (!completion || completion.runId !== evt.runId) return;
        const payload = this._asObject?.(evt.payload) || evt.payload || {};
        const result = payload.planResult ||
            payload.PlanResult ||
            payload.planModeResult ||
            payload.PlanModeResult ||
            payload.result ||
            payload.Result ||
            null;
        if (!result) {
            if (evt.eventType === 'run.completed') {
                this._rejectActivePlanRun(new Error('Plan Run 完成事件缺少 PlanModeResult。'));
            }
            return;
        }

        if (evt.eventType === 'run.completed') {
            this._applyPlanRunTerminalPayload(evt, result);
        }

        this._closeAgentRunEventSource?.();
        if (this.activePlanRunId === completion.runId) {
            this.activePlanRunId = null;
            this.activePlanRunRequestId = null;
        }
        this.activePlanRunCompletion = null;
        completion.resolve(result);
    },

    _getPlanRunTerminalPayload(evt) {
        const payload = this._asObject?.(evt?.payload) || evt?.payload || {};
        const diagnostic = this._asObject?.(payload.diagnostic || payload.Diagnostic) || payload.diagnostic || payload.Diagnostic || {};
        return {
            ...(diagnostic && typeof diagnostic === 'object' ? diagnostic : {}),
            ...(payload && typeof payload === 'object' ? payload : {})
        };
    },

    _applyPlanRunTerminalPayload(evt, result = null) {
        const payload = this._getPlanRunTerminalPayload(evt);
        const workspaceSnapshot = payload.workspaceSnapshot || payload.WorkspaceSnapshot || null;
        const persistenceStatus = payload.persistenceStatus || payload.PersistenceStatus || null;
        const persistenceWarning = payload.persistenceWarning || payload.PersistenceWarning || null;
        this._applyWorkspaceSnapshotSummary?.(workspaceSnapshot);
        this._handleWorkspacePersistenceStatus?.(persistenceStatus);
        if (result && typeof result === 'object') {
            if (workspaceSnapshot) {
                result.workspaceSnapshot = workspaceSnapshot;
                result.WorkspaceSnapshot = workspaceSnapshot;
            }
            if (persistenceStatus) {
                result.persistenceStatus = persistenceStatus;
                result.PersistenceStatus = persistenceStatus;
            }
        }
        let warning = null;
        if (persistenceWarning) {
            if (result && typeof result === 'object') {
                result.persistenceWarning = persistenceWarning;
                result.PersistenceWarning = persistenceWarning;
            }
            warning = this._getPersistenceWarning?.({ persistenceWarning }) || null;
            this._setResultStatusNote?.(
                warning?.message || '规划结果已生成，但本次 Plan 工作台状态未能保存。',
                'warning');
        }

        return {
            payload,
            workspaceSnapshot,
            persistenceStatus,
            persistenceWarning,
            warning
        };
    },

    _buildPlanRunTerminalMessage(evt, terminal, fallback) {
        const payload = terminal?.payload || this._getPlanRunTerminalPayload(evt);
        const message = String(
            payload.publicMessage ||
            payload.PublicMessage ||
            payload.message ||
            payload.Message ||
            evt?.summary ||
            fallback ||
            ''
        ).trim() || fallback;
        const warningMessage = String(terminal?.warning?.message || '').trim();
        return warningMessage && !message.includes(warningMessage)
            ? `${message} ${warningMessage}`
            : message;
    },

    _rejectActivePlanRun(error, { cancelled = false } = {}) {
        const completion = this.activePlanRunCompletion;
        const runId = String(completion?.runId || '').trim();
        this._closeAgentRunEventSource?.();
        this.activePlanRunCompletion = null;
        if (!runId || this.activePlanRunId === runId) {
            this.activePlanRunId = null;
            this.activePlanRunRequestId = null;
        }
        this._setGeneratingState?.(false);
        if (cancelled) {
            this._clearActivePlanRequest(this.activePlanRunRequestId);
            this._setWorkbenchState(AiWorkbenchStates.CANCELLED);
            this._setAssistantTurnStatus(this.activeAssistantTurn, '已取消', 'cancelled');
            this._setAssistantSectionText(this.activeAssistantTurn, 'reply', '规划已取消。');
            this._markPlanningLifecycleTerminal?.('cancelled', '规划已取消，未完成阶段保持未完成。可重试本次需求。');
        }

        completion?.reject(error);
    },

    _renderPlanRunTimeline(turn = this.activeAssistantTurn) {
        if (!turn?.processSection || !turn?.processBody) return;

        const progress = this._getPlanRunProgressState();
        turn.processSection.hidden = false;
        PLAN_PHASES.forEach(phase => {
            const item = progress.phases[phase.key] || {
                label: phase.label,
                status: PLAN_PENDING_STATUS,
                summary: ''
            };
            const statusLabel = this._formatPlanTimelineStatus(item.status);
            const summary = item.summary ? `\n${item.summary}` : '';
            const node = this._updateThinkingStep(
                this.planningLifecycle?.timelineId || this.planningLifecycle?.requestId || this.activePlanRunId || 'plan-run',
                `plan:${phase.key}`,
                `${item.label}：${statusLabel}${summary}`
            );
            if (!node) return;
            node.className = `ai-agent-run-step is-${this._getPlanTimelineTone(item.status)}`;
            node.dataset.stage = `plan:${phase.key}`;
            node.dataset.eventType = item.eventType || '';
            node.title = item.eventType || item.stage || '';
            if (item.publicReason) {
                node.dataset.publicReason = item.publicReason;
                node.title = item.publicReason;
            }
        });

        if (progress.currentLabel && progress.status === 'running') {
            this._setResultStatusNote(progress.currentLabel, progress.warning ? 'warning' : 'info');
            this._setAssistantTurnStatus?.(turn, '规划中', 'streaming');
            this._setAssistantSectionText?.(
                turn,
                'reply',
                progress.slow
                    ? '规划响应较慢，但仍在处理。可继续等待或取消，详细阶段见左侧工作台。'
                    : '规划进行中，详细阶段和当前工作见左侧工作台。');
        }
    },

    _getPlanRunProgressState() {
        const phases = Object.fromEntries(PLAN_PHASES.map(phase => [
            phase.key,
            {
                label: phase.label,
                status: PLAN_PENDING_STATUS,
                summary: '',
                eventType: ''
            }
        ]));
        const lifecycle = this.planningLifecycle || null;
        const events = Array.isArray(this.activePlanRunEvents) ? this.activePlanRunEvents : [];
        let currentLabel = String(lifecycle?.currentSummary || '').trim();
        let warning = false;

        if (lifecycle) {
            phases.understand.status = lifecycle.routerStatus || (lifecycle.phase === 'understand' ? 'running' : PLAN_PENDING_STATUS);
            phases.understand.summary = lifecycle.routerSummary || '';
            phases.understand.eventType = lifecycle.routerStatus === 'completed' ? 'intent-router-result' : 'intent-router-run';
            phases.understand.publicReason = lifecycle.routerPublicReason || '';
            if (lifecycle.status === 'running' && lifecycle.phase !== 'understand') {
                phases.context.status = 'running';
                phases.context.summary = lifecycle.currentSummary || '正在整理工程上下文。';
            }
            if (lifecycle.status !== 'running' && lifecycle.status !== 'completed') {
                const terminalPhase = phases[lifecycle.phase] || phases.validate;
                terminalPhase.status = lifecycle.status;
                terminalPhase.summary = lifecycle.currentSummary || '';
                warning = lifecycle.status === 'failed' || lifecycle.status === 'timeout';
            }
        }

        events
            .slice()
            .sort((a, b) => Number(a.sequence || 0) - Number(b.sequence || 0))
            .forEach(evt => {
                const update = this._mapPlanEventToProgress(evt);
                if (!update) return;
                phases[update.key] = {
                    ...phases[update.key],
                    ...update
                };
                currentLabel = update.summary || `${update.label}：${this._formatPlanTimelineStatus(update.status)}`;
                warning = update.status === 'failed' || update.status === 'timeout';
            });

        if (lifecycle?.status === 'completed') {
            if (this.pendingVisionPlan) {
                Object.values(phases).forEach(phase => {
                    if (phase.status === PLAN_PENDING_STATUS || phase.status === 'waiting' || phase.status === 'running') {
                        phase.status = 'completed';
                        phase.summary = phase.summary || '已由正式规划结果确认。';
                    }
                });
            }
            phases.validate.status = 'completed';
            phases.validate.summary = lifecycle.currentSummary || phases.validate.summary;
        }

        return {
            phases,
            currentLabel,
            warning,
            eventCount: events.length,
            status: lifecycle?.status || (this.activePlanRunId ? 'running' : 'idle'),
            slow: lifecycle?.slow === true,
            canCancel: lifecycle?.status === 'running',
            canRetry: ['failed', 'timeout', 'cancelled'].includes(String(lifecycle?.status || '').toLowerCase()) &&
                Boolean(this.lastPlanningRequestContext?.description)
        };
    },

    _mapPlanEventToProgress(evt) {
        const eventType = String(evt?.eventType || '').trim();
        const stage = String(evt?.stage || '').trim();
        const summary = this._formatPlanEventSummary(evt);
        const base = {
            stage,
            eventType,
            summary
        };

        if (eventType === 'plan.context.started') {
            return { ...base, key: 'context', label: '整理工程上下文', status: 'running' };
        }
        if (eventType === 'plan.context.completed') {
            return { ...base, key: 'context', label: '整理工程上下文', status: 'completed' };
        }
        if (eventType === 'plan.model.started') {
            return { ...base, key: 'generate', label: '生成方案', status: 'running' };
        }
        if (eventType === 'plan.model.completed') {
            return { ...base, key: 'generate', label: '生成方案', status: 'completed' };
        }
        if (eventType === 'plan.model.timeout') {
            return { ...base, key: 'generate', label: '生成方案', status: 'timeout', summary: '模型规划超时，规则兜底正在接管。' };
        }
        if (eventType === 'plan.model.failed') {
            return { ...base, key: 'generate', label: '生成方案', status: 'failed', summary: summary || '模型规划失败，规则兜底正在接管。' };
        }
        if (eventType === 'plan.contract.started') {
            return { ...base, key: 'validate', label: '校验方案', status: 'running' };
        }
        if (eventType === 'plan.contract.completed') {
            if (String(evt?.status || '').trim().toLowerCase() === 'failed') {
                return { ...base, key: 'validate', label: '校验方案', status: 'failed', summary: summary || '契约校验失败，规则兜底正在接管。' };
            }
            return { ...base, key: 'validate', label: '校验方案', status: 'running', summary: summary || '规划契约已校验，正在应用安全约束。' };
        }
        if (eventType === 'plan.safety.completed') {
            return { ...base, key: 'validate', label: '校验方案', status: 'completed' };
        }
        if (eventType === 'plan.fallback.used') {
            return { ...base, key: 'generate', label: '规则兜底', status: 'completed', summary: summary || '规则兜底方案已生成。' };
        }
        if (eventType === 'plan.completed' || eventType === 'run.completed') {
            return { ...base, key: 'validate', label: '校验方案', status: 'completed', summary: '规划已就绪。' };
        }
        if (eventType === 'plan.cancelled' || eventType === 'run.cancelled') {
            return { ...base, key: 'generate', label: '生成方案', status: 'cancelled', summary: '规划已取消。' };
        }
        if (eventType === 'plan.failed' || eventType === 'run.failed') {
            return { ...base, key: 'generate', label: '生成方案', status: 'failed', summary: summary || '规划失败。' };
        }

        return null;
    },

    _formatPlanEventSummary(evt) {
        const eventType = String(evt?.eventType || '').trim();
        const summary = String(evt?.summary || '').trim();
        const title = String(evt?.title || '').trim();
        const normalized = summary || title;
        if (!normalized) {
            return '';
        }

        if (eventType === 'plan.context.completed') {
            return '已收集公开需求、流程、模板、附件、算子和工站边界。';
        }
        if (eventType === 'plan.model.started') {
            return '模型规划中。';
        }
        if (eventType === 'plan.contract.started') {
            return '正在校验规划契约。';
        }
        if (eventType === 'plan.contract.completed') {
            if (String(evt?.status || '').trim().toLowerCase() === 'failed') {
                return this._localizeDisplayText(normalized) || '契约校验失败，已使用规则兜底方案。';
            }
            return '规划契约已校验。';
        }
        if (eventType === 'plan.safety.completed') {
            return '已应用安全约束。';
        }
        if (eventType === 'plan.fallback.used') {
            return '已使用规则兜底方案。';
        }

        return this._localizeDisplayText(normalized);
    },

    _formatPlanTimelineStatus(status) {
        switch (String(status || '').trim().toLowerCase()) {
            case 'running':
                return '进行中';
            case 'completed':
                return '完成';
            case 'timeout':
                return '超时';
            case 'failed':
                return '失败';
            case 'cancelled':
            case 'canceled':
                return '已取消';
            case PLAN_PENDING_STATUS:
            case 'pending':
                return '等待中';
            default:
                return this._formatBuildStatus(status);
        }
    },

    _getPlanTimelineTone(status) {
        switch (String(status || '').trim().toLowerCase()) {
            case 'completed':
                return 'success';
            case 'timeout':
            case 'failed':
                return 'warning';
            case 'cancelled':
            case 'canceled':
                return 'cancelled';
            case 'running':
                return 'running';
            default:
                return 'warning';
        }
    },

    _isPlannerTimeoutFallback(plan) {
        return String(plan?.fallbackReason || '').includes('超时') ||
            String(plan?.fallbackReason || '').toLowerCase() === 'planner_timeout';
    },

    _cancelActivePlanRun() {
        const runId = String(this.activePlanRunId || '').trim();
        if (!runId) {
            return Promise.resolve(false);
        }

        return httpClient
            .post(`/ai/agent-runs/${encodeURIComponent(runId)}/cancel`)
            .then(() => true)
            .catch(error => {
                this.isCancellingGenerate = false;
                this._setGeneratingState?.(this.isGenerating);
                this._addMessage('system', `取消规划未生效：${this._sanitizePlanDiagnosticText(error?.message || '未知错误', 260) || '未知错误'}`);
                return false;
            });
    },

    _normalizeBackendPlanResult(result, fallbackDescription = '') {
        const plan = this._asObject?.(result) || result || {};
        const route = plan.recommendedRoute || plan.RecommendedRoute || {};
        const questions = plan.clarificationQuestions || plan.ClarificationQuestions || [];
        const defaults = plan.recommendedDefaults || plan.RecommendedDefaults || [];
        const contextSummary = plan.contextSummary || plan.ContextSummary || {};
        const templateSelection = this._normalizeTemplateSelection?.(plan.templateSelection || plan.TemplateSelection) ||
            this._normalizeTemplateSelection?.({
                mode: contextSummary.templateSelectionMode || contextSummary.TemplateSelectionMode || '',
                templateId: contextSummary.templateId || contextSummary.TemplateId || ''
            }) ||
            null;
        const normalizedQuestions = Array.isArray(questions)
            ? questions.map(question => this._normalizePlanQuestion(question)).filter(Boolean)
            : [];
        const normalizedDefaults = Array.isArray(defaults)
            ? defaults.map(item => this._normalizePlanDefault(item)).filter(Boolean)
            : [];
        const requirementMaturity = this._normalizeRequirementMaturity(plan.requirementMaturity || plan.RequirementMaturity);
        const semanticExtraction = this._normalizeSemanticExtraction(plan.semanticExtraction || plan.SemanticExtraction);
        const decisionTrace = this._normalizeDecisionTrace(plan.decisionTrace || plan.DecisionTrace);
        const rawCanBuild = plan.canBuild ?? plan.CanBuild;
        const rawCanPlan = plan.canPlan ?? plan.CanPlan;
        const maturityCanPlan = requirementMaturity?.canPlan === true;
        const requirementMode = 'strict';
        const blockingReasons = this._toArray(plan.blockingReasons || plan.BlockingReasons)
            .map(item => this._sanitizePlanDisplayText(item))
            .filter(reason => !this._isDraftableImageSourceBlockingReason(reason, route, requirementMode));
        const publicEvents = this._toArray(plan.publicEvents || plan.PublicEvents)
            .map(evt => this._normalizePlanPublicEvent(evt));
        const rawFallbackReason = String(plan.fallbackReason || plan.FallbackReason || '').trim();
        const plannerFailure = this._normalizePlannerFailureDiagnostics(plan, publicEvents);
        const normalizedBuildReadiness = this._normalizePlanBuildReadiness(plan.buildReadiness || plan.BuildReadiness);
        const authoritativeBuildReadiness = this._isUsableAuthoritativeReadiness(normalizedBuildReadiness, plan)
            ? normalizedBuildReadiness
            : null;
        const buildReadiness = authoritativeBuildReadiness || {
            canBuild: false,
            blockers: [{
                id: 'contract_warning:authoritative_readiness_missing',
                category: PLAN_BUILD_BLOCKER_CATEGORIES.CONTRACT_WARNING,
                field: '',
                questionId: '',
                blocksBuild: true,
                resolutionMode: PLAN_BUILD_RESOLUTION_MODES.NON_BLOCKING,
                publicLabel: '后端未返回合法 readiness，前端不会推断可构建状态。'
            }],
            resolvedFields: [],
            remainingFields: this._toArray(plan.remainingPlanFields || plan.RemainingPlanFields),
            primaryMessage: '等待后端返回权威构建条件。',
            contractVersion: 'v2'
        };

        const normalized = {
            id: plan.planId || plan.PlanId || `plan-${Date.now()}`,
            planId: plan.planId || plan.PlanId || '',
            planHash: String(plan.planHash || plan.PlanHash || '').trim(),
            mode: AgentWorkspaceModes.PLAN,
            originalDescription: plan.originalUserPrompt || plan.OriginalUserPrompt || fallbackDescription,
            buildPrompt: plan.originalUserPrompt || plan.OriginalUserPrompt || fallbackDescription,
            goal: this._sanitizePlanDisplayText(plan.goal || plan.Goal || fallbackDescription || '视觉流程草稿', 220),
            intent: this._sanitizePlanDiagnosticCode(plan.intent || plan.Intent || ''),
            confidence: plan.confidence || plan.Confidence || 'medium',
            requirementMode,
            planSource: this._sanitizePlanDiagnosticCode(plan.planSource || plan.PlanSource || ''),
            currentPhase: this._sanitizePlanDiagnosticCode(plan.currentPhase || plan.CurrentPhase || ''),
            rawFallbackReason,
            fallbackReason: this._formatPlanFallbackReason(rawFallbackReason),
            plannerFailure,
            plannerFailureStage: plannerFailure.stage,
            plannerFailureCode: plannerFailure.code,
            sanitizedErrorKind: plannerFailure.kind,
            sanitizedErrorMessage: plannerFailure.message,
            planWarnings: this._toArray(plan.planWarnings || plan.PlanWarnings)
                .map(item => this._sanitizePlanDiagnosticText(this._localizeDisplayText(item)))
                .filter(Boolean),
            contractRepairNotes: this._toArray(plan.contractRepairNotes || plan.ContractRepairNotes)
                .map(item => this._sanitizePlanDiagnosticText(this._localizeDisplayText(item), 120))
                .filter(Boolean),
            publicEvents,
            blockerCount: blockingReasons.length,
            nextAction: this._sanitizePlanDisplayText(plan.nextAction || plan.NextAction || '复核计划后开始构建。'),
            canPlan: rawCanPlan === true || maturityCanPlan,
            executable: buildReadiness.canBuild === true,
            buildReadiness,
            authoritativeBuildReadiness,
            blockingReasons,
            resolvedPlanFields: this._toArray(plan.resolvedPlanFields || plan.ResolvedPlanFields)
                .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
                .filter(Boolean),
            remainingPlanFields: this._toArray(plan.remainingPlanFields || plan.RemainingPlanFields)
                .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
                .filter(Boolean),
            understanding: this._toArray(plan.requirementUnderstanding || plan.RequirementUnderstanding).length
                ? this._toArray(plan.requirementUnderstanding || plan.RequirementUnderstanding).map(item => this._sanitizePlanDisplayText(item, 220))
                : [this._sanitizePlanDisplayText(`用户目标：${fallbackDescription || '视觉流程草稿'}`, 220)],
            route: {
                routeId: this._sanitizePlanDiagnosticCode(route.routeId || route.RouteId || ''),
                title: this._sanitizePlanDisplayText(route.title || route.Title || '视觉方案路线', 180),
                summary: this._sanitizePlanDisplayText(route.summary || route.Summary || '', 260),
                operators: this._toArray(route.operators || route.Operators).map(op => this._sanitizePlanDiagnosticText(op, 80)).filter(Boolean),
                templateDecision: this._sanitizePlanDisplayText(route.templateDecision || route.TemplateDecision || '', 160)
            },
            questions: normalizedQuestions,
            assumptions: normalizedDefaults.length
                ? normalizedDefaults.map(item => this._sanitizePlanDisplayText(`${item.label}: ${this._localizeDisplayText(item.value)}${item.impact ? `（${item.impact}）` : ''}`, 220))
                : ['保留公开元数据边界，缺失资源在确认前保持为待补项。'],
            recommendedDefaults: normalizedDefaults,
            steps: this._toArray(plan.executablePlan || plan.ExecutablePlan).map(item => this._sanitizePlanDisplayText(item, 220)),
            risks: this._toArray(plan.risks || plan.Risks).map(item => this._sanitizePlanDisplayText(item, 220)),
            acceptanceCriteria: this._toArray(plan.acceptanceCriteria || plan.AcceptanceCriteria).map(item => this._sanitizePlanDisplayText(item, 220)),
            contextSummary,
            operatorCatalogVersion: plan.operatorCatalogVersion || plan.OperatorCatalogVersion || '',
            templateCatalogVersion: plan.templateCatalogVersion || plan.TemplateCatalogVersion || '',
            templateSelection,
            semanticExtraction,
            requirementMaturity,
            decisionTrace,
            stationBoundarySummary: this._sanitizePlanDisplayText(plan.stationBoundarySummary || plan.StationBoundarySummary || '', 220),
            plcOutputPolicy: this._sanitizePlanDisplayText(plan.plcOutputPolicy || plan.PlcOutputPolicy || '', 220),
            rawPlanSnapshot: plan,
            effectiveReadiness: null,
            previewState: 'idle',
            previewError: ''
        };
        const initialPreview = this._makeInitialPlanReadinessPreview(normalized, authoritativeBuildReadiness);
        if (initialPreview) {
            normalized.effectiveReadiness = initialPreview;
            normalized.previewState = 'ready';
            normalized.previewError = '';
            normalized.buildReadiness = initialPreview.buildReadiness;
            normalized.executable = initialPreview.buildReadiness.canBuild === true;
        } else {
            normalized.executable = false;
            normalized.previewState = 'idle';
        }
        return normalized;
    },

    _normalizePlanBuildReadiness(value) {
        if (!value || typeof value !== 'object') return null;
        const item = this._asObject?.(value) || value;
        const blockers = this._toArray(item.blockers || item.Blockers)
            .map(blocker => this._normalizePlanBuildBlocker(blocker))
            .filter(Boolean);
        return {
            canBuild: (item.canBuild ?? item.CanBuild) === true,
            blockers,
            missingResources: this._toArray(item.missingResources || item.MissingResources)
                .map(resource => normalizeCanonicalResource(resource, { source: resource?.source || resource?.Source || 'readiness' })),
            resolvedFields: this._toArray(item.resolvedFields || item.ResolvedFields)
                .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
                .filter(Boolean),
            remainingFields: this._toArray(item.remainingFields || item.RemainingFields)
                .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
                .filter(Boolean),
            primaryMessage: this._sanitizePlanDisplayText(item.primaryMessage || item.PrimaryMessage || '', 220),
            contractVersion: String(item.contractVersion || item.ContractVersion || 'v2').trim()
        };
    },

    _applyBuildFromPlanCanonicalState(payload) {
        if (!this.pendingVisionPlan || !payload || typeof payload !== 'object') return false;
        const data = this._asObject?.(payload) || payload;
        const buildReplay = this._asObject?.(data.buildFromPlan || data.BuildFromPlan) || {};
        const planSnapshot = data.planSnapshot || data.PlanSnapshot || buildReplay.planSnapshot || buildReplay.PlanSnapshot || null;
        const readiness = this._normalizePlanBuildReadiness(
            data.buildReadiness ||
            data.BuildReadiness
        );
        if (!this._isUsableAuthoritativeReadiness(readiness)) return false;

        const plan = this.pendingVisionPlan;
        const incomingPlanId = String(
            data.planId ||
            data.PlanId ||
            ''
        ).trim();
        const currentPlanId = String(plan.planId || plan.id || '').trim();
        if (!incomingPlanId || !currentPlanId || incomingPlanId !== currentPlanId) {
            return false;
        }

        const incomingPlanHash = String(data.planHash || data.PlanHash || '').trim();
        const currentPlanHash = String(plan.planHash || '').trim();
        if (incomingPlanHash && currentPlanHash && incomingPlanHash !== currentPlanHash) {
            return false;
        }

        const maturity = this._normalizeRequirementMaturity(data.requirementMaturity || data.RequirementMaturity || planSnapshot?.requirementMaturity || planSnapshot?.RequirementMaturity);
        const trace = this._normalizeDecisionTrace(data.decisionTrace || data.DecisionTrace || planSnapshot?.decisionTrace || planSnapshot?.DecisionTrace);
        const canonicalState = {
            planId: currentPlanId,
            planHash: currentPlanHash,
            requirementMode: this._normalizeRequirementMode?.(this.requirementMode || plan.requirementMode || 'strict') || 'strict',
            answerRevision: Number(this.planAnswerRevision) || 0,
            resourceRevision: Number(this.agentWorkspaceState?.resources?.revision) || 0,
            acceptedAnswers: this._toArray(data.acceptedAnswers || data.AcceptedAnswers),
            answerSetFingerprint: String(data.answerSetFingerprint || data.AnswerSetFingerprint || ''),
            buildReadiness: readiness,
            deferredQuestionIds: [],
            pendingConfirmationCount: this._toArray(readiness.remainingFields).length,
            resourcePendingCount: this._toArray(readiness.blockers)
                .filter(blocker => blocker?.category === PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING).length,
            hardBlockerCount: this._toArray(readiness.blockers)
                .filter(blocker => blocker?.blocksBuild === true &&
                    blocker?.category !== PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING).length,
            metadataOnly: (data.metadataOnly ?? data.MetadataOnly) === true,
            contractValid: true,
            failureCode: '',
            failureMessage: ''
        };
        const blockingFields = this._toArray(data.blockingClarificationFields || data.BlockingClarificationFields);
        const readinessBlockers = this._toArray(readiness.blockers)
                .filter(blocker => blocker?.blocksBuild === true)
                .map(blocker => blocker.id || blocker.field)
                .filter(Boolean);
        const nextPlan = {
            ...plan,
            rawPlanSnapshot: planSnapshot && typeof planSnapshot === 'object' ? planSnapshot : plan.rawPlanSnapshot,
            requirementMaturity: maturity || plan.requirementMaturity,
            decisionTrace: trace || plan.decisionTrace,
            authoritativeBuildReadiness: readiness,
            buildReadiness: readiness,
            executable: readiness.canBuild === true,
            effectiveReadiness: canonicalState,
            previewState: readiness.canBuild === true ? 'ready' : 'blocked',
            resolvedPlanFields: this._toArray(readiness.resolvedFields),
            remainingPlanFields: this._toArray(readiness.remainingFields),
            blockingReasons: readinessBlockers.length ? readinessBlockers : blockingFields
        };
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.PLAN_RECEIVED,
            payload: { plan: nextPlan },
            planId: currentPlanId,
            planHash: currentPlanHash
        });
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.READINESS_RECEIVED,
            payload: canonicalState,
            planId: currentPlanId,
            planHash: currentPlanHash
        });
        return true;
    },

    _isUsableAuthoritativeReadiness(snapshot) {
        if (!snapshot || typeof snapshot !== 'object') return false;
        const version = String(snapshot.contractVersion || '').trim().toLowerCase();
        if (version !== 'v2') return false;

        const blockers = this._toArray(snapshot.blockers).filter(Boolean);
        const blocking = blockers.filter(blocker => blocker?.blocksBuild === true);
        if (snapshot.canBuild === true && blocking.length > 0) return false;

        const hasContent = blockers.length > 0 ||
            this._toArray(snapshot.resolvedFields).length > 0 ||
            this._toArray(snapshot.remainingFields).length > 0 ||
            Boolean(String(snapshot.primaryMessage || '').trim());
        if (!hasContent) return false;

        if (snapshot.canBuild !== true &&
            blocking.length === 0 &&
            !String(snapshot.primaryMessage || '').trim()) {
            return false;
        }

        return true;
    },

    _normalizePlanBuildBlocker(value) {
        if (!value || typeof value !== 'object') return null;
        const item = this._asObject?.(value) || value;
        const id = String(item.id || item.Id || '').trim();
        const category = String(item.category || item.Category || '').trim().toLowerCase();
        const field = this._inferPlanQuestionField(item.field || item.Field || '') ||
            String(item.field || item.Field || '').trim().toLowerCase();
        const publicLabel = this._sanitizePlanDisplayText(item.publicLabel || item.PublicLabel || '', 160);
        if (!id && !category && !field && !publicLabel) return null;
        if (category === PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING) {
            return normalizeCanonicalResource(item, { source: 'build_readiness' });
        }
        return {
            id,
            category,
            field,
            questionId: String(item.questionId || item.QuestionId || '').trim(),
            blocksBuild: (item.blocksBuild ?? item.BlocksBuild) === true,
            resolutionMode: String(item.resolutionMode || item.ResolutionMode || '').trim().toLowerCase(),
            publicLabel
        };
    },

    _isPlanBuildBlockingField(field, requirementMode, requirementMaturity = null, context = null) {
        const normalized = this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase();
        if (!normalized) return false;
        if (normalized === PLAN_ANSWER_FIELDS.OUTPUT_TARGET) {
            return this._planRequestsExternalOutput(
                context?.plan || null,
                context?.reason || '',
                context?.semanticExtraction || null,
                context?.route || null
            );
        }
        const mode = this._normalizeRequirementMode?.(requirementMode || this.requirementMode || 'strict') || 'strict';
        if (mode !== 'draft') {
            return PLAN_STRICT_BUILD_FIELDS.has(normalized);
        }

        if (normalized === PLAN_ANSWER_FIELDS.INSPECTION_OBJECT ||
            normalized === PLAN_ANSWER_FIELDS.TASK_TYPE ||
            normalized === PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY) {
            return requirementMaturity?.canPlan !== true;
        }

        return false;
    },

    _planRequestsExternalOutput(plan = null, reason = '', semanticExtraction = null, route = null) {
        const semantic = semanticExtraction || plan?.semanticExtraction || plan?.SemanticExtraction || {};
        const routeInfo = route || plan?.route || plan?.recommendedRoute || plan?.RecommendedRoute || {};
        const taskTypes = [plan?.intent, plan?.Intent, semantic?.taskType, semantic?.TaskType]
            .map(value => String(value || '').trim().toLowerCase())
            .filter(Boolean);
        if (taskTypes.includes('plc_output')) return true;
        if (this._planExternalOutputTarget(semantic?.outputTarget || semantic?.OutputTarget)) return true;
        if (this._planExternalOutputPolicy(plan?.plcOutputPolicy || plan?.PlcOutputPolicy)) return true;
        if (this._planExternalOutputTarget(routeInfo?.routeId || routeInfo?.RouteId) ||
            this._planExternalOutputAction(routeInfo?.summary || routeInfo?.Summary)) {
            return true;
        }

        return this._planExternalOutputAction(reason) ||
            this._planExternalOutputAction(plan?.goal || plan?.Goal) ||
            this._planExternalOutputAction(plan?.originalDescription || plan?.OriginalUserPrompt || plan?.buildPrompt);
    },

    _planExternalOutputPolicy(value) {
        const text = String(value || '').trim();
        if (!text) return false;
        if (this._planLocalOrDisabledOutput(text)) return false;
        return this._planExternalOutputTarget(text) || this._planExternalOutputAction(text);
    },

    _planExternalOutputTarget(value) {
        const text = String(value || '').trim();
        if (!text) return false;
        if (this._planLocalOrDisabledOutput(text)) return false;
        return /(^|[^A-Za-z0-9])(plc|mes|erp|api|webhook)([^A-Za-z0-9]|$)/i.test(text) ||
            /plc_output|business_system_output|external_system_output|对接MES|写入PLC|发送到ERP|发送ERP|业务系统接口|网络接口|HTTP接口|API接口|Webhook/i.test(text);
    },

    _planExternalOutputAction(value) {
        const text = String(value || '').trim();
        if (!text) return false;
        const hasExplicitExternalTarget = /\b(mes|erp|api|webhook)\b/i.test(text) ||
            /输出到\s*MES|发送到\s*ERP|调用\s*HTTP\s*API|推送\s*Webhook|对接业务系统/i.test(text);
        if (this._planLocalOrDisabledOutput(text) && !hasExplicitExternalTarget) return false;
        return /\b(send|write|output|push|post|call|publish|emit)\b.{0,32}\b(plc|mes|erp|http|api|webhook)\b/i.test(text) ||
            /\b(plc|mes|erp|http|api|webhook)\b.{0,32}\b(output|endpoint|write|push|post|call)\b/i.test(text) ||
            /(输出|发送|发|写入|推送|调用|对接).{0,16}(MES|PLC|ERP|HTTP|API|Webhook|业务系统)/i.test(text) ||
            /输出到\s*MES|写入\s*PLC|发送到\s*ERP|调用\s*HTTP\s*API|推送\s*Webhook|对接业务系统|业务系统接口/i.test(text);
    },

    _planLocalOrDisabledOutput(value) {
        const text = String(value || '').trim();
        if (!text) return false;
        return /local_result_payload|structured_result_output|local\s+ResultOutput|本地\s*ResultOutput|本地输出|本地结果|PLC\s+disabled|PLC\s+writes?\s+disabled|PLC\s+write\s+disabled|不写入\s*PLC|不对接|禁用\s*PLC/i.test(text);
    },

    _planHasAnyObjectOrTaskFact(requirementMaturity, semanticExtraction, resolvedFields = new Set()) {
        if (resolvedFields.has(PLAN_ANSWER_FIELDS.INSPECTION_OBJECT) ||
            resolvedFields.has(PLAN_ANSWER_FIELDS.TASK_TYPE)) {
            return true;
        }

        const task = String(semanticExtraction?.taskType || requirementMaturity?.taskType || '')
            .trim()
            .toLowerCase();
        if (task && task !== 'unknown' && task !== 'abstract_goal') {
            return true;
        }

        if (String(semanticExtraction?.inspectionObject || '').trim()) {
            return true;
        }

        return this._toArray(requirementMaturity?.objectSignals).some(value => String(value || '').trim()) ||
            this._toArray(requirementMaturity?.taskSignals).some(value => String(value || '').trim());
    },

    _ensureRecommendedPlanQuestionSelections(plan) {
        return false;
    },

    _getPlanReadinessStats(plan) {
        const readiness = plan?.buildReadiness || {};
        const blockers = this._toArray(readiness.blockers);
        const blockingIds = new Set(blockers
            .filter(blocker => blocker?.blocksBuild === true)
            .map(blocker => String(blocker?.id || '').trim())
            .filter(Boolean));
        const resourceIds = new Set(blockers
            .filter(blocker => blocker?.category === PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING)
            .map(blocker => String(blocker?.id || '').trim())
            .filter(Boolean));
        const pendingFields = new Set(this._toArray(readiness.remainingFields)
            .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
            .filter(Boolean));
        return {
            blockingCount: blockingIds.size,
            pendingConfirmationCount: pendingFields.size,
            resourcePendingCount: resourceIds.size
        };
    },

    _buildPlanMissingSummary(plan, missingFields = null) {
        if (!plan) {
            return {
                totalCount: 0,
                mustConfirmCount: 0,
                fillLaterCount: 0,
                missingFields: [],
                summaryText: '总计 0 项；构建前必须确认 0 项；可构建后补齐 0 项'
            };
        }

        const preview = this._getCurrentCanonicalPreview?.(plan);
        const readiness = preview?.buildReadiness || plan?.buildReadiness || {};
        const readinessStats = this._getPlanReadinessStats({ ...plan, buildReadiness: readiness });
        const stats = {
            ...readinessStats,
            pendingConfirmationCount: Number(preview?.pendingConfirmationCount) || readinessStats.pendingConfirmationCount,
            resourcePendingCount: Number(preview?.resourcePendingCount) || readinessStats.resourcePendingCount,
            hardBlockerCount: Number(preview?.hardBlockerCount) || 0,
            buildBlockingConfirmationCount: Number(preview?.buildBlockingConfirmationCount) || 0,
            buildRequiredResourceCount: Number(preview?.buildRequiredResourceCount) || 0,
            deferredFieldCount: Number(preview?.deferredFieldCount) || 0,
            draftAllowedResourceCount: Number(preview?.draftAllowedResourceCount) || 0,
            mustConfirmBeforeBuildCount: Number(preview?.mustConfirmBeforeBuildCount) || 0,
            fillLaterCount: Number(preview?.fillLaterCount) || 0,
            totalIncompleteCount: Number(preview?.totalIncompleteCount) || 0
        };
        const fields = Array.isArray(missingFields)
            ? missingFields
            : this._collectPlanMissingInformation(plan).missingFields;
        const normalizedFields = [...new Set(this._toArray(fields)
            .map(field => this._inferPlanQuestionField?.(field) || String(field || '').trim().toLowerCase())
            .filter(Boolean))];
        const previewCarriesPartition = preview?.hasAuthoritativeCountPartition !== false &&
            ['mustConfirmBeforeBuildCount', 'fillLaterCount', 'totalIncompleteCount']
                .every(key => Object.prototype.hasOwnProperty.call(preview || {}, key));
        let mustConfirmCount;
        let fillLaterCount;
        let totalCount;
        if (previewCarriesPartition) {
            mustConfirmCount = Math.max(Number(preview?.mustConfirmBeforeBuildCount) || 0, 0);
            fillLaterCount = Math.max(Number(preview?.fillLaterCount) || 0, 0);
            totalCount = Math.max(Number(preview?.totalIncompleteCount) || 0, 0);
        } else {
            const blockingKeys = new Set();
            const deferredKeys = new Set();
            this._toArray(readiness.blockers).forEach(blocker => {
                if (blocker?.category === PLAN_BUILD_BLOCKER_CATEGORIES.CONTRACT_WARNING) return;
                const resource = blocker?.resource;
                const resourceId = String(resource?.canonicalId || resource?.CanonicalId || '').trim().toLowerCase();
                const field = this._inferPlanQuestionField?.(blocker?.field || blocker?.questionId || blocker?.id) ||
                    String(blocker?.field || blocker?.questionId || blocker?.id || '').trim().toLowerCase();
                const key = resourceId ? `resource:${resourceId}` : (field ? `field:${field}` : '');
                if (!key) return;
                (blocker?.blocksBuild === true ? blockingKeys : deferredKeys).add(key);
            });
            normalizedFields.forEach(field => {
                const key = `field:${field}`;
                if (!blockingKeys.has(key) && !deferredKeys.has(key)) {
                    (readiness.canBuild === true ? deferredKeys : blockingKeys).add(key);
                }
            });
            blockingKeys.forEach(key => deferredKeys.delete(key));
            mustConfirmCount = blockingKeys.size;
            fillLaterCount = deferredKeys.size;
            totalCount = mustConfirmCount + fillLaterCount;
        }

        return {
            totalCount,
            mustConfirmCount,
            fillLaterCount,
            missingFields: normalizedFields,
            stats,
            summaryText: `总计 ${totalCount} 项；构建前必须确认 ${mustConfirmCount} 项；可构建后补齐 ${fillLaterCount} 项`
        };
    },

    _hasOnlyDraftableResourceBlockers(plan) {
        const mode = this._normalizeRequirementMode?.(plan?.requirementMode || this.requirementMode || 'strict') || 'strict';
        if (mode !== 'draft') return false;
        const blockers = this._toArray(plan?.buildReadiness?.blockers);
        if (!blockers.length) return false;
        return blockers.some(blocker => blocker?.category === PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING) &&
            blockers.every(blocker => blocker?.blocksBuild !== true ||
                blocker?.category === PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING);
    },

    _getPlanBuildActionState(plan) {
        if (!plan) {
            return {
                canBuild: false,
                canAcceptRecommended: false,
                canStart: false,
                acceptedRecommended: false,
                label: '开始构建',
                statusText: '请先完成规划'
            };
        }

        const mode = this._normalizeRequirementMode?.(this.requirementMode || plan.requirementMode || 'strict') || 'strict';
        const previewState = this.agentWorkspaceState?.readinessStatus || 'idle';
        if (previewState === 'validating') {
            return {
                canBuild: false,
                canAcceptRecommended: false,
                canStart: false,
                acceptedRecommended: false,
                label: '正在校验构建条件…',
                statusText: '正在校验构建条件…',
                stats: this._getPlanReadinessStats(plan)
            };
        }
        if (previewState === 'failed' || previewState === 'timeout') {
            const timedOut = previewState === 'timeout';
            return {
                canBuild: false,
                canAcceptRecommended: false,
                canStart: false,
                acceptedRecommended: false,
                label: timedOut ? '构建条件校验超时，请重试' : '构建条件校验失败，请重试',
                statusText: timedOut ? '构建条件校验超时，请重试' : '构建条件校验失败，请重试',
                stats: this._getPlanReadinessStats(plan)
            };
        }

        const preview = this._getCurrentCanonicalPreview?.(plan);
        if (!preview?.buildReadiness) {
            return {
                canBuild: false,
                canAcceptRecommended: false,
                canStart: false,
                acceptedRecommended: false,
                label: '尚未获得权威校验结果',
                statusText: '尚未获得与当前方案、答案版本和模式匹配的权威校验结果，请重试。',
                canRetryReadiness: true,
                stats: this._getPlanReadinessStats(plan)
            };
        }

        const readiness = preview.buildReadiness;
        const stats = {
            ...this._getPlanReadinessStats({ ...plan, buildReadiness: readiness }),
            pendingConfirmationCount: Number(preview.pendingConfirmationCount) || 0,
            resourcePendingCount: Number(preview.resourcePendingCount) || 0,
            hardBlockerCount: Number(preview.hardBlockerCount) || 0
        };
        const canBuild = readiness.canBuild === true;
        if (canBuild) {
            return {
                canBuild: true,
                canAcceptRecommended: false,
                canStart: true,
                acceptedRecommended: false,
                label: mode === 'draft' ? '按当前方案生成可编辑草稿' : '开始构建',
                statusText: mode === 'draft'
                    ? '可编辑草稿可先生成；当前不代表可部署。'
                    : '当前确认项已满足构建条件。',
                stats
            };
        }

        const missingSummary = this._buildPlanMissingSummary(plan);
        const deferredCount = this._toArray(preview.deferredQuestionIds).length;
        const blockedReason = this._getPlanBuildBlockedReason(plan);
        const userBlockedText = missingSummary.totalCount > 0
            ? `还需补充 ${missingSummary.totalCount} 项信息`
            : '暂不能构建';
        const statusText = `${missingSummary.summaryText}。${blockedReason || '暂不能构建。'}`;

        return {
            canBuild: false,
            canAcceptRecommended: false,
            canStart: false,
            acceptedRecommended: false,
            label: userBlockedText,
            statusText: mode === 'strict' && deferredCount > 0
                ? `${missingSummary.summaryText}。当前选择要求构建前确认，暂缓项仍会阻止构建。`
                : statusText,
            stats,
            missingSummary
        };
    },

    _hasRecommendedAnswersForAllBlockers(plan) {
        return Boolean(plan && this._allBlockingPlanQuestionsHaveRecommendations(plan));
    },

    _allBlockingPlanQuestionsHaveRecommendations(plan) {
        const readinessBlockers = this._toArray(plan?.buildReadiness?.blockers)
            .filter(blocker => blocker?.blocksBuild === true);
        if (readinessBlockers.length) {
            return readinessBlockers.every(blocker => this._toArray(plan?.questions)
                .some(question => {
                    const id = String(question?.id || '').trim();
                    const field = this._inferPlanQuestionFieldForQuestion(question, plan) ||
                        this._fallbackPlanQuestionField(question, id);
                    const matches = (blocker.questionId && id === blocker.questionId) ||
                        (blocker.field && field === blocker.field);
                    return matches && String(this._getQuestionRecommendedValue(question) || '').trim();
                }));
        }

        const mode = this._normalizeRequirementMode?.(plan?.requirementMode || this.requirementMode || 'strict') || 'strict';
        const remainingFields = this._getRemainingPlanFields(plan, { acceptedRecommended: false })
            .filter(field => this._isPlanBuildBlockingField(field, mode, plan?.requirementMaturity, {
                plan,
                semanticExtraction: plan?.semanticExtraction,
                route: plan?.route || plan?.recommendedRoute || plan?.RecommendedRoute
            }));
        if (!remainingFields.length) return false;
        return remainingFields.every(field => this._toArray(plan?.questions)
            .some(question => this._inferPlanQuestionFieldForQuestion(question, plan) === field &&
                String(this._getQuestionRecommendedValue(question) || '').trim()));
    },

    _inferPlanQuestionField(value) {
        const normalized = String(value || '').trim().toLowerCase();
        if (!normalized) return '';
        if (PLAN_CANONICAL_FIELDS.has(normalized)) return normalized;
        if (normalized.includes('inspection_object') ||
            normalized.includes('object_type') ||
            normalized.includes('inspection_target') ||
            normalized.includes('detection_target')) return PLAN_ANSWER_FIELDS.INSPECTION_OBJECT;
        if (normalized.includes('task_type') ||
            normalized.includes('task_category') ||
            normalized.includes('inspection_task') ||
            normalized.includes('detection_task') ||
            normalized.includes('visual_task') ||
            normalized.includes('medical_modality') ||
            normalized.includes('lesion_type')) return PLAN_ANSWER_FIELDS.TASK_TYPE;
        if (normalized.includes('image_source') ||
            normalized.includes('image_input') ||
            normalized.includes('input_source') ||
            normalized.includes('source_image') ||
            normalized.includes('camera_source')) return PLAN_ANSWER_FIELDS.IMAGE_SOURCE;
        if (normalized.includes('acceptance_criteria') ||
            normalized.includes('ok_ng') ||
            normalized.includes('ok_condition') ||
            normalized.includes('ng_condition') ||
            normalized.includes('judgment_rule') ||
            normalized.includes('result_rule')) return PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA;
        if (normalized.includes('output_target') ||
            normalized.includes('output_goal') ||
            normalized.includes('output_destination') ||
            normalized.includes('result_output') ||
            normalized.includes('local_result_payload') ||
            normalized.includes('structured_result') ||
            normalized.includes('business_system')) return PLAN_ANSWER_FIELDS.OUTPUT_TARGET;
        if (normalized.includes('algorithm_strategy') ||
            normalized.includes('model_or_rule_strategy') ||
            normalized.includes('classification_strategy')) return PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY;
        return PLAN_QUESTION_FIELD_BY_ID[normalized] || '';
    },

    _parsePlanBlockingReason(reason) {
        const normalized = String(reason || '').trim().toLowerCase();
        if (!normalized) return { kind: '', key: '', field: '' };
        const match = normalized.match(/^(hard_requirement|strategy_confirmation|resource_pending|contract_warning|safety_blocker):(.+)$/i);
        const kind = match?.[1]?.toLowerCase() || '';
        const rawKey = match?.[2] || normalized;
        const key = rawKey.replace(/_missing$/i, '').trim();
        return {
            kind,
            key,
            field: this._inferPlanQuestionField(key)
        };
    },

    _inferPlanQuestionFieldForQuestion(question, plan = this.pendingVisionPlan) {
        const id = String(question?.id || question?.Id || '').trim().toLowerCase();
        const field = String(question?.field || question?.Field || '').trim().toLowerCase();
        const direct = this._inferPlanQuestionField(field || id);
        if (direct) return direct;

        const ids = new Set([id, field].filter(Boolean));
        if (!ids.size) return '';

        const blockers = [
            ...this._toArray(plan?.blockingReasons || plan?.BlockingReasons),
            ...this._toArray(plan?.requirementMaturity?.blockingReasons || plan?.requirementMaturity?.BlockingReasons)
        ];
        for (const reason of blockers) {
            const parsed = this._parsePlanBlockingReason(reason);
            if (!parsed.key || !ids.has(parsed.key)) continue;
            if (parsed.field) return parsed.field;
        }

        return '';
    },

    _fallbackPlanQuestionField(question, questionId = '') {
        const rawField = String(question?.field || question?.Field || '').trim().toLowerCase();
        if (rawField) return rawField;
        return String(question?.id || question?.Id || questionId || '').trim().toLowerCase();
    },

    _normalizePlanAnswer(answer, fallbackQuestion = null) {
        const item = this._asObject?.(answer) || answer || {};
        const questionId = String(item.questionId || item.QuestionId || fallbackQuestion?.id || '').trim();
        const field = this._inferPlanQuestionField(item.field || item.Field || fallbackQuestion?.field || questionId) ||
            this._inferPlanQuestionFieldForQuestion(fallbackQuestion || { id: questionId, field: item.field || item.Field }, this.pendingVisionPlan) ||
            this._fallbackPlanQuestionField(fallbackQuestion || { id: questionId, field: item.field || item.Field }, questionId);
        const value = String(item.value || item.Value || '').trim();
        const origin = String(item.origin || item.Origin || PLAN_ANSWER_ORIGINS.EXPLICIT_USER_SELECTION).trim().toLowerCase();
        if (!field || !value || this._isPlanPlaceholderValue(value)) return null;
        const option = this._toArray(fallbackQuestion?.options || fallbackQuestion?.Options)
            .find(candidate => String(candidate?.value || candidate?.Value || '').trim() === value);
        if (option && !this._isResolveFieldOption(option)) return null;
        return {
            questionId,
            field,
            value,
            origin: Object.values(PLAN_ANSWER_ORIGINS).includes(origin)
                ? origin
                : PLAN_ANSWER_ORIGINS.EXPLICIT_USER_SELECTION
        };
    },

    _getPlanQuestionAnswerKey(answer, fallbackQuestion = null) {
        const normalized = this._normalizePlanAnswer(answer, fallbackQuestion);
        if (!normalized) return '';
        return normalized.questionId || `field:${normalized.field}`;
    },

    _getPlanAnswerForQuestion(question) {
        const id = String(question?.id || '').trim();
        const field = this._inferPlanQuestionFieldForQuestion(question) || this._fallbackPlanQuestionField(question, id);
        const answers = this.planQuestionAnswers || {};
        if (field && answers[field]) {
            return this._normalizePlanAnswer(answers[field], question);
        }
        if (id && answers[id]) {
            return this._normalizePlanAnswer(answers[id], question);
        }
        const fieldAnswer = Object.values(answers)
            .map(answer => this._normalizePlanAnswer(answer))
            .find(answer => answer?.field === field);
        if (fieldAnswer) return fieldAnswer;

        const legacyValue = id ? String((this.planQuestionSelections || {})[id] || '').trim() : '';
        return legacyValue
            ? this._normalizePlanAnswer({
                questionId: id,
                field,
                value: legacyValue,
                origin: PLAN_ANSWER_ORIGINS.EXPLICIT_USER_SELECTION
            }, question)
            : null;
    },

    _getPlanQuestionSelectedValue(question) {
        const id = String(question?.id || '').trim();
        const selected = id ? String((this.planQuestionSelections || {})[id] || '').trim() : '';
        if (this._isPlanQuestionSelectionAllowed(question, selected)) {
            return selected;
        }
        const answer = this._getPlanAnswerForQuestion(question);
        return answer?.value || '';
    },

    _isPlanQuestionSelectionAllowed(question, value) {
        const selected = String(value || '').trim();
        if (!selected) return false;
        if (this._toArray(question?.options).some(option => String(option?.value || '').trim() === selected)) {
            return true;
        }
        return !this._isPlanPlaceholderValue(selected);
    },

    _buildConfirmedPlanAnswers(plan, { acceptedRecommended = false } = {}) {
        const answers = Object.values(this.planQuestionAnswers || {})
            .map(answer => this._normalizePlanAnswer(answer))
            .filter(Boolean);
        const resolvedFields = new Set(answers.map(answer => answer.field));
        if (acceptedRecommended) {
            this._toArray(plan?.questions).forEach(question => {
                const field = this._inferPlanQuestionFieldForQuestion(question, plan) ||
                    this._fallbackPlanQuestionField(question);
                if (!field || resolvedFields.has(field)) return;
                const recommended = String(this._getQuestionRecommendedValue(question) || '').trim();
                if (!recommended || this._isPlanPlaceholderValue(recommended)) return;
                answers.push({
                    questionId: String(question.id || '').trim(),
                    field,
                    value: recommended,
                    origin: PLAN_ANSWER_ORIGINS.ACCEPTED_RECOMMENDED_DEFAULT
                });
                resolvedFields.add(field);
            });
        }

        return answers
            .filter(answer => answer.field && answer.value)
            .sort((a, b) => a.field.localeCompare(b.field) || a.questionId.localeCompare(b.questionId));
    },

    _acceptRecommendedPlanAnswers(plan) {
        if (!plan) return false;
        if (this._isWorkspaceMutationBlocked?.()) {
            return false;
        }
        const nextAnswers = { ...(this.planQuestionAnswers || {}) };
        const nextSelections = { ...(this.planQuestionSelections || {}) };
        let changed = false;
        const resolvedFields = new Set(Object.values(nextAnswers)
            .map(answer => this._normalizePlanAnswer(answer))
            .filter(Boolean)
            .map(answer => answer.field));

        this._toArray(plan.questions).forEach(question => {
            const id = String(question?.id || '').trim();
            const field = this._inferPlanQuestionFieldForQuestion(question, plan) ||
                this._fallbackPlanQuestionField(question, id);
            if (!field || resolvedFields.has(field)) return;
            const recommended = String(this._getQuestionRecommendedValue(question) || '').trim();
            if (!recommended || this._isPlanPlaceholderValue(recommended)) return;
            const answer = {
                questionId: id,
                field,
                value: recommended,
                origin: PLAN_ANSWER_ORIGINS.ACCEPTED_RECOMMENDED_DEFAULT
            };
            nextAnswers[field] = answer;
            if (id) nextSelections[id] = recommended;
            resolvedFields.add(field);
            changed = true;
        });

        if (changed) {
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.ANSWERS_REPLACED,
                payload: { answers: nextAnswers, selections: nextSelections },
                planId: this.agentWorkspaceState?.identity?.planId,
                planHash: this.agentWorkspaceState?.identity?.planHash
            });
            this.planAcceptedRecommendedDefaults = true;
            this._queueWorkspaceSnapshotFlush?.('accept_recommended');
            this._requestPlanReadinessPreview?.(plan, { acceptedRecommended: true, reason: 'accepted_recommended' });
        }

        return changed;
    },

    _getResolvedPlanFields(plan, { acceptedRecommended = false, questions = null } = {}) {
        return [...new Set(this._buildConfirmedPlanAnswers(
            plan || { questions: questions || [] },
            { acceptedRecommended }
        ).map(answer => answer.field))];
    },

    _getRemainingPlanFields(plan, {
        acceptedRecommended = false,
        requirementMaturity = null,
        blockingReasons = null,
        questions = null
    } = {}) {
        const maturity = requirementMaturity || plan?.requirementMaturity || null;
        const reasons = blockingReasons || plan?.blockingReasons || [];
        const resolved = new Set(this._getResolvedPlanFields(plan, {
            acceptedRecommended,
            questions: questions || plan?.questions || []
        }));
        const fields = [
            ...this._toArray(plan?.remainingPlanFields || plan?.RemainingPlanFields),
            ...this._toArray(maturity?.missingFields || maturity?.MissingFields),
            ...this._toArray(reasons).map(reason => this._normalizePlanBlockingField(reason))
        ]
            .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
            .filter(field => PLAN_CANONICAL_FIELDS.has(field))
            .filter(field => !resolved.has(field));
        return [...new Set(fields)];
    },

    _normalizePlanBlockingField(reason) {
        const normalized = String(reason || '').trim().toLowerCase();
        if (!normalized) return '';
        const parsed = this._parsePlanBlockingReason(reason);
        if (parsed.field) return parsed.field;
        const key = parsed.key || normalized;
        if (key.includes('inspection_object')) return PLAN_ANSWER_FIELDS.INSPECTION_OBJECT;
        if (key.includes('task_type')) return PLAN_ANSWER_FIELDS.TASK_TYPE;
        if (key.includes('image_source')) return PLAN_ANSWER_FIELDS.IMAGE_SOURCE;
        if (key.includes('acceptance_criteria') || key.includes('condition')) {
            return PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA;
        }
        if (key.includes('strategy')) return PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY;
        return this._inferPlanQuestionField(key);
    },

    _getPlanBuildBlockedReason(plan) {
        if (!plan) return '请先完成规划';
        const readiness = plan.buildReadiness || null;
        const readinessBlocker = this._toArray(readiness?.blockers)
            .find(blocker => blocker?.blocksBuild === true);
        if (readinessBlocker?.publicLabel) {
            return readinessBlocker.publicLabel;
        }
        if (readiness?.primaryMessage) {
            return readiness.primaryMessage;
        }

        const strategyBlockers = this._getUnresolvedStrategyBlockers({
            blockingReasons: plan.blockingReasons,
            questions: plan.questions
        });
        if (strategyBlockers.length) {
            return '需确认策略后才能开始构建';
        }

        const hardBlocker = this._toArray(plan.blockingReasons)
            .map(reason => String(reason || '').trim())
            .find(reason => /^hard_requirement:/i.test(reason));
        if (hardBlocker) {
            return this._formatPlanHardBlockerTitle(hardBlocker);
        }

        return plan.requirementMaturity?.publicReason || '当前计划仍需澄清，暂不可构建';
    },

    _formatPlanHardBlockerTitle(reason) {
        const normalized = String(reason || '').toLowerCase();
        if (normalized.includes('image_source')) return '需补充图像来源后才能开始构建';
        if (normalized.includes('inspection_object')) return '需补充检测对象后才能开始构建';
        if (normalized.includes('task_type')) return '需补充检测任务类型后才能开始构建';
        if (normalized.includes('acceptance_criteria') || normalized.includes('condition')) {
            return '需补充 OK/NG 判定条件后才能开始构建';
        }
        if (normalized.includes('invalid_operator')) return '计划包含不支持算子，暂不可构建';
        return '当前计划仍有硬性缺口，暂不可构建';
    },

    _getUnresolvedStrategyBlockers({ blockingReasons = [], questions = [], acceptedRecommended = false } = {}) {
        const strategyBlockers = this._toArray(blockingReasons)
            .map(reason => String(reason || '').trim())
            .filter(reason => this._isRealStrategyConfirmationReason(reason, questions));
        if (!strategyBlockers.length) return [];

        const strategyQuestions = this._toArray(questions)
            .filter(question => this._isPlanStrategyQuestion(question));
        const hasExplicitStrategySelection = strategyQuestions.some(question =>
            this._normalizePlanStrategyChoice(this._getPlanQuestionSelectedValue(question)));
        if (hasExplicitStrategySelection) return [];

        if (acceptedRecommended) {
            const hasRecommendedStrategy = strategyQuestions.some(question =>
                this._normalizePlanStrategyChoice(this._getQuestionRecommendedValue(question)));
            if (hasRecommendedStrategy) return [];
        }

        return strategyBlockers.filter(reason => !this._isStrategyBlockerResolvedByQuestionAnswer(reason, {
            questions,
            acceptedRecommended
        }));
    },

    _isRealStrategyConfirmationReason(reason, questions = []) {
        const parsed = this._parsePlanBlockingReason(reason);
        if (parsed.kind !== PLAN_BUILD_BLOCKER_CATEGORIES.STRATEGY_CONFIRMATION) return false;
        if (parsed.field === PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY) return true;
        return this._toArray(questions).some(question => {
            const id = String(question?.id || question?.Id || '').trim().toLowerCase();
            if (id !== parsed.key) return false;
            const direct = this._inferPlanQuestionField(question?.field || question?.Field || id);
            return direct === PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY;
        });
    },

    _getUnresolvedPlanQuestionBlockers({
        blockingReasons = [],
        questions = [],
        acceptedRecommended = false,
        plan = null,
        semanticExtraction = null,
        route = null
    } = {}) {
        const questionList = this._toArray(questions);
        if (!questionList.length) return [];

        return this._toArray(blockingReasons)
            .map(reason => String(reason || '').trim())
            .filter(Boolean)
            .filter(reason => {
                const parsed = this._parsePlanBlockingReason(reason);
                if (![
                    PLAN_BUILD_BLOCKER_CATEGORIES.HARD_REQUIREMENT,
                    PLAN_BUILD_BLOCKER_CATEGORIES.STRATEGY_CONFIRMATION,
                    ''
                ].includes(parsed.kind)) {
                    return false;
                }
                if (parsed.key === 'planner_candidate_not_buildable') return false;
                if (this._isRealStrategyConfirmationReason(reason, questionList)) return false;

                const field = parsed.field || this._normalizePlanBlockingField(reason);
                if (field === PLAN_ANSWER_FIELDS.OUTPUT_TARGET &&
                    !this._planRequestsExternalOutput(plan, reason, semanticExtraction, route)) {
                    return false;
                }

                const question = this._findPlanQuestionForBlocker(reason, questionList);
                if (!question) return false;
                return !this._isPlanQuestionBlockerResolved(reason, question, { acceptedRecommended });
            });
    },

    _findPlanQuestionForBlocker(reason, questions = []) {
        const parsed = this._parsePlanBlockingReason(reason);
        const field = parsed.field || this._normalizePlanBlockingField(reason);
        return this._toArray(questions).find(question => {
            const id = String(question?.id || question?.Id || '').trim().toLowerCase();
            const qField = this._inferPlanQuestionFieldForQuestion(question, { blockingReasons: [] }) ||
                this._fallbackPlanQuestionField(question, id);
            return id === parsed.key || (field && qField === field);
        }) || null;
    },

    _isPlanQuestionBlockerResolved(reason, question, { acceptedRecommended = false } = {}) {
        const parsed = this._parsePlanBlockingReason(reason);
        const key = parsed.key;
        const blockerField = parsed.field || this._normalizePlanBlockingField(reason);
        const questionId = String(question?.id || question?.Id || '').trim().toLowerCase();
        const questionField = this._inferPlanQuestionFieldForQuestion(question, { blockingReasons: [] }) ||
            this._fallbackPlanQuestionField(question, questionId);
        const answers = Object.values(this.planQuestionAnswers || {})
            .map(answer => this._normalizePlanAnswer(answer, question))
            .filter(Boolean);
        if (answers.some(answer =>
            String(answer.questionId || '').trim().toLowerCase() === key ||
            String(answer.questionId || '').trim().toLowerCase() === questionId ||
            (blockerField && answer.field === blockerField) ||
            (questionField && answer.field === questionField))) {
            return true;
        }

        if (this._normalizePlanAnswer(this._getPlanAnswerForQuestion(question), question)) {
            return true;
        }

        return acceptedRecommended &&
            Boolean(String(this._getQuestionRecommendedValue(question) || '').trim());
    },

    _isStrategyBlockerResolvedByQuestionAnswer(reason, { questions = [], acceptedRecommended = false } = {}) {
        const key = this._parsePlanBlockingReason(reason).key;
        if (!key) return false;

        const blockerField = this._inferPlanQuestionField(key);
        const answers = Object.values(this.planQuestionAnswers || {})
            .map(answer => this._normalizePlanAnswer(answer))
            .filter(Boolean);
        if (answers.some(answer =>
            String(answer.questionId || '').trim().toLowerCase() === key ||
            (blockerField && answer.field === blockerField))) {
            return true;
        }

        const matchingQuestion = this._toArray(questions)
            .find(question => {
                const id = String(question?.id || '').trim().toLowerCase();
                const field = this._inferPlanQuestionFieldForQuestion(question, { blockingReasons: [reason] });
                return id === key || (blockerField && field === blockerField);
            });
        if (!matchingQuestion) return false;

        if (this._normalizePlanAnswer(this._getPlanAnswerForQuestion(matchingQuestion), matchingQuestion)) {
            return true;
        }

        return acceptedRecommended &&
            Boolean(String(this._getQuestionRecommendedValue(matchingQuestion) || '').trim());
    },

    _isPlanStrategyQuestion(question) {
        return this._inferPlanQuestionFieldForQuestion(question) === PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY;
    },

    _isPlanStrategyQuestionId(id) {
        return this._inferPlanQuestionField(id) === PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY;
    },

    _getQuestionRecommendedValue(question) {
        const recommendedOption = this._toArray(question?.options)
            .find(option => option?.recommended === true &&
                this._isResolveFieldOption(option) &&
                !this._isPlanPlaceholderValue(option?.value));
        const value = recommendedOption?.value || '';
        return this._isPlanPlaceholderValue(value) ? '' : value;
    },

    _normalizePlanStrategyChoice(value) {
        const normalized = String(value || '')
            .trim()
            .toLowerCase()
            .replace(/[-\s]+/g, '_')
            .replace(/_+/g, '_');
        if (!normalized) return '';
        if (['deep_learning', 'deeplearning', 'model', 'ai', 'model_strategy', 'classification_model', 'model_classification'].includes(normalized)) {
            return 'deep_learning';
        }
        if (['traditional_rule', 'traditional', 'rule', 'rule_based', 'classic_rule', 'threshold_rule', 'numeric_rule'].includes(normalized)) {
            return 'traditional_rule';
        }
        if (['template', 'template_strategy', 'catalog_template', 'selected_template'].includes(normalized)) {
            return 'template';
        }
        if (['planner_route', 'planner', 'recommended', 'use_planner_route'].includes(normalized)) {
            return 'planner_route';
        }
        return '';
    },

    _planHardFactsReady(requirementMaturity, semanticExtraction, route = null, resolvedFields = new Set(), requirementMode = 'strict') {
        const mode = this._normalizeRequirementMode?.(requirementMode || this.requirementMode || 'strict') || 'strict';
        const semanticFailureCode = String(semanticExtraction?.failureCode || semanticExtraction?.FailureCode || '').trim();
        if (semanticExtraction && !semanticFailureCode) {
            const hasObject = Boolean(String(semanticExtraction.inspectionObject || '').trim()) ||
                resolvedFields.has(PLAN_ANSWER_FIELDS.INSPECTION_OBJECT);
            const task = String(semanticExtraction.taskType || '').trim().toLowerCase();
            const hasTask = Boolean(task && task !== 'unknown' && task !== 'abstract_goal') ||
                resolvedFields.has(PLAN_ANSWER_FIELDS.TASK_TYPE);
            const hasInput = Boolean(String(semanticExtraction.imageSource || '').trim()) ||
                resolvedFields.has(PLAN_ANSWER_FIELDS.IMAGE_SOURCE) ||
                this._routeAllowsPendingImageSource(route);
            const hasJudgment = Boolean(String(
                semanticExtraction.okCondition ||
                semanticExtraction.ngCondition ||
                semanticExtraction.outputTarget ||
                ''
            ).trim()) || resolvedFields.has(PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA);
            if (mode === 'draft') {
                return (hasObject || hasTask) && this._planRouteSatisfiesBuildStrategy(route);
            }

            return hasObject && hasTask && hasInput && hasJudgment;
        }

        if (!requirementMaturity) return false;
        const hardMissing = this._toArray(requirementMaturity.missingFields).some(field => {
            const normalized = this._normalizePlanBlockingField(field);
            return this._isPlanBuildBlockingField(normalized, mode, requirementMaturity, {
                semanticExtraction,
                route
            }) && !resolvedFields.has(normalized);
        });
        const hardBlocked = this._toArray(requirementMaturity.blockingReasons).some(reason => {
            const normalized = this._normalizePlanBlockingField(reason);
            if (/abstract_goal|empty_requirement/i.test(String(reason || ''))) return true;
            return this._isPlanBuildBlockingField(normalized, mode, requirementMaturity, {
                reason,
                semanticExtraction,
                route
            }) && !resolvedFields.has(normalized);
        });
        const hardFacts = mode === 'draft'
            ? [
                PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
                PLAN_ANSWER_FIELDS.TASK_TYPE
            ]
            : [
            PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
            PLAN_ANSWER_FIELDS.TASK_TYPE,
            PLAN_ANSWER_FIELDS.IMAGE_SOURCE,
            PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA
        ];
        const answeredHardFacts = mode === 'draft'
            ? hardFacts.some(field => resolvedFields.has(field)) || requirementMaturity.canPlan === true
            : hardFacts.every(field => resolvedFields.has(field));
        return (requirementMaturity.canBuild === true ||
                (requirementMaturity.canPlan === true && answeredHardFacts)) &&
            !hardMissing &&
            !hardBlocked;
    },

    _routeAllowsPendingImageSource(route) {
        return this._toArray(route?.operators || route?.Operators)
            .some(op => /^imageacquisition$/i.test(String(op || '').trim()));
    },

    _isDraftableImageSourceBlockingReason(reason, route, requirementMode = 'strict') {
        return /^hard_requirement:.*image_source/i.test(String(reason || '').trim()) &&
            (this._normalizeRequirementMode?.(requirementMode || this.requirementMode || 'strict') || 'strict') === 'draft' &&
            this._routeAllowsPendingImageSource(route);
    },

    _planRouteSatisfiesBuildStrategy(route) {
        const forbidden = new Set(['modbuscommunication', 'httprequest', 'scriptoperator']);
        const operators = this._toArray(route?.operators || route?.Operators)
            .map(op => String(op || '').trim())
            .filter(Boolean);
        if (!operators.length) return false;
        if (operators.some(op => forbidden.has(op.toLowerCase()))) return false;
        const hasWorkOperator = operators.some(op =>
            !/^imageacquisition$/i.test(op) &&
            !/^resultoutput$/i.test(op));
        const hasResultOutput = operators.some(op => /^resultoutput$/i.test(op));
        return hasWorkOperator && hasResultOutput;
    },

    _refreshPlanEffectiveBuildReadiness(plan, { acceptedRecommended = false } = {}) {
        if (!plan) return false;
        const canonicalPreview = this._getCurrentCanonicalPreview?.(plan);
        return canonicalPreview?.buildReadiness?.canBuild === true;
    },

    _normalizeSemanticExtraction(value) {
        const item = this._asObject?.(value) || value || null;
        if (!item || typeof item !== 'object') return null;
        const source = String(item.source || item.Source || '').trim();
        const taskType = String(item.taskType || item.TaskType || '').trim();
        const missingFields = this._toArray(item.missingFields || item.MissingFields)
            .map(field => String(field || '').trim())
            .filter(Boolean);
        return {
            isVisionRequest: (item.isVisionRequest ?? item.IsVisionRequest) === true,
            intent: String(item.intent || item.Intent || '').trim(),
            taskType,
            source,
            sourceLabel: this._formatSemanticSourceLabel?.(source) || source,
            confidence: Number(item.confidence ?? item.Confidence ?? 0) || 0,
            taskTypeConfidence: Number(item.taskTypeConfidence ?? item.TaskTypeConfidence ?? 0) || 0,
            inspectionObject: this._sanitizePlanDiagnosticText(item.inspectionObject || item.InspectionObject || '', 120),
            targetAttribute: this._sanitizePlanDiagnosticText(item.targetAttribute || item.TargetAttribute || '', 120),
            defectType: this._sanitizePlanDiagnosticText(item.defectType || item.DefectType || '', 120),
            measurementTarget: this._sanitizePlanDiagnosticText(item.measurementTarget || item.MeasurementTarget || '', 120),
            imageSource: this._sanitizePlanDiagnosticText(item.imageSource || item.ImageSource || '', 80),
            okCondition: this._sanitizePlanDiagnosticText(item.okCondition || item.OkCondition || '', 160),
            ngCondition: this._sanitizePlanDiagnosticText(item.ngCondition || item.NgCondition || '', 160),
            outputTarget: this._sanitizePlanDiagnosticText(item.outputTarget || item.OutputTarget || '', 120),
            suggestedRoute: this._sanitizePlanDiagnosticText(item.suggestedRoute || item.SuggestedRoute || '', 180),
            objectSignals: this._toArray(item.objectSignals || item.ObjectSignals).map(signal => this._sanitizePlanDiagnosticText(signal, 80)).filter(Boolean),
            taskSignals: this._toArray(item.taskSignals || item.TaskSignals).map(signal => this._sanitizePlanDiagnosticText(signal, 80)).filter(Boolean),
            missingFields,
            clarificationQuestions: this._toArray(item.clarificationQuestions || item.ClarificationQuestions)
                .map(question => this._sanitizePlanDiagnosticText(question, 160))
                .filter(Boolean),
            failureCode: this._sanitizePlanDiagnosticCode(item.failureCode || item.FailureCode || ''),
            sanitizedErrorMessage: this._sanitizePlanDiagnosticText(item.sanitizedErrorMessage || item.SanitizedErrorMessage || ''),
            metadataOnly: Boolean(item.metadataOnly ?? item.MetadataOnly)
        };
    },

    _normalizeRequirementMaturity(value) {
        const item = this._asObject?.(value) || value || null;
        if (!item || typeof item !== 'object') return null;
        const maturity = String(item.maturity || item.Maturity || '').trim();
        const taskType = String(item.taskType || item.TaskType || '').trim();
        const publicReason = this._sanitizePlanDisplayText(item.publicReason || item.PublicReason || '', 220);
        const missingFields = this._toArray(item.missingFields || item.MissingFields).map(field => String(field || '').trim()).filter(Boolean);
        const blockingReasons = this._toArray(item.blockingReasons || item.BlockingReasons).map(field => String(field || '').trim()).filter(Boolean);
        return {
            maturity,
            taskType,
            canPlan: (item.canPlan ?? item.CanPlan) === true,
            canBuild: (item.canBuild ?? item.CanBuild) === true,
            objectSignals: this._toArray(item.objectSignals || item.ObjectSignals).map(signal => this._sanitizePlanDisplayText(signal, 80)).filter(Boolean),
            taskSignals: this._toArray(item.taskSignals || item.TaskSignals).map(signal => this._sanitizePlanDisplayText(signal, 80)).filter(Boolean),
            missingFields,
            blockingReasons: blockingReasons.map(reason => this._sanitizePlanDisplayText(reason, 160)).filter(Boolean),
            publicReason,
            metadataOnly: Boolean(item.metadataOnly ?? item.MetadataOnly)
        };
    },

    _normalizeDecisionTrace(value) {
        const item = this._asObject?.(value) || value || null;
        if (!item || typeof item !== 'object') return null;
        return {
            rawUserText: String(item.rawUserText || item.RawUserText || '').trim(),
            turnIntent: String(item.turnIntent || item.TurnIntent || '').trim(),
            interactionState: String(item.interactionState || item.InteractionState || '').trim(),
            businessSignalsHit: this._toArray(item.businessSignalsHit || item.BusinessSignalsHit).map(String),
            newFlowSignalsHit: this._toArray(item.newFlowSignalsHit || item.NewFlowSignalsHit).map(String),
            taskTypeSignalsHit: this._toArray(item.taskTypeSignalsHit || item.TaskTypeSignalsHit).map(String),
            objectSignalsHit: this._toArray(item.objectSignalsHit || item.ObjectSignalsHit).map(String),
            maturityLevel: String(item.maturityLevel || item.MaturityLevel || '').trim(),
            taskType: String(item.taskType || item.TaskType || '').trim(),
            canPlan: (item.canPlan ?? item.CanPlan) === true,
            canBuild: (item.canBuild ?? item.CanBuild) === true,
            fallbackReason: this._sanitizePlanDiagnosticCode(item.fallbackReason || item.FallbackReason || ''),
            blockingReasons: this._toArray(item.blockingReasons || item.BlockingReasons).map(reason => this._sanitizePlanDisplayText(reason, 160)).filter(Boolean),
            metadataOnly: Boolean(item.metadataOnly ?? item.MetadataOnly)
        };
    },

    _normalizePlanPublicEvent(evt) {
        const item = this._asObject?.(evt) || evt || {};
        const rawMetadata = item.metadata || item.Metadata || {};
        const metadata = {};
        if (rawMetadata && typeof rawMetadata === 'object') {
            Object.entries(rawMetadata).forEach(([key, value]) => {
                const safeKey = this._sanitizePlanDiagnosticCode(key);
                if (!safeKey) return;
                metadata[safeKey] = this._sanitizePlanDiagnosticText(value);
            });
        }

        return {
            stage: this._sanitizePlanDiagnosticCode(item.stage || item.Stage || ''),
            status: this._sanitizePlanDiagnosticCode(item.status || item.Status || ''),
            title: this._sanitizePlanDiagnosticText(item.title || item.Title || ''),
            summary: this._sanitizePlanDiagnosticText(item.summary || item.Summary || ''),
            metadata
        };
    },

    _normalizePlannerFailureDiagnostics(plan, publicEvents = []) {
        const item = this._asObject?.(plan) || plan || {};
        const metadataSources = publicEvents
            .map(evt => evt?.metadata || {})
            .filter(metadata => metadata && typeof metadata === 'object');
        const read = (...names) => {
            for (const name of names) {
                const direct = item[name] ?? item[this._capitalizeFirst(name)];
                if (direct !== undefined && direct !== null && String(direct).trim()) {
                    return direct;
                }

                for (const metadata of metadataSources) {
                    const matchedKey = Object.keys(metadata).find(key => key.toLowerCase() === String(name).toLowerCase());
                    if (matchedKey && String(metadata[matchedKey] ?? '').trim()) {
                        return metadata[matchedKey];
                    }
                }
            }

            return '';
        };
        const stage = this._sanitizePlanDiagnosticCode(read('plannerFailureStage'));
        const code = this._sanitizePlanDiagnosticCode(read('plannerFailureCode'));
        const kind = this._sanitizePlanDiagnosticCode(read('sanitizedErrorKind')) || code;
        const message = this._sanitizePlanDiagnosticText(read('sanitizedErrorMessage'));
        return {
            stage,
            stageLabel: this._formatPlannerFailureStage(stage, code),
            code,
            kind,
            message,
            hint: this._formatPlannerFailureHint(code)
        };
    },

    _capitalizeFirst(value) {
        const text = String(value || '');
        return text ? `${text[0].toUpperCase()}${text.slice(1)}` : text;
    },

    _sanitizePlanDiagnosticCode(value) {
        const text = String(value ?? '').trim();
        if (!text) return '';
        const redacted = this._sanitizePlanDiagnosticText(text, 80);
        return redacted
            .replace(/[^A-Za-z0-9_.:-]/g, '')
            .slice(0, 80);
    },

    _sanitizePlanDiagnosticText(value, maxChars = 200) {
        let text = String(value ?? '').trim();
        if (!text) return '';
        text = this._redactPublicDiagnosticText?.(text) || text;
        text = text
            .replace(/\b(?:rawPrompt|systemPrompt|userPrompt|chainOfThought|chain_of_thought|reasoningContent|reasoning_content)\b\s*[:=]\s*["']?[^"'\n,;}]+/gi, '[redacted]')
            .replace(/chain[-_\s]?of[-_\s]?thought/gi, '[redacted]')
            .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]{8,}/gi, '[redacted]')
            .replace(/\b(?:authorization|x-api-key|api[-_ ]?key|apiKey|token|secret|baseUrl|base_url|headers?)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '[redacted]')
            .replace(/\bhttps?:\/\/[^\s"'<>|]+/gi, '[redacted]')
            .replace(/\bsk-[A-Za-z0-9_-]{8,}/gi, '[redacted]')
            .replace(/\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?::\d+)?\b/g, '[redacted]')
            .replace(/\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b/gi, '[redacted]')
            .replace(/\bM\d+(?:\.\d+)?\b/gi, '[redacted]')
            .replace(/\bD\d+\b/gi, '[redacted]')
            .replace(/plc:\/\/[^\s"'<>|]+/gi, '[redacted]')
            .replace(/(?:[a-z]:\\|\\\\)[^\s"'<>|]+/gi, '[redacted]')
            .replace(/(?:\/users\/|\/home\/|\/var\/|\/tmp\/|\/mnt\/|\/data\/|\/models\/|\/artifacts\/)[^\s"'<>|]+/gi, '[redacted]')
            .replace(/data:image\/[a-z0-9.+-]+;base64,[a-z0-9+/=\r\n]+/gi, '[redacted]')
            .replace(/(?<![a-z0-9+/=])(?:[a-z0-9+/]{96,}={0,2})(?![a-z0-9+/=])/gi, '[redacted]');
        return text.slice(0, maxChars);
    },

    _sanitizePlanDisplayText(value, maxChars = 200) {
        return this._sanitizePlanDiagnosticText(this._localizeDisplayText(value), maxChars);
    },

    _sanitizeBuildWorkspaceText(value, maxChars = 220) {
        const text = String(value ?? '').trim();
        if (!text) return '';
        return this._sanitizeAssistantFailureText?.(text, maxChars) ||
            this._sanitizePlanDiagnosticText?.(text, maxChars) ||
            text.slice(0, maxChars);
    },

    _sanitizeDraftCanvasLabel(value, fallback = '', maxChars = 160) {
        const safe = this._sanitizeBuildWorkspaceText?.(value, maxChars) || '';
        if (safe) return safe;
        const fallbackText = String(fallback ?? '').trim();
        return fallbackText && fallbackText !== String(value ?? '').trim()
            ? (this._sanitizeBuildWorkspaceText?.(fallbackText, maxChars) || '')
            : '';
    },

    _normalizeDraftCanvasOperatorType(value, fallback = 'DeepLearning') {
        const raw = String(value ?? '').trim();
        if (!raw) return fallback;

        // Older agent-run payloads serialized OperatorType as its numeric .NET enum value.
        // The caller recovers those values from the canonical operator name instead.
        if (/^-?\d+$/.test(raw)) return fallback;

        const knownTypes = Object.keys(AI_OPERATOR_LABELS);
        const exact = knownTypes.find(type => type.toLowerCase() === raw.toLowerCase());
        if (exact) return exact;

        const embedded = knownTypes.find(type => new RegExp(`\\b${type}\\b`, 'i').test(raw));
        if (embedded) return embedded;

        const safe = this._sanitizeDraftCanvasLabel(raw, '', 80);
        return safe && !/[\[<]redacted[\]>]/i.test(safe) ? safe : fallback;
    },

    _inferDraftCanvasOperatorTypeFromLabel(value) {
        const raw = String(value ?? '').trim();
        if (!raw) return '';

        const knownTypes = Object.keys(AI_OPERATOR_LABELS);
        const exact = knownTypes.find(type => type.toLowerCase() === raw.toLowerCase());
        if (exact) return exact;

        for (let index = knownTypes.length - 1; index >= 0; index -= 1) {
            const type = knownTypes[index];
            if (String(AI_OPERATOR_LABELS[type] || '').trim().toLowerCase() === raw.toLowerCase()) {
                return type;
            }
        }

        return '';
    },

    _normalizeDraftCanvasDataType(value, fallback = 'string') {
        const legacyType = LEGACY_PORT_DATA_TYPES[String(value ?? '').trim()];
        if (legacyType) return legacyType;

        const safe = this._sanitizeDraftCanvasLabel(value || fallback, fallback, 80);
        return safe && !/[\[<]redacted[\]>]/i.test(safe) ? safe : fallback;
    },

    _formatBuildWorkspaceText(value, maxChars = 220) {
        const text = this._sanitizeBuildWorkspaceText(value, maxChars);
        return text ? this._localizeDisplayText(text) : '';
    },

    _formatPlannerFailureStage(stage, code = '') {
        const normalized = String(stage || '').trim().toLowerCase();
        const failureCode = String(code || '').trim().toLowerCase();
        if (failureCode === 'planner_timeout') return '模型规划超时';
        if (failureCode === 'planner_unauthorized') return '模型鉴权失败';
        if (normalized === 'completion_request') return '模型请求失败';
        if (normalized === 'completion_response') return '模型返回为空';
        if (normalized === 'json_parse') return 'JSON 解析失败';
        if (normalized === 'contract_repair') return '契约修复失败';
        return normalized ? this._localizeDisplayText(normalized) : '';
    },

    _formatPlannerFailureHint(code) {
        switch (String(code || '').trim().toLowerCase()) {
            case 'planner_unauthorized':
                return '请检查 Planner API Key、模型名和接口配置。';
            case 'planner_json_parse_failed':
                return '请检查 Planner 模型是否按 PlanModeResult JSON 契约输出。';
            case 'completion_request_failed':
                return '请检查网络、Planner 接口地址配置、模型服务和中转站状态。';
            case 'completion_empty':
                return '请检查 Planner 模型返回内容和中转站响应体。';
            case 'planner_contract_repair_failed':
                return '请检查 Planner 输出字段是否满足 PlanModeResult 契约。';
            default:
                return '';
        }
    },

    _normalizePlanQuestion(question) {
        if (!question) return null;
        const options = this._toArray(question.options || question.Options)
            .map(option => this._normalizePlanOption(option))
            .filter(Boolean);
        const rawDefault = String(question.defaultValue || question.DefaultValue || '').trim();
        const defaultValue = rawDefault;
        return {
            id: question.id || question.Id || '',
            field: this._inferPlanQuestionField(question.field || question.Field || question.id || question.Id),
            title: this._sanitizePlanDisplayText(question.title || question.Title || '', 160),
            why: this._sanitizePlanDisplayText(question.why || question.Why || '', 220),
            defaultValue: defaultValue || options.find(item => item.recommended)?.value || options[0]?.value || '',
            defaultAssumption: this._sanitizePlanDisplayText(question.defaultAssumption || question.DefaultAssumption || '', 220),
            impact: this._sanitizePlanDisplayText(question.impact || question.Impact || '', 220),
            options
        };
    },

    _normalizePlanOption(option) {
        if (!option) return null;
        const value = option.value || option.Value || '';
        const answerEffect = this._normalizePlanAnswerEffect(option.answerEffect || option.AnswerEffect, value);
        return {
            value,
            label: this._sanitizePlanDisplayText(option.label || option.Label || value || '', 160),
            recommended: Boolean(option.recommended ?? option.Recommended),
            answerEffect,
            recommendationReason: this._sanitizePlanDiagnosticText(option.recommendationReason || option.RecommendationReason || '', 160),
            description: this._sanitizePlanDisplayText(option.description || option.Description || '', 220),
            impact: this._sanitizePlanDisplayText(option.impact || option.Impact || '', 220)
        };
    },

    _normalizePlanAnswerEffect(effect, value = '') {
        const normalized = String(effect || '').trim().toLowerCase();
        if (Object.values(PLAN_ANSWER_EFFECTS).includes(normalized)) return normalized;
        return this._isPlanPlaceholderValue(value)
            ? PLAN_ANSWER_EFFECTS.DEFER
            : PLAN_ANSWER_EFFECTS.RESOLVE_FIELD;
    },

    _isResolveFieldOption(option) {
        return this._normalizePlanAnswerEffect(option?.answerEffect || option?.AnswerEffect, option?.value || option?.Value) === PLAN_ANSWER_EFFECTS.RESOLVE_FIELD;
    },

    _isDeferOption(option) {
        return this._normalizePlanAnswerEffect(option?.answerEffect || option?.AnswerEffect, option?.value || option?.Value) === PLAN_ANSWER_EFFECTS.DEFER;
    },

    _isInformationalOption(option) {
        return this._normalizePlanAnswerEffect(option?.answerEffect || option?.AnswerEffect, option?.value || option?.Value) === PLAN_ANSWER_EFFECTS.INFORMATIONAL;
    },

    _normalizePlanDefault(item) {
        if (!item) return null;
        return {
            id: item.id || item.Id || '',
            label: this._sanitizePlanDisplayText(item.label || item.Label || '', 120),
            value: this._sanitizePlanDisplayText(item.value || item.Value || '', 160),
            impact: this._sanitizePlanDisplayText(item.impact || item.Impact || '', 180)
        };
    },

    _toArray(value) {
        return Array.isArray(value) ? value : [];
    },

    _isPlanPlaceholderValue(value) {
        const normalized = String(value || '').trim().toLowerCase();
        if (!normalized) return false;
        return ['custom_input', 'unknown', 'unspecified', 'metadata_only', 'pending'].includes(normalized) ||
            normalized.endsWith('_pending');
    },

    _localizeDisplayText(value) {
        const text = String(value ?? '').trim();
        if (!text) return '';
        if (AI_DISPLAY_TEXT_MAP[text]) return AI_DISPLAY_TEXT_MAP[text];

        const codeKey = text.toLowerCase();
        if (AI_CODE_TEXT_MAP[codeKey]) return AI_CODE_TEXT_MAP[codeKey];
        if (BUILD_STAGE_LABELS[text]) return BUILD_STAGE_LABELS[text];
        if (AI_OPERATOR_LABELS[text]) return AI_OPERATOR_LABELS[text];
        if (AI_PARAMETER_LABELS[text]) return AI_PARAMETER_LABELS[text];

        const evidenceMatch = text.match(/^([a-z][a-z0-9_]*) completed with metadata-only public evidence\.$/i);
        if (evidenceMatch) {
            const stageLabel = BUILD_STAGE_LABELS[evidenceMatch[1]] || this._localizeDisplayText(evidenceMatch[1]);
            return `${stageLabel}已完成，已生成公开元数据证据。`;
        }

        return text
            .replace(/\bImageAcquisition\b/g, AI_OPERATOR_LABELS.ImageAcquisition)
            .replace(/\bSurfaceDefectDetection\b/g, AI_OPERATOR_LABELS.SurfaceDefectDetection)
            .replace(/\bBlobAnalysis\b/g, AI_OPERATOR_LABELS.BlobAnalysis)
            .replace(/\bDeepLearning\b/g, AI_OPERATOR_LABELS.DeepLearning)
            .replace(/\bTemplateMatching\b/g, AI_OPERATOR_LABELS.TemplateMatching)
            .replace(/\bCircleMeasurement\b/g, AI_OPERATOR_LABELS.CircleMeasurement)
            .replace(/\bMeasurement\b/g, AI_OPERATOR_LABELS.Measurement)
            .replace(/\bMeasureDistance\b/g, AI_OPERATOR_LABELS.MeasureDistance)
            .replace(/\bUnitConvert\b/g, AI_OPERATOR_LABELS.UnitConvert)
            .replace(/\bDetectionSequenceJudge\b/g, AI_OPERATOR_LABELS.DetectionSequenceJudge)
            .replace(/\bImageAdd\b/g, AI_OPERATOR_LABELS.ImageAdd)
            .replace(/\bResultJudgment\b/g, AI_OPERATOR_LABELS.ResultJudgment)
            .replace(/\bResultOutput\b/g, AI_OPERATOR_LABELS.ResultOutput)
            .replace(/\bModelId\b|\bModelPath\b|\bModelCatalogPath\b/g, '模型资源')
            .replace(/\bTemplate\b|\bTemplatePath\b|\bTemplateId\b/g, '模板资源')
            .replace(/\bTolerance\b/g, '容差阈值')
            .replace(/\bRule\b|\bCondition\b/g, '判定规则')
            .replace(/\bFieldName\b/g, '判定字段')
            .replace(/\bExpectedLabels\b/g, '期望标签')
            .replace(/\bUnit\b|\bPixelScale\b|\bScale\b/g, '测量单位/像素比例')
            .replace(/\bmodel_resource\b/g, '模型资源')
            .replace(/\btemplate_artifact\b/g, '模板资源')
            .replace(/\bmeasurement_parameter\b/g, '测量参数')
            .replace(/\bcamera_binding\b/g, '相机绑定')
            .replace(/\boutput_channel\b/g, '输出通道')
            .replace(/\bplc_address\b/g, 'PLC 地址')
            .replace(/<pending-model-resource>/g, '<待绑定模型资源>')
            .replace(/<pending-output-channel>/g, '<待绑定输出通道>')
            .replace(/<pending-pixel-to-world-scale>/g, '<待填写像素比例>')
            .replace(/<pending-wire-sequence-labels>/g, '<待填写线序标签>')
            .replace(/metadata-only/gi, '仅元数据')
            .replace(/\bpending\b/g, '待确认')
            .replace(/\bcompleted\b/g, '已完成')
            .replace(/\bblocked\b/g, '已阻断');
    },

    _formatPlanSource(value) {
        return this._localizeDisplayText(value || '未设置') || '未设置';
    },

    _formatPlanFallbackReason(value) {
        return this._localizeDisplayText(value);
    },

    _formatRequirementMaturityLabel(value) {
        switch (String(value || '').trim()) {
            case 'abstract_goal':
                return '抽象目标';
            case 'ambiguous':
                return '需求不完整';
            case 'actionable':
                return '可构建';
            case 'chat_or_help':
                return '对话/帮助';
            case 'modify_existing_flow':
                return '修改当前流程';
            default:
                return this._localizeDisplayText(value) || '未评估';
        }
    },

    _formatRequirementTaskTypeLabel(value) {
        switch (String(value || '').trim()) {
            case 'surface_defect':
            case 'surface_or_pose_defect':
                return '缺陷/贴附/位姿';
            case 'geometry_measurement':
                return '几何测量';
            case 'wire_sequence':
                return '线序检测';
            case 'code_recognition':
            case 'barcode_qr':
                return '读码/OCR';
            case 'presence_absence':
                return '有无/漏装';
            case 'classification':
                return '分类识别';
            case 'attribute_classification':
                return '属性分类 / OK-NG 判别';
            case 'template_location':
                return '模板定位';
            case 'plc_output':
                return 'PLC 输出';
            case 'abstract_goal':
                return '方案愿景';
            case 'unknown':
                return '未识别';
            default:
                return this._localizeDisplayText(value) || '未识别';
        }
    },

    _formatRequirementFieldLabel(value) {
        switch (String(value || '').trim()) {
            case 'inspection_object':
                return '检测对象';
            case 'task_type':
                return '任务类型';
            case 'image_source':
                return '图像来源';
            case 'acceptance_criteria':
                return '判定标准';
            case 'output_target':
                return '输出目标';
            default:
                return this._localizeDisplayText(value) || value;
        }
    },

    _formatPlanEvent(evt = {}) {
        const stage = evt.stage || evt.Stage || '';
        const status = evt.status || evt.Status || '';
        const stageLabel = BUILD_STAGE_LABELS[stage] || this._localizeDisplayText(stage);
        const statusLabel = this._formatBuildStatus(status);
        const rawSummary = evt.summary || evt.Summary || evt.title || evt.Title || '';
        const codedSummary = this._localizeDisplayText(`${stage} ${status}`);
        const summary = codedSummary && codedSummary !== `${stage} ${status}`
            ? codedSummary
            : (this._localizeDisplayText(rawSummary) || `${stageLabel}${statusLabel}`);
        return { stageLabel, statusLabel, summary };
    },

    _formatToolName(value) {
        const text = this._sanitizeBuildWorkspaceText?.(value, 80) || String(value || '').trim();
        if (!text) return '工具步骤';
        const normalized = text.toLowerCase();
        const map = {
            match_flow_template: '模板匹配',
            validate_flow: '流程校验',
            get_flow_template_skeleton: '获取模板骨架',
            select_operator_pipeline: '选择算子链',
            map_parameters: '参数映射',
            draft_workflow: '生成流程草稿',
            validate_schema: '结构校验',
            metadata_dry_run: '元数据预演',
            package_readiness: '运行包就绪检查',
            station_compatibility: '工站兼容检查',
            operator_contract: '算子契约检查',
            release_review: '发布复核',
            workflow_diff: '流程差异',
            apply_gate: '应用门禁'
        };
        if (map[normalized]) return map[normalized];
        if (normalized.endsWith('_tool')) {
            const stage = normalized.slice(0, -5);
            return `${BUILD_STAGE_LABELS[stage] || this._localizeDisplayText(stage)}工具`;
        }
        return this._localizeDisplayText(text);
    },

    _formatOperatorType(value) {
        const text = String(value || '').trim();
        return AI_OPERATOR_LABELS[text] || this._localizeDisplayText(text);
    },

    _formatParameterName(value) {
        const text = String(value || '').trim();
        return AI_PARAMETER_LABELS[text] || this._localizeDisplayText(text);
    },

    _formatResourceReference(value) {
        const text = String(value || '').trim();
        if (!text) return '';
        const parts = text.split('.').filter(Boolean);
        if (parts.length > 1) {
            const first = parts[0];
            const last = parts[parts.length - 1];
            const parameterLabel = this._formatParameterName(last);
            if (/^op[_-]/i.test(first)) {
                return parameterLabel;
            }
            return `${this._formatOperatorType(first)}.${parameterLabel}`;
        }
        return this._formatParameterName(this._localizeDisplayText(text));
    },

    _formatWorkflowDiffValue(value, kind = '') {
        const text = this._sanitizeBuildWorkspaceText(value, 160);
        if (!text) return '';
        if (kind === 'pending' || kind === 'blocker' || text.includes('.')) {
            return this._formatResourceReference(text);
        }
        return this._localizeDisplayText(text);
    },

    _renderAgentWorkspaceOverview() {
        const el = this.container?.querySelector('#ai-agent-workspace-overview');
        if (!el) return;

        const plan = this.pendingVisionPlan;
        if (plan) {
            this._ensureRecommendedPlanQuestionSelections?.(plan);
            this._refreshPlanEffectiveBuildReadiness?.(plan);
        }
        const mode = this._formatWorkspaceModeLabel();
        const phase = this._getAgentWorkspacePhase();
        const viewMode = this._getWorkspaceViewMode();
        const activeEvents = Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [];
        const planEvents = Array.isArray(plan?.publicEvents) ? plan.publicEvents : [];
        const planRunEvents = Array.isArray(this.activePlanRunEvents) ? this.activePlanRunEvents : [];
        const planProgress = this._getPlanRunProgressState?.() || { currentLabel: '', eventCount: 0 };
        const terminal = activeEvents.find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(evt.eventType));
        const lastEvent = activeEvents[activeEvents.length - 1];
        const blockerCount = this._countBuildBlockers(activeEvents);
        const showBuildExecutionPath = phase === AgentWorkspaceModes.BUILD || activeEvents.length > 0;
        const executionPath = showBuildExecutionPath
            ? this._getBuildExecutionPath(activeEvents)
            : { modeLabel: '', enteredLabel: '', reasonLabel: '' };
        const goal = plan?.goal || this.lastUserPrompt || '描述视觉检测目标后开始规划。';
        const confidence = this._formatWorkspaceValue(plan?.confidence || (activeEvents.length || planRunEvents.length ? '事件驱动' : '未设置'));
        const nextAction = terminal
            ? (terminal.eventType === 'run.completed' ? '复核流程草稿，可应用到画布继续编辑。' : '查看首要修复建议后重试构建。')
            : phase === AgentWorkspaceModes.BUILD
                ? (lastEvent?.summary || '等待下一条后端公开事件。')
                : (plan?.nextAction || planProgress.currentLabel || '规划模式只提出高价值工程问题。');
        const executable = phase === AgentWorkspaceModes.BUILD
            ? activeEvents.length > 0
            : Boolean(plan?.executable);
        const source = this._formatWorkspaceValue(plan?.planSource || (activeEvents.length ? '构建事件' : (planRunEvents.length ? '事件驱动' : '未设置')));
        const userSummary = plan ? this._buildPlanUserSummary?.(plan) : null;
        const missingSummary = userSummary?.missingSummary || this._buildPlanMissingSummary?.(plan);
        const missingTotal = Number(missingSummary?.totalCount) || 0;
        const overviewTone = terminal?.eventType === 'run.failed'
            ? 'danger'
            : phase === AgentWorkspaceModes.PLAN && plan && !executable && missingTotal > 0
                ? 'warning'
                : executable
                    ? 'success'
                    : 'neutral';
        const overviewKicker = phase === AgentWorkspaceModes.PLAN && plan
            ? (executable ? '方案已就绪' : '已形成初步方案')
            : mode;
        const overviewDetail = phase === AgentWorkspaceModes.PLAN && plan
            ? (missingTotal > 0 ? `还需补充 ${missingTotal} 项信息` : '信息已满足构建条件')
            : nextAction;
        const overviewState = phase === AgentWorkspaceModes.PLAN && plan
            ? (executable ? '可以构建' : '暂不能构建')
            : (terminal?.eventType === 'run.failed' ? '构建失败' : mode);
        const planOverviewMetrics = plan
            ? `
                <span><small>总计</small><b>${this._escapeHtml(String(missingSummary?.totalCount || 0))} 项</b></span>
                <span><small>构建前确认</small><b>${this._escapeHtml(String(missingSummary?.mustConfirmCount || 0))} 项</b></span>
                <span><small>可后补</small><b>${this._escapeHtml(String(missingSummary?.fillLaterCount || 0))} 项</b></span>
                <span><small>构建入口</small><b>正式 Plan→Build</b></span>
                <span><small>状态</small><b>${this._escapeHtml(executable ? '可以构建' : '暂不能构建')}</b></span>
            `
            : `<span><small>置信度</small><b>${this._escapeHtml(confidence)}</b></span>
               <span><small>来源</small><b>${this._escapeHtml(source)}</b></span>
               <span><small>总计</small><b>0 项</b></span>
               <span><small>状态</small><b>等待规划</b></span>`;

        el.innerHTML = `
            <section class="ai-agent-overview-card is-${this._escapeHtml(phase)} is-${this._escapeHtml(overviewTone)}">
                <div class="ai-agent-overview-main">
                    <span class="ai-agent-overview-kicker">${this._escapeHtml(overviewKicker)}</span>
                    <strong>${this._escapeHtml(goal)}</strong>
                    <span>${this._escapeHtml(overviewDetail)}</span>
                    <em>${this._escapeHtml(overviewState)}</em>
                </div>
                <div class="ai-agent-overview-metrics">
                    ${showBuildExecutionPath
                        ? `<span><small>构建入口</small><b>${this._escapeHtml(executionPath.modeLabel || mode)}</b></span>
                           <span><small>公开事件</small><b>${this._escapeHtml(executionPath.enteredLabel)}</b></span>`
                        : planOverviewMetrics}
                    ${showBuildExecutionPath
                        ? `<span><small>阻断项</small><b>${this._escapeHtml(String(blockerCount))}</b></span>
                           <span><small>事件数</small><b>${this._escapeHtml(String(activeEvents.length || planRunEvents.length || planEvents.length))}</b></span>`
                        : ''}
                </div>
                ${executionPath.reasonLabel ? `<div class="ai-build-note"><strong>路径原因</strong>${this._escapeHtml(executionPath.reasonLabel)}</div>` : ''}
                <div class="ai-agent-stage-strip" role="tablist" aria-label="Vision Agent 工作台视图">
                    ${[AgentWorkspaceModes.PLAN, AgentWorkspaceModes.BUILD].map(key => {
                        const disabled = key === AgentWorkspaceModes.PLAN
                            ? !this._canViewPlanWorkspace()
                            : !this._canViewBuildWorkspace();
                        const statusLabel = key === AgentWorkspaceModes.BUILD
                            ? this._getBuildViewStatusLabel(activeEvents)
                            : this._getPlanViewStatusLabel(planRunEvents);
                        return `
                            <button type="button"
                                role="tab"
                                data-workspace-view-mode="${key}"
                                data-ai-focus-key="workspace-${key}"
                                aria-controls="${key === AgentWorkspaceModes.PLAN ? 'ai-plan-workspace' : 'ai-build-workspace'}"
                                aria-selected="${key === viewMode ? 'true' : 'false'}"
                                tabindex="${key === viewMode && !disabled ? '0' : '-1'}"
                                ${disabled ? 'disabled aria-disabled="true"' : ''}
                                class="${key === phase ? 'is-active' : ''} ${key === viewMode ? 'is-selected' : ''} ${this._isWorkspacePhaseCompleted(key, phase) ? 'is-completed' : ''}">
                                <span>${key === AgentWorkspaceModes.PLAN ? '方案' : '构建与验证'}</span>
                                <small>${this._escapeHtml(statusLabel)}</small>
                            </button>
                        `;
                    }).join('')}
                    <button type="button"
                        role="tab"
                        aria-selected="${phase === 'applied' ? 'true' : 'false'}"
                        tabindex="-1"
                        disabled aria-disabled="true"
                        class="${phase === 'applied' ? 'is-active is-selected' : ''} ${terminal || phase === 'applied' ? 'is-completed' : ''}">
                        <span>Applied 复核</span>
                        <small>${phase === 'applied' ? '已应用' : (terminal ? '可复核' : '暂无')}</small>
                    </button>
                </div>
            </section>
        `;
        el.querySelectorAll('[data-workspace-view-mode]').forEach(button => {
            button.addEventListener('click', () => {
                this._setWorkspaceViewMode(button.getAttribute('data-workspace-view-mode'));
            });
        });
        this._syncAccessibilitySemantics?.();
    },

    _getPlanViewStatusLabel(events = []) {
        if (this.activePlanRunCompletion) return '运行中';
        if ((Array.isArray(events) ? events : []).some(evt => String(evt?.eventType || '').includes('failed'))) return '失败';
        if ((Array.isArray(events) ? events : []).length || this.pendingVisionPlan) return '可查看';
        return '暂无';
    },

    _getBuildViewStatusLabel(events = []) {
        const terminal = [...(Array.isArray(events) ? events : [])].reverse()
            .find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(String(evt?.eventType || '')));
        if (terminal?.eventType === 'run.completed') return '已完成';
        if (terminal?.eventType === 'run.failed') return '失败';
        if (terminal?.eventType === 'run.cancelled') return '已取消';
        if (this.activeAgentRunId || (Array.isArray(events) && events.length)) return '运行中';
        if (this.currentResult?.buildResult || this.currentResult?.BuildResult) return '可查看';
        return '暂无';
    },

    _getBuildExecutionPath(events = []) {
        const list = Array.isArray(events) ? events : [];
        return {
            modeLabel: '正式 Plan→Build',
            enteredLabel: list.length > 0 ? '已连接' : '等待事件',
            reasonLabel: ''
        };
    },

    _getAgentWorkspacePhase() {
        if (this.workbenchState === AiWorkbenchStates.APPLIED || this.agentWorkspaceMode === AgentWorkspaceModes.APPLIED) {
            return AgentWorkspaceModes.APPLIED;
        }

        return this.agentWorkspaceMode === AgentWorkspaceModes.BUILD
            ? AgentWorkspaceModes.BUILD
            : AgentWorkspaceModes.PLAN;
    },

    _isWorkspacePhaseCompleted(key, activePhase) {
        const order = [AgentWorkspaceModes.PLAN, AgentWorkspaceModes.BUILD, AgentWorkspaceModes.APPLIED];
        return order.indexOf(key) >= 0 && order.indexOf(key) < order.indexOf(activePhase);
    },

    _formatWorkspaceModeLabel() {
        if (this.workbenchState === AiWorkbenchStates.APPLIED) return '已应用';
        if (this.workbenchState === AiWorkbenchStates.READY_TO_APPLY) return '可应用';
        const phase = this._getAgentWorkspacePhase();
        const view = this._getWorkspaceViewMode();
        const phaseLabel = phase === AgentWorkspaceModes.BUILD ? '构建运行' : '规划阶段';
        const viewLabel = view === AgentWorkspaceModes.BUILD ? '查看 Build' : '查看 Plan';
        return `${phaseLabel} / ${viewLabel}`;
    },

    _formatWorkspaceValue(value) {
        const text = String(value || '').trim();
        if (!text || text === 'not set' || text === 'unknown') return '未设置';
        if (text === 'event-backed') return '事件驱动';
        if (text === 'build') return '构建事件';
        return this._localizeDisplayText(text);
    },

    _formatBuildStatus(status) {
        switch (String(status || '').trim().toLowerCase()) {
            case 'running':
                return '执行中';
            case 'completed':
                return '已完成';
            case 'blocked':
                return '已阻断';
            case 'failed':
                return '失败';
            case 'cancelled':
            case 'canceled':
                return '已取消';
            case 'pending':
                return '等待中';
            default:
                return this._localizeDisplayText(status) || '已记录';
        }
    },

    _formatGateStatus(status) {
        switch (String(status || '').trim().toLowerCase()) {
            case 'ready':
                return '已就绪';
            case 'canvas_apply_ready':
                return '画布可应用';
            case 'runtime_draft_ready':
                return '运行草稿就绪';
            case 'deployment_ready':
                return '部署就绪';
            case 'deployment_metadata_ready':
                return '部署元数据就绪';
            case 'blocked':
                return '已阻断';
            case 'pending':
                return '等待中';
            case 'unknown':
                return '未设置';
            default:
                return this._localizeDisplayText(status) || '未设置';
        }
    },

    _buildPlanUserSummary(plan) {
        if (!plan) {
            return {
                status: { tone: 'neutral', title: '等待需求描述', detail: '请输入检测目标后开始规划。' },
                understoodItems: [],
                missingCards: [],
                missingFields: [],
                missingCount: 0,
                missingSummary: {
                    totalCount: 0,
                    mustConfirmCount: 0,
                    fillLaterCount: 0,
                    missingFields: [],
                    summaryText: '总计 0 项；构建前必须确认 0 项；可构建后补齐 0 项'
                },
                nextActions: ['描述检测目标、图像来源和判定标准。']
            };
        }

        const semantic = plan.semanticExtraction || {};
        const routeOperators = this._toArray(plan.route?.operators);
        const missingInfo = this._collectPlanMissingInformation(plan);
        const missingSummary = this._buildPlanMissingSummary(plan, missingInfo.missingFields);
        const missingCount = missingSummary.totalCount;
        const hasSemanticFallback = this._hasSemanticRuleFallback(plan);
        const semanticFailureCode = String(semantic.failureCode || '').trim().toLowerCase();
        const hasSemanticFailure = semanticFailureCode.startsWith('semantic_') || hasSemanticFallback;
        const canBuild = this.agentWorkspaceState?.projection?.readiness?.canBuild === true;
        const status = canBuild && missingCount > 0
            ? {
                tone: 'warning',
                title: '已形成初步方案',
                detail: `可以先生成可编辑草稿；仍有 ${missingCount} 项信息可在构建后补齐。`
            }
            : hasSemanticFallback && hasSemanticFailure
                ? {
                tone: 'warning',
                title: '已形成初步方案',
                detail: missingCount > 0
                    ? `已启用规则兜底。还需补充 ${missingCount} 项信息，暂不能构建。`
                    : '可先复核规则兜底草案，再决定是否构建。'
                }
                : missingCount > 0
                    ? {
                        tone: 'warning',
                        title: '已形成初步方案',
                        detail: `还需补充 ${missingCount} 项信息，暂不能构建。`
                    }
                    : canBuild
                        ? {
                            tone: 'success',
                            title: '信息已足够，可以构建',
                            detail: '请复核推荐流程后开始构建。'
                        }
                        : {
                            tone: 'warning',
                            title: '需要先确认信息',
                            detail: this._getPlanBuildBlockedReason?.(plan) || '当前计划还不能开始构建。'
                        };

        const conditions = [
            semantic.okCondition ? `OK：${semantic.okCondition}` : '',
            semantic.ngCondition ? `NG：${semantic.ngCondition}` : ''
        ].filter(Boolean).join('；');
        const understoodItems = [
            plan.goal ? `目标：${plan.goal}` : '',
            semantic.inspectionObject ? `检测目标：${semantic.inspectionObject}` : '',
            semantic.targetAttribute ? `关注属性：${semantic.targetAttribute}` : '',
            conditions ? `判定标准：${conditions}` : '',
            semantic.imageSource ? `图像来源：${semantic.imageSource}` : ''
        ].filter(Boolean);
        if (!understoodItems.length) {
            understoodItems.push(...this._toArray(plan.understanding).slice(0, 4));
        }

        const nextActions = missingCount > 0
            ? [
                '补充缺失信息，或选择推荐默认值继续生成可编辑草稿。',
                '确认后再开始 Build；部署和运行预览仍按权限门禁执行。'
            ]
            : [
                plan.nextAction || '复核推荐流程后开始构建。',
                routeOperators.length ? 'Build 会生成可编辑流程草稿，资源缺口会继续保留为待补项。' : ''
            ].filter(Boolean);

        return {
            status,
            understoodItems: understoodItems.slice(0, 6),
            routeOperators,
            missingCards: missingInfo.cards,
            missingFields: missingInfo.missingFields,
            missingCount,
            missingSummary,
            nextActions
        };
    },

    _collectPlanMissingInformation(plan) {
        const semantic = plan?.semanticExtraction || {};
        const maturity = plan?.requirementMaturity || {};
        const preview = this._getCurrentCanonicalPreview?.(plan);
        const readiness = preview?.buildReadiness || plan?.buildReadiness || {};
        const fields = new Set();
        const addField = value => {
            const normalized = this._inferPlanQuestionField?.(value) || String(value || '').trim().toLowerCase();
            if (normalized) fields.add(normalized);
        };

        [
            ...this._toArray(maturity.missingFields),
            ...this._toArray(semantic.missingFields),
            ...this._toArray(plan?.remainingPlanFields),
            ...this._toArray(readiness.remainingFields)
        ].forEach(addField);

        this._toArray(readiness.blockers).forEach(blocker => {
            addField(blocker?.field || blocker?.Field || blocker?.questionId || blocker?.QuestionId);
        });
        this._toArray(plan?.blockingReasons).forEach(reason => addField(this._normalizePlanBlockingField?.(reason) || reason));
        this._toArray(plan?.questions).forEach(question => {
            const field = this._inferPlanQuestionFieldForQuestion?.(question, plan) ||
                this._fallbackPlanQuestionField?.(question, question?.id) ||
                question?.field ||
                question?.id;
            const answer = this._normalizePlanAnswer?.(this._getPlanAnswerForQuestion?.(question), question);
            if (!answer && this._isPlanBuildBlockingField?.(field, this.requirementMode || 'strict', maturity, {
                plan,
                semanticExtraction: semantic,
                route: plan?.route
            })) {
                addField(field);
            }
        });

        if (!semantic.imageSource || String(semantic.imageSource).trim().toLowerCase() === 'unknown') {
            addField(PLAN_ANSWER_FIELDS.IMAGE_SOURCE);
        }
        if (!semantic.inspectionObject && !plan?.goal) {
            addField(PLAN_ANSWER_FIELDS.INSPECTION_OBJECT);
        }
        if (!semantic.okCondition && !semantic.ngCondition) {
            addField(PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA);
        }

        const missingFields = Array.from(fields).filter(Boolean);
        const hasAny = (...names) => names.some(name => fields.has(name));
        const formatMissing = (...names) => names
            .filter(name => fields.has(name))
            .map(field => this._formatRequirementFieldLabel(field))
            .filter(Boolean)
            .join('、');
        const card = ({ id, title, value, missing, detail }) => ({
            id,
            title,
            value: value || '',
            missing: Boolean(missing),
            detail: detail || (missing ? '待补充' : '已明确')
        });
        const templateSelection = plan?.templateSelection || {};
        const templateValue = [
            templateSelection.templateName || templateSelection.TemplateName,
            templateSelection.templateId || templateSelection.TemplateId,
            templateSelection.mode || templateSelection.Mode
        ].filter(Boolean).join(' / ');

        const cards = [
            card({
                id: 'image_source',
                title: '图像来源',
                value: semantic.imageSource && String(semantic.imageSource).trim().toLowerCase() !== 'unknown'
                    ? semantic.imageSource
                    : '',
                missing: hasAny(PLAN_ANSWER_FIELDS.IMAGE_SOURCE),
                detail: hasAny(PLAN_ANSWER_FIELDS.IMAGE_SOURCE)
                    ? '请确认来自相机、样张、当前画布还是历史结果。'
                    : '已记录图像输入来源。'
            }),
            card({
                id: 'decision_rule',
                title: '检测目标/判定标准',
                value: [
                    semantic.inspectionObject ? `目标：${semantic.inspectionObject}` : '',
                    semantic.okCondition ? `OK：${semantic.okCondition}` : '',
                    semantic.ngCondition ? `NG：${semantic.ngCondition}` : ''
                ].filter(Boolean).join('；'),
                missing: hasAny(
                    PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
                    PLAN_ANSWER_FIELDS.TASK_TYPE,
                    PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
                    PLAN_ANSWER_FIELDS.DEFECT_TYPE,
                    PLAN_ANSWER_FIELDS.TARGET_ATTRIBUTE
                ),
                detail: hasAny(
                    PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
                    PLAN_ANSWER_FIELDS.TASK_TYPE,
                    PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
                    PLAN_ANSWER_FIELDS.DEFECT_TYPE,
                    PLAN_ANSWER_FIELDS.TARGET_ATTRIBUTE
                )
                    ? `还差：${formatMissing(
                        PLAN_ANSWER_FIELDS.INSPECTION_OBJECT,
                        PLAN_ANSWER_FIELDS.TASK_TYPE,
                        PLAN_ANSWER_FIELDS.ACCEPTANCE_CRITERIA,
                        PLAN_ANSWER_FIELDS.DEFECT_TYPE,
                        PLAN_ANSWER_FIELDS.TARGET_ATTRIBUTE
                    ) || '检测目标或判定标准'}`
                    : '目标和 OK/NG 标准已记录。'
            }),
            card({
                id: 'template_selection',
                title: '当前流程/模板选择',
                value: templateValue,
                missing: hasAny(PLAN_ANSWER_FIELDS.TEMPLATE_STRATEGY, PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY),
                detail: hasAny(PLAN_ANSWER_FIELDS.TEMPLATE_STRATEGY, PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY)
                    ? `还差：${formatMissing(PLAN_ANSWER_FIELDS.TEMPLATE_STRATEGY, PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY) || '流程或算法策略'}`
                    : (templateValue ? '已记录模板选择。' : '未指定模板，将使用推荐流程草稿。')
            })
        ];

        return { cards, missingFields };
    },

    _hasSemanticRuleFallback(plan) {
        const semantic = plan?.semanticExtraction || {};
        if (String(semantic.source || '').trim().toLowerCase() === 'rule_fallback') return true;
        if (String(plan?.planSource || '').trim().toLowerCase() === 'rule_fallback') return true;
        if (String(plan?.rawFallbackReason || '').trim().toLowerCase().includes('semantic_')) return true;
        return this._toArray(plan?.publicEvents).some(evt => {
            const stage = String(evt?.stage || evt?.eventType || '').trim().toLowerCase();
            const summary = String(evt?.summary || '').trim().toLowerCase();
            return stage.includes('semantic_fallback') ||
                stage.includes('rule_fallback') ||
                summary.includes('semantic') && summary.includes('fallback');
        });
    },

    _renderRequirementMaturityPanel(plan) {
        if (!plan) return '';
        const summary = this._buildPlanUserSummary(plan);
        const missingCards = summary.missingCards || [];
        const missingSummary = summary.missingSummary || this._buildPlanMissingSummary(plan, summary.missingFields);

        return `
            <section class="ai-workspace-section ai-plan-user-summary ${summary.status.tone === 'success' ? 'is-ready' : 'is-warning'}">
                <div class="ai-workspace-section-title">还需要确认的信息</div>
                <div class="ai-plan-user-status is-${this._escapeHtml(summary.status.tone)}">
                    <strong>${this._escapeHtml(summary.status.title)}</strong>
                    <span>${this._escapeHtml(summary.status.detail)}</span>
                    <small>${this._escapeHtml(missingSummary.summaryText)}</small>
                </div>
                <div class="ai-plan-user-cta">
                    <button type="button" class="ai-plan-user-action is-primary" id="ai-plan-focus-confirmation">补充信息</button>
                    <button type="button" class="ai-plan-user-action" id="ai-plan-use-recommended-defaults">使用推荐默认值继续</button>
                    <button type="button" class="ai-plan-user-action" id="ai-plan-view-draft">查看草稿</button>
                </div>
                <div class="ai-plan-cta-feedback" id="ai-plan-cta-feedback" role="status" aria-live="polite" hidden></div>
                <div class="ai-plan-missing-grid">
                    ${missingCards.map(item => `
                        <article class="ai-plan-missing-card ${item.missing ? 'is-missing' : 'is-ready'}">
                            <div>
                                <strong>${this._escapeHtml(item.title)}</strong>
                                <span>${this._escapeHtml(item.missing ? '待确认' : '已记录')}</span>
                            </div>
                            ${item.value ? `<p>${this._escapeHtml(item.value)}</p>` : ''}
                            <small>${this._escapeHtml(item.detail)}</small>
                        </article>
                    `).join('')}
                </div>
            </section>
        `;
    },

    _setPlanCtaFeedback(message, tone = 'info', root = null) {
        const text = String(message || '').trim();
        this._setResultStatusNote?.(text, tone);
        const workspace = root || this.container?.querySelector?.('#ai-plan-workspace') || null;
        const feedback = workspace?.querySelector?.('#ai-plan-cta-feedback') ||
            this.container?.querySelector?.('#ai-plan-cta-feedback');
        if (!feedback || !text) return;

        feedback.hidden = false;
        feedback.textContent = text;
        feedback.className = `ai-plan-cta-feedback is-${tone || 'info'}`;
    },

    _handlePlanSupplementInfoClick(plan = this.pendingVisionPlan, root = null) {
        const workspace = root || this.container?.querySelector?.('#ai-plan-workspace') || null;
        const details = workspace?.querySelector?.('.ai-plan-more-details');
        if (details) {
            details.open = true;
        }

        const firstQuestion = workspace?.querySelector?.('.ai-plan-question');
        if (firstQuestion) {
            firstQuestion.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
            const focusTarget = firstQuestion.querySelector?.('button, input, textarea, select') || firstQuestion;
            focusTarget?.focus?.({ preventScroll: true });
            this._setPlanCtaFeedback('已展开更多方案细节，请补充第一个关键问题。', 'info', workspace);
            return true;
        }

        const input = this.container?.querySelector?.('#ai-input');
        input?.focus?.();
        const message = plan
            ? '当前没有可展开的关键问题，请在右侧输入框补充信息。'
            : '请先输入检测需求，我会先整理规划方案。';
        this._setPlanCtaFeedback(message, 'info', workspace);
        return false;
    },

    async _handlePlanUseRecommendedDefaultsClick(plan = this.pendingVisionPlan) {
        if (!plan) {
            this._setPlanCtaFeedback('请先完成规划，再使用推荐默认值继续。', 'warning');
            return false;
        }

        const actionState = this._getPlanBuildActionState(plan);
        if (actionState.canStart === true) {
            this._setPlanCtaFeedback('当前方案已满足构建条件，正在进入构建。', 'info');
            return await this._startBuildFromCurrentPlan();
        }

        const changed = this._acceptRecommendedPlanAnswers?.(plan) === true;
        const nextActionState = this._getPlanBuildActionState(plan);
        if (nextActionState.canStart === true) {
            this._setPlanCtaFeedback('已使用推荐默认值，正在进入构建。', 'info');
            return await this._startBuildFromCurrentPlan();
        }

        const missingSummary = nextActionState.missingSummary || this._buildPlanMissingSummary?.(plan);
        const mustConfirmCount = Math.max(Number(missingSummary?.mustConfirmCount) || 0, 0);
        let feedbackMessage = '';
        let feedbackTone = 'info';
        if (mustConfirmCount > 0) {
            feedbackMessage = `仍需先确认 ${mustConfirmCount} 项构建前信息。`;
            feedbackTone = 'warning';
        } else {
            feedbackMessage = changed ? '已使用推荐默认值，正在校验构建条件。' : '当前推荐默认值不足以继续，请补充确认信息。';
            feedbackTone = changed ? 'info' : 'warning';
        }

        this._renderAgentWorkspaceOverview?.();
        this._renderPlanWorkspace?.(plan);
        this._renderBuildWorkspaceFromAgentRun?.();
        this._updatePlanBuildActionState?.();
        this._setPlanCtaFeedback(feedbackMessage, feedbackTone);
        return false;
    },

    _handlePlanViewDraftClick() {
        if (this._canViewBuildWorkspace?.()) {
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.BUILD);
            this._setPlanCtaFeedback('已切换到构建草稿视图。', 'info');
            return true;
        }

        this._setPlanCtaFeedback('开始构建后会生成可查看的流程草稿。', 'info');
        return false;
    },

    _renderWorkspaceClarificationLine(plan = this.pendingVisionPlan) {
        const projection = this.agentWorkspaceState?.projection;
        const queue = this._toArray(projection?.clarificationQueue);
        const batch = this._toArray(projection?.clarificationBatch);
        if (!queue.length) {
            return '<div class="ai-plan-maturity-empty">当前没有待澄清项。</div>';
        }

        const firstOpenKey = batch.find(item => !item?.answered && !item?.deferred)?.field ||
            batch.find(item => !item?.answered && !item?.deferred)?.id || '';
        const batchKeys = new Set(batch.map(item => item?.field || item?.id).filter(Boolean));
        const settled = queue.filter(item => (item?.answered || item?.deferred) &&
            !batchKeys.has(item?.field || item?.id));
        const rows = [...settled, ...batch].map(item => {
            const key = item?.field || item?.id || '';
            if (item?.answered) {
                return `
                    <article class="ai-plan-question is-answered" data-clarification-item="${this._escapeHtml(key)}">
                        <div class="ai-plan-question-title">${this._escapeHtml(item.title || key)}</div>
                        <div class="ai-plan-question-selection-feedback">已答：${this._escapeHtml(item.answer?.value || '')}</div>
                    </article>
                `;
            }
            if (item?.deferred) {
                return `
                    <article class="ai-plan-question is-answered" data-clarification-item="${this._escapeHtml(key)}">
                        <div class="ai-plan-question-title">${this._escapeHtml(item.title || key)}</div>
                        <div class="ai-plan-question-selection-feedback">稍后绑定：仍保持 resource_pending，不视为已就绪。</div>
                    </article>
                `;
            }
            if (key !== firstOpenKey) return '';
            if (item?.kind === 'resource') {
                const resource = item.raw || item.resourceDecision?.resource || {};
                const actionModel = this._getMissingResourceActionModel?.(resource) || {};
                const resourceIndex = this._toArray(projection?.missingResources)
                    .findIndex(candidate => candidate === item ||
                        (candidate?.resourceKey && candidate.resourceKey === item.resourceKey) ||
                        (candidate?.id && candidate.id === item.id));
                return `<div class="ai-unified-resource-question">${this._renderResourceAuditTaskCard?.(resource, actionModel, Math.max(resourceIndex, 0)) || ''}</div>`;
            }
            const question = this._toArray(plan?.questions)
                .find(candidate => String(candidate?.id || '') === String(item?.questionId || item?.id || '') ||
                    this._inferPlanQuestionFieldForQuestion?.(candidate, plan) === item?.field);
            if (question) {
                return this._renderPlanQuestion(question, this._getPlanQuestionSelectedValue(question));
            }
            return `
                <article class="ai-plan-question is-readonly" data-clarification-item="${this._escapeHtml(key)}">
                    <div class="ai-plan-question-title">${this._escapeHtml(item.title || key)}</div>
                    <div class="ai-plan-question-why">该项由后端 readiness 判定，需按提示补充后重新校验。</div>
                </article>
            `;
        }).join('');
        const remainingCount = queue.filter(item => !item?.answered && !item?.deferred).length;
        return `
            <div class="ai-unified-clarification-line" data-clarification-batch-size="${batch.length}">
                <div class="ai-build-note">本轮最多 3 项，逐项确认；本批完成后一次提交后端 readiness 校验。剩余 ${remainingCount} 项。</div>
                ${rows || '<div class="ai-plan-maturity-empty">正在等待后端刷新澄清队列。</div>'}
            </div>
        `;
    },

    _renderPlanWorkspace(plan = this.pendingVisionPlan) {
        const el = this.container?.querySelector('#ai-plan-workspace');
        if (!el) return;

        el.hidden = this._getWorkspaceViewMode() !== AgentWorkspaceModes.PLAN;
        if (!plan) {
            const progress = this._getPlanRunProgressState?.();
            const liveStatus = progress?.eventCount > 0
                ? `
                    <div class="ai-build-compact">
                        ${PLAN_PHASES.map(phase => {
                            const item = progress.phases[phase.key];
                            return `
                                <div class="ai-build-compact-row">
                                    <b>${this._escapeHtml(item.label)}</b>
                                    <span>${this._escapeHtml(this._formatPlanTimelineStatus(item.status))}</span>
                                </div>
                            `;
                        }).join('')}
                    </div>
                `
                : '';
            el.innerHTML = `
                <div class="ai-plan-empty">
                    <div class="ai-plan-empty-title">规划模式</div>
                    <div class="ai-plan-empty-copy">${this._escapeHtml(progress?.currentLabel || '正在收集工程上下文。请输入检测目标，智能体会先形成视觉工程计划，再进入构建。')}</div>
                    ${liveStatus}
                    <div class="ai-plan-empty-copy">问题、readiness 阻断和资源待绑定项会统一进入同一条澄清线。</div>
                </div>
            `;
            this._renderPlanConfirmationGuidance?.(null, null);
            this._updatePlanBuildActionState();
            return;
        }

        const userSummary = this._buildPlanUserSummary(plan);
        const maturityPanel = this._renderRequirementMaturityPanel(plan);
        const missingSummary = userSummary.missingSummary || this._buildPlanMissingSummary(plan, userSummary.missingFields);
        const routeOperators = this._toArray(plan.route?.operators);
        const routeChain = routeOperators.length
            ? `<div class="ai-plan-chain">${routeOperators.map(op => `<span title="${this._escapeHtml(op)}">${this._escapeHtml(this._formatOperatorType(op))}</span>`).join('')}</div>`
            : '<div class="ai-plan-maturity-empty">需求成熟度不足时不会提前选择算子链。</div>';
        const plannerFailureDiagnostics = this._renderPlannerFailureDiagnostics(plan);
        const requirementMode = this._normalizeRequirementMode?.(plan.requirementMode || this.requirementMode || 'strict') || 'strict';
        const confirmationModeLabel = requirementMode === 'draft'
            ? '先生成可编辑草稿'
            : '确认完整后构建';
        const actionState = this._getPlanBuildActionState(plan);
        const isPlanReadOnly = this._isPlanSnapshotReadOnly?.() === true;
        const currentPreview = this._getCurrentCanonicalPreview?.(plan);
        const hasDeferredStrictBlock = (this._normalizeRequirementMode?.(this.requirementMode) || 'strict') === 'strict' &&
            this._toArray(currentPreview?.deferredQuestionIds).length > 0 &&
            actionState.canStart !== true;
        const readinessStatus = this.agentWorkspaceState?.readinessStatus || 'idle';
        const previewFailed = ['failed', 'timeout'].includes(readinessStatus) || (plan.previewState || this.previewState) === 'failed';
        const previewMissing = actionState.canRetryReadiness === true;
        const clarificationLine = this._renderWorkspaceClarificationLine(plan);
        const modeToggle = `
            <div class="ai-plan-mode-toggle" role="group" aria-label="构建确认模式">
                <button type="button" data-requirement-mode="strict" class="${this.requirementMode === 'strict' ? 'is-active' : ''}" aria-pressed="${this.requirementMode === 'strict' ? 'true' : 'false'}">确认完整后构建</button>
                <button type="button" data-requirement-mode="draft" class="${this.requirementMode === 'draft' ? 'is-active' : ''}" aria-pressed="${this.requirementMode === 'draft' ? 'true' : 'false'}">先生成可编辑草稿</button>
            </div>
            <div class="ai-plan-mode-help" id="ai-requirement-mode-tip">${this.requirementMode === 'draft'
                ? '允许后端判定为可后补的决策或资源暂缓，先生成可编辑草稿，不代表可部署。'
                : '关键决策及当前模式要求的资源确认后才可构建。'}</div>
        `;
        const ctaAssist = hasDeferredStrictBlock
            ? `<div class="ai-plan-cta-assist">当前选择要求构建前确认，暂缓项仍会阻止构建。<button type="button" id="ai-btn-switch-draft-from-defer">切换为可编辑草稿</button></div>`
            : previewFailed
                ? `<div class="ai-plan-cta-assist">${readinessStatus === 'timeout' ? '构建条件校验超时' : '构建条件校验失败'}，请重试<button type="button" id="ai-btn-retry-readiness-preview">重试校验</button></div>`
                : previewMissing
                    ? `<div class="ai-plan-cta-assist">尚未获得与当前方案、答案版本和模式匹配的权威校验结果。<button type="button" id="ai-btn-retry-readiness-preview">重试校验</button></div>`
                : '';
        el.innerHTML = `
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">我理解的需求</div>
                <div class="ai-workspace-list">${userSummary.understoodItems.map(item => `<div>${this._escapeHtml(item)}</div>`).join('')}</div>
            </section>
            ${maturityPanel}
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">推荐流程</div>
                <div class="ai-plan-route">
                    <strong>${this._escapeHtml(plan.route.title)}</strong>
                    <span>${this._escapeHtml(plan.route.summary)}</span>
                    ${routeChain}
                </div>
            </section>
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">下一步动作</div>
                <div class="ai-workspace-list">
                    ${userSummary.nextActions.map(item => `<div>${this._escapeHtml(item)}</div>`).join('')}
                </div>
                <div class="ai-plan-confirm-mode">
                    <div>
                        <strong>构建前确认</strong>
                        <span>${this._escapeHtml(confirmationModeLabel)} · ${this._escapeHtml(missingSummary.summaryText)}</span>
                    </div>
                    ${isPlanReadOnly ? modeToggle.replaceAll('<button type="button" data-requirement-mode=', '<button type="button" disabled data-requirement-mode=') : modeToggle}
                    ${plan.buildReadiness?.primaryMessage ? `<small>${this._escapeHtml(plan.buildReadiness.primaryMessage)}</small>` : ''}
                </div>
            </section>
            <section class="ai-workspace-section">
                <details class="ai-plan-more-details">
                    <summary class="ai-workspace-section-title">更多方案细节</summary>
                    <div class="ai-plan-more-details-body">
                        <section>
                            <div class="ai-workspace-section-title">关键问题</div>
                            <div class="ai-plan-question-list">
                                ${clarificationLine}
                            </div>
                            <div class="ai-build-note">资源选择“稍后绑定”只记录延期决定，仍保留资源待绑定与部署门禁。</div>
                        </section>
                        <section class="ai-workspace-grid-2">
                            <div>
                                <div class="ai-workspace-section-title">推荐默认值</div>
                                <ul>${plan.assumptions.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>
                            </div>
                            <div>
                                <div class="ai-workspace-section-title">风险</div>
                                <ul>${plan.risks.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>
                            </div>
                        </section>
                        <section class="ai-workspace-grid-2">
                            <div>
                                <div class="ai-workspace-section-title">可执行计划</div>
                                <ol>${plan.steps.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ol>
                            </div>
                            <div>
                                <div class="ai-workspace-section-title">验收标准</div>
                                <ol>${plan.acceptanceCriteria.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ol>
                            </div>
                        </section>
                    </div>
                </details>
            </section>
            <section class="ai-workspace-section">
                <details class="ai-plan-diagnostics">
                <summary class="ai-workspace-section-title">规划诊断（诊断详情 / 原始事件 / Agent Trace）</summary>
                <div class="ai-build-compact">
                    <div class="ai-build-compact-row">
                        <b>${this._escapeHtml(this._formatPlanSource(plan.planSource))}</b>
                        <span class="ai-plan-tech-code">${this._escapeHtml(this._sanitizePlanDiagnosticCode(plan.planHash) || '计划哈希待生成')}</span>
                    </div>
                    ${plannerFailureDiagnostics}
                    ${plan.nextAction ? `<div class="ai-build-note"><strong>模型 NextAction</strong>${this._escapeHtml(plan.nextAction)}</div>` : ''}
                    ${plan.fallbackReason ? `<div class="ai-build-note"><strong>兜底原因</strong>${this._escapeHtml(plan.fallbackReason)}</div>` : ''}
                    ${plan.planWarnings.length ? `<ul>${plan.planWarnings.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>` : ''}
                    ${plan.contractRepairNotes.length ? `<div class="ai-plan-chain">${plan.contractRepairNotes.map(item => `<span>${this._escapeHtml(item)}</span>`).join('')}</div>` : ''}
                    ${plan.publicEvents.length ? `<div class="ai-workspace-list">${plan.publicEvents.map(evt => {
                        const event = this._formatPlanEvent(evt);
                        return `<div><b>${this._escapeHtml(event.stageLabel)}</b> ${this._escapeHtml(event.statusLabel)} - ${this._escapeHtml(event.summary)}</div>`;
                    }).join('')}</div>` : ''}
                    ${this._renderPlanRawDiagnostics(plan)}
                </div>
                </details>
            </section>
            <div class="ai-plan-actions">
                ${ctaAssist}
                <span class="ai-plan-action-status" id="ai-plan-build-status"></span>
                <button class="ai-plan-action is-primary" type="button" id="ai-btn-start-build">开始构建</button>
            </div>
        `;

        el.querySelectorAll('[data-plan-question-option]').forEach(button => {
            button.addEventListener('click', () => {
                this._selectPlanQuestionOption(
                    button.getAttribute('data-plan-question') || '',
                    button.getAttribute('data-plan-question-option') || ''
                );
            });
        });
        el.querySelectorAll('[data-resource-action]').forEach(button => {
            button.addEventListener('click', () => {
                const resourceIndex = Number.parseInt(button.dataset.resourceIndex || '-1', 10);
                const projectedResource = Number.isInteger(resourceIndex) && resourceIndex >= 0
                    ? this._toArray(this.agentWorkspaceState?.projection?.missingResources)[resourceIndex]
                    : null;
                const resource = projectedResource?.raw || projectedResource || null;
                if (!resource) return;
                const task = button.closest?.('.ai-followup-resource-task') || null;
                const inputEl = task?.querySelector?.('[data-resource-input="true"]') || null;
                this._handleMissingResourceAction?.(resource, button.dataset.resourceAction || '', {
                    value: inputEl?.value ?? '',
                    data: this.currentResult,
                    flow: this.currentResult?.flow || this.currentResult?.Flow || null
                });
            });
        });
        el.querySelectorAll('[data-requirement-mode]').forEach(button => {
            button.addEventListener('click', () => {
                this._setRequirementMode?.(button.dataset.requirementMode || 'strict');
            });
        });
        el.querySelectorAll('.ai-plan-custom-input-btn').forEach(button => {
            button.addEventListener('click', () => {
                const questionId = button.getAttribute('data-plan-question') || '';
                const inputField = Array.from(el.querySelectorAll('.ai-plan-custom-input-field'))
                    .find(input => input.getAttribute('data-plan-question') === questionId);
                if (inputField) {
                    this._customInputPlanQuestion(questionId, inputField.value);
                }
            });
        });
        el.querySelectorAll('.ai-plan-custom-input-field').forEach(input => {
            input.addEventListener('keydown', event => {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    const questionId = input.getAttribute('data-plan-question') || '';
                    this._customInputPlanQuestion(questionId, input.value);
                }
            });
        });
        el.querySelector('#ai-btn-start-build')?.addEventListener('click', event => this._startBuildFromCurrentPlan({
            acceptedRecommended: event.currentTarget?.dataset?.acceptRecommended === 'true'
        }));
        el.querySelector('#ai-btn-switch-draft-from-defer')?.addEventListener('click', () => this._setRequirementMode?.('draft'));
        el.querySelector('#ai-btn-retry-readiness-preview')?.addEventListener('click', () => {
            this._requestPlanReadinessPreview?.(this.pendingVisionPlan, { reason: 'retry' });
            this._renderPlanWorkspace(this.pendingVisionPlan);
        });
        el.querySelector('#ai-plan-focus-confirmation')?.addEventListener('click', () =>
            this._handlePlanSupplementInfoClick(plan, el));
        el.querySelector('#ai-plan-use-recommended-defaults')?.addEventListener('click', () =>
            this._handlePlanUseRecommendedDefaultsClick(plan));
        el.querySelector('#ai-plan-view-draft')?.addEventListener('click', () =>
            this._handlePlanViewDraftClick());
        this._renderPlanConfirmationGuidance?.(plan, userSummary);
        this._updatePlanBuildActionState();
    },

    _renderPlanRawDiagnostics(plan) {
        const semantic = plan?.semanticExtraction || plan?.rawPlanSnapshot?.semanticExtraction || plan?.rawPlanSnapshot?.SemanticExtraction || null;
        const maturity = plan?.requirementMaturity || plan?.rawPlanSnapshot?.requirementMaturity || plan?.rawPlanSnapshot?.RequirementMaturity || null;
        const trace = plan?.decisionTrace || plan?.rawPlanSnapshot?.decisionTrace || plan?.rawPlanSnapshot?.DecisionTrace || null;
        const rawEvents = plan?.rawPlanSnapshot?.publicEvents || plan?.rawPlanSnapshot?.PublicEvents || plan?.publicEvents || [];
        const workspaceSnapshot = plan?.workspaceSnapshot || plan?.WorkspaceSnapshot || plan?.rawPlanSnapshot?.workspaceSnapshot || plan?.rawPlanSnapshot?.WorkspaceSnapshot || null;
        const rows = [
            semantic ? ['semantic.source', semantic.source || semantic.Source || ''] : null,
            semantic ? ['semantic.taskType', semantic.taskType || semantic.TaskType || ''] : null,
            semantic ? ['semantic.failureCode', semantic.failureCode || semantic.FailureCode || ''] : null,
            semantic ? ['semantic.metadataOnly', semantic.metadataOnly ?? semantic.MetadataOnly ?? ''] : null,
            maturity ? ['taskType', maturity.taskType || maturity.TaskType || ''] : null,
            maturity ? ['objectSignals', this._toArray(maturity.objectSignals || maturity.ObjectSignals).join('、')] : null,
            trace ? ['Trace', trace.fallbackReason || trace.FallbackReason || trace.stage || trace.Stage || ''] : null
        ].filter(item => item && String(item[1] ?? '').trim());
        const renderRows = rows.length
            ? `<div class="ai-plan-raw-diagnostic-rows">${rows.map(([label, value]) => `
                <div><span>${this._escapeHtml(label)}</span><code>${this._escapeHtml(this._sanitizePlanDiagnosticText(value, 220))}</code></div>
            `).join('')}</div>`
            : '<div class="ai-plan-maturity-empty">当前没有 semantic/taskType/objectSignals/failureCode 诊断字段。</div>';
        const blocks = [
            semantic ? ['原始 semantic', semantic] : null,
            maturity ? ['需求成熟度原始字段', maturity] : null,
            trace ? ['Agent Trace', trace] : null,
            rawEvents?.length ? ['原始事件', rawEvents] : null,
            workspaceSnapshot ? ['workspaceSnapshot', workspaceSnapshot] : null
        ].filter(Boolean);

        return `
            <div class="ai-plan-raw-diagnostics">
                <strong>原始诊断字段</strong>
                ${renderRows}
                ${blocks.map(([title, value]) => `
                    <div class="ai-plan-raw-diagnostic-block">
                        <span>${this._escapeHtml(title)}</span>
                        <pre>${this._escapeHtml(this._stringifyPlanDiagnosticValue(value))}</pre>
                    </div>
                `).join('')}
            </div>
        `;
    },

    _stringifyPlanDiagnosticValue(value) {
        const seen = new WeakSet();
        const sanitize = raw => {
            if (typeof raw !== 'string') return raw;
            return this._sanitizePlanDiagnosticText(raw, 500);
        };
        try {
            return JSON.stringify(value, (key, raw) => {
                if (raw && typeof raw === 'object') {
                    if (seen.has(raw)) return '[Circular]';
                    seen.add(raw);
                }
                if (typeof raw === 'string') return sanitize(raw);
                return raw;
            }, 2).slice(0, 8000);
        } catch {
            return this._sanitizePlanDiagnosticText(String(value ?? ''), 8000);
        }
    },

    _renderPlanConfirmationGuidance(plan, summary = null) {
        const turn = this.activeAssistantTurn;
        const card = turn?.card;
        if (!card?.appendChild || typeof document === 'undefined') return;
        let guidance = card.querySelector?.('.ai-plan-confirmation-guidance');
        const data = summary || this._buildPlanUserSummary?.(plan);
        if (!plan || !data || Number(data.missingCount || 0) <= 0) {
            if (guidance) guidance.hidden = true;
            return;
        }
        if (!guidance) {
            guidance = document.createElement('div');
            guidance.className = 'ai-plan-confirmation-guidance';
            card.appendChild(guidance);
        }
        guidance.hidden = false;
        const missingSummary = data.missingSummary || this._buildPlanMissingSummary?.(plan, data.missingFields);
        guidance.innerHTML = `
            <div class="ai-plan-confirmation-guidance-title">还需补充 ${this._escapeHtml(String(missingSummary?.totalCount || data.missingCount))} 项信息</div>
            <div class="ai-plan-confirmation-guidance-copy">${this._escapeHtml(missingSummary?.summaryText || data.status?.detail || '补充信息后再开始构建。')}。暂不能构建。</div>
            <div class="ai-plan-confirmation-guidance-tags">
                ${(data.missingFields || []).slice(0, 5).map(field => `<span>${this._escapeHtml(this._formatRequirementFieldLabel(field))}</span>`).join('')}
            </div>
        `;
        this._scrollToBottom?.();
    },

    _renderSemanticExtractionPanel(plan) {
        const semantic = plan?.semanticExtraction || null;
        if (!semantic) return '';

        const sourceLabel = this._formatSemanticSourceLabel?.(semantic.source) || semantic.sourceLabel || semantic.source || '未知';
        const confidence = Number.isFinite(semantic.confidence)
            ? `${Math.round(semantic.confidence * 100)}%`
            : '未设置';
        const taskTypeLabel = this._formatRequirementTaskTypeLabel(semantic.taskType);
        const missingFields = this._toArray(semantic.missingFields)
            .map(field => this._formatRequirementFieldLabel(field))
            .filter(Boolean);
        const chips = [
            semantic.inspectionObject ? ['对象', semantic.inspectionObject] : null,
            semantic.targetAttribute ? ['属性', semantic.targetAttribute] : null,
            semantic.okCondition ? ['OK', semantic.okCondition] : null,
            semantic.ngCondition ? ['NG', semantic.ngCondition] : null,
            semantic.imageSource ? ['输入源', semantic.imageSource] : null
        ].filter(Boolean);
        const isFallback = String(semantic.source || '').toLowerCase() === 'rule_fallback';

        return `
            <section class="ai-workspace-section ai-requirement-maturity ${isFallback ? 'is-blocked' : 'is-ready'}">
                <div class="ai-workspace-section-title">语义抽取</div>
                <div class="ai-requirement-maturity-grid">
                    <div class="ai-build-compact-row">
                        <b>来源</b>
                        <span>${this._escapeHtml(sourceLabel)}</span>
                    </div>
                    <div class="ai-build-compact-row">
                        <b>任务类型</b>
                        <span>${this._escapeHtml(taskTypeLabel)}</span>
                    </div>
                    <div class="ai-build-compact-row">
                        <b>置信度</b>
                        <span>${this._escapeHtml(confidence)}</span>
                    </div>
                    <div class="ai-build-compact-row">
                        <b>视觉需求</b>
                        <span>${semantic.isVisionRequest ? '是' : '否'}</span>
                    </div>
                </div>
                ${chips.length ? `<div class="ai-plan-chain">${chips.map(([label, value]) => `<span>${this._escapeHtml(label)}：${this._escapeHtml(value)}</span>`).join('')}</div>` : '<div class="ai-plan-maturity-empty">语义槽位暂未完整抽取。</div>'}
                ${semantic.suggestedRoute ? `<div class="ai-build-note"><strong>语义路线</strong>${this._escapeHtml(semantic.suggestedRoute)}</div>` : ''}
                ${missingFields.length ? `<div class="ai-build-note"><strong>缺失项</strong>${this._escapeHtml(missingFields.join('、'))}</div>` : ''}
                ${semantic.failureCode ? `<div class="ai-build-note"><strong>降级诊断</strong><span class="ai-plan-tech-code">${this._escapeHtml(semantic.failureCode)}</span>${semantic.sanitizedErrorMessage ? ` ${this._escapeHtml(semantic.sanitizedErrorMessage)}` : ''}</div>` : ''}
            </section>
        `;
    },

    _renderPlannerFailureDiagnostics(plan) {
        const diagnostic = plan?.plannerFailure || {};
        const rawFallbackReason = this._sanitizePlanDiagnosticCode(plan?.rawFallbackReason || '');
        const code = this._sanitizePlanDiagnosticCode(diagnostic.code || plan?.plannerFailureCode || '');
        const stageLabel = this._sanitizePlanDiagnosticText(diagnostic.stageLabel || this._formatPlannerFailureStage(diagnostic.stage, code), 80);
        const kind = this._sanitizePlanDiagnosticCode(diagnostic.kind || plan?.sanitizedErrorKind || code);
        const message = this._sanitizePlanDiagnosticText(diagnostic.message || plan?.sanitizedErrorMessage || '');
        const hint = this._sanitizePlanDiagnosticText(diagnostic.hint || this._formatPlannerFailureHint(code));
        const isPlannerFallback = String(plan?.planSource || '').toLowerCase() === 'rule_fallback' &&
            (code || rawFallbackReason.startsWith('planner_') || rawFallbackReason.startsWith('completion_'));
        if (!isPlannerFallback) {
            return '';
        }

        return `
            <div class="ai-build-note">
                <strong>Planner 诊断</strong>
                <div>当前方案为规则兜底草案，不是大模型 Planner 生成结果。</div>
                ${stageLabel ? `<div>模型规划失败阶段：${this._escapeHtml(stageLabel)}</div>` : ''}
                ${kind ? `<div>安全错误类型：<span class="ai-plan-tech-code">${this._escapeHtml(kind)}</span></div>` : ''}
                ${rawFallbackReason ? `<div>fallbackReason：<span class="ai-plan-tech-code">${this._escapeHtml(rawFallbackReason)}</span></div>` : ''}
                ${message ? `<div>安全摘要：${this._escapeHtml(message)}</div>` : ''}
                ${hint ? `<div>${this._escapeHtml(hint)}</div>` : ''}
            </div>
        `;
    },

    _getPlanQuestionResourceBlocker(question, plan = this.pendingVisionPlan) {
        const id = String(question?.id || question?.Id || '').trim();
        const field = this._inferPlanQuestionFieldForQuestion(question, plan) ||
            this._fallbackPlanQuestionField(question, id);
        return this._toArray(plan?.buildReadiness?.blockers)
            .find(blocker => blocker?.category === PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING &&
                ((id && blocker.questionId === id) || (field && blocker.field === field))) || null;
    },

    _formatPlanOptionTag(option) {
        if (this._isInformationalOption(option)) return '仅供阅读';
        if (option?.recommended === true && this._isResolveFieldOption(option)) return '推荐方案';
        if (option?.recommended === true && this._isDeferOption(option)) return '建议暂缓';
        if (this._isDeferOption(option)) return '保持待确认';
        return '可选方案';
    },

    _formatPlanSelectionFeedback(question, selectedValue, plan = this.pendingVisionPlan) {
        const selected = String(selectedValue || '').trim();
        if (!selected) return '';
        const option = this._toArray(question?.options)
            .find(item => String(item?.value || '').trim() === selected);
        if (!option || this._isInformationalOption(option)) return '';
        if (this._isDeferOption(option)) {
            return '已选择暂缓确认，该字段仍会阻断构建。';
        }

        const resourceBlocker = this._getPlanQuestionResourceBlocker(question, plan);
        if (resourceBlocker?.blocksBuild === true) {
            return '资源仍待绑定，当前模式下不能开始构建。';
        }
        if (resourceBlocker) {
            return '可以生成可编辑草稿，部署前仍需绑定资源。';
        }

        return '已确认，该选择可用于构建判断。';
    },

    _renderPlanQuestion(question, selectedValue) {
        const isCustomValue = selectedValue && !question.options.some(opt => opt.value === selectedValue);
        const selectionFeedback = this._formatPlanSelectionFeedback(question, selectedValue);
        const resourceBlocker = this._getPlanQuestionResourceBlocker(question);
        const isPlanReadOnly = this._isPlanSnapshotReadOnly?.() === true;
        return `
            <article class="ai-plan-question">
                <div class="ai-plan-question-head">
                    <strong>${this._escapeHtml(question.title)}</strong>
                    <span>${this._escapeHtml(question.why)}</span>
                </div>
                <div class="ai-plan-question-default">
                    <b>默认假设</b>
                    <span>${this._escapeHtml(question.defaultAssumption)}</span>
                </div>
                <div class="ai-plan-question-options">
                    ${question.options.map(option => {
                        const selected = String(selectedValue || '') === option.value;
                        const tag = this._formatPlanOptionTag(option);
                        const disabled = this._isInformationalOption(option) || isPlanReadOnly;
                        return `
                            <button
                                class="ai-plan-option ${selected ? 'is-selected' : ''} ${option.recommended ? 'is-recommended' : ''} ${disabled ? 'is-informational' : ''}"
                                type="button"
                                data-plan-question="${this._escapeHtml(question.id)}"
                                data-plan-question-option="${this._escapeHtml(option.value)}"
                                ${disabled ? 'disabled' : ''}
                                aria-pressed="${selected ? 'true' : 'false'}">
                                <span>${this._escapeHtml(option.label)}</span>
                                <strong class="ai-plan-option-tag">${this._escapeHtml(tag)}</strong>
                                ${resourceBlocker && this._isResolveFieldOption(option) ? '<strong class="ai-plan-option-tag">资源待后补</strong>' : ''}
                                <small>${this._escapeHtml(option.description)}</small>
                            </button>
                        `;
                    }).join('')}
                </div>
                <div class="ai-plan-question-custom-input">
                    <input
                        class="ai-plan-custom-input-field"
                        type="text"
                        placeholder="输入自定义回答..."
                        value="${isCustomValue ? this._escapeHtml(selectedValue) : ''}"
                        data-plan-question="${this._escapeHtml(question.id)}"
                        ${isPlanReadOnly ? 'readonly disabled' : ''} />
                    <button
                        class="ai-plan-custom-input-btn"
                        type="button"
                        data-plan-question="${this._escapeHtml(question.id)}"
                        ${isPlanReadOnly ? 'disabled' : ''}>
                        确定
                    </button>
                </div>
                ${selectionFeedback ? `<div class="ai-plan-question-selection-feedback">${this._escapeHtml(selectionFeedback)}</div>` : ''}
            </article>
        `;
    },

    _customInputPlanQuestion(questionId, value) {
        if (!questionId || !this.pendingVisionPlan) return;
        if (this._isWorkspaceMutationBlocked?.()) {
            return;
        }
        const cleanedValue = String(value || '').trim();
        if (!cleanedValue) return;
        if (this._isPlanPlaceholderValue(cleanedValue)) return;

        const question = this._toArray(this.pendingVisionPlan.questions)
            .find(item => String(item?.id || '').trim() === String(questionId || '').trim());
        const field = this._inferPlanQuestionFieldForQuestion(question || { id: questionId }, this.pendingVisionPlan) ||
            this._fallbackPlanQuestionField(question || { id: questionId }, questionId);
        if (!field) return;

        const answer = {
            questionId,
            field,
            value: cleanedValue,
            origin: PLAN_ANSWER_ORIGINS.EXPLICIT_USER_TEXT
        };
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.ANSWER_OPTIMISTIC_SET,
            payload: { answer, question },
            planId: this.agentWorkspaceState?.identity?.planId,
            planHash: this.agentWorkspaceState?.identity?.planHash
        });
        this.planAcceptedRecommendedDefaults = false;
        this._queueWorkspaceSnapshotFlush?.('custom_answer');
        this._submitClarificationBatchIfComplete?.('custom_answer');
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderAgentWorkspaceOverview();
    },

    _selectPlanQuestionOption(questionId, value) {
        if (!questionId || !value || !this.pendingVisionPlan) return;
        if (this._isWorkspaceMutationBlocked?.()) {
            return;
        }
        const selectedValue = String(value || '').trim();
        const question = this._toArray(this.pendingVisionPlan.questions)
            .find(item => String(item?.id || '').trim() === String(questionId || '').trim());
        const selectedOption = this._toArray(question?.options)
            .find(option => String(option?.value || '').trim() === selectedValue);
        if (!selectedOption) {
            return;
        }
        if (this._isInformationalOption(selectedOption)) {
            return;
        }
        const field = this._inferPlanQuestionFieldForQuestion(question || { id: questionId }, this.pendingVisionPlan) ||
            this._fallbackPlanQuestionField(question || { id: questionId }, questionId);
        if (!field) return;
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.SELECTION_SET,
            payload: { questionId, value: selectedValue },
            planId: this.agentWorkspaceState?.identity?.planId,
            planHash: this.agentWorkspaceState?.identity?.planHash
        });
        if (this._isResolveFieldOption(selectedOption) && !this._isPlanPlaceholderValue(selectedValue)) {
            this._dispatchAgentWorkspaceEvent?.({
                type: AgentWorkspaceEventTypes.ANSWER_OPTIMISTIC_SET,
                payload: { answer: {
                questionId,
                field,
                value: selectedValue,
                origin: PLAN_ANSWER_ORIGINS.EXPLICIT_USER_SELECTION
                }, question },
                planId: this.agentWorkspaceState?.identity?.planId,
                planHash: this.agentWorkspaceState?.identity?.planHash
            });
        }
        this.planAcceptedRecommendedDefaults = false;
        this._queueWorkspaceSnapshotFlush?.('question_option');
        this._submitClarificationBatchIfComplete?.('question_option');
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderAgentWorkspaceOverview();
    },

    _submitClarificationBatchIfComplete(reason = 'clarification_batch') {
        const batch = this._toArray(this.agentWorkspaceState?.projection?.clarificationBatch);
        if (!batch.length || batch.some(item => !item?.answered && !item?.deferred)) {
            return false;
        }
        const answers = batch.map(item => item?.answer).filter(Boolean);
        this._dispatchAgentWorkspaceEvent?.({
            type: AgentWorkspaceEventTypes.CLARIFICATION_BATCH_SUBMITTED,
            payload: { answers, reason },
            planId: this.agentWorkspaceState?.identity?.planId,
            planHash: this.agentWorkspaceState?.identity?.planHash
        });
        this._requestPlanReadinessPreview?.(this.pendingVisionPlan, { reason: 'clarification_batch' });
        return true;
    },

    _clearPlanQuestionAnswersForField(field, keepQuestionId = '') {
        const answers = { ...(this.planQuestionAnswers || {}) };
        const normalizedField = String(field || '').trim();
        const keepId = String(keepQuestionId || '').trim();
        if (!normalizedField) return answers;
        for (const key of Object.keys(answers)) {
            const answer = this._normalizePlanAnswer(answers[key]) || this._asObject?.(answers[key]) || answers[key] || {};
            const answerField = this._inferPlanQuestionField(answer.field || answer.Field || key) ||
                String(answer.field || answer.Field || key || '').trim().toLowerCase();
            const answerQuestionId = String(answer.questionId || answer.QuestionId || '').trim();
            if (key === normalizedField ||
                key === keepId ||
                answerField === normalizedField ||
                (answerQuestionId && answerQuestionId === keepId)) {
                delete answers[key];
            }
        }
        return answers;
    },

    _clearPlanQuestionSelectionsForField(field, keepQuestionId = '') {
        const selections = { ...(this.planQuestionSelections || {}) };
        const normalizedField = String(field || '').trim();
        if (!normalizedField || !this.pendingVisionPlan) return selections;
        for (const question of this._toArray(this.pendingVisionPlan.questions)) {
            const id = String(question?.id || '').trim();
            if (!id || id === keepQuestionId) continue;
            const questionField = this._inferPlanQuestionFieldForQuestion(question, this.pendingVisionPlan) ||
                this._fallbackPlanQuestionField(question, id);
            if (questionField === normalizedField) {
                delete selections[id];
            }
        }
        return selections;
    },

    async _startBuildFromCurrentPlan({ acceptedRecommended = false } = {}) {
        if (this.isGenerating) return false;

        if (!this.pendingVisionPlan) {
            this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
            this._addMessage?.('system', '请先完成规划，再开始构建。');
            this._setResultStatusNote?.('请先完成规划，再开始构建。', 'warning');
            this._renderAgentWorkspaceOverview();
            this._renderPlanWorkspace(null);
            this._renderBuildWorkspaceFromAgentRun();
            this._updatePlanBuildActionState();
            return false;
        }

        const plan = this.pendingVisionPlan;
        if (acceptedRecommended) {
            const changed = this._acceptRecommendedPlanAnswers(plan);
            this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
            this._setResultStatusNote?.(
                changed ? '已接受推荐方案，正在校验构建条件…' : '正在校验构建条件…',
                'info'
            );
            if (!changed) {
                this._requestPlanReadinessPreview?.(plan, { acceptedRecommended: true, reason: 'accepted_recommended' });
            }
            this._renderAgentWorkspaceOverview();
            this._renderPlanWorkspace(plan);
            this._renderBuildWorkspaceFromAgentRun();
            this._updatePlanBuildActionState();
            return false;
        }
        const actionState = this._getPlanBuildActionState(plan);
        if (actionState.canStart !== true) {
            this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
            const readinessReason = actionState.statusText || this._getPlanBuildBlockedReason(plan);
            if (!this._getCurrentCanonicalPreview?.(plan) &&
                plan.previewState !== 'validating' &&
                plan.previewState !== 'failed') {
                this._requestPlanReadinessPreview?.(plan, { reason: 'build_click' });
            }
            this._addMessage?.('system', readinessReason);
            this._setResultStatusNote?.(readinessReason, 'warning');
            this._renderAgentWorkspaceOverview();
            this._renderPlanWorkspace(plan);
            this._renderBuildWorkspaceFromAgentRun();
            this._updatePlanBuildActionState();
            return false;
        }

        this.activePlanRequestId = null;
        const flushed = (await this._flushWorkspaceSnapshotBeforeBoundary?.('before_build')) ?? true;
        if (!flushed) {
            this._setResultStatusNote?.('Plan 修改尚未成功保存，已阻止创建 BuildRun。', 'warning');
            return false;
        }

        const buildFromPlan = this._buildStructuredBuildFromPlanRequest(plan, { acceptedRecommended: false });

        return this._dispatchGenerateRequest({
            description: plan.buildPrompt || plan.originalDescription,
            hint: '',
            userMessage: `从计划开始构建：${plan.goal}`,
            attachmentPaths: [],
            existingFlowJson: buildFromPlan.currentFlowSnapshot || null,
            explicitMode: buildFromPlan.buildIntent || 'new',
            templateSelection: buildFromPlan.templateSelection || null,
            clearInput: true,
            skipPlan: true,
            skipPlanSource: 'confirmed_plan',
            buildFromPlan
        });
    },

    _buildStructuredBuildFromPlanRequest(plan, { acceptedRecommended = false } = {}) {
        const acceptedRecommendedDefaults = Boolean(acceptedRecommended || this.planAcceptedRecommendedDefaults);
        const currentFlowSnapshot = this._hasCurrentFlowContext?.()
            ? this._stringifyPlanSnapshot(this._getCurrentFlowJson?.())
            : null;
        const buildIntent = this._resolvePlanBuildIntent(plan, currentFlowSnapshot);
        const templateSelection = this._resolveBuildTemplateSelection(plan);
        const planHash = String(
            plan?.planHash ||
            plan?.rawPlanSnapshot?.planHash ||
            plan?.rawPlanSnapshot?.PlanHash ||
            ''
        ).trim();

        return {
            planId: plan.planId || plan.id || '',
            planHash,
            workspaceExpectedRevision: Number(this.workspaceSnapshotRevision || 0),
            planSnapshot: this._buildPlanSnapshotForBuild(plan),
            confirmedAnswers: this._buildConfirmedPlanAnswers(plan, { acceptedRecommended: acceptedRecommendedDefaults }),
            userSelections: this._buildPlanSelectionMap(plan, { acceptedRecommended: acceptedRecommendedDefaults }),
            acceptedDefaults: this._collectAcceptedDefaultIds(plan, acceptedRecommendedDefaults),
            currentFlowSnapshot,
            templateSelection,
            attachmentSummary: this._buildPlanAttachmentSummary([]),
            operatorCatalogVersion: plan.operatorCatalogVersion || '',
            stationBoundarySummary: plan.stationBoundarySummary || '',
            plcOutputPolicy: plan.plcOutputPolicy || '',
            buildIntent,
            originalUserPrompt: plan.originalDescription || plan.buildPrompt || '',
            acceptedRecommendedDefaults,
            resourceDecisions: this._toArray(this.agentWorkspaceState?.projection?.missingResources)
                .map(resource => {
                    const decision = this.agentWorkspaceState?.resources?.decisionsByKey?.[resource.canonicalId];
                    return decision ? serializeResourceDecision(resource, decision) : null;
                })
                .filter(Boolean),
            requirementMaturity: plan.requirementMaturity || plan.rawPlanSnapshot?.requirementMaturity || plan.rawPlanSnapshot?.RequirementMaturity || null,
            decisionTrace: plan.decisionTrace || plan.rawPlanSnapshot?.decisionTrace || plan.rawPlanSnapshot?.DecisionTrace || null,
            metadataOnly: true
        };
    },

    _resolveBuildTemplateSelection(plan) {
        const contextSummary = plan?.contextSummary || plan?.rawPlanSnapshot?.contextSummary || plan?.rawPlanSnapshot?.ContextSummary || {};
        const contextSelection = {
            mode: contextSummary.templateSelectionMode || contextSummary.TemplateSelectionMode || '',
            templateId: contextSummary.templateId || contextSummary.TemplateId || ''
        };
        const candidates = [
            plan?.templateSelection,
            plan?.rawPlanSnapshot?.templateSelection,
            plan?.rawPlanSnapshot?.TemplateSelection,
            this.nextTemplateSelection,
            contextSelection
        ];

        for (const candidate of candidates) {
            const normalized = this._normalizeTemplateSelection?.(candidate);
            if (normalized) return normalized;
        }

        return null;
    },

    _buildPlanSnapshotForBuild(plan) {
        if (plan?.rawPlanSnapshot) {
            const snapshot = { ...plan.rawPlanSnapshot };
            if (!snapshot.planHash && !snapshot.PlanHash && plan?.planHash) {
                snapshot.planHash = plan.planHash;
            }
            if (!snapshot.templateSelection && !snapshot.TemplateSelection && plan?.templateSelection) {
                snapshot.templateSelection = plan.templateSelection;
            }
            if (!snapshot.requirementMaturity && !snapshot.RequirementMaturity && plan?.requirementMaturity) {
                snapshot.requirementMaturity = plan.requirementMaturity;
            }
            if (!snapshot.semanticExtraction && !snapshot.SemanticExtraction && plan?.semanticExtraction) {
                snapshot.semanticExtraction = plan.semanticExtraction;
            }
            if (!snapshot.decisionTrace && !snapshot.DecisionTrace && plan?.decisionTrace) {
                snapshot.decisionTrace = plan.decisionTrace;
            }
            return snapshot;
        }
        return {
            planId: plan?.planId || plan?.id || '',
            planHash: plan?.planHash || '',
            planSource: plan?.planSource || '',
            fallbackReason: plan?.fallbackReason || '',
            originalUserPrompt: plan?.originalDescription || plan?.buildPrompt || '',
            goal: plan?.goal || '',
            intent: plan?.intent || '',
            confidence: plan?.confidence || 'medium',
            requirementUnderstanding: this._toArray(plan?.understanding),
            recommendedRoute: plan?.route || {},
            clarificationQuestions: this._toArray(plan?.questions),
            recommendedDefaults: this._toArray(plan?.recommendedDefaults),
            risks: this._toArray(plan?.risks),
            acceptanceCriteria: this._toArray(plan?.acceptanceCriteria),
            executablePlan: this._toArray(plan?.steps),
            canBuild: plan?.executable === true,
            blockingReasons: this._toArray(plan?.blockingReasons),
            buildReadiness: plan?.buildReadiness || null,
            nextAction: plan?.nextAction || '',
            contextSummary: plan?.contextSummary || {},
            operatorCatalogVersion: plan?.operatorCatalogVersion || '',
            templateCatalogVersion: plan?.templateCatalogVersion || '',
            templateSelection: plan?.templateSelection || null,
            semanticExtraction: plan?.semanticExtraction || null,
            requirementMaturity: plan?.requirementMaturity || null,
            decisionTrace: plan?.decisionTrace || null,
            stationBoundarySummary: plan?.stationBoundarySummary || '',
            plcOutputPolicy: plan?.plcOutputPolicy || '',
            planWarnings: this._toArray(plan?.planWarnings),
            contractRepairNotes: this._toArray(plan?.contractRepairNotes),
            publicEvents: this._toArray(plan?.publicEvents),
            metadataOnly: true
        };
    },

    _buildPlanSelectionMap(plan, { acceptedRecommended = false } = {}) {
        return Object.fromEntries(this._toArray(plan?.questions)
            .map(question => {
                const value = String(this._getPlanQuestionSelectedValue(question) || '').trim();
                return value ? [question.id, value] : null;
            })
            .filter(Boolean));
    },

    _collectAcceptedDefaultIds(plan, acceptedRecommended = false) {
        const answers = this._buildConfirmedPlanAnswers(plan, { acceptedRecommended });
        const answerByQuestion = new Map(answers.map(answer => [answer.questionId, answer]));
        return this._toArray(plan?.questions)
            .filter(question => {
                const recommended = this._getQuestionRecommendedValue(question);
                const answer = answerByQuestion.get(question.id);
                return Boolean(recommended) &&
                    answer &&
                    String(answer.value || '') === String(recommended || '');
            })
            .map(question => question.id)
            .filter(Boolean);
    },

    _resolvePlanBuildIntent(plan, currentFlowSnapshot = null) {
        const prompt = plan?.originalDescription || plan?.buildPrompt || plan?.goal || '';
        const hasCurrentFlow = Boolean(currentFlowSnapshot);
        const resolved = this._resolveGenerateRequestMode?.('', prompt, hasCurrentFlow) || 'auto';
        return resolved === 'auto' ? 'new' : resolved;
    },

    _handleAgentRunWorkspaceEvent(evt) {
        if (!evt) return;
        this.agentWorkspaceMode = AgentWorkspaceModes.BUILD;
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
    },

    _renderBuildWorkspaceFromAgentRun() {
        const el = this.container?.querySelector('#ai-build-workspace');
        const timeline = this.container?.querySelector('#ai-build-event-timeline');
        const template = this.container?.querySelector('#ai-build-template-match');
        const chain = this.container?.querySelector('#ai-build-operator-chain');
        const parameters = this.container?.querySelector('#ai-build-parameters');
        const checks = this.container?.querySelector('#ai-build-checks');
        const finalDraft = this.container?.querySelector('#ai-build-final-draft');
        if (!el) return;

        el.hidden = this._getWorkspaceViewMode() !== AgentWorkspaceModes.BUILD;
        const events = Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [];
        if (!events.length) {
            if (timeline) {
                timeline.innerHTML = '<div class="ai-followup-empty">Build 阶段已进入资源审计中心，等待后端 AgentRun 公开事件。</div>';
            }
            if (chain) chain.innerHTML = '<div class="ai-followup-empty">算子链会在构建事件返回后显示。</div>';
            if (parameters) parameters.innerHTML = '<div class="ai-followup-empty">参数映射和资源审计任务会在构建结果返回后显示。</div>';
            if (checks) checks.innerHTML = '<div class="ai-followup-empty">ApplyGate 会在构建结果返回后显示。</div>';
            if (finalDraft) finalDraft.innerHTML = '<div class="ai-followup-empty">流程草稿完成后可应用到画布。</div>';
            this._renderBuildPresentation?.();
            return;
        }

        if (timeline) timeline.innerHTML = this._renderBuildTimeline(events);
        if (template) template.innerHTML = this._renderBuildTemplateSummary(events);
        if (chain) chain.innerHTML = this._renderBuildOperatorChain(events);
        if (parameters) parameters.innerHTML = this._renderBuildParameterSummary(events);
        if (checks) checks.innerHTML = this._renderBuildChecks(events);
        if (finalDraft) finalDraft.innerHTML = this._renderBuildFinalDraft(events);
        this._renderBuildPresentation?.();
    },

    _renderBuildTimeline(events) {
        const stageMap = new Map();
        events.forEach(evt => {
            const stage = evt.stage || 'run';
            const current = stageMap.get(stage) || {
                stage,
                status: 'pending',
                title: BUILD_STAGE_LABELS[stage] || stage,
                summary: '',
                sequence: 0
            };
            if (evt.sequence >= current.sequence) {
                current.status = evt.status || current.status;
                current.title = evt.title || current.title;
                current.summary = evt.summary || current.summary;
                current.sequence = evt.sequence;
                current.eventType = evt.eventType;
            }
            stageMap.set(stage, current);
        });

        const ordered = [...stageMap.values()].sort((a, b) => {
            const ai = BUILD_STAGE_ORDER.indexOf(a.stage);
            const bi = BUILD_STAGE_ORDER.indexOf(b.stage);
            return (ai < 0 ? 99 : ai) - (bi < 0 ? 99 : bi) || a.sequence - b.sequence;
        });

        return ordered.map(item => {
            const tone = this._getAgentRunTone?.(item.status, item.eventType) || 'running';
            return `
                <div class="ai-build-timeline-item is-${this._escapeHtml(tone)}">
                    <span class="ai-build-timeline-dot"></span>
                    <div>
                        <strong>${this._escapeHtml(BUILD_STAGE_LABELS[item.stage] || this._localizeDisplayText(item.stage))}</strong>
                        <span>${this._escapeHtml(this._formatBuildWorkspaceText(item.summary || item.title || '', 220))}</span>
                    </div>
                </div>
            `;
        }).join('') + this._renderToolEvidenceTimeline(events);
    },

    _renderToolEvidenceTimeline(events) {
        const buildResult = this._getBuildResult(events);
        const evidence = this._toArray(
            buildResult?.toolEvidenceTimeline ||
            buildResult?.ToolEvidenceTimeline ||
            this._getAgentRunResultPayload(events)?.toolEvidenceTimeline ||
            []
        );
        if (!evidence.length) {
            return '';
        }

        return `
            <div class="ai-workspace-section-title">Build 工具证据</div>
            ${evidence.slice(-16).map(item => {
                const stage = this._sanitizeBuildWorkspaceText(item.stage || item.Stage || '', 80);
                const toolName = this._sanitizeBuildWorkspaceText(item.toolName || item.ToolName || '', 80);
                const status = this._sanitizeBuildWorkspaceText(item.status || item.Status || '', 80);
                const source = this._sanitizeBuildWorkspaceText(item.source || item.Source || '', 120);
                const duration = item.durationMs ?? item.DurationMs ?? '';
                const warning = this._sanitizeBuildWorkspaceText(item.warningCode || item.WarningCode || '', 120);
                const summary = this._formatBuildWorkspaceText(item.outputSummary || item.OutputSummary || '', 260);
                const stageLabel = BUILD_STAGE_LABELS[stage] || this._localizeDisplayText(stage);
                const toolLabel = this._formatToolName(toolName);
                const warningLabel = this._localizeDisplayText(warning);
                const sourceLabel = this._localizeDisplayText(source);
                return `
                    <div class="ai-build-compact-row">
                        <b>${this._escapeHtml(stageLabel)}${toolName ? ` / ${this._escapeHtml(toolLabel)}` : ''}</b>
                        <span>${this._escapeHtml(this._formatBuildStatus(status))}${source ? ` / ${this._escapeHtml(sourceLabel)}` : ''}${duration !== '' ? ` / ${this._escapeHtml(String(duration))} ms` : ''}${warning ? ` / ${this._escapeHtml(warningLabel)}` : ''}</span>
                        <small>${this._escapeHtml(summary)}</small>
                    </div>
                `;
            }).join('')}
        `;
    },

    _renderBuildTemplateSummary(events) {
        const buildResult = this._getBuildResult(events);
        const evidence = this._toArray(buildResult?.toolEvidenceTimeline || buildResult?.ToolEvidenceTimeline);
        const templateEvidence = evidence.filter(item => String(item.stage || item.Stage || '').includes('template')).slice(-3);
        if (templateEvidence.length) {
            return templateEvidence.map(item => `
                <div class="ai-build-compact-row">
                    <b>${this._escapeHtml(this._formatToolName(item.toolName || item.ToolName || 'template_strategy'))}</b>
                    <span>${this._escapeHtml(this._formatBuildWorkspaceText(item.outputSummary || item.OutputSummary || '', 260))}</span>
                </div>
            `).join('');
        }

        const tools = events.filter(evt => {
            const payload = this._asObject?.(evt.payload) || {};
            const name = String(payload.toolName || payload.ToolName || evt.title || '').toLowerCase();
            return name.includes('template') || evt.stage === 'planner';
        }).slice(-4).map(evt => ({
            ...evt,
            title: this._formatBuildWorkspaceText(evt.title || 'template event', 120),
            summary: this._formatBuildWorkspaceText(evt.summary || '', 260)
        }));

        if (!tools.length) {
            return '<div class="ai-followup-empty">模板策略尚未发布。</div>';
        }

        return tools.map(evt => `
            <div class="ai-build-compact-row">
                <b>${this._escapeHtml(this._localizeDisplayText(evt.title || '模板事件'))}</b>
                <span>${this._escapeHtml(this._formatBuildWorkspaceText(evt.summary || '', 260))}</span>
            </div>
        `).join('');
    },

    _renderBuildOperatorChain(events) {
        const buildResult = this._getBuildResult(events);
        const pipeline = this._toArray(buildResult?.operatorPipeline || buildResult?.OperatorPipeline);
        if (pipeline.length) {
            const selectionSource = this._sanitizeBuildWorkspaceText(buildResult?.selectionSource || buildResult?.SelectionSource || '', 120);
            const effectiveRouteId = this._sanitizeBuildWorkspaceText(buildResult?.effectiveRouteId || buildResult?.EffectiveRouteId || '', 120);
            const strategyConfirmed = this._readBooleanField(buildResult, 'strategyConfirmed', 'StrategyConfirmed');
            const strategyConfirmationSource = this._sanitizeBuildWorkspaceText(buildResult?.strategyConfirmationSource || buildResult?.StrategyConfirmationSource || '', 120);
            const parameterStrategy = this._sanitizeBuildWorkspaceText(buildResult?.parameterStrategy || buildResult?.ParameterStrategy || '', 120);
            const unresolvedStrategyBlockers = this._toArray(buildResult?.unresolvedStrategyBlockers || buildResult?.UnresolvedStrategyBlockers)
                .map(value => this._formatBuildWorkspaceText(value, 160))
                .filter(Boolean);
            return `
                <div class="ai-build-compact">
                    <div class="ai-build-compact-row">
                        <b>策略确认</b>
                        <span>${strategyConfirmed ? '已确认' : '未确认'}${strategyConfirmationSource ? ` / ${this._escapeHtml(strategyConfirmationSource)}` : ''}</span>
                    </div>
                    <div class="ai-build-compact-row">
                        <b>有效路线</b>
                        <span>${this._escapeHtml([selectionSource, effectiveRouteId, parameterStrategy].filter(Boolean).join(' / '))}</span>
                    </div>
                    ${unresolvedStrategyBlockers.length ? `<div class="ai-build-compact-row"><b>未解除策略阻断</b><span>${this._escapeHtml(unresolvedStrategyBlockers.join(', '))}</span></div>` : ''}
                </div>
                <div class="ai-plan-chain">
                    ${pipeline.map(rawItem => {
                        const item = { ...(this._asObject?.(rawItem) || rawItem || {}) };
                        item.source = this._formatBuildWorkspaceText(item.source || item.Source || '', 120);
                        item.Source = '';
                        item.repairNote = this._formatBuildWorkspaceText(item.repairNote || item.RepairNote || '', 180);
                        item.RepairNote = '';
                        item.status = this._sanitizeBuildWorkspaceText(item.status || item.Status || '', 80);
                        item.Status = '';
                        const rawType = this._sanitizeBuildWorkspaceText(item.operatorType || item.OperatorType || '', 80);
                        return `<span title="${this._escapeHtml([
                        rawType,
                        this._localizeDisplayText(item.source || item.Source || ''),
                        this._localizeDisplayText(item.repairNote || item.RepairNote || ''),
                        this._formatBuildStatus(item.status || item.Status || '')
                    ].filter(Boolean).join(' / '))}">${this._escapeHtml(this._formatOperatorType(rawType))}</span>`;
                    }).join('')}
                </div>
                ${pipeline.slice(0, 8).map(item => {
                    const source = this._formatBuildWorkspaceText(item.source || item.Source || 'plan', 120);
                    const repair = this._formatBuildWorkspaceText(item.repairNote || item.RepairNote || '', 180);
                    const status = this._sanitizeBuildWorkspaceText(item.status || item.Status || '', 80);
                    const rawType = this._sanitizeBuildWorkspaceText(item.operatorType || item.OperatorType || '', 80);
                    const tempId = this._sanitizeBuildWorkspaceText(item.tempId || item.TempId || '', 80);
                    return `
                        <div class="ai-build-compact-row" title="${this._escapeHtml([tempId, rawType].filter(Boolean).join(' / '))}">
                            <b>${this._escapeHtml(this._formatOperatorType(rawType) || '算子')}</b>
                            <span>${this._escapeHtml(this._localizeDisplayText(source))}${status ? ` / ${this._escapeHtml(this._formatBuildStatus(status))}` : ''}</span>
                            ${tempId || repair ? `<small>${this._escapeHtml([tempId ? `节点 ${tempId}` : '', repair ? this._localizeDisplayText(repair) : ''].filter(Boolean).join(' / '))}</small>` : ''}
                        </div>
                    `;
                }).join('')}
            `;
        }

        const draft = [...events].reverse().find(evt => evt.eventType === 'workflow.draft.updated');
        const payload = this._asObject?.(draft?.payload) || {};
        const operatorTypes = Array.isArray(payload.operatorTypes || payload.OperatorTypes)
            ? (payload.operatorTypes || payload.OperatorTypes)
            : [];
        const safeOperatorTypes = operatorTypes
            .map(op => this._sanitizeBuildWorkspaceText(op, 80))
            .filter(Boolean);
        if (!safeOperatorTypes.length) {
            const planOps = this._toArray(this.pendingVisionPlan?.route?.operators)
                .map(op => this._sanitizeBuildWorkspaceText(op, 80))
                .filter(Boolean);
            if (!planOps.length) {
                return '<div class="ai-followup-empty">流程草稿生成后会显示算子链。</div>';
            }
            return `<div class="ai-plan-chain">${planOps.map(op => `<span title="${this._escapeHtml(op)}">${this._escapeHtml(this._formatOperatorType(op))}</span>`).join('')}</div>`;
        }

        return `<div class="ai-plan-chain">${safeOperatorTypes.map(op => `<span title="${this._escapeHtml(op)}">${this._escapeHtml(this._formatOperatorType(op))}</span>`).join('')}</div>`;
    },

    _renderBuildParameterSummary(events) {
        const resultPayload = this._getAgentRunResultPayload(events);
        const buildResult = this._getBuildResult(events);
        const mappings = this._toArray(buildResult?.parameterMapping || buildResult?.ParameterMapping);
        const pending = this._toArray(resultPayload?.pendingParameters || resultPayload?.PendingParameters);
        const buildPending = this._toArray(buildResult?.pendingParameters || buildResult?.PendingParameters);
        const missing = this._toArray(resultPayload?.missingResources || resultPayload?.MissingResources);
        const buildMissing = this._toArray(buildResult?.missingResources || buildResult?.MissingResources);
        const effectivePending = pending.length ? pending : buildPending;
        const effectiveMissing = missing.length ? missing : buildMissing;
        const latest = [...events].reverse().find(evt => evt.payload);
        const latestPayload = this._asObject?.(latest?.payload) || {};
        const missingCount = Number(latestPayload.missingResourceCount ?? latestPayload.MissingResourceCount ?? effectiveMissing.length);
        const pendingCount = Number(latestPayload.pendingParameterCount ?? latestPayload.PendingParameterCount ?? effectivePending.length);
        const mappingRows = mappings.slice(0, 8).map(item => {
            const tempId = this._sanitizeBuildWorkspaceText(item.tempId || item.TempId || '', 80);
            const operatorType = this._sanitizeBuildWorkspaceText(item.operatorType || item.OperatorType || '', 80);
            const parameterName = this._sanitizeBuildWorkspaceText(item.parameterName || item.ParameterName || item.name || item.Name || '', 80);
            const valueSummary = this._formatBuildWorkspaceText(item.valueSummary ?? item.ValueSummary ?? item.value ?? item.Value ?? '', 220);
            const source = this._formatBuildWorkspaceText(item.source || item.Source || 'mapped', 120);
            const pendingLabel = item.pending || item.Pending ? ' / 待确认' : '';
            const titleParts = [tempId, operatorType, parameterName, source]
                .filter(Boolean)
                .map(value => this._localizeDisplayText(value))
                .join(' / ');
            return `
                <div class="ai-build-compact-row" title="${this._escapeHtml(titleParts)}">
                    <b>${this._escapeHtml(this._formatResourceReference(`${tempId}.${parameterName}`))}</b>
                    <span>${this._escapeHtml(this._localizeDisplayText(valueSummary))}</span>
                    <small>${this._escapeHtml(this._localizeDisplayText(source))}${pendingLabel}</small>
                </div>
            `;
        }).join('');

        return `
            <div class="ai-build-metric-row">
                <span><small>待确认参数</small><b>${this._escapeHtml(String(Number.isFinite(pendingCount) ? pendingCount : 0))}</b></span>
                <span><small>缺失资源</small><b>${this._escapeHtml(String(Number.isFinite(missingCount) ? missingCount : 0))}</b></span>
            </div>
            ${mappingRows ? `<div class="ai-workspace-section-title">参数映射</div>${mappingRows}` : ''}
            ${(effectivePending.length || effectiveMissing.length) ? '<div class="ai-build-note">资源审计任务卡会在左侧下方集中显示；流程页只用于应用后的节点级复核和细调。</div>' : '<div class="ai-build-note">暂无待确认参数详情。</div>'}
        `;
    },

    _renderBuildChecks(events) {
        const resultPayload = this._getAgentRunResultPayload(events);
        const compatibilityState = this._getBuildArtifactFlowCompatibilityState(resultPayload, events);
        if (compatibilityState.status === LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE) {
            const publicMessage = this._sanitizeAssistantFailureText?.(compatibilityState.publicMessage, 360) ||
                compatibilityState.publicMessage;
            return `
                <div class="ai-build-check is-blocked">
                    <strong>应用门禁：已阻断</strong>
                    <span>${this._escapeHtml(publicMessage)}</span>
                </div>
            `;
        }

        const buildResult = this._getBuildResult(events);
        const applyGate = buildResult?.applyGate || buildResult?.ApplyGate || resultPayload?.applyGate;
        const readiness = buildResult?.readinessReport || buildResult?.ReadinessReport || null;
        const firstFix = this._sanitizeAssistantFailureText?.(buildResult?.firstFixRecommendation || buildResult?.FirstFixRecommendation || resultPayload?.firstFixRecommendation || '', 240) || '';
        if (applyGate) {
            const gate = this._asObject?.(applyGate) || {};
            const canvasReady = this._readBooleanField(gate, 'canvasApplyReady', 'CanvasApplyReady');
            const runtimeReady = this._readBooleanField(gate, 'runtimeDraftReady', 'RuntimeDraftReady');
            const deploymentReady = this._readBooleanField(gate, 'deploymentReady', 'DeploymentReady');
            const blocked = this._readBooleanField(gate, 'blocked', 'Blocked');
            return `
                <div class="ai-build-check is-${blocked ? 'blocked' : 'completed'}">
                    <strong>应用门禁：${this._escapeHtml(this._formatGateStatus(gate.status || gate.Status || 'unknown'))}</strong>
                    <span>画布可应用：${canvasReady ? '是' : '否'} / 运行草稿：${runtimeReady ? '就绪' : '阻断'} / 部署：${deploymentReady ? '就绪' : '阻断'}</span>
                    ${firstFix ? `<em class="ai-first-fix">First Fix：${this._escapeHtml(this._localizeDisplayText(firstFix))}</em>` : ''}
                </div>
                ${readiness ? `<div class="ai-build-note">就绪门禁已写入可回放 BuildResult。</div>` : ''}
            `;
        }

        const checkTypes = new Set([
            'readiness.checked',
            'manifest.dryrun.completed',
            'package.readiness.checked',
            'station.compatibility.completed',
            'operator.contract.completed',
            'release.review.completed'
        ]);
        const checks = events.filter(evt => checkTypes.has(evt.eventType));
        if (!checks.length) {
            return '<div class="ai-followup-empty">就绪检查和元数据预演尚未完成。</div>';
        }

        return checks.map(evt => {
            const tone = this._getAgentRunTone?.(evt.status, evt.eventType) || 'running';
            const payload = this._asObject?.(evt.payload) || {};
            const firstFix = this._sanitizeAssistantFailureText?.(payload.firstFixRecommendation || payload.FirstFixRecommendation || '', 240) || '';
            const title = this._formatBuildWorkspaceText(evt.title || BUILD_STAGE_LABELS[evt.stage] || evt.stage, 160);
            const summary = this._formatBuildWorkspaceText(evt.summary || '', 260);
            return `
                <div class="ai-build-check is-${this._escapeHtml(tone)}">
                    <strong>${this._escapeHtml(title)}</strong>
                    <span>${this._escapeHtml(summary)}</span>
                    ${firstFix ? `<em>${this._escapeHtml(this._localizeDisplayText(firstFix))}</em>` : ''}
                </div>
            `;
        }).join('');
    },

    _renderBuildFinalDraft(events) {
        const resultPayload = this._getAgentRunResultPayload(events);
        const buildResult = this._getBuildResult(events);
        const flow = this._getResultFlowForCanvas(resultPayload) ||
            this._getResultFlowForCanvas(this.currentResult) ||
            null;
        const ops = flow ? this._extractOperators(flow) : [];
        const connections = flow ? this._extractConnections(flow) : [];
        const terminal = events.find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(evt.eventType));
        const diff = buildResult?.workflowDiff || buildResult?.WorkflowDiff || resultPayload?.workflowDiff || null;
        const compatibilityState = this._getBuildArtifactFlowCompatibilityState(resultPayload, events);
        if (!terminal) {
            return '<div class="ai-followup-empty">构建完成后会显示最终可编辑流程草稿。</div>';
        }

        if (compatibilityState.status === LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE) {
            const publicMessage = this._sanitizeAssistantFailureText?.(compatibilityState.publicMessage, 360) ||
                compatibilityState.publicMessage;
            return `
                <div class="ai-build-check is-blocked">
                    <strong>无法应用构建结果</strong>
                    <span>${this._escapeHtml(publicMessage)}</span>
                </div>
                <details class="ai-plan-diagnostics">
                    <summary>兼容诊断</summary>
                    <div>${this._escapeHtml(compatibilityState.code)}</div>
                </details>
            `;
        }

        if (!flow) {
            return `<div class="ai-build-note">${this._escapeHtml(this._sanitizeAssistantFailureText?.(terminal.summary || '构建完成，但未收到流程草稿。', 360) || '构建完成，但未收到流程草稿。')}</div>`;
        }

        return `
            <div class="ai-build-final-ready">
                <strong>可编辑草稿已就绪</strong>
                <span>${this._escapeHtml(String(ops.length))} 个算子 / ${this._escapeHtml(String(connections.length))} 条连线</span>
            </div>
            ${this.workbenchState === AiWorkbenchStates.APPLIED ? `
                <div class="ai-build-note">
                    <strong>已应用到画布</strong>
                    仍需补齐项和已确认资源会继续保留在左侧审计记录中。应用到画布后，可在流程页点击算子进行细节复核与微调；流程页修改不会绕过部署门禁。
                </div>
            ` : ''}
            ${diff ? this._renderWorkflowDiff(diff) : ''}
        `;
    },

    _renderWorkflowDiff(diff) {
        const item = this._asObject?.(diff) || {};
        const added = this._toArray(item.addedNodes || item.AddedNodes);
        const preserved = this._toArray(item.preservedNodes || item.PreservedNodes);
        const pending = this._toArray(item.pendingParameters || item.PendingParameters);
        const blockers = this._toArray(item.deploymentBlockers || item.DeploymentBlockers);
        const preview = (values, kind = '') => values.slice(0, 4)
            .map(value => {
                const label = this._formatWorkflowDiffValue(value, kind);
                return `<span title="${this._escapeHtml(label)}">${this._escapeHtml(label)}</span>`;
            })
            .join('');
        return `
            <div class="ai-workspace-section-title">流程差异</div>
            <div class="ai-build-metric-row">
                <span><small>新增节点</small><b>${this._escapeHtml(String(added.length))}</b></span>
                <span><small>保留节点</small><b>${this._escapeHtml(String(preserved.length))}</b></span>
                <span><small>待确认</small><b>${this._escapeHtml(String(pending.length))}</b></span>
                <span><small>部署阻断</small><b>${this._escapeHtml(String(blockers.length))}</b></span>
            </div>
            ${(added.length || preserved.length || pending.length || blockers.length) ? `
                <div class="ai-build-diff-tags">
                    ${preview(added, 'added')}
                    ${preview(preserved, 'preserved')}
                    ${preview(pending, 'pending')}
                    ${preview(blockers, 'blocker')}
                </div>
            ` : ''}
        `;
    },

    _getAgentRunResultPayload(events = this.activeAgentRunEvents) {
        const terminal = [...(events || [])].reverse()
            .find(evt => ['run.completed', 'run.failed'].includes(evt.eventType) && evt.payload);
        return this._asObject?.(terminal?.payload) || {};
    },

    _getBuildResult(events = this.activeAgentRunEvents) {
        const payload = this._getAgentRunResultPayload(events);
        const buildResult = this._getPayloadBuildResult(payload);
        if (buildResult) {
            return buildResult;
        }

        return Array.isArray(events) && events.length > 0
            ? null
            : this._getPayloadBuildResult(this.currentResult);
    },

    _hasBuildArtifactValue(source, names) {
        if (!source || typeof source !== 'object') return false;

        for (const name of names) {
            if (!Object.prototype.hasOwnProperty.call(source, name)) continue;
            const value = source[name];
            if (value === null || value === undefined) continue;
            if (Array.isArray(value)) return value.length > 0;
            if (typeof value === 'object') return Object.keys(value).length > 0;
            if (String(value).trim()) return true;
        }

        return false;
    },

    _getBuildArtifactTextValue(source, names) {
        if (!source || typeof source !== 'object') return '';

        for (const name of names) {
            if (!Object.prototype.hasOwnProperty.call(source, name)) continue;
            const value = source[name];
            if (value === null || value === undefined) continue;
            const text = String(value).trim();
            if (text) return text;
        }

        return '';
    },

    _getBuildArtifactBooleanValue(source, names) {
        if (!source || typeof source !== 'object') return null;

        for (const name of names) {
            if (!Object.prototype.hasOwnProperty.call(source, name)) continue;
            const value = source[name];
            if (typeof value === 'boolean') return value;
            if (typeof value === 'string') {
                const normalized = value.trim().toLowerCase();
                if (normalized === 'true') return true;
                if (normalized === 'false') return false;
            }
        }

        return null;
    },

    _getBuildArtifactTerminalContext(payload = this.currentResult, events = this.activeAgentRunEvents) {
        const obj = this._asObject?.(payload) || {};
        const eventList = Array.isArray(events) ? events : [];
        const eventTypes = new Set(eventList.map(evt => String(evt?.eventType || '').trim().toLowerCase()).filter(Boolean));
        const success = this._getBuildArtifactBooleanValue(obj, ['success', 'Success']);
        const completionStatus = this._getBuildArtifactTextValue(obj, ['completionStatus', 'CompletionStatus']).toLowerCase();
        const status = this._getBuildArtifactTextValue(obj, ['status', 'Status']).toLowerCase();
        const interactionState = this._getBuildArtifactTextValue(obj, ['interactionState', 'InteractionState']).toLowerCase();
        const failureType = this._getBuildArtifactTextValue(obj, ['failureType', 'FailureType']);
        const normalizedFailureType = failureType.toLowerCase();
        const kind = this._getBuildArtifactTextValue(obj, ['kind', 'Kind', 'projectionKind', 'ProjectionKind']).toLowerCase();
        const compatibilityMarker = [
            this._getBuildArtifactTextValue(obj, ['buildCompatibilityStatus', 'BuildCompatibilityStatus']),
            this._getBuildArtifactTextValue(obj, ['compatibilityDiagnosticCode', 'CompatibilityDiagnosticCode'])
        ].some(value => value === LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE);

        return {
            eventTypes,
            success,
            completionStatus,
            status,
            interactionState,
            failureType,
            normalizedFailureType,
            kind,
            compatibilityMarker
        };
    },

    _getNonCompletedBuildTerminalStatus(context) {
        if (!context) return '';

        if (
            context.eventTypes.has('run.cancelled') ||
            context.completionStatus === 'cancelled' ||
            context.completionStatus === 'canceled' ||
            context.interactionState === 'cancelled'
        ) {
            return 'terminal_cancelled_without_flow';
        }

        if (
            context.completionStatus === 'clarification_required' ||
            context.interactionState === 'clarifying'
        ) {
            return 'terminal_clarification_without_flow';
        }

        if (
            context.eventTypes.has('run.failed') ||
            context.success === false ||
            context.completionStatus === 'failed' ||
            context.interactionState === 'failed' ||
            (context.failureType && context.normalizedFailureType !== LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE) ||
            context.kind === 'assistant_agent_failure'
        ) {
            return 'terminal_failed_without_flow';
        }

        return '';
    },

    _hasCompletedBuildTerminalEvidence(context) {
        if (!context) return false;

        return context.eventTypes.has('run.completed') ||
            context.success === true ||
            context.completionStatus === 'completed' ||
            (context.kind === 'assistant_agent_result' && context.status === 'completed');
    },

    _getBuildArtifactFlowCompatibilityState(payload = this.currentResult, events = this.activeAgentRunEvents) {
        const obj = this._asObject?.(payload) || {};
        const eventList = Array.isArray(events) ? events : [];
        const buildResult = this._getPayloadBuildResult(obj);
        const flow = this._getResultFlowForCanvas(obj);
        if (flow && this._extractOperators(flow).length > 0) {
            return {
                status: 'canonical_flow_available',
                flow,
                buildResult
            };
        }

        const terminalContext = this._getBuildArtifactTerminalContext(obj, eventList);
        if (terminalContext.compatibilityMarker) {
            return {
                status: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
                code: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
                publicMessage: LEGACY_BUILD_MISSING_CANONICAL_FLOW_MESSAGE,
                flow: null,
                buildResult,
                terminal: terminalContext
            };
        }

        const nonCompletedTerminalStatus = this._getNonCompletedBuildTerminalStatus(terminalContext);
        if (nonCompletedTerminalStatus) {
            return {
                status: nonCompletedTerminalStatus,
                flow: null,
                buildResult,
                terminal: terminalContext
            };
        }

        const hasTopLevelWorkflowDraft = this._hasBuildArtifactValue(obj, ['workflowDraft', 'WorkflowDraft']);
        const hasBuildWorkflowDraft = this._hasBuildArtifactValue(buildResult, ['workflowDraft', 'WorkflowDraft']);
        const hasTopLevelOperatorPipeline = this._hasBuildArtifactValue(obj, ['operatorPipeline', 'OperatorPipeline']);
        const hasBuildOperatorPipeline = this._hasBuildArtifactValue(buildResult, ['operatorPipeline', 'OperatorPipeline']);
        const hasTopLevelParameterMapping = this._hasBuildArtifactValue(obj, ['parameterMapping', 'ParameterMapping']);
        const hasBuildParameterMapping = this._hasBuildArtifactValue(buildResult, ['parameterMapping', 'ParameterMapping']);
        const hasTopLevelWorkflowDiff = this._hasBuildArtifactValue(obj, ['workflowDiff', 'WorkflowDiff']);
        const hasBuildWorkflowDiff = this._hasBuildArtifactValue(buildResult, ['workflowDiff', 'WorkflowDiff']);
        const hasBuildArtifact = Boolean(
            buildResult ||
            hasTopLevelWorkflowDraft ||
            hasBuildWorkflowDraft ||
            hasTopLevelOperatorPipeline ||
            hasBuildOperatorPipeline ||
            hasTopLevelParameterMapping ||
            hasBuildParameterMapping ||
            hasTopLevelWorkflowDiff ||
            hasBuildWorkflowDiff
        );

        if (!this._hasCompletedBuildTerminalEvidence(terminalContext) || !hasBuildArtifact) {
            return {
                status: 'no_build_artifact',
                flow: null,
                buildResult,
                terminal: terminalContext
            };
        }

        return {
            status: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
            code: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
            publicMessage: LEGACY_BUILD_MISSING_CANONICAL_FLOW_MESSAGE,
            flow: null,
            buildResult,
            hasRunCompletedTerminal: terminalContext.eventTypes.has('run.completed'),
            hasWorkflowDraft: hasTopLevelWorkflowDraft || hasBuildWorkflowDraft,
            hasOperatorPipeline: hasTopLevelOperatorPipeline || hasBuildOperatorPipeline
        };
    },

    _buildLegacyMissingCanonicalFlowResult(payload, compatibilityState) {
        const obj = this._asObject?.(payload) || {};
        const buildResult = compatibilityState?.buildResult || this._getPayloadBuildResult(obj) || null;
        const applyGate = {
            canvasApplyReady: false,
            runtimeDraftReady: false,
            deploymentReady: false,
            blocked: true,
            status: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
            firstFixRecommendation: '请基于原计划重新构建。',
            metadataOnly: true
        };

        return {
            ...obj,
            status: 'failed',
            Status: 'failed',
            success: false,
            Success: false,
            completionStatus: 'failed',
            CompletionStatus: 'failed',
            failureType: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
            FailureType: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
            interactionState: 'failed',
            InteractionState: 'failed',
            aiExplanation: LEGACY_BUILD_MISSING_CANONICAL_FLOW_MESSAGE,
            AiExplanation: LEGACY_BUILD_MISSING_CANONICAL_FLOW_MESSAGE,
            buildCompatibilityStatus: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
            compatibilityDiagnosticCode: LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE,
            buildResult,
            BuildResult: buildResult,
            applyGate,
            ApplyGate: applyGate,
            pendingParameters: this._toArray(obj.pendingParameters || obj.PendingParameters).length
                ? this._toArray(obj.pendingParameters || obj.PendingParameters)
                : this._toArray(buildResult?.pendingParameters || buildResult?.PendingParameters),
            missingResources: this._toArray(obj.missingResources || obj.MissingResources).length
                ? this._toArray(obj.missingResources || obj.MissingResources)
                : this._toArray(buildResult?.missingResources || buildResult?.MissingResources),
            flow: null,
            Flow: null
        };
    },

    _applyAgentRunResultPayload(evt) {
        const payload = this._asObject?.(evt?.payload) || {};
        if (evt.eventType !== 'run.completed') {
            return false;
        }

        if (!this._isAgentRunTerminalPlanCurrent(evt, payload)) {
            return false;
        }

        const buildResult = this._getPayloadBuildResult(payload);
        const flow = this._getResultFlowForCanvas(payload);
        if (!flow) {
            const compatibilityState = this._getBuildArtifactFlowCompatibilityState(payload, [evt, ...(this.activeAgentRunEvents || [])]);
            if (compatibilityState.status === LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE) {
                const result = this._buildLegacyMissingCanonicalFlowResult(payload, compatibilityState);
                this._setCurrentResult(result);
                this._workbenchStageTimeline = result.stageTimeline || result.StageTimeline || this._workbenchStageTimeline || [];
                if (this.container) {
                    this._displayResult(result, {
                        appendChatMessage: false,
                        assistantTurn: this.activeAssistantTurn
                    });
                    this._renderBuildWorkspaceFromAgentRun();
                }
                this._setResultStatusNote?.(compatibilityState.publicMessage, 'warning');
            }
            return false;
        }

        const result = {
            success: true,
            completionStatus: 'completed',
            sessionId: payload.sessionId || payload.SessionId || this.sessionId,
            ...payload,
            buildResult: payload.buildResult || payload.BuildResult || buildResult || null,
            pendingParameters: this._toArray(payload.pendingParameters || payload.PendingParameters).length
                ? this._toArray(payload.pendingParameters || payload.PendingParameters)
                : this._toArray(buildResult?.pendingParameters || buildResult?.PendingParameters),
            missingResources: this._toArray(payload.missingResources || payload.MissingResources).length
                ? this._toArray(payload.missingResources || payload.MissingResources)
                : this._toArray(buildResult?.missingResources || buildResult?.MissingResources),
            flow,
            Flow: flow
        };
        this._setCurrentResult(result);
        this._resetPendingDraftState();
        this._rebuildPendingOperatorBindings({
            pending: this._resolvePendingParametersForDraft(result),
            flow,
            preferIndexFallback: true
        });
        this._workbenchStageTimeline = result.stageTimeline || result.StageTimeline || this._workbenchStageTimeline || [];
        this._displayResult(result, {
            appendChatMessage: false,
            assistantTurn: this.activeAssistantTurn
        });
        this._showDraftBuildCompletionNotice(result, payload);
        this._renderBuildWorkspaceFromAgentRun();
        return true;
    },

    _showDraftBuildCompletionNotice(result, payload = {}) {
        const mode = this._normalizeRequirementMode?.(
            payload.requirementMode ||
            payload.RequirementMode ||
            this.requirementMode
        ) || 'strict';
        if (mode !== 'draft') return;

        const preview = this._getCurrentCanonicalPreview?.(this.pendingVisionPlan);
        const pendingCount = Number(preview?.pendingConfirmationCount) || 0;
        const resourceCount = Number(preview?.resourcePendingCount) ||
            this._toArray(result?.missingResources || result?.MissingResources).length;
        this._setResultStatusNote?.(
            `可编辑草稿已生成。仍有 ${pendingCount} 项待确认、${resourceCount} 项资源待补。当前不具备部署条件。`,
            'warning'
        );
    },

    _isAgentRunTerminalPlanCurrent(evt, payload) {
        const runId = String(evt?.runId || evt?.RunId || '').trim();
        if (this.activeAgentRunId && runId && runId !== this.activeAgentRunId) {
            this.agentRunStaleEventCount = (this.agentRunStaleEventCount || 0) + 1;
            return false;
        }

        const plan = this.pendingVisionPlan;
        if (!plan) return true;
        const data = this._asObject?.(payload) || {};
        const buildReplay = this._asObject?.(data.buildFromPlan || data.BuildFromPlan) || {};
        const incomingPlanId = String(
            data.planId ||
            data.PlanId ||
            buildReplay.planId ||
            buildReplay.PlanId ||
            ''
        ).trim();
        const currentPlanId = String(plan.planId || plan.id || '').trim();
        if (!incomingPlanId || !currentPlanId || incomingPlanId !== currentPlanId) {
            this.agentRunStaleEventCount = (this.agentRunStaleEventCount || 0) + 1;
            return false;
        }

        const incomingPlanHash = String(
            data.planHash ||
            data.PlanHash ||
            buildReplay.planHash ||
            buildReplay.PlanHash ||
            ''
        ).trim();
        const currentPlanHash = String(plan.planHash || '').trim();
        if (incomingPlanHash && currentPlanHash && incomingPlanHash !== currentPlanHash) {
            this.agentRunStaleEventCount = (this.agentRunStaleEventCount || 0) + 1;
            return false;
        }

        return true;
    },

    _getPayloadBuildResult(payload = this.currentResult) {
        const obj = this._asObject?.(payload) || {};
        const buildResult = obj.buildResult || obj.BuildResult || null;
        return buildResult ? (this._asObject?.(buildResult) || null) : null;
    },

    _getPayloadApplyGate(payload = this.currentResult) {
        const obj = this._asObject?.(payload) || {};
        const buildResult = this._getPayloadBuildResult(obj);
        const applyGate =
            obj.applyGate ||
            obj.ApplyGate ||
            buildResult?.applyGate ||
            buildResult?.ApplyGate ||
            null;
        return applyGate ? (this._asObject?.(applyGate) || null) : null;
    },

    _isCanvasApplyReadyForResult(result = this.currentResult) {
        const compatibilityState = this._getBuildArtifactFlowCompatibilityState(result);
        if (compatibilityState.status === LEGACY_BUILD_MISSING_CANONICAL_FLOW_CODE) {
            return false;
        }

        const gate = this._getPayloadApplyGate(result);
        if (!gate) return true;
        const canvasReady = this._readBooleanField(gate, 'canvasApplyReady', 'CanvasApplyReady');
        const blocked = this._readBooleanField(gate, 'blocked', 'Blocked');
        return canvasReady && !blocked;
    },

    _readBooleanField(obj, camelName, pascalName) {
        if (!obj || typeof obj !== 'object') return false;
        const value = obj[camelName] ?? obj[pascalName];
        return value === true || String(value).toLowerCase() === 'true';
    },

    _getResultFlowForCanvas(result = this.currentResult) {
        const obj = this._asObject?.(result) || {};
        const directFlow = this._normalizeWorkflowDraftForCanvas(obj.flow || obj.Flow);
        if (directFlow && this._extractOperators(directFlow).length > 0) {
            return directFlow;
        }

        const buildResult = this._getPayloadBuildResult(obj);
        if (!buildResult) return null;
        const fallback = this._normalizeWorkflowDraftForCanvas(buildResult.flow || buildResult.Flow, buildResult);

        if (!fallback || this._extractOperators(fallback).length === 0) {
            return null;
        }

        return fallback;
    },

    _normalizeWorkflowDraftForCanvas(flow, buildResult = null) {
        const source = this._cloneJsonSafe(flow);
        if (!source || typeof source !== 'object') return null;

        const flowData = source.project?.flow || source.flow || source;
        const rawOperators = this._toArray(flowData.operators || flowData.Operators || flowData.nodes || flowData.Nodes);
        if (!rawOperators.length) return null;

        const operators = rawOperators.map((op, index) => this._normalizeDraftOperatorForCanvas(op, index));
        const rawConnections = this._toArray(flowData.connections || flowData.Connections);
        const connections = this._normalizeDraftConnectionsForCanvas(rawConnections, operators);
        return {
            ...flowData,
            operators,
            connections,
            metadataOnly: Boolean(flowData.metadataOnly ?? flowData.MetadataOnly ?? buildResult?.metadataOnly ?? buildResult?.MetadataOnly ?? true)
        };
    },

    _normalizeDraftOperatorForCanvas(operator, index) {
        const op = this._asObject?.(operator) || {};
        const id = String(op.id || op.Id || op.tempId || op.TempId || `op_${index + 1}`).trim();
        const preferredType = op.operatorType ?? op.OperatorType;
        const declaredType = String(preferredType ?? '').trim()
            ? preferredType
            : (op.type ?? op.Type);
        const inferredType = [op.displayName, op.DisplayName, op.name, op.Name, op.title, op.Title]
            .map(label => this._inferDraftCanvasOperatorTypeFromLabel(label))
            .find(Boolean) || '';
        const type = this._normalizeDraftCanvasOperatorType(declaredType, inferredType || 'DeepLearning');
        const rawMetadata = this._asObject?.(op.metadata || op.Metadata) || {};
        const agentTempId = String(rawMetadata.agentTempId || rawMetadata.AgentTempId || op.agentTempId || op.AgentTempId || op.tempId || op.TempId || '').trim();
        const metadata = agentTempId
            ? { ...rawMetadata, agentTempId }
            : rawMetadata;
        const systemName = AI_OPERATOR_LABELS[type] || type || '未命名算子';
        const name = this._sanitizeDraftCanvasLabel(
            systemName,
            type || '未命名算子',
            160
        ) || systemName || '未命名算子';
        const description = this._sanitizeDraftCanvasLabel(op.description || op.Description || '', '', 260);
        const parameters = this._normalizeDraftParametersForCanvas(op.parameters || op.Parameters);
        const inputPorts = this._normalizeDraftPortsForCanvas(op.inputPorts || op.InputPorts || op.inputs || op.Inputs, id, type, false);
        const outputPorts = this._normalizeDraftPortsForCanvas(op.outputPorts || op.OutputPorts || op.outputs || op.Outputs, id, type, true);
        return {
            ...op,
            id,
            name,
            Name: name,
            title: name,
            Title: name,
            displayName: name,
            DisplayName: name,
            description,
            Description: description,
            type,
            Type: type,
            operatorType: type,
            OperatorType: type,
            metadata,
            x: Number(op.x ?? op.X ?? 160 + index * 180),
            y: Number(op.y ?? op.Y ?? 180),
            inputPorts,
            outputPorts,
            parameters,
            isEnabled: op.isEnabled ?? op.IsEnabled ?? true
        };
    },

    _normalizeDraftParametersForCanvas(parameters) {
        if (Array.isArray(parameters)) {
            return parameters.map(item => {
                const param = this._asObject?.(item) || {};
                const name = param.name || param.Name || '';
                const value = param.value ?? param.Value ?? param.defaultValue ?? param.DefaultValue ?? '';
                const displayName = this._sanitizeDraftCanvasLabel(param.displayName || param.DisplayName || name, name || '参数', 120) || '参数';
                const dataType = this._normalizeDraftCanvasDataType(param.dataType || param.DataType || param.type || param.Type || 'string', 'string');
                const description = this._sanitizeDraftCanvasLabel(param.description || param.Description || '', '', 220);
                return {
                    ...param,
                    name,
                    Name: name,
                    displayName,
                    DisplayName: displayName,
                    description,
                    Description: description,
                    value,
                    defaultValue: param.defaultValue ?? param.DefaultValue ?? value,
                    dataType,
                    DataType: dataType,
                    type: dataType,
                    Type: dataType,
                    isRequired: Boolean(param.isRequired ?? param.IsRequired ?? this._isPendingValueSummary(value))
                };
            });
        }

        const obj = this._asObject?.(parameters) || {};
        return Object.keys(obj).map(name => {
            const value = obj[name];
            const displayName = this._sanitizeDraftCanvasLabel(name, '参数', 120) || '参数';
            return {
                name,
                Name: name,
                displayName,
                DisplayName: displayName,
                description: '',
                Description: '',
                value,
                defaultValue: value,
                dataType: 'string',
                DataType: 'string',
                type: 'string',
                Type: 'string',
                isRequired: this._isPendingValueSummary(value)
            };
        });
    },

    _normalizeDraftPortsForCanvas(ports, operatorId, operatorType, isOutput) {
        const raw = this._toArray(ports);
        if (raw.length) {
            return raw.map((port, index) => {
                const item = this._asObject?.(port) || {};
                const fallbackName = isOutput ? 'Output' : 'Input';
                const name = this._sanitizeDraftCanvasLabel(item.name || item.Name || item.portName || item.PortName || fallbackName, fallbackName, 80) || fallbackName;
                const displayName = this._sanitizeDraftCanvasLabel(item.displayName || item.DisplayName || name, name, 80) || name;
                const declaredDataType = item.dataType ?? item.DataType ?? item.type ?? item.Type;
                const dataType = this._normalizeDraftCanvasDataType(
                    declaredDataType,
                    String(name).toLowerCase().includes('image') ? 'Image' : 'Any'
                );
                const description = this._sanitizeDraftCanvasLabel(item.description || item.Description || '', '', 180);
                return {
                    ...item,
                    id: item.id || item.Id || `${operatorId}_${isOutput ? 'out' : 'in'}_${index}`,
                    name,
                    Name: name,
                    portName: name,
                    PortName: name,
                    displayName,
                    DisplayName: displayName,
                    dataType,
                    DataType: dataType,
                    type: dataType,
                    Type: dataType,
                    description,
                    Description: description,
                    direction: item.direction ?? item.Direction ?? (isOutput ? 1 : 0),
                    isRequired: Boolean(item.isRequired ?? item.IsRequired ?? !isOutput)
                };
            });
        }

        const names = this._defaultPortNamesForOperator(operatorType, isOutput);
        return names.map((name, index) => ({
            id: `${operatorId}_${isOutput ? 'out' : 'in'}_${index}`,
            name,
            displayName: name,
            dataType: String(name).toLowerCase().includes('image') ? 'Image' : 'Any',
            direction: isOutput ? 1 : 0,
            isRequired: !isOutput
        }));
    },

    _defaultPortNamesForOperator(operatorType, isOutput) {
        const type = String(operatorType || '').toLowerCase();
        if (isOutput) {
            if (type === 'resultoutput') return [];
            if (type === 'imageacquisition') return ['Image'];
            return ['Result'];
        }

        if (type === 'imageacquisition') return [];
        if (type === 'resultoutput') return ['Input'];
        return ['Image'];
    },

    _normalizeDraftConnectionsForCanvas(connections, operators) {
        const operatorById = new Map(operators.map(op => [String(op.id).toLowerCase(), op]));
        const findOperator = value => operatorById.get(String(value || '').toLowerCase()) || null;
        return this._toArray(connections).map((connection, index) => {
            const conn = this._asObject?.(connection) || {};
            const sourceId = conn.sourceOperatorId || conn.SourceOperatorId || conn.sourceTempId || conn.SourceTempId || conn.source || conn.Source;
            const targetId = conn.targetOperatorId || conn.TargetOperatorId || conn.targetTempId || conn.TargetTempId || conn.target || conn.Target;
            const source = findOperator(sourceId);
            const target = findOperator(targetId);
            if (!source || !target) return null;
            const sourcePort = this._findCanvasPort(source.outputPorts || source.OutputPorts, conn.sourcePortName || conn.SourcePortName || conn.sourcePortId || conn.SourcePortId);
            const targetPort = this._findCanvasPort(target.inputPorts || target.InputPorts, conn.targetPortName || conn.TargetPortName || conn.targetPortId || conn.TargetPortId);
            return {
                id: conn.id || conn.Id || `conn_${index + 1}`,
                sourceOperatorId: source.id,
                sourcePortId: sourcePort?.id || sourcePort?.Id || '',
                targetOperatorId: target.id,
                targetPortId: targetPort?.id || targetPort?.Id || ''
            };
        }).filter(conn => conn?.sourceOperatorId && conn?.targetOperatorId);
    },

    _findCanvasPort(ports, preferred) {
        const items = this._toArray(ports);
        const key = String(preferred || '').trim().toLowerCase();
        return items.find(port =>
            key &&
            [port.id, port.Id, port.name, port.Name]
                .map(value => String(value || '').trim().toLowerCase())
                .includes(key)) || items[0] || null;
    },

    _cloneJsonSafe(value) {
        if (value === null || value === undefined) return value;
        try {
            return typeof structuredClone === 'function'
                ? structuredClone(value)
                : JSON.parse(JSON.stringify(value));
        } catch {
            return value;
        }
    },

    _isPendingValueSummary(value) {
        return isPendingParameterSentinel(value);
    },

    _countBuildBlockers(events = this.activeAgentRunEvents) {
        return (events || []).filter(evt => {
            const status = String(evt?.status || '').toLowerCase();
            return status === 'blocked' || status === 'failed';
        }).length;
    }
};
