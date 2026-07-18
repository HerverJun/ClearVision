import { useProductRuntime } from '@/app/productRuntime';
import type { ProjectLifecycleCommandOwner } from '@/capabilities/project-lifecycle';
import type { ReadQueryClient } from '@/platform/query';

export interface ProjectsReadRuntime {
  readonly queries: ReadQueryClient;
  readonly projectLifecycle?: ProjectLifecycleCommandOwner;
}

export function useProjectsReadRuntime(override?: ProjectsReadRuntime): ProjectsReadRuntime {
  return override ?? useProductRuntime();
}
