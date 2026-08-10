<script setup lang="ts">
withDefaults(defineProps<{
  title: string;
  description?: string | undefined;
  eyebrow?: string | undefined;
  headingLevel?: 1 | 2;
}>(), {
  description: undefined,
  eyebrow: undefined,
  headingLevel: 1
});
</script>

<template>
  <header
    class="cv-page-header"
    data-design-pattern="page-header"
  >
    <div
      v-if="$slots.breadcrumbs"
      class="cv-page-header__breadcrumbs"
    >
      <slot name="breadcrumbs" />
    </div>
    <div class="cv-page-header__row">
      <div class="cv-page-header__copy">
        <p
          v-if="eyebrow"
          class="cv-page-header__eyebrow"
        >
          {{ eyebrow }}
        </p>
        <component
          :is="`h${headingLevel}`"
          class="cv-page-header__title"
        >
          {{ title }}
        </component>
        <p
          v-if="description"
          class="cv-page-header__description"
        >
          {{ description }}
        </p>
        <div
          v-if="$slots.meta"
          class="cv-page-header__meta"
        >
          <slot name="meta" />
        </div>
      </div>
      <div
        v-if="$slots.actions"
        class="cv-page-header__actions"
      >
        <slot name="actions" />
      </div>
    </div>
  </header>
</template>

<style scoped>
.cv-page-header {
  display: grid;
  min-width: 0;
  gap: var(--cv-space-2);
  padding-block: var(--cv-space-1) var(--cv-space-2);
}

.cv-page-header__row {
  display: flex;
  min-width: 0;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--cv-space-4);
}

.cv-page-header__copy { min-width: 0; }

.cv-page-header__eyebrow {
  margin: 0 0 var(--cv-space-1);
  color: var(--cv-color-action-text);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-semibold);
  letter-spacing: var(--cv-letter-spacing-caption);
}

.cv-page-header__title {
  margin: 0;
  color: var(--cv-text-primary);
  font-size: var(--cv-type-page-title-size);
  font-weight: var(--cv-font-weight-semibold);
  letter-spacing: var(--cv-letter-spacing-title);
  line-height: var(--cv-line-height-tight);
  text-wrap: balance;
}

.cv-page-header__description {
  max-width: 76ch;
  margin: var(--cv-space-2) 0 0;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-md);
  line-height: var(--cv-line-height-normal);
  text-wrap: pretty;
}

.cv-page-header__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--cv-space-2);
  margin-top: var(--cv-space-2);
}

.cv-page-header__actions {
  display: flex;
  flex: 0 0 auto;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: var(--cv-space-2);
}

@media (max-width: 640px) {
  .cv-page-header__row { align-items: stretch; flex-direction: column; }
  .cv-page-header__actions { justify-content: flex-start; }
}
</style>
