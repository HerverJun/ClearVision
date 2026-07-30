export type AiOperationKind = 'session_create' | 'session_delete' | 'plan_run' | 'build_run' | 'handoff_create';
export type AiOperationStatus = 'pending' | 'created' | 'failed' | 'rejected';
export type AiRequirementMode = 'strict' | 'draft';
export type AiRunStatus = 'pending' | 'running' | 'completed' | 'failed' | 'cancelled' | 'blocked' | 'warning';
export type AiScalarValue = string | number | boolean | null;

export interface AiProjectBaselineV1 {
  readonly targetKind: 'new' | 'existing';
  readonly projectId: string | null;
  readonly persistenceRevision: number | null;
  readonly canonicalFlowHash: string;
}

export interface AiProjectContextV1 {
  readonly id: string;
  readonly name: string;
  readonly version: string;
  readonly persistenceRevision: number;
  readonly modifiedAt: string | null;
}

export interface AiPlanAnswerV1 {
  readonly questionId: string;
  readonly field: string;
  readonly value: string;
  readonly origin: string;
  readonly confidence: number;
  readonly resolved: boolean;
}

export interface AiResourceRequirementV1 {
  readonly canonicalId: string;
  readonly resourceType: string;
  readonly resourceName: string;
  readonly resourceKey: string;
  readonly operatorKey: string;
  readonly operatorId: string;
  readonly operatorType: string;
  readonly operatorIndex: number;
  readonly parameterName: string;
  readonly status: string;
  readonly blockingScope: string;
  readonly resolutionTarget: string;
  readonly draftPolicy: string;
  readonly description: string;
  readonly source: string;
  readonly aliases: readonly string[];
}

export interface AiResourceDecisionV1 {
  readonly canonicalId: string;
  readonly status: 'bound';
  readonly resourceKey: string;
  readonly resourceType: string;
  readonly operatorKey: string;
  readonly operatorId: string;
  readonly operatorType: string;
  readonly operatorIndex: number;
  readonly parameterName: string;
  readonly valueSummary: string;
  readonly source: string;
}

export interface AiResourceDecisionSelectionV1 {
  readonly canonicalId: string;
  readonly resourceKey: string;
}

export interface AiBuildBlockerV1 {
  readonly id: string;
  readonly category: string;
  readonly field: string;
  readonly questionId: string;
  readonly blocksBuild: boolean;
  readonly resolutionMode: string;
  readonly publicLabel: string;
  readonly resource: AiResourceRequirementV1 | null;
}

export interface AiBuildReadinessV1 {
  readonly canBuild: boolean;
  readonly blockers: readonly AiBuildBlockerV1[];
  readonly resolvedFields: readonly string[];
  readonly remainingFields: readonly string[];
  readonly primaryMessage: string;
  readonly contractVersion: string;
  readonly missingResources: readonly AiResourceRequirementV1[];
}

export interface AiReadinessPreviewV1 {
  readonly planId: string;
  readonly planHash: string;
  readonly requirementMode: AiRequirementMode;
  readonly answerRevision: number;
  readonly resourceRevision: number;
  readonly acceptedAnswers: readonly AiPlanAnswerV1[];
  readonly answerSetFingerprint: string;
  readonly buildReadiness: AiBuildReadinessV1;
  readonly deferredQuestionIds: readonly string[];
  readonly pendingConfirmationCount: number;
  readonly resourcePendingCount: number;
  readonly hardBlockerCount: number;
  readonly contractValid: boolean;
  readonly failureCode: string;
  readonly failureMessage: string;
  readonly metadataOnly: true;
}

export interface AiRequirementMaturityV1 {
  readonly maturity: string;
  readonly taskType: string;
  readonly canPlan: boolean;
  readonly canBuild: boolean;
  readonly objectSignals: readonly string[];
  readonly taskSignals: readonly string[];
  readonly missingFields: readonly string[];
  readonly blockingReasons: readonly string[];
  readonly publicReason: string;
  readonly metadataOnly: true;
}

export interface AiPlanContextSummaryV1 {
  readonly hasCurrentFlow: boolean;
  readonly hasCurrentResult: boolean;
  readonly attachmentCount: number;
  readonly templateSelectionMode: string;
  readonly templateId: string;
  readonly contextKinds: readonly string[];
  readonly operatorCatalogTools: readonly string[];
}

export interface AiTemplateSelectionV1 {
  readonly mode: string;
  readonly templateId: string | null;
  readonly scenarioKey: string | null;
}

