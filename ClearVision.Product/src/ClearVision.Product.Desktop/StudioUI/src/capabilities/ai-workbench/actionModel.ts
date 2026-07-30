import type { AiWorkbenchState } from './reducer';

export type AiWorkbenchActionId =
  | 'submitTask'
  | 'retryIntent'
  | 'startPlan'
  | 'cancelPlan'
  | 'startBuild'
  | 'cancelBuild'
  | 'confirmParameters'
  | 'updateResourceDecision'
  | 'recheckReadiness'
  | 'rebuild'
  | 'answerClarification'
  | 'acceptRecommendedAnswers'
  | 'previewReadiness'
  | 'prepareHandoff'
  | 'reconcileHandoff'
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
      return Object.freeze({ primary: action('startBuild', '开始构建', true),
        secondary: Object.freeze([action('startNewTask', '开始新任务', false)]),
        statusHint: '方案已具备构建条件。',
        nextStagePlaceholder: null });
    case 'build-starting':
    case 'building':
    case 'validating':
      return Object.freeze({
        primary: action('cancelBuild', '取消构建', false, state.run.runId !== null,
          state.run.runId ? '' : 'Build Run 尚未由服务端确认。'),
        secondary: Object.freeze([]),
        statusHint: '公开阶段通过 replay 与 SSE 恢复。',
        nextStagePlaceholder: null
      });
    case 'parameters-pending':
      return Object.freeze({ primary: action('confirmParameters', '确认参数', true), secondary: Object.freeze([]),
        statusHint: '建议值不会自动视为已确认；确认后必须重新校验。', nextStagePlaceholder: null });
    case 'resources-pending':
      return Object.freeze({ primary: action('updateResourceDecision', '保存资源决策', true), secondary: Object.freeze([]),
        statusHint: '只接受当前 Build 返回的 canonical 资源身份。', nextStagePlaceholder: null });
    case 'build-blocked':
      return Object.freeze({ primary: action('recheckReadiness', '重新校验', true),
        secondary: Object.freeze([action('rebuild', '重新构建', false)]),
        statusHint: state.buildStale ? '输入已更新，旧验证与就绪结论已失效。' : state.message,
        nextStagePlaceholder: null });
    case 'revalidating':
      return Object.freeze({ primary: null, secondary: Object.freeze([]),
        statusHint: '候选结构保持不变，服务端正在重新计算 Validation 与 ApplyGate。', nextStagePlaceholder: null });
    case 'build-ready':
      return Object.freeze({ primary: action('prepareHandoff', '交接到工作区审核', true),
        secondary: Object.freeze([action('recheckReadiness', '重新校验', false)]),
        statusHint: '交接只会创建短期候选工件，不会自动保存或运行。',
        nextStagePlaceholder: null });
    case 'handoff-creating':
      return Object.freeze({ primary: null, secondary: Object.freeze([]),
        statusHint: '服务端正在重新证明 Build、revision、baseline 与 candidate fingerprint。',
        nextStagePlaceholder: null });
    case 'handoff-unknown-outcome':
      return Object.freeze({ primary: action('reconcileHandoff', '查询交接结果', true),
        secondary: Object.freeze([]), statusHint: state.message, nextStagePlaceholder: null });
    case 'handoff-created':
      return Object.freeze({ primary: null, secondary: Object.freeze([]),
        statusHint: 'AI owner 即将释放；Workspace owner 挂载后才会读取候选。', nextStagePlaceholder: null });
    case 'build-failed':
    case 'build-cancelled':
      return Object.freeze({ primary: action('rebuild', '重新构建', true),
        secondary: Object.freeze([action('reconcile', '协调服务端状态', false)]),
        statusHint: state.build ? '上一版候选仅供查看，不能继续操作。' : state.message, nextStagePlaceholder: null });
    case 'baseline-conflict':
    case 'unknown-outcome':
      return Object.freeze({ primary: action('reconcile', '协调服务端状态', true), secondary: Object.freeze([]),
        statusHint: state.message, nextStagePlaceholder: null });
    case 'build-cancelling':
      return Object.freeze({ primary: null, secondary: Object.freeze([]),
        statusHint: '正在等待后端终态 reservation。', nextStagePlaceholder: null });
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
