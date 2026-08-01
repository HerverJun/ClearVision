import type {
  GenericSettingsSection,
  SettingsOperationKind,
  SettingsSection,
  SettingsWriteResult
} from './contracts';
import type { SettingsProjectionV1 } from './decoder';
import { projectSettingsOperationFailure, type SettingsOwner } from './settingsOwner';

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
        message: '已重新读取服务端投影。',
        savedLabel: '不适用（读取）',
        effectiveLabel: '服务端投影已读取',
        restartLabel: '不适用'
      });
    }
    if (operationKind === 'runtime-operation') {
      return Object.freeze({
        kind: 'completed',
        message: '运行操作已完成；运行时响应是当前权威结果。',
        savedLabel: '不适用（运行操作）',
        effectiveLabel: '运行状态已返回',
        restartLabel: '不适用'
      });
    }
    if (operationKind === 'account-operation') {
      return Object.freeze({
        kind: 'completed',
        message: '账户操作已完成；用户与会话状态仍以服务端为准。',
        savedLabel: '不适用（账户操作）',
        effectiveLabel: '服务端状态为准',
        restartLabel: '不适用'
      });
    }
    if (operationKind === 'database-operation') {
      return Object.freeze({
        kind: 'completed',
        message: '数据库操作已完成；返回响应是当前权威结果。',
        savedLabel: '不适用（数据库操作）',
        effectiveLabel: '服务端状态为准',
        restartLabel: '不适用'
      });
    }
    return Object.freeze({
      kind: 'saved',
      message: '操作已完成；服务端返回值已作为新的权威投影。',
      savedLabel: '已保存',
      effectiveLabel: '投影已更新',
      restartLabel: '重载要求：后端未声明'
    });
  }

  if (result.status === 'forbidden') {
    return Object.freeze({
      kind: 'forbidden',
      message: '当前角色没有执行该 Settings endpoint 的权限。',
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
  if (!owner) throw new Error('Settings panel requires the mounted Settings owner.');
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
