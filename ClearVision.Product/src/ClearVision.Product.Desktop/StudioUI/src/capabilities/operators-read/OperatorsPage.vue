<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import {
  CvButton,
  CvDataTable,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPagination,
  CvPanel,
  CvSearchField,
  CvSelect,
  CvStatusBadge,
  type CvDataTableColumn,
  type CvSelectOption
} from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { OperatorCatalogItem, OperatorCategoryId, OperatorLifecycle } from './operatorContracts';
import { createOperatorCatalogQuery } from './operatorQueries';
import { useOperatorsReadRuntime, type OperatorsReadRuntime } from './operatorsReadRuntime';
import {
  filterOperators,
  isOperatorCategory,
  isOperatorLifecycle,
  isOperatorVisibility,
  lifecycleTone,
  operatorCategoryLabels,
  operatorLifecycleLabels,
  paginateOperators,
  type OperatorFilters,
  type OperatorVisibility
} from './operatorViewModel';

const props = defineProps<{ runtime?: OperatorsReadRuntime }>();
const runtime = useOperatorsReadRuntime(props.runtime);
const route = useRoute();
const router = useRouter();
const pageSize = 25;
const catalogQuery = createOperatorCatalogQuery(runtime.queries);
const state = computed(() => catalogQuery.state.value);

function queryValue(name: string): string {
  const value = route.query[name];
  return Array.isArray(value) ? value[0] ?? '' : value ?? '';
}

function positiveInteger(value: string): number {
  const parsed = Number.parseInt(value, 10);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : 1;
}

function replaceQuery(name: string, value: string, defaultValue = ''): void {
  const query = { ...route.query };
  if (!value || value === defaultValue) delete query[name];
  else query[name] = value;
  if (name !== 'page') delete query.page;
  void router.replace({ query });
}

const searchModel = computed({
  get: () => queryValue('q'),
  set: value => replaceQuery('q', value.trim())
});
const categoryModel = computed({
  get: () => isOperatorCategory(queryValue('category')) ? queryValue('category') : '',
  set: value => replaceQuery('category', value)
});
const portModel = computed({
  get: () => queryValue('port'),
  set: value => replaceQuery('port', value.trim())
});
const parameterModel = computed({
  get: () => queryValue('parameter'),
  set: value => replaceQuery('parameter', value.trim())
});
const lifecycleModel = computed({
  get: () => isOperatorLifecycle(queryValue('lifecycle')) ? queryValue('lifecycle') : '',
  set: value => replaceQuery('lifecycle', value)
});
const visibilityModel = computed({
  get: () => isOperatorVisibility(queryValue('visibility')) ? queryValue('visibility') : 'default',
  set: value => replaceQuery('visibility', value, 'default')
});
const pageModel = computed({
  get: () => positiveInteger(queryValue('page')),
  set: value => replaceQuery('page', String(value), '1')
});

const filters = computed<OperatorFilters>(() => ({
  q: searchModel.value,
  category: categoryModel.value as '' | OperatorCategoryId,
  port: portModel.value,
  parameter: parameterModel.value,
  lifecycle: lifecycleModel.value as '' | OperatorLifecycle,
  visibility: visibilityModel.value as OperatorVisibility
}));
const filtered = computed(() => filterOperators(state.value.data ?? [], filters.value));
const pageSlice = computed(() => paginateOperators(filtered.value, pageModel.value, pageSize));
const activeFilterCount = computed(() => [
  filters.value.q,
  filters.value.category,
  filters.value.port,
  filters.value.parameter,
  filters.value.lifecycle,
  filters.value.visibility === 'default' ? '' : filters.value.visibility
].filter(Boolean).length);

watch(() => pageSlice.value.page, page => {
  if (page !== pageModel.value) pageModel.value = page;
});

const categoryOptions: readonly CvSelectOption[] = Object.freeze([
  { value: '', label: '全部分类' },
  ...Object.entries(operatorCategoryLabels).map(([value, label]) => ({ value, label }))
]);
const lifecycleOptions: readonly CvSelectOption[] = Object.freeze([
  { value: '', label: '全部生命周期' },
  ...Object.entries(operatorLifecycleLabels).map(([value, label]) => ({ value, label }))
]);
const visibilityOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'default', label: '默认可见' },
  { value: 'all', label: '全部（含隐藏）' },
  { value: 'hidden', label: '仅隐藏项' }
]);
const columns: readonly CvDataTableColumn<OperatorCatalogItem>[] = Object.freeze([
  { key: 'displayName', label: '算子', width: '22%' },
  { key: 'category', label: '分类', width: '15%' },
  { key: 'lifecycle', label: '生命周期', width: '13%' },
  { key: 'ports', label: '端口', width: '18%' },
  { key: 'parameters', label: '参数', width: '12%' },
  { key: 'version', label: '版本', width: '10%' },
  { key: 'actions', label: '操作', align: 'end', width: '10%' }
]);

