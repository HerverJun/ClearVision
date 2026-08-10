import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import RunConsole from '@/capabilities/inspection-run/RunConsole.vue';
import RunStatusBar from '@/capabilities/inspection-run/RunStatusBar.vue';
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

  it('keeps diagnostic codes in secondary technical details', () => {
    const wrapper = mount(RunConsole, {
      props: {
        ...baseProps,
        errorCode: 'RUN_ADMISSION_BLOCKED',
        violations: [{
          key: 'parameter',
          code: 'PENDING_PARAMETER',
          message: '请先配置模型文件路径。',
          target: '模型文件路径'
        }]
      }
    });

    expect(wrapper.get('.run-console__control > p').text()).not.toContain('RUN_ADMISSION_BLOCKED');
    expect(wrapper.get('.run-console__technical').text()).toContain('RUN_ADMISSION_BLOCKED');
    expect(wrapper.get('.run-console__violations').text()).toContain('PENDING_PARAMETER');
    expect(wrapper.text()).toContain('运行前检查');
  });
});

describe('RunStatusBar', () => {
  it('keeps the decision controls compact and labels an unknown result for reconciliation', async () => {
    const wrapper = mount(RunStatusBar, {
      props: {
        phaseLabel: '运行结果待确认',
        tone: 'warning',
        connected: false,
        pending: false,
        canStart: false,
        canStop: false,
        canReconcile: true,
        admissionLabel: '准入阻断 1 项',
        admissionTone: 'ng',
        blockerCount: 1,
        blockerMessage: '请先保存当前工程'
      }
    });

    expect(wrapper.get('[data-testid="run-status-bar"]').text()).toContain('阻断 1 项');
    expect(wrapper.get('[data-testid="run-status-bar"]').text()).toContain('运行检查阻断 1 项');
    expect(wrapper.get('[data-testid="run-status-bar"]').text()).not.toContain('准入');
    expect(wrapper.get('[data-testid="run-console-reconcile"]').text()).toContain('核对结果');
    expect(wrapper.find('[data-testid="run-console-start"]').attributes('disabled')).toBeDefined();

    await wrapper.get('[data-testid="workspace-run-details"]').trigger('click');
    await wrapper.get('[data-testid="run-console-admission-refresh"]').trigger('click');
    expect(wrapper.emitted('details')).toHaveLength(1);
    expect(wrapper.emitted('checkAdmission')).toHaveLength(1);
  });
});
