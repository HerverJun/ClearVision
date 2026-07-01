import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const frontendRoot = resolve(scriptDirectory, '..');
const defaultConfiguration = process.env.Configuration ?? process.env.CONFIGURATION ?? 'Debug';
const defaultTargetFramework = process.env.TargetFramework ?? process.env.TARGET_FRAMEWORK ?? 'net8.0-windows';
const distRoot = process.env.VITE_OUT_DIR
  ? resolve(process.env.VITE_OUT_DIR)
  : resolve(frontendRoot, '..', 'obj', defaultConfiguration, defaultTargetFramework, 'FrontendV2', 'dist');

function fail(message) {
  throw new Error(`[FrontendV2 assets] ${message}`);
}

function readText(path) {
  if (!existsSync(path)) {
    fail(`Missing expected file: ${path}`);
  }

  return readFileSync(path, 'utf8');
}

const indexPath = resolve(distRoot, 'index.html');
const manifestPath = resolve(distRoot, '.vite', 'manifest.json');
const indexHtml = readText(indexPath);
const manifest = JSON.parse(readText(manifestPath));

if (/["']\/assets\//.test(indexHtml)) {
  fail('index.html contains a root /assets/ reference instead of the /v2/ public base.');
}

if (!/["']\/v2\/assets\//.test(indexHtml)) {
  fail('index.html does not reference built assets through /v2/assets/.');
}

for (const [entryName, entry] of Object.entries(manifest)) {
  const candidatePaths = [
    entry.file,
    ...(entry.css ?? []),
    ...(entry.assets ?? []),
    ...(entry.imports ?? [])
  ].filter(Boolean);

  for (const assetPath of candidatePaths) {
    if (assetPath.startsWith('/assets/')) {
      fail(`manifest entry ${entryName} contains root asset path ${assetPath}.`);
    }

    if (assetPath.startsWith('/')) {
      fail(`manifest entry ${entryName} contains absolute path ${assetPath}; expected dist-relative paths.`);
    }
  }

  if (entry.isEntry && entry.file && !indexHtml.includes(`/v2/${entry.file}`)) {
    fail(`index.html does not reference entry asset /v2/${entry.file}.`);
  }
}

console.log(`[FrontendV2 assets] validated ${indexPath} and ${manifestPath}`);
