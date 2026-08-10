export type SettingsKnownRole = 'Admin' | 'Engineer' | 'Operator';
export type SettingsRole = string | null | undefined;

export type SettingsSection =
  | 'general'
  | 'storage'
  | 'runtime'
  | 'security'
  | 'plc'
  | 'tcp'
  | 'camera'
  | 'station'
  | 'ai-model'
  | 'database';

export type GenericSettingsSection = Extract<
  SettingsSection,
  'general' | 'storage' | 'runtime' | 'security'
>;

export const GENERIC_SETTINGS_SECTIONS = Object.freeze([
  'general', 'storage', 'runtime', 'security'
] as const satisfies readonly GenericSettingsSection[]);

export const SETTINGS_ROUTE_ROLES = Object.freeze(['Admin', 'Engineer'] as const);

export type SettingsEndpointMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
export type SettingsEndpointKind = 'read' | 'write' | 'test' | 'runtime-operation';
export type SettingsOperationKind =
  | 'read'
  | 'write'
  | 'runtime-operation'
  | 'account-operation'
  | 'database-operation';
export type SettingsEndpointSection = SettingsSection | 'generic';
export type SettingsServerPermission = 'authenticated' | 'engineer-or-admin' | 'admin';
export type SettingsUiPermission =
  | 'settings-route'
  | 'settings-authenticated'
  | 'engineer-or-admin'
  | 'admin'
  | 'excluded';

export type SettingsPersistenceSemantics =
  | 'projection-only'
  | 'persisted'
  | 'runtime-only'
  | 'deferred';

export type SettingsEffectiveSemantics =
  | 'observed-only'
  | 'immediate-projection'
  | 'reload-dependent'
  | 'restart-dependent'
  | 'runtime-operation'
  | 'not-applicable';

export type SettingsRestartTarget = 'none' | 'studio' | 'local-station' | 'studio-and-local-station' | 'unknown';
export type SettingsConflictSemantics = 'none' | '409-fail-closed' | 'backend-contract-gap';
export type SettingsUnknownOutcomeSemantics = 'not-applicable' | 'reload-before-retry' | 'stop-and-report';

export interface SettingsOperationSemantics {
  readonly persistence: SettingsPersistenceSemantics;
  readonly effective: SettingsEffectiveSemantics;
  readonly restart: SettingsRestartTarget;
  readonly conflict: SettingsConflictSemantics;
  readonly unknownOutcome: SettingsUnknownOutcomeSemantics;
}

export interface SettingsWriteTaskContext {
  readonly signal: AbortSignal;
  readonly generation: number;
  readonly operationKind: SettingsOperationKind;
}

export type SettingsWriteTask<T> = (context: SettingsWriteTaskContext) => Promise<T>;

export type SettingsWriteResult<T> =
  | {
      readonly status: 'completed';
      readonly section: SettingsSection;
      readonly generation: number;
      readonly operationKind?: SettingsOperationKind;
      readonly value: T;
    }
  | {
      readonly status: 'cancelled' | 'stale' | 'disposed' | 'forbidden';
      readonly section: SettingsSection;
      readonly generation: number;
      readonly operationKind?: SettingsOperationKind;
      readonly message: string;
    }
  | {
      readonly status: 'failed';
      readonly section: SettingsSection;
      readonly generation: number;
      readonly operationKind?: SettingsOperationKind;
      readonly error: unknown;
      readonly message: string;
    };

export interface SettingsWriteCoordinatorDiagnostics {
  readonly generation: number;
  readonly activeSectionCount: number;
  readonly activeAbortControllerCount: number;
  readonly queuedTaskCount: number;
  readonly activeOperationKinds: Readonly<Partial<Record<SettingsOperationKind, number>>>;
  readonly disposed: boolean;
}

export class SettingsUnknownOutcomeError extends Error {
  readonly originalError: unknown;
  readonly operationKind: SettingsOperationKind;

  constructor(originalError: unknown, operationKind: SettingsOperationKind) {
    super('设置操作结果未知；重试前请重新读取服务端状态。');
    this.name = 'SettingsUnknownOutcomeError';
    this.originalError = originalError;
    this.operationKind = operationKind;
  }
}

