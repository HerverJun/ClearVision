import { describe, expect, it } from 'vitest';
import { projectAiWorkbench } from '@/capabilities/ai-workbench/projection';
import { initialAiSessionState, reduceAiSession } from '@/capabilities/ai-workbench/reducer';

describe('AI Workbench projection', () => {
  it('uses explicit loading typography for create and recovery states', () => {
    const creating = reduceAiSession(initialAiSessionState, { type: 'start', mode: 'create', at: 1 });
    const recovering = reduceAiSession(initialAiSessionState, { type: 'start', mode: 'hydrate', at: 1 });

    expect(projectAiWorkbench(creating).statusLabel).toBe('正在建立会话…');
    expect(projectAiWorkbench(recovering).statusLabel).toBe('正在恢复…');
  });
});
