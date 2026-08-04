const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const httpServer = require('http-server');

const uiTestsRoot = path.resolve(__dirname, '..', '..');
const repositoryRoot = path.resolve(uiTestsRoot, '..', '..', '..');
const studioUiRoot = path.join(
  repositoryRoot,
  'ClearVision.Product',
  'src',
  'ClearVision.Product.Desktop',
  'StudioUI'
);
const host = (process.env.CV_UI_HOST || '127.0.0.1').trim();
const port = (process.env.CV_UI_PORT || '5177').trim();
const configuredWebRoot = process.env.CV_UI_WEB_ROOT?.trim();
const evidencePhase = (process.env.CV_STUDIO_UI_EVIDENCE_PHASE || 'f01').trim().toLowerCase();
if (!['f01', 'f02', 'f03', 'f04', 'f05', 'f06', 'f07', 'f09'].includes(evidencePhase)) {
  throw new Error(`Unsupported StudioUI evidence phase: ${evidencePhase}`);
}
const fixtureRoot = path.join(
  repositoryRoot,
  '.tmp',
  'studio-ui-next',
  evidencePhase,
  'browser-fixture'
);
const allowedTemporaryRoot = path.join(repositoryRoot, '.tmp') + path.sep;
if (!fixtureRoot.startsWith(allowedTemporaryRoot)) {
  throw new Error('StudioUI browser fixture must remain under the repository .tmp directory.');
}
const webRoot = configuredWebRoot
  ? path.resolve(configuredWebRoot)
  : path.join(fixtureRoot, 'wwwroot');
const studioDist = path.join(webRoot, 'studio');

if (!configuredWebRoot) {
  fs.rmSync(fixtureRoot, { recursive: true, force: true });
  fs.mkdirSync(studioDist, { recursive: true });

  const buildExecutable = process.platform === 'win32'
    ? (process.env.ComSpec || 'cmd.exe')
    : 'npm';
  const buildArguments = process.platform === 'win32'
    ? ['/d', '/s', '/c', 'npm.cmd run build']
    : ['run', 'build'];
  const build = spawnSync(buildExecutable, buildArguments, {
    cwd: studioUiRoot,
    env: {
      ...process.env,
      VITE_OUT_DIR: studioDist,
      CONFIGURATION: 'Debug',
      TARGET_FRAMEWORK: 'net8.0-windows'
    },
    encoding: 'utf8',
    stdio: 'inherit'
  });

  if (build.error) {
    throw build.error;
  }

  if (build.status !== 0) {
    process.exit(build.status ?? 1);
  }
}

const server = httpServer.createServer({
  root: webRoot,
  cache: -1,
  logFn: () => {}
});
let closing = false;

function stop(exitCode = 0) {
  if (closing) return;
  closing = true;
  const forceExitTimer = setTimeout(() => process.exit(exitCode), 5_000);
  forceExitTimer.unref();

  server.server.closeAllConnections?.();
  server.server.close(error => {
    clearTimeout(forceExitTimer);
    if (error) {
      console.error(error);
      process.exit(1);
      return;
    }
    process.exit(exitCode);
  });
}

server.server.on('error', error => {
  console.error(error);
  process.exit(1);
});

process.once('SIGINT', () => stop());
process.once('SIGTERM', () => stop());
process.once('exit', () => {
  if (!closing) server.server.close();
});

server.listen(Number(port), host);
