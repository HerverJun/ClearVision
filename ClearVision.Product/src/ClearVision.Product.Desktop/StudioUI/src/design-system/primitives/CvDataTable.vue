<script setup lang="ts" generic="Row">
import { computed } from 'vue';
import type { CSSProperties } from 'vue';
import CvIcon from '../icons/CvIcon.vue';
import type {
  CvDataTableColumn,
  CvDataTableSort,
  CvSortDirection
} from './types';

const props = withDefaults(defineProps<{
  rows: readonly Row[];
  columns: readonly CvDataTableColumn<Row>[];
  rowKey?: string | ((row: Row, index: number) => string | number) | undefined;
  caption?: string;
  captionVisible?: boolean;
  busy?: boolean;
  loadingLabel?: string;
  emptyLabel?: string;
  missingValueLabel?: string;
  sortKey?: string | undefined;
  sortDirection?: CvSortDirection;
}>(), {
  rowKey: undefined,
  caption: '数据表',
  captionVisible: false,
  busy: false,
  loadingLabel: '正在加载数据',
  emptyLabel: '暂无数据',
  missingValueLabel: '—',
  sortKey: undefined,
  sortDirection: 'ascending'
});

const emit = defineEmits<{
  sort: [sort: CvDataTableSort];
  'update:sortKey': [key: string];
  'update:sortDirection': [direction: CvSortDirection];
}>();

const hasRows = computed(() => props.rows.length > 0);

function rowIdentity(row: Row, index: number): string | number {
  if (typeof props.rowKey === 'function') return props.rowKey(row, index);
  if (props.rowKey !== undefined && typeof row === 'object' && row !== null) {
    const value = (row as Record<string, unknown>)[props.rowKey];
    if (typeof value === 'string' || typeof value === 'number') return value;
  }
  return index;
}

function cellValue(row: Row, column: CvDataTableColumn<Row>): unknown {
  if (column.value) return column.value(row);
  if (typeof row === 'object' && row !== null && column.key in row) {
    return (row as Record<string, unknown>)[column.key];
  }
  return undefined;
}

function displayValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return props.missingValueLabel;
  return String(value);
}

function columnStyle(column: CvDataTableColumn<Row>): CSSProperties | undefined {
  return column.width ? { width: column.width } : undefined;
}

function ariaSort(column: CvDataTableColumn<Row>): CvSortDirection | 'none' | undefined {
  if (!column.sortable) return undefined;
  return props.sortKey === column.key ? props.sortDirection : 'none';
}

function requestSort(column: CvDataTableColumn<Row>): void {
  if (!column.sortable) return;
  const direction: CvSortDirection = props.sortKey === column.key && props.sortDirection === 'ascending'
    ? 'descending'
    : 'ascending';
  const sort = Object.freeze({ key: column.key, direction });
  emit('update:sortKey', column.key);
  emit('update:sortDirection', direction);
  emit('sort', sort);
}
</script>

<template>
  <div
    class="cv-data-table"
    :class="{ 'cv-data-table--busy': busy }"
    :aria-busy="busy ? 'true' : undefined"
    data-design-primitive="data-table"
  >
    <div class="cv-data-table__scroll-region">
      <table>
        <caption :class="{ 'cv-data-table__caption--hidden': !captionVisible }">
          {{ caption }}
        </caption>
        <thead>
          <tr>
            <th
              v-for="column in columns"
              :key="column.key"
              scope="col"
              :class="`cv-data-table__cell--${column.align ?? 'start'}`"
              :style="columnStyle(column)"
              :aria-sort="ariaSort(column)"
            >
              <button
                v-if="column.sortable"
                class="cv-data-table__sort"
                type="button"
                :aria-label="`按${column.label}排序`"
                @click="requestSort(column)"
              >
                <span>{{ column.label }}</span>
                <CvIcon
                  class="cv-data-table__sort-icon"
                  :class="{
                    'cv-data-table__sort-icon--active': sortKey === column.key,
                    'cv-data-table__sort-icon--descending': sortKey === column.key && sortDirection === 'descending'
                  }"
                  name="chevron-right"
                  size="sm"
                />
              </button>
              <span v-else>{{ column.label }}</span>
            </th>
          </tr>
        </thead>
        <tbody v-if="hasRows">
          <tr
            v-for="(row, rowIndex) in rows"
            :key="rowIdentity(row, rowIndex)"
          >
            <td
              v-for="column in columns"
              :key="column.key"
              :class="`cv-data-table__cell--${column.align ?? 'start'}`"
            >
              <slot
                :name="`cell-${column.key}`"
                :row="row"
                :value="cellValue(row, column)"
                :column="column"
              >
                {{ displayValue(cellValue(row, column)) }}
              </slot>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div
      v-if="busy"
      class="cv-data-table__message"
      role="status"
      aria-live="polite"
    >
      <span
        class="cv-data-table__spinner"
        aria-hidden="true"
      />
      <span>{{ loadingLabel }}</span>
    </div>
    <div
      v-else-if="!hasRows"
      class="cv-data-table__message"
    >
      <slot name="empty">
        {{ emptyLabel }}
      </slot>
    </div>
  </div>
</template>

<style scoped>
.cv-data-table {
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-md);
  background: var(--cv-surface-1);
}

.cv-data-table__scroll-region {
  max-width: 100%;
  overflow: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  text-align: left;
}

caption {
  padding: var(--cv-space-3) var(--cv-space-4);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-semibold);
  text-align: left;
}

.cv-data-table__caption--hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
}

thead { background: var(--cv-surface-2); }

th,
td {
  min-width: 0;
  height: var(--cv-density-row-height);
  padding: var(--cv-space-2) var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-subtle);
  vertical-align: middle;
}

th {
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-semibold);
  white-space: nowrap;
}

tbody tr:last-child td { border-bottom: 0; }
tbody tr:hover { background: var(--cv-interactive-hover); }
.cv-data-table__cell--center { text-align: center; }
.cv-data-table__cell--end { text-align: right; }

.cv-data-table__sort {
  display: inline-flex;
  align-items: center;
  gap: var(--cv-space-1);
  padding: var(--cv-space-1);
  border: 0;
  border-radius: var(--cv-radius-xs);
  background: transparent;
  color: inherit;
  cursor: pointer;
  font: inherit;
  font-weight: inherit;
}

.cv-data-table__sort:hover { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.cv-data-table__sort-icon { opacity: 0.35; transform: rotate(90deg); }
.cv-data-table__sort-icon--active { color: var(--cv-color-brand-text); opacity: 1; }
.cv-data-table__sort-icon--descending { transform: rotate(-90deg); }

.cv-data-table__message {
  display: flex;
  min-height: calc(var(--cv-density-row-height) * 2);
  align-items: center;
  justify-content: center;
  gap: var(--cv-space-2);
  padding: var(--cv-space-4);
  border-top: 1px solid var(--cv-border-subtle);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
}

.cv-data-table__spinner {
  width: 14px;
  height: 14px;
  border: 2px solid var(--cv-border-strong);
  border-right-color: var(--cv-color-brand-500);
  border-radius: 50%;
  animation: cv-data-table-spin var(--cv-motion-duration-slow) linear infinite;
}

@keyframes cv-data-table-spin { to { transform: rotate(360deg); } }
</style>
