import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, Page, test } from '@playwright/test';
import {
  installOptionDG1DeterministicFixture,
  optionDG1DeterministicFixture
} from './option-d-g1-deterministic-fixture';

type Theme = 'light' | 'dark';
type CapturePhase = 'reference' | 'candidate';

interface VisualComparison {
  readonly changedPixels: number;
  readonly changedPixelRatio: number;
  readonly maxChannelDelta: number;
  readonly width: number;
  readonly height: number;
}

interface CaptureEvidence {
  readonly id: string;
  readonly phase: CapturePhase;
  readonly screenshot: string;
  readonly sha256: string;
  readonly width: number;
  readonly height: number;
  readonly comparison?: VisualComparison;
  readonly referenceSha256?: string;
  readonly diff?: string;
  readonly diffSha256?: string;
  readonly overlay?: string;
  readonly overlaySha256?: string;
  readonly fonts: FontEvidence;
}

interface PlatformFont {
  readonly familyName: string;
  readonly glyphCount: number;
  readonly isCustomFont: boolean;
}

interface FontEvidence {
  readonly computedSans: string;
  readonly computedMono: string;
  readonly headingPlatformFonts: readonly PlatformFont[];
  readonly numericPlatformFonts: readonly PlatformFont[];
  readonly availability: Readonly<Record<string, boolean>>;
}

const requestedVisualPhase = process.env.CV_OPTION_D_G1_VISUAL_PHASE?.trim();
const gateInvocationId = process.env.CV_OPTION_D_G1_GATE_INVOCATION_ID?.trim();
if (!requestedVisualPhase) {
  throw new Error(
    'CV_OPTION_D_G1_VISUAL_PHASE is required. Use the dedicated Option D G1 reference or candidate gate.'
  );
}
if (requestedVisualPhase !== 'reference'
  && requestedVisualPhase !== 'candidate') {
  throw new Error(
    `Invalid CV_OPTION_D_G1_VISUAL_PHASE: ${requestedVisualPhase}. Expected reference or candidate.`
  );
}
if (!gateInvocationId) {
  throw new Error(
    'CV_OPTION_D_G1_GATE_INVOCATION_ID is required. Use the dedicated Option D G1 gate.'
  );
}
const visualPhase = requestedVisualPhase as CapturePhase;
const evidenceRoot = resolve(
  process.cwd(),
  '../../../.tmp/studio-ui-next/option-d-g1/visual'
);
const expectedWidth = 3840;
const expectedHeight = 2160;
const expectedCaptureCount = 4;
const maxChangedPixelRatio = 0.01;
const frozenReferenceSha256: Readonly<Record<string, string>> = Object.freeze({
  'design-light-compact': 'b4a7985a23bef122184737a1b99be2bf270002f9c607914b13018c16550aac60',
  'design-dark-compact': 'c0fdc24277cb50fd2fce252bd4809d97e0e60a9a111b9601136d2481f9bf319d',
  'canvas-light-compact': '8d10052afe4ccf7746ea3bd81621036d4cd79312cf212c343fb0f87eacfac502',
  'canvas-dark-compact': '37f91663f141ba7118179eac319f3e64158fcaa67682dd04529fa875ad5091bd'
});

test.use({
  viewport: { width: 1920, height: 1080 },
  deviceScaleFactor: 2
});

function sha256(buffer: Buffer): string {
  return createHash('sha256').update(buffer).digest('hex');
}

function pngDimensions(buffer: Buffer): { width: number; height: number } {
  if (buffer.length < 24 || buffer.toString('hex', 0, 8) !== '89504e470d0a1a0a') {
    throw new Error('Visual evidence is not a valid PNG.');
  }
  return {
    width: buffer.readUInt32BE(16),
    height: buffer.readUInt32BE(20)
  };
}

function writeDataUrl(path: string, dataUrl: string): Buffer {
  const comma = dataUrl.indexOf(',');
  if (comma < 0) throw new Error('Canvas evidence data URL is invalid.');
  const buffer = Buffer.from(dataUrl.slice(comma + 1), 'base64');
  writeFileSync(path, buffer);
  return buffer;
}

