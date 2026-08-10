import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import AiBuildWorkspace from '@/capabilities/ai-workbench/AiBuildWorkspace.vue';
import { CvStatusBadge } from '@/design-system/primitives';
import { buildResultFixture, validationFixture } from './aiFixtures';

describe('AiBuildWorkspace', () => {
  it('renders public warnings, blockers and deployment boundaries without an executable handoff action', () => {
    const base = buildResultFixture();
    const build = buildResultFixture({
      publicWarnings: ['自动修复采用了保守阈值。'],
      workflowDiff: {
        ...base.workflowDiff,
        validationFailures: ['结果判定输入尚未连接。'],
        deploymentBlockers: ['工作区审核尚未执行。']
      },
      validation: {
        ...base.validation,
        applyGate: {
          ...base.validation.applyGate,
          applyBlockers: ['请确认全部待处理参数。'],
          deploymentBlockers: ['运行草稿尚未审核。']
        }
      }
    });
    const wrapper = mount(AiBuildWorkspace, {
      props: { build: build as never, stale: false, diagnostics: null }
    });

    expect(wrapper.text()).toContain('自动修复采用了保守阈值。');
    expect(wrapper.text()).toContain('结果判定输入尚未连接。');
    expect(wrapper.text()).toContain('请确认全部待处理参数。');
    expect(wrapper.text()).toContain('工作区审核尚未执行。');
    expect(wrapper.text()).toContain('运行草稿尚未审核。');
    expect(wrapper.text()).toContain('本阶段不执行交接、保存或部署。');
    expect(wrapper.find('button').exists()).toBe(false);
    wrapper.unmount();
  });

  it('does not present an expired ready candidate as eligible or ready', () => {
    const build = buildResultFixture({ validation: validationFixture(true) });
    const wrapper = mount(AiBuildWorkspace, {
      props: { build: build as never, stale: true, diagnostics: null }
    });

    expect(wrapper.text()).toContain('候选结论已失效');
    expect(wrapper.text()).toContain('请基于最新参数、资源、方案和工程保存基线重新构建并校验候选。');
    expect(wrapper.text()).not.toContain('候选已具备交接条件');
    const statusLabels = wrapper.findAllComponents(CvStatusBadge).map(badge => badge.props('label'));
    expect(statusLabels).not.toContain('候选就绪');
    expect(statusLabels.filter(label => label === '结论已失效')).toHaveLength(4);
    wrapper.unmount();
  });
});
