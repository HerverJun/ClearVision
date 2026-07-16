<script setup lang="ts">
import {
  computed,
  onBeforeUnmount,
  onMounted,
  shallowRef,
  watch
} from 'vue';
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router';
import {
  CvButton,
  CvDataTable,
  CvDescriptionList,
  CvField,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPagination,
  CvPanel,
  CvSelect,
  CvStatusBadge,
  CvToolbar,
  type CvDataTableColumn,
  type CvDescriptionItem,
  type CvSelectOption
} from '@/design-system';
import type { ReadQueryOwner, ReadQueryState } from '@/platform/query';
import {
  canonicalInspectionOutcomeKinds,
  formatInspectionOutcome,
  type CanonicalInspectionOutcomeKind
} from '@/shared/inspectionOutcome';
import type {
  LocalInspectionResultDetail,
  LocalInspectionResultPage,
  LocalInspectionResultSummary,
  ResultsProjectOption,
  StationInspectionResultPage,
  StationInspectionResultSummary
} from './resultsContracts';
import {
  createLocalResultDetailQuery,
  createLocalResultsQuery,
  createResultsProjectsQuery,
  createStationResultsQuery,
  type ResultsListFilters,
  type ResultsSource
} from './resultsQueries';
import {
  useResultsReadRuntime,
  type ResultsReadRuntime
} from './resultsReadRuntime';

const props = defineProps<{
  runtime?: ResultsReadRuntime;
}>();

const runtime = useResultsReadRuntime(props.runtime);
const route = useRoute();
const router = useRouter();
const mounted = shallowRef(false);
const projectsOwner = shallowRef<ReadQueryOwner<readonly ResultsProjectOption[]> | null>(null);
const localListOwner = shallowRef<ReadQueryOwner<LocalInspectionResultPage> | null>(null);
const localDetailOwner = shallowRef<ReadQueryOwner<LocalInspectionResultDetail> | null>(null);
const stationListOwner = shallowRef<ReadQueryOwner<StationInspectionResultPage> | null>(null);

function idleState<T>(): ReadQueryState<T> {
  return Object.freeze({
    phase: 'idle',
    isRefreshing: false,
    requestId: 0,
    sessionGeneration: runtime.queries.sessionGeneration
  });
}

function firstQueryValue(value: unknown): string {
  if (typeof value === 'string') return value;
  if (Array.isArray(value) && typeof value[0] === 'string') return value[0];
  return '';
}

