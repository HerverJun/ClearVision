<script setup lang="ts">
import { computed, nextTick, shallowRef, useTemplateRef } from 'vue';
import type { OperatorCatalogItem, OperatorCategoryId } from '@/capabilities/operators-read/operatorContracts';
import { operatorCategoryLabels } from '@/capabilities/operators-read/operatorViewModel';
import { CvIcon } from '@/design-system/icons';
import type { CvIconName } from '@/design-system/icons';
import type { OperatorCatalogProjection } from './flowCanvasOwner';
import OperatorFlyout from './OperatorFlyout.vue';

type OperatorRailMode = 'all' | 'recent' | 'favorites' | 'category';

const props = defineProps<{
  catalog: OperatorCatalogProjection;
  readonly: boolean;
  flyoutOpen: boolean;
}>();

const emit = defineEmits<{
  add: [operator: OperatorCatalogItem];
  refresh: [];
  'update:flyoutOpen': [value: boolean];
}>();

const favoritesStorageKey = 'clearvision.studio-ui.operator-favorites.v1';
const recentLimit = 12;
const search = shallowRef('');
const activeCategory = shallowRef<'' | OperatorCategoryId>('');
const activeMode = shallowRef<OperatorRailMode>('all');
const showCompatibility = shallowRef(false);
const draggingOperatorType = shallowRef<string | null>(null);
const recentOperatorTypes = shallowRef<readonly string[]>([]);
const favoriteOperatorTypes = shallowRef<readonly string[]>(readFavorites());
const categoriesNavigation = useTemplateRef<HTMLElement>('categoriesNavigation');

function readFavorites(): readonly string[] {
  try {
    const raw = globalThis.localStorage?.getItem(favoritesStorageKey);
    if (!raw) return Object.freeze([]);
    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed)
      ? Object.freeze(parsed.filter(value => typeof value === 'string'))
      : Object.freeze([]);
  } catch {
    return Object.freeze([]);
  }
}

function persistFavorites(values: readonly string[]): void {
  try {
    globalThis.localStorage?.setItem(favoritesStorageKey, JSON.stringify(values));
  } catch {
    // Favorites are a disposable UI preference; storage failure is non-fatal.
  }
}

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
  const recent = new Set(recentOperatorTypes.value);
  const favorites = new Set(favoriteOperatorTypes.value);
  return availableOperators.value.filter(operator => {
    if (activeMode.value === 'recent' && !recent.has(operator.operatorType)) return false;
    if (activeMode.value === 'favorites' && !favorites.has(operator.operatorType)) return false;
    if (activeMode.value === 'category' && activeCategory.value && operator.categoryId !== activeCategory.value) return false;
    return matches(operator, query);
  });
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

const activeLabel = computed(() => {
  if (activeMode.value === 'recent') return '最近使用';
  if (activeMode.value === 'favorites') return '收藏算子';
  if (activeMode.value === 'category' && activeCategory.value) return operatorCategoryLabels[activeCategory.value];
  return search.value ? '搜索结果' : '全部算子';
});

function categoryIcon(category: OperatorCategoryId): CvIconName {
  if (category === 'Acquisition') return 'camera';
  if (category === 'ImagePreprocessing' || category === 'DataProcessing') return 'sliders';
  if (category === 'SegmentationAndRegion') return 'region';
  if (category === 'Measurement' || category === 'CalibrationAndCoordinates') return 'measure';
  if (category === 'MatchingAndLocalization' || category === 'DefectDetection') return 'filter';
  if (category === 'AiInference') return 'spark';
  return 'operators';
}

function openMode(mode: OperatorRailMode, category: '' | OperatorCategoryId = ''): void {
  if (props.flyoutOpen && activeMode.value === mode && activeCategory.value === category) {
    emit('update:flyoutOpen', false);
    return;
  }
  activeMode.value = mode;
  activeCategory.value = category;
  emit('update:flyoutOpen', true);
}

function updateSearch(value: string): void {
  search.value = value;
  if (value.trim()) {
    activeMode.value = 'all';
    activeCategory.value = '';
    emit('update:flyoutOpen', true);
  }
}

function updateCategory(value: '' | OperatorCategoryId): void {
  activeCategory.value = value;
  activeMode.value = value ? 'category' : 'all';
  emit('update:flyoutOpen', true);
}

async function closeFlyout(restoreFocus = false): Promise<void> {
  const trigger = categoriesNavigation.value
    ?.querySelector<HTMLElement>('.operator-rail__category-button.is-active');
  emit('update:flyoutOpen', false);
  if (!restoreFocus) return;
  await nextTick();
  trigger?.focus({ preventScroll: true });
}

function handleEscape(event: KeyboardEvent): void {
  if (!props.flyoutOpen) return;
  event.preventDefault();
  event.stopPropagation();
  void closeFlyout(true);
}

function addOperator(operator: OperatorCatalogItem): void {
  recentOperatorTypes.value = Object.freeze([
    operator.operatorType,
    ...recentOperatorTypes.value.filter(type => type !== operator.operatorType)
  ].slice(0, recentLimit));
  emit('add', operator);
}

function toggleFavorite(operatorType: string): void {
  const current = new Set(favoriteOperatorTypes.value);
  if (current.has(operatorType)) current.delete(operatorType);
  else current.add(operatorType);
  favoriteOperatorTypes.value = Object.freeze([...current]);
  persistFavorites(favoriteOperatorTypes.value);
}

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
</script>

