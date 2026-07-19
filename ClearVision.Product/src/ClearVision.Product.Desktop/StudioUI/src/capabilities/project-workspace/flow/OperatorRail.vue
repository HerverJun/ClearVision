<script setup lang="ts">
import { computed, shallowRef } from 'vue';
import type { OperatorCatalogItem, OperatorCategoryId } from '@/capabilities/operators-read/operatorContracts';
import {
  operatorCategoryLabels,
  operatorLifecycleLabels
} from '@/capabilities/operators-read/operatorViewModel';
import { CvIconButton, CvSearchField } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import WorkspacePaneHeader from '../WorkspacePaneHeader.vue';
import type { OperatorCatalogProjection } from './flowCanvasOwner';

const props = defineProps<{
  catalog: OperatorCatalogProjection;
  readonly: boolean;
}>();

const emit = defineEmits<{
  add: [operator: OperatorCatalogItem];
  refresh: [];
}>();

const search = shallowRef('');
const activeCategory = shallowRef<'' | OperatorCategoryId>('');
const showCompatibility = shallowRef(false);
const draggingOperatorType = shallowRef<string | null>(null);

function normalized(value: string | null | undefined): string {
  return (value ?? '').trim().toLocaleLowerCase('zh-CN');
}

function matches(operator: OperatorCatalogItem, query: string): boolean {
  if (!query) return true;
  return [
    operator.operatorType,
    operator.displayName,
    operator.description,
    operator.category,
    ...operator.keywords,
    ...operator.tags,
    ...operator.inputPorts.flatMap(port => [port.name, port.displayName, port.dataType]),
    ...operator.outputPorts.flatMap(port => [port.name, port.displayName, port.dataType]),
    ...operator.parameters.flatMap(parameter => [
      parameter.name,
      parameter.displayName,
      parameter.dataType,
      parameter.description ?? ''
    ])
  ].some(value => normalized(value).includes(query));
}

const availableOperators = computed(() => props.catalog.operators.filter(operator =>
  showCompatibility.value || !operator.defaultHidden));

const visibleOperators = computed(() => {
  const query = normalized(search.value);
  return availableOperators.value.filter(operator =>
    (!activeCategory.value || operator.categoryId === activeCategory.value) &&
    matches(operator, query));
});

const categories = computed(() => {
  const counts = new Map<OperatorCategoryId, number>();
  for (const operator of availableOperators.value) {
    counts.set(operator.categoryId, (counts.get(operator.categoryId) ?? 0) + 1);
  }
  return Object.entries(operatorCategoryLabels)
    .map(([id, label]) => ({ id: id as OperatorCategoryId, label, count: counts.get(id as OperatorCategoryId) ?? 0 }))
    .filter(item => item.count > 0);
});

const categoryLabel = computed(() => activeCategory.value
  ? operatorCategoryLabels[activeCategory.value]
  : '全部分类');
const operatorCountLabel = computed(() => visibleOperators.value.length === availableOperators.value.length
  ? `${availableOperators.value.length} 项`
  : `${visibleOperators.value.length} / ${availableOperators.value.length}`);

function dragPayload(operator: OperatorCatalogItem): string {
  return JSON.stringify({
    type: operator.operatorType,
    operatorType: operator.operatorType,
    name: operator.displayName,
    displayName: operator.displayName,
    category: operator.category,
    iconName: operator.iconName,
    inputPorts: operator.inputPorts,
    outputPorts: operator.outputPorts,
    parameters: operator.parameters
  });
}

function startDrag(event: DragEvent, operator: OperatorCatalogItem): void {
  if (props.readonly || !event.dataTransfer) {
    event.preventDefault();
    return;
  }
  draggingOperatorType.value = operator.operatorType;
  const payload = dragPayload(operator);
  event.dataTransfer.effectAllowed = 'copy';
  event.dataTransfer.setData('application/json', payload);
  event.dataTransfer.setData('text/plain', operator.displayName);
}

function finishDrag(): void {
  draggingOperatorType.value = null;
}
</script>

