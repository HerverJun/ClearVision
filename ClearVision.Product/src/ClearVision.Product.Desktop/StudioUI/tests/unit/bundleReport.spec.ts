import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
// @ts-expect-error The production Node ESM CLI intentionally has no application TypeScript surface.
import { createBundleReport } from '../../scripts/bundle-report.mjs';

const temporaryDirectories: string[] = [];

interface FixtureOptions {
  readonly cycle?: boolean;
  readonly duplicateImport?: boolean;
  readonly missingRouteFile?: boolean;
  readonly maxBytes?: number;
}

function createFixture({
  cycle = false,
  duplicateImport = false,
  missingRouteFile = false,
  maxBytes = 100
}: FixtureOptions = {}) {
  const root = mkdtempSync(join(tmpdir(), 'studio-ui-bundle-report-'));
  temporaryDirectories.push(root);
  const distDir = join(root, 'dist');
  mkdirSync(join(distDir, '.vite'), { recursive: true });
  mkdirSync(join(distDir, 'assets'), { recursive: true });
  const manifest = {
    '_shared.js': {
      file: 'assets/shared.js',
      ...(cycle ? { imports: ['index.html'] } : {})
    },
    'index.html': {
      file: 'assets/entry.js',
      isEntry: true,
      imports: duplicateImport ? ['_shared.js', '_shared.js'] : ['_shared.js'],
      dynamicImports: ['src/Route.vue'],
      css: ['assets/entry.css']
    },
    'src/Route.vue': {
      file: 'assets/route.js',
      src: 'src/Route.vue',
      isDynamicEntry: true,
      imports: ['_shared.js']
    }
  };
  writeFileSync(join(distDir, '.vite', 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
  writeFileSync(join(distDir, 'index.html'), '<main></main>');
  writeFileSync(join(distDir, 'assets', 'entry.js'), 'entry');
  writeFileSync(join(distDir, 'assets', 'entry.css'), 'css');
  writeFileSync(join(distDir, 'assets', 'shared.js'), 'sync');
  if (!missingRouteFile) writeFileSync(join(distDir, 'assets', 'route.js'), 'route!');
  const budgetConfigPath = join(root, 'budgets.json');
  writeFileSync(budgetConfigPath, `${JSON.stringify({
    schemaVersion: 1,
    initialEntryKey: 'index.html',
    hardInitialMaxBytes: maxBytes,
    targets: {
      route: { label: 'Route', roots: ['src/Route.vue'], maxBytes }
    }
  }, null, 2)}\n`);
  return { root, distDir, budgetConfigPath };
}

afterEach(() => {
  for (const directory of temporaryDirectories.splice(0)) {
    rmSync(directory, { recursive: true, force: true });
  }
});

describe('bundle report', () => {
  it('uses original bytes and SHA-256 while excluding dynamic imports from the initial closure', () => {
    const fixture = createFixture();
    const report = createBundleReport(fixture);

    expect(report.initialClosure).toMatchObject({
      files: ['assets/entry.css', 'assets/entry.js', 'assets/shared.js'],
      totalBytes: 12
    });
    expect(report.initialClosure.files).not.toContain('assets/route.js');
    expect(report.routeChunkKeys).toEqual(['src/Route.vue']);
    expect(report.files.every((file: { readonly sha256: string }) => file.sha256.length === 64)).toBe(true);
    expect(report.budgets.status).toBe('PASS');
  });

  it('fails closed for duplicate references, synchronous cycles and missing outputs', () => {
    expect(() => createBundleReport(createFixture({ duplicateImport: true })))
      .toThrow('duplicate values');
    expect(() => createBundleReport(createFixture({ cycle: true })))
      .toThrow('synchronous import cycle');
    expect(() => createBundleReport(createFixture({ missingRouteFile: true })))
      .toThrow('missing output file');
  });

  it('returns a non-zero exit code when an artificial one-byte budget is exceeded', () => {
    const fixture = createFixture({ maxBytes: 1 });
    const scriptPath = resolve('scripts/bundle-report.mjs');
    const result = spawnSync(process.execPath, [
      scriptPath,
      '--dist', fixture.distDir,
      '--output', join(fixture.root, 'report'),
      '--budgets', fixture.budgetConfigPath,
      '--gate'
    ], { encoding: 'utf8' });

    expect(result.status).toBe(1);
    expect(result.stderr).toContain('budget gate failed');
  });
});
