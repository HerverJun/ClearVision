import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import SettingsPlcPanel from '@/capabilities/settings/SettingsPlcPanel.vue';
import type {
  PlcMappingV1,
  PlcMappingsResponseV1,
  PlcProfileV1,
  PlcSettingsResponseV1,
  PlcSettingsV1,
  SettingsOwner,
  SettingsWriteResult
} from '@/capabilities/settings';

function mapping(overrides: Partial<PlcMappingV1> = {}): PlcMappingV1 {
  return { name: 'Ready', address: 'M0', dataType: 'Bool', description: 'fixture', canWrite: false, ...overrides };
}

function profile(port: number, mappings: readonly PlcMappingV1[] = [mapping()]): PlcProfileV1 {
  return { ipAddress: '127.0.0.1', port, mappings, cpuType: 'S7-1200', rack: 0, slot: 1 };
}

function settings(overrides: Partial<PlcSettingsV1> = {}): PlcSettingsV1 {
  return { activeProtocol: 'S7', heartbeatIntervalMs: 1000, s7: profile(102), mc: profile(5002), fins: profile(9600), ...overrides };
}

function completed<T>(value: T): SettingsWriteResult<T> {
  return { status: 'completed', section: 'plc', generation: 0, operationKind: 'write', value };
}

function response(value: PlcSettingsV1): PlcSettingsResponseV1 {
  return { success: true, message: 'saved', settings: value, errors: [] };
}

function makeOwner(initial = settings()) {
  let currentSettings = initial;
  const projection = reactive({
    phase: 'ready', role: 'Admin', settings: null, error: null, message: '', generation: 0,
    started: true, dirtySectionCount: 0, pendingSectionCount: 0,
    device: {
      plcSettings: currentSettings, plcMappings: currentSettings.s7.mappings, tcpProfiles: [], tcpStatuses: {}, tcpFrames: {},
      cameraBindings: [], activeCameraId: '', cameraDiscovery: null, triggerDiagnostics: null,
      serialPorts: [], preview: {
        phase: 'idle', sessionId: null, cameraBindingId: null, imageUrl: null, width: null, height: null,
        frameSequence: null, triggerMode: null, triggerSource: null, contentType: null, message: 'idle'
      }
    }
  });
  const readPlcSettings = vi.fn(async () => completed(response(currentSettings)));
  const readPlcMappings = vi.fn(async () => completed({
    success: true, message: 'read', mappings: currentSettings.s7.mappings, errors: []
  }));
  const savePlcSettings = vi.fn(async (value: PlcSettingsV1) => {
    currentSettings = value;
    return completed(response(value));
  });
  const savePlcMappings = vi.fn(async (value: readonly PlcMappingV1[]): Promise<SettingsWriteResult<PlcMappingsResponseV1>> => completed({
    success: true, message: 'saved', mappings: [mapping({ name: 'Authoritative' }), ...value.slice(1)], errors: []
  }));
  const owner = {
    projection,
    registerPanelState: vi.fn(() => () => undefined),
    refreshPanelState: vi.fn(),
    readPlcSettings,
    readPlcMappings,
    savePlcSettings,
    savePlcMappings,
    testPlcConnection: vi.fn()
  } as unknown as SettingsOwner;
  return { owner, readPlcSettings, readPlcMappings, savePlcSettings, savePlcMappings };
}

