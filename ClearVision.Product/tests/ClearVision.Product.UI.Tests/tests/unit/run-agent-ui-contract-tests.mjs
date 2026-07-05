import { spawn } from 'node:child_process';

const minimumTests = parseMinimum('CV_AGENT_UI_CONTRACT_MIN_TESTS', 340);
const testFile = 'tests/unit/ai-agent-ui-contract.test.mjs';
const outputChunks = [];

const child = spawn(process.execPath, ['--test', '--test-reporter=tap', testFile], {
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
    console.error(`Agent UI contract test runner terminated by signal ${signal}.`);
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
    console.error('Unable to find Node test TAP summary counts in Agent UI contract output.');
    process.exit(1);
  }

  if (totalTests < minimumTests || passedTests < minimumTests || failedTests !== 0) {
    console.error(
      `Agent UI contract coverage regressed: tests=${totalTests}, pass=${passedTests}, fail=${failedTests}, ` +
      `minimumTests=${minimumTests}.`
    );
    process.exit(1);
  }

  console.log(`Agent UI contract summary validation passed: tests=${totalTests}, pass=${passedTests}.`);
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
