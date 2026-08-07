export type CalibrationMode = 'Affine' | 'Perspective' | 'ScaleOffset';

export interface CalibrationSample {
  readonly sampleId: string;
  readonly order: number;
  readonly pixelX: number | null;
  readonly pixelY: number | null;
  readonly worldX: number | null;
  readonly worldY: number | null;
  readonly source: string;
  readonly enabled: boolean;
  readonly valid: boolean;
  readonly inlier: boolean | null;
  readonly reprojectionX: number | null;
  readonly reprojectionY: number | null;
  readonly error: number | null;
  readonly note: string;
  readonly createdAtUtc: string;
}

export interface CalibrationSolverOptions {
  readonly ransacReprojectionThreshold: number;
  readonly ransacMaxIterations: number;
  readonly ransacConfidence: number;
  readonly maxAcceptedReprojectionError: number;
  readonly minInlierCount: number;
  readonly minInlierRatio: number;
}

export interface CalibrationSolveResult {
  readonly success: boolean;
  readonly transformModel: string | null;
  readonly matrix: readonly (readonly number[])[];
  readonly meanError: number | null;
  readonly maxError: number | null;
  readonly inlierMeanError: number | null;
  readonly inlierMaxError: number | null;
  readonly allSampleMeanError: number | null;
  readonly allSampleMaxError: number | null;
  readonly inlierCount: number | null;
  readonly totalSampleCount: number | null;
  readonly inlierRatio: number | null;
  readonly accepted: boolean;
  readonly diagnostics: readonly string[];
}

export interface NPointCalibrationSolveResponse {
  readonly schemaVersion: string;
  readonly sessionId: string;
  readonly projectId: string;
  readonly targetNodeId: string;
  readonly imageIdentity: string;
  readonly mode: CalibrationMode;
  readonly unit: string;
  readonly status: string;
  readonly success: boolean;
  readonly errorMessage: string | null;
  readonly samples: readonly CalibrationSample[];
  readonly lastSolveResult: CalibrationSolveResult | null;
  readonly candidateBundle: Readonly<Record<string, unknown>> | null;
  readonly candidateBundleJson: string | null;
  readonly diagnostics: readonly string[];
}

export interface CalibrationAssetSaveResponse {
  readonly schemaVersion: string;
  readonly projectId: string;
  readonly persistenceRevision: number;
  readonly assetsHash: string;
  readonly assetId: string;
  readonly contentHash: string;
  readonly projectRevision: number;
}

export class CalibrationContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, message: string) {
    super(`${path}: ${message}`);
    this.name = 'CalibrationContractDecodeError';
    this.path = path;
  }
}

function record(value: unknown, path: string): Readonly<Record<string, unknown>> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new CalibrationContractDecodeError(path, 'expected an object');
  }
  return value as Readonly<Record<string, unknown>>;
}

function value(source: Readonly<Record<string, unknown>>, ...names: string[]): unknown {
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(source, name)) return source[name];
  }
  return undefined;
}

function required(source: Readonly<Record<string, unknown>>, path: string, ...names: string[]): unknown {
  const result = value(source, ...names);
  if (result === undefined) throw new CalibrationContractDecodeError(path, `missing ${names[0]}`);
  return result;
}

function text(input: unknown, path: string, allowEmpty = false): string {
  if (typeof input !== 'string' || (!allowEmpty && !input.trim())) {
    throw new CalibrationContractDecodeError(path, 'expected a non-empty string');
  }
  return input;
}

function optionalText(input: unknown, path: string): string | null {
  if (input === null || input === undefined || input === '') return null;
  return text(input, path, true);
}

function booleanValue(input: unknown, path: string): boolean {
  if (typeof input !== 'boolean') throw new CalibrationContractDecodeError(path, 'expected a boolean');
  return input;
}

function numberValue(input: unknown, path: string, fallback: number | null = null): number | null {
  if (input === null || input === undefined || input === '') return fallback;
  if (typeof input !== 'number' || !Number.isFinite(input)) {
    throw new CalibrationContractDecodeError(path, 'expected a finite number');
  }
  return input;
}

function integerValue(input: unknown, path: string, fallback = 0): number {
  if (input === null || input === undefined || input === '') return fallback;
  if (typeof input !== 'number' || !Number.isSafeInteger(input) || input < 0) {
    throw new CalibrationContractDecodeError(path, 'expected a non-negative integer');
  }
  return input;
}

function stringArray(input: unknown, path: string): readonly string[] {
  if (!Array.isArray(input)) throw new CalibrationContractDecodeError(path, 'expected an array');
  return Object.freeze(input.map((item, index) => text(item, `${path}[${index}]`, true)));
}

