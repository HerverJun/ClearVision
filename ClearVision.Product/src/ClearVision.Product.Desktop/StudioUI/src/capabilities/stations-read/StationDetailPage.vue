<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import {
  CvButton,
  CvDataTable,
  CvDescriptionList,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPanel,
  CvSelect,
  CvStatusBadge,
  type CvDataTableColumn,
  type CvDescriptionItem,
  type CvSelectOption
} from '@/design-system';
import { formatInspectionOutcome } from '@/shared/inspectionOutcome';
import {
  createStationAdminDetailsQuery,
  createStationHealthQuery,
  createStationResultsQuery,
  createStationsQuery
} from './stationQueries';
import {
  createStationQuerySlot,
  createVisibleStationPollingOwner
} from './stationLifecycleOwner';
import type {
  StationHealthSnapshot,
  StationResult
} from './stationContracts';
import {
  formatStationBytes,
  formatStationDateTime,
  formatStationDuration,
  stationDisplayName,
  stationOnlineLabel,
  stationOnlineTone,
  stationRuntimeLabel,
  stationRuntimeTone
} from './stationViewModel';
import {
  useStationsReadRuntime,
  type StationsReadRuntime
} from './stationsReadRuntime';

const props = defineProps<{
  stationId?: string;
  runtime?: StationsReadRuntime;
}>();

const route = useRoute();
const router = useRouter();
const runtime = useStationsReadRuntime(props.runtime);
const activeStationId = computed(() => props.stationId ?? String(route.params.stationId ?? ''));

function initialTake(): number {
  const candidate = Number(route.query.take);
  return [25, 50, 100].includes(candidate) ? candidate : 50;
}

const take = ref(initialTake());
const listSlot = createStationQuerySlot(() => createStationsQuery(runtime.queries));
const resultsSlot = createStationQuerySlot(() => createStationResultsQuery(
  runtime.queries,
  () => activeStationId.value,
  () => take.value
));
const healthSlot = createStationQuerySlot(() => createStationHealthQuery(
  runtime.queries,
  () => activeStationId.value,
  () => take.value
));
const adminSlot = createStationQuerySlot(() => createStationAdminDetailsQuery(
  runtime.queries,
  () => activeStationId.value
));

const listState = listSlot.state;
const resultsState = resultsSlot.state;
const healthState = healthSlot.state;
const adminState = adminSlot.state;
const station = computed(() => listState.value.data?.find(
  item => item.stationId.toLocaleLowerCase() === activeStationId.value.toLocaleLowerCase()
) ?? null);
const lastOutcome = computed(() => station.value?.lastOutcome
  ? formatInspectionOutcome(station.value.lastOutcome)
  : null);

async function refreshAll(): Promise<void> {
  await Promise.allSettled([
    listSlot.refresh({ force: true }),
    resultsSlot.refresh({ force: true }),
    healthSlot.refresh({ force: true }),
    adminSlot.refresh({ force: true })
  ]);
}

const polling = createVisibleStationPollingOwner({
  refresh: refreshAll,
  pause: () => {
    listSlot.pause();
    resultsSlot.pause();
    healthSlot.pause();
    adminSlot.pause();
  }
});

const takeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: '25', label: '最近 25 条' },
  { value: '50', label: '最近 50 条' },
  { value: '100', label: '最近 100 条' }
]);
const takeModel = computed({
  get: () => String(take.value),
  set: value => { take.value = Number(value); }
});

const resultColumns: readonly CvDataTableColumn<StationResult>[] = Object.freeze([
  { key: 'completedAtUtc', label: '完成时间', width: '20%' },
  { key: 'outcome', label: '结果', width: '13%' },
  { key: 'axes', label: 'Execution / Decision', width: '20%' },
  { key: 'packageName', label: '运行包', width: '17%' },
  { key: 'diagnosticCode', label: '诊断码', width: '15%' },
  { key: 'executionTimeMs', label: '耗时', align: 'end', width: '15%' }
]);

const healthColumns: readonly CvDataTableColumn<StationHealthSnapshot>[] = Object.freeze([
  { key: 'createdAtUtc', label: '采集时间', width: '21%' },
  { key: 'runtimeState', label: '运行状态', width: '14%' },
  { key: 'processUptimeSeconds', label: '进程运行时长', width: '16%' },
  { key: 'cpuUsagePercent', label: 'CPU', align: 'end', width: '10%' },
  { key: 'workingSetMb', label: '内存', align: 'end', width: '10%' },
  { key: 'diskFreeMb', label: '磁盘可用', align: 'end', width: '12%' },
  { key: 'spoolPendingCount', label: '待上报', align: 'end', width: '10%' }
]);

