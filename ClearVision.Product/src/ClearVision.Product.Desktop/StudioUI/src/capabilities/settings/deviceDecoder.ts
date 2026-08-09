import { SettingsContractDecodeError } from './contracts';
import {
  PLC_PROTOCOLS,
  TCP_ENCODINGS,
  TCP_FRAME_MODES,
  TCP_LINE_ENDINGS,
  TCP_MODES,
  type CameraBindingV1,
  type CameraBindingsResponseV1,
  type CameraBindingsWriteResponseV1,
  type CameraDiscoveryDeviceV1,
  type CameraDiscoveryResponseV1,
  type ContinuousPreviewSessionV1,
  type DeviceValidationIssueV1,
  type EnterDeviceLearnResponseV1,
  type PlcMappingV1,
  type PlcMappingsResponseV1,
  type PlcProfileV1,
  type PlcProtocolV1,
  type PlcSettingsResponseV1,
  type PlcSettingsV1,
  type PlcTestConnectionResponseV1,
  type SerialPhotoelectricPortV1,
  type SerialPhotoelectricTestResponseV1,
  type TcpClearFramesResponseV1,
  type TcpFrameV1,
  type TcpFramesResponseV1,
  type TcpModeV1,
  type TcpProfileStatusV1,
  type TcpProfileV1,
  type TcpProfilesResponseV1,
  type TcpRuntimeResponseV1,
  type TcpStatusResponseV1,
  type TriggerDiagnosticsV1
} from './deviceContracts';

type JsonRecord = Readonly<Record<string, unknown>>;

function record(value: unknown, path: string): JsonRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new SettingsContractDecodeError(path, 'an object');
  }
  return value as JsonRecord;
}

function field(source: JsonRecord, canonical: string, aliases: readonly string[] = []): unknown {
  const names = [canonical, `${canonical.slice(0, 1).toUpperCase()}${canonical.slice(1)}`, ...aliases];
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(source, name)) return source[name];
  }
  return undefined;
}

function text(value: unknown, path: string, fallback = ''): string {
  if (value === undefined || value === null) return fallback;
  if (typeof value !== 'string') throw new SettingsContractDecodeError(path, 'a string');
  return value.trim();
}

function nullableText(value: unknown, path: string): string | null {
  if (value === undefined || value === null || value === '') return null;
  return text(value, path);
}

function booleanValue(value: unknown, path: string, fallback = false): boolean {
  if (value === undefined || value === null) return fallback;
  if (typeof value !== 'boolean') throw new SettingsContractDecodeError(path, 'a boolean');
  return value;
}

function numberValue(value: unknown, path: string, fallback = 0): number {
  if (value === undefined || value === null || value === '') return fallback;
  const result = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : NaN;
  if (!Number.isFinite(result)) throw new SettingsContractDecodeError(path, 'a finite number');
  return result;
}

function integerValue(value: unknown, path: string, fallback = 0): number {
  const result = numberValue(value, path, fallback);
  if (!Number.isInteger(result)) throw new SettingsContractDecodeError(path, 'an integer');
  return result;
}

function enumValue<T extends string>(value: unknown, path: string, allowed: readonly T[], fallback: T): T {
  const candidate = text(value, path, fallback);
  const match = allowed.find(item => item.toLowerCase() === candidate.toLowerCase());
  if (!match) throw new SettingsContractDecodeError(path, `one of ${allowed.join(', ')}`);
  return match;
}

function protocolValue(value: unknown, path: string): PlcProtocolV1 {
  const candidate = text(value, path, 'S7').toUpperCase();
  const aliases: Readonly<Record<string, PlcProtocolV1>> = {
    SIEMENSS7: 'S7',
    MITSUBISHIMC: 'MC',
    OMRONFINS: 'FINS'
  };
  const normalized = aliases[candidate] ?? candidate;
  if (!PLC_PROTOCOLS.includes(normalized as PlcProtocolV1)) {
    throw new SettingsContractDecodeError(path, `one of ${PLC_PROTOCOLS.join(', ')}`);
  }
  return normalized as PlcProtocolV1;
}

function freezeArray<T>(value: readonly T[]): readonly T[] {
  return Object.freeze([...value]);
}

