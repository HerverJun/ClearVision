import { f03PreviewBitmapFixture } from './f03-preview-bitmap-fixture';

function fixtureUuid(seed: number): string {
  return `aaaaaaaa-aaaa-4aaa-8aaa-${seed.toString(16).padStart(12, '0')}`;
}

const identities = Object.freeze({
  projectId: '11111111-1111-4111-8111-111111111111',
  flowId: '33333333-3333-4333-8333-333333333333',
  sourceNodeId: fixtureUuid(50_001),
  roiNodeId: fixtureUuid(50_002),
  judgeNodeId: fixtureUuid(50_003),
  sourceOutputId: fixtureUuid(50_106),
  roiInputId: fixtureUuid(50_102),
  roiOutputId: fixtureUuid(50_103),
  judgeInputId: fixtureUuid(50_104),
  judgeOutputId: fixtureUuid(50_101),
  judgeToleranceParameterId: fixtureUuid(53_001),
  variableId: fixtureUuid(61_001),
  sourceBindingId: fixtureUuid(61_002),
  targetBindingId: fixtureUuid(61_003),
  resultId: fixtureUuid(90_001),
  sessionId: fixtureUuid(90_002),
  runId: fixtureUuid(90_003)
});

function parameter(
  seed: number,
  name: string,
  dataType: string,
  value: unknown,
  overrides: Readonly<Record<string, unknown>> = {}
) {
  return {
    id: fixtureUuid(seed),
    name,
    displayName: name,
    description: `${name} Option D G0 deterministic fixture parameter`,
    dataType,
    value,
    defaultValue: value,
    minValue: null,
    maxValue: null,
    isRequired: false,
    options: null,
    ...overrides
  };
}

const flow = Object.freeze({
  id: identities.flowId,
  name: 'Option D G0 瓶盖密封确定性流程',
  operators: Object.freeze([{
    id: identities.sourceNodeId,
    name: '一号工位相机采集',
    type: 0,
    metadata: null,
    x: 60,
    y: 100,
    inputPorts: [],
    outputPorts: [{
      id: identities.sourceOutputId,
      name: 'Image',
      direction: 1,
      dataType: 0,
      isRequired: false
    }],
    parameters: [
      parameter(51_001, 'SourceType', 'string', 'Camera'),
      parameter(51_002, 'CameraBindingId', 'CameraBinding', 'camera-a'),
      parameter(51_003, 'TriggerMode', 'string', 'Software'),
      parameter(51_004, 'ExposureTime', 'double', 1000),
      parameter(51_005, 'Gain', 'double', 2)
    ],
    isEnabled: true,
    executionStatus: 0,
    executionTimeMs: null,
    errorMessage: null
  }, {
    id: identities.roiNodeId,
    name: '瓶盖密封区域 ROI',
    type: 'RoiManager',
    metadata: null,
    x: 280,
    y: 100,
    inputPorts: [{
      id: identities.roiInputId,
      name: 'Image',
      direction: 0,
      dataType: 0,
      isRequired: true
    }],
    outputPorts: [{
      id: identities.roiOutputId,
      name: 'Roi',
      direction: 1,
      dataType: 13,
      isRequired: false
    }],
    parameters: [
      parameter(52_001, 'Shape', 'enum', 'Rectangle', {
        options: [{ label: '矩形', value: 'Rectangle' }]
      }),
      parameter(52_002, 'X', 'double', 10, { minValue: 0, maxValue: 100, isRequired: true }),
      parameter(52_003, 'Y', 'double', 10, { minValue: 0, maxValue: 100, isRequired: true }),
      parameter(52_004, 'Width', 'double', 30, { minValue: 0, maxValue: 100, isRequired: true }),
      parameter(52_005, 'Height', 'double', 20, { minValue: 0, maxValue: 100, isRequired: true })
    ],
    isEnabled: true,
    executionStatus: 0,
    executionTimeMs: null,
    errorMessage: null
  }, {
    id: identities.judgeNodeId,
    name: '密封宽度判定',
    type: 8,
    metadata: null,
    x: 480,
    y: 100,
    inputPorts: [{
      id: identities.judgeInputId,
      name: 'Region',
      direction: 0,
      dataType: 13,
      isRequired: true
    }],
    outputPorts: [{
      id: identities.judgeOutputId,
      name: 'Width',
      direction: 1,
      dataType: 2,
      isRequired: false
    }],
    parameters: [parameter(53_001, 'Tolerance', 'double', 12.5, {
      minValue: 0,
      maxValue: 100
    })],
    isEnabled: true,
    executionStatus: 0,
    executionTimeMs: null,
    errorMessage: null
  }]),
  connections: Object.freeze([{
    id: fixtureUuid(54_001),
    sourceOperatorId: identities.sourceNodeId,
    sourcePortId: identities.sourceOutputId,
    targetOperatorId: identities.roiNodeId,
    targetPortId: identities.roiInputId
  }, {
    id: fixtureUuid(54_002),
    sourceOperatorId: identities.roiNodeId,
    sourcePortId: identities.roiOutputId,
    targetOperatorId: identities.judgeNodeId,
    targetPortId: identities.judgeInputId
  }]),
  decisionConfiguration: Object.freeze({
    finalDecisionBinding: Object.freeze({
      sourceOperatorId: identities.judgeNodeId,
      sourceOutputPortId: identities.judgeOutputId,
      sourceOutputName: 'Width',
      dataType: 'Float',
      rule: 'NumericComparison',
      trueMeansOk: true,
      okValue: null,
      ngValue: null,
      comparator: 'LessThanOrEqual',
      threshold: 12.5
    }),
    missingDecisionPolicy: 'Undetermined'
  })
});

