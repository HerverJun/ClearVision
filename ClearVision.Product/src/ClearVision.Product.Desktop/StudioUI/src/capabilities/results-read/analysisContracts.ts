import { isResultsProjectId, ResultsContractDecodeError } from './resultsContracts';

export interface ResultsAnalysisFilters {
  readonly from: string;
  readonly to: string;
  readonly outcome: string;
  readonly defectType: string;
}

export interface DefectDistributionItem {
  readonly defectType: string;
  readonly count: number;
  readonly percentage: number;
}

export interface DefectDistribution {
  readonly projectId: string;
  readonly startTime: string | null;
  readonly endTime: string | null;
  readonly totalDefects: number;
  readonly items: readonly DefectDistributionItem[];
}

export interface ConfidenceBucket {
  readonly range: string;
  readonly count: number;
  readonly percentage: number;
}

export interface ConfidenceDistribution {
  readonly projectId: string;
  readonly startTime: string | null;
  readonly endTime: string | null;
  readonly totalDefects: number;
  readonly buckets: readonly ConfidenceBucket[];
  readonly averageConfidence: number;
}

export interface AnalysisTrendDataPoint {
  readonly timestamp: string;
  readonly totalCount: number;
  readonly okCount: number;
  readonly ngCount: number;
  readonly errorCount: number;
  readonly okRate: number;
  readonly yieldRate: number;
  readonly validDecisionCount: number;
  readonly executionFailureCount: number;
  readonly undeterminedCount: number;
  readonly invalidCount: number;
  readonly defectCount: number;
  readonly averageProcessingTime: number;
}

export interface AnalysisTrend {
  readonly projectId: string;
  readonly interval: string;
  readonly startTime: string;
  readonly endTime: string;
  readonly dataPoints: readonly AnalysisTrendDataPoint[];
}

export interface AnalysisSummary {
  readonly projectId: string;
  readonly totalCount: number;
  readonly okCount: number;
  readonly ngCount: number;
  readonly errorCount: number;
  readonly okRate: number;
  readonly yieldRate: number;
  readonly totalDefects: number;
  readonly averageProcessingTimeMs: number;
}

export interface ResultsAnalysisReport {
  readonly projectId: string;
  readonly generatedAt: string;
  readonly period: Readonly<{ startTime: string | null; endTime: string | null }>;
  readonly summary: AnalysisSummary;
  readonly defectDistribution: DefectDistribution;
  readonly confidenceDistribution: ConfidenceDistribution;
  readonly hourlyTrend: AnalysisTrend;
  readonly recommendations: readonly string[];
}

type JsonRecord = Record<string, unknown>;

function record(value: unknown, path: string): JsonRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new ResultsContractDecodeError(path, 'an object');
  }
  return value as JsonRecord;
}

function value(source: JsonRecord, name: string, ...aliases: string[]): unknown {
  for (const key of [name, ...aliases]) {
    const lowerCamel = key.slice(0, 1).toLowerCase() + key.slice(1);
    if (source[lowerCamel] !== undefined) return source[lowerCamel];
    if (source[key] !== undefined) return source[key];
  }
  return undefined;
}

function stringValue(input: unknown, path: string, allowEmpty = false): string {
  if (typeof input !== 'string' || (!allowEmpty && input.trim().length === 0)) {
    throw new ResultsContractDecodeError(path, allowEmpty ? 'a string' : 'a non-empty string');
  }
  return input;
}

function dateTime(input: unknown, path: string): string {
  const decoded = stringValue(input, path);
  if (Number.isNaN(Date.parse(decoded))) throw new ResultsContractDecodeError(path, 'an ISO date-time');
  return decoded;
}

function nullableDateTime(input: unknown, path: string): string | null {
  if (input === undefined || input === null) return null;
  return dateTime(input, path);
}

function projectId(input: unknown, path: string): string {
  const decoded = stringValue(input, path);
  if (!isResultsProjectId(decoded)) throw new ResultsContractDecodeError(path, 'a non-empty project UUID');
  return decoded;
}

function nonNegativeInteger(input: unknown, path: string): number {
  if (typeof input !== 'number' || !Number.isSafeInteger(input) || input < 0) {
    throw new ResultsContractDecodeError(path, 'a non-negative integer');
  }
  return input;
}

