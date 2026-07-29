import type { AiWorkbenchState } from './reducer';

export type AiWorkbenchActionId =
  | 'submitTask'
  | 'retryIntent'
  | 'startPlan'
  | 'cancelPlan'
  | 'answerClarification'
  | 'acceptRecommendedAnswers'
  | 'previewReadiness'
  | 'reconcile'
  | 'startNewTask';

export interface AiWorkbenchAction {
  readonly id: AiWorkbenchActionId;
  readonly label: string;
  readonly primary: boolean;
  readonly enabled: boolean;
  readonly disabledReason: string;
}

export interface AiWorkbenchActionModel {
  readonly primary: AiWorkbenchAction | null;
  readonly secondary: readonly AiWorkbenchAction[];
  readonly statusHint: string;
  readonly nextStagePlaceholder: Readonly<{ label: string; disabledReason: string }> | null;
}

function action(
  id: AiWorkbenchActionId,
  label: string,
  primary: boolean,
  enabled = true,
  disabledReason = ''
): AiWorkbenchAction {
  return Object.freeze({ id, label, primary, enabled, disabledReason });
}

export function aiWorkbenchActionModel(state: AiWorkbenchState): AiWorkbenchActionModel {
  switch (state.phase) {
    case 'idle':
      return Object.freeze({ primary: action('submitTask', '理解并规划任务', true), secondary: Object.freeze([]),
        statusHint: '任务描述将先经过 Intent Router，再创建可恢复的 Plan Run。', nextStagePlaceholder: null });
    case 'planning':
      return Object.freeze({ primary: action('cancelPlan', '取消规划', false, state.run.runId !== null,
        state.run.runId ? '' : 'Plan Run 尚未由服务端确认。'), secondary: Object.freeze([]),
        statusHint: '公开进度通过 replay 与 SSE 恢复。', nextStagePlaceholder: null });
    case 'clarifying':
      return Object.freeze({ primary: action('answerClarification', '确认回答并重新检查', true),
        secondary: Object.freeze([action('acceptRecommendedAnswers', '采用推荐答案', false)]),
        statusHint: '推荐项会说明依据；只有服务端接受的答案会标记为已确认。', nextStagePlaceholder: null });
    case 'plan-blocked':
      return Object.freeze({
        primary: state.plan?.clarificationQuestions.length
          ? action('answerClarification', '确认回答并重新检查', true)
          : action('retryIntent', '重新理解任务', true),
        secondary: state.plan?.clarificationQuestions.length
          ? Object.freeze([action('acceptRecommendedAnswers', '采用推荐答案', false)])
          : Object.freeze([]),
        statusHint: state.message,
        nextStagePlaceholder: null
      });
    case 'plan-ready':
      return Object.freeze({ primary: null, secondary: Object.freeze([action('startNewTask', '开始新任务', false)]),
        statusHint: '方案已具备构建条件。',
        nextStagePlaceholder: Object.freeze({ label: '进入下一阶段', disabledReason: '构建与资源绑定将在 G3 开放。' }) });
    case 'cancelled':
      return Object.freeze({ primary: action('startNewTask', '开始新任务', true), secondary: Object.freeze([]),
        statusHint: '规划终态已由服务端确认。', nextStagePlaceholder: null });
    case 'recovering':
    case 'session-conflict':
    case 'offline-or-service-unavailable':
      return Object.freeze({ primary: action('reconcile', '协调服务端状态', true), secondary: Object.freeze([]),
        statusHint: state.message, nextStagePlaceholder: null });
    case 'plan-failed':
      return Object.freeze({ primary: action('retryIntent', '重新理解任务', true),
        secondary: Object.freeze([action('reconcile', '协调服务端状态', false)]),
        statusHint: state.message, nextStagePlaceholder: null });
    default:
      return Object.freeze({ primary: null, secondary: Object.freeze([]), statusHint: state.message, nextStagePlaceholder: null });
  }
}

export function aiWorkbenchActions(state: AiWorkbenchState): readonly AiWorkbenchAction[] {
  const model = aiWorkbenchActionModel(state);
  return Object.freeze([...(model.primary ? [model.primary] : []), ...model.secondary]);
}
