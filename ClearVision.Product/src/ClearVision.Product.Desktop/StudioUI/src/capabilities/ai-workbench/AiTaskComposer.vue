<script setup lang="ts">
import { computed, nextTick, shallowRef, useTemplateRef, watch } from 'vue';
import { CvButton } from '@/design-system/primitives';
import type { AiRequirementMode } from './contracts';

const props = withDefaults(defineProps<{
  initialDescription?: string;
  initialMode?: AiRequirementMode;
  busy?: boolean;
}>(), {
  initialDescription: '',
  initialMode: 'strict',
  busy: false
});

const emit = defineEmits<{
  submit: [description: string, mode: AiRequirementMode];
}>();

const description = shallowRef(props.initialDescription);
const mode = shallowRef<AiRequirementMode>(props.initialMode);
const submitted = shallowRef(false);
const textarea = useTemplateRef<HTMLTextAreaElement>('taskInput');
const trimmedLength = computed(() => description.value.trim().length);
const error = computed(() => {
  if (trimmedLength.value === 0) return submitted.value ? '请输入视觉检测任务描述。' : '';
  if (trimmedLength.value < 8) return '请再补充检测对象、目标或缺陷。';
  if (trimmedLength.value > 4000) return '任务描述不能超过 4000 个字符。';
  return '';
});
const canSubmit = computed(() => !props.busy && trimmedLength.value >= 8 && trimmedLength.value <= 4000);

watch(() => props.initialDescription, value => {
  if (!description.value) description.value = value;
});

watch(() => props.busy, async busy => {
  if (!busy) {
    await nextTick();
    textarea.value?.focus();
  }
}, { immediate: true });

function submit(): void {
  submitted.value = true;
  if (!canSubmit.value) {
    textarea.value?.focus();
    return;
  }
  emit('submit', description.value.trim(), mode.value);
}
</script>

<template>
  <form
    class="ai-task-composer"
    data-ai-task-composer
    @submit.prevent="submit"
  >
    <div class="ai-task-composer__heading">
      <div>
        <h2 class="ai-task-composer__title">
          描述视觉检测任务
        </h2>
        <p class="ai-composer-supporting-text">
          说明检测对象、缺陷或测量目标，以及期望的判定和输出。
        </p>
      </div>
      <span class="ai-task-composer__count">{{ trimmedLength }} / 4000</span>
    </div>

    <label class="ai-task-composer__field">
      <span class="ai-task-composer__label">任务描述</span>
      <textarea
        ref="taskInput"
        v-model="description"
        class="ai-task-composer__textarea"
        name="ai-task-description"
        rows="7"
        maxlength="4000"
        autocomplete="off"
        :disabled="busy"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="error ? 'ai-task-description-error' : undefined"
        placeholder="例如：检测冲压件表面划伤与压痕，图像来自顶视相机；任一缺陷长度超过 2 mm 判定 NG，并输出缺陷位置与类型…"
      />
      <span
        v-if="error"
        id="ai-task-description-error"
        class="ai-task-composer__error"
        aria-live="polite"
      >{{ error }}</span>
    </label>

    <div class="ai-task-composer__footer">
      <fieldset class="ai-task-composer__mode">
        <legend>方案策略</legend>
        <label :class="{ 'is-selected': mode === 'strict' }">
          <input
            v-model="mode"
            type="radio"
            name="ai-requirement-mode"
            autocomplete="off"
            value="strict"
            :disabled="busy"
          >
          <span>确认关键条件后构建</span>
        </label>
        <label :class="{ 'is-selected': mode === 'draft' }">
          <input
            v-model="mode"
            type="radio"
            name="ai-requirement-mode"
            autocomplete="off"
            value="draft"
            :disabled="busy"
          >
          <span>先生成可编辑草稿</span>
        </label>
      </fieldset>

      <CvButton
        type="submit"
        variant="primary"
        :disabled="busy"
        :loading="busy"
        loading-label="正在理解任务"
      >
        理解并规划任务
      </CvButton>
    </div>
  </form>
</template>

<style scoped>
.ai-task-composer {
  justify-self: center;
  width: min(1120px, 100%);
  display: grid;
  gap: var(--cv-space-4);
  padding: clamp(var(--cv-density-panel-padding), 2.2vw, var(--cv-space-6));
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-lg);
  background: var(--cv-surface-raised);
  box-shadow: var(--cv-elevation-1);
}
.ai-task-composer__heading { display: flex; align-items: start; justify-content: space-between; gap: var(--cv-space-4); }
.ai-task-composer__title { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); line-height: var(--cv-line-height-tight); }
.ai-composer-supporting-text { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-normal); }
.ai-task-composer__count { flex: 0 0 auto; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-variant-numeric: tabular-nums; }
.ai-task-composer__field { display: grid; gap: var(--cv-space-2); }
.ai-task-composer__label { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.ai-task-composer__textarea {
  width: 100%;
  min-height: 172px;
  resize: vertical;
  padding: var(--cv-space-3);
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-page);
  color: var(--cv-text-primary);
  font: inherit;
  font-size: var(--cv-font-size-sm);
  line-height: 1.7;
  transition: border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.ai-task-composer__textarea::placeholder { color: var(--cv-text-secondary); }
.ai-task-composer__textarea:hover:not(:disabled) { border-color: var(--cv-control-border-hover); }
.ai-task-composer__textarea:focus-visible { border-color: var(--cv-focus-ring-color); outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.ai-task-composer__textarea:disabled { cursor: not-allowed; opacity: 0.6; }
.ai-task-composer__error { color: var(--cv-color-status-error-strong); font-size: var(--cv-font-size-xs); }
.ai-task-composer__footer { display: flex; align-items: end; justify-content: space-between; gap: var(--cv-space-4); }
.ai-task-composer__mode { display: inline-flex; min-width: 0; margin: 0; padding: 3px; border: 1px solid var(--cv-border-default); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.ai-task-composer__mode legend { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0, 0, 0, 0); }
.ai-task-composer__mode label { position: relative; display: inline-flex; min-height: var(--cv-density-control-height-sm); align-items: center; padding: 0 var(--cv-space-3); border-radius: calc(var(--cv-radius-sm) - 2px); color: var(--cv-text-secondary); cursor: pointer; font-size: var(--cv-font-size-xs); }
.ai-task-composer__mode label.is-selected { background: var(--cv-color-action-soft); color: var(--cv-color-action-text); box-shadow: inset 0 0 0 1px var(--cv-color-action-border); }
.ai-task-composer__mode input { position: absolute; width: 1px; height: 1px; opacity: 0; }
.ai-task-composer__mode label:focus-within { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }

@media (max-width: 760px) {
  .ai-task-composer__footer { align-items: stretch; flex-direction: column; }
  .ai-task-composer__mode { display: grid; grid-template-columns: 1fr 1fr; }
  .ai-task-composer__mode label { justify-content: center; text-align: center; }
}
</style>
