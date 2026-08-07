<script setup lang="ts">
import { computed, shallowRef } from 'vue';
import {
  CvButton,
  CvInlineAlert,
  CvModal,
  CvSelect,
  CvStatusBadge,
  type CvSelectOption,
  type CvStatusTone
} from '@/design-system';
import type {
  ResultsExportFormat,
  ResultsExportOwner
} from './resultsExportOwner';

const props = defineProps<{
  open: boolean;
  projectName: string;
  owner: ResultsExportOwner;
}>();

const emit = defineEmits<{ close: [] }>();
const formatModel = shallowRef<ResultsExportFormat>('csv');

const formatOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'csv', label: 'CSV（常用结果字段）' },
  { value: 'json', label: 'JSON（结构化结果）' }
]);

const busy = computed(() => [
  'creating', 'reconciling', 'queued', 'running', 'cancelling', 'downloading'
].includes(props.owner.projection.phase));
const statusTone = computed<CvStatusTone>(() => ({
  idle: 'idle', creating: 'info', reconciling: 'warning', queued: 'info', running: 'info',
  cancelling: 'warning', downloading: 'info', completed: 'ok', cancelled: 'warning',
  failed: 'error', expired: 'warning', forbidden: 'warning', error: 'error',
  'unknown-outcome': 'warning', disposed: 'idle'
}[props.owner.projection.phase] as CvStatusTone));
const statusLabel = computed(() => ({
  idle: '等待导出', creating: '正在创建任务', reconciling: '正在对账', queued: '已排队',
  running: '正在生成', cancelling: '正在取消', downloading: '正在下载', completed: '已完成',
  cancelled: '已取消', failed: '生成失败', expired: '文件已过期', forbidden: '权限不足',
  error: '导出异常', 'unknown-outcome': '结果未知', disposed: '已关闭'
}[props.owner.projection.phase] ?? '状态未知'));
const scopeTimeLabel = computed(() => {
  const scope = props.owner.projection.scope;
  if (!scope.startTime && !scope.endTime) return '当前工程全部历史结果';
  return `${scope.startTime || '最早'} 至 ${scope.endTime || '现在'}`;
});
const scopeStatusLabel = computed(() => props.owner.projection.scope.status || '全部标准结果');
const scopeDefectLabel = computed(() => props.owner.projection.scope.defectType || '全部缺陷类型');
const scopeDiagnosticLabel = computed(() => props.owner.projection.scope.diagnosticCode || '全部诊断码');

function close(): void {
  if (busy.value) return;
  emit('close');
}

function start(): void {
  void props.owner.start(formatModel.value);
}
</script>

