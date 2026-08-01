import type { ApiBlobResponse } from '@/platform/api';

export const PLC_PROTOCOLS = Object.freeze(['S7', 'MC', 'FINS'] as const);
export type PlcProtocolV1 = typeof PLC_PROTOCOLS[number];

export interface PlcMappingV1 {
  readonly name: string;
  readonly address: string;
  readonly dataType: string;
  readonly description: string;
  readonly canWrite: boolean;
}

export interface PlcProfileV1 {
  readonly ipAddress: string;
  readonly port: number;
  readonly mappings: readonly PlcMappingV1[];
  readonly cpuType: string | null;
  readonly rack: number | null;
  readonly slot: number | null;
}

export interface PlcSettingsV1 {
  readonly activeProtocol: PlcProtocolV1;
  readonly heartbeatIntervalMs: number;
  readonly s7: PlcProfileV1;
  readonly mc: PlcProfileV1;
  readonly fins: PlcProfileV1;
}

export interface DeviceValidationIssueV1 {
  readonly protocol: string | null;
  readonly profileId: string | null;
  readonly section: string;
  readonly field: string;
  readonly index: number | null;
  readonly message: string;
}

export interface PlcSettingsResponseV1 {
  readonly success: boolean;
  readonly message: string;
  readonly settings: PlcSettingsV1 | null;
  readonly errors: readonly DeviceValidationIssueV1[];
}

export interface PlcMappingsResponseV1 {
  readonly success: boolean;
  readonly message: string;
  readonly mappings: readonly PlcMappingV1[];
  readonly errors: readonly DeviceValidationIssueV1[];
}

export interface PlcTestConnectionResponseV1 {
  readonly success: boolean;
  readonly message: string;
  readonly protocol: PlcProtocolV1;
}

export const TCP_MODES = Object.freeze(['Client', 'Server'] as const);
export type TcpModeV1 = typeof TCP_MODES[number];
export const TCP_ENCODINGS = Object.freeze(['UTF8', 'ASCII', 'GBK', 'HEX'] as const);
export type TcpEncodingV1 = typeof TCP_ENCODINGS[number];
export const TCP_FRAME_MODES = Object.freeze(['Raw', 'Line', 'FixedLength', 'Hex'] as const);
export type TcpFrameModeV1 = typeof TCP_FRAME_MODES[number];
export const TCP_LINE_ENDINGS = Object.freeze(['None', 'CR', 'LF', 'CRLF'] as const);
export type TcpLineEndingV1 = typeof TCP_LINE_ENDINGS[number];

export interface TcpProfileV1 {
  readonly id: string;
  readonly name: string;
  readonly enabled: boolean;
  readonly mode: TcpModeV1;
  readonly remoteHost: string;
  readonly remotePort: number;
  readonly localHost: string;
  readonly localPort: number;
  readonly encoding: TcpEncodingV1;
  readonly frameMode: TcpFrameModeV1;
  readonly fixedLength: number;
  readonly lineEnding: TcpLineEndingV1;
  readonly timeoutMs: number;
  readonly keepAlive: boolean;
  readonly reconnect: boolean;
  readonly connectOnStartup: boolean;
  readonly description: string;
}

export interface TcpProfileStatusV1 {
  readonly profileId: string;
  readonly mode: TcpModeV1;
  readonly isConnected: boolean;
  readonly isListening: boolean;
  readonly localEndpoint: string | null;
  readonly remoteEndpoint: string | null;
  readonly connectedClients: number;
  readonly lastError: string;
  readonly lastConnectedAtUtc: string | null;
  readonly lastReceivedAtUtc: string | null;
  readonly lastSentAtUtc: string | null;
}

export interface TcpFrameV1 {
  readonly id: string;
  readonly profileId: string;
  readonly direction: string;
  readonly timestampUtc: string;
  readonly byteCount: number;
  readonly text: string;
  readonly hex: string;
  readonly remoteEndpoint: string | null;
}

export interface TcpProfilesResponseV1 {
  readonly success: boolean;
  readonly message: string;
  readonly profiles: readonly TcpProfileV1[];
  readonly errors: readonly DeviceValidationIssueV1[];
}

export interface TcpRuntimeResponseV1 {
  readonly success: boolean;
  readonly message: string;
  readonly status: TcpProfileStatusV1 | null;
  readonly response: string;
  readonly errors: readonly DeviceValidationIssueV1[];
}

export interface TcpStatusResponseV1 {
  readonly success: boolean;
  readonly status: TcpProfileStatusV1 | null;
}

export interface TcpFramesResponseV1 {
  readonly success: boolean;
  readonly frames: readonly TcpFrameV1[];
}

export interface TcpClearFramesResponseV1 {
  readonly success: boolean;
  readonly message: string;
}

