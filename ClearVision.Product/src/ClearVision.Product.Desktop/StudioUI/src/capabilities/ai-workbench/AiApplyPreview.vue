<script setup lang="ts">
import { computed } from 'vue';
import { CvInlineAlert, CvPanel, CvStatusBadge, type CvStatusTone } from '@/design-system/primitives';
import type { AiBuildCheckV1, AiBuildResultV1, AiProjectContextV1 } from './contracts';

const props = defineProps<{
  build: AiBuildResultV1;
  project: AiProjectContextV1 | null;
  stale: boolean;
}>();

const checks = computed(() => [
  props.build.validation.structural,
  props.build.validation.dryRun,
  props.build.validation.manifest
]);
const confirmedParameters = computed(() => props.build.parameterMapping.filter(item => !item.pending));
const resourceBindings = computed(() => confirmedParameters.value.filter(item => item.resourceDependent));
const blockers = computed(() => [...new Set([
  ...props.build.validation.applyGate.applyBlockers,
  ...props.build.workflowDiff.validationFailures
])]);
const warnings = computed(() => [...new Set([
  ...props.build.publicWarnings,
  ...props.build.validation.applyGate.deploymentBlockers
])]);
const targetLabel = computed(() => props.build.projectBaseline.targetKind === 'new'
  ? '未保存的新工程'
  : props.project?.name ?? '当前既有工程');
const baselineLabel = computed(() => props.build.projectBaseline.targetKind === 'new'
  ? '缺少正式工程标识、保存修订或流程基线'
  : `工程版本 ${props.build.projectBaseline.persistenceRevision ?? 0}`);
const eligible = computed(() => !props.stale && props.build.validation.handoffEligible &&
  !props.build.validation.applyGate.blocked && props.build.validation.applyGate.canvasApplyReady &&
  props.build.validation.applyGate.runtimeDraftReady);

function checkBadge(check: AiBuildCheckV1): Readonly<{ label: string; tone: CvStatusTone }> {
  if (check.status === 'passed') return Object.freeze({ label: '通过', tone: 'ok' });
  if (check.status === 'failed') return Object.freeze({ label: '阻断', tone: 'error' });
  return Object.freeze({ label: '待处理', tone: 'warning' });
}
</script>

<template>
  <CvPanel
    title="应用预览"
    data-ai-apply-preview
  >
    <div class="ai-apply-preview">
      <header class="ai-apply-preview__header">
        <div>
          <h3>{{ targetLabel }}</h3>
          <p>{{ baselineLabel }}</p>
        </div>
        <CvStatusBadge
          :tone="eligible ? 'ok' : 'warning'"
          :label="eligible ? '可交接审核' : stale ? '结论已失效' : '存在阻断'"
        />
      </header>

      <CvInlineAlert
        :tone="eligible ? 'info' : 'warning'"
        :title="eligible ? '交接后进入工作区本地草稿' : '当前不能交接'"
      >
        交接只会让工作区加载候选草稿，不会自动保存工程、运行流程或部署运行包。
      </CvInlineAlert>

      <section
        class="ai-apply-preview__identity"
        aria-labelledby="apply-preview-identity"
      >
        <h4 id="apply-preview-identity">
          方案与构建
        </h4>
        <dl>
          <div><dt>方案</dt><dd>{{ build.planId }}</dd></div>
          <div><dt>构建</dt><dd>{{ build.buildId }}</dd></div>
          <div><dt>候选规模</dt><dd>{{ build.operatorCount }} 个算子 / {{ build.connectionCount }} 条连线</dd></div>
          <div><dt>输入版本</dt><dd>参数 {{ build.answerRevision }} / 资源 {{ build.resourceRevision }}</dd></div>
        </dl>
      </section>

      <section
        class="ai-apply-preview__section"
        aria-labelledby="apply-preview-diff"
      >
        <header>
          <h4 id="apply-preview-diff">
            候选差异
          </h4>
          <span>新增 {{ build.workflowDiff.addedNodes.length }} / 修改 {{ build.workflowDiff.modifiedNodes.length }} / 删除 {{ build.workflowDiff.removedNodes.length }}</span>
        </header>
        <div class="ai-apply-preview__columns">
          <div>
            <h5>算子与连线</h5>
            <p>{{ build.operatorPipeline.map(item => item.operatorType).join(' → ') || '无公开算子变更' }}</p>
            <ul v-if="build.workflowDiff.removedNodes.length">
              <li
                v-for="item in build.workflowDiff.removedNodes"
                :key="item"
              >
                删除：{{ item }}
              </li>
            </ul>
          </div>
          <div>
            <h5>参数确认</h5>
            <ul v-if="confirmedParameters.length">
              <li
                v-for="item in confirmedParameters.slice(0, 8)"
                :key="item.canonicalKey"
              >
                {{ item.operatorDisplayName || item.operatorType }} · {{ item.parameterDisplayName || item.parameterName }}：{{ item.valueSummary || '已确认' }}
              </li>
            </ul>
            <p v-else>
              没有需要展示的已确认参数。
            </p>
          </div>
          <div>
            <h5>资源绑定</h5>
            <ul v-if="resourceBindings.length">
              <li
                v-for="item in resourceBindings"
                :key="item.canonicalKey"
              >
                {{ item.operatorDisplayName || item.operatorType }} · {{ item.parameterDisplayName || item.parameterName }}：{{ item.valueSummary || '已绑定 canonical identity' }}
              </li>
            </ul>
            <p v-else>
              候选不包含已确认的资源绑定。
            </p>
          </div>
        </div>
      </section>

      <section
        class="ai-apply-preview__section"
        aria-labelledby="apply-preview-gate"
      >
        <header>
          <h4 id="apply-preview-gate">
            验证、运行预演与交接门禁
          </h4>
          <span>{{ blockers.length }} 阻断 / {{ warnings.length }} 警告</span>
        </header>
        <ul class="ai-apply-preview__checks">
          <li
            v-for="check in checks"
            :key="check.id"
          >
            <CvStatusBadge v-bind="checkBadge(check)" />
            <div><strong>{{ check.label }}</strong><p>{{ check.summary }}</p></div>
          </li>
        </ul>
        <div
          v-if="blockers.length || warnings.length"
          class="ai-apply-preview__issues"
        >
          <ul v-if="blockers.length">
            <li
              v-for="item in blockers"
              :key="item"
            >
              {{ item }}
            </li>
          </ul>
          <ul v-if="warnings.length">
            <li
              v-for="item in warnings"
              :key="item"
            >
              {{ item }}
            </li>
          </ul>
        </div>
      </section>

      <details class="ai-apply-preview__technical">
        <summary>查看技术身份</summary>
        <dl>
          <div><dt>Plan Hash</dt><dd>{{ build.planHash }}</dd></div>
          <div><dt>Candidate fingerprint</dt><dd>{{ build.candidateFlowFingerprint }}</dd></div>
        </dl>
      </details>
    </div>
  </CvPanel>
