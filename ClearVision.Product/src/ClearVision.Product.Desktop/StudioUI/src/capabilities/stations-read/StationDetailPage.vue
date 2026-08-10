<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, shallowRef, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import {
  CvDataTable,
  CvDescriptionList,
  CvIcon,
  CvIconButton,
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
  createStationDetailDeepLink,
  createStationResultsDeepLink,
  resolveProductionReturnTo
} from '@/shared/productionTraceLinks';
import {
  createStationAdminDetailsQuery,
  createStationAuditsQuery,
  createStationCommandsQuery,
  createStationHealthQuery,
  createStationLogsQuery,
  createStationPackagesQuery,
  createStationResultsQuery,
  createStationsQuery
} from './stationQueries';
import {
  createStationMonitoringOwner,
  createStationQuerySlot,
  type StationAuthorityRefreshRequest
} from './stationLifecycleOwner';
import { createStationSseAdapter } from './stationSseAdapter';
import { createStationAdminCommandOwner } from './stationAdminCommandOwner';
import StationAdminPanel from './StationAdminPanel.vue';
import StationProductionTrace from './StationProductionTrace.vue';
import type { StationAdminEvidenceState } from './stationProductionTrace';
import type {
  StationHealthSnapshot,
  StationResult
} from './stationContracts';
import {
  formatStationBytes,
  formatStationDateTime,
  formatStationDuration,
  formatStationReportedStatus,
  stationDisplayName,
  stationOfflineReasonLabel,
  stationOnlineLabel,
  stationOnlineTone,
  stationPackageHealthLabel,
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
const isStationAdmin = runtime.session?.projection.user?.role === 'Admin';
const canOpenWorkspace = ['Admin', 'Engineer'].includes(runtime.session?.projection.user?.role ?? '');
const returnTarget = computed(() => {
  const value = route.query.returnTo;
  return resolveProductionReturnTo(typeof value === 'string' ? value : null);
});
const returnLabel = computed(() => returnTarget.value?.startsWith('/results')
  ? '返回检测结果'
  : '返回工作站列表');

function initialTake(): number {
  const candidate = Number(route.query.take);
  return [25, 50, 100].includes(candidate) ? candidate : 50;
}

const take = shallowRef(initialTake());
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
const adminSlot = isStationAdmin ? createStationQuerySlot(() => createStationAdminDetailsQuery(
  runtime.queries, () => activeStationId.value
)) : undefined;
const logsSlot = isStationAdmin ? createStationQuerySlot(() => createStationLogsQuery(
  runtime.queries, () => activeStationId.value, () => take.value
)) : undefined;
const commandsSlot = isStationAdmin ? createStationQuerySlot(() => createStationCommandsQuery(
  runtime.queries, () => activeStationId.value, () => take.value
)) : undefined;
const auditsSlot = isStationAdmin ? createStationQuerySlot(() => createStationAuditsQuery(
  runtime.queries, () => activeStationId.value, () => take.value
)) : undefined;
const packagesSlot = isStationAdmin ? createStationQuerySlot(() => createStationPackagesQuery(runtime.queries)) : undefined;
const adminCommandOwner = isStationAdmin && runtime.api ? createStationAdminCommandOwner({
  api: runtime.api,
  stationId: () => activeStationId.value
}) : undefined;

const listState = listSlot.state;
const resultsState = resultsSlot.state;
const healthState = healthSlot.state;
const adminState = adminSlot?.state ?? null;
const logsState = logsSlot?.state ?? null;
const commandsState = commandsSlot?.state ?? null;
const auditsState = auditsSlot?.state ?? null;
const packagesState = packagesSlot?.state ?? null;
const station = computed(() => listState.value.data?.find(
  item => item.stationId.toLocaleLowerCase() === activeStationId.value.toLocaleLowerCase()
) ?? null);
const lastOutcome = computed(() => station.value?.lastOutcome
  ? formatInspectionOutcome(station.value.lastOutcome)
  : null);
const adminEvidence = computed<StationAdminEvidenceState>(() => {
  if (!isStationAdmin) return 'restricted';
  const states = [commandsState?.value, auditsState?.value, packagesState?.value];
  if (states.some(state => state?.phase === 'loading' || state?.phase === 'idle')) return 'loading';
  if (states.every(state => state && ['success', 'empty', 'stale', 'partial-failure'].includes(state.phase))) {
    return 'available';
  }
  return 'unavailable';
});

async function refreshAll(): Promise<void> {
  const refreshes: Promise<unknown>[] = [
    listSlot.refresh({ force: true }),
    resultsSlot.refresh({ force: true }),
    healthSlot.refresh({ force: true })
  ];
  for (const slot of [adminSlot, logsSlot, commandsSlot, auditsSlot, packagesSlot]) {
    if (slot) refreshes.push(slot.refresh({ force: true }));
  }
  await Promise.allSettled(refreshes);
}

async function refreshAdmin(): Promise<void> {
  const refreshes = [adminSlot, logsSlot, commandsSlot, auditsSlot, packagesSlot]
    .filter(slot => slot !== undefined)
    .map(slot => slot.refresh({ force: true }));
  await Promise.allSettled(refreshes);
}

async function refreshAuthority(request: StationAuthorityRefreshRequest): Promise<void> {
  const full = request.reason !== 'event' && request.reason !== 'heartbeat';
  const events = request.events.filter(event => !event.stationId ||
    event.stationId.toLocaleLowerCase() === activeStationId.value.toLocaleLowerCase());
  const eventTypes = new Set(events.map(event => event.type));
  const refreshes: Promise<unknown>[] = [];
  if (full || request.reason === 'heartbeat' || eventTypes.has('stationUpserted') ||
      eventTypes.has('stationHealthUpdated') || eventTypes.has('stationResultAdded')) {
    refreshes.push(listSlot.refresh({ force: true }));
  }
  if (full || eventTypes.has('stationResultAdded')) {
    refreshes.push(resultsSlot.refresh({ force: true }));
  }
  if (full || eventTypes.has('stationHealthUpdated')) {
    refreshes.push(healthSlot.refresh({ force: true }));
  }
  if (full) {
    for (const slot of [adminSlot, logsSlot, commandsSlot, auditsSlot, packagesSlot]) {
      if (slot) refreshes.push(slot.refresh({ force: true }));
    }
  } else {
    if (eventTypes.has('stationLogAdded') && logsSlot) {
      refreshes.push(logsSlot.refresh({ force: true }));
    }
    if (eventTypes.has('stationCommandUpdated')) {
      if (commandsSlot) refreshes.push(commandsSlot.refresh({ force: true }));
      if (auditsSlot) refreshes.push(auditsSlot.refresh({ force: true }));
    }
  }
  await Promise.allSettled(refreshes);
}

const monitoring = createStationMonitoringOwner({
  stream: runtime.api?.getTextStream ? createStationSseAdapter(runtime.api) : undefined,
  refreshAuthority,
  pauseAuthority: () => {
    listSlot.pause();
    resultsSlot.pause();
    healthSlot.pause();
    adminSlot?.pause();
    logsSlot?.pause();
    commandsSlot?.pause();
    auditsSlot?.pause();
    packagesSlot?.pause();
  }
});
const monitoringState = monitoring.state;
const monitoringLabel = computed(() => {
  switch (monitoringState.value.phase) {
    case 'live': return '实时连接';
    case 'recovering': return '连接恢复中';
    case 'recovery-polling': return '恢复轮询';
    case 'paused': return '监控已暂停';
    case 'unauthorized': return '会话已失效';
    case 'disposed': return '监控已停止';
    default: return '正在连接';
  }
});
const monitoringTone = computed(() => monitoringState.value.phase === 'live'
  ? 'ok'
  : monitoringState.value.phase === 'unauthorized'
    ? 'ng'
    : 'warning');

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
  { key: 'completedAtUtc', label: '完成时间', width: '18%' },
  { key: 'outcome', label: '结果', width: '13%' },
  { key: 'axes', label: '执行状态 / 判定结果', width: '18%' },
  { key: 'packageName', label: '运行包', width: '15%' },
  { key: 'diagnosticCode', label: '诊断', width: '14%' },
  { key: 'executionTimeMs', label: '耗时', align: 'end', width: '10%' },
  { key: 'actions', label: '操作', align: 'end', width: '12%' }
]);