const ordinaryItems = computed<readonly CvDescriptionItem[]>(() => {
  const value = station.value;
  if (!value) return [];
  return [
    { key: 'station-id', label: 'Station ID', value: value.stationId, span: 2 },
    { key: 'machine', label: '机器名', value: value.machineName || '—' },
    { key: 'line', label: '产线', value: value.lineName || '—' },
    { key: 'last-seen', label: '最后心跳', value: formatStationDateTime(value.lastSeenAtUtc), span: 2 },
    { key: 'package', label: '当前运行包', value: value.packageName || '—' },
    { key: 'run', label: '当前 Run ID', value: value.currentRunId || '—' },
    { key: 'average', label: '平均执行耗时', value: `${value.averageExecutionTimeMs.toFixed(1)} ms` },
    { key: 'spool', label: '待上报结果', value: value.spoolPendingCount },
    { key: 'camera', label: '相机摘要', value: value.cameraStatusSummary || '—' },
    { key: 'plc', label: 'PLC 摘要', value: value.plcStatusSummary || '—' },
    { key: 'package-health', label: '运行包健康', value: value.currentPackageHealth || '—' },
    { key: 'diagnostic', label: '最近诊断', value: value.lastDiagnosticCode || '—' }
  ];
});

const adminItems = computed<readonly CvDescriptionItem[]>(() => {
  const value = adminState.value.data;
  if (!value) return [];
  return [
    { key: 'version', label: '客户端版本', value: value.clientVersion || '—' },
    { key: 'enabled', label: '启用状态', value: value.isEnabled ? '已启用' : '已禁用' },
    { key: 'area', label: '区域', value: value.areaName || '—' },
    { key: 'workcell', label: '工作单元', value: value.workcellName || '—' },
    { key: 'node', label: '检测节点', value: value.inspectionNodeName || '—' },
    { key: 'camera', label: '相机别名', value: value.cameraAlias || '—' },
    { key: 'role', label: 'Station 角色', value: value.stationRole || '—' },
    { key: 'owner', label: '负责人', value: value.owner || '—' },
    { key: 'revision', label: '工程修订', value: value.projectRevision ?? '—' },
    { key: 'mode', label: '执行模式', value: value.executionRunMode || '—' },
    { key: 'remark', label: '备注', value: value.remark || '—', span: 2 }
  ];
});

function resultPresentation(result: StationResult) {
  return formatInspectionOutcome(result.outcome);
}

async function changeTake(): Promise<void> {
  const nextQuery = { ...route.query };
  if (take.value === 50) delete nextQuery.take;
  else nextQuery.take = String(take.value);
  await router.replace({ query: nextQuery });
  await Promise.allSettled([
    resultsSlot.refresh({ force: true }),
    healthSlot.refresh({ force: true })
  ]);
}

watch(activeStationId, (next, previous) => {
  if (next !== previous) void refreshAll();
});

onMounted(() => polling.start());

onBeforeUnmount(() => {
  polling.dispose();
  listSlot.dispose();
  resultsSlot.dispose();
  healthSlot.dispose();
  adminSlot.dispose();
});
</script>

