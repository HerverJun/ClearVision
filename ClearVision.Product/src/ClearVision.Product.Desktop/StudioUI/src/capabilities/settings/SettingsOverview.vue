<script setup lang="ts">
import { computed } from 'vue';
import {
  CvDescriptionList,
  CvInlineAlert,
  CvPanel,
  CvStatusBadge,
  type CvDescriptionItem,
  type CvStatusTone
} from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { GenericSettingsSection } from './contracts';
import type { SettingsProjectionV1 } from './decoder';
import type { SettingsOwner } from './settingsOwner';
import type { AuthLifecycleOwner } from '@/app/auth';
import {
  isGenericSettingsSection,
  settingsNavigationItem,
  settingsSectionReadState,
  settingsSectionStateLabel,
  SETTINGS_NAVIGATION_ITEMS,
  type SettingsNavigationTarget,
  type SettingsSectionReadState
} from './settingsViewModel';
import SettingsDatabasePanel from './SettingsDatabasePanel.vue';
import SettingsGeneralPanel from './SettingsGeneralPanel.vue';
import SettingsRuntimePanel from './SettingsRuntimePanel.vue';
import SettingsSecurityPanel from './SettingsSecurityPanel.vue';
import SettingsStoragePanel from './SettingsStoragePanel.vue';
import SettingsPlcPanel from './SettingsPlcPanel.vue';
import SettingsTcpPanel from './SettingsTcpPanel.vue';
import SettingsCameraPanel from './SettingsCameraPanel.vue';
import SettingsStationPanel from './SettingsStationPanel.vue';
import SettingsAiModelPanel from './SettingsAiModelPanel.vue';

const props = defineProps<{
  projection: SettingsProjectionV1;
  activeGroup: SettingsNavigationTarget;
  owner: SettingsOwner;
  role: string | null;
  auth: AuthLifecycleOwner | null;
}>();

const emit = defineEmits<{
  select: [target: SettingsNavigationTarget];
}>();

const activeItem = computed(() => settingsNavigationItem(props.activeGroup));
const activeGenericSection = computed<GenericSettingsSection | null>(() =>
  props.activeGroup !== 'overview' && isGenericSettingsSection(props.activeGroup) ? props.activeGroup : null
);
const activeGenericProjection = computed(() => activeGenericSection.value
  ? props.projection.sections[activeGenericSection.value]
  : null);

const projectionSummary = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'softwareTitle', label: '软件标题', value: props.projection.sections.general.softwareTitle },
  { key: 'productTheme', label: '默认主题', value: props.projection.sections.general.theme === 'dark' ? '深色' : '浅色' },
  { key: 'revision', label: '配置版本', value: props.projection.revision },
  { key: 'scope', label: '可用范围', value: props.projection.safeSubset ? '工程师安全范围' : '管理员完整范围' }
]);

const genericSectionRows = computed(() => SETTINGS_NAVIGATION_ITEMS
  .filter(item => item.id !== 'overview')
  .map(item => Object.freeze({
    ...item,
    state: settingsSectionReadState(item.id, props.projection)
  })));

function isCameraConnected(status: string): boolean {
  return status.trim().toLowerCase() === 'connected';
}

function isCameraOnline(status: string): boolean {
  return status.trim().toLowerCase() === 'online';
}

function stateTone(state: SettingsSectionReadState): CvStatusTone {
  switch (state) {
    case 'available': return 'ok';
    case 'restricted': return 'warning';
    case 'on-demand': return 'idle';
  }
}

