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
import {
  createResultEvidenceOwner,
  type ResultEvidenceOwner
} from './resultEvidenceOwner';

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
const evidenceOwner = shallowRef<ResultEvidenceOwner | null>(null);
const advancedFiltersOpen = shallowRef(
  Boolean(firstQueryValue(route.query.diagnosticCode) || firstQueryValue(route.query.from) || firstQueryValue(route.query.to))
);
const copiedTechnicalField = shallowRef<string | null>(null);

function evidencePhaseLabel(phase: string): string {
  return ({
    idle: '等待读取', loading: '读取中', available: '清单可用', partial: '部分可用',
    missing: '证据缺失', expired: '已过保留期', disabled: '未启用', error: '读取失败',
    exporting: '正在导出', disposed: '已关闭'
  } as Readonly<Record<string, string>>)[phase] ?? '状态未知';
}

async function copyTechnicalField(key: string, value: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value);
    copiedTechnicalField.value = key;
  } catch {
    copiedTechnicalField.value = null;
  }
}

function returnToWorkspace(): void {
  if (!projectId.value) return;
  void router.push(`/projects/${projectId.value}/workspace`);
}

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
const evidence = computed(() => evidenceOwner.value?.projection ?? null);
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

function disposeEvidence(): void {
  evidenceOwner.value?.dispose();
  evidenceOwner.value = null;
}

