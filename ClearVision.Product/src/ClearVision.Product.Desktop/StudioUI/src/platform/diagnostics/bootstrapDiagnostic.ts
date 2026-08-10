function describeBootstrapError(error: unknown): string {
  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  return 'Studio 无法验证启动配置。';
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
  heading.textContent = routeChunkFailure ? '页面资源加载失败' : 'Studio 启动失败';

  const summary = document.createElement('p');
  summary.textContent = routeChunkFailure
    ? '页面代码未能从本机资源中加载。请刷新 Studio；若问题仍在，请重新启动应用。'
    : '桌面宿主提供的启动信息不完整或无效，Studio 已停止载入以避免进入不确定状态。';

  const product = document.createElement('p');
  product.className = 'bootstrap-diagnostic__product';
  product.textContent = 'ClearVision Studio';

  const technical = document.createElement('details');
  technical.className = 'bootstrap-diagnostic__technical';
  const technicalSummary = document.createElement('summary');
  technicalSummary.textContent = '技术信息';
  const detail = document.createElement('pre');
  detail.textContent = describeBootstrapError(error);
  technical.append(technicalSummary, detail);

  const guidance = document.createElement('p');
  guidance.className = 'bootstrap-diagnostic__guidance';
  guidance.textContent = '重新加载后仍无法启动时，请复制技术信息并联系系统管理员或实施交付方。';

  const actions = document.createElement('div');
  actions.className = 'bootstrap-diagnostic__actions';
  const reload = document.createElement('button');
  reload.type = 'button';
  reload.textContent = routeChunkFailure ? '刷新 Studio' : '重新加载 Studio';
  reload.addEventListener('click', () => window.location.reload());

  const copy = document.createElement('button');
  copy.type = 'button';
  copy.textContent = '复制技术信息';
  const copyStatus = document.createElement('span');
  copyStatus.className = 'bootstrap-diagnostic__copy-status';
  copyStatus.setAttribute('role', 'status');
  copyStatus.setAttribute('aria-live', 'polite');
  copy.addEventListener('click', () => {
    void navigator.clipboard.writeText(detail.textContent ?? '').then(() => {
      copy.textContent = '已复制';
      copyStatus.textContent = '技术信息已复制。';
    }).catch(() => {
      copyStatus.textContent = '无法访问系统剪贴板，请展开后手动记录。';
    });
  });
  actions.append(reload, copy);

  diagnostic.append(product, heading, summary, actions, copyStatus, technical, guidance);
  mountTarget.replaceChildren(diagnostic);
  return diagnostic;
}