<template>
  <aside
    class="operator-rail"
    data-evidence-surface="f03-g2-operator-rail"
    :data-catalog-phase="catalog.phase"
    :data-operator-count="visibleOperators.length"
    :data-active-category="activeCategory || 'all'"
    :data-dragging-operator="draggingOperatorType ?? ''"
  >
    <WorkspacePaneHeader
      title="算子"
      :detail="operatorCountLabel"
    >
      <CvIconButton
        size="sm"
        label="刷新算子目录"
        title="重新读取算子目录"
        :loading="catalog.isRefreshing"
        @click="emit('refresh')"
      >
        <CvIcon
          name="refresh"
          size="sm"
        />
      </CvIconButton>
    </WorkspacePaneHeader>

    <div class="operator-rail__controls">
      <CvSearchField
        v-model="search"
        name="operator-search"
        label="搜索算子"
        placeholder="名称、类型或参数…"
        input-test-id="operator-search"
      />

      <label class="operator-rail__category">
        <span>分类</span>
        <select
          v-model="activeCategory"
          name="operator-category"
          data-testid="operator-category"
        >
          <option value="">全部分类（{{ availableOperators.length }}）</option>
          <option
            v-for="category in categories"
            :key="category.id"
            :value="category.id"
            :data-category="category.id"
          >
            {{ category.label }}（{{ category.count }}）
          </option>
        </select>
        <CvIcon
          class="operator-rail__category-chevron"
          name="chevron-right"
          size="sm"
        />
      </label>

      <label class="operator-rail__compatibility">
        <input
          v-model="showCompatibility"
          type="checkbox"
          name="operator-compatibility"
        >
        <span>显示兼容算子</span>
      </label>
    </div>

    <div class="operator-rail__list-heading">
      <strong>{{ categoryLabel }}</strong>
      <small>单击添加 · 拖动定位</small>
    </div>

    <p
      v-if="catalog.message"
      class="operator-rail__message"
      aria-live="polite"
    >
      {{ catalog.message }}
    </p>

    <div
      class="operator-rail__list"
      aria-label="算子列表"
    >
      <button
        v-for="operator in visibleOperators"
        :key="operator.operatorType"
        type="button"
        class="operator-item operator-rail__item"
        :class="{ 'is-dragging': draggingOperatorType === operator.operatorType }"
        :data-type="operator.operatorType"
        :data-name="operator.displayName"
        :data-operator="dragPayload(operator)"
        :data-dragging="draggingOperatorType === operator.operatorType"
        :draggable="!readonly"
        :disabled="readonly"
        :title="readonly ? '当前工作区只读，不能添加算子' : `${operator.displayName}：单击添加到画布，或拖动到指定位置`"
        @click="emit('add', operator)"
        @dragstart="startDrag($event, operator)"
        @dragend="finishDrag"
      >
        <span class="operator-rail__drag-handle">
          <CvIcon
            name="drag"
            size="sm"
          />
        </span>
        <span class="operator-rail__item-content">
          <span class="operator-rail__item-main">
            <strong :title="operator.displayName">{{ operator.displayName }}</strong>
            <em :data-lifecycle="operator.lifecycle">{{ operatorLifecycleLabels[operator.lifecycle] }}</em>
          </span>
          <span
            class="operator-rail__item-description"
            :title="operator.description || operator.operatorType"
          >{{ operator.description || operator.operatorType }}</span>
          <span class="operator-rail__item-meta">
            <small>{{ operatorCategoryLabels[operator.categoryId] }}</small>
            <code
              translate="no"
              :title="operator.operatorType"
            >{{ operator.operatorType }}</code>
          </span>
        </span>
      </button>
      <p
        v-if="catalog.operators.length > 0 && visibleOperators.length === 0"
        class="operator-rail__empty"
      >
        <strong>没有匹配的算子</strong>
        <span>调整关键词、分类或兼容算子选项后重试。</span>
      </p>
    </div>
  </aside>
</template>

<style scoped>
.operator-rail {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: auto auto auto auto minmax(0, 1fr);
  overflow: hidden;
  border-right: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}

.operator-rail__controls {
  display: grid;
  gap: var(--cv-space-2);
  padding: var(--cv-space-2) var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-subtle);
}

