export interface ProductNavigationItem {
  readonly to: string;
  readonly label: string;
  readonly description: string;
  readonly allowedRoles?: readonly string[];
  readonly requiredFeatureFlag?: string;
}

export const productNavigation: readonly ProductNavigationItem[] = Object.freeze([
  { to: '/overview', label: '概览', description: '系统与最近工程摘要' },
  { to: '/projects', label: '工程', description: '创建、打开、编辑与删除工程' },
  { to: '/operators', label: '算子库', description: '只读算子合同与元数据' },
  {
    to: '/stations',
    label: '工作站',
    description: '受控 profile 下的工作站只读状态',
    requiredFeatureFlag: 'Studio2.StationsRead'
  },
  { to: '/results', label: '检测结果', description: '正式运行结果与历史投影' },
  {
    to: '/diagnostics',
    label: '诊断',
    description: '宿主、会话与本地服务状态',
    allowedRoles: Object.freeze(['Admin', 'Engineer'])
  },
  { to: '/about', label: '关于', description: '版本、边界与证据范围' }
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
