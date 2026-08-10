<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, shallowRef, watch } from 'vue';
import { CvButton, CvField, CvInlineAlert, CvPanel, CvSelect } from '@/design-system';
import type { SettingsOwner } from './settingsOwner';
import type { SettingsGeneralProjectionV1 } from './decoder';
import {
  settingsFeedbackForResult,
  type SettingsFeedback
} from './settingsViewModel';
import SettingsWriteFeedback from './SettingsWriteFeedback.vue';

const props = defineProps<{
  projection: SettingsGeneralProjectionV1;
  owner: SettingsOwner;
  canWrite: boolean;
}>();

interface GeneralDraft {
  softwareTitle: string;
  theme: SettingsGeneralProjectionV1['theme'];
  autoStart: boolean | null;
}

function copy(value: SettingsGeneralProjectionV1): GeneralDraft {
  return {
    softwareTitle: value.softwareTitle,
    theme: value.theme,
    autoStart: value.autoStart
  };
}

const draft = reactive<GeneralDraft>(copy(props.projection));
const baseline = shallowRef<GeneralDraft>(copy(props.projection));
const busy = shallowRef(false);
const feedback = shallowRef<SettingsFeedback | null>(null);
const dirty = computed(() => JSON.stringify(draft) !== JSON.stringify(baseline.value));
const detachPanelState = props.owner.registerPanelState('general', () => ({
  dirty: dirty.value,
  pending: busy.value
}));
watch([dirty, busy], () => props.owner.refreshPanelState());
const themeOptions = Object.freeze([
  { value: 'dark', label: '深色' },
  { value: 'light', label: '浅色' }
]);

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
  if (!props.canWrite || busy.value || !dirty.value) return;
  busy.value = true;
  feedback.value = null;
  try {
    const result = await props.owner.saveGenericSection('general', {
      softwareTitle: draft.softwareTitle,
      theme: draft.theme
    });
    feedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') {
      const next = copy(result.value.config.sections.general);
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
    title="常规设置"
    description="设置软件标题和默认主题。顶栏中的个人外观偏好不会被此处覆盖。"
    data-settings-section="general"
  >
    <div class="settings-form-grid">
      <CvField
        v-model="draft.softwareTitle"
        label="软件标题"
        name="softwareTitle"
        :readonly="!canWrite"
        hint="显示在软件窗口和产品标识附近。"
      />
      <CvSelect
        v-model="draft.theme"
        label="产品主题"
        name="productTheme"
        :options="themeOptions"
        :disabled="!canWrite"
        hint="作为未设置个人外观偏好时的默认主题。"
      />
      <CvField
        :model-value="draft.autoStart === null ? '未返回' : draft.autoStart ? '已启用' : '未启用'"
        label="自动启动（兼容字段）"
        readonly
        disabled
        hint="当前版本不使用此项，保持只读。"
      />
    </div>

    <CvInlineAlert
      v-if="!canWrite"
      class="settings-panel__notice"
      tone="info"
      title="当前账户为只读"
    >
      你可以查看当前设置；修改需要管理员权限。
    </CvInlineAlert>

    <template #footer>
      <div class="settings-panel__footer">
        <span class="settings-panel__dirty">{{ dirty ? '有未保存修改' : '当前分组已保存' }}</span>
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
            loading-label="正在保存常规设置"
            :disabled="!dirty"
            @click="save"
          >
            保存常规设置
          </CvButton>
        </div>
      </div>
    </template>
  </CvPanel>

  <SettingsWriteFeedback :feedback="feedback" />
</template>

<style scoped>
.settings-form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--cv-space-4);
}

.settings-panel__notice { margin-top: var(--cv-space-4); }

.settings-panel__footer {
  display: flex;
  min-width: 0;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-3);
}

.settings-panel__dirty {
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-xs);
}

.settings-panel__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: var(--cv-space-2);
}

@media (max-width: 680px) {
  .settings-form-grid { grid-template-columns: 1fr; }
  .settings-panel__footer { align-items: stretch; flex-direction: column; }
  .settings-panel__actions { justify-content: flex-start; }
}
</style>
