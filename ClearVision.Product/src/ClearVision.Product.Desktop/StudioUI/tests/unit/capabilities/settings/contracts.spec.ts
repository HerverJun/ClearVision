import { describe, expect, it } from 'vitest';
import {
  buildGenericSectionWritePayload,
  evaluateSettingsEndpointAccess,
  evaluateSettingsRouteAccess,
  findSettingsEndpoint,
  findSettingsSection,
  GENERIC_SETTINGS_SECTIONS,
  SETTINGS_ENDPOINT_MATRIX,
  SETTINGS_EXCLUDED_ENDPOINTS,
  SETTINGS_SECTION_CONTRACTS,
  SETTINGS_SEMANTICS
} from '@/capabilities/settings';

describe('F07 G1 Settings contract matrix', () => {
  it('freezes the G0 route roles without adding a backend permission', () => {
    expect(evaluateSettingsRouteAccess('Admin')).toMatchObject({ allowed: true, reason: 'allowed' });
    expect(evaluateSettingsRouteAccess('Engineer')).toMatchObject({ allowed: true, reason: 'allowed' });
    expect(evaluateSettingsRouteAccess('Operator')).toMatchObject({
      allowed: false, reason: 'operator-forbidden'
    });
    expect(evaluateSettingsRouteAccess(null).reason).toBe('authenticated-role-required');
  });

  it('keeps generic scope to four sections and routes other authorities to dedicated endpoints', () => {
    expect(GENERIC_SETTINGS_SECTIONS).toEqual(['general', 'storage', 'runtime', 'security']);
    expect(findSettingsSection('plc').genericScope).toBeNull();
    expect(findSettingsSection('camera').endpointIds).toContain('camera.bindings.write');
    expect(findSettingsEndpoint('plc.settings.write')?.genericScope).toBeNull();
    expect(findSettingsEndpoint('settings.write')?.serverPermission).toBe('admin');
    expect(findSettingsEndpoint('settings.write')?.section).toBe('generic');
    expect(findSettingsEndpoint('settings.write')?.genericScopes).toEqual(GENERIC_SETTINGS_SECTIONS);
    expect(SETTINGS_SECTION_CONTRACTS).toHaveLength(10);
    expect(SETTINGS_ENDPOINT_MATRIX.some(item => item.path.includes('communication') && item.genericScope !== null))
      .toBe(false);
  });

  it('keeps saved, effective, restart and conflict semantics explicit', () => {
    expect(SETTINGS_SEMANTICS.cameraMutation).toMatchObject({
      persistence: 'persisted', effective: 'immediate-projection', conflict: '409-fail-closed'
    });
    expect(SETTINGS_SEMANTICS.stationRestart).toMatchObject({
      persistence: 'persisted', effective: 'restart-dependent', restart: 'studio-and-local-station'
    });
    expect(SETTINGS_SEMANTICS.databaseMaintenance.persistence).toBe('runtime-only');
    expect(SETTINGS_EXCLUDED_ENDPOINTS).toEqual(expect.arrayContaining([
      'settings/import', 'settings/export', 'settings/database/restore', 'settings/runtime-preview-pilot/**'
    ]));
  });

  it('requires a concrete endpoint contract and applies its UI permission', () => {
    expect(evaluateSettingsEndpointAccess('plc.test-connection', 'plc', 'Engineer'))
      .toMatchObject({ allowed: true, reason: 'allowed', endpoint: { id: 'plc.test-connection' } });
    expect(evaluateSettingsEndpointAccess('tcp.runtime', 'tcp', 'Engineer'))
      .toMatchObject({ allowed: true, reason: 'allowed' });
    expect(evaluateSettingsEndpointAccess('camera.trigger-and-preview', 'camera', 'Engineer'))
      .toMatchObject({ allowed: true, reason: 'allowed' });
    expect(evaluateSettingsEndpointAccess('plc.settings.write', 'plc', 'Engineer'))
      .toMatchObject({ allowed: false, reason: 'admin-required' });
    expect(evaluateSettingsEndpointAccess('ai.reasoning-support', 'ai-model', 'Engineer'))
      .toMatchObject({ allowed: false, reason: 'route-only' });
    expect(evaluateSettingsEndpointAccess('settings.write', 'storage', 'Admin'))
      .toMatchObject({ allowed: true, reason: 'allowed' });
    expect(evaluateSettingsEndpointAccess('plc.settings.write', 'tcp', 'Admin'))
      .toMatchObject({ allowed: false, reason: 'section-mismatch' });
    expect(evaluateSettingsEndpointAccess('unknown.endpoint', 'plc', 'Admin'))
      .toMatchObject({ allowed: false, reason: 'unknown-endpoint' });
    expect(evaluateSettingsEndpointAccess('settings/import', 'general', 'Admin'))
      .toMatchObject({ allowed: false, reason: 'excluded-endpoint' });
    expect(evaluateSettingsEndpointAccess('plc.test-connection', 'plc', 'Operator'))
      .toMatchObject({ allowed: false, reason: 'engineer-or-admin-required' });
  });

  it('builds a scoped generic payload without copying unrelated authority sections', () => {
    const payload = buildGenericSectionWritePayload('general', { softwareTitle: 'Studio' });
    expect(payload).toEqual({ saveScope: 'general', general: { softwareTitle: 'Studio' } });
    expect(Object.keys(payload)).toEqual(['saveScope', 'general']);
    expect(Object.isFrozen(payload)).toBe(true);
    expect(Object.isFrozen(payload.general)).toBe(true);
  });
});
