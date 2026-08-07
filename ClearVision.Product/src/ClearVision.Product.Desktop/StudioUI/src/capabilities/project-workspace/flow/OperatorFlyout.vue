<script setup lang="ts">
import { computed } from 'vue';
import type { OperatorCatalogItem, OperatorCategoryId } from '@/capabilities/operators-read/operatorContracts';
import { operatorCategoryLabels, operatorLifecycleLabels } from '@/capabilities/operators-read/operatorViewModel';
import { CvIconButton, CvSearchField } from '@/design-system';
import { CvIcon } from '@/design-system/icons';

const props = defineProps<{
  operators: readonly OperatorCatalogItem[];
  availableCount: number;
  categories: readonly Readonly<{ id: OperatorCategoryId; label: string; count: number }>[];
  activeCategory: '' | OperatorCategoryId;
  activeLabel: string;
  search: string;
  showCompatibility: boolean;
  readonly: boolean;
  refreshing: boolean;
  message: string | null;
  draggingOperatorType: string | null;
  favoriteOperatorTypes: readonly string[];
}>();

const emit = defineEmits<{
  close: [];
  refresh: [];
  add: [operator: OperatorCatalogItem];
  toggleFavorite: [operatorType: string];
  dragStart: [event: DragEvent, operator: OperatorCatalogItem];
  dragEnd: [];
  'update:search': [value: string];
  'update:activeCategory': [value: '' | OperatorCategoryId];
  'update:showCompatibility': [value: boolean];
}>();

const favoriteSet = computed(() => new Set(props.favoriteOperatorTypes));

function updateCategory(event: Event): void {
  emit('update:activeCategory', (event.target as HTMLSelectElement).value as '' | OperatorCategoryId);
}

function updateCompatibility(event: Event): void {
  emit('update:showCompatibility', (event.target as HTMLInputElement).checked);
}
</script>

<template>
  <section
    class="operator-flyout"
    data-capability="operator-flyout"
    aria-label="算子选择面板"
  >
    <header class="operator-flyout__header">
      <div>
        <strong>{{ activeLabel }}</strong>
        <small>{{ operators.length }} / {{ availableCount }} 项</small>
      </div>
      <div class="operator-flyout__header-actions">
        <CvIconButton
          size="sm"
          label="刷新算子目录"
          title="重新读取算子目录"
          :loading="refreshing"
          @click="emit('refresh')"
        >
          <CvIcon
            name="refresh"
            size="sm"
          />
        </CvIconButton>
        <CvIconButton
          size="sm"
          label="关闭算子面板"
          @click="emit('close')"
        >
          <CvIcon
            name="close"
            size="sm"
          />
        </CvIconButton>
      </div>
    </header>

    <div class="operator-flyout__controls">
      <CvSearchField
        :model-value="search"
        name="operator-search"
        label="搜索算子"
        placeholder="搜索名称、类型、端口或参数…"
        input-test-id="operator-search"
        @update:model-value="emit('update:search', $event)"
      />
      <label class="operator-flyout__category">
        <span>分类</span>
        <select
          name="operator-category"
          data-testid="operator-category"
          :value="activeCategory"
          @change="updateCategory"
        >
          <option value="">全部分类（{{ availableCount }}）</option>
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
          class="operator-flyout__category-chevron"
          name="chevron-right"
          size="sm"
        />
      </label>
      <label class="operator-flyout__compatibility">
        <input
          type="checkbox"
          name="operator-compatibility"
          :checked="showCompatibility"
          @change="updateCompatibility"
        >
        <span>显示兼容算子</span>
      </label>
    </div>

    <div class="operator-flyout__list-heading">
      <strong>单击添加</strong>
      <small>也可拖动到画布定位</small>
    </div>

    <p
      v-if="message"
      class="operator-flyout__message"
      aria-live="polite"
    >
      {{ message }}
    </p>

    <div
      class="operator-flyout__list"
      aria-label="算子列表"
    >
      <article
        v-for="operator in operators"
        :key="operator.operatorType"
        class="operator-flyout__item-shell"
      >
        <button
          type="button"
          class="operator-item operator-flyout__item"
          :class="{ 'is-dragging': draggingOperatorType === operator.operatorType }"
          :data-type="operator.operatorType"
          :data-name="operator.displayName"
          :data-operator="JSON.stringify(operator)"
          :data-dragging="draggingOperatorType === operator.operatorType"
          :draggable="!readonly"
          :disabled="readonly"
          :title="readonly ? '当前工作区只读，不能添加算子' : `${operator.displayName}：单击添加到画布，或拖动到指定位置`"
          @click="emit('add', operator)"
          @dragstart="emit('dragStart', $event, operator)"
          @dragend="emit('dragEnd')"
        >
          <span class="operator-flyout__drag-handle"><CvIcon
            name="drag"
            size="sm"
          /></span>
          <span class="operator-flyout__item-content">
            <span class="operator-flyout__item-main">
              <strong :title="operator.displayName">{{ operator.displayName }}</strong>
              <em :data-lifecycle="operator.lifecycle">{{ operatorLifecycleLabels[operator.lifecycle] }}</em>
            </span>
            <span
              class="operator-flyout__item-description"
              :title="operator.description || operator.operatorType"
            >
              {{ operator.description || operator.operatorType }}
            </span>
            <span class="operator-flyout__item-meta">
              <small>{{ operatorCategoryLabels[operator.categoryId] }}</small>
              <code
                translate="no"
                :title="operator.operatorType"
              >{{ operator.operatorType }}</code>
            </span>
          </span>
        </button>
        <button
          type="button"
          class="operator-flyout__favorite"
          :class="{ 'is-active': favoriteSet.has(operator.operatorType) }"
          :aria-label="favoriteSet.has(operator.operatorType) ? `取消收藏 ${operator.displayName}` : `收藏 ${operator.displayName}`"
          :aria-pressed="favoriteSet.has(operator.operatorType)"
          @click="emit('toggleFavorite', operator.operatorType)"
        >
          <CvIcon
            name="star"
            size="sm"
          />
        </button>
      </article>
      <p
        v-if="operators.length === 0"
        class="operator-flyout__empty"
      >
        <strong>没有匹配的算子</strong>
        <span>调整关键词、分类、最近/收藏范围或兼容算子选项后重试。</span>
      </p>
    </div>
  </section>