const globalVariables = Object.freeze({
  schemaVersion: '1.0',
  variables: Object.freeze([{
    id: identities.variableId,
    name: 'SealWidth',
    displayName: '密封宽度',
    description: '正式判定使用的密封宽度变量。',
    valueType: 'Double',
    initialValue: 12.5,
    min: 0,
    max: 100,
    manualWriteAllowed: true,
    includeInResultMetadata: true,
    order: 0
  }]),
  sourceBindings: Object.freeze([{
    id: identities.sourceBindingId,
    variableId: identities.variableId,
    operatorId: identities.judgeNodeId,
    outputPortId: identities.judgeOutputId,
    operatorName: '密封宽度判定',
    outputPortName: 'Width',
    resultPathVersion: null,
    resultPath: null,
    conversionMode: 'Exact',
    expression: null
  }]),
  targetBindings: Object.freeze([{
    id: identities.targetBindingId,
    variableId: identities.variableId,
    operatorId: identities.judgeNodeId,
    parameterId: identities.judgeToleranceParameterId,
    operatorName: '密封宽度判定',
    parameterName: 'Tolerance',
    conversionMode: 'Exact',
    expression: null
  }])
});

const project = Object.freeze({
  id: identities.projectId,
  name: '瓶盖检测 A',
  description: 'Option D G0 deterministic fixture',
  version: '1.0.0',
  persistenceRevision: 8,
  flow,
  globalSettings: Object.freeze({}),
  globalVariables,
  assets: Object.freeze({
    schemaVersion: 1,
    calibrationAssets: Object.freeze([]),
    spatialAssets: Object.freeze([])
  }),
  createdAt: '2026-07-22T01:00:00Z',
  modifiedAt: '2026-07-22T01:02:01Z',
  lastOpenedAt: null
});

const resultSummary = Object.freeze({
  id: identities.resultId,
  resultId: identities.resultId,
  projectId: identities.projectId,
  status: 'Completed',
  executionOutcome: 'Succeeded',
  decisionOutcome: 'Ok',
  decisionSource: 'FinalDecision',
  reasonCode: 'OPTION_D_G0_OK',
  hasJudgmentSignal: true,
  defectCount: 0,
  processingTimeMs: 18,
  inspectionTime: '2026-07-22T01:02:03Z',
  startedAt: '2026-07-22T01:02:02Z',
  completedAt: '2026-07-22T01:02:03Z',
  confidenceScore: 0.98,
  flowVersionHash: 'fixture-persisted-flow-hash',
  calibrationBundleId: null,
  runId: identities.runId,
  diagnosticCode: 'OPTION_D_G0_OK',
  diagnosticMessage: 'Option D G0 确定性正式运行完成。',
  errorMessage: null
});

