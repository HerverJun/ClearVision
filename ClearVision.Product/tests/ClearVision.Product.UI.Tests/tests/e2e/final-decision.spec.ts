import { expect, Page, test } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const PROJECT_ID = '11111111-1111-1111-1111-111111111111';
const OPERATOR_ID = '22222222-2222-2222-2222-222222222222';
const OUTPUT_PORT_ID = '33333333-3333-3333-3333-333333333333';

type FlowPayload = {
  operators: Array<{
    id: string;
    name: string;
    type: string;
    x: number;
    y: number;
    inputPorts: unknown[];
    outputPorts: Array<{
      id: string;
      name: string;
      displayName: string;
      dataType: string;
      direction: number;
      isRequired: boolean;
    }>;
    parameters: unknown[];
    isEnabled: boolean;
  }>;
  connections: unknown[];
  decisionConfiguration: Record<string, unknown> | null;
};

function createFlow(): FlowPayload {
  return {
    operators: [{
      id: OPERATOR_ID,
      name: '分类结果',
      type: 'ResultOutput',
      x: 320,
      y: 220,
      inputPorts: [],
      outputPorts: [{
        id: OUTPUT_PORT_ID,
        name: 'Judgment',
        displayName: '判定结果',
        dataType: 'String',
        direction: 1,
        isRequired: false,
      }],
      parameters: [],
      isEnabled: true,
    }],
    connections: [],
    decisionConfiguration: null,
  };
}

function createProject(flow: FlowPayload, persistenceRevision: number) {
  return {
    id: PROJECT_ID,
    name: '最终判定 E2E',
    description: '验证 canonical 最终判定配置闭环',
    version: '1.0.0',
    persistenceRevision,
    flow,
    globalVariables: {
      schemaVersion: '1.0',
      variables: [],
      sourceBindings: [],
      targetBindings: [],
    },
  };
}

async function openProject(page: Page, expectedNodeCount = 1) {
  await page.evaluate(async projectId => {
    const { default: projectManager } = await import('/src/features/project/projectManager.js');
    await projectManager.openProject(projectId);
  }, PROJECT_ID);

  await expect.poll(() => page.evaluate(() => {
    const canvas = (window as typeof window & { flowCanvas?: { nodes?: Map<string, unknown> } }).flowCanvas;
    return canvas?.nodes?.size ?? 0;
  })).toBe(expectedNodeCount);
}

