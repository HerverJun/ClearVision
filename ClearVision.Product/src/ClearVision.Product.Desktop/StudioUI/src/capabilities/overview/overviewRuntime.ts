import { useProductRuntime } from '@/app/productRuntime';
import type { SessionProjectionOwner } from '@/app/session';
import type { ReadQueryClient } from '@/platform/query';
import type { SystemStatusOwner } from '@/platform/status';

export interface OverviewRuntime {
  readonly queries: ReadQueryClient;
  readonly session: Pick<SessionProjectionOwner, 'projection' | 'refresh'>;
  readonly systemStatus: Pick<SystemStatusOwner, 'projection' | 'refresh'>;
}

export function useOverviewRuntime(override?: OverviewRuntime): OverviewRuntime {
  return override ?? useProductRuntime();
}