async function refreshEvidence(): Promise<void> {
  disposeEvidence();
  if (!runtime.api || !projectId.value || !resultId.value) return;
  const owner = createResultEvidenceOwner({
    projectId: projectId.value,
    resultId: resultId.value,
    api: runtime.api
  });
  evidenceOwner.value = owner;
  await owner.load();
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
    disposeEvidence();
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
      refreshLocalDetail(force),
      refreshEvidence()
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

function localOverviewItems(detail: LocalInspectionResultDetail): readonly CvDescriptionItem[] {
  const formatted = formatInspectionOutcome(detail.outcome);
  return Object.freeze([
    { key: 'result', label: '标准结果', value: formatted.label },
    { key: 'execution', label: '执行状态', value: formatted.executionLabel },
    { key: 'decision', label: '判定结果', value: formatted.decisionLabel },
    { key: 'inspectionTime', label: '完成时间', value: formatDateTime(detail.inspectionTime) },
    { key: 'processingTimeMs', label: '执行耗时', value: formatDuration(detail.processingTimeMs) },
    { key: 'defectCount', label: '缺陷数量', value: detail.defectCount }
  ]);
}

function localDiagnosticItems(detail: LocalInspectionResultDetail): readonly CvDescriptionItem[] {
  return Object.freeze([
    { key: 'diagnosticCode', label: '诊断码', value: detail.diagnosticCode },
    { key: 'diagnosticMessage', label: '诊断说明', value: detail.diagnosticMessage, span: 2 }
  ]);
}

function localTraceabilityItems(detail: LocalInspectionResultDetail): readonly CvDescriptionItem[] {
  return Object.freeze([
    { key: 'flowVersionHash', label: '流程版本哈希', value: detail.traceability.flowVersionHash },
    { key: 'calibrationBundleId', label: '标定包', value: detail.traceability.calibrationBundleId },
    { key: 'runId', label: '运行标识', value: detail.traceability.runId },
    { key: 'projectPersistenceRevision', label: '工程保存修订', value: detail.traceability.projectPersistenceRevision },
    { key: 'decisionConfigurationHash', label: '判定配置哈希', value: detail.traceability.decisionConfigurationHash },
    { key: 'sessionId', label: '会话标识', value: detail.traceability.sessionId }
  ]);
}

function stationOverviewItems(detail: StationInspectionResultSummary): readonly CvDescriptionItem[] {
  const formatted = formatInspectionOutcome(detail.outcome);
  return Object.freeze([
    { key: 'result', label: '标准结果', value: formatted.label },
    { key: 'execution', label: '执行状态', value: formatted.executionLabel },
    { key: 'decision', label: '判定结果', value: formatted.decisionLabel },
    { key: 'stationId', label: '工作站', value: detail.stationId },
    { key: 'lineName', label: '产线', value: detail.lineName },
    { key: 'completedAtUtc', label: '完成时间', value: formatDateTime(detail.completedAtUtc) },
    { key: 'executionTimeMs', label: '执行耗时', value: formatDuration(detail.executionTimeMs) }
  ]);
}

function stationDiagnosticItems(detail: StationInspectionResultSummary): readonly CvDescriptionItem[] {
  return Object.freeze([
    { key: 'diagnosticCode', label: '诊断码', value: detail.diagnosticCode },
    { key: 'diagnosticMessage', label: '诊断说明', value: detail.diagnosticMessage, span: 2 }
  ]);
}

function stationTraceabilityItems(detail: StationInspectionResultSummary): readonly CvDescriptionItem[] {
  return Object.freeze([
    { key: 'packageName', label: '运行包', value: detail.packageName },
    { key: 'packageVersion', label: '运行包版本', value: detail.packageVersion },
    { key: 'runId', label: '运行标识', value: detail.runId },
    { key: 'messageId', label: '消息标识', value: detail.messageId }
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
    void refreshEvidence();
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
  disposeEvidence();
  disposeStationList();
});
</script>

<template>
  <section
    class="results-page"
    data-capability="results-read"
    :data-results-source="source"
  >
    <header class="results-page__commandbar">
      <div class="results-page__title">
        <h1>检测结果</h1>
        <CvStatusBadge
          tone="info"
          :dot="false"
        >
          只读
        </CvStatusBadge>
        <span class="results-page__meta">
          {{ source === 'local' ? '本机结果' : '工作站上报' }}
        </span>
      </div>
      <div class="results-page__commands">
        <CvButton
          v-if="source === 'local' && projectId"
          size="sm"
          variant="quiet"
          data-testid="results-return-workspace"
          @click="returnToWorkspace"
        >
          返回工作区
        </CvButton>
        <CvButton
          size="sm"
          :loading="localListState.isRefreshing || stationListState.isRefreshing"
          loading-label="正在刷新结果"
          @click="refreshActive(true)"
        >
          刷新
        </CvButton>
      </div>
    </header>

    <section
      class="results-page__filters"
      aria-label="结果筛选"
    >
      <CvToolbar
        class="results-page__filter-toolbar"
        interaction="group"
        label="结果筛选工具栏"
      >
        <CvSelect
          v-model="sourceModel"
          class="results-page__source"
          name="resultsSource"
          label="数据来源"
          :options="sourceOptions"
        />
        <CvSelect
          v-if="source === 'local'"
          v-model="projectModel"
          class="results-page__project"
          name="resultsProject"
          label="本机工程"
          :options="projectOptions"
          :disabled="projectsState.phase === 'loading'"
        />
        <CvSelect
          v-model="outcomeModel"
          class="results-page__outcome"
          name="resultsOutcome"
          label="标准结果"
          :options="outcomeOptions"
        />
        <CvField
          v-model="diagnosticModel"
          class="results-page__diagnostic"
          name="diagnosticCode"
          label="诊断码"
          placeholder="例如 CAMERA_TIMEOUT…"
          autocomplete="off"
        />
        <CvSelect
          v-model="pageSizeModel"
          class="results-page__page-size"
          name="resultsPageSize"
          label="分页大小"
          :options="pageSizeOptions"
        />
        <button
          type="button"
          class="results-page__advanced-trigger"
          :aria-expanded="advancedFiltersOpen"
          aria-controls="results-advanced-filters"
          @click="advancedFiltersOpen = !advancedFiltersOpen"
        >
          高级筛选
          <span
            v-if="from || to"
            class="results-page__filter-count"
          >已应用</span>
        </button>
      </CvToolbar>

      <div
        v-show="advancedFiltersOpen"
        id="results-advanced-filters"
        class="results-page__advanced"
      >
        <CvField
          v-model="fromModel"
          class="results-page__date"
          name="resultsFrom"
          label="开始时间（ISO）"
          placeholder="2026-07-15T00:00:00Z…"
          autocomplete="off"
          :error="invalidFrom ? '请输入有效的 ISO 时间。' : undefined"
        />
        <CvField
          v-model="toModel"
          class="results-page__date"
          name="resultsTo"
          label="结束时间（ISO）"
          placeholder="2026-07-15T23:59:59Z…"
          autocomplete="off"
          :error="invalidTo ? '请输入有效的 ISO 时间。' : undefined"
        />
      </div>
    </section>

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
        class="results-page__list-panel"
        title="本机结果"
        :padded="false"
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
          当前显示上次成功读取的旧数据。
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
        class="results-page__detail-panel"
        title="结果详情"
        :padded="false"
      >
        <CvInlineAlert
          v-if="localDetailState.phase === 'stale' || localDetailState.phase === 'partial-failure'"
          class="results-page__notice"
          tone="warning"
          title="详情刷新失败"
        >
          当前显示上次成功读取的旧数据。
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
          <section class="results-page__detail-section results-page__detail-section--summary">
            <h3>判定摘要</h3>
            <CvDescriptionList
              :items="localOverviewItems(localDetailState.data)"
              label="本机结果判定摘要"
            />
          </section>
          <section class="results-page__detail-section">
            <h3>缺陷摘要</h3>
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
          </section>
          <section class="results-page__detail-section">
            <h3>诊断</h3>
            <CvDescriptionList
              :items="localDiagnosticItems(localDetailState.data)"
              label="本机结果诊断"
            />
          </section>
          <section
            class="results-page__detail-section results-page__evidence"
            data-capability="result-evidence"
            :data-evidence-phase="evidence?.phase ?? localDetailState.data.evidenceStatus"
          >
            <div class="results-page__evidence-heading">
              <div>
                <h3>证据清单</h3>
                <p>{{ evidence?.message ?? localDetailState.data.evidenceMessage ?? '证据状态未知。' }}</p>
              </div>
              <CvButton
                v-if="evidence?.canExport"
                size="sm"
                :disabled="evidence.phase === 'exporting'"
                data-testid="result-evidence-export"
                @click="evidenceOwner?.exportEvidence()"
              >
                导出本条证据
              </CvButton>
            </div>
            <dl
              v-if="evidence?.manifest"
              class="results-page__evidence-summary"
            >
              <div><dt>证据状态</dt><dd>{{ evidencePhaseLabel(evidence.phase) }}</dd></div>
              <div><dt>大小</dt><dd>{{ evidence.manifest.totalBytes.toLocaleString('zh-CN') }} B</dd></div>
              <div><dt>保留</dt><dd>{{ evidence.manifest.retentionClass }} / {{ formatDateTime(evidence.manifest.retentionExpiresAtUtc) }}</dd></div>
              <div><dt>脱敏</dt><dd>{{ evidence.manifest.redactionApplied ? '已应用' : '未应用' }}</dd></div>
            </dl>
            <table
              v-if="evidence?.manifest?.items.length"
              class="results-page__evidence-items"
            >
              <thead><tr><th>角色</th><th>类型</th><th>大小</th><th>可用性</th></tr></thead>
              <tbody>
                <tr
                  v-for="item in evidence.manifest.items"
                  :key="item.id"
                >
                  <td>{{ item.role }}</td><td>{{ item.contentType }}</td><td>{{ item.sizeBytes.toLocaleString('zh-CN') }} B</td><td>{{ item.available ? '可用' : item.missingReason ?? '缺失' }}</td>
                </tr>
              </tbody>
            </table>
            <details
              v-if="evidence?.manifest"
              class="results-page__technical-details"
            >
              <summary>证据技术详情</summary>
              <dl>
                <div>
                  <dt>清单标识 <span translate="no">Manifest ID</span></dt>
                  <dd>
                    <code translate="no">{{ evidence.manifest.manifestId }}</code><CvButton
                      size="sm"
                      variant="quiet"
                      @click="copyTechnicalField('manifest', evidence.manifest.manifestId)"
                    >
                      {{ copiedTechnicalField === 'manifest' ? '已复制' : '复制' }}
                    </CvButton>
                  </dd>
                </div>
                <div>
                  <dt>完整性校验 <span translate="no">Checksum</span></dt>
                  <dd>
                    <code translate="no">{{ evidence.manifest.checksum ?? '—' }}</code><CvButton
                      v-if="evidence.manifest.checksum"
                      size="sm"
                      variant="quiet"
                      @click="copyTechnicalField('checksum', evidence.manifest.checksum)"
                    >
                      {{ copiedTechnicalField === 'checksum' ? '已复制' : '复制' }}
                    </CvButton>
                  </dd>
                </div>
              </dl>
            </details>
          </section>
          <details class="results-page__traceability">
            <summary>技术追溯</summary>
            <CvDescriptionList
              :items="localTraceabilityItems(localDetailState.data)"
              label="本机结果技术追溯"
            />
          </details>
        </template>
      </CvPanel>
    </section>

    <section
      v-else
      class="results-page__layout"
    >
      <CvPanel
        class="results-page__list-panel"
        title="工作站上报结果"
        :padded="false"
      >
        <CvInlineAlert
          v-if="stationListState.phase === 'stale' || stationListState.phase === 'partial-failure'"
          class="results-page__notice"
          tone="warning"
          title="工作站结果刷新失败"
        >
          当前显示上次成功读取的旧数据。
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
        class="results-page__detail-panel"
        title="工作站结果详情"
        :padded="false"
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
          <section class="results-page__detail-section results-page__detail-section--summary">
            <h3>判定摘要</h3>
            <CvDescriptionList
              :items="stationOverviewItems(selectedStationResult)"
              label="工作站结果判定摘要"
            />
          </section>
          <section class="results-page__detail-section">
            <h3>诊断</h3>
            <CvDescriptionList
              :items="stationDiagnosticItems(selectedStationResult)"
              label="工作站结果诊断"
            />
          </section>
          <details class="results-page__traceability">
            <summary>技术追溯</summary>
            <CvDescriptionList
              :items="stationTraceabilityItems(selectedStationResult)"
              label="工作站结果技术追溯"
            />
          </details>
        </template>
      </CvPanel>
    </section>
  </section>
</template>

<style scoped>
.results-page { height: 100%; min-height: 0; display: flex; flex-direction: column; max-width: 1720px; min-width: 0; gap: var(--cv-space-2); overflow: hidden; }
.results-page__commandbar { min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.results-page__title,.results-page__commands { min-width: 0; display: flex; align-items: center; gap: var(--cv-space-2); }
.results-page__title h1 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-md); font-weight: var(--cv-font-weight-semibold); letter-spacing: 0; }
.results-page__meta { align-self: center; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.results-page__filters { overflow: hidden; border-block: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.results-page__filter-toolbar { padding: var(--cv-space-2); background: var(--cv-surface-raised); }
.results-page__filter-toolbar :deep(.cv-toolbar__primary) { flex: 1 1 100%; align-items: end; }
.results-page__source { min-width: 124px; flex: 0 1 124px; }
.results-page__project { min-width: 220px; flex: 1 1 260px; }
.results-page__outcome { min-width: 132px; flex: 0 1 132px; }
.results-page__diagnostic { min-width: 180px; flex: 1 1 220px; }
.results-page__date { min-width: 206px; flex: 1 1 240px; }
.results-page__page-size { min-width: 112px; flex: 0 1 112px; }
.results-page__advanced-trigger { min-height: var(--cv-density-control-height); padding: 0 var(--cv-space-3); display: inline-flex; align-items: center; gap: var(--cv-space-2); border: 1px solid transparent; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-color-link); cursor: pointer; font: inherit; font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); touch-action: manipulation; }
.results-page__advanced-trigger:hover { background: var(--cv-interactive-hover); }
.results-page__advanced-trigger:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.results-page__filter-count { padding: 1px var(--cv-space-1); border-radius: var(--cv-radius-pill); background: var(--cv-color-status-info-soft); color: var(--cv-color-status-info-strong); font-size: var(--cv-font-size-2xs); }
.results-page__advanced { display: flex; flex-wrap: wrap; align-items: start; gap: var(--cv-space-3); padding: var(--cv-space-2); border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.results-page__layout { min-height: 0; flex: 1 1 auto; display: grid; grid-template-columns: minmax(0, 1.65fr) minmax(390px, 0.85fr); gap: var(--cv-space-2); align-items: stretch; overflow: hidden; }
.results-page__list-panel,.results-page__detail-panel { height: 100%; min-height: 0; border-radius: var(--cv-radius-sm); }
.results-page :deep(.results-page__list-panel > .cv-panel__content),.results-page :deep(.results-page__detail-panel > .cv-panel__content) { max-height: 100%; overflow: auto; overscroll-behavior: contain; }
.results-page__notice { margin-bottom: var(--cv-space-3); }
.results-page__outcome-cell { display: grid; justify-items: start; gap: var(--cv-space-1); }
.results-page__outcome-cell small { color: var(--cv-color-status-warning-strong); font-size: var(--cv-font-size-2xs); }
.results-page__detail-panel { position: static; }
.results-page__detail-section { padding: var(--cv-space-3) var(--cv-density-panel-padding); border-top: 1px solid var(--cv-border-subtle); }
.results-page__detail-section--summary { background: var(--cv-surface-page); }
.results-page__detail-section h3 { margin: 0 0 var(--cv-space-2); color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); }
.results-page__defects { display: grid; gap: var(--cv-space-2); margin: 0; padding: 0; list-style: none; }
.results-page__defects li { display: grid; grid-template-columns: minmax(96px, 0.62fr) minmax(116px, 0.72fr) minmax(0, 1fr); gap: var(--cv-space-3); padding: var(--cv-space-2) 0; border-bottom: 1px solid var(--cv-border-subtle); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.results-page__defects li:last-child { border-bottom: 0; }
.results-page__defects strong { color: var(--cv-text-primary); }
.results-page__evidence-heading { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.results-page__evidence-heading h3 { margin-bottom: 2px; }
.results-page__evidence-heading p { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.results-page__evidence-summary { margin: var(--cv-space-3) 0 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); border-block: 1px solid var(--cv-border-subtle); }
.results-page__evidence-summary div { min-width: 0; padding: 6px 8px; border-bottom: 1px solid var(--cv-border-subtle); }
.results-page__evidence-summary dt { color: var(--cv-text-muted); font-size: 9px; }
.results-page__evidence-summary dd { margin: 2px 0 0; font-size: var(--cv-font-size-2xs); overflow-wrap: anywhere; }
.results-page__evidence-items { width: 100%; margin-top: var(--cv-space-2); border-collapse: collapse; font-size: var(--cv-font-size-2xs); }
.results-page__evidence-items th,.results-page__evidence-items td { padding: 5px 6px; text-align: left; border-bottom: 1px solid var(--cv-border-subtle); }
.results-page__traceability { border-top: 1px solid var(--cv-border-subtle); }
.results-page__traceability summary { padding: var(--cv-space-3) var(--cv-density-panel-padding); color: var(--cv-text-secondary); cursor: pointer; font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); list-style-position: inside; }
.results-page__traceability summary:hover { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.results-page__traceability summary:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }
.results-page__traceability :deep(.cv-description-list) { padding: 0 var(--cv-density-panel-padding) var(--cv-space-3); }
.results-page__technical-details { margin-top: var(--cv-space-2); border-top: 1px solid var(--cv-border-subtle); }
.results-page__technical-details summary { min-height: 32px; display: flex; align-items: center; color: var(--cv-text-secondary); cursor: pointer; font-size: var(--cv-font-size-2xs); font-weight: var(--cv-font-weight-semibold); }
.results-page__technical-details summary:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }
.results-page__technical-details dl { margin: 0; display: grid; gap: var(--cv-space-2); }
.results-page__technical-details dl > div { min-width: 0; padding: var(--cv-space-2); background: var(--cv-surface-page); }
.results-page__technical-details dt { color: var(--cv-text-secondary); font-size: 9px; }
.results-page__technical-details dt span { margin-left: var(--cv-space-1); color: var(--cv-text-muted); }
.results-page__technical-details dd { margin: 2px 0 0; display: flex; align-items: flex-start; gap: var(--cv-space-2); }
.results-page__technical-details code { min-width: 0; flex: 1; color: var(--cv-text-primary); font-size: 10px; overflow-wrap: anywhere; user-select: all; }
.results-page :deep(.results-page__list-panel > .cv-panel__header),
.results-page :deep(.results-page__detail-panel > .cv-panel__header) { padding-bottom: var(--cv-space-3); }
.results-page :deep(.results-page__list-panel .cv-inline-alert),
.results-page :deep(.results-page__list-panel .cv-page-state),
.results-page :deep(.results-page__detail-panel > .cv-panel__content > .cv-inline-alert),
.results-page :deep(.results-page__detail-panel > .cv-panel__content > .cv-page-state) { margin: 0 var(--cv-density-panel-padding) var(--cv-space-3); }
.results-page :deep(.results-page__list-panel .cv-pagination) { padding: var(--cv-space-3) var(--cv-density-panel-padding); border-top: 1px solid var(--cv-border-subtle); }
@media (max-width: 1240px) { .results-page__layout { grid-template-columns: minmax(0, 1fr) minmax(340px, 0.72fr); } }
@media (max-width: 980px) { .results-page__layout { grid-template-columns: 1fr; } .results-page__detail-panel { position: static; } }
@media (max-width: 640px) { .results-page__defects li { grid-template-columns: 1fr; } }
</style>
