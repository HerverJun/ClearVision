import { createHash, randomBytes } from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { get } from 'node:http';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  computeCandidateContentHash,
  computeFixtureHash,
  findRepositoryRoot,
  validateBrowserSession
} from './validate-r2-evidence.mjs';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = findRepositoryRoot(scriptDirectory);
const uiTestsRoot = resolve(repositoryRoot, 'ClearVision.Product/tests/ClearVision.Product.UI.Tests');
const serverPath = resolve(uiTestsRoot, 'tests/support/studio-ui-next-server.cjs');
const phaseRoot = resolve(repositoryRoot, '.tmp/studio-ui-next/r2');
const fixtureRoot = resolve(phaseRoot, 'browser-fixture');
const webRoot = resolve(fixtureRoot, 'wwwroot');
const statePath = resolve(phaseRoot, 'r2-browser-session.json');
const logPath = resolve(phaseRoot, 'r2-browser-session.log');

export function getSessionPaths() {
  return Object.freeze({ repositoryRoot, uiTestsRoot, serverPath, phaseRoot, fixtureRoot, webRoot, statePath, logPath });
}

export function validateSessionOwnership(document, cleanupToken) {
  const errors = validateBrowserSession(document);
  if (document.serverOwner !== 'R2_BROWSER_FIXTURE_SESSION') errors.push('The session is not owned by R2_BROWSER_FIXTURE_SESSION.');
  if (cleanupToken && document.cleanupToken !== cleanupToken) errors.push('The cleanup token does not match this session.');
  return errors;
}

function readState() {
  if (!existsSync(statePath)) return null;
  return JSON.parse(readFileSync(statePath, 'utf8'));
}

function writeState(document) {
  mkdirSync(phaseRoot, { recursive: true });
  writeFileSync(statePath, `${JSON.stringify(document, null, 2)}\n`, 'utf8');
}

function processExists(pid) {
  if (!Number.isInteger(pid) || pid < 1) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function listenerBelongsToProcess(port, pid) {
  if (process.platform !== 'win32') return true;
  try {
    const output = execFileSync('netstat.exe', ['-ano', '-p', 'tcp'], {
      encoding: 'utf8',
      windowsHide: true
    });
    return output.split(/\r?\n/).some(line => {
      const columns = line.trim().split(/\s+/);
      if (columns.length < 5 || columns[0].toUpperCase() !== 'TCP') return false;
      const localAddress = columns[1];
      const state = columns[3]?.toUpperCase();
      const owningPid = Number.parseInt(columns.at(-1) ?? '', 10);
      const separator = localAddress.lastIndexOf(':');
      const localPort = Number.parseInt(localAddress.slice(separator + 1), 10);
      return localPort === port && state === 'LISTENING' && owningPid === pid;
    });
  } catch {
    return false;
  }
}

function parsePort(value) {
  const port = Number.parseInt(value ?? '', 10);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error(`Invalid R2 fixture port: ${value}.`);
  return port;
}

function safeBatch(value) {
  const batch = value?.trim() ?? '';
  if (!/^[a-z0-9][a-z0-9_.-]{2,79}$/i.test(batch)) throw new Error('Batch must contain 3-80 safe characters.');
  return batch;
}

async function requestJson(url, timeoutMs = 2_000) {
  return new Promise((resolvePromise, reject) => {
    const request = get(url, { timeout: timeoutMs }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => {
        if (!response.statusCode || response.statusCode < 200 || response.statusCode >= 300) {
          reject(new Error(`GET ${url} returned ${response.statusCode}.`));
          return;
        }
        try {
          resolvePromise(JSON.parse(Buffer.concat(chunks).toString('utf8')));
        } catch (error) {
          reject(error);
        }
      });
    });
    request.once('timeout', () => request.destroy(new Error(`GET ${url} timed out.`)));
    request.once('error', reject);
  });
}

