<script setup lang="ts">
withDefaults(defineProps<{
  as?: 'div' | 'section' | 'article' | 'aside';
  level?: 1 | 2 | 3;
  elevation?: 0 | 1 | 2 | 3;
  padding?: 'none' | 'sm' | 'md' | 'lg';
  bordered?: boolean;
}>(), {
  as: 'div',
  level: 1,
  elevation: 0,
  padding: 'md',
  bordered: true
});
</script>

<template>
  <component
    :is="as"
    class="cv-surface"
    :class="[
      `cv-surface--level-${level}`,
      `cv-surface--elevation-${elevation}`,
      `cv-surface--padding-${padding}`,
      { 'cv-surface--bordered': bordered }
    ]"
    :data-surface-level="level"
    data-design-primitive="surface"
  >
    <slot />
  </component>
</template>

<style scoped>
.cv-surface {
  min-width: 0;
  border-radius: var(--cv-radius-lg);
  color: var(--cv-text-primary);
}

.cv-surface--level-1 { background: var(--cv-surface-1); }
.cv-surface--level-2 { background: var(--cv-surface-2); }
.cv-surface--level-3 { background: var(--cv-surface-3); }
.cv-surface--bordered { border: 1px solid var(--cv-border-subtle); }
.cv-surface--elevation-1 { box-shadow: var(--cv-elevation-1); }
.cv-surface--elevation-2 { box-shadow: var(--cv-elevation-2); }
.cv-surface--elevation-3 { box-shadow: var(--cv-elevation-3); }
.cv-surface--padding-none { padding: 0; }
.cv-surface--padding-sm { padding: var(--cv-space-3); }
.cv-surface--padding-md { padding: var(--cv-density-panel-padding); }
.cv-surface--padding-lg { padding: var(--cv-space-6); }
</style>
