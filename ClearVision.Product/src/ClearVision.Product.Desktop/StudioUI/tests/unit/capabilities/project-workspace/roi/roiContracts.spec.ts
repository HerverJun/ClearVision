import { describe, expect, it } from 'vitest';
import {
  createRoiCommitPayload,
  createRoiSessionIdentity,
  decodeRoiGeometry,
  isSameRoiSessionIdentity,
  resolveRoiEditorDescriptor,
  type RoiGeometry,
  type RoiSelectedNodeDraft
} from '@/capabilities/project-workspace/roi/roiContracts';

const bounds = Object.freeze({ width: 160, height: 120 });
const flags = Object.freeze({
  'Studio:CircleSearchV2ToolEnabled': true,
  'Studio:NPointCalibrationWorkbenchEnabled': true
});

function node(
  id: string,
  type: string,
  values: Readonly<Record<string, unknown>>
): RoiSelectedNodeDraft {
  return Object.freeze({
    id,
    type,
    parameters: Object.freeze(Object.entries(values).map(([name, value]) => Object.freeze({ name, value })))
  });
}

describe('G4 ROI contracts', () => {
  it.each([
    [
      node('roi-rect', 'RoiManager', { Shape: 'Rectangle', X: 10, Y: 12, Width: 30, Height: 20 }),
      'roi-manager-rectangle',
      'rectangle'
    ],
    [
      node('roi-circle', 'RoiManager', { Shape: 'Circle', CenterX: 40, CenterY: 45, Radius: 12 }),
      'roi-manager-circle',
      'circle'
    ],
    [
      node('roi-polygon', 'RoiManager', {
        Shape: 'Polygon', PolygonPoints: '[[10,10],[50,10],[50,40],[10,40]]'
      }),
      'roi-manager-polygon',
      'polygon'
    ],
    [
      node('template', 'TemplateMatching', {
        UseRoi: true, RoiX: 4, RoiY: 5, RoiWidth: 30, RoiHeight: 20
      }),
      'template-matching-roi',
      'rectangle'
    ],
    [
      node('box', 'BoxFilter', {
        FilterMode: 'Region', RegionX: 7, RegionY: 8, RegionW: 20, RegionH: 18
      }),
      'box-filter-region',
      'rectangle'
    ],
    [
      node('polar-annulus', 'PolarUnwrap', {
        CenterX: 60, CenterY: 50, InnerRadius: 10, OuterRadius: 30, StartAngle: 0, EndAngle: 360
      }),
      'polar-annulus',
      'annulus'
    ],
    [
      node('polar-arc', 'PolarUnwrap', {
        CenterX: 60, CenterY: 50, InnerRadius: 10, OuterRadius: 30, StartAngle: 20, EndAngle: 140
      }),
      'polar-arc',
      'arc'
    ],
    [
      node('circle-search', 'CircleMeasurement', {
        Method: 'CaliperFitV2', SearchCenterMode: 'Explicit', SearchCenterX: 70, SearchCenterY: 50,
        MinRadius: 10, NominalRadius: 20, MaxRadius: 30
      }),
      'circle-search-v2',
      'circle-search-v2'
    ],
    [
      node('npoint', 'NPointCalibration', {
        PointPairs: JSON.stringify([
          { ImageX: 10, ImageY: 12, WorldX: 1, WorldY: 2, Enabled: true },
          { ImageX: 30, ImageY: 32, WorldX: 3, WorldY: 4, Enabled: false }
        ])
      }),
      'npoint-sequence',
      'point-sequence'
    ],
    [
      node('caliper', 'CaliperTool', {}),
      'caliper-search-region',
      'rectangle'
    ]
  ])('maps canonical node semantics to %s/%s', (selectedNode, expectedKind, expectedGeometryKind) => {
    const descriptor = resolveRoiEditorDescriptor(selectedNode, flags);
    expect(descriptor).toMatchObject({
      nodeId: selectedNode.id,
      kind: expectedKind,
      geometryKind: expectedGeometryKind,
      supported: true
    });
  });

  it('decodes canonical geometry and creates one typed parameter-patch payload', () => {
    const selectedNode = node('roi-circle', 'RoiManager', {
      Shape: 'Circle', CenterX: 40, CenterY: 45, Radius: 12
    });
    const descriptor = resolveRoiEditorDescriptor(selectedNode, flags);
    const geometry = decodeRoiGeometry(selectedNode, descriptor, bounds);
    expect(geometry).toEqual({ kind: 'circle', centerX: 40, centerY: 45, radius: 12 });

    expect(createRoiCommitPayload(descriptor, {
      kind: 'circle', centerX: 50, centerY: 55, radius: 15
    })).toEqual({
      kind: 'parameter-patch',
      nodeId: 'roi-circle',
      descriptorId: 'roi-circle:roi-manager-circle',
      values: { CenterX: 50, CenterY: 55, Radius: 15 }
    });
  });

  it('merges TemplateMatching enable semantics into the typed payload', () => {
    const selectedNode = node('template', 'TemplateMatching', {
      UseRoi: false, RoiX: 0, RoiY: 0, RoiWidth: 0, RoiHeight: 0
    });
    const descriptor = resolveRoiEditorDescriptor(selectedNode, flags);
    expect(decodeRoiGeometry(selectedNode, descriptor, bounds)).toBeNull();
    expect(createRoiCommitPayload(descriptor, {
      kind: 'rectangle', x: 8, y: 9, width: 40, height: 30
    })).toMatchObject({
      kind: 'parameter-patch',
      values: { UseRoi: true, RoiX: 8, RoiY: 9, RoiWidth: 40, RoiHeight: 30 }
    });
  });

  it('preserves polygon and NPoint JSON through canonical codecs', () => {
    const polygonNode = node('polygon', 'RoiManager', {
      Shape: 'Polygon', PolygonPoints: '[[10,10],[50,10],[50,40],[10,40]]'
    });
    const polygonDescriptor = resolveRoiEditorDescriptor(polygonNode, flags);
    const polygon = decodeRoiGeometry(polygonNode, polygonDescriptor, bounds);
    expect(polygon).toEqual({
      kind: 'polygon',
      points: [{ x: 10, y: 10 }, { x: 50, y: 10 }, { x: 50, y: 40 }, { x: 10, y: 40 }]
    });
    expect(createRoiCommitPayload(polygonDescriptor, polygon as RoiGeometry)).toMatchObject({
      kind: 'parameter-patch',
      values: { PolygonPoints: '[[10,10],[50,10],[50,40],[10,40]]' }
    });

    const npointNode = node('npoint', 'NPointCalibration', {
      PointPairs: JSON.stringify([
        { ImageX: 10, ImageY: 12, WorldX: 1, WorldY: 2, Enabled: true },
        { ImageX: 30, ImageY: 32, WorldX: 3, WorldY: 4, Enabled: false }
      ])
    });
    const npointDescriptor = resolveRoiEditorDescriptor(npointNode, flags);
    const sequence = decodeRoiGeometry(npointNode, npointDescriptor, bounds);
    expect(sequence).toMatchObject({
      kind: 'pointSequence',
      points: [
        { x: 10, y: 12, worldX: 1, worldY: 2, enabled: true },
        { x: 30, y: 32, worldX: 3, worldY: 4, enabled: false }
      ]
    });
    expect(createRoiCommitPayload(npointDescriptor, sequence as RoiGeometry)).toMatchObject({
      kind: 'parameter-patch',
      values: {
        PointPairs: JSON.stringify([
          { ImageX: 10, ImageY: 12, WorldX: 1, WorldY: 2, Enabled: true },
          { ImageX: 30, ImageY: 32, WorldX: 3, WorldY: 4, Enabled: false }
        ])
      }
    });
  });

  it('creates a typed Caliper structural payload instead of fake parameters', () => {
    const descriptor = resolveRoiEditorDescriptor(node('caliper', 'CaliperTool', {}), flags);
    expect(createRoiCommitPayload(descriptor, {
      kind: 'rectangle', x: 12, y: 13, width: 44, height: 22
    })).toEqual({
      kind: 'caliper-structural',
      caliperNodeId: 'caliper',
      descriptorId: 'caliper:caliper-search-region',
      sourceOperatorType: 'RectangleRegion',
      sourceOutputPortName: 'Rectangle',
      targetInputPortName: 'SearchRegion',
      regionParameters: { X: 12, Y: 13, Width: 44, Height: 22 }
    });
  });

  it('fails closed when a geometry codec cannot produce every typed parameter', () => {
    const descriptor = resolveRoiEditorDescriptor(node('caliper', 'CaliperTool', {}), flags);
    expect(createRoiCommitPayload(descriptor, {
      kind: 'rectangle', x: Number.NaN, y: 13, width: 44, height: 22
    })).toMatchObject({
      kind: 'unsupported',
      reason: expect.stringContaining('X')
    });
  });

  it('fails closed for unsupported modes and disabled startup capabilities', () => {
    expect(resolveRoiEditorDescriptor(node('unknown', 'UnknownOperator', {}), flags))
      .toMatchObject({ kind: 'unsupported', supported: false, editable: false });
    expect(resolveRoiEditorDescriptor(node('roi-line', 'RoiManager', { Shape: 'Line' }), flags))
      .toMatchObject({ kind: 'unsupported', supported: false });
    expect(resolveRoiEditorDescriptor(node('circle', 'CircleMeasurement', { Method: 'HoughCircle' }), flags))
      .toMatchObject({ kind: 'unsupported', supported: false });
    expect(resolveRoiEditorDescriptor(
      node('circle-search', 'CircleMeasurement', { Method: 'CaliperFitV2' }),
      { 'Studio:CircleSearchV2ToolEnabled': false }
    )).toMatchObject({ kind: 'unsupported', supported: false });
    expect(resolveRoiEditorDescriptor(
      node('npoint', 'NPointCalibration', { PointPairs: '[]' }),
      { 'Studio:NPointCalibrationWorkbenchEnabled': false }
    )).toMatchObject({ kind: 'unsupported', supported: false });
  });

  it('rejects decoding with a descriptor from another selected node', () => {
    const first = node('first', 'RoiManager', { Shape: 'Rectangle', X: 1, Y: 2, Width: 3, Height: 4 });
    const second = node('second', 'RoiManager', { Shape: 'Rectangle', X: 5, Y: 6, Width: 7, Height: 8 });
    expect(decodeRoiGeometry(second, resolveRoiEditorDescriptor(first, flags), bounds)).toBeNull();
  });

  it('builds stable session identity and changes key for every stale boundary', () => {
    const baseInput = {
      projectId: 'project-1',
      nodeId: 'node-1',
      selectionRevision: 4,
      flowRevision: 9,
      previewRequestKey: 'preview:17',
      imageGeneration: 3
    } as const;
    const base = createRoiSessionIdentity(baseInput);
    expect(isSameRoiSessionIdentity(base, createRoiSessionIdentity({ ...baseInput }))).toBe(true);

    const changes = [
      { projectId: 'project-2' },
      { nodeId: 'node-2' },
      { selectionRevision: 5 },
      { flowRevision: 10 },
      { previewRequestKey: 'preview:18' },
      { imageGeneration: 4 }
    ];
    for (const change of changes) {
      expect(isSameRoiSessionIdentity(base, createRoiSessionIdentity({ ...baseInput, ...change }))).toBe(false);
    }
    expect(base.key).toBe('["project-1","node-1",4,9,"preview:17",3]');
  });

  it('rejects incomplete or negative session identities', () => {
    expect(() => createRoiSessionIdentity({
      projectId: ' ', nodeId: 'node', selectionRevision: 0, flowRevision: 0,
      previewRequestKey: null, imageGeneration: 0
    })).toThrow(/projectId/);
    expect(() => createRoiSessionIdentity({
      projectId: 'project', nodeId: 'node', selectionRevision: 0, flowRevision: -1,
      previewRequestKey: null, imageGeneration: 0
    })).toThrow(/flowRevision/);
  });
});