export interface CameraBindingV1 {
  readonly id: string;
  readonly displayName: string;
  readonly deviceId: string;
  readonly serialNumber: string;
  readonly ipAddress: string;
  readonly manufacturer: string;
  readonly modelName: string;
  readonly interfaceType: string;
  readonly isEnabled: boolean;
  readonly isActive: boolean;
  readonly exposureTimeUs: number;
  readonly gainDb: number;
  readonly pixelFormat: string;
  readonly triggerMode: 'Software' | 'External' | 'Continuous';
  readonly hardwareTriggerSource: string;
  readonly softwareTriggerSource: 'Manual' | 'EnterPhotoelectric' | 'SerialPhotoelectric';
  readonly enterPhotoelectricDebounceMs: number;
  readonly enterPhotoelectricTimeoutMs: number;
  readonly ignoreEnterTriggerWhileBusy: boolean;
  readonly enterPhotoelectricDeviceId: string;
  readonly serialPhotoelectricPortName: string;
  readonly serialPhotoelectricBaudRate: number;
  readonly serialPhotoelectricDebounceMs: number;
  readonly serialPhotoelectricTimeoutMs: number;
  readonly ignoreSerialPhotoelectricTriggerWhileBusy: boolean;
  readonly targetFrameRateFps: number;
  readonly connectionStatus: string;
}

export interface CameraDiscoveryDeviceV1 {
  readonly cameraId: string;
  readonly name: string;
  readonly serialNumber: string;
  readonly manufacturer: string;
  readonly model: string;
  readonly userDefinedName: string;
  readonly ipAddress: string | null;
  readonly connectionType: string;
  readonly interfaceType: string;
  readonly isConnected: boolean;
}

export interface CameraDiscoveryResponseV1 {
  readonly devices: readonly CameraDiscoveryDeviceV1[];
  readonly diagnostics: Readonly<Record<string, string | number | boolean | null>>;
}

export interface CameraBindingsResponseV1 {
  readonly bindings: readonly CameraBindingV1[];
  readonly activeCameraId: string;
}

export interface CameraBindingsWriteResponseV1 {
  readonly success: boolean;
  readonly message: string;
}

export interface TriggerDiagnosticsV1 {
  readonly isAvailable: boolean;
  readonly listenerType: string;
  readonly pendingWaiterCount: number;
  readonly attachedWindowHandle: string | null;
  readonly lastDeviceId: string | null;
  readonly lastSignalUtc: string | null;
  readonly lastError: string | null;
}

export interface SerialPhotoelectricPortV1 {
  readonly portName: string;
  readonly displayName: string;
  readonly isRecommended: boolean;
}

export interface SerialPhotoelectricTestResponseV1 {
  readonly message: string;
  readonly source: string;
  readonly portName: string;
  readonly timestampUtc: string;
}

export interface EnterDeviceLearnResponseV1 {
  readonly deviceId: string;
  readonly timestampUtc: string;
}

export interface ContinuousPreviewSessionV1 {
  readonly sessionId: string;
  readonly cameraBindingId: string;
  readonly triggerMode: string;
  readonly targetFrameRateFps: number;
}

export type CameraPreviewPhase = 'idle' | 'starting' | 'running' | 'capturing' | 'stopping' | 'captured' | 'error';

export interface CameraPreviewProjectionV1 {
  readonly phase: CameraPreviewPhase;
  readonly sessionId: string | null;
  readonly cameraBindingId: string | null;
  readonly imageUrl: string | null;
  readonly width: number | null;
  readonly height: number | null;
  readonly frameSequence: number | null;
  readonly triggerMode: string | null;
  readonly triggerSource: string | null;
  readonly contentType: string | null;
  readonly message: string;
  /** Runtime resource diagnostics are a projection, never a second preview owner. */
  readonly diagnostics?: CameraPreviewDiagnosticsV1;
}

export interface CameraPreviewDiagnosticsV1 {
  readonly controller: 'idle' | 'active';
  readonly session: 'none' | 'active';
  readonly frameLoop: 'idle' | 'active';
  readonly blobUrl: 'none' | 'active';
  readonly controllerCount: number;
  readonly sessionCount: number;
  readonly frameLoopCount: number;
  readonly blobUrlCount: number;
}

export interface CameraSettingsProjectionV1 {
  readonly bindings: readonly CameraBindingV1[];
  readonly activeCameraId: string;
  readonly discovery: CameraDiscoveryResponseV1 | null;
  readonly triggerDiagnostics: TriggerDiagnosticsV1 | null;
  readonly serialPorts: readonly SerialPhotoelectricPortV1[];
  readonly preview: CameraPreviewProjectionV1;
}

export interface CameraSoftCaptureResultV1 {
  readonly blob: ApiBlobResponse;
  readonly cameraBindingId: string;
  readonly triggerMode: string;
  readonly triggerSource: string;
  readonly width: number | null;
  readonly height: number | null;
}

export interface SettingsDeviceProjectionV1 {
  readonly plcSettings: PlcSettingsV1 | null;
  readonly plcMappings: readonly PlcMappingV1[];
  readonly tcpProfiles: readonly TcpProfileV1[];
  readonly tcpStatuses: Readonly<Record<string, TcpProfileStatusV1 | null>>;
  readonly tcpFrames: Readonly<Record<string, readonly TcpFrameV1[]>>;
  readonly cameraBindings: readonly CameraBindingV1[];
  readonly activeCameraId: string;
  readonly cameraDiscovery: CameraDiscoveryResponseV1 | null;
  readonly triggerDiagnostics: TriggerDiagnosticsV1 | null;
  readonly serialPorts: readonly SerialPhotoelectricPortV1[];
  readonly preview: CameraPreviewProjectionV1;
}
