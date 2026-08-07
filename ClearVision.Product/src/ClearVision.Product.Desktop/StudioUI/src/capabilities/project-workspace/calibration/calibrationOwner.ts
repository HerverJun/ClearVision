import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiConflictError,
  ApiForbiddenError,
  ApiHttpError,
  ApiNetworkError,
  type ApiTransport
} from '@/platform/api';
import type { FlowCanvasOwner } from '../flow';
import type { ImageCanvasClick, ImageCanvasOwner } from '../image/imageCanvasOwner';
import type { InspectorOwner } from '../inspector/inspectorOwner';
import {
  decodeCalibrationAssetSaveResponse,
  decodeNPointCalibrationSolveResponse,
  isCalibrationSampleComplete,
  type CalibrationAssetSaveResponse,
  type CalibrationMode,
  type CalibrationSample,
  type CalibrationSolveResult,
  type CalibrationSolverOptions
} from './calibrationContracts';

export type CalibrationOwnerPhase =
  | 'unavailable'
  | 'ready'
  | 'dirty'
  | 'solving'
  | 'solved'
  | 'saving'
  | 'saved'
  | 'stale'
  | 'readonly'
  | 'error'
  | 'disposed';

export interface CalibrationOwnerProjection {
  readonly phase: CalibrationOwnerPhase;
  readonly projectId: string;
  readonly targetNodeId: string | null;
  readonly imageIdentity: string | null;
  readonly imageGeneration: number;
  readonly mode: CalibrationMode;
  readonly unit: string;
  readonly samples: readonly CalibrationSample[];
  readonly solverOptions: CalibrationSolverOptions;
  readonly lastSolveResult: CalibrationSolveResult | null;
  readonly candidateBundle: Readonly<Record<string, unknown>> | null;
  readonly candidateBundleJson: string | null;
  readonly formalAssetId: string | null;
  readonly formalAssetRevision: number | null;
  readonly formalAssetHash: string | null;
  readonly dirty: boolean;
  readonly captureArmed: boolean;
  readonly message: string;
  readonly diagnostics: readonly string[];
  readonly canCapture: boolean;
  readonly canSolve: boolean;
  readonly canSave: boolean;
}

type MutableProjection = { -readonly [Key in keyof CalibrationOwnerProjection]: CalibrationOwnerProjection[Key] };

type CalibrationSampleInput = Readonly<Partial<CalibrationSample> & { pixelX: number; pixelY: number }>;
type CalibrationSamplePatch = Readonly<Partial<CalibrationSample>>;

export interface CalibrationOwner {
  readonly projection: DeepReadonly<CalibrationOwnerProjection>;
  toggleCapture(): void;
  addSample(input: CalibrationSampleInput): void;
  updateSample(sampleId: string, patch: CalibrationSamplePatch): void;
  removeSample(sampleId: string): void;
  toggleSample(sampleId: string): void;
  reset(): void;
  solve(): Promise<void>;
  save(): Promise<void>;
  dispose(reason?: string): void;
}

const defaultSolverOptions: CalibrationSolverOptions = Object.freeze({
  ransacReprojectionThreshold: 3,
  ransacMaxIterations: 3000,
  ransacConfidence: 0.995,
  maxAcceptedReprojectionError: 3,
  minInlierCount: 0,
  minInlierRatio: 0.5
});

function record(value: unknown): Readonly<Record<string, unknown>> {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : value === null || value === undefined ? '' : String(value);
}

function number(value: unknown, fallback: number | null = null): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function nodeId(flowOwner: FlowCanvasOwner): string | null {
  return flowOwner.projection.runtime?.selectedNodeId ?? null;
}

function selectedNode(flowOwner: FlowCanvasOwner): Readonly<Record<string, unknown>> | null {
  const selected = nodeId(flowOwner);
  if (!selected) return null;
  return flowOwner.projection.draft.operators.find(operator => text(operator.id ?? operator.Id) === selected) ?? null;
}

