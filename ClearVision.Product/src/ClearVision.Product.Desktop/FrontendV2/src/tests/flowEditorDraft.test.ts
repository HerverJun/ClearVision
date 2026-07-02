import { describe, expect, it } from 'vitest';
import {
  createFlowEditorDraftBaseline,
  getFlowEditorScalarParameters,
  isFlowEditorDraftBaselineStale,
  parseFlowEditorDraftValue,
  stringifyFlowEditorDraftValue
} from '@/flowEditor/flowEditorDraft';
import type {
  StudioFlowEditorParameterSnapshot,
  StudioFlowEditorSnapshot
} from '@/flowEditor/studioFlowEditorPort';

describe('FlowEditorPortPanel draft helpers', () => {
  it('marks a dirty draft stale when flow or selection revisions change', () => {
    const snapshot = createSnapshot({
      flowRevision: 2,
      selectionRevision: 3,
      selectedNodeId: 'node-a'
    });
    const baseline = createFlowEditorDraftBaseline(snapshot);

    expect(baseline).not.toBeNull();
    if (!baseline) {
      throw new Error('Expected draft baseline.');
    }

    expect(isFlowEditorDraftBaselineStale(baseline, snapshot)).toBe(false);
    expect(isFlowEditorDraftBaselineStale(baseline, {
      ...snapshot,
      flowRevision: 4
    })).toBe(true);
    expect(isFlowEditorDraftBaselineStale(baseline, {
      ...snapshot,
      selectionRevision: 4
    })).toBe(true);
    expect(isFlowEditorDraftBaselineStale(baseline, {
      ...snapshot,
      selectedNodeId: 'node-b'
    })).toBe(true);
  });

  it('treats project switches as stale so the panel can abandon the old draft explicitly', () => {
    const baseline = createFlowEditorDraftBaseline(createSnapshot({
      projectId: 'project-a',
      selectedNodeId: 'node-a'
    }));

    expect(baseline).not.toBeNull();
    if (!baseline) {
      throw new Error('Expected draft baseline.');
    }

    expect(isFlowEditorDraftBaselineStale(baseline, createSnapshot({
      projectId: 'project-b',
      selectedNodeId: 'node-b'
    }))).toBe(true);
  });

  it('blocks invalid number drafts instead of returning raw strings', () => {
    const numberParameter = createParameter({
      name: 'Threshold',
      value: 10,
      dataType: 'int'
    });

    expect(parseFlowEditorDraftValue(numberParameter, '42')).toEqual({
      ok: true,
      value: 42
    });
    expect(parseFlowEditorDraftValue(numberParameter, 'abc')).toMatchObject({
      ok: false,
      error: '请输入有效数字'
    });
  });

  it('parses boolean drafts only from checkbox-compatible values', () => {
    const booleanParameter = createParameter({
      name: 'Enabled',
      value: true,
      dataType: 'bool'
    });

    expect(parseFlowEditorDraftValue(booleanParameter, 'true')).toEqual({
      ok: true,
      value: true
    });
    expect(parseFlowEditorDraftValue(booleanParameter, 'false')).toEqual({
      ok: true,
      value: false
    });
    expect(parseFlowEditorDraftValue(booleanParameter, 'yes')).toMatchObject({
      ok: false,
      error: '请选择布尔值'
    });
  });

  it('keeps scalar parameter filtering and draft stringification deterministic', () => {
    const parameters = [
      createParameter({ name: 'Threshold', value: 10, dataType: 'int' }),
      createParameter({ name: 'Enabled', value: false, dataType: 'bool' }),
      createParameter({ name: 'Image', value: null, dataType: 'Image' })
    ];

    expect(getFlowEditorScalarParameters(parameters).map((parameter) => parameter.name))
      .toEqual(['Threshold', 'Enabled']);
    expect(stringifyFlowEditorDraftValue(false)).toBe('false');
    expect(stringifyFlowEditorDraftValue(null)).toBe('');
  });
});

function createSnapshot(overrides: Partial<StudioFlowEditorSnapshot> = {}): StudioFlowEditorSnapshot {
  return {
    projectId: 'project-a',
    flowRevision: 1,
    selectionRevision: 1,
    selectedNodeId: 'node-a',
    flow: { operators: [] },
    selectedNode: {
      id: 'node-a',
      type: 'Thresholding',
      title: 'Threshold',
      parameters: [
        createParameter({
          name: 'Threshold',
          value: 10,
          dataType: 'int'
        })
      ]
    },
    ...overrides
  };
}

function createParameter(
  overrides: Partial<StudioFlowEditorParameterSnapshot> & Pick<StudioFlowEditorParameterSnapshot, 'name'>
): StudioFlowEditorParameterSnapshot {
  return {
    displayName: overrides.name,
    value: null,
    dataType: '',
    ...overrides
  };
}
