import type { AiSessionState } from './reducer';

export type AiWorkbenchActionId = 'retry-session' | 'refresh-session';

export interface AiWorkbenchAction {
  readonly id: AiWorkbenchActionId;
  readonly label: string;
  readonly primary: boolean;
}

export function aiWorkbenchActions(state: AiSessionState): readonly AiWorkbenchAction[] {
  if (state.phase === 'error') {
    return Object.freeze([{ id: 'retry-session', label: '重试会话', primary: true }]);
  }
  if (state.phase === 'ready') {
    return Object.freeze([{ id: 'refresh-session', label: '刷新状态', primary: false }]);
  }
  return Object.freeze([]);
}
