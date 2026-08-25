import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, type Locator, type Page, test } from '@playwright/test';
import {
  installOptionDG3DeterministicFixture,
  optionDG3DeterministicFixture,
  type OptionDG3FixtureAudit,
  type OptionDG3FixtureMode
} from './option-d-g3-deterministic-fixture';

type CapturePhase = 'reference' | 'candidate';
type ScreenId = 'D02' | 'D03' | 'D04' | 'D15' | 'D22' | 'D23';
type MasterAxis = 'verticalEdges' | 'horizontalEdges';

interface BoxEvidence {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly right: number;
  readonly bottom: number;
}

interface MasterEdge {
  readonly id: string;
  readonly cssPixel: number;
}

interface MasterMeasurement {
  readonly id: ScreenId;
  readonly file: string;
  readonly sha256: string;
  readonly verticalEdges: readonly MasterEdge[];
  readonly horizontalEdges: readonly MasterEdge[];
}

interface AnchorEvidence {
  readonly axis: 'x' | 'y';
  readonly id: string;
  readonly authority: 'SCREEN_MASTER' | 'G2_PRODUCT_SHELL' | 'D03_D04_FAMILY';
  readonly actualCssPixel: number;
  readonly expectedCssPixel: number;
  readonly rawMasterCssPixel: number;
  readonly deltaCssPixels: number;
  readonly toleranceCssPixels: number;
  readonly withinTolerance: boolean;
}

interface GeometryEvidence {
  readonly viewport: { readonly width: number; readonly height: number };
  readonly horizontalOverflow: number;
  readonly verticalOverflow: number;
  readonly contentHorizontalOverflow: number;
  readonly pageHorizontalOverflow: number;
  readonly mainCount: number;
  readonly topbarPageOverlap: number;
  readonly topbar: BoxEvidence;
  readonly content: BoxEvidence;
  readonly page: BoxEvidence;
  readonly primarySurface: BoxEvidence;
  readonly familySurface: BoxEvidence | null;
  readonly familyToolbar: BoxEvidence | null;
  readonly familyFooter: BoxEvidence | null;
  readonly anchors: readonly AnchorEvidence[];
}

interface QueryCleanupSnapshot {
  readonly activeOwnerCount: number;
  readonly activeRequestCount: number;
}

interface ProjectLifecycleSnapshot {
  readonly ownerCount: number;
  readonly activeAbortControllerCount: number;
  readonly inFlightCommandCount: number;
  readonly disposed: boolean;
}

interface OwnerCleanupEvidence {
  readonly result: 'PASS';
  readonly initialProjectLifecycle: ProjectLifecycleSnapshot;
  readonly firstUnmount: QueryCleanupSnapshot;
  readonly firstUnmountProjectLifecycle: ProjectLifecycleSnapshot;
  readonly remountProjectLifecycle: ProjectLifecycleSnapshot;
  readonly secondUnmount: QueryCleanupSnapshot;
  readonly secondUnmountProjectLifecycle: ProjectLifecycleSnapshot;
}

interface FunctionalAuditEvidence {
  readonly result: 'PASS';
  readonly sourceId: string;
  readonly regionsConfirmed: readonly string[];
  readonly controlsConfirmed: readonly string[];
  readonly forbiddenAdditionsChecked: readonly string[];
}

interface VisualComparison {
  readonly changedPixels: number;
  readonly changedPixelRatio: number;
  readonly maxChannelDelta: number;
  readonly width: number;
  readonly height: number;
}

interface CaptureEvidence {
  readonly id: string;
  readonly screen: ScreenId;
  readonly route: string;
  readonly state: OptionDG3FixtureMode;
  readonly phase: CapturePhase;
  readonly screenshot: string;
  readonly sha256: string;
  readonly width: number;
  readonly height: number;
  readonly cssViewport: { readonly width: number; readonly height: number };
  readonly masterSha256: string;
  readonly geometry: GeometryEvidence;
  readonly functionalAssertions: 'PASS';
  readonly functionalAudit: FunctionalAuditEvidence;
  readonly ownerCleanup: OwnerCleanupEvidence;
  readonly requestAudit: OptionDG3FixtureAudit['requests'];
  readonly runtimeErrors: readonly string[];
  readonly fontFamily: string;
  readonly theme: 'light';
  readonly density: 'compact';
  readonly comparison?: VisualComparison;
  readonly referenceSha256?: string;
  readonly diff?: string;
  readonly diffSha256?: string;
  readonly overlay?: string;
  readonly overlaySha256?: string;
}

const requestedVisualPhase = process.env.CV_OPTION_D_G3_VISUAL_PHASE?.trim();
const gateInvocationId = process.env.CV_OPTION_D_G3_GATE_INVOCATION_ID?.trim();
if (requestedVisualPhase !== 'reference' && requestedVisualPhase !== 'candidate') {
  throw new Error(
    'CV_OPTION_D_G3_VISUAL_PHASE must be reference or candidate. Use the dedicated Option D G3 gate.'
  );
}
if (!gateInvocationId) {
  throw new Error('CV_OPTION_D_G3_GATE_INVOCATION_ID is required. Use the dedicated Option D G3 gate.');
}

