import { decodeWorkspaceHandoffFlowV1 } from '../workspaceContracts';
import {
  WorkspaceHandoffContractError,
  type WorkspaceHandoffArtifactStatus,
  type WorkspaceHandoffArtifactV1,
  type WorkspaceHandoffBaselineV1,
  type WorkspaceHandoffBuildSummaryV1,
  type WorkspaceHandoffParameterSummaryV1
} from './handoffContracts';

type JsonRecord = Record<string, unknown>;

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const artifactPattern = /^[0-9a-f]{32}$/i;
const tokenPattern = /^[a-z0-9_.:-]{1,128}$/i;
const buildIdentityPattern = /^[a-z0-9_.:-]{1,512}$/i;
const sessionPattern = /^[a-z0-9_-]{1,80}$/i;
const hashPattern = /^(?:sha256:)?[0-9a-f]{64}$/i;
const statuses = new Set<WorkspaceHandoffArtifactStatus>([
  'available', 'consuming', 'consumed', 'expired', 'rejected'
]);

function record(value: unknown, path: string): JsonRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new WorkspaceHandoffContractError(path, 'an object');
  }
  return value as JsonRecord;
}

function exact(source: JsonRecord, allowed: readonly string[], path: string): void {
  const keys = new Set(allowed);
  const unexpected = Object.keys(source).find(key => !keys.has(key));
  if (unexpected) throw new WorkspaceHandoffContractError(`${path}.${unexpected}`, 'absent');
}

function required(source: JsonRecord, key: string, path: string): unknown {
  if (!(key in source)) throw new WorkspaceHandoffContractError(`${path}.${key}`, 'present');
  return source[key];
}

function text(value: unknown, path: string, pattern?: RegExp): string {
  if (typeof value !== 'string' || (pattern && !pattern.test(value))) {
    throw new WorkspaceHandoffContractError(path, pattern ? 'a valid public identifier' : 'a string');
  }
  return value;
}

function nullableText(value: unknown, path: string, pattern: RegExp): string | null {
  return value === null ? null : text(value, path, pattern);
}

function integer(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new WorkspaceHandoffContractError(path, 'a non-negative safe integer');
  }
  return value;
}

function bool(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') throw new WorkspaceHandoffContractError(path, 'a boolean');
  return value;
}

function timestamp(value: unknown, path: string): string {
  const decoded = text(value, path);
  if (!Number.isFinite(Date.parse(decoded))) throw new WorkspaceHandoffContractError(path, 'an ISO timestamp');
  return decoded;
}

function texts(value: unknown, path: string): readonly string[] {
  if (!Array.isArray(value)) throw new WorkspaceHandoffContractError(path, 'an array');
  return Object.freeze(value.map((item, index) => text(item, `${path}[${index}]`)));
}

function baseline(value: unknown, path: string): WorkspaceHandoffBaselineV1 {
  const source = record(value, path);
  exact(source, ['targetKind', 'projectId', 'persistenceRevision', 'canonicalFlowHash'], path);
  const targetKind = text(required(source, 'targetKind', path), `${path}.targetKind`);
  if (targetKind !== 'new' && targetKind !== 'existing') {
    throw new WorkspaceHandoffContractError(`${path}.targetKind`, 'new or existing');
  }
  const projectId = nullableText(required(source, 'projectId', path), `${path}.projectId`, guidPattern);
  const revisionValue = required(source, 'persistenceRevision', path);
  const persistenceRevision = revisionValue === null ? null : integer(revisionValue, `${path}.persistenceRevision`);
  const canonicalFlowHash = text(required(source, 'canonicalFlowHash', path), `${path}.canonicalFlowHash`);
  if (targetKind === 'new') {
    if (projectId !== null || persistenceRevision !== null || canonicalFlowHash !== '') {
      throw new WorkspaceHandoffContractError(path, 'a new target without Project identity');
    }
  } else if (projectId === null || persistenceRevision === null || !hashPattern.test(canonicalFlowHash)) {
    throw new WorkspaceHandoffContractError(path, 'an existing Project baseline');
  }
  return Object.freeze({ targetKind, projectId, persistenceRevision, canonicalFlowHash });
}

function check(value: unknown, path: string): Readonly<{ blockers: number; warnings: number }> {
  const source = record(value, path);
  for (const key of ['id', 'label', 'status', 'summary']) text(required(source, key, path), `${path}.${key}`);
  return Object.freeze({
    blockers: integer(required(source, 'blockerCount', path), `${path}.blockerCount`),
    warnings: integer(required(source, 'warningCount', path), `${path}.warningCount`)
  });
}

