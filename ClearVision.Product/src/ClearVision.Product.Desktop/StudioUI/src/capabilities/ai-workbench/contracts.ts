export type AiOperationKind = 'session_create' | 'session_delete' | 'plan_run' | 'build_run';
export type AiOperationStatus = 'pending' | 'created' | 'failed' | 'rejected';
export type AiRequirementMode = 'strict' | 'draft';
export type AiRunStatus = 'pending' | 'running' | 'completed' | 'failed' | 'cancelled' | 'blocked' | 'warning';

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
  readonly status: string;
  readonly blockingScope: string;
  readonly resolutionTarget: string;
  readonly draftPolicy: string;
  readonly description: string;
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
  readonly readinessPreview: AiReadinessPreviewV1 | null;
  readonly planAcceptedRecommendedDefaults: boolean;
  readonly planTerminalSequence: number | null;
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
  readonly payloadFingerprint: string;
  readonly projectBaseline: AiProjectBaselineV1 | null;
  readonly errorCode: string | null;
  readonly publicMessage: string | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly expiresAtUtc: string;
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
  readonly readinessPreview?: AiReadinessPreviewV1;
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
  readonly nextAction: string;
  readonly operatorCatalogVersion: string;
  readonly templateCatalogVersion: string;
  readonly stationBoundarySummary: string;
  readonly plcOutputPolicy: string;
  readonly planWarnings: readonly string[];
  readonly publicEvents: readonly AiPlanPublicEventV1[];
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

export interface AiAgentRunReplayV1 {
  readonly summary: AiAgentRunSummaryV1;
  readonly events: readonly AiAgentRunEventV1[];
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

export class AiContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expected: string) {
    super(`${path} must be ${expected}.`);
    this.name = 'AiContractDecodeError';
    this.path = path;
  }
}
