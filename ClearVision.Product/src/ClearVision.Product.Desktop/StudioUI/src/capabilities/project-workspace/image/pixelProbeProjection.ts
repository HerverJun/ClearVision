import {
  PIXEL_PROBE_DEFAULT_MESSAGE,
  PIXEL_PROBE_LOADING_MESSAGE,
  PIXEL_PROBE_NO_IMAGE_MESSAGE,
  PIXEL_PROBE_OUTSIDE_MESSAGE,
  PIXEL_PROBE_UNREADABLE_MESSAGE,
  formatLockedPixelProbeStatus,
  formatPixelProbeStatus,
  formatRoiProbeStatus,
  resolvePixelWorldCoordinate
} from '@clearvision/canonical-image-pixel-probe';

export const PIXEL_PROBE_STALE_MESSAGE = '预览图像已过期，无法读取像素值';

export type PixelProbePhase =
  | 'no-image'
  | 'loading'
  | 'stale'
  | 'idle'
  | 'hover'
  | 'locked'
  | 'roi'
  | 'outside'
  | 'unreadable';

export type PixelProbeImageStatus = 'no-image' | 'loading' | 'ready' | 'stale';

export interface PixelProbeImageContext {
  readonly identity: string | null;
  readonly status: PixelProbeImageStatus;
  readonly width?: number;
  readonly height?: number;
  readonly scale?: number;
  readonly worldSource?: unknown;
}

export interface PixelProbeGrayStatistics {
  readonly mean: number;
  readonly min: number;
  readonly max: number;
}

export interface PixelProbeRgbMean {
  readonly r: number;
  readonly g: number;
  readonly b: number;
}

export interface PixelProbeStatistics {
  readonly ok: boolean;
  readonly count?: number;
  readonly gray?: PixelProbeGrayStatistics;
  readonly rgbMean?: PixelProbeRgbMean | null;
}

export interface PixelProbePointSample {
  readonly x: number;
  readonly y: number;
  readonly rgba: ArrayLike<number>;
}

export interface PixelProbeLockedSample extends PixelProbePointSample {
  readonly neighborhoods?: Readonly<Record<number, PixelProbeStatistics>>;
  readonly world?: Readonly<Record<string, unknown>> | null;
}

