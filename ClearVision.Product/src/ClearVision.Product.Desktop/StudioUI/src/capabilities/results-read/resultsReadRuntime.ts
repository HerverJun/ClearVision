import { useProductRuntime } from '@/app/productRuntime';
import type { ReadQueryClient } from '@/platform/query';

export interface ResultsReadRuntime {
  readonly queries: ReadQueryClient;
}

export function useResultsReadRuntime(override?: ResultsReadRuntime): ResultsReadRuntime {
  return override ?? useProductRuntime();
}