<template>
  <aside
    class="operator-rail"
    data-capability="operator-rail"
    data-evidence-surface="f03-g2-operator-rail"
    :data-catalog-phase="catalog.phase"
    :data-operator-count="visibleOperators.length"
    :data-active-category="activeCategory || 'all'"
    :data-dragging-operator="draggingOperatorType ?? ''"
    :data-flyout-open="props.flyoutOpen"
    @keydown.esc="handleEscape"
  >
    <nav
      ref="categoriesNavigation"
      class="operator-rail__categories"
      aria-label="算子分类"
    >
      <button
        type="button"
        class="operator-rail__category-button"
        :class="{ 'is-active': props.flyoutOpen && activeMode === 'all' }"
        :aria-expanded="props.flyoutOpen && activeMode === 'all'"
        aria-controls="operator-flyout"
        title="搜索与全部算子"
        aria-label="搜索与全部算子"
        @click="openMode('all')"
      >
        <CvIcon
          name="search"
          size="md"
        />
        <span>搜索</span>
      </button>
      <button
        type="button"
        class="operator-rail__category-button"
        :class="{ 'is-active': props.flyoutOpen && activeMode === 'recent' }"
        :aria-expanded="props.flyoutOpen && activeMode === 'recent'"
        aria-controls="operator-flyout"
        title="最近使用的算子"
        @click="openMode('recent')"
      >
        <CvIcon
          name="clock"
          size="md"
        />
        <span>最近</span>
      </button>
      <button
        type="button"
        class="operator-rail__category-button"
        :class="{ 'is-active': props.flyoutOpen && activeMode === 'favorites' }"
        :aria-expanded="props.flyoutOpen && activeMode === 'favorites'"
        aria-controls="operator-flyout"
        title="收藏的算子"
        @click="openMode('favorites')"
      >
        <CvIcon
          name="star"
          size="md"
        />
        <span>收藏</span>
      </button>
      <span
        class="operator-rail__separator"
        aria-hidden="true"
      />
      <button
        v-for="category in categories"
        :key="category.id"
        type="button"
        class="operator-rail__category-button"
        :class="{ 'is-active': props.flyoutOpen && activeMode === 'category' && activeCategory === category.id }"
        :aria-expanded="props.flyoutOpen && activeMode === 'category' && activeCategory === category.id"
        aria-controls="operator-flyout"
        :title="`${category.label}（${category.count}）`"
        @click="openMode('category', category.id)"
      >
        <CvIcon
          :name="categoryIcon(category.id)"
          size="md"
        />
        <span>{{ category.label }}</span>
      </button>
    </nav>

    <OperatorFlyout
      v-if="props.flyoutOpen"
      id="operator-flyout"
      class="operator-rail__flyout"
      :operators="visibleOperators"
      :available-count="availableOperators.length"
      :categories="categories"
      :active-category="activeCategory"
      :active-label="activeLabel"
      :search="search"
      :show-compatibility="showCompatibility"
      :readonly="readonly"
      :refreshing="catalog.isRefreshing"
      :message="catalog.message"
      :dragging-operator-type="draggingOperatorType"
      :favorite-operator-types="favoriteOperatorTypes"
      @close="closeFlyout(true)"
      @refresh="emit('refresh')"
      @add="addOperator"
      @toggle-favorite="toggleFavorite"
      @drag-start="startDrag"
      @drag-end="draggingOperatorType = null"
      @update:search="updateSearch"
      @update:active-category="updateCategory"
      @update:show-compatibility="showCompatibility = $event"
    />
  </aside>
</template>

<style scoped>
.operator-rail {
  position: relative;
  width: var(--cv-workspace-operator-rail-width);
  height: 100%;
  min-width: 0;
  min-height: 0;
  overflow: visible;
  border-right: 1px solid var(--cv-shell-sidebar-border);
  background: var(--cv-shell-sidebar);
  color: var(--cv-shell-sidebar-muted);
}
.operator-rail__categories { width: 100%; height: 100%; padding: var(--cv-space-2) 0; display: flex; flex-direction: column; gap: 2px; overflow-y: auto; overflow-x: hidden; overscroll-behavior: contain; scrollbar-width: none; }
.operator-rail__categories::-webkit-scrollbar { display: none; }
.operator-rail__category-button { position: relative; width: 100%; min-height: 44px; padding: 4px 2px; display: flex; flex: 0 0 auto; flex-direction: column; align-items: center; justify-content: center; gap: 3px; border: 0; background: transparent; color: var(--cv-shell-sidebar-muted); font: inherit; cursor: pointer; }
.operator-rail__category-button::before { position: absolute; inset: 8px auto 8px 0; width: 3px; border-radius: 0 var(--cv-radius-pill) var(--cv-radius-pill) 0; background: transparent; content: ''; }
.operator-rail__category-button span { width: 100%; overflow: hidden; font-size: var(--cv-font-size-2xs); line-height: 1.2; text-align: center; text-overflow: ellipsis; white-space: nowrap; }
.operator-rail__category-button:hover { background: var(--cv-shell-sidebar-hover); color: var(--cv-shell-sidebar-text); }
.operator-rail__category-button.is-active { background: var(--cv-color-brand-soft); color: var(--cv-color-brand-text); }
.operator-rail__category-button.is-active::before { background: var(--cv-color-brand-500); }
.operator-rail__category-button:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }
.operator-rail__separator { height: 1px; margin: 5px var(--cv-space-2); flex: 0 0 auto; background: var(--cv-shell-sidebar-border); }
.operator-rail__flyout { position: absolute; z-index: calc(var(--cv-z-dropdown) - 1); inset: 0 auto 0 100%; }

@media (max-height: 760px) {
  .operator-rail__categories { padding-block: var(--cv-space-1); }
  .operator-rail__category-button { min-height: 39px; }
}
</style>
