<script setup lang="ts">
import { computed, shallowRef } from 'vue';
import { CvButton, CvModal, CvStatusBadge, type CvStatusTone } from '@/design-system';
import type { WorkspaceDecisionComparator, WorkspaceMissingDecisionPolicy } from '../workspaceContracts';
import { finalDecisionCandidateKey, type FinalDecisionOwner } from './finalDecisionOwner';

const props = defineProps<{ open: boolean; owner: FinalDecisionOwner; readonly: boolean }>();
const emit = defineEmits<{ close: [] }>();
const binding = computed(() => props.owner.projection.draft?.finalDecisionBinding ?? null);
const selectedKey = computed(() => binding.value ? `${binding.value.sourceOperatorId}:${binding.value.sourceOutputPortId}` : '');
const copiedField = shallowRef<string | null>(null);
const sourceOperatorName = computed(() => {
  const sourceOperatorId = binding.value?.sourceOperatorId;
  return props.owner.projection.candidates.find(candidate => candidate.operatorId === sourceOperatorId)?.operatorName ?? '当前算子';
});
const statusTone = computed<CvStatusTone>(() => {
  if (props.owner.projection.issues.length > 0 || props.owner.projection.phase === 'error') return 'error';
  if (props.readonly || props.owner.projection.dirty) return 'warning';
  if (binding.value) return 'ok';
  return 'idle';
});
const statusLabel = computed(() => {
  if (props.readonly) return '只读';
  if (props.owner.projection.issues.length > 0) return '需要修正';
  if (props.owner.projection.phase === 'validating') return '正在校验';
  if (props.owner.projection.dirty) return '存在未应用修改';
  return binding.value ? '判定已配置' : '尚未配置';
});
function select(event: Event): void { props.owner.selectCandidate((event.target as HTMLSelectElement).value); }
function patchText(field: 'okValue'|'ngValue', event: Event): void { props.owner.patchBinding({ [field]: (event.target as HTMLInputElement).value }); }
function patchThreshold(event: Event): void { props.owner.patchBinding({ threshold: Number((event.target as HTMLInputElement).value) }); }
function patchComparator(event: Event): void { props.owner.patchBinding({ comparator: (event.target as HTMLSelectElement).value as WorkspaceDecisionComparator }); }
function setPolicy(event: Event): void { props.owner.setMissingPolicy((event.target as HTMLSelectElement).value as WorkspaceMissingDecisionPolicy); }
function ruleLabel(value: string): string {
  return ({ Boolean: '布尔判定', StringMap: '文本映射', NumericComparison: '数值比较' } as Readonly<Record<string, string>>)[value] ?? '后端规则';
}
function dataTypeLabel(value: string): string {
  return ({ Boolean: '布尔值', String: '文本', Integer: '整数', Float: '数值' } as Readonly<Record<string, string>>)[value] ?? '结构化输出';
}
async function copyField(key: string, value: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value);
    copiedField.value = key;
  } catch {
    copiedField.value = null;
  }
}
</script>

