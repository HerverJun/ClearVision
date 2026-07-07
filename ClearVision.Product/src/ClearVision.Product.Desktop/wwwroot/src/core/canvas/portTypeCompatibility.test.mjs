import test from 'node:test';
import assert from 'node:assert/strict';
import {
    arePortTypesCompatible,
    getPortTypeMismatchMessage,
    normalizePortType
} from './portTypeCompatibility.mjs';

test('Region port type is normalized from enum and string forms', () => {
    assert.equal(normalizePortType('region'), 'Region');
    assert.equal(normalizePortType('13'), 13);
    assert.equal(normalizePortType(13), 13);
});

test('Image cannot connect directly to Region but Any and Region can', () => {
    assert.equal(arePortTypesCompatible('Image', 'Region'), false);
    assert.equal(arePortTypesCompatible(0, 13), false);
    assert.equal(arePortTypesCompatible('Region', 'Region'), true);
    assert.equal(arePortTypesCompatible('Any', 'Region'), true);
});

test('Image to Region mismatch explains the BinaryImageToRegion product path', () => {
    const message = getPortTypeMismatchMessage('Image', 'Region');

    assert.match(message, /二值图转区域/);
    assert.match(message, /图像形态学闭运算/);
});
