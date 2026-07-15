import { describe, expect, it } from 'vitest';
import {
  StationContractDecodeError,
  decodeStationAdminDetails,
  decodeStationHealth,
  decodeStationList,
  decodeStationResults,
  decodeStationStatistics,
  decodeStationSummary
} from '@/capabilities/stations-read';
import {
  outcomeStatistics,
  stationHealth,
  stationResult,
  stationStatistics,
  stationStatus,
  stationSummary
} from './stationFixtures';

describe('Station response decoders', () => {
  it('decodes list, summary, statistics, health and the admin-only projection', () => {
    const stations = decodeStationList([stationStatus({
      onlineState: 1,
      runtimeState: 2,
      futureField: 'ignored'
    })]);
    const summary = decodeStationSummary(stationSummary());
    const statistics = decodeStationStatistics(stationStatistics());
    const health = decodeStationHealth([stationHealth()]);
    const admin = decodeStationAdminDetails(stationStatus({ recentCommands: 'ignored' }));

    expect(stations[0]).toMatchObject({
      stationId: 'station-a',
      onlineState: 'Online',
      runtimeState: 'Running',
      lastOutcome: { execution: 'Succeeded', decision: 'Ng' }
    });
    expect(summary.outcomeStatistics).toEqual(outcomeStatistics());
    expect(statistics.outcomeStatistics).toEqual(outcomeStatistics());
    expect(statistics.byDiagnosticCode).toEqual([{ diagnosticCode: 'WIRE_SWAP', count: 1 }]);
    expect(health[0]).toMatchObject({ stationId: 'station-a', runtimeState: 'Running' });
    expect(admin).toMatchObject({ stationId: 'station-a', owner: '生产一组' });
    expect(admin).not.toHaveProperty('recentCommands');
  });

  it('preserves canonical Execution and Decision axes without folding non-NG outcomes', () => {
    const results = decodeStationResults([
      stationResult({ executionOutcome: 'Succeeded', decisionOutcome: 'Undetermined' }),
      stationResult({ sequenceId: 10, messageId: 'm-10', executionOutcome: 'Succeeded', decisionOutcome: 'Invalid' }),
      stationResult({ sequenceId: 11, messageId: 'm-11', executionOutcome: 'Failed', decisionOutcome: 'Undetermined' })
    ]);

    expect(results.map(item => item.outcome)).toEqual([
      { execution: 'Succeeded', decision: 'Undetermined' },
      { execution: 'Succeeded', decision: 'Invalid' },
      { execution: 'Failed', decision: 'Undetermined' }
    ]);
    expect(results.every(item => !item.legacyOutcomeProjection)).toBe(true);
  });

  it.each([
    ['Ok', 'Succeeded', 'Ok'],
    ['Ng', 'Succeeded', 'Ng'],
    ['Error', 'Failed', 'Undetermined'],
    ['Canceled', 'Cancelled', 'NotApplicable'],
    ['Undetermined', 'Succeeded', 'Undetermined']
  ] as const)('projects legacy Station outcome %s exactly like the current backend reader', (legacy, execution, decision) => {
    const [result] = decodeStationResults([
      stationResult({ executionOutcome: null, decisionOutcome: null, outcome: legacy })
    ]);

    expect(result).toMatchObject({
      outcome: { execution, decision },
      legacyOutcomeProjection: true
    });
  });

  it.each([
    [0, 'Succeeded', 'Ok'],
    [1, 'Succeeded', 'Ng'],
    [2, 'Failed', 'Undetermined'],
    [3, 'Cancelled', 'NotApplicable'],
    [4, 'Succeeded', 'Undetermined']
  ] as const)('decodes authoritative numeric legacy outcome %s', (legacy, execution, decision) => {
    const [result] = decodeStationResults([
      stationResult({ executionOutcome: null, decisionOutcome: null, outcome: legacy })
    ]);

    expect(result).toMatchObject({
      outcome: { execution, decision },
      legacyOutcomeProjection: true
    });
  });

  it.each([
    ['list object', {}, '$'],
    ['unknown online state', [stationStatus({ onlineState: 'FutureOnline' })], '$[0].onlineState'],
    ['partial last axes', [stationStatus({ lastDecisionOutcome: null })], '$[0].lastOutcome'],
    ['negative counter', stationSummary({ outcomeStatistics: outcomeStatistics({ failedCount: -1 }) }), '$.outcomeStatistics.failedCount']
  ])('rejects malformed %s', (_label, payload, expectedPath) => {
    const decode = Array.isArray(payload) || expectedPath === '$' ? decodeStationList : decodeStationSummary;
    expect(() => decode(payload)).toThrow(StationContractDecodeError);
    try {
      decode(payload);
    } catch (error) {
      expect(error).toMatchObject({ path: expectedPath });
    }
  });

  it('rejects unknown legacy outcomes and partial canonical result axes', () => {
    expect(() => decodeStationResults([
      stationResult({ executionOutcome: null, decisionOutcome: null, outcome: 'FutureOutcome' })
    ])).toThrow(StationContractDecodeError);
    expect(() => decodeStationList([
      stationStatus({ onlineState: 99 })
    ])).toThrow(StationContractDecodeError);
    expect(() => decodeStationResults([
      stationResult({ executionOutcome: 'Succeeded', decisionOutcome: null })
    ])).toThrow(StationContractDecodeError);
  });
});
