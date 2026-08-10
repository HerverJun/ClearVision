<script setup lang="ts">
import { computed, onMounted, onUnmounted, shallowRef, watch, type WatchStopHandle } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useProductRuntime } from '@/app/productRuntime';
import { useStudioPlatform } from '@/app/studioPlatform';
import { CvPageHeader, CvPageState, type CvPageStateKind } from '@/design-system/patterns';
import { CvIcon } from '@/design-system/icons';
import { CvIconButton, CvInlineAlert } from '@/design-system/primitives';
import AiDiagnosticsDrawer from './AiDiagnosticsDrawer.vue';
import AiHistoryDrawer from './AiHistoryDrawer.vue';
import AiClarificationPanel from './AiClarificationPanel.vue';
import AiBuildProgress from './AiBuildProgress.vue';
import AiBuildWorkspace from './AiBuildWorkspace.vue';
import AiApplyPreview from './AiApplyPreview.vue';
import AiPendingParametersPanel from './AiPendingParametersPanel.vue';
import AiResourceDecisionPanel from './AiResourceDecisionPanel.vue';
import AiPlanProgress from './AiPlanProgress.vue';
import AiPlanWorkspace from './AiPlanWorkspace.vue';
import AiProjectContext from './AiProjectContext.vue';
import AiTaskComposer from './AiTaskComposer.vue';
import AiWorkbenchStage from './AiWorkbenchStage.vue';
import { aiWorkbenchActionModel, type AiWorkbenchActionId } from './actionModel';
import { createAiSessionOwner, type AiSessionOwner } from './aiSessionOwner';
import type { AiRequirementMode, AiSessionSummaryV1 } from './contracts';
import { projectAiWorkbench } from './projection';
import { initialAiWorkbenchState } from './reducer';

const route = useRoute();
const router = useRouter();
const runtime = useProductRuntime();
const platform = useStudioPlatform();
const owner = shallowRef<AiSessionOwner | null>(null);
const historyOpen = shallowRef(false);
const diagnosticsOpen = shallowRef(false);
let stopRouteWatch: WatchStopHandle | null = null;

const state = computed(() => owner.value?.state.value ?? initialAiWorkbenchState);
const projection = computed(() => owner.value?.projection.value ?? projectAiWorkbench(state.value));
const actionModel = computed(() => owner.value?.actionModel.value ?? aiWorkbenchActionModel(state.value));
const diagnostics = computed(() => owner.value?.diagnostics() ?? {
  requestCount: 0, streamCount: 0, timerCount: 0, subscriptionCount: 0, disposed: true
});
const history = computed(() => owner.value?.history.value ?? null);
const currentSessionId = computed(() => state.value.session?.sessionId ?? null);
const requestedSessionId = computed(() => typeof route.query.sessionId === 'string' ? route.query.sessionId : null);
const projectId = computed(() => typeof route.params.id === 'string' ? route.params.id : null);
const routeIdentity = computed(() => `${String(route.name)}|${projectId.value ?? ''}|${requestedSessionId.value ?? ''}`);
const showComposer = computed(() => state.value.phase === 'idle');
const showProgress = computed(() => ['intent-routing', 'planning', 'cancelling', 'recovering'].includes(state.value.phase) && !state.value.plan);
const showBuildProgress = computed(() => state.value.run.kind === 'build' && [
  'build-starting', 'building', 'validating', 'build-cancelling', 'recovering'
].includes(state.value.phase));
const buildReadonly = computed(() => state.value.buildStale || ['build-failed', 'build-cancelled'].includes(state.value.phase));
const showApplyPreview = computed(() => state.value.build !== null && [
  'build-ready', 'handoff-creating', 'handoff-unknown-outcome', 'handoff-created'
].includes(state.value.phase));
const terminalPageState = computed<CvPageStateKind | null>(() => {
  if (state.value.plan || state.value.build) return null;
  if (['build-failed', 'plan-failed'].includes(state.value.phase)) return 'error';
  if (['baseline-conflict', 'session-conflict'].includes(state.value.phase)) return 'conflict';
  if (['unknown-outcome', 'handoff-unknown-outcome'].includes(state.value.phase)) return 'unknown';
  if (state.value.phase === 'offline-or-service-unavailable') return 'offline';
  if (['build-cancelled', 'cancelled'].includes(state.value.phase)) return 'empty';
  return null;
});
const terminalDescription = computed(() => `${projection.value.stageDescription} ${projection.value.nextHint}`.trim());

function replaceOwner(): void {
  owner.value?.dispose();
  historyOpen.value = false;
  diagnosticsOpen.value = false;
  const next = createAiSessionOwner({
    api: runtime.api,
    requestedSessionId: requestedSessionId.value,
    projectId: projectId.value
  });
  owner.value = next;
  void next.start();
}

function releaseOwner(current: AiSessionOwner): void {
  current.dispose();
  if (owner.value === current) owner.value = null;
  const released = current.diagnostics();
  if (released.requestCount || released.streamCount || released.timerCount || released.subscriptionCount) {
    throw new Error('切换 AI 会话前未能释放全部运行资源。');
  }
}