async function waitForIndex(url, pid, timeoutMs = 180_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (!processExists(pid)) throw new Error(`R2 fixture process ${pid} exited before readiness.`);
    try {
      await new Promise((resolvePromise, reject) => {
        const request = get(url, { timeout: 2_000 }, response => {
          response.resume();
          response.once('end', () => response.statusCode >= 200 && response.statusCode < 400
            ? resolvePromise()
            : reject(new Error(`Unexpected status ${response.statusCode}.`)));
        });
        request.once('timeout', () => request.destroy(new Error('Readiness request timed out.')));
        request.once('error', reject);
      });
      return;
    } catch {
      await new Promise(resolvePromise => setTimeout(resolvePromise, 100));
    }
  }
  throw new Error(`R2 fixture did not become ready within ${timeoutMs}ms.`);
}

async function waitForExit(pid, timeoutMs = 5_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (!processExists(pid)) return true;
    await new Promise(resolvePromise => setTimeout(resolvePromise, 50));
  }
  return !processExists(pid);
}

async function waitForEndpointStop(document, timeoutMs = 5_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      await requestJson(`http://127.0.0.1:${document.port}/studio/.r2-session.json`, 250);
    } catch {
      return true;
    }
    await new Promise(resolvePromise => setTimeout(resolvePromise, 50));
  }
  return false;
}

export async function startSession({ port = 5177, batch = 'r2.0-baseline' } = {}) {
  const normalizedPort = parsePort(String(port));
  const normalizedBatch = safeBatch(batch);
  const current = readState();
  if (current?.status === 'READY' && processExists(current.pid)) {
    const marker = await verifyLiveOwnership(current).catch(() => null);
    if (marker && current.port === normalizedPort) return current;
    throw new Error(`R2 fixture state references live PID ${current.pid}, but its marker or port ownership cannot be verified. Refusing to replace it.`);
  }
  mkdirSync(phaseRoot, { recursive: true });
  if (existsSync(statePath)) rmSync(statePath, { force: true });
  const logDescriptor = openSync(logPath, 'a');
  const child = spawn(process.execPath, [serverPath], {
    cwd: uiTestsRoot,
    detached: true,
    windowsHide: true,
    stdio: ['ignore', logDescriptor, logDescriptor],
    env: {
      ...process.env,
      CV_UI_HOST: '127.0.0.1',
      CV_UI_PORT: String(normalizedPort),
      CV_STUDIO_UI_EVIDENCE_PHASE: 'r2',
      CV_UI_PERSISTENT_SESSION: 'true'
    }
  });
  child.unref();
  closeSync(logDescriptor);
  const readyUrl = `http://127.0.0.1:${normalizedPort}/studio/index.html`;
  try {
    await waitForIndex(readyUrl, child.pid);
    const sourceSha = execGit(['rev-parse', 'HEAD']).trim().toLowerCase();
    const candidateContentHash = computeCandidateContentHash(repositoryRoot);
    const fixtureHash = computeFixtureHash(repositoryRoot);
    const cleanupToken = randomBytes(24).toString('hex');
    const document = {
      schemaVersion: 'r2-in-app-browser.v1',
      status: 'READY',
      readyUrl,
      pid: child.pid,
      port: normalizedPort,
      serverOwner: 'R2_BROWSER_FIXTURE_SESSION',
      sourceSha,
      candidateContentHash,
      fixtureHash,
      createdByBatch: normalizedBatch,
      cleanupToken,
      fixtureRoot,
      startedAtUtc: new Date().toISOString()
    };
    const errors = validateSessionOwnership(document);
    if (errors.length) throw new Error(errors.join('\n'));
    const markerPath = resolve(webRoot, 'studio', '.r2-session.json');
    mkdirSync(dirname(markerPath), { recursive: true });
    writeFileSync(markerPath, `${JSON.stringify(document, null, 2)}\n`, 'utf8');
    writeState(document);
    const marker = await verifyMarker(document);
    if (marker.cleanupToken !== cleanupToken) throw new Error('R2 fixture marker ownership did not round-trip.');
    return document;
  } catch (error) {
    try { process.kill(child.pid, 'SIGTERM'); } catch { /* The failed child may already be gone. */ }
    await waitForExit(child.pid);
    throw error;
  }
}

