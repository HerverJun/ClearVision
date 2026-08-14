<script setup lang="ts">
import { computed, useId } from 'vue';

const props = withDefaults(defineProps<{
  modelValue?: boolean;
  label: string;
  description?: string | undefined;
  id?: string | undefined;
  name?: string | undefined;
  disabled?: boolean;
  inputAttributes?: Readonly<Record<string, string | number | boolean | undefined>>;
}>(), {
  modelValue: false,
  description: undefined,
  id: undefined,
  name: undefined,
  disabled: false,
  inputAttributes: () => Object.freeze({})
});

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
}>();

const generatedId = useId();
const inputId = computed(() => props.id ?? `cv-toggle-${generatedId}`);
const descriptionId = computed(() => `${inputId.value}-description`);

function updateValue(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement).checked);
}
</script>

<template>
  <label
    class="cv-toggle"
    :class="{ 'cv-toggle--disabled': disabled }"
    :for="inputId"
    data-design-primitive="toggle"
  >
    <input
      v-bind="inputAttributes"
      :id="inputId"
      class="cv-toggle__input"
      type="checkbox"
      role="switch"
      :name="name ?? inputId"
      :checked="modelValue"
      :disabled="disabled"
      :aria-checked="modelValue ? 'true' : 'false'"
      :aria-describedby="description ? descriptionId : undefined"
      @change="updateValue"
    >
    <span
      class="cv-toggle__track"
      aria-hidden="true"
    >
      <span class="cv-toggle__thumb" />
    </span>
    <span class="cv-toggle__copy">
      <span class="cv-toggle__label">{{ label }}</span>
      <span
        v-if="description"
        :id="descriptionId"
        class="cv-toggle__description"
      >{{ description }}</span>
    </span>
  </label>
</template>

<style scoped>
.cv-toggle {
  display: inline-flex;
  min-width: 0;
  min-height: var(--cv-density-control-height);
  align-items: center;
  gap: var(--cv-space-2);
  color: var(--cv-text-primary);
  cursor: pointer;
}

.cv-toggle__input {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
}

.cv-toggle__track {
  position: relative;
  width: 36px;
  height: 20px;
  flex: 0 0 36px;
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-pill);
  background: var(--cv-surface-sunken);
  transition:
    background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    box-shadow var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}

.cv-toggle__thumb {
  position: absolute;
  top: 3px;
  left: 3px;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: var(--cv-text-muted);
  transition:
    background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    transform var(--cv-motion-duration-fast) var(--cv-motion-ease-emphasized);
}

.cv-toggle__copy {
  display: grid;
  min-width: 0;
  gap: var(--cv-space-1);
}

.cv-toggle__label {
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-medium);
  line-height: var(--cv-line-height-tight);
}

.cv-toggle__description {
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-xs);
  line-height: var(--cv-line-height-normal);
}

.cv-toggle:hover .cv-toggle__track { border-color: var(--cv-control-border-hover); }

.cv-toggle__input:checked + .cv-toggle__track {
  border-color: var(--cv-color-action);
  background: var(--cv-color-action);
}

.cv-toggle__input:checked + .cv-toggle__track .cv-toggle__thumb {
  background: var(--cv-color-on-action);
  transform: translateX(16px);
}

.cv-toggle__input:focus-visible + .cv-toggle__track {
  border-color: var(--cv-focus-ring-color);
  box-shadow: var(--cv-focus-ring);
}

.cv-toggle--disabled {
  cursor: not-allowed;
  opacity: 0.52;
}

@media (forced-colors: active) {
  .cv-toggle__track {
    border-color: CanvasText;
    background: Canvas;
    forced-color-adjust: none;
  }
  .cv-toggle__thumb { background: CanvasText; }
  .cv-toggle__input:focus-visible + .cv-toggle__track {
    outline: 2px solid Highlight;
    outline-offset: 2px;
    box-shadow: none;
  }
  .cv-toggle__input:checked + .cv-toggle__track {
    border-color: Highlight;
    background: Highlight;
  }
  .cv-toggle__input:checked + .cv-toggle__track .cv-toggle__thumb { background: HighlightText; }
}
</style>