export interface AiSessionSnapshotV1 {
  readonly schemaVersion: number;
  readonly revision: number;
  readonly projectId: string | null;
  readonly lifecycleState: string;
  readonly planRunId: string | null;
  readonly planRunStatus: string | null;
  readonly buildRunId: string | null;
  readonly buildRunStatus: string | null;
  readonly buildClientOperationId: string | null;
  readonly projectBaseline: AiProjectBaselineV1 | null;
  readonly requirementMode: AiRequirementMode;
  readonly planQuestionSelections: Readonly<Record<string, string>>;
  readonly confirmedPlanAnswers: readonly AiPlanAnswerV1[];
  readonly optimisticPlanAnswers: readonly AiPlanAnswerV1[];
  readonly answerRevision: number;
  readonly buildParameterValues: Readonly<Record<string, AiScalarValue>>;
  readonly readinessPreview: AiReadinessPreviewV1 | null;
  readonly missingResources: readonly AiResourceRequirementV1[];
  readonly resourceDecisions: readonly AiResourceDecisionV1[];
  readonly resourceRevision: number;
  readonly buildResult: AiBuildResultV1 | null;
  readonly planAcceptedRecommendedDefaults: boolean;
  readonly planTerminalSequence: number | null;
  readonly buildTerminalSequence: number | null;
  readonly submittedBuildFingerprint: string | null;
  readonly updatedAtUtc: string;
}

export interface AiSessionDetailV1 {
  readonly sessionId: string;
  readonly snapshot: AiSessionSnapshotV1;
  readonly updatedAtUtc: string;
}

export interface AiOperationProjectionV1 {
  readonly clientOperationId: string;
  readonly kind: AiOperationKind;
  readonly status: AiOperationStatus;
  readonly sessionId: string | null;
  readonly runId: string | null;
  readonly artifactId: string | null;
  readonly payloadFingerprint: string;
  readonly projectBaseline: AiProjectBaselineV1 | null;
  readonly errorCode: string | null;
  readonly publicMessage: string | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly expiresAtUtc: string;
}

export type AiHandoffStatus = 'available' | 'consuming' | 'consumed' | 'expired' | 'rejected';

export interface AiHandoffArtifactIdentityV1 {
  readonly schemaVersion: 1;
  readonly artifactId: string;
  readonly clientOperationId: string;
  readonly sessionId: string;
  readonly sessionRevision: number;
  readonly planRunId: string;
  readonly planId: string;
  readonly planHash: string;
  readonly buildRunId: string;
  readonly buildClientOperationId: string;
  readonly buildIdentity: string;
  readonly targetKind: 'new' | 'existing';
  readonly projectBaseline: AiProjectBaselineV1;
  readonly candidateFlowFingerprint: string;
  readonly createdAtUtc: string;
  readonly expiresAtUtc: string;
  readonly status: AiHandoffStatus;
}

export interface AiHandoffCreateCommandV1 {
  readonly clientOperationId: string;
  readonly sessionId: string;
  readonly expectedSessionRevision: number;
  readonly planRunId: string;
  readonly planId: string;
  readonly planHash: string;
  readonly buildRunId: string;
  readonly buildClientOperationId: string;
  readonly buildIdentity: string;
  readonly candidateFlowFingerprint: string;
  readonly answerRevision: number;
  readonly resourceRevision: number;
  readonly projectBaseline: AiProjectBaselineV1;
}

export interface AiSessionCreateResponseV1 {
  readonly operation: AiOperationProjectionV1;
  readonly session: AiSessionDetailV1 | null;
}

export interface AiSessionCreateCommandV1 {
  readonly clientOperationId: string;
  readonly projectId?: string;
}

export interface AiWorkspaceSnapshotMutationV1 {
  readonly expectedRevision: number;
  readonly clientMutationId: string;
  readonly projectId?: string | null;
  readonly lifecycleState?: string;
  readonly planQuestionSelections?: Readonly<Record<string, string>>;
  readonly confirmedPlanAnswers?: readonly AiPlanAnswerV1[];
  readonly optimisticPlanAnswers?: readonly AiPlanAnswerV1[];
  readonly answerRevision?: number;
  readonly buildParameterValues?: Readonly<Record<string, AiScalarValue>>;
  readonly readinessPreview?: AiReadinessPreviewV1;
  readonly resourceDecisions?: readonly AiResourceDecisionSelectionV1[];
  readonly requirementMode?: AiRequirementMode;
  readonly planAcceptedRecommendedDefaults?: boolean;
}

