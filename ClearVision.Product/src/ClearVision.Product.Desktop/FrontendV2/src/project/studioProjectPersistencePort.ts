import type { LegacyHttpClient } from '@/adapters/legacyModules';
import type {
  FlowEditorCommandResult,
  StudioFlowEditorPort,
  StudioFlowEditorSnapshot
} from '@/flowEditor/studioFlowEditorPort';

export type StudioProjectPersistenceDisposition =
  | 'idle'
  | 'accepted'
  | 'stale_request'
  | 'stale_persistence_revision'
  | 'runtime_busy'
  | 'validation_error'
  | 'network_error'
  | 'cancelled'
  | 'in_flight'
  | 'project_mismatch'
  | 'disposed';

export type StudioProjectPersistenceStatus =
  | 'empty'
  | 'loading'
  | 'loaded'
  | 'saving'
  | 'error'
  | 'disposed';

export interface StudioProjectDto {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly persistenceRevision: number;
  readonly flow: unknown;
  readonly globalVariables: unknown;
  readonly raw: unknown;
}

export interface StudioProjectPersistenceSnapshot {
  readonly projectId: string | null;
  readonly project: StudioProjectDto | null;
  readonly name: string;
  readonly description: string | null;
  readonly persistenceRevision: number | null;
  readonly globalVariables: unknown;
  readonly status: StudioProjectPersistenceStatus;
  readonly loaded: boolean;
  readonly saving: boolean;
  readonly dirty: boolean;
  readonly error: string;
  readonly lastDisposition: StudioProjectPersistenceDisposition;
}

export interface StudioProjectPersistenceResult {
  readonly accepted: boolean;
  readonly disposition: StudioProjectPersistenceDisposition;
  readonly snapshot: StudioProjectPersistenceSnapshot;
  readonly httpStatus?: number;
  readonly errorCode?: string;
}

export type StudioProjectPersistenceListener = (snapshot: StudioProjectPersistenceSnapshot) => void;

export interface StudioProjectPersistencePort {
  openProject(projectId: string): Promise<StudioProjectPersistenceResult>;
  getSnapshot(): StudioProjectPersistenceSnapshot;
  save(): Promise<StudioProjectPersistenceResult>;
  subscribe(listener: StudioProjectPersistenceListener): () => void;
  dispose(): void;
}

export function createStudioProjectPersistencePort(
  httpClient: LegacyHttpClient,
  flowEditorPort: StudioFlowEditorPort
): StudioProjectPersistencePort {
  return new StudioProjectPersistencePortAdapter(httpClient, flowEditorPort);
}

class StudioProjectPersistencePortAdapter implements StudioProjectPersistencePort {
  private snapshot: StudioProjectPersistenceSnapshot = createEmptySnapshot();
  private savedFlowRevision: number | null = null;
  private disposed = false;
  private readonly listeners = new Set<StudioProjectPersistenceListener>();
  private readonly openControllers = new Set<AbortController>();
  private readonly saveControllersByProject = new Map<string, AbortController>();
  private readonly unsubscribeFlowStructure: () => void;

  constructor(
    private readonly httpClient: LegacyHttpClient,
    private readonly flowEditorPort: StudioFlowEditorPort
  ) {
    this.unsubscribeFlowStructure = flowEditorPort.subscribeStructure((flowSnapshot) => {
      this.onFlowStructureChanged(flowSnapshot);
    });
  }

  async openProject(projectId: string): Promise<StudioProjectPersistenceResult> {
    if (this.disposed) {
      return this.result(false, 'disposed');
    }

    const requestSequence = this.flowEditorPort.nextRequestSequence(projectId);
    const controller = new AbortController();
    this.openControllers.add(controller);
    this.setSnapshot({
      ...this.snapshot,
      status: 'loading',
      error: '',
      lastDisposition: 'idle'
    });

    try {
      const project = normalizeProjectDto(await this.httpClient.get(
        `/projects/${encodeURIComponent(projectId)}`,
        null,
        { signal: controller.signal }
      ));
      const replaceResult = this.flowEditorPort.replaceFlow({
        projectId: project.id,
        requestSequence,
        flow: project.flow ?? createEmptyFlow()
      });

      if (!replaceResult.accepted) {
        return this.handleRejectedReplace(replaceResult);
      }

      this.savedFlowRevision = replaceResult.snapshot.flowRevision;
      this.setSnapshot(createLoadedSnapshot(
        project,
        this.saveControllersByProject.has(project.id),
        false,
        'accepted'
      ));
      return this.result(true, 'accepted');
    } catch (error) {
      return this.handleRequestError(error, projectId, 'open');
    } finally {
      this.openControllers.delete(controller);
    }
  }