function positiveInteger(value: string, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function normalizedOutcome(value: string): CanonicalInspectionOutcomeKind | '' {
  return canonicalInspectionOutcomeKinds.includes(value as CanonicalInspectionOutcomeKind)
    ? value as CanonicalInspectionOutcomeKind
    : '';
}

const source = computed<ResultsSource>(() =>
  firstQueryValue(route.query.source) === 'station' ? 'station' : 'local'
);
const projectId = computed(() => firstQueryValue(route.query.projectId));
const resultId = computed(() => firstQueryValue(route.query.resultId));
const outcome = computed(() => normalizedOutcome(firstQueryValue(route.query.outcome)));
const diagnosticCode = computed(() => firstQueryValue(route.query.diagnosticCode));
const from = computed(() => firstQueryValue(route.query.from));
const to = computed(() => firstQueryValue(route.query.to));
const page = computed(() => positiveInteger(firstQueryValue(route.query.page), 1));
const pageSize = computed(() => Math.min(
  positiveInteger(firstQueryValue(route.query.pageSize), 20),
  200
));
const invalidFrom = computed(() => from.value.length > 0 && Number.isNaN(Date.parse(from.value)));
const invalidTo = computed(() => to.value.length > 0 && Number.isNaN(Date.parse(to.value)));
const hasInvalidDate = computed(() => invalidFrom.value || invalidTo.value);
const filters = computed<ResultsListFilters>(() => Object.freeze({
  outcome: outcome.value,
  diagnosticCode: diagnosticCode.value,
  from: from.value,
  to: to.value,
  page: page.value,
  pageSize: pageSize.value
}));

const projectsState = computed(() => projectsOwner.value?.state.value ?? idleState<readonly ResultsProjectOption[]>());
const localListState = computed(() => localListOwner.value?.state.value ?? idleState<LocalInspectionResultPage>());
const localDetailState = computed(() => localDetailOwner.value?.state.value ?? idleState<LocalInspectionResultDetail>());
const stationListState = computed(() => stationListOwner.value?.state.value ?? idleState<StationInspectionResultPage>());
const localItems = computed(() => {
  const items = localListState.value.data?.items ?? [];
  const code = diagnosticCode.value.trim().toLocaleLowerCase();
  if (!code) return items;
  return items.filter(item => item.diagnosticCode?.toLocaleLowerCase() === code);
});
const selectedStationResult = computed(() => {
  if (!resultId.value) return null;
  return stationListState.value.data?.items.find(item =>
    item.messageId === resultId.value || item.runId === resultId.value
  ) ?? null;
});

const sourceOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'local', label: '本机结果' },
  { value: 'station', label: '工作站上报' }
]);
const outcomeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: '', label: '全部结果' },
  ...canonicalInspectionOutcomeKinds.map(value => ({
    value,
    label: formatInspectionOutcome(
      value === 'Ok' ? { execution: 'Succeeded', decision: 'Ok' }
        : value === 'Ng' ? { execution: 'Succeeded', decision: 'Ng' }
          : value === 'Undetermined' ? { execution: 'Succeeded', decision: 'Undetermined' }
            : value === 'NotApplicable' ? { execution: 'Succeeded', decision: 'NotApplicable' }
              : value === 'Invalid' ? { execution: 'Succeeded', decision: 'Invalid' }
                : value === 'Failed' ? { execution: 'Failed', decision: 'Undetermined' }
                  : value === 'Cancelled' ? { execution: 'Cancelled', decision: 'NotApplicable' }
                    : value === 'TimedOut' ? { execution: 'TimedOut', decision: 'Undetermined' }
                      : { execution: 'Skipped', decision: 'NotApplicable' }
    ).label
  }))
]);
const pageSizeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: '20', label: '20 条/页' },
  { value: '50', label: '50 条/页' },
  { value: '100', label: '100 条/页' },
  { value: '200', label: '200 条/页' }
]);
const projectOptions = computed<readonly CvSelectOption[]>(() => [
  { value: '', label: '请选择工程' },
  ...(projectsState.value.data ?? []).map(project => ({
    value: project.id,
    label: `${project.name} · ${project.version}`
  }))
]);

const localColumns: readonly CvDataTableColumn<LocalInspectionResultSummary>[] = Object.freeze([
  { key: 'inspectionTime', label: '完成时间', width: '17%' },
  { key: 'outcome', label: '结果', width: '12%' },
  { key: 'execution', label: '执行状态', width: '12%' },
  { key: 'decision', label: '判定结果', width: '12%' },
  { key: 'diagnosticCode', label: '诊断码', width: '15%' },
  { key: 'defectCount', label: '缺陷', align: 'end', width: '8%' },
  { key: 'processingTimeMs', label: '耗时', align: 'end', width: '10%' },
  { key: 'actions', label: '操作', align: 'end', width: '14%' }
]);
const stationColumns: readonly CvDataTableColumn<StationInspectionResultSummary>[] = Object.freeze([
  { key: 'completedAtUtc', label: '完成时间', width: '16%' },
  { key: 'stationId', label: '工作站', width: '13%' },
  { key: 'outcome', label: '结果', width: '11%' },
  { key: 'execution', label: '执行状态', width: '11%' },
  { key: 'decision', label: '判定结果', width: '12%' },
  { key: 'diagnosticCode', label: '诊断码', width: '14%' },
  { key: 'executionTimeMs', label: '耗时', align: 'end', width: '9%' },
  { key: 'actions', label: '操作', align: 'end', width: '14%' }
]);

