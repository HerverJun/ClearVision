<script setup lang="ts">
import { computed } from 'vue';
import { CvDescriptionList, CvInlineAlert, CvPanel, CvStatusBadge, type CvDescriptionItem } from '@/design-system/primitives';
import type { AiAgentRunEventV1, AiPlanV1, AiReadinessPreviewV1, AiSessionDetailV1 } from './contracts';

const props = defineProps<{
  plan: AiPlanV1;
  readiness: AiReadinessPreviewV1 | null;
  session: AiSessionDetailV1;
  events: readonly AiAgentRunEventV1[];
}>();

const semantic = computed(() => props.plan.semanticExtraction);
const effectiveReadiness = computed(() => props.readiness?.buildReadiness ?? props.plan.buildReadiness);
const summaryItems = computed<readonly CvDescriptionItem[]>(() => Object.freeze([
  { key: 'object', label: '检测对象', value: semantic.value?.inspectionObject || '待确认' },
  { key: 'task', label: '任务类型', value: semantic.value?.taskType || props.plan.intent || '待确认' },
  { key: 'source', label: '图像来源', value: semantic.value?.imageSource || '待确认' },
  { key: 'target', label: '检测目标 / 缺陷', value: [semantic.value?.targetAttribute, semantic.value?.defectType, semantic.value?.measurementTarget].filter(Boolean).join('；') || '待确认', span: 2 },
  { key: 'judgement', label: 'OK / NG 判定', value: [semantic.value?.okCondition && `OK：${semantic.value.okCondition}`, semantic.value?.ngCondition && `NG：${semantic.value.ngCondition}`].filter(Boolean).join('；') || '待确认', span: 2 },
  { key: 'output', label: '输出目标', value: semantic.value?.outputTarget || '待确认' },
  { key: 'build', label: '构建条件', value: effectiveReadiness.value.canBuild ? '已具备条件' : '尚未具备条件' }
]));

const publicEvents = computed(() => props.events.filter(event =>
  event.eventType.startsWith('plan.') || event.eventType.startsWith('semantic.') || event.eventType.startsWith('run.')
));
</script>

