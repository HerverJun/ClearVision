import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { visibleProductNavigation } from '@/app/navigation';

const studioRoot = resolve(process.cwd());

function read(relativePath: string): string {
  return readFileSync(resolve(studioRoot, relativePath), 'utf8').replace(/\r\n?/g, '\n');
}

function sourceFiles(relativeDirectory: string): readonly string[] {
  const directory = resolve(studioRoot, relativeDirectory);
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const relativePath = `${relativeDirectory}/${entry.name}`;
    if (entry.isDirectory()) return sourceFiles(relativePath);
    return /\.(?:vue|css|ts)$/u.test(entry.name) ? [relativePath] : [];
  });
}

describe('M-series shared visual and navigation guards', () => {
  it('derives product navigation from role and flags without CSS positional hiding', () => {
    const operatorItems = visibleProductNavigation('Operator', {
      'Studio2.AiWorkbench': false,
      'Studio2.InspectionRun': false,
      'Studio2.Settings': false,
      'Studio2.StationsRead': true
    });
    expect(operatorItems.map(item => item.to)).toEqual([
      '/overview', '/operators', '/projects', '/results', '/stations', '/about'
    ]);

    const layout = read('src/app/layouts/product-layout.css');
    expect(layout).not.toMatch(/workspace-nav-item:nth-child/);
    expect(layout).toContain('overflow-x: auto;');
    expect(layout).toMatch(/workspace-nav-item\.is-current::after[\s\S]*?bottom:\s*0;/);
  });

  it('keeps the workspace route protected by role while flag-off stays an explicit capability state', () => {
    const router = read('src/app/router.ts');
    expect(router).toContain("path: 'projects/:id/workspace'");
    expect(router).toContain("allowedRoles: editorRoles");
    const runtime = read('src/capabilities/project-workspace/workspaceRuntime.ts');
    expect(runtime).toContain("export const workspaceCapabilityFlagKey = 'Studio2.Workspace';");
    expect(runtime).toContain('const enabled = options.featureFlags[workspaceCapabilityFlagKey] === true;');
  });

  it('defines shared elevation and zero product letter-spacing semantics', () => {
    const tokens = read('src/design-system/tokens/tokens.css');
    expect(tokens).toContain('--cv-elevation-raised: var(--cv-elevation-1);');
    expect(tokens).toContain('--cv-elevation-floating: var(--cv-elevation-2);');
    expect(tokens).toContain('--cv-letter-spacing-display: 0;');
    expect(tokens).toContain('--cv-letter-spacing-title: 0;');
    expect(tokens).toContain('--cv-letter-spacing-caption: 0;');
    expect(tokens).not.toMatch(/--cv-letter-spacing[^:]*:\s*-/);

    const typography = read('src/design-system/primitives/CvTypography.vue');
    const pageHeader = read('src/design-system/patterns/CvPageHeader.vue');
    expect(typography).not.toMatch(/letter-spacing:\s*-/);
    expect(pageHeader).not.toMatch(/letter-spacing:\s*-/);
  });

  it('keeps shared transient controls icon based and accessible', () => {
    const modal = read('src/design-system/primitives/CvModal.vue');
    const toast = read('src/design-system/primitives/CvToastRegion.vue');
    expect(modal).toContain("name=\"close\"");
    expect(toast).toContain("name=\"close\"");
    expect(modal).not.toMatch(/>\s*[×✕✖]\s*</);
    expect(toast).not.toMatch(/>\s*[×✕✖]\s*</);
  });

  it('keeps shared select focus visible in Windows forced-colors mode', () => {
    const select = read('src/design-system/primitives/CvSelect.vue');
    expect(select).toMatch(/@media \(forced-colors: active\)[\s\S]*?\.cv-select__control:focus-visible/);
    expect(select).toMatch(/outline:\s*2px solid Highlight;/);
    expect(select).toMatch(/box-shadow:\s*none;/);
  });

  it('keeps formal run context visible at Windows 125 percent layout pressure', () => {
    const statusBar = read('src/capabilities/inspection-run/RunStatusBar.vue');
    const pressureStart = statusBar.indexOf('@media (max-width: 1180px)');
    const narrowStart = statusBar.indexOf('@media (max-width: 760px)');
    const pressureStyles = statusBar.slice(pressureStart, narrowStart);

    expect(pressureStart).toBeGreaterThanOrEqual(0);
    expect(narrowStart).toBeGreaterThan(pressureStart);
    expect(pressureStyles).not.toContain('display: none');
    expect(pressureStyles).toContain('.run-status-bar__connection { flex: 1 1 auto; }');
  });

  it('keeps product source colors in the shared token table', () => {
    const rawColor = /#[0-9a-f]{3,8}\b|\brgba?\s*\(|\bhsla?\s*\(/iu;
    const violations = sourceFiles('src')
      .filter(relativePath => !relativePath.endsWith('src/design-system/tokens/tokens.css'))
      .filter(relativePath => rawColor.test(read(relativePath)));

    expect(violations).toEqual([]);
  });
});
