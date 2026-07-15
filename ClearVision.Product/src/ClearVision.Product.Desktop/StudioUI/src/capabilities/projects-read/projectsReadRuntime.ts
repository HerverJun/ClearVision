import { useProductRuntime } from '@/app/productRuntime';
import type { ReadQueryClient } from '@/platform/query';

export interface ProjectsReadRuntime {
  readonly queries: ReadQueryClient;
}

export function useProjectsReadRuntime(override?: ProjectsReadRuntime): ProjectsReadRuntime {
  return override ?? useProductRuntime();
}
