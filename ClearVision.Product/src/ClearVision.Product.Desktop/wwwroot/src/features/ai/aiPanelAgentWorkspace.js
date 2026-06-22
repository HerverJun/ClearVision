import httpClient from '../../core/messaging/httpClient.js';
import { AiWorkbenchStates } from './aiPanelWorkbench.js';

export const AgentWorkspaceModes = Object.freeze({
    PLAN: 'plan',
    BUILD: 'build',
    APPLIED: 'applied'
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
    'tool_loop',
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
    tool_loop: 'Tool Loop 实验',
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
    { key: 'context', label: '收集上下文' },
    { key: 'model', label: '模型规划' },
    { key: 'contract', label: '契约校验' },
    { key: 'safety', label: '安全约束' }
];

const PLAN_PENDING_STATUS = 'waiting';

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
    'Tool Loop fallback': 'Tool Loop 已回退',
    'Experimental Tool Loop could not safely produce a complete Build payload; using stable BuildOrchestrator.': '实验 Tool Loop 未能安全产出完整构建结果，已回退稳定构建链路。',
    'Tool Loop completion source is not registered; using stable BuildOrchestrator.': 'Tool Loop completion source 未注册，已回退稳定构建链路。',
    'Experimental Tool Loop fallback decision.': '实验 Tool Loop 回退决策。',
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
    llm_tool_loop: 'LLM 工具循环',
    fixed_build_orchestrator: '固定构建链路',
    fallback_build_orchestrator: '回退构建链路',
    partial_final_requires_stable_completion: 'Tool Loop 草稿不完整，已回退稳定构建链路',
    completion_failed: 'Tool Loop completion 失败，已回退稳定构建链路',
    failed_with_tool_limit: 'Tool Loop 超过最大轮次，已回退稳定构建链路',
    max_tool_calls_per_round: '单轮工具调用超限，已回退稳定构建链路',
    duplicate_tool_call: '重复工具调用超限，已回退稳定构建链路',
    invalid_json: 'final JSON 无效，已回退稳定构建链路',
    validate_flow_failed: '流程校验未通过，已回退稳定构建链路',
    dryrun_flow_failed: '元数据预演未通过，已回退稳定构建链路',
    runtime_package_precheck_failed: '运行包预检查未通过，已回退稳定构建链路',
    unsafe_final_payload: 'final 草稿含敏感信息，已回退稳定构建链路',
    draft_edits_require_stable_completion: 'draftEdits 需要稳定链路补全，已回退稳定构建链路',
    completion_source_missing: 'Tool Loop completion source 未注册，已回退稳定构建链路',
    mode_mismatch: 'mode 不匹配',
    not_enabled: '未启用',
    completion_disabled: 'completion disabled',
    permission_denied: '权限拒绝',
    protocol_failed: '协议失败',
    max_tool_rounds_exceeded: 'MaxToolRounds 超限',
    template_not_found: '未找到匹配模板骨架，已改用算子链生成',
    required_template_missing: '必需模板骨架缺失',
    tool_permission_denied: '工具权限被拒绝，已回退稳定构建链路',
    unknown_tool: '未知工具被拒绝，已回退稳定构建链路',
    runtime_preview_consent_required: 'RuntimePreview 需要显式授权，已回退稳定构建链路',
    tool_loop_fallback: 'Tool Loop 回退'
};

