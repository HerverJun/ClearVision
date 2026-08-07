<script setup lang="ts">
import { computed, reactive, shallowRef, watch } from 'vue';
import { CvButton, CvStatusBadge } from '@/design-system/primitives';
import { resolveFilePickerFilter, type FilePickerPort } from '@/platform/host';
import type { AiBuildParameterV1, AiScalarValue } from './contracts';
import { validateBuildParameterValues } from './parameterValidation';

const props = defineProps<{
  parameters: readonly AiBuildParameterV1[];
  confirmedValues: Readonly<Record<string, AiScalarValue>>;
  busy: boolean;
  filePicker?: FilePickerPort | null;
}>();

const emit = defineEmits<{
  confirm: [values: Readonly<Record<string, AiScalarValue>>];
}>();

const drafts = reactive<Record<string, string>>({});
const nullValues = reactive<Record<string, boolean>>({});
const pickerBusy = reactive<Record<string, boolean>>({});
const pickerErrors = reactive<Record<string, string>>({});
const pickerGeneration = shallowRef(0);
const pending = computed(() => props.parameters.filter(item => item.pending && !item.resourceDependent));
const groups = computed(() => {
  const byOperator = new Map<string, { label: string; items: AiBuildParameterV1[] }>();
  for (const parameter of pending.value) {
    const group = byOperator.get(parameter.tempId) ?? { label: parameter.operatorDisplayName || parameter.operatorType, items: [] };
    group.items.push(parameter);
    byOperator.set(parameter.tempId, group);
  }
  return [...byOperator.entries()].map(([id, group]) => Object.freeze({ id, ...group }));
});

watch(() => [props.parameters, props.confirmedValues] as const, () => {
  for (const parameter of pending.value) {
    if (Object.prototype.hasOwnProperty.call(props.confirmedValues, parameter.canonicalKey)) {
      const value = props.confirmedValues[parameter.canonicalKey];
      nullValues[parameter.canonicalKey] = value === null;
      drafts[parameter.canonicalKey] = value === null ? '' : String(value);
    } else if (!(parameter.canonicalKey in drafts)) {
      drafts[parameter.canonicalKey] = '';
      nullValues[parameter.canonicalKey] = false;
    }
  }
}, { immediate: true, deep: true });

const draftValues = computed<Readonly<Record<string, AiScalarValue>>>(() => {
  const result: Record<string, AiScalarValue> = { ...props.confirmedValues };
  for (const parameter of pending.value) {
    const key = parameter.canonicalKey;
    if (nullValues[key]) result[key] = null;
    else if (parameter.dataType === 'bool' && ['true', 'false'].includes(drafts[key] ?? '')) {
      result[key] = drafts[key] === 'true';
    } else if (parameter.dataType === 'int' && /^-?\d+$/.test(drafts[key] ?? '')) {
      result[key] = Number.parseInt(drafts[key] ?? '', 10);
    } else if (['double', 'number'].includes(parameter.dataType) && drafts[key] && Number.isFinite(Number(drafts[key]))) {
      result[key] = Number(drafts[key]);
    } else {
      result[key] = drafts[key] ?? '';
    }
  }
  return Object.freeze(result);
});

const validation = computed(() => validateBuildParameterValues(pending.value, draftValues.value));
const errors = computed<Readonly<Record<string, string>>>(() => validation.value.errors);

const canSubmit = computed(() => pending.value.length > 0 && pending.value.every(parameter => {
  const key = parameter.canonicalKey;
  return !errors.value[key] && (nullValues[key] || key in drafts);
}));

function isFileParameter(parameter: AiBuildParameterV1): boolean {
  const normalized = parameter.dataType.trim().toLowerCase();
  const name = parameter.parameterName.trim().toLowerCase();
  return normalized === 'file' || normalized === 'path' || name.endsWith('path') || name.endsWith('filepath');
}

async function chooseFile(parameter: AiBuildParameterV1): Promise<void> {
  if (props.busy || pickerBusy[parameter.canonicalKey] || !props.filePicker) {
    if (!props.filePicker) pickerErrors[parameter.canonicalKey] = '文件选择服务尚未就绪。';
    return;
  }
  const generation = pickerGeneration.value;
  pickerBusy[parameter.canonicalKey] = true;
  delete pickerErrors[parameter.canonicalKey];
  try {
    const result = await props.filePicker.pick({
      parameterName: parameter.parameterName,
      filter: resolveFilePickerFilter(parameter.parameterName)
    });
    if (generation !== pickerGeneration.value || result.status === 'cancelled') return;
    drafts[parameter.canonicalKey] = result.filePath;
    nullValues[parameter.canonicalKey] = false;
  } catch (error) {
    if (generation === pickerGeneration.value) {
      pickerErrors[parameter.canonicalKey] = error instanceof Error ? error.message : '文件选择失败。';
    }
  } finally {
    if (generation === pickerGeneration.value) pickerBusy[parameter.canonicalKey] = false;
  }
}

function suggested(parameter: AiBuildParameterV1): string {
  if (parameter.valueSummary === 'null') return '建议使用 null';
  if (parameter.valueSummary === '') return '建议为空字符串';
  return `建议：${parameter.valueSummary}`;
}

function submit(): void {
  if (!canSubmit.value || props.busy) return;
  const pendingKeys = new Set(validation.value.activeKeys);
  emit('confirm', Object.freeze(Object.fromEntries(
    Object.entries(draftValues.value).filter(([key]) => pendingKeys.has(key))
  )));
}
</script>