export interface AiSemanticExtractionV1 {
  readonly isVisionRequest: boolean;
  readonly intent: string;
  readonly taskType: string;
  readonly confidence: number;
  readonly taskTypeConfidence: number;
  readonly inspectionObject: string;
  readonly targetAttribute: string;
  readonly defectType: string;
  readonly measurementTarget: string;
  readonly imageSource: string;
  readonly okCondition: string;
  readonly ngCondition: string;
  readonly outputTarget: string;
  readonly suggestedRoute: string;
  readonly canPlanCandidate: boolean;
  readonly canBuildCandidate: boolean;
  readonly missingFields: readonly string[];
  readonly source: string;
  readonly failureCode: string;
  readonly sanitizedErrorMessage: string;
}

export interface AiIntentResultV1 {
  readonly intent: string;
  readonly confidence: string;
  readonly shouldOpenPlan: boolean;
  readonly shouldBuildDirectly: boolean;
  readonly canBuild: boolean;
  readonly needsClarification: boolean;
  readonly publicReason: string;
  readonly assistantReply: string;
  readonly fallbackAllowed: boolean;
  readonly routerSource: string;
  readonly fallbackReason: string;
  readonly semanticExtraction: AiSemanticExtractionV1 | null;
  readonly shouldMergeIntoPendingPlan: boolean;
  readonly shouldResetPendingPlan: boolean;
  readonly planAnswerUpdates: readonly AiPlanAnswerV1[];
  readonly resolvedPlanFields: readonly string[];
  readonly remainingPlanFields: readonly string[];
}

export interface AiRecommendedRouteV1 {
  readonly routeId: string;
  readonly title: string;
  readonly summary: string;
  readonly operators: readonly string[];
  readonly templateDecision: string;
}

export interface AiClarificationOptionV1 {
  readonly value: string;
  readonly label: string;
  readonly recommended: boolean;
  readonly answerEffect: string;
  readonly recommendationReason: string;
  readonly description: string;
  readonly impact: string;
}

export interface AiClarificationQuestionV1 {
  readonly id: string;
  readonly field: string;
  readonly title: string;
  readonly why: string;
  readonly defaultValue: string;
  readonly defaultAssumption: string;
  readonly impact: string;
  readonly options: readonly AiClarificationOptionV1[];
}

export interface AiDefaultAssumptionV1 {
  readonly id: string;
  readonly label: string;
  readonly value: string;
  readonly impact: string;
}

export interface AiPlanPublicEventV1 {
  readonly stage: string;
  readonly status: string;
  readonly title: string;
  readonly summary: string;
  readonly metadata: Readonly<Record<string, string>>;
}

export interface AiPlanV1 {
  readonly planContractVersion: string;
  readonly planId: string;
  readonly planHash: string;
  readonly planSource: string;
  readonly currentPhase: string;
  readonly fallbackReason: string;
  readonly plannerFailureStage: string;
  readonly plannerFailureCode: string;
  readonly sanitizedErrorKind: string;
  readonly sanitizedErrorMessage: string;
  readonly originalUserPrompt: string;
  readonly goal: string;
  readonly intent: string;
  readonly confidence: string;
  readonly requirementUnderstanding: readonly string[];
  readonly confirmedPlanAnswers: readonly AiPlanAnswerV1[];
  readonly resolvedPlanFields: readonly string[];
  readonly remainingPlanFields: readonly string[];
  readonly recommendedRoute: AiRecommendedRouteV1;
  readonly clarificationQuestions: readonly AiClarificationQuestionV1[];
  readonly recommendedDefaults: readonly AiDefaultAssumptionV1[];
  readonly risks: readonly string[];
  readonly acceptanceCriteria: readonly string[];
  readonly executablePlan: readonly string[];
  readonly canBuild: boolean;
  readonly blockingReasons: readonly string[];
  readonly buildReadiness: AiBuildReadinessV1;
  readonly semanticExtraction: AiSemanticExtractionV1 | null;
  readonly requirementMaturity: AiRequirementMaturityV1 | null;
  readonly decisionTrace: null;
  readonly nextAction: string;
  readonly contextSummary: AiPlanContextSummaryV1;
  readonly operatorCatalogVersion: string;
  readonly templateCatalogVersion: string;
  readonly templateSelection: AiTemplateSelectionV1 | null;
  readonly stationBoundarySummary: string;
  readonly plcOutputPolicy: string;
  readonly planWarnings: readonly string[];
  readonly contractRepairNotes: readonly string[];
  readonly publicEvents: readonly AiPlanPublicEventV1[];
  readonly metadataOnly: true;
}

