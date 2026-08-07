'use strict';

const { spawn } = require('node:child_process');
const { request } = require('node:http');
const path = require('node:path');

const startupTimeoutMs = 180_000;
const shutdownTimeoutMs = 5_000;

module.exports = async function studioUiNextGlobalSetup() {
  const uiTestsRoot = path.resolve(__dirname, '..', '..');
  const serverPath = path.join(__dirname, 'studio-ui-next-server.cjs');
  const host = (process.env.CV_UI_HOST || '127.0.0.1').trim();
  const port = Number(process.env.CV_UI_PORT || '5177');
  const child = spawn(process.execPath, [serverPath], {
    cwd: uiTestsRoot,
    env: process.env,
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true
  });
  let stderr = '';
  child.stderr.setEncoding('utf8');
  child.stderr.on('data', chunk => {
    stderr += chunk;
    if (stderr.length > 8_000) stderr = stderr.slice(-8_000);
  });
  child.stdout.resume();

  try {
    await waitForReady(child, host, port, '/studio/index.html');
  } catch (error) {
    await stopChild(child);
    const detail = stderr.trim() ? `\n${stderr.trim()}` : '';
    throw new Error(`${error instanceof Error ? error.message : String(error)}${detail}`);
  }

  return async function studioUiNextGlobalTeardown() {
    await stopChild(child);
  };
};

async function waitForReady(child, host, port, readyPath) {
  const deadline = Date.now() + startupTimeoutMs;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(`StudioUI fixture exited before readiness with code ${child.exitCode}.`);
    }
    try {
      const response = await get(host, port, readyPath);
      if (response.statusCode >= 200 && response.statusCode < 400) return;
    } catch {
      // The fixture may still be building the StudioUI bundle.
    }
    await delay(100);
  }
  throw new Error(`StudioUI fixture did not become ready within ${startupTimeoutMs}ms.`);
}

function get(host, port, requestPath) {
  return new Promise((resolve, reject) => {
    const client = request({ host, port, path: requestPath, method: 'GET' }, response => {
      response.resume();
      response.once('end', () => resolve(response));
    });
    client.once('error', reject);
    client.end();
  });
}

async function stopChild(child) {
  if (child.exitCode !== null) return;
  try {
    child.stdin.end();
  } catch {
    // The launcher may have already closed stdin.
  }
  const exited = await waitForExit(child, shutdownTimeoutMs);
  if (exited) return;
  child.kill();
  await waitForExit(child, 1_000);
}

function waitForExit(child, timeoutMs) {
  if (child.exitCode !== null || child.signalCode !== null) {
    return Promise.resolve(true);
  }
  return new Promise(resolve => {
    const timer = setTimeout(() => {
      child.removeListener('exit', onExit);
      resolve(false);
    }, timeoutMs);
    timer.unref();
    function onExit() {
      clearTimeout(timer);
      resolve(true);
    }
    child.once('exit', onExit);
  });
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}
