import { describe, expect, it } from 'vitest';
import {
  OperatorContractDecodeError,
  decodeOperatorCatalog,
  decodeOperatorCatalogItem
} from '@/capabilities/operators-read';

function operator(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    type: 45,
    displayName: '颜色分析',
    description: '颜色检查',
    categoryId: 8,
    category: 'AI推理',
    lifecycle: 0,
    lifecycleNote: null,
    defaultHidden: false,
    iconName: 'color',
    keywords: ['颜色', 'Color'],
    tags: ['inspection'],
    version: '1.0.0',
    inputPorts: [{
      name: 'Image',
      displayName: '图像',
      dataType: 0,
      isRequired: true,
      description: null
    }],
    outputPorts: [{
      name: 'Result',
      displayName: '结果',
      dataType: 6,
      isRequired: false,
      description: '检测结果'
    }],
    parameters: [{
      name: 'Threshold',
      displayName: '阈值',
      description: null,
      dataType: 'double',
      defaultValue: 0.5,
      minValue: 0,
      maxValue: 1,
      isRequired: true,
      options: null
    }],
    ...overrides
  };
}

describe('operator contracts', () => {
  it('decodes the numeric endpoint enums and ignores unknown extensions', () => {
    const decoded = decodeOperatorCatalogItem(operator({ futureExtension: { ignored: true } }));

    expect(decoded).toMatchObject({
      operatorType: '45',
      categoryId: 'AiInference',
      lifecycle: 'Stable',
      defaultHidden: false
    });
    expect(decoded.inputPorts[0]).toMatchObject({ name: 'Image', dataType: 'Image' });
    expect(decoded.parameters[0]).toMatchObject({ name: 'Threshold', defaultValue: 0.5 });
    expect('futureExtension' in decoded).toBe(false);
  });

  it('accepts known string enums for compatibility without accepting unknown values', () => {
    expect(decodeOperatorCatalogItem(operator({
      type: 'ColorDetection',
      categoryId: 'AiInference',
      lifecycle: 'Experimental'
    }))).toMatchObject({
      operatorType: 'ColorDetection',
      categoryId: 'AiInference',
      lifecycle: 'Experimental'
    });

    expect(() => decodeOperatorCatalogItem(operator({ categoryId: 99 })))
      .toThrow(OperatorContractDecodeError);
    expect(() => decodeOperatorCatalogItem(operator({ lifecycle: 'Ready' })))
      .toThrow(OperatorContractDecodeError);
    expect(() => decodeOperatorCatalogItem(operator({
      inputPorts: [{
        name: 'Image',
        displayName: '图像',
        dataType: 98,
        isRequired: true,
        description: null
      }]
    }))).toThrow(OperatorContractDecodeError);
  });

  it('rejects malformed ports, parameters and duplicate operator identities', () => {
    expect(() => decodeOperatorCatalogItem(operator({ inputPorts: { items: [] } })))
      .toThrow(OperatorContractDecodeError);
    expect(() => decodeOperatorCatalogItem(operator({ parameters: [{ name: '' }] })))
      .toThrow(OperatorContractDecodeError);
    expect(() => decodeOperatorCatalog([operator(), operator()]))
      .toThrow(/unique operator identities/);
  });

  it('freezes stable conditional, output and image contract metadata without interpreting G3 editors', () => {
    const decoded = decodeOperatorCatalogItem(operator({
      qualityState: { execution: 'Implemented', productionReadiness: 'Experimental' },
      parameterConstraints: [{ parameter: 'Mode', reasonCode: 'MODE_REQUIRED' }],
      outputAvailabilityRules: [{ output: 'Region', reasonCode: 'REGION_MODE_ONLY' }],
      imageInputContracts: [{ inputPort: 'Image', contractVersion: 'v1', variants: [] }],
      imageInputContractPresentations: [{ inputPort: 'Image', exactVariantGroups: [] }]
    }));

    expect(decoded.qualityState).toMatchObject({ execution: 'Implemented' });
    expect(decoded.parameterConstraints[0]).toMatchObject({ reasonCode: 'MODE_REQUIRED' });
    expect(decoded.outputAvailabilityRules[0]).toMatchObject({ output: 'Region' });
    expect(decoded.imageInputContracts[0]).toMatchObject({ contractVersion: 'v1' });
    expect(decoded.imageInputContractPresentations[0]).toMatchObject({ inputPort: 'Image' });
    expect(Object.isFrozen(decoded.parameterConstraints)).toBe(true);

    expect(() => decodeOperatorCatalogItem(operator({ imageInputContracts: ['invalid'] })))
      .toThrow(OperatorContractDecodeError);
  });
});