const visualPhase = requestedVisualPhase as CapturePhase;
const canonicalCssViewport = Object.freeze({ width: 1920, height: 1080 });
const deterministicBrowserTime = '2026-07-15T03:00:00.000Z';
const evidenceRoot = resolve(process.cwd(), '../../../.tmp/studio-ui-next/option-d-g3/visual');
const metricsPath = resolve(
  process.cwd(),
  '../../../.tmp/studio-ui-next/option-d-g3/master-measurements.json'
);
const masterManifest = JSON.parse(readFileSync(metricsPath, 'utf8')) as {
  readonly assertionResult: string;
  readonly measurements: readonly MasterMeasurement[];
};
if (masterManifest.assertionResult !== 'PASS') {
  throw new Error(`Option D G3 master measurements are not asserted: ${metricsPath}`);
}

const masterAnchorToleranceCssPixels = 1;
const maxChangedPixelRatio = 0.01;
const productRuntimeBaselineQueryOwnerCount = 1;
const frozenReferenceSha256: Readonly<Record<string, string>> = Object.freeze({
  'd02-overview-1920x1080': '9092fd277e68b22ba7ebd24621b964a0bae0d5145d4f037cfdcd1317dfa3828a',
  'd03-projects-data-1920x1080': 'f23c0f7c2b2f1dd0b96ba65fd155082a1fea4bb1e3a4d79b6066971a3d678d76',
  'd04-projects-empty-1920x1080': '36e60095f9e6709f38ce59da5222461d75f42600dfe7a7b9f2ff9ddbbb07c50a',
  'd15-operators-1920x1080': 'e95611647aaa6453e3233e61752f52664833d045f6cedf1d7cf039ca197f3276',
  'd22-diagnostics-1920x1080': 'e634b795acb655dd135ebdedb7ee2bd19c777dc61752d2657f9c1961029b9371',
  'd23-about-1920x1080': '41ea8777ff348d262bdf49c24718217d2b67b0c52c854faf36e5b2d534160dea'
});
const referenceSealPending = Object.values(frozenReferenceSha256)
  .some(value => value === 'PENDING_G3_REFERENCE_SEAL');
const captureCases = Object.freeze([
  { id: 'd02-overview-1920x1080', screen: 'D02', mode: 'overview', route: '/overview', selector: '[data-capability="overview"]' },
  { id: 'd03-projects-data-1920x1080', screen: 'D03', mode: 'projects-data', route: '/projects', selector: '[data-capability="projects-read"]' },
  { id: 'd04-projects-empty-1920x1080', screen: 'D04', mode: 'projects-empty', route: '/projects', selector: '[data-capability="projects-read"]' },
  { id: 'd15-operators-1920x1080', screen: 'D15', mode: 'operators', route: '/operators', selector: '[data-capability="operators-read"]' },
  { id: 'd22-diagnostics-1920x1080', screen: 'D22', mode: 'diagnostics', route: '/diagnostics', selector: '[data-studio-page="diagnostics"]' },
  { id: 'd23-about-1920x1080', screen: 'D23', mode: 'about', route: '/about', selector: '[data-studio-page="about"]' }
] as const);

const functionalContracts: Readonly<Record<ScreenId, Omit<FunctionalAuditEvidence, 'result'>>> = Object.freeze({
  D02: {
    sourceId: 'D_02_overview',
    regionsConfirmed: ['page header', 'continue work', 'runtime environment', 'available functions'],
    controlsConfirmed: ['刷新概览', '查看全部工程', '查看详情', '继续配置'],
    forbiddenAdditionsChecked: ['KPI', '工程分析', '工作站遥测', 'PLC 仪表盘', '告警时间线']
  },
  D03: {
    sourceId: 'D_03_projects_data',
    regionsConfirmed: ['page header', 'project command area', 'search and sort toolbar', 'project table', 'pagination'],
    controlsConfirmed: ['刷新工程列表', '导入', '新建工程', '搜索', '排序', '查看详情', '打开', '导出', '删除'],
    forbiddenAdditionsChecked: ['批量操作', '标签筛选', '实时运行状态', '流程数量', '算子数量', '资产数量', '工程分析', '预览详情']
  },
  D04: {
    sourceId: 'D_04_projects_empty',
    regionsConfirmed: ['application shell', 'project page header', 'project toolbar', 'empty-state region'],
    controlsConfirmed: ['刷新工程列表', '导入', '新建工程', '搜索工程', '搜索', '排序', '创建工程'],
    forbiddenAdditionsChecked: ['示例工程', '新手步骤', '导入向导', '工程统计', '新导航模块', '装饰插图']
  },
  D15: {
    sourceId: 'D_15_operator_catalog',
    regionsConfirmed: ['page header', 'read-only badge', 'dense filter toolbar', 'operator table', 'pagination'],
    controlsConfirmed: ['刷新', '搜索', '分类', '生命周期', '可见范围', '端口', '参数', '清除筛选', '查看详情'],
    forbiddenAdditionsChecked: ['安装算子', '编辑算子', '删除算子', '运行算子', '算子市场', '目录统计']
  },
  D22: {
    sourceId: 'D_22_diagnostics',
    regionsConfirmed: ['page header', 'service/session/desktop-host status', 'version and environment summary', 'technical diagnostics', 'copy feedback'],
    controlsConfirmed: ['复制诊断信息', '刷新', '技术诊断'],
    forbiddenAdditionsChecked: ['运行控制', '重启服务', '显示令牌', 'API 密钥', '健康遥测', '面包屑导航', '窗口控制']
  },
  D23: {
    sourceId: 'D_23_about',
    regionsConfirmed: ['About header', 'product and version', 'license and support', 'product composition note'],
    controlsConfirmed: [],
    forbiddenAdditionsChecked: ['虚构版本', '虚构许可证', '检查更新', '运行控制', '在线支持服务', '营销主视觉']
  }
});