async function bootLab(page: Page, route: 'design' | 'canvas'): Promise<string[]> {
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
  page.on('response', response => {
    const url = new URL(response.url());
    if ((url.pathname === '/health' || url.pathname.startsWith('/api/'))
      && response.status() >= 400) {
      runtimeErrors.push(
        `Request returned ${response.status()}: ${response.request().method()} ${response.url()}`
      );
    }
  });

  await installOptionDG1DeterministicFixture(page);

  await page.goto(`/studio/index.html#/labs/${route}`);
  if (route === 'design') {
    await expect(page.locator('[data-design-lab="ready"]')).toBeVisible();
    await expect(page.locator(`[data-design-fixture="${optionDG1DeterministicFixture.id}"]`)).toHaveCount(1);
  } else {
    await expect(page.locator('[data-canvas-lab="ready"]')).toBeVisible();
  }
  return runtimeErrors;
}

async function collectFontEvidence(page: Page, route: 'design' | 'canvas'): Promise<FontEvidence> {
  const headingSelector = route === 'design' ? '.design-lab__hero h1' : '.canvas-lab h1';
  const numericSelector = route === 'design' ? '[data-design-numeric-sample]' : '[data-canvas-dpr]';
  const [computedSans, computedMono, availability] = await Promise.all([
    page.locator(headingSelector).evaluate(element => getComputedStyle(element).fontFamily),
    page.locator(numericSelector).evaluate(element => getComputedStyle(element).fontFamily),
    page.evaluate(() => ({
      segoeUi: document.fonts.check('16px "Segoe UI"'),
      microsoftYaHeiUi: document.fonts.check('16px "Microsoft YaHei UI"'),
      cascadiaCode: document.fonts.check('16px "Cascadia Code"'),
      consolas: document.fonts.check('16px Consolas')
    }))
  ]);

  const session = await page.context().newCDPSession(page);
  try {
    await session.send('DOM.enable');
    await session.send('CSS.enable');
    const { root } = await session.send('DOM.getDocument');
    const platformFonts = async (selector: string): Promise<readonly PlatformFont[]> => {
      const { nodeId } = await session.send('DOM.querySelector', {
        nodeId: root.nodeId,
        selector
      });
      if (!nodeId) throw new Error(`Font evidence node is missing: ${selector}`);
      const { fonts } = await session.send('CSS.getPlatformFontsForNode', { nodeId });
      return fonts.map(font => ({
        familyName: font.familyName,
        glyphCount: font.glyphCount,
        isCustomFont: font.isCustomFont
      }));
    };
    return {
      computedSans,
      computedMono,
      headingPlatformFonts: await platformFonts(headingSelector),
      numericPlatformFonts: await platformFonts(numericSelector),
      availability
    };
  } finally {
    await session.detach();
  }
}

function assertSystemFontEvidence(fonts: FontEvidence): void {
  expect(fonts.computedSans).toContain('Segoe UI');
  expect(fonts.computedSans).toContain('Microsoft YaHei UI');
  expect(fonts.computedMono).toContain('Cascadia Code');
  expect(fonts.computedMono).toContain('Consolas');
  expect(fonts.headingPlatformFonts.length).toBeGreaterThan(0);
  expect(fonts.headingPlatformFonts.every(font => !font.isCustomFont)).toBe(true);
  expect(fonts.headingPlatformFonts.some(font => /Segoe UI|Microsoft YaHei/i.test(font.familyName))).toBe(true);
  expect(fonts.numericPlatformFonts.length).toBeGreaterThan(0);
  expect(fonts.numericPlatformFonts.every(font => !font.isCustomFont)).toBe(true);
  expect(fonts.numericPlatformFonts.some(font => /Cascadia|Consolas/i.test(font.familyName))).toBe(true);
}

