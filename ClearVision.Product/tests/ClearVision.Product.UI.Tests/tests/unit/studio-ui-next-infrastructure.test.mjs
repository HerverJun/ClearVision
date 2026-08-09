import assert from 'node:assert/strict';
import { once } from 'node:events';
import { existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { spawn } from 'node:child_process';
import { request } from 'node:http';
import test from 'node:test';

const unitRoot = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(unitRoot, '..', '..', '..', '..', '..');
const desktopRoot = join(repositoryRoot, 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop');
const studioUiRoot = join(desktopRoot, 'StudioUI');
const projectPath = join(desktopRoot, 'ClearVision.Product.Desktop.csproj');
const viteConfigPath = join(studioUiRoot, 'vite.config.ts');
const browserServerPath = join(
  repositoryRoot,
  'ClearVision.Product',
  'tests',
  'ClearVision.Product.UI.Tests',
  'tests',
  'support',
  'studio-ui-next-server.cjs'
);
const webView2SmokePath = join(
  repositoryRoot,
  'ClearVision.Product',
  'tests',
  'ClearVision.Product.UI.Tests',
  'tests',
  'e2e',
  'studio-ui-next',
  'studio-ui-webview2-smoke.cjs'
);
const productPerformancePath = join(
  repositoryRoot,
  'ClearVision.Product',
  'tests',
  'ClearVision.Product.UI.Tests',
  'tests',
  'e2e',
  'studio-ui-next',
  'studio-ui-product-performance.cjs'
);
const dpiEvidencePath = join(
  repositoryRoot,
  'scripts',
  'studio-ui-next',
  'Test-StudioUiDpiEvidence.ps1'
);

test('StudioUI MSBuild inputs cover the complete Vite canonical alias closure', async () => {
  const [viteConfig, project] = await Promise.all([
    readText(viteConfigPath),
    readText(projectPath)
  ]);
  const canonicalRoots = new Map();
  const declarationPattern = /const\s+(canonical\w+)\s*=\s*resolve\(\s*desktopRoot,([\s\S]*?)\);/g;
  for (const match of viteConfig.matchAll(declarationPattern)) {
    const segments = [...match[2].matchAll(/'([^']+)'/g)].map(segment => segment[1]);
    assert.ok(segments.length > 0, `Canonical declaration ${match[1]} has no path segments.`);
    canonicalRoots.set(match[1], resolve(desktopRoot, ...segments));
  }

  const aliasRoots = [...viteConfig.matchAll(/'(@clearvision\/[^']+)'\s*:\s*(canonical\w+)/g)]
    .map(match => canonicalRoots.get(match[2]));
  assert.ok(aliasRoots.length > 0, 'Vite must expose at least one canonical alias.');
  assert.equal(aliasRoots.length, canonicalRoots.size, 'Every canonical declaration must be used by an alias.');

  const closure = new Set();
  const pending = [...aliasRoots];
  while (pending.length > 0) {
    const filePath = pending.pop();
    if (closure.has(filePath)) continue;
    assert.ok(['.js', '.mjs'].includes(extname(filePath)), `Canonical entry must be JavaScript: ${filePath}`);
    closure.add(filePath);
    const source = await readText(filePath);
    for (const specifier of collectLocalSpecifiers(source)) {
      const dependency = resolveLocalModule(dirname(filePath), specifier);
      assert.ok(dependency, `${filePath} references missing local module ${specifier}.`);
      if (dependency.startsWith(join(studioUiRoot, 'src') + '\\')) continue;
      pending.push(dependency);
    }
  }

  const normalizedProject = project.replaceAll('\\', '/').toLowerCase();
  const missing = [...closure]
    .map(filePath => relative(desktopRoot, filePath).replaceAll('\\', '/').toLowerCase())
    .filter(relativePath => !normalizedProject.includes(relativePath));
  assert.deepEqual(missing, [], `StudioUiBuildInput is missing canonical files: ${missing.join(', ')}`);
});

test('StudioUI browser fixture exits when its launcher closes stdin', async () => {
  const temporaryRoot = await mkdtemp(join(tmpdir(), 'clearvision-studio-ui-server-'));
  const webRoot = join(temporaryRoot, 'wwwroot');
  const studioRoot = join(webRoot, 'studio');
  const port = await findFreePort();
  let child;

  try {
    await mkdir(studioRoot, { recursive: true });
    await writeFile(join(studioRoot, 'index.html'), '<!doctype html><title>StudioUI fixture</title>');
    child = spawn(process.execPath, [browserServerPath], {
      cwd: join(repositoryRoot, 'ClearVision.Product', 'tests', 'ClearVision.Product.UI.Tests'),
      env: {
        ...process.env,
        CV_UI_HOST: '127.0.0.1',
        CV_UI_PORT: String(port),
        CV_UI_WEB_ROOT: webRoot,
        CV_STUDIO_UI_EVIDENCE_PHASE: 'f09'
      },
      stdio: ['pipe', 'pipe', 'pipe']
    });
    const stderr = collectOutput(child.stderr);
    const startup = await waitForHttp(port, '/studio/index.html', child);
    assert.equal(startup.statusCode, 200);

    child.stdin.end();
    const [exitCode, signal] = await waitForExit(child, 4_000);
    assert.equal(signal, null, `fixture exited by signal; stderr=${stderr()}`);
    assert.equal(exitCode, 0, `fixture exited with code ${exitCode}; stderr=${stderr()}`);
    assert.equal(await isPortOpen(port), false, 'fixture port remained open after stdin teardown.');
  } finally {
    if (child && child.exitCode === null) child.kill();
    await rm(temporaryRoot, { recursive: true, force: true });
  }
});

test('M08 f09 current-candidate evidence includes formal Workspace DPI and pointer coverage', async () => {
  const [smoke, productPerformance, dpiAudit] = await Promise.all([
    readText(webView2SmokePath),
    readText(productPerformancePath),
    readText(dpiEvidencePath)
  ]);

  assert.match(
    smoke,
    /const isFormalWorkspaceEvidence = \['f04', 'f09'\]\.includes\(navigationContract\.phase\);/
  );
  assert.doesNotMatch(smoke, /waitForLoadState\('networkidle'\)/);
  assert.match(smoke, /waitForLoadState\('domcontentloaded'\)/);
  assert.match(smoke, /'\.results-page__advanced-trigger'/);
  assert.doesNotMatch(smoke, /'\.results-page__date'/);
  assert.match(smoke, /\.filter\(element => element\.getClientRects\(\)\.length > 0\)/);
  assert.match(smoke, /resultsFilterLayout\.controls\.length === 6/);
  assert.match(smoke, /resultsFilterLayout\.maximumBottomDelta <= 1/);
  assert.doesNotMatch(smoke, /1350px WebView2 client/);
  assert.match(productPerformance, /async function waitForFunctionWithoutHandle/);
  assert.match(productPerformance, /await handle\.dispose\(\)/);
  assert.doesNotMatch(productPerformance, /await page\.waitForSelector\(/);
  assert.equal([...productPerformance.matchAll(/await page\.waitForFunction\(/g)].length, 1);
  assert.match(productPerformance, /required\('CV_NODE_COMPLETION_SIGNAL'\)/);
  assert.match(dpiAudit, /\$\_\.phase -in @\("f04", "f09"\)/);
});

async function readText(filePath) {
  return readFile(filePath, 'utf8');
}

function collectLocalSpecifiers(source) {
  const specifiers = new Set();
  const patterns = [
    /\bfrom\s*['"]([^'"]+)['"]/g,
    /\bimport\s*['"]([^'"]+)['"]/g,
    /\bimport\s*\(\s*['"]([^'"]+)['"]\s*\)/g,
    /\brequire\s*\(\s*['"]([^'"]+)['"]\s*\)/g
  ];
  for (const pattern of patterns) {
    for (const match of source.matchAll(pattern)) {
      if (match[1].startsWith('.')) specifiers.add(match[1]);
    }
  }
  return specifiers;
}

function resolveLocalModule(importerDirectory, specifier) {
  const basePath = resolve(importerDirectory, specifier);
  const candidates = [
    basePath,
    `${basePath}.js`,
    `${basePath}.mjs`,
    `${basePath}.json`,
    join(basePath, 'index.js'),
    join(basePath, 'index.mjs')
  ];
  return candidates.find(candidate => {
    try {
      return requireFile(candidate);
    } catch {
      return false;
    }
  }) ?? null;
}

function requireFile(filePath) {
  return existsSync(filePath) && statSync(filePath).isFile();
}

function findFreePort() {
  return new Promise((resolvePort, reject) => {
    const probe = createServer();
    probe.once('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      const address = probe.address();
      if (!address || typeof address === 'string') {
        probe.close(() => reject(new Error('Could not determine a free TCP port.')));
        return;
      }
      const selectedPort = address.port;
      probe.close(error => error ? reject(error) : resolvePort(selectedPort));
    });
  });
}

function collectOutput(stream) {
  let output = '';
  stream?.setEncoding('utf8');
  stream?.on('data', chunk => { output += chunk; });
  return () => output;
}

async function waitForHttp(port, path, child) {
  const deadline = Date.now() + 5_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`fixture exited before becoming ready with code ${child.exitCode}`);
    try {
      return await httpRequest(port, path);
    } catch {
      await new Promise(resolveDelay => setTimeout(resolveDelay, 50));
    }
  }
  throw new Error(`fixture did not become ready on port ${port}`);
}

function httpRequest(port, path) {
  return new Promise((resolveResponse, reject) => {
    const client = request({ host: '127.0.0.1', port, path, method: 'GET' }, response => {
      response.resume();
      response.once('end', () => resolveResponse(response));
    });
    client.once('error', reject);
    client.end();
  });
}

async function waitForExit(child, timeoutMs) {
  if (child.exitCode !== null || child.signalCode !== null) {
    return [child.exitCode, child.signalCode];
  }
  return Promise.race([
    once(child, 'exit'),
    new Promise((_, reject) => {
      const timer = setTimeout(() => reject(new Error(`fixture did not exit within ${timeoutMs}ms`)), timeoutMs);
      timer.unref();
    })
  ]);
}

function isPortOpen(port) {
  return new Promise(resolvePort => {
    const client = request({ host: '127.0.0.1', port, path: '/', method: 'GET' }, response => {
      response.resume();
      response.once('end', () => resolvePort(true));
    });
    client.once('error', () => resolvePort(false));
    client.end();
  });
}
