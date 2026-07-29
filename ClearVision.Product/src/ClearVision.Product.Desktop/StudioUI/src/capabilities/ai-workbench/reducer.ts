import type { AiOperationProjectionV1, AiSessionDetailV1 } from './contracts';

export type AiSessionPhase = 'idle' | 'loading' | 'recovering' | 'ready' | 'error' | 'disposed';

export interface AiSessionState {
  readonly phase: AiSessionPhase;
  readonly session: AiSessionDetailV1 | null;
  readonly operation: AiOperationProjectionV1 | null;
  readonly errorCode: string | null;
  readonly message: string;
  readonly updatedAt: number;
}

export type AiSessionEvent =
  | Readonly<{ type: 'start'; mode: 'create' | 'hydrate'; at: number }>
  | Readonly<{ type: 'ready'; session: AiSessionDetailV1; operation?: AiOperationProjectionV1 | null; at: number }>
  | Readonly<{ type: 'failed'; errorCode: string; message: string; at: number }>
  | Readonly<{ type: 'retry'; at: number }>
  | Readonly<{ type: 'dispose'; at: number }>;

export const initialAiSessionState: AiSessionState = Object.freeze({
  phase: 'idle',
  session: null,
  operation: null,
  errorCode: null,
  message: '',
  updatedAt: 0
});

export function reduceAiSession(state: AiSessionState, event: AiSessionEvent): AiSessionState {
  if (state.phase === 'disposed') return state;
  switch (event.type) {
    case 'start':
      return Object.freeze({
        ...state,
        phase: event.mode === 'hydrate' ? 'recovering' : 'loading',
        errorCode: null,
        message: event.mode === 'hydrate' ? '正在恢复会话状态。' : '正在建立安全会话。',
        updatedAt: event.at
      });
    case 'ready':
      return Object.freeze({
        phase: 'ready',
        session: event.session,
        operation: event.operation ?? state.operation,
        errorCode: null,
        message: '会话已由服务端确认。',
        updatedAt: event.at
      });
    case 'failed':
      return Object.freeze({
        ...state,
        phase: 'error',
        errorCode: event.errorCode,
        message: event.message,
        updatedAt: event.at
      });
    case 'retry':
      return Object.freeze({ ...state, phase: 'idle', errorCode: null, message: '', updatedAt: event.at });
    case 'dispose':
      return Object.freeze({ ...state, phase: 'disposed', message: '', updatedAt: event.at });
  }
}
