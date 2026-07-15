<script setup lang="ts">
import type { CvDescriptionItem } from './types';

withDefaults(defineProps<{
  items: readonly CvDescriptionItem[];
  columns?: 1 | 2;
  label?: string | undefined;
  missingValueLabel?: string;
}>(), {
  columns: 2,
  label: undefined,
  missingValueLabel: '—'
});
</script>

<template>
  <dl
    class="cv-description-list"
    :class="`cv-description-list--columns-${columns}`"
    :aria-label="label"
    data-design-primitive="description-list"
  >
    <div
      v-for="item in items"
      :key="item.key"
      class="cv-description-list__item"
      :class="{ 'cv-description-list__item--wide': item.span === 2 }"
    >
      <dt>{{ item.label }}</dt>
      <dd>
        <slot
          name="value"
          :item="item"
        >
          {{ item.value === null || item.value === undefined || item.value === '' ? missingValueLabel : item.value }}
        </slot>
      </dd>
    </div>
  </dl>
</template>

<style scoped>
.cv-description-list {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 0;
  margin: 0;
  border-top: 1px solid var(--cv-border-subtle);
}

.cv-description-list--columns-2 {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.cv-description-list__item {
  display: grid;
  min-width: 0;
  grid-template-columns: minmax(96px, 0.42fr) minmax(0, 1fr);
  gap: var(--cv-space-3);
  padding: var(--cv-space-3) var(--cv-space-2);
  border-bottom: 1px solid var(--cv-border-subtle);
}

.cv-description-list__item--wide { grid-column: 1 / -1; }

dt {
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-medium);
}

dd {
  min-width: 0;
  margin: 0;
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  overflow-wrap: anywhere;
}

@media (max-width: 760px) {
  .cv-description-list--columns-2 { grid-template-columns: minmax(0, 1fr); }
  .cv-description-list__item--wide { grid-column: auto; }
}

@media (max-width: 460px) {
  .cv-description-list__item { grid-template-columns: minmax(0, 1fr); gap: var(--cv-space-1); }
}
</style>
