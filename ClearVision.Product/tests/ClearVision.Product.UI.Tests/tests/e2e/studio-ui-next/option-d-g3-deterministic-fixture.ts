import type { Page, Route } from '@playwright/test';

export type OptionDG3FixtureMode =
  | 'overview'
  | 'projects-data'
  | 'projects-empty'
  | 'operators'
  | 'diagnostics'
  | 'about';

export interface OptionDG3RequestEvidence {
  readonly method: string;
  readonly pathname: string;
  readonly search: string;
  readonly authorization: string | null;
  readonly handledAs: string;
}

export interface OptionDG3FixtureAudit {
  readonly requests: OptionDG3RequestEvidence[];
}

const projectId = '11111111-1111-4111-8111-111111111111';
const flowId = '22222222-2222-4222-8222-222222222222';
const token = 'option-d-g3-session-token';

const projectSummary = Object.freeze({
  id: projectId,
  name: '瓶盖检测',
  description: '稳定工程摘要',
  version: '1.0.0',
  persistenceRevision: 12,
  createdAt: '2026-07-15T01:00:00Z',
  modifiedAt: '2026-07-15T02:00:00Z',
  lastOpenedAt: '2026-07-15T03:00:00Z'
});

const recentProject = Object.freeze({
  ...projectSummary,
  description: 'Browser fixture 最近工程'
});

const categoryLabels = Object.freeze([
  '采集', '图像预处理', '分割与区域', '特征提取', '匹配与定位', '缺陷检测', '测量',
  '标定与坐标', 'AI 推理', '3D 点云', '数据处理', '流程控制', '通信', '输出与辅助'
]);

function operator(index: number): Readonly<Record<string, unknown>> {
  const type = 1000 + index;
  const categoryId = index === 0 ? 8 : index % categoryLabels.length;
  const lifecycle = index === 0 ? 1 : 0;
  return Object.freeze({
    fixtureId: `option-d-g3-operator-${String(index + 1).padStart(3, '0')}`,
    originalOperatorType: index % 158,
    type,
    displayName: index === 0 ? '颜色分析' : `性能算子 ${String(index + 1).padStart(3, '0')}`,
    description: index === 0
      ? '分析图像颜色分布并输出检测区域。'
      : `${categoryLabels[categoryId]}目录项 ${index + 1}`,
    categoryId,
    category: categoryLabels[categoryId],
    lifecycle,
    lifecycleNote: lifecycle === 0 ? null : '该版本仍在现场数据验证中。',
    defaultHidden: false,
    iconName: 'operator',
    keywords: index === 0 ? ['颜色', 'Color'] : [`fixture-${index + 1}`],
    tags: ['视觉检测', '目录验证'],
    version: '1.0.0',
    inputPorts: [{
      name: index === 0 ? 'Image' : 'Input',
      displayName: index === 0 ? '图像' : '输入',
      dataType: 0,
      isRequired: true,
      description: null
    }],
    outputPorts: [{
      name: 'Result',
      displayName: '结果',
      dataType: 6,
      isRequired: false,
      description: null
    }],
    parameters: index === 0
      ? [{
          name: 'Threshold',
          displayName: '颜色差异阈值',
          description: '差异超过此阈值时标记为候选缺陷区域。',
          dataType: 'double',
          defaultValue: 0.5,
          minValue: 0,
          maxValue: 1,
          isRequired: true,
          options: null
        }, {
          name: 'ColorSpace',
          displayName: '颜色空间',
          description: '选择用于比较颜色差异的计算空间。',
          dataType: 'enum',
          defaultValue: 'Lab',
          minValue: null,
          maxValue: null,
          isRequired: true,
          options: [{ label: 'Lab 感知颜色', value: 'Lab' }]
        }, {
          name: 'IgnoreLowSaturation',
          displayName: '忽略低饱和度区域',
          description: null,
          dataType: 'boolean',
          defaultValue: true,
          minValue: null,
          maxValue: null,
          isRequired: false,
          options: null
        }, {
          name: 'MinimumDefectArea',
          displayName: '最小缺陷面积',
          description: null,
          dataType: 'double',
          defaultValue: 12.5,
          minValue: 0,
          maxValue: 100000,
          isRequired: false,
          options: null
        }]
      : [{
          name: 'Value',
          displayName: '值',
          description: null,
          dataType: 'double',
          defaultValue: 0.5,
          minValue: 0,
          maxValue: 1,
          isRequired: true,
          options: null
        }]
  });
}

const operators = Object.freeze(Array.from({ length: 250 }, (_, index) => operator(index)));

export const optionDG3DeterministicFixture = Object.freeze({
  schemaVersion: 1,
  id: 'option-d-g3-read-surfaces.v1',
  dataSource: 'OPTION_D_G3_FIXTURE',
  approvedFixture: 'option-d-g0-deterministic.v1',
  token,
  projectId,
  projectSummary,
  recentProject,
  operators,
  health: Object.freeze({
    status: 'Healthy',
    port: 5177,
    service: 'ClearVision 本地服务',
    version: '2.8.0'
  }),
  setupStatus: Object.freeze({
    requiresInitialAdminSetup: false,
    usernameMinLength: 3,
    passwordMinLength: 6,
    requiresUppercase: false,
    requiresLowercase: false,
    requiresDigit: false
  }),
  startup: Object.freeze({
    schemaVersion: 1,
    uiKind: 'studio-ui',
    hostKind: 'browser-test',
    studioUiBasePath: '/studio/',
    startupProfile: 'NEXT_DEFAULT',
    profileAllowedRoles: Object.freeze(['Admin', 'Engineer', 'Operator']),
    featureFlags: Object.freeze({
      'Studio2.Workspace': true,
      'Studio2.Settings': true,
      'Studio2.StationsRead': true,
      'Studio2.InspectionRun': true,
      'Studio2.AiWorkbench': true
    })
  })
});

