<script setup lang="ts">
import { computed } from 'vue';
import { CvInlineAlert, CvPanel, CvStatusBadge, type CvStatusTone } from '@/design-system/primitives';
import type { AiAgentRunReplayDiagnosticsV1, AiBuildCheckV1, AiBuildResultV1 } from './contracts';

const props = defineProps<{
  build: AiBuildResultV1;
  stale: boolean;
  diagnostics: AiAgentRunReplayDiagnosticsV1 | null;
}>();

const checks = computed(() => [props.build.validation.structural, props.build.validation.dryRun, props.build.validation.manifest]);
const changeCount = computed(() => props.build.workflowDiff.addedNodes.length +
  props.build.workflowDiff.modifiedNodes.length + props.build.workflowDiff.removedNodes.length);
const validationBlockers = computed(() => [...new Set([
  ...props.build.validation.applyGate.applyBlockers,
  ...props.build.workflowDiff.validationFailures
])]);
const publicWarnings = computed(() => [...new Set(props.build.publicWarnings)]);
const deploymentBlockers = computed(() => [...new Set([
  ...props.build.validation.applyGate.deploymentBlockers,
  ...props.build.workflowDiff.deploymentBlockers
])]);
const candidateReviewReady = computed(() => !props.stale && props.build.validation.handoffEligible);
const canvasCandidateReady = computed(() => !props.stale && props.build.validation.applyGate.canvasApplyReady);
const runtimeDraftReady = computed(() => !props.stale && props.build.validation.applyGate.runtimeDraftReady);
const deploymentReady = computed(() => !props.stale && props.build.validation.applyGate.deploymentReady);
const warningCount = computed(() => publicWarnings.value.length +
  checks.value.reduce((total, check) => total + check.warningCount, 0));

function checkBadge(check: AiBuildCheckV1): Readonly<{ label: string; tone: CvStatusTone }> {
  if (check.status === 'passed') return Object.freeze({ label: '通过', tone: 'ok' });
  if (check.status === 'failed') return Object.freeze({ label: '未通过', tone: 'error' });
  return Object.freeze({ label: '待处理', tone: 'warning' });
}
</script>

