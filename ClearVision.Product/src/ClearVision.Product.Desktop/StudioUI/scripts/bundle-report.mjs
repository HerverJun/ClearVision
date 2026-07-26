import { createHash } from 'node:crypto';
import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, extname, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const manifestRelativePath = '.vite/manifest.json';

function fail(message) {
  throw new Error(`StudioUI bundle report: ${message}`);
}

function toPosixPath(value) {
  return value.split(sep).join('/');
}

function assertRelativeFilePath(value, context) {
  if (typeof value !== 'string' || !value || value.includes('\\')) {
    fail(`${context} must be a non-empty POSIX relative path.`);
  }
  if (value.startsWith('/') || value.split('/').includes('..')) {
    fail(`${context} escapes the build output directory: ${value}`);
  }
}

function assertStringArray(value, context) {
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.some(item => typeof item !== 'string' || !item)) {
    fail(`${context} must be an array of non-empty strings.`);
  }
  if (new Set(value).size !== value.length) {
    fail(`${context} contains duplicate values.`);
  }
  return value;
}

function readManifest(distDir) {
  const manifestPath = resolve(distDir, manifestRelativePath);
  if (!existsSync(manifestPath)) fail(`missing ${manifestRelativePath}.`);

  let parsed;
  try {
    parsed = JSON.parse(readFileSync(manifestPath, 'utf8'));
  } catch (error) {
    fail(`could not parse ${manifestRelativePath}: ${error instanceof Error ? error.message : String(error)}`);
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed) || Object.keys(parsed).length === 0) {
    fail(`${manifestRelativePath} must contain at least one manifest record.`);
  }
  return parsed;
}

function validateManifest(manifest) {
  const keys = Object.keys(manifest).sort();
  for (const key of keys) {
    const record = manifest[key];
    if (!record || typeof record !== 'object' || Array.isArray(record)) fail(`manifest record ${key} is invalid.`);
    assertRelativeFilePath(record.file, `${key}.file`);
    const imports = assertStringArray(record.imports, `${key}.imports`);
    const dynamicImports = assertStringArray(record.dynamicImports, `${key}.dynamicImports`);
    const css = assertStringArray(record.css, `${key}.css`);
    const assets = assertStringArray(record.assets, `${key}.assets`);
    for (const dependency of [...imports, ...dynamicImports]) {
      if (!Object.hasOwn(manifest, dependency)) fail(`${key} references missing manifest record ${dependency}.`);
    }
    const emittedFiles = [record.file, ...css, ...assets];
    if (new Set(emittedFiles).size !== emittedFiles.length) {
      fail(`${key} repeats an emitted file and would be double-counted.`);
    }
    for (const file of emittedFiles) assertRelativeFilePath(file, `${key} emitted file`);
  }

  const visited = new Set();
  const visiting = new Set();
  function visit(key, path) {
    if (visiting.has(key)) fail(`synchronous import cycle detected: ${[...path, key].join(' -> ')}.`);
    if (visited.has(key)) return;
    visiting.add(key);
    for (const importedKey of manifest[key].imports ?? []) visit(importedKey, [...path, key]);
    visiting.delete(key);
    visited.add(key);
  }
  for (const key of keys) visit(key, []);
}

function walkOutputFiles(distDir, currentDir = distDir) {
  const files = [];
  for (const entry of readdirSync(currentDir, { withFileTypes: true }).sort((left, right) =>
    left.name < right.name ? -1 : left.name > right.name ? 1 : 0)) {
    const absolutePath = resolve(currentDir, entry.name);
    if (entry.isDirectory()) files.push(...walkOutputFiles(distDir, absolutePath));
    else if (entry.isFile()) files.push(toPosixPath(relative(distDir, absolutePath)));
    else fail(`unsupported output entry ${toPosixPath(relative(distDir, absolutePath))}.`);
  }
  return files.sort();
}

function fileKind(path) {
  const extension = extname(path).toLowerCase();
  if (extension === '.js' || extension === '.mjs') return 'js';
  if (extension === '.css') return 'css';
  return 'other';
}

function readOutputFiles(distDir) {
  const files = walkOutputFiles(distDir).map(path => {
    const absolutePath = resolve(distDir, path);
    const bytes = statSync(absolutePath).size;
    const sha256 = createHash('sha256').update(readFileSync(absolutePath)).digest('hex');
    return Object.freeze({ path, bytes, kind: fileKind(path), sha256 });
  });
  const byPath = new Map(files.map(file => [file.path, file]));
  return { files, byPath };
}

