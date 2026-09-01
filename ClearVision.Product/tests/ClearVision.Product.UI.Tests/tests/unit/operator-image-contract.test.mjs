import test from 'node:test';
import assert from 'node:assert/strict';
import {
  LEGACY_IMAGE_COMPATIBILITY_NOTICE,
  normalizeImageInputContracts,
  OperatorLibraryPanel
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/operator-library/operatorLibrary.js';

test('operator library fail-closes disabled MqttPublish metadata', () => {
  const panel = Object.create(OperatorLibraryPanel.prototype);

  assert.equal(panel.normalizeOperatorMetadata({
    type: 'MqttPublish',
    exposureClassification: 'disabled'
  }), null);
  assert.equal(panel.normalizeOperatorMetadata({ type: 'MqttPublish' }), null);
  assert.equal(panel.normalizeOperatorMetadata({
    type: 'ImageAcquisition',
    exposureClassification: 'package-public'
  }).type, 'ImageAcquisition');
});

test('operator library normalizes authoritative image contract presentation', () => {
  const contracts = normalizeImageInputContracts([
    {
      InputPort: 'Image',
      ContractVersion: '2.1'
    }
  ], [
    {
        InputPort: 'Image',
        ContractVersion: '2.1',
        AllowedVariantCount: 3,
        VerifiedSupportVariantCount: 0,
        VerifiedConversionVariantCount: 0,
        LegacyCompatibilityVariantCount: 3,
        VerifiedRejectionVariantCount: 0,
        UnknownVariantCount: 25,
        HasProductionSupport: false,
        CompatibilityOnly: true,
        EvidenceSummary: LEGACY_IMAGE_COMPATIBILITY_NOTICE,
        ExactVariantGroups: [
          {
            Mode: 'Default',
            Condition: 'Legacy path',
            Admission: 'Allowed',
            Verification: 'LegacyCompatibilityAllowance',
            ExactInputTypes: ['CV_8UC1', 'CV_8UC3', 'CV_8UC4'],
            ConversionPolicy: 'None',
            OutputDepthPolicy: 'Legacy',
            DynamicRangePolicy: '8-bit',
            InputValuePolicy: 'Any',
            FailureCode: 'IMAGE_DEPTH_UNSUPPORTED',
            EvidenceLevel: 'E0_SOURCE_AUDIT'
          }
        ]
    }
  ]);

  assert.equal(contracts.length, 1);
  assert.equal(contracts[0].compatibilityOnly, true);
  assert.equal(contracts[0].hasProductionSupport, false);
  assert.equal(contracts[0].evidenceSummary, LEGACY_IMAGE_COMPATIBILITY_NOTICE);
  assert.deepEqual(contracts[0].exactVariantGroups[0].exactInputTypes, ['CV_8UC1', 'CV_8UC3', 'CV_8UC4']);
});

test('operator library fallback grouping preserves exact pairs without Cartesian expansion', () => {
  const contracts = normalizeImageInputContracts([
    {
      inputPort: 'Image',
      contractVersion: '2.1',
      variants: [
        {
          mode: 'Fixed', depth: 'CV_64F', channels: 1, condition: 'Fixed threshold',
          admission: 'Allowed', verification: 'VerifiedSupport', conversionPolicy: 'None',
          outputDepthPolicy: 'CV_8U', dynamicRangePolicy: 'Binary', inputValuePolicy: 'Any',
          failureCode: 'THRESHOLD_DEPTH_UNSUPPORTED', evidenceLevel: 'E2_STAGE2_RUNTIME'
        },
        {
          mode: 'Fixed', depth: 'CV_32F', channels: 3, condition: 'Fixed threshold',
          admission: 'Allowed', verification: 'VerifiedSupport', conversionPolicy: 'ColorToGray',
          outputDepthPolicy: 'CV_8U', dynamicRangePolicy: 'Binary', inputValuePolicy: 'Any',
          failureCode: 'THRESHOLD_DEPTH_UNSUPPORTED', evidenceLevel: 'E2_STAGE2_RUNTIME'
        }
      ]
    }
  ]);

  const exactTypes = contracts[0].exactVariantGroups.flatMap(group => group.exactInputTypes);
  assert.deepEqual(exactTypes.sort(), ['CV_32FC3', 'CV_64FC1']);
  assert.ok(!exactTypes.includes('CV_64FC3'));
  assert.ok(!exactTypes.includes('CV_32FC1'));
});

test('operator library preview renders compatibility evidence and exact input types', () => {
  const panel = Object.create(OperatorLibraryPanel.prototype);
  const html = panel.renderImageInputContracts(normalizeImageInputContracts([
    {
      inputPort: 'Image',
      contractVersion: '2.1',
      presentation: {
        inputPort: 'Image',
        contractVersion: '2.1',
        allowedVariantCount: 3,
        verifiedSupportVariantCount: 0,
        verifiedConversionVariantCount: 0,
        legacyCompatibilityVariantCount: 3,
        verifiedRejectionVariantCount: 0,
        unknownVariantCount: 25,
        hasProductionSupport: false,
        compatibilityOnly: true,
        evidenceSummary: LEGACY_IMAGE_COMPATIBILITY_NOTICE,
        exactVariantGroups: [{
          mode: 'Default',
          condition: 'Legacy path',
          admission: 'Allowed',
          verification: 'LegacyCompatibilityAllowance',
          exactInputTypes: ['CV_8UC1', 'CV_8UC3', 'CV_8UC4'],
          conversionPolicy: 'None',
          outputDepthPolicy: 'Legacy',
          dynamicRangePolicy: '8-bit',
          inputValuePolicy: 'Any',
          failureCode: 'IMAGE_DEPTH_UNSUPPORTED',
          evidenceLevel: 'E0_SOURCE_AUDIT'
        }]
      }
    }
  ]));

  assert.match(html, /Legacy 8U compatibility allowance — unverified/);
  assert.match(html, /LegacyCompatibilityAllowance/);
  assert.match(html, /CV_8UC1, CV_8UC3, CV_8UC4/);
  assert.doesNotMatch(html, /Verified support: [1-9]/);
});