test('最终判定配置可保存重开，并在来源算子禁用后显示稳定问题码', async ({ page }) => {
  let persistedFlow = createFlow();
  let persistenceRevision = 1;
  const flowWrites: FlowPayload[] = [];

  await page.route(`**/api/projects/${PROJECT_ID}`, async route => {
    const request = route.request();
    if (request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(createProject(persistedFlow, persistenceRevision)),
      });
      return;
    }

    if (request.method() === 'PUT') {
      persistenceRevision += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(createProject(persistedFlow, persistenceRevision)),
      });
      return;
    }

    await route.fulfill({ status: 405, body: 'Method not allowed' });
  });

  await page.route(`**/api/projects/${PROJECT_ID}/flow`, async route => {
    const request = route.request();
    const body = request.postDataJSON() as FlowPayload;
    flowWrites.push(body);
    persistedFlow = structuredClone(body);
    persistenceRevision += 1;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        projectId: PROJECT_ID,
        persistenceRevision,
        flow: persistedFlow,
      }),
    });
  });

  await page.route('**/api/inspection/decision-configuration/validate', async route => {
    const flow = route.request().postDataJSON() as FlowPayload;
    const source = flow.operators.find(operator => operator.id.toLowerCase() === OPERATOR_ID);
    const binding = flow.decisionConfiguration?.finalDecisionBinding as Record<string, unknown> | undefined;
    const isDisabled = source?.isEnabled === false;
    const issues = !binding
      ? [{ code: 'DECISION_BINDING_REQUIRED', message: 'A final decision binding is required.' }]
      : isDisabled
        ? [{
            code: 'DECISION_SOURCE_OPERATOR_DISABLED',
            message: 'Final decision source operator is disabled.',
            operatorId: OPERATOR_ID,
            outputName: 'Judgment',
          }]
        : [];

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        isValid: issues.length === 0,
        issues,
        eligibleOutputs: isDisabled ? [] : [{
          operatorId: OPERATOR_ID,
          operatorName: '分类结果',
          outputPortId: OUTPUT_PORT_ID,
          outputName: 'Judgment',
          dataType: 'String',
          rule: 'StringMap',
        }],
      }),
    });
  });

  await bootAuthenticatedApp(page);
  await openProject(page);

  const entry = page.locator('#btn-final-decision');
  await expect(entry).toBeVisible();
  await expect(entry).toHaveAttribute('data-decision-state', 'invalid');
  await entry.click();

  const dialog = page.locator('.final-decision-dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('配置需要修复');
  await expect(dialog).toContainText('DECISION_BINDING_REQUIRED');

  await dialog.locator('[data-decision-source]').selectOption(`${OPERATOR_ID}:${OUTPUT_PORT_ID}`);
  await dialog.locator('[data-decision-input="okValue"]').fill('PASS');
  await dialog.locator('[data-decision-input="ngValue"]').fill('REJECT');
  await dialog.locator('[data-decision-missing-policy]').selectOption('Invalid');
  await expect(dialog.locator('.final-decision-rule-summary')).toContainText('OK：“PASS”');
  await expect(dialog.locator('.final-decision-rule-summary')).toContainText('缺失信号：判定无效');

  await dialog.locator('[data-decision-save]').click();
  await expect(dialog).toBeHidden();
  expect(flowWrites).toHaveLength(1);
  expect(flowWrites[0].decisionConfiguration).toEqual({
    finalDecisionBinding: {
      sourceOperatorId: OPERATOR_ID,
      sourceOutputPortId: OUTPUT_PORT_ID,
      sourceOutputName: 'Judgment',
      dataType: 'String',
      rule: 'StringMap',
      okValue: 'PASS',
      ngValue: 'REJECT',
    },
    missingDecisionPolicy: 'Invalid',
  });

  await page.evaluate(async projectId => {
    const { default: projectManager } = await import('/src/features/project/projectManager.js');
    await projectManager.closeProject({ promptToSave: false });
    await projectManager.openProject(projectId);
  }, PROJECT_ID);
  await expect.poll(() => page.evaluate(() => {
    const canvas = (window as typeof window & { flowCanvas?: { nodes?: Map<string, unknown> } }).flowCanvas;
    return canvas?.nodes?.size ?? 0;
  })).toBe(1);

  await entry.click();
  await expect(dialog.locator('[data-decision-source]')).toHaveValue(`${OPERATOR_ID}:${OUTPUT_PORT_ID}`);
  await expect(dialog.locator('[data-decision-input="okValue"]')).toHaveValue('PASS');
  await expect(dialog.locator('[data-decision-input="ngValue"]')).toHaveValue('REJECT');
  await expect(dialog.locator('[data-decision-missing-policy]')).toHaveValue('Invalid');

  await page.evaluate(operatorId => {
    const canvas = (window as typeof window & {
      flowCanvas?: {
        toggleNodeDisabled?: (id: string) => boolean;
      };
    }).flowCanvas;
    canvas?.toggleNodeDisabled?.(operatorId);
  }, OPERATOR_ID);

  await expect(entry).toHaveAttribute('data-decision-state', 'invalid');
  await expect(dialog).toContainText('配置需要修复');
  await expect(dialog).toContainText('DECISION_SOURCE_OPERATOR_DISABLED');
  await expect(dialog).toContainText('绑定的来源算子已禁用，请启用算子或重新选择。');
});

