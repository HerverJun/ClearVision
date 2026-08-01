import type { ApiBlobResponse, ApiTransport } from '@/platform/api';
import {
  decodeCameraBindingsResponse,
  decodeCameraBindingsWriteResponse,
  decodeCameraDiscoveryResponse,
  decodeContinuousPreviewSession,
  decodeDeviceNoContentResponse,
  decodeEnterDeviceLearnResponse,
  decodePlcMappingsResponse,
  decodePlcSettingsResponse,
  decodePlcTestConnectionResponse,
  decodeSerialPhotoelectricTestResponse,
  decodeSerialPorts,
  decodeTcpFramesResponse,
  decodeTcpProfilesResponse,
  decodeTcpRuntimeResponse,
  decodeTcpStatusResponse,
  decodeTriggerDiagnostics,
  type CameraBindingsResponseV1,
  type CameraBindingsWriteResponseV1,
  type CameraDiscoveryResponseV1,
  type ContinuousPreviewSessionV1,
  type EnterDeviceLearnResponseV1,
  type PlcSettingsResponseV1,
  type PlcSettingsV1,
  type PlcTestConnectionResponseV1,
  type SerialPhotoelectricPortV1,
  type SerialPhotoelectricTestResponseV1,
  type TcpClearFramesResponseV1,
  type TcpFramesResponseV1,
  type TcpProfileV1,
  type TcpProfilesResponseV1,
  type TcpRuntimeResponseV1,
  type TcpStatusResponseV1,
  type TriggerDiagnosticsV1
} from './deviceDecoder';
import type {
  CameraBindingV1,
  CameraSoftCaptureResultV1,
  PlcMappingsResponseV1
} from './deviceContracts';

export interface PlcTestConnectionRequestV1 {
  readonly protocol: string;
  readonly ipAddress: string;
  readonly port: number;
  readonly cpuType?: string;
  readonly rack?: number;
  readonly slot?: number;
}

export interface TcpSendRequestV1 {
  readonly payload: string;
  readonly isHex: boolean;
  readonly waitResponse: boolean;
  readonly responseTimeoutMs: number | null;
}

export type CameraDiscoveryProviderV1 = 'all' | 'huaray' | 'hikvision';

export interface SerialPhotoelectricTestRequestV1 {
  readonly portName: string;
  readonly baudRate: number;
  readonly debounceMs: number;
  readonly timeoutMs: number;
}

export interface SettingsDeviceApiAdapter {
  readPlcSettings(signal?: AbortSignal): Promise<PlcSettingsResponseV1>;
  readPlcMappings(signal?: AbortSignal): Promise<PlcMappingsResponseV1>;
  writePlcSettings(settings: PlcSettingsV1, signal?: AbortSignal): Promise<PlcSettingsResponseV1>;
  writePlcMappings(mappings: readonly unknown[], signal?: AbortSignal): Promise<PlcMappingsResponseV1>;
  testPlcConnection(request: PlcTestConnectionRequestV1, signal?: AbortSignal): Promise<PlcTestConnectionResponseV1>;
  readTcpProfiles(signal?: AbortSignal): Promise<TcpProfilesResponseV1>;
  writeTcpProfiles(profiles: readonly TcpProfileV1[], signal?: AbortSignal): Promise<TcpProfilesResponseV1>;
  connectTcp(profileId: string, signal?: AbortSignal): Promise<TcpRuntimeResponseV1>;
  disconnectTcp(profileId: string, signal?: AbortSignal): Promise<TcpRuntimeResponseV1>;
  startTcpServer(profileId: string, signal?: AbortSignal): Promise<TcpRuntimeResponseV1>;
  stopTcpServer(profileId: string, signal?: AbortSignal): Promise<TcpRuntimeResponseV1>;
  sendTcp(profileId: string, request: TcpSendRequestV1, signal?: AbortSignal): Promise<TcpRuntimeResponseV1>;
  readTcpStatus(profileId: string, signal?: AbortSignal): Promise<TcpStatusResponseV1>;
  readTcpFrames(profileId: string, signal?: AbortSignal): Promise<TcpFramesResponseV1>;
  clearTcpFrames(profileId: string, signal?: AbortSignal): Promise<TcpClearFramesResponseV1>;
  discoverCameras(provider: CameraDiscoveryProviderV1, signal?: AbortSignal): Promise<CameraDiscoveryResponseV1>;
  readCameraBindings(signal?: AbortSignal): Promise<CameraBindingsResponseV1>;
  writeCameraBindings(
    bindings: readonly CameraBindingV1[],
    activeCameraId: string,
    signal?: AbortSignal
  ): Promise<CameraBindingsWriteResponseV1>;
  readTriggerDiagnostics(signal?: AbortSignal): Promise<TriggerDiagnosticsV1>;
  readSerialPhotoelectricPorts(signal?: AbortSignal): Promise<readonly SerialPhotoelectricPortV1[]>;
  testSerialPhotoelectric(
    request: SerialPhotoelectricTestRequestV1,
    signal?: AbortSignal
  ): Promise<SerialPhotoelectricTestResponseV1>;
  learnEnterPhotoelectricDevice(timeoutMs: number, signal?: AbortSignal): Promise<EnterDeviceLearnResponseV1>;
  softTriggerCapture(cameraBindingId: string, signal?: AbortSignal): Promise<CameraSoftCaptureResultV1>;
  startContinuousPreview(cameraBindingId: string, signal?: AbortSignal): Promise<ContinuousPreviewSessionV1>;
  getContinuousPreviewFrame(sessionId: string, signal?: AbortSignal): Promise<ApiBlobResponse>;
  stopContinuousPreview(sessionId: string, signal?: AbortSignal): Promise<void>;
}

