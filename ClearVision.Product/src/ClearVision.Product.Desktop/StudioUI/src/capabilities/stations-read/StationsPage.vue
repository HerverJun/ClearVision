<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
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
  type CvDataTableColumn,
  type CvSelectOption
} from '@/design-system';
import { formatInspectionOutcome } from '@/shared/inspectionOutcome';
import {
  createStationStatisticsQuery,
  createStationSummaryQuery,
  createStationsQuery,
  type StationStatisticsFilters
} from './stationQueries';
import {
  createStationQuerySlot,
  createVisibleStationPollingOwner
} from './stationLifecycleOwner';
import type { StationStatus } from './stationContracts';
import {
  filterStations,
  formatStationDateTime,
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
  runtime?: StationsReadRuntime;
}>();

const route = useRoute();
const router = useRouter();
const runtime = useStationsReadRuntime(props.runtime);

function queryText(key: string): string {
  const value = route.query[key];
  return typeof value === 'string' ? value : '';
}

const searchDraft = ref(queryText('q'));
const activeSearch = ref(queryText('q'));
const onlineState = ref(queryText('online') || 'all');
const runtimeState = ref(queryText('runtime') || 'all');
const range = ref(queryText('range') || 'today');
const outcome = ref(queryText('outcome') || 'all');
const diagnosticCode = ref(queryText('diagnosticCode'));

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
const visibleStations = computed(() => filterStations(
  listState.value.data ?? [],
  activeSearch.value,
  onlineState.value,
  runtimeState.value
));

const polling = createVisibleStationPollingOwner({
  refresh: async () => {
    await Promise.allSettled([
      listSlot.refresh({ force: true }),
      summarySlot.refresh({ force: true }),
      statisticsSlot.refresh({ force: true })
    ]);
  },
  pause: () => {
    listSlot.pause();
    summarySlot.pause();
    statisticsSlot.pause();
  }
});

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
  { key: 'station', label: '工作站', width: '21%' },
  { key: 'onlineState', label: '连接', width: '11%' },
  { key: 'runtimeState', label: '运行状态', width: '12%' },
  { key: 'packageName', label: '运行包', width: '17%' },
  { key: 'lastOutcome', label: '最近结果', width: '12%' },
  { key: 'lastSeenAtUtc', label: '最后心跳', width: '17%' },
  { key: 'actions', label: '操作', align: 'end', width: '10%' }
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

onMounted(() => polling.start());

