import { describe, expect, it } from 'vitest';
import {
  decodeAiModelsProjectionV1,
  decodeSettingsErrorPayloadV1,
  decodeSettingsProjectionV1,
  decodeSettingsThemeWriteResponseV1,
  decodeSettingsWriteResponseV1,
  decodeStationCommunicationProjectionV1,
  decodeStationTokenOperationV1,
  SettingsContractDecodeError
} from '@/capabilities/settings';

function fullSettings(overrides: Record<string, unknown> = {}) {
  return {
    revision: 9,
    general: { softwareTitle: 'ClearVision 检测站', theme: 'dark', autoStart: false },
    storage: { imageSavePath: 'D:/VisionData/Images', savePolicy: 'NgOnly', retentionDays: 30, minFreeSpaceGb: 5 },
    runtime: {
      autoRun: false,
      stopOnConsecutiveNg: 3,
      missingMaterialTimeoutSeconds: 120,
      applyProtectionRules: true,
      runtimePreviewPilot: { mode: 'metadata_only', denyImageBytes: true }
    },
    security: { passwordMinLength: 8, sessionTimeoutMinutes: 30, loginFailureLockoutCount: 5 },
    communication: { activeProtocol: 'S7', s7: { ipAddress: '192.168.1.10' } },
    tcpCommunication: { profiles: [] },
    features: { continuousInspection: { enabled: false } },
    cameras: [],
    activeCameraId: '',
    ...overrides
  };
}

function safeSettings() {
  return {
    safeSubset: true,
    revision: 11,
    general: { softwareTitle: 'ClearVision 检测站', theme: 'light' }
  };
}

function stationSettings(overrides: Record<string, unknown> = {}) {
  return {
    success: true,
    message: '读取成功',
    mode: 'LocalLoopback',
    port: 5001,
    lanHost: '127.0.0.1',
    lanAddresses: ['127.0.0.1'],
    localStationSyncEnabled: true,
    token: { hasToken: true, mask: '******', last4: '1234' },
    paths: { studio: 'redacted', localStation: 'redacted' },
    currentRunning: {
      studioEnabled: true,
      studioListenMode: 'LocalLoopback',
      studioPort: 5001,
      studioToken: { hasToken: true, mask: '******', last4: '1234' }
    },
    requiresRestart: { studio: true, localStation: false },
    localStationBaseUrl: 'http://127.0.0.1:5002',
    remoteStationBaseUrl: '',
    localStationHubUrl: 'http://127.0.0.1:5002/hub/station',
    remoteStationHubUrl: '',
    diagnostics: [],
    ...overrides
  };
}

function aiModel(overrides: Record<string, unknown> = {}) {
  return {
    id: 'model_01',
    name: '主模型',
    displayName: '主模型',
    provider: 'OpenAI Compatible',
    model: 'gpt-4o',
    hasApiKey: true,
    apiKeyMasked: '••••••••',
    baseUrl: 'https://api.example.test',
    timeoutMs: 120000,
    isActive: true,
    isEnabled: true,
    protocol: 'openai_compatible',
    wireApi: 'responses',
    authMode: 'bearer',
    authHeaderName: 'Authorization',
    extraHeaders: { Authorization: '<redacted>' },
    extraQuery: { api_key: '<redacted>' },
    extraBody: { token: '<redacted>' },
    roleBindings: ['planner'],
    modelRole: 'planner',
    priority: 1,
    remark: 'fixture',
    createdAt: '2026-07-31T00:00:00Z',
    updatedAt: '2026-07-31T00:00:00Z',
    lastTestStatus: 'ok',
    lastTestAt: '2026-07-31T00:00:00Z',
    lastTestLatencyMs: 42,
    capabilities: { supportsVisionInput: true },
    reasoning: { mode: 'auto', effort: 'medium' },
    reasoningSupport: { familyId: 'openai', allowedModes: ['auto'] },
    ...overrides
  };
}

