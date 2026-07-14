export type CvButtonVariant = 'primary' | 'secondary' | 'quiet' | 'danger';

export type CvStatusTone = 'ok' | 'ng' | 'warning' | 'info' | 'idle';

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
