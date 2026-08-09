<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, shallowRef } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import {
  CvButton,
  CvDataTable,
  CvField,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPanel,
  CvSearchField,
  CvSelect,
  CvStatusBadge,
  CvToolbar,
  CvViewTabs,
  type CvDataTableColumn,
  type CvSelectOption
} from '@/design-system';
import { formatInspectionOutcome } from '@/shared/inspectionOutcome';
import {
  createStationDetailDeepLink,
  createStationFleetDeepLink
} from '@/shared/productionTraceLinks';
import {
  createStationStatisticsQuery,
  createStationSummaryQuery,
  createStationsQuery,
  type StationStatisticsFilters
} from './stationQueries';
import {
  createStationMonitoringOwner,
  createStationQuerySlot,
  type StationAuthorityRefreshRequest
} from './stationLifecycleOwner';
import { createStationSseAdapter } from './stationSseAdapter';
import type { StationStatus } from './stationContracts';
import {
  filterStations,
  formatStationDateTime,
  formatStationBytes,
  stationDisplayName,
  stationOfflineReasonLabel,
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
  runtime?: StationsReadRuntime;
}>();

const route = useRoute();
const router = useRouter();
const runtime = useStationsReadRuntime(props.runtime);

function queryText(key: string): string {
  const value = route.query[key];
  return typeof value === 'string' ? value : '';
}

const searchDraft = shallowRef(queryText('q'));
const activeSearch = shallowRef(queryText('q'));
const onlineState = shallowRef(queryText('online') || 'all');
const runtimeState = shallowRef(queryText('runtime') || 'all');
const range = shallowRef(queryText('range') || 'today');
const outcome = shallowRef(queryText('outcome') || 'all');
const diagnosticCode = shallowRef(queryText('diagnosticCode'));
type StationsView = 'overview' | 'investigation';
const activeView = shallowRef<StationsView>(
  queryText('q') || queryText('online') || queryText('runtime') || queryText('packageId') ||
    queryText('projectId') || queryText('revision')
    ? 'investigation'
    : 'overview'
);
const viewOptions = Object.freeze([
  {
    value: 'overview',
    label: '全站概览',
    description: '按异常优先查看全站运行态势',
    id: 'stations-overview-tab',
    controls: 'stations-overview-panel'
  },
  {
    value: 'investigation',
    label: '异常调查',
    description: '筛选工作站并进入详情核对',
    id: 'stations-investigation-tab',
    controls: 'stations-investigation-panel'
  }
] as const);
const packageIdFilter = computed(() => queryText('packageId').trim());
const projectIdFilter = computed(() => queryText('projectId').trim());
const revisionText = computed(() => queryText('revision').trim());
const revisionFilter = computed<number | null>(() => {
  if (!revisionText.value) return null;
  const value = Number(revisionText.value);
  return Number.isInteger(value) && value >= 0 ? value : null;
});
const hasProductionTraceFilter = computed(() => Boolean(
  packageIdFilter.value || projectIdFilter.value || revisionText.value
));
const hasInvalidRevisionFilter = computed(() => Boolean(revisionText.value) && revisionFilter.value === null);

const statisticsFilters = computed<StationStatisticsFilters>(() => ({
  range: range.value,
  ...(outcome.value === 'all' ? {} : { status: outcome.value }),
  ...(diagnosticCode.value.trim() ? { diagnosticCode: diagnosticCode.value.trim() } : {})
}));

const listSlot = createStationQuerySlot(() => createStationsQuery(runtime.queries));
const summarySlot = createStationQuerySlot(() => createStationSummaryQuery(runtime.queries));
const statisticsSlot = createStationQuerySlot(() => createStationStatisticsQuery(
  runtime.queries,
  () => statisticsFilters.value
));

const listState = listSlot.state;
const summaryState = summarySlot.state;
const statisticsState = statisticsSlot.state;

function stationPriorityScore(station: StationStatus): number {
  const onlineScore: Readonly<Record<string, number>> = {
    Critical: 700, Offline: 650, Degraded: 520, Warning: 440, Unknown: 320, Online: 0
  };
  const runtimeScore: Readonly<Record<string, number>> = {
    Faulted: 600, Unknown: 300, Stopping: 180, Paused: 160, LoadingPackage: 100, Idle: 0, Running: 0
  };
  const outcomeScore = lastOutcome(station)?.tone === 'error'
    ? 360
    : lastOutcome(station)?.tone === 'ng'
      ? 280
      : lastOutcome(station)?.tone === 'warning'
        ? 180
        : 0;
  return Math.max(onlineScore[station.onlineState] ?? 0, runtimeScore[station.runtimeState] ?? 0) +
    outcomeScore + Math.min(station.spoolPendingCount, 99);
}