function decodeIssues(value: unknown, path: string): readonly DeviceValidationIssueV1[] {
  if (value === undefined || value === null) return Object.freeze([]);
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array');
  return freezeArray(value.map((item, index) => {
    const source = record(item, `${path}[${index}]`);
    return Object.freeze({
      protocol: nullableText(field(source, 'protocol'), `${path}[${index}].protocol`),
      profileId: nullableText(field(source, 'profileId'), `${path}[${index}].profileId`),
      section: text(field(source, 'section'), `${path}[${index}].section`),
      field: text(field(source, 'field'), `${path}[${index}].field`),
      index: field(source, 'index') === null || field(source, 'index') === undefined
        ? null
        : integerValue(field(source, 'index'), `${path}[${index}].index`),
      message: text(field(source, 'message'), `${path}[${index}].message`)
    });
  }));
}

function decodePlcMapping(value: unknown, path: string): PlcMappingV1 {
  const source = record(value, path);
  return Object.freeze({
    name: text(field(source, 'name'), `${path}.name`),
    address: text(field(source, 'address'), `${path}.address`),
    dataType: text(field(source, 'dataType'), `${path}.dataType`, 'Bool'),
    description: text(field(source, 'description'), `${path}.description`),
    canWrite: booleanValue(field(source, 'canWrite'), `${path}.canWrite`)
  });
}

function decodeMappings(value: unknown, path: string): readonly PlcMappingV1[] {
  if (value === undefined || value === null) return Object.freeze([]);
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array');
  return freezeArray(value.map((item, index) => decodePlcMapping(item, `${path}[${index}]`)));
}

function decodePlcProfile(value: unknown, path: string, protocol: PlcProtocolV1): PlcProfileV1 {
  const source = record(value, path);
  return Object.freeze({
    ipAddress: text(field(source, 'ipAddress'), `${path}.ipAddress`),
    port: integerValue(field(source, 'port'), `${path}.port`),
    mappings: decodeMappings(field(source, 'mappings'), `${path}.mappings`),
    cpuType: protocol === 'S7' ? text(field(source, 'cpuType'), `${path}.cpuType`, 'S7-1200') : null,
    rack: protocol === 'S7' ? integerValue(field(source, 'rack'), `${path}.rack`) : null,
    slot: protocol === 'S7' ? integerValue(field(source, 'slot'), `${path}.slot`, 1) : null
  });
}

export function decodePlcSettings(value: unknown, path = '$'): PlcSettingsV1 {
  const source = record(value, path);
  const activeProtocol = protocolValue(field(source, 'activeProtocol', ['protocol']), `${path}.activeProtocol`);
  return Object.freeze({
    activeProtocol,
    heartbeatIntervalMs: integerValue(field(source, 'heartbeatIntervalMs'), `${path}.heartbeatIntervalMs`, 1000),
    s7: decodePlcProfile(field(source, 's7'), `${path}.s7`, 'S7'),
    mc: decodePlcProfile(field(source, 'mc'), `${path}.mc`, 'MC'),
    fins: decodePlcProfile(field(source, 'fins'), `${path}.fins`, 'FINS')
  });
}

function responseSource(value: unknown, path: string): JsonRecord {
  return record(value, path);
}

export function decodePlcSettingsResponse(value: unknown, path = '$'): PlcSettingsResponseV1 {
  const source = responseSource(value, path);
  const settingsValue = field(source, 'settings');
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`, true),
    message: text(field(source, 'message'), `${path}.message`),
    settings: settingsValue === undefined || settingsValue === null
      ? null
      : decodePlcSettings(settingsValue, `${path}.settings`),
    errors: decodeIssues(field(source, 'errors'), `${path}.errors`)
  });
}

export function decodePlcMappingsResponse(value: unknown, path = '$'): PlcMappingsResponseV1 {
  if (Array.isArray(value)) {
    return Object.freeze({ success: true, message: '', mappings: decodeMappings(value, path), errors: Object.freeze([]) });
  }
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`, true),
    message: text(field(source, 'message'), `${path}.message`),
    mappings: decodeMappings(field(source, 'mappings'), `${path}.mappings`),
    errors: decodeIssues(field(source, 'errors'), `${path}.errors`)
  });
}