test.use({
  viewport: canonicalCssViewport,
  deviceScaleFactor: 2,
  colorScheme: 'light',
  locale: 'zh-CN',
  timezoneId: 'Asia/Shanghai',
  reducedMotion: 'reduce'
});

function sha256(buffer: Buffer): string {
  return createHash('sha256').update(buffer).digest('hex');
}

function pngDimensions(buffer: Buffer): { width: number; height: number } {
  if (buffer.length < 24 || buffer.toString('hex', 0, 8) !== '89504e470d0a1a0a') {
    throw new Error('Visual evidence is not a valid PNG.');
  }
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

function writeDataUrl(path: string, dataUrl: string): Buffer {
  const comma = dataUrl.indexOf(',');
  if (comma < 0) throw new Error('Canvas evidence data URL is invalid.');
  const buffer = Buffer.from(dataUrl.slice(comma + 1), 'base64');
  writeFileSync(path, buffer);
  return buffer;
}

async function boxOf(locator: Locator): Promise<BoxEvidence> {
  const box = await locator.boundingBox();
  expect(box).not.toBeNull();
  return {
    x: box!.x,
    y: box!.y,
    width: box!.width,
    height: box!.height,
    right: box!.x + box!.width,
    bottom: box!.y + box!.height
  };
}

function masterMeasurement(screen: ScreenId): MasterMeasurement {
  const measurement = masterManifest.measurements.find(candidate => candidate.id === screen);
  if (!measurement) throw new Error(`Missing G3 master measurement for ${screen}.`);
  return measurement;
}

function rawMasterEdge(screen: ScreenId, axis: MasterAxis, id: string): number {
  const edge = masterMeasurement(screen)[axis].find(candidate => candidate.id === id);
  if (!edge) throw new Error(`Missing ${screen}/${axis}/${id} master anchor.`);
  return edge.cssPixel;
}

function expectedAnchor(
  screen: ScreenId,
  axis: MasterAxis,
  id: string
): { expected: number; authority: AnchorEvidence['authority'] } {
  if (id === 'masthead-end') {
    return { expected: 74, authority: 'G2_PRODUCT_SHELL' };
  }
  if (screen === 'D04') {
    return { expected: rawMasterEdge('D03', axis, id), authority: 'D03_D04_FAMILY' };
  }
  return { expected: rawMasterEdge(screen, axis, id), authority: 'SCREEN_MASTER' };
}

async function bootScreen(
  page: Page,
  captureCase: typeof captureCases[number]
): Promise<{ audit: OptionDG3FixtureAudit; runtimeErrors: string[] }> {
  const runtimeErrors: string[] = [];
  page.on('pageerror', error => runtimeErrors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') runtimeErrors.push(message.text());
  });
  page.on('requestfailed', request => {
    runtimeErrors.push(
      `Request failed: ${request.method()} ${request.url()} (${request.failure()?.errorText ?? 'unknown'})`
    );
  });
  await page.clock.setFixedTime(deterministicBrowserTime);
  const audit = await installOptionDG3DeterministicFixture(page, captureCase.mode);
  await page.goto(`/studio/index.html#${captureCase.route}`);
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  await expect(page.locator(captureCase.selector)).toBeVisible();
  await page.addStyleTag({ content: `
    *, *::before, *::after {
      animation-duration: 0s !important;
      animation-delay: 0s !important;
      transition-duration: 0s !important;
      scroll-behavior: auto !important;
    }
  ` });
  await page.evaluate(async () => {
    await document.fonts.ready;
    await new Promise<void>(resolveFrame => {
      requestAnimationFrame(() => requestAnimationFrame(() => resolveFrame()));
    });
  });
  return { audit, runtimeErrors };
}