const visibleStations = computed(() => {
  if (hasInvalidRevisionFilter.value) return [];
  return [...filterStations(
    listState.value.data ?? [],
    activeSearch.value,
    onlineState.value,
    runtimeState.value
  ).filter(station =>
    (!packageIdFilter.value || station.packageId?.toLocaleLowerCase() === packageIdFilter.value.toLocaleLowerCase()) &&
    (!projectIdFilter.value || station.sourceProjectId?.toLocaleLowerCase() === projectIdFilter.value.toLocaleLowerCase()) &&
    (revisionFilter.value === null || station.sourceProjectRevision === revisionFilter.value)
  )].sort((left, right) => stationPriorityScore(right) - stationPriorityScore(left) ||
    stationDisplayName(left).localeCompare(stationDisplayName(right), 'zh-CN'));
});
const priorityStations = computed(() => visibleStations.value
  .filter(station => stationPriorityScore(station) > 0)
  .slice(0, 6));
const fleetReturnTo = computed(() => createStationFleetDeepLink({
  packageId: packageIdFilter.value,
  projectId: projectIdFilter.value,
  revision: revisionFilter.value,
  q: activeSearch.value,
  online: onlineState.value === 'all' ? '' : onlineState.value,
  runtime: runtimeState.value === 'all' ? '' : runtimeState.value,
  range: range.value === 'today' ? '' : range.value,
  outcome: outcome.value === 'all' ? '' : outcome.value,
  diagnosticCode: diagnosticCode.value
}));

async function refreshAuthority(request: StationAuthorityRefreshRequest): Promise<void> {
  const full = request.reason !== 'event' && request.reason !== 'heartbeat';
  const eventTypes = new Set(request.events.map(event => event.type));
  const refreshes: Promise<unknown>[] = [];
  if (full || request.reason === 'heartbeat' || eventTypes.has('stationUpserted') ||
      eventTypes.has('stationHealthUpdated') || eventTypes.has('stationResultAdded')) {
    refreshes.push(listSlot.refresh({ force: true }));
  }
  if (full || request.reason === 'heartbeat' || eventTypes.has('summaryUpdated') ||
      eventTypes.has('stationUpserted') || eventTypes.has('stationHealthUpdated') ||
      eventTypes.has('stationResultAdded')) {
    refreshes.push(summarySlot.refresh({ force: true }));
  }
  if (full || eventTypes.has('stationResultAdded')) {
    refreshes.push(statisticsSlot.refresh({ force: true }));
  }
  await Promise.allSettled(refreshes);
}

const monitoring = createStationMonitoringOwner({
  stream: runtime.api?.getTextStream ? createStationSseAdapter(runtime.api) : undefined,
  refreshAuthority,
  pauseAuthority: () => {
    listSlot.pause();
    summarySlot.pause();
    statisticsSlot.pause();
  }
});
const monitoringState = monitoring.state;

const onlineOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'all', label: '全部在线状态' },
  { value: 'Online', label: '在线' },
  { value: 'Warning', label: '警告' },
  { value: 'Degraded', label: '降级' },
  { value: 'Critical', label: '严重' },
  { value: 'Offline', label: '离线' },
  { value: 'Unknown', label: '未知' }
]);

const runtimeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'all', label: '全部运行状态' },
  { value: 'Running', label: '运行中' },
  { value: 'Idle', label: '空闲' },
  { value: 'Paused', label: '已暂停' },
  { value: 'LoadingPackage', label: '加载运行包' },
  { value: 'Stopping', label: '停止中' },
  { value: 'Faulted', label: '故障' },
  { value: 'Unknown', label: '未知' }
]);

const rangeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'today', label: '今天' },
  { value: 'week', label: '最近 7 天' },
  { value: 'month', label: '最近 1 个月' },
  { value: 'all', label: '全部时间' }
]);

const outcomeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'all', label: '全部结果' },
  { value: 'Ok', label: 'OK' },
  { value: 'Ng', label: 'NG' },
  { value: 'Undetermined', label: '未判定' },
  { value: 'NotApplicable', label: '不适用' },
  { value: 'Invalid', label: '判定无效' },
  { value: 'Failed', label: '执行失败' },
  { value: 'Cancelled', label: '已取消' },
  { value: 'TimedOut', label: '执行超时' },
  { value: 'Skipped', label: '已跳过' }
]);