export function decodePlcTestConnectionResponse(value: unknown, path = '$'): PlcTestConnectionResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`),
    message: text(field(source, 'message'), `${path}.message`),
    protocol: protocolValue(field(source, 'protocol'), `${path}.protocol`)
  });
}

function decodeTcpProfile(value: unknown, path: string): TcpProfileV1 {
  const source = record(value, path);
  return Object.freeze({
    id: text(field(source, 'id'), `${path}.id`),
    name: text(field(source, 'name'), `${path}.name`, 'TCP 连接配置'),
    enabled: booleanValue(field(source, 'enabled'), `${path}.enabled`),
    mode: enumValue(field(source, 'mode'), `${path}.mode`, TCP_MODES, 'Client'),
    remoteHost: text(field(source, 'remoteHost'), `${path}.remoteHost`, '127.0.0.1'),
    remotePort: integerValue(field(source, 'remotePort'), `${path}.remotePort`),
    localHost: text(field(source, 'localHost'), `${path}.localHost`, '127.0.0.1'),
    localPort: integerValue(field(source, 'localPort'), `${path}.localPort`),
    encoding: enumValue(field(source, 'encoding'), `${path}.encoding`, TCP_ENCODINGS, 'UTF8'),
    frameMode: enumValue(field(source, 'frameMode'), `${path}.frameMode`, TCP_FRAME_MODES, 'Raw'),
    fixedLength: integerValue(field(source, 'fixedLength'), `${path}.fixedLength`),
    lineEnding: enumValue(field(source, 'lineEnding'), `${path}.lineEnding`, TCP_LINE_ENDINGS, 'None'),
    timeoutMs: integerValue(field(source, 'timeoutMs'), `${path}.timeoutMs`, 5000),
    keepAlive: booleanValue(field(source, 'keepAlive'), `${path}.keepAlive`),
    reconnect: booleanValue(field(source, 'reconnect'), `${path}.reconnect`, true),
    connectOnStartup: booleanValue(field(source, 'connectOnStartup'), `${path}.connectOnStartup`),
    description: text(field(source, 'description'), `${path}.description`)
  });
}

function decodeTcpProfiles(value: unknown, path: string): readonly TcpProfileV1[] {
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array');
  return freezeArray(value.map((item, index) => decodeTcpProfile(item, `${path}[${index}]`)));
}

function decodeTcpStatus(value: unknown, path: string): TcpProfileStatusV1 | null {
  if (value === undefined || value === null) return null;
  const source = record(value, path);
  return Object.freeze({
    profileId: text(field(source, 'profileId'), `${path}.profileId`),
    mode: enumValue(field(source, 'mode'), `${path}.mode`, TCP_MODES, 'Client'),
    isConnected: booleanValue(field(source, 'isConnected'), `${path}.isConnected`),
    isListening: booleanValue(field(source, 'isListening'), `${path}.isListening`),
    localEndpoint: nullableText(field(source, 'localEndpoint'), `${path}.localEndpoint`),
    remoteEndpoint: nullableText(field(source, 'remoteEndpoint'), `${path}.remoteEndpoint`),
    connectedClients: integerValue(field(source, 'connectedClients'), `${path}.connectedClients`),
    lastError: text(field(source, 'lastError'), `${path}.lastError`),
    lastConnectedAtUtc: nullableText(field(source, 'lastConnectedAtUtc'), `${path}.lastConnectedAtUtc`),
    lastReceivedAtUtc: nullableText(field(source, 'lastReceivedAtUtc'), `${path}.lastReceivedAtUtc`),
    lastSentAtUtc: nullableText(field(source, 'lastSentAtUtc'), `${path}.lastSentAtUtc`)
  });
}

function decodeTcpFrame(value: unknown, path: string): TcpFrameV1 {
  const source = record(value, path);
  return Object.freeze({
    id: text(field(source, 'id'), `${path}.id`),
    profileId: text(field(source, 'profileId'), `${path}.profileId`),
    direction: text(field(source, 'direction'), `${path}.direction`),
    timestampUtc: text(field(source, 'timestampUtc'), `${path}.timestampUtc`),
    byteCount: integerValue(field(source, 'byteCount'), `${path}.byteCount`),
    text: text(field(source, 'text'), `${path}.text`),
    hex: text(field(source, 'hex'), `${path}.hex`),
    remoteEndpoint: nullableText(field(source, 'remoteEndpoint'), `${path}.remoteEndpoint`)
  });
}

function decodeTcpFrames(value: unknown, path: string): readonly TcpFrameV1[] {
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array');
  const frames = value.map((item, index) => decodeTcpFrame(item, `${path}[${index}]`));
  return freezeArray(frames.slice(-200));
}

export function decodeTcpProfilesResponse(value: unknown, path = '$'): TcpProfilesResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`, true),
    message: text(field(source, 'message'), `${path}.message`),
    profiles: decodeTcpProfiles(field(source, 'profiles'), `${path}.profiles`),
    errors: decodeIssues(field(source, 'errors'), `${path}.errors`)
  });
}