describe('F07 G1 Settings public decoders', () => {
  it('decodes the current full AppConfig response while dropping non-generic authorities', () => {
    const decoded = decodeSettingsProjectionV1(fullSettings());
    expect(decoded.revision).toBe(9);
    expect(decoded.sections.storage?.retentionDays).toBe(30);
    expect(decoded.sections.runtime?.missingMaterialTimeoutSeconds).toBe(120);
    expect(decoded.ignoredAuthoritySections).toEqual([
      'communication', 'tcpCommunication', 'features', 'cameras', 'activeCameraId'
    ]);
  });

  it('accepts PascalCase aliases and safe subset responses without inventing restricted sections', () => {
    const decoded = decodeSettingsProjectionV1({
      SafeSubset: true,
      Revision: 12,
      General: { SoftwareTitle: 'Studio', Theme: 'LIGHT' }
    });
    expect(decoded.safeSubset).toBe(true);
    expect(decoded.sections.general.theme).toBe('light');
    expect(decoded.sections.storage).toBeNull();
    expect(() => decodeSettingsProjectionV1({ ...safeSettings(), storage: {} })).toThrow(SettingsContractDecodeError);
  });

  it('fails closed on unknown fields and raw sensitive fields', () => {
    expect(() => decodeSettingsProjectionV1({ ...fullSettings(), privateTrace: 'hidden' }))
      .toThrow(SettingsContractDecodeError);
    expect(() => decodeSettingsProjectionV1({ ...fullSettings(), security: {
      ...fullSettings().security, password: 'raw'
    } })).toThrow(SettingsContractDecodeError);
    expect(() => decodeSettingsWriteResponseV1({ message: 'saved', config: fullSettings(), apiKey: 'raw' }))
      .toThrow(SettingsContractDecodeError);
  });

  it('decodes saved/effective theme semantics without accepting arbitrary response fields', () => {
    const result = decodeSettingsThemeWriteResponseV1({ message: '主题已保存', theme: 'dark' });
    expect(result).toMatchObject({ theme: 'dark', semantics: { persistence: 'persisted' } });
    expect(() => decodeSettingsThemeWriteResponseV1({ message: 'ok', theme: 'dark', config: {} }))
      .toThrow(SettingsContractDecodeError);
  });

  it('requires station read projections to be masked and keeps raw token separate', () => {
    const decoded = decodeStationCommunicationProjectionV1(stationSettings());
    expect(decoded.token).toEqual({ hasToken: true, mask: '******', last4: '1234' });
    expect(() => decodeStationCommunicationProjectionV1(stationSettings({
      token: '123456'
    }))).toThrow(SettingsContractDecodeError);
    const operation = decodeStationTokenOperationV1({
      success: true,
      operation: 'reveal',
      token: '123456',
      tokenInfo: { hasToken: true, mask: '******', last4: '1234' },
      settings: null,
      message: 'revealed',
      errors: []
    });
    expect(operation.token).toBe('123456');
  });

  it('accepts redacted AI model projections and rejects a raw API key', () => {
    const decoded = decodeAiModelsProjectionV1([aiModel()]);
    expect(decoded.safeSubset).toBe(false);
    const nullableMetadata = decodeAiModelsProjectionV1([aiModel({ timeoutMs: null, priority: null, lastTestLatencyMs: null })]);
    expect(nullableMetadata.items[0]).toMatchObject({ timeoutMs: null, priority: null, lastTestLatencyMs: null });
    expect(decoded.items[0]).toMatchObject({ id: 'model_01', hasApiKey: true, apiKeyMasked: '••••••••' });
    expect(() => decodeAiModelsProjectionV1([aiModel({ apiKey: 'sk-raw' })]))
      .toThrow(SettingsContractDecodeError);
    expect(() => decodeAiModelsProjectionV1([aiModel({ extraHeaders: { Authorization: 'Bearer raw' } })]))
      .toThrow(SettingsContractDecodeError);
  });

  it('decodes the backend safe AI projection and rejects mixed projection shapes', () => {
    const decoded = decodeAiModelsProjectionV1([{
      id: 'safe-model',
      displayName: 'Safe Model',
      provider: 'OpenAI Compatible',
      model: 'gpt-4o',
      modelRole: 'planner',
      isEnabled: true,
      isActive: false,
      capabilities: { supportsVisionInput: true }
    }]);
    expect(decoded.safeSubset).toBe(true);
    expect(decoded.items[0]).toMatchObject({ id: 'safe-model', displayName: 'Safe Model' });
    expect(() => decodeAiModelsProjectionV1([aiModel(), {
      id: 'safe-model',
      displayName: 'Safe Model',
      provider: 'OpenAI Compatible',
      model: 'gpt-4o',
      modelRole: null,
      isEnabled: true,
      isActive: false,
      capabilities: null
    }])).toThrow(SettingsContractDecodeError);
  });

  it('normalizes safe error payloads and refuses private error keys', () => {
    const error = decodeSettingsErrorPayloadV1({
      code: 'AdminRequired', policy: 'RequireAdmin', message: '需要管理员权限。', errors: []
    }, '$.error', 'forbidden');
    expect(error).toMatchObject({ code: 'forbidden', policy: 'RequireAdmin' });
    expect(() => decodeSettingsErrorPayloadV1({ error: 'forbidden', token: 'raw' })).toThrow(SettingsContractDecodeError);
    expect(() => decodeSettingsErrorPayloadV1({ error: 'failed', backupPath: 'C:/private/backup.cvdbbak' }))
      .toThrow(SettingsContractDecodeError);
  });

  it('drops database paths while keeping status and backup result metadata', async () => {
    const { decodeSettingsDatabaseBackupProjectionV1, decodeSettingsDatabaseStatusProjectionV1 } = await import('@/capabilities/settings');
    const status = decodeSettingsDatabaseStatusProjectionV1({
      databasePath: 'C:/private/vision.db',
      exists: true,
      state: 'Healthy',
      schemaVersion: 6,
      currentSchemaVersion: 6,
      appliedMigrations: ['001'],
      pendingMigrations: [],
      missingSchemaItems: [],
      integrityCheck: 'ok',
      foreignKeyViolationCount: 0,
      rowCounts: { projects: 2 },
      issues: [],
      databaseSizeBytes: 1024,
      walSizeBytes: 0,
      backupRootDirectory: 'C:/private/backups',
      packageRootDirectory: 'C:/private/packages',
      packageFileCount: 2
    });
    expect(status).not.toHaveProperty('databasePath');
    expect(status).toMatchObject({ state: 'Healthy', schemaVersion: 6, rowCounts: { projects: 2 } });

    const backup = decodeSettingsDatabaseBackupProjectionV1({
      backupPath: 'C:/private/backups/manual.cvdbbak',
      createdAtUtc: '2026-08-01T00:00:00Z',
      sizeBytes: 2048,
      databaseSizeBytes: 1024,
      packageFileCount: 2,
      packageBytes: 256
    });
    expect(backup).not.toHaveProperty('backupPath');
    expect(backup).toMatchObject({ sizeBytes: 2048, packageFileCount: 2 });
  });

  it('decodes user projections without accepting password fields', async () => {
    const { decodeSettingsUsersProjectionV1 } = await import('@/capabilities/settings');
    const decoded = decodeSettingsUsersProjectionV1([{
      id: 'user-1',
      username: 'engineer',
      displayName: 'Engineer',
      role: 1,
      isActive: true,
      lastLoginAt: null
    }]);
    expect(decoded.items[0]).toMatchObject({ id: 'user-1', role: 'Engineer' });
    expect(() => decodeSettingsUsersProjectionV1([{
      id: 'user-1', username: 'engineer', displayName: 'Engineer', role: 1, isActive: true,
      lastLoginAt: null, passwordHash: 'raw'
    }])).toThrow(SettingsContractDecodeError);
  });
});