  getSnapshot(): StudioProjectPersistenceSnapshot {
    return cloneSnapshot(this.snapshot);
  }

  async save(): Promise<StudioProjectPersistenceResult> {
    if (this.disposed) {
      return this.result(false, 'disposed');
    }

    const current = this.snapshot;
    if (!current.project || !current.projectId) {
      return this.setDisposition(false, 'project_mismatch');
    }

    if (this.saveControllersByProject.has(current.projectId)) {
      return this.setDisposition(false, 'in_flight');
    }

    const flowSnapshot = this.flowEditorPort.getSnapshot();
    if (flowSnapshot.projectId !== current.projectId) {
      return this.setDisposition(false, 'project_mismatch');
    }

    const capture = {
      projectId: current.projectId,
      persistenceRevision: current.persistenceRevision ?? 0,
      flowRevision: flowSnapshot.flowRevision,
      flow: deepClone(flowSnapshot.flow),
      name: current.name,
      description: current.description,
      globalVariables: deepClone(current.globalVariables)
    };
    const controller = new AbortController();
    this.saveControllersByProject.set(capture.projectId, controller);
    this.setSnapshot({
      ...this.snapshot,
      status: 'saving',
      saving: true,
      error: '',
      lastDisposition: 'idle'
    });

    try {
      const saved = normalizeProjectDto(await this.httpClient.put(
        `/projects/${encodeURIComponent(capture.projectId)}`,
        {
          name: capture.name,
          description: capture.description,
          flow: capture.flow,
          globalVariables: capture.globalVariables,
          expectedPersistenceRevision: capture.persistenceRevision
        },
        { signal: controller.signal }
      ));

      return this.applySaveResponse(capture, saved);
    } catch (error) {
      return this.handleRequestError(error, capture.projectId, 'save');
    } finally {
      this.saveControllersByProject.delete(capture.projectId);
      if (this.snapshot.projectId === capture.projectId && this.snapshot.saving) {
        this.setSnapshot({
          ...this.snapshot,
          status: this.snapshot.loaded ? 'loaded' : 'error',
          saving: false
        });
      }
    }
  }

  subscribe(listener: StudioProjectPersistenceListener): () => void {
    if (this.disposed) {
      listener(this.getSnapshot());
      return () => {};
    }

    this.listeners.add(listener);
    listener(this.getSnapshot());
    return () => {
      this.listeners.delete(listener);
    };
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }

