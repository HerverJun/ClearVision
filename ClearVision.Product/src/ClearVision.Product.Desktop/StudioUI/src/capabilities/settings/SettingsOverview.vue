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
import type { GenericSettingsSection, SettingsSection } from './contracts';
import type { SettingsProjectionV1 } from './decoder';
import type { SettingsOwner } from './settingsOwner';
import type { AuthLifecycleOwner } from '@/app/auth';
import {
  isGenericSettingsSection,
  settingsAuthorityLabel,
  settingsNavigationItem,
  settingsSectionLabel,
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

const activeItem = computed(() => settingsNavigationItem(props.activeGroup));
const activeSection = computed<SettingsSection | null>(() =>
  props.activeGroup === 'overview' ? null : props.activeGroup
);
const activeGenericSection = computed<GenericSettingsSection | null>(() =>
  activeSection.value && isGenericSettingsSection(activeSection.value) ? activeSection.value : null
);
const activeGenericProjection = computed(() => activeGenericSection.value
  ? props.projection.sections[activeGenericSection.value]
  : null);

const projectionSummary = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'softwareTitle', label: '软件标题', value: props.projection.sections.general.softwareTitle },
  { key: 'productTheme', label: '产品主题', value: props.projection.sections.general.theme === 'dark' ? '深色' : '浅色' },
  { key: 'revision', label: '服务端观察版本', value: props.projection.revision },
  { key: 'scope', label: '读取范围', value: props.projection.safeSubset ? 'safe subset' : '完整管理员投影' },
  { key: 'authority', label: '保存 authority', value: '后端配置服务' },
  { key: 'revision-policy', label: '并发语义', value: '现有无条件 revision；不新增 conditional revision' }
]);

const genericSectionRows = computed(() => SETTINGS_NAVIGATION_ITEMS
  .filter(item => item.id !== 'overview')
  .map(item => Object.freeze({
    ...item,
    state: settingsSectionReadState(item.id, props.projection)
  })));

const authorityRows = computed(() => genericSectionRows.value
  .filter(item => !isGenericSettingsSection(item.id)));

const isolatedAuthorityRows = computed(() => props.projection.ignoredAuthoritySections
  .map(key => Object.freeze({ key, label: settingsAuthorityLabel(key) })));

const activeProjectionItems = computed<readonly CvDescriptionItem[]>(() => {
  const section = activeGenericSection.value;
  if (!section) return [];

  switch (section) {
    case 'general':
      if (!props.projection.sections.general) return [];
      return [
        { key: 'softwareTitle', label: '软件标题', value: props.projection.sections.general.softwareTitle },
        { key: 'theme', label: '产品主题', value: props.projection.sections.general.theme === 'dark' ? '深色' : '浅色' },
        { key: 'autoStart', label: '自动启动', value: booleanLabel(props.projection.sections.general.autoStart) }
      ];
    case 'storage':
      if (!props.projection.sections.storage) return [];
      return [
        { key: 'imageSavePath', label: '图像保存路径', value: props.projection.sections.storage.imageSavePath },
        { key: 'savePolicy', label: '保存策略', value: props.projection.sections.storage.savePolicy },
        { key: 'retentionDays', label: '保留天数', value: `${props.projection.sections.storage.retentionDays} 天` },
        { key: 'minFreeSpaceGb', label: '最低可用空间', value: `${props.projection.sections.storage.minFreeSpaceGb} GB` }
      ];
    case 'runtime':
      if (!props.projection.sections.runtime) return [];
      return [
        { key: 'autoRun', label: '自动运行', value: booleanLabel(props.projection.sections.runtime.autoRun) },
        { key: 'stopOnConsecutiveNg', label: '连续 NG 停止', value: `${props.projection.sections.runtime.stopOnConsecutiveNg} 次` },
        { key: 'missingMaterialTimeoutSeconds', label: '缺料超时', value: `${props.projection.sections.runtime.missingMaterialTimeoutSeconds} 秒` },
        { key: 'applyProtectionRules', label: '启用保护规则', value: booleanLabel(props.projection.sections.runtime.applyProtectionRules) }
      ];
    case 'security':
      if (!props.projection.sections.security) return [];
      return [
        { key: 'passwordMinLength', label: '密码最小长度', value: `${props.projection.sections.security.passwordMinLength} 位` },
         { key: 'sessionTimeoutMinutes', label: '会话超时（历史只读，不控制当前 session expiry）', value: `${props.projection.sections.security.sessionTimeoutMinutes} 分钟` },
        { key: 'loginFailureLockoutCount', label: '失败锁定次数', value: `${props.projection.sections.security.loginFailureLockoutCount} 次` }
      ];
  }
  return [];
});

function booleanLabel(value: boolean | null): string {
  if (value === null) return '未返回';
  return value ? '已启用' : '未启用';
}

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
    case 'shell-only': return 'idle';
  }
}

function mountedSection(target: SettingsNavigationTarget): boolean {
  return target === 'overview' || target === 'database' || target === 'plc' || target === 'tcp' ||
    target === 'camera' || target === 'station' || target === 'ai-model';
}

