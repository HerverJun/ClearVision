import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { isAbsolute, relative, resolve } from 'node:path';
import type { Page } from '@playwright/test';

export interface F04RuntimeErrorAudit {
  readonly consoleErrors: string[];
  readonly pageErrors: string[];
}

export interface F04VisualEvidenceOptions {
  readonly scenario: string;
  readonly viewport: Readonly<{ width: number; height: number }>;
  readonly runtimeErrors: F04RuntimeErrorAudit;
  readonly requestAudit?: readonly Readonly<{ method: string; path: string }>[];
  readonly notes?: readonly string[];
}

function visualEvidenceRoot(): string | null {
  const configured = process.env.CV_F04_VISUAL_EVIDENCE_DIR?.trim();
  if (!configured) return null;
  const repositoryRoot = resolve(process.cwd(), '..', '..', '..');
  const allowedRoot = resolve(repositoryRoot, '.tmp', 'studio-ui-next', 'f04');
  const outputRoot = isAbsolute(configured) ? resolve(configured) : resolve(repositoryRoot, configured);
  const relativeOutput = relative(allowedRoot, outputRoot);
  if (relativeOutput.startsWith('..') || isAbsolute(relativeOutput)) {
    throw new Error('CV_F04_VISUAL_EVIDENCE_DIR must remain under .tmp/studio-ui-next/f04.');
  }
  return outputRoot;
}

function requiredSourceSha(): string {
  const value = process.env.CV_F04_SOURCE_SHA?.trim() ?? '';
  if (!/^[0-9a-f]{40}$/i.test(value)) {
    throw new Error('CV_F04_SOURCE_SHA must contain a 40-character SHA.');
  }
  return value.toLowerCase();
}

function safeEvidenceName(value: string): string {
  const safe = value.trim().toLowerCase().replace(/[^a-z0-9_.-]+/g, '-').replace(/^-+|-+$/g, '');
  if (!safe) throw new Error('F04 evidence scenario requires a safe filename.');
  return safe;
}

export function hasF04VisualEvidenceTarget(): boolean {
  return visualEvidenceRoot() !== null;
}

export function createF04RuntimeErrorAudit(page: Page): F04RuntimeErrorAudit {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => pageErrors.push(error.stack ?? error.message));
  return { consoleErrors, pageErrors };
}

export async function captureF04VisualEvidence(
  page: Page,
  options: F04VisualEvidenceOptions
): Promise<Readonly<{ screenshotPath: string; metadataPath: string }>> {
  const outputRoot = visualEvidenceRoot();
  if (!outputRoot) throw new Error('CV_F04_VISUAL_EVIDENCE_DIR is required for visual capture.');
  const sourceSha = requiredSourceSha();
  const projection = await page.evaluate(() => ({
    viewport: { width: window.innerWidth, height: window.innerHeight },
    dpr: window.devicePixelRatio,
    theme: document.documentElement.dataset.theme ?? null,
    density: document.documentElement.dataset.density ?? null,
    reducedMotion: document.documentElement.dataset.reducedMotion ?? null,
    horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight,
    authPage: document.querySelector('[data-auth-page]')?.getAttribute('data-auth-page') ?? null,
    studioPage: document.querySelector('[data-studio-page]')?.getAttribute('data-studio-page') ?? null,
    capabilities: [...document.querySelectorAll<HTMLElement>('[data-capability]')]
      .map(element => element.dataset.capability ?? '')
      .filter(Boolean),
    workspaceState: document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-state') ?? null,
    workspacePersistencePhase: document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-persistence-phase') ?? null,
    workspaceRunPhase: document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-run-phase') ?? null,
    leaveGuardPhase: document.querySelector('[data-product-shell]')
      ?.getAttribute('data-leave-guard-phase') ?? null,
    workspaceGeometry: (() => {
      const rect = (selector: string) => {
        const bounds = document.querySelector(selector)?.getBoundingClientRect();
        return bounds
          ? { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height }
          : null;
      };
      return {
        productRail: rect('.product-layout--workspace .product-layout__sidebar'),
        productTopbar: rect('.product-layout--workspace .product-layout__topbar'),
        workspaceToolbar: rect('.workspace-shell__toolbar'),
        operatorRail: rect('.operator-rail'),
        canvasStage: rect('.flow-canvas-surface__stage'),
        preview: rect('.preview-panel'),
        inspector: rect('.inspector-panel'),
        workspaceStatusbar: rect('.workspace-shell__statusbar'),
        saveCommand: rect('[data-testid="workspace-save"]'),
        runCommand: rect('[data-testid="workspace-run"]')
      };
    })(),
    modalTitle: document.querySelector('[role="dialog"] h2')?.textContent?.trim() ?? null,
    activeElement: document.activeElement instanceof HTMLElement
      ? {
          tagName: document.activeElement.tagName,
          testId: document.activeElement.dataset.testid ?? null,
          ariaLabel: document.activeElement.getAttribute('aria-label'),
          text: document.activeElement.textContent?.trim() ?? null
        }
      : null
  }));
  if (projection.viewport.width !== options.viewport.width || projection.viewport.height !== options.viewport.height) {
    throw new Error(`F04 evidence viewport drifted: ${JSON.stringify(projection.viewport)}.`);
  }
  if (projection.horizontalOverflow > 1) {
    throw new Error(`F04 product surface overflowed horizontally: ${JSON.stringify(projection)}.`);
  }
  if (options.runtimeErrors.consoleErrors.length || options.runtimeErrors.pageErrors.length) {
    throw new Error(`F04 product surface emitted runtime errors: ${JSON.stringify(options.runtimeErrors)}.`);
  }

  const dprName = String(projection.dpr).replace('.', 'p');
  const stem = `${safeEvidenceName(options.scenario)}-${options.viewport.width}x${options.viewport.height}-dpr-${dprName}`;
  const screenshotPath = resolve(outputRoot, `${stem}.png`);
  const metadataPath = resolve(outputRoot, `${stem}.json`);
  await mkdir(outputRoot, { recursive: true });
  const screenshot = await page.screenshot({ animations: 'disabled', fullPage: false, type: 'png' });
  await writeFile(screenshotPath, screenshot);
  await writeFile(metadataPath, `${JSON.stringify({
    schemaVersion: 'f04-product-visual-browser.v1',
    capturedAtUtc: new Date().toISOString(),
    sourceSha,
    evidencePhase: 'f04',
    evidenceType: 'BROWSER_FIXTURE',
    route: page.url(),
    scenario: options.scenario,
    viewport: options.viewport,
    projection,
    runtimeErrors: options.runtimeErrors,
    requestAudit: options.requestAudit ?? [],
    notes: options.notes ?? [],
    dpr: { type: 'BROWSER_EMULATED_DPR', value: projection.dpr },
    nativeWebView2Dpi: 'NOT_PERFORMED_BY_THIS_EVIDENCE',
    screenshot: {
      fileName: `${stem}.png`,
      bytes: screenshot.byteLength,
      sha256: createHash('sha256').update(screenshot).digest('hex').toUpperCase()
    }
  }, null, 2)}\n`, 'utf8');
  return Object.freeze({ screenshotPath, metadataPath });
}
