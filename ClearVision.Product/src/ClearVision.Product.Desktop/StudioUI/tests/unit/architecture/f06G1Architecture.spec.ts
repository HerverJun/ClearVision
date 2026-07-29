import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { productNavigation } from '@/app/navigation';
import { resolveSafeReturnRoute } from '@/app/router';
import { createTestRouter } from '@/test-support/createTestRouter';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const desktopRoot = resolve(studioRoot, '..');
const sourceRoot = join(studioRoot, 'src');
const capabilityRoot = join(sourceRoot, 'capabilities/ai-workbench');

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

function studioRelative(path: string): string {
  return relative(studioRoot, path).replaceAll('\\', '/');
}

describe('F06 G1 AI contract security and single owner architecture guards', () => {
  const capabilityFiles = sourceFiles(capabilityRoot);

  it('keeps the dedicated AI Workbench flag default-off and maps it through both Host startup contracts', () => {
    const options = read(join(desktopRoot, 'Configuration/StudioOptions.cs'));
    const host = read(join(desktopRoot, 'WebView2Host.cs'));

    expect(options).toMatch(/AiWorkbenchCapabilityEnabled\s*\{\s*get;\s*set;\s*\}\s*=\s*false;/);
    expect(host.match(/\["Studio2\.AiWorkbench"\]\s*=\s*/g)).toHaveLength(2);
    expect(host).toContain('studioOptions.AiWorkbenchCapabilityEnabled');
    expect(host).not.toMatch(/\["Studio2\.AiPanel"\]\s*=\s*studioOptions\.AiWorkbenchCapabilityEnabled/);
  });

  it('keeps both AI routes lazy role-bound flag-bound and safe for return navigation', () => {
    const routerSource = read(join(sourceRoot, 'app/router.ts'));
    const routes = createTestRouter().getRoutes();
    const routeExpectations = [
      ['ai-workbench', '/ai'],
      ['project-ai-workbench', '/projects/:id/ai']
    ] as const;

    expect(routerSource).toContain(
      "const AiWorkbenchPage = () => import('@/capabilities/ai-workbench/AiWorkbenchPage.vue')"
    );
    for (const [name, path] of routeExpectations) {
      const route = routes.find(candidate => candidate.name === name);
      expect(route?.path).toBe(path);
      expect(route?.meta.allowedRoles).toEqual(['Admin', 'Engineer']);
      expect(route?.meta.requiredFeatureFlag).toBe('Studio2.AiWorkbench');
    }

    const navigation = productNavigation.find(item => item.to === '/ai');
    expect(navigation?.allowedRoles).toEqual(['Admin', 'Engineer']);
    expect(navigation?.requiredFeatureFlag).toBe('Studio2.AiWorkbench');
    expect(resolveSafeReturnRoute('/ai?sessionId=safe')).toBe('/ai?sessionId=safe');
    expect(resolveSafeReturnRoute('/projects/project-1/ai')).toBe('/projects/project-1/ai');
    expect(resolveSafeReturnRoute('//host/ai')).toBeNull();
  });

  it('keeps one route-scoped AI Session owner with explicit unmount disposal', () => {
    expect(capabilityFiles.filter(path => path.endsWith('aiSessionOwner.ts'))).toHaveLength(1);
    const ownerConsumers = sourceFiles(sourceRoot)
      .filter(path => !path.endsWith('aiSessionOwner.ts'))
      .filter(path => read(path).includes('createAiSessionOwner('))
      .map(studioRelative);
    expect(ownerConsumers).toEqual(['src/capabilities/ai-workbench/AiWorkbenchPage.vue']);

    const page = read(join(capabilityRoot, 'AiWorkbenchPage.vue'));
    expect(page).toContain('onUnmounted(() => owner.dispose())');
    expect(page).not.toMatch(/v-show|display:\s*none/);
  });

  it('uses the shared API transport and excludes forbidden legacy or parallel authority paths', () => {
    const combined = capabilityFiles.map(path => read(path)).join('\n');
    const apiAdapter = read(join(capabilityRoot, 'apiAdapter.ts'));

    expect(apiAdapter).toContain("import type { ApiTransport } from '@/platform/api'");
    expect(combined).not.toMatch(/\bfetch\s*\(|new\s+EventSource\s*\(|window\.chrome\.webview|postMessage\s*\(/);
    expect(combined).not.toMatch(/\/api\/ai\/agent-plan(?:["'/?]|$)/);
    expect(combined).not.toMatch(/localStorage|defineStore\s*\(|createPinia\s*\(|EventBus|ServiceRegistry/);
    expect(combined).not.toMatch(/legacy|aiPanel|\.css["']/i);
  });

  it('keeps public frontend contracts free of private reasoning authorization and raw payload fields', () => {
    const contracts = read(join(capabilityRoot, 'contracts.ts'));
    const decoder = read(join(capabilityRoot, 'decoder.ts'));
    const publicContractSource = `${contracts}\n${decoder}`;

    expect(publicContractSource).not.toMatch(/\breasoning\b|chainOfThought|authorization|rawPayload|systemPrompt/i);
    expect(decoder).toMatch(/function exact\(source: JsonRecord, allowed: readonly string\[\], path: string\)/);
    expect(decoder.match(/exact\(source,/g)?.length).toBeGreaterThanOrEqual(4);
  });
});
