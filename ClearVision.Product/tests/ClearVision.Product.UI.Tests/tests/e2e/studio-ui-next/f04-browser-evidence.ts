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
    workspaceInteractionState: {
      operatorCatalogPhase: document.querySelector('[data-capability="operator-rail"]')
        ?.getAttribute('data-catalog-phase') ?? null,
      operatorCategory: document.querySelector('[data-capability="operator-rail"]')
        ?.getAttribute('data-active-category') ?? null,
      draggingOperator: document.querySelector('[data-capability="operator-rail"]')
        ?.getAttribute('data-dragging-operator') ?? null,
      canvasNodeCount: document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]')
        ?.getAttribute('data-node-count') ?? null,
      canvasConnectionCount: document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]')
        ?.getAttribute('data-connection-count') ?? null,
      canvasSelectedCount: document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]')
        ?.getAttribute('data-selected-count') ?? null,
      canvasSelectedDisabledCount: document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]')
        ?.getAttribute('data-selected-disabled-count') ?? null,
      inspectorMode: document.querySelector('[data-evidence-surface="f03-g3-inspector"]')
        ?.getAttribute('data-inspector-mode') ?? null,
      inspectorMetadataPhase: document.querySelector('[data-evidence-surface="f03-g3-inspector"]')
        ?.getAttribute('data-metadata-phase') ?? null,
      inspectorValidationErrorCount: document.querySelectorAll('.parameter-editor__errors li').length,
      previewPhase: document.querySelector('[data-capability="preview-workbench"]')
        ?.getAttribute('data-preview-phase') ?? null,
      previewStale: document.querySelector('[data-capability="preview-workbench"]')
        ?.getAttribute('data-preview-stale') ?? null,
      imagePhase: document.querySelector('[data-capability="image-canvas"]')
        ?.getAttribute('data-image-phase') ?? null,
      pixelProbePhase: document.querySelector('.image-viewport__probe')
        ?.getAttribute('data-probe-phase') ?? null,
      roiPhase: document.querySelector('.preview-panel__roi')
        ?.getAttribute('data-roi-phase') ?? null,
      portCompatibility: document.querySelector('.flow-port-tooltip')
        ?.getAttribute('data-compatibility') ?? null,
      operatorFlyoutPresent: Boolean(document.querySelector('[data-capability="operator-flyout"], #operator-group-flyout')),
      finalDecisionEntryPresent: Boolean(document.querySelector('[data-capability="final-decision"], [data-testid="final-decision"]')),
      globalVariablesEntryPresent: Boolean(document.querySelector('[data-capability="global-variables"], [data-testid="global-variables"]'))
    },
    workspaceGeometry: (() => {
      const rect = (selector: string) => {
        const bounds = document.querySelector(selector)?.getBoundingClientRect();
        return bounds
          ? { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height }
          : null;
      };
      const overflow = (selector: string) => {
        const element = document.querySelector(selector);
        return element
          ? {
              horizontal: element.scrollWidth - element.clientWidth,
              vertical: element.scrollHeight - element.clientHeight
            }
          : null;
      };
      const styles = (selector: string) => {
        const element = document.querySelector(selector);
        if (!element) return null;
        const style = getComputedStyle(element);
        return {
          display: style.display,
          position: style.position,
          width: style.width,
          height: style.height,
          minHeight: style.minHeight,
          gridTemplateColumns: style.gridTemplateColumns,
          gridTemplateRows: style.gridTemplateRows,
          transform: style.transform,
          zoom: style.zoom
        };
      };
      return {
        appRoot: rect('#app'),
        productLayout: rect('.product-layout'),
        productWorkspace: rect('.product-layout__workspace'),
        productContent: rect('.product-layout__content--workspace'),
        computedStyles: {
          appRoot: styles('#app'),
          productLayout: styles('.product-layout'),
          productWorkspace: styles('.product-layout__workspace'),
          productContent: styles('.product-layout__content--workspace')
        },
        productRail: rect('.operator-rail'),
        productTopbar: rect('.product-layout--workspace .product-layout__topbar'),
        workspaceToolbar: rect('.workspace-shell__toolbar'),
        operatorRail: rect('.operator-rail'),
        operatorSearch: rect('[data-testid="operator-search"]'),
        operatorCategory: rect('[data-testid="operator-category"]'),
        operatorFirstItem: rect('.operator-item'),
        flowToolbar: rect('.flow-canvas-surface__toolbar'),
        canvasStage: rect('.flow-canvas-surface__stage'),
        preview: rect('.preview-panel'),
        previewDetails: rect('.preview-panel__details'),
        imageToolbar: rect('.image-viewport__toolbar'),
        imageStage: rect('.image-viewport__stage'),
        previewSplitter: rect('[data-workspace-splitter="preview"]'),
        inspector: rect('.inspector-panel'),
        inspectorBody: rect('.inspector-panel__body'),
        inspectorFirstParameter: rect('.parameter-editor'),
        inspectorSplitter: rect('[data-workspace-splitter="inspector"]'),
        workspaceStatusbar: rect('.workspace-shell__statusbar'),
        saveCommand: rect('[data-testid="workspace-save"]'),
        runCommand: rect('[data-testid="workspace-run"]'),
        inspectorWidth: document.querySelector('.flow-workspace')
          ?.getAttribute('data-inspector-width') ?? null,
        inspectorMinWidth: document.querySelector('.flow-workspace')
          ?.getAttribute('data-inspector-min-width') ?? null,
        inspectorMaxWidth: document.querySelector('.flow-workspace')
          ?.getAttribute('data-inspector-max-width') ?? null,
        previewHeight: document.querySelector('.flow-workspace')
          ?.getAttribute('data-preview-height') ?? null,
        previewMinHeight: document.querySelector('.flow-workspace')
          ?.getAttribute('data-preview-min-height') ?? null,
        previewMaxHeight: document.querySelector('.flow-workspace')
          ?.getAttribute('data-preview-max-height') ?? null,
        previewCollapsed: document.querySelector('.flow-workspace')
          ?.getAttribute('data-preview-collapsed') ?? null,
        previewWidth: document.querySelector('.flow-workspace')
          ?.getAttribute('data-preview-width') ?? null,
        previewMinWidth: document.querySelector('.flow-workspace')
          ?.getAttribute('data-preview-min-width') ?? null,
        previewMaxWidth: document.querySelector('.flow-workspace')
          ?.getAttribute('data-preview-max-width') ?? null,
        internalOverflow: {
          operatorRail: overflow('.operator-rail__categories'),
          flowToolbar: overflow('.flow-canvas-surface__toolbar'),
          inspectorBody: overflow('.inspector-panel__body'),
          previewBody: overflow('.preview-panel__body'),
          previewDetails: overflow('.preview-panel__details'),
          imageToolbar: overflow('.image-viewport__toolbar')
        }
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
