<script setup lang="ts">
import { computed, ref } from 'vue';
import type { OperatorCatalogItem, OperatorCategoryId } from '@/capabilities/operators-read/operatorContracts';
import {
  operatorCategoryLabels,
  operatorLifecycleLabels
} from '@/capabilities/operators-read/operatorViewModel';
import type { OperatorCatalogProjection } from './flowCanvasOwner';

const props = defineProps<{
  catalog: OperatorCatalogProjection;
  readonly: boolean;
}>();

const emit = defineEmits<{
  add: [operator: OperatorCatalogItem];
  refresh: [];
}>();

const search = ref('');
const activeCategory = ref<'' | OperatorCategoryId>('');
const showCompatibility = ref(false);

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

const visibleOperators = computed(() => {
  const query = normalized(search.value);
  return props.catalog.operators.filter(operator =>
    (showCompatibility.value || !operator.defaultHidden) &&
    (!activeCategory.value || operator.categoryId === activeCategory.value) &&
    matches(operator, query));
});

const categories = computed(() => {
  const counts = new Map<OperatorCategoryId, number>();
  for (const operator of props.catalog.operators) {
    if (!showCompatibility.value && operator.defaultHidden) continue;
    counts.set(operator.categoryId, (counts.get(operator.categoryId) ?? 0) + 1);
  }
  return Object.entries(operatorCategoryLabels)
    .map(([id, label]) => ({ id: id as OperatorCategoryId, label, count: counts.get(id as OperatorCategoryId) ?? 0 }))
    .filter(item => item.count > 0);
});

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
  const payload = dragPayload(operator);
  event.dataTransfer.effectAllowed = 'copy';
  event.dataTransfer.setData('application/json', payload);
  event.dataTransfer.setData('text/plain', operator.displayName);
}
</script>

<template>
  <aside
    class="operator-rail"
    data-evidence-surface="f03-g2-operator-rail"
    :data-catalog-phase="catalog.phase"
    :data-operator-count="visibleOperators.length"
  >
    <header class="operator-rail__header">
      <div>
        <strong>算子区</strong>
        <small>{{ catalog.operators.length }} 项 metadata</small>
      </div>
      <button
        type="button"
        class="operator-rail__refresh"
        :disabled="catalog.isRefreshing"
        aria-label="刷新算子目录"
        @click="emit('refresh')"
      >
        ↻
      </button>
    </header>

    <label class="operator-rail__search">
      <span>搜索算子</span>
      <input
        v-model="search"
        type="search"
        placeholder="名称、类型、端口或参数"
        data-testid="operator-search"
      >
    </label>

    <label class="operator-rail__compatibility">
      <input
        v-model="showCompatibility"
        type="checkbox"
      >
      <span>显示兼容/隐藏算子</span>
    </label>

    <nav
      class="operator-rail__categories"
      aria-label="算子分类"
    >
      <button
        type="button"
        :class="{ 'is-active': activeCategory === '' }"
        @click="activeCategory = ''"
      >
        <span>全部</span><small>{{ catalog.operators.length }}</small>
      </button>
      <button
        v-for="category in categories"
        :key="category.id"
        type="button"
        :class="{ 'is-active': activeCategory === category.id }"
        :data-category="category.id"
        @click="activeCategory = category.id"
      >
        <span>{{ category.label }}</span><small>{{ category.count }}</small>
      </button>
    </nav>

    <p
      v-if="catalog.message"
      class="operator-rail__message"
    >
      {{ catalog.message }}
    </p>

    <div
      class="operator-rail__list"
      role="list"
      aria-label="算子列表"
    >
      <button
        v-for="operator in visibleOperators"
        :key="operator.operatorType"
        type="button"
        role="listitem"
        class="operator-item operator-rail__item"
        :data-type="operator.operatorType"
        :data-name="operator.displayName"
        :data-operator="dragPayload(operator)"
        :draggable="!readonly"
        :disabled="readonly"
        @click="emit('add', operator)"
        @dragstart="startDrag($event, operator)"
      >
        <span class="operator-rail__item-main">
          <strong>{{ operator.displayName }}</strong>
          <small>{{ operatorCategoryLabels[operator.categoryId] }}</small>
        </span>
        <span class="operator-rail__item-description">{{ operator.description || operator.operatorType }}</span>
        <span class="operator-rail__item-meta">
          <code>{{ operator.operatorType }}</code>
          <em :data-lifecycle="operator.lifecycle">{{ operatorLifecycleLabels[operator.lifecycle] }}</em>
        </span>
      </button>
    </div>

    <p
      v-if="catalog.operators.length > 0 && visibleOperators.length === 0"
      class="operator-rail__message"
    >
      没有匹配的算子。
    </p>
  </aside>
