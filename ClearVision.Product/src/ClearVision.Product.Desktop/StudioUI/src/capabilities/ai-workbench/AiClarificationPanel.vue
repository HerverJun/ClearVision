<script setup lang="ts">
import { computed, reactive, watch } from 'vue';
import { CvButton, CvStatusBadge } from '@/design-system/primitives';
import type { AiClarificationQuestionV1, AiPlanAnswerV1 } from './contracts';

const props = defineProps<{
  questions: readonly AiClarificationQuestionV1[];
  selections: Readonly<Record<string, string>>;
  confirmedAnswers: readonly AiPlanAnswerV1[];
  optimisticAnswers: readonly AiPlanAnswerV1[];
  busy: boolean;
}>();

const emit = defineEmits<{
  submit: [answers: Readonly<Record<string, string>>];
  acceptRecommended: [];
}>();

const answers = reactive<Record<string, string>>({});
const customModes = reactive<Record<string, boolean>>({});
const visibleQuestions = computed(() => props.questions.slice(0, 3));
const canSubmit = computed(() => visibleQuestions.value.some(question => answers[question.field]?.trim()));

watch(() => [props.questions, props.selections] as const, () => {
  for (const question of props.questions) {
    const existing = props.selections[question.field] ?? '';
    if (existing) answers[question.field] = existing;
    const isOption = question.options.some(option => option.value === existing);
    customModes[question.field] = Boolean(existing && !isOption);
  }
}, { immediate: true, deep: true });

function answerStatus(field: string): Readonly<{ label: string; tone: 'ok' | 'info' | 'warning' }> {
  if (props.confirmedAnswers.some(answer => answer.field === field)) return Object.freeze({ label: '已确认', tone: 'ok' });
  if (props.optimisticAnswers.some(answer => answer.field === field)) return Object.freeze({ label: '等待服务端确认', tone: 'info' });
  return Object.freeze({ label: '待确认', tone: 'warning' });
}

function chooseCustom(field: string): void {
  customModes[field] = true;
  if (props.questions.find(item => item.field === field)?.options.some(option => option.value === answers[field])) {
    answers[field] = '';
  }
}

function submit(): void {
  if (!canSubmit.value || props.busy) return;
  emit('submit', Object.freeze({ ...answers }));
}
</script>

<template>
  <section
    class="ai-clarification"
    data-ai-clarification-panel
    aria-labelledby="ai-clarification-title"
  >
    <header class="ai-clarification__header">
      <div>
        <h2 id="ai-clarification-title">
          待确认事项
        </h2>
        <p>本批只显示最多 3 个影响方案的关键问题。</p>
      </div>
      <span>{{ visibleQuestions.length }} 项</span>
    </header>

    <div class="ai-clarification__questions">
      <fieldset
        v-for="question in visibleQuestions"
        :key="question.field"
        class="ai-clarification__question"
      >
        <legend>
          <span>{{ question.title }}</span>
          <CvStatusBadge v-bind="answerStatus(question.field)" />
        </legend>
        <p
          v-if="question.why"
          class="ai-clarification__why"
        >
          {{ question.why }}
        </p>

        <div class="ai-clarification__options">
          <label
            v-for="option in question.options"
            :key="option.value"
            :class="{ 'is-selected': answers[question.field] === option.value && !customModes[question.field] }"
          >
            <input
              v-model="answers[question.field]"
              type="radio"
              autocomplete="off"
              :name="`question-${question.field}`"
              :value="option.value"
              :disabled="busy"
              @change="customModes[question.field] = false"
            >
            <span class="ai-clarification__option-label">
              <strong>{{ option.label }}</strong>
              <CvStatusBadge
                v-if="option.recommended"
                tone="info"
                label="推荐"
              />
            </span>
            <small>{{ option.recommendationReason || option.description || option.impact }}</small>
          </label>

          <label
            class="ai-clarification__custom"
            :class="{ 'is-selected': customModes[question.field] }"
          >
            <input
              type="radio"
              autocomplete="off"
              :name="`question-${question.field}`"
              :checked="customModes[question.field]"
              :disabled="busy"
              @change="chooseCustom(question.field)"
            >
            <span>填写明确答案</span>
            <input
              v-if="customModes[question.field]"
              v-model="answers[question.field]"
              type="text"
              autocomplete="off"
              :name="`custom-${question.field}`"
              :disabled="busy"
              :placeholder="question.defaultAssumption ? `${question.defaultAssumption}…` : '请输入明确条件…'"
            >
          </label>
        </div>
        <p
          v-if="question.impact"
          class="ai-clarification__impact"
        >
          影响：{{ question.impact }}
        </p>
      </fieldset>
    </div>

    <footer class="ai-clarification__actions">
      <CvButton
        size="sm"
        variant="quiet"
        :disabled="busy"
        @click="emit('acceptRecommended')"
      >
        采用推荐答案
      </CvButton>
      <CvButton
        size="sm"
        variant="primary"
        :disabled="!canSubmit"
        :loading="busy"
        loading-label="正在确认回答"
        @click="submit"
      >
        确认回答并重新检查
      </CvButton>
    </footer>
  </section>
