function describeBootstrapError(error: unknown): string {
  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  return 'StudioUI could not validate its startup configuration.';
}

const routeChunkErrorPatterns = Object.freeze([
  /failed to fetch dynamically imported module/i,
  /error loading dynamically imported module/i,
  /importing a module script failed/i,
  /unable to preload css/i,
  /chunkloaderror/i
]);

export function isRouteChunkLoadError(error: unknown): boolean {
  const message = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  return routeChunkErrorPatterns.some(pattern => pattern.test(message));
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
  const routeChunkFailure = isRouteChunkLoadError(error);
  diagnostic.dataset.diagnosticKind = routeChunkFailure ? 'route-load' : 'startup-contract';

  const heading = document.createElement('h1');
  heading.textContent = routeChunkFailure ? '页面资源加载失败' : 'StudioUI startup failed';

  const summary = document.createElement('p');
  summary.textContent = routeChunkFailure
    ? '页面代码未能从本机资源中加载。请刷新 Studio；若问题仍在，请重新启动应用。'
    : 'The host startup contract was rejected. StudioUI stopped before mounting.';

  const detail = document.createElement('pre');
  detail.textContent = describeBootstrapError(error);

  diagnostic.append(heading, summary, detail);
  if (routeChunkFailure) {
    const reload = document.createElement('button');
    reload.type = 'button';
    reload.textContent = '刷新 Studio';
    reload.addEventListener('click', () => window.location.reload());
    diagnostic.append(reload);
  }
  mountTarget.replaceChildren(diagnostic);
  return diagnostic;
}
