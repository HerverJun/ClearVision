<script setup lang="ts">
import { computed } from 'vue';
import CvIcon from '../icons/CvIcon.vue';

type PageItem = number | 'ellipsis-start' | 'ellipsis-end';

const props = withDefaults(defineProps<{
  page: number;
  pageSize: number;
  totalItems: number;
  label?: string;
  previousLabel?: string;
  nextLabel?: string;
  showSummary?: boolean;
  maxVisiblePages?: number;
}>(), {
  label: '分页导航',
  previousLabel: '上一页',
  nextLabel: '下一页',
  showSummary: true,
  maxVisiblePages: 5
});

const emit = defineEmits<{
  'update:page': [page: number];
  change: [page: number];
}>();

const totalPages = computed(() => Math.max(1, Math.ceil(Math.max(0, props.totalItems) / Math.max(1, props.pageSize))));
const currentPage = computed(() => Math.min(totalPages.value, Math.max(1, props.page)));
const hasItems = computed(() => props.totalItems > 0);
const firstItem = computed(() => hasItems.value ? ((currentPage.value - 1) * props.pageSize) + 1 : 0);
const lastItem = computed(() => hasItems.value ? Math.min(props.totalItems, currentPage.value * props.pageSize) : 0);

const pageItems = computed<readonly PageItem[]>(() => {
  const total = totalPages.value;
  const maximum = Math.max(3, props.maxVisiblePages);
  if (total <= maximum) return Array.from({ length: total }, (_, index) => index + 1);

  const pages = new Set<number>([1, total, currentPage.value]);
  let distance = 1;
  while (pages.size < maximum) {
    const before = currentPage.value - distance;
    const after = currentPage.value + distance;
    if (before > 1) pages.add(before);
    if (pages.size < maximum && after < total) pages.add(after);
    if (before <= 1 && after >= total) break;
    distance += 1;
  }

  const sorted = [...pages].sort((left, right) => left - right);
  const items: PageItem[] = [];
  sorted.forEach((page, index) => {
    const previous = sorted[index - 1];
    if (previous !== undefined && page - previous > 1) {
      items.push(index === 1 ? 'ellipsis-start' : 'ellipsis-end');
    }
    items.push(page);
  });
  return items;
});

function goTo(page: number): void {
  const next = Math.min(totalPages.value, Math.max(1, page));
  if (next === currentPage.value) return;
  emit('update:page', next);
  emit('change', next);
}
</script>

<template>
  <div
    class="cv-pagination"
    data-design-primitive="pagination"
  >
    <p
      v-if="showSummary"
      class="cv-pagination__summary"
      aria-live="polite"
    >
      第 {{ firstItem }}–{{ lastItem }} 项，共 {{ Math.max(0, totalItems) }} 项
    </p>
    <nav :aria-label="label">
      <ul class="cv-pagination__list">
        <li>
          <button
            class="cv-pagination__button"
            type="button"
            :disabled="currentPage <= 1"
            :aria-label="previousLabel"
            :title="previousLabel"
            @click="goTo(currentPage - 1)"
          >
            <CvIcon
              name="chevron-left"
              size="sm"
            />
          </button>
        </li>
        <li
          v-for="item in pageItems"
          :key="item"
        >
          <span
            v-if="typeof item !== 'number'"
            class="cv-pagination__ellipsis"
            aria-hidden="true"
          >…</span>
          <button
            v-else
            class="cv-pagination__button"
            :class="{ 'cv-pagination__button--current': item === currentPage }"
            type="button"
            :aria-label="`第 ${item} 页`"
            :aria-current="item === currentPage ? 'page' : undefined"
            @click="goTo(item)"
          >
            {{ item }}
          </button>
        </li>
        <li>
          <button
            class="cv-pagination__button"
            type="button"
            :disabled="currentPage >= totalPages"
            :aria-label="nextLabel"
            :title="nextLabel"
            @click="goTo(currentPage + 1)"
          >
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </button>
        </li>
      </ul>
    </nav>
  </div>
</template>

<style scoped>
.cv-pagination {
  display: flex;
  min-width: 0;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-3);
  padding-block: var(--cv-space-2);
}

.cv-pagination__summary {
  margin: 0;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  white-space: nowrap;
}

.cv-pagination__list {
  display: flex;
  align-items: center;
  gap: var(--cv-space-1);
  margin: 0;
  padding: 0;
  list-style: none;
}

.cv-pagination__button,
.cv-pagination__ellipsis {
  display: grid;
  min-width: var(--cv-density-control-height-sm);
  height: var(--cv-density-control-height-sm);
  place-items: center;
}

.cv-pagination__button {
  padding: 0 var(--cv-space-2);
  border: 1px solid transparent;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-secondary);
  cursor: pointer;
  font-size: var(--cv-font-size-xs);
}

.cv-pagination__button:hover:not(:disabled) { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.cv-pagination__button--current { border-color: var(--cv-color-action-border); background: var(--cv-color-action-soft); color: var(--cv-color-action-text); font-weight: var(--cv-font-weight-semibold); }
.cv-pagination__button:disabled { cursor: not-allowed; opacity: 0.42; }
.cv-pagination__ellipsis { color: var(--cv-text-muted); }

@media (max-width: 640px) {
  .cv-pagination { align-items: flex-start; flex-direction: column; }
}
</style>
