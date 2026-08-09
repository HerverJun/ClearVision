import type { WorkspaceGlobalVariableValueType, WorkspaceVariableConversionMode } from '../workspaceContracts';

export type GlobalVariableBindableDataType = WorkspaceGlobalVariableValueType | 'Unknown';

function compact(value: unknown): string {
  return typeof value === 'string' || typeof value === 'number'
    ? String(value).trim().toLocaleLowerCase().replace(/[\s_-]/g, '')
    : '';
}

/**
 * Normalizes the several data-type spellings used by operator metadata into
 * the four scalar types that a GlobalVariable binding can represent.
 */
export function normalizeGlobalVariableDataType(value: unknown): GlobalVariableBindableDataType {
  switch (compact(value)) {
    case '4':
    case 'string':
    case 'text':
      return 'String';
    case '3':
    case 'bool':
    case 'boolean':
      return 'Boolean';
    case '1':
    case 'int':
    case 'integer':
    case 'int32':
    case 'uint32':
    case 'int64':
    case 'uint64':
    case 'long':
      return 'Int64';
    case '2':
    case 'float':
    case 'single':
    case 'double':
    case 'number':
    case 'decimal':
      return 'Double';
    default:
      return 'Unknown';
  }
}

export function isGlobalVariableDataTypeCompatible(
  variableType: WorkspaceGlobalVariableValueType,
  candidateType: unknown,
  conversionMode: WorkspaceVariableConversionMode = 'Exact'
): boolean {
  const normalizedCandidate = normalizeGlobalVariableDataType(candidateType);
  if (normalizedCandidate === 'Unknown') return false;
  if (variableType === normalizedCandidate) return true;
  return (variableType === 'Int64' || variableType === 'Double') &&
    (normalizedCandidate === 'Int64' || normalizedCandidate === 'Double') &&
    conversionMode !== 'Exact';
}

export function globalVariableDataTypeLabel(value: unknown): string {
  switch (normalizeGlobalVariableDataType(value)) {
    case 'String': return '文本';
    case 'Int64': return '整数';
    case 'Double': return '数值';
    case 'Boolean': return '布尔值';
    default: return '不支持的类型';
  }
}