const healthColumns: readonly CvDataTableColumn<StationHealthSnapshot>[] = Object.freeze([
  { key: 'createdAtUtc', label: '采集时间', width: '18%' },
  { key: 'runtimeState', label: '运行状态', width: '12%' },
  { key: 'processUptimeSeconds', label: '进程运行时长', width: '15%' },
  { key: 'resources', label: '资源', width: '20%' },
  { key: 'fieldStatus', label: '设备 / 运行包', width: '23%' },
  { key: 'spoolPendingCount', label: '待同步', align: 'end', width: '12%' }
]);

const operationalItems = computed<readonly CvDescriptionItem[]>(() => {
  const value = station.value;
  if (!value) return [];
  return [
    { key: 'connection', label: '连接状态', value: value.isOnline ? '在线' : (stationOfflineReasonLabel(value.offlineReason) ?? '离线') },
    { key: 'enabled', label: '启用状态', value: value.isEnabled ? '已启用' : '已停用' },
    { key: 'machine', label: '机器名', value: value.machineName || '—' },
    { key: 'line', label: '产线', value: value.lineName || '—' },
    { key: 'last-seen', label: '最后心跳', value: formatStationDateTime(value.lastSeenAtUtc), span: 2 },
    { key: 'package', label: '当前运行包', value: value.packageName ? `${value.packageName}${value.packageVersion ? ` · ${value.packageVersion}` : ''}` : '未激活运行包' },
    { key: 'package-health', label: '运行包状态', value: stationPackageHealthLabel(value.currentPackageHealth, value.packageId) },
    { key: 'average', label: '平均执行耗时', value: `${value.averageExecutionTimeMs.toFixed(1)} ms` },
    { key: 'sync', label: '结果同步', value: value.spoolPendingCount > 0 ? `等待同步 ${value.spoolPendingCount} 条 · ${formatStationBytes(value.spoolBytes)}` : '同步正常' },
    { key: 'camera', label: '相机', value: formatStationReportedStatus(value.cameraStatusSummary) },
    { key: 'plc', label: 'PLC', value: formatStationReportedStatus(value.plcStatusSummary) },
    { key: 'tcp', label: 'TCP', value: '未上报/不可确认' },
    { key: 'diagnostic', label: '最近诊断', value: value.lastDiagnosticMessage || value.lastDiagnosticCode || '无已上报诊断', span: 2 }
  ];
});

