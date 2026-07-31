import {
  SettingsContractDecodeError,
  type GenericSettingsSection,
  type SettingsErrorCode,
  type SettingsOperationSemantics,
  type SettingsSection
} from './contracts';

type JsonRecord = Readonly<Record<string, unknown>>;

export interface SettingsGeneralProjectionV1 {
  readonly softwareTitle: string;
  readonly theme: 'dark' | 'light';
  readonly autoStart: boolean | null;
}

export interface SettingsStorageProjectionV1 {
  readonly imageSavePath: string;
  readonly savePolicy: string;
  readonly retentionDays: number;
  readonly minFreeSpaceGb: number;
}

export interface SettingsRuntimeProjectionV1 {
  readonly autoRun: boolean;
  readonly stopOnConsecutiveNg: number;
  readonly missingMaterialTimeoutSeconds: number;
  readonly applyProtectionRules: boolean;
}

export interface SettingsSecurityProjectionV1 {
  readonly passwordMinLength: number;
  readonly sessionTimeoutMinutes: number;
  readonly loginFailureLockoutCount: number;
}

export interface SettingsProjectionV1 {
  readonly revision: number;
  readonly safeSubset: boolean;
  readonly sections: Readonly<{
    readonly general: SettingsGeneralProjectionV1;
    readonly storage: SettingsStorageProjectionV1 | null;
    readonly runtime: SettingsRuntimeProjectionV1 | null;
    readonly security: SettingsSecurityProjectionV1 | null;
  }>;
  readonly ignoredAuthoritySections: readonly string[];
}

export interface SettingsWriteResponseV1 {
  readonly message: string;
  readonly config: SettingsProjectionV1;
  readonly semantics: SettingsOperationSemantics;
}

export interface SettingsThemeWriteResponseV1 {
  readonly message: string;
  readonly theme: 'dark' | 'light';
  readonly semantics: SettingsOperationSemantics;
}

export interface SettingsValidationIssueV1 {
  readonly field: string;
  readonly message: string;
  readonly code: string | null;
}

export interface SettingsErrorProjectionV1 {
  readonly code: SettingsErrorCode;
  readonly publicMessage: string;
  readonly policy: string | null;
  readonly issues: readonly SettingsValidationIssueV1[];
}

export interface StationTokenViewV1 {
  readonly hasToken: boolean;
  readonly mask: string;
  readonly last4: string;
}

export interface StationCommunicationProjectionV1 {
  readonly mode: string;
  readonly port: number;
  readonly lanHost: string;
  readonly localStationSyncEnabled: boolean;
  readonly token: StationTokenViewV1;
  readonly requiresRestart: Readonly<{ readonly studio: boolean; readonly localStation: boolean }>;
}

export interface StationTokenOperationV1 {
  readonly success: boolean;
  readonly operation: 'reveal' | 'regenerate';
  /** Ephemeral response only. The Settings owner must never store this value. */
  readonly token: string;
  readonly tokenInfo: StationTokenViewV1;
  readonly settings: StationCommunicationProjectionV1 | null;
  readonly message: string;
  readonly issues: readonly SettingsValidationIssueV1[];
}

export interface AiModelProjectionV1 {
  readonly id: string;
  readonly name: string | null;
  readonly displayName: string;
  readonly provider: string;
  readonly model: string;
  readonly hasApiKey: boolean | null;
  readonly apiKeyMasked: string | null;
  readonly baseUrl: string | null;
  readonly timeoutMs: number | null;
  readonly isActive: boolean;
  readonly isEnabled: boolean;
  readonly protocol: string | null;
  readonly wireApi: string | null;
  readonly authMode: string | null;
  readonly authHeaderName: string | null;
  readonly roleBindings: readonly string[];
  readonly modelRole: string | null;
  readonly priority: number | null;
  readonly remark: string | null;
  readonly lastTestStatus: string | null;
  readonly lastTestAt: string | null;
  readonly lastTestLatencyMs: number | null;
  readonly capabilities: JsonRecord | null;
  readonly reasoning: JsonRecord | null;
  readonly reasoningSupport: JsonRecord | null;
}

