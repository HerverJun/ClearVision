import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, type Locator, type Page, test } from '@playwright/test';
import {
  installOptionDG2DeterministicFixture,
  optionDG2DeterministicFixture,
  type OptionDG2FixtureAudit,
  type OptionDG2FixtureMode
} from './option-d-g2-deterministic-fixture';

type CapturePhase = 'reference' | 'candidate';

interface VisualComparison {
  readonly changedPixels: number;
  readonly changedPixelRatio: number;
  readonly maxChannelDelta: number;
  readonly width: number;
  readonly height: number;
}

interface BoxEvidence {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly right: number;
  readonly bottom: number;
}

interface GeometryEvidence {
  readonly viewport: { readonly width: number; readonly height: number };
  readonly horizontalOverflow: number;
  readonly verticalOverflow: number;
  readonly stageHorizontalOverflow: number;
  readonly stageVerticalOverflow: number;
  readonly mainCount: number;
  readonly frame: BoxEvidence;
  readonly masthead: BoxEvidence;
  readonly contentAnchor: BoxEvidence;
  readonly primaryControl: BoxEvidence;
  readonly frameMastheadOverlap: number;
}

interface CaptureEvidence {
  readonly id: string;
  readonly phase: CapturePhase;
  readonly screenshot: string;
  readonly sha256: string;
  readonly width: number;
  readonly height: number;
  readonly cssViewport: { readonly width: number; readonly height: number };
  readonly geometry: GeometryEvidence;
  readonly functionalAssertions: 'PASS';
  readonly requestAudit: OptionDG2FixtureAudit['requests'];
  readonly runtimeErrors: readonly string[];
  readonly fontFamily: string;
  readonly comparison?: VisualComparison;
  readonly referenceSha256?: string;
  readonly diff?: string;
  readonly diffSha256?: string;
  readonly overlay?: string;
  readonly overlaySha256?: string;
}

interface MasterMeasurement {
  readonly id: 'D01' | 'D24';
  readonly verticalEdges: readonly { readonly id: string; readonly cssPixel: number }[];
  readonly horizontalEdges: readonly { readonly id: string; readonly cssPixel: number }[];
}

const requestedVisualPhase = process.env.CV_OPTION_D_G2_VISUAL_PHASE?.trim();
const gateInvocationId = process.env.CV_OPTION_D_G2_GATE_INVOCATION_ID?.trim();
if (requestedVisualPhase !== 'reference' && requestedVisualPhase !== 'candidate') {
  throw new Error(
    'CV_OPTION_D_G2_VISUAL_PHASE must be reference or candidate. Use the dedicated Option D G2 gate.'
  );
}
if (!gateInvocationId) {
  throw new Error('CV_OPTION_D_G2_GATE_INVOCATION_ID is required. Use the dedicated Option D G2 gate.');
}
const visualPhase = requestedVisualPhase as CapturePhase;
const evidenceRoot = resolve(process.cwd(), '../../../.tmp/studio-ui-next/option-d-g2/visual');
const metricsPath = resolve(
  process.cwd(),
  '../../../.tmp/studio-ui-next/option-d-g2/master-measurements.json'
);
const masterManifest = JSON.parse(readFileSync(metricsPath, 'utf8')) as {
  readonly assertionResult: string;
  readonly measurements: readonly MasterMeasurement[];
};
if (masterManifest.assertionResult !== 'PASS') {
  throw new Error(`Option D G2 master measurements are not asserted: ${metricsPath}`);
}
const maxChangedPixelRatio = 0.01;
const frozenReferenceSha256: Readonly<Record<string, string>> = Object.freeze({
  'd01-login-1920x1080': 'e91dbd8f3fe24f8eeddfd85195e52a5fe6af5660fbb02d6a943364c7960f7497',
  'd24-forbidden-1920x1080': 'f1b68e41e2e24eed3206b66585c27d07359fa78eb7a908bf1ed1e1127e2acbf4',
  'd01-login-1536x864': '294ecaec97fcd9e0eb7e185491e59b30f633ca44264e7f5bc4e7241dcc4b865e',
  'd24-forbidden-1536x864': '175ff491ffd616e573f6d1770616eabc46747360f54fc22387ca15a3c360a311',
  'd01-login-1366x768': '600bdf728c3d0dd412d15d9ec2b0e2deee906e1be9af33d106ee24ce0c617a1f',
  'd24-forbidden-1366x768': '452a1a083457f165f780577f28bb1a91a22a1944cece0e380ba20396f0c4ee9f'
});
const captureCases = Object.freeze([
  { id: 'd01-login-1920x1080', screen: 'D01', mode: 'login', width: 1920, height: 1080 },
  { id: 'd24-forbidden-1920x1080', screen: 'D24', mode: 'forbidden', width: 1920, height: 1080 },
  { id: 'd01-login-1536x864', screen: 'D01', mode: 'login', width: 1536, height: 864 },
  { id: 'd24-forbidden-1536x864', screen: 'D24', mode: 'forbidden', width: 1536, height: 864 },
  { id: 'd01-login-1366x768', screen: 'D01', mode: 'login', width: 1366, height: 768 },
  { id: 'd24-forbidden-1366x768', screen: 'D24', mode: 'forbidden', width: 1366, height: 768 }
] as const);

