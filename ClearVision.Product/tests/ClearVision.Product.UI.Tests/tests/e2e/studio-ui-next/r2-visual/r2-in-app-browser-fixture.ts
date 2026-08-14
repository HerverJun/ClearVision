import type { Page, Request, Route } from '@playwright/test';
import {
  auditF02Request,
  fulfillF02Json,
  installF02BrowserStartup,
  installF02VisualPreferences,
  type F02MethodAuditEntry
} from '../f02-browser-fixture';
import { findR2RouteState, r2FixtureContract } from './r2-visual-fixture';

export interface R2BrowserScenarioOptions {
  readonly routeStateId: string;
  readonly theme: 'light' | 'dark';
  readonly density: 'compact' | 'comfortable';
  readonly reducedMotion: boolean;
}

export interface R2RuntimeAudit {
  readonly requests: F02MethodAuditEntry[];
  readonly consoleErrors: string[];
  readonly pageErrors: string[];
  readonly failedRequests: string[];
  readonly httpErrors: string[];
  readonly expectedHttpErrors: string[];
  readonly observedExpectedHttpErrors: string[];
  readonly unexpectedWrites: string[];
}

export async function installR2BrowserScenario(page: Page, options: R2BrowserScenarioOptions): Promise<void> {
  const routeState = findR2RouteState(options.routeStateId);
  await installF02VisualPreferences(page, options.theme, options.density);
  await installF02BrowserStartup(page, routeState.featureFlags);
  await page.addInitScript(({ fixture, state, reducedMotion }) => {
    Object.defineProperty(window, '__R2_VISUAL_FIXTURE__', {
      value: Object.freeze({ fixture, state, reducedMotion }),
      writable: false,
      configurable: false
    });
  }, { fixture: r2FixtureContract, state: routeState, reducedMotion: options.reducedMotion });
}

export async function installR2ReadOnlyDispatcher(
  page: Page,
  role: 'Operator' | 'Engineer' | 'Admin' = 'Engineer',
  expectedHttpErrors: readonly Readonly<{ method: string; path: `/${string}`; status: number }>[] = []
): Promise<R2RuntimeAudit> {
  const invalidHttpError = expectedHttpErrors.find(entry =>
    !/^[A-Z]+$/.test(entry.method) || !entry.path.startsWith('/') ||
    !Number.isInteger(entry.status) || entry.status < 400 || entry.status > 599);
  if (invalidHttpError) throw new Error(`Invalid expected HTTP error: ${JSON.stringify(invalidHttpError)}.`);
  const expectedErrors = new Set(expectedHttpErrors.map(entry =>
    `${entry.method} ${entry.path}: ${entry.status}`));
  const audit: R2RuntimeAudit = {
    requests: [],
    consoleErrors: [],
    pageErrors: [],
    failedRequests: [],
    httpErrors: [],
    expectedHttpErrors: [...expectedErrors],
    observedExpectedHttpErrors: [],
    unexpectedWrites: []
  };
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const content = message.text();
    const statusMatch = content.match(/status of (\d{3})/i);
    const sourcePath = message.location().url
      ? new URL(message.location().url).pathname
      : null;
    if (statusMatch && sourcePath && [...expectedErrors].some(entry =>
      entry.endsWith(`${sourcePath}: ${statusMatch[1]}`))) return;
    audit.consoleErrors.push(content);
  });
  page.on('pageerror', error => audit.pageErrors.push(error.stack ?? error.message));
  page.on('requestfailed', request => audit.failedRequests.push(`${request.method()} ${new URL(request.url()).pathname}: ${request.failure()?.errorText ?? 'unknown'}`));
  page.on('response', response => {
    if (response.status() < 400) return;
    const request = response.request();
    const signature = `${request.method()} ${new URL(response.url()).pathname}: ${response.status()}`;
    if (expectedErrors.has(signature)) {
      audit.observedExpectedHttpErrors.push(signature);
      return;
    }
    audit.httpErrors.push(signature);
  });

  await page.addInitScript(({ selectedRole }) => {
    if (!Object.hasOwn(window, '__CLEARVISION_STARTUP__')) {
      Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
        value: Object.freeze({
          schemaVersion: 1,
          uiKind: 'studio-ui',
          hostKind: 'browser-test',
          apiBaseUrl: `${window.location.origin}/api`,
          studioUiBasePath: '/studio/',
          startupProfile: 'NEXT_DEFAULT',
          profileAllowedRoles: Object.freeze(['Admin', 'Engineer', 'Operator']),
          featureFlags: Object.freeze({})
        }),
        writable: false,
        configurable: false
      });
    }
    Object.defineProperty(window, '__R2_PUBLIC_ROLE__', {
      value: selectedRole,
      writable: false,
      configurable: false
    });
  }, { selectedRole: role });

  await page.route('**/health', async route => {
    audit.requests.push(auditF02Request(route.request()));
    await fulfill(route, 200, {
      status: 'Healthy',
      port: 5177,
      service: 'ClearVision 本地服务',
      version: '2.8.0-r2'
    });
  });
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.requests.push(auditF02Request(request));
    if (!isReadOnly(request) && !isAuthLifecycleWrite(request, url.pathname)) {
      audit.unexpectedWrites.push(`${request.method()} ${url.pathname}`);
      await fulfill(route, 409, { error: 'R2 fixture forbids unowned writes.' });
      return;
    }
    if (url.pathname === '/api/auth/setup-status') {
      await fulfill(route, 200, {
        requiresInitialAdminSetup: false,
        usernameMinLength: 3,
        passwordMinLength: 6,
        requiresUppercase: false,
        requiresLowercase: false,
        requiresDigit: false
      });
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfill(route, 200, { userId: 'r2-fixture-user', username: '现场工程师', role });
      return;
    }
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') {
      const body = request.postDataJSON() as { username?: string; password?: string };
      if (!body.username || body.password !== 'ClearVision-R2!') {
        await fulfill(route, 401, { error: '用户名或密码错误' });
        return;
      }
      await fulfill(route, 200, { token: 'r2-browser-fixture-token', user: { username: body.username, role } });
      return;
    }
    if (url.pathname === '/api/auth/logout' && request.method() === 'POST') {
      await fulfill(route, 200, { loggedOut: true });
      return;
    }
    await fulfill(route, 404, { error: 'R2FixtureRouteUnavailable', path: url.pathname });
  });
  return audit;
}