onBeforeUnmount(() => {
  polling.dispose();
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
      eyebrow="现场监控"
      title="工作站"
      description="只读查看现场工作站的连接、运行、结果与健康状态。页面不提供命令、部署或身份修改。"
    >
      <template #actions>
        <CvButton
          size="sm"
          :loading="listState.isRefreshing || summaryState.isRefreshing || statisticsState.isRefreshing"
          loading-label="正在刷新 Station"
          @click="polling.refreshNow()"
        >
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <CvPanel
      title="运行摘要"
      description="来自工作站摘要接口的只读聚合；页面可见时每 15 秒保守刷新。"
    >
      <CvInlineAlert
        v-if="(summaryState.phase === 'stale' || summaryState.phase === 'partial-failure') && summaryState.data"
        tone="warning"
        title="摘要刷新未完成"
      >
        当前显示上次成功读取的 Station 摘要。
      </CvInlineAlert>
      <CvPageState
        v-if="summaryState.phase === 'loading' && !summaryState.data"
        compact
        kind="loading"
        title="正在读取 Station 摘要"
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
        title="无权读取 Station 摘要"
      />
      <CvPageState
        v-else-if="summaryState.phase === 'error' || summaryState.phase === 'not-found'"
        compact
        kind="error"
        title="Station 摘要读取失败"
        :description="summaryState.failure?.message"
      />
      <CvPageState
        v-else-if="summaryState.data?.totalStations === 0"
        compact
        kind="empty"
        title="暂无 Station 摘要数据"
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
      title="工作站列表"
      description="普通详情完全由列表项构建；管理员详情仅作为独立增强区域。"
    >
      <CvToolbar
        interaction="group"
        label="工作站列表筛选"
      >
        <CvSearchField
          v-model="searchDraft"
          class="stations-page__search"
          label="搜索工作站"
          placeholder="名称、ID、产线、运行包或诊断码"
          clear-label="清除工作站搜索"
          :hide-label="false"
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
            label="连接状态"
            :options="onlineOptions"
            @update:model-value="applyListFilters"
          />
          <CvSelect
            v-model="runtimeState"
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
        正在刷新，暂时显示上次读取的 Station 列表。
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
        title="正在读取 Station 列表"
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
        title="Station 列表读取失败"
        :description="listState.failure?.message"
      />
      <CvPageState
        v-else-if="listState.phase === 'empty'"
        kind="empty"
        title="暂无 Station"
        description="当前后端没有返回可查看的 Station。"
      />
      <CvPageState
        v-else-if="listState.data && visibleStations.length === 0"
        kind="empty"
        title="没有匹配的 Station"
        description="请调整搜索词或状态筛选。"
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
            <strong>{{ stationDisplayName(row) }}</strong>
            <span>{{ row.lineName || row.stationId }}</span>
          </div>
        </template>
        <template #cell-onlineState="{ row }">
          <CvStatusBadge :tone="stationOnlineTone(row.onlineState)">
            {{ stationOnlineLabel(row.onlineState) }}
          </CvStatusBadge>
        </template>
        <template #cell-runtimeState="{ row }">
          <CvStatusBadge :tone="stationRuntimeTone(row.runtimeState)">
            {{ stationRuntimeLabel(row.runtimeState) }}
          </CvStatusBadge>
        </template>
        <template #cell-packageName="{ row }">
          {{ row.packageName || '—' }}
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
        <template #cell-actions="{ row }">
          <RouterLink :to="`/stations/${encodeURIComponent(row.stationId)}`">
            查看详情
          </RouterLink>
        </template>
      </CvDataTable>
    </CvPanel>

    <CvPanel
      title="结果统计"
      description="九类标准结果分别呈现，不将未判定、无效或执行失败折叠为 NG。"
    >
      <CvToolbar
        interaction="group"
        label="Station 结果统计筛选"
      >
        <CvSelect
          v-model="range"
          label="时间范围"
          :options="rangeOptions"
          @update:model-value="applyStatisticsFilters"
        />
        <CvSelect
          v-model="outcome"
          label="结果分类"
          :options="outcomeOptions"
          @update:model-value="applyStatisticsFilters"
        />
        <CvField
          v-model="diagnosticCode"
          label="诊断码"
          placeholder="例如 WIRE_SWAP"
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
</template>

<style scoped>
.stations-page { display: grid; max-width: 1620px; min-width: 0; gap: var(--cv-density-page-gap); }
.stations-page__search { flex: 1 1 320px; }
.stations-page__metrics { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 0; margin: 0; overflow: hidden; border-block: 1px solid var(--cv-border-subtle); }
.stations-page__metrics div { display: grid; gap: var(--cv-space-1); padding: var(--cv-space-2) var(--cv-space-3); border-right: 1px solid var(--cv-border-subtle); }
.stations-page__metrics div:last-child { border-right: 0; }
.stations-page__metrics dt { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.stations-page__metrics dd { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xl); font-weight: var(--cv-font-weight-medium); font-variant-numeric: tabular-nums lining-nums; }
.stations-page__station-name { display: grid; gap: var(--cv-space-1); }
.stations-page__station-name span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.stations-page__outcomes { display: grid; grid-template-columns: repeat(auto-fit, minmax(104px, 1fr)); gap: var(--cv-space-2); }
.stations-page__outcomes article { display: grid; justify-items: start; gap: var(--cv-space-2); padding: var(--cv-space-3); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.stations-page__outcomes strong { font-size: var(--cv-font-size-lg); }
@media (max-width: 980px) {
  .stations-page__metrics { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .stations-page__metrics div { border-bottom: 1px solid var(--cv-border-subtle); }
  .stations-page__metrics div:nth-child(3n) { border-right: 0; }
  .stations-page__metrics div:nth-last-child(-n + 3) { border-bottom: 0; }
}
@media (max-width: 620px) {
  .stations-page__metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .stations-page__metrics div,
  .stations-page__metrics div:nth-child(3n) { border-right: 1px solid var(--cv-border-subtle); border-bottom: 1px solid var(--cv-border-subtle); }
  .stations-page__metrics div:nth-child(2n) { border-right: 0; }
  .stations-page__metrics div:nth-last-child(-n + 2) { border-bottom: 0; }
}
</style>
