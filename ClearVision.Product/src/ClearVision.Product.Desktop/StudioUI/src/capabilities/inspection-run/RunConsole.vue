<script setup lang="ts">
import { computed } from 'vue';
import { CvButton, CvStatusBadge, type CvStatusTone } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import { formatInspectionOutcome } from '@/shared/inspectionOutcome';
import type {
  RunConsoleAdmissionCheck,
  RunConsoleIdentityItem,
  RunConsoleMode,
  RunConsoleResultItem,
  RunConsoleStatistics,
  RunConsoleViolation
} from './runConsoleProjection';

const props = defineProps<{
  mode: RunConsoleMode;
  projectName: string;
  phaseLabel: string;
  tone: CvStatusTone;
  message: string;
  errorCode?: string | null;
  connected: boolean;
  reconnectAttempt?: number;
  pending: boolean;
  canStart: boolean;
  canStop: boolean;
  canReconcile: boolean;
  identity: readonly RunConsoleIdentityItem[];
  admission: readonly RunConsoleAdmissionCheck[];
  violations: readonly RunConsoleViolation[];
  statistics: RunConsoleStatistics;
  results: readonly RunConsoleResultItem[];
  startTestId?: string;
  stopTestId?: string;
  reconcileTestId?: string;
  latestResultTestId?: string;
}>();

const emit = defineEmits<{
  start: [];
  stop: [];
  reconcile: [];
  refreshAdmission: [];
}>();

const title = computed(() => props.mode === 'formal' ? '正式运行控制台' : '连续检测控制台');
const startLabel = computed(() => props.mode === 'formal' ? '正式运行' : '启动连续检测');
const reconcileLabel = computed(() => props.mode === 'formal' ? '查询运行结果' : '核对状态');
const latestResults = computed(() => props.results.slice(0, 8));
const integerFormatter = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 0 });
const percentFormatter = new Intl.NumberFormat('zh-CN', {
  style: 'percent',
  minimumFractionDigits: 1,
  maximumFractionDigits: 1
});
const integer = (value: number): string => integerFormatter.format(value);
const percent = (value: number | null): string => value == null ? '--' : percentFormatter.format(value);
const duration = (value: number | null): string => value == null
  ? '--'
  : integerFormatter.format(Math.round(value)) + '\u00a0ms';
const checkTone = (state: RunConsoleAdmissionCheck['state']): CvStatusTone => {
  if (state === 'pass') return 'ok';
  if (state === 'blocked') return 'ng';
  if (state === 'pending') return 'warning';
  return 'idle';
};
const checkLabel = (state: RunConsoleAdmissionCheck['state']): string => ({
  pass: '通过',
  blocked: '阻断',
  pending: '待处理',
  unknown: '待确认',
  'not-applicable': '不适用'
})[state];
const formattedTime = (value: string | null): string => {
  if (!value) return '--';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('zh-CN', {
    hour: '2-digit', minute: '2-digit', second: '2-digit'
  }).format(date);
};
</script>