function collectSynchronousClosure(manifest, filesByPath, rootKey) {
  if (!Object.hasOwn(manifest, rootKey)) fail(`closure root ${rootKey} is missing from the manifest.`);
  const manifestKeys = new Set();
  const filePaths = new Set();

  function addFile(path, owner) {
    const file = filesByPath.get(path);
    if (!file) fail(`${owner} references missing output file ${path}.`);
    filePaths.add(path);
  }

  function visit(key) {
    if (manifestKeys.has(key)) return;
    manifestKeys.add(key);
    const record = manifest[key];
    addFile(record.file, key);
    for (const path of record.css ?? []) addFile(path, key);
    for (const path of record.assets ?? []) addFile(path, key);
    for (const importedKey of record.imports ?? []) visit(importedKey);
  }
  visit(rootKey);

  const paths = [...filePaths].sort();
  if (paths.length !== filePaths.size) fail(`closure ${rootKey} contains duplicate file paths.`);
  const subtotals = { jsBytes: 0, cssBytes: 0, otherBytes: 0, totalBytes: 0 };
  for (const path of paths) {
    const file = filesByPath.get(path);
    if (!file) fail(`closure ${rootKey} lost output file ${path}.`);
    subtotals.totalBytes += file.bytes;
    if (file.kind === 'js') subtotals.jsBytes += file.bytes;
    else if (file.kind === 'css') subtotals.cssBytes += file.bytes;
    else subtotals.otherBytes += file.bytes;
  }
  if (subtotals.jsBytes + subtotals.cssBytes + subtotals.otherBytes !== subtotals.totalBytes) {
    fail(`closure ${rootKey} subtotal accounting is inconsistent.`);
  }
  return Object.freeze({
    rootKey,
    manifestKeys: [...manifestKeys].sort(),
    files: paths,
    ...subtotals
  });
}

function collectRouteRootKeys(manifest, entryKeys) {
  const visited = new Set();
  const routeRoots = new Set();
  function visit(key) {
    if (visited.has(key)) return;
    visited.add(key);
    const record = manifest[key];
    for (const importedKey of record.imports ?? []) visit(importedKey);
    for (const dynamicKey of record.dynamicImports ?? []) {
      routeRoots.add(dynamicKey);
      visit(dynamicKey);
    }
  }
  for (const entryKey of entryKeys) visit(entryKey);
  return routeRoots;
}

function resolveRootKey(manifest, requestedRoot) {
  if (Object.hasOwn(manifest, requestedRoot)) return requestedRoot;
  const matches = Object.keys(manifest).filter(key => manifest[key].src === requestedRoot);
  if (matches.length !== 1) {
    fail(`budget root ${requestedRoot} resolved to ${matches.length} manifest records.`);
  }
  return matches[0];
}

function readBudgetConfig(path) {
  if (!path) return null;
  let parsed;
  try {
    parsed = JSON.parse(readFileSync(resolve(path), 'utf8'));
  } catch (error) {
    fail(`could not parse budget config ${path}: ${error instanceof Error ? error.message : String(error)}`);
  }
  if (parsed?.schemaVersion !== 1 || typeof parsed.initialEntryKey !== 'string' ||
      !Number.isSafeInteger(parsed.hardInitialMaxBytes) || parsed.hardInitialMaxBytes <= 0 ||
      !parsed.targets || typeof parsed.targets !== 'object' || Array.isArray(parsed.targets)) {
    fail(`budget config ${path} does not match schema version 1.`);
  }
  return parsed;
}

