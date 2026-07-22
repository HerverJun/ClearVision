export interface ProductNavigationItem {
  readonly to: string;
  readonly label: string;
  readonly description: string;
  readonly allowedRoles?: readonly string[];
  readonly requiredFeatureFlag?: string;
}

export const productNavigation: readonly ProductNavigationItem[] = Object.freeze([
  { to: '/projects', label: '工程', description: '创建、打开、编辑与删除工程' },
  { to: '/results', label: '检测结果', description: '正式运行结果与证据追溯' }
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