<template>
  <section
    class="run-console"
    :data-run-mode="mode"
    data-testid="run-console"
  >
    <header class="run-console__header">
      <div class="run-console__heading">
        <h2 class="run-console__mode">
          {{ title }}
        </h2>
        <strong :title="projectName">{{ projectName }}</strong>
      </div>
      <div class="run-console__state">
        <CvStatusBadge
          :tone="tone"
          :label="phaseLabel"
        />
        <span :class="connected ? 'is-connected' : ''">
          {{ connected ? '实时已连接' : reconnectAttempt ? '实时恢复 ' + reconnectAttempt : '权威状态已读取' }}
        </span>
      </div>
      <div class="run-console__actions">
        <CvButton
          size="sm"
          variant="primary"
          :disabled="!canStart || pending"
          :loading="pending && !canStop"
          :data-testid="startTestId ?? 'run-console-start'"
          @click="emit('start')"
        >
          <template #leading>
            <CvIcon
              name="play"
              size="sm"
            />
          </template>
          {{ startLabel }}
        </CvButton>
        <CvButton
          v-if="canStop"
          size="sm"
          variant="danger"
          :disabled="pending"
          :data-testid="stopTestId ?? 'run-console-stop'"
          @click="emit('stop')"
        >
          <template #leading>
            <CvIcon
              name="square"
              size="sm"
            />
          </template>
          停止
        </CvButton>
        <CvButton
          v-if="canReconcile"
          size="sm"
          variant="secondary"
          :disabled="pending"
          :data-testid="reconcileTestId ?? 'run-console-reconcile'"
          @click="emit('reconcile')"
        >
          <template #leading>
            <CvIcon
              name="refresh"
              size="sm"
            />
          </template>
          {{ reconcileLabel }}
        </CvButton>
        <CvButton
          v-else-if="!canStop"
          size="sm"
          variant="quiet"
          :disabled="pending"
          data-testid="run-console-admission-refresh"
          @click="emit('refreshAdmission')"
        >
          <template #leading>
            <CvIcon
              name="refresh"
              size="sm"
            />
          </template>
          刷新准入
        </CvButton>
      </div>
    </header>

    <div
      class="run-console__metrics"
      aria-label="运行统计"
    >
      <div><span>总数</span><strong>{{ integer(statistics.total) }}</strong></div>
      <div><span>OK / NG</span><strong>{{ integer(statistics.ok) }} / {{ integer(statistics.ng) }}</strong></div>
      <div><span>有效判定良率</span><strong>{{ percent(statistics.yieldRate) }}</strong></div>
      <div><span>判定覆盖率</span><strong>{{ percent(statistics.decisionCoverageRate) }}</strong></div>
      <div><span>平均节拍</span><strong>{{ duration(statistics.averageProcessingTimeMs) }}</strong></div>
      <div><span>执行异常</span><strong>{{ integer(statistics.executionFailed) }}</strong></div>
    </div>

    <div class="run-console__body">
      <section
        class="run-console__control"
        aria-labelledby="run-console-control-title"
      >
        <h3 id="run-console-control-title">
          运行状态
        </h3>
        <slot name="configuration" />
        <p
          role="status"
          aria-live="polite"
        >
          {{ message }}
        </p>
        <code
          v-if="errorCode"
          translate="no"
        >{{ errorCode }}</code>
        <dl class="run-console__identity">
          <div
            v-for="item in identity"
            :key="item.key"
          >
            <dt>{{ item.label }}</dt>
            <dd
              :title="item.value"
              translate="no"
            >
              {{ item.value }}
            </dd>
          </div>
        </dl>
      </section>

      <section
        class="run-console__admission"
        aria-labelledby="run-console-admission-title"
      >
        <div class="run-console__section-title">
          <h3 id="run-console-admission-title">
            权威准入
          </h3>
          <span>{{ admission.filter(item => item.state === 'pass').length }}/{{ admission.length }}</span>
        </div>
        <ul>
          <li
            v-for="item in admission"
            :key="item.key"
          >
            <CvStatusBadge
              :tone="checkTone(item.state)"
              :label="checkLabel(item.state)"
            />
            <span><strong>{{ item.label }}</strong><small>{{ item.detail }}</small></span>
          </li>
        </ul>
        <div
          v-if="violations.length"
          class="run-console__violations"
        >
          <div
            v-for="item in violations"
            :key="item.key"
          >
            <code translate="no">{{ item.code }}</code>
            <span>{{ item.message }}</span>
            <small v-if="item.target">{{ item.target }}</small>
          </div>
        </div>
      </section>

      <section
        class="run-console__results"
        aria-labelledby="run-console-results-title"
      >
        <div class="run-console__section-title">
          <h3 id="run-console-results-title">
            近期结果
          </h3>
          <span>{{ results.length }}</span>
        </div>
        <p
          v-if="latestResults.length === 0"
          class="run-console__empty"
        >
          暂无本次会话结果
        </p>
        <ol v-else>
          <li
            v-for="(result, index) in latestResults"
            :key="result.id"
            :data-testid="index === 0 ? latestResultTestId : undefined"
          >
            <CvStatusBadge
              :tone="formatInspectionOutcome(result.outcome).tone"
              :label="formatInspectionOutcome(result.outcome).label"
            />
            <span>{{ formattedTime(result.timestamp) }}</span>
            <span>{{ duration(result.processingTimeMs) }}</span>
            <span>{{ result.defectCount == null ? '--' : result.defectCount + ' 缺陷' }}</span>
            <div
              v-if="$slots['result-action']"
              class="run-console__result-action"
            >
              <slot
                name="result-action"
                :result="result"
              />
            </div>
            <details v-if="result.diagnostics.length || result.errorMessage">
              <summary>诊断</summary>
              <p v-if="result.errorMessage">
                {{ result.errorMessage }}
              </p>
              <dl>
                <div
                  v-for="item in result.diagnostics"
                  :key="item.key"
                >
                  <dt>{{ item.label }}</dt><dd>{{ item.value }}</dd>
                </div>
              </dl>
            </details>
          </li>
        </ol>
      </section>
    </div>
  </section>
</template>