async function setVisualMode(page: Page, route: 'design' | 'canvas', theme: Theme): Promise<void> {
  if (route === 'design') {
    await page.locator(`[data-design-theme="${theme}"]`).click();
    await page.locator('[data-design-density="compact"]').click();
  } else {
    await page.locator('html').evaluate((root, nextTheme) => {
      (root as HTMLElement).dataset.theme = nextTheme;
      (root as HTMLElement).dataset.density = 'compact';
      (root as HTMLElement).dataset.reducedMotion = 'true';
    }, theme);
  }
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
  await expect(page.locator('html')).toHaveAttribute('data-density', 'compact');
  await expect(page.locator('html')).toHaveAttribute(
    'data-reduced-motion',
    route === 'design' ? 'false' : 'true'
  );
  await page.evaluate(() => new Promise<void>(resolveFrame => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolveFrame()));
  }));
}

async function compareWholeImage(
  page: Page,
  reference: Buffer,
  candidate: Buffer
): Promise<VisualComparison & { diffDataUrl: string; overlayDataUrl: string }> {
  return page.evaluate(async ({ referenceBase64, candidateBase64 }) => {
    async function decode(base64: string): Promise<ImageBitmap> {
      const response = await fetch(`data:image/png;base64,${base64}`);
      return createImageBitmap(await response.blob());
    }

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
    const sourceContext = source.getContext('2d', { willReadFrequently: true });
    if (!sourceContext) throw new Error('2D comparison context is unavailable.');

    sourceContext.drawImage(referenceImage, 0, 0);
    const referencePixels = sourceContext.getImageData(0, 0, width, height);
    sourceContext.clearRect(0, 0, width, height);
    sourceContext.drawImage(candidateImage, 0, 0);
    const candidatePixels = sourceContext.getImageData(0, 0, width, height);

    const diff = sourceContext.createImageData(width, height);
    const overlay = sourceContext.createImageData(width, height);
    let changedPixels = 0;
    let maxChannelDelta = 0;
    const perChannelThreshold = 8;

    for (let index = 0; index < referencePixels.data.length; index += 4) {
      const redDelta = Math.abs(referencePixels.data[index] - candidatePixels.data[index]);
      const greenDelta = Math.abs(referencePixels.data[index + 1] - candidatePixels.data[index + 1]);
      const blueDelta = Math.abs(referencePixels.data[index + 2] - candidatePixels.data[index + 2]);
      const alphaDelta = Math.abs(referencePixels.data[index + 3] - candidatePixels.data[index + 3]);
      const pixelDelta = Math.max(redDelta, greenDelta, blueDelta, alphaDelta);
      maxChannelDelta = Math.max(maxChannelDelta, pixelDelta);

      const changed = pixelDelta > perChannelThreshold;
      if (changed) changedPixels += 1;
      const referenceLuma = Math.round(
        referencePixels.data[index] * 0.2126 +
        referencePixels.data[index + 1] * 0.7152 +
        referencePixels.data[index + 2] * 0.0722
      );
      diff.data[index] = changed ? 229 : referenceLuma;
      diff.data[index + 1] = changed ? 42 : referenceLuma;
      diff.data[index + 2] = changed ? 50 : referenceLuma;
      diff.data[index + 3] = 255;

      overlay.data[index] = Math.round(
        (referencePixels.data[index] + candidatePixels.data[index]) / 2
      );
      overlay.data[index + 1] = Math.round(
        (referencePixels.data[index + 1] + candidatePixels.data[index + 1]) / 2
      );
      overlay.data[index + 2] = Math.round(
        (referencePixels.data[index + 2] + candidatePixels.data[index + 2]) / 2
      );
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

async function capture(
  page: Page,
  id: string,
  phase: CapturePhase,
  route: 'design' | 'canvas'
): Promise<CaptureEvidence> {
  const phaseDirectory = resolve(evidenceRoot, phase);
  mkdirSync(phaseDirectory, { recursive: true });
  const screenshotPath = resolve(phaseDirectory, `${id}.png`);
  const referenceAlreadyExists = phase === 'reference' && existsSync(screenshotPath);
  const screenshot = referenceAlreadyExists
    ? readFileSync(screenshotPath)
    : await page.screenshot({
        animations: 'disabled',
        caret: 'hide',
        fullPage: false,
        path: phase === 'candidate' ? screenshotPath : undefined
      });
  const dimensions = pngDimensions(screenshot);
  expect(dimensions).toEqual({ width: expectedWidth, height: expectedHeight });
  const fonts = await collectFontEvidence(page, route);
  assertSystemFontEvidence(fonts);

  const evidence: CaptureEvidence = {
    id,
    phase,
    screenshot: screenshotPath,
    sha256: sha256(screenshot),
    fonts,
    ...dimensions
  };
  const expectedReferenceSha256 = frozenReferenceSha256[id];
  expect(expectedReferenceSha256, `Missing frozen G1 reference hash for ${id}`).toBeDefined();
  if (phase === 'reference') {
    expect(
      evidence.sha256,
      `G1 reference hash does not match the frozen authority for ${id}`
    ).toBe(expectedReferenceSha256);
    if (!referenceAlreadyExists) {
      writeFileSync(screenshotPath, screenshot, { flag: 'wx' });
    }
    return evidence;
  }

  const referencePath = resolve(evidenceRoot, 'reference', `${id}.png`);
  expect(existsSync(referencePath), `Missing G1 reference ${referencePath}`).toBe(true);
  const reference = readFileSync(referencePath);
  expect(
    sha256(reference),
    `Frozen G1 reference hash changed for ${id}`
  ).toBe(expectedReferenceSha256);
  const comparison = await compareWholeImage(page, reference, screenshot);
  const diffPath = resolve(phaseDirectory, `${id}.diff.png`);
  const overlayPath = resolve(phaseDirectory, `${id}.overlay.png`);
  const diff = writeDataUrl(diffPath, comparison.diffDataUrl);
  const overlay = writeDataUrl(overlayPath, comparison.overlayDataUrl);
  expect(
    comparison.changedPixelRatio,
    `${id} changed-pixel ratio exceeds the G1 whole-image threshold`
  ).toBeLessThanOrEqual(maxChangedPixelRatio);
  return {
    ...evidence,
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

test.describe('Option D G1 deterministic visual evidence', () => {
  test.describe.configure({ mode: 'serial' });

  const captures: CaptureEvidence[] = [];

  test.afterAll(() => {
    if (captures.length !== expectedCaptureCount) {
      throw new Error(
        `G1 ${visualPhase} evidence captured ${captures.length}/${expectedCaptureCount} required images.`
      );
    }
    const expectedCaptureIds = Object.keys(frozenReferenceSha256).sort();
    const actualCaptureIds = captures.map(capture => capture.id).sort();
    expect(actualCaptureIds, 'G1 visual evidence capture IDs are incomplete or duplicated')
      .toEqual(expectedCaptureIds);
    mkdirSync(evidenceRoot, { recursive: true });
    const manifestPath = resolve(evidenceRoot, `${visualPhase}.json`);
    const manifest = `${JSON.stringify({
        schemaVersion: 2,
        gateInvocationId,
        fixtureId: optionDG1DeterministicFixture.id,
        fixtureSchemaVersion: optionDG1DeterministicFixture.schemaVersion,
        dataSource: optionDG1DeterministicFixture.dataSource,
        visualPhase,
        cssViewport: { width: 1920, height: 1080 },
        deviceScaleFactor: 2,
        outputPixels: { width: expectedWidth, height: expectedHeight },
        maskPolicy: 'NO_MASKS',
        complete: true,
        thresholds: {
          perChannelDelta: 8,
          maxChangedPixelRatio
        },
        captures
      }, null, 2)}\n`;
    writeFileSync(manifestPath, manifest);
  });

  for (const route of ['design', 'canvas'] as const) {
    for (const theme of ['light', 'dark'] as const) {
      test(`${route} ${theme}/compact @ 1920x1080 DSF2`, async ({ page }) => {
        const runtimeErrors = await bootLab(page, route);
        await setVisualMode(page, route, theme);
        const evidence = await capture(
          page,
          `${route}-${theme}-compact`,
          visualPhase,
          route
        );
        expect(runtimeErrors).toEqual([]);
        captures.push(evidence);
      });
    }
  }
});