const AI_OPERATOR_LABELS = {
    ImageAcquisition: '图像采集',
    SurfaceDefectDetection: '表面缺陷检测',
    BlobAnalysis: '斑点分析',
    DeepLearning: '深度学习检测',
    TemplateMatching: '模板匹配',
    CircleMeasurement: '圆测量',
    Measurement: '几何测量',
    MeasureDistance: '距离测量',
    UnitConvert: '单位换算',
    DetectionSequenceJudge: '序列判定',
    ImageAdd: '图像叠加',
    ResultJudgment: '结果判定',
    ResultOutput: '结果输出'
};

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
    get planQuestionAnswers() {
        if (!this._proxyPlanQuestionAnswers) {
            this._rawPlanQuestionAnswers = {};
            this._proxyPlanQuestionAnswers = this._createAnswersProxy(this._rawPlanQuestionAnswers);
        }
        return this._proxyPlanQuestionAnswers;
    },
    set planQuestionAnswers(val) {
        this._rawPlanQuestionAnswers = val || {};
        this._proxyPlanQuestionAnswers = this._createAnswersProxy(this._rawPlanQuestionAnswers);
    },

    _createAnswersProxy(target) {
        return new Proxy(target, {
            get(obj, prop) {
                if (prop in obj) {
                    return obj[prop];
                }
                if (typeof prop === 'string') {
                    // Try to find by questionId
                    for (const key in obj) {
                        const answer = obj[key];
                        if (answer && (answer.questionId === prop || answer.QuestionId === prop)) {
                            return answer;
                        }
                    }
                }
                return undefined;
            }
        });
    },

    _resetAgentWorkspace({ preservePlan = false } = {}) {
        this.activeIntentRouterRequestId = null;
        this.activePlanRequestId = null;
        this.activePlanRunId = null;
        this.activePlanRunRequestId = null;
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();
        this.activePlanRunCompletion = null;
        if (!preservePlan) {
            this.pendingVisionPlan = null;
            this.pendingClarificationPayload = null;
            this._clearPlanQuestionAnswers();
        }

        this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
        this._updatePlanBuildActionState();
    },

    _clearPlanQuestionAnswers() {
        this.planQuestionSelections = {};
        this.planQuestionAnswers = {};
        this.planAnswerRevision = (Number(this.planAnswerRevision) || 0) + 1;
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
            this.planQuestionAnswers = nextAnswers;
            this.planQuestionSelections = nextSelections;
            this.planAnswerRevision = (Number(this.planAnswerRevision) || 0) + 1;
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

        if (mode && !['auto', 'new', 'build', 'stable', 'scripted', 'planner', 'tool_loop'].includes(mode)) {
            return false;
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
                : canBuild
                    ? '当前选择已满足构建条件'
                    : blockedTitle;
        }

        const mainBuildBtn = this.container?.querySelector('#ai-btn-start-build');
        if (mainBuildBtn) {
            mainBuildBtn.disabled = busy || !canBuild;
            mainBuildBtn.textContent = actionState.label || 'Start Build';
            mainBuildBtn.dataset.acceptRecommended = actionState.acceptedRecommended ? 'true' : 'false';
            mainBuildBtn.title = hasPlan ? actionState.statusText : 'Finish the plan first';
            mainBuildBtn.setAttribute?.('aria-disabled', mainBuildBtn.disabled ? 'true' : 'false');
        }

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
        hasCurrentFlowContext = false
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
        this._addMessage('user', userMessage || normalizedDescription);
        const turn = this._startAssistantTurn({
            activate: true,
            statusText: '正在判断请求类型',
            statusTone: 'streaming',
            openReply: true
        });
        this._setAssistantSectionText(turn, 'reply', '正在判断请求类型。');
        this._updateIntentRouterTimeline(routerRequestId, 'intent-router-run', '正在判断请求类型', 'running');
        this._setResultStatusNote('正在判断请求类型。', 'info');
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
                const fallback = this._buildLocalIntentRouterFallback(normalizedDescription, error);
                this._handleIntentRouterResult(fallback, {
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
            canBuild: plan.executable === true,
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
        const isRuleFallback = /rule_fallback/i.test(String(route.routerSource || ''));
        const tone = route.needsClarification ? 'warning' : 'streaming';
        const visibleStatus = isRuleFallback
            ? '规则降级解析'
            : route.needsClarification || route.intent === 'ambiguous_vision_requirement'
            ? '需要补充信息'
            : '已理解请求';
        this._setAssistantTurnStatus(context.turn, visibleStatus, tone);
        this._setAssistantSectionText(context.turn, 'reply', this._formatIntentRouterReply(route));
        this._updateIntentRouterTimeline(
            context.routerRequestId,
            'intent-router-result',
            visibleStatus,
            'completed',
            isRuleFallback
                ? (route.publicReason || '模型路由不可用，当前为规则降级解析。')
                : route.publicReason);

        if (route.shouldMergeIntoPendingPlan && this.pendingVisionPlan) {
            this._mergePlanAnswerUpdates(this.pendingVisionPlan, route.planAnswerUpdates);
            this.pendingVisionPlan.resolvedPlanFields = route.resolvedPlanFields.length
                ? route.resolvedPlanFields
                : this._getResolvedPlanFields(this.pendingVisionPlan);
            this.pendingVisionPlan.remainingPlanFields = route.remainingPlanFields.length
                ? route.remainingPlanFields
                : this._getRemainingPlanFields(this.pendingVisionPlan);
            this._refreshPlanEffectiveBuildReadiness?.(this.pendingVisionPlan);

            if (route.intent === 'build_from_confirmed_plan' && this.pendingVisionPlan.executable === true) {
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
            return true;
        }

        if (route.shouldOpenPlan || route.intent === 'actionable_vision_plan') {
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

        if (route.intent === 'build_from_confirmed_plan' && this.pendingVisionPlan && this.pendingVisionPlan.executable !== true) {
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
            const clarificationPayload = this._buildIntentRouterClarificationPayload(route, context);
            const shouldClearPendingPlan = route.shouldResetPendingPlan === true || !this.pendingVisionPlan;
            if (shouldClearPendingPlan) {
                this.pendingVisionPlan = null;
                this._clearPlanQuestionAnswers();
            }
            this.pendingClarificationPayload = clarificationPayload;
            this._resetClarificationSelectionDraft?.();
            this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
            this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
            this._renderAgentRuntime?.(clarificationPayload);
            this._setResultStatusNote('需求不足，暂不可构建。', 'warning');
            this._setAssistantTurnStatus(context.turn, '需求不足', 'warning');
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
        return true;
    },

    _normalizeIntentRouterResult(result) {
        const item = this._asObject?.(result) || result || {};
        const intent = String(item.intent || item.Intent || 'ambiguous_vision_requirement').trim() || 'ambiguous_vision_requirement';
        const normalizeDisplayText = value => {
            const localized = this._localizeDisplayText(String(value || '').trim());
            return this._redactPublicDiagnosticText?.(localized) || localized;
        };
        const questions = (this._normalizeClarificationQuestionList?.(item.clarificationQuestions || item.ClarificationQuestions) || [])
            .map((question, index) => ({
                field: String(question.field || `clarification_${index + 1}`).trim() || `clarification_${index + 1}`,
                question: normalizeDisplayText(question.question || ''),
                required: question.required !== false,
                reason: normalizeDisplayText(question.reason || ''),
                priority: String(question.priority || 'high').trim() || 'high',
                options: this._toArray(question.options)
                    .map(option => normalizeDisplayText(option))
                    .filter(Boolean)
            }))
            .filter(question => question.question || question.options.length > 0)
            .slice(0, 5);
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
            this.planQuestionAnswers = nextAnswers;
            this.planQuestionSelections = nextSelections;
            this.planAnswerRevision = (Number(this.planAnswerRevision) || 0) + 1;
        }

        return changed;
    },

    _buildIntentRouterClarificationPayload(route = {}, context = {}) {
        const normalizedQuestions = this._normalizeClarificationQuestionList?.(route.clarificationQuestions || []) || [];
        const questions = normalizedQuestions.length > 0
            ? normalizedQuestions
            : (this._getDefaultClarificationQuestions?.() || []);
        const requiredQuestions = questions.filter(question => question.required !== false);
        const blockingClarificationFields = [...new Set(requiredQuestions
            .map(question => String(question.field || '').trim())
            .filter(Boolean))];
        const nonBlockingMissingFields = [...new Set(questions
            .filter(question => question.required === false)
            .map(question => String(question.field || '').trim())
            .filter(Boolean))];
        const missingFacts = requiredQuestions
            .map(question => String(question.question || question.field || '').trim())
            .filter(Boolean);
        const knownFacts = [
            context.description ? `User request: ${context.description}` : '',
            route.publicReason ? `Router reason: ${route.publicReason}` : ''
        ].filter(Boolean).slice(0, 6);
        const routerConfidence = String(route.confidence || 'low').trim().toLowerCase() || 'low';
        const confidenceScore = routerConfidence === 'high'
            ? 0.75
            : routerConfidence === 'medium'
                ? 0.5
                : 0.25;
        const aiExplanation = this._formatIntentRouterReply(route) ||
            route.assistantReply ||
            route.publicReason ||
            'Current requirement needs clarification before build.';

        return {
            success: false,
            status: 'clarification_required',
            failureType: 'clarification_required',
            clarificationRequired: true,
            turnIntent: 'new_flow',
            interactionState: 'clarifying',
            routerConfidence,
            aiExplanation,
            clarificationQuestions: questions,
            blockingClarificationFields,
            nonBlockingMissingFields,
            requirementMaturity: route.requirementMaturity || null,
            decisionTrace: route.decisionTrace || null,
            requirementBrief: {
                scenarioKey: '',
                scenarioName: '',
                intentType: String(route.intent || 'ambiguous_vision_requirement').trim() || 'ambiguous_vision_requirement',
                requirementMode: this.requirementMode || 'strict',
                confidence: confidenceScore,
                hasOpenQuestions: true,
                clarificationRequired: true,
                canGenerateDraftNow: false,
                draftRiskLevel: 'high',
                requiredFields: blockingClarificationFields,
                blockingClarificationFields,
                nonBlockingMissingFields,
                knownFacts,
                missingFacts,
                attachmentFacts: [],
                clarificationQuestions: questions
            }
        };
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

    _assessLocalRequirementMaturity(description) {
        const raw = String(description || '').trim();
        const lower = raw.toLowerCase();
        const collect = terms => terms.filter(term => lower.includes(String(term).toLowerCase()));
        const explicitObject = [];
        const explicitTarget = [];
        raw.replace(/检测(?:目标|对象)\s*(?:是|为|:|：)\s*([^，。；;,.!?！？]+)/g, (_, value) => {
            explicitObject.push(String(value || '').trim());
            return '';
        });
        raw.replace(/识别内容\s*(?:是|为|:|：)\s*([^，。；;,.!?！？]+)/g, (_, value) => {
            explicitTarget.push(String(value || '').trim());
            return '';
        });
        raw.replace(/检测\s*([^，。；;,.!?！？]+?)上的([^，。；;,.!?！？]+)/g, (_, objectValue, targetValue) => {
            explicitObject.push(String(objectValue || '').trim());
            explicitTarget.push(String(targetValue || '').trim());
            return '';
        });
        raw.replace(/判断\s*([^，。；;,.!?！？]+?)是否存在/g, (_, objectValue) => {
            explicitObject.push(String(objectValue || '').trim());
            explicitTarget.push('是否存在');
            return '';
        });
        const objectTerms = ['包装箱', '纸箱', '箱体', '胶带', '金属件', '金属表面', '端子', '线束', '连接器', '标签', '二维码', '条码', '圆孔', '孔位', '遥控器', '按键', '面板', '产品', '零件', 'carton', 'package', 'tape', 'metal', 'surface', 'terminal', 'wire', 'harness', 'connector', 'label', 'qr', 'barcode', 'hole', 'button', 'part', 'product'];
        const abstractTerms = ['终极', '有野心', '高级', '完整方案', '智能方案', '视觉检测方案', '检测方案', '真正', '最佳', '全套', '整体方案', '系统方案', '解决方案', 'ultimate', 'ambitious', 'advanced solution', 'complete solution', 'full solution'];
        const taskGroups = [
            ['geometry_measurement', ['测量', '尺寸', '孔距', '圆心距', '直径', '宽度', '高度', '距离', '间距', '角度', 'measure', 'measurement', 'distance', 'diameter', 'width', 'height', 'hole', 'spacing']],
            ['wire_sequence', ['线序', '端子', '线束', '排线', '插线', '颜色顺序', 'wire sequence', 'terminal', 'harness', 'wire order']],
            ['barcode_qr', ['二维码', '条码', '读码', '扫码', '标签识别', 'OCR', '字符', '文字', 'DataMatrix', 'barcode', 'qr', 'code', 'ocr']],
            ['presence_absence', ['有无', '漏装', '缺件', '少装', '缺失', '装配完整', '装配是否完整', '是否存在', 'presence', 'absence', 'missing part']],
            ['classification', ['分类', '类别', '型号', '类型识别', 'classification', 'classify']],
            ['template_location', ['定位', '对位', '找正', '模板', '匹配', '位姿', 'locate', 'position', 'align', 'template', 'matching', 'pose']],
            ['surface_or_pose_defect', ['缺陷', '外观', '划痕', '刮伤', '裂纹', '破损', '凹坑', '压痕', '脏污', '污渍', '贴正', '贴歪', '贴附', '胶带', '偏斜', 'surface', 'defect', 'scratch', 'crack', 'damage', 'dent', 'stain', 'tape']]
        ];
        const strategyTerms = ['规则', '模型', '深度学习', '传统算法', '模板', '阈值', 'AI', 'rule', 'model', 'deep learning', 'template', 'threshold'];
        const knownObjectSignals = collect(objectTerms).slice(0, 12);
        const objectSignals = [...new Set([...knownObjectSignals, ...explicitObject.filter(Boolean)])].slice(0, 12);
        const matchedTask = taskGroups
            .map(([taskType, terms]) => ({ taskType, signals: collect(terms).slice(0, 12) }))
            .find(item => item.signals.length > 0) || {
                taskType: explicitTarget.length > 0 ? 'classification' : 'unknown',
                signals: []
            };
        const taskSignals = [...new Set([...(matchedTask.signals || []), ...explicitTarget.filter(Boolean)])].slice(0, 12);
        const hasAbstractGoal = collect(abstractTerms).length > 0;
        const hasTask = matchedTask.taskType !== 'unknown';
        const hasObject = objectSignals.length > 0;
        const canPlan = hasTask || hasObject;
        if (hasAbstractGoal && !canPlan) {
            return {
                maturity: 'abstract_goal',
                taskType: 'abstract_goal',
                canPlan: false,
                canBuild: false,
                objectSignals,
                taskSignals,
                missingFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
                blockingReasons: ['abstract_goal_needs_decomposition', !hasTask ? 'task_type_missing' : '', !hasObject ? 'inspection_object_missing' : ''].filter(Boolean),
                publicReason: '这是方案愿景，不是可直接构建的检测流程。'
            };
        }

        if (!canPlan) {
            return {
                maturity: 'ambiguous',
                taskType: 'unknown',
                canPlan: false,
                canBuild: false,
                objectSignals,
                taskSignals,
                missingFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
                blockingReasons: ['inspection_object_missing', 'task_type_missing'],
                publicReason: '需求仍缺少检测对象或任务类型，暂不能构建。'
            };
        }

        const missingFields = [!hasObject ? 'inspection_object' : '', !hasTask ? 'task_type' : '', 'image_source', 'acceptance_criteria']
            .filter(Boolean);
        if (hasObject && hasTask && !collect(strategyTerms).length) {
            missingFields.push('model_or_rule_strategy');
        }
        const strictBlockingFields = new Set(['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'model_or_rule_strategy']);
        const canBuild = !missingFields.some(field => strictBlockingFields.has(field));
        if (!canBuild) {
            return {
                maturity: 'ambiguous',
                taskType: hasTask ? matchedTask.taskType : 'unknown',
                canPlan: true,
                canBuild: false,
                objectSignals,
                taskSignals,
                missingFields,
                blockingReasons: [!hasObject ? 'inspection_object_missing' : '', !hasTask ? 'task_type_missing' : '', missingFields.includes('model_or_rule_strategy') ? 'model_or_rule_strategy_missing' : ''].filter(Boolean),
                publicReason: '需求已足够进入规划，但构建前仍需补充图像来源、判定标准或实现策略。'
            };
        }

        return {
            maturity: 'actionable',
            taskType: matchedTask.taskType,
            canPlan: true,
            canBuild: true,
            objectSignals,
            taskSignals,
            missingFields: ['image_source', 'acceptance_criteria'],
            blockingReasons: [],
            publicReason: '需求已明确到可规划视觉流程。'
        };
    },

    _buildLocalIntentRouterFallback(description, error = null) {
        const text = String(description || '').trim().toLowerCase();
        const normalized = text.replace(/[\s!?！？。,.，、]/g, '');
        const isCasual = ['hi', 'hello', 'hey', '你好', '您好', '在吗', '在不在'].includes(normalized);
        const isHelp = normalized.includes('能做什么') ||
            normalized.includes('可以做什么') ||
            normalized === 'help' ||
            normalized === '帮助';
        const maturity = this._assessLocalRequirementMaturity(description);
        const shouldResetPendingPlan = this._looksLikeExplicitNewPlanRequest?.(description) === true;
        const isActionable = maturity.canPlan === true;
        const ambiguousReply = /包装箱|纸箱|箱/.test(String(description || ''))
            ? '你想检测包装箱的哪一类问题？比如胶带贴歪、条码不可读、Logo 缺失、箱角破损，或外观污渍。'
            : '你想检测哪一类问题？请补充检测目标、缺陷类型、输入来源，以及 OK/NG 判定规则。';
        const intent = isHelp
            ? 'help'
            : isCasual
                ? 'casual_chat'
                : isActionable
                    ? 'actionable_vision_plan'
                    : 'ambiguous_vision_requirement';
        const routedMaturity = (isHelp || isCasual)
            ? {
                ...maturity,
                maturity: 'chat_or_help',
                taskType: 'unknown',
                canPlan: false,
                canBuild: false,
                missingFields: [],
                blockingReasons: [],
                publicReason: '这是普通对话或能力咨询，不进入构建。'
            }
            : maturity;
        return {
            intent,
            confidence: isCasual || isHelp ? 'high' : (isActionable && maturity.canBuild !== true ? 'low' : 'medium'),
            shouldOpenPlan: intent === 'actionable_vision_plan',
            shouldBuildDirectly: false,
            canPlan: intent === 'actionable_vision_plan',
            canBuild: intent === 'actionable_vision_plan' && routedMaturity.canBuild === true,
            needsClarification: intent === 'ambiguous_vision_requirement',
            publicReason: error
                ? '模型路由不可用，当前为规则降级解析。'
                : '已使用安全规则兜底判断请求类型。',
            assistantReply: intent === 'casual_chat'
                ? '在的。你可以直接描述检测目标、缺陷类型、测量项或流程修改需求，我会先帮你规划方案。'
                : intent === 'help'
                    ? '我可以帮你规划视觉检测流程、选择算子链、整理待确认资源，并在人工确认后生成可应用到画布的草稿。'
                    : intent === 'actionable_vision_plan'
                        ? '我先帮你整理规划方案。'
                        : ambiguousReply,
            clarificationQuestions: intent === 'ambiguous_vision_requirement'
                ? [
                    '请补充检测目标或产品对象。',
                    '请说明缺陷、测量项或识别内容。',
                    '请说明输入来源和 OK/NG 判定规则。'
                ]
                : [],
            fallbackAllowed: true,
            routerSource: 'local_rule_fallback',
            fallbackReason: error?.message || 'router_unavailable',
            shouldResetPendingPlan,
            requirementMaturity: routedMaturity,
            decisionTrace: {
                rawUserText: String(description || ''),
                turnIntent: intent,
                interactionState: intent === 'actionable_vision_plan' ? 'planning' : 'clarifying',
                businessSignalsHit: [],
                newFlowSignalsHit: [],
                taskTypeSignalsHit: routedMaturity.taskSignals || [],
                objectSignalsHit: routedMaturity.objectSignals || [],
                maturityLevel: routedMaturity.maturity,
                taskType: routedMaturity.taskType,
                canPlan: routedMaturity.canPlan === true,
                canBuild: routedMaturity.canBuild === true,
                fallbackReason: error?.message || 'router_unavailable',
                blockingReasons: routedMaturity.blockingReasons || [],
                metadataOnly: true
            },
            metadataOnly: true
        };
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
        this._setAssistantTurnStatus(turn, '规划中', 'warning');
        this._setAssistantSectionText(
            turn,
            'reply',
            '规划中。公开进度会实时更新在下方时间线。'
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
            semanticExtraction
        });
        this._requestBackendVisionPlanLive(planRequest, {
            planRequestId,
            turn,
            fallbackDescription: normalizedDescription
        })
            .then(result => {
                if (!this._isActivePlanRequest(planRequestId)) return;
                this.pendingVisionPlan = this._normalizeBackendPlanResult(result, normalizedDescription);
                this._mergeBackendPlanAnswers(this.pendingVisionPlan);
                this._clearActivePlanRequest(planRequestId);
                this._setGeneratingState?.(false);
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
                this._setResultStatusNote('规划模式等待确认，确认后进入构建模式。', 'info');
                this._renderAgentWorkspaceOverview();
                this._renderPlanWorkspace(this.pendingVisionPlan);
                this._updatePlanBuildActionState();
                this.activeAssistantTurn = null;
            })
            .catch(error => {
                if (!this._isActivePlanRequest(planRequestId)) return;
                this._clearActivePlanRequest(planRequestId);
                this._setGeneratingState?.(false);
                this.pendingVisionPlan = null;
                this._setAssistantTurnStatus(turn, '规划失败', 'failed');
                this._setAssistantSectionText(
                    turn,
                    'reply',
                    `规划模式失败：${error?.message || String(error || '未知错误')}`
                );
                this._setResultStatusNote('规划模式失败，请检查后端连接后重试。', 'warning');
                this._renderAgentWorkspaceOverview();
                this._renderPlanWorkspace(null);
                this._updatePlanBuildActionState();
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
        semanticExtraction = null
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
            remainingPlanFields: this._getRemainingPlanFields(this.pendingVisionPlan)
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

    async _requestBackendVisionPlan(request) {
        return await httpClient.post('/ai/agent-plan', request);
    },

    _shouldUsePlanRunEventStream() {
        return typeof window !== 'undefined' && typeof fetch === 'function';
    },

    async _requestBackendVisionPlanLive(request, { planRequestId, turn, fallbackDescription = '' } = {}) {
        if (!this._shouldUsePlanRunEventStream()) {
            this._setAssistantTurnStatus(turn, '普通规划请求', 'warning');
            this._setAssistantSectionText(
                turn,
                'reply',
                '事件流不可用，已切换为普通规划请求。'
            );
            this._setResultStatusNote('事件流不可用，已切换为普通规划请求。', 'warning');
            return await this._requestBackendVisionPlan(request);
        }

        try {
            return await this._requestBackendVisionPlanRun(request, {
                planRequestId,
                turn,
                fallbackDescription
            });
        } catch (error) {
            if (!this._isActivePlanRequest(planRequestId)) {
                throw error;
            }

            if (this.activePlanRunId || this.activePlanRunCompletion) {
                throw error;
            }

            this._closeAgentRunEventSource?.();
            this.activePlanRunId = null;
            this.activePlanRunRequestId = null;
            this.activePlanRunEvents = [];
            this.activePlanRunEventKeys = new Set();
            this.activePlanRunCompletion = null;
            this._setAssistantTurnStatus(turn, '普通规划请求', 'warning');
            this._setAssistantSectionText(
                turn,
                'reply',
                '事件流不可用，已切换为普通规划请求。'
            );
            this._setResultStatusNote('事件流不可用，已切换为普通规划请求。', 'warning');
            return await this._requestBackendVisionPlan(request);
        }
    },

    async _requestBackendVisionPlanRun(request, { planRequestId, turn, fallbackDescription = '' } = {}) {
        const createResult = await httpClient.post('/ai/agent-plan-runs', request);
        const runId = String(createResult?.runId || createResult?.RunId || '').trim();
        if (!runId) {
            throw new Error('Plan Run 创建接口没有返回 runId。');
        }

        if (!this._isActivePlanRequest(planRequestId)) {
            throw new Error('Plan Run 已过期。');
        }

        this.activePlanRunId = runId;
        this.activePlanRunRequestId = planRequestId;
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();
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

        this.activePlanRunEventKeys = this.activePlanRunEventKeys instanceof Set
            ? this.activePlanRunEventKeys
            : new Set();
        const key = `${evt.runId}:${evt.sequence}:${evt.eventType}`;
        if (this.activePlanRunEventKeys.has(key)) {
            this._recordPublicLiveEventDrop?.('duplicate');
            return;
        }

        this.activePlanRunEventKeys.add(key);
        this.activePlanRunEvents = Array.isArray(this.activePlanRunEvents)
            ? this.activePlanRunEvents
            : [];
        this.activePlanRunEvents.push(evt);
        this._routePublicLiveEvent?.(this._normalizePublicLiveEvent?.(evt, { source: 'plan-run' }));

        if (evt.eventType === 'assistant.brief') {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '规划中', 'streaming');
        } else {
            this._renderPlanRunTimeline(this.activeAssistantTurn);
        }

        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);

        if (evt.eventType === 'plan.completed') {
            this._resolveActivePlanRun(evt);
            return;
        }

        if (evt.eventType === 'run.completed') {
            this._resolveActivePlanRun(evt);
            return;
        }

        if (evt.eventType === 'plan.cancelled' || evt.eventType === 'run.cancelled') {
            this._rejectActivePlanRun(new Error('规划已取消。'), { cancelled: true });
            return;
        }

        if (evt.eventType === 'plan.failed' || evt.eventType === 'run.failed') {
            this._rejectActivePlanRun(new Error(evt.summary || '规划失败。'));
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

        this._closeAgentRunEventSource?.();
        this.activePlanRunCompletion = null;
        completion.resolve(result);
    },

    _rejectActivePlanRun(error, { cancelled = false } = {}) {
        const completion = this.activePlanRunCompletion;
        this._closeAgentRunEventSource?.();
        this.activePlanRunCompletion = null;
        this._setGeneratingState?.(false);
        if (cancelled) {
            this._clearActivePlanRequest(this.activePlanRunRequestId);
            this._setWorkbenchState(AiWorkbenchStates.CANCELLED);
            this._setAssistantTurnStatus(this.activeAssistantTurn, '已取消', 'cancelled');
            this._setAssistantSectionText(this.activeAssistantTurn, 'reply', '规划已取消。');
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
                this.activePlanRunId || 'plan-run',
                `plan:${phase.key}`,
                `${item.label}：${statusLabel}${summary}`
            );
            if (!node) return;
            node.className = `ai-agent-run-step is-${this._getPlanTimelineTone(item.status)}`;
            node.dataset.stage = `plan:${phase.key}`;
            node.dataset.eventType = item.eventType || '';
            node.title = item.eventType || item.stage || '';
        });

        if (progress.currentLabel) {
            this._setResultStatusNote(progress.currentLabel, progress.warning ? 'warning' : 'info');
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
        const events = Array.isArray(this.activePlanRunEvents) ? this.activePlanRunEvents : [];
        let currentLabel = this.activePlanRunId ? '正在收集工程上下文。' : '';
        let warning = false;

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

        return {
            phases,
            currentLabel,
            warning,
            eventCount: events.length
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
            return { ...base, key: 'context', label: '收集上下文', status: 'running' };
        }
        if (eventType === 'plan.context.completed') {
            return { ...base, key: 'context', label: '收集上下文', status: 'completed' };
        }
        if (eventType === 'plan.model.started') {
            return { ...base, key: 'model', label: '模型规划', status: 'running' };
        }
        if (eventType === 'plan.model.completed') {
            return { ...base, key: 'model', label: '模型规划', status: 'completed' };
        }
        if (eventType === 'plan.model.timeout') {
            return { ...base, key: 'model', label: '模型规划', status: 'timeout', summary: '模型规划超时，已使用规则兜底方案。' };
        }
        if (eventType === 'plan.model.failed') {
            return { ...base, key: 'model', label: '模型规划', status: 'failed', summary: summary || '模型规划失败，已使用规则兜底方案。' };
        }
        if (eventType === 'plan.contract.started') {
            return { ...base, key: 'contract', label: '契约校验', status: 'running' };
        }
        if (eventType === 'plan.contract.completed') {
            if (String(evt?.status || '').trim().toLowerCase() === 'failed') {
                return { ...base, key: 'contract', label: '契约校验', status: 'failed', summary: summary || '契约校验失败，已使用规则兜底方案。' };
            }
            return { ...base, key: 'contract', label: '契约校验', status: 'completed' };
        }
        if (eventType === 'plan.safety.completed') {
            return { ...base, key: 'safety', label: '安全约束', status: 'completed' };
        }
        if (eventType === 'plan.fallback.used') {
            return { ...base, key: 'model', label: '规则兜底', status: 'completed', summary: summary || '已使用规则兜底方案。' };
        }
        if (eventType === 'plan.completed' || eventType === 'run.completed') {
            return { ...base, key: 'safety', label: '安全约束', status: 'completed', summary: '规划已就绪。' };
        }
        if (eventType === 'plan.cancelled' || eventType === 'run.cancelled') {
            return { ...base, key: 'model', label: '规划', status: 'cancelled', summary: '规划已取消。' };
        }
        if (eventType === 'plan.failed' || eventType === 'run.failed') {
            return { ...base, key: 'model', label: '规划', status: 'failed', summary: summary || '规划失败。' };
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
                this._addMessage('system', `取消规划未生效：${error?.message || '未知错误'}`);
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
        const requirementMode = this._normalizeRequirementMode?.(plan.requirementMode ?? plan.RequirementMode ?? this.requirementMode ?? 'strict') || 'strict';
        const blockingReasons = this._toArray(plan.blockingReasons || plan.BlockingReasons)
            .map(item => this._localizeDisplayText(item))
            .filter(reason => !this._isDraftableImageSourceBlockingReason(reason, route, requirementMode));
        const publicEvents = this._toArray(plan.publicEvents || plan.PublicEvents)
            .map(evt => this._normalizePlanPublicEvent(evt));
        const rawFallbackReason = String(plan.fallbackReason || plan.FallbackReason || '').trim();
        const plannerFailure = this._normalizePlannerFailureDiagnostics(plan, publicEvents);
        const normalizedBuildReadiness = this._normalizePlanBuildReadiness(plan.buildReadiness || plan.BuildReadiness);
        const authoritativeBuildReadiness = this._isUsableAuthoritativeReadiness(normalizedBuildReadiness, plan)
            ? normalizedBuildReadiness
            : null;
        const buildReadiness = authoritativeBuildReadiness ||
            this._buildLegacyPlanReadinessSnapshot({
                plan: null,
                rawCanBuild,
                requirementMaturity,
                semanticExtraction,
                route,
                blockingReasons,
                questions: normalizedQuestions,
                requirementMode
            });

        return {
            id: plan.planId || plan.PlanId || `plan-${Date.now()}`,
            planId: plan.planId || plan.PlanId || '',
            planHash: String(plan.planHash || plan.PlanHash || '').trim(),
            mode: AgentWorkspaceModes.PLAN,
            originalDescription: plan.originalUserPrompt || plan.OriginalUserPrompt || fallbackDescription,
            buildPrompt: plan.originalUserPrompt || plan.OriginalUserPrompt || fallbackDescription,
            goal: this._localizeDisplayText(plan.goal || plan.Goal || fallbackDescription || '视觉流程草稿'),
            intent: plan.intent || plan.Intent || '',
            confidence: plan.confidence || plan.Confidence || 'medium',
            requirementMode,
            planSource: plan.planSource || plan.PlanSource || '',
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
            nextAction: this._localizeDisplayText(plan.nextAction || plan.NextAction || '复核计划后开始构建。'),
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
                ? this._toArray(plan.requirementUnderstanding || plan.RequirementUnderstanding).map(item => this._localizeDisplayText(item))
                : [`用户目标：${fallbackDescription || '视觉流程草稿'}`],
            route: {
                routeId: route.routeId || route.RouteId || '',
                title: this._localizeDisplayText(route.title || route.Title || '视觉方案路线'),
                summary: this._localizeDisplayText(route.summary || route.Summary || ''),
                operators: this._toArray(route.operators || route.Operators),
                templateDecision: this._localizeDisplayText(route.templateDecision || route.TemplateDecision || '')
            },
            questions: normalizedQuestions,
            assumptions: normalizedDefaults.length
                ? normalizedDefaults.map(item => `${item.label}: ${this._localizeDisplayText(item.value)}${item.impact ? `（${item.impact}）` : ''}`)
                : ['保留公开元数据边界，缺失资源在确认前保持为待补项。'],
            recommendedDefaults: normalizedDefaults,
            steps: this._toArray(plan.executablePlan || plan.ExecutablePlan).map(item => this._localizeDisplayText(item)),
            risks: this._toArray(plan.risks || plan.Risks).map(item => this._localizeDisplayText(item)),
            acceptanceCriteria: this._toArray(plan.acceptanceCriteria || plan.AcceptanceCriteria).map(item => this._localizeDisplayText(item)),
            contextSummary,
            operatorCatalogVersion: plan.operatorCatalogVersion || plan.OperatorCatalogVersion || '',
            templateCatalogVersion: plan.templateCatalogVersion || plan.TemplateCatalogVersion || '',
            templateSelection,
            semanticExtraction,
            requirementMaturity,
            decisionTrace,
            stationBoundarySummary: plan.stationBoundarySummary || plan.StationBoundarySummary || '',
            plcOutputPolicy: plan.plcOutputPolicy || plan.PlcOutputPolicy || '',
            rawPlanSnapshot: plan
        };
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
            resolvedFields: this._toArray(item.resolvedFields || item.ResolvedFields)
                .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
                .filter(Boolean),
            remainingFields: this._toArray(item.remainingFields || item.RemainingFields)
                .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
                .filter(Boolean),
            primaryMessage: this._localizeDisplayText(item.primaryMessage || item.PrimaryMessage || ''),
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

        if (planSnapshot && typeof planSnapshot === 'object') {
            plan.rawPlanSnapshot = planSnapshot;
        }

        const maturity = this._normalizeRequirementMaturity(data.requirementMaturity || data.RequirementMaturity || planSnapshot?.requirementMaturity || planSnapshot?.RequirementMaturity);
        if (maturity) {
            plan.requirementMaturity = maturity;
        }

        const trace = this._normalizeDecisionTrace(data.decisionTrace || data.DecisionTrace || planSnapshot?.decisionTrace || planSnapshot?.DecisionTrace);
        if (trace) {
            plan.decisionTrace = trace;
        }

        plan.authoritativeBuildReadiness = readiness;
        plan.buildReadiness = readiness;
        plan.executable = readiness.canBuild === true;
        plan.resolvedPlanFields = this._toArray(readiness.resolvedFields);
        plan.remainingPlanFields = this._toArray(readiness.remainingFields);
        const blockingFields = this._toArray(data.blockingClarificationFields || data.BlockingClarificationFields);
        const readinessBlockers = this._toArray(readiness.blockers)
                .filter(blocker => blocker?.blocksBuild === true)
                .map(blocker => blocker.id || blocker.field)
                .filter(Boolean);
        plan.blockingReasons = readinessBlockers.length
            ? readinessBlockers
            : blockingFields;
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
        const publicLabel = this._localizeDisplayText(item.publicLabel || item.PublicLabel || '');
        if (!id && !category && !field && !publicLabel) return null;
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

    _buildLegacyPlanReadinessSnapshot({
        plan = null,
        rawCanBuild,
        requirementMaturity,
        semanticExtraction,
        route,
        blockingReasons = [],
        questions = [],
        acceptedRecommended = false,
        requirementMode = null
    }) {
        const mode = this._normalizeRequirementMode?.(requirementMode || plan?.requirementMode || this.requirementMode || 'strict') || 'strict';
        const legacyBuildBlockerPresent = this._toArray(blockingReasons)
            .some(reason => /^(hard_requirement|strategy_confirmation):/i.test(String(reason || '').trim()));
        let canBuild = this._computeEffectivePlanBuildReadiness({
            plan,
            rawCanBuild,
            requirementMaturity,
            semanticExtraction,
            route,
            blockingReasons,
            questions,
            acceptedRecommended,
            requirementMode: mode
        });
        if (rawCanBuild === true &&
            requirementMaturity?.canBuild === true &&
            !legacyBuildBlockerPresent) {
            canBuild = true;
        }
        const resolvedFields = this._getResolvedPlanFields(plan || { questions }, {
            acceptedRecommended,
            questions
        });
        const remainingFields = this._getRemainingPlanFields(plan || { questions, blockingReasons, requirementMaturity }, {
            acceptedRecommended,
            requirementMaturity,
            blockingReasons,
            questions
        });
        const blockers = [];
        const addBlocker = blocker => {
            if (!blocker?.id) return;
            if (blockers.some(item => item.id.toLowerCase() === blocker.id.toLowerCase())) return;
            blockers.push(blocker);
        };

        for (const field of remainingFields.filter(field => this._isPlanBuildBlockingField(field, mode, requirementMaturity, {
            plan,
            semanticExtraction,
            route
        }))) {
            addBlocker(this._makePlanBuildBlocker({
                id: `hard_requirement:${field}_missing`,
                category: PLAN_BUILD_BLOCKER_CATEGORIES.HARD_REQUIREMENT,
                field,
                blocksBuild: !canBuild,
                resolutionMode: PLAN_BUILD_RESOLUTION_MODES.ANSWER_QUESTION,
                publicLabel: `还缺：${this._formatRequirementFieldLabel(field)}`
            }));
        }

        for (const reason of this._toArray(blockingReasons)) {
            const parsed = this._parsePlanBlockingReason(reason);
            const field = parsed.field || this._normalizePlanBlockingField(reason);
            const isOutputTarget = field === PLAN_ANSWER_FIELDS.OUTPUT_TARGET;
            const outputTargetBlocksBuild = isOutputTarget &&
                this._planRequestsExternalOutput(plan, reason, semanticExtraction, route);
            const question = this._toArray(questions).find(item => {
                const id = String(item?.id || '').trim().toLowerCase();
                const qField = this._inferPlanQuestionFieldForQuestion(item, { blockingReasons: [reason] });
                return id === parsed.key || (field && qField === field);
            });
            const isResource = parsed.kind === PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING;
            const isSafety = parsed.kind === PLAN_BUILD_BLOCKER_CATEGORIES.SAFETY_BLOCKER;
            const isWarning = parsed.kind === PLAN_BUILD_BLOCKER_CATEGORIES.CONTRACT_WARNING ||
                (isOutputTarget && !outputTargetBlocksBuild) ||
                (!field && !question && parsed.kind !== PLAN_BUILD_BLOCKER_CATEGORIES.STRATEGY_CONFIRMATION &&
                    parsed.kind !== PLAN_BUILD_BLOCKER_CATEGORIES.HARD_REQUIREMENT &&
                    parsed.kind !== PLAN_BUILD_BLOCKER_CATEGORIES.SAFETY_BLOCKER);
            const category = isResource
                ? PLAN_BUILD_BLOCKER_CATEGORIES.RESOURCE_PENDING
                : isSafety
                    ? PLAN_BUILD_BLOCKER_CATEGORIES.SAFETY_BLOCKER
                : isWarning
                    ? PLAN_BUILD_BLOCKER_CATEGORIES.CONTRACT_WARNING
                    : isOutputTarget
                        ? PLAN_BUILD_BLOCKER_CATEGORIES.HARD_REQUIREMENT
                    : field === PLAN_ANSWER_FIELDS.ALGORITHM_STRATEGY || this._isRealStrategyConfirmationReason(reason, questions)
                        ? PLAN_BUILD_BLOCKER_CATEGORIES.STRATEGY_CONFIRMATION
                        : PLAN_BUILD_BLOCKER_CATEGORIES.HARD_REQUIREMENT;
            const blocksBuild = isSafety || (!canBuild && !isResource && !isWarning);
            const label = isResource
                ? '资源可在开始构建后补齐。'
                : isSafety
                    ? '安全阻断需后端复核。'
                : category === PLAN_BUILD_BLOCKER_CATEGORIES.STRATEGY_CONFIRMATION
                    ? '请选择构建策略。'
                    : isOutputTarget && outputTargetBlocksBuild
                        ? '请选择输出目标。'
                    : isOutputTarget
                        ? '默认使用本地结构化结果输出。'
                    : field
                        ? `还缺：${this._formatRequirementFieldLabel(field)}`
                        : question?.title
                            ? `请确认“${question.title}”。`
                            : '规划返回了无法映射的阻断项，已作为诊断保留。';
            addBlocker(this._makePlanBuildBlocker({
                id: this._normalizeLegacyBuildBlockerId(reason, category),
                category,
                field,
                questionId: String(question?.id || '').trim(),
                blocksBuild,
                resolutionMode: isSafety
                    ? PLAN_BUILD_RESOLUTION_MODES.NON_BLOCKING
                    : blocksBuild ? PLAN_BUILD_RESOLUTION_MODES.ANSWER_QUESTION : PLAN_BUILD_RESOLUTION_MODES.NON_BLOCKING,
                publicLabel: label
            }));
        }

        const blocking = blockers.find(blocker => blocker.blocksBuild);
        const effectiveCanBuild = canBuild && !blocking;
        return {
            canBuild: effectiveCanBuild,
            blockers,
            resolvedFields,
            remainingFields,
            primaryMessage: effectiveCanBuild
                ? '规划已完成，可以开始构建。'
                : blocking?.publicLabel || requirementMaturity?.publicReason || '当前规划仍需澄清，暂不可构建。',
            contractVersion: 'v2'
        };
    },

    _makePlanBuildBlocker({ id, category, field = '', questionId = '', blocksBuild = false, resolutionMode = '', publicLabel = '' }) {
        return {
            id: String(id || '').trim(),
            category: String(category || '').trim().toLowerCase(),
            field: this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase(),
            questionId: String(questionId || '').trim(),
            blocksBuild: blocksBuild === true,
            resolutionMode: String(resolutionMode || '').trim().toLowerCase(),
            publicLabel: this._localizeDisplayText(publicLabel || '')
        };
    },

    _applyAnswersToAuthoritativeReadiness(authoritative, questions = [], answers = {}, { acceptedRecommended = false } = {}) {
        const baseline = this._isUsableAuthoritativeReadiness(authoritative)
            ? authoritative
            : null;
        if (!baseline) return null;

        const questionList = this._toArray(questions);
        const answerList = this._buildAuthoritativeReadinessAnswerList(questionList, answers, { acceptedRecommended });
        const resolvedFields = new Set(this._toArray(baseline.resolvedFields)
            .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
            .filter(Boolean));
        const remainingBlockers = [];

        for (const blocker of this._toArray(baseline.blockers)) {
            const normalizedBlocker = this._makePlanBuildBlocker(blocker);
            const resolved = answerList.some(answer =>
                this._doesAnswerResolveAuthoritativeBlocker(normalizedBlocker, answer, questionList));
            if (resolved) {
                if (normalizedBlocker.field) resolvedFields.add(normalizedBlocker.field);
                continue;
            }

            remainingBlockers.push(normalizedBlocker);
        }

        const canBuild = !remainingBlockers.some(blocker => blocker.blocksBuild === true);
        const remainingFields = this._toArray(baseline.remainingFields)
            .map(field => this._inferPlanQuestionField(field) || String(field || '').trim().toLowerCase())
            .filter(field => field && !resolvedFields.has(field));
        const blocking = remainingBlockers.find(blocker => blocker.blocksBuild === true);
        return {
            canBuild,
            blockers: remainingBlockers,
            resolvedFields: [...resolvedFields].sort(),
            remainingFields: [...new Set(remainingFields)].sort(),
            primaryMessage: canBuild
                ? '规划已完成，可以开始构建。'
                : blocking?.publicLabel || baseline.primaryMessage || '当前规划仍需澄清，暂不可构建。',
            contractVersion: baseline.contractVersion || 'v2'
        };
    },

    _buildAuthoritativeReadinessAnswerList(questions = [], answers = {}, { acceptedRecommended = false } = {}) {
        const byId = new Map(this._toArray(questions)
            .map(question => [String(question?.id || question?.Id || '').trim(), question])
            .filter(([id]) => id));
        const answerList = Object.values(answers || {})
            .map(answer => {
                const questionId = String(answer?.questionId || answer?.QuestionId || '').trim();
                return this._normalizePlanAnswer(answer, byId.get(questionId) || null);
            })
            .filter(answer => this._isAuthoritativeReadinessAnswerAllowed(answer, byId.get(answer?.questionId || '')));
        const answeredKeys = new Set(answerList.map(answer => answer.field));

        if (acceptedRecommended) {
            for (const question of this._toArray(questions)) {
                const questionId = String(question?.id || question?.Id || '').trim();
                const field = this._inferPlanQuestionFieldForQuestion(question, { blockingReasons: [] }) ||
                    this._fallbackPlanQuestionField(question, questionId);
                if (!field || answeredKeys.has(field)) continue;
                const recommended = String(this._getQuestionRecommendedValue(question) || '').trim();
                if (!recommended || this._isPlanPlaceholderValue(recommended)) continue;
                const answer = {
                    questionId,
                    field,
                    value: recommended,
                    origin: PLAN_ANSWER_ORIGINS.ACCEPTED_RECOMMENDED_DEFAULT
                };
                if (!this._isAuthoritativeReadinessAnswerAllowed(answer, question, { requireRecommended: true })) {
                    continue;
                }
                answerList.push(answer);
                answeredKeys.add(field);
            }
        }

        return answerList;
    },

    _doesAnswerResolveAuthoritativeBlocker(blocker, answer, questions = []) {
        if (!blocker || !answer) return false;
        if (blocker.category === PLAN_BUILD_BLOCKER_CATEGORIES.SAFETY_BLOCKER) return false;
        const question = this._toArray(questions)
            .find(item => String(item?.id || item?.Id || '').trim() === String(answer.questionId || '').trim()) || null;
        if (!this._isAuthoritativeReadinessAnswerAllowed(answer, question)) return false;
        const blockerQuestionId = String(blocker.questionId || '').trim();
        if (blockerQuestionId && blockerQuestionId === String(answer.questionId || '').trim()) return true;
        return Boolean(blocker.field && answer.field === blocker.field);
    },

    _isAuthoritativeReadinessAnswerAllowed(answer, question = null, { requireRecommended = false } = {}) {
        if (!answer || !String(answer.value || '').trim() || !String(answer.field || '').trim()) return false;
        if (this._isPlanPlaceholderValue(answer.value)) return false;
        const options = this._toArray(question?.options || question?.Options);
        const recommended = String(this._getQuestionRecommendedValue(question) || '').trim();
        if (requireRecommended || answer.origin === PLAN_ANSWER_ORIGINS.ACCEPTED_RECOMMENDED_DEFAULT) {
            return Boolean(recommended && !this._isPlanPlaceholderValue(recommended) && String(answer.value || '').trim() === recommended);
        }

        if (options.length > 0) {
            return options.some(option => String(option?.value || option?.Value || '').trim() === String(answer.value || '').trim());
        }

        return answer.origin === PLAN_ANSWER_ORIGINS.EXPLICIT_USER_TEXT ||
            Boolean(String(answer.questionId || '').trim());
    },

    _normalizeLegacyBuildBlockerId(reason, category) {
        const parsed = this._parsePlanBlockingReason(reason);
        const key = parsed.key || String(reason || '').trim().toLowerCase() || 'planner_blocker';
        const prefix = category || parsed.kind || PLAN_BUILD_BLOCKER_CATEGORIES.CONTRACT_WARNING;
        const normalizedReason = String(reason || '').trim().toLowerCase();
        const reasonKind = normalizedReason.match(/^(hard_requirement|strategy_confirmation|resource_pending|contract_warning|safety_blocker):/i)?.[1] || '';
        if (reasonKind && (!category || reasonKind === String(category).toLowerCase())) {
            return normalizedReason;
        }
        const idKey = [
            PLAN_BUILD_BLOCKER_CATEGORIES.HARD_REQUIREMENT,
            PLAN_BUILD_BLOCKER_CATEGORIES.STRATEGY_CONFIRMATION
        ].includes(prefix) && !/_missing$/i.test(key)
            ? `${key}_missing`
            : key;
        return `${prefix}:${idKey}`;
    },

    _computeEffectivePlanBuildReadiness({
        plan = null,
        rawCanBuild,
        requirementMaturity,
        semanticExtraction,
        route,
        blockingReasons = [],
        questions = [],
        acceptedRecommended = false,
        requirementMode = null
    }) {
        const answerPlan = plan || { questions, requirementMaturity, blockingReasons };
        const mode = this._normalizeRequirementMode?.(requirementMode || plan?.requirementMode || this.requirementMode || 'strict') || 'strict';
        const resolvedFields = new Set(this._getResolvedPlanFields(answerPlan, {
            acceptedRecommended,
            questions
        }));
        const unresolvedStrategyBlockers = this._getUnresolvedStrategyBlockers({
            blockingReasons,
            questions,
            acceptedRecommended
        });
        const unresolvedQuestionBlockers = this._getUnresolvedPlanQuestionBlockers({
            blockingReasons,
            questions,
            acceptedRecommended,
            plan,
            semanticExtraction,
            route
        });

        if (mode === 'draft' &&
            !this._planHasAnyObjectOrTaskFact(requirementMaturity, semanticExtraction, resolvedFields)) {
            return false;
        }

        if (unresolvedStrategyBlockers.length &&
            (mode !== 'draft' ||
                requirementMaturity?.canPlan !== true ||
                !this._planRouteSatisfiesBuildStrategy(route))) {
            return false;
        }
        if (unresolvedQuestionBlockers.length) return false;

        const maturityCanBuild = requirementMaturity?.canBuild === true;
        if (rawCanBuild === true && maturityCanBuild) return true;

        const remainingFields = this._getRemainingPlanFields(answerPlan, {
            acceptedRecommended,
            requirementMaturity,
            blockingReasons,
            questions
        })
            .filter(field => this._isPlanBuildBlockingField(field, mode, requirementMaturity, {
                plan,
                semanticExtraction,
                route
            }))
            .filter(field => !(mode === 'draft' && field === PLAN_ANSWER_FIELDS.IMAGE_SOURCE && this._routeAllowsPendingImageSource(route)));
        if (remainingFields.length) return false;

        return this._planHardFactsReady(requirementMaturity, semanticExtraction, route, resolvedFields, mode) &&
            this._planRouteSatisfiesBuildStrategy(route);
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

        const canBuild = this._refreshPlanEffectiveBuildReadiness?.(plan, { acceptedRecommended: false }) === true;
        const canAcceptRecommended = !canBuild && this._canBuildPlanWithRecommendedAnswers(plan);
        if (canBuild) {
            return {
                canBuild: true,
                canAcceptRecommended: false,
                canStart: true,
                acceptedRecommended: false,
                label: '开始构建',
                statusText: '当前显式选择已满足构建条件'
            };
        }

        if (canAcceptRecommended) {
            return {
                canBuild: false,
                canAcceptRecommended: true,
                canStart: true,
                acceptedRecommended: true,
                label: '按推荐项确认并构建',
                statusText: '当前阻断问题均有推荐项，可批量确认后构建'
            };
        }

        return {
            canBuild: false,
            canAcceptRecommended: false,
            canStart: false,
            acceptedRecommended: false,
            label: '开始构建',
            statusText: this._getPlanBuildBlockedReason(plan)
        };
    },

    _canBuildPlanWithRecommendedAnswers(plan) {
        if (!plan || !this._allBlockingPlanQuestionsHaveRecommendations(plan)) return false;
        if (this._isUsableAuthoritativeReadiness(plan.authoritativeBuildReadiness)) {
            const preview = this._applyAnswersToAuthoritativeReadiness(
                plan.authoritativeBuildReadiness,
                plan.questions,
                this.planQuestionAnswers || {},
                { acceptedRecommended: true }
            );
            return preview?.canBuild === true;
        }

        return this._buildLegacyPlanReadinessSnapshot({
            plan,
            rawCanBuild: plan.rawPlanSnapshot?.canBuild ?? plan.rawPlanSnapshot?.CanBuild ?? plan.executable,
            requirementMaturity: plan.requirementMaturity,
            semanticExtraction: plan.semanticExtraction,
            route: plan.route || plan.recommendedRoute || plan.RecommendedRoute,
            blockingReasons: plan.blockingReasons,
            questions: plan.questions,
            acceptedRecommended: true
        }).canBuild === true;
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
        const answer = this._getPlanAnswerForQuestion(question);
        return answer?.value || '';
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
            this.planQuestionAnswers = nextAnswers;
            this.planQuestionSelections = nextSelections;
            this.planAnswerRevision = (Number(this.planAnswerRevision) || 0) + 1;
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
            .find(option => option?.recommended === true && !this._isPlanPlaceholderValue(option?.value));
        const value = recommendedOption?.value || question?.defaultValue || '';
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
        if (this._isUsableAuthoritativeReadiness(plan.authoritativeBuildReadiness)) {
            const preview = this._applyAnswersToAuthoritativeReadiness(
                plan.authoritativeBuildReadiness,
                plan.questions,
                this.planQuestionAnswers || {},
                { acceptedRecommended }
            );
            plan.buildReadiness = preview || plan.authoritativeBuildReadiness;
            plan.executable = plan.buildReadiness.canBuild === true;
            return plan.executable === true;
        }

        const hasLocalAnswers = Object.values(this.planQuestionAnswers || {})
            .some(answer => this._normalizePlanAnswer(answer));
        if (!acceptedRecommended &&
            !hasLocalAnswers &&
            plan.authoritativeBuildReadiness) {
            plan.buildReadiness = plan.authoritativeBuildReadiness;
            plan.executable = plan.buildReadiness.canBuild === true;
            return plan.executable === true;
        }

        const rawCanBuild = plan.rawPlanSnapshot?.canBuild ?? plan.rawPlanSnapshot?.CanBuild ?? plan.executable;
        const hasLegacyBuildBlocker = this._toArray(plan.blockingReasons)
            .some(reason => /^(hard_requirement|strategy_confirmation):/i.test(String(reason || '').trim()));
        if (!acceptedRecommended &&
            !hasLocalAnswers &&
            rawCanBuild === true &&
            plan.requirementMaturity?.canBuild === true &&
            !hasLegacyBuildBlocker) {
            plan.buildReadiness = {
                canBuild: true,
                blockers: [],
                resolvedFields: this._getResolvedPlanFields(plan),
                remainingFields: this._toArray(plan.remainingPlanFields),
                primaryMessage: '规划已完成，可以开始构建。',
                contractVersion: 'v2'
            };
            plan.executable = true;
            return true;
        }

        plan.buildReadiness = this._buildLegacyPlanReadinessSnapshot({
            plan,
            rawCanBuild,
            requirementMaturity: plan.requirementMaturity,
            semanticExtraction: plan.semanticExtraction,
            route: plan.route || plan.recommendedRoute || plan.RecommendedRoute,
            blockingReasons: plan.blockingReasons,
            questions: plan.questions,
            acceptedRecommended,
            requirementMode: plan.requirementMode || this.requirementMode || 'strict'
        });
        plan.executable = plan.buildReadiness.canBuild === true;
        return plan.executable === true;
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
        const publicReason = this._localizeDisplayText(item.publicReason || item.PublicReason || '');
        const missingFields = this._toArray(item.missingFields || item.MissingFields).map(field => String(field || '').trim()).filter(Boolean);
        const blockingReasons = this._toArray(item.blockingReasons || item.BlockingReasons).map(field => String(field || '').trim()).filter(Boolean);
        return {
            maturity,
            taskType,
            canPlan: (item.canPlan ?? item.CanPlan) === true,
            canBuild: (item.canBuild ?? item.CanBuild) === true,
            objectSignals: this._toArray(item.objectSignals || item.ObjectSignals).map(signal => String(signal || '').trim()).filter(Boolean),
            taskSignals: this._toArray(item.taskSignals || item.TaskSignals).map(signal => String(signal || '').trim()).filter(Boolean),
            missingFields,
            blockingReasons,
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
            fallbackReason: String(item.fallbackReason || item.FallbackReason || '').trim(),
            blockingReasons: this._toArray(item.blockingReasons || item.BlockingReasons).map(String),
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
            .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]{8,}/gi, '[redacted]')
            .replace(/\b(?:authorization|x-api-key|api[-_ ]?key|token|secret|baseUrl|base_url|headers?)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '[redacted]')
            .replace(/\bhttps?:\/\/[^\s"'<>|]+/gi, '[redacted]')
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
            .filter(option => option && !this._isPlanPlaceholderValue(option.value));
        const rawDefault = String(question.defaultValue || question.DefaultValue || '').trim();
        const defaultValue = this._isPlanPlaceholderValue(rawDefault) ? '' : rawDefault;
        return {
            id: question.id || question.Id || '',
            field: this._inferPlanQuestionField(question.field || question.Field || question.id || question.Id),
            title: this._localizeDisplayText(question.title || question.Title || ''),
            why: this._localizeDisplayText(question.why || question.Why || ''),
            defaultValue: defaultValue || options.find(item => item.recommended)?.value || options[0]?.value || '',
            defaultAssumption: this._localizeDisplayText(question.defaultAssumption || question.DefaultAssumption || ''),
            impact: this._localizeDisplayText(question.impact || question.Impact || ''),
            options
        };
    },

    _normalizePlanOption(option) {
        if (!option) return null;
        return {
            value: option.value || option.Value || '',
            label: this._localizeDisplayText(option.label || option.Label || option.value || option.Value || ''),
            recommended: Boolean(option.recommended ?? option.Recommended),
            description: this._localizeDisplayText(option.description || option.Description || ''),
            impact: this._localizeDisplayText(option.impact || option.Impact || '')
        };
    },

    _normalizePlanDefault(item) {
        if (!item) return null;
        return {
            id: item.id || item.Id || '',
            label: this._localizeDisplayText(item.label || item.Label || ''),
            value: this._localizeDisplayText(item.value || item.Value || ''),
            impact: this._localizeDisplayText(item.impact || item.Impact || '')
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
        const text = String(value || '').trim();
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
        const text = String(value || '').trim();
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
        const activeEvents = Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [];
        const planEvents = Array.isArray(plan?.publicEvents) ? plan.publicEvents : [];
        const planRunEvents = Array.isArray(this.activePlanRunEvents) ? this.activePlanRunEvents : [];
        const planProgress = this._getPlanRunProgressState?.() || { currentLabel: '', eventCount: 0 };
        const terminal = activeEvents.find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(evt.eventType));
        const lastEvent = activeEvents[activeEvents.length - 1];
        const blockerCount = this._countBuildBlockers(activeEvents);
        const showBuildExecutionPath = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD || activeEvents.length > 0;
        const executionPath = showBuildExecutionPath
            ? this._getBuildExecutionPath(activeEvents)
            : { modeLabel: '', enteredLabel: '', reasonLabel: '' };
        const goal = plan?.goal || this.lastUserPrompt || '描述视觉检测目标后开始规划。';
        const confidence = this._formatWorkspaceValue(plan?.confidence || (activeEvents.length || planRunEvents.length ? '事件驱动' : '未设置'));
        const nextAction = terminal
            ? (terminal.eventType === 'run.completed' ? '复核流程草稿，可应用到画布继续编辑。' : '查看首要修复建议后重试构建。')
            : this.agentWorkspaceMode === AgentWorkspaceModes.BUILD
                ? (lastEvent?.summary || '等待下一条后端公开事件。')
                : (plan?.nextAction || planProgress.currentLabel || '规划模式只提出高价值工程问题。');
        const executable = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD
            ? activeEvents.length > 0
            : Boolean(plan?.executable);
        const canPlan = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD
            ? true
            : Boolean(plan?.canPlan || plan?.requirementMaturity?.canPlan);
        const source = this._formatWorkspaceValue(plan?.planSource || (activeEvents.length ? '构建事件' : (planRunEvents.length ? '事件驱动' : '未设置')));

        el.innerHTML = `
            <section class="ai-agent-overview-card is-${this._escapeHtml(phase)}">
                <div class="ai-agent-overview-main">
                    <span class="ai-agent-overview-kicker">${this._escapeHtml(mode)}</span>
                    <strong>${this._escapeHtml(goal)}</strong>
                    <span>${this._escapeHtml(nextAction)}</span>
                    <em>可规划：${canPlan ? '是' : '否'} · 可构建：${executable ? '是' : '否'}</em>
                </div>
                <div class="ai-agent-overview-metrics">
                    ${showBuildExecutionPath
                        ? `<span><small>当前模式</small><b>${this._escapeHtml(executionPath.modeLabel || mode)}</b></span>
                           <span><small>VisionAgentLoop</small><b>${this._escapeHtml(executionPath.enteredLabel)}</b></span>`
                        : `<span><small>置信度</small><b>${this._escapeHtml(confidence)}</b></span>
                           <span><small>来源</small><b>${this._escapeHtml(source)}</b></span>`}
                    <span><small>阻断项</small><b>${this._escapeHtml(String(blockerCount))}</b></span>
                    <span><small>事件数</small><b>${this._escapeHtml(String(activeEvents.length || planRunEvents.length || planEvents.length))}</b></span>
                </div>
                ${executionPath.reasonLabel ? `<div class="ai-build-note"><strong>路径原因</strong>${this._escapeHtml(executionPath.reasonLabel)}</div>` : ''}
                <div class="ai-agent-stage-strip" aria-label="Vision Agent 阶段">
                    ${['plan', 'build', 'applied'].map(key => `
                        <span class="${key === phase ? 'is-active' : ''} ${this._isWorkspacePhaseCompleted(key, phase) ? 'is-completed' : ''}">
                            ${key === 'plan' ? 'Plan 规划' : key === 'build' ? 'Build 审计' : 'Applied 复核'}
                        </span>
                    `).join('')}
                </div>
            </section>
        `;
    },

    _getBuildExecutionPath(events = []) {
        const list = Array.isArray(events) ? events : [];
        const started = list.find(evt => evt?.eventType === 'run.started');
        const startedPayload = this._asObject(started?.payload);
        const configuredMode = this._payloadString(startedPayload, 'agentGenerateFlowMode') ||
            this._normalizeAgentGenerateFlowMode?.(this.agentGenerateFlowMode) ||
            this.agentGenerateFlowMode ||
            'scripted';
        const enabledFromPayload = startedPayload.useVisionAgentGenerateFlow ?? startedPayload.UseVisionAgentGenerateFlow;
        const enabled = typeof enabledFromPayload === 'boolean'
            ? enabledFromPayload
            : Boolean(this.useVisionAgentGenerateFlow);
        const normalizedMode = String(configuredMode || '').trim().toLowerCase();
        const requestedToolLoop = enabled && normalizedMode === 'tool_loop';
        const entered = list.some(evt => {
            const type = String(evt?.eventType || '');
            return type === 'tool_loop.started' ||
                type.startsWith('tool_call.') ||
                type.startsWith('tool_result.') ||
                (type.startsWith('tool_loop.') && type !== 'tool_loop.fallback');
        });
        const reason = this._getBuildExecutionPathReason(list, { enabled, requestedToolLoop, entered, normalizedMode });

        return {
            modeLabel: requestedToolLoop ? 'Tool Loop 实验' : '稳定构建链路',
            entered,
            enteredLabel: entered ? '已进入' : '未进入',
            reasonLabel: reason ? this._localizeDisplayText(reason) : ''
        };
    },

    _getBuildExecutionPathReason(events, state) {
        const terminal = [...events].reverse().find(evt => {
            const type = String(evt?.eventType || '');
            return type === 'tool_loop.fallback' ||
                type === 'tool_loop.failed' ||
                type === 'tool_loop.draft.rejected' ||
                type === 'tool_call.denied';
        });
        if (terminal) {
            const payload = this._asObject(terminal.payload);
            return this._payloadString(payload, 'fallbackReason') ||
                this._payloadString(payload, 'rejectionReason') ||
                this._payloadString(payload, 'failureType') ||
                this._payloadString(payload, 'reason') ||
                this._payloadString(payload, 'errorCode') ||
                terminal.summary ||
                terminal.title ||
                '';
        }

        if (!state.entered) {
            if (!state.enabled) return 'not_enabled';
            if (!state.requestedToolLoop) return 'mode_mismatch';
            return 'completion_disabled';
        }

        return '';
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
        return this.agentWorkspaceMode === AgentWorkspaceModes.BUILD ? '构建模式' : '规划模式';
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

    _resolveActiveClarificationPayload() {
        const candidates = [
            this.pendingClarificationPayload,
            this._lastAgentRuntime
        ].filter(Boolean);

        return candidates.find(payload => {
            const interactionState = String(this._getInteractionState?.(payload) || '').trim().toLowerCase();
            return interactionState === 'clarifying' || this._isClarificationResult?.(payload);
        }) || null;
    },

    _normalizeClarificationQuestionList(value) {
        const normalizeOptions = options => this._toArray(options)
            .map(option => {
                if (option && typeof option === 'object') {
                    return String(option.value ?? option.Value ?? option.label ?? option.Label ?? '').trim();
                }

                return String(option ?? '').trim();
            })
            .filter(Boolean);

        return this._toArray(value)
            .map((item, index) => {
                if (typeof item === 'string') {
                    const question = item.trim();
                    return question
                        ? {
                            field: `clarification_${index + 1}`,
                            question,
                            required: true,
                            reason: '',
                            priority: 'high',
                            options: []
                        }
                        : null;
                }

                if (!item || typeof item !== 'object') return null;

                const field = String(item.field ?? item.Field ?? item.id ?? item.Id ?? `clarification_${index + 1}`).trim();
                const question = String(item.question ?? item.Question ?? item.title ?? item.Title ?? '').trim();
                const options = normalizeOptions(item.options ?? item.Options);
                return (field || question || options.length)
                    ? {
                        field,
                        question: question || `请补充${this._getRequirementFieldLabel?.(field) || '关键信息'}。`,
                        required: Boolean(item.required ?? item.Required ?? true),
                        reason: String(item.reason ?? item.Reason ?? item.why ?? item.Why ?? '').trim(),
                        priority: String(item.priority ?? item.Priority ?? 'high').trim() || 'high',
                        options
                    }
                    : null;
            })
            .filter(Boolean);
    },

    _getDefaultClarificationQuestions() {
        return [
            {
                field: 'scene',
                question: '请确认这是哪一类视觉场景。',
                required: true,
                reason: '场景类型会决定模板、算子链和判定策略。',
                priority: 'high',
                options: ['外观缺陷', '漏装/有无', '线序判定', '尺寸测量']
            },
            {
                field: 'object_type',
                question: '请补充检测对象或产品对象。',
                required: true,
                reason: '检测对象不明确时无法安全选择模板和 ROI 策略。',
                priority: 'high',
                options: ['金属件', '包装箱/纸箱', '线束端子', '标签/条码']
            },
            {
                field: 'image_source_roi',
                question: '请说明图像来源以及是否已有 ROI。',
                required: true,
                reason: '采集来源和 ROI 会影响 ImageAcquisition、Crop 与坐标约定。',
                priority: 'high',
                options: ['相机实时图', '本地图像样本', '整图检测', '需要绘制 ROI']
            },
            {
                field: 'decision_rule',
                question: '请描述 OK/NG 或数值判定标准。',
                required: true,
                reason: '判定标准会影响阈值、Comparator 和 ResultJudgment。',
                priority: 'high',
                options: ['OK/NG 分类', '缺陷面积阈值', '尺寸公差', '类别/数量匹配']
            },
            {
                field: 'output_target',
                question: '请确认输出目标。',
                required: false,
                reason: '输出目标会影响结果字段、PLC/IO 和报表绑定。',
                priority: 'medium',
                options: ['画布流程', '运行包草稿', 'PLC/IO 输出', '报表字段']
            },
            {
                field: 'draft_first',
                question: '是否允许先按安全假设生成草稿，再把未确认项留作待补？',
                required: false,
                reason: '草稿优先可以推进方案，但应用前仍需复核阻断风险。',
                priority: 'medium',
                options: ['允许先出草稿', '必须补齐后生成']
            }
        ];
    },

    _normalizeClarificationPlanBrief(payload = {}) {
        const item = this._asObject?.(payload) || payload || {};
        const isClarification = this._isClarificationResult?.(item) ||
            String(this._getInteractionState?.(item) || '').trim().toLowerCase() === 'clarifying';
        let brief = this._normalizeRequirementBrief?.(item.requirementBrief ?? item.RequirementBrief ?? null) || null;

        if (!brief && !isClarification) {
            return null;
        }

        const normalizeList = value => this._normalizeRuntimeFieldList?.(value) ||
            (Array.isArray(value) ? value.map(entry => String(entry || '').trim()).filter(Boolean) : []);
        const topLevelQuestions = this._normalizeClarificationQuestionList(
            item.clarificationQuestions ?? item.ClarificationQuestions ?? item.questions ?? item.Questions
        );
        const topLevelBlockingFields = normalizeList(item.blockingClarificationFields ?? item.BlockingClarificationFields);
        const topLevelNonBlockingFields = normalizeList(item.nonBlockingMissingFields ?? item.NonBlockingMissingFields);

        brief = {
            scenarioKey: '',
            scenarioName: '',
            intentType: '',
            requirementMode: this._normalizeRequirementMode?.(item.requirementMode ?? item.RequirementMode ?? 'strict') || 'strict',
            confidence: 0,
            hasOpenQuestions: true,
            clarificationRequired: true,
            canGenerateDraftNow: false,
            draftRiskLevel: 'high',
            requiredFields: [],
            blockingClarificationFields: [],
            nonBlockingMissingFields: [],
            knownFacts: [],
            missingFacts: [],
            attachmentFacts: [],
            objectName: '',
            imageSource: '',
            outputTarget: '',
            decisionRule: '',
            roiRequirement: '',
            calibrationRequirement: '',
            objectTypes: [],
            defectTypes: [],
            measurementTargets: [],
            requiredResources: [],
            clarificationQuestions: [],
            ...(brief || {})
        };

        if (topLevelBlockingFields.length > 0 && brief.blockingClarificationFields.length === 0) {
            brief.blockingClarificationFields = topLevelBlockingFields;
        }
        if (topLevelNonBlockingFields.length > 0 && brief.nonBlockingMissingFields.length === 0) {
            brief.nonBlockingMissingFields = topLevelNonBlockingFields;
        }
        if (topLevelQuestions.length > 0 && brief.clarificationQuestions.length === 0) {
            brief.clarificationQuestions = topLevelQuestions;
        }
        if (brief.clarificationQuestions.length === 0) {
            brief.clarificationQuestions = this._getDefaultClarificationQuestions();
        }

        const resolvedSet = new Set([
            ...this._toArray(this.pendingVisionPlan?.resolvedPlanFields || this.pendingVisionPlan?.ResolvedPlanFields || []),
            ...Object.values(this.planQuestionAnswers || {})
                .map(answer => this._normalizePlanAnswer(answer))
                .filter(Boolean)
                .map(answer => answer.field)
        ].map(f => String(f || '').trim().toLowerCase()).filter(Boolean));

        if (brief.clarificationQuestions) {
            brief.clarificationQuestions = brief.clarificationQuestions
                .filter(q => {
                    const field = String(q.field || q.Field || '').trim().toLowerCase();
                    return !resolvedSet.has(field);
                });
        }

        brief.blockingClarificationFields = (brief.blockingClarificationFields || [])
            .filter(field => !resolvedSet.has(String(field || '').trim().toLowerCase()));

        brief.nonBlockingMissingFields = (brief.nonBlockingMissingFields || [])
            .filter(field => !resolvedSet.has(String(field || '').trim().toLowerCase()));

        const requiredQuestionFields = brief.clarificationQuestions
            .filter(question => question.required !== false)
            .map(question => String(question.field || '').trim())
            .filter(Boolean);

        if (brief.blockingClarificationFields.length === 0) {
            brief.blockingClarificationFields = [...new Set(requiredQuestionFields)];
        }

        brief.missingFacts = brief.blockingClarificationFields
            .slice(0, 6)
            .map(field => `请确认${this._getRequirementFieldLabel?.(field) || field}`);

        brief.clarificationRequired = true;
        brief.hasOpenQuestions = true;
        return brief;
    },

    _renderClarificationPlanWorkspace(el, payload = {}) {
        const brief = this._normalizeClarificationPlanBrief(payload);
        if (!brief) return false;

        const summary = String(payload.aiExplanation ?? payload.AiExplanation ?? payload.errorMessage ?? payload.message ?? '').trim()
            || '当前需求需要先补充信息。';
        const followupText = this._buildClarificationFollowupText?.(brief) || summary;
        const safeHint = this._buildClarificationSafeHint?.(brief) || followupText;
        const blockingCount = Math.max(
            brief.blockingClarificationFields.length,
            brief.clarificationQuestions.filter(question => question.required !== false).length,
            brief.missingFacts.length
        );
        const nextAction = this._buildAgentNextAction?.({
            turnIntent: this._getTurnIntent?.(payload) || 'new_flow',
            interactionState: 'clarifying',
            blockingCount,
            nonBlockingCount: brief.nonBlockingMissingFields.length,
            pendingCount: 0,
            missingResourceCount: 0,
            hasFlow: false
        }) || '下一步：先回答阻断问题，系统会在补齐后继续生成。';

        const renderTags = (items, emptyText, tone = '') => {
            const normalized = this._normalizeRuntimeFieldList?.(items) ||
                (Array.isArray(items) ? items.map(item => String(item || '').trim()).filter(Boolean) : []);
            if (!normalized.length) {
                return `<div class="ai-clarification-plan-empty">${this._escapeHtml(emptyText)}</div>`;
            }
            const toneClass = tone ? ` is-${tone}` : '';
            return `<div class="ai-clarification-plan-tags">${normalized
                .map(item => `<span class="ai-clarification-plan-chip${toneClass}">${this._escapeHtml(item)}</span>`)
                .join('')}</div>`;
        };

        const renderFields = (fields, emptyText, tone = '') => {
            const normalized = this._normalizeRuntimeFieldList?.(fields) || [];
            if (!normalized.length) {
                return `<div class="ai-clarification-plan-empty">${this._escapeHtml(emptyText)}</div>`;
            }
            const toneClass = tone ? ` is-${tone}` : '';
            return `<div class="ai-clarification-plan-tags">${normalized
                .map(field => `<span class="ai-clarification-plan-chip${toneClass}" title="${this._escapeHtml(field)}">${this._escapeHtml(this._getRequirementFieldLabel?.(field) || field)}</span>`)
                .join('')}</div>`;
        };

        const renderQuestions = questions => `
            <div class="ai-clarification-plan-question-list">
                ${questions.map((question, index) => {
                    const required = question.required !== false;
                    const fieldLabel = this._getRequirementFieldLabel?.(question.field) || question.field || '澄清项';
                    const options = this._normalizeClarificationQuestionList([question])[0]?.options || [];
                    return `
                        <article class="ai-clarification-plan-question ${required ? 'is-required' : 'is-optional'}">
                            <div class="ai-clarification-plan-question-head">
                                <span>${required ? '阻断问题' : '建议补充'} · ${this._escapeHtml(fieldLabel)}</span>
                                <strong>${this._escapeHtml(`${index + 1}. ${question.question}`)}</strong>
                            </div>
                            ${question.reason ? `<div class="ai-clarification-plan-reason">${this._escapeHtml(question.reason)}</div>` : ''}
                            ${options.length > 0 ? `
                                <div class="ai-clarification-plan-options-title">参考选项，点击后生成澄清回答草稿</div>
                                <div class="ai-clarification-plan-options">
                                    ${options.map(option => `
                                        <button class="ai-clarification-option ai-requirement-question-option" type="button"
                                            aria-pressed="false"
                                            data-clarification-field="${this._escapeHtml(question.field)}"
                                            data-clarification-value="${this._escapeHtml(option)}">
                                            ${this._escapeHtml(option)}
                                        </button>
                                    `).join('')}
                                </div>
                            ` : ''}
                        </article>
                    `;
                }).join('')}
            </div>
        `;

        el.hidden = false;
        el.innerHTML = `
            <section class="ai-clarification-plan-card" id="ai-clarification-plan-card" data-ai-clarification-plan-card="true">
                <div class="ai-clarification-plan-header">
                    <div>
                        <span class="ai-clarification-plan-kicker">ClarificationPlanCard</span>
                        <h3>待澄清</h3>
                    </div>
                    <span class="ai-clarification-plan-count">${this._escapeHtml(String(blockingCount))} 个阻断问题</span>
                </div>
                <div class="ai-clarification-plan-summary">${this._escapeHtml(summary)}</div>
                <div class="ai-clarification-plan-grid">
                    <section>
                        <div class="ai-clarification-plan-label">已知事实</div>
                        ${renderTags(brief.knownFacts, '当前还没有可靠的已知事实。', 'known')}
                    </section>
                    <section>
                        <div class="ai-clarification-plan-label">阻断问题</div>
                        ${renderTags(brief.missingFacts, '当前没有阻断待确认项。', 'blocking')}
                        ${renderFields(brief.blockingClarificationFields, '当前没有阻断字段。', 'blocking')}
                    </section>
                    <section>
                        <div class="ai-clarification-plan-label">非阻断待补</div>
                        ${renderFields(brief.nonBlockingMissingFields, '当前没有非阻断待补字段。', 'nonblocking')}
                    </section>
                    <section>
                        <div class="ai-clarification-plan-label">下一步</div>
                        <div class="ai-clarification-plan-next">${this._escapeHtml(nextAction)}</div>
                    </section>
                </div>
                <section class="ai-clarification-plan-section">
                    <div class="ai-clarification-plan-label">澄清问题</div>
                    ${renderQuestions(brief.clarificationQuestions)}
                </section>
                <div class="ai-clarification-plan-actions">
                    <button class="ai-clarification-plan-action" type="button" data-brief-action="copy">复制澄清清单</button>
                    <button class="ai-clarification-plan-action" type="button" data-brief-action="insert">插入输入框</button>
                    <button class="ai-clarification-plan-action" type="button" data-brief-action="queue">挂到下一轮</button>
                    <button class="ai-clarification-plan-action" type="button" data-brief-action="draft">切到草稿优先</button>
                    <button class="ai-clarification-plan-action is-primary" type="button" id="ai-btn-send-clarification-plan" data-brief-action="send-clarification" disabled>发送澄清回答</button>
                </div>
            </section>
        `;

        el.querySelectorAll('[data-brief-action]').forEach(button => {
            const action = button.dataset.briefAction;
            button.disabled = this.isGenerating || action === 'send-clarification';
            button.addEventListener('click', async () => {
                if (action === 'copy') {
                    const copied = await this._copyTextToClipboard?.(followupText);
                    this._addMessage?.('system', copied ? '澄清清单已复制。' : '复制失败，请手动复制。');
                    return;
                }

                if (action === 'insert') {
                    this._appendFollowupTextToInput?.(followupText);
                    this._addMessage?.('system', '澄清清单已插入输入框。');
                    return;
                }

                if (action === 'queue') {
                    this.nextHintDraft = safeHint;
                    this._renderQueuedHintBanner?.();
                    this._addMessage?.('system', '已挂载安全澄清上下文，下一轮不会把示例选项误当作用户答案。');
                    return;
                }

                if (action === 'draft') {
                    this._setRequirementMode?.('draft');
                    this._addMessage?.('system', '已切到草稿优先，下一轮会尽量在安全假设下先生成草稿。');
                    return;
                }

                if (action === 'send-clarification') {
                    const draftText = this._buildClarificationAnswerDraft?.() || '';
                    if (!draftText) {
                        this._addMessage?.('system', '请先选择澄清选项，或直接在输入框里补充答案。');
                        return;
                    }
                    this._mergeClarificationDraftIntoInput?.(draftText);
                    this._handleGenerate?.();
                }
            });
        });

        this._bindClarificationOptionButtons?.(el);
        this._updatePlanBuildActionState?.();
        return true;
    },

    _formatGateStatus(status) {
        switch (String(status || '').trim().toLowerCase()) {
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
            default:
                return this._localizeDisplayText(status) || '未设置';
        }
    },

    _renderRequirementMaturityPanel(plan) {
        const maturity = plan?.requirementMaturity || null;
        const trace = plan?.decisionTrace || null;
        if (!maturity && !trace) return '';
        const canPlan = plan?.canPlan === true || maturity?.canPlan === true || trace?.canPlan === true;
        const canBuild = plan?.executable === true;
        const missingFields = this._toArray(maturity?.missingFields)
            .map(field => this._formatRequirementFieldLabel(field))
            .filter(Boolean);
        const blockingReasons = this._toArray(maturity?.blockingReasons)
            .map(reason => this._localizeDisplayText(reason))
            .filter(Boolean);
        const unresolvedStrategyBlockers = this._getUnresolvedStrategyBlockers({
            blockingReasons: plan?.blockingReasons,
            questions: plan?.questions
        });
        const taskSignals = this._toArray(maturity?.taskSignals).slice(0, 8);
        const objectSignals = this._toArray(maturity?.objectSignals).slice(0, 8);
        const fallbackReason = trace?.fallbackReason || plan?.fallbackReason || '';
        const renderChips = (items, emptyText) => items.length
            ? `<div class="ai-plan-chain">${items.map(item => `<span>${this._escapeHtml(item)}</span>`).join('')}</div>`
            : `<div class="ai-plan-maturity-empty">${this._escapeHtml(emptyText)}</div>`;

        return `
            <section class="ai-workspace-section ai-requirement-maturity ${canBuild ? 'is-ready' : 'is-blocked'}">
                <div class="ai-workspace-section-title">需求成熟度</div>
                <div class="ai-requirement-maturity-grid">
                    <div class="ai-build-compact-row">
                        <b>成熟度</b>
                        <span>${this._escapeHtml(this._formatRequirementMaturityLabel(maturity?.maturity || trace?.maturityLevel))}</span>
                    </div>
                    <div class="ai-build-compact-row">
                        <b>任务类型</b>
                        <span>${this._escapeHtml(this._formatRequirementTaskTypeLabel(maturity?.taskType || trace?.taskType))}</span>
                    </div>
                    <div class="ai-build-compact-row">
                        <b>Plan</b>
                        <span>${canPlan ? '允许' : '阻断'}</span>
                    </div>
                    <div class="ai-build-compact-row">
                        <b>Build</b>
                        <span>${canBuild ? '允许' : '阻断'}</span>
                    </div>
                </div>
                ${unresolvedStrategyBlockers.length ? `<div class="ai-build-note"><strong>需确认策略</strong>${renderChips(unresolvedStrategyBlockers, '无')}</div>` : ''}
                ${maturity?.publicReason ? `<div class="ai-build-note"><strong>判断原因</strong>${this._escapeHtml(maturity.publicReason)}</div>` : ''}
                ${missingFields.length ? `<div class="ai-build-note"><strong>缺失字段</strong>${renderChips(missingFields, '无')}</div>` : ''}
                ${blockingReasons.length ? `<div class="ai-build-note"><strong>阻断原因</strong>${renderChips(blockingReasons, '无')}</div>` : ''}
                ${(objectSignals.length || taskSignals.length) ? `<div class="ai-build-note"><strong>命中信号</strong>${renderChips([...objectSignals, ...taskSignals], '无')}</div>` : ''}
                ${fallbackReason ? `<div class="ai-build-note"><strong>Trace</strong>${this._escapeHtml(this._localizeDisplayText(fallbackReason))}</div>` : ''}
            </section>
        `;
    },

    _renderPlanWorkspace(plan = this.pendingVisionPlan) {
        const el = this.container?.querySelector('#ai-plan-workspace');
        if (!el) return;

        el.hidden = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD;
        const clarificationPayload = !plan && this.agentWorkspaceMode !== AgentWorkspaceModes.BUILD
            ? this._resolveActiveClarificationPayload()
            : null;
        if (clarificationPayload && this._renderClarificationPlanWorkspace(el, clarificationPayload)) {
            return;
        }

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
                    <div class="ai-plan-empty-copy">资源补齐会在开始构建后出现；Plan 阶段只显示目标、关键问题、推荐默认值和规划诊断。</div>
                </div>
            `;
            this._updatePlanBuildActionState();
            return;
        }

        const semanticPanel = this._renderSemanticExtractionPanel(plan);
        const maturityPanel = this._renderRequirementMaturityPanel(plan);
        const routeOperators = this._toArray(plan.route?.operators);
        const routeChain = routeOperators.length
            ? `<div class="ai-plan-chain">${routeOperators.map(op => `<span title="${this._escapeHtml(op)}">${this._escapeHtml(this._formatOperatorType(op))}</span>`).join('')}</div>`
            : '<div class="ai-plan-maturity-empty">需求成熟度不足时不会提前选择算子链。</div>';
        const plannerFailureDiagnostics = this._renderPlannerFailureDiagnostics(plan);
        el.innerHTML = `
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">需求理解</div>
                <div class="ai-workspace-list">${plan.understanding.map(item => `<div>${this._escapeHtml(item)}</div>`).join('')}</div>
            </section>
            ${semanticPanel}
            ${maturityPanel}
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">推荐方案</div>
                <div class="ai-plan-route">
                    <strong>${this._escapeHtml(plan.route.title)}</strong>
                    <span>${this._escapeHtml(plan.route.summary)}</span>
                    ${routeChain}
                </div>
            </section>
            <section class="ai-workspace-section">
                <details class="ai-plan-diagnostics">
                <summary class="ai-workspace-section-title">规划诊断</summary>
                <div class="ai-build-compact">
                    <div class="ai-build-compact-row">
                        <b>${this._escapeHtml(this._formatPlanSource(plan.planSource))}</b>
                        <span class="ai-plan-tech-code">${this._escapeHtml(plan.planHash || '计划哈希待生成')}</span>
                    </div>
                    ${plannerFailureDiagnostics}
                    ${plan.fallbackReason ? `<div class="ai-build-note"><strong>兜底原因</strong>${this._escapeHtml(plan.fallbackReason)}</div>` : ''}
                    ${plan.planWarnings.length ? `<ul>${plan.planWarnings.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>` : ''}
                    ${plan.contractRepairNotes.length ? `<div class="ai-plan-chain">${plan.contractRepairNotes.map(item => `<span>${this._escapeHtml(item)}</span>`).join('')}</div>` : ''}
                    ${plan.publicEvents.length ? `<div class="ai-workspace-list">${plan.publicEvents.map(evt => {
                        const event = this._formatPlanEvent(evt);
                        return `<div><b>${this._escapeHtml(event.stageLabel)}</b> ${this._escapeHtml(event.statusLabel)} - ${this._escapeHtml(event.summary)}</div>`;
                    }).join('')}</div>` : ''}
                </div>
                </details>
            </section>
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">关键问题</div>
                <div class="ai-plan-question-list">
                    ${plan.questions
                        .filter(question => {
                            const field = this._inferPlanQuestionFieldForQuestion(question, plan) || '';
                            return !field || !(plan.resolvedPlanFields || []).includes(field);
                        })
                        .map(question => this._renderPlanQuestion(question, this._getPlanQuestionSelectedValue(question))).join('')}
                </div>
                <div class="ai-build-note">资源补齐会在开始构建后出现。此阶段不会提前显示完整资源补齐卡。</div>
            </section>
            <section class="ai-workspace-section ai-workspace-grid-2">
                <div>
                    <div class="ai-workspace-section-title">推荐默认值</div>
                    <ul>${plan.assumptions.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>
                </div>
                <div>
                    <div class="ai-workspace-section-title">风险</div>
                    <ul>${plan.risks.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>
                </div>
            </section>
            <section class="ai-workspace-section ai-workspace-grid-2">
                <div>
                    <div class="ai-workspace-section-title">可执行计划</div>
                    <ol>${plan.steps.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ol>
                </div>
                <div>
                    <div class="ai-workspace-section-title">验收标准</div>
                    <ol>${plan.acceptanceCriteria.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ol>
                </div>
            </section>
            <div class="ai-plan-actions">
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
        this._updatePlanBuildActionState();
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

    _renderPlanQuestion(question, selectedValue) {
        const isCustomValue = selectedValue && !question.options.some(opt => opt.value === selectedValue);
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
                        return `
                            <button
                                class="ai-plan-option ${selected ? 'is-selected' : ''} ${option.recommended ? 'is-recommended' : ''}"
                                type="button"
                                data-plan-question="${this._escapeHtml(question.id)}"
                                data-plan-question-option="${this._escapeHtml(option.value)}"
                                aria-pressed="${selected ? 'true' : 'false'}">
                                <span>${this._escapeHtml(option.label)}${option.recommended ? '（推荐）' : ''}</span>
                                <small>${this._escapeHtml(option.description)}</small>
                                <em>${this._escapeHtml(option.impact)}</em>
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
                        data-plan-question="${this._escapeHtml(question.id)}" />
                    <button
                        class="ai-plan-custom-input-btn"
                        type="button"
                        data-plan-question="${this._escapeHtml(question.id)}">
                        确定
                    </button>
                </div>
                <div class="ai-plan-question-impact">${this._escapeHtml(question.impact)}</div>
            </article>
        `;
    },

    _customInputPlanQuestion(questionId, value) {
        if (!questionId || !this.pendingVisionPlan) return;
        const cleanedValue = String(value || '').trim();
        if (!cleanedValue) return;

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
        const cleanedSelections = this._clearPlanQuestionSelectionsForField(field, questionId);
        this.planQuestionSelections = {
            ...cleanedSelections,
            [questionId]: cleanedValue
        };
        this.planQuestionAnswers = {
            ...(this.planQuestionAnswers || {}),
            [field]: answer
        };
        this.planAnswerRevision = (Number(this.planAnswerRevision) || 0) + 1;
        this._refreshPlanEffectiveBuildReadiness?.(this.pendingVisionPlan);
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderAgentWorkspaceOverview();
    },

    _selectPlanQuestionOption(questionId, value) {
        if (!questionId || !value || !this.pendingVisionPlan) return;
        const question = this._toArray(this.pendingVisionPlan.questions)
            .find(item => String(item?.id || '').trim() === String(questionId || '').trim());
        const field = this._inferPlanQuestionFieldForQuestion(question || { id: questionId }, this.pendingVisionPlan) ||
            this._fallbackPlanQuestionField(question || { id: questionId }, questionId);
        if (!field) return;
        const answer = {
            questionId,
            field,
            value,
            origin: PLAN_ANSWER_ORIGINS.EXPLICIT_USER_SELECTION
        };
        const cleanedSelections = this._clearPlanQuestionSelectionsForField(field, questionId);
        this.planQuestionSelections = {
            ...cleanedSelections,
            [questionId]: value
        };
        this.planQuestionAnswers = {
            ...(this.planQuestionAnswers || {}),
            [field]: answer
        };
        this.planAnswerRevision = (Number(this.planAnswerRevision) || 0) + 1;
        this._refreshPlanEffectiveBuildReadiness?.(this.pendingVisionPlan);
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderAgentWorkspaceOverview();
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

    _startBuildFromCurrentPlan({ acceptedRecommended = false } = {}) {
        if (this.isGenerating) return false;

        if (!this.pendingVisionPlan) {
            this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
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
            this._acceptRecommendedPlanAnswers(plan);
        }
        this._refreshPlanEffectiveBuildReadiness?.(plan, { acceptedRecommended });
        if (plan.executable !== true) {
            this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
            const readinessReason = this._getPlanBuildBlockedReason(plan);
            const strategyBlockers = this._getUnresolvedStrategyBlockers({
                blockingReasons: plan.blockingReasons,
                questions: plan.questions,
                acceptedRecommended
            });
            const reason = strategyBlockers.length
                ? '需确认策略后才能开始构建。'
                : plan.requirementMaturity?.publicReason || '当前计划仍需澄清，暂不可构建。';
            this._addMessage?.('system', readinessReason);
            this._setResultStatusNote?.(readinessReason, 'warning');
            this._renderAgentWorkspaceOverview();
            this._renderPlanWorkspace(plan);
            this._renderBuildWorkspaceFromAgentRun();
            this._updatePlanBuildActionState();
            return false;
        }

        this.activePlanRequestId = null;
        const buildFromPlan = this._buildStructuredBuildFromPlanRequest(plan, { acceptedRecommended });
        this.agentWorkspaceMode = AgentWorkspaceModes.BUILD;
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(plan);
        this._renderBuildWorkspaceFromAgentRun();
        this._setResultStatusNote('构建模式已启动，进度来自后端 AgentRun 公开事件。', 'info');

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
            planSnapshot: this._buildPlanSnapshotForBuild(plan),
            confirmedAnswers: this._buildConfirmedPlanAnswers(plan, { acceptedRecommended }),
            userSelections: this._buildPlanSelectionMap(plan, { acceptedRecommended }),
            acceptedDefaults: this._collectAcceptedDefaultIds(plan, acceptedRecommended),
            currentFlowSnapshot,
            templateSelection,
            attachmentSummary: this._buildPlanAttachmentSummary([]),
            operatorCatalogVersion: plan.operatorCatalogVersion || '',
            stationBoundarySummary: plan.stationBoundarySummary || '',
            plcOutputPolicy: plan.plcOutputPolicy || '',
            buildIntent,
            originalUserPrompt: plan.originalDescription || plan.buildPrompt || '',
            acceptedRecommendedDefaults: Boolean(acceptedRecommended),
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
            if (snapshot.canBuild === undefined && snapshot.CanBuild === undefined) {
                snapshot.canBuild = plan?.executable === true;
            }
            snapshot.buildReadiness = plan?.buildReadiness || snapshot.buildReadiness || snapshot.BuildReadiness || null;
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

        el.hidden = this.agentWorkspaceMode !== AgentWorkspaceModes.BUILD;
        const events = Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [];
        if (!events.length) {
            if (timeline) {
                timeline.innerHTML = '<div class="ai-followup-empty">Build 阶段已进入资源审计中心，等待后端 AgentRun 公开事件。</div>';
            }
            if (chain) chain.innerHTML = '<div class="ai-followup-empty">算子链会在构建事件返回后显示。</div>';
            if (parameters) parameters.innerHTML = '<div class="ai-followup-empty">参数映射和资源审计任务会在构建结果返回后显示。</div>';
            if (checks) checks.innerHTML = '<div class="ai-followup-empty">ApplyGate 会在构建结果返回后显示。</div>';
            if (finalDraft) finalDraft.innerHTML = '<div class="ai-followup-empty">流程草稿完成后可应用到画布。</div>';
            return;
        }

        if (timeline) timeline.innerHTML = this._renderBuildTimeline(events);
        if (template) template.innerHTML = this._renderBuildTemplateSummary(events);
        if (chain) chain.innerHTML = this._renderBuildOperatorChain(events);
        if (parameters) parameters.innerHTML = this._renderBuildParameterSummary(events);
        if (checks) checks.innerHTML = this._renderBuildChecks(events);
        if (finalDraft) finalDraft.innerHTML = this._renderBuildFinalDraft(events);
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
                        <span>${this._escapeHtml(this._localizeDisplayText(item.summary || item.title || ''))}</span>
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
                const stage = item.stage || item.Stage || '';
                const toolName = item.toolName || item.ToolName || '';
                const status = item.status || item.Status || '';
                const source = item.source || item.Source || '';
                const duration = item.durationMs ?? item.DurationMs ?? '';
                const warning = item.warningCode || item.WarningCode || '';
                const summary = item.outputSummary || item.OutputSummary || '';
                const stageLabel = BUILD_STAGE_LABELS[stage] || this._localizeDisplayText(stage);
                const toolLabel = this._formatToolName(toolName);
                const warningLabel = this._localizeDisplayText(warning);
                const sourceLabel = this._localizeDisplayText(source);
                return `
                    <div class="ai-build-compact-row">
                        <b>${this._escapeHtml(stageLabel)}${toolName ? ` / ${this._escapeHtml(toolLabel)}` : ''}</b>
                        <span>${this._escapeHtml(this._formatBuildStatus(status))}${source ? ` / ${this._escapeHtml(sourceLabel)}` : ''}${duration !== '' ? ` / ${this._escapeHtml(String(duration))} ms` : ''}${warning ? ` / ${this._escapeHtml(warningLabel)}` : ''}</span>
                        <small>${this._escapeHtml(this._localizeDisplayText(summary))}</small>
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
                    <span>${this._escapeHtml(this._localizeDisplayText(item.outputSummary || item.OutputSummary || ''))}</span>
                </div>
            `).join('');
        }

        const tools = events.filter(evt => {
            const payload = this._asObject?.(evt.payload) || {};
            const name = String(payload.toolName || payload.ToolName || evt.title || '').toLowerCase();
            return name.includes('template') || evt.stage === 'planner';
        }).slice(-4);

        if (!tools.length) {
            return '<div class="ai-followup-empty">模板策略尚未发布。</div>';
        }

        return tools.map(evt => `
            <div class="ai-build-compact-row">
                <b>${this._escapeHtml(this._localizeDisplayText(evt.title || '模板事件'))}</b>
                <span>${this._escapeHtml(this._localizeDisplayText(evt.summary || ''))}</span>
            </div>
        `).join('');
    },

    _renderBuildOperatorChain(events) {
        const buildResult = this._getBuildResult(events);
        const pipeline = this._toArray(buildResult?.operatorPipeline || buildResult?.OperatorPipeline);
        if (pipeline.length) {
            const selectionSource = buildResult?.selectionSource || buildResult?.SelectionSource || '';
            const effectiveRouteId = buildResult?.effectiveRouteId || buildResult?.EffectiveRouteId || '';
            const strategyConfirmed = this._readBooleanField(buildResult, 'strategyConfirmed', 'StrategyConfirmed');
            const strategyConfirmationSource = buildResult?.strategyConfirmationSource || buildResult?.StrategyConfirmationSource || '';
            const parameterStrategy = buildResult?.parameterStrategy || buildResult?.ParameterStrategy || '';
            const unresolvedStrategyBlockers = this._toArray(buildResult?.unresolvedStrategyBlockers || buildResult?.UnresolvedStrategyBlockers);
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
                    ${pipeline.map(item => {
                        const rawType = item.operatorType || item.OperatorType || '';
                        return `<span title="${this._escapeHtml([
                        rawType,
                        this._localizeDisplayText(item.source || item.Source || ''),
                        this._localizeDisplayText(item.repairNote || item.RepairNote || ''),
                        this._formatBuildStatus(item.status || item.Status || '')
                    ].filter(Boolean).join(' / '))}">${this._escapeHtml(this._formatOperatorType(rawType))}</span>`;
                    }).join('')}
                </div>
                ${pipeline.slice(0, 8).map(item => {
                    const source = item.source || item.Source || 'plan';
                    const repair = item.repairNote || item.RepairNote || '';
                    const status = item.status || item.Status || '';
                    const rawType = item.operatorType || item.OperatorType || '';
                    const tempId = item.tempId || item.TempId || '';
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
        if (!operatorTypes.length) {
            const planOps = this.pendingVisionPlan?.route?.operators || [];
            if (!planOps.length) {
                return '<div class="ai-followup-empty">流程草稿生成后会显示算子链。</div>';
            }
            return `<div class="ai-plan-chain">${planOps.map(op => `<span title="${this._escapeHtml(op)}">${this._escapeHtml(this._formatOperatorType(op))}</span>`).join('')}</div>`;
        }

        return `<div class="ai-plan-chain">${operatorTypes.map(op => `<span title="${this._escapeHtml(op)}">${this._escapeHtml(this._formatOperatorType(op))}</span>`).join('')}</div>`;
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
            const tempId = item.tempId || item.TempId || '';
            const operatorType = item.operatorType || item.OperatorType || '';
            const parameterName = item.parameterName || item.ParameterName || item.name || item.Name || '';
            const valueSummary = item.valueSummary ?? item.ValueSummary ?? item.value ?? item.Value ?? '';
            const source = item.source || item.Source || 'mapped';
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
        const buildResult = this._getBuildResult(events);
        const applyGate = buildResult?.applyGate || buildResult?.ApplyGate || this._getAgentRunResultPayload(events)?.applyGate;
        const readiness = buildResult?.readinessReport || buildResult?.ReadinessReport || null;
        const firstFix = buildResult?.firstFixRecommendation || buildResult?.FirstFixRecommendation || this._getAgentRunResultPayload(events)?.firstFixRecommendation || '';
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
            const firstFix = payload.firstFixRecommendation || payload.FirstFixRecommendation || '';
            return `
                <div class="ai-build-check is-${this._escapeHtml(tone)}">
                    <strong>${this._escapeHtml(this._localizeDisplayText(evt.title || BUILD_STAGE_LABELS[evt.stage] || evt.stage))}</strong>
                    <span>${this._escapeHtml(this._localizeDisplayText(evt.summary || ''))}</span>
                    ${firstFix ? `<em>${this._escapeHtml(this._localizeDisplayText(firstFix))}</em>` : ''}
                </div>
            `;
        }).join('');
    },

    _renderBuildFinalDraft(events) {
        const resultPayload = this._getAgentRunResultPayload(events);
        const buildResult = this._getBuildResult(events);
        const flow = this._getResultFlowForCanvas(resultPayload, { allowMergeFallback: false }) ||
            this._getResultFlowForCanvas(this.currentResult, { allowMergeFallback: false }) ||
            null;
        const ops = flow ? this._extractOperators(flow) : [];
        const connections = flow ? this._extractConnections(flow) : [];
        const terminal = events.find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(evt.eventType));
        const diff = buildResult?.workflowDiff || buildResult?.WorkflowDiff || resultPayload?.workflowDiff || null;
        if (!terminal) {
            return '<div class="ai-followup-empty">构建完成后会显示最终可编辑流程草稿。</div>';
        }

        if (!flow) {
            return `<div class="ai-build-note">${this._escapeHtml(terminal.summary || '构建完成，但未收到流程草稿。')}</div>`;
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
        this._renderBuildWorkspaceFromAgentRun();
        return true;
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

    _getResultFlowForCanvas(result = this.currentResult, options = {}) {
        const allowMergeFallback = options.allowMergeFallback !== false;
        const obj = this._asObject?.(result) || {};
        const directFlow = this._normalizeWorkflowDraftForCanvas(obj.flow || obj.Flow);
        if (directFlow && this._extractOperators(directFlow).length > 0) {
            return directFlow;
        }

        const buildResult = this._getPayloadBuildResult(obj);
        if (!buildResult) return null;

        const workflowDraft = buildResult.workflowDraft || buildResult.WorkflowDraft || null;
        let fallback = this._normalizeWorkflowDraftForCanvas(workflowDraft, buildResult);
        if (!fallback || this._extractOperators(fallback).length === 0) {
            fallback = this._buildCanvasFlowFromOperatorPipeline(buildResult);
        }

        if (!fallback || this._extractOperators(fallback).length === 0) {
            return null;
        }

        return allowMergeFallback
            ? this._mergeBuildFallbackWithCurrentCanvas(fallback, buildResult)
            : fallback;
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
            connections: connections.length ? connections : this._buildLinearCanvasConnections(operators),
            metadataOnly: Boolean(flowData.metadataOnly ?? flowData.MetadataOnly ?? buildResult?.metadataOnly ?? buildResult?.MetadataOnly ?? true)
        };
    },

    _normalizeDraftOperatorForCanvas(operator, index) {
        const op = this._asObject?.(operator) || {};
        const id = String(op.id || op.Id || op.tempId || op.TempId || `op_${index + 1}`).trim();
        const type = String(op.type || op.Type || op.operatorType || op.OperatorType || 'DeepLearning').trim();
        const name = String(op.name || op.Name || op.displayName || op.DisplayName || op.title || op.Title || id || type).trim();
        const parameters = this._normalizeDraftParametersForCanvas(op.parameters || op.Parameters);
        const inputPorts = this._normalizeDraftPortsForCanvas(op.inputPorts || op.InputPorts || op.inputs || op.Inputs, id, type, false);
        const outputPorts = this._normalizeDraftPortsForCanvas(op.outputPorts || op.OutputPorts || op.outputs || op.Outputs, id, type, true);
        return {
            ...op,
            id,
            name,
            type,
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
                return {
                    ...param,
                    name,
                    displayName: param.displayName || param.DisplayName || name,
                    value,
                    defaultValue: param.defaultValue ?? param.DefaultValue ?? value,
                    dataType: param.dataType || param.DataType || param.type || param.Type || 'string',
                    isRequired: Boolean(param.isRequired ?? param.IsRequired ?? this._isPendingValueSummary(value))
                };
            });
        }

        const obj = this._asObject?.(parameters) || {};
        return Object.keys(obj).map(name => {
            const value = obj[name];
            return {
                name,
                displayName: name,
                value,
                defaultValue: value,
                dataType: 'string',
                isRequired: this._isPendingValueSummary(value)
            };
        });
    },

    _normalizeDraftPortsForCanvas(ports, operatorId, operatorType, isOutput) {
        const raw = this._toArray(ports);
        if (raw.length) {
            return raw.map((port, index) => {
                const item = this._asObject?.(port) || {};
                const name = item.name || item.Name || item.portName || item.PortName || (isOutput ? 'Output' : 'Input');
                return {
                    ...item,
                    id: item.id || item.Id || `${operatorId}_${isOutput ? 'out' : 'in'}_${index}`,
                    name,
                    displayName: item.displayName || item.DisplayName || name,
                    dataType: item.dataType || item.DataType || item.type || item.Type || (String(name).toLowerCase().includes('image') ? 'Image' : 'Any'),
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

    _buildLinearCanvasConnections(operators) {
        const connections = [];
        for (let index = 0; index < operators.length - 1; index += 1) {
            const source = operators[index];
            const target = operators[index + 1];
            const sourcePort = this._toArray(source.outputPorts || source.OutputPorts)[0];
            const targetPort = this._toArray(target.inputPorts || target.InputPorts)[0];
            if (!sourcePort || !targetPort) continue;
            connections.push({
                id: `conn_${index + 1}`,
                sourceOperatorId: source.id,
                sourcePortId: sourcePort.id || sourcePort.Id || '',
                targetOperatorId: target.id,
                targetPortId: targetPort.id || targetPort.Id || ''
            });
        }
        return connections;
    },

    _buildCanvasFlowFromOperatorPipeline(buildResult) {
        const pipeline = this._toArray(buildResult?.operatorPipeline || buildResult?.OperatorPipeline);
        if (!pipeline.length) return null;
        const mappings = this._toArray(buildResult?.parameterMapping || buildResult?.ParameterMapping);
        const operators = pipeline.map(step => {
            const tempId = step.tempId || step.TempId || step.operatorType || step.OperatorType || '';
            const operatorType = step.operatorType || step.OperatorType || 'DeepLearning';
            const parameters = Object.fromEntries(mappings
                .filter(item => String(item.tempId || item.TempId || '').toLowerCase() === String(tempId).toLowerCase())
                .map(item => [item.parameterName || item.ParameterName || '', item.valueSummary || item.ValueSummary || ''])
                .filter(([name]) => name));
            return {
                tempId,
                operatorType,
                displayName: this._formatOperatorType(operatorType) || operatorType,
                parameters
            };
        });
        return this._normalizeWorkflowDraftForCanvas({
            operators,
            connections: [],
            metadataOnly: true
        }, buildResult);
    },

    _mergeBuildFallbackWithCurrentCanvas(fallbackFlow, buildResult = null) {
        const intent = String(buildResult?.buildIntent || buildResult?.BuildIntent || '').toLowerCase();
        const diff = this._asObject?.(buildResult?.workflowDiff || buildResult?.WorkflowDiff) || {};
        const preserved = this._toArray(diff.preservedNodes || diff.PreservedNodes);
        if (!['modify', 'refactor', 'review_pending_parameters'].includes(intent) && preserved.length === 0) {
            return fallbackFlow;
        }

        let current = null;
        try {
            current = this.flowCanvas?.serialize?.() || null;
        } catch {
            current = null;
        }

        const currentFlow = this._normalizeWorkflowDraftForCanvas(current);
        if (!currentFlow || this._extractOperators(currentFlow).length === 0) {
            return fallbackFlow;
        }

        const currentOps = this._extractOperators(currentFlow);
        const fallbackOps = this._extractOperators(fallbackFlow);
        const seen = new Set(currentOps.map(op => String(op.id || op.Id || op.name || op.Name || '').toLowerCase()).filter(Boolean));
        const addedOps = fallbackOps.filter(op => {
            const key = String(op.id || op.Id || op.name || op.Name || '').toLowerCase();
            if (!key || seen.has(key)) return false;
            seen.add(key);
            return true;
        });
        return {
            ...currentFlow,
            operators: [...currentOps, ...addedOps],
            connections: [
                ...this._extractConnections(currentFlow),
                ...this._extractConnections(fallbackFlow).filter(conn =>
                    addedOps.some(op => op.id === conn.sourceOperatorId || op.id === conn.targetOperatorId))
            ],
            metadataOnly: true
        };
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
        const text = String(value ?? '').toLowerCase();
        return text.includes('<pending') || text.includes('pending-') || text.includes('missing');
    },

    _countBuildBlockers(events = this.activeAgentRunEvents) {
        return (events || []).filter(evt => {
            const status = String(evt?.status || '').toLowerCase();
            return status === 'blocked' || status === 'failed';
        }).length;
    }
};
