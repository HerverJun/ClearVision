import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import SettingsPlcPanel from '@/capabilities/settings/SettingsPlcPanel.vue';
import type {
  PlcMappingV1,
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
  const savePlcMappings = vi.fn(async (value: readonly PlcMappingV1[]) => completed({
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
});