function nullableBoolean(input: unknown, path: string): boolean | null {
  if (input === null || input === undefined) return null;
  return booleanValue(input, path);
}

function decodeSample(input: unknown, path: string, index: number): CalibrationSample {
  const source = record(input, path);
  const pixelX = numberValue(value(source, 'pixelX', 'PixelX'), `${path}.pixelX`);
  const pixelY = numberValue(value(source, 'pixelY', 'PixelY'), `${path}.pixelY`);
  const worldX = numberValue(value(source, 'worldX', 'WorldX'), `${path}.worldX`);
  const worldY = numberValue(value(source, 'worldY', 'WorldY'), `${path}.worldY`);
  const valid = value(source, 'valid', 'Valid') === undefined
    ? pixelX !== null && pixelY !== null && worldX !== null && worldY !== null
    : booleanValue(value(source, 'valid', 'Valid'), `${path}.valid`);
  return Object.freeze({
    sampleId: text(value(source, 'sampleId', 'SampleId') ?? `sample-${index + 1}`, `${path}.sampleId`),
    order: integerValue(value(source, 'order', 'Order'), `${path}.order`, index + 1),
    pixelX,
    pixelY,
    worldX,
    worldY,
    source: text(value(source, 'source', 'Source') ?? 'ManualClick', `${path}.source`, true),
    enabled: value(source, 'enabled', 'Enabled') === undefined
      ? true
      : booleanValue(value(source, 'enabled', 'Enabled'), `${path}.enabled`),
    valid,
    inlier: nullableBoolean(value(source, 'inlier', 'Inlier'), `${path}.inlier`),
    reprojectionX: numberValue(value(source, 'reprojectionX', 'ReprojectionX'), `${path}.reprojectionX`),
    reprojectionY: numberValue(value(source, 'reprojectionY', 'ReprojectionY'), `${path}.reprojectionY`),
    error: numberValue(value(source, 'error', 'Error'), `${path}.error`),
    note: text(value(source, 'note', 'Note') ?? '', `${path}.note`, true),
    createdAtUtc: text(value(source, 'createdAtUtc', 'CreatedAtUtc') ?? new Date(0).toISOString(), `${path}.createdAtUtc`, true)
  });
}

function decodeSolveResult(input: unknown, path: string): CalibrationSolveResult {
  const source = record(input, path);
  const matrixValue = value(source, 'matrix', 'Matrix');
  const matrix = Array.isArray(matrixValue)
    ? Object.freeze(matrixValue.map((row, rowIndex) => {
      if (!Array.isArray(row)) throw new CalibrationContractDecodeError(`${path}.matrix[${rowIndex}]`, 'expected an array');
      return Object.freeze(row.map((cell, cellIndex) => numberValue(cell, `${path}.matrix[${rowIndex}][${cellIndex}]`, 0) ?? 0));
    }))
    : Object.freeze([]);
  return Object.freeze({
    success: value(source, 'success', 'Success') === undefined
      ? true
      : booleanValue(value(source, 'success', 'Success'), `${path}.success`),
    transformModel: optionalText(value(source, 'transformModel', 'TransformModel'), `${path}.transformModel`),
    matrix,
    meanError: numberValue(value(source, 'meanError', 'MeanError'), `${path}.meanError`),
    maxError: numberValue(value(source, 'maxError', 'MaxError'), `${path}.maxError`),
    inlierMeanError: numberValue(value(source, 'inlierMeanError', 'InlierMeanError'), `${path}.inlierMeanError`),
    inlierMaxError: numberValue(value(source, 'inlierMaxError', 'InlierMaxError'), `${path}.inlierMaxError`),
    allSampleMeanError: numberValue(value(source, 'allSampleMeanError', 'AllSampleMeanError'), `${path}.allSampleMeanError`),
    allSampleMaxError: numberValue(value(source, 'allSampleMaxError', 'AllSampleMaxError'), `${path}.allSampleMaxError`),
    inlierCount: integerValue(value(source, 'inlierCount', 'InlierCount'), `${path}.inlierCount`, 0),
    totalSampleCount: integerValue(value(source, 'totalSampleCount', 'TotalSampleCount'), `${path}.totalSampleCount`, 0),
    inlierRatio: numberValue(value(source, 'inlierRatio', 'InlierRatio'), `${path}.inlierRatio`),
    accepted: value(source, 'accepted', 'Accepted') === undefined
      ? false
      : booleanValue(value(source, 'accepted', 'Accepted'), `${path}.accepted`),
    diagnostics: value(source, 'diagnostics', 'Diagnostics') === undefined
      ? Object.freeze([])
      : stringArray(value(source, 'diagnostics', 'Diagnostics'), `${path}.diagnostics`)
  });
}