const technicalItems = computed<readonly CvDescriptionItem[]>(() => {
  const value = station.value;
  if (!value) return [];
  return [
    { key: 'station-id', label: '工作站标识', value: value.stationId, span: 2 },
    { key: 'package-id', label: '运行包标识', value: value.packageId || '未上报' },
    { key: 'source-project', label: '来源工程标识', value: value.sourceProjectId || '未上报' },
    { key: 'source-revision', label: '来源工程修订', value: value.sourceProjectRevision === null ? '未上报' : `r${value.sourceProjectRevision}` },
    { key: 'project-revision', label: '执行工程修订', value: value.projectRevision === null ? '未上报' : `r${value.projectRevision}` },
    { key: 'package-flow', label: '运行包流程哈希', value: value.packageFlowHash || '未上报', span: 2 },
    { key: 'execution-flow', label: '执行流程哈希', value: value.executionFlowHash || '未上报', span: 2 },
    { key: 'decision', label: '判定配置哈希', value: value.decisionConfigurationHash || '未上报', span: 2 },
    { key: 'snapshot', label: '执行快照标识', value: value.executionSnapshotId || '未上报', span: 2 },
    { key: 'run', label: '当前运行标识', value: value.currentRunId || '无活动运行' },
    { key: 'mode', label: '运行模式', value: value.executionRunMode || '未上报' }
  ];
});

function resultPresentation(result: StationResult) {
  return formatInspectionOutcome(result.outcome);
}

function stationReturnPath(): string {
  const fleetReturnTo = returnTarget.value === '/stations' || returnTarget.value?.startsWith('/stations?')
    ? returnTarget.value
    : null;
  return createStationDetailDeepLink(activeStationId.value, fleetReturnTo);
}

function stationResultLink(result: StationResult): string {
  return createStationResultsDeepLink({
    stationId: result.stationId,
    resultId: result.messageId,
    returnTo: stationReturnPath()
  });
}

const stationResultsLink = computed(() => createStationResultsDeepLink({
  stationId: activeStationId.value,
  returnTo: stationReturnPath()
}));

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

onMounted(() => monitoring.start());

