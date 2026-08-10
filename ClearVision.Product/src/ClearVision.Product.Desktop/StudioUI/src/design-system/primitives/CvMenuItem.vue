<script setup lang="ts">
import { computed } from 'vue';
import CvIcon from '../icons/CvIcon.vue';

const props = withDefaults(defineProps<{
  value: string;
  label?: string | undefined;
  disabled?: boolean;
  tone?: 'default' | 'destructive';
  checked?: boolean | undefined;
}>(), {
  label: undefined,
  disabled: false,
  tone: 'default',
  checked: undefined
});

const emit = defineEmits<{
  select: [value: string];
}>();

const role = computed(() => props.checked === undefined ? 'menuitem' : 'menuitemcheckbox');

function select(): void {
  if (!props.disabled) emit('select', props.value);
}
</script>

<template>
  <button
    class="cv-menu-item"
    :class="`cv-menu-item--${tone}`"
    type="button"
    :role="role"
    :tabindex="-1"
    :disabled="disabled"
    :aria-disabled="disabled ? 'true' : undefined"
    :aria-checked="checked === undefined ? undefined : checked"
    :data-menu-value="value"
    data-design-primitive="menu-item"
    @click="select"
  >
    <span
      v-if="$slots.leading || checked !== undefined"
      class="cv-menu-item__leading"
      aria-hidden="true"
    >
      <slot name="leading">
        <CvIcon
          v-if="checked"
          name="success"
          size="sm"
        />
      </slot>
    </span>
    <span class="cv-menu-item__label"><slot>{{ label }}</slot></span>
    <span
      v-if="$slots.trailing"
      class="cv-menu-item__trailing"
    ><slot name="trailing" /></span>
  </button>
</template>

<style scoped>
.cv-menu-item {
  display: grid;
  width: 100%;
  min-height: var(--cv-density-control-height);
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--cv-space-2);
  padding: var(--cv-space-1) var(--cv-space-3);
  border: 0;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-primary);
  cursor: pointer;
  font-size: var(--cv-font-size-sm);
  text-align: left;
}
.cv-menu-item:hover:not(:disabled),
.cv-menu-item:focus-visible { outline: 0; background: var(--cv-interactive-hover); }
.cv-menu-item:focus-visible { box-shadow: inset 0 0 0 2px var(--cv-focus-ring-color); }
.cv-menu-item:disabled { cursor: not-allowed; opacity: 0.5; }
.cv-menu-item--destructive { color: var(--cv-color-destructive-strong); }
.cv-menu-item--destructive:hover:not(:disabled),
.cv-menu-item--destructive:focus-visible { background: var(--cv-color-destructive-soft); }
.cv-menu-item__leading { display: grid; width: var(--cv-density-icon-size); place-items: center; }
.cv-menu-item__label { min-width: 0; overflow-wrap: anywhere; }
.cv-menu-item__trailing { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); white-space: nowrap; }
</style>