async function assertFunctionalContract(page: Page, screen: ScreenId): Promise<FunctionalAuditEvidence> {
  const contract = functionalContracts[screen];
  const root = page.locator(
    screen === 'D02' ? '[data-capability="overview"]'
      : screen === 'D03' || screen === 'D04' ? '[data-capability="projects-read"]'
        : screen === 'D15' ? '[data-capability="operators-read"]'
          : screen === 'D22' ? '[data-studio-page="diagnostics"]'
            : '[data-studio-page="about"]'
  );
  await expect(page.locator('[data-product-shell="ready"]')).toHaveCount(1);
  await expect(page.locator('main')).toHaveCount(1);
  await expect(root).toBeVisible();

  if (screen === 'D02') {
    await expect(root.getByRole('heading', { name: '工作台' })).toBeVisible();
    await expect(root.getByText('继续工作', { exact: true })).toBeVisible();
    await expect(root.getByText('运行环境', { exact: true })).toBeVisible();
    await expect(root.getByText('可用功能', { exact: true })).toBeVisible();
    await expect(root.getByRole('button', { name: '刷新概览' })).toHaveCount(1);
    await expect(root.getByRole('link', { name: '查看全部工程' })).toHaveCount(1);
    await expect(root.getByRole('link', { name: '查看详情' })).toHaveCount(1);
    await expect(root.getByRole('link', { name: /继续配置/ })).toHaveCount(1);
    const quickLinks = root.getByRole('navigation', { name: '产品快速入口' });
    for (const label of ['工程', '连续检测', '检测结果', '诊断', '关于']) {
      await expect(quickLinks.getByRole('link', { name: label })).toHaveCount(1);
    }
  } else if (screen === 'D03' || screen === 'D04') {
    await expect(root.getByRole('heading', { name: '工程', exact: true })).toBeVisible();
    await expect(root.getByRole('button', { name: '刷新工程列表' })).toHaveCount(1);
    await expect(root.getByRole('button', { name: '导入', exact: true })).toHaveCount(1);
    await expect(root.getByRole('button', { name: '新建工程' })).toHaveCount(1);
    await expect(root.getByLabel('搜索工程')).toHaveCount(1);
    await expect(root.getByRole('button', { name: '搜索', exact: true })).toHaveCount(1);
    await expect(root.getByLabel('排序')).toHaveCount(1);
    await expect(root.getByRole('navigation', { name: '工程列表分页' })).toHaveCount(1);
    if (screen === 'D03') {
      const table = root.getByRole('table');
      await expect(table).toHaveCount(1);
      for (const heading of ['名称', '描述', '版本', '修改时间', '最近打开', '操作']) {
        await expect(table.getByRole('columnheader', { name: heading })).toHaveCount(1);
      }
      for (const action of ['查看详情', '打开', '导出', '删除']) {
        await expect(table.getByRole(action === '查看详情' ? 'link' : 'button', {
          name: action,
          exact: true
        })).toHaveCount(1);
      }
      await expect(table.getByText('瓶盖检测', { exact: true })).toBeVisible();
    } else {
      await expect(root.getByRole('table')).toHaveCount(0);
      await expect(root.getByText('暂无工程', { exact: true })).toBeVisible();
      await expect(root.getByRole('button', { name: '创建工程' })).toHaveCount(1);
    }
  } else if (screen === 'D15') {
    await expect(root.getByRole('heading', { name: '算子库' })).toBeVisible();
    await expect(root.getByText('只读', { exact: true })).toHaveCount(1);
    await expect(root.getByRole('button', { name: '刷新', exact: true })).toHaveCount(1);
    for (const label of ['搜索算子', '分类', '生命周期', '可见范围', '端口', '参数']) {
      await expect(root.getByLabel(label)).toHaveCount(1);
    }
    await expect(root.getByRole('button', { name: /清除筛选/ })).toHaveCount(1);
    const table = root.getByRole('table');
    await expect(table).toHaveCount(1);
    for (const heading of ['算子', '分类', '生命周期', '端口', '参数', '版本', '操作']) {
      await expect(table.getByRole('columnheader', { name: heading })).toHaveCount(1);
    }
    await expect(root.getByText('输入 1 · 输出 1', { exact: true }).first()).toBeVisible();
    await expect(root.getByRole('link', { name: /查看颜色分析详情/ })).toHaveCount(1);
    await expect(root.getByRole('navigation', { name: '算子目录分页' })).toHaveCount(1);
  } else if (screen === 'D22') {
    await expect(root.getByRole('heading', { name: '运行诊断' })).toBeVisible();
    await expect(root.getByRole('button', { name: '复制诊断信息' })).toHaveCount(1);
    await expect(root.getByRole('button', { name: '刷新', exact: true })).toHaveCount(1);
    for (const status of ['本地服务', '当前会话', '桌面宿主']) {
      await expect(root.getByText(status, { exact: true }).first()).toBeVisible();
    }
    await expect(root.getByText('版本与环境', { exact: true })).toBeVisible();
    await expect(root.locator('details')).toHaveCount(1);
    await expect(root.locator('summary')).toContainText('技术诊断');
    await expect(root).not.toContainText(optionDG3DeterministicFixture.token);
    await expect(root.getByRole('button')).toHaveCount(2);
  } else {
    await expect(root.getByRole('heading', { name: '关于 ClearVision Studio' })).toBeVisible();
    await expect(root.getByText('产品与版本', { exact: true })).toBeVisible();
    await expect(root.getByText('许可与支持', { exact: true })).toBeVisible();
    await expect(root.getByText('产品组成', { exact: true })).toBeVisible();
    await expect(root.locator('button, input, select, textarea')).toHaveCount(0);
  }

  for (const forbidden of contract.forbiddenAdditionsChecked) {
    await expect(root, `${screen} contains forbidden addition: ${forbidden}`).not.toContainText(forbidden);
  }
  return { result: 'PASS', ...contract };
}