export type SettingsErrorCode =
  | 'unauthorized'
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'validation'
  | 'network'
  | 'abort'
  | 'decode'
  | 'server'
  | 'unexpected-http-status'
  | 'unknown-outcome'
  | 'sensitive-field'
  | 'unsupported';

export interface SettingsEndpointContract {
  readonly id: string;
  readonly section: SettingsEndpointSection;
  readonly method: SettingsEndpointMethod;
  /** Relative to the shared /api/ transport base. */
  readonly path: string;
  readonly kind: SettingsEndpointKind;
  readonly serverPermission: SettingsServerPermission;
  readonly uiPermission: SettingsUiPermission;
  readonly genericScope: GenericSettingsSection | null;
  /** Empty for dedicated endpoints; all four generic scopes for /settings. */
  readonly genericScopes: readonly GenericSettingsSection[];
  readonly semantics: SettingsOperationSemantics;
  readonly sensitiveFields: readonly string[];
}

export interface SettingsEndpointTaskContext extends SettingsWriteTaskContext {
  readonly endpoint: SettingsEndpointContract;
}

export type SettingsEndpointTask<T> = (context: SettingsEndpointTaskContext) => Promise<T>;

export interface SettingsSectionContract {
  readonly section: SettingsSection;
  readonly authority:
    | 'app-config'
    | 'plc'
    | 'tcp-runtime'
    | 'camera-system'
    | 'station-communication'
    | 'ai-model-store'
    | 'database-maintenance';
  readonly genericScope: GenericSettingsSection | null;
  readonly routePermission: SettingsUiPermission;
  readonly readPermission: SettingsServerPermission;
  readonly writePermission: SettingsServerPermission | null;
  readonly endpointIds: readonly string[];
  readonly sensitiveFields: readonly string[];
  readonly semantics: SettingsOperationSemantics;
}

export interface SettingsRouteAccess {
  readonly allowed: boolean;
  readonly role: SettingsRole;
  readonly reason: 'allowed' | 'operator-forbidden' | 'authenticated-role-required';
}

export type SettingsEndpointAccessReason =
  | 'allowed'
  | 'unknown-endpoint'
  | 'excluded-endpoint'
  | 'section-mismatch'
  | 'route-only'
  | 'engineer-or-admin-required'
  | 'admin-required';

export interface SettingsEndpointAccess {
  readonly allowed: boolean;
  readonly endpointId: string;
  readonly role: SettingsRole;
  readonly endpoint: SettingsEndpointContract | null;
  readonly reason: SettingsEndpointAccessReason;
}

export function evaluateSettingsRouteAccess(role: SettingsRole): SettingsRouteAccess {
  const normalized = typeof role === 'string' ? role.trim() : '';
  if (normalized === 'Admin' || normalized === 'Engineer') {
    return Object.freeze({ allowed: true, role, reason: 'allowed' });
  }
  return Object.freeze({
    allowed: false,
    role,
    reason: normalized === 'Operator' ? 'operator-forbidden' : 'authenticated-role-required'
  });
}

