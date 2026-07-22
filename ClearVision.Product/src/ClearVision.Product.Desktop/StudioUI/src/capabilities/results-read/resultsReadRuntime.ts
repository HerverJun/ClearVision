import { useProductRuntime } from '@/app/productRuntime';
import type { ReadQueryClient } from '@/platform/query';
import type { ApiTransport } from '@/platform/api';

export interface ResultsReadRuntime {
  readonly queries: ReadQueryClient;
  readonly api?: ApiTransport;
}

export function useResultsReadRuntime(override?: ResultsReadRuntime): ResultsReadRuntime {
  return override ?? useProductRuntime();
}
