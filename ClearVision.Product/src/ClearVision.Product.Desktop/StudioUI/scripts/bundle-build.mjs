import { existsSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const studioUiRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

export function buildProductionBundle(outDir) {
  const resolvedOutDir = resolve(studioUiRoot, outDir);
  if (!existsSync(dirname(resolvedOutDir))) mkdirSync(dirname(resolvedOutDir), { recursive: true });
  const viteCli = resolve(studioUiRoot, 'node_modules/vite/bin/vite.js');
  const result = spawnSync(process.execPath, [viteCli, 'build', '--mode', 'production'], {
    cwd: studioUiRoot,
    env: { ...process.env, VITE_OUT_DIR: resolvedOutDir },
    encoding: 'utf8',
    stdio: 'inherit'
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`StudioUI production build failed with exit code ${result.status}.`);
  return resolvedOutDir;
}

function parseOutput(args) {
  const outputIndex = args.indexOf('--out-dir');
  if (outputIndex < 0) return '.tmp/bundle/dist';
  const value = args[outputIndex + 1];
  if (!value) throw new Error('--out-dir requires a value.');
  return value;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const outDir = buildProductionBundle(parseOutput(process.argv.slice(2)));
    console.log(`StudioUI production bundle: ${outDir}`);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
