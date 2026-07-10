import test from 'node:test';
import assert from 'node:assert/strict';
import {
  arePortTypesCompatible,
  buildPortTooltipModel,
  getPortTypeColor,
  getPortTypeMismatchMessage
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/portTypeCompatibility.mjs';

test('Region connection compatibility stays strict', () => {
  assert.equal(arePortTypesCompatible('Contour', 'Region'), false);
  assert.equal(arePortTypesCompatible('Image', 'Region'), false);
  assert.equal(arePortTypesCompatible('BlobList', 'Region'), false);
  assert.equal(arePortTypesCompatible('Region', 'Region'), true);
  assert.equal(arePortTypesCompatible('Any', 'Region'), true);
});

test('Region mismatch messages name the real source type and BinaryImageToRegion path', () => {
  const contourMessage = getPortTypeMismatchMessage('Contour', 'Region');
  const imageMessage = getPortTypeMismatchMessage('Image', 'Region');

  assert.match(contourMessage, /Contour\/轮廓/);
  assert.match(contourMessage, /Region\/像素区域/);
  assert.match(contourMessage, /BinaryImageToRegion/);
  assert.match(imageMessage, /Image\/图像/);
  assert.match(imageMessage, /BinaryImageToRegion/);
});

test('port presentation distinguishes Contour and Region and exposes tooltip semantics', () => {
  assert.notEqual(getPortTypeColor('Contour'), getPortTypeColor('Region'));

  const tooltip = buildPortTooltipModel({
    name: 'Region',
    displayName: '输入区域',
    dataType: 'Region',
    isRequired: true,
    description: '区域形态学主输入。'
  }, { direction: 'input' });

  assert.match(tooltip.text, /名称：输入区域（Region）/);
  assert.match(tooltip.text, /方向：输入/);
  assert.match(tooltip.text, /数据类型：Region\/像素区域/);
  assert.match(tooltip.text, /必填：是/);
  assert.match(tooltip.text, /说明：区域形态学主输入/);
});