function parameterValue(node: Readonly<Record<string, unknown>>, name: string): unknown {
  const parameters = Array.isArray(node.parameters ?? node.Parameters) ? node.parameters ?? node.Parameters : [];
  const parameter = (parameters as readonly unknown[]).map(record).find(item =>
    text(item.name ?? item.Name).toLocaleLowerCase() === name.toLocaleLowerCase());
  return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue;
}

function isNPointNode(node: Readonly<Record<string, unknown>> | null): boolean {
  return text(node?.type ?? node?.Type).toLocaleLowerCase() === 'npointcalibration';
}

function newSample(order: number, input: CalibrationSampleInput): CalibrationSample {
  const pixelX = number(input.pixelX);
  const pixelY = number(input.pixelY);
  const worldX = number(input.worldX);
  const worldY = number(input.worldY);
  const enabled = typeof input.enabled === 'boolean' ? input.enabled : true;
  const sample = {
    sampleId: input.sampleId?.trim() || `sample-${globalThis.crypto.randomUUID()}`,
    order,
    pixelX,
    pixelY,
    worldX,
    worldY,
    source: input.source?.trim() || 'ManualClick',
    enabled,
    valid: isCalibrationSampleComplete({ pixelX, pixelY, worldX, worldY }),
    inlier: null,
    reprojectionX: null,
    reprojectionY: null,
    error: null,
    note: input.note?.trim() || '',
    createdAtUtc: input.createdAtUtc?.trim() || new Date().toISOString()
  } satisfies CalibrationSample;
  return Object.freeze(sample);
}

function parseExistingSamples(node: Readonly<Record<string, unknown>> | null): readonly CalibrationSample[] {
  const raw = parameterValue(node ?? Object.freeze({}), 'PointPairs');
  if (typeof raw !== 'string' || !raw.trim()) return Object.freeze([]);
  try {
    const parsed: unknown = JSON.parse(raw);
    const source: Readonly<Record<string, unknown>> = parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? record(parsed)
      : Object.freeze({});
    const points = Array.isArray(parsed)
      ? parsed
      : Array.isArray(source.points ?? source.Points)
        ? source.points ?? source.Points
        : [];
    return Object.freeze((points as readonly unknown[]).map((item, index) => {
      const point = record(item);
      const enabledValue = point.enabled ?? point.Enabled;
      return newSample(index + 1, {
        sampleId: text(point.sampleId ?? point.SampleId),
        pixelX: number(point.x ?? point.X ?? point.pixelX ?? point.PixelX ?? point.imageX ?? point.ImageX) ?? 0,
        pixelY: number(point.y ?? point.Y ?? point.pixelY ?? point.PixelY ?? point.imageY ?? point.ImageY) ?? 0,
        worldX: number(point.worldX ?? point.WorldX),
        worldY: number(point.worldY ?? point.WorldY),
        enabled: typeof enabledValue === 'boolean'
          ? enabledValue
          : true,
        source: text(point.source ?? point.Source) || 'Imported',
        note: text(point.note ?? point.Note)
      });
    }));
  } catch {
    return Object.freeze([]);
  }
}

function identityKey(flowOwner: FlowCanvasOwner, imageOwner: ImageCanvasOwner): string {
  return [
    nodeId(flowOwner) ?? '',
    flowOwner.projection.runtime?.selectionRevision ?? 0,
    imageOwner.projection.imageIdentity ?? '',
    imageOwner.projection.imageGeneration
  ].join(':');
}

function errorMessage(error: unknown, action: 'solve' | 'save' = 'save'): string {
  if (error instanceof ApiConflictError) return '工程 revision 已变化；请先重新读取工程后再保存标定资产。';
  if (error instanceof ApiForbiddenError) {
    return action === 'solve'
      ? '当前账户没有执行标定计算的权限。'
      : '当前账户没有保存标定资产的权限。';
  }
  if (error instanceof ApiNetworkError || error instanceof ApiAbortError) return '标定请求结果未知；请重新读取工程确认资产状态。';
  if (error instanceof ApiHttpError) return error.message;
  return error instanceof Error ? error.message : '标定请求失败。';
}

