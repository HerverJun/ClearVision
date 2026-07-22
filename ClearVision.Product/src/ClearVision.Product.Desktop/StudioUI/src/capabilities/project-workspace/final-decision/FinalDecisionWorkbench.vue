<script setup lang="ts">
import { computed } from 'vue';
import { CvButton, CvModal } from '@/design-system';
import type { WorkspaceDecisionComparator, WorkspaceMissingDecisionPolicy } from '../workspaceContracts';
import { finalDecisionCandidateKey, type FinalDecisionOwner } from './finalDecisionOwner';

const props = defineProps<{ open: boolean; owner: FinalDecisionOwner; readonly: boolean }>();
const emit = defineEmits<{ close: [] }>();
const binding = computed(() => props.owner.projection.draft?.finalDecisionBinding ?? null);
const selectedKey = computed(() => binding.value ? `${binding.value.sourceOperatorId}:${binding.value.sourceOutputPortId}` : '');
function select(event: Event): void { props.owner.selectCandidate((event.target as HTMLSelectElement).value); }
function patchText(field: 'okValue'|'ngValue', event: Event): void { props.owner.patchBinding({ [field]: (event.target as HTMLInputElement).value }); }
function patchThreshold(event: Event): void { props.owner.patchBinding({ threshold: Number((event.target as HTMLInputElement).value) }); }
function patchComparator(event: Event): void { props.owner.patchBinding({ comparator: (event.target as HTMLSelectElement).value as WorkspaceDecisionComparator }); }
function setPolicy(event: Event): void { props.owner.setMissingPolicy((event.target as HTMLSelectElement).value as WorkspaceMissingDecisionPolicy); }
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
      class="decision-workbench"
      data-capability="final-decision-workbench"
      :data-phase="owner.projection.phase"
      :data-dirty="owner.projection.dirty"
      :data-draft-hash="owner.projection.draftFingerprint"
    >
      <label>候选输出<select
        :value="selectedKey"
        :disabled="readonly || owner.projection.phase === 'validating'"
        @change="select"
      ><option value="">未配置</option><option
        v-for="candidate in owner.projection.candidates"
        :key="finalDecisionCandidateKey(candidate)"
        :value="finalDecisionCandidateKey(candidate)"
      >{{ candidate.operatorName }} / {{ candidate.outputName }} · {{ candidate.dataType }}</option></select></label>
      <template v-if="binding">
        <dl><div><dt>来源算子</dt><dd>{{ binding.sourceOperatorId }}</dd></div><div><dt>输出</dt><dd>{{ binding.sourceOutputName }}</dd></div><div><dt>规则</dt><dd>{{ binding.rule.value }}</dd></div><div><dt>草稿</dt><dd>r{{ owner.projection.draftRevision }} / {{ owner.projection.draftFingerprint }}</dd></div></dl>
        <label
          v-if="binding.rule.value === 'Boolean'"
          class="decision-workbench__check"
        ><input
          type="checkbox"
          :checked="binding.trueMeansOk"
          :disabled="readonly"
          @change="owner.patchBinding({ trueMeansOk: ($event.target as HTMLInputElement).checked })"
        >true 表示 OK</label>
        <div
          v-else-if="binding.rule.value === 'StringMap'"
          class="decision-workbench__pair"
        >
          <label>OK 值<input
            :value="binding.okValue ?? ''"
            :disabled="readonly"
            @input="patchText('okValue',$event)"
          ></label><label>NG 值<input
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
            :value="binding.comparator?.value ?? 'GreaterThanOrEqual'"
            :disabled="readonly"
            @change="patchComparator"
          ><option>Equal</option><option>NotEqual</option><option>GreaterThan</option><option>GreaterThanOrEqual</option><option>LessThan</option><option>LessThanOrEqual</option></select></label><label>阈值<input
            type="number"
            step="any"
            :value="binding.threshold ?? 0"
            :disabled="readonly"
            @input="patchThreshold"
          ></label>
        </div>
      </template>
      <label>信号缺失策略<select
        :value="owner.projection.draft?.missingDecisionPolicy.value ?? 'Undetermined'"
        :disabled="readonly"
        @change="setPolicy"
      ><option value="Undetermined">未判定</option><option value="NotApplicable">不适用</option><option value="Invalid">无效</option></select></label>
      <div
        v-if="owner.projection.issues.length"
        class="decision-workbench__issues"
        role="alert"
      >
        <strong>后端校验未通过</strong><ul>
          <li
            v-for="issue in owner.projection.issues"
            :key="`${issue.code}-${issue.field}`"
          >
            <code>{{ issue.code }}</code> {{ issue.message }}<small>{{ issue.field }}</small>
          </li>
        </ul>
      </div>
      <div class="decision-workbench__status">
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
        :disabled="readonly"
        @click="owner.clearBinding"
      >
        清除配置
      </CvButton><span class="decision-workbench__spacer" /><CvButton
        variant="quiet"
        @click="owner.cancel(); emit('close')"
      >
        取消
      </CvButton><CvButton
        variant="primary"
        :disabled="readonly || owner.projection.phase === 'validating'"
        @click="owner.apply().then(ok => ok && emit('close'))"
      >
        校验并应用
      </CvButton>
    </template>
  </CvModal>
</template>

<style scoped>
.decision-workbench { min-height: 360px; display: grid; align-content: start; gap: var(--cv-space-4); }.decision-workbench label { display: grid; gap: var(--cv-space-1); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }.decision-workbench input,.decision-workbench select { width: 100%; min-width: 0; height: var(--cv-density-control-height); padding: 0 var(--cv-space-2); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); font: inherit; }.decision-workbench dl { margin: 0; display: grid; grid-template-columns:repeat(2,minmax(0,1fr)); border: 1px solid var(--cv-border-subtle); }.decision-workbench dl div { padding: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); }.decision-workbench dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }.decision-workbench dd { margin: 2px 0 0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; font-size:var(--cv-font-size-xs); }.decision-workbench__check { display:flex!important; align-items:center; }.decision-workbench__check input { width:auto; }.decision-workbench__pair { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:var(--cv-space-3); }.decision-workbench__issues { padding:var(--cv-space-3); border:1px solid var(--cv-color-status-ng-border); background:var(--cv-color-status-ng-soft); color:var(--cv-color-status-ng-strong); font-size:var(--cv-font-size-xs); }.decision-workbench__issues ul { margin:var(--cv-space-1) 0 0; padding-left:18px; }.decision-workbench__issues small { display:block; color:var(--cv-text-muted); }.decision-workbench__status { display:flex; align-items:center; justify-content:space-between; gap:var(--cv-space-3); color:var(--cv-text-secondary); font-size:var(--cv-font-size-xs); }.decision-workbench__spacer { flex:1; }
</style>