export interface PixelProbeRoi {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface PixelProbeRoiSample {
  readonly roi: PixelProbeRoi;
  readonly stats: PixelProbeStatistics;
  readonly world?: Readonly<Record<string, unknown>> | null;
}

export interface PixelProbeProjection {
  readonly identity: string | null;
  readonly phase: PixelProbePhase;
  readonly message: string;
  readonly canProbe: boolean;
  readonly imageWidth: number;
  readonly imageHeight: number;
  readonly scale: number;
  readonly point: Readonly<{ x: number; y: number }> | null;
  readonly rgba: readonly number[] | null;
  readonly roi: PixelProbeRoi | null;
  readonly neighborhoods: Readonly<Record<number, PixelProbeStatistics>> | null;
  readonly stats: PixelProbeStatistics | null;
  readonly world: Readonly<Record<string, unknown>> | null;
}

export interface PixelProbeProjectionModel {
  setImageContext(context: PixelProbeImageContext): PixelProbeProjection;
  showHover(sample: PixelProbePointSample): PixelProbeProjection;
  showOutside(): PixelProbeProjection;
  showUnreadable(): PixelProbeProjection;
  lockPixel(sample: PixelProbeLockedSample): PixelProbeProjection;
  showRoi(sample: PixelProbeRoiSample): PixelProbeProjection;
  reset(): PixelProbeProjection;
  getProjection(): PixelProbeProjection;
}

type Observation =
  | Readonly<{ kind: 'none' }>
  | Readonly<{ kind: 'hover'; sample: PixelProbePointSample }>
  | Readonly<{ kind: 'locked'; sample: PixelProbeLockedSample }>
  | Readonly<{ kind: 'roi'; sample: PixelProbeRoiSample }>
  | Readonly<{ kind: 'outside' }>
  | Readonly<{ kind: 'unreadable' }>;

function finitePositive(value: unknown): number {
  const numberValue = Number(value);
  return Number.isFinite(numberValue) && numberValue > 0 ? numberValue : 0;
}

function finiteScale(value: unknown): number {
  const numberValue = Number(value);
  return Number.isFinite(numberValue) && numberValue > 0 ? numberValue : 1;
}

function finitePoint(sample: PixelProbePointSample): boolean {
  return Number.isFinite(Number(sample.x)) &&
    Number.isFinite(Number(sample.y)) &&
    sample.rgba !== null &&
    sample.rgba !== undefined &&
    sample.rgba.length >= 3 &&
    Array.from(sample.rgba).every(value => Number.isFinite(Number(value)));
}

function validRoi(sample: PixelProbeRoiSample): boolean {
  return Number.isFinite(Number(sample.roi.x)) &&
    Number.isFinite(Number(sample.roi.y)) &&
    finitePositive(sample.roi.width) > 0 &&
    finitePositive(sample.roi.height) > 0 &&
    sample.stats?.ok === true &&
    sample.stats.gray !== undefined;
}

function frozenPoint(sample: PixelProbePointSample): Readonly<{ x: number; y: number }> {
  return Object.freeze({ x: Number(sample.x), y: Number(sample.y) });
}

function frozenRgba(rgba: ArrayLike<number>): readonly number[] {
  return Object.freeze(Array.from(rgba, Number));
}

function frozenRoi(roi: PixelProbeRoi): PixelProbeRoi {
  return Object.freeze({
    x: Number(roi.x),
    y: Number(roi.y),
    width: Number(roi.width),
    height: Number(roi.height)
  });
}

function unavailableProjection(
  context: PixelProbeImageContext,
  phase: Extract<PixelProbePhase, 'no-image' | 'loading' | 'stale'>,
  message: string
): PixelProbeProjection {
  return Object.freeze({
    identity: context.identity,
    phase,
    message,
    canProbe: false,
    imageWidth: finitePositive(context.width),
    imageHeight: finitePositive(context.height),
    scale: finiteScale(context.scale),
    point: null,
    rgba: null,
    roi: null,
    neighborhoods: null,
    stats: null,
    world: null
  });
}

function baseProjection(
  context: PixelProbeImageContext,
  phase: PixelProbePhase,
  message: string
): PixelProbeProjection {
  return Object.freeze({
    identity: context.identity,
    phase,
    message,
    canProbe: context.status === 'ready' &&
      context.identity !== null &&
      finitePositive(context.width) > 0 &&
      finitePositive(context.height) > 0,
    imageWidth: finitePositive(context.width),
    imageHeight: finitePositive(context.height),
    scale: finiteScale(context.scale),
    point: null,
    rgba: null,
    roi: null,
    neighborhoods: null,
    stats: null,
    world: null
  });
}

function imageUnavailable(context: PixelProbeImageContext): PixelProbeProjection | null {
  if (context.status === 'loading') {
    return unavailableProjection(context, 'loading', PIXEL_PROBE_LOADING_MESSAGE);
  }
  if (context.status === 'stale') {
    return unavailableProjection(context, 'stale', PIXEL_PROBE_STALE_MESSAGE);
  }
  if (context.status === 'no-image' || context.identity === null) {
    return unavailableProjection(context, 'no-image', PIXEL_PROBE_NO_IMAGE_MESSAGE);
  }
  return null;
}

export function createPixelProbeProjectionModel(
  initialContext: PixelProbeImageContext = Object.freeze({ identity: null, status: 'no-image' })
): PixelProbeProjectionModel {
  let context: PixelProbeImageContext = Object.freeze({ ...initialContext });
  let observation: Observation = Object.freeze({ kind: 'none' });

  function project(): PixelProbeProjection {
    const unavailable = imageUnavailable(context);
    if (unavailable) return unavailable;

    const width = finitePositive(context.width);
    const height = finitePositive(context.height);
    const scale = finiteScale(context.scale);
    if (width === 0 || height === 0) {
      return baseProjection(context, 'unreadable', PIXEL_PROBE_UNREADABLE_MESSAGE);
    }

    if (observation.kind === 'outside') {
      return baseProjection(context, 'outside', PIXEL_PROBE_OUTSIDE_MESSAGE);
    }
    if (observation.kind === 'unreadable') {
      return baseProjection(context, 'unreadable', PIXEL_PROBE_UNREADABLE_MESSAGE);
    }
    if (observation.kind === 'hover') {
      const sample = observation.sample;
      if (!finitePoint(sample)) return baseProjection(context, 'unreadable', PIXEL_PROBE_UNREADABLE_MESSAGE);
      const point = frozenPoint(sample);
      const rgba = frozenRgba(sample.rgba);
      return Object.freeze({
        ...baseProjection(context, 'hover', formatPixelProbeStatus({
          ...point,
          width,
          height,
          rgba,
          scale
        })),
        point,
        rgba
      });
    }
    if (observation.kind === 'locked') {
      const sample = observation.sample;
      if (!finitePoint(sample)) return baseProjection(context, 'unreadable', PIXEL_PROBE_UNREADABLE_MESSAGE);
      const point = frozenPoint(sample);
      const rgba = frozenRgba(sample.rgba);
      const neighborhoods = Object.freeze({ ...(sample.neighborhoods ?? {}) });
      const world = sample.world === undefined
        ? resolvePixelWorldCoordinate(point, context.worldSource)
        : sample.world;
      return Object.freeze({
        ...baseProjection(context, 'locked', formatLockedPixelProbeStatus({
          ...point,
          width,
          height,
          rgba,
          scale,
          neighborhoods,
          world
        })),
        point,
        rgba,
        neighborhoods,
        world
      });
    }
    if (observation.kind === 'roi') {
      const sample = observation.sample;
      if (!validRoi(sample)) return baseProjection(context, 'unreadable', PIXEL_PROBE_UNREADABLE_MESSAGE);
      const roi = frozenRoi(sample.roi);
      const stats = Object.freeze({ ...sample.stats });
      const world = sample.world === undefined
        ? resolvePixelWorldCoordinate({ x: roi.x, y: roi.y }, context.worldSource)
        : sample.world;
      return Object.freeze({
        ...baseProjection(context, 'roi', formatRoiProbeStatus({ roi, stats, world })),
        roi,
        stats,
        world
      });
    }

    return baseProjection(context, 'idle', PIXEL_PROBE_DEFAULT_MESSAGE);
  }

  function readyForObservation(): boolean {
    return imageUnavailable(context) === null &&
      finitePositive(context.width) > 0 &&
      finitePositive(context.height) > 0;
  }

  function copyPointSample(sample: PixelProbePointSample): PixelProbePointSample {
    return Object.freeze({
      x: Number(sample.x),
      y: Number(sample.y),
      rgba: frozenRgba(sample.rgba)
    });
  }

  function copyStatistics(stats: PixelProbeStatistics): PixelProbeStatistics {
    const copy: {
      ok: boolean;
      count?: number;
      gray?: PixelProbeGrayStatistics;
      rgbMean?: PixelProbeRgbMean | null;
    } = { ok: stats.ok };
    if (stats.count !== undefined) copy.count = Number(stats.count);
    if (stats.gray !== undefined) copy.gray = Object.freeze({ ...stats.gray });
    if (stats.rgbMean !== undefined) {
      copy.rgbMean = stats.rgbMean === null ? null : Object.freeze({ ...stats.rgbMean });
    }
    return Object.freeze(copy);
  }

  function copyLockedSample(sample: PixelProbeLockedSample): PixelProbeLockedSample {
    const neighborhoods = Object.freeze(Object.fromEntries(
      Object.entries(sample.neighborhoods ?? {}).map(([size, stats]) => [
        size,
        copyStatistics(stats)
      ])
    )) as Readonly<Record<number, PixelProbeStatistics>>;
    return Object.freeze({
      ...copyPointSample(sample),
      neighborhoods,
      ...(sample.world === undefined
        ? {}
        : { world: sample.world === null ? null : Object.freeze({ ...sample.world }) })
    });
  }

  function copyRoiSample(sample: PixelProbeRoiSample): PixelProbeRoiSample {
    return Object.freeze({
      roi: frozenRoi(sample.roi),
      stats: copyStatistics(sample.stats),
      ...(sample.world === undefined
        ? {}
        : { world: sample.world === null ? null : Object.freeze({ ...sample.world }) })
    });
  }

  return Object.freeze({
    setImageContext(nextContext: PixelProbeImageContext): PixelProbeProjection {
      const identityChanged = nextContext.identity !== context.identity;
      context = Object.freeze({ ...nextContext });
      if (identityChanged || context.status !== 'ready') {
        observation = Object.freeze({ kind: 'none' });
      }
      return project();
    },
    showHover(sample: PixelProbePointSample): PixelProbeProjection {
      if (readyForObservation()) observation = Object.freeze({ kind: 'hover', sample: copyPointSample(sample) });
      return project();
    },
    showOutside(): PixelProbeProjection {
      if (readyForObservation()) observation = Object.freeze({ kind: 'outside' });
      return project();
    },
    showUnreadable(): PixelProbeProjection {
      if (readyForObservation()) observation = Object.freeze({ kind: 'unreadable' });
      return project();
    },
    lockPixel(sample: PixelProbeLockedSample): PixelProbeProjection {
      if (readyForObservation()) observation = Object.freeze({ kind: 'locked', sample: copyLockedSample(sample) });
      return project();
    },
    showRoi(sample: PixelProbeRoiSample): PixelProbeProjection {
      if (readyForObservation()) observation = Object.freeze({ kind: 'roi', sample: copyRoiSample(sample) });
      return project();
    },
    reset(): PixelProbeProjection {
      observation = Object.freeze({ kind: 'none' });
      return project();
    },
    getProjection: project
  });
}
