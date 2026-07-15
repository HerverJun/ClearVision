import type { ProjectDetails, ProjectSummary } from './projectContracts';

export type ProjectSort = 'modified-desc' | 'created-desc' | 'name-asc';

export interface ProjectPageSlice {
  readonly items: readonly ProjectSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly pageCount: number;
  readonly totalCount: number;
}

const dateFormatter = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit'
});

function timestamp(value: string | null): number {
  return value === null ? Number.NEGATIVE_INFINITY : Date.parse(value);
}

export function sortProjects(
  projects: readonly ProjectSummary[],
  sort: ProjectSort
): readonly ProjectSummary[] {
  return [...projects].sort((left, right) => {
    if (sort === 'name-asc') {
      return left.name.localeCompare(right.name, 'zh-CN');
    }
    if (sort === 'created-desc') {
      return timestamp(right.createdAt) - timestamp(left.createdAt);
    }
    const modifiedDifference = timestamp(right.modifiedAt) - timestamp(left.modifiedAt);
    return modifiedDifference !== 0
      ? modifiedDifference
      : left.name.localeCompare(right.name, 'zh-CN');
  });
}

export function paginateProjects(
  projects: readonly ProjectSummary[],
  requestedPage: number,
  pageSize: number
): ProjectPageSlice {
  const normalizedPageSize = Math.max(1, Math.trunc(pageSize));
  const pageCount = Math.max(1, Math.ceil(projects.length / normalizedPageSize));
  const page = Math.min(pageCount, Math.max(1, Math.trunc(requestedPage)));
  const offset = (page - 1) * normalizedPageSize;
  return Object.freeze({
    items: Object.freeze(projects.slice(offset, offset + normalizedPageSize)),
    page,
    pageSize: normalizedPageSize,
    pageCount,
    totalCount: projects.length
  });
}

export function formatProjectDateTime(value: string | null): string {
  return value === null ? '—' : dateFormatter.format(new Date(value));
}

export function describeProjectDecision(project: ProjectDetails): string {
  if (project.flow?.decision === null || project.flow === null) return '未提供决策配置';
  const decision = project.flow.decision;
  return decision.configured
    ? `已配置（缺失策略：${decision.missingDecisionPolicy}）`
    : `未绑定最终决策（缺失策略：${decision.missingDecisionPolicy}）`;
}