const sourceModel = computed({
  get: () => source.value,
  set: value => { void updateQuery({ source: value, resultId: undefined, page: '1' }); }
});
const projectModel = computed({
  get: () => projectId.value,
  set: value => { void updateQuery({ projectId: value || undefined, resultId: undefined, page: '1' }); }
});
const outcomeModel = computed({
  get: () => outcome.value,
  set: value => { void updateQuery({ outcome: value || undefined, resultId: undefined, page: '1' }); }
});
const diagnosticModel = computed({
  get: () => diagnosticCode.value,
  set: value => { void updateQuery({ diagnosticCode: value || undefined, resultId: undefined, page: '1' }); }
});
const fromModel = computed({
  get: () => from.value,
  set: value => { void updateQuery({ from: value || undefined, resultId: undefined, page: '1' }); }
});
const toModel = computed({
  get: () => to.value,
  set: value => { void updateQuery({ to: value || undefined, resultId: undefined, page: '1' }); }
});
const pageSizeModel = computed({
  get: () => String(pageSize.value),
  set: value => { void updateQuery({ pageSize: value, resultId: undefined, page: '1' }); }
});
const pageModel = computed({
  get: () => page.value,
  set: value => { void updateQuery({ page: String(value), resultId: undefined }); }
});

function updateQuery(changes: LocationQueryRaw): Promise<unknown> {
  return router.replace({
    query: {
      ...route.query,
      ...changes
    }
  });
}

function ensureProjectsOwner(): ReadQueryOwner<readonly ResultsProjectOption[]> {
  projectsOwner.value ??= createResultsProjectsQuery(runtime.queries);
  return projectsOwner.value;
}

function ensureLocalListOwner(): ReadQueryOwner<LocalInspectionResultPage> {
  localListOwner.value ??= createLocalResultsQuery(
    runtime.queries,
    () => projectId.value,
    () => filters.value
  );
  return localListOwner.value;
}

function ensureLocalDetailOwner(): ReadQueryOwner<LocalInspectionResultDetail> {
  localDetailOwner.value ??= createLocalResultDetailQuery(
    runtime.queries,
    () => projectId.value,
    () => resultId.value
  );
  return localDetailOwner.value;
}

function ensureStationListOwner(): ReadQueryOwner<StationInspectionResultPage> {
  stationListOwner.value ??= createStationResultsQuery(runtime.queries, () => filters.value);
  return stationListOwner.value;
}

function disposeProjects(): void {
  projectsOwner.value?.dispose();
  projectsOwner.value = null;
}

function disposeLocalList(): void {
  localListOwner.value?.dispose();
  localListOwner.value = null;
}

function disposeLocalDetail(): void {
  localDetailOwner.value?.dispose();
  localDetailOwner.value = null;
}

function disposeStationList(): void {
  stationListOwner.value?.dispose();
  stationListOwner.value = null;
}

async function refreshProjects(force = false): Promise<void> {
  await ensureProjectsOwner().refresh({ force });
}

async function refreshLocalList(force = false): Promise<void> {
  if (!projectId.value || hasInvalidDate.value) {
    disposeLocalList();
    return;
  }
  await ensureLocalListOwner().refresh({ force });
}

async function refreshLocalDetail(force = false): Promise<void> {
  if (!projectId.value || !resultId.value) {
    disposeLocalDetail();
    return;
  }
  await ensureLocalDetailOwner().refresh({ force });
}

async function refreshStationList(force = false): Promise<void> {
  if (hasInvalidDate.value) {
    disposeStationList();
    return;
  }
  await ensureStationListOwner().refresh({ force });
}

async function refreshActive(force = false): Promise<void> {
  if (source.value === 'local') {
    await Promise.all([
      refreshProjects(force),
      refreshLocalList(force),
      refreshLocalDetail(force)
    ]);
    return;
  }
  await refreshStationList(force);
}

