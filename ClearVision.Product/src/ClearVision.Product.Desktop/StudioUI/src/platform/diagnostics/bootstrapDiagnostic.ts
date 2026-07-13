function describeBootstrapError(error: unknown): string {
  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  return 'StudioUI could not validate its startup configuration.';
}

export function renderBootstrapDiagnostic(
  target: string | Element,
  error: unknown
): Element {
  const requestedTarget = typeof target === 'string'
    ? document.querySelector(target)
    : target;
  const mountTarget = requestedTarget instanceof Element
    ? requestedTarget
    : document.body;

  const diagnostic = document.createElement('main');
  diagnostic.className = 'foundation-page bootstrap-diagnostic';
  diagnostic.dataset.studioPage = 'bootstrap-diagnostic';

  const heading = document.createElement('h1');
  heading.textContent = 'StudioUI startup failed';

  const summary = document.createElement('p');
  summary.textContent = 'The host startup contract was rejected. StudioUI stopped before mounting.';

  const detail = document.createElement('pre');
  detail.textContent = describeBootstrapError(error);

  diagnostic.append(heading, summary, detail);
  mountTarget.replaceChildren(diagnostic);
  return diagnostic;
}