export function decodeTcpRuntimeResponse(value: unknown, path = '$'): TcpRuntimeResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`),
    message: text(field(source, 'message'), `${path}.message`),
    status: decodeTcpStatus(field(source, 'status'), `${path}.status`),
    response: text(field(source, 'response'), `${path}.response`),
    errors: decodeIssues(field(source, 'errors'), `${path}.errors`)
  });
}

export function decodeTcpStatusResponse(value: unknown, path = '$'): TcpStatusResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`, true),
    status: decodeTcpStatus(field(source, 'status'), `${path}.status`)
  });
}

export function decodeTcpFramesResponse(value: unknown, path = '$'): TcpFramesResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`, true),
    frames: decodeTcpFrames(field(source, 'frames'), `${path}.frames`)
  });
}

export function decodeTcpClearFramesResponse(value: unknown, path = '$'): TcpClearFramesResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`, true),
    message: text(field(source, 'message'), `${path}.message`)
  });
}

function cameraTriggerMode(value: unknown, path: string): CameraBindingV1['triggerMode'] {
  return enumValue(value, path, ['Software', 'External', 'Continuous'] as const, 'Software');
}

function cameraSoftwareSource(value: unknown, path: string): CameraBindingV1['softwareTriggerSource'] {
  return enumValue(value, path, ['Manual', 'EnterPhotoelectric', 'SerialPhotoelectric'] as const, 'Manual');
}

function decodeCameraBinding(value: unknown, path: string): CameraBindingV1 {
  const source = record(value, path);
  const id = text(field(source, 'id'), `${path}.id`);
  if (!id) throw new SettingsContractDecodeError(`${path}.id`, 'a non-empty string');
  const serialNumber = text(field(source, 'serialNumber', ['deviceId']), `${path}.serialNumber`);
  return Object.freeze({
    id,
    displayName: text(field(source, 'displayName'), `${path}.displayName`, id),
    deviceId: text(field(source, 'deviceId', ['serialNumber']), `${path}.deviceId`, serialNumber),
    serialNumber,
    ipAddress: text(field(source, 'ipAddress'), `${path}.ipAddress`),
    manufacturer: text(field(source, 'manufacturer'), `${path}.manufacturer`),
    modelName: text(field(source, 'modelName', ['model']), `${path}.modelName`),
    interfaceType: text(field(source, 'interfaceType', ['connectionType']), `${path}.interfaceType`),
    isEnabled: booleanValue(field(source, 'isEnabled'), `${path}.isEnabled`, true),
    isActive: booleanValue(field(source, 'isActive'), `${path}.isActive`),
    exposureTimeUs: numberValue(field(source, 'exposureTimeUs'), `${path}.exposureTimeUs`, 5000),
    gainDb: numberValue(field(source, 'gainDb'), `${path}.gainDb`, 1),
    pixelFormat: text(field(source, 'pixelFormat'), `${path}.pixelFormat`, 'Mono8'),
    triggerMode: cameraTriggerMode(field(source, 'triggerMode'), `${path}.triggerMode`),
    hardwareTriggerSource: text(field(source, 'hardwareTriggerSource'), `${path}.hardwareTriggerSource`, 'Line0'),
    softwareTriggerSource: cameraSoftwareSource(field(source, 'softwareTriggerSource'), `${path}.softwareTriggerSource`),
    enterPhotoelectricDebounceMs: integerValue(field(source, 'enterPhotoelectricDebounceMs'), `${path}.enterPhotoelectricDebounceMs`, 200),
    enterPhotoelectricTimeoutMs: integerValue(field(source, 'enterPhotoelectricTimeoutMs'), `${path}.enterPhotoelectricTimeoutMs`, 30000),
    ignoreEnterTriggerWhileBusy: booleanValue(field(source, 'ignoreEnterTriggerWhileBusy'), `${path}.ignoreEnterTriggerWhileBusy`, true),
    enterPhotoelectricDeviceId: text(field(source, 'enterPhotoelectricDeviceId'), `${path}.enterPhotoelectricDeviceId`),
    serialPhotoelectricPortName: text(field(source, 'serialPhotoelectricPortName'), `${path}.serialPhotoelectricPortName`),
    serialPhotoelectricBaudRate: integerValue(field(source, 'serialPhotoelectricBaudRate'), `${path}.serialPhotoelectricBaudRate`, 9600),
    serialPhotoelectricDebounceMs: integerValue(field(source, 'serialPhotoelectricDebounceMs'), `${path}.serialPhotoelectricDebounceMs`, 200),
    serialPhotoelectricTimeoutMs: integerValue(field(source, 'serialPhotoelectricTimeoutMs'), `${path}.serialPhotoelectricTimeoutMs`, 30000),
    ignoreSerialPhotoelectricTriggerWhileBusy: booleanValue(field(source, 'ignoreSerialPhotoelectricTriggerWhileBusy'), `${path}.ignoreSerialPhotoelectricTriggerWhileBusy`, true),
    targetFrameRateFps: integerValue(field(source, 'targetFrameRateFps'), `${path}.targetFrameRateFps`, 30),
    connectionStatus: text(field(source, 'connectionStatus', ['status']), `${path}.connectionStatus`, 'Unknown')
  });
}