function finiteNumber(input: unknown, path: string): number {
  if (typeof input !== 'number' || !Number.isFinite(input)) {
    throw new ResultsContractDecodeError(path, 'a finite number');
  }
  return input;
}

function rate(input: unknown, path: string, max = 1): number {
  const decoded = finiteNumber(input, path);
  if (decoded < 0 || decoded > max) throw new ResultsContractDecodeError(path, `a rate from 0 to ${max}`);
  return decoded;
}

function decodeDistributionItem(input: unknown, path: string): DefectDistributionItem {
  const item = record(input, path);
  return Object.freeze({
    defectType: stringValue(value(item, 'defectType'), `${path}.defectType`),
    count: nonNegativeInteger(value(item, 'count'), `${path}.count`),
    percentage: rate(value(item, 'percentage'), `${path}.percentage`, 100)
  });
}

function decodeDefectDistribution(input: unknown, path = '$'): DefectDistribution {
  const item = record(input, path);
  const items = Array.isArray(value(item, 'items'))
    ? (value(item, 'items') as unknown[]).map((entry, index) => decodeDistributionItem(entry, `${path}.items[${index}]`))
    : [];
  return Object.freeze({
    projectId: projectId(value(item, 'projectId'), `${path}.projectId`),
    startTime: nullableDateTime(value(item, 'startTime'), `${path}.startTime`),
    endTime: nullableDateTime(value(item, 'endTime'), `${path}.endTime`),
    totalDefects: nonNegativeInteger(value(item, 'totalDefects'), `${path}.totalDefects`),
    items: Object.freeze(items)
  });
}

function decodeConfidenceBucket(input: unknown, path: string): ConfidenceBucket {
  const item = record(input, path);
  return Object.freeze({
    range: stringValue(value(item, 'range'), `${path}.range`),
    count: nonNegativeInteger(value(item, 'count'), `${path}.count`),
    percentage: rate(value(item, 'percentage'), `${path}.percentage`, 100)
  });
}

function decodeConfidenceDistribution(input: unknown, path = '$'): ConfidenceDistribution {
  const item = record(input, path);
  const rawBuckets = value(item, 'buckets');
  if (!Array.isArray(rawBuckets)) throw new ResultsContractDecodeError(`${path}.buckets`, 'an array');
  return Object.freeze({
    projectId: projectId(value(item, 'projectId'), `${path}.projectId`),
    startTime: nullableDateTime(value(item, 'startTime'), `${path}.startTime`),
    endTime: nullableDateTime(value(item, 'endTime'), `${path}.endTime`),
    totalDefects: nonNegativeInteger(value(item, 'totalDefects'), `${path}.totalDefects`),
    buckets: Object.freeze(rawBuckets.map((entry, index) => decodeConfidenceBucket(entry, `${path}.buckets[${index}]`))),
    averageConfidence: rate(value(item, 'averageConfidence'), `${path}.averageConfidence`)
  });
}

function decodeTrendPoint(input: unknown, path: string): AnalysisTrendDataPoint {
  const item = record(input, path);
  return Object.freeze({
    timestamp: dateTime(value(item, 'timestamp'), `${path}.timestamp`),
    totalCount: nonNegativeInteger(value(item, 'totalCount'), `${path}.totalCount`),
    okCount: nonNegativeInteger(value(item, 'okCount', 'OKCount', 'oKCount'), `${path}.okCount`),
    ngCount: nonNegativeInteger(value(item, 'ngCount', 'NGCount', 'nGCount'), `${path}.ngCount`),
    errorCount: nonNegativeInteger(value(item, 'errorCount'), `${path}.errorCount`),
    okRate: rate(value(item, 'okRate', 'OKRate', 'oKRate'), `${path}.okRate`),
    yieldRate: rate(value(item, 'yieldRate'), `${path}.yieldRate`),
    validDecisionCount: nonNegativeInteger(value(item, 'validDecisionCount'), `${path}.validDecisionCount`),
    executionFailureCount: nonNegativeInteger(value(item, 'executionFailureCount'), `${path}.executionFailureCount`),
    undeterminedCount: nonNegativeInteger(value(item, 'undeterminedCount'), `${path}.undeterminedCount`),
    invalidCount: nonNegativeInteger(value(item, 'invalidCount'), `${path}.invalidCount`),
    defectCount: nonNegativeInteger(value(item, 'defectCount'), `${path}.defectCount`),
    averageProcessingTime: finiteNumber(
      value(item, 'averageProcessingTime', 'averageProcessingTimeMs'),
      `${path}.averageProcessingTime`
    )
  });
}

