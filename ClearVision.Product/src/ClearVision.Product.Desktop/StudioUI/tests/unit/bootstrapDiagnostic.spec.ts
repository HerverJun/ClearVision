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
});
