import type { CvStatusTone } from '@/design-system';
import {
  operatorCategories,
  operatorLifecycles,
  type OperatorCatalogItem,
  type OperatorCategoryId,
  type OperatorLifecycle
} from './operatorContracts';

export const operatorCategoryLabels: Readonly<Record<OperatorCategoryId, string>> = Object.freeze({
  Acquisition: '采集',
  ImagePreprocessing: '图像预处理',
  SegmentationAndRegion: '分割与区域',
  FeatureExtraction: '特征提取',
  MatchingAndLocalization: '匹配与定位',
  DefectDetection: '缺陷检测',
  Measurement: '测量',
  CalibrationAndCoordinates: '标定与坐标',
  AiInference: 'AI 推理',
  PointCloud3D: '3D 点云',
  DataProcessing: '数据处理',
  FlowControl: '流程控制',
  Communication: '通信',
  OutputAndAuxiliary: '输出与辅助'
});

export const operatorLifecycleLabels: Readonly<Record<OperatorLifecycle, string>> = Object.freeze({
  Stable: '稳定',
  Experimental: '实验性',
  Reference: '参考',
  Legacy: '兼容旧版',
  Deprecated: '已弃用'
});

const operatorDataTypeLabels: Readonly<Record<string, string>> = Object.freeze({
  Image: '图像',
  Integer: '整数',
  Float: '浮点数',
  Boolean: '是 / 否',
  String: '文本',
  Point: '点',
  Rectangle: '矩形',
  Contour: '轮廓',
  PointList: '点集',
  DetectionResult: '检测结果',
  DetectionList: '检测结果集',
  CircleData: '圆',
  LineData: '直线',
  Region: '区域',
  BlobList: '连通区域集',
  BlobFeatureList: '连通区域特征集',
  Any: '任意类型'
});

const parameterDataTypeLabels: Readonly<Record<string, string>> = Object.freeze({
  bool: '是 / 否',
  boolean: '是 / 否',
  byte: '整数',
  short: '整数',
  int: '整数',
  integer: '整数',
  long: '整数',
  float: '浮点数',
  single: '浮点数',
  double: '浮点数',
  decimal: '小数',
  string: '文本',
  text: '文本',
  enum: '选项',
  image: '图像',
  point: '点',
  rectangle: '矩形',
  region: '区域'
});

export type OperatorVisibility = 'default' | 'all' | 'hidden';

export interface OperatorFilters {
  readonly q: string;
  readonly category: '' | OperatorCategoryId;
  readonly port: string;
  readonly parameter: string;
  readonly lifecycle: '' | OperatorLifecycle;
  readonly visibility: OperatorVisibility;
}

export interface OperatorPageSlice {
  readonly items: readonly OperatorCatalogItem[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageCount: number;
}

export function isOperatorCategory(value: string): value is OperatorCategoryId {
  return operatorCategories.includes(value as OperatorCategoryId);
}

export function isOperatorLifecycle(value: string): value is OperatorLifecycle {
  return operatorLifecycles.includes(value as OperatorLifecycle);
}

export function isOperatorVisibility(value: string): value is OperatorVisibility {
  return value === 'default' || value === 'all' || value === 'hidden';
}

function normalized(value: string): string {
  return value.trim().toLocaleLowerCase('zh-CN');
}

function includes(value: string | null | undefined, term: string): boolean {
  return normalized(value ?? '').includes(term);
}

export function filterOperators(
  operators: readonly OperatorCatalogItem[],
  filters: OperatorFilters
): readonly OperatorCatalogItem[] {
  const query = normalized(filters.q);
  const port = normalized(filters.port);
  const parameter = normalized(filters.parameter);

  return operators.filter(operator => {
    if (filters.category && operator.categoryId !== filters.category) return false;
    if (filters.lifecycle && operator.lifecycle !== filters.lifecycle) return false;
    if (filters.visibility === 'default' && operator.defaultHidden) return false;
    if (filters.visibility === 'hidden' && !operator.defaultHidden) return false;
    if (query && ![
      operator.operatorType,
      operator.displayName,
      operator.description,
      operator.category,
      ...operator.keywords,
      ...operator.tags
    ].some(value => includes(value, query))) return false;
    if (port && ![...operator.inputPorts, ...operator.outputPorts].some(item =>
      [item.name, item.displayName, item.dataType].some(value => includes(value, port)))) return false;
    if (parameter && !operator.parameters.some(item =>
      [item.name, item.displayName, item.dataType, item.description].some(value => includes(value, parameter)))) return false;
    return true;
  });
}

export function paginateOperators(
  operators: readonly OperatorCatalogItem[],
  requestedPage: number,
  pageSize: number
): OperatorPageSlice {
  const pageCount = Math.max(1, Math.ceil(operators.length / pageSize));
  const page = Math.min(Math.max(1, requestedPage), pageCount);
  const offset = (page - 1) * pageSize;
  return Object.freeze({
    items: Object.freeze(operators.slice(offset, offset + pageSize)),
    totalCount: operators.length,
    page,
    pageCount
  });
}

export function lifecycleTone(lifecycle: OperatorLifecycle): CvStatusTone {
  if (lifecycle === 'Stable') return 'ok';
  if (lifecycle === 'Experimental' || lifecycle === 'Reference') return 'info';
  return 'warning';
}

export function formatOperatorDataType(value: string): string {
  return operatorDataTypeLabels[value] ?? value;
}

export function formatParameterDataType(value: string): string {
  return parameterDataTypeLabels[value.trim().toLocaleLowerCase('en-US')] ?? value;
}

export function formatMetadataValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'string') return value;
  if (typeof value === 'boolean') return value ? '是' : '否';
  if (typeof value === 'number') return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return '—';
  }
}