function parameter(value: unknown, path: string): WorkspaceHandoffParameterSummaryV1 {
  const source = record(value, path);
  return Object.freeze({
    canonicalKey: text(required(source, 'canonicalKey', path), `${path}.canonicalKey`, tokenPattern),
    operatorLabel: text(required(source, 'operatorDisplayName', path), `${path}.operatorDisplayName`) ||
      text(required(source, 'operatorType', path), `${path}.operatorType`),
    parameterLabel: text(required(source, 'parameterDisplayName', path), `${path}.parameterDisplayName`) ||
      text(required(source, 'parameterName', path), `${path}.parameterName`),
    valueSummary: text(required(source, 'valueSummary', path), `${path}.valueSummary`),
    resourceDependent: bool(required(source, 'resourceDependent', path), `${path}.resourceDependent`)
  });
}

function buildSummary(value: unknown, path: string): WorkspaceHandoffBuildSummaryV1 {
  const source = record(value, path);
  exact(source, [
    'schemaVersion', 'runId', 'buildId', 'clientOperationId', 'buildIdentity',
    'submittedBuildFingerprint', 'planId', 'planHash', 'answerSetFingerprint',
    'answerRevision', 'resourceRevision', 'projectBaseline', 'candidateFlowFingerprint',
    'operatorCount', 'connectionCount', 'operatorPipeline', 'parameterMapping',
    'missingResources', 'workflowDiff', 'validation', 'publicTimeline', 'publicWarnings',
    'metadataOnly', 'redactionPass'
  ], path);
  if (required(source, 'schemaVersion', path) !== 1 || required(source, 'metadataOnly', path) !== true ||
      required(source, 'redactionPass', path) !== true) {
    throw new WorkspaceHandoffContractError(path, 'a redacted schema v1 Build');
  }
  const validation = record(required(source, 'validation', path), `${path}.validation`);
  const structural = check(required(validation, 'structural', `${path}.validation`), `${path}.validation.structural`);
  const dryRun = check(required(validation, 'dryRun', `${path}.validation`), `${path}.validation.dryRun`);
  const manifest = check(required(validation, 'manifest', `${path}.validation`), `${path}.validation.manifest`);
  const applyGate = record(required(validation, 'applyGate', `${path}.validation`), `${path}.validation.applyGate`);
  const applyBlockers = texts(required(applyGate, 'applyBlockers', `${path}.validation.applyGate`),
    `${path}.validation.applyGate.applyBlockers`);
  const publicWarnings = texts(required(source, 'publicWarnings', path), `${path}.publicWarnings`);
  const workflow = record(required(source, 'workflowDiff', path), `${path}.workflowDiff`);
  const parameterValues = required(source, 'parameterMapping', path);
  if (!Array.isArray(parameterValues)) throw new WorkspaceHandoffContractError(`${path}.parameterMapping`, 'an array');
  const parameters = Object.freeze(parameterValues.map((item, index) => parameter(item, `${path}.parameterMapping[${index}]`)));
  return Object.freeze({
    buildId: text(required(source, 'buildId', path), `${path}.buildId`, tokenPattern),
    buildIdentity: text(
      required(source, 'buildIdentity', path), `${path}.buildIdentity`, buildIdentityPattern
    ),
    candidateFlowFingerprint: text(
      required(source, 'candidateFlowFingerprint', path), `${path}.candidateFlowFingerprint`, hashPattern
    ),
    operatorCount: integer(required(source, 'operatorCount', path), `${path}.operatorCount`),
    connectionCount: integer(required(source, 'connectionCount', path), `${path}.connectionCount`),
    handoffEligible: bool(required(validation, 'handoffEligible', `${path}.validation`),
      `${path}.validation.handoffEligible`),
    applyGateReady: bool(required(applyGate, 'canvasApplyReady', `${path}.validation.applyGate`),
      `${path}.validation.applyGate.canvasApplyReady`) &&
      bool(required(applyGate, 'runtimeDraftReady', `${path}.validation.applyGate`),
        `${path}.validation.applyGate.runtimeDraftReady`) &&
      !bool(required(applyGate, 'blocked', `${path}.validation.applyGate`),
        `${path}.validation.applyGate.blocked`),
    blockerCount: structural.blockers + dryRun.blockers + manifest.blockers + applyBlockers.length,
    warningCount: structural.warnings + dryRun.warnings + manifest.warnings + publicWarnings.length,
    diff: Object.freeze({
      addedNodes: texts(required(workflow, 'addedNodes', `${path}.workflowDiff`), `${path}.workflowDiff.addedNodes`),
      modifiedNodes: texts(required(workflow, 'modifiedNodes', `${path}.workflowDiff`), `${path}.workflowDiff.modifiedNodes`),
      removedNodes: texts(required(workflow, 'removedNodes', `${path}.workflowDiff`), `${path}.workflowDiff.removedNodes`),
      addedOrChangedParameters: texts(required(workflow, 'addedOrChangedParameters', `${path}.workflowDiff`),
        `${path}.workflowDiff.addedOrChangedParameters`),
      missingResources: texts(required(workflow, 'missingResources', `${path}.workflowDiff`),
        `${path}.workflowDiff.missingResources`)
    }),
    parameters
  });
}