function sectionStateLabel(
  target: SettingsNavigationTarget,
  state: SettingsSectionReadState
): string {
  if (isGenericSettingsSection(target)) {
    const accessLabel = props.role === 'Admin' ? 'Admin 可编辑' : props.role === 'Engineer' ? 'Engineer 只读' : '只读';
    return state === 'restricted' ? `${accessLabel}（安全子集未返回）` : accessLabel;
  }
  if (target === 'database') return '已接入';
  if (target === 'station') {
    if (props.role !== 'Admin') return 'Admin only';
    return props.owner.projection.station ? '已接入' : '读取中';
  }
  if (target === 'ai-model') {
    if (!props.owner.projection.aiModels) return props.role === 'Engineer' ? 'safe read' : '读取中';
    return props.owner.projection.aiModels.safeSubset ? 'Engineer safe read' : 'Admin 可管理';
  }
  const device = props.owner.projection.device;
  if (target === 'plc') return device.plcSettings ? `已接入（${device.plcSettings.activeProtocol}）` : '未读取';
  if (target === 'tcp') {
    const statuses = Object.values(device.tcpStatuses);
    if (statuses.some(status => status?.isConnected || status?.isListening)) return '运行中';
    return device.tcpProfiles.length > 0 ? '已配置，未运行' : '未读取';
  }
  if (target === 'camera') {
    const bindings = device.cameraBindings;
    if (bindings.some(binding => isCameraConnected(binding.connectionStatus))) return '已连接';
    if (bindings.some(binding => isCameraOnline(binding.connectionStatus))) return '在线（已发现）';
    return bindings.length > 0 ? '已读取，未连接' : '未读取';
  }
  return settingsSectionStateLabel(state);
}

function sectionTone(target: SettingsNavigationTarget, state: SettingsSectionReadState): CvStatusTone {
  const device = props.owner.projection.device;
  if (target === 'database') return 'ok';
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
        title="服务端投影"
        description="只显示本次从现有 Settings endpoint 读取并通过 decoder 校验的字段。"
      >
        <CvDescriptionList
          :items="projectionSummary"
          :columns="2"
          label="Settings 服务端投影摘要"
        />
      </CvPanel>

      <CvPanel
        title="分组读取状态"
        description="设备、工作站、AI 与数据库分组使用各自服务端状态；此处只显示已读取的权威投影。"
      >
        <ul class="settings-overview__section-list">
          <li
            v-for="item in genericSectionRows"
            :key="item.id"
            class="settings-overview__section-row"
          >
            <div class="settings-overview__section-copy">
              <strong>{{ item.label }}</strong>
              <span>{{ item.description }}</span>
            </div>
            <CvStatusBadge :tone="sectionTone(item.id, item.state)">
              {{ sectionStateLabel(item.id, item.state) }}
            </CvStatusBadge>
          </li>
        </ul>
        <div class="settings-overview__authority-list">
          <span class="settings-overview__authority-label">独立 authority</span>
          <span
            v-for="item in authorityRows"
            :key="item.id"
            class="settings-overview__authority-chip"
          >
            {{ item.label }}
          </span>
        </div>
        <CvInlineAlert
          v-if="isolatedAuthorityRows.length"
          class="settings-overview__notice"
          tone="info"
          title="后端 authority 已隔离"
        >
          {{ isolatedAuthorityRows.map(item => item.label).join('、') }} 未进入 generic Settings 投影。
        </CvInlineAlert>
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
        :description="activeItem.description"
      >
        <template #actions>
          <CvStatusBadge
            v-if="activeGenericProjection"
            tone="ok"
            label="只读投影"
          />
          <CvStatusBadge
            v-else
            tone="warning"
            label="安全子集"
          />
        </template>
        <CvInlineAlert
          v-if="!activeGenericProjection"
          tone="warning"
          title="当前响应未包含此分组"
        >
          当前账户只收到 safe subset。后端未返回该分组时，界面不会用本地默认值补齐，也不会发起保存请求。
        </CvInlineAlert>
        <CvDescriptionList
          v-else
          :items="activeProjectionItems"
          :columns="2"
          :label="`${settingsSectionLabel(activeGroup)}只读投影`"
        />
      </CvPanel>
    </template>

    <CvPanel
      v-if="!isGenericSection(activeGroup) && !mountedSection(activeGroup)"
      :title="activeItem.label"
      :description="activeItem.description"
    >
      <template #actions>
        <CvStatusBadge
          tone="idle"
          label="待接入"
        />
      </template>
      <div class="settings-overview__deferred">
        <strong>{{ activeItem.label }}保持独立 authority</strong>
        <p>
          该分组仍保持独立 authority，待对应 endpoint contract 接入后再挂载工作台。
        </p>
        <p>该分组当前没有可用的编辑或运行操作。</p>
      </div>
    </CvPanel>
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
  list-style: none;
}

.settings-overview__section-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--cv-space-3);
  padding: var(--cv-space-3) 0;
  border-bottom: 1px solid var(--cv-border-subtle);
}

.settings-overview__section-row:last-child { border-bottom: 0; }

.settings-overview__section-copy {
  display: grid;
  min-width: 0;
  gap: var(--cv-space-1);
}

.settings-overview__section-copy strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-overview__section-copy span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }

.settings-overview__authority-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--cv-space-1) var(--cv-space-2);
  padding-top: var(--cv-space-3);
  border-top: 1px solid var(--cv-border-subtle);
}

.settings-overview__authority-label { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }

.settings-overview__authority-chip {
  padding: 2px var(--cv-space-2);
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-sm);
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
}

.settings-overview__notice { margin-top: var(--cv-space-3); }

.settings-overview__deferred {
  display: grid;
  gap: var(--cv-space-2);
  color: var(--cv-text-secondary);
}

.settings-overview__deferred strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-overview__deferred p { margin: 0; font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-normal); }

@media (max-width: 560px) {
  .settings-overview__section-row { align-items: start; grid-template-columns: minmax(0, 1fr); }
}
</style>
