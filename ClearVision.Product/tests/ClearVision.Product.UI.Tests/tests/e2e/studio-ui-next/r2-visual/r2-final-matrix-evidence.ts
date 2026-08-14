import { createHash } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { Page } from '@playwright/test';
import approvedWritesContract from './r2-approved-writes.json';
import { collectR2DomReport } from './r2-in-app-browser-fixture';

export type R2FinalVariant = 'B0' | 'B2' | 'EXCEPTION';

export interface R2MatrixRuntimeAudit {
  readonly requests: R2MatrixRequestEntry[];
  readonly consoleErrors: string[];
  readonly pageErrors: string[];
  readonly failedRequests: string[];
  readonly httpErrors: string[];
  readonly expectedHttpErrors: string[];
  readonly observedExpectedHttpErrors: string[];
}

export interface R2MatrixRequestEntry {
  readonly method: string;
  readonly path: string;
  readonly url: string;
}

export interface R2MatrixRuntimeAuditOptions {
  readonly expectedHttpErrors?: readonly Readonly<{
    method: string;
    path: `/${string}`;
    status: number;
  }>[];
  readonly expectedRequestAborts?: readonly Readonly<{ method: 'GET'; path: `/${string}` }>[];
}

export interface R2FinalMatrixCaptureOptions {
  readonly scene: `S${string}`;
  readonly variant: R2FinalVariant;
  readonly route: `#/${string}`;
  readonly state: string;
  readonly role: 'Public' | 'Operator' | 'Engineer' | 'Admin';
  readonly flags?: Readonly<Record<string, boolean>>;
  readonly owner: string;
  readonly subscriptions?: number;
  readonly writes?: number;
  readonly allowedWrites?: readonly string[];
  readonly runtime: R2MatrixRuntimeAudit;
  readonly requiredCriticalActions: readonly string[];
  readonly notes?: readonly string[];
  readonly s05BitmapBefore?: R2S05BitmapEvidence;
  readonly s05BitmapSource?: Readonly<{
    bytes: Uint8Array;
    method: 'GET';
    path: string;
    url: string;
    contentType: string;
    status: number;
    sha256: string;
    byteLength: number;
  }>;
}

export interface R2CanvasPixelEvidence {
  readonly readable: boolean;
  readonly error: string | null;
  readonly backing: Readonly<{ width: number; height: number }>;
  readonly css: Readonly<{ x: number; y: number; width: number; height: number }>;
  readonly inViewport: boolean;
  readonly byteLength: number;
  readonly nonTransparentPixels: number;
  readonly uniqueColorCount: number;
  readonly sha256: string | null;
  readonly sourceSamples: readonly R2CanvasSourceSampleEvidence[];
}

export interface R2CanvasSourceSampleEvidence {
  readonly x: number;
  readonly y: number;
  readonly expected: readonly number[];
  readonly observed: readonly number[] | null;
  readonly backingX: number | null;
  readonly backingY: number | null;
  readonly maxChannelDelta: number | null;
}

export interface R2S05BitmapEvidence {
  readonly contract: Readonly<{
    schemaVersion: string;
    contentType: string;
    sha256: string;
    byteLength: number;
    width: number;
    height: number;
    channels: number;
    samples: readonly Readonly<{ x: number; y: number; rgba: readonly number[] }>[];
  }>;
  readonly canvas: R2CanvasPixelEvidence;
  readonly roi: Readonly<{ phase: string; kind: string; x: number; y: number; width: number; height: number }>;
}

interface R2MotionMeasurements {
  readonly layoutShifts: readonly { value: number; hadRecentInput: boolean }[];
  readonly longTasks: readonly { startTime: number; duration: number }[];
}

const uiTestsRoot = resolve(__dirname, '../../../..');
const repositoryRoot = resolve(uiTestsRoot, '../../..');
const approvedWritesByScene: Readonly<Record<string, readonly string[]>> = approvedWritesContract.scenes;