</template>

<style scoped>
.operator-flyout {
  width: 272px;
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: 42px auto auto auto minmax(0, 1fr);
  overflow: hidden;
  border: 1px solid var(--cv-border-subtle);
  border-left: 0;
  border-radius: 0 var(--cv-radius-md) var(--cv-radius-md) 0;
  background: var(--cv-surface-floating);
  box-shadow: var(--cv-elevation-floating);
}
.operator-flyout__header,
.operator-flyout__header-actions,
.operator-flyout__list-heading,
.operator-flyout__item-main,
.operator-flyout__item-meta { min-width: 0; display: flex; align-items: center; }
.operator-flyout__header { justify-content: space-between; gap: var(--cv-space-2); padding: 0 var(--cv-space-2) 0 var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.operator-flyout__header > div:first-child { min-width: 0; display: grid; gap: 1px; }
.operator-flyout__header strong { overflow: hidden; font-size: var(--cv-font-size-sm); text-overflow: ellipsis; white-space: nowrap; }
.operator-flyout__header small { color: var(--cv-text-muted); font-size: 10px; }
.operator-flyout__header-actions { flex: 0 0 auto; gap: 2px; }
.operator-flyout__controls { display: grid; gap: var(--cv-space-2); padding: var(--cv-space-2) var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.operator-flyout__controls :deep(.cv-search-field) { min-width: 0; gap: 0; }
.operator-flyout__controls :deep(.cv-search-field__control) { height: 30px; font-size: var(--cv-font-size-xs); }
.operator-flyout__category { position: relative; display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: var(--cv-space-2); color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.operator-flyout__category select { width: 100%; min-width: 0; height: 28px; padding: 0 26px 0 var(--cv-space-2); appearance: none; border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); font: inherit; font-size: var(--cv-font-size-xs); }
.operator-flyout__category select:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.operator-flyout__category-chevron { position: absolute; right: var(--cv-space-2); color: var(--cv-text-muted); pointer-events: none; transform: rotate(90deg); }
.operator-flyout__compatibility { min-height: 22px; display: inline-flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); cursor: pointer; }
.operator-flyout__compatibility input { margin: 0; accent-color: var(--cv-color-industrial-blue); }
.operator-flyout__list-heading { justify-content: space-between; gap: var(--cv-space-2); padding: 7px var(--cv-space-3) 5px; color: var(--cv-text-muted); }
.operator-flyout__list-heading strong { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.operator-flyout__list-heading small { font-size: 9px; }
.operator-flyout__message,
.operator-flyout__empty { margin: 0; padding: var(--cv-space-2) var(--cv-space-3); color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: 1.45; overflow-wrap: anywhere; }
.operator-flyout__list { min-height: 0; padding: 0 var(--cv-space-2) var(--cv-space-2); overflow: auto; overscroll-behavior: contain; scrollbar-gutter: stable; }
.operator-flyout__item-shell { position: relative; min-width: 0; border-bottom: 1px solid var(--cv-border-subtle); }
.operator-flyout__item { width: 100%; min-width: 0; min-height: 62px; padding: var(--cv-space-2) 28px var(--cv-space-2) 2px; display: grid; grid-template-columns: 14px minmax(0, 1fr); align-items: start; gap: var(--cv-space-1); text-align: left; border: 0; background: transparent; color: var(--cv-text-primary); cursor: grab; content-visibility: auto; contain-intrinsic-size: 62px; }
.operator-flyout__item:hover:not(:disabled) { background: var(--cv-interactive-hover); }
.operator-flyout__item:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }
.operator-flyout__item:active:not(:disabled) { background: var(--cv-interactive-active); cursor: grabbing; }
.operator-flyout__item.is-dragging { background: var(--cv-color-brand-soft); color: var(--cv-color-brand-text); opacity: .76; }
.operator-flyout__item:disabled { color: var(--cv-text-muted); cursor: not-allowed; opacity: .58; }
.operator-flyout__drag-handle { padding-top: 1px; color: var(--cv-text-muted); }
.operator-flyout__item-content { min-width: 0; display: grid; gap: 3px; }
.operator-flyout__item-main,
.operator-flyout__item-meta { justify-content: space-between; gap: var(--cv-space-2); }
.operator-flyout__item-main strong,
.operator-flyout__item-description,
.operator-flyout__item-meta code,
.operator-flyout__item-meta small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.operator-flyout__item-main strong { font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.operator-flyout__item-main em { flex: 0 0 auto; color: var(--cv-text-muted); font-size: 9px; font-style: normal; }
.operator-flyout__item-main em[data-lifecycle="Experimental"],
.operator-flyout__item-main em[data-lifecycle="Reference"] { color: var(--cv-color-status-info-strong); }
.operator-flyout__item-main em[data-lifecycle="Legacy"],
.operator-flyout__item-main em[data-lifecycle="Deprecated"] { color: var(--cv-color-status-warning-strong); }
.operator-flyout__item-description { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: 1.35; }
.operator-flyout__item-meta { color: var(--cv-text-muted); font-size: 9px; }
.operator-flyout__item-meta code { min-width: 0; font-family: var(--cv-font-mono); font-size: 9px; }
.operator-flyout__item-meta small { max-width: 50%; }
.operator-flyout__favorite { position: absolute; top: 7px; right: 4px; width: 24px; height: 24px; display: grid; place-items: center; border: 0; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-text-muted); cursor: pointer; }
.operator-flyout__favorite:hover { background: var(--cv-interactive-hover); color: var(--cv-color-industrial-blue); }
.operator-flyout__favorite.is-active { color: var(--cv-color-brand-500); }
.operator-flyout__favorite:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 0; }
.operator-flyout__empty { display: grid; gap: var(--cv-space-1); }
.operator-flyout__empty strong { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }

@media (max-height: 760px) {
  .operator-flyout { grid-template-rows: 38px auto auto auto minmax(0, 1fr); }
  .operator-flyout__controls { gap: var(--cv-space-1); padding-block: var(--cv-space-1); }
  .operator-flyout__item { min-height: 56px; contain-intrinsic-size: 56px; }
}
</style>
