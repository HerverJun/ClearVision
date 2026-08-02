export function outcomeStatistics(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    totalAttemptCount: 9,
    executionSucceededCount: 5,
    validDecisionCount: 2,
    okCount: 1,
    ngCount: 1,
    undeterminedCount: 1,
    notApplicableCount: 1,
    invalidCount: 1,
    failedCount: 1,
    cancelledCount: 1,
    timedOutCount: 1,
    skippedCount: 1,
    ...overrides
  };
}

export function stationStatus(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    stationId: 'station-a',
    stationName: '一号检测站',
    lineName: '一号线',
    machineName: 'CV-STATION-A',
    clientVersion: '2.1.0',
    areaName: 'A 区',
    workcellName: '单元 1',
    inspectionNodeName: '瓶盖检测',
    cameraAlias: '顶视相机',
    stationRole: 'Inspection',
    owner: '生产一组',
    isEnabled: true,
    remark: '只读 fixture',
    onlineState: 'Online',
    state: 'Running',
    runtimeState: 'Running',
    isOnline: true,
    startedAtUtc: '2026-07-15T01:00:00Z',
    lastSeenAtUtc: '2026-07-15T02:00:00Z',
    packageId: 'pkg-a',
    packageName: '瓶盖检测包',
    packageVersion: '1.0.0',
    packageSha256: `sha256:${'a'.repeat(64)}`,
    sourceProjectId: 'project-a',
    sourceProjectRevision: 12,
    packageFlowHash: 'sha256:package',
    executionFlowHash: 'sha256:execution',
    flowHash: 'sha256:execution',
    executionSnapshotId: '11111111-1111-4111-8111-111111111111',
    projectRevision: 12,
    decisionConfigurationHash: 'sha256:decision',
    executionRunMode: 'Production',
    currentRunId: 'run-a',
    sessionOkCount: 1,
    sessionNgCount: 1,
    sessionErrorCount: 1,
    sessionOutcomeStatistics: outcomeStatistics(),
    sessionOutcomeStatisticsIsLegacyProjection: false,
    lastOutcome: 'Ng',
    lastInspectionStatus: 'NG',
    lastExecutionOutcome: 'Succeeded',
    lastDecisionOutcome: 'Ng',
    lastHasJudgmentSignal: true,
    lastDecisionSource: 'Fixture',
    lastReasonCode: 'NG_SIGNAL',
    lastDiagnosticCode: 'WIRE_SWAP',
    lastDiagnosticMessage: '线序错误',
    lastResultAtUtc: '2026-07-15T01:59:30Z',
    lastSequenceId: 9,
    averageExecutionTimeMs: 24.5,
    recentResultCount: 9,
    spoolPendingCount: 2,
    spoolBytes: 4096,
    cpuUsagePercent: 32.5,
    workingSetMb: 256,
    diskFreeMb: 20480,
    diskTotalMb: 51200,
    cameraStatusSummary: 'Ready',
    plcStatusSummary: 'Connected',
    currentPackageHealth: 'Healthy',
    ...overrides
  };
}

export function stationSummary(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    totalStations: 2,
    onlineStations: 1,
    offlineStations: 1,
    runningStations: 1,
    faultedStations: 0,
    alertCount: 1,
    warningStations: 0,
    criticalStations: 0,
    totalOkCount: 1,
    totalNgCount: 1,
    totalErrorCount: 1,
    outcomeStatistics: outcomeStatistics(),
    averageExecutionTimeMs: 24.5,
    offlineThresholdSeconds: 15,
    updatedAtUtc: '2026-07-15T02:00:00Z',
    ...overrides
  };
}

export function stationResult(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    schemaVersion: 2,
    stationId: 'station-a',
    lineName: '一号线',
    sequenceId: 9,
    messageId: 'message-9',
    runId: 'run-9',
    packageId: 'pkg-a',
    packageName: '瓶盖检测包',
    packageVersion: '1.0.0',
    packageFlowHash: 'sha256:package',
    executionFlowHash: 'sha256:execution',
    flowHash: 'sha256:execution',
    projectRevision: 12,
    decisionConfigurationHash: 'sha256:decision',
    executionSnapshotId: '11111111-1111-4111-8111-111111111111',
    executionRunMode: 'Production',
    imageId: 'image-9',
    outcome: 'Ng',
    inspectionStatus: 'NG',
    executionOutcome: 'Succeeded',
    decisionOutcome: 'Ng',
    hasJudgmentSignal: true,
    decisionSource: 'Fixture',
    reasonCode: 'NG_SIGNAL',
    executionTimeMs: 25,
    diagnosticCode: 'WIRE_SWAP',
    diagnosticMessage: '线序错误',
    primaryOutputsPreview: { score: '0.91' },
    startedAtUtc: '2026-07-15T01:59:59Z',
    completedAtUtc: '2026-07-15T02:00:00Z',
    createdAtUtc: '2026-07-15T02:00:01Z',
    ...overrides
  };
}