.operator-rail__controls :deep(.cv-search-field) { min-width: 0; gap: 0; }
.operator-rail__controls :deep(.cv-search-field__control) { height: 30px; font-size: var(--cv-font-size-xs); }
.operator-rail__category {
  position: relative;
  min-width: 0;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  align-items: center;
  gap: var(--cv-space-2);
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
}
.operator-rail__category select {
  width: 100%;
  min-width: 0;
  height: 28px;
  padding: 0 26px 0 var(--cv-space-2);
  appearance: none;
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-page);
  color: var(--cv-text-primary);
  font: inherit;
  font-size: var(--cv-font-size-xs);
}
.operator-rail__category select:hover { border-color: var(--cv-control-border-hover); }
.operator-rail__category select:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.operator-rail__category-chevron {
  position: absolute;
  right: var(--cv-space-2);
  color: var(--cv-text-muted);
  pointer-events: none;
  transform: rotate(90deg);
}
.operator-rail__compatibility {
  min-height: 22px;
  display: inline-flex;
  align-items: center;
  gap: var(--cv-space-2);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-2xs);
  cursor: pointer;
}
.operator-rail__compatibility input { margin: 0; }

.operator-rail__list-heading {
  min-width: 0;
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--cv-space-2);
  padding: 7px var(--cv-space-3) 5px;
  color: var(--cv-text-muted);
}
.operator-rail__list-heading strong {
  overflow: hidden;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-2xs);
  font-weight: var(--cv-font-weight-semibold);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.operator-rail__list-heading small { flex: 0 0 auto; font-size: 9px; }

.operator-rail__list {
  grid-row: 5;
  min-height: 0;
  padding: 0 var(--cv-space-2) var(--cv-space-2);
  overflow-y: auto;
  overflow-x: hidden;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}
.operator-rail__item {
  width: 100%;
  min-width: 0;
  min-height: 58px;
  padding: var(--cv-space-2) var(--cv-space-1);
  display: grid;
  grid-template-columns: 14px minmax(0, 1fr);
  align-items: start;
  gap: var(--cv-space-1);
  text-align: left;
  border: 0;
  border-bottom: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-xs);
  background: transparent;
  color: var(--cv-text-primary);
  cursor: grab;
  content-visibility: auto;
  contain-intrinsic-size: 58px;
  touch-action: manipulation;
  user-select: none;
  transition:
    background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    opacity var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.operator-rail__item:hover:not(:disabled) { background: var(--cv-interactive-hover); }
.operator-rail__item:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }
.operator-rail__item:active:not(:disabled) { background: var(--cv-interactive-active); cursor: grabbing; }
.operator-rail__item.is-dragging { background: var(--cv-color-brand-soft); color: var(--cv-color-brand-text); opacity: 0.72; }
.operator-rail__item:disabled { color: var(--cv-text-muted); cursor: not-allowed; opacity: 0.58; }
.operator-rail__drag-handle { padding-top: 1px; color: var(--cv-text-muted); }
.operator-rail__item-content { min-width: 0; display: grid; gap: 3px; }
.operator-rail__item-main,
.operator-rail__item-meta { min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
.operator-rail__item-main strong,
.operator-rail__item-description,
.operator-rail__item-meta code,
.operator-rail__item-meta small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.operator-rail__item-main strong { font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.operator-rail__item-main em {
  flex: 0 0 auto;
  color: var(--cv-text-muted);
  font-size: 9px;
  font-style: normal;
}
.operator-rail__item-main em[data-lifecycle="Experimental"],
.operator-rail__item-main em[data-lifecycle="Reference"] { color: var(--cv-color-status-info-strong); }
.operator-rail__item-main em[data-lifecycle="Legacy"],
.operator-rail__item-main em[data-lifecycle="Deprecated"] { color: var(--cv-color-status-warning-strong); }
.operator-rail__item-description { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: 1.35; }
.operator-rail__item-meta { color: var(--cv-text-muted); font-size: 9px; }
.operator-rail__item-meta code { min-width: 0; font-family: var(--cv-font-mono); font-size: 9px; }
.operator-rail__item-meta small { max-width: 48%; }

.operator-rail__message,
.operator-rail__empty {
  margin: 0;
  padding: var(--cv-space-2) var(--cv-space-3);
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
  line-height: 1.45;
  overflow-wrap: anywhere;
}
.operator-rail__empty { display: grid; align-content: start; gap: var(--cv-space-1); }
.operator-rail__empty strong { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
</style>