function selectLocalResult(id: string): void {
  void updateQuery({ resultId: id });
}

function selectStationResult(id: string): void {
  void updateQuery({ resultId: id });
}

function formatDateTime(value: string | null): string {
  if (!value) return '—';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  }).format(parsed);
}

function formatDuration(value: number): string {
  return `${value.toLocaleString('zh-CN')} ms`;
}

function localDetailItems(detail: LocalInspectionResultDetail): readonly CvDescriptionItem[] {
  const formatted = formatInspectionOutcome(detail.outcome);
  return Object.freeze([
    { key: 'result', label: '标准结果', value: formatted.label },
    { key: 'execution', label: 'Execution', value: formatted.executionLabel },
    { key: 'decision', label: 'Decision', value: formatted.decisionLabel },
    { key: 'inspectionTime', label: '完成时间', value: formatDateTime(detail.inspectionTime) },
    { key: 'processingTimeMs', label: '执行耗时', value: formatDuration(detail.processingTimeMs) },
    { key: 'defectCount', label: '缺陷数量', value: detail.defectCount },
    { key: 'diagnosticCode', label: '诊断码', value: detail.diagnosticCode },
    { key: 'diagnosticMessage', label: '诊断说明', value: detail.diagnosticMessage, span: 2 },
    { key: 'flowVersionHash', label: 'Flow Hash', value: detail.traceability.flowVersionHash },
    { key: 'calibrationBundleId', label: '标定包', value: detail.traceability.calibrationBundleId },
    { key: 'runId', label: 'Run ID', value: detail.traceability.runId },
    { key: 'sessionId', label: 'Session ID', value: detail.traceability.sessionId }
  ]);
}

function stationDetailItems(detail: StationInspectionResultSummary): readonly CvDescriptionItem[] {
  const formatted = formatInspectionOutcome(detail.outcome);
  return Object.freeze([
    { key: 'result', label: '标准结果', value: formatted.label },
    { key: 'execution', label: 'Execution', value: formatted.executionLabel },
    { key: 'decision', label: 'Decision', value: formatted.decisionLabel },
    { key: 'stationId', label: '工作站', value: detail.stationId },
    { key: 'lineName', label: '产线', value: detail.lineName },
    { key: 'completedAtUtc', label: '完成时间', value: formatDateTime(detail.completedAtUtc) },
    { key: 'executionTimeMs', label: '执行耗时', value: formatDuration(detail.executionTimeMs) },
    { key: 'diagnosticCode', label: '诊断码', value: detail.diagnosticCode },
    { key: 'diagnosticMessage', label: '诊断说明', value: detail.diagnosticMessage, span: 2 },
    { key: 'packageName', label: '运行包', value: detail.packageName },
    { key: 'packageVersion', label: '运行包版本', value: detail.packageVersion },
    { key: 'runId', label: 'Run ID', value: detail.runId },
    { key: 'messageId', label: 'Message ID', value: detail.messageId }
  ]);
}

watch(source, next => {
  if (!mounted.value) return;
  if (next === 'local') {
    disposeStationList();
  } else {
    disposeProjects();
    disposeLocalList();
    disposeLocalDetail();
  }
  void refreshActive(true);
});

watch(
  () => [projectId.value, outcome.value, from.value, to.value, page.value, pageSize.value],
  () => {
    if (!mounted.value || source.value !== 'local') return;
    void refreshLocalList(true);
  }
);

watch(diagnosticCode, () => {
  if (!mounted.value || source.value !== 'station') return;
  void refreshStationList(true);
});

watch(
  () => [outcome.value, from.value, to.value, page.value, pageSize.value],
  () => {
    if (!mounted.value || source.value !== 'station') return;
    void refreshStationList(true);
  }
);

watch(
  () => [projectId.value, resultId.value],
  () => {
    if (!mounted.value || source.value !== 'local') return;
    void refreshLocalDetail(true);
  }
);

