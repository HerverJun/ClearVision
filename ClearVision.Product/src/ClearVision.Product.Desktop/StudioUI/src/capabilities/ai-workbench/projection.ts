import type { CvPageStateKind } from '@/design-system/patterns';
import type { CvStatusTone } from '@/design-system/primitives';
import type { AiClarificationQuestionV1 } from './contracts';
import type { AiWorkbenchState } from './reducer';

export interface AiWorkbenchProjection {
  readonly statusLabel: string;
  readonly statusTone: CvStatusTone;
  readonly pageState: CvPageStateKind | null;
  readonly pageStateTitle: string;
  readonly pageStateDescription: string;
  readonly currentStage: string;
  readonly stageDescription: string;
  readonly blockerCount: number;
  readonly primaryReason: string;
  readonly nextHint: string;
  readonly busy: boolean;
  readonly clarificationQuestions: readonly AiClarificationQuestionV1[];
}

function uniqueQuestions(state: AiWorkbenchState): readonly AiClarificationQuestionV1[] {
  const byField = new Map<string, AiClarificationQuestionV1>();
  const confirmedFields = new Set(
    state.session?.snapshot.confirmedPlanAnswers.map(answer => answer.field.trim().toLowerCase()) ?? []
  );
  for (const question of state.plan?.clarificationQuestions ?? []) {
    const key = question.field.trim().toLowerCase();
    if (key && !confirmedFields.has(key) && !byField.has(key)) byField.set(key, question);
  }
  return Object.freeze([...byField.values()].slice(0, 3));
}

function blockers(state: AiWorkbenchState): number {
  if (state.build) {
    return state.build.parameterMapping.filter(item => item.pending && !item.resourceDependent).length +
      state.build.missingResources.length +
      state.build.validation.structural.blockerCount + state.build.validation.dryRun.blockerCount;
  }
  const readiness = state.readiness?.buildReadiness ?? state.plan?.buildReadiness;
  if (readiness) return readiness.blockers.filter(item => item.blocksBuild).length;
  return state.intent?.remainingPlanFields.length ?? 0;
}