export const SETTINGS_SEMANTICS = Object.freeze({
  genericRead: Object.freeze({
    persistence: 'projection-only',
    effective: 'observed-only',
    restart: 'none',
    conflict: 'backend-contract-gap',
    unknownOutcome: 'reload-before-retry'
  } satisfies SettingsOperationSemantics),
  genericWrite: Object.freeze({
    persistence: 'persisted',
    effective: 'immediate-projection',
    restart: 'unknown',
    conflict: 'backend-contract-gap',
    unknownOutcome: 'reload-before-retry'
  } satisfies SettingsOperationSemantics),
  runtimeOperation: Object.freeze({
    persistence: 'runtime-only',
    effective: 'runtime-operation',
    restart: 'none',
    conflict: 'none',
    unknownOutcome: 'stop-and-report'
  } satisfies SettingsOperationSemantics),
  cameraMutation: Object.freeze({
    persistence: 'persisted',
    effective: 'immediate-projection',
    restart: 'none',
    conflict: '409-fail-closed',
    unknownOutcome: 'reload-before-retry'
  } satisfies SettingsOperationSemantics),
  stationRestart: Object.freeze({
    persistence: 'persisted',
    effective: 'restart-dependent',
    restart: 'studio-and-local-station',
    conflict: 'none',
    unknownOutcome: 'stop-and-report'
  } satisfies SettingsOperationSemantics),
  aiModelMutation: Object.freeze({
    persistence: 'persisted',
    effective: 'immediate-projection',
    restart: 'none',
    conflict: 'none',
    unknownOutcome: 'reload-before-retry'
  } satisfies SettingsOperationSemantics),
  aiModelTest: Object.freeze({
    persistence: 'persisted',
    effective: 'immediate-projection',
    restart: 'none',
    conflict: 'none',
    unknownOutcome: 'reload-before-retry'
  } satisfies SettingsOperationSemantics),
  databaseMaintenance: Object.freeze({
    persistence: 'runtime-only',
    effective: 'runtime-operation',
    restart: 'none',
    conflict: 'none',
    unknownOutcome: 'stop-and-report'
  } satisfies SettingsOperationSemantics),
  accountOperation: Object.freeze({
    persistence: 'persisted',
    effective: 'reload-dependent',
    restart: 'none',
    conflict: 'none',
    unknownOutcome: 'stop-and-report'
  } satisfies SettingsOperationSemantics)
} as const);

const genericSensitiveFields = Object.freeze([
  'apiKey', 'token', 'password', 'authorization', 'secret', 'privateKey'
]);

function endpoint(
  value: Omit<SettingsEndpointContract, 'genericScope' | 'genericScopes' | 'sensitiveFields'> & {
    readonly genericScope?: GenericSettingsSection | null;
    readonly genericScopes?: readonly GenericSettingsSection[];
    readonly sensitiveFields?: readonly string[];
  }
): SettingsEndpointContract {
  return Object.freeze({
    ...value,
    genericScope: value.genericScope ?? null,
    genericScopes: Object.freeze([...(value.genericScopes ?? [])]),
    sensitiveFields: Object.freeze([...(value.sensitiveFields ?? genericSensitiveFields)])
  });
}

const genericEndpoint = (
  id: string,
  method: SettingsEndpointMethod,
  path: string,
  kind: SettingsEndpointKind,
  scope: GenericSettingsSection | null,
  permission: SettingsServerPermission,
  semantics: SettingsOperationSemantics
): SettingsEndpointContract => endpoint({
  id,
  section: scope ?? 'generic',
  method,
  path,
  kind,
  serverPermission: permission,
  uiPermission: permission === 'admin' ? 'admin' : 'settings-route',
  genericScope: scope,
  genericScopes: scope ? [scope] : GENERIC_SETTINGS_SECTIONS,
  semantics
});