const columns: readonly CvDataTableColumn<StationStatus>[] = Object.freeze([
  { key: 'station', label: '工作站', width: '17%' },
  { key: 'onlineState', label: '连接', width: '10%' },
  { key: 'runtimeState', label: '运行状态', width: '12%' },
  { key: 'packageName', label: '当前运行包', width: '18%' },
  { key: 'fieldHealth', label: '现场健康', width: '23%' },
  { key: 'lastOutcome', label: '最近结果', width: '10%' },
  { key: 'lastSeenAtUtc', label: '最后心跳', width: '10%' }
]);

const summaryCounters = computed(() => {
  const summary = summaryState.value.data;
  if (!summary) return [];
  return [
    ['工作站总数', summary.totalStations],
    ['在线', summary.onlineStations],
    ['离线', summary.offlineStations],
    ['运行中', summary.runningStations],
    ['故障', summary.faultedStations],
    ['告警', summary.alertCount]
  ] as const;
});

const outcomeCounters = computed(() => {
  const statistics = statisticsState.value.data?.outcomeStatistics;
  if (!statistics) return [];
  return [
    ['OK', statistics.okCount, 'ok'],
    ['NG', statistics.ngCount, 'ng'],
    ['未判定', statistics.undeterminedCount, 'warning'],
    ['不适用', statistics.notApplicableCount, 'info'],
    ['判定无效', statistics.invalidCount, 'warning'],
    ['执行失败', statistics.failedCount, 'error'],
    ['已取消', statistics.cancelledCount, 'idle'],
    ['执行超时', statistics.timedOutCount, 'error'],
    ['已跳过', statistics.skippedCount, 'idle']
  ] as const;
});

function lastOutcome(station: StationStatus) {
  return station.lastOutcome ? formatInspectionOutcome(station.lastOutcome) : null;
}

function priorityLabel(station: StationStatus): string {
  if (station.onlineState === 'Critical') return '严重连接异常';
  if (station.onlineState === 'Offline') return '工作站离线';
  if (station.runtimeState === 'Faulted') return '运行故障';
  if (station.onlineState === 'Degraded' || station.onlineState === 'Warning') return '连接状态异常';
  if (lastOutcome(station)?.tone === 'error') return '最近执行失败';
  if (lastOutcome(station)?.tone === 'ng') return '最近判定 NG';
  if (station.spoolPendingCount > 0) return '结果待回放';
  return '状态待确认';
}

function priorityTone(station: StationStatus): 'error' | 'ng' | 'warning' {
  if (station.onlineState === 'Critical' || station.onlineState === 'Offline' ||
      station.runtimeState === 'Faulted' || lastOutcome(station)?.tone === 'error') return 'error';
  if (lastOutcome(station)?.tone === 'ng') return 'ng';
  return 'warning';
}

async function showInvestigation(): Promise<void> {
  activeView.value = 'investigation';
  await nextTick();
  document.getElementById('stations-investigation-tab')?.focus();
}

const monitoringLabel = computed(() => {
  switch (monitoring.state.value.phase) {
    case 'live': return '实时连接';
    case 'recovering': return '连接恢复中';
    case 'recovery-polling': return '恢复轮询';
    case 'paused': return '监控已暂停';
    case 'unauthorized': return '会话已失效';
    case 'disposed': return '监控已停止';
    default: return '正在连接';
  }
});
const monitoringTone = computed(() => monitoring.state.value.phase === 'live'
  ? 'ok'
  : monitoring.state.value.phase === 'unauthorized'
    ? 'ng'
    : 'warning');

function packageIdentity(station: StationStatus): string {
  if (!station.packageId) return '未激活运行包';
  const revision = station.sourceProjectRevision === null ? '来源修订未上报' : `来源 r${station.sourceProjectRevision}`;
  return `${station.packageId} · ${revision}`;
}

function deviceReport(label: string, value: string | null): string {
  return value ? `${label} ${value}` : `${label} 未上报/不可确认`;
}

function spoolReport(station: StationStatus): string {
  return station.spoolPendingCount > 0
    ? `Spool 待回放 ${station.spoolPendingCount} · ${formatStationBytes(station.spoolBytes)}`
    : 'Spool 无待回放';
}

