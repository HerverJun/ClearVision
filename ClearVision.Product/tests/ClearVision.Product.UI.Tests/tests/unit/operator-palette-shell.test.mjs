import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildOperatorGroups,
  filterOperatorsForFlyout
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/operatorPaletteShell.js';

const operators = [
  {
    type: 'Thresholding',
    displayName: '阈值分割',
    category: '预处理',
    description: '按灰度阈值生成二值图',
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Mask', dataType: 'Image' }],
    parameters: [{ name: 'Threshold', displayName: '阈值', dataType: 'int' }]
  },
  {
    type: 'ImageAcquisition',
    displayName: '图像采集',
    category: '输入',
    description: '从相机或文件读取图像',
    inputPorts: [],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
    keywords: ['camera']
  },
  {
    type: 'GaussianBlur',
    displayName: '高斯滤波',
    category: '预处理',
    description: '平滑图像噪声',
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Image', dataType: 'Image' }]
  }
];

test('OperatorPaletteShell groups operators by category for the rail', () => {
  const groups = buildOperatorGroups(operators);

  assert.deepEqual(groups.map(group => [group.label, group.operators.length]), [
    ['输入', 1],
    ['预处理', 2]
  ]);
  assert.equal(groups[1].operators[0].displayName, '高斯滤波');
  assert.equal(groups[1].operators[1].displayName, '阈值分割');
});

test('OperatorPaletteShell search keeps name, type, description, port, parameter and keyword matches', () => {
  assert.deepEqual(
    filterOperatorsForFlyout(operators, 'camera').map(operator => operator.type),
    ['ImageAcquisition']
  );
  assert.deepEqual(
    filterOperatorsForFlyout(operators, 'Mask').map(operator => operator.type),
    ['Thresholding']
  );
  assert.deepEqual(
    filterOperatorsForFlyout(operators, '阈值').map(operator => operator.type),
    ['Thresholding']
  );
  assert.deepEqual(
    filterOperatorsForFlyout(operators, '不存在').map(operator => operator.type),
    []
  );
});
