<script setup lang="ts">
import { CvIcon } from '@/design-system/icons';
import {
  isGenericSettingsSection,
  SETTINGS_NAVIGATION_ITEMS,
  type SettingsNavigationTarget
} from './settingsViewModel';
import type { SettingsProjectionV1 } from './decoder';

defineProps<{
  active: SettingsNavigationTarget;
  role: string | null;
  projection: SettingsProjectionV1;
}>();

const emit = defineEmits<{
  select: [target: SettingsNavigationTarget];
}>();

function stateLabel(target: SettingsNavigationTarget, role: string | null, projection: SettingsProjectionV1): string {
  if (target === 'overview') return '服务端投影';
  if (isGenericSettingsSection(target)) {
    const accessLabel = role === 'Admin' ? 'Admin 可编辑' : role === 'Engineer' ? 'Engineer 只读' : '只读';
    return projection.sections[target] ? accessLabel : `${accessLabel}（安全子集未返回）`;
  }
  if (target === 'plc' || target === 'tcp' || target === 'camera') return '已接入';
  return '后续';
}
</script>

<template>
  <nav
    class="settings-group-navigation"
    aria-label="Settings 分组导航"
    data-settings-navigation
  >
    <div class="settings-group-navigation__heading">
      <span class="settings-group-navigation__eyebrow">配置分组</span>
      <strong>系统配置工作台</strong>
    </div>
    <div class="settings-group-navigation__items">
      <button
        v-for="item in SETTINGS_NAVIGATION_ITEMS"
        :key="item.id"
        class="settings-group-navigation__item"
        :class="{ 'is-active': active === item.id }"
        type="button"
        :aria-current="active === item.id ? 'page' : undefined"
        :data-settings-group="item.id"
        @click="emit('select', item.id)"
      >
        <CvIcon
          :name="item.id === 'overview' ? 'overview' : 'sliders'"
          size="sm"
        />
        <span class="settings-group-navigation__item-copy">
          <strong>{{ item.label }}</strong>
          <small>{{ item.description }}</small>
        </span>
        <span class="settings-group-navigation__item-state">{{ stateLabel(item.id, role, projection) }}</span>
      </button>
    </div>
  </nav>
</template>

<style scoped>
.settings-group-navigation {
  min-width: 0;
  padding-right: var(--cv-space-3);
  border-right: 1px solid var(--cv-border-subtle);
}

.settings-group-navigation__heading {
  display: grid;
  gap: var(--cv-space-1);
  padding: 0 var(--cv-space-2) var(--cv-space-3);
}

.settings-group-navigation__eyebrow {
  color: var(--cv-color-brand-text);
  font-size: var(--cv-font-size-2xs);
  font-weight: var(--cv-font-weight-medium);
  letter-spacing: var(--cv-letter-spacing-caption);
}

.settings-group-navigation__heading strong {
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-semibold);
}

.settings-group-navigation__items {
  display: grid;
  gap: 2px;
}

.settings-group-navigation__item {
  display: grid;
  min-width: 0;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--cv-space-2);
  width: 100%;
  padding: var(--cv-space-2);
  border: 0;
  border-left: 2px solid transparent;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-secondary);
  text-align: left;
  cursor: pointer;
}

.settings-group-navigation__item:hover {
  background: var(--cv-interactive-hover);
  color: var(--cv-text-primary);
}

.settings-group-navigation__item.is-active {
  border-left-color: var(--cv-color-brand-500);
  background: var(--cv-color-brand-soft);
  color: var(--cv-color-brand-text);
}

.settings-group-navigation__item-copy {
  display: grid;
  min-width: 0;
  gap: 2px;
}

.settings-group-navigation__item-copy strong {
  overflow: hidden;
  color: inherit;
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-medium);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.settings-group-navigation__item-copy small {
  overflow: hidden;
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.settings-group-navigation__item-state {
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
  white-space: nowrap;
}

@media (max-width: 900px) {
  .settings-group-navigation {
    padding-right: 0;
    padding-bottom: var(--cv-space-2);
    border-right: 0;
    border-bottom: 1px solid var(--cv-border-subtle);
  }

  .settings-group-navigation__heading { padding-inline: 0; }

  .settings-group-navigation__items {
    display: flex;
    gap: var(--cv-space-1);
    overflow-x: auto;
    padding-bottom: var(--cv-space-1);
    scrollbar-width: thin;
  }

  .settings-group-navigation__item {
    flex: 0 0 auto;
    width: auto;
    min-width: 148px;
    border-left: 0;
    border-bottom: 2px solid transparent;
  }

  .settings-group-navigation__item.is-active { border-bottom-color: var(--cv-color-brand-500); }
}
</style>