export interface AiModelSafeProjectionV1 {
  readonly id: string;
  readonly displayName: string;
  readonly provider: string;
  readonly model: string;
  readonly modelRole: string | null;
  readonly isEnabled: boolean;
  readonly isActive: boolean;
  readonly capabilities: JsonRecord | null;
}

export type AiModelPublicProjectionV1 = AiModelProjectionV1 | AiModelSafeProjectionV1;

export interface AiModelsProjectionV1 {
  readonly safeSubset: boolean;
  readonly items: readonly AiModelPublicProjectionV1[];
}

const genericTopLevelKeys = [
  'safeSubset', 'revision', 'general', 'storage', 'runtime', 'security',
  'communication', 'tcpCommunication', 'features', 'cameras', 'activeCameraId'
] as const;
const genericAuthorityKeys = new Set(['communication', 'tcpcommunication', 'features', 'cameras', 'activecameraid']);
const sensitiveKeyPattern = /^(?:api[-_]?key|authorization|password|private[-_]?key|secret|token)$/i;
const redactedValue = '<redacted>';

function record(value: unknown, path: string): JsonRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new SettingsContractDecodeError(path, 'an object');
  }
  return value as JsonRecord;
}

function keyOf(source: JsonRecord, canonical: string): string | undefined {
  const normalized = canonical.toLowerCase();
  return Object.keys(source).find(key => key.toLowerCase() === normalized);
}

function has(source: JsonRecord, canonical: string): boolean {
  return keyOf(source, canonical) !== undefined;
}

function valueOf(source: JsonRecord, canonical: string, path: string): unknown {
  const key = keyOf(source, canonical);
  if (!key) throw new SettingsContractDecodeError(`${path}.${canonical}`, 'present');
  return source[key];
}

function optionalValueOf(source: JsonRecord, canonical: string): unknown {
  const key = keyOf(source, canonical);
  return key ? source[key] : undefined;
}

function exact(source: JsonRecord, allowed: readonly string[], path: string): void {
  const allowedKeys = new Set(allowed.map(item => item.toLowerCase()));
  const unexpected = Object.keys(source).find(key => !allowedKeys.has(key.toLowerCase()));
  if (unexpected) throw new SettingsContractDecodeError(`${path}.${unexpected}`, 'an approved DTO field');
}

function stringValue(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && !value.trim())) {
    throw new SettingsContractDecodeError(path, allowEmpty ? 'a string' : 'a non-empty string');
  }
  return value;
}

function nullableString(value: unknown, path: string): string | null {
  return value === null || value === undefined ? null : stringValue(value, path, true);
}

function booleanValue(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') throw new SettingsContractDecodeError(path, 'a boolean');
  return value;
}

function integerValue(value: unknown, path: string, minimum = 0): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum) {
    throw new SettingsContractDecodeError(path, `a safe integer >= ${minimum}`);
  }
  return value;
}

function finiteNumber(value: unknown, path: string, minimum = 0): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < minimum) {
    throw new SettingsContractDecodeError(path, `a finite number >= ${minimum}`);
  }
  return value;
}

function stringArray(value: unknown, path: string): readonly string[] {
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array of strings');
  return Object.freeze(value.map((item, index) => stringValue(item, `${path}[${index}]`, true)));
}

function assertNoRawSensitiveKeys(value: unknown, path: string): void {
  if (Array.isArray(value)) {
    value.forEach((item, index) => assertNoRawSensitiveKeys(item, `${path}[${index}]`));
    return;
  }
  if (typeof value !== 'object' || value === null) return;
  for (const [key, item] of Object.entries(value)) {
    if (sensitiveKeyPattern.test(key)) {
      throw new SettingsContractDecodeError(`${path}.${key}`, 'absent from a persisted public projection');
    }
    assertNoRawSensitiveKeys(item, `${path}.${key}`);
  }
}