test.use({ deviceScaleFactor: 2 });

function sha256(buffer: Buffer): string {
  return createHash('sha256').update(buffer).digest('hex');
}

function pngDimensions(buffer: Buffer): { width: number; height: number } {
  if (buffer.length < 24 || buffer.toString('hex', 0, 8) !== '89504e470d0a1a0a') {
    throw new Error('Visual evidence is not a valid PNG.');
  }
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

function writeDataUrl(path: string, dataUrl: string): Buffer {
  const comma = dataUrl.indexOf(',');
  if (comma < 0) throw new Error('Canvas evidence data URL is invalid.');
  const buffer = Buffer.from(dataUrl.slice(comma + 1), 'base64');
  writeFileSync(path, buffer);
  return buffer;
}

async function boxOf(locator: Locator): Promise<BoxEvidence> {
  const box = await locator.boundingBox();
  expect(box).not.toBeNull();
  return {
    x: box!.x,
    y: box!.y,
    width: box!.width,
    height: box!.height,
    right: box!.x + box!.width,
    bottom: box!.y + box!.height
  };
}

async function bootScreen(
  page: Page,
  screen: 'D01' | 'D24',
  mode: OptionDG2FixtureMode
): Promise<{ audit: OptionDG2FixtureAudit; runtimeErrors: string[] }> {
  const runtimeErrors: string[] = [];
  page.on('pageerror', error => runtimeErrors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') runtimeErrors.push(message.text());
  });
  page.on('requestfailed', request => {
    runtimeErrors.push(
      `Request failed: ${request.method()} ${request.url()} (${request.failure()?.errorText ?? 'unknown'})`
    );
  });
  const audit = await installOptionDG2DeterministicFixture(page, mode);
  const route = screen === 'D01'
    ? '/studio/index.html#/login'
    : '/studio/index.html#/projects/11111111-1111-4111-8111-111111111111/workspace';
  await page.goto(route);
  await page.addStyleTag({ content: `
    *, *::before, *::after {
      animation-duration: 0s !important;
      animation-delay: 0s !important;
      transition-duration: 0s !important;
      scroll-behavior: auto !important;
    }
  ` });
  await page.evaluate(async () => {
    await document.fonts.ready;
    await new Promise<void>(resolveFrame => {
      requestAnimationFrame(() => requestAnimationFrame(() => resolveFrame()));
    });
  });
  return { audit, runtimeErrors };
}