export function projectAiWorkbench(state: AiWorkbenchState): AiWorkbenchProjection {
  const questions = uniqueQuestions(state);
  const blockerCount = blockers(state);
  const base = {
    pageState: null,
    pageStateTitle: '',
    pageStateDescription: '',
    blockerCount,
    primaryReason: state.message,
    clarificationQuestions: questions
  } satisfies Partial<AiWorkbenchProjection>;

  switch (state.phase) {
    case 'session-loading':
      return Object.freeze({ ...base, statusLabel: '正在建立会话', statusTone: 'info', pageState: 'loading',
        pageStateTitle: '正在建立安全会话', pageStateDescription: '服务端正在绑定当前认证用户并保存会话身份。',
        currentStage: '会话准备', stageDescription: '正在确认用户、工程上下文和服务端状态。',
        nextHint: '会话就绪后可直接描述视觉检测任务。', busy: true });
    case 'intent-routing':
      return Object.freeze({ ...base, statusLabel: '正在理解任务', statusTone: 'info', currentStage: '任务理解',
        stageDescription: '正在识别检测对象、任务类型、图像来源和关键缺口。',
        nextHint: '完成后将自动建立可恢复的规划任务。', busy: true });
    case 'planning':
      return Object.freeze({ ...base, statusLabel: '正在规划', statusTone: 'info', currentStage: '方案规划',
        stageDescription: state.message || '正在生成公开规划步骤并校验方案合同。',
        nextHint: '规划完成后只展示需要你确认的高价值事项。', busy: true });
    case 'clarifying':
      return Object.freeze({ ...base, statusLabel: '等待确认', statusTone: 'warning', currentStage: '关键条件确认',
        stageDescription: questions.length > 0 ? `还有 ${questions.length} 个高价值事项需要确认。` : '服务端正在重新计算就绪条件。',
        nextHint: '选择推荐项或填写明确答案，再由服务端重新检查。', busy: false });
    case 'plan-blocked':
      return Object.freeze({ ...base, statusLabel: '方案受阻', statusTone: 'warning', currentStage: '方案待补充',
        stageDescription: state.message || '当前方案仍缺少关键条件。',
        nextHint: questions.length > 0 ? '回答待确认事项后重新检查。' : '修改任务描述后重新规划。', busy: false });
    case 'plan-ready':
      return Object.freeze({ ...base, statusLabel: '方案已就绪', statusTone: 'ok', currentStage: '方案就绪',
        stageDescription: '方案已具备构建条件。', blockerCount: 0,
        nextHint: '开始构建后将生成流程候选并执行结构校验与运行预演。', busy: false });
    case 'build-starting':
      return Object.freeze({ ...base, statusLabel: '正在启动构建', statusTone: 'info', currentStage: '构建准备',
        stageDescription: '正在绑定 Plan、会话、工程基线和 durable operation identity。',
        nextHint: '服务端确认 Build Run 后开始生成候选。', busy: true });
    case 'building':
      return Object.freeze({ ...base, statusLabel: '正在构建', statusTone: 'info', currentStage: '生成流程候选',
        stageDescription: state.message || '正在映射算子、参数和资源声明。',
        nextHint: '候选生成后将自动进入结构校验和运行预演。', busy: true });
    case 'validating':
      return Object.freeze({ ...base, statusLabel: '正在验证', statusTone: 'info', currentStage: '验证与预演',
        stageDescription: state.message || '正在执行结构校验、运行预演和清单预检。',
        nextHint: '完成后将列出需要人工处理的参数和资源。', busy: true });
    case 'parameters-pending':
      return Object.freeze({ ...base, statusLabel: '参数待确认', statusTone: 'warning', currentStage: '确认人工参数',
        stageDescription: '候选已生成，但仍有普通参数需要按真实算子合同确认。',
        nextHint: '完成类型、范围、枚举和互斥校验后确认参数。', busy: false });
    case 'resources-pending':
      return Object.freeze({ ...base, statusLabel: '资源待处理', statusTone: 'warning', currentStage: '处理资源依赖',
        stageDescription: '候选仍缺少相机、模型、模板或其他声明资源。',
        nextHint: '只使用安全查询返回的 canonical 资源身份；无法安全选择的资源保持阻断。', busy: false });
    case 'build-blocked':
      return Object.freeze({ ...base, statusLabel: state.buildStale ? '等待重新校验' : '候选受阻', statusTone: 'warning',
        currentStage: state.buildStale ? '输入已更新' : '候选验证未通过',
        stageDescription: state.buildStale ? '参数或资源已改变，旧 Validation 与 ApplyGate 已失效。' : state.message,
        nextHint: state.buildStale ? '重新校验会保持候选结构不变。' : '按首要修复建议处理后重新校验。', busy: false });
    case 'revalidating':
      return Object.freeze({ ...base, statusLabel: '正在重新校验', statusTone: 'info', currentStage: '重新计算就绪条件',
        stageDescription: '候选结构不变，正在使用最新参数和资源重新计算 Validation、DryRun 与 ApplyGate。',
        nextHint: '完成后只以后端 canonical 结论更新就绪状态。', busy: true });
    case 'build-ready':
      return Object.freeze({ ...base, statusLabel: '候选已就绪', statusTone: 'ok', currentStage: '候选具备交接条件',
        stageDescription: '结构校验、运行预演和当前输入条件已通过。', blockerCount: 0,
        nextHint: '下一阶段将交接到工作区审核；本轮不执行交接、保存或 Canvas 写入。', busy: false });
    case 'build-failed':
      return Object.freeze({ ...base, statusLabel: '构建失败', statusTone: 'error', currentStage: '构建未完成',
        stageDescription: state.message || '服务端没有产生新的可操作候选。',
        nextHint: state.build ? '上一版候选仅供查看；修复后重新构建会创建新 Build identity。' : '查看首要修复建议后重新构建。', busy: false });
    case 'build-cancelling':
      return Object.freeze({ ...base, statusLabel: '正在取消构建', statusTone: 'warning', currentStage: '取消构建',
        stageDescription: '正在等待服务端确认终态，期间不会创建第二个 Build。',
        nextHint: '终态确认后可重新构建。', busy: true });
    case 'build-cancelled':
      return Object.freeze({ ...base, statusLabel: '构建已取消', statusTone: 'idle', currentStage: '构建已安全停止',
        stageDescription: state.message, nextHint: '重新构建将使用新的 operation 与 Build identity。', busy: false });
    case 'baseline-conflict':
      return Object.freeze({ ...base, statusLabel: '工程基线冲突', statusTone: 'warning', currentStage: '重新确认工程基线',
        stageDescription: state.message, nextHint: '协调服务端最新 PersistenceRevision 与流程 Hash 后重新构建。', busy: false });
    case 'unknown-outcome':
      return Object.freeze({ ...base, statusLabel: '构建结果待确认', statusTone: 'warning', currentStage: '查询构建操作',
        stageDescription: state.message, nextHint: '先按 clientOperationId 查询，禁止盲目重复创建。', busy: false });
    case 'cancelling':
      return Object.freeze({ ...base, statusLabel: '正在取消', statusTone: 'warning', currentStage: '取消规划',
        stageDescription: '正在等待服务端确认终态，期间不会创建新的规划任务。',
        nextHint: '终态确认后可修改任务并重新开始。', busy: true });
    case 'cancelled':
      return Object.freeze({ ...base, statusLabel: '已取消', statusTone: 'idle', currentStage: '规划已取消',
        stageDescription: '本次规划已安全停止，已有公开结果不会继续推进。',
        nextHint: '开始新任务或修改原描述后重新规划。', busy: false });
    case 'recovering':
      return Object.freeze({ ...base, statusLabel: '正在恢复', statusTone: 'info', pageState: state.session ? null : 'loading',
        pageStateTitle: '正在恢复会话', pageStateDescription: '正在读取公开回放并补齐服务端进度。',
        currentStage: '恢复服务端状态', stageDescription: state.message || '正在回放公开事件。',
        nextHint: '恢复期间不会盲目创建新的 Plan Run。', busy: true });
    case 'session-conflict':
      return Object.freeze({ ...base, statusLabel: '状态冲突', statusTone: 'warning', currentStage: '协调会话状态',
        stageDescription: state.message || '服务端状态已更新。',
        nextHint: '核对最新答案与版本后再继续。', busy: false });
    case 'plan-failed':
      return Object.freeze({ ...base, statusLabel: '规划失败', statusTone: 'error', currentStage: '规划未完成',
        stageDescription: state.message || '服务端没有产生可用方案。',
        nextHint: '查看公开诊断并重试任务理解。', busy: false });
    case 'offline-or-service-unavailable':
      return Object.freeze({ ...base, statusLabel: '服务不可用', statusTone: 'error', pageState: state.session ? null : 'error',
        pageStateTitle: 'AI 服务暂时不可用', pageStateDescription: state.message || '无法连接本地服务。',
        currentStage: '等待服务恢复', stageDescription: state.message || '本地服务暂时不可用。',
        nextHint: '检查服务状态后协调恢复，系统不会盲目重建任务。', busy: false });
    case 'disposed':
      return Object.freeze({ ...base, statusLabel: '已卸载', statusTone: 'idle', currentStage: '页面已离开',
        stageDescription: 'Owner 已停止请求、事件流和计时器。', nextHint: '无可用操作。', busy: false });
    case 'idle':
    default:
      return Object.freeze({ ...base, statusLabel: '等待任务', statusTone: 'idle', currentStage: '描述视觉任务',
        stageDescription: state.project ? `当前会话已绑定工程“${state.project.name}”。` : '当前尚未绑定工程，可先完成需求理解与规划。',
        nextHint: '说明检测对象、目标或缺陷，以及期望的 OK/NG 与输出。', busy: false });
  }
}
