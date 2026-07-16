export class PreviewContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expectation: string) {
    super(`Preview response field ${path} must be ${expectation}.`);
    this.name = 'PreviewContractDecodeError';
    this.path = path;
  }
}

export interface PreviewIdentityV1 {
  readonly projectId: string;
  readonly targetNodeId: string;
  readonly debugSessionId: string;
  readonly clientRequestSequence: number;
  readonly flowRevision: number;
}

export interface PreviewIdentityInputV1 {
  readonly projectId: string;
  readonly targetNodeId: string;
  readonly debugSessionId: string;
  readonly clientRequestSequence: number;
  readonly flowRevision: number;
}

export interface PreviewStructuredObjectV1 {
  readonly [key: string]: PreviewStructuredValueV1;
}

export type PreviewStructuredValueV1 =
  | null
  | boolean
  | number
  | string
  | readonly PreviewStructuredValueV1[]
  | PreviewStructuredObjectV1;

export interface PreviewDiagnosticV1 {
  readonly code: string;
  readonly message: string;
  readonly pathHint: string | null;
}

export interface PreviewMissingResourceV1 {
  readonly resourceType: string;
  readonly resourceKey: string;
  readonly description: string;
  readonly diagnosticCode: string;
}

export interface PreviewArtifactReferenceV1 {
  readonly artifactId: string;
  readonly kind: string;
  readonly role: string;
  readonly pathHint: string | null;
  readonly contentType: string;
  readonly length: number;
  readonly sha256: string;
  readonly createdAtUtc: string | null;
  readonly expiresAtUtc: string | null;
  readonly width: number | null;
  readonly height: number | null;
  readonly channels: number | null;
}

export interface PreviewObservationOutcomeV1 {
  readonly success: boolean;
  readonly executionTimeMs: number;
  readonly errorMessage: string | null;
  readonly failedOperatorId: string | null;
  readonly failedOperatorName: string | null;
  readonly failedOperatorType: string | null;
  readonly executedOperatorCount: number | null;
}

export interface PreviewObservationV1 {
  readonly [key: string]: unknown;
  readonly schemaVersion: 'execution-observation.v1';
  readonly identity: PreviewIdentityV1;
  readonly outcome: PreviewObservationOutcomeV1 | null;
  readonly diagnostics: readonly PreviewDiagnosticV1[];
}

export interface PreviewNodeResponseV1 {
  readonly success: boolean;
  readonly projectId: string;
  readonly targetNodeId: string;
  readonly debugSessionId: string;
  readonly executionTimeMs: number;
  readonly inputImageBase64: string | null;
  readonly outputImageBase64: string | null;
  readonly outputData: PreviewStructuredObjectV1 | null;
  readonly errorMessage: string | null;
  readonly failedOperatorId: string | null;
  readonly failedOperatorName: string | null;
  readonly failedOperatorType: string | null;
  readonly diagnostics: readonly PreviewDiagnosticV1[];
  readonly missingResources: readonly PreviewMissingResourceV1[];
  readonly artifacts: readonly PreviewArtifactReferenceV1[];
  readonly observation: PreviewObservationV1;
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const emptyUuid = '00000000-0000-0000-0000-000000000000';
const artifactIdPattern = /^[A-Za-z0-9_-]{43}$/;
const sha256Pattern = /^[0-9a-f]{64}$/i;
const contentTypePattern = /^[^\s/]+\/[^\s/]+$/;
const maxStructuredDepth = 32;

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function decodeRecord(value: unknown, path: string): Readonly<Record<string, unknown>> {
  if (!isRecord(value)) {
    throw new PreviewContractDecodeError(path, 'an object');
  }
  return value;
}

function decodeBoolean(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') {
    throw new PreviewContractDecodeError(path, 'a boolean');
  }
  return value;
}

function decodeString(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim().length === 0)) {
    throw new PreviewContractDecodeError(path, allowEmpty ? 'a string' : 'a non-empty string');
  }
  return value;
}

function decodeNullableString(value: unknown, path: string, allowEmpty = true): string | null {
  if (value === null || value === undefined) return null;
  return decodeString(value, path, allowEmpty);
}

function decodeUuid(value: unknown, path: string): string {
  const decoded = decodeString(value, path).toLowerCase();
  if (!uuidPattern.test(decoded) || decoded === emptyUuid) {
    throw new PreviewContractDecodeError(path, 'a non-empty UUID');
  }
  return decoded;
}

function decodeNullableUuid(value: unknown, path: string): string | null {
  if (value === null || value === undefined) return null;
  return decodeUuid(value, path);
}

