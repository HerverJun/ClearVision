import { expect, test } from '@playwright/test';
import { installF07DeviceFixture, type F07DeviceAudit } from './f07-device-fixture';

async function openSettings(page: Parameters<typeof installF07DeviceFixture>[0], audit: F07DeviceAudit): Promise<void> {
  await page.goto('/studio/index.html#/settings');
  await expect(page.locator('[data-capability="settings"][data-settings-phase="ready"]')).toBeVisible();
  expect(audit.audit.some(entry => entry.path === '/api/settings')).toBe(true);
}

function expectNoFormalInspection(audit: F07DeviceAudit): void {
  expect(audit.audit.some(entry => entry.path.startsWith('/api/inspection'))).toBe(false);
  expect(audit.audit.some(entry => entry.path.includes('/api/projects'))).toBe(false);
}

test('G5 Admin PLC keeps protocol drafts isolated and separates save, mapping, and connection test', async ({ page }) => {
  const fixture = await installF07DeviceFixture(page, 'Admin');
  await openSettings(page, fixture);

  await page.locator('[data-settings-group="plc"]').click();
  const plc = page.locator('[data-settings-section="plc"]');
  await expect(plc).toHaveCount(1);
  await expect(plc.locator('input[name="plcIpAddress"]')).toHaveValue('127.0.0.1');

  await plc.locator('select[name="plcProtocol"]').selectOption('MC');
  await plc.locator('input[name="plcIpAddress"]').fill('10.0.0.20');
  await plc.locator('select[name="plcProtocol"]').selectOption('S7');
  await expect(plc.locator('input[name="plcIpAddress"]')).toHaveValue('127.0.0.1');
  await plc.locator('select[name="plcProtocol"]').selectOption('MC');
  await expect(plc.locator('input[name="plcIpAddress"]')).toHaveValue('10.0.0.20');

  await plc.locator('select[name="plcProtocol"]').selectOption('S7');
  await plc.locator('input[name="plcIpAddress"]').fill('10.0.0.99');
  const saveSettings = plc.locator('[data-plc-action="save-settings"]');
  await expect(saveSettings).toHaveCount(1);
  await saveSettings.click();
  await expect(plc.locator('[data-settings-device-feedback="plc"]')).toContainText('校验');
  expect(fixture.audit.filter(entry => entry.method === 'PUT' && entry.path === '/api/plc/settings')).toHaveLength(1);

  await plc.locator('input[name="plcIpAddress"]').fill('10.0.0.21');
  await saveSettings.click();
  await expect(plc.locator('[data-settings-device-feedback="plc"]')).toBeVisible();
  expect(fixture.audit.filter(entry => entry.method === 'PUT' && entry.path === '/api/plc/settings')).toHaveLength(2);

  const mappingInputs = plc.locator('table tbody input');
  const mappingInputCount = await mappingInputs.count();
  expect(mappingInputCount).toBeGreaterThanOrEqual(1);
  await mappingInputs.nth(0).fill('FixtureReady');
  await plc.locator('[data-plc-action="save-mappings"]').click();
  expect(fixture.audit.filter(entry => entry.method === 'PUT' && entry.path === '/api/plc/mappings')).toHaveLength(1);

  await plc.locator('[data-plc-action="test-connection"]').click();
  await expect(plc.locator('[data-settings-device-feedback="plc"]')).toBeVisible();
  expect(fixture.audit.filter(entry => entry.method === 'POST' && entry.path === '/api/plc/test-connection')).toHaveLength(1);
  expect(fixture.audit.some(entry => entry.method === 'POST' && entry.path.includes('/api/tcp'))).toBe(false);
});

test('G5 TCP Admin saves profiles without auto-connect, then operates the runtime and bounded log', async ({ page }) => {
  const fixture = await installF07DeviceFixture(page, 'Admin');
  await openSettings(page, fixture);

  await page.locator('[data-settings-group="tcp"]').click();
  const tcp = page.locator('[data-settings-section="tcp"]');
  await expect(tcp).toHaveCount(1);
  await expect(tcp.locator('input[name="tcpProfileName"]')).toHaveValue('Fixture Loopback');

  await tcp.locator('input[name="tcpProfileName"]').fill('Fixture Loopback Saved');
  await tcp.locator('[data-tcp-action="save-profiles"]').click();
  expect(fixture.audit.filter(entry => entry.method === 'PUT' && entry.path === '/api/tcp/profiles')).toHaveLength(1);
  expect(fixture.audit.filter(entry => entry.method === 'POST' && entry.path.includes('/connect'))).toHaveLength(0);

  await tcp.locator('[data-tcp-action="connect"]').click();
  await expect.poll(() => fixture.state.tcpConnectCount).toBe(1);
  await expect(tcp).toContainText('OK');
  await tcp.locator('textarea').fill('fixture-payload');
  await tcp.locator('[data-tcp-action="send"]').click();
  await expect.poll(() => fixture.state.tcpSendCount).toBe(1);
  await expect(tcp).toContainText('fixture-response');
  expect(fixture.audit.some(entry => entry.method === 'POST' && entry.path.endsWith('/send'))).toBe(true);

  await tcp.locator('[data-tcp-action="disconnect"]').click();
  expect(fixture.audit.some(entry => entry.method === 'POST' && entry.path.endsWith('/disconnect'))).toBe(true);
  expect(fixture.audit.some(entry => entry.path.includes('/api/settings'))).toBe(true);
});