<template>
  <CvPanel
    class="ai-plan-workspace"
    title="任务理解与视觉方案"
    data-ai-plan-workspace
  >
    <div class="ai-plan-workspace__content">
      <section
        class="ai-plan-workspace__section"
        aria-labelledby="ai-plan-summary-title"
      >
        <div class="ai-plan-workspace__section-heading">
          <h3 id="ai-plan-summary-title">
            任务理解
          </h3>
          <CvStatusBadge
            :tone="effectiveReadiness.canBuild ? 'ok' : 'warning'"
            :label="effectiveReadiness.canBuild ? '方案已具备构建条件' : `${effectiveReadiness.blockers.filter(item => item.blocksBuild).length} 项阻断`"
          />
        </div>
        <p class="ai-plan-workspace__goal">
          {{ plan.goal }}
        </p>
        <CvDescriptionList
          :items="summaryItems"
          :columns="2"
          label="AI 任务理解"
        />
      </section>

      <section
        class="ai-plan-workspace__section ai-plan-workspace__route"
        aria-labelledby="ai-plan-route-title"
      >
        <div>
          <h3 id="ai-plan-route-title">
            推荐视觉路线
          </h3>
          <strong>{{ plan.recommendedRoute.title || '待生成推荐路线' }}</strong>
          <p>{{ plan.recommendedRoute.summary || semantic?.suggestedRoute || '当前没有可公开的路线摘要。' }}</p>
        </div>
        <ol
          v-if="plan.executablePlan.length"
          class="ai-plan-workspace__steps"
        >
          <li
            v-for="(step, index) in plan.executablePlan"
            :key="`${index}-${step}`"
          >
            <span>{{ index + 1 }}</span><p>{{ step }}</p>
          </li>
        </ol>
      </section>

      <div class="ai-plan-workspace__split">
        <section
          class="ai-plan-workspace__section"
          aria-labelledby="ai-plan-assumptions-title"
        >
          <h3 id="ai-plan-assumptions-title">
            关键假设
          </h3>
          <ul
            v-if="plan.recommendedDefaults.length"
            class="ai-plan-workspace__list"
          >
            <li
              v-for="item in plan.recommendedDefaults"
              :key="item.id"
            >
              <strong>{{ item.label }}</strong>
              <span>{{ item.value }}</span>
              <p v-if="item.impact">
                {{ item.impact }}
              </p>
            </li>
          </ul>
          <p
            v-else
            class="ai-plan-workspace__empty"
          >
            没有需要公开的默认假设。
          </p>
        </section>

        <section
          class="ai-plan-workspace__section"
          aria-labelledby="ai-plan-acceptance-title"
        >
          <h3 id="ai-plan-acceptance-title">
            验收标准
          </h3>
          <ul
            v-if="plan.acceptanceCriteria.length"
            class="ai-plan-workspace__list ai-plan-workspace__list--plain"
          >
            <li
              v-for="criterion in plan.acceptanceCriteria"
              :key="criterion"
            >
              {{ criterion }}
            </li>
          </ul>
          <p
            v-else
            class="ai-plan-workspace__empty"
          >
            尚无公开验收标准。
          </p>
        </section>
      </div>

      <section
        v-if="effectiveReadiness.blockers.length || effectiveReadiness.missingResources.length"
        class="ai-plan-workspace__section"
        aria-labelledby="ai-plan-blockers-title"
      >
        <h3 id="ai-plan-blockers-title">
          阻断项
        </h3>
        <div class="ai-plan-workspace__alerts">
          <CvInlineAlert
            v-for="blocker in effectiveReadiness.blockers.filter(item => item.blocksBuild)"
            :key="blocker.id"
            tone="warning"
            compact
            :title="blocker.publicLabel || blocker.field || '待确认条件'"
          >
            {{ blocker.resource?.description || `需要处理：${blocker.resolutionMode || blocker.category}` }}
          </CvInlineAlert>
          <CvInlineAlert
            v-for="resource in effectiveReadiness.missingResources"
            :key="resource.canonicalId"
            tone="warning"
            compact
            :title="resource.resourceName || resource.resourceType || '资源尚未绑定'"
          >
            {{ resource.description || '资源合同尚未开放，当前不提供绑定入口。' }}
          </CvInlineAlert>
        </div>
      </section>

      <details class="ai-plan-workspace__diagnostics">
        <summary>工程诊断与公开规划详情</summary>
        <div class="ai-plan-workspace__diagnostic-grid">
          <dl>
            <div><dt>方案编号</dt><dd>{{ plan.planId }}</dd></div>
            <div><dt>方案摘要</dt><dd>{{ plan.planHash }}</dd></div>
            <div><dt>会话版本</dt><dd>{{ session.snapshot.revision }}</dd></div>
            <div><dt>Planner 来源</dt><dd>{{ plan.planSource || '未标记' }}</dd></div>
            <div><dt>Fallback 原因</dt><dd>{{ plan.fallbackReason || '无' }}</dd></div>
            <div><dt>合同版本</dt><dd>{{ plan.planContractVersion }}</dd></div>
          </dl>
          <section>
            <h4>完整算子候选</h4>
            <p>{{ plan.recommendedRoute.operators.join('、') || '无公开候选' }}</p>
            <h4>公开 validation</h4>
            <ul>
              <li
                v-for="warning in plan.planWarnings"
                :key="warning"
              >
                {{ warning }}
              </li>
            </ul>
          </section>
        </div>
        <ol class="ai-plan-workspace__timeline">
          <li
            v-for="event in publicEvents"
            :key="`${event.runId}-${event.sequence}`"
          >
            <span>{{ event.sequence }}</span>
            <div><strong>{{ event.title }}</strong><p>{{ event.summary }}</p></div>
          </li>
        </ol>
      </details>
    </div>
  </CvPanel>
</template>