describe('F07 G5 PLC protocol and mapping lifecycle', () => {
  it('rereads the protocol projection after saving settings', async () => {
    const fixture = makeOwner();
    const wrapper = mount(SettingsPlcPanel, { props: { owner: fixture.owner, canWrite: true } });
    await flushPromises();

    await wrapper.get('input[name="plcHeartbeatIntervalMs"]').setValue('2000');
    await wrapper.get('[data-plc-action="save-settings"]').trigger('click');
    await flushPromises();

    expect(fixture.savePlcSettings).toHaveBeenCalledWith(expect.objectContaining({ heartbeatIntervalMs: 2000 }));
    expect(fixture.readPlcSettings).toHaveBeenCalledTimes(2);
    wrapper.unmount();
  });

  it('blocks mapping save when local protocol differs from server ActiveProtocol and uses returned mappings as baseline', async () => {
    const fixture = makeOwner();
    const wrapper = mount(SettingsPlcPanel, { props: { owner: fixture.owner, canWrite: true } });
    await flushPromises();

    await wrapper.get('select[name="plcProtocol"]').setValue('MC');
    await wrapper.get('input[aria-label="变量名"]').setValue('LocalDraft');
    const mismatchButton = wrapper.get('[data-plc-action="save-mappings"]');
    expect(mismatchButton.attributes('disabled')).toBeDefined();
    await mismatchButton.trigger('click');
    expect(fixture.savePlcMappings).not.toHaveBeenCalled();

    await wrapper.get('select[name="plcProtocol"]').setValue('S7');
    await wrapper.get('input[aria-label="变量名"]').setValue('LocalDraft');
    await wrapper.get('[data-plc-action="save-mappings"]').trigger('click');
    await flushPromises();

    expect(fixture.savePlcMappings).toHaveBeenCalledWith([expect.objectContaining({ name: 'LocalDraft' })]);
    expect((wrapper.get('input[aria-label="变量名"]').element as HTMLInputElement).value).toBe('Authoritative');
    wrapper.unmount();
  });

  it('associates local and server validation errors with their PLC fields without duplicate announcements', async () => {
    const fixture = makeOwner();
    const wrapper = mount(SettingsPlcPanel, { props: { owner: fixture.owner, canWrite: true } });
    await flushPromises();

    const rackField = wrapper.get('input[name="plcRack"]');
    await rackField.setValue('16');
    expect(rackField.attributes('aria-invalid')).toBe('true');
    const rackErrorId = rackField.attributes('aria-describedby');
    expect(rackErrorId).toBeTruthy();
    expect(wrapper.get(`#${rackErrorId}`).text()).toContain('机架号（Rack）必须在 0-15 之间');

    await rackField.setValue('0');
    await wrapper.get('input[aria-label="变量名"]').setValue('InvalidName');
    fixture.savePlcMappings.mockResolvedValueOnce(completed({
      success: false,
      message: 'invalid mapping',
      mappings: [],
      errors: [{
        protocol: 'S7', profileId: null, section: 'mapping', field: 'Name', index: 0,
        message: '变量名与现有映射冲突。'
      }]
    }));
    await wrapper.get('[data-plc-action="save-mappings"]').trigger('click');
    await flushPromises();

    const mappingName = wrapper.get('input[aria-label="变量名"]');
    expect(mappingName.attributes('name')).toBe('plcMappingName-0');
    expect(mappingName.attributes('autocomplete')).toBe('off');
    expect(mappingName.attributes('aria-invalid')).toBe('true');
    const mappingErrorId = mappingName.attributes('aria-describedby');
    expect(mappingErrorId).toBeTruthy();
    expect(wrapper.get(`#${mappingErrorId}`).text()).toBe('变量名与现有映射冲突。');
    const validationList = wrapper.get('.validation-list');
    expect(validationList.attributes('role')).toBeUndefined();
    expect(validationList.attributes('aria-live')).toBeUndefined();
    expect(wrapper.get(`#${mappingErrorId}`).attributes('role')).toBe('alert');

    await wrapper.findAll('button').find(button => button.text().includes('刷新'))?.trigger('click');
    await flushPromises();
    expect(wrapper.find('.validation-list').exists()).toBe(false);
    wrapper.unmount();
  });

  it('matches validation issues by protocol and section, then clears them when the protocol changes', async () => {
    const fixture = makeOwner();
    const wrapper = mount(SettingsPlcPanel, { props: { owner: fixture.owner, canWrite: true } });
    await flushPromises();

    await wrapper.get('input[aria-label="变量名"]').setValue('ProtocolDraft');
    fixture.savePlcMappings.mockResolvedValueOnce(completed({
      success: false,
      message: 'invalid mapping',
      mappings: [],
      errors: [
        {
          protocol: 'S7', profileId: null, section: 'mapping', field: 'address', index: 0,
          message: '当前协议地址无效。'
        },
        {
          protocol: 'MC', profileId: null, section: 'mapping', field: 'name', index: 0,
          message: '其他协议错误不应绑定。'
        },
        {
          protocol: 'S7', profileId: null, section: 'connection', field: 'dataType', index: 0,
          message: '其他区段错误不应绑定。'
        }
      ]
    }));
    await wrapper.get('[data-plc-action="save-mappings"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('input[aria-label="PLC 地址"]').attributes('aria-invalid')).toBe('true');
    expect(wrapper.get('input[aria-label="变量名"]').attributes('aria-invalid')).toBeUndefined();
    expect(wrapper.get('select[name="plcMappingDataType-0"]').attributes('aria-invalid')).toBeUndefined();

    await wrapper.get('select[name="plcProtocol"]').setValue('MC');
    expect(wrapper.find('.validation-list').exists()).toBe(false);
    expect(wrapper.get('input[aria-label="PLC 地址"]').attributes('aria-invalid')).toBeUndefined();
    wrapper.unmount();
  });

  it('clears row-indexed validation issues when deleting a mapping shifts row identity', async () => {
    const fixture = makeOwner(settings({
      s7: profile(102, [
        mapping({ name: 'First', address: 'M0' }),
        mapping({ name: 'Second', address: 'M1' })
      ])
    }));
    const wrapper = mount(SettingsPlcPanel, { props: { owner: fixture.owner, canWrite: true } });
    await flushPromises();

    const mappingNames = wrapper.findAll('input[aria-label="变量名"]');
    await mappingNames[1]!.setValue('SecondDraft');
    fixture.savePlcMappings.mockResolvedValueOnce(completed({
      success: false,
      message: 'invalid mapping',
      mappings: [],
      errors: [{
        protocol: 'S7', profileId: null, section: 'mapping', field: 'name', index: 1,
        message: '第二行变量名无效。'
      }]
    }));
    await wrapper.get('[data-plc-action="save-mappings"]').trigger('click');
    await flushPromises();

    expect(wrapper.findAll('input[aria-label="变量名"]')[1]!.attributes('aria-invalid')).toBe('true');
    await wrapper.findAll('button[aria-label="删除映射"]')[0]!.trigger('click');

    const remainingName = wrapper.get('input[aria-label="变量名"]');
    expect(remainingName.attributes('aria-invalid')).toBeUndefined();
    expect(wrapper.find('.validation-list').exists()).toBe(false);
    wrapper.unmount();
  });
});