function sectionStateLabel(
  target: SettingsNavigationTarget,
  state: SettingsSectionReadState
): string {
  if (isGenericSettingsSection(target)) {
    const accessLabel = props.role === 'Admin' ? '可编辑' : '只读';
    return state === 'restricted' ? '当前账户不可用' : accessLabel;
  }
  if (target === 'database') return props.role === 'Admin' ? '进入后读取' : '仅管理员';
  if (target === 'station') {
    if (props.role !== 'Admin') return '仅管理员';
    return props.owner.projection.station ? '已读取' : '进入后读取';
  }
  if (target === 'ai-model') {
    if (!props.owner.projection.aiModels) return props.role === 'Engineer' ? '只读' : '进入后读取';
    return props.owner.projection.aiModels.safeSubset ? '只读' : '可管理';
  }
  const device = props.owner.projection.device;
  if (target === 'plc') return device.plcSettings ? `已读取 · ${device.plcSettings.activeProtocol}` : '进入后读取';
  if (target === 'tcp') {
    const statuses = Object.values(device.tcpStatuses);
    if (statuses.some(status => status?.isConnected || status?.isListening)) return '运行中';
    return device.tcpProfiles.length > 0 ? '已配置，未运行' : '进入后读取';
  }
  if (target === 'camera') {
    const bindings = device.cameraBindings;
    if (bindings.some(binding => isCameraConnected(binding.connectionStatus))) return '已连接';
    if (bindings.some(binding => isCameraOnline(binding.connectionStatus))) return '在线（已发现）';
    return bindings.length > 0 ? '已读取，未连接' : '进入后读取';
  }
  return settingsSectionStateLabel(state);
}

function sectionTone(target: SettingsNavigationTarget, state: SettingsSectionReadState): CvStatusTone {
  const device = props.owner.projection.device;
  if (target === 'database') return props.role === 'Admin' ? 'idle' : 'warning';
  if (target === 'station') return props.owner.projection.station ? 'ok' : 'idle';
  if (target === 'ai-model') return props.owner.projection.aiModels ? 'ok' : 'idle';
  if (target === 'plc') return device.plcSettings ? 'ok' : 'idle';
  if (target === 'tcp') {
    return Object.values(device.tcpStatuses).some(status => status?.isConnected || status?.isListening)
      ? 'ok'
      : device.tcpProfiles.length > 0 ? 'warning' : 'idle';
  }
  if (target === 'camera') {
    if (device.cameraBindings.some(binding => isCameraConnected(binding.connectionStatus))) return 'ok';
    if (device.cameraBindings.some(binding => isCameraOnline(binding.connectionStatus))) return 'info';
    return device.cameraBindings.length > 0 ? 'warning' : 'idle';
  }
  return stateTone(state);
}

function isGenericSection(target: SettingsNavigationTarget): target is GenericSettingsSection {
  return target !== 'overview' && isGenericSettingsSection(target);
}
</script>