export interface AiAgentRunEventV1 {
  readonly runId: string;
  readonly sequence: number;
  readonly timestamp: string;
  readonly eventType: string;
  readonly stage: string;
  readonly title: string;
  readonly summary: string;
  readonly status: AiRunStatus;
  readonly sessionId: string | null;
  readonly planId: string | null;
  readonly planHash: string | null;
  readonly publicMessage: string | null;
  readonly plan: AiPlanV1 | null;
  readonly build: AiBuildResultV1 | null;
  readonly workspaceSnapshot: AiSessionSnapshotV1 | null;
  readonly metadataOnly: true;
  readonly redactionPass: true;
}

export interface AiAgentRunSummaryV1 {
  readonly runId: string;
  readonly status: AiRunStatus;
  readonly title: string;
  readonly summary: string;
  readonly firstFixRecommendation: string;
  readonly lastSequence: number;
  readonly eventCount: number;
  readonly duplicateEventCount: number;
  readonly droppedEventCount: number;
  readonly staleEventCount: number;
}

export interface AiAgentRunReplaySnapshotV1 {
  readonly storageVersion: string;
  readonly runId: string;
  readonly generatedAt: string;
  readonly firstSequence: number;
  readonly lastSequence: number;
  readonly eventCount: number;
  readonly metadataOnly: true;
  readonly redactionPass: true;
  readonly events: readonly AiAgentRunEventV1[];
}

export interface AiAgentRunReplayDiagnosticsV1 {
  readonly runId: string;
  readonly eventCount: number;
  readonly duplicateEventCount: number;
  readonly droppedEventCount: number;
  readonly staleEventCount: number;
  readonly metadataOnly: true;
  readonly redactionPass: true;
}

export interface AiAgentRunReplayV1 {
  readonly summary: AiAgentRunSummaryV1;
  readonly events: readonly AiAgentRunEventV1[];
  readonly snapshot: AiAgentRunReplaySnapshotV1;
  readonly diagnostics: AiAgentRunReplayDiagnosticsV1;
}

export interface AiPlanRunResponseV1 {
  readonly runId: string | null;
  readonly sessionId: string | null;
  readonly brief: string | null;
  readonly events: readonly AiAgentRunEventV1[];
  readonly workspaceSnapshot: AiSessionSnapshotV1 | null;
  readonly operation: AiOperationProjectionV1;
}

export interface AiReadinessPreviewCommandV1 {
  readonly sessionId: string;
  readonly expectedRevision: number;
  readonly planId: string;
  readonly planHash: string;
  readonly planSnapshot: AiPlanV1;
  readonly requirementMode: AiRequirementMode;
  readonly confirmedAnswers: readonly AiPlanAnswerV1[];
  readonly userSelections: Readonly<Record<string, string>>;
  readonly acceptedDefaults: readonly string[];
  readonly acceptedRecommendedDefaults: boolean;
  readonly answerRevision: number;
  readonly resourceRevision: number;
  readonly originalUserPrompt: string;
  readonly metadataOnly: true;
}

export interface AiParameterOptionV1 {
  readonly label: string;
  readonly value: string;
}

export interface AiParameterConditionV1 {
  readonly parameter: string;
  readonly comparison: 'equals' | 'not-equals' | 'empty' | 'not-empty';
  readonly value: AiScalarValue;
}

export interface AiParameterConditionSetV1 {
  readonly allConditions: readonly AiParameterConditionV1[];
  readonly anyConditions: readonly AiParameterConditionV1[];
}

export interface AiBuildParameterV1 {
  readonly canonicalKey: string;
  readonly tempId: string;
  readonly operatorType: string;
  readonly operatorDisplayName: string;
  readonly parameterName: string;
  readonly parameterDisplayName: string;
  readonly purpose: string;
  readonly dataType: string;
  readonly isRequired: boolean;
  readonly value: AiScalarValue;
  readonly hasExplicitValue: boolean;
  readonly valueSummary: string;
  readonly source: string;
  readonly pending: boolean;
  readonly impact: string;
  readonly suggestedReason: string;
  readonly defaultValue: AiScalarValue;
  readonly minValue: AiScalarValue;
  readonly maxValue: AiScalarValue;
  readonly options: readonly AiParameterOptionV1[];
  readonly requiredPolicy: string;
  readonly atLeastOneGroup: string;
  readonly mutuallyExclusiveGroup: string;
  readonly requiredWhen: AiParameterConditionSetV1 | null;
  readonly enabledWhen: AiParameterConditionSetV1 | null;
  readonly disabledWhen: AiParameterConditionSetV1 | null;
  readonly resourceKind: string;
  readonly resourceCanonicalId: string;
  readonly resourceDependent: boolean;
}

export interface AiOperatorPipelineStepV1 {
  readonly tempId: string;
  readonly operatorType: string;
  readonly source: string;
  readonly status: string;
  readonly repairNote: string;
}

