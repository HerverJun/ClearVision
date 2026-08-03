import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import RunConsole from '@/capabilities/inspection-run/RunConsole.vue';
import { emptyRunConsoleStatistics } from '@/capabilities/inspection-run/runConsoleProjection';

const baseProps = {
  mode: 'formal' as const,
  projectName: '瓶盖检测 A',
  phaseLabel: '正式运行中',
  tone: 'warning' as const,
  message: '后端正在执行已准入的工程快照。',
  connected: true,
  pending: true,
  canStart: false,
  canStop: true,
  canReconcile: true,
  identity: [],
  admission: [],
  violations: [],
  statistics: emptyRunConsoleStatistics,
  results: []
};

describe('RunConsole', () => {
  it('keeps Stop and reconcile available while a run is pending when the owner authorizes them', async () => {
    const wrapper = mount(RunConsole, { props: baseProps });
    const stop = wrapper.get('[data-testid="run-console-stop"]');
    const reconcile = wrapper.get('[data-testid="run-console-reconcile"]');

    expect(stop.attributes()).not.toHaveProperty('disabled');
    expect(reconcile.attributes()).not.toHaveProperty('disabled');

    await stop.trigger('click');
    await reconcile.trigger('click');
    expect(wrapper.emitted('stop')).toHaveLength(1);
    expect(wrapper.emitted('reconcile')).toHaveLength(1);
  });
});