<template>
  <div
    class="settings-overview"
    data-settings-overview
    :data-settings-safe-subset="projection.safeSubset"
  >
    <template v-if="activeGroup === 'overview'">
      <CvPanel
        title="当前配置"
        description="基础设置已加载。设备和系统服务会在进入对应分组时读取。"
        variant="section"
      >
        <CvDescriptionList
          :items="projectionSummary"
          :columns="2"
          label="当前设置摘要"
        />
      </CvPanel>

      <CvPanel
        title="设置分组"
        description="选择分组查看当前配置、可用操作和保存状态。"
        variant="section"
      >
        <ul class="settings-overview__section-list">
          <li
            v-for="item in genericSectionRows"
            :key="item.id"
            class="settings-overview__section-row"
          >
            <button
              type="button"
              :data-settings-overview-group="item.id"
              @click="emit('select', item.id)"
            >
              <CvIcon
                :name="item.icon"
                size="sm"
              />
              <span class="settings-overview__section-copy">
                <strong>{{ item.label }}</strong>
                <span>{{ item.description }}</span>
              </span>
              <CvStatusBadge :tone="sectionTone(item.id, item.state)">
                {{ sectionStateLabel(item.id, item.state) }}
              </CvStatusBadge>
              <CvIcon
                name="chevron-right"
                size="sm"
              />
            </button>
          </li>
        </ul>
      </CvPanel>
    </template>

    <KeepAlive>
      <SettingsGeneralPanel
        v-if="activeGroup === 'general' && projection.sections.general"
        :projection="projection.sections.general"
        :owner="owner"
        :can-write="role === 'Admin'"
      />

      <SettingsStoragePanel
        v-else-if="activeGroup === 'storage' && projection.sections.storage"
        :projection="projection.sections.storage"
        :owner="owner"
        :can-write="role === 'Admin'"
      />

      <SettingsRuntimePanel
        v-else-if="activeGroup === 'runtime' && projection.sections.runtime"
        :projection="projection.sections.runtime"
        :owner="owner"
        :can-write="role === 'Admin'"
      />

      <SettingsSecurityPanel
        v-else-if="activeGroup === 'security' && projection.sections.security"
        :projection="projection.sections.security"
        :owner="owner"
        :role="role"
        :auth="auth"
      />

      <SettingsDatabasePanel
        v-else-if="activeGroup === 'database'"
        :owner="owner"
        :role="role"
      />

      <SettingsPlcPanel
        v-else-if="activeGroup === 'plc'"
        :owner="owner"
        :can-write="role === 'Admin'"
      />

      <SettingsTcpPanel
        v-else-if="activeGroup === 'tcp'"
        :owner="owner"
        :can-write="role === 'Admin'"
      />

      <SettingsCameraPanel
        v-else-if="activeGroup === 'camera'"
        :owner="owner"
      />

      <SettingsStationPanel
        v-else-if="activeGroup === 'station'"
        :owner="owner"
        :role="role"
      />

      <SettingsAiModelPanel
        v-else-if="activeGroup === 'ai-model'"
        :owner="owner"
        :role="role"
      />
    </KeepAlive>

    <template v-if="isGenericSection(activeGroup) && !activeGenericProjection">
      <CvPanel
        :title="activeItem.label"
        description="此分组不在当前账户可查看的设置范围内。"
      >
        <template #actions>
          <CvStatusBadge
            tone="warning"
            label="不可用"
          />
        </template>
        <CvInlineAlert
          tone="warning"
          title="当前响应未包含此分组"
        >
          当前账户不能查看此分组。界面不会显示默认值，也不会提交修改。
        </CvInlineAlert>
      </CvPanel>
    </template>
  </div>
</template>

<style scoped>
.settings-overview {
  display: grid;
  min-width: 0;
  gap: var(--cv-density-page-gap);
}

.settings-overview__section-list {
  display: grid;
  gap: 0;
  margin: 0;
  padding: 0;
  overflow: hidden;
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-md);
  background: var(--cv-surface-raised);
  list-style: none;
}

.settings-overview__section-row {
  border-bottom: 1px solid var(--cv-border-subtle);
}

.settings-overview__section-row:last-child { border-bottom: 0; }

.settings-overview__section-row > button {
  display: grid;
  min-width: 0;
  grid-template-columns: auto minmax(0, 1fr) auto auto;
  align-items: center;
  gap: var(--cv-space-3);
  width: 100%;
  min-height: calc(var(--cv-density-row-height) + var(--cv-space-4));
  padding: var(--cv-space-3) var(--cv-space-4);
  border: 0;
  border-radius: 0;
  background: transparent;
  color: var(--cv-text-secondary);
  text-align: left;
  cursor: pointer;
}

.settings-overview__section-row > button:hover { background: var(--cv-color-action-soft); }
.settings-overview__section-row > button:focus-visible { outline: none; box-shadow: var(--cv-focus-ring); }

.settings-overview__section-copy {
  display: grid;
  min-width: 0;
  gap: var(--cv-space-1);
}

.settings-overview__section-copy strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-overview__section-copy span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }

@media (max-width: 560px) {
  .settings-overview__section-row > button {
    grid-template-columns: auto minmax(0, 1fr) auto;
  }

  .settings-overview__section-row :deep([data-design-primitive="status-badge"]) { display: none; }
}
</style>