export async function prepareR2FinalMatrixPage(
  page: Page,
  options: R2MatrixRuntimeAuditOptions = {}
): Promise<R2MatrixRuntimeAudit> {
  const invalidHttpError = options.expectedHttpErrors?.find(entry =>
    !/^[A-Z]+$/.test(entry.method) || !entry.path.startsWith('/') ||
    !Number.isInteger(entry.status) || entry.status < 400 || entry.status > 599);
  if (invalidHttpError) {
    throw new Error(`Invalid expected HTTP error: ${JSON.stringify(invalidHttpError)}.`);
  }
  const expectedHttpErrors = new Set(
    (options.expectedHttpErrors ?? []).map(entry =>
      `${entry.method} ${entry.path}: ${entry.status}`)
  );
  const expectedRequestAborts = new Set(
    (options.expectedRequestAborts ?? []).map(entry => `${entry.method} ${entry.path}`)
  );
  const audit: R2MatrixRuntimeAudit = {
    requests: [],
    consoleErrors: [],
    pageErrors: [],
    failedRequests: [],
    httpErrors: [],
    expectedHttpErrors: [...expectedHttpErrors],
    observedExpectedHttpErrors: []
  };
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const content = message.text();
    const statusMatch = content.match(/status of (\d{3})/i);
    const sourcePath = message.location().url
      ? new URL(message.location().url).pathname
      : null;
    if (statusMatch && sourcePath && [...expectedHttpErrors].some(entry =>
      entry.endsWith(`${sourcePath}: ${statusMatch[1]}`))) return;
    audit.consoleErrors.push(content);
  });
  page.on('pageerror', error => audit.pageErrors.push(error.stack ?? error.message));
  page.on('request', request => {
    const path = new URL(request.url()).pathname;
    if (path !== '/health' && !path.startsWith('/api/')) return;
    audit.requests.push({ method: request.method(), path, url: request.url() });
  });
  page.on('response', response => {
    if (response.status() < 400) return;
    const request = response.request();
    const path = new URL(response.url()).pathname;
    const signature = `${request.method()} ${path}: ${response.status()}`;
    if (expectedHttpErrors.has(signature)) {
      audit.observedExpectedHttpErrors.push(signature);
      return;
    }
    audit.httpErrors.push(signature);
  });
  page.on('requestfailed', request => {
    const path = new URL(request.url()).pathname;
    const failure = request.failure()?.errorText ?? 'unknown';
    const signature = `${request.method()} ${path}`;
    if (failure === 'net::ERR_ABORTED' && expectedRequestAborts.has(signature)) return;
    audit.failedRequests.push(`${signature}: ${failure}`);
  });
  await page.addInitScript(() => {
    const measurements: { layoutShifts: { value: number; hadRecentInput: boolean }[]; longTasks: { startTime: number; duration: number }[] } = {
      layoutShifts: [],
      longTasks: []
    };
    Object.defineProperty(window, '__R2_MATRIX_MOTION__', {
      value: measurements,
      configurable: false,
      writable: false
    });
    try {
      new PerformanceObserver(list => {
        for (const entry of list.getEntries()) {
          const shift = entry as PerformanceEntry & { value?: number; hadRecentInput?: boolean };
          measurements.layoutShifts.push({
            value: shift.value ?? 0,
            hadRecentInput: shift.hadRecentInput ?? false
          });
        }
      }).observe({ type: 'layout-shift', buffered: true });
    } catch {
      // Unsupported entry types are recorded as an empty measurement set.
    }
    try {
      new PerformanceObserver(list => {
        for (const entry of list.getEntries()) {
          measurements.longTasks.push({ startTime: entry.startTime, duration: entry.duration });
        }
      }).observe({ type: 'longtask', buffered: true });
    } catch {
      // Unsupported entry types are recorded as an empty measurement set.
    }
  });
  return audit;
}

export function r2Viewport(variant: R2FinalVariant): Readonly<{ width: number; height: number }> {
  return variant === 'B0'
    ? Object.freeze({ width: 1920, height: 1080 })
    : Object.freeze({ width: 1366, height: 768 });
}

