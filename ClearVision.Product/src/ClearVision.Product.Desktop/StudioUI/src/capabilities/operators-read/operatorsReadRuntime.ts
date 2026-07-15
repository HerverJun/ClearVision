import { useProductRuntime } from '@/app/productRuntime';
import type { ReadQueryClient } from '@/platform/query';

export interface OperatorsReadRuntime {
  readonly queries: ReadQueryClient;
}

export function useOperatorsReadRuntime(override?: OperatorsReadRuntime): OperatorsReadRuntime {
  return override ?? useProductRuntime();
}