function samplePayload(sample: CalibrationSample): Readonly<Record<string, unknown>> {
  return Object.freeze({
    sampleId: sample.sampleId,
    order: sample.order,
    pixelX: sample.pixelX,
    pixelY: sample.pixelY,
    worldX: sample.worldX,
    worldY: sample.worldY,
    source: sample.source,
    enabled: sample.enabled,
    note: sample.note,
    createdAtUtc: sample.createdAtUtc
  });
}

export function createCalibrationOwner(options: {
  readonly projectId: string;
  readonly flowOwner: FlowCanvasOwner;
  readonly inspectorOwner: InspectorOwner;
  readonly imageOwner: ImageCanvasOwner;
  readonly api: ApiTransport;
  readonly getPersistenceRevision: () => number | null;
  readonly reconcileAfterSave: () => Promise<boolean>;
}): CalibrationOwner {
  const post = options.api.post;
  if (typeof post !== 'function') throw new TypeError('Calibration owner requires POST on the shared ApiTransport.');
  const state = reactive<MutableProjection>({
    phase: 'unavailable',
    projectId: options.projectId,
    targetNodeId: null,
    imageIdentity: null,
    imageGeneration: 0,
    mode: 'Affine',
    unit: 'mm',
    samples: Object.freeze([]),
    solverOptions: defaultSolverOptions,
    lastSolveResult: null,
    candidateBundle: null,
    candidateBundleJson: null,
    formalAssetId: null,
    formalAssetRevision: null,
    formalAssetHash: null,
    dirty: false,
    captureArmed: false,
    message: '请选择 N 点标定节点，并先完成节点预览。',
    diagnostics: Object.freeze([]),
    canCapture: false,
    canSolve: false,
    canSave: false
  });
  let disposed = false;
  let contextKey = '';
  let solveSequence = 0;
  let solveAbort: AbortController | undefined;
  let saveSequence = 0;
  let saveAbort: AbortController | undefined;
  let saving = false;
  let draftSessionId = '';

  function currentNode(): Readonly<Record<string, unknown>> | null {
    return selectedNode(options.flowOwner);
  }

  function editable(): boolean {
    return options.flowOwner.projection.mutationGate === 'editable';
  }

  function clearCandidate(): void {
    state.lastSolveResult = null;
    state.candidateBundle = null;
    state.candidateBundleJson = null;
    state.formalAssetId = null;
    state.formalAssetRevision = null;
    state.formalAssetHash = null;
  }

  function syncAvailability(): void {
    const currentImage = options.imageOwner.projection;
    const imageMatches = state.imageIdentity === currentImage.imageIdentity &&
      state.imageGeneration === currentImage.imageGeneration;
    const imageReady = currentImage.phase === 'ready' && Boolean(state.imageIdentity) && imageMatches;
    const selected = Boolean(state.targetNodeId) && state.targetNodeId === nodeId(options.flowOwner);
    const usableDraft = state.phase !== 'stale' && state.phase !== 'unavailable' && state.phase !== 'readonly' &&
      state.phase !== 'disposed' && !saving && state.phase !== 'solving' && state.phase !== 'saving';
    state.canCapture = !disposed && selected && imageReady && editable() && usableDraft;
    const requiredSampleCount = state.mode === 'Perspective' ? 4 : 3;
    state.canSolve = !disposed && selected && imageReady && editable() && usableDraft &&
      state.samples.filter(sample => sample.enabled && isCalibrationSampleComplete(sample)).length >= requiredSampleCount;
    state.canSave = !disposed && selected && imageReady && editable() && !saving &&
      state.phase !== 'stale' &&
      state.candidateBundleJson !== null && state.lastSolveResult?.accepted === true &&
      options.getPersistenceRevision() !== null;
  }

  function resetToSelection(reason = '已按当前节点读取标定样本。'): void {
    const node = currentNode();
    const selected = nodeId(options.flowOwner);
    if (!isNPointNode(node) || !selected) {
      if (contextKey && state.dirty) {
        state.captureArmed = false;
        state.phase = 'stale';
        state.message = '图像或选择已变化，当前标定草稿已失效；请重新读取样本。';
        clearCandidate();
        syncAvailability();
        return;
      }
      contextKey = '';
      state.targetNodeId = null;
      state.imageIdentity = null;
      state.imageGeneration = 0;
      state.samples = Object.freeze([]);
      clearCandidate();
      state.dirty = false;
      state.captureArmed = false;
      state.phase = 'unavailable';
      state.message = '请选择 N 点标定节点。';
      syncAvailability();
      return;
    }

    const nextKey = identityKey(options.flowOwner, options.imageOwner);
    const contextChanged = nextKey !== contextKey;
    if (contextChanged && contextKey && state.dirty) {
      state.captureArmed = false;
      state.phase = 'stale';
      state.message = '图像或选择已变化，当前标定草稿已失效；请重新读取样本。';
      clearCandidate();
      syncAvailability();
      return;
    }
    if (contextChanged) {
      contextKey = nextKey;
      draftSessionId = `calibration-draft-${globalThis.crypto.randomUUID()}`;
      state.targetNodeId = selected;
      state.imageIdentity = options.imageOwner.projection.imageIdentity;
      state.imageGeneration = options.imageOwner.projection.imageGeneration;
      const selectedNodeRecord = node ?? Object.freeze({});
      const rawMode = text(parameterValue(selectedNodeRecord, 'CalibrationMode')).toLocaleLowerCase();
      const mode = rawMode === 'perspective'
        ? 'Perspective'
        : rawMode === 'scaleoffset' || rawMode === 'planarscaleoffset' || rawMode === 'planar'
          ? 'ScaleOffset'
          : 'Affine';
      state.mode = mode;
      state.unit = text(parameterValue(selectedNodeRecord, 'CalibrationUnit')) || 'mm';
      state.samples = parseExistingSamples(node);
      state.solverOptions = defaultSolverOptions;
      clearCandidate();
      state.dirty = false;
      state.captureArmed = false;
    }

    if (options.imageOwner.projection.phase !== 'ready' || !state.imageIdentity) {
      state.phase = 'unavailable';
      state.message = '请先完成当前 N 点标定节点预览，加载输入图像后再采集。';
    } else if (!editable()) {
      state.phase = 'readonly';
      state.message = options.flowOwner.projection.mutationGate === 'running' ? '流程正在运行，标定仅可查看。' : '当前工程只读，标定仅可查看。';
    } else if (state.phase === 'unavailable' || state.phase === 'readonly' || state.phase === 'stale') {
      state.phase = state.dirty ? 'dirty' : 'ready';
      state.message = reason;
    }
    syncAvailability();
  }

  function markDirty(message = '样本草稿已修改，请重新计算。'): void {
    if (disposed) return;
    clearCandidate();
    state.dirty = true;
    state.phase = editable() ? 'dirty' : 'readonly';
    state.message = message;
    state.captureArmed = false;
    syncAvailability();
  }

  function snapshot(): Readonly<{ key: string; nodeId: string; imageIdentity: string; imageGeneration: number }> | null {
    const selected = nodeId(options.flowOwner);
    const imageIdentity = options.imageOwner.projection.imageIdentity;
    if (!selected || !imageIdentity || !isNPointNode(currentNode()) || options.imageOwner.projection.phase !== 'ready') return null;
    return Object.freeze({
      key: identityKey(options.flowOwner, options.imageOwner),
      nodeId: selected,
      imageIdentity,
      imageGeneration: options.imageOwner.projection.imageGeneration
    });
  }

  function isCurrent(identity: Readonly<{ key: string; nodeId: string; imageIdentity: string; imageGeneration: number }>): boolean {
    return !disposed && identity.key === identityKey(options.flowOwner, options.imageOwner) &&
      identity.nodeId === nodeId(options.flowOwner) && identity.imageIdentity === options.imageOwner.projection.imageIdentity &&
      identity.imageGeneration === options.imageOwner.projection.imageGeneration;
  }

  function handleImageClick(click: ImageCanvasClick): void {
    if (!state.captureArmed || !state.canCapture || click.imageIdentity !== state.imageIdentity ||
        click.imageGeneration !== state.imageGeneration) return;
    owner.addSample({ pixelX: click.x, pixelY: click.y, source: 'ManualClick' });
    state.captureArmed = false;
    syncAvailability();
  }

  const stopSelectionWatch = watch(
    () => [
      nodeId(options.flowOwner),
      options.flowOwner.projection.runtime?.selectionRevision ?? 0,
      options.flowOwner.projection.runtime?.flowRevision ?? 0,
      options.flowOwner.projection.mutationGate,
      options.imageOwner.projection.imageIdentity,
      options.imageOwner.projection.imageGeneration,
      options.imageOwner.projection.phase
    ] as const,
    () => {
      if (disposed) return;
      const previousKey = contextKey;
      const nextKey = identityKey(options.flowOwner, options.imageOwner);
      if (previousKey && nextKey !== previousKey && state.targetNodeId && state.dirty &&
          nodeId(options.flowOwner) === state.targetNodeId) {
        state.captureArmed = false;
        state.phase = 'stale';
        state.message = '图像或选择已变化，当前标定草稿已失效；请重新读取样本。';
        clearCandidate();
      }
      resetToSelection(previousKey === nextKey ? state.message : undefined);
    },
    { immediate: true }
  );
  const unsubscribeImageClick = options.imageOwner.subscribeImageClick(handleImageClick);

  const owner: CalibrationOwner = Object.freeze({
    projection: readonly(state),
    toggleCapture(): void {
      if (disposed || !state.canCapture) return;
      state.captureArmed = !state.captureArmed;
      state.message = state.captureArmed ? '请在右侧图像上点击采集像素点。' : '已停止采集像素点。';
      syncAvailability();
    },
    addSample(input: CalibrationSampleInput): void {
      if (disposed || !state.targetNodeId || !state.canCapture || !Number.isFinite(input.pixelX) || !Number.isFinite(input.pixelY)) return;
      const next = newSample(state.samples.length + 1, input);
      state.samples = Object.freeze([...state.samples, next]);
      markDirty('已新增像素点，请补充 World 坐标并重新计算。');
    },
    updateSample(sampleId: string, patch: CalibrationSamplePatch): void {
      const index = state.samples.findIndex(sample => sample.sampleId === sampleId);
      if (disposed || index < 0 || !editable()) return;
      const previous = state.samples[index]!;
      const next = newSample(previous.order, {
        ...previous,
        ...patch,
        pixelX: number(patch.pixelX ?? previous.pixelX) ?? 0,
        pixelY: number(patch.pixelY ?? previous.pixelY) ?? 0
      });
      state.samples = Object.freeze(state.samples.map((sample, itemIndex) => itemIndex === index ? next : sample));
      markDirty('样本草稿已修改，请重新计算。');
    },
    removeSample(sampleId: string): void {
      if (disposed || !editable()) return;
      const next = state.samples.filter(sample => sample.sampleId !== sampleId)
        .map((sample, index) => Object.freeze({ ...sample, order: index + 1 }));
      if (next.length === state.samples.length) return;
      state.samples = Object.freeze(next);
      markDirty('已删除样本，请重新计算。');
    },
    toggleSample(sampleId: string): void {
      const sample = state.samples.find(item => item.sampleId === sampleId);
      if (!sample || disposed || !editable()) return;
      owner.updateSample(sampleId, { enabled: !sample.enabled });
    },
    reset(): void {
      if (disposed) return;
      contextKey = '';
      resetToSelection('已放弃标定草稿并重新读取当前节点。');
    },
    async solve(): Promise<void> {
      if (disposed || !state.canSolve) return;
      const identity = snapshot();
      if (!identity) return;
      solveAbort?.abort('superseded');
      const controller = new AbortController();
      solveAbort = controller;
      const sequence = ++solveSequence;
      state.phase = 'solving';
      state.message = '正在请求服务端拟合与残差分析…';
      state.diagnostics = Object.freeze([]);
      syncAvailability();
      try {
        const response = await post<unknown>('calibration/npoint-draft/solve', {
          sessionId: draftSessionId,
          projectId: options.projectId,
          targetNodeId: identity.nodeId,
          flowRevision: options.flowOwner.projection.runtime?.flowRevision ?? null,
          imageIdentity: identity.imageIdentity,
          mode: state.mode,
          unit: state.unit,
          solverOptions: state.solverOptions,
          samples: state.samples.map(samplePayload)
        }, { signal: controller.signal });
        if (sequence !== solveSequence || !isCurrent(identity)) return;
        const decoded = decodeNPointCalibrationSolveResponse(response);
        state.samples = decoded.samples;
        state.lastSolveResult = decoded.lastSolveResult;
        state.candidateBundle = decoded.candidateBundle;
        state.candidateBundleJson = decoded.candidateBundleJson;
        state.diagnostics = decoded.diagnostics;
        state.dirty = !decoded.success;
        state.phase = decoded.success ? 'solved' : 'error';
        state.message = decoded.success
          ? decoded.lastSolveResult?.accepted === true ? '拟合完成，候选标定包已准备好正式保存。' : '拟合完成，但质量门禁未接受该候选。'
          : decoded.errorMessage ?? '服务端未接受当前样本。';
      } catch (error) {
        if (sequence !== solveSequence || disposed || error instanceof ApiAbortError) return;
        state.phase = 'error';
        state.dirty = true;
        state.message = errorMessage(error, 'solve');
        state.diagnostics = Object.freeze([state.message]);
      } finally {
        if (solveAbort === controller) solveAbort = undefined;
        syncAvailability();
      }
    },
    async save(): Promise<void> {
      if (disposed || !state.canSave || saving) return;
      const identity = snapshot();
      const expectedPersistenceRevision = options.getPersistenceRevision();
      if (!identity || expectedPersistenceRevision === null || !state.candidateBundleJson) return;
      saving = true;
      state.phase = 'saving';
      state.message = '正在通过 Project asset 保存链保存标定候选…';
      syncAvailability();
      const controller = new AbortController();
      saveAbort?.abort('superseded');
      saveAbort = controller;
      const saveRequestSequence = ++saveSequence;
      try {
        const response = await post<unknown>(
          `projects/${encodeURIComponent(options.projectId)}/calibration-assets/from-draft`,
          {
            expectedPersistenceRevision,
            sessionId: draftSessionId,
            targetNodeId: identity.nodeId,
            imageIdentity: identity.imageIdentity,
            candidateBundleJson: state.candidateBundleJson
          },
          { signal: controller.signal }
        );
        if (saveRequestSequence !== saveSequence || !isCurrent(identity)) return;
        const decoded: CalibrationAssetSaveResponse = decodeCalibrationAssetSaveResponse(response);
        state.formalAssetId = decoded.assetId;
        state.formalAssetRevision = decoded.persistenceRevision;
        state.formalAssetHash = decoded.contentHash || decoded.assetsHash || null;
        state.dirty = false;
        const reconciled = await options.reconcileAfterSave();
        if (saveRequestSequence !== saveSequence || !isCurrent(identity)) return;
        if (!reconciled) {
          state.phase = 'error';
          state.message = '标定资产已返回保存结果，但工程 revision reconcile 未完成；请先核对保存结果。';
          return;
        }
        state.phase = 'saved';
        state.message = `标定资产 ${decoded.assetId} 已保存，工程 revision ${decoded.persistenceRevision}。`;
      } catch (error) {
        if (saveRequestSequence !== saveSequence || disposed || error instanceof ApiAbortError) return;
        state.phase = 'error';
        state.message = errorMessage(error);
        state.diagnostics = Object.freeze([state.message]);
      } finally {
        if (saveAbort === controller) {
          saveAbort = undefined;
          saving = false;
          syncAvailability();
        }
      }
    },
    dispose(reason = 'calibration-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      solveSequence += 1;
      solveAbort?.abort(reason);
      solveAbort = undefined;
      saveSequence += 1;
      saveAbort?.abort(reason);
      saveAbort = undefined;
      saving = false;
      stopSelectionWatch();
      unsubscribeImageClick();
      state.phase = 'disposed';
      state.canCapture = false;
      state.canSolve = false;
      state.canSave = false;
      state.captureArmed = false;
      state.message = '标定工作台已关闭。';
    }
  });

  return owner;
}