</template>

<style scoped>
.ai-apply-preview { display: grid; min-width: 0; }
.ai-apply-preview__header { display: flex; min-width: 0; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); padding-bottom: var(--cv-space-4); }
.ai-apply-preview h3, .ai-apply-preview h4, .ai-apply-preview h5 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.ai-apply-preview__header p, .ai-apply-preview__columns p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-apply-preview__identity, .ai-apply-preview__section, .ai-apply-preview__technical { padding-block: var(--cv-space-4); border-block-start: 1px solid var(--cv-border-subtle); }
.ai-apply-preview__identity dl { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: var(--cv-space-3); margin: var(--cv-space-3) 0 0; }
.ai-apply-preview__identity dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.ai-apply-preview__identity dd { min-width: 0; margin: 2px 0 0; overflow-wrap: anywhere; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.ai-apply-preview__section > header { display: flex; align-items: baseline; justify-content: space-between; gap: var(--cv-space-3); }
.ai-apply-preview__section > header span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); white-space: nowrap; }
.ai-apply-preview__columns { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--cv-space-5); margin-top: var(--cv-space-3); }
.ai-apply-preview__columns > div + div { padding-inline-start: var(--cv-space-4); border-inline-start: 1px solid var(--cv-border-subtle); }
.ai-apply-preview ul { display: grid; gap: var(--cv-space-1); margin: var(--cv-space-2) 0 0; padding-inline-start: var(--cv-space-5); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-apply-preview__checks { padding: 0 !important; list-style: none; }
.ai-apply-preview__checks li { display: grid; grid-template-columns: 64px minmax(0, 1fr); align-items: center; gap: var(--cv-space-3); min-height: 48px; border-block-start: 1px solid var(--cv-border-subtle); }
.ai-apply-preview__checks strong { color: var(--cv-text-primary); }
.ai-apply-preview__checks p { margin: 2px 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.ai-apply-preview__issues { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); }
.ai-apply-preview__technical summary { width: fit-content; cursor: pointer; color: var(--cv-color-link); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.ai-apply-preview__technical summary:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 2px; }
.ai-apply-preview__technical dl { display: grid; gap: var(--cv-space-2); margin: var(--cv-space-3) 0 0; }
.ai-apply-preview__technical dl div { display: grid; grid-template-columns: 150px minmax(0, 1fr); gap: var(--cv-space-3); }
.ai-apply-preview__technical dd { min-width: 0; margin: 0; overflow-wrap: anywhere; color: var(--cv-text-primary); font-family: var(--cv-font-family-mono); font-size: var(--cv-font-size-xs); }
@media (max-width: 980px) {
  .ai-apply-preview__identity dl { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .ai-apply-preview__columns { grid-template-columns: 1fr; }
  .ai-apply-preview__columns > div + div { padding-block-start: var(--cv-space-3); padding-inline-start: 0; border-block-start: 1px solid var(--cv-border-subtle); border-inline-start: 0; }
}
@media (max-width: 620px) {
  .ai-apply-preview__header, .ai-apply-preview__section > header { align-items: flex-start; flex-direction: column; }
  .ai-apply-preview__identity dl, .ai-apply-preview__issues { grid-template-columns: 1fr; }
  .ai-apply-preview__section > header span { white-space: normal; }
}
</style>
