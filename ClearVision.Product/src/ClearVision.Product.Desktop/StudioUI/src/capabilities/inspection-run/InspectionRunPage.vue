<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { useProductRuntime } from '@/app/productRuntime';
import { isProjectId } from '@/capabilities/projects-read/projectContracts';
import { createLocalResultsDeepLink } from '@/shared/productionTraceLinks';
import { createInspectionRunOwner } from './inspectionRunOwner';
import { createInspectionRunApiAdapter } from './realtimeApiAdapter';
import { createInspectionSseAdapter } from './sseAdapter';
import { createInspectionRunPageOwner } from './inspectionRunPageOwner';
import RunConsole from './RunConsole.vue';
import {
  flattenRunDiagnostics,
  type RunConsoleAdmissionCheck,
  type RunConsoleResultItem,
  type RunConsoleViolation
} from './runConsoleProjection';
import {
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvSelect,
  type CvSelectOption,
  type CvStatusTone
} from '@/design-system';

const route = useRoute();
const runtime = useProductRuntime();
const projectId = typeof route.params.id === 'string' ? route.params.id : '';
if (!isProjectId(projectId)) throw new TypeError('Inspection route requires a valid Project id.');
const run = createInspectionRunOwner({
  projectId,
  api: createInspectionRunApiAdapter(runtime.api),
  sse: createInspectionSseAdapter(runtime.api)
});
const owner = createInspectionRunPageOwner({ projectId, api: runtime.api, run });
const detachLeaveGuard = runtime.leaveGuard.attachInspectionRun(run);
const projection = owner.projection;
const runState = run.projection;
const isContinuous = computed(() => runState.runtime?.sessionType === 'ContinuousInspection');
const occupiedByOther = computed(() => runState.runtime?.isBusy === true && !isContinuous.value);
const pending = computed(() => ['loading', 'admitting', 'starting', 'stopping'].includes(projection.phase) ||
  ['hydrating', 'starting', 'stopping', 'reconnecting'].includes(runState.phase));
const canStart = computed(() => projection.phase === 'ready' && projection.admission?.allowed === true &&
  !runState.runtime?.isBusy && Boolean(projection.project));
const canStop = computed(() => isContinuous.value && runState.runtime?.isBusy === true &&
  projection.phase !== 'stopping');
const canReconcile = computed(() => runState.phase === 'disconnected' ||
  runState.runtime?.isBusy === true || projection.phase === 'error');
const cameraOptions = computed<readonly CvSelectOption[]>(() => projection.cameras.map(camera => ({
  value: camera.id,
  label: camera.connectionStatus
    ? camera.label + ' · ' + (camera.connectionStatus === 'Connected' ? '已连接' : camera.connectionStatus)
    : camera.label,
  disabled: !camera.enabled
})));
const tone = computed<CvStatusTone>(() => {
  if (occupiedByOther.value || runState.phase === 'reconnecting' || runState.phase === 'disconnected') return 'warning';
  if (runState.phase === 'faulted' || projection.phase === 'error') return 'error';
  if (runState.runtime?.isBusy) return 'info';
  if (projection.admission?.allowed) return 'ok';
  return 'idle';
});
const phaseLabel = computed(() => {
  if (occupiedByOther.value) return '其他运行占用';
  const labels = {
    idle: '未运行',
    hydrating: '读取状态',
    starting: '启动中',
    running: '连续检测中',
    stopping: '停止中',
    reconnecting: '实时恢复中',
    disconnected: '实时已断开',
    occupied: '其他运行占用',
    faulted: '运行故障',
    disposed: '已释放'
  };
  return labels[runState.phase];
});
const selectedCamera = computed(() => projection.cameras.find(
  camera => camera.id === projection.selectedCameraId) ?? null);
const identity = computed(() => [
  { key: 'revision', label: '保存修订', value: String(projection.admission?.persistenceRevision ??
    runState.runtime?.persistenceRevision ?? projection.project?.persistenceRevision ?? '--') },
  { key: 'snapshot', label: '执行快照', value: projection.admission?.clientSnapshotId ??
    runState.runtime?.clientSnapshotId ?? '--' },
  { key: 'flow', label: '流程身份', value: projection.admission?.canonicalFlowHash ??
    runState.runtime?.canonicalFlowHash ?? '--' },
  { key: 'decision', label: '判定身份', value: projection.admission?.decisionConfigurationHash ??
    runState.runtime?.decisionConfigurationHash ?? '--' },
  { key: 'session', label: '会话', value: runState.runtime?.sessionId ?? '--' }
]);
const admissionCodes = computed(() => projection.admission?.violations
  .map(item => item.code ?? '')
  .filter(Boolean) ?? []);
const hasCode = (...segments: readonly string[]): boolean => admissionCodes.value.some(
  code => segments.some(segment => code.includes(segment)));
const checkState = (blocked: boolean): RunConsoleAdmissionCheck['state'] =>
  blocked ? 'blocked' : projection.admission?.allowed ? 'pass' :
    projection.phase === 'admitting' ? 'pending' : 'unknown';