async function writeQueryAndRefresh(refreshStatistics: boolean): Promise<void> {
  const nextQuery = { ...route.query };
  const values: Readonly<Record<string, string>> = {
    q: activeSearch.value,
    online: onlineState.value === 'all' ? '' : onlineState.value,
    runtime: runtimeState.value === 'all' ? '' : runtimeState.value,
    range: range.value === 'today' ? '' : range.value,
    outcome: outcome.value === 'all' ? '' : outcome.value,
    diagnosticCode: diagnosticCode.value.trim()
  };
  for (const [key, value] of Object.entries(values)) {
    if (value) nextQuery[key] = value;
    else delete nextQuery[key];
  }
  await router.replace({ query: nextQuery });
  if (refreshStatistics) await statisticsSlot.refresh({ force: true });
}

async function submitSearch(): Promise<void> {
  activeSearch.value = searchDraft.value.trim();
  await writeQueryAndRefresh(false);
}

async function clearSearch(): Promise<void> {
  searchDraft.value = '';
  activeSearch.value = '';
  await writeQueryAndRefresh(false);
}

async function applyListFilters(): Promise<void> {
  await writeQueryAndRefresh(false);
}

async function applyStatisticsFilters(): Promise<void> {
  await writeQueryAndRefresh(true);
}

async function clearProductionTraceFilter(): Promise<void> {
  const nextQuery = { ...route.query };
  delete nextQuery.packageId;
  delete nextQuery.projectId;
  delete nextQuery.revision;
  await router.replace({ query: nextQuery });
}

onMounted(() => monitoring.start());

onBeforeUnmount(() => {
  monitoring.dispose();
  listSlot.dispose();
  summarySlot.dispose();
  statisticsSlot.dispose();
});
</script>

