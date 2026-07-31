import type { GenericSettingsSection, SettingsSection } from './contracts';
import type { SettingsProjectionV1 } from './decoder';

export type SettingsNavigationTarget = 'overview' | SettingsSection;

export interface SettingsNavigationItem {
  readonly id: SettingsNavigationTarget;
  readonly label: string;
  readonly description: string;
}

export const SETTINGS_NAVIGATION_ITEMS: readonly SettingsNavigationItem[] = Object.freeze([
  { id: 'overview', label: '总览', description: '服务端投影与读取范围' },
  { id: 'general', label: '常规', description: '产品标题与主题' },
  { id: 'storage', label: '存储', description: '图像保存与保留策略' },
  { id: 'runtime', label: '运行保护', description: '自动运行与保护规则' },
  { id: 'security', label: '安全策略', description: '密码与会话策略' },
  { id: 'plc', label: 'PLC', description: '协议与连接诊断' },
  { id: 'tcp', label: 'TCP', description: '配置文件与运行状态' },
  { id: 'camera', label: '相机系统', description: '系统绑定与触发诊断' },
  { id: 'station', label: '工作站通信', description: 'Studio 与 Station 同步' },
  { id: 'ai-model', label: 'AI 模型', description: '模型配置与测试' },
  { id: 'database', label: '数据库维护', description: '状态与备份操作' }
]);

const sectionLabels: Readonly<Record<SettingsSection, string>> = Object.freeze(
  Object.fromEntries(
    SETTINGS_NAVIGATION_ITEMS
      .filter((item): item is SettingsNavigationItem & { readonly id: SettingsSection } => item.id !== 'overview')
      .map(item => [item.id, item.label])
  ) as Record<SettingsSection, string>
);

const ignoredAuthorityLabels: Readonly<Record<string, string>> = Object.freeze({
  communication: 'PLC 配置 authority',
  tcpCommunication: 'TCP 配置 authority',
  features: '产品能力开关 authority',
  cameras: '相机系统 authority',
  activeCameraId: '活动相机 authority'
});

export function settingsNavigationItem(target: SettingsNavigationTarget): SettingsNavigationItem {
  return SETTINGS_NAVIGATION_ITEMS.find(item => item.id === target) ?? SETTINGS_NAVIGATION_ITEMS[0]!;
}

export function settingsSectionLabel(section: SettingsSection): string {
  return sectionLabels[section];
}

export function settingsAuthorityLabel(key: string): string {
  return ignoredAuthorityLabels[key] ?? key;
}

export function isGenericSettingsSection(value: SettingsNavigationTarget): value is GenericSettingsSection {
  return value === 'general' || value === 'storage' || value === 'runtime' || value === 'security';
}

export type SettingsSectionReadState = 'available' | 'restricted' | 'shell-only';

export function settingsSectionReadState(
  target: SettingsNavigationTarget,
  projection: SettingsProjectionV1
): SettingsSectionReadState {
  if (target === 'overview') return 'available';
  if (!isGenericSettingsSection(target)) return 'shell-only';
  return projection.sections[target] ? 'available' : 'restricted';
}

export function settingsSectionStateLabel(state: SettingsSectionReadState): string {
  switch (state) {
    case 'available': return '已读取';
    case 'restricted': return '安全子集未返回';
    case 'shell-only': return '后续接入';
  }
}
