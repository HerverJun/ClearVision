import { existsSync, readFileSync, statSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { describe, expect, it } from 'vitest';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const sourceRoot = join(studioRoot, 'src');
const aiRoot = join(sourceRoot, 'capabilities/ai-workbench');

function read(path: string): string {
  return readFileSync(path, 'utf8');
}

function scriptSource(path: string): string {
  const source = read(path);
  if (extname(path) !== '.vue') return source;
  return [...source.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/g)]
    .map(match => match[1] ?? '')
    .join('\n');
}

function runtimeImports(path: string): readonly string[] {
  const source = ts.createSourceFile(path, scriptSource(path), ts.ScriptTarget.Latest, true);
  const imports: string[] = [];
  for (const statement of source.statements) {
    if (ts.isImportDeclaration(statement)) {
      const clause = statement.importClause;
      const isTypeOnly = clause?.isTypeOnly === true || (
        clause?.name === undefined &&
        clause?.namedBindings !== undefined &&
        ts.isNamedImports(clause.namedBindings) &&
        clause.namedBindings.elements.every(element => element.isTypeOnly)
      );
      if (!isTypeOnly && ts.isStringLiteral(statement.moduleSpecifier)) {
        imports.push(statement.moduleSpecifier.text);
      }
    }
    if (ts.isExportDeclaration(statement) && !statement.isTypeOnly &&
        statement.moduleSpecifier && ts.isStringLiteral(statement.moduleSpecifier)) {
      imports.push(statement.moduleSpecifier.text);
    }
  }
  return imports;
}

function resolveLocalImport(importer: string, specifier: string): string | null {
  const base = specifier.startsWith('@/')
    ? join(sourceRoot, specifier.slice(2))
    : specifier.startsWith('.')
      ? resolve(dirname(importer), specifier)
      : null;
  if (!base) return null;
  const candidates = [
    base,
    ...['.ts', '.vue', '.js', '.mjs', '.css'].map(extension => `${base}${extension}`),
    ...['.ts', '.vue', '.js', '.mjs'].map(extension => join(base, `index${extension}`))
  ];
  return candidates.find(candidate => existsSync(candidate) && statSync(candidate).isFile()) ?? null;
}

function runtimeClosure(entry: string): Readonly<{
  files: readonly string[];
  externalImports: readonly string[];
}> {
  const pending = [entry];
  const files = new Set<string>();
  const externalImports = new Set<string>();
  while (pending.length > 0) {
    const current = pending.pop();
    if (!current || files.has(current)) continue;
    files.add(current);
    if (extname(current) === '.css') continue;
    for (const specifier of runtimeImports(current)) {
      const resolved = resolveLocalImport(current, specifier);
      if (resolved) pending.push(resolved);
      else externalImports.add(specifier);
    }
  }
  return Object.freeze({
    files: Object.freeze([...files]),
    externalImports: Object.freeze([...externalImports])
  });
}

function studioRelative(path: string): string {
  return relative(studioRoot, path).replaceAll('\\', '/');
}

describe('F06 G5 AI history diagnostics and lazy-boundary architecture guards', () => {
  it('keeps both AI routes on one shared lazy import and therefore one AI route chunk', () => {
    const router = read(join(sourceRoot, 'app/router.ts'));
    expect(router.match(/import\(['"]@\/capabilities\/ai-workbench\/AiWorkbenchPage\.vue['"]\)/g))
      .toHaveLength(1);
    expect(router.match(/component:\s*AiWorkbenchPage/g)).toHaveLength(2);
    expect(router).toContain("name: 'ai-workbench'");
    expect(router).toContain("name: 'project-ai-workbench'");
  });

  it('keeps Legacy AI, model SDKs and Canvas out of the eager Shell and AI lazy closures', () => {
    const shell = runtimeClosure(join(sourceRoot, 'main.ts'));
    const ai = runtimeClosure(join(aiRoot, 'AiWorkbenchPage.vue'));
    const combinedFiles = [...shell.files, ...ai.files].map(studioRelative);
    const combinedImports = [...shell.externalImports, ...ai.externalImports];

    expect(combinedFiles).not.toEqual(expect.arrayContaining([
      expect.stringMatching(/(?:^|\/)(?:FrontendV2|wwwroot)(?:\/|$)/i),
      expect.stringMatching(/(?:^|\/)platform\/canvas(?:\/|$)/i),
      expect.stringMatching(/canonical(?:Flow|Image)Canvas|FlowCanvas|ImageCanvas/i)
    ]));
    expect(combinedImports).not.toEqual(expect.arrayContaining([
      expect.stringMatching(/^@clearvision\/canonical-(?:flow|image)/i),
      expect.stringMatching(/^(?:openai|@anthropic-ai\/|@google\/generative-ai|ollama|langchain|@langchain\/|ai(?:\/|$))/i)
    ]));
  });

  it('keeps public diagnostics default-closed and free of private or sensitive render fields', () => {
    const page = read(join(aiRoot, 'AiWorkbenchPage.vue'));
    const drawer = read(join(aiRoot, 'AiDiagnosticsDrawer.vue'));
    const drawerShell = read(join(aiRoot, 'AiWorkbenchDrawer.vue'));
    const template = drawer.match(/<template>([\s\S]*?)<\/template>/)?.[1] ?? '';

    expect(page).toContain('const diagnosticsOpen = shallowRef(false);');
    expect(page).toContain(':open="diagnosticsOpen"');
    expect(drawerShell).toContain('v-if="open"');
    expect(template).not.toMatch(
      /\b(?:reasoning|chainOfThought|systemPrompt|tokenUsage|promptTokens|completionTokens|accessToken|apiKey|secret|ownerHash|runId|sessionId|planId|buildId|artifactId|internalException|stackTrace|absolutePath|ipAddress|plcAddress|rawAttachment|rawPayload|toolPayload|toolCall)\b/i
    );
    expect(template).not.toMatch(/v-html|window\.location|file:\/\//i);
  });
});
