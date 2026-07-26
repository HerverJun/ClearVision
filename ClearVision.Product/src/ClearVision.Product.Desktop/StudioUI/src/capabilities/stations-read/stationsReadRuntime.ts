import { useProductRuntime } from '@/app/productRuntime';
import type { ReadQueryClient } from '@/platform/query';
import type { ApiTransport } from '@/platform/api';
import type { SessionProjectionOwner } from '@/app/session';

export interface StationsReadRuntime {
  readonly queries: ReadQueryClient;
  readonly api?: ApiTransport;
  readonly session?: SessionProjectionOwner;
}

export function useStationsReadRuntime(override?: StationsReadRuntime): StationsReadRuntime {
  return override ?? useProductRuntime();
}