async function fulfill(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, 'r2-route-state.v1');
}

function isReadOnly(request: Request): boolean {
  return request.method() === 'GET' || request.method() === 'HEAD';
}

function isAuthLifecycleWrite(request: Request, path: string): boolean {
  return request.method() === 'POST' && (path === '/api/auth/login' || path === '/api/auth/logout');
}

export async function collectR2DomReport(
  page: Page,
  requiredCriticalActions: readonly string[] = []
): Promise<Readonly<Record<string, unknown>>> {
  return page.evaluate(requiredSelectors => {
    const root = document.documentElement;
    const body = document.body;
    const active = document.activeElement as HTMLElement | null;
    const visible = (element: Element): boolean => {
      const style = getComputedStyle(element);
      const box = element.getBoundingClientRect();
      return style.visibility !== 'hidden' && style.display !== 'none' && box.width > 0 && box.height > 0;
    };
    const selectors = requiredSelectors.length > 0
      ? requiredSelectors
      : ['[data-r2-critical-action]', 'button[data-testid*="save"]', 'button[data-testid*="run"]', 'button[data-testid*="stop"]'];
    const criticalActionSelector = selectors.join(', ');
    const clippedByAncestor = (element: HTMLElement, box: DOMRect): boolean => {
      let ancestor = element.parentElement;
      while (ancestor && ancestor !== body) {
        const style = getComputedStyle(ancestor);
        const ancestorBox = ancestor.getBoundingClientRect();
        if ((style.overflowX === 'hidden' || style.overflowX === 'clip') &&
            (box.right <= ancestorBox.left || box.left >= ancestorBox.right)) return true;
        if ((style.overflowY === 'hidden' || style.overflowY === 'clip') &&
            (box.bottom <= ancestorBox.top || box.top >= ancestorBox.bottom)) return true;
        ancestor = ancestor.parentElement;
      }
      return false;
    };
    const documentHeight = Math.max(root.scrollHeight, body.scrollHeight);
    const criticalActions = Array.from(document.querySelectorAll<HTMLElement>(criticalActionSelector))
      .filter(visible)
      .map(element => {
        const box = element.getBoundingClientRect();
        const documentTop = box.top + scrollY;
        const documentBottom = box.bottom + scrollY;
        const testId = element.dataset.testid ?? null;
        const selector = selectors.find(candidate => element.matches(candidate)) ?? null;
        const style = getComputedStyle(element);
        const disabled = element.matches(':disabled') || element.getAttribute('aria-disabled') === 'true';
        const centerX = Math.min(innerWidth - 1, Math.max(0, box.left + box.width / 2));
        const centerY = Math.min(innerHeight - 1, Math.max(0, box.top + box.height / 2));
        const hit = box.right > 0 && box.bottom > 0 && box.left < innerWidth && box.top < innerHeight
          ? document.elementFromPoint(centerX, centerY)
          : null;
        return {
          selector,
          testId,
          text: element.textContent?.trim() ?? '',
          box: { x: box.x, y: box.y, width: box.width, height: box.height },
          truncated: element.scrollWidth > element.clientWidth || element.scrollHeight > element.clientHeight,
          inViewport: box.right > 0 && box.bottom > 0 && box.left < innerWidth && box.top < innerHeight,
          reachable: box.right > 0 && box.left < innerWidth && documentBottom > 0 &&
            documentTop < documentHeight && !clippedByAncestor(element, box),
          enabled: !disabled && style.pointerEvents !== 'none',
          unobscured: hit === null || hit === element || element.contains(hit) || hit.contains(element)
        };
      });
    const main = document.querySelector('main');
    const mainBox = main?.getBoundingClientRect();
    return {
      url: location.href,
      viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio },
      theme: root.dataset.theme ?? null,
      density: root.dataset.density ?? null,
      reducedMotion: root.dataset.reducedMotion ?? null,
      focus: active ? { tagName: active.tagName, id: active.id, testId: active.dataset.testid ?? null } : null,
      landmarks: {
        main: document.querySelectorAll('main').length,
        nav: document.querySelectorAll('nav').length,
        dialogs: document.querySelectorAll('[role="dialog"]').length
      },
      mainBox: mainBox ? { x: mainBox.x, y: mainBox.y, width: mainBox.width, height: mainBox.height } : null,
      horizontalOverflow: Math.max(root.scrollWidth - root.clientWidth, body.scrollWidth - body.clientWidth),
      pageScroll: { x: scrollX, y: scrollY },
      criticalActions
    };
  }, [...requiredCriticalActions]);
}
