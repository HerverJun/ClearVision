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
        nextHint: '进入下一阶段将在 G3 开放；本页面不会启动构建。', busy: false });
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