    this.disposed = true;
    this.unsubscribeFlowStructure();
    for (const controller of this.openControllers) {
      controller.abort();
    }
    this.openControllers.clear();
    for (const controller of this.saveControllersByProject.values()) {
      controller.abort();
    }
    this.saveControllersByProject.clear();
    this.setSnapshot({
      ...this.snapshot,
      status: 'disposed',
      saving: false,
      lastDisposition: 'disposed'
    });
    this.listeners.clear();
  }

  private onFlowStructureChanged(flowSnapshot: StudioFlowEditorSnapshot): void {
    if (this.disposed || !this.snapshot.projectId || flowSnapshot.projectId !== this.snapshot.projectId) {
      return;
    }

    const dirty = this.savedFlowRevision !== null && flowSnapshot.flowRevision !== this.savedFlowRevision;
    if (dirty === this.snapshot.dirty && this.snapshot.status !== 'saving') {
      return;
    }

    this.setSnapshot({
      ...this.snapshot,
      dirty,
      status: this.snapshot.saving ? 'saving' : 'loaded'
    });
  }

  private handleRejectedReplace(replaceResult: FlowEditorCommandResult): StudioProjectPersistenceResult {
    const disposition = replaceResult.disposition === 'stale_request'
      ? 'stale_request'
      : 'project_mismatch';
    if (this.snapshot.projectId) {
      this.setSnapshot({
        ...this.snapshot,
        status: this.snapshot.loaded ? 'loaded' : 'error',
        lastDisposition: disposition,
        error: disposition
      });
    }

    return this.result(false, disposition);
  }

  private applySaveResponse(
    capture: {
      readonly projectId: string;
      readonly flowRevision: number;
    },
    saved: StudioProjectDto
  ): StudioProjectPersistenceResult {
    if (this.disposed) {
      return this.result(false, 'disposed');
    }

    if (this.snapshot.projectId !== capture.projectId) {
      return this.result(false, 'stale_request');
    }

    const latestFlow = this.flowEditorPort.getSnapshot();
    if (latestFlow.projectId !== capture.projectId) {
      return this.result(false, 'stale_request');
    }

    const localFlowChanged = latestFlow.flowRevision !== capture.flowRevision;
    this.savedFlowRevision = capture.flowRevision;
    this.setSnapshot(createLoadedSnapshot(
      saved,
      false,
      localFlowChanged,
      'accepted'
    ));
    return this.result(true, 'accepted');
  }

  private handleRequestError(
    error: unknown,
    projectId: string,
    phase: 'open' | 'save'
  ): StudioProjectPersistenceResult {
    if (this.disposed) {
      return this.result(false, 'disposed');
    }

    const mapped = mapRequestError(error);
    if (this.snapshot.projectId && this.snapshot.projectId !== projectId) {
      return this.result(false, 'stale_request', mapped.httpStatus, mapped.errorCode);
    }

    this.setSnapshot({
      ...this.snapshot,
      status: phase === 'open' ? 'error' : (this.snapshot.loaded ? 'loaded' : 'error'),
      saving: false,
      dirty: phase === 'save' ? true : this.snapshot.dirty,
      error: mapped.message,
      lastDisposition: mapped.disposition
    });
    return this.result(false, mapped.disposition, mapped.httpStatus, mapped.errorCode);
  }

  private setDisposition(
    accepted: boolean,
    disposition: StudioProjectPersistenceDisposition
  ): StudioProjectPersistenceResult {
    this.setSnapshot({
      ...this.snapshot,
      lastDisposition: disposition,
      error: accepted ? '' : disposition
    });
    return this.result(accepted, disposition);
  }

  private result(
    accepted: boolean,
    disposition: StudioProjectPersistenceDisposition,
    httpStatus?: number,
    errorCode?: string
  ): StudioProjectPersistenceResult {
    return {
      accepted,
      disposition,
      snapshot: this.getSnapshot(),
      ...(httpStatus !== undefined ? { httpStatus } : {}),
      ...(errorCode !== undefined ? { errorCode } : {})
    };
  }

  private setSnapshot(snapshot: StudioProjectPersistenceSnapshot): void {
    this.snapshot = cloneSnapshot(snapshot);
    const published = this.getSnapshot();
    for (const listener of this.listeners) {
      listener(published);
    }
  }
}

function createEmptySnapshot(): StudioProjectPersistenceSnapshot {
  return {
    projectId: null,
    project: null,
    name: '',
    description: null,
    persistenceRevision: null,
    globalVariables: createEmptyGlobalVariables(),
    status: 'empty',
    loaded: false,
    saving: false,
    dirty: false,
    error: '',
    lastDisposition: 'idle'
  };
}

function createLoadedSnapshot(
  project: StudioProjectDto,
  saving: boolean,
  dirty: boolean,
  disposition: StudioProjectPersistenceDisposition
): StudioProjectPersistenceSnapshot {
  return {
    projectId: project.id,
    project: deepClone(project),
    name: project.name,
    description: project.description,
    persistenceRevision: project.persistenceRevision,
    globalVariables: deepClone(project.globalVariables),
    status: saving ? 'saving' : 'loaded',
    loaded: true,
    saving,
    dirty,
    error: '',
    lastDisposition: disposition
  };
}