const evidenceManifest = Object.freeze({
  status: 'available',
  message: '本次结果证据完整，可导出。',
  manifest: Object.freeze({
    schemaVersion: 1,
    manifestId: 'manifest-option-d-g0',
    projectId: identities.projectId,
    inspectionResultId: identities.resultId,
    status: 'available',
    outcome: 'OK',
    createdAtUtc: '2026-07-22T01:02:03Z',
    flowVersionHash: 'fixture-persisted-flow-hash',
    calibrationBundleId: null,
    sessionId: identities.sessionId,
    runId: identities.runId,
    retentionClass: 'standard',
    retentionExpiresAtUtc: null,
    totalBytes: f03PreviewBitmapFixture.byteLength,
    checksum: f03PreviewBitmapFixture.sha256,
    redaction: Object.freeze({ applied: true }),
    items: Object.freeze([{
      id: 'output-image',
      role: 'output-image',
      contentType: f03PreviewBitmapFixture.contentType,
      relativePath: 'output.png',
      sizeBytes: f03PreviewBitmapFixture.byteLength,
      sha256: f03PreviewBitmapFixture.sha256,
      available: true,
      missingReason: null
    }])
  })
});

export const optionDG0DeterministicFixture = Object.freeze({
  schemaVersion: 'option-d-g0-deterministic.v1',
  approvedBy: 'HerverJun',
  approvedAt: '2026-08-23',
  subgraphDisposition: 'NOT_APPLICABLE',
  identities,
  coverage: Object.freeze({
    ordinaryNodeId: identities.sourceNodeId,
    previewNodeId: identities.roiNodeId,
    roiNodeId: identities.roiNodeId,
    globalVariableId: identities.variableId,
    formalDecisionNodeId: identities.judgeNodeId,
    formalResultId: identities.resultId,
    evidenceManifestId: evidenceManifest.manifest.manifestId
  }),
  project,
  resultSummary,
  evidenceManifest,
  authority: Object.freeze({
    preview: 'DEBUG_PROJECTION',
    formalRun: 'AUTHENTICATED_HTTP',
    formalResult: 'RESULTS_READ',
    projectSave: 'PROJECT_SAVE_COORDINATOR'
  })
});

export function createOptionDG0Project(
  projectId = identities.projectId
): Readonly<Record<string, unknown>> {
  return {
    ...structuredClone(project),
    id: projectId,
    name: projectId === identities.projectId ? project.name : `Option D G0 ${projectId}`
  };
}

export function createOptionDG0PreviewResponse(
  request: Readonly<Record<string, unknown>>,
  call: number,
  artifact: Readonly<Record<string, unknown>>
): Readonly<Record<string, unknown>> {
  return {
    success: true,
    projectId: String(request.projectId),
    targetNodeId: String(request.targetNodeId),
    debugSessionId: String(request.debugSessionId),
    executionTimeMs: 5 + call,
    inputImageBase64: null,
    outputImageBase64: null,
    outputData: { width: 12.8, tolerance: 12.5, outcome: 'OK' },
    errorMessage: null,
    failedOperatorId: null,
    failedOperatorName: null,
    failedOperatorType: null,
    diagnostics: [],
    missingResources: [],
    artifacts: [artifact],
    observation: {
      schemaVersion: 'execution-observation.v1',
      identity: {
        projectId: String(request.projectId),
        targetNodeId: String(request.targetNodeId),
        debugSessionId: String(request.debugSessionId),
        clientRequestSequence: Number(request.clientRequestSequence),
        flowRevision: Number(request.flowRevision)
      },
      outcome: {
        success: true,
        executionTimeMs: 5 + call,
        errorMessage: null,
        failedOperatorId: null,
        failedOperatorName: null,
        failedOperatorType: null,
        executedOperatorCount: 1
      },
      diagnostics: []
    }
  };
}