function evaluateBudgets(manifest, filesByPath, initialClosure, budgetConfig) {
  if (!budgetConfig) return null;
  const failures = [];
  if (initialClosure.totalBytes > budgetConfig.hardInitialMaxBytes) {
    failures.push(`initial entry ${initialClosure.totalBytes} > hard limit ${budgetConfig.hardInitialMaxBytes}`);
  }
  const initialFiles = new Set(initialClosure.files);
  const targets = Object.keys(budgetConfig.targets).sort().map(name => {
    const target = budgetConfig.targets[name];
    if (!target || typeof target.label !== 'string' || !Array.isArray(target.roots) || target.roots.length === 0 ||
        new Set(target.roots).size !== target.roots.length ||
        !Number.isSafeInteger(target.maxBytes) || target.maxBytes <= 0) {
      fail(`budget target ${name} is invalid.`);
    }
    const variants = target.roots.map(requestedRoot => {
      const rootKey = resolveRootKey(manifest, requestedRoot);
      const closure = collectSynchronousClosure(manifest, filesByPath, rootKey);
      const incrementalBytes = closure.files
        .filter(path => !initialFiles.has(path))
        .reduce((total, path) => total + filesByPath.get(path).bytes, 0);
      if (closure.totalBytes > target.maxBytes) {
        failures.push(`${name}:${rootKey} ${closure.totalBytes} > budget ${target.maxBytes}`);
      }
      return Object.freeze({ requestedRoot, rootKey, closure, incrementalBytes });
    }).sort((left, right) => left.rootKey < right.rootKey ? -1 : left.rootKey > right.rootKey ? 1 : 0);
    return Object.freeze({
      name,
      label: target.label,
      maxBytes: target.maxBytes,
      measuredBytes: Math.max(...variants.map(variant => variant.closure.totalBytes)),
      variants
    });
  });
  return Object.freeze({
    status: failures.length === 0 ? 'PASS' : 'FAIL',
    hardInitialMaxBytes: budgetConfig.hardInitialMaxBytes,
    failures: failures.sort(),
    targets
  });
}

function sumFiles(files) {
  const totals = { jsBytes: 0, cssBytes: 0, otherBytes: 0, totalBytes: 0 };
  for (const file of files) {
    totals.totalBytes += file.bytes;
    if (file.kind === 'js') totals.jsBytes += file.bytes;
    else if (file.kind === 'css') totals.cssBytes += file.bytes;
    else totals.otherBytes += file.bytes;
  }
  return totals;
}

export function createBundleReport({ distDir, budgetConfigPath }) {
  const resolvedDistDir = resolve(distDir);
  const manifest = readManifest(resolvedDistDir);
  validateManifest(manifest);
  const { files, byPath } = readOutputFiles(resolvedDistDir);
  for (const key of Object.keys(manifest).sort()) {
    const record = manifest[key];
    for (const path of [record.file, ...(record.css ?? []), ...(record.assets ?? [])]) {
      if (!byPath.has(path)) fail(`${key} references missing output file ${path}.`);
    }
  }
  const entryKeys = Object.keys(manifest).filter(key => manifest[key].isEntry === true).sort();
  if (entryKeys.length === 0) fail('manifest contains no entry chunks.');
  const routeRootKeys = collectRouteRootKeys(manifest, entryKeys);
  const budgetConfig = readBudgetConfig(budgetConfigPath);
  const initialEntryKey = budgetConfig?.initialEntryKey ?? entryKeys[0];
  if (!entryKeys.includes(initialEntryKey)) fail(`initial entry ${initialEntryKey} is not an entry chunk.`);
  const initialClosure = collectSynchronousClosure(manifest, byPath, initialEntryKey);

  const chunks = Object.keys(manifest)
    .filter(key => fileKind(manifest[key].file) === 'js')
    .sort()
    .map(key => {
      const record = manifest[key];
      const kind = entryKeys.includes(key) ? 'entry' : routeRootKeys.has(key) ? 'route' : 'shared';
      return Object.freeze({
        key,
        name: record.name ?? null,
        file: record.file,
        kind,
        imports: [...(record.imports ?? [])].sort(),
        dynamicImports: [...(record.dynamicImports ?? [])].sort(),
        synchronousClosure: collectSynchronousClosure(manifest, byPath, key)
      });
    });
  const budgets = evaluateBudgets(manifest, byPath, initialClosure, budgetConfig);

  return Object.freeze({
    schemaVersion: 1,
    accounting: Object.freeze({
      unit: 'bytes',
      synchronousClosure: 'Root chunk plus imports recursively; emitted JS, CSS and assets are counted once by relative path.',
      dynamicImports: 'Listed separately and excluded from the parent synchronous closure.',
      compression: 'Original production output bytes; gzip and brotli are not used for gates.'
    }),
    totals: sumFiles(files),
    files,
    entryKeys,
    routeChunkKeys: [...routeRootKeys].sort(),
    initialClosure,
    chunks,
    budgets
  });
}

