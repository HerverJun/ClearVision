import type {
  StudioFlowEditorParameterSnapshot,
  StudioFlowEditorSnapshot
} from '@/flowEditor/studioFlowEditorPort';

export interface FlowEditorDraftBaseline {
  readonly projectId: string;
  readonly nodeId: string;
  readonly flowRevision: number;
  readonly selectionRevision: number;
}

export interface FlowEditorDraftParseResult {
  readonly ok: boolean;
  readonly value?: unknown;
  readonly error?: string;
}

export function createFlowEditorDraftBaseline(
  snapshot: StudioFlowEditorSnapshot | null
): FlowEditorDraftBaseline | null {
  if (!snapshot?.projectId || !snapshot.selectedNodeId) {
    return null;
  }

  return {
    projectId: snapshot.projectId,
    nodeId: snapshot.selectedNodeId,
    flowRevision: snapshot.flowRevision,
    selectionRevision: snapshot.selectionRevision
  };
}

export function isFlowEditorDraftBaselineStale(
  baseline: FlowEditorDraftBaseline,
  snapshot: StudioFlowEditorSnapshot | null
): boolean {
  return !snapshot ||
    snapshot.projectId !== baseline.projectId ||
    snapshot.selectedNodeId !== baseline.nodeId ||
    snapshot.flowRevision !== baseline.flowRevision ||
    snapshot.selectionRevision !== baseline.selectionRevision;
}

export function getFlowEditorScalarParameters(
  parameters: readonly StudioFlowEditorParameterSnapshot[]
): StudioFlowEditorParameterSnapshot[] {
  return parameters.filter(isFlowEditorScalarParameter);
}

export function isFlowEditorScalarParameter(parameter: StudioFlowEditorParameterSnapshot): boolean {
  const dataType = parameter.dataType.toLowerCase();
  return !dataType ||
    dataType.includes('string') ||
    dataType.includes('int') ||
    dataType.includes('float') ||
    dataType.includes('double') ||
    dataType.includes('number') ||
    dataType.includes('bool');
}

export function isFlowEditorBooleanParameter(parameter: StudioFlowEditorParameterSnapshot): boolean {
  return parameter.dataType.toLowerCase().includes('bool');
}

export function isFlowEditorNumericParameter(parameter: StudioFlowEditorParameterSnapshot): boolean {
  const dataType = parameter.dataType.toLowerCase();
  return dataType.includes('int') ||
    dataType.includes('float') ||
    dataType.includes('double') ||
    dataType.includes('number');
}

export function getFlowEditorInputType(parameter: StudioFlowEditorParameterSnapshot): string {
  return isFlowEditorNumericParameter(parameter) ? 'number' : 'text';
}

export function parseFlowEditorDraftValue(
  parameter: StudioFlowEditorParameterSnapshot,
  rawValue: string
): FlowEditorDraftParseResult {
  if (isFlowEditorBooleanParameter(parameter)) {
    const normalized = rawValue.toLowerCase();
    if (normalized === 'true') {
      return { ok: true, value: true };
    }
    if (normalized === 'false') {
      return { ok: true, value: false };
    }

    return {
      ok: false,
      error: '请选择布尔值'
    };
  }

  if (isFlowEditorNumericParameter(parameter)) {
    const trimmed = rawValue.trim();
    const numberValue = Number(trimmed);
    if (!trimmed || !Number.isFinite(numberValue)) {
      return {
        ok: false,
        error: '请输入有效数字'
      };
    }

    return {
      ok: true,
      value: numberValue
    };
  }

  return {
    ok: true,
    value: rawValue
  };
}

export function stringifyFlowEditorDraftValue(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (
    typeof value === 'string' ||
    typeof value === 'number' ||
    typeof value === 'boolean' ||
    typeof value === 'bigint'
  ) {
    return String(value);
  }

  const serialized = JSON.stringify(value);
  return typeof serialized === 'string' ? serialized : '';
}
