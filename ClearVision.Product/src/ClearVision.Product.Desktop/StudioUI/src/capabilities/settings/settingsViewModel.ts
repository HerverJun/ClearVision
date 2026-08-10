import type {
  GenericSettingsSection,
  SettingsOperationKind,
  SettingsSection,
  SettingsWriteResult
} from './contracts';
import type { CvIconName } from '@/design-system/icons';
import type { SettingsProjectionV1 } from './decoder';
import { projectSettingsOperationFailure, type SettingsOwner } from './settingsOwner';

export type SettingsNavigationTarget = 'overview' | SettingsSection;

export interface SettingsNavigationItem {
  readonly id: SettingsNavigationTarget;
  readonly label: string;
  readonly description: string;
  readonly group: 'overview' | 'basic' | 'device' | 'system';
  readonly icon: CvIconName;
}

export const SETTINGS_NAVIGATION_ITEMS: readonly SettingsNavigationItem[] = Object.freeze([
  { id: 'overview', label: '总览', description: '配置状态与可用范围', group: 'overview', icon: 'overview' },
  { id: 'general', label: '常规', description: '软件标题与默认主题', group: 'basic', icon: 'theme' },
  { id: 'storage', label: '存储', description: '图像保存与保留规则', group: 'basic', icon: 'server' },
  { id: 'runtime', label: '运行保护', description: '自动运行与停止条件', group: 'basic', icon: 'power' },
  { id: 'security', label: '安全与用户', description: '密码策略、本人密码与用户', group: 'basic', icon: 'lock' },
  { id: 'plc', label: 'PLC', description: '协议、映射与连接测试', group: 'device', icon: 'sliders' },
  { id: 'tcp', label: 'TCP', description: '连接配置与收发调试', group: 'device', icon: 'link' },
  { id: 'camera', label: '相机', description: '发现、绑定、触发与预览', group: 'device', icon: 'camera' },
  { id: 'station', label: '工作站通信', description: 'Studio 与工作站同步', group: 'device', icon: 'stations' },
  { id: 'ai-model', label: 'AI 模型', description: '模型配置与连接测试', group: 'system', icon: 'spark' },
  { id: 'database', label: '数据库维护', description: '健康状态与备份', group: 'system', icon: 'diagnostics' }
]);

const sectionLabels: Readonly<Record<SettingsSection, string>> = Object.freeze(
  Object.fromEntries(
    SETTINGS_NAVIGATION_ITEMS
      .filter((item): item is SettingsNavigationItem & { readonly id: SettingsSection } => item.id !== 'overview')
      .map(item => [item.id, item.label])
  ) as Record<SettingsSection, string>
);