<style scoped>
.ai-plan-workspace__content { display: grid; }
.ai-plan-workspace__section { min-width: 0; padding-block: var(--cv-space-4); border-block-start: 1px solid var(--cv-border-subtle); }
.ai-plan-workspace__section:first-child { padding-block-start: 0; border-block-start: 0; }
.ai-plan-workspace__section h3 { margin: 0 0 var(--cv-space-2); color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-tight); }
.ai-plan-workspace__section-heading { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.ai-plan-workspace__goal { max-width: 72ch; margin: 0 0 var(--cv-space-4); color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); line-height: 1.65; }
.ai-plan-workspace__route { display: grid; grid-template-columns: minmax(240px, 0.8fr) minmax(320px, 1.2fr); gap: var(--cv-space-6); }
.ai-plan-workspace__route strong { display: block; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.ai-plan-workspace__route p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-plan-workspace__steps { display: grid; gap: var(--cv-space-2); margin: 0; padding: 0; list-style: none; }
.ai-plan-workspace__steps li { display: grid; grid-template-columns: 24px minmax(0, 1fr); align-items: start; gap: var(--cv-space-2); }
.ai-plan-workspace__steps span { display: grid; width: 22px; height: 22px; place-items: center; border: 1px solid var(--cv-border-default); border-radius: 50%; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); font-variant-numeric: tabular-nums; }
.ai-plan-workspace__steps p { margin: 2px 0 0; color: var(--cv-text-primary); }
.ai-plan-workspace__split { display: grid; grid-template-columns: 1fr 1fr; gap: var(--cv-space-6); border-block-start: 1px solid var(--cv-border-subtle); }
.ai-plan-workspace__split .ai-plan-workspace__section { border-block-start: 0; }
.ai-plan-workspace__list { display: grid; gap: var(--cv-space-3); margin: 0; padding: 0; list-style: none; }
.ai-plan-workspace__list li { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-plan-workspace__list strong, .ai-plan-workspace__list span { display: block; }
.ai-plan-workspace__list span, .ai-plan-workspace__list p { color: var(--cv-text-secondary); }
.ai-plan-workspace__list p { margin: 2px 0 0; }
.ai-plan-workspace__list--plain { padding-inline-start: var(--cv-space-4); list-style: disc; }
.ai-plan-workspace__empty { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.ai-plan-workspace__alerts { display: grid; gap: var(--cv-space-2); }
.ai-plan-workspace__diagnostics { padding-block: var(--cv-space-3); border-block-start: 1px solid var(--cv-border-subtle); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.ai-plan-workspace__diagnostics summary { width: fit-content; cursor: pointer; color: var(--cv-color-link); font-weight: var(--cv-font-weight-medium); }
.ai-plan-workspace__diagnostics summary:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 2px; }
.ai-plan-workspace__diagnostic-grid { display: grid; grid-template-columns: 1fr 1fr; gap: var(--cv-space-5); margin-top: var(--cv-space-3); }
.ai-plan-workspace__diagnostic-grid dl { display: grid; gap: var(--cv-space-2); margin: 0; }
.ai-plan-workspace__diagnostic-grid dl div { display: grid; grid-template-columns: 108px minmax(0, 1fr); gap: var(--cv-space-2); }
.ai-plan-workspace__diagnostic-grid dt { color: var(--cv-text-muted); }
.ai-plan-workspace__diagnostic-grid dd { min-width: 0; margin: 0; overflow-wrap: anywhere; color: var(--cv-text-primary); font-family: var(--cv-font-family-mono); }
.ai-plan-workspace__diagnostic-grid h4 { margin: 0 0 var(--cv-space-1); color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.ai-plan-workspace__diagnostic-grid p, .ai-plan-workspace__diagnostic-grid ul { margin: 0 0 var(--cv-space-3); }
.ai-plan-workspace__timeline { display: grid; gap: var(--cv-space-2); margin: var(--cv-space-4) 0 0; padding: 0; list-style: none; }
.ai-plan-workspace__timeline li { display: grid; grid-template-columns: 34px minmax(0, 1fr); gap: var(--cv-space-2); }
.ai-plan-workspace__timeline > li > span { color: var(--cv-text-muted); font-family: var(--cv-font-family-mono); font-variant-numeric: tabular-nums; }
.ai-plan-workspace__timeline strong { color: var(--cv-text-primary); }
.ai-plan-workspace__timeline p { margin: 1px 0 0; }

@media (max-width: 920px) {
  .ai-plan-workspace__route, .ai-plan-workspace__split, .ai-plan-workspace__diagnostic-grid { grid-template-columns: 1fr; gap: var(--cv-space-3); }
}
</style>
