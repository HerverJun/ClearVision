export interface CvBreadcrumbItem {
  readonly label: string;
  readonly href?: string;
  readonly current?: boolean;
}

export type CvPageStateKind =
  | 'loading'
  | 'empty'
  | 'error'
  | 'unauthorized'
  | 'forbidden'
  | 'not-found';