function signalOptions(signal?: AbortSignal): Readonly<{ readonly signal?: AbortSignal }> {
  return signal ? { signal } : {};
}

function requiredMethod<T>(method: T | undefined, name: string): T {
  if (method === undefined) throw new TypeError(`Settings device API transport does not provide ${name}.`);
  return method;
}

function profilePath(profileId: string, suffix: string): string {
  const normalized = profileId.trim();
  if (!normalized || normalized.includes('/')) throw new TypeError('TCP Profile id must be a path-safe value.');
  return `tcp/profiles/${encodeURIComponent(normalized)}${suffix}`;
}

function plcProfilePayload(profile: PlcSettingsV1['s7'] | PlcSettingsV1['mc'] | PlcSettingsV1['fins'], protocol: string) {
  const base = {
    ipAddress: profile.ipAddress,
    port: profile.port,
    mappings: profile.mappings
  };
  return protocol === 'S7'
    ? { ...base, cpuType: profile.cpuType ?? 'S7-1200', rack: profile.rack ?? 0, slot: profile.slot ?? 1 }
    : base;
}

function plcPayload(settings: PlcSettingsV1): Readonly<Record<string, unknown>> {
  return Object.freeze({
    activeProtocol: settings.activeProtocol,
    heartbeatIntervalMs: settings.heartbeatIntervalMs,
    s7: plcProfilePayload(settings.s7, 'S7'),
    mc: plcProfilePayload(settings.mc, 'MC'),
    fins: plcProfilePayload(settings.fins, 'FINS')
  });
}

function cameraBindingPayload(binding: CameraBindingV1): Readonly<Record<string, unknown>> {
  return Object.freeze({
    id: binding.id,
    displayName: binding.displayName,
    serialNumber: binding.serialNumber,
    ipAddress: binding.ipAddress,
    manufacturer: binding.manufacturer,
    modelName: binding.modelName,
    interfaceType: binding.interfaceType,
    isEnabled: binding.isEnabled,
    exposureTimeUs: binding.exposureTimeUs,
    gainDb: binding.gainDb,
    pixelFormat: binding.pixelFormat,
    triggerMode: binding.triggerMode,
    hardwareTriggerSource: binding.hardwareTriggerSource,
    softwareTriggerSource: binding.softwareTriggerSource,
    enterPhotoelectricDebounceMs: binding.enterPhotoelectricDebounceMs,
    enterPhotoelectricTimeoutMs: binding.enterPhotoelectricTimeoutMs,
    ignoreEnterTriggerWhileBusy: binding.ignoreEnterTriggerWhileBusy,
    enterPhotoelectricDeviceId: binding.enterPhotoelectricDeviceId,
    serialPhotoelectricPortName: binding.serialPhotoelectricPortName,
    serialPhotoelectricBaudRate: binding.serialPhotoelectricBaudRate,
    serialPhotoelectricDebounceMs: binding.serialPhotoelectricDebounceMs,
    serialPhotoelectricTimeoutMs: binding.serialPhotoelectricTimeoutMs,
    ignoreSerialPhotoelectricTriggerWhileBusy: binding.ignoreSerialPhotoelectricTriggerWhileBusy,
    targetFrameRateFps: binding.targetFrameRateFps
  });
}

