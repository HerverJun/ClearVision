export interface ProductNavigationItem {
  readonly to: string;
  readonly label: string;
  readonly description: string;
  readonly allowedRoles?: readonly string[];
  readonly requiredFeatureFlag?: string;
}

export const productNavigation: readonly ProductNavigationItem[] = Object.freeze([
  { to: '/overview', label: '概览', description: '查看当前 Studio 状态与工作摘要' },
  { to: '/ai', label: 'AI 工程工作台', description: '建立或恢复 AI 工程会话', allowedRoles: ['Admin', 'Engineer'], requiredFeatureFlag: 'Studio2.AiWorkbench' },
  { to: '/inspection', label: '连续检测', description: '选择工程并运行连续检测', allowedRoles: ['Admin', 'Engineer'], requiredFeatureFlag: 'Studio2.InspectionRun' },
  { to: '/operators', label: '算子库', description: '浏览可用算子与参数契约' },
  { to: '/projects', label: '工程', description: '创建、打开、编辑与删除工程' },
  { to: '/results', label: '检测结果', description: '正式运行结果与证据追溯' },
  { to: '/settings', label: '设置', description: '查看服务端配置投影与读取范围', allowedRoles: ['Admin', 'Engineer'], requiredFeatureFlag: 'Studio2.Settings' },
  { to: '/stations', label: '工作站', description: '工作站监控与授权控制', requiredFeatureFlag: 'Studio2.StationsRead' },
  { to: '/diagnostics', label: '诊断', description: '检查本地服务和 Studio 状态', allowedRoles: ['Admin', 'Engineer'] },
  { to: '/about', label: '关于', description: '查看版本与运行环境信息' }
]);

export function visibleProductNavigation(
  role: string | undefined,
  featureFlags: Readonly<Record<string, boolean>>
): readonly ProductNavigationItem[] {
  return productNavigation.filter(item =>
    (!item.allowedRoles || (role !== undefined && item.allowedRoles.includes(role))) &&
    (!item.requiredFeatureFlag || featureFlags[item.requiredFeatureFlag] === true)
  );
}