</template>

<style scoped>
.operator-rail { min-width: 0; min-height: 0; display: grid; grid-template-rows: auto auto auto auto minmax(0, 1fr); gap: var(--cv-space-2); padding: var(--cv-space-3); overflow: hidden; border-right: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.operator-rail__header { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
.operator-rail__header strong, .operator-rail__header small { display: block; }
.operator-rail__header strong { font-size: var(--cv-font-size-sm); }
.operator-rail__header small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.operator-rail__refresh { width: 28px; height: 28px; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-secondary); cursor: pointer; }
.operator-rail__search { display: grid; gap: 4px; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.operator-rail__search input { width: 100%; min-width: 0; height: 32px; padding: 0 var(--cv-space-2); border: 1px solid var(--cv-border-default); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); }
.operator-rail__compatibility { display: flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.operator-rail__categories { display: flex; gap: 4px; padding-bottom: 4px; overflow-x: auto; scrollbar-gutter: stable; }
.operator-rail__categories button { flex: 0 0 auto; min-width: 54px; padding: 5px 7px; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-1); border: 1px solid var(--cv-border-subtle); border-radius: 999px; background: var(--cv-surface-page); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); cursor: pointer; }
.operator-rail__categories button.is-active { border-color: var(--cv-color-brand-border); background: var(--cv-color-brand-soft); color: var(--cv-color-brand-text); }
.operator-rail__categories small { font-size: 9px; }
.operator-rail__list { min-height: 0; display: grid; align-content: start; gap: 6px; overflow-y: auto; overflow-x: hidden; scrollbar-gutter: stable; }
.operator-rail__item { width: 100%; min-width: 0; padding: 8px; display: grid; gap: 5px; text-align: left; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); cursor: grab; }
.operator-rail__item:hover { border-color: var(--cv-border-strong); background: var(--cv-surface-overlay); }
.operator-rail__item:disabled { opacity: 0.58; cursor: not-allowed; }
.operator-rail__item-main, .operator-rail__item-meta { min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
.operator-rail__item-main strong { overflow: hidden; font-size: var(--cv-font-size-xs); text-overflow: ellipsis; white-space: nowrap; }
.operator-rail__item-main small { max-width: 46%; overflow: hidden; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.operator-rail__item-description { overflow: hidden; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: 1.35; text-overflow: ellipsis; white-space: nowrap; }
.operator-rail__item-meta code { overflow: hidden; color: var(--cv-text-muted); font-size: 9px; text-overflow: ellipsis; }
.operator-rail__item-meta em { padding: 1px 5px; border-radius: 999px; background: var(--cv-surface-sunken); color: var(--cv-text-muted); font-size: 9px; font-style: normal; }
.operator-rail__item-meta em[data-lifecycle="Experimental"], .operator-rail__item-meta em[data-lifecycle="Reference"] { background: var(--cv-color-status-info-soft); color: var(--cv-color-status-info-strong); }
.operator-rail__item-meta em[data-lifecycle="Legacy"], .operator-rail__item-meta em[data-lifecycle="Deprecated"] { background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong); }
.operator-rail__message { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: 1.4; }
</style>