function formatBytes(bytes) {
  return `${bytes.toLocaleString('en-US')} B`;
}

export function renderBundleMarkdown(report) {
  const lines = [
    '# StudioUI Bundle Report',
    '',
    `Status: ${report.budgets?.status ?? 'REPORT_ONLY'}`,
    '',
    '## Accounting',
    '',
    `- Total package: ${formatBytes(report.totals.totalBytes)}`,
    `- JavaScript: ${formatBytes(report.totals.jsBytes)}`,
    `- CSS: ${formatBytes(report.totals.cssBytes)}`,
    `- Other: ${formatBytes(report.totals.otherBytes)}`,
    `- Initial synchronous closure: ${formatBytes(report.initialClosure.totalBytes)}`,
    '- Dynamic imports are listed but excluded from parent synchronous closures.',
    '',
    '## Critical Budgets',
    '',
    '| Target | Measured | Budget | Status |',
    '|---|---:|---:|---|'
  ];
  if (report.budgets) {
    lines.push(`| Initial hard limit | ${formatBytes(report.initialClosure.totalBytes)} | ${formatBytes(report.budgets.hardInitialMaxBytes)} | ${report.initialClosure.totalBytes <= report.budgets.hardInitialMaxBytes ? 'PASS' : 'FAIL'} |`);
    for (const target of report.budgets.targets) {
      lines.push(`| ${target.label} | ${formatBytes(target.measuredBytes)} | ${formatBytes(target.maxBytes)} | ${target.measuredBytes <= target.maxBytes ? 'PASS' : 'FAIL'} |`);
    }
  } else {
    lines.push('| No budget config | - | - | REPORT_ONLY |');
  }
  lines.push('', '## Entry And Route Chunks', '', '| Kind | Manifest key | File | Sync closure | Dynamic imports |', '|---|---|---|---:|---:|');
  for (const chunk of report.chunks.filter(chunk => chunk.kind !== 'shared')) {
    lines.push(`| ${chunk.kind} | \`${chunk.key}\` | \`${chunk.file}\` | ${formatBytes(chunk.synchronousClosure.totalBytes)} | ${chunk.dynamicImports.length} |`);
  }
  lines.push('', '## Output Files', '', '| File | Kind | Bytes | SHA-256 |', '|---|---|---:|---|');
  for (const file of report.files) {
    lines.push(`| \`${file.path}\` | ${file.kind} | ${file.bytes} | \`${file.sha256}\` |`);
  }
  return `${lines.join('\n')}\n`;
}

export function writeBundleReport(report, outputPrefix) {
  const resolvedPrefix = resolve(outputPrefix);
  const jsonPath = `${resolvedPrefix}.json`;
  const markdownPath = `${resolvedPrefix}.md`;
  const outputDirectory = dirname(jsonPath);
  if (!existsSync(outputDirectory)) fail(`output directory does not exist: ${outputDirectory}`);
  writeFileSync(jsonPath, `${JSON.stringify(report, null, 2)}\n`, 'utf8');
  writeFileSync(markdownPath, renderBundleMarkdown(report), 'utf8');
  return { jsonPath, markdownPath };
}

function parseArguments(args) {
  const options = { distDir: '.tmp/bundle/dist', outputPrefix: '.tmp/bundle/report', budgetConfigPath: null, gate: false };
  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    if (argument === '--gate') options.gate = true;
    else if (argument === '--dist') options.distDir = args[++index];
    else if (argument === '--output') options.outputPrefix = args[++index];
    else if (argument === '--budgets') options.budgetConfigPath = args[++index];
    else fail(`unknown argument ${argument}.`);
  }
  if (!options.distDir || !options.outputPrefix) fail('missing argument value.');
  if (options.gate && !options.budgetConfigPath) fail('--gate requires --budgets.');
  return options;
}

function runCli() {
  const options = parseArguments(process.argv.slice(2));
  const report = createBundleReport(options);
  const paths = writeBundleReport(report, options.outputPrefix);
  if (options.gate && report.budgets?.status !== 'PASS') {
    fail(`budget gate failed: ${report.budgets?.failures.join('; ') || 'unknown budget failure'}.`);
  }
  console.log(`StudioUI bundle ${options.gate ? 'gate' : 'report'} ${report.budgets?.status ?? 'REPORT_ONLY'}: ${paths.jsonPath}`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    runCli();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