const ignoredAuthorityLabels: Readonly<Record<string, string>> = Object.freeze({
  communication: 'PLC 配置服务',
  tcpCommunication: 'TCP 配置服务',
  features: '产品能力开关服务',
  cameras: '相机系统服务',
  activeCameraId: '活动相机服务'
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

export type SettingsSectionReadState = 'available' | 'restricted' | 'on-demand';

export function settingsSectionReadState(
  target: SettingsNavigationTarget,
  projection: SettingsProjectionV1
): SettingsSectionReadState {
  if (target === 'overview') return 'available';
  if (!isGenericSettingsSection(target)) return 'on-demand';
  return projection.sections[target] ? 'available' : 'restricted';
}

export function settingsSectionStateLabel(state: SettingsSectionReadState): string {
  switch (state) {
    case 'available': return '已读取';
    case 'restricted': return '当前账户不可用';
    case 'on-demand': return '进入后读取';
  }
}

export type SettingsFeedbackKind = 'saved' | 'completed' | 'error' | 'unknown' | 'cancelled' | 'forbidden';

export interface SettingsFeedback {
  readonly kind: SettingsFeedbackKind;
  readonly message: string;
  readonly savedLabel: string;
  readonly effectiveLabel: string;
  readonly restartLabel: string;
}

export function settingsFeedbackForResult<T>(result: SettingsWriteResult<T>): SettingsFeedback {
  if (result.status === 'completed') {
    const operationKind: SettingsOperationKind = result.operationKind ?? 'write';
    if (operationKind === 'read') {
      return Object.freeze({
        kind: 'completed',
        message: '已重新读取当前状态。',
        savedLabel: '不适用（读取）',
        effectiveLabel: '当前状态已更新',
        restartLabel: '不适用'
      });
    }
    if (operationKind === 'runtime-operation') {
      return Object.freeze({
        kind: 'completed',
        message: '运行操作已完成，当前状态已更新。',
        savedLabel: '不适用（运行操作）',
        effectiveLabel: '运行状态已返回',
        restartLabel: '不适用'
      });
    }
    if (operationKind === 'account-operation') {
      return Object.freeze({
        kind: 'completed',
        message: '账户操作已完成，用户或会话状态已更新。',
        savedLabel: '不适用（账户操作）',
        effectiveLabel: '服务端状态为准',
        restartLabel: '不适用'
      });
    }
    if (operationKind === 'database-operation') {
      return Object.freeze({
        kind: 'completed',
        message: '数据库操作已完成。',
        savedLabel: '不适用（数据库操作）',
        effectiveLabel: '服务端状态为准',
        restartLabel: '不适用'
      });
    }
    return Object.freeze({
      kind: 'saved',
      message: '设置已保存，当前页面已更新。',
      savedLabel: '已保存',
      effectiveLabel: '状态已更新',
      restartLabel: '未说明'
    });
  }

  if (result.status === 'forbidden') {
    return Object.freeze({
      kind: 'forbidden',
      message: '当前角色没有执行该设置操作的权限。',
      savedLabel: '未保存',
      effectiveLabel: '未生效',
      restartLabel: '不适用'
    });
  }

  if (result.status === 'cancelled' || result.status === 'disposed') {
    if (result.operationKind && result.operationKind !== 'read') {
      return Object.freeze({
        kind: 'unknown',
        message: '修改操作被中断，结果未知；请先重新读取服务端状态再重试。',
        savedLabel: '结果未知',
        effectiveLabel: '结果未知',
        restartLabel: '重新读取后判断'
      });
    }
    return Object.freeze({
      kind: 'cancelled',
      message: '操作已取消；未根据本地结果推断服务端状态。',
      savedLabel: '未确认',
      effectiveLabel: '未确认',
      restartLabel: '未确认'
    });
  }

  if (result.status === 'stale') {
    return Object.freeze({
      kind: 'unknown',
      message: '操作结果已过期；请重新读取服务端状态后再决定是否重试。',
      savedLabel: '结果未知',
      effectiveLabel: '结果未知',
      restartLabel: '重新读取后判断'
    });
  }

  if (result.status !== 'failed') {
    return Object.freeze({
      kind: 'cancelled',
      message: '操作已取消；未根据本地结果推断服务端状态。',
      savedLabel: '未确认',
      effectiveLabel: '未确认',
      restartLabel: '未确认'
    });
  }

  const error = projectSettingsOperationFailure(result.error, result.operationKind);
  return Object.freeze({
    kind: error.code === 'unknown-outcome' ? 'unknown' : 'error',
    message: error.publicMessage,
    savedLabel: error.code === 'unknown-outcome' ? '结果未知' : '未保存',
    effectiveLabel: error.code === 'unknown-outcome' ? '结果未知' : '未生效',
    restartLabel: error.code === 'unknown-outcome' ? '重新读取后判断' : '不适用'
  });
}

export function settingsRoleCanWrite(role: string | null | undefined): boolean {
  return role === 'Admin';
}

export function settingsRoleCanUseOwnerEndpoint(role: string | null | undefined): boolean {
  return role === 'Admin' || role === 'Engineer';
}

export function settingsOwnerForPanel(owner: SettingsOwner | null): SettingsOwner {
  if (!owner) throw new Error('设置面板缺少已挂载的生命周期管理器。');
  return owner;
}

export function formatSettingsBytes(value: number): string {
  if (!Number.isFinite(value) || value < 0) return '未提供';
  if (value < 1024) return `${value} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let amount = value;
  let unit = 'B';
  for (const nextUnit of units) {
    amount /= 1024;
    unit = nextUnit;
    if (amount < 1024) break;
  }
  return `${amount.toFixed(2)} ${unit}`;
}
