import type { CvStatusTone } from '@/design-system';
import type {
  StationOfflineReason,
  StationOnlineState,
  StationRuntimeState,
  StationStatus
} from './stationContracts';

const offlineReasonLabels: Readonly<Record<StationOfflineReason, string>> = Object.freeze({
  NeverRegistered: '从未注册',
  HeartbeatExpired: '心跳过期',
  Disabled: '已停用',
  Disconnected: '连接已断开'
});

const onlineLabels: Readonly<Record<StationOnlineState, string>> = Object.freeze({
  Unknown: '未知',
  Online: '在线',
  Warning: '警告',
  Degraded: '降级',
  Critical: '严重',
  Offline: '离线'
});

const runtimeLabels: Readonly<Record<StationRuntimeState, string>> = Object.freeze({
  Unknown: '未知',
  Idle: '空闲',
  Running: '运行中',
  Paused: '已暂停',
  LoadingPackage: '加载运行包',
  Faulted: '故障',
  Stopping: '停止中'
});

export function stationOnlineLabel(state: StationOnlineState): string {
  return onlineLabels[state];
}

export function stationRuntimeLabel(state: StationRuntimeState): string {
  return runtimeLabels[state];
}

export function stationOfflineReasonLabel(reason: StationOfflineReason | null): string | null {
  return reason ? offlineReasonLabels[reason] : null;
}

export function stationOnlineTone(state: StationOnlineState): CvStatusTone {
  switch (state) {
    case 'Online':
      return 'ok';
    case 'Warning':
    case 'Degraded':
      return 'warning';
    case 'Critical':
      return 'ng';
    case 'Offline':
    case 'Unknown':
      return 'idle';
  }
}

export function stationRuntimeTone(state: StationRuntimeState): CvStatusTone {
  switch (state) {
    case 'Running':
      return 'ok';
    case 'Paused':
    case 'LoadingPackage':
    case 'Stopping':
      return 'warning';
    case 'Faulted':
      return 'ng';
    case 'Idle':
      return 'info';
    case 'Unknown':
      return 'idle';
  }
}

export function stationDisplayName(station: StationStatus): string {
  return station.stationName.trim() || station.machineName.trim() || station.stationId;
}

export function formatStationDateTime(value: string | null): string {
  if (!value) return '—';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return '—';
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(parsed);
}

export function formatStationDuration(seconds: number): string {
  const normalized = Math.max(0, Math.floor(seconds));
  const days = Math.floor(normalized / 86_400);
  const hours = Math.floor((normalized % 86_400) / 3_600);
  const minutes = Math.floor((normalized % 3_600) / 60);
  if (days > 0) return `${days} 天 ${hours} 小时`;
  if (hours > 0) return `${hours} 小时 ${minutes} 分`;
  return `${minutes} 分钟`;
}

export function formatStationBytes(bytes: number): string {
  if (bytes < 1_024) return `${bytes} B`;
  if (bytes < 1_048_576) return `${(bytes / 1_024).toFixed(1)} KB`;
  if (bytes < 1_073_741_824) return `${(bytes / 1_048_576).toFixed(1)} MB`;
  return `${(bytes / 1_073_741_824).toFixed(1)} GB`;
}

export function filterStations(
  stations: readonly StationStatus[],
  search: string,
  onlineState: string,
  runtimeState: string
): readonly StationStatus[] {
  const normalizedSearch = search.trim().toLocaleLowerCase('zh-CN');
  return stations.filter(station => {
    if (onlineState && onlineState !== 'all' && station.onlineState !== onlineState) return false;
    if (runtimeState && runtimeState !== 'all' && station.runtimeState !== runtimeState) return false;
    if (!normalizedSearch) return true;
    return [
      station.stationId,
      station.stationName,
      station.machineName,
      station.lineName ?? '',
      station.packageName ?? '',
      station.lastDiagnosticCode ?? ''
    ].some(value => value.toLocaleLowerCase('zh-CN').includes(normalizedSearch));
  });
}
