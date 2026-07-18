export interface ProductNavigationItem {
  readonly to: string;
  readonly label: string;
  readonly description: string;
  readonly allowedRoles?: readonly string[];
  readonly requiredFeatureFlag?: string;
}

export const productNavigation: readonly ProductNavigationItem[] = Object.freeze([
  { to: '/overview', label: '概览', description: '系统与最近工程摘要' },
  { to: '/projects', label: '工程', description: '只读工程列表与详情' },
  { to: '/operators', label: '算子库', description: '只读算子合同与元数据' },
  {
    to: '/stations',
    label: '工作站',
    description: '现场工作站状态与健康',
    requiredFeatureFlag: 'Studio2.StationsRead'
  },
  { to: '/results', label: '检测结果', description: '本机与工作站结果复核' },
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
