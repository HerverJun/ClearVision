<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, shallowRef, watch } from 'vue';
import { CvButton, CvField, CvInlineAlert, CvPanel } from '@/design-system';
import type { SettingsOwner } from './settingsOwner';
import type { SettingsRuntimeProjectionV1 } from './decoder';
import { settingsFeedbackForResult, type SettingsFeedback } from './settingsViewModel';
import SettingsWriteFeedback from './SettingsWriteFeedback.vue';

const props = defineProps<{
  projection: SettingsRuntimeProjectionV1;
  owner: SettingsOwner;
  canWrite: boolean;
}>();

interface RuntimeDraft {
  autoRun: boolean;
  stopOnConsecutiveNg: string;
  missingMaterialTimeoutSeconds: string;
  applyProtectionRules: boolean;
}

function copy(value: SettingsRuntimeProjectionV1): RuntimeDraft {
  return {
    autoRun: value.autoRun,
    stopOnConsecutiveNg: String(value.stopOnConsecutiveNg),
    missingMaterialTimeoutSeconds: String(value.missingMaterialTimeoutSeconds),
    applyProtectionRules: value.applyProtectionRules
  };
}

const draft = reactive<RuntimeDraft>(copy(props.projection));
const baseline = shallowRef<RuntimeDraft>(copy(props.projection));
const busy = shallowRef(false);
const feedback = shallowRef<SettingsFeedback | null>(null);
const dirty = computed(() => JSON.stringify(draft) !== JSON.stringify(baseline.value));
const detachPanelState = props.owner.registerPanelState('runtime', () => ({
  dirty: dirty.value,
  pending: busy.value
}));
watch([dirty, busy], () => props.owner.refreshPanelState());
const validationMessage = computed(() => {
  const stopOnNg = Number(draft.stopOnConsecutiveNg);
  const missingMaterial = Number(draft.missingMaterialTimeoutSeconds);
  if (!Number.isInteger(stopOnNg) || stopOnNg < 0) return '连续 NG 停止次数必须是非负整数。';
  if (!Number.isInteger(missingMaterial) || missingMaterial < 0) return '缺料超时必须是非负整数。';
  return null;
});

watch(() => props.projection, value => {
  const wasDirty = dirty.value;
  const next = copy(value);
  baseline.value = next;
  if (!wasDirty) Object.assign(draft, next);
  feedback.value = null;
});

function discard(): void {
  Object.assign(draft, baseline.value);
  feedback.value = null;
}

async function save(): Promise<void> {
  if (!props.canWrite || busy.value || !dirty.value || validationMessage.value) return;
  busy.value = true;
  feedback.value = null;
  try {
    const result = await props.owner.saveGenericSection('runtime', {
      autoRun: draft.autoRun,
      stopOnConsecutiveNg: Number(draft.stopOnConsecutiveNg),
      missingMaterialTimeoutSeconds: Number(draft.missingMaterialTimeoutSeconds),
      applyProtectionRules: draft.applyProtectionRules
    });
    feedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') {
      const next = copy(result.value.config.sections.runtime!);
      baseline.value = next;
      Object.assign(draft, next);
    }
  } finally {
    busy.value = false;
  }
}

onBeforeUnmount(() => detachPanelState());
</script>

<template>
  <CvPanel
    title="运行保护"
    description="只编辑运行保护的四个范围字段；RuntimePreview Pilot 继续保持仅开发者可用。"
    data-settings-section="runtime"
  >
    <div class="settings-form-grid">
      <label class="settings-toggle">
        <input
          v-model="draft.autoRun"
          type="checkbox"
          :disabled="!canWrite"
        >
        <span>
          <strong>自动运行</strong>
          <small>由服务端运行时使用方决定具体生效时机。</small>
        </span>
      </label>
      <label class="settings-toggle">
        <input
          v-model="draft.applyProtectionRules"
          type="checkbox"
          :disabled="!canWrite"
        >
        <span>
          <strong>启用保护规则</strong>
          <small>保存成功不等于所有运行中的使用方已重新加载。</small>
        </span>
      </label>
      <CvField
        v-model="draft.stopOnConsecutiveNg"
        label="连续 NG 停止次数"
        name="stopOnConsecutiveNg"
        type="number"
        :readonly="!canWrite"
        :error="canWrite ? validationMessage ?? undefined : undefined"
      />
      <CvField
        v-model="draft.missingMaterialTimeoutSeconds"
        label="缺料超时（秒）"
        name="missingMaterialTimeoutSeconds"
        type="number"
        :readonly="!canWrite"
        :error="canWrite ? validationMessage ?? undefined : undefined"
      />
    </div>

    <CvInlineAlert
      class="settings-runtime__developer-note"
      tone="info"
      title="RuntimePreview Pilot 未进入普通设置"
    >
      仅元数据的 Pilot 仍由仅开发者可用的界面管理，本分区不会显示、保存或扩展它。
    </CvInlineAlert>

    <CvInlineAlert
      v-if="!canWrite"
      class="settings-panel__notice"
      tone="info"
      title="当前为安全视图"
    >
      当前角色可以核对已返回字段，但不能修改运行保护配置。
    </CvInlineAlert>

    <template #footer>
      <div class="settings-panel__footer">
        <span class="settings-panel__dirty">{{ dirty ? '有未保存修改' : '与服务端配置一致' }}</span>
        <div class="settings-panel__actions">
          <CvButton
            v-if="canWrite"
            variant="quiet"
            size="sm"
            :disabled="!dirty || busy"
            @click="discard"
          >
            放弃修改
          </CvButton>
          <CvButton
            v-if="canWrite"
            variant="primary"
            size="sm"
            :loading="busy"
            loading-label="正在保存运行保护"
            :disabled="!dirty || Boolean(validationMessage)"
            @click="save"
          >
            保存运行保护
          </CvButton>
        </div>
      </div>
    </template>
  </CvPanel>

  <SettingsWriteFeedback :feedback="feedback" />
</template>

<style scoped>
.settings-form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); }
.settings-toggle { display: flex; min-width: 0; align-items: flex-start; gap: var(--cv-space-3); padding: var(--cv-space-3); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); cursor: pointer; }
.settings-toggle input { width: 18px; height: 18px; flex: 0 0 auto; margin-top: 2px; accent-color: var(--cv-color-brand-500); }
.settings-toggle span { display: grid; min-width: 0; gap: var(--cv-space-1); }
.settings-toggle strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-toggle small { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.settings-runtime__developer-note { margin-top: var(--cv-space-4); }
.settings-panel__notice { margin-top: var(--cv-space-4); }
.settings-panel__footer { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.settings-panel__dirty { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.settings-panel__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
@media (max-width: 720px) {
  .settings-form-grid { grid-template-columns: 1fr; }
  .settings-panel__footer { align-items: stretch; flex-direction: column; }
  .settings-panel__actions { justify-content: flex-start; }
}
</style>