onBeforeUnmount(() => {
  monitoring.dispose();
  listSlot.dispose();
  resultsSlot.dispose();
  healthSlot.dispose();
  adminSlot?.dispose();
  logsSlot?.dispose();
  commandsSlot?.dispose();
  auditsSlot?.dispose();
  packagesSlot?.dispose();
  adminCommandOwner?.dispose();
});
</script>

<template>
  <section
    class="station-detail"
    data-capability="stations-read-detail"
  >
    <CvPageHeader
      :title="station ? stationDisplayName(station) : '工作站详情'"
      description="核对连接、运行包、最近结果与现场健康；管理操作仅对工作站管理员开放。"
    >
      <template #breadcrumbs>
        <RouterLink
          class="station-detail__back"
          :to="returnTarget ?? '/stations'"
        >
          <CvIcon
            name="chevron-left"
            size="sm"
          />
          {{ returnLabel }}
        </RouterLink>
      </template>
      <template #actions>
        <RouterLink
          class="station-detail__nav-link"
          :to="stationResultsLink"
          data-testid="station-open-results"
        >
          查看结果
        </RouterLink>
        <CvSelect
          v-model="takeModel"
          class="station-detail__take"
          name="stationDetailTake"
          label="明细数量"
          :options="takeOptions"
          @update:model-value="changeTake"
        />
        <CvIconButton
          size="sm"
          variant="secondary"
          label="刷新工作站详情"
          :loading="Boolean(listState.isRefreshing || resultsState.isRefreshing || healthState.isRefreshing || adminState?.isRefreshing || logsState?.isRefreshing || commandsState?.isRefreshing)"
          @click="monitoring.refreshNow()"
        >
          <CvIcon
            name="refresh"
            size="sm"
          />
        </CvIconButton>
      </template>
      <template
        v-if="station"
        #meta
      >
        <CvStatusBadge :tone="stationOnlineTone(station.onlineState)">
          {{ stationOnlineLabel(station.onlineState) }}
        </CvStatusBadge>
        <CvStatusBadge :tone="monitoringTone">
          {{ monitoringLabel }}
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
      </template>
    </CvPageHeader>

    <CvInlineAlert
      v-if="monitoringState.phase === 'recovering'"
      tone="warning"
      title="实时连接正在恢复"
    >
      当前保留上次成功读取；恢复期间按服务端状态重新同步。
    </CvInlineAlert>

    <CvInlineAlert
      v-if="(listState.phase === 'stale' || listState.phase === 'partial-failure') && listState.data"
      tone="warning"
      title="普通详情刷新未完成"
    >
      当前显示上次成功读取的工作站列表数据。
    </CvInlineAlert>
    <CvPageState
      v-if="listState.phase === 'loading' && !listState.data"
      kind="loading"
      title="正在读取工作站详情"
    />
    <CvPageState
      v-else-if="listState.phase === 'unauthorized'"
      kind="unauthorized"
      title="当前会话不可用"
    />
    <CvPageState
      v-else-if="listState.phase === 'forbidden'"
      kind="forbidden"
      title="无权读取工作站列表"
    />
    <CvPageState
      v-else-if="listState.phase === 'error' || listState.phase === 'not-found'"
      kind="error"
      title="工作站详情读取失败"
      :description="listState.failure?.message"
    />
    <CvPageState
      v-else-if="(listState.phase === 'success' || listState.phase === 'empty') && !station"
      kind="not-found"
      title="工作站不存在"
      description="工作站列表中没有该记录；管理信息不会替代普通详情。"
    />

    <div
      v-if="station"
      class="station-detail__grid"
    >
      <CvPanel
        class="station-detail__overview-panel"
        title="状态概览"
        :padded="false"
      >
        <CvDescriptionList
          :items="operationalItems"
          :columns="1"
          label="工作站运行概览"
        />
        <details class="station-detail__technical-details">
          <summary>技术身份</summary>
          <CvDescriptionList
            :items="technicalItems"
            :columns="1"
            label="工作站技术身份"
          />
        </details>
      </CvPanel>
    </div>

    <CvPanel
      class="station-detail__results-panel"
      title="最近结果"
      :padded="false"
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
        当前显示上次成功读取的工作站结果。
      </CvInlineAlert>
      <CvPageState
        v-if="resultsState.phase === 'loading' && !resultsState.data"
        compact
        kind="loading"
        title="正在读取工作站结果"
      />
      <CvPageState
        v-else-if="resultsState.phase === 'empty'"
        compact
        kind="empty"
        title="暂无工作站结果"
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
        title="无权读取工作站结果"
      />
      <CvPageState
        v-else-if="resultsState.phase === 'error' || resultsState.phase === 'not-found'"
        compact
        kind="error"
        title="工作站结果读取失败"
        :description="resultsState.failure?.message"
      />
      <CvDataTable
        v-if="resultsState.data?.length"
        :rows="resultsState.data"
        :columns="resultColumns"
        :row-key="row => `${row.stationId}:${row.sequenceId}:${row.messageId}`"
        caption="工作站最近结果"
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
            <span v-if="row.legacyOutcomeProjection">旧版结果映射</span>
          </div>
        </template>
        <template #cell-axes="{ row }">
          {{ resultPresentation(row).executionLabel }} / {{ resultPresentation(row).decisionLabel }}
        </template>
        <template #cell-packageName="{ row }">
          {{ row.packageName || '—' }}
        </template>
        <template #cell-diagnosticCode="{ row }">
          <div class="station-detail__diagnostic">
            <span>{{ row.diagnosticMessage || '无诊断说明' }}</span>
            <code v-if="row.diagnosticCode">{{ row.diagnosticCode }}</code>
          </div>
        </template>
        <template #cell-executionTimeMs="{ row }">
          {{ row.executionTimeMs }} ms
        </template>
        <template #cell-actions="{ row }">
          <RouterLink
            class="station-detail__nav-link"
            :to="stationResultLink(row)"
            data-testid="station-result-link"
          >
            追溯
          </RouterLink>
        </template>
      </CvDataTable>
    </CvPanel>

    <StationProductionTrace
      v-if="station"
      class="station-detail__production-trace"
      :station="station"
      :results="resultsState.data ?? []"
      :commands="commandsState?.data ?? []"
      :audits="auditsState?.data ?? []"
      :packages="packagesState?.data ?? []"
      :admin-evidence="adminEvidence"
      :can-open-project="canOpenWorkspace"
    />

    <CvPanel
      class="station-detail__health-panel"
      title="健康快照"
      :padded="false"
    >
      <CvInlineAlert
        v-if="healthState.isRefreshing && healthState.data"
        tone="info"
      >
        正在刷新健康快照，暂时显示上次读取的数据。
      </CvInlineAlert>
      <CvInlineAlert
        v-if="(healthState.phase === 'stale' || healthState.phase === 'partial-failure') && healthState.data"
        tone="warning"
        title="健康快照刷新未完成"
      >
        当前显示上次成功读取的健康快照。
      </CvInlineAlert>
      <CvPageState
        v-if="healthState.phase === 'loading' && !healthState.data"
        compact
        kind="loading"
        title="正在读取工作站健康快照"
      />
      <CvPageState
        v-else-if="healthState.phase === 'empty'"
        compact
        kind="empty"
        title="暂无健康快照"
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
        title="无权读取工作站健康快照"
      />
      <CvPageState
        v-else-if="healthState.phase === 'error' || healthState.phase === 'not-found'"
        compact
        kind="error"
        title="工作站健康快照读取失败"
        :description="healthState.failure?.message"
      />
      <CvDataTable
        v-if="healthState.data?.length"
        :rows="healthState.data"
        :columns="healthColumns"
        :row-key="row => `${row.stationId}:${row.sequenceId}:${row.messageId}`"
        caption="工作站健康快照"
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
        <template #cell-resources="{ row }">
          <div class="station-detail__health-stack">
            <span>CPU {{ row.cpuUsagePercent === null ? '未上报' : `${row.cpuUsagePercent.toFixed(1)}%` }}</span>
            <small>内存 {{ row.workingSetMb }} MB · 磁盘可用 {{ row.diskFreeMb }} MB</small>
          </div>
        </template>
        <template #cell-fieldStatus="{ row }">
          <div class="station-detail__health-stack">
            <span>相机 {{ formatStationReportedStatus(row.cameraStatusSummary) }} · PLC {{ formatStationReportedStatus(row.plcStatusSummary) }}</span>
            <small>{{ stationPackageHealthLabel(row.currentPackageHealth, row.currentPackageId) }}</small>
            <small
              v-if="row.lastErrorMessage || row.lastErrorCode"
              class="station-detail__health-error"
            >
              {{ row.lastErrorMessage || row.lastErrorCode }}
            </small>
          </div>
        </template>
        <template #cell-spoolPendingCount="{ row }">
          <span :title="`待同步数据 ${formatStationBytes(row.spoolBytes)}`">
            {{ row.spoolPendingCount }} 条
          </span>
        </template>
      </CvDataTable>
    </CvPanel>

    <StationAdminPanel
      v-if="adminCommandOwner && adminState && logsState && commandsState && auditsState && packagesState"
      class="station-detail__admin-control"
      :details-state="adminState"
      :logs-state="logsState"
      :commands-state="commandsState"
      :audits-state="auditsState"
      :packages-state="packagesState"
      :owner="adminCommandOwner"
      @changed="refreshAdmin"
    />
  </section>
