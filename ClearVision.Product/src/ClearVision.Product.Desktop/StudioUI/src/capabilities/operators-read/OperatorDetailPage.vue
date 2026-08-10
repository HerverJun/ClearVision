<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import {
  CvButton,
  CvDataTable,
  CvDescriptionList,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPanel,
  CvStatusBadge,
  type CvDataTableColumn,
  type CvDescriptionItem
} from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { OperatorParameter, OperatorPort } from './operatorContracts';
import { createOperatorDetailQuery } from './operatorQueries';
import { useOperatorsReadRuntime, type OperatorsReadRuntime } from './operatorsReadRuntime';
import {
  formatOperatorDataType,
  formatParameterDataType,
  formatMetadataValue,
  lifecycleTone,
  operatorCategoryLabels,
  operatorLifecycleLabels
} from './operatorViewModel';

const props = defineProps<{ operatorType?: string; runtime?: OperatorsReadRuntime }>();
const route = useRoute();
const runtime = useOperatorsReadRuntime(props.runtime);
const activeOperatorType = computed(() => props.operatorType ?? String(route.params.operatorType ?? ''));
const detailQuery = createOperatorDetailQuery(runtime.queries, () => activeOperatorType.value);
const state = computed(() => detailQuery.state.value);
const summary = computed<readonly CvDescriptionItem[]>(() => state.value.data ? [
  { key: 'category', label: '分类', value: operatorCategoryLabels[state.value.data.categoryId] },
  { key: 'version', label: '当前版本', value: state.value.data.version },
  { key: 'visibility', label: '目录可见性', value: state.value.data.defaultHidden ? '默认隐藏' : '默认可见' },
  { key: 'ports', label: '接口规模', value: `输入 ${state.value.data.inputPorts.length} · 输出 ${state.value.data.outputPorts.length}` },
  { key: 'parameters', label: '参数数量', value: `${state.value.data.parameters.length} 项` },
  ...(state.value.data.lifecycleNote
    ? [{ key: 'lifecycle-note', label: '使用说明', value: state.value.data.lifecycleNote, span: 2 as const }]
    : [])
] : []);
const technicalSummary = computed<readonly CvDescriptionItem[]>(() => state.value.data ? [
  { key: 'type', label: '算子类型标识', value: state.value.data.operatorType },
  { key: 'icon', label: '图标标识', value: state.value.data.iconName ?? '未提供' },
  { key: 'keywords', label: '关键词', value: state.value.data.keywords.join('、'), span: 2 },
  { key: 'tags', label: '标签', value: state.value.data.tags.join('、'), span: 2 }
] : []);

const portColumns: readonly CvDataTableColumn<OperatorPort>[] = Object.freeze([
  { key: 'identity', label: '端口', width: '30%' },
  { key: 'dataType', label: '数据类型', width: '20%' },
  { key: 'isRequired', label: '要求', width: '14%' },
  { key: 'description', label: '说明', width: '36%' }
]);
const parameterColumns: readonly CvDataTableColumn<OperatorParameter>[] = Object.freeze([
  { key: 'identity', label: '参数', width: '30%' },
  { key: 'dataType', label: '类型', width: '14%' },
  { key: 'isRequired', label: '要求', width: '12%' },
  { key: 'defaultValue', label: '默认值', width: '16%' },
  { key: 'range', label: '范围 / 选项', width: '28%' }
]);

function parameterRange(parameter: OperatorParameter): string {
  if (parameter.options?.length) {
    return parameter.options.map(option => `${option.label} (${option.value})`).join('、');
  }
  if (parameter.minValue !== null && parameter.minValue !== undefined ||
      parameter.maxValue !== null && parameter.maxValue !== undefined) {
    return `${formatMetadataValue(parameter.minValue)} ～ ${formatMetadataValue(parameter.maxValue)}`;
  }
  return '—';
}

onMounted(() => { void detailQuery.refresh(); });
watch(activeOperatorType, (next, previous) => {
  if (next !== previous) void detailQuery.refresh({ force: true });
});
onBeforeUnmount(() => { detailQuery.dispose(); });
</script>

