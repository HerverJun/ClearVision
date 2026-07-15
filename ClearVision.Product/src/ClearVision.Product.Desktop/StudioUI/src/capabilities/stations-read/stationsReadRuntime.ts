import { useProductRuntime } from '@/app/productRuntime';
import type { ReadQueryClient } from '@/platform/query';

export interface StationsReadRuntime {
  readonly queries: ReadQueryClient;
}

export function useStationsReadRuntime(override?: StationsReadRuntime): StationsReadRuntime {
  return override ?? useProductRuntime();
}