export function decodeWorkspaceHandoffArtifactV1(value: unknown): WorkspaceHandoffArtifactV1 {
  const source = record(value, '$');
  exact(source, [
    'schemaVersion', 'artifactId', 'clientOperationId', 'sessionId', 'sessionRevision',
    'planRunId', 'planId', 'planHash', 'buildRunId', 'buildClientOperationId',
    'buildIdentity', 'targetKind', 'projectBaseline', 'candidateFlow',
    'candidateFlowFingerprint', 'build', 'createdAtUtc', 'expiresAtUtc', 'status',
    'consumeClientOperationId', 'consumeReceipt'
  ], '$');
  if (required(source, 'schemaVersion', '$') !== 1) {
    throw new WorkspaceHandoffContractError('$.schemaVersion', '1');
  }
  const targetKind = text(required(source, 'targetKind', '$'), '$.targetKind');
  if (targetKind !== 'new' && targetKind !== 'existing') {
    throw new WorkspaceHandoffContractError('$.targetKind', 'new or existing');
  }
  const status = text(required(source, 'status', '$'), '$.status') as WorkspaceHandoffArtifactStatus;
  if (!statuses.has(status)) throw new WorkspaceHandoffContractError('$.status', 'a supported artifact status');
  const projectBaseline = baseline(required(source, 'projectBaseline', '$'), '$.projectBaseline');
  if (projectBaseline.targetKind !== targetKind) {
    throw new WorkspaceHandoffContractError('$.projectBaseline.targetKind', targetKind);
  }
  const build = buildSummary(required(source, 'build', '$'), '$.build');
  const fingerprint = text(required(source, 'candidateFlowFingerprint', '$'), '$.candidateFlowFingerprint', hashPattern);
  if (build.candidateFlowFingerprint !== fingerprint || !build.handoffEligible || !build.applyGateReady) {
    throw new WorkspaceHandoffContractError('$.build', 'the eligible canonical candidate identity');
  }
  const consumeIdentity = nullableText(
    required(source, 'consumeClientOperationId', '$'), '$.consumeClientOperationId', guidPattern
  );
  const receipt = required(source, 'consumeReceipt', '$');
  if (receipt !== null) {
    const decodedReceipt = record(receipt, '$.consumeReceipt');
    if (bool(required(decodedReceipt, 'projectSaved', '$.consumeReceipt'), '$.consumeReceipt.projectSaved')) {
      throw new WorkspaceHandoffContractError('$.consumeReceipt.projectSaved', 'false');
    }
  }
  const artifact = Object.freeze({
    schemaVersion: 1 as const,
    artifactId: text(required(source, 'artifactId', '$'), '$.artifactId', artifactPattern),
    clientOperationId: text(required(source, 'clientOperationId', '$'), '$.clientOperationId', guidPattern),
    sessionId: text(required(source, 'sessionId', '$'), '$.sessionId', sessionPattern),
    sessionRevision: integer(required(source, 'sessionRevision', '$'), '$.sessionRevision'),
    planRunId: text(required(source, 'planRunId', '$'), '$.planRunId', tokenPattern),
    planId: text(required(source, 'planId', '$'), '$.planId', tokenPattern),
    planHash: text(required(source, 'planHash', '$'), '$.planHash', hashPattern),
    buildRunId: text(required(source, 'buildRunId', '$'), '$.buildRunId', tokenPattern),
    buildClientOperationId: text(
      required(source, 'buildClientOperationId', '$'), '$.buildClientOperationId', guidPattern
    ),
    buildIdentity: text(required(source, 'buildIdentity', '$'), '$.buildIdentity', buildIdentityPattern),
    targetKind,
    projectBaseline,
    candidateFlow: decodeWorkspaceHandoffFlowV1(required(source, 'candidateFlow', '$')),
    candidateFlowFingerprint: fingerprint,
    build,
    createdAtUtc: timestamp(required(source, 'createdAtUtc', '$'), '$.createdAtUtc'),
    expiresAtUtc: timestamp(required(source, 'expiresAtUtc', '$'), '$.expiresAtUtc'),
    status,
    consumeClientOperationId: consumeIdentity
  });
  if (artifact.buildIdentity !== artifact.build.buildIdentity) {
    throw new WorkspaceHandoffContractError('$.buildIdentity', 'the public Build identity');
  }
  return artifact;
}
