import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import AiResourceDecisionPanel from '@/capabilities/ai-workbench/AiResourceDecisionPanel.vue';
import { resourceRequirementFixture } from './aiFixtures';

describe('AiResourceDecisionPanel', () => {
  it('emits only canonical resource identity fields for enabled camera bindings', async () => {
    const resource = resourceRequirementFixture();
    const wrapper = mount(AiResourceDecisionPanel, {
      props: {
        resources: [resource],
        cameraBindings: [
          { id: 'camera-binding-01', displayName: 'Line camera', isEnabled: true },
          { id: 'camera-binding-disabled', displayName: 'Disabled camera', isEnabled: false }
        ],
        busy: false
      }
    });

    await wrapper.get('select').setValue('camera-binding-01');
    await wrapper.get('button').trigger('click');

    const payload = wrapper.emitted('save')?.[0]?.[0] as Array<Record<string, unknown>>;
    expect(payload).toEqual([{
      canonicalId: resource.canonicalId,
      resourceKey: 'camera-binding-01'
    }]);
    expect(Object.keys(payload[0] ?? {}).sort()).toEqual(['canonicalId', 'resourceKey']);
    expect(wrapper.find('option[value="camera-binding-disabled"]').attributes('disabled')).toBeDefined();
    wrapper.unmount();
  });

  it('keeps unsupported resource types blocked without a free-text control', () => {
    const wrapper = mount(AiResourceDecisionPanel, {
      props: {
        resources: [resourceRequirementFixture('model_resource')],
        cameraBindings: [],
        busy: false
      }
    });

    expect(wrapper.find('select').exists()).toBe(false);
    expect(wrapper.find('input').exists()).toBe(false);
    expect(wrapper.text()).toContain('当前没有可用的资源选择项');
    expect(wrapper.text()).not.toContain('安全选择合同');
    expect(wrapper.get('button').attributes('disabled')).toBeDefined();
    wrapper.unmount();
  });
});