</template>

<style scoped>
.ai-clarification { min-width: 0; overflow: hidden; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-lg); background: var(--cv-surface-raised); }
.ai-clarification__header { display: flex; align-items: start; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-clarification__header h2 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); line-height: var(--cv-line-height-tight); }
.ai-clarification__header p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.ai-clarification__header > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-variant-numeric: tabular-nums; }
.ai-clarification__questions { display: grid; }
.ai-clarification__question { min-width: 0; margin: 0; padding: var(--cv-space-4) var(--cv-density-panel-padding); border: 0; border-block-end: 1px solid var(--cv-border-subtle); }
.ai-clarification__question legend { display: flex; width: 100%; align-items: center; justify-content: space-between; gap: var(--cv-space-2); padding: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); }
.ai-clarification__why, .ai-clarification__impact { margin: var(--cv-space-1) 0 var(--cv-space-3); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-clarification__impact { margin-block: var(--cv-space-2) 0; color: var(--cv-text-muted); }
.ai-clarification__options { display: grid; gap: var(--cv-space-2); }
.ai-clarification__options > label { position: relative; display: grid; gap: var(--cv-space-1); min-width: 0; padding: var(--cv-space-2) var(--cv-space-3) var(--cv-space-2) calc(var(--cv-space-3) + 22px); border: 1px solid var(--cv-border-default); border-radius: var(--cv-radius-sm); cursor: pointer; }
.ai-clarification__options > label.is-selected { border-color: var(--cv-color-industrial-blue); background: var(--cv-color-status-info-soft); }
.ai-clarification__options > label:focus-within { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.ai-clarification__options > label > input[type='radio'] { position: absolute; top: 12px; left: var(--cv-space-3); }
.ai-clarification__option-label { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
.ai-clarification__option-label strong { overflow-wrap: anywhere; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.ai-clarification__options small { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.ai-clarification__custom > span { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.ai-clarification__custom input[type='text'] { width: 100%; height: var(--cv-density-control-height); padding: 0 var(--cv-space-3); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); font: inherit; font-size: var(--cv-font-size-xs); }
.ai-clarification__custom input[type='text']::placeholder { color: var(--cv-text-secondary); }
.ai-clarification__custom input[type='text']:focus-visible { border-color: var(--cv-focus-ring-color); outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.ai-clarification__actions { display: flex; align-items: center; justify-content: flex-end; gap: var(--cv-space-2); padding: var(--cv-space-3) var(--cv-density-panel-padding); background: var(--cv-surface-page); }

@media (max-width: 600px) {
  .ai-clarification__actions { align-items: stretch; flex-direction: column-reverse; }
}
</style>