async function collectGeometry(page: Page, screen: ScreenId): Promise<GeometryEvidence> {
  const viewport = page.viewportSize();
  if (!viewport) throw new Error('G3 visual viewport is unavailable.');
  const pageLocator = page.locator(
    screen === 'D02' ? '.overview-page'
      : screen === 'D03' || screen === 'D04' ? '.projects-page'
        : screen === 'D15' ? '.operators-page'
          : screen === 'D22' ? '.diagnostics-page'
            : '.about-page'
  );
  const topbar = await boxOf(page.locator('.product-layout__topbar'));
  const content = await boxOf(page.locator('.product-layout__content'));
  const pageBox = await boxOf(pageLocator);
  let primaryLocator: Locator;
  let familySurface: BoxEvidence | null = null;
  let familyToolbar: BoxEvidence | null = null;
  let familyFooter: BoxEvidence | null = null;
  const actual = new Map<string, number>();

  actual.set('y:masthead-end', topbar.bottom);
  if (screen === 'D02') {
    const resume = await boxOf(page.locator('.overview-page__resume'));
    const environment = await boxOf(page.locator('.overview-page__environment-grid'));
    primaryLocator = page.locator('.overview-page__resume-section');
    actual.set('x:content-start', pageBox.x);
    actual.set('x:content-end', pageBox.right);
    actual.set('y:resume-start', resume.y);
    actual.set('y:environment-start', environment.y);
  } else if (screen === 'D03' || screen === 'D04') {
    primaryLocator = page.locator('.projects-page__library');
    familySurface = await boxOf(primaryLocator);
    familyToolbar = await boxOf(page.locator('.projects-page__library-toolbar'));
    familyFooter = await boxOf(page.locator('.projects-page__pagination'));
    actual.set('x:library-start', familySurface.x);
    actual.set('x:library-end', familySurface.right);
    actual.set('y:library-start', familySurface.y);
    actual.set('y:pagination-start', familyFooter.y);
  } else if (screen === 'D15') {
    const filters = await boxOf(page.locator('.operators-page__filters'));
    const table = await boxOf(page.locator('.operators-page .cv-data-table__scroll-region'));
    primaryLocator = page.locator('.operators-page__filters');
    actual.set('x:content-start', pageBox.x);
    actual.set('x:content-end', pageBox.right);
    actual.set('y:filters-start', filters.y);
    actual.set('y:table-start', table.y);
  } else if (screen === 'D22') {
    const status = await boxOf(page.locator('.diagnostics-page__status-list'));
    const version = await boxOf(page.locator('.diagnostics-page > .cv-panel').nth(1));
    primaryLocator = page.locator('.diagnostics-page__status-list');
    actual.set('x:content-start', pageBox.x);
    actual.set('x:content-end', pageBox.right);
    actual.set('y:status-start', status.y);
    actual.set('y:version-start', version.y);
  } else {
    const productGrid = await boxOf(page.locator('.about-page .cv-description-list').first());
    const support = await boxOf(page.locator('.about-page > .cv-panel').nth(1));
    primaryLocator = page.locator('.about-page .cv-description-list').first();
    actual.set('x:content-start', pageBox.x);
    actual.set('x:content-end', pageBox.right);
    actual.set('y:product-grid-start', productGrid.y);
    actual.set('y:support-start', support.y);
  }

  const primarySurface = await boxOf(primaryLocator);
  const documentGeometry = await page.evaluate(() => {
    const contentElement = document.querySelector('.product-layout__content');
    const pageElement = contentElement?.firstElementChild;
    return {
      horizontalOverflow: Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth),
      verticalOverflow: Math.max(0, document.documentElement.scrollHeight - document.documentElement.clientHeight),
      contentHorizontalOverflow: Math.max(0, (contentElement?.scrollWidth ?? 0) - (contentElement?.clientWidth ?? 0)),
      pageHorizontalOverflow: Math.max(0, (pageElement?.scrollWidth ?? 0) - (pageElement?.clientWidth ?? 0)),
      mainCount: document.querySelectorAll('main').length
    };
  });
  const horizontalIntersection = Math.max(0, Math.min(topbar.right, pageBox.right) - Math.max(topbar.x, pageBox.x));
  const verticalIntersection = Math.max(0, Math.min(topbar.bottom, pageBox.bottom) - Math.max(topbar.y, pageBox.y));
  const topbarPageOverlap = horizontalIntersection > 0 ? verticalIntersection : 0;
  const measurement = masterMeasurement(screen);
  const anchors: AnchorEvidence[] = [];
  for (const [axis, edges] of [
    ['verticalEdges', measurement.verticalEdges],
    ['horizontalEdges', measurement.horizontalEdges]
  ] as const) {
    for (const edge of edges) {
      const coordinateAxis = axis === 'verticalEdges' ? 'x' : 'y';
      const actualValue = actual.get(`${coordinateAxis}:${edge.id}`);
      if (actualValue === undefined) throw new Error(`Missing candidate anchor ${screen}/${axis}/${edge.id}.`);
      const target = expectedAnchor(screen, axis, edge.id);
      const delta = Math.abs(actualValue - target.expected);
      anchors.push({
        axis: coordinateAxis,
        id: edge.id,
        authority: target.authority,
        actualCssPixel: actualValue,
        expectedCssPixel: target.expected,
        rawMasterCssPixel: edge.cssPixel,
        deltaCssPixels: delta,
        toleranceCssPixels: masterAnchorToleranceCssPixels,
        withinTolerance: delta <= masterAnchorToleranceCssPixels
      });
    }
  }

  const evidence: GeometryEvidence = {
    viewport,
    ...documentGeometry,
    topbarPageOverlap,
    topbar,
    content,
    page: pageBox,
    primarySurface,
    familySurface,
    familyToolbar,
    familyFooter,
    anchors
  };
  if (!referenceSealPending) {
    expect(evidence.horizontalOverflow).toBe(0);
    expect(evidence.verticalOverflow).toBe(0);
    expect(evidence.contentHorizontalOverflow).toBe(0);
    expect(evidence.pageHorizontalOverflow).toBe(0);
    expect(evidence.mainCount).toBe(1);
    expect(evidence.topbarPageOverlap).toBe(0);
    expect(pageBox.x).toBeGreaterThanOrEqual(0);
    expect(pageBox.right).toBeLessThanOrEqual(viewport.width);
    expect(primarySurface.x).toBeGreaterThanOrEqual(0);
    expect(primarySurface.right).toBeLessThanOrEqual(viewport.width);
  }
  return evidence;
}

