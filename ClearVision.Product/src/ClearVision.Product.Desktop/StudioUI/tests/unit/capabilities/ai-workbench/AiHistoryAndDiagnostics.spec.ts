import { afterEach, describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { nextTick } from 'vue';
import AiDiagnosticsDrawer from '@/capabilities/ai-workbench/AiDiagnosticsDrawer.vue';
import AiHistoryDrawer from '@/capabilities/ai-workbench/AiHistoryDrawer.vue';
import type { AiHistoryState } from '@/capabilities/ai-workbench/aiHistoryController';
import { projectAiWorkbench } from '@/capabilities/ai-workbench/projection';
import { initialAiWorkbenchState } from '@/capabilities/ai-workbench/reducer';
import { aiTimestamp } from './aiFixtures';

afterEach(() => {
  document.body.innerHTML = '';
});

const history: AiHistoryState = Object.freeze({
  sessions: Object.freeze({
    items: Object.freeze([{
      sessionId: 'session_private_internal_01',
      lifecycleState: 'plan_ready',
      projectId: null,
      revision: 7,
      updatedAtUtc: aiTimestamp
    }]),
    offset: 0,
    limit: 10,
    total: 1
  }),
  runs: Object.freeze({
    items: Object.freeze([{
      runId: 'run_private_internal_01',
      sessionId: 'session_private_internal_01',
      kind: 'plan' as const,
      status: 'completed' as const,
      title: '方案规划',
      summary: '已形成公开方案摘要。',
      firstFixRecommendation: '核对目标缺陷尺寸。',
      recoveryState: 'terminal' as const,
      createdAtUtc: aiTimestamp,
      updatedAtUtc: aiTimestamp,
      lastSequence: 4,
      eventCount: 4
    }]),
    offset: 0,
    limit: 10,
    total: 1
  }),
  sessionsPhase: 'ready',
  runsPhase: 'ready',
  deletePhase: 'idle',
  deletingSessionId: null,
  deleteOperation: null,
  errorCode: null,
  message: ''
});

describe('AI history and diagnostics drawers', () => {
  it('keeps history unmounted by default, traps focus when opened and hides internal identities', async () => {
    const wrapper = mount(AiHistoryDrawer, {
      attachTo: document.body,
      props: {
        open: false,
        history,
        currentSessionId: null,
        routeProjectId: null
      }
    });
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();

    await wrapper.setProps({ open: true });
    await nextTick();
    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(dialog?.textContent).toContain('历史与恢复');
    expect(dialog?.textContent).not.toContain('session_private_internal_01');
    expect(dialog?.textContent).not.toContain('run_private_internal_01');
    expect(document.activeElement?.getAttribute('aria-label')).toBe('关闭抽屉');

    const escapedTarget = document.createElement('button');
    escapedTarget.textContent = 'outside';
    document.body.append(escapedTarget);
    escapedTarget.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    expect(dialog?.contains(document.activeElement)).toBe(true);
    escapedTarget.remove();

    const tabs = [...(dialog?.querySelectorAll<HTMLButtonElement>('[role="tab"]') ?? [])];
    expect(tabs.map(tab => tab.tabIndex)).toEqual([0, -1]);
    expect(dialog?.querySelector<HTMLElement>('#ai-history-session-panel')?.style.display).toBe('');
    expect(dialog?.querySelector<HTMLElement>('#ai-history-run-panel')?.style.display).toBe('none');
    tabs[0]?.focus();
    tabs[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await nextTick();
    expect(tabs.map(tab => tab.tabIndex)).toEqual([-1, 0]);
    expect(document.activeElement).toBe(tabs[1]);
    expect(dialog?.querySelector<HTMLElement>('#ai-history-session-panel')?.style.display).toBe('none');
    expect(dialog?.querySelector<HTMLElement>('#ai-history-run-panel')?.style.display).toBe('');

    tabs[1]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    await nextTick();
    expect(document.activeElement).toBe(tabs[0]);
    tabs[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    await nextTick();
    expect(document.activeElement).toBe(tabs[1]);
    tabs[1]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
    await nextTick();
    expect(document.activeElement).toBe(tabs[0]);

    dialog?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(wrapper.emitted('close')).toHaveLength(1);
    wrapper.unmount();
  });

  it('renders only the approved public diagnostic projection and starts closed', async () => {
    const state = Object.freeze({
      ...initialAiWorkbenchState,
      errorCode: 'public_error_code',
      message: '公开错误说明。',
      updatedAt: 1
    });
    const wrapper = mount(AiDiagnosticsDrawer, {
      attachTo: document.body,
      props: {
        open: false,
        state,
        projection: projectAiWorkbench(state)
      }
    });
    expect(document.body.textContent).not.toContain('公开诊断');

    await wrapper.setProps({ open: true });
    await nextTick();
    const text = document.body.textContent ?? '';
    expect(text).toContain('阶段时间线');
    expect(text).toContain('public_error_code');
    expect(text).toContain('首要修复建议');
    expect(text).not.toMatch(/活动资源|重复 \/ 丢弃|公开事件/);
    expect(text).not.toMatch(/system prompt|chain-of-thought|Bearer|api[_ -]?key|C:\\|PLC:\/\//i);
    wrapper.unmount();
  });

  it('isolates the application while open and restores scroll and focus after close or unmount', async () => {
    const application = document.createElement('div');
    application.id = 'app';
    const trigger = document.createElement('button');
    trigger.textContent = '打开历史';
    application.append(trigger);
    document.body.append(application);
    trigger.focus();

    const wrapper = mount(AiHistoryDrawer, {
      attachTo: application,
      props: {
        open: false,
        history,
        currentSessionId: null,
        routeProjectId: null
      }
    });

    await wrapper.setProps({ open: true });
    await nextTick();
    expect(application.inert).toBe(true);
    expect(application.getAttribute('aria-hidden')).toBe('true');
    expect(document.body.style.overflow).toBe('hidden');

    await wrapper.setProps({ open: false });
    await nextTick();
    expect(application.inert).toBe(false);
    expect(application.hasAttribute('aria-hidden')).toBe(false);
    expect(document.body.style.overflow).toBe('');
    expect(document.activeElement).toBe(trigger);

    await wrapper.setProps({ open: true });
    await nextTick();
    wrapper.unmount();
    expect(application.inert).toBe(false);
    expect(application.hasAttribute('aria-hidden')).toBe(false);
    expect(document.body.style.overflow).toBe('');
    expect(document.activeElement).toBe(trigger);
  });
});
