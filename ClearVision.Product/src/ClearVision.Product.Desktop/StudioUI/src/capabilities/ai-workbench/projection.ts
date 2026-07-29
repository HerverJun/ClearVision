import type { CvPageStateKind } from '@/design-system/patterns';
import type { CvStatusTone } from '@/design-system/primitives';
import type { AiSessionState } from './reducer';

export interface AiWorkbenchProjection {
  readonly statusLabel: string;
  readonly statusTone: CvStatusTone;
  readonly pageState: CvPageStateKind | null;
  readonly pageStateTitle: string;
  readonly pageStateDescription: string;
  readonly canRetry: boolean;
}

export function projectAiWorkbench(state: AiSessionState): AiWorkbenchProjection {
  if (state.phase === 'loading') return Object.freeze({
    statusLabel: '正在建立会话…', statusTone: 'info', pageState: 'loading',
    pageStateTitle: '正在建立安全会话', pageStateDescription: '服务端正在绑定当前认证用户并保存会话身份。', canRetry: false
  });
  if (state.phase === 'recovering') return Object.freeze({
    statusLabel: '正在恢复…', statusTone: 'info', pageState: 'loading',
    pageStateTitle: '正在恢复会话', pageStateDescription: '正在读取当前用户可访问的最新公开状态。', canRetry: false
  });
  if (state.phase === 'error') return Object.freeze({
    statusLabel: '会话不可用', statusTone: 'error', pageState: 'error',
    pageStateTitle: '会话未能就绪', pageStateDescription: state.message || '服务端没有确认会话状态。', canRetry: true
  });
  if (state.phase === 'ready') return Object.freeze({
    statusLabel: '会话已就绪', statusTone: 'ok', pageState: null,
    pageStateTitle: '', pageStateDescription: '', canRetry: false
  });
  return Object.freeze({
    statusLabel: '尚未连接', statusTone: 'idle', pageState: 'empty',
    pageStateTitle: '会话尚未建立', pageStateDescription: '进入页面后将建立或恢复当前用户的会话。', canRetry: false
  });
}
