<script setup lang="ts">
import { computed, useId } from 'vue';

withDefaults(defineProps<{
  as?: 'section' | 'article' | 'aside';
  title: string;
  description?: string | undefined;
  level?: 1 | 2;
  variant?: 'section' | 'card' | 'tool';
  padded?: boolean;
  elevated?: boolean;
}>(), {
  as: 'section',
  description: undefined,
  level: 1,
  variant: 'card',
  padded: true,
  elevated: false
});

const generatedId = useId();
const titleId = computed(() => `cv-panel-${generatedId}`);
</script>

<template>
  <component
    :is="as"
    class="cv-panel"
    :class="[
      `cv-panel--level-${level}`,
      `cv-panel--${variant}`,
      { 'cv-panel--padded': padded, 'cv-panel--elevated': elevated }
    ]"
    :aria-labelledby="titleId"
    data-design-primitive="panel"
    :data-panel-variant="variant"
  >
    <header class="cv-panel__header">
      <div class="cv-panel__heading">
        <h2
          :id="titleId"
          class="cv-panel__title"
        >
          {{ title }}
        </h2>
        <p
          v-if="description"
          class="cv-panel__description"
        >
          {{ description }}
        </p>
      </div>
      <div
        v-if="$slots.actions"
        class="cv-panel__actions"
      >
        <slot name="actions" />
      </div>
    </header>
    <div class="cv-panel__content">
      <slot />
    </div>
    <footer
      v-if="$slots.footer"
      class="cv-panel__footer"
    >
      <slot name="footer" />
    </footer>
  </component>
</template>

<style scoped>
.cv-panel {
  min-width: 0;
  overflow: hidden;
  border: 0;
  border-radius: 0;
  background: var(--cv-surface-section);
  box-shadow: none;
}
.cv-panel--card { border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-lg); background: var(--cv-surface-card); }
.cv-panel--tool { border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-tool); }
.cv-panel--section { border-block-start: 1px solid var(--cv-border-subtle); }
.cv-panel--section:first-child { border-block-start: 0; }
.cv-panel--level-2 { background: var(--cv-surface-page); box-shadow: none; }
.cv-panel--section.cv-panel--level-2 { background: transparent; }
.cv-panel--elevated { border-color: transparent; background: var(--cv-surface-floating); box-shadow: var(--cv-elevation-2); }
.cv-panel__header { display: flex; min-width: 0; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); padding: var(--cv-density-panel-padding) var(--cv-density-panel-padding) var(--cv-space-2); }
.cv-panel__heading { min-width: 0; }
.cv-panel__title { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); font-weight: var(--cv-font-weight-semibold); letter-spacing: var(--cv-letter-spacing-title); line-height: var(--cv-line-height-tight); }
.cv-panel__description { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.cv-panel__actions { display: flex; flex: 0 0 auto; align-items: center; gap: var(--cv-space-2); }
.cv-panel__content { min-width: 0; }
.cv-panel--padded .cv-panel__content { padding: 0 var(--cv-density-panel-padding) var(--cv-density-panel-padding); }
.cv-panel__footer { padding: var(--cv-space-3) var(--cv-density-panel-padding); border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.cv-panel--section .cv-panel__header,
.cv-panel--section.cv-panel--padded .cv-panel__content,
.cv-panel--section .cv-panel__footer { padding-inline: 0; }
</style>
