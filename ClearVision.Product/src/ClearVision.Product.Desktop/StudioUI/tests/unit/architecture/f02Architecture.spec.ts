import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const sourceRoot = join(studioRoot, 'src');
const repositoryRoot = resolve(studioRoot, '../../../..');

function sourceFiles(root: string): string[] {
  if (!existsSync(root)) return [];
  return readdirSync(root, { withFileTypes: true }).flatMap(entry => {
    const path = join(root, entry.name);
    return entry.isDirectory()
      ? sourceFiles(path)
      : ['.ts', '.vue', '.js'].includes(extname(path)) ? [path] : [];
  });
}

function read(path: string): string {
  return readFileSync(path, 'utf8');
}

describe('F02 architecture guards', () => {
  const files = sourceFiles(sourceRoot);

  it('keeps apiTransport as the only direct fetch and rejects a second HTTP stack', () => {
    const directFetchFiles = files
      .filter(path => /\b(?:globalThis\.)?fetch\s*\(/.test(read(path)))
      .map(path => relative(studioRoot, path).replaceAll('\\', '/'));

    expect(directFetchFiles).toEqual(['src/platform/api/apiTransport.ts']);
    expect(files.filter(path => /\baxios\b/i.test(read(path)))).toEqual([]);
    expect(files.filter(path => /create(?:HttpClient|ApiClient|RequestCore)/.test(read(path))))
      .toEqual([]);
  });

  it('keeps product writes inside the approved shared transport owners', () => {
    const productFiles = files.filter(path => !path.includes(`${join(sourceRoot, 'labs')}`));
    const forbidden = productFiles.filter(path =>
      /\b(?:axios|httpClient|apiClient)\.(?:post|put|patch|delete)\s*\(/i.test(read(path))
    );
    expect(forbidden).toEqual([]);
  });

  it('has one product shell, one session owner and one health owner', () => {
    expect(files.filter(path => path.endsWith('ProductLayout.vue'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('sessionProjectionOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('systemStatusOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => /\bEventSource\b/.test(read(path)))).toEqual([]);
    expect(files.filter(path => /\b(?:EventBus|ServiceRegistry)\b/.test(read(path)))).toEqual([]);
  });

  it('keeps one formal main landmark and one design token source', () => {
    const formalMainFiles = files
      .filter(path => !path.includes(`${join(sourceRoot, 'labs')}`))
      .filter(path => /<main(?:\s|>)/.test(read(path)))
      .map(path => relative(studioRoot, path).replaceAll('\\', '/'));
    expect(formalMainFiles).toEqual(expect.arrayContaining([
      'src/app/layouts/ProductLayout.vue',
      'src/app/pages/auth/AuthShell.vue',
      'src/app/pages/ForbiddenPage.vue'
    ]));
    expect(existsSync(join(sourceRoot, 'design-system/tokens/tokens.css'))).toBe(true);
    expect(files.filter(path => /data-product-shell="ready"/.test(read(path))))
      .toEqual([join(sourceRoot, 'app/layouts/ProductLayout.vue')]);
  });

  it('isolates Labs from the formal navigation', () => {
    const navigation = read(join(sourceRoot, 'app/navigation.ts'));
    const router = read(join(sourceRoot, 'app/router.ts'));
    expect(navigation).not.toContain('/labs');
    expect(router).toContain("path: '/labs'");
    expect(router).toContain('InternalLabLayout');
    expect(router).toContain('ProductLayout');
  });

  it('keeps the named F09 default entry explicit', () => {
    const settings = JSON.parse(read(join(
      repositoryRoot,
      'ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json'
    ))) as { Studio: { StartupProfile: string; StudioUiEnabled: boolean; WorkspaceCapabilityEnabled: boolean } };
    expect(settings.Studio).toMatchObject({
      StartupProfile: 'NEXT_DEFAULT',
      StudioUiEnabled: true,
      WorkspaceCapabilityEnabled: true
    });
  });
});