<template>
  <section
    class="stations-page"
    data-capability="stations-read"
  >
    <CvPageHeader
      title="工作站监控"
      description="查看连接与运行状态，定位异常，并进入工作站详情核对最近结果。"
    >
      <template #meta>
        <CvStatusBadge :tone="monitoringTone">
          {{ monitoringLabel }}
        </CvStatusBadge>
        <CvStatusBadge
          tone="info"
          :dot="false"
        >
          只读监控
        </CvStatusBadge>
        <span
          v-if="summaryState.data"
          class="stations-page__updated-at"
        >
          更新于 {{ formatStationDateTime(summaryState.data.updatedAtUtc) }}
        </span>
      </template>
      <template #actions>
        <CvButton
          size="sm"
          :loading="listState.isRefreshing || summaryState.isRefreshing || statisticsState.isRefreshing"
          loading-label="正在刷新工作站"
          @click="monitoring.refreshNow()"
        >
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <CvViewTabs
      v-model="activeView"
      :options="viewOptions"
      label="工作站监控视图"
      data-testid="stations-view-tabs"
    />

    <CvInlineAlert
      v-if="monitoringState.phase === 'recovering'"
      tone="warning"
      title="实时连接正在恢复"
    >
      当前保留上次权威读取；恢复期间按服务端状态重新同步。
    </CvInlineAlert>

    <section
      v-show="activeView === 'overview'"
      id="stations-overview-panel"
      class="stations-page__overview"
      role="tabpanel"
      aria-labelledby="stations-overview-tab"
      tabindex="0"
    >
      <CvPanel
        class="stations-page__summary-panel"
        title="运行摘要"
        :padded="false"
      >
        <CvInlineAlert
          v-if="(summaryState.phase === 'stale' || summaryState.phase === 'partial-failure') && summaryState.data"
          tone="warning"
          title="摘要刷新未完成"
        >
          当前显示上次成功读取的工作站摘要。
        </CvInlineAlert>
        <CvPageState
          v-if="summaryState.phase === 'loading' && !summaryState.data"
          compact
          kind="loading"
          title="正在读取工作站摘要"
        />
        <CvPageState
          v-else-if="summaryState.phase === 'unauthorized'"
          compact
          kind="unauthorized"
          title="当前会话不可用"
        />
        <CvPageState
          v-else-if="summaryState.phase === 'forbidden'"
          compact
          kind="forbidden"
          title="无权读取工作站摘要"
        />
        <CvPageState
          v-else-if="summaryState.phase === 'error' || summaryState.phase === 'not-found'"
          compact
          kind="error"
          title="工作站摘要读取失败"
          :description="summaryState.failure?.message"
        />
        <CvPageState
          v-else-if="summaryState.data?.totalStations === 0"
          compact
          kind="empty"
          title="暂无工作站摘要数据"
        />
        <dl
          v-if="summaryState.data && summaryState.data.totalStations > 0"
          class="stations-page__metrics"
        >
          <div
            v-for="counter in summaryCounters"
            :key="counter[0]"
          >
            <dt>{{ counter[0] }}</dt>
            <dd>{{ counter[1] }}</dd>
          </div>
        </dl>
      </CvPanel>

      <CvPanel
        class="stations-page__priority-panel"
        title="异常优先"
        description="按连接、运行、最近结果与待回放数量综合排序。"
        :padded="false"
      >
        <CvPageState
          v-if="listState.phase === 'loading' && !listState.data"
          compact
          kind="loading"
          title="正在读取异常工作站"
        />
        <CvPageState
          v-else-if="listState.phase === 'unauthorized'"
          compact
          kind="unauthorized"
          title="当前会话不可用"
        />
        <CvPageState
          v-else-if="listState.phase === 'forbidden'"
          compact
          kind="forbidden"
          title="无权读取工作站列表"
        />
        <CvPageState
          v-else-if="listState.phase === 'error' || listState.phase === 'not-found'"
          compact
          kind="error"
          title="工作站列表读取失败"
          :description="listState.failure?.message"
        />
        <CvPageState
          v-else-if="listState.phase === 'empty'"
          compact
          kind="empty"
          title="暂无工作站"
          description="当前后端没有返回可查看的工作站。"
        />
        <CvPageState
          v-else-if="priorityStations.length === 0"
          compact
          kind="empty"
          title="当前没有需要优先处理的异常"
          description="可进入异常调查查看全部工作站。"
        />
        <ol
          v-else
          class="stations-page__priority-list"
        >
          <li
            v-for="station in priorityStations"
            :key="station.stationId"
          >
            <div>
              <RouterLink :to="createStationDetailDeepLink(station.stationId, fleetReturnTo)">
                {{ stationDisplayName(station) }}
              </RouterLink>
              <span>{{ station.lastDiagnosticCode || station.lineName || station.stationId }}</span>
            </div>
            <CvStatusBadge :tone="priorityTone(station)">
              {{ priorityLabel(station) }}
            </CvStatusBadge>
          </li>
        </ol>
        <div class="stations-page__priority-footer">
          <CvButton
            size="sm"
            variant="quiet"
            @click="showInvestigation"
          >
            查看全部工作站
          </CvButton>
        </div>
      </CvPanel>

      <CvPanel
        class="stations-page__statistics-panel"
        title="结果统计"
        :padded="false"
      >
        <CvToolbar
          class="stations-page__statistics-toolbar"
          interaction="group"
          label="工作站结果统计筛选"
        >
          <CvSelect
            v-model="range"
            name="stationStatisticsRange"
            label="时间范围"
            :options="rangeOptions"
            @update:model-value="applyStatisticsFilters"
          />
          <CvSelect
            v-model="outcome"
            name="stationStatisticsOutcome"
            label="结果分类"
            :options="outcomeOptions"
            @update:model-value="applyStatisticsFilters"
          />
          <CvField
            v-model="diagnosticCode"
            name="stationDiagnosticCode"
            label="诊断码"
            placeholder="例如 WIRE_SWAP…"
            autocomplete="off"
            @keyup.enter="applyStatisticsFilters"
          />
          <CvButton
            size="sm"
            @click="applyStatisticsFilters"
          >
            应用筛选
          </CvButton>
        </CvToolbar>

        <CvInlineAlert
          v-if="statisticsState.isRefreshing && statisticsState.data"
          tone="info"
        >
          正在刷新统计，暂时显示上次读取的数据。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="(statisticsState.phase === 'stale' || statisticsState.phase === 'partial-failure') && statisticsState.data"
          tone="warning"
          title="统计刷新未完成"
        >
          当前显示上次成功读取的结果统计。
        </CvInlineAlert>
        <CvPageState
          v-if="statisticsState.phase === 'loading' && !statisticsState.data"
          compact
          kind="loading"
          title="正在读取结果统计"
        />
        <CvPageState
          v-else-if="statisticsState.phase === 'unauthorized'"
          compact
          kind="unauthorized"
          title="当前会话不可用"
        />
        <CvPageState
          v-else-if="statisticsState.phase === 'forbidden'"
          compact
          kind="forbidden"
          title="无权读取结果统计"
        />
        <CvPageState
          v-else-if="statisticsState.phase === 'error' || statisticsState.phase === 'not-found'"
          compact
          kind="error"
          title="结果统计读取失败"
          :description="statisticsState.failure?.message"
        />
        <CvPageState
          v-else-if="statisticsState.data?.outcomeStatistics.totalAttemptCount === 0"
          compact
          kind="empty"
          title="当前筛选范围暂无结果"
        />
        <div
          v-if="statisticsState.data && statisticsState.data.outcomeStatistics.totalAttemptCount > 0"
          class="stations-page__outcomes"
        >
          <article
            v-for="counter in outcomeCounters"
            :key="counter[0]"
          >
            <CvStatusBadge :tone="counter[2]">
              {{ counter[0] }}
            </CvStatusBadge>
            <strong>{{ counter[1] }}</strong>
          </article>
        </div>
      </CvPanel>
    </section>

    <CvPanel
      v-show="activeView === 'investigation'"
      id="stations-investigation-panel"
      class="stations-page__list-panel"
      title="工作站列表"
      :padded="false"
      role="tabpanel"
      aria-labelledby="stations-investigation-tab"
      tabindex="0"
    >
      <CvToolbar
        class="stations-page__list-toolbar"
        interaction="group"
        label="工作站列表筛选"
      >
        <CvSearchField
          v-model="searchDraft"
          class="stations-page__search"
          name="stationSearch"
          label="搜索工作站"
          placeholder="名称、ID、产线、运行包或诊断码…"
          clear-label="清除工作站搜索"
          :hide-label="true"
          @search="submitSearch"
          @clear="clearSearch"
        />
        <CvButton
          size="sm"
          variant="secondary"
          @click="submitSearch"
        >
          搜索
        </CvButton>
        <template #secondary>
          <CvSelect
            v-model="onlineState"
            name="stationOnlineState"
            label="连接状态"
            :options="onlineOptions"
            @update:model-value="applyListFilters"
          />
          <CvSelect
            v-model="runtimeState"
            name="stationRuntimeState"
            label="运行状态"
            :options="runtimeOptions"
            @update:model-value="applyListFilters"
          />
        </template>
      </CvToolbar>

      <CvInlineAlert
        v-if="listState.isRefreshing && listState.data"
        tone="info"
      >
        正在刷新，暂时显示上次读取的工作站列表。
      </CvInlineAlert>
      <CvInlineAlert
        v-if="hasProductionTraceFilter"
        :tone="hasInvalidRevisionFilter ? 'warning' : 'info'"
        :title="hasInvalidRevisionFilter ? '生产身份筛选无效' : '正在定位生产身份'"
        data-testid="stations-production-filter"
      >
        <span v-if="hasInvalidRevisionFilter">工程修订必须是非负整数，当前未猜测关联。</span>
        <span v-else>
          运行包 {{ packageIdFilter || '未指定' }} · 工程 {{ projectIdFilter || '未指定' }} ·
          修订 {{ revisionFilter === null ? '未指定' : `r${revisionFilter}` }}。列表已重新读取后端权威并按完整身份筛选。
        </span>
        <template #actions>
          <CvButton
            size="sm"
            variant="quiet"
            @click="clearProductionTraceFilter"
          >
            清除定位
          </CvButton>
        </template>
      </CvInlineAlert>
      <CvInlineAlert
        v-if="(listState.phase === 'stale' || listState.phase === 'partial-failure') && listState.data"
        tone="warning"
        title="列表刷新未完成"
      >
        当前显示上次成功读取的数据。
      </CvInlineAlert>
      <CvPageState
        v-if="listState.phase === 'loading' && !listState.data"
        kind="loading"
        title="正在读取工作站列表"
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
        title="工作站列表读取失败"
        :description="listState.failure?.message"
      />
      <CvPageState
        v-else-if="listState.phase === 'empty'"
        kind="empty"
        title="暂无工作站"
        description="当前后端没有返回可查看的工作站。"
      />
      <CvPageState
        v-else-if="listState.data && visibleStations.length === 0"
        kind="empty"
        :title="hasProductionTraceFilter ? '没有匹配该生产身份的工作站' : '没有匹配的工作站'"
        :description="hasProductionTraceFilter
          ? '该运行包可能尚未激活、已归档，或旧工作站没有上报完整工程身份；当前不会猜测关联。'
          : '请调整搜索词或状态筛选。'"
      />

      <CvDataTable
        v-if="visibleStations.length > 0"
        :rows="visibleStations"
        :columns="columns"
        row-key="stationId"
        caption="工作站只读列表"
        :busy="listState.isRefreshing"
      >
        <template #cell-station="{ row }">
          <div class="stations-page__station-name">
            <RouterLink :to="createStationDetailDeepLink(row.stationId, fleetReturnTo)">
              <strong>{{ stationDisplayName(row) }}</strong>
            </RouterLink>
            <span>{{ row.lineName || row.stationId }}</span>
            <small v-if="row.lastDiagnosticCode">
              {{ row.lastDiagnosticCode }}<template v-if="row.lastDiagnosticMessage"> · {{ row.lastDiagnosticMessage }}</template>
            </small>
          </div>
        </template>
        <template #cell-onlineState="{ row }">
          <div class="stations-page__stack">
            <CvStatusBadge :tone="stationOnlineTone(row.onlineState)">
              {{ stationOnlineLabel(row.onlineState) }}
            </CvStatusBadge>
            <small v-if="stationOfflineReasonLabel(row.offlineReason)">
              {{ stationOfflineReasonLabel(row.offlineReason) }}
            </small>
          </div>
        </template>
        <template #cell-runtimeState="{ row }">
          <div class="stations-page__stack">
            <CvStatusBadge :tone="stationRuntimeTone(row.runtimeState)">
              {{ stationRuntimeLabel(row.runtimeState) }}
            </CvStatusBadge>
            <small>{{ row.currentRunId || '无活动运行' }}</small>
          </div>
        </template>
        <template #cell-packageName="{ row }">
          <div class="stations-page__stack">
            <strong>{{ row.packageName || '未激活运行包' }}<template v-if="row.packageVersion"> · {{ row.packageVersion }}</template></strong>
            <small>{{ packageIdentity(row) }}</small>
            <small>{{ row.currentPackageHealth ? `包健康 ${row.currentPackageHealth}` : '包一致性未上报/不可确认' }}</small>
          </div>
        </template>
        <template #cell-fieldHealth="{ row }">
          <div class="stations-page__stack stations-page__field-health">
            <span>{{ spoolReport(row) }}</span>
            <small>{{ deviceReport('相机', row.cameraStatusSummary) }}</small>
            <small>{{ deviceReport('PLC', row.plcStatusSummary) }}</small>
            <small>TCP 未上报/不可确认</small>
          </div>
        </template>
        <template #cell-lastOutcome="{ row }">
          <CvStatusBadge
            v-if="lastOutcome(row)"
            :tone="lastOutcome(row)?.tone ?? 'idle'"
          >
            {{ lastOutcome(row)?.label }}
          </CvStatusBadge>
          <span v-else>—</span>
        </template>
        <template #cell-lastSeenAtUtc="{ row }">
          {{ formatStationDateTime(row.lastSeenAtUtc) }}
        </template>
      </CvDataTable>
    </CvPanel>
  </section>