function detailHref(operator: OperatorCatalogItem): string {
  return `/operators/${encodeURIComponent(operator.operatorType)}`;
}

function clearFilters(): void {
  void router.replace({ query: {} });
}

onMounted(() => { void catalogQuery.refresh(); });
onBeforeUnmount(() => { catalogQuery.dispose(); });
</script>

<template>
  <section
    class="operators-page"
    data-capability="operators-read"
  >
    <CvPageHeader
      eyebrow="资源目录"
      title="算子库"
      description="按名称、分类、端口和参数查找可用算子，查看当前版本的接口定义。"
    >
      <template #actions>
        <CvStatusBadge
          tone="idle"
          :dot="false"
        >
          只读目录
        </CvStatusBadge>
        <CvButton
          size="sm"
          :loading="state.isRefreshing"
          loading-label="正在刷新算子库"
          @click="catalogQuery.refresh({ force: true })"
        >
          <template #leading>
            <CvIcon name="refresh" />
          </template>
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <CvPanel
      title="目录与筛选"
      description="筛选条件会随当前链接保留，返回目录时可继续上次查找。"
    >
      <div
        class="operators-page__filters"
        role="search"
        aria-label="算子目录筛选"
      >
        <div class="operators-page__primary-filters">
          <CvSearchField
            v-model="searchModel"
            class="operators-page__search"
            label="搜索算子"
            placeholder="名称、标识、关键词或标签"
            clear-label="清除算子搜索"
            :hide-label="false"
          />
          <CvSelect
            v-model="categoryModel"
            label="分类"
            :options="categoryOptions"
          />
          <CvSelect
            v-model="lifecycleModel"
            label="生命周期"
            :options="lifecycleOptions"
          />
          <CvSelect
            v-model="visibilityModel"
            label="可见范围"
            :options="visibilityOptions"
          />
        </div>
        <div class="operators-page__metadata-filters">
          <span class="operators-page__metadata-label">接口条件</span>
          <CvSearchField
            v-model="portModel"
            class="operators-page__compact-search"
            label="端口"
            placeholder="名称或数据类型"
            clear-label="清除端口过滤"
            :hide-label="false"
          />
          <CvSearchField
            v-model="parameterModel"
            class="operators-page__compact-search"
            label="参数"
            placeholder="名称或数据类型"
            clear-label="清除参数过滤"
            :hide-label="false"
          />
        </div>
      </div>

      <div
        v-if="state.data"
        class="operators-page__result-bar"
        aria-live="polite"
      >
        <p>
          <strong>{{ pageSlice.totalCount }}</strong>
          个匹配项
          <span v-if="pageSlice.totalCount !== state.data.length">/ 目录共 {{ state.data.length }} 项</span>
        </p>
        <CvButton
          v-if="activeFilterCount > 0"
          variant="quiet"
          size="sm"
          @click="clearFilters"
        >
          清除 {{ activeFilterCount }} 项筛选
        </CvButton>
      </div>

      <CvInlineAlert
        v-if="state.isRefreshing && state.data"
        tone="info"
      >
        正在刷新，暂时显示上次读取的数据。
      </CvInlineAlert>
      <CvInlineAlert
        v-if="(state.phase === 'stale' || state.phase === 'partial-failure') && state.data"
        tone="warning"
        title="刷新未完成"
      >
        当前显示上次成功读取的算子目录。
      </CvInlineAlert>

      <CvPageState
        v-if="state.phase === 'loading' && !state.data"
        kind="loading"
        title="正在读取算子目录"
      />
      <CvPageState
        v-else-if="state.phase === 'unauthorized'"
        kind="unauthorized"
        title="当前会话不可用"
        description="请由宿主或测试环境预置有效会话后重试。"
      />
      <CvPageState
        v-else-if="state.phase === 'forbidden'"
        kind="forbidden"
        title="无权读取算子目录"
        description="导航可见性不代表后端授权。"
      />
      <CvPageState
        v-else-if="state.phase === 'error' || state.phase === 'not-found'"
        kind="error"
        title="算子目录读取失败"
        :description="state.failure?.message ?? '本地服务未返回可用的算子目录。'"
      >
        <template #actions>
          <CvButton
            size="sm"
            @click="catalogQuery.refresh({ force: true })"
          >
            重试
          </CvButton>
        </template>
      </CvPageState>
      <CvPageState
        v-else-if="state.phase === 'empty'"
        kind="empty"
        title="暂无算子"
        description="当前服务端没有返回可浏览的算子元数据。"
      />
      <CvPageState
        v-else-if="state.data && pageSlice.totalCount === 0"
        kind="empty"
        title="没有匹配的算子"
        description="请调整搜索、分类、端口、参数、生命周期或可见性条件。"
      />

      <CvDataTable
        v-if="pageSlice.totalCount > 0"
        :rows="pageSlice.items"
        :columns="columns"
        row-key="operatorType"
        caption="算子只读目录"
        :busy="state.isRefreshing"
      >
        <template #cell-displayName="{ row }">
          <div class="operators-page__identity">
            <strong>{{ row.displayName }}</strong>
            <div class="operators-page__identity-meta">
              <span>{{ row.description || '暂无说明' }}</span>
              <code>标识 {{ row.operatorType }}</code>
            </div>
          </div>
        </template>
        <template #cell-category="{ row }">
          {{ operatorCategoryLabels[row.categoryId] }}
        </template>
        <template #cell-lifecycle="{ row }">
          <CvStatusBadge :tone="lifecycleTone(row.lifecycle)">
            {{ operatorLifecycleLabels[row.lifecycle] }}
          </CvStatusBadge>
        </template>
        <template #cell-ports="{ row }">
          输入 {{ row.inputPorts.length }} · 输出 {{ row.outputPorts.length }}
        </template>
        <template #cell-parameters="{ row }">
          {{ row.parameters.length }} 项
        </template>
        <template #cell-actions="{ row }">
          <RouterLink
            class="operators-page__detail-link"
            :to="detailHref(row)"
            :aria-label="`查看${row.displayName}详情`"
          >
            查看
            <CvIcon name="chevron-right" />
          </RouterLink>
        </template>
      </CvDataTable>

      <CvPagination
        v-if="pageSlice.totalCount > pageSize"
        v-model:page="pageModel"
        :page-size="pageSize"
        :total-items="pageSlice.totalCount"
        label="算子目录分页"
        previous-label="上一页"
        next-label="下一页"
      />
    </CvPanel>
  </section>
