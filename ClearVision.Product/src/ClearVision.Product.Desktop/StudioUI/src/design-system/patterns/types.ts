export interface CvBreadcrumbItem {
  readonly label: string;
  readonly href?: string;
  readonly current?: boolean;
}

export type CvPageStateKind =
  | 'loading'
  | 'empty'
  | 'error'
  | 'offline'
  | 'stale'
  | 'partial'
  | 'conflict'
  | 'unknown'
  | 'unauthorized'
  | 'forbidden'
  | 'not-found';
