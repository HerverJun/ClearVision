export class ProjectContractDecodeError extends Error {
  readonly path: string;
  readonly expectation: string;

  constructor(path: string, expectation: string) {
    super('工程服务返回的数据格式不符合要求，请刷新后重试。');
    this.name = 'ProjectContractDecodeError';
    this.path = path;
    this.expectation = expectation;
  }
}

export interface ProjectSummary {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly version: string;
  readonly persistenceRevision: number;
  readonly createdAt: string;
  readonly modifiedAt: string | null;
  readonly lastOpenedAt: string | null;
}

export interface ProjectDecisionSummary {
  readonly configured: boolean;
  readonly missingDecisionPolicy: string;
}

export interface ProjectFlowSummary {
  readonly id: string;
  readonly name: string;
  readonly operatorCount: number;
  readonly connectionCount: number;
  readonly decision: ProjectDecisionSummary | null;
}

export interface ProjectAssetSummary {
  readonly schemaVersion: number;
  readonly calibrationAssetCount: number;
  readonly spatialAssetCount: number;
}

export interface ProjectDetails extends ProjectSummary {
  readonly flow: ProjectFlowSummary | null;
  readonly assets: ProjectAssetSummary;
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const emptyUuid = '00000000-0000-0000-0000-000000000000';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function decodeRecord(value: unknown, path: string): Record<string, unknown> {
  if (!isRecord(value)) {
    throw new ProjectContractDecodeError(path, 'an object');
  }
  return value;
}

function decodeString(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim().length === 0)) {
    throw new ProjectContractDecodeError(path, allowEmpty ? 'a string' : 'a non-empty string');
  }
  return value;
}

function decodeNullableString(value: unknown, path: string): string | null {
  if (value === null) return null;
  return decodeString(value, path, true);
}

function decodeUuid(value: unknown, path: string): string {
  const decoded = decodeString(value, path);
  if (!uuidPattern.test(decoded) || decoded.toLowerCase() === emptyUuid) {
    throw new ProjectContractDecodeError(path, 'a non-empty UUID');
  }
  return decoded;
}

function decodeNonNegativeInteger(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 0) {
    throw new ProjectContractDecodeError(path, 'a non-negative integer');
  }
  return value;
}

function decodePositiveInteger(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value <= 0) {
    throw new ProjectContractDecodeError(path, 'a positive integer');
  }
  return value;
}

function decodeDateTime(value: unknown, path: string): string {
  const decoded = decodeString(value, path);
  if (Number.isNaN(Date.parse(decoded))) {
    throw new ProjectContractDecodeError(path, 'an ISO date-time string');
  }
  return decoded;
}

function decodeNullableDateTime(value: unknown, path: string): string | null {
  if (value === null) return null;
  return decodeDateTime(value, path);
}

function decodeRecordArray(value: unknown, path: string): readonly Record<string, unknown>[] {
  if (!Array.isArray(value)) {
    throw new ProjectContractDecodeError(path, 'an array');
  }
  return value.map((item, index) => decodeRecord(item, `${path}[${index}]`));
}

function decodeSummary(value: unknown, path: string): ProjectSummary {
  const record = decodeRecord(value, path);
  return Object.freeze({
    id: decodeUuid(record.id, `${path}.id`),
    name: decodeString(record.name, `${path}.name`),
    description: decodeNullableString(record.description, `${path}.description`),
    version: decodeString(record.version, `${path}.version`),
    persistenceRevision: decodeNonNegativeInteger(
      record.persistenceRevision,
      `${path}.persistenceRevision`
    ),
    createdAt: decodeDateTime(record.createdAt, `${path}.createdAt`),
    modifiedAt: decodeNullableDateTime(record.modifiedAt, `${path}.modifiedAt`),
    lastOpenedAt: decodeNullableDateTime(record.lastOpenedAt, `${path}.lastOpenedAt`)
  });
}

function decodeDecisionSummary(value: unknown, path: string): ProjectDecisionSummary | null {
  if (value === null) return null;
  const record = decodeRecord(value, path);
  const finalBinding = record.finalDecisionBinding;
  if (finalBinding !== null) {
    decodeRecord(finalBinding, `${path}.finalDecisionBinding`);
  }
  return Object.freeze({
    configured: finalBinding !== null,
    missingDecisionPolicy: decodeString(
      record.missingDecisionPolicy,
      `${path}.missingDecisionPolicy`
    )
  });
}

function decodeFlowSummary(value: unknown, path: string): ProjectFlowSummary | null {
  if (value === null) return null;
  const record = decodeRecord(value, path);
  const operators = decodeRecordArray(record.operators, `${path}.operators`);
  const connections = decodeRecordArray(record.connections, `${path}.connections`);
  return Object.freeze({
    id: decodeUuid(record.id, `${path}.id`),
    name: decodeString(record.name, `${path}.name`),
    operatorCount: operators.length,
    connectionCount: connections.length,
    decision: decodeDecisionSummary(record.decisionConfiguration, `${path}.decisionConfiguration`)
  });
}

function decodeAssetSummary(value: unknown, path: string): ProjectAssetSummary {
  const record = decodeRecord(value, path);
  const calibrationAssets = decodeRecordArray(record.calibrationAssets, `${path}.calibrationAssets`);
  const spatialAssets = decodeRecordArray(record.spatialAssets, `${path}.spatialAssets`);
  return Object.freeze({
    schemaVersion: decodePositiveInteger(record.schemaVersion, `${path}.schemaVersion`),
    calibrationAssetCount: calibrationAssets.length,
    spatialAssetCount: spatialAssets.length
  });
}

export function isProjectId(value: string): boolean {
  return uuidPattern.test(value) && value.toLowerCase() !== emptyUuid;
}

export function decodeProjectSummaryList(payload: unknown): readonly ProjectSummary[] {
  if (!Array.isArray(payload)) {
    throw new ProjectContractDecodeError('$', 'an array');
  }
  return Object.freeze(payload.map((item, index) => decodeSummary(item, `$[${index}]`)));
}

export function decodeProjectDetails(payload: unknown): ProjectDetails {
  const record = decodeRecord(payload, '$');
  const summary = decodeSummary(record, '$');
  return Object.freeze({
    ...summary,
    flow: decodeFlowSummary(record.flow, '$.flow'),
    assets: decodeAssetSummary(record.assets, '$.assets')
  });
}
