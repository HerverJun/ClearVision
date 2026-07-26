<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { useProductRuntime } from '@/app/productRuntime';
import { isProjectId } from '@/capabilities/projects-read/projectContracts';
import { createInspectionRunOwner } from './inspectionRunOwner';
import { createInspectionRunApiAdapter } from './realtimeApiAdapter';
import { createInspectionSseAdapter } from './sseAdapter';
import { createInspectionRunPageOwner } from './inspectionRunPageOwner';
import { CvButton, CvDescriptionList, CvInlineAlert, CvPageHeader, CvPageState, CvPanel, CvSelect, CvStatusBadge, type CvSelectOption } from '@/design-system';

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
const canStart = computed(() => projection.phase === 'ready' && !runState.runtime?.isBusy && Boolean(projection.project));
const canStop = computed(() => isContinuous.value && runState.runtime?.isBusy === true && projection.phase !== 'stopping');
const cameraOptions = computed<readonly CvSelectOption[]>(() => projection.cameras.map(camera => ({
  value: camera.id,
  label: camera.connectionStatus
    ? `${camera.label} · ${camera.connectionStatus === 'Connected' ? '已连接' : camera.connectionStatus}`
    : camera.label,
  disabled: !camera.enabled
})));
const stateTone = computed(() => occupiedByOther.value ? 'warning' : runState.runtime?.status === 'Faulted' ? 'error' :
  runState.runtime?.isBusy ? 'info' : 'idle');
const stateLabels = Object.freeze({
  Idle: '未运行', Starting: '启动中', Running: '运行中', Stopping: '停止中', Stopped: '已停止', Faulted: '运行故障'
});
const stateLabel = computed(() => occupiedByOther.value ? '正式运行占用' :
  stateLabels[runState.runtime?.status ?? 'Idle']);
const sessionTypeLabel = computed(() => {
  if (runState.runtime?.sessionType === 'ContinuousInspection') return '连续检测';
  if (runState.runtime?.sessionType === 'WorkspaceFormalRun') return '正式运行';
  if (runState.runtime?.sessionType === 'LegacyRealtime') return '兼容实时运行';
  return '未运行';
});
const dateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit'
});
const identityItems = computed(() => [
  { key: 'project', label: '工程', value: projection.project?.name ?? projectId },
  { key: 'revision', label: '保存修订', value: String(projection.project?.persistenceRevision ?? '—') },
  { key: 'snapshot', label: '快照', value: runState.runtime?.clientSnapshotId ?? '启动时由后端 admission 确认' },
  { key: 'flow-hash', label: '流程哈希', value: runState.runtime?.canonicalFlowHash ?? '—' },
  { key: 'session-type', label: '会话类型', value: sessionTypeLabel.value }
]);

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
      :description="projection.project ? `${projection.project.name} · 保存修订 ${projection.project.persistenceRevision}` : '读取工程中'"
    >
      <template #actions>
        <RouterLink to="/results">
          查看检测结果
        </RouterLink>
      </template>
    </CvPageHeader>

    <CvPageState
      v-if="projection.phase === 'loading'"
      kind="loading"
      title="正在准备连续检测"
      description="读取工程、相机与运行权威状态。"
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
        title="工程正在正式运行"
      >
        当前会话不属于连续检测。本页不会挂载停止权限，可直接离开或等待正式运行结束。
      </CvInlineAlert>
      <CvInlineAlert
        v-else-if="projection.errorCode || runState.errorCode"
        tone="error"
        title="连续检测操作未完成"
      >
        {{ projection.message || runState.message }}
        <span v-if="projection.errorCode || runState.errorCode">（{{ projection.errorCode || runState.errorCode }}）</span>
      </CvInlineAlert>

      <div class="inspection-run-page__layout">
        <CvPanel
          title="运行控制"
          description="只运行后端确认的保存快照。"
        >
          <div class="inspection-run-page__status">
            <CvStatusBadge
              :tone="stateTone"
              :label="stateLabel"
            />
            <span>{{ runState.connected ? '实时连接已建立' : isContinuous ? '实时连接恢复中' : '未连接' }}</span>
          </div>
          <CvSelect
            label="相机"
            :model-value="projection.selectedCameraId ?? ''"
            :options="cameraOptions"
            :disabled="Boolean(runState.runtime?.isBusy) || projection.phase === 'starting'"
            @update:model-value="owner.selectCamera($event || null)"
          />
          <div class="inspection-run-page__actions">
            <CvButton
              variant="primary"
              :disabled="!canStart"
              :loading="projection.phase === 'starting'"
              data-testid="inspection-start"
              @click="owner.start"
            >
              启动连续检测
            </CvButton>
            <CvButton
              variant="danger"
              :disabled="!canStop"
              :loading="projection.phase === 'stopping'"
              data-testid="inspection-stop"
              @click="owner.stop"
            >
              停止连续检测
            </CvButton>
          </div>
          <p
            class="inspection-run-page__message"
            role="status"
            aria-live="polite"
          >
            {{ runState.message }}
          </p>
        </CvPanel>

        <CvPanel title="工程与保存快照身份">
          <CvDescriptionList :items="identityItems" />
        </CvPanel>
      </div>

      <CvPanel
        title="最近检测结果"
        description="实时流仅接受当前权威会话的事件。"
      >
        <CvPageState
          v-if="!runState.latestResult"
          kind="empty"
          title="暂无本会话结果"
          description="启动连续检测后，最新结果会显示在这里。"
        />
        <div
          v-else
          class="inspection-run-page__result"
          data-testid="inspection-latest-result"
        >
          <strong>{{ runState.latestResult.status }}</strong>
          <span>判定 {{ runState.latestResult.decisionOutcome ?? '未判定' }}</span>
          <span>缺陷 {{ runState.latestResult.defectCount }}</span>
          <span>{{ runState.latestResult.processingTimeMs }} ms</span>
          <span>{{ dateTimeFormatter.format(new Date(runState.latestResult.timestamp)) }}</span>
          <span v-if="runState.latestResult.errorMessage">{{ runState.latestResult.errorMessage }}</span>
        </div>
      </CvPanel>
    </template>
  </section>
</template>

<style scoped>
.inspection-run-page { display: grid; max-width: 1380px; gap: var(--cv-density-page-gap); }
.inspection-run-page__layout { display: grid; grid-template-columns: minmax(360px, 0.85fr) minmax(420px, 1.15fr); gap: var(--cv-space-4); align-items: start; }
.inspection-run-page__status { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); margin-bottom: var(--cv-space-4); color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.inspection-run-page__actions { display: flex; gap: var(--cv-space-2); margin-top: var(--cv-space-4); }
.inspection-run-page__message { margin: var(--cv-space-3) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.inspection-run-page__result { display: flex; flex-wrap: wrap; align-items: baseline; gap: var(--cv-space-3); padding: 0 var(--cv-density-panel-padding) var(--cv-density-panel-padding); color: var(--cv-text-secondary); }
.inspection-run-page__result strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-lg); }
@media (max-width: 960px) { .inspection-run-page__layout { grid-template-columns: 1fr; } }
</style>
