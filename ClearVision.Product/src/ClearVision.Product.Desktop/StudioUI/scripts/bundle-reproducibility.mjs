import { existsSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildProductionBundle } from './bundle-build.mjs';
import { createBundleReport, renderBundleMarkdown, writeBundleReport } from './bundle-report.mjs';

const studioUiRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const tempRoot = resolve(studioUiRoot, '.tmp/bundle/reproducibility');

function parseBudgetPath(args) {
  const index = args.indexOf('--budgets');
  if (index < 0 || !args[index + 1]) throw new Error('--budgets is required.');
  return resolve(studioUiRoot, args[index + 1]);
}

function resetDirectory(path) {
  if (!path.startsWith(`${tempRoot}\\`) && path !== tempRoot) {
    throw new Error(`Refusing to reset non-temporary path ${path}.`);
  }
  rmSync(path, { recursive: true, force: true });
  mkdirSync(path, { recursive: true });
}

function normalizedJson(report) {
  return `${JSON.stringify(report, null, 2)}\n`;
}

try {
  const budgetConfigPath = parseBudgetPath(process.argv.slice(2));
  const firstDist = resolve(tempRoot, 'first');
  const secondDist = resolve(tempRoot, 'second');
  resetDirectory(tempRoot);
  buildProductionBundle(firstDist);
  buildProductionBundle(secondDist);

  const first = createBundleReport({ distDir: firstDist, budgetConfigPath });
  const second = createBundleReport({ distDir: secondDist, budgetConfigPath });
  if (normalizedJson(first) !== normalizedJson(second) || renderBundleMarkdown(first) !== renderBundleMarkdown(second)) {
    throw new Error('StudioUI normalized bundle reports differ across identical production builds.');
  }
  if (second.budgets?.status !== 'PASS') {
    throw new Error(`StudioUI bundle budget failed: ${second.budgets?.failures.join('; ')}.`);
  }

  const reportDirectory = resolve(studioUiRoot, '.tmp/bundle');
  if (!existsSync(reportDirectory)) mkdirSync(reportDirectory, { recursive: true });
  const paths = writeBundleReport(second, resolve(reportDirectory, 'report'));
  console.log(`StudioUI bundle reproducibility PASS: ${paths.jsonPath}`);
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}
