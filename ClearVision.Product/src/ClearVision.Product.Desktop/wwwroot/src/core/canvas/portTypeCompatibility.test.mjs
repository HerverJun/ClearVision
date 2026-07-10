import test from 'node:test';
import assert from 'node:assert/strict';
import {
    arePortTypesCompatible,
    buildPortTooltipModel,
    canonicalizeOperatorPortType,
    getPortTypeColor,
    getPortTypeMismatchMessage,
    normalizePortType
} from './portTypeCompatibility.mjs';

test('Region and Blob port types are normalized from enum and string forms', () => {
    assert.equal(normalizePortType('region'), 'Region');
    assert.equal(normalizePortType('13'), 13);
    assert.equal(normalizePortType(13), 13);
    assert.equal(normalizePortType('blob_list'), 'BlobList');
    assert.equal(normalizePortType(15), 15);
});

test('strict Region compatibility rejects Image and Contour while Region remains compatible', () => {
    assert.equal(arePortTypesCompatible('Contour', 'Region'), false);
    assert.equal(arePortTypesCompatible(7, 13), false);
    assert.equal(arePortTypesCompatible('Image', 'Region'), false);
    assert.equal(arePortTypesCompatible(0, 13), false);
    assert.equal(arePortTypesCompatible('Region', 'Region'), true);
    assert.equal(arePortTypesCompatible(13, 13), true);
});

test('Any compatibility remains explicit and does not turn known mismatches into Any', () => {
    assert.equal(arePortTypesCompatible('Any', 'Region'), true);
    assert.equal(arePortTypesCompatible('Contour', 'Any'), true);
    assert.equal(normalizePortType('Contour'), 'Contour');
    assert.equal(normalizePortType('Image'), 'Image');
    assert.equal(arePortTypesCompatible('BlobList', 'Region'), false);
    assert.equal(arePortTypesCompatible('BlobFeatureList', 'BlobList'), false);
});

test('Contour to Region mismatch explains the semantic difference and conversion path', () => {
    const message = getPortTypeMismatchMessage('Contour', 'Region');

    assert.equal(
        message,
        '当前输出是 Contour/轮廓，不是 Region/像素区域。区域形态学需要 Region；请从二值图使用 BinaryImageToRegion 生成 Region，或改用轮廓测量、Blob特征处理算子。'
    );
});

test('Image to Region mismatch explains the BinaryImageToRegion product path', () => {
    const message = getPortTypeMismatchMessage('Image', 'Region');

    assert.equal(message, '当前输出是 Image/图像，不是 Region；请插入 BinaryImageToRegion。');
});

test('Blob result list cannot masquerade as Region', () => {
    const message = getPortTypeMismatchMessage('BlobList', 'Region');

    assert.equal(arePortTypesCompatible('BlobList', 'Region'), false);
    assert.match(message, /BlobList\/Blob结果列表/);
    assert.match(message, /Region\/像素区域/);
    assert.match(message, /BinaryImageToRegion/);
});

test('Contour and Region colors are visibly distinct', () => {
    const contour = hexToRgb(getPortTypeColor('Contour'));
    const region = hexToRgb(getPortTypeColor('Region'));
    const distance = Math.hypot(contour.r - region.r, contour.g - region.g, contour.b - region.b);

    assert.notEqual(getPortTypeColor('Contour'), getPortTypeColor('Region'));
    assert.ok(distance > 120, `expected visible RGB distance, got ${distance}`);
});

test('port tooltip includes name, direction, type, required state and description', () => {
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

test('legacy BlobAnalysis port declarations are migrated without renaming old ports', () => {
    assert.equal(canonicalizeOperatorPortType('BlobAnalysis', 'Blobs', 'output', 'Contour'), 'BlobList');
    assert.equal(canonicalizeOperatorPortType('BlobAnalysis', 'BlobFeatures', 'output', 'Any'), 'BlobFeatureList');
    assert.equal(canonicalizeOperatorPortType('BlobLabeling', 'Blobs', 'input', 'Contour'), 'BlobList');
    assert.equal(canonicalizeOperatorPortType('ContourDetection', 'Contours', 'output', 'Contour'), 'Contour');
});

function hexToRgb(hex) {
    const normalized = String(hex).replace('#', '');
    return {
        r: Number.parseInt(normalized.slice(0, 2), 16),
        g: Number.parseInt(normalized.slice(2, 4), 16),
        b: Number.parseInt(normalized.slice(4, 6), 16)
    };
}
