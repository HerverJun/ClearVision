import { flushPromises, mount } from '@vue/test-utils';
import { nextTick, reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import SettingsCameraPanel from '@/capabilities/settings/SettingsCameraPanel.vue';
import type {
  CameraBindingV1,
  SettingsOwner,
  SettingsWriteResult
} from '@/capabilities/settings';

function binding(connectionStatus = 'Connected'): CameraBindingV1 {
  return {
    id: 'cam-1', displayName: 'Fixture Camera', deviceId: 'fixture-serial', serialNumber: 'fixture-serial',
    ipAddress: '192.168.0.10', manufacturer: 'Huaray', modelName: 'Fixture-1', interfaceType: 'GigE',
    isEnabled: true, isActive: true, exposureTimeUs: 5000, gainDb: 1, pixelFormat: 'Mono8',
    triggerMode: 'Software', hardwareTriggerSource: 'Line0', softwareTriggerSource: 'Manual',
    enterPhotoelectricDebounceMs: 200, enterPhotoelectricTimeoutMs: 30000, ignoreEnterTriggerWhileBusy: true,
    enterPhotoelectricDeviceId: '', serialPhotoelectricPortName: 'COM3', serialPhotoelectricBaudRate: 9600,
    serialPhotoelectricDebounceMs: 200, serialPhotoelectricTimeoutMs: 30000,
    ignoreSerialPhotoelectricTriggerWhileBusy: true, targetFrameRateFps: 30, connectionStatus
  };
}

function completed<T>(value: T): SettingsWriteResult<T> {
  return { status: 'completed', section: 'camera', generation: 0, value };
}

function cameraOwner(
  role: string,
  previewPhase: 'idle' | 'running' = 'idle',
  connectionStatus = 'Connected'
) {
  const current = binding(connectionStatus);
  const projection = reactive({
    phase: 'ready', role, settings: null, error: null, message: '', generation: 0, started: true,
    device: {
      plcSettings: null, plcMappings: [], tcpProfiles: [], tcpStatuses: {}, tcpFrames: {},
      cameraBindings: [current], activeCameraId: current.id, cameraDiscovery: null,
      triggerDiagnostics: {
        isAvailable: true, listenerType: 'Fixture', pendingWaiterCount: 0,
        attachedWindowHandle: null, lastDeviceId: null, lastSignalUtc: null, lastError: null
      },
      serialPorts: [{ portName: 'COM3', displayName: 'Fixture Serial', isRecommended: true }],
      preview: {
        phase: previewPhase, sessionId: previewPhase === 'running' ? 'session-1' : null,
        cameraBindingId: previewPhase === 'running' ? current.id : null, imageUrl: null,
        width: null, height: null, frameSequence: null, triggerMode: null, triggerSource: null,
        contentType: null, message: previewPhase === 'running' ? 'running' : 'idle'
      }
    }
  });
  const saveCameraBindings = vi.fn(async () => completed({ success: true, message: 'saved' }));
  const stopCameraPreview = vi.fn(async () => completed(undefined));
  let panelStateReader: (() => { dirty: boolean; pending: boolean }) | undefined;
  const owner = {
    projection,
    registerPanelState: vi.fn((_section: string, readState: () => { dirty: boolean; pending: boolean }) => {
      panelStateReader = readState;
      return () => {
        if (panelStateReader === readState) panelStateReader = undefined;
      };
    }),
    refreshPanelState: vi.fn(),
    readCameraBindings: vi.fn(async () => completed({ bindings: [current], activeCameraId: current.id })),
    readTriggerDiagnostics: vi.fn(async () => completed(projection.device.triggerDiagnostics)),
    readSerialPhotoelectricPorts: vi.fn(async () => completed(projection.device.serialPorts)),
    discoverCameras: vi.fn(),
    saveCameraBindings,
    stopCameraPreview,
    testSerialPhotoelectric: vi.fn(),
    learnEnterPhotoelectricDevice: vi.fn(),
    captureSoftTrigger: vi.fn(async () => completed({ success: true, message: 'captured' })),
    startCameraPreview: vi.fn(async () => completed({ success: true, message: 'started' }))
  } as unknown as SettingsOwner;
  return { owner, projection, saveCameraBindings, stopCameraPreview, panelStateReader: () => panelStateReader?.() };
}

describe('F07 G6 Camera/Trigger/Preview panel', () => {
  it('separates binding save from debug capture and renders provider/system controls', async () => {
    const fixture = cameraOwner('Admin');
    const wrapper = mount(SettingsCameraPanel, { props: { owner: fixture.owner } });
    await flushPromises();

    expect(wrapper.find('[data-camera-section="discovery"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('华睿');
    expect(wrapper.text()).toContain('海康威视');
    expect(wrapper.text()).toContain('曝光');
    expect(wrapper.text()).toContain('触发输入与诊断');
    expect(wrapper.text()).toContain('采集预览');
    expect(wrapper.text()).toContain('N 点标定');
    expect(wrapper.get('.camera-binding-row').attributes('aria-pressed')).toBe('true');
    expect(wrapper.get('input[name="cameraEnabled-cam-1"]').attributes('type')).toBe('checkbox');

    await wrapper.get('input[name="cameraExposure"]').setValue('7000');
    await wrapper.findAll('button').find(button => button.text().includes('保存绑定'))?.trigger('click');
    expect(fixture.saveCameraBindings).toHaveBeenCalledWith(
      [expect.objectContaining({ id: 'cam-1', exposureTimeUs: 7000 })],
      'cam-1'
    );

    await wrapper.findAll('button').find(button => button.text().includes('采集单帧'))?.trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('captured');
    expect(fixture.owner.captureSoftTrigger).toHaveBeenCalledWith('cam-1');
    wrapper.unmount();
  });

  it('disables engineer/admin operations for Operator and stops preview on panel leave', async () => {
    const fixture = cameraOwner('Operator', 'running');
    const wrapper = mount(SettingsCameraPanel, { props: { owner: fixture.owner } });
    await flushPromises();

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存绑定'));
    const captureButton = wrapper.findAll('button').find(button => button.text().includes('采集单帧'));
    const previewButton = wrapper.findAll('button').find(button => button.text().includes('停止连续预览'));
    expect(saveButton).toBeUndefined();
    expect(captureButton?.attributes('disabled')).toBeDefined();
    expect(previewButton?.attributes('disabled')).toBeDefined();
    wrapper.unmount();
    expect(fixture.stopCameraPreview).toHaveBeenCalledWith('离开相机设置面板。');
    expect(fixture.stopCameraPreview).toHaveBeenCalledTimes(1);
  });

  it('allows Engineer to save camera bindings while keeping Operator read-only', async () => {
    const fixture = cameraOwner('Engineer');
    const wrapper = mount(SettingsCameraPanel, { props: { owner: fixture.owner } });
    await flushPromises();

    await wrapper.get('input[name="cameraExposure"]').setValue('7000');
    await wrapper.findAll('button').find(button => button.text().includes('保存绑定'))?.trigger('click');

    expect(fixture.saveCameraBindings).toHaveBeenCalledWith(
      [expect.objectContaining({ id: 'cam-1', exposureTimeUs: 7000 })],
      'cam-1'
    );
    wrapper.unmount();
  });

  it('rebuilds the binding draft from the server-normalized authority projection after save', async () => {
    const fixture = cameraOwner('Admin');
    const normalized = {
      ...binding(),
      displayName: 'Server normalized camera',
      exposureTimeUs: 1250
    };
    fixture.saveCameraBindings.mockImplementation(async () => {
      fixture.projection.device.cameraBindings = [normalized];
      fixture.projection.device.activeCameraId = normalized.id;
      return completed({ success: true, message: 'saved and normalized' });
    });
    const wrapper = mount(SettingsCameraPanel, { props: { owner: fixture.owner } });
    await flushPromises();

    await wrapper.get('input[name="cameraExposure"]').setValue('7000');
    await wrapper.find('[data-camera-action="save-bindings"]').trigger('click');
    await flushPromises();
    await nextTick();

    expect((wrapper.get('input[name="cameraExposure"]').element as HTMLInputElement).value).toBe('1250');
    expect((wrapper.get('input[name="cameraDisplayName"]').element as HTMLInputElement).value)
      .toBe('Server normalized camera');
    wrapper.unmount();
  });

  it('does not classify Disconnected as a connected camera', async () => {
    const fixture = cameraOwner('Engineer', 'idle', 'Disconnected');
    const wrapper = mount(SettingsCameraPanel, { props: { owner: fixture.owner } });
    await flushPromises();

    const status = wrapper.find('[data-camera-section="bindings"] [data-status-tone="warning"]');
    expect(status.exists()).toBe(true);
    expect(status.text()).toContain('未连接');
    wrapper.unmount();
  });
});
