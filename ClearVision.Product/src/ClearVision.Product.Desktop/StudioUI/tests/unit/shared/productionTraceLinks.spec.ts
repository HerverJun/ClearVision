import { describe, expect, it } from 'vitest';
import {
  createLocalResultsDeepLink,
  createStationDetailDeepLink,
  createStationFleetDeepLink,
  createStationResultsDeepLink,
  resolveProductionReturnTo
} from '@/shared/productionTraceLinks';

describe('production trace deep links', () => {
  it('serializes only stable identities and filters in deterministic order', () => {
    expect(createLocalResultsDeepLink({
      projectId: 'project-a',
      resultId: 'result-a',
      returnTo: '/projects/project-a/workspace'
    })).toBe('/results?source=local&projectId=project-a&resultId=result-a&returnTo=%2Fprojects%2Fproject-a%2Fworkspace');

    expect(createStationResultsDeepLink({
      stationId: 'station A/B',
      resultId: 'message-9',
      outcome: 'Ng',
      diagnosticCode: 'WIRE_SWAP',
      page: 2,
      pageSize: 50
    })).toBe('/results?source=station&stationId=station+A%2FB&resultId=message-9&outcome=Ng&diagnosticCode=WIRE_SWAP&page=2&pageSize=50');

    expect(createStationFleetDeepLink({
      packageId: 'package-a', projectId: 'project-a', revision: 12
    })).toBe('/stations?packageId=package-a&projectId=project-a&revision=12');
  });

  it('accepts only explicit internal return targets and rejects query smuggling', () => {
    expect(resolveProductionReturnTo('/results?source=station&stationId=station-a')).toBe(
      '/results?source=station&stationId=station-a'
    );
    expect(resolveProductionReturnTo('/stations?packageId=package-a&projectId=project-a')).toBe(
      '/stations?packageId=package-a&projectId=project-a'
    );
    expect(resolveProductionReturnTo('/projects/project-a/inspection')).toBe('/projects/project-a/inspection');
    expect(resolveProductionReturnTo('/results?source=station&token=secret')).toBeNull();
    expect(resolveProductionReturnTo('https://example.com/results?source=station')).toBeNull();
    expect(resolveProductionReturnTo('//example.com/results?source=station')).toBeNull();
  });

  it('never forwards an unsafe return target to Station detail', () => {
    expect(createStationDetailDeepLink('station A/B', '/results?source=station&token=secret')).toBe(
      '/stations/station%20A%2FB'
    );
    expect(() => createStationDetailDeepLink('   ')).toThrow(TypeError);
    expect(() => createStationDetailDeepLink('station-a\u0000suffix')).toThrow(TypeError);
  });
});