export async function collectR2S05BitmapEvidence(
  page: Page,
  contract: R2S05BitmapEvidence['contract']
): Promise<R2S05BitmapEvidence> {
  await page.evaluate(() => new Promise<void>(resolvePromise => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolvePromise()));
  }));
  const evidence = await page.evaluate(async fixtureContract => {
    const canvas = document.querySelector<HTMLCanvasElement>('[data-testid="image-canvas"]');
    const roiSurface = document.querySelector<HTMLElement>('.preview-panel__roi');
    const parameter = (name: string): number => {
      const input = document.querySelector<HTMLInputElement>(`[data-parameter-name="${name}"] input`);
      return Number(input?.value ?? Number.NaN);
    };
    const fallback = {
      readable: false,
      error: 'image canvas is missing',
      backing: { width: 0, height: 0 },
      css: { x: 0, y: 0, width: 0, height: 0 },
      inViewport: false,
      byteLength: 0,
      nonTransparentPixels: 0,
      uniqueColorCount: 0,
      sha256: null,
      sourceSamples: []
    };
    let canvasEvidence = fallback;
    if (canvas) {
      const bounds = canvas.getBoundingClientRect();
      const common = {
        backing: { width: canvas.width, height: canvas.height },
        css: { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height },
        inViewport: bounds.width > 0 && bounds.height > 0 && bounds.left >= 0 && bounds.top >= 0 &&
          bounds.right <= window.innerWidth && bounds.bottom <= window.innerHeight
      };
      try {
        const context = canvas.getContext('2d', { willReadFrequently: true });
        if (!context || canvas.width < 1 || canvas.height < 1) throw new Error('2D canvas context is unavailable');
        const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
        let nonTransparentPixels = 0;
        const colors = new Set<number>();
        for (let offset = 0; offset < pixels.length; offset += 4) {
          if (pixels[offset + 3] > 0) nonTransparentPixels += 1;
          if (colors.size < 257) {
            colors.add(((pixels[offset] << 24) | (pixels[offset + 1] << 16) |
              (pixels[offset + 2] << 8) | pixels[offset + 3]) >>> 0);
          }
        }
        const digest = await crypto.subtle.digest('SHA-256', pixels);
        const imageSurface = document.querySelector<HTMLElement>('[data-capability="image-canvas"]');
        const scale = Number(imageSurface?.dataset.imageScale ?? Number.NaN);
        const offsetX = (bounds.width - fixtureContract.width * scale) / 2;
        const offsetY = (bounds.height - fixtureContract.height * scale) / 2;
        const dpr = bounds.width > 0 ? canvas.width / bounds.width : Number.NaN;
        const sourceSamples = fixtureContract.samples.map(sample => {
          if (![scale, offsetX, offsetY, dpr].every(Number.isFinite) || scale <= 0 || dpr <= 0) {
            return {
              x: sample.x, y: sample.y, expected: [...sample.rgba], observed: null,
              backingX: null, backingY: null, maxChannelDelta: null
            };
          }
          const centerX = Math.round((offsetX + (sample.x + 0.5) * scale) * dpr);
          const centerY = Math.round((offsetY + (sample.y + 0.5) * scale) * dpr);
          let best: { rgba: number[]; x: number; y: number; delta: number } | null = null;
          for (let y = centerY - 2; y <= centerY + 2; y += 1) {
            for (let x = centerX - 2; x <= centerX + 2; x += 1) {
              if (x < 0 || y < 0 || x >= canvas.width || y >= canvas.height) continue;
              const offset = (y * canvas.width + x) * 4;
              const rgba = Array.from(pixels.subarray(offset, offset + 4));
              const delta = Math.max(...rgba.map((channel, index) => Math.abs(channel - sample.rgba[index])));
              if (!best || delta < best.delta) best = { rgba, x, y, delta };
            }
          }
          return {
            x: sample.x,
            y: sample.y,
            expected: [...sample.rgba],
            observed: best?.rgba ?? null,
            backingX: best?.x ?? null,
            backingY: best?.y ?? null,
            maxChannelDelta: best?.delta ?? null
          };
        });
        canvasEvidence = {
          readable: true,
          error: null,
          ...common,
          byteLength: pixels.byteLength,
          nonTransparentPixels,
          uniqueColorCount: colors.size,
          sha256: [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, '0')).join(''),
          sourceSamples
        };
      } catch (error) {
        canvasEvidence = {
          ...fallback,
          ...common,
          error: error instanceof Error ? error.message : String(error)
        };
      }
    }
    return {
      contract: fixtureContract,
      canvas: canvasEvidence,
      roi: {
        phase: roiSurface?.dataset.roiPhase ?? '',
        kind: 'rectangle',
        x: parameter('X'),
        y: parameter('Y'),
        width: parameter('Width'),
        height: parameter('Height')
      }
    };
  }, contract);
  return evidence;
}