test('ResultOutput.Text 旧绑定显示为无效且候选仅保留 BlobCount', async ({ page }) => {
  const resultOutputId = OPERATOR_ID;
  const resultTextPortId = OUTPUT_PORT_ID;
  const blobId = '44444444-4444-4444-4444-444444444444';
  const blobCountPortId = '55555555-5555-5555-5555-555555555555';
  const flow = createFlow();
  flow.operators[0].name = '结果输出';
  flow.operators[0].outputPorts[0].name = 'Text';
  flow.operators[0].outputPorts[0].displayName = '文本';
  flow.operators.push({
    id: blobId,
    name: 'Blob 分析',
    type: 'BlobAnalysis',
    x: 120,
    y: 120,
    inputPorts: [],
    outputPorts: [{
      id: blobCountPortId,
      name: 'BlobCount',
      displayName: 'Blob 数量',
      dataType: 'Integer',
      direction: 1,
      isRequired: false,
    }],
    parameters: [],
    isEnabled: true,
  });
  flow.decisionConfiguration = {
    finalDecisionBinding: {
      sourceOperatorId: resultOutputId,
      sourceOutputPortId: resultTextPortId,
      sourceOutputName: 'Text',
      dataType: 'String',
      rule: 'StringMap',
      okValue: 'OK',
      ngValue: 'NG',
    },
    missingDecisionPolicy: 'Undetermined',
  };

  await page.route(`**/api/projects/${PROJECT_ID}`, async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(createProject(flow, 7)),
    });
  });
  await page.route('**/api/inspection/decision-configuration/validate', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        isValid: false,
        issues: [{
          code: 'DECISION_SOURCE_OUTPUT_INELIGIBLE',
          message: 'Result Output.Text is not an official decision source.',
          operatorId: resultOutputId,
          outputName: 'Text',
        }],
        eligibleOutputs: [{
          operatorId: blobId,
          operatorName: 'Blob 分析',
          outputPortId: blobCountPortId,
          outputName: 'BlobCount',
          dataType: 'Integer',
          rule: 'NumericComparison',
        }],
      }),
    });
  });

  await bootAuthenticatedApp(page);
  await page.evaluate(async projectId => {
    const { default: projectManager } = await import('/src/features/project/projectManager.js');
    await projectManager.openProject(projectId);
  }, PROJECT_ID);
  await expect.poll(() => page.evaluate(() => {
    const canvas = (window as typeof window & { flowCanvas?: { nodes?: Map<string, unknown> } }).flowCanvas;
    return canvas?.nodes?.size ?? 0;
  })).toBe(2);
  const entry = page.locator('#btn-final-decision');
  await expect(entry).toHaveAttribute('data-decision-state', 'invalid');
  await entry.click();

  const dialog = page.locator('.final-decision-dialog');
  await expect(dialog).toContainText('DECISION_SOURCE_OUTPUT_INELIGIBLE');
  const sourceSelect = dialog.locator('[data-decision-source]');
  const options = sourceSelect.locator('option');
  await expect(options).toHaveCount(2);
  await expect(options.nth(1)).toContainText('BlobCount');
  await expect(sourceSelect).not.toContainText('结果输出 → Text');
});