</template>

<style scoped>
.operators-page { display: grid; max-width: 1620px; gap: var(--cv-density-page-gap); min-width: 0; }
.operators-page__filters { display: grid; gap: var(--cv-space-3); padding-bottom: var(--cv-space-3); }
.operators-page__primary-filters { display: grid; grid-template-columns: minmax(260px, 1fr) repeat(3, minmax(148px, 0.42fr)); gap: var(--cv-space-2); align-items: end; }
.operators-page__metadata-filters { display: grid; grid-template-columns: max-content repeat(2, minmax(180px, 280px)) 1fr; gap: var(--cv-space-2); align-items: end; }
.operators-page__metadata-label { align-self: center; padding: var(--cv-space-5) var(--cv-space-2) var(--cv-space-2) 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.operators-page__result-bar { display: flex; min-height: var(--cv-density-control-height-sm); align-items: center; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-space-2) 0; border-top: 1px solid var(--cv-border-subtle); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.operators-page__result-bar p { margin: 0; }
.operators-page__result-bar strong { color: var(--cv-text-primary); font-variant-numeric: tabular-nums; }
.operators-page__identity { display: grid; gap: 2px; min-width: 0; }
.operators-page__identity-meta { display: flex; min-width: 0; align-items: baseline; gap: var(--cv-space-2); }
.operators-page__identity-meta > span { min-width: 0; overflow: hidden; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); text-overflow: ellipsis; white-space: nowrap; }
.operators-page__identity code { flex: 0 0 auto; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.operators-page__detail-link { display: inline-flex; align-items: center; gap: var(--cv-space-1); white-space: nowrap; }
.operators-page__detail-link :deep(svg) { width: 14px; height: 14px; }

@media (max-width: 1080px) {
  .operators-page__primary-filters { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .operators-page__search { grid-column: 1 / -1; }
}

@media (max-width: 720px) {
  .operators-page__primary-filters,
  .operators-page__metadata-filters { grid-template-columns: 1fr; }
  .operators-page__metadata-label { padding: 0; }
  .operators-page__result-bar { align-items: flex-start; flex-direction: column; }
}
</style>
