import { readdir } from 'node:fs/promises';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

const unitDir = dirname(fileURLToPath(import.meta.url));
const minimumTestFiles = parseMinimum('CV_UI_UNIT_MIN_FILES', 25);
const minimumTests = parseMinimum('CV_UI_UNIT_MIN_TESTS', 650);
const testFiles = (await readdir(unitDir))
  .filter(name => name.endsWith('.test.mjs'))
  .sort((left, right) => left.localeCompare(right, 'en'))
  .map(name => relative(process.cwd(), join(unitDir, name)).replaceAll('\\', '/'));

if (testFiles.length === 0) {
  console.error('No UI unit test files were found.');
  process.exit(1);
}

if (testFiles.length < minimumTestFiles) {
  console.error(`Expected at least ${minimumTestFiles} UI unit test files, but found ${testFiles.length}.`);
  process.exit(1);
}

const outputChunks = [];
const child = spawn(process.execPath, ['--test', '--test-reporter=tap', ...testFiles], {
  cwd: process.cwd(),
  env: process.env,
  stdio: ['inherit', 'pipe', 'pipe']
});

child.stdout.on('data', chunk => {
  outputChunks.push(chunk);
  process.stdout.write(chunk);
});

child.stderr.on('data', chunk => {
  outputChunks.push(chunk);
  process.stderr.write(chunk);
});

child.on('close', (code, signal) => {
  if (signal) {
    console.error(`UI unit test runner terminated by signal ${signal}.`);
    process.exit(1);
  }

  const exitCode = code ?? 1;
  if (exitCode !== 0) {
    process.exit(exitCode);
  }

  const output = Buffer.concat(outputChunks).toString('utf8');
  const totalTests = parseTapSummaryCount(output, 'tests');
  const passedTests = parseTapSummaryCount(output, 'pass');
  const failedTests = parseTapSummaryCount(output, 'fail');

  if (totalTests === null || passedTests === null || failedTests === null) {
    console.error('Unable to find Node test TAP summary counts in UI unit test output.');
    process.exit(1);
  }

  if (totalTests < minimumTests || passedTests < minimumTests || failedTests !== 0) {
    console.error(
      `UI unit test coverage regressed: files=${testFiles.length}, tests=${totalTests}, pass=${passedTests}, fail=${failedTests}, ` +
      `minimumFiles=${minimumTestFiles}, minimumTests=${minimumTests}.`
    );
    process.exit(1);
  }

  console.log(`UI unit test summary validation passed: files=${testFiles.length}, tests=${totalTests}, pass=${passedTests}.`);
});

function parseMinimum(name, fallback) {
  const raw = process.env[name];
  if (!raw) {
    return fallback;
  }

  const parsed = Number.parseInt(raw, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function parseTapSummaryCount(output, name) {
  const match = output.match(new RegExp(`^# ${name}\\s+(\\d+)`, 'm'));
  return match ? Number.parseInt(match[1], 10) : null;
}
