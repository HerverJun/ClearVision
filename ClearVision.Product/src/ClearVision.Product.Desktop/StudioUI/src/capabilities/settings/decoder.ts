import {
  SettingsContractDecodeError,
  type GenericSettingsSection,
  type SettingsErrorCode,
  type SettingsOperationSemantics,
  type SettingsSection
} from './contracts';

export type JsonRecord = Readonly<Record<string, unknown>>;

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

export interface SettingsDiskUsageProjectionV1 {
  readonly driveName: string;
  readonly sourcePath: string;
  readonly isAccessible: boolean;
  readonly canWrite: boolean;
  readonly totalBytes: number;
  readonly usedBytes: number;
  readonly freeBytes: number;
  readonly totalGb: number;
  readonly usedGb: number;
  readonly freeGb: number;
  readonly usedPercent: number;
}

export interface SettingsDatabaseStatusProjectionV1 {
  readonly exists: boolean;
  readonly state: string;
  readonly schemaVersion: number;
  readonly currentSchemaVersion: number;
  readonly appliedMigrations: readonly string[];
  readonly pendingMigrations: readonly string[];
  readonly missingSchemaItems: readonly string[];
  readonly integrityCheck: string;
  readonly foreignKeyViolationCount: number;
  readonly rowCounts: Readonly<Record<string, number>>;
  readonly issues: readonly string[];
  readonly databaseSizeBytes: number;
  readonly walSizeBytes: number;
  readonly packageFileCount: number;
}

export interface SettingsDatabaseBackupProjectionV1 {
  readonly createdAtUtc: string;
  readonly sizeBytes: number;
  readonly databaseSizeBytes: number;
  readonly packageFileCount: number;
  readonly packageBytes: number;
}

export interface SettingsUserProjectionV1 {
  readonly id: string;
  readonly username: string;
  readonly displayName: string;
  readonly role: 'Admin' | 'Engineer' | 'Operator';
  readonly isActive: boolean;
  readonly lastLoginAt: string | null;
}

export interface SettingsUsersProjectionV1 {
  readonly items: readonly SettingsUserProjectionV1[];
}

export interface SettingsAccountOperationResponseV1 {
  readonly message: string;
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
  readonly lanAddresses: readonly string[];
  readonly localStationSyncEnabled: boolean;
  readonly token: StationTokenViewV1;
  readonly paths: Readonly<{ readonly studio: string; readonly localStation: string }>;
  readonly currentRunning: Readonly<{
    readonly studioEnabled: boolean;
    readonly studioListenMode: string;
    readonly studioPort: number;
    readonly studioToken: StationTokenViewV1;
  }>;
  readonly requiresRestart: Readonly<{ readonly studio: boolean; readonly localStation: boolean }>;
  readonly localStationBaseUrl: string;
  readonly remoteStationBaseUrl: string;
  readonly localStationHubUrl: string;
  readonly remoteStationHubUrl: string;
  readonly diagnostics: readonly string[];
}