async function readProjectLifecycle(page: Page): Promise<ProjectLifecycleSnapshot> {
  return page.evaluate(() => {
    const diagnostics = (window as Window & {
      __STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__?: {
        ownerCount: number;
        activeAbortControllerCount: number;
        inFlightCommandCount: number;
        disposed: boolean;
      };
    }).__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__;
    if (!diagnostics) throw new Error('Project lifecycle diagnostics are unavailable.');
    return {
      ownerCount: diagnostics.ownerCount,
      activeAbortControllerCount: diagnostics.activeAbortControllerCount,
      inFlightCommandCount: diagnostics.inFlightCommandCount,
      disposed: diagnostics.disposed
    };
  });
}

async function readQueryCleanup(page: Page): Promise<QueryCleanupSnapshot> {
  const valueFor = async (label: string): Promise<number> => {
    const text = await page.getByText(label, { exact: true }).locator('..').locator('dd').textContent();
    const value = Number(text?.trim());
    if (!Number.isFinite(value)) throw new Error(`Query diagnostic ${label} is not numeric: ${text ?? '<null>'}`);
    return value;
  };
  return {
    activeOwnerCount: await valueFor('活动查询组'),
    activeRequestCount: await valueFor('活动请求')
  };
}

async function navigateHash(page: Page, route: string, selector: string): Promise<void> {
  await page.evaluate(nextRoute => { window.location.hash = nextRoute; }, route);
  await expect(page).toHaveURL(new RegExp(`#${route.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`));
  await expect(page.locator(selector)).toBeVisible();
  await page.evaluate(() => new Promise<void>(resolveFrame => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolveFrame()));
  }));
}

async function assertOwnerCleanup(
  page: Page,
  captureCase: typeof captureCases[number]
): Promise<OwnerCleanupEvidence> {
  const initialProjectLifecycle = await readProjectLifecycle(page);
  expect(initialProjectLifecycle).toMatchObject({
    ownerCount: 1,
    activeAbortControllerCount: 0,
    inFlightCommandCount: 0,
    disposed: false
  });

  if (captureCase.screen === 'D22') {
    const initialQueries = await readQueryCleanup(page);
    expect(initialQueries).toEqual({
      activeOwnerCount: productRuntimeBaselineQueryOwnerCount,
      activeRequestCount: 0
    });
    await navigateHash(page, '/about', '[data-studio-page="about"]');
    await navigateHash(page, '/diagnostics', '[data-studio-page="diagnostics"]');
    const firstUnmount = await readQueryCleanup(page);
    const firstUnmountProjectLifecycle = await readProjectLifecycle(page);
    await navigateHash(page, '/about', '[data-studio-page="about"]');
    const remountProjectLifecycle = await readProjectLifecycle(page);
    await navigateHash(page, '/diagnostics', '[data-studio-page="diagnostics"]');
    const secondUnmount = await readQueryCleanup(page);
    const secondUnmountProjectLifecycle = await readProjectLifecycle(page);
    for (const snapshot of [firstUnmount, secondUnmount]) {
      expect(snapshot).toEqual({
        activeOwnerCount: productRuntimeBaselineQueryOwnerCount,
        activeRequestCount: 0
      });
    }
    for (const snapshot of [firstUnmountProjectLifecycle, remountProjectLifecycle, secondUnmountProjectLifecycle]) {
      expect(snapshot).toMatchObject({ ownerCount: 1, activeAbortControllerCount: 0, inFlightCommandCount: 0, disposed: false });
    }
    return {
      result: 'PASS',
      initialProjectLifecycle,
      firstUnmount,
      firstUnmountProjectLifecycle,
      remountProjectLifecycle,
      secondUnmount,
      secondUnmountProjectLifecycle
    };
  }

  await navigateHash(page, '/diagnostics', '[data-studio-page="diagnostics"]');
  const firstUnmount = await readQueryCleanup(page);
  const firstUnmountProjectLifecycle = await readProjectLifecycle(page);
  await navigateHash(page, captureCase.route, captureCase.selector);
  const remountProjectLifecycle = await readProjectLifecycle(page);
  await navigateHash(page, '/diagnostics', '[data-studio-page="diagnostics"]');
  const secondUnmount = await readQueryCleanup(page);
  const secondUnmountProjectLifecycle = await readProjectLifecycle(page);
  for (const snapshot of [firstUnmount, secondUnmount]) {
    expect(snapshot).toEqual({
      activeOwnerCount: productRuntimeBaselineQueryOwnerCount,
      activeRequestCount: 0
    });
  }
  for (const snapshot of [firstUnmountProjectLifecycle, remountProjectLifecycle, secondUnmountProjectLifecycle]) {
    expect(snapshot).toMatchObject({ ownerCount: 1, activeAbortControllerCount: 0, inFlightCommandCount: 0, disposed: false });
  }
  return {
    result: 'PASS',
    initialProjectLifecycle,
    firstUnmount,
    firstUnmountProjectLifecycle,
    remountProjectLifecycle,
    secondUnmount,
    secondUnmountProjectLifecycle
  };
}