async function restoreSession(session: AiSessionSummaryV1): Promise<void> {
  const current = owner.value;
  if (!current || session.sessionId === currentSessionId.value) return;
  historyOpen.value = false;
  diagnosticsOpen.value = false;
  releaseOwner(current);
  await router.push({
    name: session.projectId ? 'project-ai-workbench' : 'ai-workbench',
    ...(session.projectId ? { params: { id: session.projectId } } : {}),
    query: { sessionId: session.sessionId }
  });
}

async function deleteSession(session: AiSessionSummaryV1): Promise<void> {
  const current = owner.value;
  if (!current) return;
  const wasCurrent = session.sessionId === currentSessionId.value;
  const deleted = await current.deleteSession(session);
  if (!deleted || owner.value !== current || !wasCurrent) return;
  historyOpen.value = false;
  diagnosticsOpen.value = false;
  releaseOwner(current);
  const query = { ...route.query };
  delete query.sessionId;
  await router.replace({ name: route.name ?? undefined, params: route.params, query });
  if (!owner.value) replaceOwner();
}

async function reconcileSessionDelete(): Promise<void> {
  const current = owner.value;
  const deletedSessionId = current?.history.value.deletingSessionId ?? null;
  if (!current || !deletedSessionId) return;
  const wasCurrent = deletedSessionId === currentSessionId.value;
  const deleted = await current.reconcileSessionDelete();
  if (!deleted || owner.value !== current || !wasCurrent) return;
  historyOpen.value = false;
  releaseOwner(current);
  const query = { ...route.query };
  delete query.sessionId;
  await router.replace({ name: route.name ?? undefined, params: route.params, query });
  if (!owner.value) replaceOwner();
}

async function handoffAndOpenWorkspace(reconcile: boolean): Promise<void> {
  const current = owner.value;
  if (!current) return;
  const artifact = reconcile
    ? await current.reconcileHandoff()
    : await current.prepareHandoff();
  if (!artifact || owner.value !== current) return;
  const targetId = artifact.targetKind === 'new' ? 'new' : artifact.projectBaseline.projectId;
  if (!targetId) return;
  releaseOwner(current);
  await router.push({
    name: 'project-workspace',
    params: { id: targetId },
    query: { handoff: artifact.artifactId }
  });
}

function handleAction(actionId: AiWorkbenchActionId): void {
  const current = owner.value;
  if (!current) return;
  if (actionId === 'retryIntent') void current.retryIntent();
  if (actionId === 'startPlan') void current.startPlan();
  if (actionId === 'cancelPlan') void current.cancelPlan();
  if (actionId === 'startBuild') void current.startBuild();
  if (actionId === 'cancelBuild') void current.cancelBuild();
  if (actionId === 'recheckReadiness') void current.recheckReadiness();
  if (actionId === 'rebuild') void current.rebuild();
  if (actionId === 'previewReadiness') void current.previewReadiness();
  if (actionId === 'reconcile') void current.reconcile();
  if (actionId === 'prepareHandoff') void handoffAndOpenWorkspace(false);
  if (actionId === 'reconcileHandoff') void handoffAndOpenWorkspace(true);
  if (actionId === 'startNewTask') current.startNewTask();
}

function submitTask(description: string, mode: AiRequirementMode): void {
  void owner.value?.submitTask(description, mode);
}

onMounted(() => {
  stopRouteWatch = watch(routeIdentity, replaceOwner, { immediate: true });
});

onUnmounted(() => {
  stopRouteWatch?.();
  stopRouteWatch = null;
  owner.value?.dispose();
  owner.value = null;
});
</script>