export function stationHealth(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    schemaVersion: 2,
    stationId: 'station-a',
    sequenceId: 10,
    messageId: 'health-10',
    runtimeState: 'Running',
    processUptimeSeconds: 3600,
    cpuUsagePercent: 32.5,
    workingSetMb: 256,
    privateMemoryMb: 220,
    diskFreeMb: 20480,
    diskTotalMb: 51200,
    spoolPendingCount: 2,
    spoolBytes: 4096,
    cameraStatusSummary: 'Ready',
    plcStatusSummary: 'Connected',
    currentPackageId: 'pkg-a',
    currentPackageHealth: 'Healthy',
    lastErrorCode: null,
    lastErrorMessage: null,
    createdAtUtc: '2026-07-15T02:00:00Z',
    ...overrides
  };
}

export function stationStatistics(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    fromUtc: '2026-07-15T00:00:00Z',
    toUtc: '2026-07-15T02:00:00Z',
    outcomeStatistics: outcomeStatistics(),
    averageExecutionTimeMs: 24.5,
    byStation: [{
      stationId: 'station-a',
      outcomeStatistics: outcomeStatistics(),
      averageExecutionTimeMs: 24.5
    }],
    byDiagnosticCode: [{ diagnosticCode: 'WIRE_SWAP', count: 1 }],
    hourlyTrend: [{
      hourUtc: '2026-07-15T01:00:00Z',
      outcomeStatistics: outcomeStatistics()
    }],
    ...overrides
  };
}

export function stationCommand(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    schemaVersion: 2,
    commandId: 'command-a',
    stationId: 'station-a',
    commandType: 'Ping',
    payloadJson: '{}',
    createdAtUtc: '2026-07-15T02:00:00Z',
    expiresAtUtc: '2026-07-15T02:05:00Z',
    issuedBy: 'admin',
    correlationId: 'correlation-a',
    clientRequestId: 'request-a',
    status: 'Created',
    progressPercent: 0,
    deliveredAtUtc: null,
    acceptedAtUtc: null,
    startedAtUtc: null,
    completedAtUtc: null,
    resultMessage: null,
    errorCode: null,
    ...overrides
  };
}

export function stationLog(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    schemaVersion: 2,
    stationId: 'station-a',
    sequenceId: 11,
    messageId: 'log-11',
    timestampUtc: '2026-07-15T02:00:00Z',
    level: 'WARN',
    source: 'RuntimeHost',
    eventId: 'runtime-warning',
    messageTemplate: null,
    renderedMessage: '运行包健康状态降级',
    exceptionType: null,
    exceptionMessage: null,
    correlationId: null,
    runId: 'run-a',
    packageId: 'pkg-a',
    createdAtUtc: '2026-07-15T02:00:01Z',
    ...overrides
  };
}

export function stationAudit(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    auditId: 'audit-a',
    userName: 'admin',
    action: 'StationCommandCreated',
    targetStationId: 'station-a',
    commandId: 'command-a',
    payloadSummary: 'Ping',
    createdAtUtc: '2026-07-15T02:00:00Z',
    result: 'Created',
    clientIp: '127.0.0.1',
    ...overrides
  };
}

export function stationPackage(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    schemaVersion: 2,
    packageId: 'pkg-a',
    packageName: '瓶盖检测包',
    packageVersion: '1.0.0',
    packageKind: 'Production',
    flowHash: 'sha256:package',
    sourceProjectId: 'project-a',
    sourceProjectRevision: 12,
    decisionConfigurationHash: 'sha256:decision',
    createdBy: 'admin',
    minStationVersion: '2.0.0',
    requiredOperators: ['Threshold'],
    sizeBytes: 4096,
    sha256: 'a'.repeat(64),
    createdAtUtc: '2026-07-15T01:00:00Z',
    ...overrides
  };
}