<template>
  <CvModal
    :open="open"
    title="导出完整结果"
    description="由服务端按当前工程和筛选条件读取完整结果范围。"
    size="md"
    :close-on-backdrop="false"
    @close="close"
  >
    <div
      class="results-export-dialog cv-workbench"
      data-capability="results-export"
      :data-phase="owner.projection.phase"
    >
      <section
        class="results-export-dialog__scope"
        aria-labelledby="results-export-scope-title"
      >
        <div class="results-export-dialog__section-heading">
          <h3 id="results-export-scope-title">
            导出范围
          </h3>
          <CvStatusBadge
            tone="info"
            :label="owner.projection.scope.source === 'local' ? '本机结果' : '不可用来源'"
          />
        </div>
        <dl>
          <div><dt>工程</dt><dd>{{ projectName }}</dd></div>
          <div><dt>时间范围</dt><dd>{{ scopeTimeLabel }}</dd></div>
          <div><dt>标准结果</dt><dd>{{ scopeStatusLabel }}</dd></div>
          <div><dt>缺陷类型</dt><dd>{{ scopeDefectLabel }}</dd></div>
          <div><dt>诊断码</dt><dd>{{ scopeDiagnosticLabel }}</dd></div>
        </dl>
        <p class="results-export-dialog__scope-note">
          结果文件包含服务端当前工程历史查询能读取的全部匹配记录，不受当前页分页大小影响。
        </p>
      </section>

      <CvSelect
        v-model="formatModel"
        name="resultsExportFormat"
        label="文件格式"
        hint="CSV 便于表格分析；JSON 保留结构化缺陷明细。"
        :options="formatOptions"
        :disabled="busy || !owner.projection.canStart"
        data-modal-initial-focus
      />

      <div
        class="results-export-dialog__status cv-workbench-status"
        :data-phase="owner.projection.phase"
        role="status"
        aria-live="polite"
      >
        <CvStatusBadge
          :tone="statusTone"
          :label="statusLabel"
        />
        <p>{{ owner.projection.message }}</p>
      </div>

      <CvInlineAlert
        v-if="owner.projection.phase === 'unknown-outcome'"
        tone="warning"
        title="创建结果尚未确认"
      >
        当前不会自动重发创建请求。请按操作身份查询已有任务；确认任务状态前不要重复导出。
      </CvInlineAlert>

      <CvInlineAlert
        v-else-if="owner.projection.phase === 'forbidden'"
        tone="warning"
        title="当前会话无权导出"
      >
        服务端权限策略拒绝了结果导出请求；仅在界面隐藏按钮不能替代该检查。
      </CvInlineAlert>

      <section
        v-if="owner.projection.job"
        class="results-export-dialog__job"
        aria-labelledby="results-export-job-title"
      >
        <div class="results-export-dialog__section-heading">
          <h3 id="results-export-job-title">
            任务身份
          </h3>
          <span>{{ owner.projection.job.format.toUpperCase() }}</span>
        </div>
        <dl>
          <div><dt>导出任务</dt><dd><code translate="no">{{ owner.projection.job.exportId }}</code></dd></div>
          <div><dt>结果文件</dt><dd>{{ owner.projection.job.fileName }}</dd></div>
          <div><dt>快照上界</dt><dd>{{ owner.projection.job.snapshotUpperBoundUtc || '创建时记录' }}</dd></div>
          <div><dt>文件状态</dt><dd>{{ owner.projection.job.downloadAvailable ? '可下载' : '不可下载或已过期' }}</dd></div>
        </dl>
      </section>

      <details class="results-export-dialog__technical cv-technical-detail">
        <summary>技术追溯</summary>
        <dl>
          <div><dt>工程标识</dt><dd><code translate="no">{{ owner.projection.scope.projectId }}</code></dd></div>
          <div><dt>clientOperationId</dt><dd><code translate="no">{{ owner.projection.clientOperationId || '尚未创建' }}</code></dd></div>
          <div v-if="owner.projection.downloadedSha256">
            <dt>下载校验和</dt><dd><code translate="no">{{ owner.projection.downloadedSha256 }}</code></dd>
          </div>
        </dl>
      </details>
    </div>

    <template #footer>
      <CvButton
        v-if="owner.projection.canCancel"
        size="sm"
        variant="quiet"
        @click="owner.cancel"
      >
        取消导出
      </CvButton>
      <CvButton
        v-if="owner.projection.canReconcile"
        size="sm"
        variant="secondary"
        @click="owner.reconcile"
      >
        查询已有任务
      </CvButton>
      <CvButton
        size="sm"
        variant="quiet"
        :disabled="busy"
        @click="close"
      >
        关闭
      </CvButton>
      <CvButton
        v-if="owner.projection.canDownload"
        size="sm"
        variant="primary"
        @click="owner.download"
      >
        下载文件
      </CvButton>
      <CvButton
        v-if="owner.projection.canStart"
        size="sm"
        variant="primary"
        @click="start"
      >
        {{ owner.projection.phase === 'idle' ? '开始导出' : '重新导出' }}
      </CvButton>
    </template>
  </CvModal>
</template>

<style scoped>
.results-export-dialog { display: grid; gap: var(--cv-space-4); }
.results-export-dialog h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); }
.results-export-dialog__section-heading { min-height: 28px; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.results-export-dialog__section-heading > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.results-export-dialog dl { margin: var(--cv-space-2) 0 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); border-block: 1px solid var(--cv-border-subtle); }
.results-export-dialog dl div { min-width: 0; padding: var(--cv-space-2) var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.results-export-dialog dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.results-export-dialog dd { margin: 2px 0 0; overflow-wrap: anywhere; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.results-export-dialog code { overflow-wrap: anywhere; color: var(--cv-text-primary); font-size: var(--cv-font-size-2xs); user-select: all; }
.results-export-dialog__scope-note { margin: var(--cv-space-2) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.results-export-dialog__status { display: flex; align-items: flex-start; gap: var(--cv-space-2); }
.results-export-dialog__status p { min-width: 0; margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); overflow-wrap: anywhere; }
.results-export-dialog__job { padding-top: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); }
.results-export-dialog__technical { border-top: 1px solid var(--cv-border-subtle); }
.results-export-dialog__technical dl { grid-template-columns: 1fr; }
@media (max-width: 560px) {
  .results-export-dialog dl { grid-template-columns: 1fr; }
}
</style>