function decodeNonNegativeSafeInteger(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new PreviewContractDecodeError(path, 'a non-negative safe integer');
  }
  return value;
}

function decodeNullablePositiveInteger(value: unknown, path: string): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) {
    throw new PreviewContractDecodeError(path, 'a positive safe integer or null');
  }
  return value;
}

function decodeNullableDateTime(value: unknown, path: string): string | null {
  if (value === null || value === undefined) return null;
  const decoded = decodeString(value, path);
  if (Number.isNaN(Date.parse(decoded))) {
    throw new PreviewContractDecodeError(path, 'an ISO date-time string or null');
  }
  return decoded;
}

function decodeStructuredValue(
  value: unknown,
  path: string,
  depth = 0,
  activeObjects = new WeakSet<object>()
): PreviewStructuredValueV1 {
  if (depth > maxStructuredDepth) {
    throw new PreviewContractDecodeError(path, `structured JSON no deeper than ${maxStructuredDepth} levels`);
  }
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) {
      throw new PreviewContractDecodeError(path, 'a finite JSON number');
    }
    return value;
  }
  if (typeof value !== 'object') {
    throw new PreviewContractDecodeError(path, 'a structured JSON value');
  }
  if (activeObjects.has(value)) {
    throw new PreviewContractDecodeError(path, 'an acyclic structured JSON value');
  }

  activeObjects.add(value);
  try {
    if (Array.isArray(value)) {
      return Object.freeze(value.map((entry, index) =>
        decodeStructuredValue(entry, `${path}[${index}]`, depth + 1, activeObjects)));
    }

    const source = value as Readonly<Record<string, unknown>>;
    const decoded: Record<string, PreviewStructuredValueV1> = {};
    for (const [key, entry] of Object.entries(source)) {
      decoded[key] = decodeStructuredValue(entry, `${path}.${key}`, depth + 1, activeObjects);
    }
    return Object.freeze(decoded);
  } finally {
    activeObjects.delete(value);
  }
}

function decodeStructuredObject(value: unknown, path: string): PreviewStructuredObjectV1 {
  const decoded = decodeStructuredValue(value, path);
  if (!isRecord(decoded)) {
    throw new PreviewContractDecodeError(path, 'a structured JSON object');
  }
  return decoded as PreviewStructuredObjectV1;
}

function decodeNullableStructuredObject(value: unknown, path: string): PreviewStructuredObjectV1 | null {
  if (value === null || value === undefined) return null;
  return decodeStructuredObject(value, path);
}

function decodeIdentity(value: unknown, path: string): PreviewIdentityV1 {
  const record = decodeRecord(value, path);
  return Object.freeze({
    projectId: decodeUuid(record.projectId, `${path}.projectId`),
    targetNodeId: decodeUuid(record.targetNodeId, `${path}.targetNodeId`),
    debugSessionId: decodeUuid(record.debugSessionId, `${path}.debugSessionId`),
    clientRequestSequence: decodeNonNegativeSafeInteger(
      record.clientRequestSequence,
      `${path}.clientRequestSequence`
    ),
    flowRevision: decodeNonNegativeSafeInteger(record.flowRevision, `${path}.flowRevision`)
  });
}

export function buildPreviewIdentityV1(input: PreviewIdentityInputV1): PreviewIdentityV1 {
  return decodeIdentity(input, '$identity');
}

export function previewIdentityEquals(
  left: PreviewIdentityV1 | null | undefined,
  right: PreviewIdentityV1 | null | undefined
): boolean {
  return Boolean(left && right
    && left.projectId.toLowerCase() === right.projectId.toLowerCase()
    && left.targetNodeId.toLowerCase() === right.targetNodeId.toLowerCase()
    && left.debugSessionId.toLowerCase() === right.debugSessionId.toLowerCase()
    && left.clientRequestSequence === right.clientRequestSequence
    && left.flowRevision === right.flowRevision);
}

function assertIdentityFieldMatches(
  actual: string | number,
  expected: string | number,
  path: string
): void {
  const matches = typeof actual === 'string' && typeof expected === 'string'
    ? actual.toLowerCase() === expected.toLowerCase()
    : actual === expected;
  if (!matches) {
    throw new PreviewContractDecodeError(path, 'to match the active preview request identity');
  }
}