<style scoped>
.run-console { min-width: 0; display: grid; border-block: 1px solid var(--cv-border-subtle); background: var(--cv-surface-1); }
.run-console__header { min-width: 0; min-height: 52px; padding: var(--cv-space-2) var(--cv-space-3); display: grid; grid-template-columns: minmax(180px, 1fr) auto auto; align-items: center; gap: var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.run-console__heading { min-width: 0; display: flex; align-items: baseline; gap: var(--cv-space-2); }
.run-console__heading strong { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--cv-font-size-sm); }
.run-console__mode { flex: none; margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-weight: 500; text-wrap: balance; }
.run-console__state,.run-console__actions { display: flex; align-items: center; gap: var(--cv-space-2); }
.run-console__state > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.run-console__state > span.is-connected { color: var(--cv-color-status-ok-strong); }
.run-console__metrics { display: grid; grid-template-columns: repeat(6, minmax(96px, 1fr)); border-bottom: 1px solid var(--cv-border-subtle); }
.run-console__metrics > div { min-width: 0; padding: var(--cv-space-2) var(--cv-space-3); display: grid; gap: 2px; border-right: 1px solid var(--cv-border-subtle); }
.run-console__metrics > div:last-child { border-right: 0; }
.run-console__metrics span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.run-console__metrics strong { font-family: var(--cv-font-mono); font-size: var(--cv-font-size-sm); font-variant-numeric: tabular-nums; }
.run-console__body { min-width: 0; display: grid; grid-template-columns: minmax(240px, .8fr) minmax(300px, 1fr) minmax(340px, 1.2fr); }
.run-console__body > section { min-width: 0; padding: var(--cv-space-3); border-right: 1px solid var(--cv-border-subtle); }
.run-console__body > section:last-child { border-right: 0; }
.run-console h2,.run-console h3 { scroll-margin-top: var(--cv-space-4); }
.run-console h3 { margin: 0; font-size: var(--cv-font-size-xs); font-weight: 650; text-wrap: balance; }
.run-console__control > p { margin: var(--cv-space-2) 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: 1.45; }
.run-console__control > code { display: inline-block; color: var(--cv-color-status-ng-strong); font-size: 10px; overflow-wrap: anywhere; }
.run-console__identity { margin: var(--cv-space-2) 0 0; display: grid; gap: var(--cv-space-1); }
.run-console__identity div { min-width: 0; display: grid; grid-template-columns: 76px minmax(0, 1fr); gap: var(--cv-space-2); }
.run-console__identity dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.run-console__identity dd { margin: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: var(--cv-font-mono); font-size: 10px; }
.run-console__section-title { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
.run-console__section-title > span { color: var(--cv-text-muted); font-family: var(--cv-font-mono); font-size: 10px; }
.run-console__admission ul,.run-console__results ol { margin: var(--cv-space-2) 0 0; padding: 0; list-style: none; }
.run-console__admission li { min-width: 0; padding: var(--cv-space-1) 0; display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: start; gap: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); }
.run-console__admission li > span { min-width: 0; display: grid; gap: 1px; }
.run-console__admission li strong { font-size: var(--cv-font-size-2xs); font-weight: 600; }
.run-console__admission li small { color: var(--cv-text-muted); font-size: 10px; overflow-wrap: anywhere; }
.run-console__violations { max-height: 116px; margin-top: var(--cv-space-2); overflow: auto; }
.run-console__violations > div { padding: var(--cv-space-1); display: grid; grid-template-columns: minmax(100px, auto) minmax(0, 1fr); gap: var(--cv-space-2); background: var(--cv-color-status-ng-soft); font-size: 10px; }
.run-console__violations code { color: var(--cv-color-status-ng-strong); overflow-wrap: anywhere; }
.run-console__violations span,.run-console__violations small { overflow-wrap: anywhere; }
.run-console__violations small { grid-column: 2; color: var(--cv-text-muted); }
.run-console__results ol { max-height: 214px; overflow: auto; }
.run-console__results li { min-width: 0; padding: var(--cv-space-1) 0; display: grid; grid-template-columns: auto 64px 58px minmax(62px, 1fr) auto; align-items: center; gap: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); color: var(--cv-text-secondary); font-size: 10px; }
.run-console__result-action { justify-self: end; }
.run-console__result-action :deep(a) { color: var(--cv-color-link); font-size: var(--cv-font-size-2xs); font-weight: var(--cv-font-weight-medium); text-decoration: none; white-space: nowrap; }
.run-console__result-action :deep(a:hover) { text-decoration: underline; }
.run-console__result-action :deep(a:focus-visible) { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 2px; }
.run-console__results details { grid-column: 1 / -1; }
.run-console__results summary { cursor: pointer; color: var(--cv-text-muted); }
.run-console__results summary:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 2px; }
.run-console__results details p { margin: var(--cv-space-1) 0; color: var(--cv-color-status-ng-strong); }
.run-console__results details dl { margin: 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1px var(--cv-space-2); }
.run-console__results details dl div { min-width: 0; display: grid; grid-template-columns: minmax(70px, .8fr) minmax(0, 1.2fr); gap: var(--cv-space-1); }
.run-console__results details dt,.run-console__results details dd { min-width: 0; margin: 0; overflow-wrap: anywhere; }
.run-console__results details dt { color: var(--cv-text-muted); }
.run-console__empty { margin: var(--cv-space-4) 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); text-align: center; }
@media (max-width: 1180px) {
  .run-console__header { grid-template-columns: minmax(180px, 1fr) auto; }
  .run-console__actions { grid-column: 1 / -1; }
  .run-console__metrics { grid-template-columns: repeat(3, minmax(96px, 1fr)); }
  .run-console__body { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .run-console__results { grid-column: 1 / -1; border-top: 1px solid var(--cv-border-subtle); }
}
@media (max-width: 760px) {
  .run-console__header,.run-console__body { grid-template-columns: 1fr; }
  .run-console__state { justify-content: flex-start; }
  .run-console__metrics { grid-template-columns: repeat(2, minmax(96px, 1fr)); }
  .run-console__body > section { border-right: 0; border-bottom: 1px solid var(--cv-border-subtle); }
  .run-console__results { grid-column: auto; }
}
</style>
