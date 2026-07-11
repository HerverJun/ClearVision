import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '../../../..');
const styleRoot = path.join(repoRoot, 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/shared/styles');
const legacyPath = path.join(styleRoot, 'ai-panel.css');
const moduleRoot = path.join(styleRoot, 'ai-panel');
const moduleNames = ['shell', 'conversation', 'plan', 'build-validation', 'application', 'details', 'responsive'];
const modulePaths = moduleNames.map(name => path.join(moduleRoot, `${name}.css`));
const baseline = JSON.parse(fs.readFileSync(path.join(scriptDir, 'ai-css-baseline.json'), 'utf8'));
const coreSelectors = Object.keys(baseline.coreSelectorOccurrences);

const count = (source, pattern) => source.match(pattern)?.length || 0;
const escapeRegExp = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const read = file => fs.readFileSync(file, 'utf8');
const legacy = read(legacyPath);
const modules = modulePaths.map(file => ({ file, source: read(file) }));
const combined = [legacy, ...modules.map(item => item.source)].join('\n');

const selectorOccurrences = Object.fromEntries(coreSelectors.map(selector => [
  selector,
  count(combined, new RegExp(`(^|[},]\\s*)${escapeRegExp(selector)}(?=\\s|[,:{\\[])`, 'gm'))
]));
const tailStart = Math.floor(legacy.split(/\r?\n/).length * 0.75);
const tail = legacy.split(/\r?\n/).slice(tailStart).join('\n');
const cssClasses = [...new Set([...legacy.matchAll(/\.([a-z][a-z0-9_-]+)/gi)].map(match => match[1]))];
const sourceRoot = path.join(repoRoot, 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot');
const sourceFiles = fs.readdirSync(sourceRoot, { recursive: true })
  .filter(name => /\.(?:html|js|mjs)$/i.test(name))
  .map(name => path.join(sourceRoot, name));
const sourceCorpus = sourceFiles.map(read).join('\n');
const unreachableCandidates = cssClasses
  .filter(name => !sourceCorpus.includes(name))
  .sort()
  .slice(0, 40);

export const audit = {
  legacyLineCount: legacy.split(/\r?\n/).length,
  totalLineCount: combined.split(/\r?\n/).length,
  hexColorCount: count(combined, /#[0-9a-f]{3,8}\b/gi),
  importantCount: count(combined, /!important\b/gi),
  backdropFilterCount: count(combined, /(?:-webkit-)?backdrop-filter\s*:/gi),
  tailOverrideMarkerCount: count(tail, /\/\*\s*-{2,}/g),
  coreSelectorOccurrences: selectorOccurrences,
  unreachableCandidates
};

export function validateAudit() {
  const errors = [];
  if (audit.hexColorCount > baseline.hexColorCount) errors.push('AI CSS added hexadecimal color constants.');
  if (audit.importantCount > baseline.importantCount) errors.push('AI CSS added unexplained !important declarations.');
  if (audit.backdropFilterCount > baseline.backdropFilterCount) errors.push('AI CSS added backdrop-filter declarations.');
  for (const [selector, baselineCount] of Object.entries(baseline.coreSelectorOccurrences)) {
    if (audit.coreSelectorOccurrences[selector] > baselineCount) {
      errors.push(`Core selector ${selector} exceeds frozen duplicate baseline ${baselineCount}.`);
    }
  }
  for (const { file, source } of modules) {
    const relative = path.relative(repoRoot, file).replaceAll('\\', '/');
    if (/#[0-9a-f]{3,8}\b/i.test(source)) errors.push(`${relative} contains a hexadecimal color constant.`);
    if (/!important\b/i.test(source)) errors.push(`${relative} contains !important.`);
    for (const match of source.matchAll(/(?:-webkit-)?backdrop-filter\s*:\s*([^;]+);/gi)) {
      if (match[1].trim().toLowerCase() !== 'none') {
        errors.push(`${relative} contains a non-disabled backdrop-filter.`);
      }
    }
    for (const match of source.matchAll(/(--ai-[a-z0-9-]+)\s*:\s*([^;]+);/gi)) {
      if (!/^\s*var\(--[a-z0-9-]+\)\s*$/i.test(match[2])) {
        errors.push(`${relative} defines ${match[1]} as a non-token value.`);
      }
    }
  }
  return errors;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const errors = validateAudit();
  console.log(JSON.stringify({ baseline, audit, errors }, null, 2));
  if (process.argv.includes('--check') && errors.length) process.exitCode = 1;
}
