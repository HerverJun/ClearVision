import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createPropertyPanelCapabilityAdapter } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertySidebarController.mjs';

function readRepoText(relativeUrl) {
  return readFileSync(new URL(relativeUrl, import.meta.url), 'utf8');
}

test('PropertyPanelCapabilityAdapter projects selected node metadata and writes through FlowCanvasAdapter once', () => {
  const writeCalls = [];
  const fakeFlowCanvasAdapter = {
    selectedNode: 'node-1',
    nodes: new Map([
      ['node-1', {
        id: 'node-1',
        type: 'Thresholding',
        title: 'Threshold A',
        parameters: [
          { name: 'Threshold', value: 88, dataType: 'int' },
          { name: 'NodeOnly', value: 'kept', dataType: 'string' }
        ]
      }]
    ]),
    subscribeSelection(listener) {
      listener({ selectedNodeId: 'node-1', reason: 'initial' });
      return () => {};
    },
    subscribeStructureState() {
      return () => {};
    },
    patchNodeParameters(nodeId, values, options) {
      writeCalls.push({ nodeId, values, options });
      return { updated: true, reason: 'updated', missingParameters: [] };
    }
  };
  const propertyAdapter = createPropertyPanelCapabilityAdapter({
    flowCanvasAdapter: fakeFlowCanvasAdapter,
    getOperatorMetadata: () => ({
      displayName: '阈值',
      parameters: [
        { name: 'Threshold', displayName: '阈值', value: 10, dataType: 'int' },
        { name: 'Mode', displayName: '模式', value: 'Binary', dataType: 'string' }
      ]
    })
  });

  let selectedOperator = null;
  propertyAdapter.subscribeSelectedNode(operator => {
    selectedOperator = operator;
  });

  assert.equal(selectedOperator.id, 'node-1');
  assert.equal(selectedOperator.displayName, '阈值');
  assert.deepEqual(
    selectedOperator.parameters.map(parameter => [parameter.name, parameter.value]),
    [
      ['Threshold', 88],
      ['Mode', 'Binary'],
      ['NodeOnly', 'kept']
    ]
  );

  const result = propertyAdapter.writeParameters('node-1', { Threshold: 90 });

  assert.equal(result.updated, true);
  assert.equal(writeCalls.length, 1);
  assert.equal(writeCalls[0].nodeId, 'node-1');
  assert.deepEqual(writeCalls[0].values, { Threshold: 90 });
  assert.equal(writeCalls[0].options.allowCreateParameters, true);
  assert.equal(writeCalls[0].options.parameterDefinitions.length, 3);
});

test('PropertyPanelCapabilityOwner source stays scoped to Property Panel governance', () => {
  const ownerSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanelCapabilityOwner.mjs');

  for (const requiredText of [
    '属性面板',
    '请选择一个算子',
    '参数',
    '基础信息',
    '当前算子',
    '未选择算子',
    '参数已更新',
    '参数校验失败'
  ]) {
    assert.match(ownerSource, new RegExp(requiredText));
  }

  assert.match(ownerSource, /subscribeSelectedNode/);
  assert.match(ownerSource, /writeParameters/);
  assert.doesNotMatch(ownerSource, /PreviewPanel/);
  assert.doesNotMatch(ownerSource, /NodePreviewOverlay/);
  assert.doesNotMatch(ownerSource, /GlobalVariablePanel|globalVariablePanel/);
  assert.doesNotMatch(ownerSource, /ResultPanel|resultPanel/);
  assert.doesNotMatch(ownerSource, /ImageCanvas|new\s+ImageViewerComponent/);
});

test('app composition root switches between legacy and V2 Property Panel owners by startup flag', () => {
  const appSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/app.js');

  assert.match(appSource, /const PROPERTY_PANEL_CAPABILITY_FLAG_KEY = 'Studio2\.PropertyPanel'/);
  assert.match(appSource, /const PROPERTY_PANEL_CAPABILITY_ENABLED = readPropertyPanelCapabilityFlagOnce\(\);/);
  assert.match(appSource, /if \(isPropertyPanelCapabilityEnabled\(\)\) \{[\s\S]*new PropertyPanelCapabilityOwner/);
  assert.match(appSource, /propertyPanelOwner = await createLegacyPropertyPanelOwner\(\);/);
  assert.equal((appSource.match(/new PropertyPanelCapabilityOwner\(/g) || []).length, 1);
  assert.equal((appSource.match(/new PropertyPanel\('property-panel'/g) || []).length, 1);
  assert.doesNotMatch(appSource, /import\s+\{\s*PropertyPanel\s*\}\s+from\s+'\.\/features\/flow-editor\/propertyPanel\.js'/);
  assert.match(appSource, /legacyPropertyPanelModulePromise = import\('\.\/features\/flow-editor\/propertyPanel\.js'\)/);
  assert.doesNotMatch(appSource, /trackedSubscribe\(subscribeSelectedOperator/);
  assert.match(appSource, /disposePropertyPanelOwner\(\);/);
});