export interface AiWorkflowDiffV1 {
  readonly addedNodes: readonly string[];
  readonly modifiedNodes: readonly string[];
  readonly preservedNodes: readonly string[];
  readonly removedNodes: readonly string[];
  readonly addedOrChangedParameters: readonly string[];
  readonly pendingParameters: readonly string[];
  readonly missingResources: readonly string[];
  readonly validationFailures: readonly string[];
  readonly autoRepairs: readonly string[];
  readonly deploymentBlockers: readonly string[];
  readonly metadataOnly: true;
}

export interface AiApplyGateV1 {
  readonly canvasApplyReady: boolean;
  readonly runtimeDraftReady: boolean;
  readonly deploymentReady: boolean;
  readonly blocked: boolean;
  readonly status: string;
  readonly applyBlockers: readonly string[];
  readonly deploymentBlockers: readonly string[];
  readonly firstFixRecommendation: string;
  readonly metadataOnly: true;
}

export interface AiBuildCheckV1 {
  readonly id: string;
  readonly label: string;
  readonly status: 'passed' | 'failed' | 'pending';
  readonly summary: string;
  readonly blockerCount: number;
  readonly warningCount: number;
}

export interface AiBuildValidationV1 {
  readonly structural: AiBuildCheckV1;
  readonly dryRun: AiBuildCheckV1;
  readonly manifest: AiBuildCheckV1;
  readonly applyGate: AiApplyGateV1;
  readonly handoffEligible: boolean;
  readonly readinessStatus: string;
  readonly firstFixRecommendation: string;
  readonly metadataOnly: true;
}

export interface AiBuildTimelineItemV1 {
  readonly stage: string;
  readonly toolName: string;
  readonly source: string;
  readonly inputSummary: string;
  readonly outputSummary: string;
  readonly status: string;
  readonly durationMs: number;
  readonly evidenceId: string;
  readonly repairAction: string;
  readonly warningCode: string;
  readonly applyImpact: string;
  readonly deploymentImpact: string;
  readonly metadataOnly: true;
  readonly redactionPass: true;
}

export interface AiBuildResultV1 {
  readonly schemaVersion: 1;
  readonly runId: string;
  readonly buildId: string;
  readonly clientOperationId: string;
  readonly buildIdentity: string;
  readonly submittedBuildFingerprint: string;
  readonly planId: string;
  readonly planHash: string;
  readonly answerSetFingerprint: string;
  readonly answerRevision: number;
  readonly resourceRevision: number;
  readonly projectBaseline: AiProjectBaselineV1;
  readonly candidateFlowFingerprint: string;
  readonly operatorCount: number;
  readonly connectionCount: number;
  readonly operatorPipeline: readonly AiOperatorPipelineStepV1[];
  readonly parameterMapping: readonly AiBuildParameterV1[];
  readonly missingResources: readonly AiResourceRequirementV1[];
  readonly workflowDiff: AiWorkflowDiffV1;
  readonly validation: AiBuildValidationV1;
  readonly publicTimeline: readonly AiBuildTimelineItemV1[];
  readonly publicWarnings: readonly string[];
  readonly metadataOnly: true;
  readonly redactionPass: true;
}

export interface AiBuildRunCommandV1 {
  readonly clientOperationId: string;
  readonly target: AiProjectBaselineV1;
  readonly description: string;
  readonly sessionId: string;
  readonly requirementMode: AiRequirementMode;
  readonly buildFromPlan: Readonly<{
    planId: string;
    planHash: string;
    workspaceExpectedRevision: number;
    planSnapshot: AiPlanV1;
    confirmedAnswers: readonly AiPlanAnswerV1[];
    userSelections: Readonly<Record<string, string>>;
    acceptedDefaults: readonly string[];
    operatorCatalogVersion: string;
    stationBoundarySummary: string;
    plcOutputPolicy: string;
    buildIntent: string;
    originalUserPrompt: string;
    acceptedRecommendedDefaults: boolean;
    answerRevision: number;
    resourceRevision: number;
    parameterValues: Readonly<Record<string, AiScalarValue>>;
    resourceDecisions: readonly AiResourceDecisionV1[];
    metadataOnly: true;
  }>;
}

export interface AiBuildRevalidationResponseV1 {
  readonly build: AiBuildResultV1;
  readonly snapshot: AiSessionSnapshotV1;
  readonly metadataOnly: true;
}

export interface AiCameraBindingOptionV1 {
  readonly id: string;
  readonly displayName: string;
  readonly isEnabled: boolean;
}

export class AiContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expected: string) {
    super(`${path} must be ${expected}.`);
    this.name = 'AiContractDecodeError';
    this.path = path;
  }
}