function decodeGeneral(value: unknown, path: string): SettingsGeneralProjectionV1 {
  const source = record(value, path);
  exact(source, ['softwareTitle', 'theme', 'autoStart'], path);
  const theme = stringValue(valueOf(source, 'theme', path), `${path}.theme`).toLowerCase();
  if (theme !== 'dark' && theme !== 'light') {
    throw new SettingsContractDecodeError(`${path}.theme`, 'dark or light');
  }
  const autoStart = optionalValueOf(source, 'autoStart');
  return Object.freeze({
    softwareTitle: stringValue(valueOf(source, 'softwareTitle', path), `${path}.softwareTitle`),
    theme,
    autoStart: autoStart === undefined ? null : booleanValue(autoStart, `${path}.autoStart`)
  });
}

function decodeStorage(value: unknown, path: string): SettingsStorageProjectionV1 {
  const source = record(value, path);
  exact(source, ['imageSavePath', 'savePolicy', 'retentionDays', 'minFreeSpaceGb'], path);
  return Object.freeze({
    imageSavePath: stringValue(valueOf(source, 'imageSavePath', path), `${path}.imageSavePath`, true),
    savePolicy: stringValue(valueOf(source, 'savePolicy', path), `${path}.savePolicy`, true),
    retentionDays: integerValue(valueOf(source, 'retentionDays', path), `${path}.retentionDays`),
    minFreeSpaceGb: finiteNumber(valueOf(source, 'minFreeSpaceGb', path), `${path}.minFreeSpaceGb`)
  });
}

function decodeRuntime(value: unknown, path: string): SettingsRuntimeProjectionV1 {
  const source = record(value, path);
  exact(source, [
    'autoRun', 'stopOnConsecutiveNg', 'missingMaterialTimeoutSeconds', 'applyProtectionRules', 'runtimePreviewPilot'
  ], path);
  return Object.freeze({
    autoRun: booleanValue(valueOf(source, 'autoRun', path), `${path}.autoRun`),
    stopOnConsecutiveNg: integerValue(valueOf(source, 'stopOnConsecutiveNg', path), `${path}.stopOnConsecutiveNg`),
    missingMaterialTimeoutSeconds: integerValue(
      valueOf(source, 'missingMaterialTimeoutSeconds', path), `${path}.missingMaterialTimeoutSeconds`
    ),
    applyProtectionRules: booleanValue(
      valueOf(source, 'applyProtectionRules', path), `${path}.applyProtectionRules`
    )
  });
}

function decodeSecurity(value: unknown, path: string): SettingsSecurityProjectionV1 {
  const source = record(value, path);
  exact(source, ['passwordMinLength', 'sessionTimeoutMinutes', 'loginFailureLockoutCount'], path);
  return Object.freeze({
    passwordMinLength: integerValue(valueOf(source, 'passwordMinLength', path), `${path}.passwordMinLength`, 1),
    sessionTimeoutMinutes: integerValue(valueOf(source, 'sessionTimeoutMinutes', path), `${path}.sessionTimeoutMinutes`, 1),
    loginFailureLockoutCount: integerValue(
      valueOf(source, 'loginFailureLockoutCount', path), `${path}.loginFailureLockoutCount`, 1
    )
  });
}

export function decodeSettingsProjectionV1(value: unknown, path = '$'): SettingsProjectionV1 {
  const source = record(value, path);
  exact(source, genericTopLevelKeys, path);
  assertNoRawSensitiveKeys(source, path);

  const safeSubsetValue = optionalValueOf(source, 'safeSubset');
  const safeSubset = safeSubsetValue === undefined
    ? false
    : booleanValue(safeSubsetValue, `${path}.safeSubset`);
  const hasRestrictedSection = ['storage', 'runtime', 'security'].some(key => has(source, key));
  if (safeSubset && hasRestrictedSection) {
    throw new SettingsContractDecodeError(path, 'a safe subset without restricted sections');
  }

  const ignoredAuthoritySections = Object.freeze(
    Object.keys(source).filter(key => genericAuthorityKeys.has(key.toLowerCase()))
  );
  return Object.freeze({
    revision: integerValue(valueOf(source, 'revision', path), `${path}.revision`),
    safeSubset,
    sections: Object.freeze({
      general: decodeGeneral(valueOf(source, 'general', path), `${path}.general`),
      storage: has(source, 'storage') ? decodeStorage(valueOf(source, 'storage', path), `${path}.storage`) : null,
      runtime: has(source, 'runtime') ? decodeRuntime(valueOf(source, 'runtime', path), `${path}.runtime`) : null,
      security: has(source, 'security') ? decodeSecurity(valueOf(source, 'security', path), `${path}.security`) : null
    }),
    ignoredAuthoritySections
  });
}

