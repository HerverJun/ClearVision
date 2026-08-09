import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const sourceRoot = join(studioRoot, 'src');
const repositoryRoot = resolve(studioRoot, '../../../..');

function read(path: string): string {
  return readFileSync(path, 'utf8');
}

describe('F03 G1 composition and startup flag guards', () => {
  it('mounts the formal Workspace route inside the one ProductLayout and ProductRuntime', () => {
    const router = read(join(sourceRoot, 'app/router.ts'));
    const layout = read(join(sourceRoot, 'app/layouts/ProductLayout.vue'));
    const runtime = read(join(sourceRoot, 'app/productRuntimeFactory.ts'));

    expect(router.match(/path:\s*'projects\/:id\/workspace'/g)).toHaveLength(1);
    expect(router).toContain("name: 'project-workspace'");
    expect(router).toContain('workspaceMode: true');
    expect(layout.match(/<main(?:\s|>)/g)).toHaveLength(1);
    expect(layout).toContain("const routeViewKey = computed(() => route.name === 'project-workspace'");
    expect(layout).toContain("? 'project-workspace'");
    expect(layout).toContain(': route.path);');
    expect(layout).toContain(':key="routeViewKey"');
    expect(layout).toContain("route.meta.workspaceMode === true");
    expect(layout).not.toContain('item.label.slice(0, 1)');
    expect(layout).toContain(':name="navigationIcons[item.to]');
    expect(layout).not.toMatch(/v-show=.*workspace/i);
    expect(runtime).toContain('const workspace = createWorkspaceRuntime({');
    expect(runtime).toContain('workspace.dispose();');
  });

  it('uses the named F09 default and exposes one startup flag mapping', () => {
    const settings = JSON.parse(read(join(
      repositoryRoot,
      'ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json'
    ))) as { Studio: { StartupProfile: string; StudioUiEnabled: boolean; WorkspaceCapabilityEnabled: boolean } };
    const options = read(join(
      repositoryRoot,
      'ClearVision.Product/src/ClearVision.Product.Desktop/Configuration/StudioOptions.cs'
    ));
    const host = read(join(
      repositoryRoot,
      'ClearVision.Product/src/ClearVision.Product.Desktop/WebView2Host.cs'
    ));

    expect(settings.Studio.StartupProfile).toBe('NEXT_DEFAULT');
    expect(settings.Studio.StudioUiEnabled).toBe(true);
    expect(settings.Studio.WorkspaceCapabilityEnabled).toBe(true);
    expect(options).toContain('StudioUiEnabled { get; set; } = false');
    expect(options).toContain('WorkspaceCapabilityEnabled { get; set; } = false');
    expect(host.match(/\["Studio2\.Workspace"\]\s*=\s*studioOptions\.WorkspaceCapabilityEnabled/g))
      .toHaveLength(1);
  });
});
