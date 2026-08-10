<script setup lang="ts">
import { computed } from 'vue';
import { CvInlineAlert, CvStatusBadge, type CvStatusTone } from '@/design-system/primitives';
import type { AiWorkbenchProjection } from './projection';
import type { AiWorkbenchPhase, AiWorkbenchState } from './reducer';
import AiWorkbenchDrawer from './AiWorkbenchDrawer.vue';

const props = defineProps<{
  open: boolean;
  state: AiWorkbenchState;
  projection: AiWorkbenchProjection;
}>();

const emit = defineEmits<{
  close: [];
}>();

interface PublicStage {
  readonly id: string;
  readonly label: string;
  readonly status: 'completed' | 'current' | 'pending' | 'blocked';
}

const phaseOrder: Readonly<Record<AiWorkbenchPhase, number>> = {
  'session-loading': 0,
  idle: 0,
  'intent-routing': 0,
  planning: 1,
  clarifying: 1,
  'plan-blocked': 1,
  'plan-ready': 1,
  'plan-failed': 1,
  cancelling: 1,
  cancelled: 1,
  'build-starting': 2,
  building: 2,
  validating: 3,
  'parameters-pending': 3,
  'resources-pending': 3,
  'build-blocked': 3,
  revalidating: 3,
  'build-ready': 3,
  'build-failed': 3,
  'build-cancelling': 2,
  'build-cancelled': 2,
  'handoff-creating': 4,
  'handoff-unknown-outcome': 4,
  'handoff-created': 4,
  'baseline-conflict': 2,
  'unknown-outcome': 2,
  recovering: 1,
  'session-conflict': 1,
  'offline-or-service-unavailable': 0,
  disposed: 0
};

const blockedPhases = new Set<AiWorkbenchPhase>([
  'plan-blocked', 'plan-failed', 'build-blocked', 'build-failed', 'baseline-conflict',
  'session-conflict', 'unknown-outcome', 'handoff-unknown-outcome', 'offline-or-service-unavailable'
]);

const stages = computed<readonly PublicStage[]>(() => {
  const current = phaseOrder[props.state.phase];
  const labels = ['任务理解', '方案规划', '候选构建', '验证与处理', '工作区交接'];
  return Object.freeze(labels.map((label, index) => Object.freeze({
    id: `stage-${index}`,
    label,
    status: index < current
      ? 'completed' as const
      : index > current
        ? 'pending' as const
        : blockedPhases.has(props.state.phase)
          ? 'blocked' as const
          : 'current' as const
  })));
});

const blockers = computed(() => {
  if (props.state.build) {
    return Object.freeze([...new Set([
      ...props.state.build.validation.applyGate.applyBlockers,
      ...props.state.build.workflowDiff.validationFailures,
      ...props.state.build.workflowDiff.deploymentBlockers
    ])].filter(Boolean));
  }
  const readiness = props.state.readiness?.buildReadiness ?? props.state.plan?.buildReadiness;
  return Object.freeze((readiness?.blockers ?? [])
    .filter(blocker => blocker.blocksBuild)
    .map(blocker => blocker.publicLabel || blocker.field));
});

const warnings = computed(() => Object.freeze([...new Set([
  ...(props.state.plan?.planWarnings ?? []),
  ...(props.state.build?.publicWarnings ?? [])
])].filter(Boolean)));

const firstFixRecommendation = computed(() =>
  props.state.build?.validation.firstFixRecommendation ||
  props.state.build?.validation.applyGate.firstFixRecommendation ||
  props.projection.nextHint
);

const recoveryLabel = computed(() => {
  if (props.state.phase === 'recovering') return '正在按服务端回放恢复';
  if (props.state.phase === 'unknown-outcome' || props.state.phase === 'handoff-unknown-outcome') {
    return '结果待协调，禁止重复写入';
  }
  if (props.state.run.terminalSequence !== null) return '运行终态已确认';
  if (props.state.run.runId) return '运行中，可由公开回放恢复';
  return '当前无待恢复运行';
});

function stageTone(status: PublicStage['status']): CvStatusTone {
  if (status === 'completed') return 'ok';
  if (status === 'current') return 'info';
  if (status === 'blocked') return 'warning';
  return 'idle';
}

function stageStatusLabel(status: PublicStage['status']): string {
  if (status === 'completed') return '已完成';
  if (status === 'current') return '当前阶段';
  if (status === 'blocked') return '当前受阻';
  return '待开始';
}
</script>

