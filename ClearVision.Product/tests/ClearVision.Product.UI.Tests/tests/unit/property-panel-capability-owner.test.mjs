import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { createPropertyPanelCapabilityAdapter } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertySidebarController.mjs';

function readRepoText(relativeUrl) {
  return readFileSync(new URL(relativeUrl, import.meta.url), 'utf8');
}

function collectFiles(rootUrl, extension) {
  const files = [];
  for (const entry of readdirSync(rootUrl, { withFileTypes: true })) {
    const childUrl = new URL(`${entry.name}${entry.isDirectory() ? '/' : ''}`, rootUrl);
    if (entry.isDirectory()) {
      files.push(...collectFiles(childUrl, extension));
    } else if (entry.name.endsWith(extension)) {
      files.push(childUrl);
    }
  }
  return files;
}

function collectOperatorParams() {
  const operatorRoot = new URL('../../../../src/ClearVision.Product.Infrastructure/Operators/', import.meta.url);
  const records = [];
  for (const fileUrl of collectFiles(operatorRoot, '.cs')) {
    const source = readFileSync(fileUrl, 'utf8');
    const matches = source.matchAll(/\[OperatorParam\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)"/g);
    for (const match of matches) {
      records.push({
        name: match[1],
        label: match[2],
        type: match[3]
      });
    }
  }
  return records;
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

test('Studio2 Inspector keeps the full legacy PropertyPanel capability surface', () => {
  const panelSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');

  for (const requiredText of [
    'FilePickedEvent',
    'PickFileCommand',
    'btn-pick-file',
    'data-camera-binding-select="true"',
    'gv-binding-select',
    'param-slider',
    'form-color-hidden',
    'btn-recommend',
    'btn-reset',
    'roi-editor-container',
    'calibration-draft-workbench-container',
    'setConnection(connection)'
  ]) {
    assert.match(panelSource, new RegExp(requiredText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }

  assert.match(panelSource, /previewPanelEnabled/);
  assert.match(panelSource, /auxiliaryWorkbenchesEnabled/);
});

test('PropertyPanelCapabilityOwner keeps migrated file and camera controls', () => {
  const ownerSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanelCapabilityOwner.mjs');

  for (const requiredText of [
    'FilePickedEvent',
    'PickFileCommand',
    'btn-pick-file',
    'data-camera-binding-select="true"',
    'resolveParameterControlType',
    'isPathLikeParameter',
    'normalizeAcquisitionSourceType',
    'syncImageAcquisitionSourceControls',
    "httpClient.get('/cameras/bindings')",
    'param-slider',
    'form-color-hidden'
  ]) {
    assert.match(ownerSource, new RegExp(requiredText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }

  assert.match(ownerSource, /controlType === 'file'[\s\S]*btn-pick-file/);
  assert.match(ownerSource, /controlType === 'cameraBinding'[\s\S]*data-camera-binding-select="true"/);
  assert.match(ownerSource, /webMessageBridge\.sendMessage\('PickFileCommand'/);
  assert.match(ownerSource, /webMessageBridge\.on\('FilePickedEvent'/);
  assert.match(ownerSource, /normalizeParameterName\(parameterName\) === 'filepath'/);
});

test('all backend operator parameter types are covered by migrated Inspector controls', () => {
  const panelSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');
  const params = collectOperatorParams();
  const knownTypes = new Set(['string', 'int', 'double', 'float', 'number', 'bool', 'boolean', 'enum', 'select', 'file', 'cameraBinding']);
  const unknownTypes = params.filter(param => !knownTypes.has(param.type));
  const fileParams = params.filter(param => param.type === 'file');
  const cameraBindingParams = params.filter(param => param.type === 'cameraBinding');

  assert.equal(unknownTypes.length, 0, `未覆盖参数类型: ${unknownTypes.map(param => `${param.name}:${param.type}`).join(', ')}`);
  assert.ok(fileParams.length > 0, '应至少扫描到 file 参数');
  assert.ok(cameraBindingParams.length > 0, '应至少扫描到 cameraBinding 参数');
  assert.match(panelSource, /case 'file':[\s\S]*btn-pick-file[\s\S]*PickFileCommand/);
  assert.match(panelSource, /case 'cameraBinding':[\s\S]*data-camera-binding-select="true"/);
});

test('app composition root uses PropertyPanelCapabilityOwner with legacy PropertyPanel fallback', () => {
  const appSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/app.js');

  assert.match(appSource, /const PROPERTY_PANEL_CAPABILITY_FLAG_KEY = 'Studio2\.PropertyPanel'/);
  assert.match(appSource, /const PROPERTY_PANEL_CAPABILITY_ENABLED = readPropertyPanelCapabilityFlagOnce\(\);/);
  assert.match(appSource, /function createPropertyPanelCapabilityOwner\(\)/);
  assert.match(appSource, /isPropertyPanelCapabilityEnabled\(\)[\s\S]*\? createPropertyPanelCapabilityOwner\(\)/);
  assert.match(appSource, /if \(!propertyPanelOwner\) \{[\s\S]*propertyPanelOwner = await createLegacyPropertyPanelOwner\(\);[\s\S]*\}/);
  assert.equal((appSource.match(/new PropertyPanel\('property-panel'/g) || []).length, 1);
  assert.equal((appSource.match(/new PropertyPanelCapabilityOwner\(/g) || []).length, 1);
  assert.match(appSource, /propertyPanelCapabilityOwner\.mjs/);
  assert.match(appSource, /serviceRegistry\.register\('propertyPanelCapabilityOwner'/);
  assert.match(appSource, /createPropertyPanelCapabilityAdapter/);
  assert.match(appSource, /previewPanelEnabled:\s*ownsPreviewSidebar/);
  assert.match(appSource, /previewResourcesEnabled:\s*!isPreviewPanelCapabilityEnabled\(\)/);
  assert.match(appSource, /auxiliaryWorkbenchesEnabled/);
  assert.match(appSource, /panel\.setConnection/);
  assert.doesNotMatch(appSource, /import\s+\{\s*PropertyPanel\s*\}\s+from\s+'\.\/features\/flow-editor\/propertyPanel\.js'/);
  assert.match(appSource, /legacyPropertyPanelModulePromise = import\('\.\/features\/flow-editor\/propertyPanel\.js'\)/);
  assert.doesNotMatch(appSource, /trackedSubscribe\(subscribeSelectedOperator/);
  assert.match(appSource, /disposePropertyPanelOwner\(\);/);
});
