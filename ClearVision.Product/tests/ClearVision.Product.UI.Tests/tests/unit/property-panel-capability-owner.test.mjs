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

test('PropertyPanelCapabilityAdapter creates and reuses RectangleRegion for Caliper SearchRegion', () => {
  const connections = [];
  const addNodeCalls = [];
  const patchCalls = [];
  const structureReasons = [];
  const nodes = new Map([
    ['caliper-1', {
      id: 'caliper-1',
      type: 'CaliperTool',
      title: 'Caliper A',
      x: 400,
      y: 120,
      inputs: [
        { name: 'Image', dataType: 'Image' },
        { name: 'SearchRegion', dataType: 'Rectangle' }
      ],
      outputs: [],
      parameters: []
    }]
  ]);
  const canvas = {
    connections,
    addConnection(source, sourcePort, target, targetPort) {
      const connection = {
        id: `conn-${connections.length + 1}`,
        source,
        sourcePort,
        target,
        targetPort
      };
      connections.push(connection);
      return connection;
    }
  };
  const fakeFlowCanvasAdapter = {
    selectedNode: 'caliper-1',
    nodes,
    raw: canvas,
    addNode(type, x, y, config) {
      const node = {
        id: `region-${addNodeCalls.length + 1}`,
        type,
        title: config.title,
        x,
        y,
        inputs: config.inputs,
        outputs: config.outputs,
        parameters: config.parameters
      };
      nodes.set(node.id, node);
      addNodeCalls.push({ type, x, y, config, node });
      return node;
    },
    patchNodeParameters(nodeId, values, options) {
      const node = nodes.get(nodeId);
      patchCalls.push({ nodeId, values, options });
      for (const [name, value] of Object.entries(values)) {
        const parameter = node.parameters.find(item => item.name === name);
        if (parameter) {
          parameter.value = value;
        } else {
          node.parameters.push({ name, value, dataType: 'int' });
        }
      }
      return { updated: true, reason: 'updated', missingParameters: [] };
    },
    markFlowStructureChanged(reason) {
      structureReasons.push(reason);
    }
  };
  const propertyAdapter = createPropertyPanelCapabilityAdapter({
    flowCanvasAdapter: fakeFlowCanvasAdapter,
    getOperatorMetadata(type) {
      if (type === 'RectangleRegion') {
        return {
          displayName: 'Rectangle Region',
          parameters: [
            { name: 'X', dataType: 'int', value: 0 },
            { name: 'Y', dataType: 'int', value: 0 },
            { name: 'Width', dataType: 'int', value: 1 },
            { name: 'Height', dataType: 'int', value: 1 }
          ],
          inputPorts: [],
          outputPorts: [
            { name: 'Rectangle', dataType: 'Rectangle' }
          ]
        };
      }

      return null;
    }
  });

  const created = propertyAdapter.upsertCaliperSearchRegion('caliper-1', {
    X: 10,
    Y: 12,
    Width: 30,
    Height: 16
  });

  assert.equal(created.updated, true);
  assert.equal(created.reason, 'created');
  assert.equal(addNodeCalls.length, 1);
  assert.equal(addNodeCalls[0].type, 'RectangleRegion');
  assert.equal(addNodeCalls[0].x, 140);
  assert.equal(addNodeCalls[0].y, 120);
  assert.deepEqual(connections[0], {
    id: 'conn-1',
    source: 'region-1',
    sourcePort: 0,
    target: 'caliper-1',
    targetPort: 1
  });
  assert.deepEqual(
    created.operator.parameters.map(parameter => [parameter.name, parameter.value]),
    [
      ['X', 10],
      ['Y', 12],
      ['Width', 30],
      ['Height', 16]
    ]
  );
  assert.deepEqual(structureReasons, ['caliper-search-region-upsert']);

  const reused = propertyAdapter.upsertCaliperSearchRegion('caliper-1', {
    X: 11,
    Y: 13,
    Width: 31,
    Height: 17
  });

  assert.equal(reused.updated, true);
  assert.equal(addNodeCalls.length, 1);
  assert.equal(patchCalls.length, 1);
  assert.equal(patchCalls[0].nodeId, 'region-1');
  assert.deepEqual(patchCalls[0].values, {
    X: 11,
    Y: 13,
    Width: 31,
    Height: 17
  });
  assert.equal(propertyAdapter.getCaliperSearchRegionBinding('caliper-1').sourceNode.id, 'region-1');
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
    'form-color-hidden',
    'RoiEditorPanel',
    'getOperatorRoiConfig',
    'previewCoordinator',
    'previewResourcesEnabled',
    'onOpenPreviewImage',
    'data-property-geometry-editor-container',
    'property-geometry-section',
    'upsertCaliperSearchRegion'
  ]) {
    assert.match(ownerSource, new RegExp(requiredText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }

  assert.match(ownerSource, /controlType === 'file'[\s\S]*btn-pick-file/);
  assert.match(ownerSource, /controlType === 'cameraBinding'[\s\S]*data-camera-binding-select="true"/);
  assert.match(ownerSource, /webMessageBridge\.sendMessage\('PickFileCommand'/);
  assert.match(ownerSource, /webMessageBridge\.on\('FilePickedEvent'/);
  assert.match(ownerSource, /normalizeParameterName\(parameterName\) === 'filepath'/);
  assert.match(ownerSource, /this\.propertyAdapter\.writeParameters\(this\.currentNodeId, writeValues\)/);
  assert.match(ownerSource, /this\.propertyAdapter\.upsertCaliperSearchRegion\?\.\(this\.currentNodeId, writeValues\)/);
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
  assert.match(appSource, /previewCoordinator:\s*nodePreviewCoordinator/);
  assert.match(appSource, /previewResourcesEnabled:\s*!isPreviewPanelCapabilityEnabled\(\)/);
  assert.match(appSource, /onOpenPreviewImage:\s*openImageViewerFromPreview/);
  assert.match(appSource, /circleSearchV2ToolEnabled:\s*readStartupFeatureFlagOnce\('Studio:CircleSearchV2ToolEnabled'\)/);
  assert.match(appSource, /nPointCalibrationWorkbenchEnabled:\s*readStartupFeatureFlagOnce\('Studio:NPointCalibrationWorkbenchEnabled'\)/);
  assert.match(appSource, /auxiliaryWorkbenchesEnabled/);
  assert.match(appSource, /panel\.setConnection/);
  assert.doesNotMatch(appSource, /import\s+\{\s*PropertyPanel\s*\}\s+from\s+'\.\/features\/flow-editor\/propertyPanel\.js'/);
  assert.match(appSource, /legacyPropertyPanelModulePromise = import\('\.\/features\/flow-editor\/propertyPanel\.js'\)/);
  assert.doesNotMatch(appSource, /trackedSubscribe\(subscribeSelectedOperator/);
  assert.match(appSource, /disposePropertyPanelOwner\(\);/);
});