function decodeTrend(input: unknown, path = '$'): AnalysisTrend {
  const item = record(input, path);
  const rawPoints = value(item, 'dataPoints');
  if (!Array.isArray(rawPoints)) throw new ResultsContractDecodeError(`${path}.dataPoints`, 'an array');
  const startTime = dateTime(value(item, 'startTime'), `${path}.startTime`);
  const endTime = dateTime(value(item, 'endTime'), `${path}.endTime`);
  if (Date.parse(startTime) > Date.parse(endTime)) {
    throw new ResultsContractDecodeError(path, 'a non-decreasing trend period');
  }
  return Object.freeze({
    projectId: projectId(value(item, 'projectId'), `${path}.projectId`),
    interval: stringValue(value(item, 'interval'), `${path}.interval`),
    startTime,
    endTime,
    dataPoints: Object.freeze(rawPoints.map((entry, index) => decodeTrendPoint(entry, `${path}.dataPoints[${index}]`)))
  });
}

function decodeSummary(input: unknown, path: string): AnalysisSummary {
  const item = record(input, path);
  return Object.freeze({
    projectId: projectId(value(item, 'projectId'), `${path}.projectId`),
    totalCount: nonNegativeInteger(value(item, 'totalCount'), `${path}.totalCount`),
    okCount: nonNegativeInteger(value(item, 'okCount', 'OKCount', 'oKCount'), `${path}.okCount`),
    ngCount: nonNegativeInteger(value(item, 'ngCount', 'NGCount', 'nGCount'), `${path}.ngCount`),
    errorCount: nonNegativeInteger(value(item, 'errorCount'), `${path}.errorCount`),
    okRate: rate(value(item, 'okRate', 'OKRate', 'oKRate'), `${path}.okRate`),
    yieldRate: rate(value(item, 'yieldRate'), `${path}.yieldRate`),
    totalDefects: nonNegativeInteger(value(item, 'totalDefects'), `${path}.totalDefects`),
    averageProcessingTimeMs: finiteNumber(
      value(item, 'averageProcessingTimeMs'),
      `${path}.averageProcessingTimeMs`
    )
  });
}

export function decodeDefectDistributionResponse(payload: unknown): DefectDistribution {
  return decodeDefectDistribution(payload);
}

export function decodeResultsAnalysisReport(payload: unknown): ResultsAnalysisReport {
  const item = record(payload, '$');
  const period = record(value(item, 'period'), '$.period');
  return Object.freeze({
    projectId: projectId(value(item, 'projectId'), '$.projectId'),
    generatedAt: dateTime(value(item, 'generatedAt'), '$.generatedAt'),
    period: Object.freeze({
      startTime: nullableDateTime(value(period, 'startTime'), '$.period.startTime'),
      endTime: nullableDateTime(value(period, 'endTime'), '$.period.endTime')
    }),
    summary: decodeSummary(value(item, 'summary'), '$.summary'),
    defectDistribution: decodeDefectDistribution(value(item, 'defectDistribution'), '$.defectDistribution'),
    confidenceDistribution: decodeConfidenceDistribution(value(item, 'confidenceDistribution'), '$.confidenceDistribution'),
    hourlyTrend: decodeTrend(value(item, 'hourlyTrend'), '$.hourlyTrend'),
    recommendations: Object.freeze(
      Array.isArray(value(item, 'recommendations'))
        ? (value(item, 'recommendations') as unknown[]).map((entry, index) =>
          stringValue(entry, `$.recommendations[${index}]`, true))
        : []
    )
  });
}

export function decodeResultsAnalysisTrend(payload: unknown): AnalysisTrend {
  return decodeTrend(payload);
}