function decodeCameraBindings(value: unknown, path: string): readonly CameraBindingV1[] {
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array');
  return freezeArray(value.map((item, index) => decodeCameraBinding(item, `${path}[${index}]`)));
}

function scalarDiagnostics(value: unknown, path: string): Readonly<Record<string, string | number | boolean | null>> {
  const source = record(value, path);
  const result: Record<string, string | number | boolean | null> = {};
  for (const [key, item] of Object.entries(source)) {
    if (item === null || typeof item === 'string' || typeof item === 'number' || typeof item === 'boolean') {
      result[key] = item;
    }
  }
  return Object.freeze(result);
}

function decodeDiscoveryDevice(value: unknown, path: string): CameraDiscoveryDeviceV1 {
  const source = record(value, path);
  const serialNumber = text(field(source, 'serialNumber'), `${path}.serialNumber`);
  const cameraId = text(field(source, 'cameraId', ['id']), `${path}.cameraId`, serialNumber);
  return Object.freeze({
    cameraId,
    name: text(field(source, 'name'), `${path}.name`, cameraId),
    serialNumber,
    manufacturer: text(field(source, 'manufacturer'), `${path}.manufacturer`),
    model: text(field(source, 'model'), `${path}.model`),
    userDefinedName: text(field(source, 'userDefinedName'), `${path}.userDefinedName`),
    ipAddress: nullableText(field(source, 'ipAddress'), `${path}.ipAddress`),
    connectionType: text(field(source, 'connectionType'), `${path}.connectionType`),
    interfaceType: text(field(source, 'interfaceType'), `${path}.interfaceType`),
    isConnected: booleanValue(field(source, 'isConnected'), `${path}.isConnected`)
  });
}

export function decodeCameraDiscoveryResponse(value: unknown, path = '$'): CameraDiscoveryResponseV1 {
  if (Array.isArray(value)) {
    return Object.freeze({
      devices: freezeArray(value.map((item, index) => decodeDiscoveryDevice(item, `${path}[${index}]`))),
      diagnostics: Object.freeze({})
    });
  }
  const source = responseSource(value, path);
  const devicesValue = field(source, 'devices');
  if (!Array.isArray(devicesValue)) throw new SettingsContractDecodeError(`${path}.devices`, 'an array');
  return Object.freeze({
    devices: freezeArray(devicesValue.map((item, index) => decodeDiscoveryDevice(item, `${path}.devices[${index}]`))),
    diagnostics: field(source, 'diagnostics') === undefined
      ? Object.freeze({})
      : scalarDiagnostics(field(source, 'diagnostics'), `${path}.diagnostics`)
  });
}

export function decodeCameraBindingsResponse(value: unknown, path = '$'): CameraBindingsResponseV1 {
  if (Array.isArray(value)) {
    const bindings = decodeCameraBindings(value, path);
    return Object.freeze({
      bindings,
      activeCameraId: bindings.find(item => item.isActive)?.id ?? ''
    });
  }
  const source = responseSource(value, path);
  const bindings = decodeCameraBindings(field(source, 'bindings'), `${path}.bindings`);
  return Object.freeze({
    bindings,
    activeCameraId: text(field(source, 'activeCameraId'), `${path}.activeCameraId`)
  });
}