<template>
  <CvPanel
    title="构建结果"
    variant="section"
    data-ai-build-workspace
  >
    <div class="ai-build-workspace">
      <CvInlineAlert
        v-if="stale"
        tone="warning"
        title="当前结果已过期"
      >
        参数、资源、方案或工程保存基线已变化；以下候选仅供查看，旧校验与候选就绪结论不再有效。
      </CvInlineAlert>

      <section
        class="ai-build-workspace__summary"
        aria-labelledby="ai-build-summary-title"
      >
        <div>
          <h3 id="ai-build-summary-title">
            流程候选摘要
          </h3>
          <p>{{ build.operatorPipeline.map(item => item.operatorType).join(' → ') || '未生成公开算子链' }}</p>
        </div>
        <dl>
          <div><dt>算子</dt><dd>{{ build.operatorCount }}</dd></div>
          <div><dt>连线</dt><dd>{{ build.connectionCount }}</dd></div>
          <div><dt>流程变化</dt><dd>{{ changeCount }}</dd></div>
          <div><dt>待处理</dt><dd>{{ build.parameterMapping.filter(item => item.pending && !item.resourceDependent).length + build.missingResources.length }}</dd></div>
        </dl>
      </section>

      <section
        class="ai-build-workspace__section"
        aria-labelledby="ai-build-validation-title"
      >
        <header>
          <h3 id="ai-build-validation-title">
            校验与运行预演
          </h3>
          <CvStatusBadge
            :tone="candidateReviewReady ? 'ok' : 'warning'"
            :label="stale ? '结论已失效' : candidateReviewReady ? '具备交接条件' : '尚未就绪'"
          />
        </header>
        <ul class="ai-build-workspace__checks">
          <li
            v-for="check in checks"
            :key="check.id"
          >
            <CvStatusBadge v-bind="checkBadge(check)" />
            <div><strong>{{ check.label }}</strong><p>{{ check.summary }}</p></div>
            <span>{{ check.blockerCount }} 阻断 / {{ check.warningCount }} 警告</span>
          </li>
        </ul>
      </section>

      <section
        v-if="validationBlockers.length || publicWarnings.length"
        class="ai-build-workspace__section"
        aria-labelledby="ai-build-issues-title"
      >
        <header>
          <h3 id="ai-build-issues-title">
            公开问题
          </h3>
          <span class="ai-build-workspace__issue-count">{{ validationBlockers.length }} 阻断 / {{ warningCount }} 警告</span>
        </header>
        <div class="ai-build-workspace__issues">
          <section v-if="validationBlockers.length">
            <h4>阻断</h4>
            <ul>
              <li
                v-for="blocker in validationBlockers"
                :key="blocker"
              >
                {{ blocker }}
              </li>
            </ul>
          </section>
          <section v-if="publicWarnings.length">
            <h4>警告</h4>
            <ul>
              <li
                v-for="warning in publicWarnings"
                :key="warning"
              >
                {{ warning }}
              </li>
            </ul>
          </section>
        </div>
      </section>

      <section
        class="ai-build-workspace__section"
        aria-labelledby="ai-build-gate-title"
      >
        <header>
          <h3 id="ai-build-gate-title">
            候选就绪条件
          </h3>
        </header>
        <CvInlineAlert
          :tone="stale ? 'warning' : candidateReviewReady ? 'success' : build.validation.applyGate.blocked ? 'error' : 'warning'"
          :title="stale ? '候选结论已失效' : candidateReviewReady ? '候选已具备交接条件' : '候选尚未具备交接条件'"
        >
          <template v-if="stale">
            请基于最新参数、资源、方案和工程保存基线重新构建并校验候选。
          </template>
          <template v-else>
            {{ build.validation.firstFixRecommendation }}
            <template v-if="candidateReviewReady">
              构建通过后可交接到工作区审核。
            </template>
          </template>
        </CvInlineAlert>
        <ul class="ai-build-workspace__boundaries">
          <li>
            <div><strong>工作区候选</strong><p>仅表示候选具备工作区审核条件。</p></div>
            <CvStatusBadge
              :tone="canvasCandidateReady ? 'ok' : 'warning'"
              :label="stale ? '结论已失效' : canvasCandidateReady ? '候选就绪' : '阻断'"
            />
          </li>
          <li>
            <div><strong>运行草稿</strong><p>仍需后续工作区审核，不写入正式工程。</p></div>
            <CvStatusBadge
              :tone="runtimeDraftReady ? 'ok' : 'warning'"
              :label="stale ? '结论已失效' : runtimeDraftReady ? '候选就绪' : '阻断'"
            />
          </li>
          <li>
            <div><strong>部署</strong><p>本阶段不执行交接、保存或部署。</p></div>
            <CvStatusBadge
              :tone="deploymentReady ? 'ok' : stale ? 'warning' : 'idle'"
              :label="stale ? '结论已失效' : deploymentReady ? '就绪' : '未就绪'"
            />
          </li>
        </ul>
        <ul
          v-if="deploymentBlockers.length"
          class="ai-build-workspace__deployment-blockers"
        >
          <li
            v-for="blocker in deploymentBlockers"
            :key="blocker"
          >
            {{ blocker }}
          </li>
        </ul>
      </section>

      <details class="ai-build-workspace__details">
        <summary>查看完整映射与公开诊断</summary>
        <div class="ai-build-workspace__detail-grid">
          <dl>
            <div><dt>构建编号</dt><dd>{{ build.buildId }}</dd></div>
            <div><dt>方案编号</dt><dd>{{ build.planId }}</dd></div>
            <div><dt>方案摘要</dt><dd>{{ build.planHash }}</dd></div>
            <div><dt>候选指纹</dt><dd>{{ build.candidateFlowFingerprint }}</dd></div>
            <div><dt>输入指纹</dt><dd>{{ build.submittedBuildFingerprint }}</dd></div>
            <div><dt>参数 / 资源版本</dt><dd>{{ build.answerRevision }} / {{ build.resourceRevision }}</dd></div>
            <template v-if="diagnostics">
              <div><dt>回放事件</dt><dd>{{ diagnostics.eventCount }}</dd></div>
              <div><dt>重复 / 丢弃 / 过期</dt><dd>{{ diagnostics.duplicateEventCount }} / {{ diagnostics.droppedEventCount }} / {{ diagnostics.staleEventCount }}</dd></div>
            </template>
          </dl>
          <section>
            <h4>流程差异</h4>
            <p>新增 {{ build.workflowDiff.addedNodes.length }}，修改 {{ build.workflowDiff.modifiedNodes.length }}，保留 {{ build.workflowDiff.preservedNodes.length }}，移除 {{ build.workflowDiff.removedNodes.length }}</p>
            <h4>公开阶段</h4>
            <ol>
              <li
                v-for="item in build.publicTimeline"
                :key="item.evidenceId || `${item.stage}-${item.toolName}`"
              >
                <strong>{{ item.outputSummary }}</strong><span>{{ item.status }}</span>
              </li>
            </ol>
          </section>
        </div>
      </details>
    </div>
  </CvPanel>
