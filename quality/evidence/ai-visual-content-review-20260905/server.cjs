const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../..');
const web = path.join(root, 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot');
const tests = path.join(root, 'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e');
const ts = require(path.join(root, 'ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/node_modules/typescript'));

// Reuse the repository's scenario data through its TypeScript AST.
function extract(file, names) {
  const source = ts.createSourceFile(file, fs.readFileSync(path.join(tests, file), 'utf8'), ts.ScriptTarget.Latest, true);
  const selected = source.statements.filter(node => {
    if (ts.isFunctionDeclaration(node)) return names.includes(node.name?.text);
    if (ts.isVariableStatement(node)) return node.declarationList.declarations.some(d => names.includes(d.name.getText(source)));
    return false;
  });
  return ts.transpileModule(selected.map(node => node.getText(source)).join('\n'), {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.None }
  }).outputText;
}
const fixtureSources = [
  extract('authHelper.ts', ['E2E_ADMIN_CAPABILITIES', 'E2E_USER']),
  extract('ai-shell-visual.spec.ts', ['seedActivePlan']),
  extract('ai-plan-clarification.spec.ts', ['basePlan', 'imageSourceQuestion', 'outputQuestion', 'seedPlan']),
  extract('ai-build-workspace.spec.ts', ['buildFlow', 'createBuildPayload', 'seedBuild'])
].join('\n');
const setup = `
${fixtureSources}
const reviewParams = new URLSearchParams(location.search);
const reviewTheme = reviewParams.get('theme') || 'light';
sessionStorage.clear();
sessionStorage.setItem('cv_auth_token', 'isolated-visual-review');
sessionStorage.setItem('cv_current_user', JSON.stringify(E2E_USER));
localStorage.setItem('cv_welcome_shown', 'true');
localStorage.removeItem('cv_ai_session_id');
localStorage.removeItem('cv_ai_agent_dev_ui');
const originalFetch = window.fetch.bind(window);
window.fetch = async (input, options) => {
  const url = new URL(typeof input === 'string' ? input : input.url, location.href);
  if (!url.pathname.startsWith('/api/')) return originalFetch(input, options);
  let data = [];
  if (url.pathname === '/api/auth/me') data = E2E_USER;
  else if (url.pathname === '/api/settings') data = {general:{softwareTitle:'ClearVision',theme:reviewTheme,autoStart:false}};
  else if (url.pathname === '/api/health') data = {ok:true};
  else if (url.pathname === '/api/ai/models') data = [{id:'review-model',name:'视觉任务模型',provider:'OpenAI Compatible',model:'vision-model',baseUrl:'https://example.invalid/v1',isActive:true,isEnabled:true,hasApiKey:true,roleBindings:['generation'],priority:100,timeoutMs:120000}];
  else if (url.pathname.includes('reasoning-support')) data = {familyName:'Compatible',familyId:'compatible',allowedModes:['auto'],allowedEfforts:['medium']};
  return new Response(JSON.stringify(data), {status:200,headers:{'Content-Type':'application/json'}});
};
window.addEventListener('load', () => {
  let attempts = 0;
  const timer = setInterval(async () => {
    if (!window.aiPanel && document.querySelector('#app:not(.hidden)')) document.querySelector('.nav-btn[data-view="ai"]')?.click();
    if (!window.aiPanel && ++attempts < 150) return;
    clearInterval(timer);
    try {
      if (!window.aiPanel) throw new Error('AI panel not initialized');
      document.documentElement.dataset.theme = reviewTheme;
      document.querySelector('.nav-btn[data-view="ai"]').click();
      const page = {evaluate: async (fn, arg) => fn(arg), locator: () => ({})};
      const scenario = reviewParams.get('scenario') || 'idle';
      if (scenario === 'plan') await seedActivePlan(page);
      else if (scenario === 'clarify') await seedPlan(page, {resourcePending:true});
      else if (scenario !== 'idle') await seedBuild(page, scenario);
      document.body.dataset.reviewScenario = scenario;
      document.body.dataset.reviewReady = 'true';
    } catch (error) {
      console.error('Review fixture:', error);
      document.body.dataset.reviewError = error.message;
    }
  }, 100);
});
function expect() { return new Proxy({}, {get: () => () => {}}); }
`;
const types = {'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.mjs':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.json':'application/json','.svg':'image/svg+xml','.png':'image/png','.woff2':'font/woff2'};
http.createServer((req,res) => {
  const url = new URL(req.url, 'http://127.0.0.1:5082');
  if (url.pathname === '/review-setup.js') {
    res.writeHead(200, {'Content-Type':types['.js'], 'Cache-Control':'no-store'});
    return res.end(setup);
  }
  const file = path.resolve(web, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
  if (!file.startsWith(web + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
    res.writeHead(404); return res.end();
  }
  let content = fs.readFileSync(file);
  if (file === path.join(web,'index.html')) content = content.toString('utf8').replace('<head>', '<head><script src="/review-setup.js"></script>');
  res.writeHead(200, {'Content-Type':types[path.extname(file)] || 'application/octet-stream', 'Cache-Control':'no-store'});
  res.end(content);
}).listen(5082, '127.0.0.1');