onMounted(() => {
  mounted.value = true;
  void refreshActive();
});

onBeforeUnmount(() => {
  mounted.value = false;
  disposeProjects();
  disposeLocalList();
  disposeLocalDetail();
  disposeStationList();
});
</script>

<template>
  <section
    class="results-page"
    data-capability="results-read"
    :data-results-source="source"
  >
    <CvPageHeader
      eyebrow="质量追溯"
      title="检测结果"
      description="只读浏览本机检测历史与工作站上报摘要；执行状态与判定结果双轴始终分别展示。"
    >
      <template #actions>
        <CvButton
          size="sm"
          :loading="localListState.isRefreshing || stationListState.isRefreshing"
          loading-label="正在刷新结果"
          @click="refreshActive(true)"
        >
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <CvPanel
      title="筛选"
      description="筛选条件保存在地址栏参数中，便于恢复当前只读视图。"
    >
      <CvToolbar
        interaction="group"
        label="结果筛选工具栏"
      >
        <CvSelect
          v-model="sourceModel"
          class="results-page__source"
          label="数据来源"
          :options="sourceOptions"
        />
        <CvSelect
          v-if="source === 'local'"
          v-model="projectModel"
          class="results-page__project"
          label="本机工程"
          :options="projectOptions"
          :disabled="projectsState.phase === 'loading'"
        />
        <CvSelect
          v-model="outcomeModel"
          class="results-page__outcome"
          label="标准结果"
          :options="outcomeOptions"
        />
        <CvField
          v-model="diagnosticModel"
          class="results-page__diagnostic"
          label="诊断码"
          placeholder="例如 CAMERA_TIMEOUT"
        />
        <CvField
          v-model="fromModel"
          class="results-page__date"
          label="开始时间"
          placeholder="2026-07-15T00:00:00Z"
          :error="invalidFrom ? '请输入有效的 ISO 时间。' : undefined"
        />
        <CvField
          v-model="toModel"
          class="results-page__date"
          label="结束时间"
          placeholder="2026-07-15T23:59:59Z"
          :error="invalidTo ? '请输入有效的 ISO 时间。' : undefined"
        />
        <CvSelect
          v-model="pageSizeModel"
          class="results-page__page-size"
          label="分页大小"
          :options="pageSizeOptions"
        />
      </CvToolbar>
    </CvPanel>

    <CvInlineAlert
      v-if="source === 'local' && diagnosticCode.trim()"
      tone="info"
      title="本机诊断码为当前页过滤"
    >
      当前本机历史接口没有诊断码参数；此条件只过滤当前页已读取结果，不代表后端全量结果计数。
    </CvInlineAlert>

    <CvInlineAlert
      v-if="hasInvalidDate"
      tone="warning"
      title="时间条件无效"
    >
      已停止结果列表请求。修正 ISO 时间后会自动重新读取。
    </CvInlineAlert>

    <section
      v-if="source === 'local'"
      class="results-page__layout"
    >
      <CvPanel
        title="本机结果"
        description="列表由工程检测历史只读接口提供；九类标准结果分别保留。"
      >
        <CvInlineAlert
          v-if="projectsState.phase === 'stale' || projectsState.phase === 'partial-failure'"
          tone="warning"
          title="工程列表已过期"
        >
          当前显示上次成功读取的工程摘要。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="localListState.phase === 'stale' || localListState.phase === 'partial-failure'"
          class="results-page__notice"
          tone="warning"
          title="结果列表刷新失败"
        >
          当前显示上次成功读取的旧数据（Stale）。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="localListState.phase === 'aborted'"
          class="results-page__notice"
          tone="warning"
          title="结果请求已取消"
        >
          请求已被更新的筛选条件取代。
        </CvInlineAlert>

        <CvPageState
          v-if="projectsState.phase === 'unauthorized' || localListState.phase === 'unauthorized'"
          kind="unauthorized"
          title="当前会话不可用"
        />
        <CvPageState
          v-else-if="projectsState.phase === 'forbidden' || localListState.phase === 'forbidden'"
          kind="forbidden"
          title="无权读取本机结果"
        />
        <CvPageState
          v-else-if="projectsState.phase === 'error'"
          kind="error"
          title="工程列表读取失败"
          :description="projectsState.failure?.message"
        />
        <CvPageState
          v-else-if="!projectId"
          kind="empty"
          title="请选择本机工程"
          description="选择工程后才会请求该工程的检测历史。"
        />
        <CvPageState
          v-else-if="hasInvalidDate"
          kind="error"
          title="无法应用时间筛选"
          description="时间格式有效后才能读取结果。"
        />
        <CvPageState
          v-else-if="localListState.phase === 'loading' && !localListState.data"
          kind="loading"
          title="正在读取本机结果"
        />
        <CvPageState
          v-else-if="localListState.phase === 'error' || localListState.phase === 'not-found'"
          kind="error"
          title="本机结果读取失败"
          :description="localListState.failure?.message"
        >
          <template #actions>
            <CvButton
              size="sm"
              @click="refreshLocalList(true)"
            >
              重试
            </CvButton>
          </template>
        </CvPageState>
        <CvPageState
          v-else-if="localListState.phase === 'empty'"
          kind="empty"
          title="暂无本机结果"
        />
        <CvPageState
          v-else-if="localListState.data && localItems.length === 0"
          kind="empty"
          title="当前页没有匹配诊断码的结果"
          description="本机诊断码条件只作用于当前页投影。"
        />

        <CvDataTable
          v-if="localItems.length > 0"
          :rows="localItems"
          :columns="localColumns"
          row-key="id"
          caption="本机检测结果列表"
          :busy="localListState.isRefreshing"
        >
          <template #cell-inspectionTime="{ row }">
            {{ formatDateTime(row.inspectionTime) }}
          </template>
          <template #cell-outcome="{ row }">
            <CvStatusBadge :tone="formatInspectionOutcome(row.outcome).tone">
              {{ formatInspectionOutcome(row.outcome).label }}
            </CvStatusBadge>
          </template>
          <template #cell-execution="{ row }">
            {{ formatInspectionOutcome(row.outcome).executionLabel }}
          </template>
          <template #cell-decision="{ row }">
            {{ formatInspectionOutcome(row.outcome).decisionLabel }}
          </template>
          <template #cell-diagnosticCode="{ row }">
            {{ row.diagnosticCode || '—' }}
          </template>
          <template #cell-processingTimeMs="{ row }">
            {{ formatDuration(row.processingTimeMs) }}
          </template>
          <template #cell-actions="{ row }">
            <CvButton
              size="sm"
              variant="quiet"
              @click="selectLocalResult(row.id)"
            >
              查看详情
            </CvButton>
          </template>
        </CvDataTable>

        <CvPagination
          v-if="localListState.data && localListState.data.totalCount > 0"
          v-model:page="pageModel"
          :page-size="localListState.data.pageSize"
          :total-items="localListState.data.totalCount"
          label="本机结果分页"
        />
      </CvPanel>

      <CvPanel
        title="本机结果详情"
        description="只显示标量、诊断、缺陷摘要和追溯信息；不提供图片、感兴趣区域、对比、导出或重跑。"
      >
        <CvInlineAlert
          v-if="localDetailState.phase === 'stale' || localDetailState.phase === 'partial-failure'"
          class="results-page__notice"
          tone="warning"
          title="详情刷新失败"
        >
          当前显示上次成功读取的旧数据（Stale）。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="localDetailState.phase === 'aborted'"
          class="results-page__notice"
          tone="warning"
          title="详情请求已取消"
        />
        <CvPageState
          v-if="!resultId"
          compact
          kind="empty"
          title="请选择一条结果"
        />
        <CvPageState
          v-else-if="localDetailState.phase === 'loading' && !localDetailState.data"
          compact
          kind="loading"
          title="正在读取结果详情"
        />
        <CvPageState
          v-else-if="localDetailState.phase === 'unauthorized'"
          compact
          kind="unauthorized"
          title="当前会话不可用"
        />
        <CvPageState
          v-else-if="localDetailState.phase === 'forbidden'"
          compact
          kind="forbidden"
          title="无权读取结果详情"
        />
        <CvPageState
          v-else-if="localDetailState.phase === 'not-found'"
          compact
          kind="not-found"
          title="结果详情不存在"
          description="本地服务返回 404，该结果可能已被清理。"
        />
        <CvPageState
          v-else-if="localDetailState.phase === 'error'"
          compact
          kind="error"
          title="结果详情读取失败"
          :description="localDetailState.failure?.message"
        />
        <template v-if="localDetailState.data">
          <CvDescriptionList
            :items="localDetailItems(localDetailState.data)"
            label="本机结果详情"
          />
          <h3 class="results-page__subheading">
            缺陷摘要
          </h3>
          <ul
            v-if="localDetailState.data.defects.length"
            class="results-page__defects"
          >
            <li
              v-for="defect in localDetailState.data.defects"
              :key="defect.id"
            >
              <strong>{{ defect.type }}</strong>
              <span>置信度 {{ defect.confidenceScore.toFixed(3) }}</span>
              <span>{{ defect.description || '无描述' }}</span>
            </li>
          </ul>
          <CvPageState
            v-else
            compact
            kind="empty"
            title="没有缺陷摘要"
          />
        </template>
      </CvPanel>
    </section>

    <section
      v-else
      class="results-page__layout"
    >
      <CvPanel
        title="工作站上报结果"
        description="结果由 Studio 的工作站结果只读接口提供；旧格式数据仅做与后端一致的读取时映射。"
      >
        <CvInlineAlert
          v-if="stationListState.phase === 'stale' || stationListState.phase === 'partial-failure'"
          class="results-page__notice"
          tone="warning"
          title="工作站结果刷新失败"
        >
          当前显示上次成功读取的旧数据（Stale）。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="stationListState.phase === 'aborted'"
          class="results-page__notice"
          tone="warning"
          title="工作站结果请求已取消"
        />
        <CvPageState
          v-if="hasInvalidDate"
          kind="error"
          title="无法应用时间筛选"
        />
        <CvPageState
          v-else-if="stationListState.phase === 'loading' && !stationListState.data"
          kind="loading"
          title="正在读取工作站结果"
        />
        <CvPageState
          v-else-if="stationListState.phase === 'unauthorized'"
          kind="unauthorized"
          title="当前会话不可用"
        />
        <CvPageState
          v-else-if="stationListState.phase === 'forbidden'"
          kind="forbidden"
          title="无权读取工作站结果"
        />
        <CvPageState
          v-else-if="stationListState.phase === 'error' || stationListState.phase === 'not-found'"
          kind="error"
          title="工作站结果读取失败"
          :description="stationListState.failure?.message"
        >
          <template #actions>
            <CvButton
              size="sm"
              @click="refreshStationList(true)"
            >
              重试
            </CvButton>
          </template>
        </CvPageState>
        <CvPageState
          v-else-if="stationListState.phase === 'empty'"
          kind="empty"
          title="暂无工作站上报结果"
        />

        <CvDataTable
          v-if="stationListState.data?.items.length"
          :rows="stationListState.data.items"
          :columns="stationColumns"
          row-key="messageId"
          caption="工作站上报结果列表"
          :busy="stationListState.isRefreshing"
        >
          <template #cell-completedAtUtc="{ row }">
            {{ formatDateTime(row.completedAtUtc) }}
          </template>
          <template #cell-outcome="{ row }">
            <span class="results-page__outcome-cell">
              <CvStatusBadge :tone="formatInspectionOutcome(row.outcome).tone">
                {{ formatInspectionOutcome(row.outcome).label }}
              </CvStatusBadge>
              <small v-if="row.legacyProjection">兼容投影（旧版结果映射）</small>
            </span>
          </template>
          <template #cell-execution="{ row }">
            {{ formatInspectionOutcome(row.outcome).executionLabel }}
          </template>
          <template #cell-decision="{ row }">
            {{ formatInspectionOutcome(row.outcome).decisionLabel }}
          </template>
          <template #cell-diagnosticCode="{ row }">
            {{ row.diagnosticCode || '—' }}
          </template>
          <template #cell-executionTimeMs="{ row }">
            {{ formatDuration(row.executionTimeMs) }}
          </template>
          <template #cell-actions="{ row }">
            <CvButton
              size="sm"
              variant="quiet"
              @click="selectStationResult(row.messageId)"
            >
              查看详情
            </CvButton>
          </template>
        </CvDataTable>

        <CvPagination
          v-if="stationListState.data && stationListState.data.totalCount > 0"
          v-model:page="pageModel"
          :page-size="stationListState.data.pageSize"
          :total-items="stationListState.data.totalCount"
          label="工作站结果分页"
        />
      </CvPanel>

      <CvPanel
        title="工作站结果详情"
        description="详情来自当前页的只读工作站上报摘要，不请求命令、部署、图片或重跑接口。"
      >
        <CvPageState
          v-if="!resultId"
          compact
          kind="empty"
          title="请选择一条工作站结果"
        />
        <CvPageState
          v-else-if="!selectedStationResult"
          compact
          kind="not-found"
          title="当前页未找到该工作站结果"
          description="请返回包含该结果的分页后重新选择。"
        />
        <template v-else>
          <CvInlineAlert
            v-if="selectedStationResult.legacyProjection"
            class="results-page__notice"
            tone="warning"
            title="兼容投影（旧版工作站结果映射）"
          >
            此旧格式数据缺少标准双轴；当前结果仅按后端固定映射展示，不从诊断文案推断。
          </CvInlineAlert>
          <CvDescriptionList
            :items="stationDetailItems(selectedStationResult)"
            label="工作站结果详情"
          />
        </template>
      </CvPanel>
    </section>
  </section>
