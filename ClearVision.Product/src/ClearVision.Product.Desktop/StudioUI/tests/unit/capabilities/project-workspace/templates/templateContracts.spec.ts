import { describe, expect, it } from 'vitest';
import type { OperatorCatalogItem } from '@/capabilities/operators-read/operatorContracts';
import {
  convertTemplateFlow,
  decodeFlowTemplate,
  templateMatches,
  type FlowTemplateV1
} from '@/capabilities/project-workspace/templates';

function catalogItem(
  operatorType: string,
  inputPorts: readonly Record<string, unknown>[] = [],
  outputPorts: readonly Record<string, unknown>[] = [],
  parameters: readonly Record<string, unknown>[] = []
): OperatorCatalogItem {
  return {
    operatorType,
    displayName: operatorType,
    description: '',
    categoryId: 'DataProcessing',
    category: '数据处理',
    lifecycle: 'Stable',
    lifecycleNote: null,
    defaultHidden: false,
    iconName: null,
    keywords: [],
    tags: [],
    version: '1.0.0',
    qualityState: null,
    inputPorts: inputPorts as never,
    outputPorts: outputPorts as never,
    parameters: parameters as never,
    parameterConstraints: [],
    outputAvailabilityRules: [],
    imageInputContracts: [],
    imageInputContractPresentations: []
  };
}

function template(flow: unknown): FlowTemplateV1 {
  return decodeFlowTemplate({
    id: '11111111-1111-4111-8111-111111111111',
    name: '线序模板',
    description: '模板说明',
    industry: '电子制造',
    tags: ['线序', '测试'],
    flowJson: typeof flow === 'string' ? flow : JSON.stringify(flow),
    templateVersion: '1.0.0',
    scenarioKey: null,
    createdAt: null
  });
}

describe('template contracts', () => {
  it('decodes object flowData and normalizes template metadata', () => {
    const decoded = decodeFlowTemplate({
      id: 'template-1',
      name: '  模板  ',
      flowData: { operators: [{ id: 'node-1', type: 'Source' }], connections: [] },
      tags: ['  A ', '', 'B']
    });

    expect(decoded.name).toBe('  模板  ');
    expect(JSON.parse(decoded.flowJson)).toEqual({
      operators: [{ id: 'node-1', type: 'Source' }],
      connections: []
    });
    expect(decoded.tags).toEqual(['A', 'B']);
  });

  it('converts legacy temp ids through current operator metadata', () => {
    const source = catalogItem(
      'Source',
      [],
      [{ name: 'out', displayName: '输出', dataType: 'Image', isRequired: false, description: null }]
    );
    const threshold = catalogItem(
      'Threshold',
      [{ name: 'in', displayName: '输入', dataType: 'Image', isRequired: true, description: null }],
      [{ name: 'out', displayName: '输出', dataType: 'Image', isRequired: false, description: null }],
      [{
        name: 'value',
        displayName: '阈值',
        description: null,
        dataType: 'Integer',
        defaultValue: 0,
        minValue: 0,
        maxValue: 255,
        isRequired: true,
        options: null
      }]
    );
    const converted = convertTemplateFlow(template({
      operators: [
        { tempId: 'source', operatorType: 'Source' },
        { tempId: 'threshold', operatorType: 'Threshold', parameters: { value: '12' } }
      ],
      connections: [{
        sourceTempId: 'source',
        sourcePortName: 'out',
        targetTempId: 'threshold',
        targetPortName: 'in'
      }]
    }), [source, threshold]);

    expect(converted.diagnostics).toEqual([]);
    expect(converted.operatorCount).toBe(2);
    expect(converted.connectionCount).toBe(1);
    const flow = converted.flow as { operators: readonly unknown[]; connections: readonly unknown[] };
    expect(flow.operators).toHaveLength(2);
    expect(flow.connections).toHaveLength(1);
    expect((flow.operators[1] as { parameters: readonly { value?: unknown }[] }).parameters[0]?.value)
      .toBe(12);
  });

  it('blocks legacy conversion when an operator or connection cannot be resolved', () => {
    const converted = convertTemplateFlow(template({
      operators: [{ tempId: 'source', operatorType: 'MissingOperator' }],
      connections: [{
        sourceTempId: 'source',
        sourcePortName: 'out',
        targetTempId: 'missing',
        targetPortName: 'in'
      }]
    }), []);

    expect(converted.flow).toBeNull();
    expect(converted.diagnostics.map(item => item.code)).toEqual([
      'template-operator-unknown',
      'template-connection-unresolved'
    ]);
  });

  it('keeps canonical flow while diagnosing catalog drift', () => {
    const converted = convertTemplateFlow(template({
      id: 'flow-1',
      name: 'canonical',
      operators: [{ id: 'node-1', type: 'RemovedOperator' }],
      connections: []
    }), []);

    expect((converted.flow as { operators: readonly unknown[] }).operators).toHaveLength(1);
    expect(converted.diagnostics[0]?.code).toBe('template-operator-not-in-catalog');
  });

  it('matches search and industry filters using normalized text', () => {
    const value = template({ operators: [{ id: 'node-1', type: 'Source' }], connections: [] });

    expect(templateMatches(value, '线序', '')).toBe(true);
    expect(templateMatches(value, '测试', '电子制造')).toBe(true);
    expect(templateMatches(value, '测试', '汽车')).toBe(false);
  });
});