<template>
  <AiWorkbenchDrawer
    :open="open"
    title="公开诊断"
    description="服务端公开投影与脱敏摘要。"
    @close="emit('close')"
  >
    <section class="ai-diagnostics__section">
      <div class="ai-diagnostics__heading">
        <h3>阶段时间线</h3>
        <CvStatusBadge
          :tone="projection.statusTone"
          :label="projection.statusLabel"
        />
      </div>
      <ol class="ai-diagnostics__timeline">
        <li
          v-for="stage in stages"
          :key="stage.id"
          :data-status="stage.status"
        >
          <span
            class="ai-diagnostics__stage-marker"
            aria-hidden="true"
          />
          <strong>{{ stage.label }}</strong>
          <CvStatusBadge
            :tone="stageTone(stage.status)"
            :label="stageStatusLabel(stage.status)"
          />
        </li>
      </ol>
    </section>

    <section class="ai-diagnostics__section">
      <h3>恢复状态</h3>
      <dl class="ai-diagnostics__facts">
        <div>
          <dt>恢复</dt>
          <dd>{{ recoveryLabel }}</dd>
        </div>
        <div>
          <dt>会话版本</dt>
          <dd>{{ state.session?.snapshot.revision ?? '未建立' }}</dd>
        </div>
        <div>
          <dt>工程保存基线</dt>
          <dd>{{ state.projectBaseline?.targetKind === 'existing' ? `已绑定 · 保存修订 ${state.projectBaseline.persistenceRevision}` : '新工程尚无工程保存基线' }}</dd>
        </div>
        <div>
          <dt>方案</dt>
          <dd>{{ state.plan ? '已生成公开方案' : state.run.kind === 'plan' ? '正在规划' : '尚未生成' }}</dd>
        </div>
        <div>
          <dt>构建</dt>
          <dd>{{ state.build ? `${state.build.operatorCount} 个算子 · ${state.build.connectionCount} 条连接` : state.run.kind === 'build' ? '正在构建' : '尚未构建' }}</dd>
        </div>
        <div>
          <dt>交接候选</dt>
          <dd>{{ state.handoff ? `已创建 · ${state.handoff.status === 'available' ? '待工作区接收' : state.handoff.status}` : '尚未创建' }}</dd>
        </div>
      </dl>
    </section>

    <CvInlineAlert
      v-if="state.errorCode"
      tone="error"
      title="公开错误代码"
    >
      <code translate="no">{{ state.errorCode }}</code>
      <p>{{ state.message }}</p>
    </CvInlineAlert>

    <section class="ai-diagnostics__section">
      <div class="ai-diagnostics__heading">
        <h3>阻断与警告</h3>
        <span>{{ blockers.length }} 阻断 · {{ warnings.length }} 警告</span>
      </div>
      <div
        v-if="blockers.length === 0 && warnings.length === 0"
        class="ai-diagnostics__clear"
      >
        当前没有公开阻断或警告。
      </div>
      <div
        v-else
        class="ai-diagnostics__issues"
      >
        <div v-if="blockers.length">
          <h4>阻断</h4>
          <ul>
            <li
              v-for="blocker in blockers"
              :key="blocker"
            >
              {{ blocker }}
            </li>
          </ul>
        </div>
        <div v-if="warnings.length">
          <h4>警告</h4>
          <ul>
            <li
              v-for="warning in warnings"
              :key="warning"
            >
              {{ warning }}
            </li>
          </ul>
        </div>
      </div>
      <div class="ai-diagnostics__recommendation">
        <strong>首要修复建议</strong>
        <p>{{ firstFixRecommendation }}</p>
      </div>
    </section>
  </AiWorkbenchDrawer>
</template>

<style scoped>
.ai-diagnostics__section { min-width: 0; padding-block: var(--cv-space-4); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-diagnostics__section:first-child { padding-block-start: 0; }
.ai-diagnostics__section:last-child { border-block-end: 0; }
.ai-diagnostics__section h3 { margin: 0; font-size: var(--cv-font-size-sm); }
.ai-diagnostics__heading { display: flex; min-width: 0; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
.ai-diagnostics__heading > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.ai-diagnostics__timeline { display: grid; margin: var(--cv-space-3) 0 0; padding: 0; list-style: none; }
.ai-diagnostics__timeline li { display: grid; min-width: 0; grid-template-columns: 16px minmax(0, 1fr) auto; align-items: center; gap: var(--cv-space-2); min-height: 36px; }
.ai-diagnostics__stage-marker { width: 8px; height: 8px; justify-self: center; border: 1px solid var(--cv-border-strong); border-radius: 50%; background: var(--cv-surface-raised); }
.ai-diagnostics__timeline li[data-status="completed"] > .ai-diagnostics__stage-marker { border-color: var(--cv-color-status-ok); background: var(--cv-color-status-ok); }
.ai-diagnostics__timeline li[data-status="current"] > .ai-diagnostics__stage-marker { border-color: var(--cv-color-status-info); background: var(--cv-color-status-info); }
.ai-diagnostics__timeline li[data-status="blocked"] > .ai-diagnostics__stage-marker { border-color: var(--cv-color-status-warning); background: var(--cv-color-status-warning); }
.ai-diagnostics__timeline strong { font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.ai-diagnostics__facts { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); margin: var(--cv-space-3) 0 0; border-block: 1px solid var(--cv-border-subtle); }
.ai-diagnostics__facts div { min-width: 0; padding: var(--cv-space-2) var(--cv-space-3); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-diagnostics__facts dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.ai-diagnostics__facts dd { margin: 2px 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); overflow-wrap: anywhere; font-variant-numeric: tabular-nums; }
.ai-diagnostics__clear { margin-block-start: var(--cv-space-3); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.ai-diagnostics__issues { display: grid; gap: var(--cv-space-3); margin-block-start: var(--cv-space-3); }
.ai-diagnostics__issues h4 { margin: 0; font-size: var(--cv-font-size-xs); }
.ai-diagnostics__issues ul { display: grid; gap: var(--cv-space-1); margin: var(--cv-space-1) 0 0; padding-inline-start: var(--cv-space-5); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-diagnostics__issues li { overflow-wrap: anywhere; }
.ai-diagnostics__recommendation { margin-block-start: var(--cv-space-3); padding: var(--cv-space-3); background: var(--cv-color-status-info-soft); }
.ai-diagnostics__recommendation strong { font-size: var(--cv-font-size-xs); }
.ai-diagnostics__recommendation p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); overflow-wrap: anywhere; }
code { display: inline-block; max-width: 100%; overflow-wrap: anywhere; }

@media (max-width: 520px) {
  .ai-diagnostics__facts { grid-template-columns: 1fr; }
}
</style>