</template>

<style scoped>
.results-page { display: grid; max-width: 1720px; min-width: 0; gap: var(--cv-density-page-gap); }
.results-page__layout { display: grid; grid-template-columns: minmax(0, 1.55fr) minmax(300px, 0.8fr); gap: var(--cv-space-4); align-items: start; }
.results-page__source { min-width: 120px; }
.results-page__project { min-width: 180px; flex: 1 1 200px; }
.results-page__outcome { min-width: 124px; }
.results-page__diagnostic { min-width: 144px; }
.results-page__date { min-width: 160px; }
.results-page__page-size { min-width: 104px; }
.results-page__notice { margin-bottom: var(--cv-space-3); }
.results-page__outcome-cell { display: grid; justify-items: start; gap: var(--cv-space-1); }
.results-page__outcome-cell small { color: var(--cv-color-status-warning-strong); font-size: var(--cv-font-size-2xs); }
.results-page__subheading { margin: var(--cv-space-5) 0 var(--cv-space-3); color: var(--cv-text-primary); font-size: var(--cv-font-size-md); }
.results-page__defects { display: grid; gap: var(--cv-space-2); margin: 0; padding: 0; list-style: none; }
.results-page__defects li { display: grid; grid-template-columns: minmax(100px, 0.7fr) minmax(120px, 0.7fr) minmax(0, 1fr); gap: var(--cv-space-3); padding: var(--cv-space-3); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.results-page__defects strong { color: var(--cv-text-primary); }
@media (max-width: 1440px) { .results-page__layout { grid-template-columns: 1fr; } }
@media (max-width: 640px) { .results-page__defects li { grid-template-columns: 1fr; } }
</style>