export function decodeCameraBindingsWriteResponse(value: unknown, path = '$'): CameraBindingsWriteResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`, true),
    message: text(field(source, 'message', ['Message']), `${path}.message`)
  });
}

export function decodeTriggerDiagnostics(value: unknown, path = '$'): TriggerDiagnosticsV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    isAvailable: booleanValue(field(source, 'isAvailable'), `${path}.isAvailable`),
    listenerType: text(field(source, 'listenerType'), `${path}.listenerType`),
    pendingWaiterCount: integerValue(field(source, 'pendingWaiterCount'), `${path}.pendingWaiterCount`),
    attachedWindowHandle: nullableText(field(source, 'attachedWindowHandle'), `${path}.attachedWindowHandle`),
    lastDeviceId: nullableText(field(source, 'lastDeviceId'), `${path}.lastDeviceId`),
    lastSignalUtc: nullableText(field(source, 'lastSignalUtc'), `${path}.lastSignalUtc`),
    lastError: nullableText(field(source, 'lastError'), `${path}.lastError`)
  });
}

function decodeSerialPort(value: unknown, path: string): SerialPhotoelectricPortV1 {
  const source = record(value, path);
  return Object.freeze({
    portName: text(field(source, 'portName'), `${path}.portName`),
    displayName: text(field(source, 'displayName'), `${path}.displayName`),
    isRecommended: booleanValue(field(source, 'isRecommended'), `${path}.isRecommended`)
  });
}

export function decodeSerialPorts(value: unknown, path = '$'): readonly SerialPhotoelectricPortV1[] {
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array');
  return freezeArray(value.map((item, index) => decodeSerialPort(item, `${path}[${index}]`)));
}

export function decodeSerialPhotoelectricTestResponse(value: unknown, path = '$'): SerialPhotoelectricTestResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    message: text(field(source, 'message', ['Message']), `${path}.message`),
    source: text(field(source, 'source'), `${path}.source`),
    portName: text(field(source, 'portName'), `${path}.portName`),
    timestampUtc: text(field(source, 'timestampUtc'), `${path}.timestampUtc`)
  });
}

export function decodeEnterDeviceLearnResponse(value: unknown, path = '$'): EnterDeviceLearnResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    deviceId: text(field(source, 'deviceId'), `${path}.deviceId`),
    timestampUtc: text(field(source, 'timestampUtc'), `${path}.timestampUtc`)
  });
}

export function decodeContinuousPreviewSession(value: unknown, path = '$'): ContinuousPreviewSessionV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    sessionId: text(field(source, 'sessionId'), `${path}.sessionId`),
    cameraBindingId: text(field(source, 'cameraBindingId'), `${path}.cameraBindingId`),
    triggerMode: text(field(source, 'triggerMode'), `${path}.triggerMode`),
    targetFrameRateFps: integerValue(field(source, 'targetFrameRateFps'), `${path}.targetFrameRateFps`, 30)
  });
}

export function decodeDeviceNoContentResponse(value: unknown, path = '$'): TcpClearFramesResponseV1 {
  const source = responseSource(value, path);
  return Object.freeze({
    success: booleanValue(field(source, 'success'), `${path}.success`, true),
    message: text(field(source, 'message', ['Message']), `${path}.message`)
  });
}

export type {
  CameraBindingV1,
  CameraBindingsResponseV1,
  CameraBindingsWriteResponseV1,
  CameraDiscoveryDeviceV1,
  CameraDiscoveryResponseV1,
  ContinuousPreviewSessionV1,
  DeviceValidationIssueV1,
  EnterDeviceLearnResponseV1,
  PlcMappingV1,
  PlcProfileV1,
  PlcSettingsResponseV1,
  PlcSettingsV1,
  PlcTestConnectionResponseV1,
  SerialPhotoelectricPortV1,
  SerialPhotoelectricTestResponseV1,
  TcpClearFramesResponseV1,
  TcpFrameV1,
  TcpFramesResponseV1,
  TcpModeV1,
  TcpProfileStatusV1,
  TcpProfileV1,
  TcpProfilesResponseV1,
  TcpRuntimeResponseV1,
  TcpStatusResponseV1,
  TriggerDiagnosticsV1
};