test('G6 Camera discovery, trigger inputs, soft capture, preview, 409 fail-closed, and leave cleanup', async ({ page }) => {
  const fixture = await installF07DeviceFixture(page, 'Admin');
  await openSettings(page, fixture);

  await page.locator('[data-settings-group="camera"]').click();
  const camera = page.locator('[data-settings-section="camera"]');
  await expect(camera).toHaveCount(1);
  await camera.locator('[data-camera-discovery="huaray"]').click();
  await expect(camera.locator('[data-camera-section="discovery"]')).toContainText('Huaray Fixture');
  await camera.locator('[data-camera-discovery="hikvision"]').click();
  await expect(camera.locator('[data-camera-section="discovery"]')).toContainText('Hikvision Fixture');
  await expect(camera.locator('input[name="cameraExposure"]')).toHaveValue('5000');
  await expect(camera.locator('select[name="cameraTriggerMode"]')).toHaveValue('Software');
  await expect(camera.locator('select[name="cameraSoftwareTriggerSource"]')).toHaveValue('Manual');

  await camera.locator('[data-camera-action="soft-capture"]').click();
  await expect(camera.locator('[data-camera-section="preview"] img')).toHaveCount(1);
  await expect.poll(() => fixture.audit.filter(entry => entry.path === '/api/cameras/soft-trigger-capture').length).toBe(1);
  expectNoFormalInspection(fixture);

  await camera.locator('[data-camera-action="toggle-preview"]').click();
  await expect.poll(() => fixture.state.previewStartCount).toBe(1);
  await expect.poll(() => fixture.state.previewFrameCount).toBeGreaterThan(0);
  await expect(camera.locator('[data-camera-section="preview"] img')).toHaveCount(1);

  await camera.locator('input[name="cameraExposure"]').fill('7000');
  await camera.locator('[data-camera-action="save-bindings"]').click();
  await expect(camera.locator('[data-settings-device-feedback="camera"]')).toBeVisible();
  await expect(camera.locator('[data-camera-action="toggle-preview"]')).toContainText('停止');
  expect(fixture.state.previewStopCount).toBe(0);

  await camera.locator('[data-camera-action="toggle-preview"]').click();
  await expect.poll(() => fixture.state.previewStopCount).toBe(1);

  await camera.locator('[data-camera-action="toggle-preview"]').click();
  await expect.poll(() => fixture.state.previewStartCount).toBe(2);
  await page.locator('[data-settings-group="overview"]').click();
  await expect(page.locator('[data-settings-section="camera"]')).toHaveCount(0);
  await expect.poll(() => fixture.state.previewStopCount).toBe(2);
  expectNoFormalInspection(fixture);
});

test('G5/G6 Engineer gets diagnostics/runtime access while PLC/TCP mutation controls stay Admin-only', async ({ page }) => {
  const fixture = await installF07DeviceFixture(page, 'Engineer');
  await openSettings(page, fixture);

  await page.locator('[data-settings-group="plc"]').click();
  const plc = page.locator('[data-settings-section="plc"]');
  await expect(plc.locator('[data-plc-action="test-connection"]')).toHaveCount(1);
  await expect(plc.locator('[data-plc-action="save-settings"]')).toHaveCount(0);
  await plc.locator('[data-plc-action="test-connection"]').click();
  expect(fixture.audit.some(entry => entry.method === 'POST' && entry.path === '/api/plc/test-connection')).toBe(true);

  await page.locator('[data-settings-group="tcp"]').click();
  const tcp = page.locator('[data-settings-section="tcp"]');
  await expect(tcp.locator('[data-tcp-action="save-profiles"]')).toHaveCount(0);
  await tcp.locator('[data-tcp-action="connect"]').click();
  expect(fixture.audit.some(entry => entry.method === 'POST' && entry.path.endsWith('/connect'))).toBe(true);
});

test('G5/G6 Operator remains forbidden before SettingsOwner or device endpoints mount', async ({ page }) => {
  const fixture = await installF07DeviceFixture(page, 'Operator');
  await page.goto('/studio/index.html#/settings');

  await expect(page.locator('[data-studio-page="forbidden"]')).toBeVisible();
  expect(fixture.audit.some(entry => entry.path === '/api/settings')).toBe(false);
  expect(fixture.audit.some(entry => /^\/api\/(plc|tcp|cameras|trigger-input)/.test(entry.path))).toBe(false);
});
