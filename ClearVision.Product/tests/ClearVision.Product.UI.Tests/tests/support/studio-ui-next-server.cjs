const { spawn, spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

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
const fixtureRoot = path.join(
  repositoryRoot,
  '.tmp',
  'studio-ui-next',
  'f01',
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

const serverEntry = path.join(
  uiTestsRoot,
  'node_modules',
  'http-server',
  'bin',
  'http-server'
);
const server = spawn(
  process.execPath,
  [serverEntry, webRoot, '-p', port, '-a', host, '-c-1', '--silent'],
  {
    cwd: uiTestsRoot,
    env: process.env,
    stdio: 'inherit'
  }
);

function stop(signal) {
  if (!server.killed) {
    server.kill(signal);
  }
}

process.on('SIGINT', () => stop('SIGINT'));
process.on('SIGTERM', () => stop('SIGTERM'));
process.on('exit', () => stop('SIGTERM'));

server.on('exit', code => {
  process.exit(code ?? 0);
});

server.on('error', error => {
  console.error(error);
  process.exit(1);
});