export async function captureR2FinalMatrixGroup(page: Page, options: R2FinalMatrixCaptureOptions): Promise<void> {
  const phase = process.env.CV_R2_CAPTURE_PHASE?.trim().toLowerCase();
  if (phase !== 'before' && phase !== 'final') {
    throw new Error('CV_R2_CAPTURE_PHASE must be before or final for @r2-final tests.');
  }
  const runId = process.env.CV_R2_MATRIX_RUN_ID?.trim() || 'dirty-candidate';
  if (!/^[a-z0-9][a-z0-9._-]*$/i.test(runId)) throw new Error(`Invalid CV_R2_MATRIX_RUN_ID: ${runId}.`);
  const groupId = `${options.scene}-${options.variant}`;
  const groupRoot = resolve(
    repositoryRoot,
    '.tmp',
    'studio-ui-next',
    'view-polish-r2',
    'R2.7',
    runId,
    'final-matrix',
    groupId
  );
  mkdirSync(groupRoot, { recursive: true });
  let s05SourceArtifact: Readonly<{
    path: string;
    sha256: string;
    byteLength: number;
    contentType: string;
    status: number;
    response: Readonly<{
      source: 'PLAYWRIGHT_ROUTE_FULFILL_RESPONSE_SNAPSHOT';
      method: 'GET';
      path: string;
      url: string;
      status: number;
      contentType: string;
      sha256: string;
      byteLength: number;
    }>;
  }> | undefined;
  if (options.scene === 'S05') {
    if (!options.s05BitmapSource || options.s05BitmapSource.bytes.byteLength < 1) {
      throw new Error(`R2 ${groupId} requires the actual preview bitmap response bytes.`);
    }
    const sourceName = phase === 'before' ? 'source-before.png' : 'source-final.png';
    const sourceBytes = Buffer.from(options.s05BitmapSource.bytes);
    const sourceSha256 = createHash('sha256').update(sourceBytes).digest('hex');
    if (options.s05BitmapSource.method !== 'GET' ||
      !/^\/api\/preview-artifacts\/[A-Za-z0-9_-]{43}$/.test(options.s05BitmapSource.path) ||
      new URL(options.s05BitmapSource.url).pathname !== options.s05BitmapSource.path ||
      options.s05BitmapSource.status !== 200 ||
      options.s05BitmapSource.contentType !== 'image/png' ||
      options.s05BitmapSource.sha256 !== sourceSha256 ||
      options.s05BitmapSource.byteLength !== sourceBytes.byteLength) {
      throw new Error(`R2 ${groupId} preview artifact response snapshot metadata is inconsistent.`);
    }
    writeFileSync(resolve(groupRoot, sourceName), sourceBytes);
    s05SourceArtifact = Object.freeze({
      path: sourceName,
      sha256: sourceSha256,
      byteLength: sourceBytes.byteLength,
      contentType: options.s05BitmapSource.contentType,
      status: options.s05BitmapSource.status,
      response: Object.freeze({
        source: 'PLAYWRIGHT_ROUTE_FULFILL_RESPONSE_SNAPSHOT',
        method: options.s05BitmapSource.method,
        path: options.s05BitmapSource.path,
        url: options.s05BitmapSource.url,
        status: options.s05BitmapSource.status,
        contentType: options.s05BitmapSource.contentType,
        sha256: options.s05BitmapSource.sha256,
        byteLength: options.s05BitmapSource.byteLength
      })
    });
  }

  const screenshotName = phase === 'before' ? 'before.png' : 'after.png';
  const domName = phase === 'before' ? 'dom-before.json' : 'dom-after.json';
  await page.screenshot({
    path: resolve(groupRoot, screenshotName),
    animations: 'disabled',
    caret: 'hide',
    fullPage: false
  });
  const dom = await collectR2DomReport(page, options.requiredCriticalActions);
  const motion = await page.evaluate(() => {
    const value = (window as Window & { __R2_MATRIX_MOTION__?: R2MotionMeasurements }).__R2_MATRIX_MOTION__;
    return value ?? { layoutShifts: [], longTasks: [] };
  });
  const s05Bitmap = options.scene === 'S05'
    ? await collectR2S05BitmapEvidence(page, options.s05BitmapBefore?.contract ?? {
      schemaVersion: '', contentType: '', sha256: '', byteLength: 0, width: 0, height: 0, channels: 0, samples: []
    })
    : undefined;
  const requestSummary = options.runtime.requests.map(entry => `${entry.method.toUpperCase()} ${entry.path}`);
  const runtimeRequests = options.runtime.requests.map(entry => ({
    method: entry.method.toUpperCase(),
    path: entry.path,
    url: entry.url
  }));
  const allowedWrites = new Set(options.allowedWrites ?? []);
  const approvedWrites = new Set(approvedWritesByScene[options.scene] ?? []);
  const invalidAllowedWrites = [...allowedWrites].filter(entry => !approvedWrites.has(entry));
  const unusedAllowedWrites = [...allowedWrites].filter(entry => !requestSummary.includes(entry));
  const unexpectedWrites = requestSummary.filter(entry => {
    const method = entry.split(' ', 1)[0];
    return method !== 'GET' && method !== 'HEAD' && !allowedWrites.has(entry);
  });
  const runtimeFailures = [
    ...options.runtime.consoleErrors.map(error => `console: ${error}`),
    ...options.runtime.pageErrors.map(error => `page: ${error}`),
    ...options.runtime.failedRequests.map(error => `request: ${error}`),
    ...options.runtime.httpErrors.map(error => `http: ${error}`),
    ...options.runtime.expectedHttpErrors
      .filter(error => !options.runtime.observedExpectedHttpErrors.includes(error))
      .map(error => `missing expected http: ${error}`),
    ...invalidAllowedWrites.map(error => `unapproved write allowance: ${error}`),
    ...unusedAllowedWrites.map(error => `unused write allowance: ${error}`),
    ...unexpectedWrites.map(error => `write: ${error}`)
  ];
  if (runtimeFailures.length > 0) throw new Error(`R2 ${groupId} runtime audit failed:\n${runtimeFailures.join('\n')}`);
  const horizontalOverflow = Number(dom.horizontalOverflow ?? 0);
  if (horizontalOverflow > 0) throw new Error(`R2 ${groupId} has ${horizontalOverflow}px horizontal overflow.`);
  const criticalActions = Array.isArray(dom.criticalActions)
    ? dom.criticalActions as { selector?: string; truncated?: boolean; inViewport?: boolean; reachable?: boolean; enabled?: boolean; unobscured?: boolean }[]
    : [];
  if (options.requiredCriticalActions.length === 0) {
    throw new Error(`R2 ${groupId} must declare at least one required critical action.`);
  }
  for (const selector of options.requiredCriticalActions) {
    const matches = criticalActions.filter(action => action.selector === selector);
    if (matches.length !== 1) {
      throw new Error(`R2 ${groupId} requires exactly one critical action for ${selector}; found ${matches.length}.`);
    }
    const [action] = matches;
    if (action.truncated) throw new Error(`R2 ${groupId} has a truncated critical action: ${selector}.`);
    if (!action.inViewport) throw new Error(`R2 ${groupId} has an offscreen critical action: ${selector}.`);
    if (!action.reachable) throw new Error(`R2 ${groupId} has an unreachable critical action: ${selector}.`);
    if (!action.enabled) throw new Error(`R2 ${groupId} has a disabled critical action: ${selector}.`);
    if (!action.unobscured) throw new Error(`R2 ${groupId} has an obscured critical action: ${selector}.`);
  }
  const interaction = {
    schemaVersion: 'r2-final-matrix-capture.v1',
    scene: options.scene,
    variant: options.variant,
    route: options.route,
    state: options.state,
    role: options.role,
    flags: options.flags ?? {},
    owner: {
      capability: options.owner,
      mounted: 1,
      subscriptions: options.subscriptions ?? 0,
      writes: options.writes ?? 0
    },
    requests: requestSummary,
    runtimeRequests,
    allowedWrites: [...allowedWrites],
    runtime: {
      consoleErrors: [...options.runtime.consoleErrors],
      pageErrors: [...options.runtime.pageErrors],
      failedRequests: [...options.runtime.failedRequests],
      httpErrors: [...options.runtime.httpErrors],
      expectedHttpErrors: [...options.runtime.expectedHttpErrors],
      observedExpectedHttpErrors: [...options.runtime.observedExpectedHttpErrors],
      unexpectedWrites
    },
    requiredCriticalActions: [...options.requiredCriticalActions],
    motion,
    notes: options.notes ?? [],
    ...(s05Bitmap ? {
      bitmapEvidence: {
        source: s05SourceArtifact,
        beforeAction: options.s05BitmapBefore ?? s05Bitmap,
        capture: s05Bitmap
      }
    } : {})
  };
  writeFileSync(resolve(groupRoot, domName), `${JSON.stringify(dom, null, 2)}\n`, 'utf8');
  writeFileSync(resolve(groupRoot, `interaction-${phase}.json`), `${JSON.stringify(interaction, null, 2)}\n`, 'utf8');
  writeFileSync(resolve(groupRoot, `capture-${phase}.json`), `${JSON.stringify({ ...interaction, dom }, null, 2)}\n`, 'utf8');
}