<template>
  <main
    class="station-detail"
    data-capability="stations-read-detail"
  >
    <CvPageHeader
      :title="station ? stationDisplayName(station) : 'Station 详情'"
      description="普通详情来自 /stations 列表；结果、health 与管理员增强信息分别保持独立权限和失败边界。"
    >
      <template #breadcrumbs>
        <RouterLink
          class="station-detail__back"
          to="/stations"
        >
          ← 返回 Station 列表
        </RouterLink>
      </template>
      <template #actions>
        <CvSelect
          v-model="takeModel"
          class="station-detail__take"
          label="明细数量"
          :options="takeOptions"
          @update:model-value="changeTake"
        />
        <CvButton
          size="sm"
          :loading="listState.isRefreshing || resultsState.isRefreshing || healthState.isRefreshing || adminState.isRefreshing"
          loading-label="正在刷新 Station 详情"
          @click="polling.refreshNow()"
        >
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <CvInlineAlert
      v-if="(listState.phase === 'stale' || listState.phase === 'partial-failure') && listState.data"
      tone="warning"
      title="普通详情刷新未完成"
    >
      当前显示上次成功读取的 Station 列表投影。
    </CvInlineAlert>
    <CvPageState
      v-if="listState.phase === 'loading' && !listState.data"
      kind="loading"
      title="正在读取 Station 普通详情"
    />
    <CvPageState
      v-else-if="listState.phase === 'unauthorized'"
      kind="unauthorized"
      title="当前会话不可用"
    />
    <CvPageState
      v-else-if="listState.phase === 'forbidden'"
      kind="forbidden"
      title="无权读取 Station 列表"
    />
    <CvPageState
      v-else-if="listState.phase === 'error' || listState.phase === 'not-found'"
      kind="error"
      title="Station 普通详情读取失败"
      :description="listState.failure?.message"
    />
    <CvPageState
      v-else-if="(listState.phase === 'success' || listState.phase === 'empty') && !station"
      kind="not-found"
      title="Station 不存在"
      description="普通 Station 列表中没有该 Station，管理员增强接口不会替代普通详情 authority。"
    />

    <div
      v-if="station"
      class="station-detail__grid"
    >
      <CvPanel
        title="普通详情"
        description="完全由普通 /stations 列表项构建。"
      >
        <div class="station-detail__status-row">
          <CvStatusBadge :tone="stationOnlineTone(station.onlineState)">
            {{ stationOnlineLabel(station.onlineState) }}
          </CvStatusBadge>
          <CvStatusBadge :tone="stationRuntimeTone(station.runtimeState)">
            {{ stationRuntimeLabel(station.runtimeState) }}
          </CvStatusBadge>
          <CvStatusBadge
            v-if="lastOutcome"
            :tone="lastOutcome.tone"
          >
            {{ lastOutcome.label }}
          </CvStatusBadge>
        </div>
        <CvDescriptionList
          :items="ordinaryItems"
          label="Station 普通详情"
        />
      </CvPanel>

      <CvPanel
        title="管理员增强信息"
        description="可选 GET 增强区；403 仅降级本区域。"
      >
        <CvInlineAlert
          v-if="(adminState.phase === 'stale' || adminState.phase === 'partial-failure') && adminState.data"
          tone="warning"
          title="增强信息刷新未完成"
        >
          当前显示上次成功读取的管理员增强信息。
        </CvInlineAlert>
        <CvPageState
          v-if="adminState.phase === 'loading' && !adminState.data"
          compact
          kind="loading"
          title="正在读取管理员增强信息"
        />
        <CvPageState
          v-else-if="adminState.phase === 'forbidden'"
          compact
          kind="forbidden"
          title="管理员增强信息不可用"
          description="当前账号没有 StationAdmin 权限；普通详情、结果与 health 仍可继续使用。"
        />
        <CvPageState
          v-else-if="adminState.phase === 'unauthorized'"
          compact
          kind="unauthorized"
          title="管理员增强信息需要有效会话"
        />
        <CvPageState
          v-else-if="adminState.phase === 'not-found'"
          compact
          kind="empty"
          title="暂无管理员增强信息"
        />
        <CvPageState
          v-else-if="adminState.phase === 'error'"
          compact
          kind="error"
          title="管理员增强信息读取失败"
          :description="adminState.failure?.message"
        />
        <CvDescriptionList
          v-if="adminState.data"
          :items="adminItems"
          label="Station 管理员增强信息"
        />
      </CvPanel>
    </div>

    <CvPanel
      title="最近结果"
      description="严格保留 Execution / Decision 双轴；legacy payload 仅按当前后端映射读取。"
    >
      <CvInlineAlert
        v-if="resultsState.isRefreshing && resultsState.data"
        tone="info"
      >
        正在刷新结果，暂时显示上次读取的数据。
      </CvInlineAlert>
      <CvInlineAlert
        v-if="(resultsState.phase === 'stale' || resultsState.phase === 'partial-failure') && resultsState.data"
        tone="warning"
        title="结果刷新未完成"
      >
        当前显示上次成功读取的 Station 结果。
      </CvInlineAlert>
      <CvPageState
        v-if="resultsState.phase === 'loading' && !resultsState.data"
        compact
        kind="loading"
        title="正在读取 Station 结果"
      />
      <CvPageState
        v-else-if="resultsState.phase === 'empty'"
        compact
        kind="empty"
        title="暂无 Station 结果"
      />
      <CvPageState
        v-else-if="resultsState.phase === 'unauthorized'"
        compact
        kind="unauthorized"
        title="当前会话不可用"
      />
      <CvPageState
        v-else-if="resultsState.phase === 'forbidden'"
        compact
        kind="forbidden"
        title="无权读取 Station 结果"
      />
      <CvPageState
        v-else-if="resultsState.phase === 'error' || resultsState.phase === 'not-found'"
        compact
        kind="error"
        title="Station 结果读取失败"
        :description="resultsState.failure?.message"
      />
      <CvDataTable
        v-if="resultsState.data?.length"
        :rows="resultsState.data"
        :columns="resultColumns"
        :row-key="row => `${row.stationId}:${row.sequenceId}:${row.messageId}`"
        caption="Station 最近结果"
        :busy="resultsState.isRefreshing"
      >
        <template #cell-completedAtUtc="{ row }">
          {{ formatStationDateTime(row.completedAtUtc) }}
        </template>
        <template #cell-outcome="{ row }">
          <div class="station-detail__outcome">
            <CvStatusBadge :tone="resultPresentation(row).tone">
              {{ resultPresentation(row).label }}
            </CvStatusBadge>
            <span v-if="row.legacyOutcomeProjection">legacy projection</span>
          </div>
        </template>
        <template #cell-axes="{ row }">
          {{ resultPresentation(row).executionLabel }} / {{ resultPresentation(row).decisionLabel }}
        </template>
        <template #cell-packageName="{ row }">
          {{ row.packageName || '—' }}
        </template>
        <template #cell-diagnosticCode="{ row }">
          {{ row.diagnosticCode || '—' }}
        </template>
        <template #cell-executionTimeMs="{ row }">
          {{ row.executionTimeMs }} ms
        </template>
      </CvDataTable>
    </CvPanel>

    <CvPanel
      title="Health"
      description="低频健康快照；不读取日志、命令或 SSE。"
    >
      <CvInlineAlert
        v-if="healthState.isRefreshing && healthState.data"
        tone="info"
      >
        正在刷新 health，暂时显示上次读取的数据。
      </CvInlineAlert>
      <CvInlineAlert
        v-if="(healthState.phase === 'stale' || healthState.phase === 'partial-failure') && healthState.data"
        tone="warning"
        title="Health 刷新未完成"
      >
        当前显示上次成功读取的 health 快照。
      </CvInlineAlert>
      <CvPageState
        v-if="healthState.phase === 'loading' && !healthState.data"
        compact
        kind="loading"
        title="正在读取 Station health"
      />
      <CvPageState
        v-else-if="healthState.phase === 'empty'"
        compact
        kind="empty"
        title="暂无 health 快照"
      />
      <CvPageState
        v-else-if="healthState.phase === 'unauthorized'"
        compact
        kind="unauthorized"
        title="当前会话不可用"
      />
      <CvPageState
        v-else-if="healthState.phase === 'forbidden'"
        compact
        kind="forbidden"
        title="无权读取 Station health"
      />
      <CvPageState
        v-else-if="healthState.phase === 'error' || healthState.phase === 'not-found'"
        compact
        kind="error"
        title="Station health 读取失败"
        :description="healthState.failure?.message"
      />
      <CvDataTable
        v-if="healthState.data?.length"
        :rows="healthState.data"
        :columns="healthColumns"
        :row-key="row => `${row.stationId}:${row.sequenceId}:${row.messageId}`"
        caption="Station health 快照"
        :busy="healthState.isRefreshing"
      >
        <template #cell-createdAtUtc="{ row }">
          {{ formatStationDateTime(row.createdAtUtc) }}
        </template>
        <template #cell-runtimeState="{ row }">
          <CvStatusBadge :tone="stationRuntimeTone(row.runtimeState)">
            {{ stationRuntimeLabel(row.runtimeState) }}
          </CvStatusBadge>
        </template>
        <template #cell-processUptimeSeconds="{ row }">
          {{ formatStationDuration(row.processUptimeSeconds) }}
        </template>
        <template #cell-cpuUsagePercent="{ row }">
          {{ row.cpuUsagePercent === null ? '—' : `${row.cpuUsagePercent.toFixed(1)}%` }}
        </template>
        <template #cell-workingSetMb="{ row }">
          {{ row.workingSetMb }} MB
        </template>
        <template #cell-diskFreeMb="{ row }">
          {{ row.diskFreeMb }} MB
        </template>
        <template #cell-spoolPendingCount="{ row }">
          <span :title="`Spool 大小 ${formatStationBytes(row.spoolBytes)}`">
            {{ row.spoolPendingCount }}
          </span>
        </template>
      </CvDataTable>
    </CvPanel>
  </main>
</template>

<style scoped>
.station-detail { display: grid; min-width: 0; gap: var(--cv-space-5); }
.station-detail__back { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); text-decoration: none; }
.station-detail__back:hover { color: var(--cv-color-link); text-decoration: underline; }
.station-detail__take { min-width: 150px; }
.station-detail__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); align-items: start; }
.station-detail__status-row { display: flex; flex-wrap: wrap; gap: var(--cv-space-2); margin-bottom: var(--cv-space-4); }
.station-detail__outcome { display: grid; justify-items: start; gap: var(--cv-space-1); }
.station-detail__outcome span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
@media (max-width: 860px) { .station-detail__grid { grid-template-columns: 1fr; } }
</style>
