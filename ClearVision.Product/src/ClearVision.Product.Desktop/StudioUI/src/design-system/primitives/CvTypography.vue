<script setup lang="ts">
withDefaults(defineProps<{
  as?: 'span' | 'p' | 'div' | 'h1' | 'h2' | 'h3' | 'h4' | 'code';
  variant?:
    | 'display'
    | 'page-title'
    | 'section-title'
    | 'body'
    | 'secondary'
    | 'caption'
    | 'numeric'
    | 'title'
    | 'heading'
    | 'label';
  tone?: 'primary' | 'secondary' | 'muted' | 'inverse';
  weight?: 'regular' | 'medium' | 'semibold';
  mono?: boolean;
  truncate?: boolean;
}>(), {
  as: 'span',
  variant: 'body',
  tone: 'primary',
  weight: 'regular',
  mono: false,
  truncate: false
});
</script>

<template>
  <component
    :is="as"
    class="cv-typography"
    :class="[
      `cv-typography--${variant}`,
      `cv-typography--${tone}`,
      `cv-typography--${weight}`,
      { 'cv-typography--mono': mono, 'cv-typography--truncate': truncate }
    ]"
    data-design-primitive="typography"
  >
    <slot />
  </component>
</template>

<style scoped>
.cv-typography {
  margin: 0;
  color: var(--cv-text-primary);
  font-family: var(--cv-font-sans);
}

.cv-typography--display {
  font-size: var(--cv-type-display-size);
  line-height: var(--cv-line-height-tight);
  letter-spacing: var(--cv-letter-spacing-display);
}

.cv-typography--page-title,
.cv-typography--title {
  font-size: var(--cv-type-page-title-size);
  line-height: var(--cv-line-height-tight);
  letter-spacing: var(--cv-letter-spacing-title);
}

.cv-typography--section-title,
.cv-typography--heading {
  font-size: var(--cv-type-section-title-size);
  line-height: var(--cv-line-height-tight);
}

.cv-typography--body {
  font-size: var(--cv-type-body-size);
  line-height: var(--cv-line-height-normal);
  letter-spacing: var(--cv-letter-spacing-body);
}

.cv-typography--secondary {
  font-size: var(--cv-type-secondary-size);
  line-height: var(--cv-line-height-normal);
}

.cv-typography--caption,
.cv-typography--label {
  font-size: var(--cv-type-caption-size);
  line-height: var(--cv-line-height-normal);
  letter-spacing: var(--cv-letter-spacing-caption);
}

.cv-typography--label {
  letter-spacing: 0;
  text-transform: uppercase;
}

.cv-typography--numeric {
  font-family: var(--cv-font-numeric);
  font-size: var(--cv-type-numeric-size);
  font-variant-numeric: tabular-nums lining-nums;
  letter-spacing: 0;
  line-height: var(--cv-line-height-tight);
}

.cv-typography--secondary {
  color: var(--cv-text-secondary);
}

.cv-typography--muted {
  color: var(--cv-text-muted);
}

.cv-typography--inverse {
  color: var(--cv-text-inverse);
}

.cv-typography--regular {
  font-weight: var(--cv-font-weight-regular);
}

.cv-typography--medium {
  font-weight: var(--cv-font-weight-medium);
}

.cv-typography--semibold {
  font-weight: var(--cv-font-weight-semibold);
}

.cv-typography--mono {
  font-family: var(--cv-font-mono);
}

.cv-typography--truncate {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