export function decodeSettingsWriteResponseV1(value: unknown, path = '$'): SettingsWriteResponseV1 {
  const source = record(value, path);
  exact(source, ['message', 'config'], path);
  return Object.freeze({
    message: stringValue(valueOf(source, 'message', path), `${path}.message`),
    config: decodeSettingsProjectionV1(valueOf(source, 'config', path), `${path}.config`),
    semantics: Object.freeze({
      persistence: 'persisted', effective: 'immediate-projection', restart: 'unknown',
      conflict: 'backend-contract-gap', unknownOutcome: 'reload-before-retry'
    } satisfies SettingsOperationSemantics)
  });
}

export function decodeSettingsThemeWriteResponseV1(value: unknown, path = '$'): SettingsThemeWriteResponseV1 {
  const source = record(value, path);
  exact(source, ['message', 'theme'], path);
  const theme = stringValue(valueOf(source, 'theme', path), `${path}.theme`).toLowerCase();
  if (theme !== 'dark' && theme !== 'light') {
    throw new SettingsContractDecodeError(`${path}.theme`, 'dark or light');
  }
  return Object.freeze({
    message: stringValue(valueOf(source, 'message', path), `${path}.message`),
    theme,
    semantics: Object.freeze({
      persistence: 'persisted', effective: 'immediate-projection', restart: 'unknown',
      conflict: 'backend-contract-gap', unknownOutcome: 'reload-before-retry'
    } satisfies SettingsOperationSemantics)
  });
}

function decodeIssue(value: unknown, path: string): SettingsValidationIssueV1 {
  const source = record(value, path);
  exact(source, ['field', 'message', 'code'], path);
  return Object.freeze({
    field: stringValue(valueOf(source, 'field', path), `${path}.field`, true),
    message: stringValue(valueOf(source, 'message', path), `${path}.message`, true),
    code: nullableString(optionalValueOf(source, 'code'), `${path}.code`)
  });
}

export function decodeSettingsErrorPayloadV1(
  value: unknown,
  path = '$',
  fallbackCode: SettingsErrorCode = 'unexpected-http-status'
): SettingsErrorProjectionV1 {
  const source = record(value, path);
  exact(source, ['error', 'code', 'policy', 'errorCode', 'publicMessage', 'message', 'errors', 'success', 'status'], path);
  assertNoRawSensitiveKeys(source, path);
  const codeValue = optionalValueOf(source, 'errorCode') ?? optionalValueOf(source, 'code');
  const knownCodes = new Set<SettingsErrorCode>([
    'unauthorized', 'forbidden', 'not-found', 'conflict', 'validation', 'network', 'abort', 'decode',
    'server', 'unexpected-http-status', 'unknown-outcome', 'sensitive-field', 'unsupported'
  ]);
  const code = typeof codeValue === 'string' && knownCodes.has(codeValue as SettingsErrorCode)
    ? codeValue as SettingsErrorCode
    : fallbackCode;
  const issueValue = optionalValueOf(source, 'errors');
  const issues = issueValue === undefined || issueValue === null
    ? Object.freeze([])
    : Array.isArray(issueValue)
      ? Object.freeze(issueValue.map((item, index) => decodeIssue(item, `${path}.errors[${index}]`)))
      : (() => { throw new SettingsContractDecodeError(`${path}.errors`, 'an array of validation issues'); })();
  const publicMessage = optionalValueOf(source, 'publicMessage') ??
    optionalValueOf(source, 'message') ?? optionalValueOf(source, 'error');
  return Object.freeze({
    code,
    publicMessage: typeof publicMessage === 'string' && publicMessage.trim()
      ? publicMessage.trim()
      : 'Settings 请求未能完成。',
    policy: nullableString(optionalValueOf(source, 'policy'), `${path}.policy`),
    issues
  });
}