async function verifyMarker(document) {
  const markerUrl = `http://127.0.0.1:${document.port}/studio/.r2-session.json`;
  const marker = await requestJson(markerUrl);
  const errors = validateSessionOwnership(marker, document.cleanupToken);
  if (errors.length) throw new Error(errors.join('\n'));
  return marker;
}

async function verifyLiveOwnership(document) {
  if (!processExists(document.pid)) throw new Error(`R2 fixture PID ${document.pid} is not running.`);
  const marker = await verifyMarker(document);
  if (!listenerBelongsToProcess(document.port, document.pid)) {
    throw new Error(`Port ${document.port} is not owned by R2 fixture PID ${document.pid}.`);
  }
  return marker;
}

export async function statusSession() {
  const document = readState();
  if (!document) return Object.freeze({ status: 'STOPPED', statePath });
  const errors = validateSessionOwnership(document);
  if (errors.length) return Object.freeze({ ...document, status: 'FAILED', errors });
  if (document.status === 'STOPPED') return document;
  if (!processExists(document.pid)) return Object.freeze({ ...document, status: 'STOPPED', stoppedAtUtc: new Date().toISOString() });
  try {
    await verifyLiveOwnership(document);
    return document;
  } catch (error) {
    return Object.freeze({ ...document, status: 'FAILED', errors: [error instanceof Error ? error.message : String(error)] });
  }
}

export async function stopSession({ cleanupToken } = {}) {
  const document = readState();
  if (!document) return Object.freeze({ status: 'STOPPED', statePath });
  const errors = validateSessionOwnership(document, cleanupToken);
  if (errors.length) throw new Error(errors.join('\n'));
  if (document.status === 'STOPPED') return document;
  if (processExists(document.pid)) {
    await verifyLiveOwnership(document);
    process.kill(document.pid, 'SIGTERM');
    const exited = await waitForExit(document.pid);
    if (!exited) throw new Error(`R2 fixture PID ${document.pid} did not stop within 5 seconds.`);
  }
  const endpointStopped = await waitForEndpointStop(document);
  if (!endpointStopped) throw new Error(`R2 fixture marker on port ${document.port} remained reachable after stop.`);
  const stopped = { ...document, status: 'STOPPED', stoppedAtUtc: new Date().toISOString() };
  writeState(stopped);
  return stopped;
}

function execGit(args) {
  return execFileSync('git', args, { cwd: repositoryRoot, encoding: 'utf8' });
}

function parseArguments(argv) {
  const [command, ...rest] = argv;
  const options = {};
  for (let index = 0; index < rest.length; index += 1) {
    const argument = rest[index];
    if (argument === '--port') options.port = parsePort(rest[++index]);
    else if (argument === '--batch') options.batch = safeBatch(rest[++index]);
    else if (argument === '--cleanup-token') options.cleanupToken = rest[++index];
    else throw new Error(`Unknown R2 session argument: ${argument}.`);
  }
  return { command, options };
}

async function runCli() {
  const { command, options } = parseArguments(process.argv.slice(2));
  if (command === 'start') console.log(JSON.stringify(await startSession(options), null, 2));
  else if (command === 'status') console.log(JSON.stringify(await statusSession(), null, 2));
  else if (command === 'stop') console.log(JSON.stringify(await stopSession(options), null, 2));
  else throw new Error('Usage: r2-browser-fixture-session.mjs <start|status|stop> [--port N] [--batch ID] [--cleanup-token TOKEN].');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runCli().catch(error => {
    console.error(error instanceof Error ? error.stack ?? error.message : String(error));
    process.exitCode = 1;
  });
}
