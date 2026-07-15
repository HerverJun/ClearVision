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
import type { OperatorParameter, OperatorPort } from './operatorContracts';
import { createOperatorDetailQuery } from './operatorQueries';
import { useOperatorsReadRuntime, type OperatorsReadRuntime } from './operatorsReadRuntime';
import {
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
  { key: 'type', label: '算子类型标识', value: state.value.data.operatorType },
  { key: 'category', label: '分类', value: operatorCategoryLabels[state.value.data.categoryId] },
  { key: 'version', label: '当前版本', value: state.value.data.version },
  { key: 'hidden', label: '默认隐藏', value: state.value.data.defaultHidden ? '是' : '否' },
  { key: 'icon', label: '图标标识', value: state.value.data.iconName },
  { key: 'lifecycle-note', label: '生命周期说明', value: state.value.data.lifecycleNote, span: 2 },
  { key: 'keywords', label: '关键词', value: state.value.data.keywords.join('、'), span: 2 },
  { key: 'tags', label: '标签', value: state.value.data.tags.join('、'), span: 2 }
] : []);

const portColumns: readonly CvDataTableColumn<OperatorPort>[] = Object.freeze([
  { key: 'name', label: '名称', width: '24%' },
  { key: 'displayName', label: '显示名', width: '24%' },
  { key: 'dataType', label: '数据类型', width: '20%' },
  { key: 'isRequired', label: '必需', width: '12%' },
  { key: 'description', label: '说明', width: '20%' }
]);
const parameterColumns: readonly CvDataTableColumn<OperatorParameter>[] = Object.freeze([
  { key: 'name', label: '名称', width: '18%' },
  { key: 'displayName', label: '显示名', width: '18%' },
  { key: 'dataType', label: '类型', width: '12%' },
  { key: 'isRequired', label: '必需', width: '10%' },
  { key: 'defaultValue', label: '元数据默认值', width: '16%' },
  { key: 'range', label: '范围 / 选项', width: '26%' }
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
  <main
    class="operator-detail"
    data-capability="operators-read-detail"
  >
    <CvPageHeader
      :title="state.data?.displayName ?? '算子详情'"
      description="详情只读取当前分支的算子元数据，不调用预览、执行或参数推荐。"
    >
      <template #breadcrumbs>
        <RouterLink to="/operators">
          ← 返回算子库
        </RouterLink>
      </template>
      <template #actions>
        <CvButton
          size="sm"
          :loading="state.isRefreshing"
          loading-label="正在刷新算子详情"
          @click="detailQuery.refresh({ force: true })"
        >
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
      title="算子不存在（404）"
      description="该 OperatorType 不存在，或当前链接无效。"
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
        title="身份与生命周期"
        :description="state.data.description"
      >
        <div class="operator-detail__lifecycle">
          <CvStatusBadge :tone="lifecycleTone(state.data.lifecycle)">
            {{ operatorLifecycleLabels[state.data.lifecycle] }}
          </CvStatusBadge>
        </div>
        <CvDescriptionList
          :items="summary"
          label="算子身份"
        />
      </CvPanel>

      <CvPanel
        title="输入端口"
        description="当前分支运行时元数据。"
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
          <template #cell-isRequired="{ row }">
            {{ row.isRequired ? '是' : '否' }}
          </template>
          <template #cell-description="{ row }">
            {{ row.description || '—' }}
          </template>
        </CvDataTable>
      </CvPanel>

      <CvPanel
        title="输出端口"
        description="只展示当前接口返回的正式输出端口，不推断条件输出。"
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
          <template #cell-isRequired="{ row }">
            {{ row.isRequired ? '是' : '否' }}
          </template>
          <template #cell-description="{ row }">
            {{ row.description || '—' }}
          </template>
        </CvDataTable>
      </CvPanel>

      <CvPanel
        title="参数"
        description="展示当前分支元数据；页面不会生成、写入或推荐参数。"
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
          <template #cell-isRequired="{ row }">
            {{ row.isRequired ? '是' : '否' }}
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
  </main>
</template>

<style scoped>
.operator-detail { display: grid; gap: var(--cv-space-5); min-width: 0; }
.operator-detail__grid { display: grid; gap: var(--cv-space-4); min-width: 0; }
.operator-detail__lifecycle { margin-bottom: var(--cv-space-3); }
</style>