export interface StationTokenOperationV1 {
  readonly success: boolean;
  readonly operation: 'regenerate';
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
  readonly extraHeaders: JsonRecord | null;
  readonly extraQuery: JsonRecord | null;
  readonly extraBody: JsonRecord | null;
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

export interface AiModelMutationResponseV1 {
  readonly message: string;
  readonly id: string | null;
  readonly role: string | null;
}

export interface AiModelConnectionTestProjectionV1 {
  readonly connectionOk: boolean;
  readonly success: boolean;
  readonly statusCode: number | null;
  readonly errorCode: string;
  readonly latencyMs: number;
  readonly sanitizedMessage: string;
  readonly message: string;
  readonly provider: string;
  readonly modelName: string;
  readonly protocol: string;
  readonly wireApi: string;
}

export interface AiReasoningSupportProjectionV1 {
  readonly familyId: string;
  readonly familyName: string;
  readonly allowedModes: readonly string[];
  readonly allowedEfforts: readonly string[];
  readonly helpText: string;
  readonly supportsExplicitMode: boolean;
  readonly supportsEffort: boolean;
  readonly isModelLockedOn: boolean;
  readonly defaultMode: string;
}

const genericTopLevelKeys = [
  'safeSubset', 'revision', 'general', 'storage', 'runtime', 'security',
  'communication', 'tcpCommunication', 'features', 'cameras', 'activeCameraId'
] as const;
const genericAuthorityKeys = new Set(['communication', 'tcpcommunication', 'features', 'cameras', 'activecameraid']);
const sensitiveKeyPattern = /^(?:api[-_]?key|authorization|(?:old|new|reset)?password(?:hash)?|private[-_]?key|secret|token|backup[-_]?path)$/i;
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

function nullableInteger(value: unknown, path: string, minimum = 0): number | null {
  return value === null || value === undefined ? null : integerValue(value, path, minimum);
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

export function decodeSettingsDiskUsageProjectionV1(
  value: unknown,
  path = '$'
): SettingsDiskUsageProjectionV1 {
  const source = record(value, path);
  exact(source, [
    'driveName', 'sourcePath', 'isAccessible', 'canWrite', 'totalBytes', 'usedBytes', 'freeBytes',
    'totalGb', 'usedGb', 'freeGb', 'usedPercent'
  ], path);
  return Object.freeze({
    driveName: stringValue(valueOf(source, 'driveName', path), `${path}.driveName`),
    sourcePath: stringValue(valueOf(source, 'sourcePath', path), `${path}.sourcePath`),
    isAccessible: booleanValue(valueOf(source, 'isAccessible', path), `${path}.isAccessible`),
    canWrite: booleanValue(valueOf(source, 'canWrite', path), `${path}.canWrite`),
    totalBytes: integerValue(valueOf(source, 'totalBytes', path), `${path}.totalBytes`),
    usedBytes: integerValue(valueOf(source, 'usedBytes', path), `${path}.usedBytes`),
    freeBytes: integerValue(valueOf(source, 'freeBytes', path), `${path}.freeBytes`),
    totalGb: finiteNumber(valueOf(source, 'totalGb', path), `${path}.totalGb`),
    usedGb: finiteNumber(valueOf(source, 'usedGb', path), `${path}.usedGb`),
    freeGb: finiteNumber(valueOf(source, 'freeGb', path), `${path}.freeGb`),
    usedPercent: finiteNumber(valueOf(source, 'usedPercent', path), `${path}.usedPercent`)
  });
}

function decodeStringNumberRecord(value: unknown, path: string): Readonly<Record<string, number>> {
  const source = record(value, path);
  const result: Record<string, number> = {};
  for (const [key, item] of Object.entries(source)) {
    result[key] = integerValue(item, `${path}.${key}`);
  }
  return Object.freeze(result);
}

export function decodeSettingsDatabaseStatusProjectionV1(
  value: unknown,
  path = '$'
): SettingsDatabaseStatusProjectionV1 {
  const source = record(value, path);
  // Paths are validated for contract shape but intentionally omitted from the public projection.
  exact(source, [
    'databasePath', 'exists', 'state', 'schemaVersion', 'currentSchemaVersion', 'appliedMigrations',
    'pendingMigrations', 'missingSchemaItems', 'integrityCheck', 'foreignKeyViolationCount', 'rowCounts',
    'issues', 'databaseSizeBytes', 'walSizeBytes', 'backupRootDirectory', 'packageRootDirectory', 'packageFileCount'
  ], path);
  stringValue(valueOf(source, 'databasePath', path), `${path}.databasePath`, true);
  stringValue(valueOf(source, 'backupRootDirectory', path), `${path}.backupRootDirectory`, true);
  stringValue(valueOf(source, 'packageRootDirectory', path), `${path}.packageRootDirectory`, true);
  return Object.freeze({
    exists: booleanValue(valueOf(source, 'exists', path), `${path}.exists`),
    state: stringValue(valueOf(source, 'state', path), `${path}.state`),
    schemaVersion: integerValue(valueOf(source, 'schemaVersion', path), `${path}.schemaVersion`),
    currentSchemaVersion: integerValue(valueOf(source, 'currentSchemaVersion', path), `${path}.currentSchemaVersion`),
    appliedMigrations: stringArray(valueOf(source, 'appliedMigrations', path), `${path}.appliedMigrations`),
    pendingMigrations: stringArray(valueOf(source, 'pendingMigrations', path), `${path}.pendingMigrations`),
    missingSchemaItems: stringArray(valueOf(source, 'missingSchemaItems', path), `${path}.missingSchemaItems`),
    integrityCheck: stringValue(valueOf(source, 'integrityCheck', path), `${path}.integrityCheck`, true),
    foreignKeyViolationCount: integerValue(
      valueOf(source, 'foreignKeyViolationCount', path), `${path}.foreignKeyViolationCount`
    ),
    rowCounts: decodeStringNumberRecord(valueOf(source, 'rowCounts', path), `${path}.rowCounts`),
    issues: stringArray(valueOf(source, 'issues', path), `${path}.issues`,),
    databaseSizeBytes: integerValue(valueOf(source, 'databaseSizeBytes', path), `${path}.databaseSizeBytes`),
    walSizeBytes: integerValue(valueOf(source, 'walSizeBytes', path), `${path}.walSizeBytes`),
    packageFileCount: integerValue(valueOf(source, 'packageFileCount', path), `${path}.packageFileCount`)
  });
}

export function decodeSettingsDatabaseBackupProjectionV1(
  value: unknown,
  path = '$'
): SettingsDatabaseBackupProjectionV1 {
  const source = record(value, path);
  exact(source, ['backupPath', 'createdAtUtc', 'sizeBytes', 'databaseSizeBytes', 'packageFileCount', 'packageBytes'], path);
  stringValue(valueOf(source, 'backupPath', path), `${path}.backupPath`, true);
  return Object.freeze({
    createdAtUtc: stringValue(valueOf(source, 'createdAtUtc', path), `${path}.createdAtUtc`),
    sizeBytes: integerValue(valueOf(source, 'sizeBytes', path), `${path}.sizeBytes`),
    databaseSizeBytes: integerValue(valueOf(source, 'databaseSizeBytes', path), `${path}.databaseSizeBytes`),
    packageFileCount: integerValue(valueOf(source, 'packageFileCount', path), `${path}.packageFileCount`),
    packageBytes: integerValue(valueOf(source, 'packageBytes', path), `${path}.packageBytes`)
  });
}

function decodeUserRole(value: unknown, path: string): SettingsUserProjectionV1['role'] {
  if (typeof value === 'number' && Number.isInteger(value)) {
    if (value === 0) return 'Admin';
    if (value === 1) return 'Engineer';
    if (value === 2) return 'Operator';
  }
  if (typeof value === 'string') {
    const normalized = value.trim().toLowerCase();
    if (normalized === 'admin') return 'Admin';
    if (normalized === 'engineer') return 'Engineer';
    if (normalized === 'operator') return 'Operator';
  }
  throw new SettingsContractDecodeError(path, 'Admin, Engineer or Operator');
}

export function decodeSettingsUserProjectionV1(value: unknown, path = '$'): SettingsUserProjectionV1 {
  const source = record(value, path);
  exact(source, ['id', 'username', 'displayName', 'role', 'isActive', 'lastLoginAt'], path);
  return Object.freeze({
    id: stringValue(valueOf(source, 'id', path), `${path}.id`),
    username: stringValue(valueOf(source, 'username', path), `${path}.username`),
    displayName: stringValue(valueOf(source, 'displayName', path), `${path}.displayName`),
    role: decodeUserRole(valueOf(source, 'role', path), `${path}.role`),
    isActive: booleanValue(valueOf(source, 'isActive', path), `${path}.isActive`),
    lastLoginAt: optionalValueOf(source, 'lastLoginAt') === undefined
      ? null
      : nullableString(optionalValueOf(source, 'lastLoginAt'), `${path}.lastLoginAt`)
  });
}

export function decodeSettingsUsersProjectionV1(value: unknown, path = '$'): SettingsUsersProjectionV1 {
  if (!Array.isArray(value)) throw new SettingsContractDecodeError(path, 'an array of user projections');
  return Object.freeze({
    items: Object.freeze(value.map((item, index) => decodeSettingsUserProjectionV1(item, `${path}[${index}]`)))
  });
}

export function decodeSettingsAccountOperationResponseV1(
  value: unknown,
  path = '$'
): SettingsAccountOperationResponseV1 {
  const source = record(value, path);
  exact(source, ['message'], path);
  return Object.freeze({
    message: stringValue(valueOf(source, 'message', path), `${path}.message`, true),
    semantics: Object.freeze({
      persistence: 'persisted', effective: 'reload-dependent', restart: 'none', conflict: 'none',
      unknownOutcome: 'stop-and-report'
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
      : '设置请求未能完成。',
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
  const paths = record(valueOf(source, 'paths', path), `${path}.paths`);
  exact(paths, ['studio', 'localStation'], `${path}.paths`);
  const currentRunning = record(valueOf(source, 'currentRunning', path), `${path}.currentRunning`);
  exact(currentRunning, ['studioEnabled', 'studioListenMode', 'studioPort', 'studioToken'], `${path}.currentRunning`);
  const runningToken = decodeStationTokenView(
    valueOf(currentRunning, 'studioToken', `${path}.currentRunning`),
    `${path}.currentRunning.studioToken`
  );
  const restart = record(valueOf(source, 'requiresRestart', path), `${path}.requiresRestart`);
  exact(restart, ['studio', 'localStation'], `${path}.requiresRestart`);
  return Object.freeze({
    mode: stringValue(valueOf(source, 'mode', path), `${path}.mode`),
    port: integerValue(valueOf(source, 'port', path), `${path}.port`, 0),
    lanHost: stringValue(valueOf(source, 'lanHost', path), `${path}.lanHost`, true),
    lanAddresses: stringArray(valueOf(source, 'lanAddresses', path), `${path}.lanAddresses`),
    localStationSyncEnabled: booleanValue(
      valueOf(source, 'localStationSyncEnabled', path), `${path}.localStationSyncEnabled`
    ),
    token,
    paths: Object.freeze({
      studio: stringValue(valueOf(paths, 'studio', `${path}.paths`), `${path}.paths.studio`, true),
      localStation: stringValue(
        valueOf(paths, 'localStation', `${path}.paths`),
        `${path}.paths.localStation`,
        true
      )
    }),
    currentRunning: Object.freeze({
      studioEnabled: booleanValue(
        valueOf(currentRunning, 'studioEnabled', `${path}.currentRunning`),
        `${path}.currentRunning.studioEnabled`
      ),
      studioListenMode: stringValue(
        valueOf(currentRunning, 'studioListenMode', `${path}.currentRunning`),
        `${path}.currentRunning.studioListenMode`,
        true
      ),
      studioPort: integerValue(
        valueOf(currentRunning, 'studioPort', `${path}.currentRunning`),
        `${path}.currentRunning.studioPort`,
        0
      ),
      studioToken: runningToken
    }),
    requiresRestart: Object.freeze({
      studio: booleanValue(valueOf(restart, 'studio', `${path}.requiresRestart`), `${path}.requiresRestart.studio`),
      localStation: booleanValue(
        valueOf(restart, 'localStation', `${path}.requiresRestart`), `${path}.requiresRestart.localStation`
      )
    }),
    localStationBaseUrl: stringValue(
      valueOf(source, 'localStationBaseUrl', path), `${path}.localStationBaseUrl`, true
    ),
    remoteStationBaseUrl: stringValue(
      valueOf(source, 'remoteStationBaseUrl', path), `${path}.remoteStationBaseUrl`, true
    ),
    localStationHubUrl: stringValue(
      valueOf(source, 'localStationHubUrl', path), `${path}.localStationHubUrl`, true
    ),
    remoteStationHubUrl: stringValue(
      valueOf(source, 'remoteStationHubUrl', path), `${path}.remoteStationHubUrl`, true
    ),
    diagnostics: stringArray(valueOf(source, 'diagnostics', path), `${path}.diagnostics`)
  });
}

export function decodeStationTokenOperationV1(value: unknown, path = '$'): StationTokenOperationV1 {
  const source = record(value, path);
  exact(source, ['success', 'operation', 'tokenInfo', 'settings', 'message', 'errors'], path);
  const operation = stringValue(valueOf(source, 'operation', path), `${path}.operation`).toLowerCase();
  if (operation !== 'regenerate') {
    throw new SettingsContractDecodeError(`${path}.operation`, 'regenerate');
  }
  const errors = optionalValueOf(source, 'errors');
  return Object.freeze({
    success: booleanValue(valueOf(source, 'success', path), `${path}.success`),
    operation,
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
  const extraHeaders = redactedJson(optionalValueOf(source, 'extraHeaders'), `${path}.extraHeaders`);
  const extraQuery = redactedJson(optionalValueOf(source, 'extraQuery'), `${path}.extraQuery`);
  const extraBody = redactedJson(optionalValueOf(source, 'extraBody'), `${path}.extraBody`);
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
    timeoutMs: nullableInteger(optionalValueOf(source, 'timeoutMs'), `${path}.timeoutMs`),
    isActive: booleanValue(valueOf(source, 'isActive', path), `${path}.isActive`),
    isEnabled: booleanValue(valueOf(source, 'isEnabled', path), `${path}.isEnabled`),
    protocol: nullableString(optionalValueOf(source, 'protocol'), `${path}.protocol`),
    wireApi: nullableString(optionalValueOf(source, 'wireApi'), `${path}.wireApi`),
    authMode: nullableString(optionalValueOf(source, 'authMode'), `${path}.authMode`),
    authHeaderName: nullableString(optionalValueOf(source, 'authHeaderName'), `${path}.authHeaderName`),
    roleBindings: optionalValueOf(source, 'roleBindings') === undefined
      ? Object.freeze([]) : stringArray(optionalValueOf(source, 'roleBindings'), `${path}.roleBindings`),
    modelRole: nullableString(optionalValueOf(source, 'modelRole'), `${path}.modelRole`),
    priority: nullableInteger(optionalValueOf(source, 'priority'), `${path}.priority`),
    remark: nullableString(optionalValueOf(source, 'remark'), `${path}.remark`),
    lastTestStatus: nullableString(optionalValueOf(source, 'lastTestStatus'), `${path}.lastTestStatus`),
    lastTestAt: nullableString(optionalValueOf(source, 'lastTestAt'), `${path}.lastTestAt`),
    lastTestLatencyMs: nullableInteger(optionalValueOf(source, 'lastTestLatencyMs'), `${path}.lastTestLatencyMs`),
    extraHeaders,
    extraQuery,
    extraBody,
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

export function decodeAiModelMutationResponseV1(value: unknown, path = '$'): AiModelMutationResponseV1 {
  const source = record(value, path);
  exact(source, ['message', 'id', 'role'], path);
  const id = optionalValueOf(source, 'id');
  const role = optionalValueOf(source, 'role');
  return Object.freeze({
    message: stringValue(valueOf(source, 'message', path), `${path}.message`, true),
    id: nullableString(id, `${path}.id`),
    role: nullableString(role, `${path}.role`)
  });
}

export function decodeAiModelConnectionTestProjectionV1(
  value: unknown,
  path = '$'
): AiModelConnectionTestProjectionV1 {
  const source = record(value, path);
  exact(source, [
    'connectionOk', 'success', 'statusCode', 'errorCode', 'latencyMs', 'sanitizedMessage', 'message',
    'provider', 'modelName', 'protocol', 'wireApi'
  ], path);
  const statusCode = optionalValueOf(source, 'statusCode');
  return Object.freeze({
    connectionOk: booleanValue(valueOf(source, 'connectionOk', path), `${path}.connectionOk`),
    success: booleanValue(valueOf(source, 'success', path), `${path}.success`),
    statusCode: statusCode === null || statusCode === undefined
      ? null : integerValue(statusCode, `${path}.statusCode`),
    errorCode: stringValue(valueOf(source, 'errorCode', path), `${path}.errorCode`, true),
    latencyMs: integerValue(valueOf(source, 'latencyMs', path), `${path}.latencyMs`),
    sanitizedMessage: stringValue(
      valueOf(source, 'sanitizedMessage', path), `${path}.sanitizedMessage`, true
    ),
    message: stringValue(valueOf(source, 'message', path), `${path}.message`, true),
    provider: stringValue(valueOf(source, 'provider', path), `${path}.provider`, true),
    modelName: stringValue(valueOf(source, 'modelName', path), `${path}.modelName`, true),
    protocol: stringValue(valueOf(source, 'protocol', path), `${path}.protocol`, true),
    wireApi: stringValue(valueOf(source, 'wireApi', path), `${path}.wireApi`, true)
  });
}

export function decodeAiReasoningSupportProjectionV1(
  value: unknown,
  path = '$'
): AiReasoningSupportProjectionV1 {
  const source = record(value, path);
  exact(source, [
    'familyId', 'familyName', 'allowedModes', 'allowedEfforts', 'helpText', 'supportsExplicitMode',
    'supportsEffort', 'isModelLockedOn', 'defaultMode'
  ], path);
  return Object.freeze({
    familyId: stringValue(valueOf(source, 'familyId', path), `${path}.familyId`, true),
    familyName: stringValue(valueOf(source, 'familyName', path), `${path}.familyName`, true),
    allowedModes: stringArray(valueOf(source, 'allowedModes', path), `${path}.allowedModes`),
    allowedEfforts: stringArray(valueOf(source, 'allowedEfforts', path), `${path}.allowedEfforts`),
    helpText: stringValue(valueOf(source, 'helpText', path), `${path}.helpText`, true),
    supportsExplicitMode: booleanValue(
      valueOf(source, 'supportsExplicitMode', path), `${path}.supportsExplicitMode`
    ),
    supportsEffort: booleanValue(valueOf(source, 'supportsEffort', path), `${path}.supportsEffort`),
    isModelLockedOn: booleanValue(valueOf(source, 'isModelLockedOn', path), `${path}.isModelLockedOn`),
    defaultMode: stringValue(valueOf(source, 'defaultMode', path), `${path}.defaultMode`, true)
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