export function createSettingsDeviceApiAdapter(api: ApiTransport): SettingsDeviceApiAdapter {
  const get = api.get.bind(api);
  const post: NonNullable<ApiTransport['post']> = (...args) =>
    requiredMethod(api.post, 'POST')(...args);
  const put: NonNullable<ApiTransport['put']> = (...args) =>
    requiredMethod(api.put, 'PUT')(...args);
  const getBlob: NonNullable<ApiTransport['getBlob']> = (...args) =>
    requiredMethod(api.getBlob, 'GET blob')(...args);
  const postBlob: NonNullable<ApiTransport['postBlob']> = (...args) =>
    requiredMethod(api.postBlob, 'POST blob')(...args);

  return Object.freeze({
    async readPlcSettings(signal?: AbortSignal): Promise<PlcSettingsResponseV1> {
      return decodePlcSettingsResponse(await get('plc/settings', signalOptions(signal)));
    },
    async readPlcMappings(signal?: AbortSignal): Promise<PlcMappingsResponseV1> {
      return decodePlcMappingsResponse(await get('plc/mappings', signalOptions(signal)));
    },
    async writePlcSettings(settings: PlcSettingsV1, signal?: AbortSignal): Promise<PlcSettingsResponseV1> {
      return decodePlcSettingsResponse(await put('plc/settings', plcPayload(settings), signalOptions(signal)));
    },
    async writePlcMappings(mappings: readonly unknown[], signal?: AbortSignal): Promise<PlcMappingsResponseV1> {
      return decodePlcMappingsResponse(await put('plc/mappings', [...mappings], signalOptions(signal)));
    },
    async testPlcConnection(request: PlcTestConnectionRequestV1, signal?: AbortSignal): Promise<PlcTestConnectionResponseV1> {
      return decodePlcTestConnectionResponse(await post('plc/test-connection', request, signalOptions(signal)));
    },
    async readTcpProfiles(signal?: AbortSignal): Promise<TcpProfilesResponseV1> {
      return decodeTcpProfilesResponse(await get('tcp/profiles', signalOptions(signal)));
    },
    async writeTcpProfiles(profiles: readonly TcpProfileV1[], signal?: AbortSignal): Promise<TcpProfilesResponseV1> {
      return decodeTcpProfilesResponse(await put('tcp/profiles', [...profiles], signalOptions(signal)));
    },
    async connectTcp(profileId: string, signal?: AbortSignal): Promise<TcpRuntimeResponseV1> {
      return decodeTcpRuntimeResponse(await post(`${profilePath(profileId, '/connect')}`, {}, signalOptions(signal)));
    },
    async disconnectTcp(profileId: string, signal?: AbortSignal): Promise<TcpRuntimeResponseV1> {
      return decodeTcpRuntimeResponse(await post(`${profilePath(profileId, '/disconnect')}`, {}, signalOptions(signal)));
    },
    async startTcpServer(profileId: string, signal?: AbortSignal): Promise<TcpRuntimeResponseV1> {
      return decodeTcpRuntimeResponse(await post(`${profilePath(profileId, '/server/start')}`, {}, signalOptions(signal)));
    },
    async stopTcpServer(profileId: string, signal?: AbortSignal): Promise<TcpRuntimeResponseV1> {
      return decodeTcpRuntimeResponse(await post(`${profilePath(profileId, '/server/stop')}`, {}, signalOptions(signal)));
    },
    async sendTcp(profileId: string, request: TcpSendRequestV1, signal?: AbortSignal): Promise<TcpRuntimeResponseV1> {
      return decodeTcpRuntimeResponse(await post(`${profilePath(profileId, '/send')}`, {
        payload: request.payload,
        isHex: request.isHex,
        waitResponse: request.waitResponse,
        responseTimeoutMs: request.responseTimeoutMs
      }, signalOptions(signal)));
    },
    async readTcpStatus(profileId: string, signal?: AbortSignal): Promise<TcpStatusResponseV1> {
      return decodeTcpStatusResponse(await get(`${profilePath(profileId, '/status')}`, signalOptions(signal)));
    },
    async readTcpFrames(profileId: string, signal?: AbortSignal): Promise<TcpFramesResponseV1> {
      return decodeTcpFramesResponse(await get(`${profilePath(profileId, '/frames')}`, signalOptions(signal)));
    },
    async clearTcpFrames(profileId: string, signal?: AbortSignal): Promise<TcpClearFramesResponseV1> {
      return decodeDeviceNoContentResponse(await post(`${profilePath(profileId, '/frames/clear')}`, {}, signalOptions(signal)));
    },
    async discoverCameras(provider: CameraDiscoveryProviderV1, signal?: AbortSignal): Promise<CameraDiscoveryResponseV1> {
      const path = provider === 'all' ? 'cameras/discover' : `cameras/discover/${provider}`;
      return decodeCameraDiscoveryResponse(await get(path, signalOptions(signal)));
    },
    async readCameraBindings(signal?: AbortSignal): Promise<CameraBindingsResponseV1> {
      return decodeCameraBindingsResponse(await get('cameras/bindings', signalOptions(signal)));
    },
    async writeCameraBindings(
      bindings: readonly CameraBindingV1[],
      activeCameraId: string,
      signal?: AbortSignal
    ): Promise<CameraBindingsWriteResponseV1> {
      return decodeCameraBindingsWriteResponse(await put('cameras/bindings', {
        bindings: bindings.map(cameraBindingPayload),
        activeCameraId
      }, signalOptions(signal)));
    },
    async readTriggerDiagnostics(signal?: AbortSignal): Promise<TriggerDiagnosticsV1> {
      return decodeTriggerDiagnostics(await get('trigger-input/diagnostics', signalOptions(signal)));
    },
    async readSerialPhotoelectricPorts(signal?: AbortSignal): Promise<readonly SerialPhotoelectricPortV1[]> {
      return decodeSerialPorts(await get('trigger-input/serial-photoelectric-ports', signalOptions(signal)));
    },
    async testSerialPhotoelectric(
      request: SerialPhotoelectricTestRequestV1,
      signal?: AbortSignal
    ): Promise<SerialPhotoelectricTestResponseV1> {
      return decodeSerialPhotoelectricTestResponse(await post('trigger-input/test-serial-photoelectric', request, signalOptions(signal)));
    },
    async learnEnterPhotoelectricDevice(timeoutMs: number, signal?: AbortSignal): Promise<EnterDeviceLearnResponseV1> {
      return decodeEnterDeviceLearnResponse(await post('trigger-input/learn-enter-device', { timeoutMs }, signalOptions(signal)));
    },
    async softTriggerCapture(cameraBindingId: string, signal?: AbortSignal): Promise<CameraSoftCaptureResultV1> {
      const response = await postBlob('cameras/soft-trigger-capture', { cameraBindingId }, signalOptions(signal));
      return Object.freeze({
        blob: response,
        cameraBindingId: response.headers.get('X-Camera-Id') || cameraBindingId,
        triggerMode: response.headers.get('X-Trigger-Mode') || 'Software',
        triggerSource: response.headers.get('X-Trigger-Source') || 'Manual',
        width: positiveHeader(response.headers, 'X-Image-Width'),
        height: positiveHeader(response.headers, 'X-Image-Height')
      });
    },
    async startContinuousPreview(cameraBindingId: string, signal?: AbortSignal): Promise<ContinuousPreviewSessionV1> {
      return decodeContinuousPreviewSession(await post('cameras/continuous-preview/start', { cameraBindingId }, signalOptions(signal)));
    },
    async getContinuousPreviewFrame(sessionId: string, signal?: AbortSignal): Promise<ApiBlobResponse> {
      return getBlob(`cameras/continuous-preview/frame/${encodeURIComponent(sessionId)}?_=${Date.now()}`, signalOptions(signal));
    },
    async stopContinuousPreview(sessionId: string, signal?: AbortSignal): Promise<void> {
      await post('cameras/continuous-preview/stop', { sessionId }, signalOptions(signal));
    }
  });
}

function positiveHeader(headers: Headers, name: string): number | null {
  const value = Number(headers.get(name));
  return Number.isFinite(value) && value > 0 ? value : null;
}
