import test from 'node:test';
import assert from 'node:assert/strict';
import { buildOperatorNodeConfig } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/operatorVisuals.js';

// 这些用例锁定「算子库 → 画布」端口契约：
//  - metadata 明确给出空数组时，必须原样保留空端口，不得伪造 Any 端口；
//  - metadata 缺失该侧端口时，才允许使用兼容 fallback（旧流程/旧数据兼容）；
//  - metadata 给出真实端口时，端口数量/名称/类型必须与 metadata 一致。

// ImageAcquisition 的真实后端契约：2 个可选输入端口 + 1 个图像输出端口。
// 详见 ImageAcquisitionOperator.cs（Image/FilePath 输入端口为运行时供图刻意声明）。
const IMAGE_ACQUISITION_METADATA = {
  type: 'ImageAcquisition',
  displayName: '图像采集',
  category: '采集',
  inputPorts: [
    { name: 'Image', displayName: 'Runtime supplied image', dataType: 'Image', isRequired: false },
    { name: 'FilePath', displayName: '文件路径输入', dataType: 'String', isRequired: false }
  ],
  outputPorts: [
    { name: 'Image', displayName: '图像', dataType: 'Image' }
  ]
};

// RectangleRegion：真正的「无输入」几何源头算子，metadata 明确声明 inputPorts: []。
const RECTANGLE_REGION_METADATA = {
  type: 'RectangleRegion',
  displayName: '矩形框定义',
  category: '几何',
  inputPorts: [],
  outputPorts: [
    { name: 'Rectangle', displayName: '矩形', dataType: 'Rectangle' }
  ]
};

test('explicit empty inputPorts is preserved, no fabricated Any port', () => {
  const config = buildOperatorNodeConfig('RectangleRegion', RECTANGLE_REGION_METADATA);
  assert.equal(config.inputs.length, 0, 'RectangleRegion must have zero input ports');
  assert.equal(config.outputs.length, 1);
  assert.equal(config.outputs[0].name, 'Rectangle');
  assert.equal(config.outputs[0].type, 'Rectangle');
});

test('PascalCase explicit empty InputPorts is also preserved', () => {
  const config = buildOperatorNodeConfig('RectangleRegion', {
    Type: 'RectangleRegion',
    InputPorts: [],
    OutputPorts: [{ Name: 'Rectangle', DataType: 'Rectangle' }]
  });
  assert.equal(config.inputs.length, 0, 'explicit InputPorts: [] must not be replaced by a fallback');
  assert.equal(config.outputs.length, 1);
});

test('ImageAcquisition keeps its two real backend-declared input ports', () => {
  const config = buildOperatorNodeConfig('ImageAcquisition', IMAGE_ACQUISITION_METADATA);
  assert.equal(config.inputs.length, 2, 'ImageAcquisition declares Image + FilePath inputs');
  assert.deepEqual(config.inputs.map(p => p.name), ['Image', 'FilePath']);
  assert.equal(config.inputs[0].type, 'Image');
  assert.equal(config.inputs[1].type, 'String');
  assert.equal(config.outputs.length, 1);
  assert.equal(config.outputs[0].name, 'Image');
  assert.equal(config.outputs[0].type, 'Image');
});

test('port ids/names/types match metadata exactly (no re-invention)', () => {
  const config = buildOperatorNodeConfig('ImageAcquisition', {
    ...IMAGE_ACQUISITION_METADATA,
    inputPorts: [
      { id: 'in-image', name: 'Image', dataType: 'Image', isRequired: true },
      { id: 'in-file', name: 'FilePath', dataType: 'String', isRequired: false }
    ]
  });
  assert.equal(config.inputs[0].id, 'in-image');
  assert.equal(config.inputs[0].isRequired, true);
  assert.equal(config.inputs[1].id, 'in-file');
  assert.equal(config.inputs[1].isRequired, false);
});

test('missing metadata ports fall back to a compatible Any port (legacy compat)', () => {
  // 旧数据/无 metadata 场景：完全没有端口声明时，保留兼容 fallback，
  // 避免旧流程反序列化/旧调用方拿不到任何端口而炸裂。
  const config = buildOperatorNodeConfig('UnknownLegacyOperator', {});
  assert.equal(config.inputs.length, 1, 'undefined inputPorts keeps one fallback input');
  assert.equal(config.inputs[0].type, 'Any');
  assert.equal(config.outputs.length, 1, 'undefined outputPorts keeps one fallback output');
  assert.equal(config.outputs[0].type, 'Any');
});

test('null data yields fallback ports without throwing', () => {
  const config = buildOperatorNodeConfig('Whatever', null);
  assert.equal(config.inputs.length, 1);
  assert.equal(config.inputs[0].type, 'Any');
  assert.equal(config.outputs.length, 1);
  assert.equal(config.outputs[0].type, 'Any');
});

test('explicit empty outputPorts is preserved (no fabricated output)', () => {
  const config = buildOperatorNodeConfig('SinkOperator', {
    type: 'SinkOperator',
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: []
  });
  assert.equal(config.inputs.length, 1);
  assert.equal(config.outputs.length, 0, 'explicit outputPorts: [] must stay empty');
});