export function createOptionDG0AdmissionResponse(
  request: Readonly<Record<string, unknown>>
): Readonly<Record<string, unknown>> {
  return {
    allowed: true,
    code: null,
    message: 'Option D G0 fixture admission allowed',
    projectId: request.projectId,
    clientSnapshotId: request.clientSnapshotId,
    projectPersistenceRevision: request.expectedPersistenceRevision,
    canonicalFlowHash: 'fixture-persisted-flow-hash',
    decisionConfigurationHash: 'fixture-decision-hash',
    violations: []
  };
}

export function createOptionDG0FormalRunResult(
  request: Readonly<Record<string, unknown>>
): Readonly<Record<string, unknown>> {
  return {
    id: identities.resultId,
    projectId: request.projectId,
    status: 'Completed',
    executionOutcome: 'Succeeded',
    decisionOutcome: 'Ok',
    executionSnapshotId: request.clientSnapshotId,
    projectPersistenceRevision: request.expectedPersistenceRevision,
    flowVersionHash: request.expectedCanonicalFlowHash,
    decisionConfigurationHash: request.expectedDecisionConfigurationHash,
    errorMessage: null
  };
}

export function createOptionDG0ResultPage(): Readonly<Record<string, unknown>> {
  return {
    items: [structuredClone(resultSummary)],
    totalCount: 1,
    pageIndex: 0,
    pageSize: 20
  };
}

export function createOptionDG0ResultDetail(
  executionSnapshotId: string | null
): Readonly<Record<string, unknown>> {
  return {
    ...structuredClone(resultSummary),
    defects: [],
    traceability: {
      flowVersionHash: 'fixture-persisted-flow-hash',
      calibrationBundleId: null,
      sessionId: identities.sessionId,
      runId: identities.runId,
      executionSnapshotId,
      packageId: 'cvpkg-option-d-g0',
      stationId: null,
      projectPersistenceRevision: 8,
      decisionConfigurationHash: 'fixture-decision-hash'
    },
    hasEvidenceManifest: true,
    evidenceStatus: 'available',
    evidenceManifestReference: evidenceManifest.manifest.manifestId,
    evidenceTotalBytes: f03PreviewBitmapFixture.byteLength,
    retentionExpiresAtUtc: null,
    evidenceMessage: '本次结果证据完整，可导出。',
    hasImage: false,
    imageReference: null,
    imageMissing: false,
    imageMissingMessage: null,
    hasOutputData: true,
    hasAnalysisData: true
  };
}

export function createOptionDG0EvidenceManifest(): Readonly<Record<string, unknown>> {
  return structuredClone(evidenceManifest);
}

function assertFixtureIntegrity(): void {
  const operators = flow.operators;
  const nodeIds = new Set(operators.map(item => item.id));
  const decision = flow.decisionConfiguration.finalDecisionBinding;
  if (!nodeIds.has(optionDG0DeterministicFixture.coverage.ordinaryNodeId) ||
      !nodeIds.has(optionDG0DeterministicFixture.coverage.previewNodeId) ||
      !nodeIds.has(optionDG0DeterministicFixture.coverage.roiNodeId)) {
    throw new Error('Option D G0 fixture is missing an approved node identity.');
  }
  if (globalVariables.variables.length !== 1 || globalVariables.sourceBindings.length !== 1 ||
      globalVariables.targetBindings.length !== 1) {
    throw new Error('Option D G0 fixture must freeze one variable and both binding directions.');
  }
  if (decision.sourceOperatorId !== identities.judgeNodeId ||
      decision.sourceOutputPortId !== identities.judgeOutputId) {
    throw new Error('Option D G0 fixture decision identity drifted from the judge output.');
  }
  if (resultSummary.projectId !== project.id ||
      evidenceManifest.manifest.inspectionResultId !== resultSummary.resultId) {
    throw new Error('Option D G0 fixture formal result evidence identity is inconsistent.');
  }
}

assertFixtureIntegrity();