<template>
  <section
    class="operator-detail"
    data-capability="operators-read-detail"
  >
    <CvPageHeader
      :title="state.data?.displayName ?? '算子详情'"
      :description="state.data?.description || '查看算子的版本、端口和参数定义。'"
    >
      <template #breadcrumbs>
        <RouterLink
          class="operator-detail__back-link"
          to="/operators"
        >
          <CvIcon name="chevron-left" />
          返回算子库
        </RouterLink>
      </template>
      <template #actions>
        <CvStatusBadge
          tone="idle"
          :dot="false"
        >
          只读
        </CvStatusBadge>
        <CvButton
          size="sm"
          :loading="state.isRefreshing"
          loading-label="正在刷新算子详情"
          @click="detailQuery.refresh({ force: true })"
        >
          <template #leading>
            <CvIcon name="refresh" />
          </template>
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

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
      当前显示上次成功读取的算子详情。
    </CvInlineAlert>

    <CvPageState
      v-if="state.phase === 'loading' && !state.data"
      kind="loading"
      title="正在读取算子详情"
    />
    <CvPageState
      v-else-if="state.phase === 'unauthorized'"
      kind="unauthorized"
      title="当前会话不可用"
    />
    <CvPageState
      v-else-if="state.phase === 'forbidden'"
      kind="forbidden"
      title="无权读取算子详情"
    />
    <CvPageState
      v-else-if="state.phase === 'not-found'"
      kind="not-found"
      title="未找到算子"
      description="该算子可能已被移除，或当前链接无效。"
    >
      <template #actions>
        <RouterLink to="/operators">
          返回算子库
        </RouterLink>
      </template>
    </CvPageState>
    <CvPageState
      v-else-if="state.phase === 'error'"
      kind="error"
      title="算子详情读取失败"
      :description="state.failure?.message ?? '本地服务未返回可用的算子详情。'"
    >
      <template #actions>
        <CvButton
          size="sm"
          @click="detailQuery.refresh({ force: true })"
        >
          重试
        </CvButton>
      </template>
    </CvPageState>

    <div
      v-if="state.data"
      class="operator-detail__grid"
    >
      <CvPanel
        title="算子概况"
        description="当前目录发布的版本与使用状态。"
        variant="section"
      >
        <div class="operator-detail__status-row">
          <CvStatusBadge :tone="lifecycleTone(state.data.lifecycle)">
            {{ operatorLifecycleLabels[state.data.lifecycle] }}
          </CvStatusBadge>
        </div>
        <CvDescriptionList
          :items="summary"
          label="算子身份"
        />
        <details class="operator-detail__technical">
          <summary>技术标识</summary>
          <CvDescriptionList
            :items="technicalSummary"
            label="算子技术标识"
          />
        </details>
      </CvPanel>

      <CvPanel
        title="输入端口"
        description="算子接收的数据及其必需条件。"
        variant="section"
      >
        <CvPageState
          v-if="state.data.inputPorts.length === 0"
          compact
          kind="empty"
          title="无输入端口"
        />
        <CvDataTable
          v-else
          :rows="state.data.inputPorts"
          :columns="portColumns"
          row-key="name"
          caption="算子输入端口"
        >
          <template #cell-identity="{ row }">
            <div class="operator-detail__identity-cell">
              <strong>{{ row.displayName || row.name }}</strong>
              <code>{{ row.name }}</code>
            </div>
          </template>
          <template #cell-dataType="{ row }">
            <div class="operator-detail__type-cell">
              <span>{{ formatOperatorDataType(row.dataType) }}</span>
              <code>{{ row.dataType }}</code>
            </div>
          </template>
          <template #cell-isRequired="{ row }">
            {{ row.isRequired ? '必需' : '可选' }}
          </template>
          <template #cell-description="{ row }">
            {{ row.description || '—' }}
          </template>
        </CvDataTable>
      </CvPanel>

      <CvPanel
        title="输出端口"
        description="算子当前声明的输出数据。"
        variant="section"
      >
        <CvPageState
          v-if="state.data.outputPorts.length === 0"
          compact
          kind="empty"
          title="无输出端口"
        />
        <CvDataTable
          v-else
          :rows="state.data.outputPorts"
          :columns="portColumns"
          row-key="name"
          caption="算子输出端口"
        >
          <template #cell-identity="{ row }">
            <div class="operator-detail__identity-cell">
              <strong>{{ row.displayName || row.name }}</strong>
              <code>{{ row.name }}</code>
            </div>
          </template>
          <template #cell-dataType="{ row }">
            <div class="operator-detail__type-cell">
              <span>{{ formatOperatorDataType(row.dataType) }}</span>
              <code>{{ row.dataType }}</code>
            </div>
          </template>
          <template #cell-isRequired="{ row }">
            {{ row.isRequired ? '必需' : '可选' }}
          </template>
          <template #cell-description="{ row }">
            {{ row.description || '—' }}
          </template>
        </CvDataTable>
      </CvPanel>

      <CvPanel
        title="参数"
        description="默认值与约束来自当前算子目录，仅用于查阅。"
        variant="section"
      >
        <CvPageState
          v-if="state.data.parameters.length === 0"
          compact
          kind="empty"
          title="无参数"
        />
        <CvDataTable
          v-else
          :rows="state.data.parameters"
          :columns="parameterColumns"
          row-key="name"
          caption="算子参数元数据"
        >
          <template #cell-identity="{ row }">
            <div class="operator-detail__identity-cell">
              <strong>{{ row.displayName || row.name }}</strong>
              <code>{{ row.name }}</code>
              <span v-if="row.description">{{ row.description }}</span>
            </div>
          </template>
          <template #cell-dataType="{ row }">
            <div class="operator-detail__type-cell">
              <span>{{ formatParameterDataType(row.dataType) }}</span>
              <code>{{ row.dataType }}</code>
            </div>
          </template>
          <template #cell-isRequired="{ row }">
            {{ row.isRequired ? '必需' : '可选' }}
          </template>
          <template #cell-defaultValue="{ row }">
            {{ formatMetadataValue(row.defaultValue) }}
          </template>
          <template #cell-range="{ row }">
            {{ parameterRange(row) }}
          </template>
        </CvDataTable>
      </CvPanel>
    </div>
  </section>
</template>

<style scoped>
.operator-detail { display: grid; gap: var(--cv-space-5); min-width: 0; }
.operator-detail__grid { display: grid; gap: var(--cv-space-6); min-width: 0; }
.operator-detail__back-link { display: inline-flex; align-items: center; gap: var(--cv-space-1); }
.operator-detail__back-link :deep(svg) { width: 14px; height: 14px; }
.operator-detail__status-row { display: flex; align-items: center; gap: var(--cv-space-3); margin-bottom: var(--cv-space-3); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.operator-detail__technical { margin-top: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); }
.operator-detail__technical summary { padding: var(--cv-space-3) 0; color: var(--cv-color-link); cursor: pointer; font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.operator-detail__technical summary:focus-visible { border-radius: var(--cv-radius-xs); outline: none; box-shadow: var(--cv-focus-ring); }
.operator-detail__identity-cell,
.operator-detail__type-cell { display: grid; gap: 2px; min-width: 0; }
.operator-detail__identity-cell code,
.operator-detail__type-cell code { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
.operator-detail__identity-cell > span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }

@media (max-width: 720px) {
  .operator-detail__status-row { align-items: flex-start; flex-direction: column; }
}
</style>
