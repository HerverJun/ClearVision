const childProcess = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('@playwright/test');

const repoRoot = path.resolve(__dirname, '../../../../../..');

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function requiredEnvironment(name) {
  const value = String(process.env[name] || '').trim();
  assert(value, `${name} is required.`);
  return value;
}

function safeFileName(value) {
  return String(value).replace(/[^a-z0-9_.-]+/gi, '-');
}

async function connectToDesktopWebView2(cdpPort) {
  const response = await fetch(`http://127.0.0.1:${cdpPort}/json/version`);
  assert(response.ok, `CDP version endpoint returned ${response.status}.`);
  const version = await response.json();
  assert(version.webSocketDebuggerUrl, 'CDP version response did not expose webSocketDebuggerUrl.');
  const browser = await chromium.connectOverCDP(version.webSocketDebuggerUrl);
  const context = browser.contexts()[0];
  assert(context, 'WebView2 CDP connection exposed no browser context.');
  const page = context.pages()[0];
  assert(page, 'WebView2 CDP connection exposed no page.');
  return { browser, context, page, version };
}

function captureRuntimeErrors(page) {
  const consoleErrors = [];
  const pageErrors = [];
  const requestFailures = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => pageErrors.push(error?.stack || error?.message || String(error)));
  page.on('requestfailed', request => {
    requestFailures.push({
      url: request.url(),
      method: request.method(),
      errorText: request.failure()?.errorText || 'unknown'
    });
  });
  return { consoleErrors, pageErrors, requestFailures };
}

async function seedAuthenticatedSession(page, webPort, token, user) {
  assert(Number.isInteger(webPort) && webPort > 0, 'A valid Web port is required.');
  assert(token, 'CV_SMOKE_TOKEN is required.');
  assert(user, 'CV_SMOKE_USER is required.');
  const expectedOrigin = `http://localhost:${webPort}`;
  await page.waitForURL(url => url.origin === expectedOrigin, { timeout: 45_000 });
  const seedUrl = `${expectedOrigin}/__clearvision_test_auth_seed__`;
  const seedRoute = route => route.fulfill({
    status: 200,
    contentType: 'text/html; charset=utf-8',
    body: '<!doctype html><html><head><link rel="icon" href="data:,">' +
      '<title>ClearVision authentication seed</title></head><body></body></html>'
  });
  await page.route(seedUrl, seedRoute);
  try {
    await page.goto(seedUrl, {
      waitUntil: 'domcontentloaded',
      timeout: 45_000
    });
  } finally {
    await page.unroute(seedUrl, seedRoute);
  }
  await page.evaluate(({ authToken, authUser }) => {
    sessionStorage.setItem('cv_auth_token', authToken);
    sessionStorage.setItem('cv_current_user', authUser);
    localStorage.setItem('cv_welcome_shown', 'true');
  }, { authToken: token, authUser: user });
}

async function readApi(webPort, requestPath, token = '') {
  const response = await fetch(`http://127.0.0.1:${webPort}${requestPath}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined
  });
  const text = await response.text();
  if (!response.ok) {
    throw new Error(`GET ${requestPath} returned ${response.status}: ${text.slice(0, 500)}`);
  }
  let payload;
  if (text) {
    try { payload = JSON.parse(text); } catch { payload = text; }
  }
  return {
    path: requestPath,
    status: response.status,
    contentType: response.headers.get('content-type') || '',
    shape: Array.isArray(payload)
      ? { kind: 'array', count: payload.length }
      : payload && typeof payload === 'object'
        ? { kind: 'object', keys: Object.keys(payload).sort().slice(0, 40) }
        : { kind: typeof payload }
  };
}

function parsePngDimensions(buffer) {
  assert(Buffer.isBuffer(buffer) && buffer.length >= 24, 'Screenshot did not contain a PNG header.');
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

async function readBrowserDpiEvidence(page, context) {
  const session = await context.newCDPSession(page);
  try {
    await session.send('Performance.enable');
    const [layoutMetrics, performanceMetrics, heapUsage, screenshot] = await Promise.all([
      session.send('Page.getLayoutMetrics'),
      session.send('Performance.getMetrics'),
      session.send('Runtime.getHeapUsage'),
      page.screenshot({ type: 'png' })
    ]);
    const js = await page.evaluate(() => ({
      devicePixelRatio: window.devicePixelRatio,
      innerWidth: window.innerWidth,
      innerHeight: window.innerHeight,
      outerWidth: window.outerWidth,
      outerHeight: window.outerHeight,
      screenWidth: window.screen.width,
      screenHeight: window.screen.height,
      visualViewport: window.visualViewport ? {
        width: window.visualViewport.width,
        height: window.visualViewport.height,
        scale: window.visualViewport.scale
      } : null
    }));
    return {
      js,
      screenshotPixels: parsePngDimensions(screenshot),
      cdp: {
        cssLayoutViewport: layoutMetrics.cssLayoutViewport,
        cssVisualViewport: layoutMetrics.cssVisualViewport,
        cssContentSize: layoutMetrics.cssContentSize,
        performance: Object.fromEntries(
          performanceMetrics.metrics.map(metric => [metric.name, metric.value])
        ),
        heapUsage
      }
    };
  } finally {
    await session.detach();
  }
}

function runDesktopRuntimeProbe(executablePath) {
  const probeScript = process.env.CV_NATIVE_DPI_PROBE
    ? path.resolve(process.env.CV_NATIVE_DPI_PROBE)
    : path.join(repoRoot, 'scripts', 'studio-ui-next', 'Get-DesktopRuntimeProbe.ps1');
  assert(fs.existsSync(probeScript), `Desktop runtime probe was not found: ${probeScript}`);
  const output = childProcess.execFileSync('powershell.exe', [
    '-NoLogo',
    '-NoProfile',
    '-NonInteractive',
    '-ExecutionPolicy', 'Bypass',
    '-File', probeScript,
    '-ExecutablePath', executablePath
  ], { encoding: 'utf8', windowsHide: true });
  return JSON.parse(output.trim());
}

async function waitForDoubleAnimationFrame(page) {
  await page.evaluate(() => new Promise(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(resolve));
  }));
}

function writeJsonEvidence(evidenceDirectory, fileName, value) {
  fs.mkdirSync(evidenceDirectory, { recursive: true });
  const outputPath = path.join(evidenceDirectory, safeFileName(fileName));
  fs.writeFileSync(outputPath, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
  return outputPath;
}

module.exports = {
  assert,
  captureRuntimeErrors,
  connectToDesktopWebView2,
  readApi,
  readBrowserDpiEvidence,
  requiredEnvironment,
  repoRoot,
  runDesktopRuntimeProbe,
  safeFileName,
  seedAuthenticatedSession,
  waitForDoubleAnimationFrame,
  writeJsonEvidence
};