export function decodeNPointCalibrationSolveResponse(input: unknown): NPointCalibrationSolveResponse {
  const source = record(input, '$');
  const samplesValue = required(source, '$', 'samples', 'Samples');
  if (!Array.isArray(samplesValue)) throw new CalibrationContractDecodeError('$.samples', 'expected an array');
  const candidateValue = value(source, 'candidateBundle', 'CandidateBundle');
  const candidateBundle = candidateValue === null || candidateValue === undefined
    ? null
    : record(candidateValue, '$.candidateBundle');
  const candidateBundleJson = optionalText(
    value(source, 'candidateBundleJson', 'CandidateBundleJson'),
    '$.candidateBundleJson'
  );
  const success = booleanValue(required(source, '$', 'success', 'Success'), '$.success');
  if (success && (!candidateBundle || !candidateBundleJson)) {
    throw new CalibrationContractDecodeError('$', 'a successful solve requires a candidate bundle');
  }
  return Object.freeze({
    schemaVersion: text(value(source, 'schemaVersion', 'SchemaVersion') ?? 'calibration-draft-session.v1', '$.schemaVersion'),
    sessionId: text(required(source, '$', 'sessionId', 'SessionId'), '$.sessionId'),
    projectId: text(required(source, '$', 'projectId', 'ProjectId'), '$.projectId'),
    targetNodeId: text(required(source, '$', 'targetNodeId', 'TargetNodeId'), '$.targetNodeId'),
    imageIdentity: text(value(source, 'imageIdentity', 'ImageIdentity') ?? '', '$.imageIdentity', true),
    mode: (() => {
      const normalized = String(value(source, 'mode', 'Mode') ?? 'Affine').toLowerCase();
      return normalized === 'perspective'
        ? 'Perspective'
        : normalized === 'scaleoffset' || normalized === 'planarscaleoffset' || normalized === 'planar'
          ? 'ScaleOffset'
          : 'Affine';
    })(),
    unit: text(value(source, 'unit', 'Unit') ?? 'mm', '$.unit'),
    status: text(value(source, 'status', 'Status') ?? (success ? 'Solved' : 'Failed'), '$.status'),
    success,
    errorMessage: optionalText(value(source, 'errorMessage', 'ErrorMessage'), '$.errorMessage'),
    samples: Object.freeze(samplesValue.map((item, index) => decodeSample(item, `$.samples[${index}]`, index))),
    lastSolveResult: value(source, 'lastSolveResult', 'LastSolveResult') == null
      ? null
      : decodeSolveResult(value(source, 'lastSolveResult', 'LastSolveResult'), '$.lastSolveResult'),
    candidateBundle,
    candidateBundleJson,
    diagnostics: value(source, 'diagnostics', 'Diagnostics') === undefined
      ? Object.freeze([])
      : stringArray(value(source, 'diagnostics', 'Diagnostics'), '$.diagnostics')
  });
}

export function decodeCalibrationAssetSaveResponse(input: unknown): CalibrationAssetSaveResponse {
  const source = record(input, '$');
  const asset = record(required(source, '$', 'asset', 'Asset'), '$.asset');
  const persistenceRevision = integerValue(
    required(source, '$', 'persistenceRevision', 'PersistenceRevision'),
    '$.persistenceRevision'
  );
  return Object.freeze({
    schemaVersion: text(value(source, 'schemaVersion', 'SchemaVersion') ?? 'project-calibration-asset-save.v1', '$.schemaVersion'),
    projectId: text(required(source, '$', 'projectId', 'ProjectId'), '$.projectId'),
    persistenceRevision,
    assetsHash: text(value(source, 'assetsHash', 'AssetsHash') ?? '', '$.assetsHash', true),
    assetId: text(required(asset, '$.asset', 'assetId', 'AssetId'), '$.asset.assetId'),
    contentHash: text(value(asset, 'contentHash', 'ContentHash') ?? '', '$.asset.contentHash', true),
    projectRevision: integerValue(value(asset, 'projectRevision', 'ProjectRevision'), '$.asset.projectRevision', persistenceRevision)
  });
}

export function isCalibrationSampleComplete(sample: Pick<CalibrationSample, 'pixelX' | 'pixelY' | 'worldX' | 'worldY'>): boolean {
  return [sample.pixelX, sample.pixelY, sample.worldX, sample.worldY].every(value =>
    typeof value === 'number' && Number.isFinite(value));
}