function assertIdentityMatches(
  actual: PreviewIdentityV1,
  expected: PreviewIdentityV1,
  path: string
): void {
  assertIdentityFieldMatches(actual.projectId, expected.projectId, `${path}.projectId`);
  assertIdentityFieldMatches(actual.targetNodeId, expected.targetNodeId, `${path}.targetNodeId`);
  assertIdentityFieldMatches(actual.debugSessionId, expected.debugSessionId, `${path}.debugSessionId`);
  assertIdentityFieldMatches(
    actual.clientRequestSequence,
    expected.clientRequestSequence,
    `${path}.clientRequestSequence`
  );
  assertIdentityFieldMatches(actual.flowRevision, expected.flowRevision, `${path}.flowRevision`);
}

function decodeDiagnostic(value: unknown, path: string): PreviewDiagnosticV1 {
  if (typeof value === 'string') {
    const message = decodeString(value, path);
    return Object.freeze({ code: 'preview', message, pathHint: null });
  }
  const record = decodeRecord(value, path);
  return Object.freeze({
    code: decodeString(record.code, `${path}.code`),
    message: decodeString(record.message, `${path}.message`),
    pathHint: decodeNullableString(record.pathHint, `${path}.pathHint`)
  });
}

function decodeDiagnostics(value: unknown, path: string, required = false): readonly PreviewDiagnosticV1[] {
  if ((value === null || value === undefined) && !required) return Object.freeze([]);
  if (!Array.isArray(value)) {
    throw new PreviewContractDecodeError(path, 'an array');
  }
  return Object.freeze(value.map((entry, index) => decodeDiagnostic(entry, `${path}[${index}]`)));
}

function decodeMissingResource(value: unknown, path: string): PreviewMissingResourceV1 {
  const record = decodeRecord(value, path);
  return Object.freeze({
    resourceType: decodeString(record.resourceType, `${path}.resourceType`),
    resourceKey: decodeString(record.resourceKey, `${path}.resourceKey`),
    description: decodeString(record.description, `${path}.description`),
    diagnosticCode: decodeString(record.diagnosticCode, `${path}.diagnosticCode`)
  });
}

function decodeMissingResources(value: unknown, path: string): readonly PreviewMissingResourceV1[] {
  if (value === null || value === undefined) return Object.freeze([]);
  if (!Array.isArray(value)) {
    throw new PreviewContractDecodeError(path, 'an array');
  }
  return Object.freeze(value.map((entry, index) => decodeMissingResource(entry, `${path}[${index}]`)));
}

function decodeArtifact(value: unknown, path: string): PreviewArtifactReferenceV1 {
  const record = decodeRecord(value, path);
  const artifactId = decodeString(record.artifactId, `${path}.artifactId`);
  if (!artifactIdPattern.test(artifactId)) {
    throw new PreviewContractDecodeError(`${path}.artifactId`, 'a 43-character opaque Base64URL token');
  }
  const contentType = decodeString(record.contentType, `${path}.contentType`);
  if (!contentTypePattern.test(contentType)) {
    throw new PreviewContractDecodeError(`${path}.contentType`, 'a MIME content type');
  }
  const sha256 = decodeString(record.sha256, `${path}.sha256`).toLowerCase();
  if (!sha256Pattern.test(sha256)) {
    throw new PreviewContractDecodeError(`${path}.sha256`, 'a 64-character hexadecimal SHA-256');
  }

  return Object.freeze({
    artifactId,
    kind: decodeString(record.kind, `${path}.kind`),
    role: decodeString(record.role, `${path}.role`),
    pathHint: decodeNullableString(record.pathHint, `${path}.pathHint`),
    contentType,
    length: decodeNonNegativeSafeInteger(record.length, `${path}.length`),
    sha256,
    createdAtUtc: decodeNullableDateTime(record.createdAtUtc, `${path}.createdAtUtc`),
    expiresAtUtc: decodeNullableDateTime(record.expiresAtUtc, `${path}.expiresAtUtc`),
    width: decodeNullablePositiveInteger(record.width, `${path}.width`),
    height: decodeNullablePositiveInteger(record.height, `${path}.height`),
    channels: decodeNullablePositiveInteger(record.channels, `${path}.channels`)
  });
}

function decodeArtifacts(value: unknown, path: string): readonly PreviewArtifactReferenceV1[] {
  if (value === null || value === undefined) return Object.freeze([]);
  if (!Array.isArray(value)) {
    throw new PreviewContractDecodeError(path, 'an array');
  }
  return Object.freeze(value.map((entry, index) => decodeArtifact(entry, `${path}[${index}]`)));
}