const admissionChecks = computed<readonly RunConsoleAdmissionCheck[]>(() => [
  {
    key: 'revision',
    label: '保存修订',
    state: projection.admission?.persistenceRevision === projection.project?.persistenceRevision ? 'pass' :
      projection.phase === 'admitting' ? 'pending' : 'unknown',
    detail: projection.admission?.persistenceRevision == null
      ? '等待后端确认'
      : 'revision ' + projection.admission.persistenceRevision
  },
  {
    key: 'flow',
    label: '流程与判定身份',
    state: projection.admission?.canonicalFlowHash && projection.admission.decisionConfigurationHash
      ? 'pass' : checkState(hasCode('FLOW', 'DECISION')),
    detail: projection.admission?.canonicalFlowHash ? '已取得 canonical identity' : '等待权威身份'
  },
  {
    key: 'parameters',
    label: '必要参数',
    state: checkState(hasCode('PARAMETER', 'REQUIRED')),
    detail: hasCode('PARAMETER', 'REQUIRED') ? '存在未满足的必要参数' : '由 admission 校验'
  },
  {
    key: 'resources',
    label: '工程资源',
    state: checkState(hasCode('RESOURCE', 'ASSET', 'MISSING')),
    detail: hasCode('RESOURCE', 'ASSET', 'MISSING') ? '存在缺失资源' : '由 admission 校验'
  },
  {
    key: 'decision',
    label: '最终判定',
    state: checkState(hasCode('DECISION')),
    detail: hasCode('DECISION') ? '最终判定配置阻断运行' : '由 admission 校验'
  },
  {
    key: 'device',
    label: '采集设备',
    state: selectedCamera.value?.enabled && selectedCamera.value.connectionStatus === 'Connected'
      ? 'pass' : selectedCamera.value?.enabled ? 'unknown' : 'blocked',
    detail: selectedCamera.value
      ? selectedCamera.value.label + ' · ' + (selectedCamera.value.connectionStatus ?? '状态未知')
      : '未选择可用相机'
  },
  {
    key: 'package',
    label: '运行包',
    state: 'not-applicable',
    detail: 'Studio 连续检测使用已保存工程快照'
  }
]);
const violations = computed<readonly RunConsoleViolation[]>(() => (projection.admission?.violations ?? []).map(
  (item, index) => ({
    key: (item.code ?? 'ADMISSION') + '-' + index,
    code: item.code ?? projection.admission?.code ?? 'ADMISSION_REJECTED',
    message: item.reason,
    target: item.operatorName || item.parameterName
      ? [item.operatorName, item.parameterName].filter(Boolean).join(' · ')
      : null
  })));
const results = computed<readonly RunConsoleResultItem[]>(() => runState.recentResults.map(result => ({
  id: result.resultId,
  timestamp: result.timestamp,
  outcome: result.outcome,
  defectCount: result.defectCount,
  processingTimeMs: result.processingTimeMs,
  errorMessage: result.errorMessage,
  diagnostics: Object.freeze([
    ...flattenRunDiagnostics(result.analysisData, 'analysis'),
    ...flattenRunDiagnostics(result.outputData, 'output')
  ])
})));
function inspectionResultsLink(resultId?: string): string {
  return createLocalResultsDeepLink({
    projectId,
    ...(resultId ? { resultId } : {}),
    returnTo: `/projects/${encodeURIComponent(projectId)}/inspection`
  });
}

onMounted(() => owner.load());
onBeforeUnmount(() => {
  detachLeaveGuard();
  owner.dispose();
});
</script>

<template>
  <section
    class="inspection-run-page"
    data-testid="inspection-run-page"
  >
    <CvPageHeader
      title="连续检测"
      :description="projection.project ? projection.project.name + ' · 保存修订 ' + projection.project.persistenceRevision : '正在读取工程'"
    >
      <template #actions>
        <RouterLink :to="inspectionResultsLink()">
          查看检测结果
        </RouterLink>
      </template>
    </CvPageHeader>

    <CvPageState
      v-if="projection.phase === 'loading'"
      kind="loading"
      title="正在准备连续检测"
      description="正在读取工程、相机与运行权威状态。"
    />
    <CvPageState
      v-else-if="projection.phase === 'error' && !projection.project"
      kind="error"
      title="连续检测不可用"
      :description="projection.message"
    />
    <template v-else>
      <CvInlineAlert
        v-if="occupiedByOther"
        tone="warning"
        title="工程已被其他运行会话占用"
      >
        {{ runState.message }}
      </CvInlineAlert>
      <RunConsole
        mode="continuous"
        :project-name="projection.project?.name ?? projectId"
        :phase-label="phaseLabel"
        :tone="tone"
        :message="projection.message || runState.message"
        :error-code="projection.errorCode || runState.errorCode"
        :connected="runState.connected"
        :reconnect-attempt="runState.reconnectAttempt"
        :pending="pending"
        :can-start="canStart"
        :can-stop="canStop"
        :can-reconcile="canReconcile"
        :identity="identity"
        :admission="admissionChecks"
        :violations="violations"
        :statistics="runState.statistics"
        :results="results"
        start-test-id="inspection-start"
        stop-test-id="inspection-stop"
        latest-result-test-id="inspection-latest-result"
        @start="owner.start"
        @stop="owner.stop"
        @reconcile="run.reconcile"
        @refresh-admission="owner.refreshAdmission"
      >
        <template #configuration>
          <CvSelect
            label="相机"
            :model-value="projection.selectedCameraId ?? ''"
            :options="cameraOptions"
            :disabled="Boolean(runState.runtime?.isBusy) || pending"
            @update:model-value="owner.selectCamera($event || null)"
          />
        </template>
        <template #result-action="{ result }">
          <RouterLink
            :to="inspectionResultsLink(result.id)"
            data-testid="inspection-run-result-link"
          >
            查看结果
          </RouterLink>
        </template>
      </RunConsole>
    </template>
  </section>
</template>

<style scoped>
.inspection-run-page { width: 100%; max-width: 1540px; min-width: 0; display: grid; gap: var(--cv-density-page-gap); }
</style>