function decodeStationTokenView(value: unknown, path: string): StationTokenViewV1 {
  const source = record(value, path);
  exact(source, ['hasToken', 'mask', 'last4'], path);
  return Object.freeze({
    hasToken: booleanValue(valueOf(source, 'hasToken', path), `${path}.hasToken`),
    mask: stringValue(valueOf(source, 'mask', path), `${path}.mask`, true),
    last4: stringValue(valueOf(source, 'last4', path), `${path}.last4`, true)
  });
}

export function decodeStationCommunicationProjectionV1(
  value: unknown,
  path = '$'
): StationCommunicationProjectionV1 {
  const source = record(value, path);
  exact(source, [
    'success', 'message', 'mode', 'port', 'lanHost', 'lanAddresses', 'localStationSyncEnabled', 'token', 'paths',
    'currentRunning', 'requiresRestart', 'localStationBaseUrl', 'remoteStationBaseUrl', 'localStationHubUrl',
    'remoteStationHubUrl', 'diagnostics'
  ], path);
  const token = decodeStationTokenView(valueOf(source, 'token', path), `${path}.token`);
  const restart = record(valueOf(source, 'requiresRestart', path), `${path}.requiresRestart`);
  exact(restart, ['studio', 'localStation'], `${path}.requiresRestart`);
  return Object.freeze({
    mode: stringValue(valueOf(source, 'mode', path), `${path}.mode`),
    port: integerValue(valueOf(source, 'port', path), `${path}.port`, 0),
    lanHost: stringValue(valueOf(source, 'lanHost', path), `${path}.lanHost`, true),
    localStationSyncEnabled: booleanValue(
      valueOf(source, 'localStationSyncEnabled', path), `${path}.localStationSyncEnabled`
    ),
    token,
    requiresRestart: Object.freeze({
      studio: booleanValue(valueOf(restart, 'studio', `${path}.requiresRestart`), `${path}.requiresRestart.studio`),
      localStation: booleanValue(
        valueOf(restart, 'localStation', `${path}.requiresRestart`), `${path}.requiresRestart.localStation`
      )
    })
  });
}

export function decodeStationTokenOperationV1(value: unknown, path = '$'): StationTokenOperationV1 {
  const source = record(value, path);
  exact(source, ['success', 'operation', 'token', 'tokenInfo', 'settings', 'message', 'errors'], path);
  const operation = stringValue(valueOf(source, 'operation', path), `${path}.operation`).toLowerCase();
  if (operation !== 'reveal' && operation !== 'regenerate') {
    throw new SettingsContractDecodeError(`${path}.operation`, 'reveal or regenerate');
  }
  const errors = optionalValueOf(source, 'errors');
  return Object.freeze({
    success: booleanValue(valueOf(source, 'success', path), `${path}.success`),
    operation,
    token: stringValue(valueOf(source, 'token', path), `${path}.token`, true),
    tokenInfo: decodeStationTokenView(valueOf(source, 'tokenInfo', path), `${path}.tokenInfo`),
    settings: optionalValueOf(source, 'settings') === null || optionalValueOf(source, 'settings') === undefined
      ? null
      : decodeStationCommunicationProjectionV1(valueOf(source, 'settings', path), `${path}.settings`),
    message: stringValue(valueOf(source, 'message', path), `${path}.message`, true),
    issues: errors === undefined || errors === null
      ? Object.freeze([])
      : Array.isArray(errors)
        ? Object.freeze(errors.map((item, index) => decodeIssue(item, `${path}.errors[${index}]`)))
        : (() => { throw new SettingsContractDecodeError(`${path}.errors`, 'an array of validation issues'); })()
  });
}