function decodeObservationOutcome(
  value: unknown,
  path: string
): PreviewObservationOutcomeV1 | null {
  if (value === null || value === undefined) return null;
  const record = decodeRecord(value, path);
  return Object.freeze({
    success: decodeBoolean(record.success, `${path}.success`),
    executionTimeMs: decodeNonNegativeSafeInteger(record.executionTimeMs, `${path}.executionTimeMs`),
    errorMessage: decodeNullableString(record.errorMessage, `${path}.errorMessage`),
    failedOperatorId: decodeNullableUuid(record.failedOperatorId, `${path}.failedOperatorId`),
    failedOperatorName: decodeNullableString(record.failedOperatorName, `${path}.failedOperatorName`, false),
    failedOperatorType: decodeNullableString(record.failedOperatorType, `${path}.failedOperatorType`, false),
    executedOperatorCount: record.executedOperatorCount === null || record.executedOperatorCount === undefined
      ? null
      : decodeNonNegativeSafeInteger(record.executedOperatorCount, `${path}.executedOperatorCount`)
  });
}

function decodeObservation(value: unknown, path: string): PreviewObservationV1 {
  const record = decodeRecord(value, path);
  const structured = decodeStructuredObject(record, path);
  const schemaVersion = decodeString(record.schemaVersion, `${path}.schemaVersion`);
  if (schemaVersion !== 'execution-observation.v1') {
    throw new PreviewContractDecodeError(`${path}.schemaVersion`, 'execution-observation.v1');
  }
  return Object.freeze({
    ...structured,
    schemaVersion,
    identity: decodeIdentity(record.identity, `${path}.identity`),
    outcome: decodeObservationOutcome(record.outcome, `${path}.outcome`),
    diagnostics: decodeDiagnostics(record.diagnostics, `${path}.diagnostics`, true)
  });
}

function assertOutcomeMatchesResponse(
  response: Pick<PreviewNodeResponseV1, 'success' | 'executionTimeMs'>,
  outcome: PreviewObservationOutcomeV1 | null
): void {
  if (!outcome) return;
  if (response.success !== outcome.success) {
    throw new PreviewContractDecodeError('$.observation.outcome.success', 'to match $.success');
  }
  if (response.executionTimeMs !== outcome.executionTimeMs) {
    throw new PreviewContractDecodeError(
      '$.observation.outcome.executionTimeMs',
      'to match $.executionTimeMs'
    );
  }
}

export function decodePreviewNodeResponseV1(
  payload: unknown,
  expectedIdentity?: PreviewIdentityV1
): PreviewNodeResponseV1 {
  const record = decodeRecord(payload, '$');
  const success = decodeBoolean(record.success, '$.success');
  const projectId = decodeUuid(record.projectId, '$.projectId');
  const targetNodeId = decodeUuid(record.targetNodeId, '$.targetNodeId');
  const debugSessionId = decodeUuid(record.debugSessionId, '$.debugSessionId');
  const executionTimeMs = decodeNonNegativeSafeInteger(record.executionTimeMs, '$.executionTimeMs');
  const observation = decodeObservation(record.observation, '$.observation');

  assertIdentityFieldMatches(observation.identity.projectId, projectId, '$.observation.identity.projectId');
  assertIdentityFieldMatches(
    observation.identity.targetNodeId,
    targetNodeId,
    '$.observation.identity.targetNodeId'
  );
  assertIdentityFieldMatches(
    observation.identity.debugSessionId,
    debugSessionId,
    '$.observation.identity.debugSessionId'
  );
  if (expectedIdentity) {
    assertIdentityMatches(observation.identity, expectedIdentity, '$.observation.identity');
  }

  const response = Object.freeze({
    success,
    projectId,
    targetNodeId,
    debugSessionId,
    executionTimeMs,
    inputImageBase64: decodeNullableString(record.inputImageBase64, '$.inputImageBase64', false),
    outputImageBase64: decodeNullableString(record.outputImageBase64, '$.outputImageBase64', false),
    outputData: decodeNullableStructuredObject(record.outputData, '$.outputData'),
    errorMessage: decodeNullableString(record.errorMessage, '$.errorMessage'),
    failedOperatorId: decodeNullableUuid(record.failedOperatorId, '$.failedOperatorId'),
    failedOperatorName: decodeNullableString(record.failedOperatorName, '$.failedOperatorName', false),
    failedOperatorType: decodeNullableString(record.failedOperatorType, '$.failedOperatorType', false),
    diagnostics: decodeDiagnostics(record.diagnostics, '$.diagnostics'),
    missingResources: decodeMissingResources(record.missingResources, '$.missingResources'),
    artifacts: decodeArtifacts(record.artifacts, '$.artifacts'),
    observation
  } satisfies PreviewNodeResponseV1);

  assertOutcomeMatchesResponse(response, observation.outcome);
  return response;
}
