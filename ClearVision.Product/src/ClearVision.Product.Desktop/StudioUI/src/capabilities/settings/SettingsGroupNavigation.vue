<script setup lang="ts">
import { computed } from 'vue';
import { CvIcon } from '@/design-system/icons';
import {
  isGenericSettingsSection,
  SETTINGS_NAVIGATION_ITEMS,
  type SettingsNavigationItem,
  type SettingsNavigationTarget
} from './settingsViewModel';
import type { SettingsProjectionV1 } from './decoder';

const props = defineProps<{
  active: SettingsNavigationTarget;
  role: string | null;
  projection: SettingsProjectionV1;
}>();

const emit = defineEmits<{
  select: [target: SettingsNavigationTarget];
}>();

const groups = computed(() => [
  { id: 'overview', label: '', items: itemsForGroup('overview') },
  { id: 'basic', label: '基础设置', items: itemsForGroup('basic') },
  { id: 'device', label: '设备与通信', items: itemsForGroup('device') },
  { id: 'system', label: '系统服务', items: itemsForGroup('system') }
].filter(group => group.items.length > 0));

const accessSummary = computed(() => {
  if (props.role === 'Admin') return '可查看并管理全部分组';
  if (props.role === 'Engineer') return '可查看安全范围，部分分组只读';
  return '只读';
});

function itemsForGroup(group: SettingsNavigationItem['group']): readonly SettingsNavigationItem[] {
  return SETTINGS_NAVIGATION_ITEMS.filter(item => item.group === group);
}

function stateLabel(target: SettingsNavigationTarget): string | null {
  if (target === 'overview') return null;
  if (isGenericSettingsSection(target)) {
    if (!props.projection.sections[target]) return '不可用';
    return props.role === 'Admin' ? null : '只读';
  }
  if (target === 'station' || target === 'database') return props.role === 'Admin' ? null : '仅管理员';
  if (target === 'ai-model') return props.role === 'Admin' ? null : '只读';
  if (target === 'plc' || target === 'tcp') return props.role === 'Admin' ? null : '只读';
  return null;
}
</script>

<template>
  <nav
    class="settings-group-navigation"
    aria-label="设置分组导航"
    data-settings-navigation
  >
    <div class="settings-group-navigation__heading">
      <strong>设置分组</strong>
      <span>{{ accessSummary }}</span>
    </div>
    <div class="settings-group-navigation__groups">
      <section
        v-for="group in groups"
        :key="group.id"
        class="settings-group-navigation__group"
      >
        <span
          v-if="group.label"
          class="settings-group-navigation__group-label"
        >{{ group.label }}</span>
        <div class="settings-group-navigation__items">
          <button
            v-for="item in group.items"
            :key="item.id"
            class="settings-group-navigation__item"
            :class="{ 'is-active': active === item.id }"
            type="button"
            :title="item.description"
            :aria-current="active === item.id ? 'page' : undefined"
            :data-settings-group="item.id"
            @click="emit('select', item.id)"
          >
            <CvIcon
              :name="item.icon"
              size="sm"
            />
            <strong>{{ item.label }}</strong>
            <span
              v-if="stateLabel(item.id)"
              class="settings-group-navigation__item-state"
            >{{ stateLabel(item.id) }}</span>
          </button>
        </div>
      </section>
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
  gap: 2px;
  padding: 0 var(--cv-space-2) var(--cv-space-4);
}

.settings-group-navigation__heading strong {
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-semibold);
}

.settings-group-navigation__heading span {
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
  line-height: var(--cv-line-height-normal);
}

.settings-group-navigation__groups,
.settings-group-navigation__group,
.settings-group-navigation__items {
  display: grid;
  min-width: 0;
}

.settings-group-navigation__groups { gap: var(--cv-space-4); }
.settings-group-navigation__group { gap: var(--cv-space-1); }
.settings-group-navigation__items {
  gap: 2px;
}

.settings-group-navigation__group-label {
  padding-inline: var(--cv-space-2);
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
  font-weight: var(--cv-font-weight-medium);
}

.settings-group-navigation__item {
  display: grid;
  min-width: 0;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--cv-space-2);
  width: 100%;
  min-height: var(--cv-density-control-height-sm);
  padding: var(--cv-space-2) var(--cv-space-3);
  border: 1px solid transparent;
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
  border-color: var(--cv-color-action-border);
  background: var(--cv-color-action-soft);
  color: var(--cv-color-action-text);
}

.settings-group-navigation__item > strong {
  overflow: hidden;
  color: inherit;
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-medium);
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

  .settings-group-navigation__groups {
    display: flex;
    gap: var(--cv-space-1);
    overflow-x: auto;
    padding-bottom: var(--cv-space-1);
    scrollbar-width: thin;
  }

  .settings-group-navigation__group { display: contents; }
  .settings-group-navigation__group-label { display: none; }
  .settings-group-navigation__items { display: flex; gap: var(--cv-space-1); }

  .settings-group-navigation__item {
    flex: 0 0 auto;
    width: auto;
    min-width: 126px;
  }
}
</style>