function validateRedactedJson(value: unknown, path: string): void {
  if (value === null || value === undefined) return;
  if (typeof value !== 'object') return;
  if (Array.isArray(value)) {
    value.forEach((item, index) => validateRedactedJson(item, `${path}[${index}]`));
    return;
  }
  const source = record(value, path);
  for (const [key, item] of Object.entries(source)) {
    if (sensitiveKeyPattern.test(key) && item !== redactedValue) {
      throw new SettingsContractDecodeError(`${path}.${key}`, 'the literal <redacted> marker');
    }
    validateRedactedJson(item, `${path}.${key}`);
  }
}

function redactedJson(value: unknown, path: string): JsonRecord | null {
  if (value === null || value === undefined) return null;
  const source = record(value, path);
  validateRedactedJson(source, path);
  return Object.freeze({ ...source });
}

function decodeFullAiModel(value: unknown, path: string): AiModelProjectionV1 {
  const source = record(value, path);
  exact(source, [
    'id', 'name', 'displayName', 'provider', 'model', 'hasApiKey', 'apiKeyMasked', 'baseUrl', 'timeoutMs', 'isActive',
    'isEnabled', 'protocol', 'wireApi', 'authMode', 'authHeaderName', 'extraHeaders', 'extraQuery', 'extraBody',
    'roleBindings', 'modelRole', 'priority', 'remark', 'createdAt', 'updatedAt', 'lastTestStatus', 'lastTestAt',
    'lastTestLatencyMs', 'capabilities', 'reasoning', 'reasoningSupport'
  ], path);
  redactedJson(optionalValueOf(source, 'extraHeaders'), `${path}.extraHeaders`);
  redactedJson(optionalValueOf(source, 'extraQuery'), `${path}.extraQuery`);
  redactedJson(optionalValueOf(source, 'extraBody'), `${path}.extraBody`);
  const capability = redactedJson(optionalValueOf(source, 'capabilities'), `${path}.capabilities`);
  const reasoning = redactedJson(optionalValueOf(source, 'reasoning'), `${path}.reasoning`);
  const reasoningSupport = redactedJson(optionalValueOf(source, 'reasoningSupport'), `${path}.reasoningSupport`);
  return Object.freeze({
    id: stringValue(valueOf(source, 'id', path), `${path}.id`),
    name: nullableString(optionalValueOf(source, 'name'), `${path}.name`),
    displayName: stringValue(valueOf(source, 'displayName', path), `${path}.displayName`),
    provider: stringValue(valueOf(source, 'provider', path), `${path}.provider`),
    model: stringValue(valueOf(source, 'model', path), `${path}.model`, true),
    hasApiKey: optionalValueOf(source, 'hasApiKey') === undefined
      ? null : booleanValue(optionalValueOf(source, 'hasApiKey'), `${path}.hasApiKey`),
    apiKeyMasked: nullableString(optionalValueOf(source, 'apiKeyMasked'), `${path}.apiKeyMasked`),
    baseUrl: nullableString(optionalValueOf(source, 'baseUrl'), `${path}.baseUrl`),
    timeoutMs: optionalValueOf(source, 'timeoutMs') === undefined
      ? null : integerValue(optionalValueOf(source, 'timeoutMs'), `${path}.timeoutMs`),
    isActive: booleanValue(valueOf(source, 'isActive', path), `${path}.isActive`),
    isEnabled: booleanValue(valueOf(source, 'isEnabled', path), `${path}.isEnabled`),
    protocol: nullableString(optionalValueOf(source, 'protocol'), `${path}.protocol`),
    wireApi: nullableString(optionalValueOf(source, 'wireApi'), `${path}.wireApi`),
    authMode: nullableString(optionalValueOf(source, 'authMode'), `${path}.authMode`),
    authHeaderName: nullableString(optionalValueOf(source, 'authHeaderName'), `${path}.authHeaderName`),
    roleBindings: optionalValueOf(source, 'roleBindings') === undefined
      ? Object.freeze([]) : stringArray(optionalValueOf(source, 'roleBindings'), `${path}.roleBindings`),
    modelRole: nullableString(optionalValueOf(source, 'modelRole'), `${path}.modelRole`),
    priority: optionalValueOf(source, 'priority') === undefined
      ? null : integerValue(optionalValueOf(source, 'priority'), `${path}.priority`),
    remark: nullableString(optionalValueOf(source, 'remark'), `${path}.remark`),
    lastTestStatus: nullableString(optionalValueOf(source, 'lastTestStatus'), `${path}.lastTestStatus`),
    lastTestAt: nullableString(optionalValueOf(source, 'lastTestAt'), `${path}.lastTestAt`),
    lastTestLatencyMs: optionalValueOf(source, 'lastTestLatencyMs') === undefined
      ? null : integerValue(optionalValueOf(source, 'lastTestLatencyMs'), `${path}.lastTestLatencyMs`),
    capabilities: capability,
    reasoning,
    reasoningSupport
  });
}