<template>
  <section
    class="ai-workbench-page"
    :data-ai-owner-phase="state.phase"
    :data-ai-owner-request-count="diagnostics.requestCount"
    :data-ai-owner-stream-count="diagnostics.streamCount"
    :data-ai-owner-timer-count="diagnostics.timerCount"
    :data-ai-owner-subscription-count="diagnostics.subscriptionCount"
  >
    <CvPageHeader title="AI 工程工作台">
      <template #meta>
        <span class="ai-workbench-page__scope">{{ projectId ? '工程方案' : '独立方案' }}</span>
      </template>
      <template #actions>
        <CvIconButton
          label="打开历史与恢复"
          size="sm"
          :disabled="!owner"
          :aria-expanded="historyOpen"
          @click="historyOpen = true; diagnosticsOpen = false"
        >
          <CvIcon
            name="clock"
            size="sm"
          />
        </CvIconButton>
        <CvIconButton
          label="打开公开诊断"
          size="sm"
          :disabled="!owner"
          :aria-expanded="diagnosticsOpen"
          @click="diagnosticsOpen = true; historyOpen = false"
        >
          <CvIcon
            name="diagnostics"
            size="sm"
          />
        </CvIconButton>
      </template>
    </CvPageHeader>

    <div
      v-if="state.session"
      class="ai-workbench-page__context"
    >
      <AiProjectContext
        :project="state.project"
        :session="state.session"
      />
    </div>

    <CvPageState
      v-if="projection.pageState && !state.session"
      :kind="projection.pageState"
      :title="projection.pageStateTitle"
      :description="projection.pageStateDescription"
      data-ai-session-state
    />

    <template v-else>
      <AiWorkbenchStage
        :projection="projection"
        :action-model="actionModel"
        @action="handleAction"
      />

      <main class="ai-workbench-page__main">
        <AiTaskComposer
          v-if="showComposer"
          :initial-description="state.taskDescription"
          :initial-mode="state.requirementMode"
          :busy="projection.busy"
          @submit="submitTask"
        />

        <CvInlineAlert
          v-if="state.intent && !state.plan && !showProgress && !terminalPageState"
          :tone="state.phase === 'plan-failed' ? 'error' : 'warning'"
          title="任务理解结果"
        >
          {{ state.intent.publicReason || state.intent.assistantReply }}
        </CvInlineAlert>

        <AiPlanProgress
          v-if="showProgress"
          :events="state.run.events"
        />

        <AiBuildProgress
          v-if="showBuildProgress"
          :events="state.run.events"
        />

        <CvPageState
          v-if="terminalPageState"
          class="ai-workbench-page__terminal"
          :kind="terminalPageState"
          :title="projection.currentStage"
          :description="terminalDescription"
          data-ai-terminal-state
        />

        <div
          v-if="(state.plan || state.build) && state.session"
          class="ai-workbench-page__workspace"
        >
          <AiBuildWorkspace
            v-if="state.build && !showApplyPreview"
            :build="state.build"
            :stale="buildReadonly"
            :diagnostics="state.replayDiagnostics"
          />
          <AiApplyPreview
            v-else-if="state.build && showApplyPreview"
            :build="state.build"
            :project="state.project"
            :stale="buildReadonly"
          />
          <AiPlanWorkspace
            v-else-if="state.plan"
            :plan="state.plan"
            :readiness="state.readiness"
            :session="state.session"
            :events="state.run.events"
          />
          <AiClarificationPanel
            v-if="projection.clarificationQuestions.length"
            :questions="projection.clarificationQuestions"
            :selections="state.session.snapshot.planQuestionSelections"
            :confirmed-answers="state.session.snapshot.confirmedPlanAnswers"
            :optimistic-answers="state.session.snapshot.optimisticPlanAnswers"
            :busy="projection.busy"
            @submit="answers => owner?.answerClarification(answers)"
            @accept-recommended="owner?.acceptRecommendedAnswers()"
          />
          <AiPendingParametersPanel
            v-else-if="state.build && state.phase === 'parameters-pending'"
            :parameters="state.build.parameterMapping"
            :confirmed-values="state.session.snapshot.buildParameterValues"
            :busy="projection.busy"
            :file-picker="platform.filePicker"
            @confirm="values => owner?.confirmParameters(values)"
          />
          <AiResourceDecisionPanel
            v-else-if="state.build && state.phase === 'resources-pending'"
            :resources="state.build.missingResources"
            :camera-bindings="state.cameraBindings"
            :busy="projection.busy"
            @save="decisions => owner?.updateResourceDecisions(decisions)"
          />
        </div>
      </main>
    </template>

    <AiHistoryDrawer
      v-if="owner && history"
      :open="historyOpen"
      :history="history"
      :current-session-id="currentSessionId"
      :route-project-id="projectId"
      @close="historyOpen = false"
      @load-sessions="offset => owner?.loadSessionHistory(offset)"
      @load-runs="(offset, sessionId) => owner?.loadRunHistory(offset, sessionId)"
      @restore="restoreSession"
      @delete="deleteSession"
      @reconcile-delete="reconcileSessionDelete"
    />
    <AiDiagnosticsDrawer
      v-if="owner"
      :open="diagnosticsOpen"
      :state="state"
      :projection="projection"
      @close="diagnosticsOpen = false"
    />
  </section>
</template>

<style scoped>
.ai-workbench-page { display: grid; min-width: 0; align-content: start; overflow-x: clip; }
.ai-workbench-page__scope { display: inline-flex; min-height: 22px; align-items: center; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.ai-workbench-page__context { padding: 0 var(--cv-density-page-padding) var(--cv-space-3); }
.ai-workbench-page__main { display: grid; min-width: 0; gap: var(--cv-density-page-gap); padding: var(--cv-density-page-gap) var(--cv-density-page-padding) var(--cv-density-page-padding); }
.ai-workbench-page__terminal { max-width: 980px; border-block: 1px solid var(--cv-border-subtle); background: transparent; }
.ai-workbench-page__workspace { display: grid; grid-template-columns: minmax(0, 1.65fr) minmax(340px, 0.85fr); min-width: 0; align-items: start; gap: var(--cv-density-page-gap); }

@media (max-width: 1180px) {
  .ai-workbench-page__workspace { grid-template-columns: 1fr; }
}
@media (max-width: 640px) {
  .ai-workbench-page__context { padding-inline: var(--cv-space-4); }
  .ai-workbench-page__main { padding: var(--cv-space-4); }
}
</style>