function normalizeProjectDto(value: unknown): StudioProjectDto {
  if (!isRecord(value)) {
    throw new Error('Project response is not an object.');
  }

  const id = toSafeString(value.id ?? value.Id);
  if (!id) {
    throw new Error('Project response is missing id.');
  }

  return {
    id,
    name: toSafeString(value.name ?? value.Name),
    description: toNullableString(value.description ?? value.Description),
    persistenceRevision: toSafeNumber(value.persistenceRevision ?? value.PersistenceRevision),
    flow: deepClone(value.flow ?? value.Flow ?? createEmptyFlow()),
    globalVariables: deepClone(value.globalVariables ?? value.GlobalVariables ?? createEmptyGlobalVariables()),
    raw: deepClone(value)
  };
}

function mapRequestError(error: unknown): {
  readonly disposition: StudioProjectPersistenceDisposition;
  readonly message: string;
  readonly httpStatus?: number;
  readonly errorCode?: string;
} {
  if (isAbortError(error)) {
    return {
      disposition: 'cancelled',
      message: 'cancelled'
    };
  }

  const httpStatus = isRecord(error) && typeof error.status === 'number' ? error.status : undefined;
  const payload = isRecord(error) ? error.payload : null;
  const code = getStableErrorCode(payload);
  if (code === 'PSV011') {
    return {
      disposition: 'stale_persistence_revision',
      message: getErrorMessage(error),
      ...(httpStatus !== undefined ? { httpStatus } : {}),
      errorCode: code
    };
  }
  if (code === 'GV031') {
    return {
      disposition: 'runtime_busy',
      message: getErrorMessage(error),
      ...(httpStatus !== undefined ? { httpStatus } : {}),
      errorCode: code
    };
  }
  if (httpStatus && httpStatus >= 400 && httpStatus < 500) {
    return {
      disposition: 'validation_error',
      message: getErrorMessage(error),
      httpStatus,
      ...(code ? { errorCode: code } : {})
    };
  }

  return {
    disposition: 'network_error',
    message: getErrorMessage(error),
    ...(httpStatus !== undefined ? { httpStatus } : {}),
    ...(code ? { errorCode: code } : {})
  };
}

function getStableErrorCode(payload: unknown): string | undefined {
  if (!isRecord(payload)) {
    return undefined;
  }

  const code = payload.code ?? payload.Code ?? payload.errorCode ?? payload.ErrorCode;
  return typeof code === 'string' && code.trim() ? code.trim() : undefined;
}

function getErrorMessage(error: unknown): string {
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return String(error);
}

function isAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === 'AbortError' ||
    (isRecord(error) && error.name === 'AbortError');
}

function createEmptyFlow(): unknown {
  return {
    name: 'MainFlow',
    operators: [],
    connections: []
  };
}

function createEmptyGlobalVariables(): unknown {
  return {
    schemaVersion: '1.0',
    variables: [],
    sourceBindings: [],
    targetBindings: []
  };
}

function cloneSnapshot(snapshot: StudioProjectPersistenceSnapshot): StudioProjectPersistenceSnapshot {
  return {
    ...snapshot,
    project: snapshot.project ? deepClone(snapshot.project) : null,
    globalVariables: deepClone(snapshot.globalVariables)
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value));
}

function toSafeString(value: unknown, fallback = ''): string {
  if (value === null || value === undefined) {
    return fallback;
  }

  if (
    typeof value === 'string' ||
    typeof value === 'number' ||
    typeof value === 'boolean' ||
    typeof value === 'bigint'
  ) {
    return String(value);
  }

  return fallback;
}

function toNullableString(value: unknown): string | null {
  const text = toSafeString(value);
  return text || null;
}

function toSafeNumber(value: unknown): number {
  const numberValue = Number(value);
  return Number.isFinite(numberValue) ? numberValue : 0;
}

function deepClone<T>(value: T): T {
  if (value === null || value === undefined) {
    return value;
  }

  if (typeof structuredClone === 'function') {
    return structuredClone(value);
  }

  return JSON.parse(JSON.stringify(value)) as T;
}