function assertRequestContract(audit: OptionDG3FixtureAudit, screen: ScreenId): void {
  expect(audit.requests.some(request => request.handledAs === 'UNHANDLED_FAIL_CLOSED')).toBe(false);
  expect(audit.requests.every(request => request.method === 'GET')).toBe(true);
  expect(audit.requests.some(request => request.pathname === '/api/auth/me'
    && request.authorization === `Bearer ${optionDG3DeterministicFixture.token}`)).toBe(true);
  const protectedRequests = audit.requests.filter(request =>
    request.pathname.startsWith('/api/') && request.pathname !== '/api/auth/setup-status'
  );
  expect(protectedRequests.every(request =>
    request.authorization === `Bearer ${optionDG3DeterministicFixture.token}`
  )).toBe(true);
  if (screen === 'D02') {
    expect(audit.requests.some(request => request.pathname === '/api/projects/recent')).toBe(true);
  } else if (screen === 'D03' || screen === 'D04') {
    expect(audit.requests.some(request => request.pathname === '/api/projects'
      || request.pathname === '/api/projects/search')).toBe(true);
    expect(audit.requests.some(request => request.pathname === '/api/projects/recent')).toBe(true);
  } else if (screen === 'D15') {
    expect(audit.requests.some(request => request.pathname === '/api/operators/library')).toBe(true);
  }
}

async function compareWholeImage(
  page: Page,
  reference: Buffer,
  candidate: Buffer
): Promise<VisualComparison & { diffDataUrl: string; overlayDataUrl: string }> {
  return page.evaluate(async ({ referenceBase64, candidateBase64 }) => {
    const decode = async (base64: string): Promise<ImageBitmap> => {
      const response = await fetch(`data:image/png;base64,${base64}`);
      return createImageBitmap(await response.blob());
    };
    const [referenceImage, candidateImage] = await Promise.all([
      decode(referenceBase64),
      decode(candidateBase64)
    ]);
    if (referenceImage.width !== candidateImage.width || referenceImage.height !== candidateImage.height) {
      throw new Error('Reference and candidate dimensions differ.');
    }
    const width = referenceImage.width;
    const height = referenceImage.height;
    const source = document.createElement('canvas');
    source.width = width;
    source.height = height;
    const context = source.getContext('2d', { willReadFrequently: true });
    if (!context) throw new Error('2D comparison context is unavailable.');
    context.drawImage(referenceImage, 0, 0);
    const referencePixels = context.getImageData(0, 0, width, height);
    context.clearRect(0, 0, width, height);
    context.drawImage(candidateImage, 0, 0);
    const candidatePixels = context.getImageData(0, 0, width, height);
    const diff = context.createImageData(width, height);
    const overlay = context.createImageData(width, height);
    let changedPixels = 0;
    let maxChannelDelta = 0;
    const perChannelThreshold = 8;
    for (let index = 0; index < referencePixels.data.length; index += 4) {
      const delta = Math.max(
        Math.abs(referencePixels.data[index] - candidatePixels.data[index]),
        Math.abs(referencePixels.data[index + 1] - candidatePixels.data[index + 1]),
        Math.abs(referencePixels.data[index + 2] - candidatePixels.data[index + 2]),
        Math.abs(referencePixels.data[index + 3] - candidatePixels.data[index + 3])
      );
      maxChannelDelta = Math.max(maxChannelDelta, delta);
      const changed = delta > perChannelThreshold;
      if (changed) changedPixels += 1;
      const luma = Math.round(
        referencePixels.data[index] * 0.2126 +
        referencePixels.data[index + 1] * 0.7152 +
        referencePixels.data[index + 2] * 0.0722
      );
      diff.data[index] = changed ? 229 : luma;
      diff.data[index + 1] = changed ? 42 : luma;
      diff.data[index + 2] = changed ? 50 : luma;
      diff.data[index + 3] = 255;
      overlay.data[index] = Math.round((referencePixels.data[index] + candidatePixels.data[index]) / 2);
      overlay.data[index + 1] = Math.round((referencePixels.data[index + 1] + candidatePixels.data[index + 1]) / 2);
      overlay.data[index + 2] = Math.round((referencePixels.data[index + 2] + candidatePixels.data[index + 2]) / 2);
      overlay.data[index + 3] = 255;
    }
    const diffCanvas = document.createElement('canvas');
    diffCanvas.width = width;
    diffCanvas.height = height;
    diffCanvas.getContext('2d')?.putImageData(diff, 0, 0);
    const overlayCanvas = document.createElement('canvas');
    overlayCanvas.width = width;
    overlayCanvas.height = height;
    overlayCanvas.getContext('2d')?.putImageData(overlay, 0, 0);
    referenceImage.close();
    candidateImage.close();
    return {
      changedPixels,
      changedPixelRatio: changedPixels / (width * height),
      maxChannelDelta,
      width,
      height,
      diffDataUrl: diffCanvas.toDataURL('image/png'),
      overlayDataUrl: overlayCanvas.toDataURL('image/png')
    };
  }, {
    referenceBase64: reference.toString('base64'),
    candidateBase64: candidate.toString('base64')
  });
}