</template>

<style scoped>
.station-detail { display: grid; min-width: 0; grid-template-columns: minmax(300px, 360px) minmax(0, 1fr); grid-auto-flow: row dense; gap: var(--cv-density-page-gap); align-items: start; }
.station-detail :deep(.cv-page-header),
.station-detail > :deep(.cv-inline-alert),
.station-detail > :deep(.cv-page-state) { grid-column: 1 / -1; }
.station-detail__back { display: inline-flex; align-items: center; gap: var(--cv-space-1); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); text-decoration: none; }
.station-detail__back:hover { color: var(--cv-color-link); text-decoration: underline; }
.station-detail__nav-link { min-height: var(--cv-density-control-height-sm); padding: 0 var(--cv-space-2); display: inline-flex; align-items: center; justify-content: center; border-radius: var(--cv-radius-sm); color: var(--cv-color-link); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); text-decoration: none; touch-action: manipulation; }
.station-detail__nav-link:hover { background: var(--cv-interactive-hover); color: var(--cv-color-link-hover); }
.station-detail__nav-link:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.station-detail__take { min-width: 150px; }
.station-detail__grid { display: contents; }
.station-detail__overview-panel { grid-column: 1; }
.station-detail__results-panel { grid-column: 2; grid-row: span 2; }
.station-detail__health-panel { grid-column: 1 / -1; }
.station-detail__production-trace { grid-column: 1 / -1; }
.station-detail__admin-control { grid-column: 1 / -1; }
.station-detail__outcome { display: grid; justify-items: start; gap: var(--cv-space-1); }
.station-detail__outcome span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.station-detail__diagnostic,
.station-detail__health-stack { min-width: 0; display: grid; gap: 2px; }
.station-detail__diagnostic span,
.station-detail__health-stack span { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
.station-detail__diagnostic code,
.station-detail__health-stack small { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
.station-detail__health-stack .station-detail__health-error { color: var(--cv-color-status-error-strong); }
.station-detail__technical-details { border-top: 1px solid var(--cv-border-subtle); }
.station-detail__technical-details summary { min-height: 36px; padding: 0 var(--cv-density-panel-padding); display: flex; align-items: center; color: var(--cv-text-secondary); cursor: pointer; font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.station-detail__technical-details summary:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }
.station-detail :deep(.station-detail__overview-panel > .cv-panel__header),
.station-detail :deep(.station-detail__results-panel > .cv-panel__header),
.station-detail :deep(.station-detail__health-panel > .cv-panel__header) { padding-bottom: var(--cv-space-3); }
.station-detail :deep(.station-detail__overview-panel > .cv-description-list) { padding: 0 var(--cv-density-panel-padding) var(--cv-space-3); }
.station-detail__technical-details :deep(.cv-description-list) { padding: 0 var(--cv-density-panel-padding) var(--cv-space-3); }
.station-detail :deep(.station-detail__results-panel .cv-inline-alert),
.station-detail :deep(.station-detail__results-panel .cv-page-state),
.station-detail :deep(.station-detail__health-panel .cv-inline-alert),
.station-detail :deep(.station-detail__health-panel .cv-page-state) { margin: 0 var(--cv-density-panel-padding) var(--cv-space-3); }
@media (max-width: 1080px) {
  .station-detail { grid-template-columns: 1fr; }
  .station-detail__overview-panel,
  .station-detail__results-panel,
  .station-detail__health-panel { grid-column: 1; grid-row: auto; }
}
</style>