<template>
  <section
    class="ai-parameters"
    aria-labelledby="ai-parameters-title"
    data-ai-pending-parameters
  >
    <header class="ai-parameters__header">
      <div>
        <h2 id="ai-parameters-title">
          待确认参数
        </h2><p>草稿值不会自动视为用户确认。</p>
      </div>
      <span>{{ pending.length }} 项</span>
    </header>
    <div class="ai-parameters__groups">
      <section
        v-for="group in groups"
        :key="group.id"
        class="ai-parameters__group"
      >
        <h3>{{ group.label }}</h3>
        <div
          v-for="parameter in group.items"
          :key="parameter.canonicalKey"
          class="ai-parameters__field"
        >
          <div class="ai-parameters__label">
            <label :for="`ai-param-${parameter.canonicalKey}`">{{ parameter.parameterDisplayName || parameter.parameterName }}</label>
            <CvStatusBadge
              tone="warning"
              :label="parameter.dataType"
            />
          </div>
          <p>{{ parameter.purpose || parameter.impact }}</p>
          <select
            v-if="parameter.options.length"
            :id="`ai-param-${parameter.canonicalKey}`"
            v-model="drafts[parameter.canonicalKey]"
            :disabled="busy || nullValues[parameter.canonicalKey]"
          >
            <option value="">
              请选择
            </option>
            <option
              v-for="option in parameter.options"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
          <select
            v-else-if="parameter.dataType === 'bool'"
            :id="`ai-param-${parameter.canonicalKey}`"
            v-model="drafts[parameter.canonicalKey]"
            :disabled="busy || nullValues[parameter.canonicalKey]"
          >
            <option value="">
              请选择
            </option><option value="true">
              是
            </option><option value="false">
              否
            </option>
          </select>
          <template v-else-if="isFileParameter(parameter)">
            <input
              :id="`ai-param-${parameter.canonicalKey}`"
              :value="drafts[parameter.canonicalKey] ?? ''"
              type="text"
              readonly
              :disabled="busy || Boolean(pickerBusy[parameter.canonicalKey]) || nullValues[parameter.canonicalKey]"
              :title="drafts[parameter.canonicalKey] || '尚未选择文件'"
            >
            <CvButton
              size="sm"
              variant="quiet"
              :disabled="Boolean(busy || pickerBusy[parameter.canonicalKey] || nullValues[parameter.canonicalKey])"
              :loading="Boolean(pickerBusy[parameter.canonicalKey])"
              loading-label="等待文件窗口"
              @click="chooseFile(parameter)"
            >
              选择文件
            </CvButton>
          </template>
          <input
            v-else
            :id="`ai-param-${parameter.canonicalKey}`"
            v-model="drafts[parameter.canonicalKey]"
            :type="['int', 'double', 'number'].includes(parameter.dataType) ? 'number' : 'text'"
            :min="typeof parameter.minValue === 'number' ? parameter.minValue : undefined"
            :max="typeof parameter.maxValue === 'number' ? parameter.maxValue : undefined"
            :step="parameter.dataType === 'int' ? '1' : 'any'"
            :disabled="busy || nullValues[parameter.canonicalKey]"
            :placeholder="suggested(parameter)"
          >
          <p
            v-if="pickerErrors[parameter.canonicalKey]"
            class="ai-parameters__error"
            role="alert"
          >
            {{ pickerErrors[parameter.canonicalKey] }}
          </p>
          <label
            v-if="!parameter.isRequired"
            class="ai-parameters__null"
          >
            <input
              v-model="nullValues[parameter.canonicalKey]"
              type="checkbox"
              :disabled="busy"
            >
            使用 null（与空字符串不同）
          </label>
          <p class="ai-parameters__reason">
            {{ suggested(parameter) }}。{{ parameter.suggestedReason }}
          </p>
          <p
            v-if="errors[parameter.canonicalKey]"
            class="ai-parameters__error"
            role="alert"
          >
            {{ errors[parameter.canonicalKey] }}
          </p>
        </div>
      </section>
    </div>
    <footer>
      <CvButton
        size="sm"
        variant="primary"
        :disabled="!canSubmit"
        :loading="busy"
        loading-label="正在确认参数"
        @click="submit"
      >
        确认全部参数
      </CvButton>
    </footer>
  </section>
</template>

<style scoped>
.ai-parameters { min-width: 0; overflow: hidden; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-lg); background: var(--cv-surface-raised); }
.ai-parameters__header { display: flex; align-items: start; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-parameters h2, .ai-parameters h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.ai-parameters__header p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.ai-parameters__header > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.ai-parameters__groups { display: grid; }
.ai-parameters__group { padding: var(--cv-space-4) var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-parameters__group h3 { margin-bottom: var(--cv-space-3); font-size: var(--cv-font-size-sm); }
.ai-parameters__field { display: grid; gap: var(--cv-space-2); padding-block: var(--cv-space-3); border-block-start: 1px solid var(--cv-border-subtle); }
.ai-parameters__label { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
.ai-parameters__label label { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.ai-parameters__field > p { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.ai-parameters select, .ai-parameters input[type='text'], .ai-parameters input[type='number'] { width: 100%; height: var(--cv-density-control-height); padding: 0 var(--cv-space-3); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); font: inherit; font-size: var(--cv-font-size-xs); }
.ai-parameters input::placeholder { color: var(--cv-text-secondary); }
.ai-parameters select:focus-visible, .ai-parameters input:focus-visible { border-color: var(--cv-focus-ring-color); outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.ai-parameters__null { display: flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.ai-parameters__reason { color: var(--cv-text-muted) !important; }
.ai-parameters__error { color: var(--cv-color-status-error) !important; }
.ai-parameters footer { display: flex; justify-content: flex-end; padding: var(--cv-space-3) var(--cv-density-panel-padding); background: var(--cv-surface-page); }
</style>
