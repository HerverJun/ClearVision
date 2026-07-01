import { describe, expect, it } from 'vitest';
import { readStartupConfig } from '@/startup/startupConfig';

describe('readStartupConfig', () => {
  it('reads the host-injected startup object as the configuration authority', () => {
    const startup = Object.freeze({
      workspaceV2Enabled: true,
      apiBaseUrl: 'http://localhost:5000/api',
      hostKind: 'desktop-webview2',
      frontendV2BasePath: '/v2'
    });
    const windowRef = {
      __CLEARVISION_STARTUP__: startup,
      __API_BASE_URL__: 'http://localhost:5000/api'
    } as Window;

    expect(readStartupConfig(windowRef)).toBe(startup);
  });

  it('defaults to disabled when the host did not load the V2 startup object', () => {
    const windowRef = {
      __API_BASE_URL__: 'http://localhost:5000/api'
    } as Window;

    expect(readStartupConfig(windowRef)).toEqual({
      workspaceV2Enabled: false,
      apiBaseUrl: 'http://localhost:5000/api',
      hostKind: 'desktop-webview2',
      frontendV2BasePath: '/v2'
    });
  });
});