async function assertFunctionalContract(page: Page, screen: 'D01' | 'D24'): Promise<void> {
  await expect(page.locator('[data-auth-shell="ready"]')).toBeVisible();
  await expect(page.locator('[data-product-shell]')).toHaveCount(0);
  await expect(page.locator('nav')).toHaveCount(0);
  await expect(page.locator('main')).toHaveCount(1);
  if (screen === 'D01') {
    await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
    await expect(page.getByLabel('用户名')).toHaveCount(1);
    await expect(page.getByLabel('密码', { exact: true })).toHaveCount(1);
    await expect(page.getByRole('checkbox', { name: '记住账号' })).toHaveCount(1);
    await expect(page.getByRole('button', { name: '显示登录密码' })).toHaveCount(1);
    await expect(page.getByRole('button', { name: '登录', exact: true })).toHaveCount(1);
    await expect(page.locator('a')).toHaveCount(0);
    await expect(page.locator('input')).toHaveCount(3);
    await expect(page.locator('button')).toHaveCount(2);
    await expect(page.locator('body')).not.toContainText(/SSO|注册|找回密码|验证码|多因素认证/);
    return;
  }
  await expect(page).toHaveURL(/#\/forbidden$/);
  await expect(page.locator('[data-studio-page="forbidden"]')).toBeVisible();
  await expect(page.getByRole('link', { name: /返回工程库/ })).toHaveCount(1);
  await expect(page.locator('a')).toHaveCount(1);
  await expect(page.locator('button, input, select, textarea')).toHaveCount(0);
  await expect(page.locator('.workspace-page, .workspace-shell, .ai-workbench-page')).toHaveCount(0);
  await expect(page.locator('body')).not.toContainText(/重试|申请权限|修改角色|权限编辑|支持聊天/);
}

async function collectGeometry(page: Page, screen: 'D01' | 'D24'): Promise<GeometryEvidence> {
  const viewport = page.viewportSize();
  if (!viewport) throw new Error('G2 visual viewport is unavailable.');
  const frame = await boxOf(page.locator('.auth-shell__frame'));
  const masthead = await boxOf(page.locator('.auth-shell__masthead'));
  const contentAnchor = await boxOf(screen === 'D01'
    ? page.getByLabel('用户名')
    : page.locator('.auth-form__message'));
  const primaryControl = await boxOf(screen === 'D01'
    ? page.getByRole('button', { name: '登录', exact: true })
    : page.getByRole('link', { name: /返回工程库/ }));
  const documentGeometry = await page.evaluate(() => ({
    horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight,
    stageHorizontalOverflow: Math.max(0,
      (document.querySelector('.auth-shell__stage')?.scrollWidth ?? 0) -
      (document.querySelector('.auth-shell__stage')?.clientWidth ?? 0)),
    stageVerticalOverflow: Math.max(0,
      (document.querySelector('.auth-shell__stage')?.scrollHeight ?? 0) -
      (document.querySelector('.auth-shell__stage')?.clientHeight ?? 0)),
    mainCount: document.querySelectorAll('main').length
  }));
  const horizontalIntersection = Math.max(
    0,
    Math.min(frame.right, masthead.right) - Math.max(frame.x, masthead.x)
  );
  const verticalIntersection = Math.max(
    0,
    Math.min(frame.bottom, masthead.bottom) - Math.max(frame.y, masthead.y)
  );
  const evidence = {
    viewport,
    ...documentGeometry,
    frame,
    masthead,
    contentAnchor,
    primaryControl,
    frameMastheadOverlap: horizontalIntersection > 0 ? verticalIntersection : 0
  };
  expect(evidence.horizontalOverflow).toBe(0);
  expect(evidence.verticalOverflow).toBe(0);
  expect(evidence.stageHorizontalOverflow).toBe(0);
  expect(evidence.stageVerticalOverflow).toBe(0);
  expect(evidence.frameMastheadOverlap).toBe(0);
  expect(evidence.mainCount).toBe(1);
  expect(frame.x).toBeGreaterThanOrEqual(0);
  expect(frame.right).toBeLessThanOrEqual(viewport.width);
  expect(frame.y).toBeGreaterThanOrEqual(0);
  expect(frame.bottom).toBeLessThanOrEqual(viewport.height);
  expect(primaryControl.x).toBeGreaterThanOrEqual(0);
  expect(primaryControl.right).toBeLessThanOrEqual(viewport.width);
  expect(primaryControl.y).toBeGreaterThanOrEqual(0);
  expect(primaryControl.bottom).toBeLessThanOrEqual(viewport.height);
  return evidence;
}

function masterEdge(screen: 'D01' | 'D24', axis: 'verticalEdges' | 'horizontalEdges', id: string): number {
  const measurement = masterManifest.measurements.find(candidate => candidate.id === screen);
  const edge = measurement?.[axis].find(candidate => candidate.id === id);
  if (!edge) throw new Error(`Missing ${screen}/${axis}/${id} master anchor.`);
  return edge.cssPixel;
}

function expectAnchor(actual: number, expected: number, label: string, tolerance = 4): void {
  expect(Math.abs(actual - expected), `${label}: ${actual}px vs ${expected}px`).toBeLessThanOrEqual(tolerance);
}

function assertMasterGeometry(screen: 'D01' | 'D24', geometry: GeometryEvidence): void {
  if (screen === 'D01') {
    expectAnchor(geometry.frame.x, masterEdge('D01', 'verticalEdges', 'auth-frame-start'), 'D01 frame left');
    expectAnchor(geometry.frame.right, masterEdge('D01', 'verticalEdges', 'auth-frame-end'), 'D01 frame right');
    expectAnchor(geometry.frame.y, masterEdge('D01', 'horizontalEdges', 'auth-frame-start'), 'D01 frame top');
    expectAnchor(geometry.frame.bottom, masterEdge('D01', 'horizontalEdges', 'auth-frame-end'), 'D01 frame bottom');
    expectAnchor(geometry.primaryControl.y,
      masterEdge('D01', 'horizontalEdges', 'primary-action-start'), 'D01 primary action top');
    expectAnchor(geometry.contentAnchor.y,
      masterEdge('D01', 'horizontalEdges', 'username-control-start'), 'D01 username control top');
    return;
  }
  expectAnchor(geometry.masthead.bottom, masterEdge('D24', 'horizontalEdges', 'masthead-end'), 'D24 masthead');
  expectAnchor(geometry.frame.x, masterEdge('D24', 'verticalEdges', 'authority-frame-start'), 'D24 frame left');
  expectAnchor(geometry.frame.right, masterEdge('D24', 'verticalEdges', 'authority-frame-end'), 'D24 frame right');
  expectAnchor(geometry.frame.y, masterEdge('D24', 'horizontalEdges', 'authority-frame-start'), 'D24 frame top');
  expectAnchor(geometry.frame.bottom, masterEdge('D24', 'horizontalEdges', 'authority-frame-end'), 'D24 frame bottom');
  expectAnchor(geometry.contentAnchor.y,
    masterEdge('D24', 'horizontalEdges', 'warning-start'), 'D24 warning top');
}

async function compareWholeImage(
  page: Page,
  reference: Buffer,
  candidate: Buffer
): Promise<VisualComparison & { diffDataUrl: string; overlayDataUrl: string }> {
  return page.evaluate(async ({ referenceBase64, candidateBase64 }) => {
    const decode = async (base64: string): Promise<ImageBitmap> => {
      const response = await fetch(`data:image/png;base64,${base64}`);
      return createImageBitmap(await response.blob());
    };
    const [referenceImage, candidateImage] = await Promise.all([
      decode(referenceBase64),
      decode(candidateBase64)
    ]);
    if (referenceImage.width !== candidateImage.width || referenceImage.height !== candidateImage.height) {
      throw new Error('Reference and candidate dimensions differ.');
    }
    const width = referenceImage.width;
    const height = referenceImage.height;
    const source = document.createElement('canvas');
    source.width = width;
    source.height = height;
    const context = source.getContext('2d', { willReadFrequently: true });
    if (!context) throw new Error('2D comparison context is unavailable.');
    context.drawImage(referenceImage, 0, 0);
    const referencePixels = context.getImageData(0, 0, width, height);
    context.clearRect(0, 0, width, height);
    context.drawImage(candidateImage, 0, 0);
    const candidatePixels = context.getImageData(0, 0, width, height);
    const diff = context.createImageData(width, height);
    const overlay = context.createImageData(width, height);
    let changedPixels = 0;
    let maxChannelDelta = 0;
    const perChannelThreshold = 8;
    for (let index = 0; index < referencePixels.data.length; index += 4) {
      const delta = Math.max(
        Math.abs(referencePixels.data[index] - candidatePixels.data[index]),
        Math.abs(referencePixels.data[index + 1] - candidatePixels.data[index + 1]),
        Math.abs(referencePixels.data[index + 2] - candidatePixels.data[index + 2]),
        Math.abs(referencePixels.data[index + 3] - candidatePixels.data[index + 3])
      );
      maxChannelDelta = Math.max(maxChannelDelta, delta);
      const changed = delta > perChannelThreshold;
      if (changed) changedPixels += 1;
      const luma = Math.round(
        referencePixels.data[index] * 0.2126 +
        referencePixels.data[index + 1] * 0.7152 +
        referencePixels.data[index + 2] * 0.0722
      );
      diff.data[index] = changed ? 229 : luma;
      diff.data[index + 1] = changed ? 42 : luma;
      diff.data[index + 2] = changed ? 50 : luma;
      diff.data[index + 3] = 255;
      overlay.data[index] = Math.round((referencePixels.data[index] + candidatePixels.data[index]) / 2);
      overlay.data[index + 1] = Math.round((referencePixels.data[index + 1] + candidatePixels.data[index + 1]) / 2);
      overlay.data[index + 2] = Math.round((referencePixels.data[index + 2] + candidatePixels.data[index + 2]) / 2);
      overlay.data[index + 3] = 255;
    }
    const diffCanvas = document.createElement('canvas');
    diffCanvas.width = width;
    diffCanvas.height = height;
    diffCanvas.getContext('2d')?.putImageData(diff, 0, 0);
    const overlayCanvas = document.createElement('canvas');
    overlayCanvas.width = width;
    overlayCanvas.height = height;
    overlayCanvas.getContext('2d')?.putImageData(overlay, 0, 0);
    referenceImage.close();
    candidateImage.close();
    return {
      changedPixels,
      changedPixelRatio: changedPixels / (width * height),
      maxChannelDelta,
      width,
      height,
      diffDataUrl: diffCanvas.toDataURL('image/png'),
      overlayDataUrl: overlayCanvas.toDataURL('image/png')
    };
  }, {
    referenceBase64: reference.toString('base64'),
    candidateBase64: candidate.toString('base64')
  });
}

async function captureImage(page: Page, id: string): Promise<{
  screenshot: string;
  sha256: string;
  width: number;
  height: number;
  comparison?: VisualComparison;
  referenceSha256?: string;
  diff?: string;
  diffSha256?: string;
  overlay?: string;
  overlaySha256?: string;
}> {
  const phaseDirectory = resolve(evidenceRoot, visualPhase);
  mkdirSync(phaseDirectory, { recursive: true });
  const screenshotPath = resolve(phaseDirectory, `${id}.png`);
  const expectedHash = frozenReferenceSha256[id];
  if (!expectedHash) throw new Error(`Missing G2 reference identity for ${id}.`);
  const pendingReferenceSeal = expectedHash === 'PENDING_G2_REFERENCE_SEAL';
  const existingReference = visualPhase === 'reference' && !pendingReferenceSeal && existsSync(screenshotPath);
  const screenshot = existingReference
    ? readFileSync(screenshotPath)
    : await page.screenshot({
        animations: 'disabled',
        caret: 'hide',
        fullPage: false,
        path: visualPhase === 'candidate' ? screenshotPath : undefined
      });
  const dimensions = pngDimensions(screenshot);
  if (visualPhase === 'reference') {
    if (!pendingReferenceSeal) {
      expect(sha256(screenshot), `G2 reference hash changed for ${id}`).toBe(expectedHash);
    }
    if (pendingReferenceSeal) writeFileSync(screenshotPath, screenshot);
    else if (!existingReference) writeFileSync(screenshotPath, screenshot, { flag: 'wx' });
    return { screenshot: screenshotPath, sha256: sha256(screenshot), ...dimensions };
  }
  if (expectedHash === 'PENDING_G2_REFERENCE_SEAL') {
    throw new Error(`G2 reference hash for ${id} has not been frozen in source.`);
  }
  const referencePath = resolve(evidenceRoot, 'reference', `${id}.png`);
  expect(existsSync(referencePath), `Missing G2 reference ${referencePath}`).toBe(true);
  const reference = readFileSync(referencePath);
  expect(sha256(reference), `Frozen G2 reference hash changed for ${id}`).toBe(expectedHash);
  const comparison = await compareWholeImage(page, reference, screenshot);
  const diffPath = resolve(phaseDirectory, `${id}.diff.png`);
  const overlayPath = resolve(phaseDirectory, `${id}.overlay.png`);
  const diff = writeDataUrl(diffPath, comparison.diffDataUrl);
  const overlay = writeDataUrl(overlayPath, comparison.overlayDataUrl);
  expect(comparison.changedPixelRatio, `${id} exceeds the G2 whole-image threshold`)
    .toBeLessThanOrEqual(maxChangedPixelRatio);
  return {
    screenshot: screenshotPath,
    sha256: sha256(screenshot),
    ...dimensions,
    comparison: {
      changedPixels: comparison.changedPixels,
      changedPixelRatio: comparison.changedPixelRatio,
      maxChannelDelta: comparison.maxChannelDelta,
      width: comparison.width,
      height: comparison.height
    },
    referenceSha256: sha256(reference),
    diff: diffPath,
    diffSha256: sha256(diff),
    overlay: overlayPath,
    overlaySha256: sha256(overlay)
  };
}

test.describe('Option D G2 Shell and Auth visual-functional evidence', () => {
  test.describe.configure({ mode: 'serial' });
  const captures: CaptureEvidence[] = [];

  test.afterAll(() => {
    if (captures.length !== captureCases.length) {
      throw new Error(`G2 ${visualPhase} captured ${captures.length}/${captureCases.length} required images.`);
    }
    const actualIds = captures.map(capture => capture.id).sort();
    expect(actualIds).toEqual(Object.keys(frozenReferenceSha256).sort());
    mkdirSync(evidenceRoot, { recursive: true });
    writeFileSync(resolve(evidenceRoot, `${visualPhase}.json`), `${JSON.stringify({
      schemaVersion: 2,
      gateInvocationId,
      fixtureId: optionDG2DeterministicFixture.id,
      fixtureSchemaVersion: optionDG2DeterministicFixture.schemaVersion,
      dataSource: optionDG2DeterministicFixture.dataSource,
      visualAuthority: '_visual_master/option_D/screens',
      masterMeasurements: metricsPath,
      visualPhase,
      referenceSealStatus: Object.values(frozenReferenceSha256)
        .some(value => value === 'PENDING_G2_REFERENCE_SEAL') ? 'PENDING_SOURCE_PATCH' : 'FROZEN',
      canonicalCssViewport: { width: 1920, height: 1080 },
      deviceScaleFactor: 2,
      maskPolicy: 'NO_MASKS',
      complete: true,
      thresholds: { perChannelDelta: 8, maxChangedPixelRatio, masterAnchorToleranceCssPixels: 4 },
      captures
    }, null, 2)}\n`);
  });

  for (const captureCase of captureCases) {
    test(`${captureCase.id} light/compact DSF2`, async ({ page }) => {
      await page.setViewportSize({ width: captureCase.width, height: captureCase.height });
      const { audit, runtimeErrors } = await bootScreen(page, captureCase.screen, captureCase.mode);
      await assertFunctionalContract(page, captureCase.screen);
      if (captureCase.screen === 'D24') {
        expect(audit.requests.some(request => request.pathname === '/api/auth/me'
          && request.authorization === `Bearer ${optionDG2DeterministicFixture.forbiddenToken}`)).toBe(true);
      }
      const geometry = await collectGeometry(page, captureCase.screen);
      if (captureCase.width === 1920 && captureCase.height === 1080) {
        assertMasterGeometry(captureCase.screen, geometry);
      }
      const image = await captureImage(page, captureCase.id);

      if (captureCase.width === 1920 && captureCase.screen === 'D01') {
        const password = page.getByLabel('密码', { exact: true });
        await expect(password).toHaveAttribute('type', 'password');
        await page.getByRole('button', { name: '显示登录密码' }).click();
        await expect(password).toHaveAttribute('type', 'text');
        await page.getByLabel('用户名').fill(optionDG2DeterministicFixture.engineer.username);
        await password.fill(optionDG2DeterministicFixture.password);
        await page.getByRole('checkbox', { name: '记住账号' }).check();
        await page.getByRole('button', { name: '登录', exact: true }).click();
        await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
        expect(audit.requests.some(request => request.pathname === '/api/auth/me'
          && request.authorization === `Bearer ${optionDG2DeterministicFixture.loginToken}`)).toBe(true);
      } else if (captureCase.width === 1920 && captureCase.screen === 'D24') {
        await page.getByRole('link', { name: /返回工程库/ }).click();
        await expect(page).toHaveURL(/#\/projects$/);
        await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
      }

      expect(audit.requests.some(request => request.handledAs === 'UNHANDLED_FAIL_CLOSED')).toBe(false);
      expect(runtimeErrors).toEqual([]);
      const fontFamily = await page.locator('body').evaluate(element => getComputedStyle(element).fontFamily);
      expect(fontFamily).toContain('Segoe UI');
      captures.push({
        id: captureCase.id,
        phase: visualPhase,
        cssViewport: { width: captureCase.width, height: captureCase.height },
        geometry,
        functionalAssertions: 'PASS',
        requestAudit: [...audit.requests],
        runtimeErrors: [...runtimeErrors],
        fontFamily,
        ...image
      });
    });
  }
});
