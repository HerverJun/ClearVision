import { afterEach, describe, expect, it } from 'vitest';
import { renderBootstrapDiagnostic } from '@/platform/diagnostics/bootstrapDiagnostic';

afterEach(() => {
  document.body.innerHTML = '';
});

describe('bootstrap diagnostic', () => {
  it('renders a minimal escaped failure without mounting Vue', () => {
    document.body.innerHTML = '<div id="app"></div>';

    const diagnostic = renderBootstrapDiagnostic(
      '#app',
      new Error('Invalid startup <img src=x onerror=alert(1)>')
    );

    expect(diagnostic.getAttribute('data-studio-page')).toBe('bootstrap-diagnostic');
    expect(diagnostic.textContent).toContain('StudioUI stopped before mounting');
    expect(diagnostic.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(diagnostic.querySelector('img')).toBeNull();
  });

  it('renders a recoverable route resource failure for cold deep-link chunk errors', () => {
    document.body.innerHTML = '<div id="app"></div>';

    const diagnostic = renderBootstrapDiagnostic(
      '#app',
      new TypeError('Unable to preload CSS for /studio/assets/WorkspacePage.css')
    );

    expect(diagnostic.getAttribute('data-diagnostic-kind')).toBe('route-load');
    expect(diagnostic.textContent).toContain('页面资源加载失败');
    expect(diagnostic.querySelector('button')?.textContent).toBe('刷新 Studio');
  });
});