</template>

<style scoped>
.stations-page { display: grid; max-width: 1720px; min-width: 0; gap: var(--cv-density-page-gap); align-items: start; }
.stations-page__overview { display: grid; min-width: 0; grid-template-columns: minmax(0, 1fr) minmax(300px, 340px); gap: var(--cv-density-page-gap); }
.stations-page__overview > * { min-width: 0; }
.stations-page__updated-at { align-self: center; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-variant-numeric: tabular-nums lining-nums; }
.stations-page__summary-panel { grid-column: 1 / -1; }
.stations-page__priority-panel { grid-column: 1; }
.stations-page__statistics-panel { grid-column: 2; }
.stations-page__list-toolbar,
.stations-page__statistics-toolbar { padding: var(--cv-space-3) var(--cv-density-panel-padding); border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.stations-page__statistics-toolbar :deep(.cv-toolbar__primary) { width: 100%; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); align-items: end; }
.stations-page__statistics-toolbar :deep(.cv-select),
.stations-page__statistics-toolbar :deep(.cv-field),
.stations-page__statistics-toolbar :deep(.cv-button) { width: 100%; }
.stations-page__statistics-toolbar :deep(.cv-field),
.stations-page__statistics-toolbar :deep(.cv-button) { grid-column: 1 / -1; }
.stations-page__search { flex: 1 1 360px; max-width: 560px; }
.stations-page__metrics { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 0; margin: 0; overflow: hidden; border-top: 1px solid var(--cv-border-subtle); }
.stations-page__metrics div { display: grid; gap: 2px; padding: var(--cv-space-3); border-right: 1px solid var(--cv-border-subtle); border-bottom: 1px solid var(--cv-border-subtle); }
.stations-page__metrics div:nth-child(3n) { border-right: 0; }
.stations-page__metrics div:nth-last-child(-n + 3) { border-bottom: 0; }
.stations-page__metrics dt { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.stations-page__metrics dd { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-lg); font-weight: var(--cv-font-weight-semibold); font-variant-numeric: tabular-nums lining-nums; }
.stations-page__station-name { display: grid; gap: var(--cv-space-1); }
.stations-page__station-name a { color: var(--cv-color-link); text-decoration: none; }
.stations-page__station-name a:hover { text-decoration: underline; }
.stations-page__station-name span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.stations-page__station-name small { max-width: 34ch; overflow: hidden; color: var(--cv-color-status-warning-strong); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.stations-page__stack { display: grid; gap: 2px; min-width: 0; }
.stations-page__stack strong,
.stations-page__stack span,
.stations-page__stack small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.stations-page__stack small { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.stations-page__field-health span { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.stations-page__outcomes { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 0; border-top: 1px solid var(--cv-border-subtle); }
.stations-page__outcomes article { display: grid; place-items: center; align-content: center; gap: 2px; min-height: 52px; padding: var(--cv-space-1); border-right: 1px solid var(--cv-border-subtle); border-bottom: 1px solid var(--cv-border-subtle); }
.stations-page__outcomes article:nth-child(3n) { border-right: 0; }
.stations-page__outcomes article:nth-last-child(-n + 3) { border-bottom: 0; }
.stations-page__outcomes article:last-child { border-bottom: 0; }
.stations-page__outcomes strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-md); font-variant-numeric: tabular-nums lining-nums; }
.stations-page__priority-list { margin: 0; padding: 0; list-style: none; }
.stations-page__priority-list li { min-width: 0; min-height: 48px; padding: var(--cv-space-2) var(--cv-density-panel-padding); display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); }
.stations-page__priority-list li > div { min-width: 0; display: grid; gap: 2px; }
.stations-page__priority-list a { overflow-wrap: anywhere; color: var(--cv-color-link); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); text-decoration: none; }
.stations-page__priority-list a:hover { text-decoration: underline; }
.stations-page__priority-list span { overflow-wrap: anywhere; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.stations-page__priority-footer { padding: var(--cv-space-2) var(--cv-density-panel-padding); display: flex; justify-content: flex-end; border-top: 1px solid var(--cv-border-subtle); }
.stations-page :deep(.stations-page__summary-panel > .cv-panel__header),
.stations-page :deep(.stations-page__list-panel > .cv-panel__header),
.stations-page :deep(.stations-page__statistics-panel > .cv-panel__header) { padding-bottom: var(--cv-space-3); }
.stations-page :deep(.stations-page__summary-panel .cv-inline-alert),
.stations-page :deep(.stations-page__summary-panel .cv-page-state),
.stations-page :deep(.stations-page__list-panel .cv-inline-alert),
.stations-page :deep(.stations-page__list-panel .cv-page-state),
.stations-page :deep(.stations-page__statistics-panel .cv-inline-alert),
.stations-page :deep(.stations-page__statistics-panel .cv-page-state) { margin: 0 var(--cv-density-panel-padding) var(--cv-space-3); }
@media (max-width: 1080px) {
  .stations-page__overview { grid-template-columns: minmax(0, 1fr); }
  .stations-page__summary-panel,
  .stations-page__priority-panel,
  .stations-page__statistics-panel { grid-column: 1; grid-row: auto; }
  .stations-page__summary-panel { order: 1; }
  .stations-page__priority-panel { order: 2; }
  .stations-page__statistics-panel { order: 3; }
  .stations-page__metrics { grid-template-columns: repeat(6, minmax(0, 1fr)); }
  .stations-page__metrics div { border-right: 1px solid var(--cv-border-subtle); border-bottom: 0; }
  .stations-page__metrics div:nth-child(2n) { border-right: 1px solid var(--cv-border-subtle); }
  .stations-page__metrics div:last-child { border-right: 0; }
  .stations-page__statistics-toolbar :deep(.cv-toolbar__primary) { display: flex; align-items: end; flex-direction: row; }
  .stations-page__statistics-toolbar :deep(.cv-select),
  .stations-page__statistics-toolbar :deep(.cv-field),
  .stations-page__statistics-toolbar :deep(.cv-button) { width: auto; }
  .stations-page__outcomes { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
@media (max-width: 720px) { .stations-page__metrics { grid-template-columns: repeat(3, minmax(0, 1fr)); } .stations-page__metrics div { border-bottom: 1px solid var(--cv-border-subtle); } .stations-page__metrics div:nth-child(3n) { border-right: 0; } .stations-page__outcomes { grid-template-columns: 1fr; } .stations-page__outcomes article { border-right: 0; border-bottom: 1px solid var(--cv-border-subtle); } .stations-page__outcomes article:last-child { border-bottom: 0; } }
</style>