async function captureImage(page: Page, id: string): Promise<{
  screenshot: string;
  sha256: string;
  width: number;
  height: number;
  comparison?: VisualComparison;
  referenceSha256?: string;
  diff?: string;
  diffSha256?: string;
  overlay?: string;
  overlaySha256?: string;
}> {
  const phaseDirectory = resolve(evidenceRoot, visualPhase);
  mkdirSync(phaseDirectory, { recursive: true });
  const screenshotPath = resolve(phaseDirectory, `${id}.png`);
  const expectedHash = frozenReferenceSha256[id];
  if (!expectedHash) throw new Error(`Missing G3 reference identity for ${id}.`);
  const pendingReferenceSeal = expectedHash === 'PENDING_G3_REFERENCE_SEAL';
  const existingFrozenReference = visualPhase === 'reference'
    && !pendingReferenceSeal
    && existsSync(screenshotPath);
  const screenshot = existingFrozenReference
    ? readFileSync(screenshotPath)
    : await page.screenshot({
        animations: 'disabled',
        caret: 'hide',
        fullPage: false,
        path: visualPhase === 'candidate' ? screenshotPath : undefined
      });
  const dimensions = pngDimensions(screenshot);
  expect(dimensions).toEqual({ width: 3840, height: 2160 });
  if (visualPhase === 'reference') {
    if (pendingReferenceSeal) writeFileSync(screenshotPath, screenshot);
    else {
      expect(sha256(screenshot), `G3 reference hash changed for ${id}`).toBe(expectedHash);
      if (!existingFrozenReference) writeFileSync(screenshotPath, screenshot, { flag: 'wx' });
    }
    return { screenshot: screenshotPath, sha256: sha256(screenshot), ...dimensions };
  }
  if (pendingReferenceSeal) {
    throw new Error(`G3 reference hash for ${id} has not been frozen in source.`);
  }
  const referencePath = resolve(evidenceRoot, 'reference', `${id}.png`);
  expect(existsSync(referencePath), `Missing G3 reference ${referencePath}`).toBe(true);
  const reference = readFileSync(referencePath);
  expect(sha256(reference), `Frozen G3 reference hash changed for ${id}`).toBe(expectedHash);
  const comparison = await compareWholeImage(page, reference, screenshot);
  const diffPath = resolve(phaseDirectory, `${id}.diff.png`);
  const overlayPath = resolve(phaseDirectory, `${id}.overlay.png`);
  const diff = writeDataUrl(diffPath, comparison.diffDataUrl);
  const overlay = writeDataUrl(overlayPath, comparison.overlayDataUrl);
  expect(comparison.changedPixelRatio, `${id} exceeds the G3 whole-image threshold`)
    .toBeLessThanOrEqual(maxChangedPixelRatio);
  return {
    screenshot: screenshotPath,
    sha256: sha256(screenshot),
    ...dimensions,
    comparison: {
      changedPixels: comparison.changedPixels,
      changedPixelRatio: comparison.changedPixelRatio,
      maxChannelDelta: comparison.maxChannelDelta,
      width: comparison.width,
      height: comparison.height
    },
    referenceSha256: sha256(reference),
    diff: diffPath,
    diffSha256: sha256(diff),
    overlay: overlayPath,
    overlaySha256: sha256(overlay)
  };
}

test.describe('Option D G3 read-only and list-page visual-functional evidence', () => {
  test.describe.configure({ mode: 'serial' });
  const captures: CaptureEvidence[] = [];

  test.afterAll(() => {
    if (captures.length !== captureCases.length) {
      throw new Error(`G3 ${visualPhase} captured ${captures.length}/${captureCases.length} required images.`);
    }
    const actualIds = captures.map(capture => capture.id).sort();
    expect(actualIds).toEqual(Object.keys(frozenReferenceSha256).sort());
    mkdirSync(evidenceRoot, { recursive: true });
    writeFileSync(resolve(evidenceRoot, `${visualPhase}.json`), `${JSON.stringify({
      schemaVersion: 2,
      gateInvocationId,
      fixtureId: optionDG3DeterministicFixture.id,
      fixtureSchemaVersion: optionDG3DeterministicFixture.schemaVersion,
      approvedFixture: optionDG3DeterministicFixture.approvedFixture,
      dataSource: optionDG3DeterministicFixture.dataSource,
      visualAuthority: '_visual_master/option_D/screens',
      functionalAuditAuthority: '_visual_master/image_prompts.json',
      forbiddenAdditionAuthority: '_visual_master/functional_remapping.json',
      masterMeasurements: metricsPath,
      visualPhase,
      referenceSealStatus: referenceSealPending ? 'PENDING_SOURCE_PATCH' : 'FROZEN',
      canonicalCssViewport,
      deviceScaleFactor: 2,
      theme: 'light',
      density: 'compact',
      maskPolicy: 'NO_MASKS',
      complete: true,
      thresholds: {
        perChannelDelta: 8,
        maxChangedPixelRatio,
        masterAnchorToleranceCssPixels,
        productRuntimeBaselineQueryOwnerCount,
        globalOverflowPixels: 0,
        incoherentOverlapCount: 0
      },
      captures
    }, null, 2)}\n`);
  });

  for (const captureCase of captureCases) {
    test(`${captureCase.id} light/compact DSF2`, async ({ page }) => {
      const { audit, runtimeErrors } = await bootScreen(page, captureCase);
      const functionalAudit = await assertFunctionalContract(page, captureCase.screen);
      const geometry = await collectGeometry(page, captureCase.screen);
      const image = await captureImage(page, captureCase.id);
      const ownerCleanup = await assertOwnerCleanup(page, captureCase);
      assertRequestContract(audit, captureCase.screen);
      expect(runtimeErrors).toEqual([]);
      const fontFamily = await page.locator('body').evaluate(element => getComputedStyle(element).fontFamily);
      expect(fontFamily).toContain('Segoe UI');
      captures.push({
        id: captureCase.id,
        screen: captureCase.screen,
        route: captureCase.route,
        state: captureCase.mode,
        phase: visualPhase,
        cssViewport: canonicalCssViewport,
        masterSha256: masterMeasurement(captureCase.screen).sha256,
        geometry,
        functionalAssertions: 'PASS',
        functionalAudit,
        ownerCleanup,
        requestAudit: [...audit.requests],
        runtimeErrors: [...runtimeErrors],
        fontFamily,
        theme: 'light',
        density: 'compact',
        ...image
      });
    });
  }
});