function decodeSafeAiModel(value: unknown, path: string): AiModelSafeProjectionV1 {
  const source = record(value, path);
  exact(source, ['id', 'displayName', 'provider', 'model', 'modelRole', 'isEnabled', 'isActive', 'capabilities'], path);
  return Object.freeze({
    id: stringValue(valueOf(source, 'id', path), `${path}.id`),
    displayName: stringValue(valueOf(source, 'displayName', path), `${path}.displayName`),
    provider: stringValue(valueOf(source, 'provider', path), `${path}.provider`),
    model: stringValue(valueOf(source, 'model', path), `${path}.model`, true),
    modelRole: nullableString(optionalValueOf(source, 'modelRole'), `${path}.modelRole`),
    isEnabled: booleanValue(valueOf(source, 'isEnabled', path), `${path}.isEnabled`),
    isActive: booleanValue(valueOf(source, 'isActive', path), `${path}.isActive`),
    capabilities: redactedJson(optionalValueOf(source, 'capabilities'), `${path}.capabilities`)
  });
}

function decodeAiModel(value: unknown, path: string): AiModelPublicProjectionV1 {
  const source = record(value, path);
  const fullProjectionMarkers = ['name', 'hasApiKey', 'apiKeyMasked', 'baseUrl', 'timeoutMs', 'extraHeaders', 'extraQuery', 'extraBody'];
  return fullProjectionMarkers.some(key => has(source, key))
    ? decodeFullAiModel(value, path)
    : decodeSafeAiModel(value, path);
}

export function decodeAiModelsProjectionV1(value: unknown, path = '$'): AiModelsProjectionV1 {
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array of redacted AI model projections');
  const items = value.map((item, index) => decodeAiModel(item, `${path}[${index}]`));
  const safeSubset = items.length > 0 && items.every(item => !('name' in item));
  const fullSubset = items.length > 0 && items.every(item => 'name' in item);
  if (items.length > 0 && !safeSubset && !fullSubset) {
    throw new SettingsContractDecodeError(path, 'a consistent full or safe AI model projection');
  }
  return Object.freeze({
    safeSubset,
    items: Object.freeze(items)
  });
}

export function decodeSettingsSectionPayload(
  sectionName: GenericSettingsSection,
  value: unknown,
  path = '$'
): SettingsProjectionV1['sections'][GenericSettingsSection] {
  switch (sectionName) {
    case 'general':
      return decodeGeneral(value, path);
    case 'storage':
      return decodeStorage(value, path);
    case 'runtime':
      return decodeRuntime(value, path);
    case 'security':
      return decodeSecurity(value, path);
  }
}

export function settingsErrorCodeFromHttpStatus(status: number): SettingsErrorCode {
  switch (status) {
    case 401: return 'unauthorized';
    case 403: return 'forbidden';
    case 404: return 'not-found';
    case 409: return 'conflict';
    case 400: return 'validation';
    default: return status >= 500 ? 'server' : 'unexpected-http-status';
  }
}

export function settingsSectionForGenericScope(scope: string): GenericSettingsSection | null {
  const normalized = scope.trim().toLowerCase();
  return normalized === 'general' || normalized === 'storage' || normalized === 'runtime' || normalized === 'security'
    ? normalized
    : null;
}

export function isSettingsSection(value: string): value is SettingsSection {
  return ['general', 'storage', 'runtime', 'security', 'plc', 'tcp', 'camera', 'station', 'ai-model', 'database']
    .includes(value as SettingsSection);
}