</template>

<style scoped>
.ai-build-workspace { display: grid; min-width: 0; }
.ai-build-workspace__summary { display: flex; min-width: 0; align-items: start; justify-content: space-between; gap: var(--cv-space-5); padding-block: 0 var(--cv-space-4); }
.ai-build-workspace h3, .ai-build-workspace h4 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.ai-build-workspace__summary p { max-width: 72ch; margin: var(--cv-space-1) 0 0; overflow-wrap: anywhere; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-build-workspace__summary dl { display: flex; flex-wrap: wrap; margin: 0; }
.ai-build-workspace__summary dl div { min-width: 74px; padding-inline: var(--cv-space-3); border-inline-start: 1px solid var(--cv-border-subtle); }
.ai-build-workspace__summary dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.ai-build-workspace__summary dd { margin: 2px 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-variant-numeric: tabular-nums; font-weight: var(--cv-font-weight-semibold); }
.ai-build-workspace__section { padding-block: var(--cv-space-4); border-block-start: 1px solid var(--cv-border-subtle); }
.ai-build-workspace__section > header { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); margin-bottom: var(--cv-space-3); }
.ai-build-workspace__checks { display: grid; margin: 0; padding: 0; list-style: none; }
.ai-build-workspace__checks li { display: grid; grid-template-columns: 64px minmax(0, 1fr) auto; align-items: center; gap: var(--cv-space-3); min-height: 52px; padding-block: var(--cv-space-2); border-block-start: 1px solid var(--cv-border-subtle); }
.ai-build-workspace__checks strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.ai-build-workspace__checks p { margin: 2px 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.ai-build-workspace__checks > li > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); white-space: nowrap; }
.ai-build-workspace__issue-count { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); white-space: nowrap; }
.ai-build-workspace__issues { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-5); }
.ai-build-workspace__issues ul, .ai-build-workspace__deployment-blockers { display: grid; gap: var(--cv-space-1); margin: var(--cv-space-2) 0 0; padding-inline-start: var(--cv-space-5); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.ai-build-workspace__boundaries { display: grid; margin: var(--cv-space-3) 0 0; padding: 0; list-style: none; }
.ai-build-workspace__boundaries li { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-4); padding-block: var(--cv-space-2); border-block-start: 1px solid var(--cv-border-subtle); }
.ai-build-workspace__boundaries strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.ai-build-workspace__boundaries p { margin: 2px 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.ai-build-workspace__details { padding-block: var(--cv-space-3); border-block-start: 1px solid var(--cv-border-subtle); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.ai-build-workspace__details summary { width: fit-content; cursor: pointer; color: var(--cv-color-link); font-weight: var(--cv-font-weight-medium); }
.ai-build-workspace__details summary:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 2px; }
.ai-build-workspace__detail-grid { display: grid; grid-template-columns: 1fr 1fr; gap: var(--cv-space-5); margin-top: var(--cv-space-3); }
.ai-build-workspace__detail-grid dl { display: grid; gap: var(--cv-space-2); margin: 0; }
.ai-build-workspace__detail-grid dl div { display: grid; grid-template-columns: 110px minmax(0, 1fr); gap: var(--cv-space-2); }
.ai-build-workspace__detail-grid dd { min-width: 0; margin: 0; overflow-wrap: anywhere; color: var(--cv-text-primary); font-family: var(--cv-font-family-mono); }
.ai-build-workspace__detail-grid ol { display: grid; gap: var(--cv-space-2); margin: var(--cv-space-2) 0 0; padding: 0; list-style: none; }
.ai-build-workspace__detail-grid li { display: flex; justify-content: space-between; gap: var(--cv-space-3); }
.ai-build-workspace__detail-grid li strong { color: var(--cv-text-primary); font-weight: var(--cv-font-weight-medium); }
@media (max-width: 900px) {
  .ai-build-workspace__summary { align-items: stretch; flex-direction: column; }
  .ai-build-workspace__issues { grid-template-columns: 1fr; }
  .ai-build-workspace__detail-grid { grid-template-columns: 1fr; }
}
@media (max-width: 620px) {
  .ai-build-workspace__checks li { grid-template-columns: 64px minmax(0, 1fr); }
  .ai-build-workspace__checks > li > span { grid-column: 2; white-space: normal; }
}
</style>
