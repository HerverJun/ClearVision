export type CvButtonVariant = 'primary' | 'secondary' | 'quiet' | 'danger';

export type CvStatusTone = 'ok' | 'ng' | 'warning' | 'info' | 'idle';

export type CvInlineAlertTone = 'info' | 'success' | 'warning' | 'error';

export type CvSortDirection = 'ascending' | 'descending';

export interface CvDataTableColumn<Row> {
  readonly key: string;
  readonly label: string;
  readonly align?: 'start' | 'center' | 'end';
  readonly width?: string;
  readonly sortable?: boolean;
  readonly value?: { bivarianceHack(row: Row): unknown }['bivarianceHack'];
}

export interface CvDataTableSort {
  readonly key: string;
  readonly direction: CvSortDirection;
}

export interface CvDescriptionItem {
  readonly key: string;
  readonly label: string;
  readonly value?: string | number | null;
  readonly span?: 1 | 2;
}

export interface CvSelectOption {
  readonly value: string;
  readonly label: string;
  readonly disabled?: boolean;
}

export interface CvToastItem {
  readonly id: string;
  readonly title: string;
  readonly message?: string;
  readonly tone?: CvStatusTone;
  readonly durationMs?: number;
}
