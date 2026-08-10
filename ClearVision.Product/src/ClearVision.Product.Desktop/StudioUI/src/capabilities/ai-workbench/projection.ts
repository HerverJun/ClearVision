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
        pageStateTitle: '正在准备工作会话', pageStateDescription: '正在确认当前账户并读取可恢复的会话状态。',
        currentStage: '会话准备', stageDescription: '正在确认用户、工程上下文和服务端状态。',
        nextHint: '会话就绪后可直接描述视觉检测任务。', busy: true });
    case 'intent-routing':
      return Object.freeze({ ...base, statusLabel: '正在理解任务', statusTone: 'info', currentStage: '任务理解',
        stageDescription: '正在识别检测对象、任务类型、图像来源和关键缺口。',
        nextHint: '完成后将自动建立可恢复的规划任务。', busy: true });
    case 'planning':
      return Object.freeze({ ...base, statusLabel: '正在规划', statusTone: 'info', currentStage: '方案规划',
        stageDescription: state.message || '正在生成规划步骤并校验方案约束。',
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
        stageDescription: '正在绑定方案、会话、工程保存基线和本次构建身份。',
        nextHint: '服务端确认本次构建后开始生成候选。', busy: true });
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
        nextHint: '只使用服务端返回的已确认资源；无法安全选择的资源保持阻断。', busy: false });
    case 'build-blocked':
      return Object.freeze({ ...base, statusLabel: state.buildStale ? '等待重新校验' : '候选受阻', statusTone: 'warning',
        currentStage: state.buildStale ? '输入已更新' : '候选验证未通过',
        stageDescription: state.buildStale ? '参数或资源已改变，原有校验与交接条件已失效。' : state.message,
        nextHint: state.buildStale ? '重新校验会保持候选结构不变。' : '按首要修复建议处理后重新校验。', busy: false });
    case 'revalidating':
      return Object.freeze({ ...base, statusLabel: '正在重新校验', statusTone: 'info', currentStage: '重新计算就绪条件',
        stageDescription: '候选结构不变，正在使用最新参数和资源重新执行结构校验、运行预演与交接检查。',
        nextHint: '完成后只使用服务端确认的结论更新就绪状态。', busy: true });
    case 'build-ready':
      return Object.freeze({ ...base, statusLabel: '候选已就绪', statusTone: 'ok', currentStage: '候选具备交接条件',
        stageDescription: '结构校验、运行预演和当前输入条件已通过。', blockerCount: 0,
        nextHint: '可交接到工作区审核；交接不会自动保存或运行。', busy: false });
    case 'handoff-creating':
      return Object.freeze({ ...base, statusLabel: '正在创建交接', statusTone: 'info', currentStage: '创建候选交接',
        stageDescription: state.message, blockerCount: 0,
        nextHint: '服务端确认后将结束当前工作台资源，再进入工程工作区。', busy: true });
    case 'handoff-unknown-outcome':
      return Object.freeze({ ...base, statusLabel: '交接结果待确认', statusTone: 'warning', currentStage: '查询交接结果',
        stageDescription: state.message, blockerCount: 0,
        nextHint: '按当前构建查询已有交接记录，避免重复创建。', busy: false });
    case 'handoff-created':
      return Object.freeze({ ...base, statusLabel: '交接已创建', statusTone: 'ok', currentStage: '释放 AI 工作台',
        stageDescription: state.message, blockerCount: 0,
        nextHint: '进入工程工作区后，将由工作区独立读取并校验候选。', busy: true });
    case 'build-failed':
      return Object.freeze({ ...base, statusLabel: '构建失败', statusTone: 'error', currentStage: '构建未完成',
        stageDescription: state.message || '服务端没有产生新的可操作候选。',
        nextHint: state.build ? '上一版候选仅供查看；修复后重新构建会创建新的构建记录。' : '查看首要修复建议后重新构建。', busy: false });
    case 'build-cancelling':
      return Object.freeze({ ...base, statusLabel: '正在取消构建', statusTone: 'warning', currentStage: '取消构建',
        stageDescription: '正在等待服务端确认终态，期间不会创建第二个构建任务。',
        nextHint: '终态确认后可重新构建。', busy: true });
    case 'build-cancelled':
      return Object.freeze({ ...base, statusLabel: '构建已取消', statusTone: 'idle', currentStage: '构建已安全停止',
        stageDescription: state.message, nextHint: '重新构建将创建新的构建记录。', busy: false });
    case 'baseline-conflict':
      return Object.freeze({ ...base, statusLabel: '工程保存基线冲突', statusTone: 'warning', currentStage: '重新确认工程保存基线',
        stageDescription: state.message, nextHint: '读取最新工程保存修订与流程版本后重新构建。', busy: false });
    case 'unknown-outcome':
      return Object.freeze({ ...base, statusLabel: '构建结果待确认', statusTone: 'warning', currentStage: '查询构建操作',
        stageDescription: state.message, nextHint: '先按本次操作标识查询结果，避免重复创建。', busy: false });
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
        nextHint: '恢复期间不会重复创建新的规划任务。', busy: true });
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
        stageDescription: '页面相关请求、事件连接和计时器已停止。', nextHint: '无可用操作。', busy: false });
    case 'idle':
    default:
      return Object.freeze({ ...base, statusLabel: '等待任务', statusTone: 'idle', currentStage: '描述视觉任务',
        stageDescription: state.project ? `当前会话已绑定工程“${state.project.name}”。` : '当前尚未绑定工程，可先完成需求理解与规划。',
        nextHint: '说明检测对象、目标或缺陷，以及期望的 OK/NG 与输出。', busy: false });
  }
}