test('候选 Rule 与安全默认值完全来自后端，BlobCount 在显式阈值前保持无效', async ({ page }) => {
  const judgmentValuePortId = '66666666-6666-6666-6666-666666666666';
  const blobId = '77777777-7777-7777-7777-777777777777';
  const blobCountPortId = '88888888-8888-8888-8888-888888888888';
  const flow = createFlow();
  flow.operators[0].name = '结果判定';
  flow.operators[0].type = 'ResultJudgment';
  flow.operators[0].outputPorts[0].name = 'JudgmentResult';
  flow.operators[0].outputPorts[0].displayName = '判定结果';
  flow.operators[0].outputPorts.push({
    id: judgmentValuePortId,
    name: 'JudgmentValue',
    displayName: '判定值',
    dataType: 'String',
    direction: 1,
    isRequired: false,
  });
  flow.operators.push({
    id: blobId,
    name: 'Blob 分析',
    type: 'BlobAnalysis',
    x: 120,
    y: 120,
    inputPorts: [],
    outputPorts: [{
      id: blobCountPortId,
      name: 'BlobCount',
      displayName: 'Blob 数量',
      dataType: 'Integer',
      direction: 1,
      isRequired: false,
    }],
    parameters: [],
    isEnabled: true,
  });

  const eligibleOutputs = [{
    operatorId: OPERATOR_ID,
    operatorName: '结果判定',
    outputPortId: OUTPUT_PORT_ID,
    outputName: 'JudgmentResult',
    dataType: 'String',
    rule: 'StringMap',
    defaultOkValue: 'OK',
    defaultNgValue: 'NG',
    requiredOkValue: 'OK',
    requiredNgValue: 'NG',
  }, {
    operatorId: OPERATOR_ID,
    operatorName: '结果判定',
    outputPortId: judgmentValuePortId,
    outputName: 'JudgmentValue',
    dataType: 'String',
    rule: 'StringMap',
    defaultOkValue: '1',
    defaultNgValue: '0',
    requiredOkValue: '1',
    requiredNgValue: '0',
  }, {
    operatorId: blobId,
    operatorName: 'Blob 分析',
    outputPortId: blobCountPortId,
    outputName: 'BlobCount',
    dataType: 'Integer',
    rule: 'NumericComparison',
  }];

  await page.route(`**/api/projects/${PROJECT_ID}`, async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(createProject(flow, 11)),
    });
  });
  await page.route('**/api/inspection/decision-configuration/validate', async route => {
    const postedFlow = route.request().postDataJSON() as FlowPayload;
    const binding = postedFlow.decisionConfiguration?.finalDecisionBinding as Record<string, unknown> | undefined;
    const issues = !binding
      ? [{ code: 'DECISION_BINDING_REQUIRED', message: 'Binding required.' }]
      : binding.rule === 'NumericComparison' && (!binding.comparator || binding.threshold === null || binding.threshold === undefined)
        ? [{ code: 'DECISION_NUMERIC_COMPARISON_REQUIRED', message: 'Comparator and threshold required.' }]
        : [];
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isValid: issues.length === 0, issues, eligibleOutputs }),
    });
  });

  await bootAuthenticatedApp(page);
  await openProject(page, 2);
  const entry = page.locator('#btn-final-decision');
  await entry.click();
  const dialog = page.locator('.final-decision-dialog');
  const source = dialog.locator('[data-decision-source]');

  await source.selectOption(`${OPERATOR_ID}:${judgmentValuePortId}`);
  await expect(dialog.locator('[data-decision-input="okValue"]')).toHaveValue('1');
  await expect(dialog.locator('[data-decision-input="ngValue"]')).toHaveValue('0');
  await expect(entry).toHaveAttribute('data-decision-state', 'valid');

  await source.selectOption(`${OPERATOR_ID}:${OUTPUT_PORT_ID}`);
  await expect(dialog.locator('[data-decision-input="okValue"]')).toHaveValue('OK');
  await expect(dialog.locator('[data-decision-input="ngValue"]')).toHaveValue('NG');

  await source.selectOption(`${blobId}:${blobCountPortId}`);
  await expect(dialog.locator('[data-decision-input="comparator"]')).toHaveValue('');
  await expect(dialog.locator('[data-decision-input="threshold"]')).toHaveValue('');
  await expect(entry).toHaveAttribute('data-decision-state', 'invalid');
  await expect(dialog).toContainText('DECISION_NUMERIC_COMPARISON_REQUIRED');

  await dialog.locator('[data-decision-input="comparator"]').selectOption('GreaterThanOrEqual');
  await dialog.locator('[data-decision-input="threshold"]').fill('2');
  await expect(entry).toHaveAttribute('data-decision-state', 'valid');
  await expect.poll(() => page.evaluate(() => {
    const canvas = (window as typeof window & { flowCanvas?: { serialize?: () => FlowPayload } }).flowCanvas;
    return canvas?.serialize?.().decisionConfiguration?.finalDecisionBinding;
  })).toMatchObject({
    sourceOutputName: 'BlobCount',
    rule: 'NumericComparison',
    comparator: 'GreaterThanOrEqual',
    threshold: 2,
  });
});
