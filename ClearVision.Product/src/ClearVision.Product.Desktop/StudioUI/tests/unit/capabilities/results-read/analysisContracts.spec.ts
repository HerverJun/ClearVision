import { describe, expect, it } from 'vitest';
import {
  decodeDefectDistributionResponse,
  decodeResultsAnalysisReport,
  decodeResultsAnalysisTrend
} from '@/capabilities/results-read/analysisContracts';
import {
  createAnalysisTrendDefinition,
  createDefectDistributionDefinition,
  normalizeAnalysisTrendWindow
} from '@/capabilities/results-read/analysisQueries';

const projectId = '11111111-1111-4111-8111-111111111111';
const startTime = '2026-08-07T00:00:00Z';
const endTime = '2026-08-07T01:00:00Z';

describe('results analysis contracts', () => {
  it('decodes server defect distribution percentages', () => {
    const decoded = decodeDefectDistributionResponse({
      projectId,
      startTime,
      endTime,
      totalDefects: 4,
      items: [{ defectType: 'Scratch', count: 3, percentage: 75 }]
    });

    expect(decoded.projectId).toBe(projectId);
    expect(decoded.items[0]).toEqual({ defectType: 'Scratch', count: 3, percentage: 75 });
  });

  it('rejects a distribution whose percentage is outside the backend contract', () => {
    expect(() => decodeDefectDistributionResponse({
      projectId,
      totalDefects: 1,
      items: [{ defectType: 'Scratch', count: 1, percentage: 101.5 }]
    })).toThrow(/percentage/);
  });

  it('decodes report summary, confidence, trend and recommendations together', () => {
    const report = decodeResultsAnalysisReport({
      projectId,
      generatedAt: endTime,
      period: { startTime, endTime },
      summary: {
        projectId,
        totalCount: 10,
        okCount: 8,
        ngCount: 2,
        errorCount: 0,
        okRate: 0.8,
        yieldRate: 0.8,
        totalDefects: 2,
        averageProcessingTimeMs: 25
      },
      defectDistribution: {
        projectId,
        startTime,
        endTime,
        totalDefects: 2,
        items: [{ defectType: 'Scratch', count: 2, percentage: 100 }]
      },
      confidenceDistribution: {
        projectId,
        startTime,
        endTime,
        totalDefects: 2,
        buckets: [{ range: '90-100%', count: 2, percentage: 100 }],
        averageConfidence: 0.92
      },
      hourlyTrend: {
        projectId,
        interval: 'Hour',
        startTime,
        endTime,
        dataPoints: [{
          timestamp: startTime,
          totalCount: 10,
          okCount: 8,
          ngCount: 2,
          errorCount: 0,
          okRate: 0.8,
          yieldRate: 0.8,
          validDecisionCount: 10,
          executionFailureCount: 0,
          undeterminedCount: 0,
          invalidCount: 0,
          defectCount: 2,
          averageProcessingTime: 25
        }]
      },
      recommendations: ['复核划痕缺陷分布']
    });

    expect(report.summary.totalCount).toBe(10);
    expect(report.confidenceDistribution.averageConfidence).toBe(0.92);
    expect(report.hourlyTrend.dataPoints).toHaveLength(1);
    expect(report.recommendations).toEqual(['复核划痕缺陷分布']);
  });

  it('builds protected query paths from the selected project and filters', () => {
    const filters = () => ({ from: startTime, to: endTime, outcome: 'Ng', defectType: '' });
    const distribution = createDefectDistributionDefinition(() => projectId, filters);
    const trend = createAnalysisTrendDefinition(
      () => projectId,
      filters,
      () => 'Hour',
      () => startTime,
      () => endTime
    );

    const distributionPath = typeof distribution.path === 'function' ? distribution.path() : distribution.path;
    const trendPath = typeof trend.path === 'function' ? trend.path() : trend.path;
    expect(distributionPath).toContain(`analysis/defect-distribution/${projectId}`);
    expect(distributionPath).toContain('status=Ng');
    expect(trendPath).toContain(`analysis/trend/${projectId}`);
    expect(trendPath).toContain('interval=Hour');
    expect(trendPath).toContain('startTime=2026-08-07T00%3A00%3A00Z');
  });

  it('decodes a sparse trend only when required server counters are present', () => {
    expect(() => decodeResultsAnalysisTrend({
      projectId,
      interval: 'Hour',
      startTime,
      endTime,
      dataPoints: [{ timestamp: startTime }]
    })).toThrow(/totalCount/);
  });

  it('normalizes a future-only start into an ordered zero-width trend window', () => {
    const now = Date.parse('2026-08-07T00:00:00Z');
    const window = normalizeAnalysisTrendWindow('2026-08-08T00:00:00Z', '', now);

    expect(window.start).toBe('2026-08-08T00:00:00.000Z');
    expect(window.end).toBe('2026-08-08T00:00:00.000Z');
  });

  it('keeps a past end-only filter within the previous twenty-four hours', () => {
    const window = normalizeAnalysisTrendWindow('', '2026-08-07T00:00:00Z', Date.parse('2026-08-08T12:00:00Z'));

    expect(window.start).toBe('2026-08-06T00:00:00.000Z');
    expect(window.end).toBe('2026-08-07T00:00:00.000Z');
  });
});