function userFor(mode: OptionDG3FixtureMode): Readonly<Record<string, string>> {
  if (mode === 'overview') {
    return Object.freeze({ userId: 'option-d-g3-overview', username: 'fixture-operator', role: 'Engineer' });
  }
  if (mode === 'diagnostics' || mode === 'about') {
    return Object.freeze({ userId: 'option-d-g3-support', username: '现场工程师', role: 'Engineer' });
  }
  return Object.freeze({ userId: 'option-d-g3-engineer', username: 'fixture-engineer', role: 'Engineer' });
}

function authorizationOf(route: Route): string | null {
  return route.request().headers().authorization ?? null;
}

function fulfillJson(route: Route, status: number, payload: unknown): Promise<void> {
  return route.fulfill({
    status,
    contentType: 'application/json',
    headers: { 'x-clearvision-data-source': optionDG3DeterministicFixture.dataSource },
    body: JSON.stringify(payload)
  });
}

export async function installOptionDG3DeterministicFixture(
  page: Page,
  mode: OptionDG3FixtureMode
): Promise<OptionDG3FixtureAudit> {
  const fixture = optionDG3DeterministicFixture;
  const requests: OptionDG3RequestEvidence[] = [];

  function audit(route: Route, handledAs: string): void {
    const url = new URL(route.request().url());
    requests.push(Object.freeze({
      method: route.request().method(),
      pathname: url.pathname,
      search: url.search,
      authorization: authorizationOf(route),
      handledAs
    }));
  }

  async function getOnly(route: Route, handledAs: string, payload: unknown): Promise<void> {
    audit(route, handledAs);
    if (route.request().method() !== 'GET') {
      await fulfillJson(route, 405, { message: 'Method not allowed.' });
      return;
    }
    await fulfillJson(route, 200, payload);
  }

  await page.route('**/api/**', route => {
    audit(route, 'UNHANDLED_FAIL_CLOSED');
    throw new Error(
      `Option D G3 fixture received an unhandled API request: ${route.request().method()} ${route.request().url()}`
    );
  });
  await page.route('**/health', route => getOnly(route, 'health', fixture.health));
  await page.route('**/api/auth/setup-status', route =>
    getOnly(route, 'auth/setup-status', fixture.setupStatus));
  await page.route('**/api/auth/me', route => {
    audit(route, 'auth/me');
    if (route.request().method() !== 'GET') {
      return fulfillJson(route, 405, { message: 'Method not allowed.' });
    }
    if (authorizationOf(route) !== `Bearer ${fixture.token}`) {
      return fulfillJson(route, 401, { message: '需要登录。', errorCode: 'AUTH_REQUIRED' });
    }
    return fulfillJson(route, 200, userFor(mode));
  });
  await page.route('**/api/projects/recent**', route =>
    getOnly(route, 'projects/recent', [fixture.recentProject]));
  await page.route('**/api/projects/search**', route =>
    getOnly(route, 'projects/search', mode === 'projects-empty' ? [] : [fixture.projectSummary]));
  await page.route(`**/api/projects/${projectId}`, route => getOnly(route, 'projects/detail', {
    ...fixture.projectSummary,
    flow: {
      id: flowId,
      name: '主流程',
      operators: [{ id: 'a' }, { id: 'b' }],
      connections: [{ id: 'c' }],
      decisionConfiguration: {
        finalDecisionBinding: { sourceOperatorId: 'b' },
        missingDecisionPolicy: 'Undetermined'
      }
    },
    assets: {
      schemaVersion: 1,
      calibrationAssets: [{ assetId: 'calibration' }],
      spatialAssets: [{ assetId: 'spatial' }]
    }
  }));
  await page.route('**/api/projects', route =>
    getOnly(route, 'projects', mode === 'projects-empty' ? [] : [fixture.projectSummary]));
  await page.route('**/api/operators/library**', route =>
    getOnly(route, 'operators/library', fixture.operators));
  await page.route('**/api/operators/*/metadata', route => {
    const match = /\/api\/operators\/(\d+)\/metadata$/.exec(new URL(route.request().url()).pathname);
    const item = match ? fixture.operators.find(candidate => candidate.type === Number(match[1])) : null;
    audit(route, 'operators/metadata');
    if (route.request().method() !== 'GET') {
      return fulfillJson(route, 405, { message: 'Method not allowed.' });
    }
    return fulfillJson(route, item ? 200 : 404, item ?? { error: 'NotFound' });
  });

  await page.addInitScript(({ authToken, startup, fixtureId }) => {
    localStorage.setItem('clearvision.studio-ui.preferences.v1', JSON.stringify({
      schemaVersion: 1,
      theme: 'light',
      density: 'compact',
      rememberedUsername: null
    }));
    sessionStorage.setItem('cv_auth_token', authToken);
    sessionStorage.setItem('cv_option_d_g3_fixture_id', fixtureId);
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      configurable: false,
      writable: false,
      value: Object.freeze({
        ...startup,
        profileAllowedRoles: Object.freeze([...startup.profileAllowedRoles]),
        featureFlags: Object.freeze({ ...startup.featureFlags }),
        apiBaseUrl: `${window.location.origin}/api`
      })
    });
  }, {
    authToken: fixture.token,
    startup: fixture.startup,
    fixtureId: fixture.id
  });

  return Object.freeze({ requests });
}
