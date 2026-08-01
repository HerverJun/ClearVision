import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import SettingsTcpPanel from '@/capabilities/settings/SettingsTcpPanel.vue';
import type {
  SettingsOwner,
  SettingsWriteResult,
  TcpFrameV1,
  TcpProfileV1,
  TcpProfileStatusV1,
  TcpProfilesResponseV1,
  TcpRuntimeResponseV1,
  TcpStatusResponseV1,
  TcpFramesResponseV1
} from '@/capabilities/settings';

function profile(overrides: Partial<TcpProfileV1> = {}): TcpProfileV1 {
  return {
    id: 'tcp-1', name: 'Fixture Client', enabled: true, mode: 'Client',
    remoteHost: '127.0.0.1', remotePort: 9000, localHost: '127.0.0.1', localPort: 9001,
    encoding: 'UTF8', frameMode: 'Raw', fixedLength: 0, lineEnding: 'None', timeoutMs: 5000,
    keepAlive: false, reconnect: true, connectOnStartup: false, description: 'fixture',
    ...overrides
  };
}

function status(id: string): TcpProfileStatusV1 {
  return {
    profileId: id, mode: 'Client', isConnected: false, isListening: false,
    localEndpoint: null, remoteEndpoint: null, connectedClients: 0, lastError: '',
    lastConnectedAtUtc: null, lastReceivedAtUtc: null, lastSentAtUtc: null
  };
}

function completed<T>(value: T): SettingsWriteResult<T> {
  return { status: 'completed', section: 'tcp', generation: 0, operationKind: 'write', value };
}

function tcpResponse(profiles: readonly TcpProfileV1[]): TcpProfilesResponseV1 {
  return { success: true, message: 'saved', profiles, errors: [] };
}

function runtimeResponse(id: string): TcpRuntimeResponseV1 {
  return { success: true, message: 'ok', status: status(id), response: '', errors: [] };
}

function makeOwner(savedProfiles: readonly TcpProfileV1[] = [profile()]) {
  const panelStates = new Map<string, () => { dirty: boolean; pending: boolean }>();
  const projection = reactive({
    phase: 'ready', role: 'Admin', settings: null, error: null, message: '', generation: 0,
    started: true, dirtySectionCount: 0, pendingSectionCount: 0,
    device: {
      plcSettings: null, plcMappings: [], tcpProfiles: [...savedProfiles], tcpStatuses: {}, tcpFrames: {},
      cameraBindings: [], activeCameraId: '', cameraDiscovery: null, triggerDiagnostics: null,
      serialPorts: [], preview: {
        phase: 'idle', sessionId: null, cameraBindingId: null, imageUrl: null, width: null, height: null,
        frameSequence: null, triggerMode: null, triggerSource: null, contentType: null, message: 'idle'
      }
    }
  });
  const readTcpProfiles = vi.fn(async () => completed<TcpProfilesResponseV1>(tcpResponse(savedProfiles)));
  const readTcpStatus = vi.fn(async (id: string) => completed<TcpStatusResponseV1>({ success: true, status: status(id) }));
  const readTcpFrames = vi.fn(async () => completed<TcpFramesResponseV1>({ success: true, frames: [] as TcpFrameV1[] }));
  const saveTcpProfiles = vi.fn(async (profiles: readonly TcpProfileV1[]) => completed(tcpResponse(profiles)));
  const owner = {
    projection,
    registerPanelState: vi.fn((section: string, readState: () => { dirty: boolean; pending: boolean }) => {
      panelStates.set(section, readState);
      return () => panelStates.delete(section);
    }),
    refreshPanelState: vi.fn(),
    readTcpProfiles,
    readTcpStatus,
    readTcpFrames,
    saveTcpProfiles,
    connectTcp: vi.fn(async (id: string) => completed(runtimeResponse(id))),
    disconnectTcp: vi.fn(async (id: string) => completed(runtimeResponse(id))),
    startTcpServer: vi.fn(async (id: string) => completed(runtimeResponse(id))),
    stopTcpServer: vi.fn(async (id: string) => completed(runtimeResponse(id))),
    sendTcp: vi.fn(async (id: string) => completed(runtimeResponse(id))),
    clearTcpFrames: vi.fn(async () => completed({ success: true, message: 'cleared' }))
  } as unknown as SettingsOwner;
  return { owner, projection, panelStates, readTcpProfiles, readTcpStatus, readTcpFrames, saveTcpProfiles };
}

describe('F07 G5 TCP Profile lifecycle', () => {
  it('loads runtime status and frames on first mount, accepts IPv6, and does not auto-run', async () => {
    const fixture = makeOwner();
    const wrapper = mount(SettingsTcpPanel, { props: { owner: fixture.owner, canWrite: true } });
    await flushPromises();

    expect(fixture.readTcpProfiles).toHaveBeenCalledTimes(1);
    expect(fixture.readTcpStatus).toHaveBeenCalledWith('tcp-1');
    expect(fixture.readTcpFrames).toHaveBeenCalledWith('tcp-1');

    await wrapper.get('input[name="tcpRemoteHost"]').setValue('::1');
    await wrapper.get('[data-tcp-action="save-profiles"]').trigger('click');
    await flushPromises();

    expect(fixture.saveTcpProfiles).toHaveBeenCalledWith([expect.objectContaining({ remoteHost: '::1' })]);
    expect((fixture.owner.connectTcp as ReturnType<typeof vi.fn>)).not.toHaveBeenCalled();
    expect((fixture.owner.startTcpServer as ReturnType<typeof vi.fn>)).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it('keeps IDs stable and blocks runtime mutations for dirty and new profiles', async () => {
    const fixture = makeOwner();
    const wrapper = mount(SettingsTcpPanel, { props: { owner: fixture.owner, canWrite: true } });
    await flushPromises();

    expect(wrapper.get('input[name="tcpProfileId"]').attributes('readonly')).toBeDefined();
    await wrapper.get('input[name="tcpProfileName"]').setValue('Dirty Client');
    await wrapper.get('textarea[name="tcpPayload"]').setValue('PING');
    expect(wrapper.get('[data-tcp-action="connect"]').attributes('disabled')).toBeDefined();
    expect(wrapper.get('[data-tcp-action="send"]').attributes('disabled')).toBeDefined();

    await wrapper.get('button[aria-label="添加 Client Profile"]').trigger('click');
    await flushPromises();
    expect(wrapper.get('input[name="tcpProfileId"]').attributes('readonly')).toBeDefined();
    expect(wrapper.get('[data-tcp-action="connect"]').attributes('disabled')).toBeDefined();
    expect(wrapper.get('[data-tcp-action="send"]').attributes('disabled')).toBeDefined();
    expect(fixture.panelStates.get('tcp')?.().dirty).toBe(true);
    expect((fixture.owner.connectTcp as ReturnType<typeof vi.fn>)).not.toHaveBeenCalled();
    wrapper.unmount();
  });
});