<template>
  <CvModal
    :open="open"
    title="最终判定"
    description="候选与规则由后端目录校验；应用后随工程统一保存。"
    size="md"
    :close-on-backdrop="false"
    @close="emit('close')"
  >
    <div
      class="decision-workbench cv-workbench"
      data-capability="final-decision-workbench"
      :data-phase="owner.projection.phase"
      :data-dirty="owner.projection.dirty"
      :data-readonly="readonly"
      :data-draft-hash="owner.projection.draftFingerprint"
    >
      <label>候选输出<select
        name="final-decision-candidate"
        :value="selectedKey"
        :disabled="readonly || owner.projection.phase === 'validating'"
        @change="select"
      ><option value="">未配置</option><option
        v-for="candidate in owner.projection.candidates"
        :key="finalDecisionCandidateKey(candidate)"
        :value="finalDecisionCandidateKey(candidate)"
      >{{ candidate.operatorName }} / {{ candidate.outputName }} · {{ dataTypeLabel(candidate.dataType) }}</option></select></label>
      <template v-if="binding">
        <dl><div><dt>来源</dt><dd>{{ sourceOperatorName }}</dd></div><div><dt>输出</dt><dd>{{ binding.sourceOutputName }}</dd></div><div><dt>判定方式</dt><dd>{{ ruleLabel(binding.rule.value) }}</dd></div><div><dt>保存范围</dt><dd>随工程统一保存</dd></div></dl>
        <label
          v-if="binding.rule.value === 'Boolean'"
          class="decision-workbench__check"
        ><input
          type="checkbox"
          name="final-decision-true-means-ok"
          :checked="binding.trueMeansOk"
          :disabled="readonly"
          @change="owner.patchBinding({ trueMeansOk: ($event.target as HTMLInputElement).checked })"
        >值为真时判定为 OK</label>
        <div
          v-else-if="binding.rule.value === 'StringMap'"
          class="decision-workbench__pair"
        >
          <label>OK 值<input
            name="final-decision-ok-value"
            autocomplete="off"
            :value="binding.okValue ?? ''"
            :disabled="readonly"
            @input="patchText('okValue',$event)"
          ></label><label>NG 值<input
            name="final-decision-ng-value"
            autocomplete="off"
            :value="binding.ngValue ?? ''"
            :disabled="readonly"
            @input="patchText('ngValue',$event)"
          ></label>
        </div>
        <div
          v-else
          class="decision-workbench__pair"
        >
          <label>比较符<select
            name="final-decision-comparator"
            :value="binding.comparator?.value ?? 'GreaterThanOrEqual'"
            :disabled="readonly"
            @change="patchComparator"
          ><option value="Equal">等于</option><option value="NotEqual">不等于</option><option value="GreaterThan">大于</option><option value="GreaterThanOrEqual">大于或等于</option><option value="LessThan">小于</option><option value="LessThanOrEqual">小于或等于</option></select></label><label>阈值<input
            type="number"
            name="final-decision-threshold"
            step="any"
            :value="binding.threshold ?? 0"
            :disabled="readonly"
            @input="patchThreshold"
          ></label>
        </div>
        <details class="decision-workbench__technical cv-technical-detail">
          <summary>技术详情</summary>
          <dl>
            <div>
              <dt>来源算子标识</dt><dd class="cv-copyable-value">
                <code translate="no">{{ binding.sourceOperatorId }}</code><CvButton
                  size="sm"
                  variant="quiet"
                  @click="copyField('operator', binding.sourceOperatorId)"
                >
                  {{ copiedField === 'operator' ? '已复制' : '复制' }}
                </CvButton>
              </dd>
            </div>
            <div><dt>草稿修订</dt><dd><code translate="no">r{{ owner.projection.draftRevision }}</code></dd></div>
            <div>
              <dt>草稿指纹</dt><dd class="cv-copyable-value">
                <code translate="no">{{ owner.projection.draftFingerprint }}</code><CvButton
                  size="sm"
                  variant="quiet"
                  @click="copyField('fingerprint', owner.projection.draftFingerprint)"
                >
                  {{ copiedField === 'fingerprint' ? '已复制' : '复制' }}
                </CvButton>
              </dd>
            </div>
          </dl>
        </details>
      </template>
      <label>信号缺失策略<select
        name="final-decision-missing-policy"
        :value="owner.projection.draft?.missingDecisionPolicy.value ?? 'Undetermined'"
        :disabled="readonly"
        @change="setPolicy"
      ><option value="Undetermined">未判定</option><option value="NotApplicable">不适用</option><option value="Invalid">无效</option></select></label>
      <div
        v-if="owner.projection.issues.length"
        class="decision-workbench__issues cv-workbench-error"
        role="alert"
      >
        <strong>后端校验未通过</strong><ul>
          <li
            v-for="issue in owner.projection.issues"
            :key="`${issue.code}-${issue.field}`"
          >
            <span>{{ issue.message }}</span>
            <details class="decision-workbench__issue-detail">
              <summary>诊断信息</summary>
              <code translate="no">{{ issue.code }} · {{ issue.field }}</code>
            </details>
          </li>
        </ul>
      </div>
      <div
        class="decision-workbench__status cv-workbench-status"
        role="status"
        aria-live="polite"
        :data-tone="statusTone"
      >
        <CvStatusBadge
          :tone="statusTone"
          :label="statusLabel"
        />
        <span>{{ owner.projection.message }}</span><CvButton
          size="sm"
          variant="quiet"
          :disabled="owner.projection.phase === 'validating'"
          @click="owner.validate"
        >
          重新校验
        </CvButton>
      </div>
    </div>
    <template #footer>
      <CvButton
        v-if="binding"
        variant="danger"
        :disabled="readonly || owner.projection.phase === 'validating'"
        @click="owner.clearBinding"
      >
        清除配置
      </CvButton><span class="cv-workbench-footer-spacer" /><CvButton
        variant="quiet"
        @click="owner.cancel(); emit('close')"
      >
        取消
      </CvButton><CvButton
        variant="primary"
        :disabled="readonly || !owner.projection.dirty || owner.projection.phase === 'validating'"
        :loading="owner.projection.phase === 'validating'"
        loading-label="正在校验最终判定"
        @click="owner.apply().then(ok => ok && emit('close'))"
      >
        校验并应用
      </CvButton>
    </template>
  </CvModal>
</template>

<style scoped>
.decision-workbench { min-height: 360px; align-content: start; gap: var(--cv-space-4); }.decision-workbench label { display: grid; gap: var(--cv-space-1); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }.decision-workbench dl { margin: 0; display: grid; grid-template-columns:repeat(2,minmax(0,1fr)); border: 1px solid var(--cv-border-subtle); }.decision-workbench dl div { min-width: 0; padding: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); }.decision-workbench dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }.decision-workbench dd { margin: 2px 0 0; min-width: 0; overflow-wrap: anywhere; font-size:var(--cv-font-size-xs); }.decision-workbench__check { display:flex!important; align-items:center; }.decision-workbench__check input { width:auto; }.decision-workbench__pair { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:var(--cv-space-3); }.decision-workbench__issues ul { margin:var(--cv-space-1) 0 0; padding-left:18px; }.decision-workbench__issues li { margin-top: var(--cv-space-1); }.decision-workbench__issue-detail summary { cursor: pointer; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }.decision-workbench__issue-detail code { overflow-wrap: anywhere; font-size: 10px; }.decision-workbench__status { justify-content:flex-start; }.decision-workbench__status > span { min-width: 0; flex: 1; overflow-wrap: anywhere; }
.decision-workbench__technical dl { border: 0; }
@media (max-width: 720px) { .decision-workbench__pair,.decision-workbench dl { grid-template-columns: 1fr; } }
</style>
