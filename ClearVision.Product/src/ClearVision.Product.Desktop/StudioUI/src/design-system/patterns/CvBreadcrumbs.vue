<script setup lang="ts">
import { computed } from 'vue';
import CvIcon from '../icons/CvIcon.vue';
import type { CvBreadcrumbItem } from './types';

const props = withDefaults(defineProps<{
  items: readonly CvBreadcrumbItem[];
  label?: string;
}>(), {
  label: '面包屑导航'
});

const hasExplicitCurrent = computed(() => props.items.some(item => item.current !== undefined));

function isCurrent(item: CvBreadcrumbItem, index: number): boolean {
  return hasExplicitCurrent.value ? item.current === true : index === props.items.length - 1;
}
</script>

<template>
  <nav
    class="cv-breadcrumbs"
    :aria-label="label"
    data-design-pattern="breadcrumbs"
  >
    <ol>
      <li
        v-for="(item, index) in items"
        :key="`${item.label}-${index}`"
      >
        <CvIcon
          v-if="index > 0"
          class="cv-breadcrumbs__separator"
          name="chevron-right"
          size="sm"
        />
        <span
          class="cv-breadcrumbs__item"
          :aria-current="isCurrent(item, index) ? 'page' : undefined"
        >
          <slot
            name="item"
            :item="item"
            :current="isCurrent(item, index)"
            :index="index"
          >
            <span v-if="isCurrent(item, index) || !item.href">
              {{ item.label }}
            </span>
            <a
              v-else
              :href="item.href"
            >
              {{ item.label }}
            </a>
          </slot>
        </span>
      </li>
    </ol>
  </nav>
</template>

<style scoped>
.cv-breadcrumbs ol {
  display: flex;
  min-width: 0;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--cv-space-1);
  margin: 0;
  padding: 0;
  list-style: none;
}

.cv-breadcrumbs li {
  display: inline-flex;
  min-width: 0;
  align-items: center;
  gap: var(--cv-space-1);
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-xs);
}

.cv-breadcrumbs a { color: var(--cv-text-secondary); text-decoration: none; }
.cv-breadcrumbs a:hover { color: var(--cv-color-link); text-decoration: underline; }
.cv-breadcrumbs [aria-current="page"] { color: var(--cv-text-primary); font-weight: var(--cv-font-weight-medium); }
.cv-breadcrumbs__item { display: inline-flex; min-width: 0; }
.cv-breadcrumbs__separator { color: var(--cv-text-muted); }
</style>