export const SETTINGS_ENDPOINT_MATRIX: readonly SettingsEndpointContract[] = Object.freeze([
  genericEndpoint('settings.read', 'GET', 'settings', 'read', null, 'authenticated', SETTINGS_SEMANTICS.genericRead),
  genericEndpoint('settings.write', 'PUT', 'settings', 'write', null, 'admin', SETTINGS_SEMANTICS.genericWrite),
  endpoint({
    id: 'settings.theme.write', section: 'general', method: 'PUT', path: 'settings/theme', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', genericScope: 'general', genericScopes: ['general'],
    semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  endpoint({
    id: 'settings.disk-usage.read', section: 'storage', method: 'GET', path: 'settings/disk-usage', kind: 'read',
    serverPermission: 'admin', uiPermission: 'admin', genericScope: 'storage', genericScopes: ['storage'],
    semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'settings.database.status.read', section: 'database', method: 'GET', path: 'settings/database/status',
    kind: 'read', serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.databaseMaintenance
  }),
  endpoint({
    id: 'settings.database.backup', section: 'database', method: 'POST', path: 'settings/database/backup',
    kind: 'runtime-operation', serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.databaseMaintenance
  }),
  endpoint({
    id: 'auth.change-password', section: 'security', method: 'POST', path: 'auth/change-password', kind: 'write',
    serverPermission: 'authenticated', uiPermission: 'settings-authenticated', semantics: SETTINGS_SEMANTICS.accountOperation,
    sensitiveFields: ['oldPassword', 'newPassword']
  }),
  endpoint({
    id: 'users.read', section: 'security', method: 'GET', path: 'users', kind: 'read',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.accountOperation,
    sensitiveFields: ['password', 'passwordHash']
  }),
  endpoint({
    id: 'users.create', section: 'security', method: 'POST', path: 'users', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.accountOperation,
    sensitiveFields: ['password']
  }),
  endpoint({
    id: 'users.update', section: 'security', method: 'PUT', path: 'users/{id}', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.accountOperation
  }),
  endpoint({
    id: 'users.delete', section: 'security', method: 'DELETE', path: 'users/{id}', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.accountOperation
  }),
  endpoint({
    id: 'users.reset-password', section: 'security', method: 'POST', path: 'users/{id}/reset-password', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.accountOperation,
    sensitiveFields: ['newPassword']
  }),
  endpoint({
    id: 'plc.settings.read', section: 'plc', method: 'GET', path: 'plc/settings', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'plc.settings.write', section: 'plc', method: 'PUT', path: 'plc/settings', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  endpoint({
    id: 'plc.test-connection', section: 'plc', method: 'POST', path: 'plc/test-connection', kind: 'test',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'plc.mappings.read', section: 'plc', method: 'GET', path: 'plc/mappings', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'plc.mappings.write', section: 'plc', method: 'PUT', path: 'plc/mappings', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  endpoint({
    id: 'tcp.profiles.read', section: 'tcp', method: 'GET', path: 'tcp/profiles', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'tcp.profiles.write', section: 'tcp', method: 'PUT', path: 'tcp/profiles', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  endpoint({
    id: 'tcp.runtime', section: 'tcp', method: 'POST', path: 'tcp/profiles/{id}/<operation>', kind: 'runtime-operation',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'camera.discovery', section: 'camera', method: 'GET', path: 'cameras/discover/<provider>', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'camera.discovery.all', section: 'camera', method: 'GET', path: 'cameras/discover', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'camera.discovery.huaray', section: 'camera', method: 'GET', path: 'cameras/discover/huaray', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'camera.discovery.hikvision', section: 'camera', method: 'GET', path: 'cameras/discover/hikvision', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'camera.bindings.read', section: 'camera', method: 'GET', path: 'cameras/bindings', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'camera.bindings.write', section: 'camera', method: 'PUT', path: 'cameras/bindings', kind: 'write',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.cameraMutation
  }),
  endpoint({
    id: 'camera.trigger-and-preview', section: 'camera', method: 'POST', path: '<camera-or-trigger-operation>', kind: 'runtime-operation',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'camera.soft-trigger-capture', section: 'camera', method: 'POST', path: 'cameras/soft-trigger-capture', kind: 'runtime-operation',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'trigger-input.diagnostics.read', section: 'camera', method: 'GET', path: 'trigger-input/diagnostics', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'trigger-input.serial-ports.read', section: 'camera', method: 'GET', path: 'trigger-input/serial-photoelectric-ports', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.genericRead
  }),
  endpoint({
    id: 'trigger-input.serial-test', section: 'camera', method: 'POST', path: 'trigger-input/test-serial-photoelectric', kind: 'runtime-operation',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'trigger-input.enter-learn', section: 'camera', method: 'POST', path: 'trigger-input/learn-enter-device', kind: 'runtime-operation',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'camera.preview.start', section: 'camera', method: 'POST', path: 'cameras/continuous-preview/start', kind: 'runtime-operation',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'camera.preview.frame', section: 'camera', method: 'GET', path: 'cameras/continuous-preview/frame/{sessionId}', kind: 'read',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'camera.preview.stop', section: 'camera', method: 'POST', path: 'cameras/continuous-preview/stop', kind: 'runtime-operation',
    serverPermission: 'engineer-or-admin', uiPermission: 'engineer-or-admin', semantics: SETTINGS_SEMANTICS.runtimeOperation
  }),
  endpoint({
    id: 'station.settings.read', section: 'station', method: 'GET', path: 'station-communication/settings', kind: 'read',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.stationRestart
  }),
  endpoint({
    id: 'station.settings.write', section: 'station', method: 'PUT', path: 'station-communication/settings', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.stationRestart
  }),
  endpoint({
    id: 'station.token', section: 'station', method: 'POST', path: 'station-communication/token', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.stationRestart,
    sensitiveFields: ['token', 'last4', 'mask']
  }),
  endpoint({
    id: 'ai.models.read', section: 'ai-model', method: 'GET', path: 'ai/models', kind: 'read',
    serverPermission: 'authenticated', uiPermission: 'settings-authenticated', semantics: SETTINGS_SEMANTICS.genericRead,
    sensitiveFields: ['apiKey', 'apiKeyMasked', 'extraHeaders', 'extraQuery', 'extraBody']
  }),
  endpoint({
    id: 'ai.models.create', section: 'ai-model', method: 'POST', path: 'ai/models', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.aiModelMutation,
    sensitiveFields: ['apiKey', 'extraHeaders', 'extraQuery', 'extraBody']
  }),
  endpoint({
    id: 'ai.models.update', section: 'ai-model', method: 'PUT', path: 'ai/models/{id}', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.aiModelMutation,
    sensitiveFields: ['apiKey', 'extraHeaders', 'extraQuery', 'extraBody']
  }),
  endpoint({
    id: 'ai.models.delete', section: 'ai-model', method: 'DELETE', path: 'ai/models/{id}', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.aiModelMutation,
    sensitiveFields: ['apiKey', 'extraHeaders', 'extraQuery', 'extraBody']
  }),
  endpoint({
    id: 'ai.models.activate', section: 'ai-model', method: 'POST', path: 'ai/models/{id}/activate', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.aiModelMutation,
    sensitiveFields: ['apiKey', 'extraHeaders', 'extraQuery', 'extraBody']
  }),
  endpoint({
    id: 'ai.models.default-planner', section: 'ai-model', method: 'POST', path: 'ai/models/{id}/default-planner', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.aiModelMutation,
    sensitiveFields: ['apiKey', 'extraHeaders', 'extraQuery', 'extraBody']
  }),
  endpoint({
    id: 'ai.models.default-shadow-eval', section: 'ai-model', method: 'POST', path: 'ai/models/{id}/default-shadow-eval', kind: 'write',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.aiModelMutation,
    sensitiveFields: ['apiKey', 'extraHeaders', 'extraQuery', 'extraBody']
  }),
  endpoint({
    id: 'ai.models.test', section: 'ai-model', method: 'POST', path: 'ai/models/{id}/test', kind: 'test',
    serverPermission: 'admin', uiPermission: 'admin', semantics: SETTINGS_SEMANTICS.aiModelTest,
    sensitiveFields: ['apiKey', 'extraHeaders', 'extraQuery', 'extraBody']
  }),
  endpoint({
    id: 'ai.reasoning-support', section: 'ai-model', method: 'POST', path: 'ai/reasoning-support', kind: 'test',
    serverPermission: 'authenticated', uiPermission: 'settings-authenticated', semantics: SETTINGS_SEMANTICS.runtimeOperation
  })
]);

export const SETTINGS_EXCLUDED_ENDPOINTS = Object.freeze([
  'settings/reset',
  'settings/database/repair',
  'settings/database/restore',
  'settings/database/cleanup',
  'settings/import',
  'settings/export',
  'settings/runtime-preview-pilot/**'
] as const);

function normalizeEndpointReference(value: string): string {
  const normalized = value.trim().replace(/^\/+/, '');
  return normalized.startsWith('api/') ? normalized.slice('api/'.length) : normalized;
}

export function isSettingsEndpointExcluded(endpointReference: string): boolean {
  const normalized = normalizeEndpointReference(endpointReference);
  return SETTINGS_EXCLUDED_ENDPOINTS.some(pattern => {
    const normalizedPattern = normalizeEndpointReference(pattern);
    return normalizedPattern.endsWith('/**')
      ? normalized.startsWith(normalizedPattern.slice(0, -2))
      : normalized === normalizedPattern;
  });
}

const section = (
  value: Omit<SettingsSectionContract, 'genericScope'> & { readonly genericScope?: GenericSettingsSection | null }
): SettingsSectionContract => Object.freeze({
  ...value,
  genericScope: value.genericScope ?? null,
  endpointIds: Object.freeze([...value.endpointIds]),
  sensitiveFields: Object.freeze([...value.sensitiveFields])
});

export const SETTINGS_SECTION_CONTRACTS: readonly SettingsSectionContract[] = Object.freeze([
  section({
    section: 'general', authority: 'app-config', genericScope: 'general', routePermission: 'settings-route',
    readPermission: 'authenticated', writePermission: 'admin', endpointIds: ['settings.read', 'settings.write', 'settings.theme.write'],
    sensitiveFields: genericSensitiveFields, semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  section({
    section: 'storage', authority: 'app-config', genericScope: 'storage', routePermission: 'settings-route',
    readPermission: 'authenticated', writePermission: 'admin', endpointIds: ['settings.read', 'settings.write', 'settings.disk-usage.read'],
    sensitiveFields: genericSensitiveFields, semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  section({
    section: 'runtime', authority: 'app-config', genericScope: 'runtime', routePermission: 'settings-route',
    readPermission: 'authenticated', writePermission: 'admin', endpointIds: ['settings.read', 'settings.write'],
    sensitiveFields: genericSensitiveFields, semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  section({
    section: 'security', authority: 'app-config', genericScope: 'security', routePermission: 'settings-route',
    readPermission: 'authenticated', writePermission: 'admin', endpointIds: [
      'settings.read', 'settings.write', 'auth.change-password', 'users.read', 'users.create', 'users.update',
      'users.delete', 'users.reset-password'
    ],
    sensitiveFields: genericSensitiveFields, semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  section({
    section: 'plc', authority: 'plc', routePermission: 'settings-route', readPermission: 'engineer-or-admin', writePermission: 'admin',
    endpointIds: ['plc.settings.read', 'plc.settings.write', 'plc.test-connection', 'plc.mappings.read', 'plc.mappings.write'],
    sensitiveFields: ['ipAddress', 'port'], semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  section({
    section: 'tcp', authority: 'tcp-runtime', routePermission: 'settings-route', readPermission: 'engineer-or-admin', writePermission: 'admin',
    endpointIds: ['tcp.profiles.read', 'tcp.profiles.write', 'tcp.runtime'],
    sensitiveFields: ['remoteHost', 'remotePort', 'localHost', 'localPort'], semantics: SETTINGS_SEMANTICS.genericWrite
  }),
  section({
    section: 'camera', authority: 'camera-system', routePermission: 'settings-route', readPermission: 'engineer-or-admin', writePermission: 'engineer-or-admin',
    endpointIds: [
      'camera.discovery', 'camera.discovery.all', 'camera.discovery.huaray', 'camera.discovery.hikvision',
      'camera.bindings.read', 'camera.bindings.write', 'camera.trigger-and-preview', 'camera.soft-trigger-capture',
      'trigger-input.diagnostics.read', 'trigger-input.serial-ports.read', 'trigger-input.serial-test',
      'trigger-input.enter-learn', 'camera.preview.start', 'camera.preview.frame', 'camera.preview.stop'
    ],
    sensitiveFields: ['ipAddress', 'serialNumber', 'sessionId'], semantics: SETTINGS_SEMANTICS.cameraMutation
  }),
  section({
    section: 'station', authority: 'station-communication', routePermission: 'settings-route', readPermission: 'admin', writePermission: 'admin',
    endpointIds: ['station.settings.read', 'station.settings.write', 'station.token'],
    sensitiveFields: ['token', 'last4', 'mask', 'lanHost'], semantics: SETTINGS_SEMANTICS.stationRestart
  }),
  section({
    section: 'ai-model', authority: 'ai-model-store', routePermission: 'settings-route', readPermission: 'authenticated', writePermission: 'admin',
    endpointIds: [
      'ai.models.read', 'ai.models.create', 'ai.models.update', 'ai.models.delete', 'ai.models.activate',
      'ai.models.default-planner', 'ai.models.default-shadow-eval', 'ai.models.test', 'ai.reasoning-support'
    ],
    sensitiveFields: ['apiKey', 'apiKeyMasked', 'extraHeaders', 'extraQuery', 'extraBody'], semantics: SETTINGS_SEMANTICS.aiModelMutation
  }),
  section({
    section: 'database', authority: 'database-maintenance', routePermission: 'settings-route', readPermission: 'admin', writePermission: 'admin',
    endpointIds: ['settings.database.status.read', 'settings.database.backup'],
    sensitiveFields: ['backupPath'], semantics: SETTINGS_SEMANTICS.databaseMaintenance
  })
]);

export function findSettingsEndpoint(id: string): SettingsEndpointContract | null {
  return SETTINGS_ENDPOINT_MATRIX.find(item => item.id === id) ?? null;
}

export function evaluateSettingsEndpointAccess(
  endpointId: string,
  sectionName: SettingsSection,
  role: SettingsRole
): SettingsEndpointAccess {
  const endpoint = findSettingsEndpoint(endpointId);
  if (!endpoint) {
    return Object.freeze({
      allowed: false,
      endpointId,
      role,
      endpoint: null,
      reason: isSettingsEndpointExcluded(endpointId) ? 'excluded-endpoint' : 'unknown-endpoint'
    });
  }

  const sectionMatches = endpoint.section === sectionName ||
    (endpoint.section === 'generic' && endpoint.genericScopes.some(scope => scope === sectionName));
  if (!sectionMatches) {
    return Object.freeze({ allowed: false, endpointId, role, endpoint, reason: 'section-mismatch' });
  }

  const normalizedRole = typeof role === 'string' ? role.trim() : '';
  switch (endpoint.uiPermission) {
    case 'engineer-or-admin':
      return Object.freeze({
        allowed: normalizedRole === 'Admin' || normalizedRole === 'Engineer',
        endpointId,
        role,
        endpoint,
        reason: normalizedRole === 'Admin' || normalizedRole === 'Engineer'
          ? 'allowed'
          : 'engineer-or-admin-required'
      });
    case 'admin':
      return Object.freeze({
        allowed: normalizedRole === 'Admin',
        endpointId,
        role,
        endpoint,
        reason: normalizedRole === 'Admin' ? 'allowed' : 'admin-required'
      });
    case 'settings-route':
      return Object.freeze({ allowed: false, endpointId, role, endpoint, reason: 'route-only' });
    case 'settings-authenticated':
      return Object.freeze({
        allowed: normalizedRole === 'Admin' || normalizedRole === 'Engineer',
        endpointId,
        role,
        endpoint,
        reason: normalizedRole === 'Admin' || normalizedRole === 'Engineer'
          ? 'allowed'
          : 'engineer-or-admin-required'
      });
    case 'excluded':
      return Object.freeze({ allowed: false, endpointId, role, endpoint, reason: 'excluded-endpoint' });
  }
}

export function findSettingsSection(sectionName: SettingsSection): SettingsSectionContract {
  const contract = SETTINGS_SECTION_CONTRACTS.find(item => item.section === sectionName);
  if (!contract) throw new Error(`Settings section contract is missing: ${sectionName}`);
  return contract;
}

export function buildGenericSectionWritePayload(
  sectionName: GenericSettingsSection,
  value: Readonly<Record<string, unknown>>
): Readonly<Record<string, unknown>> {
  return Object.freeze({ saveScope: sectionName, [sectionName]: Object.freeze({ ...value }) });
}

export class SettingsContractDecodeError extends TypeError {
  readonly path: string;
  readonly expected: string;

  constructor(path: string, expected: string) {
    super('设置服务返回的数据格式不符合要求，请刷新后重试。');
    this.name = 'SettingsContractDecodeError';
    this.path = path;
    this.expected = expected;
  }
}
