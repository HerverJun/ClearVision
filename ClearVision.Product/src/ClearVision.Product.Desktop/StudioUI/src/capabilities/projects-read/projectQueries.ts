import type {
  ReadQueryClient,
  ReadQueryDefinition,
  ReadQueryOwner
} from '@/platform/query';
import {
  decodeProjectDetails,
  decodeProjectSummaryList,
  isProjectId,
  type ProjectDetails,
  type ProjectSummary
} from './projectContracts';

export const defaultRecentProjectCount = 5;

export function normalizeProjectSearchTerm(value: string): string {
  return value.trim();
}

export function createProjectsPath(searchTerm: string): string {
  const normalized = normalizeProjectSearchTerm(searchTerm);
  if (!normalized) return 'projects';
  const query = new URLSearchParams({ keyword: normalized });
  return `projects/search?${query.toString()}`;
}

export function createRecentProjectsPath(count = defaultRecentProjectCount): string {
  if (!Number.isInteger(count) || count <= 0) {
    throw new RangeError('Recent project count must be a positive integer.');
  }
  return `projects/recent?count=${count}`;
}

export function createProjectDetailsPath(projectId: string): string {
  if (!isProjectId(projectId)) {
    throw new TypeError('Project id must be a non-empty UUID.');
  }
  return `projects/${projectId}`;
}

export function createProjectsListDefinition(
  searchTerm: () => string
): ReadQueryDefinition<readonly ProjectSummary[]> {
  return Object.freeze({
    key: () => {
      const normalized = normalizeProjectSearchTerm(searchTerm());
      return normalized ? `projects:search:${normalized}` : 'projects:list';
    },
    path: () => createProjectsPath(searchTerm()),
    decode: decodeProjectSummaryList,
    isEmpty: (projects: readonly ProjectSummary[]) => projects.length === 0,
    protected: true,
    cacheTimeMs: 10_000
  });
}

export function createProjectsListQuery(
  client: ReadQueryClient,
  searchTerm: () => string
): ReadQueryOwner<readonly ProjectSummary[]> {
  return client.createQuery(createProjectsListDefinition(searchTerm));
}

export function createRecentProjectsDefinition(
  count = defaultRecentProjectCount
): ReadQueryDefinition<readonly ProjectSummary[]> {
  const path = createRecentProjectsPath(count);
  return Object.freeze({
    key: `projects:recent:${count}`,
    path,
    decode: decodeProjectSummaryList,
    isEmpty: (projects: readonly ProjectSummary[]) => projects.length === 0,
    protected: true,
    cacheTimeMs: 15_000
  });
}

export function createRecentProjectsQuery(
  client: ReadQueryClient,
  count = defaultRecentProjectCount
): ReadQueryOwner<readonly ProjectSummary[]> {
  return client.createQuery(createRecentProjectsDefinition(count));
}

export function createProjectDetailsDefinition(
  projectId: () => string
): ReadQueryDefinition<ProjectDetails> {
  return Object.freeze({
    key: () => `projects:detail:${projectId()}`,
    path: () => createProjectDetailsPath(projectId()),
    decode: decodeProjectDetails,
    protected: true,
    cacheTimeMs: 10_000
  });
}

export function createProjectDetailsQuery(
  client: ReadQueryClient,
  projectId: () => string
): ReadQueryOwner<ProjectDetails> {
  return client.createQuery(createProjectDetailsDefinition(projectId));
}
