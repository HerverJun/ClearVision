import { reactive, readonly, type DeepReadonly } from 'vue';
import { ApiAbortError, ApiHttpError, ApiNetworkError, type ApiTransport } from '@/platform/api';
import type { CanonicalFlowDraft } from '@/platform/canvas';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceResourceSnapshot
} from '../workspaceLifecycleDiagnostics';
import type {
  WorkspaceGlobalVariableDefinitionV1,
  WorkspaceGlobalVariableSourceBindingV1,
  WorkspaceGlobalVariableTargetBindingV1,
  WorkspaceGlobalVariablesV1,
  WorkspaceGlobalVariableValueType,
  WorkspaceJsonValue,
  WorkspaceVariableConversionMode
} from '../workspaceContracts';
import { isGlobalVariableDataTypeCompatible } from './globalVariablesContracts';

export interface WorkspaceGlobalVariableRuntimeValueV1 {
  readonly variableId: string;
  readonly name: string;
  readonly displayName: string;
  readonly valueType: string;
  readonly value: unknown;
  readonly version: number;
  readonly updatedAtUtc: string | null;
  readonly updatedBy: string;
  readonly runId: string | null;
  readonly operatorId: string | null;
}

export interface WorkspaceGlobalVariableFieldError {
  readonly code: string;
  readonly message: string;
  readonly field: string;
  readonly variableId: string | null;
  readonly operatorId: string | null;
  readonly portId: string | null;
  readonly parameterId: string | null;
  readonly severity: string;
}

export interface WorkspaceGlobalVariablesProjection {
  readonly phase: 'ready' | 'loading-runtime' | 'error' | 'disposed';
  readonly applied: WorkspaceGlobalVariablesV1;
  readonly draft: WorkspaceGlobalVariablesV1;
  readonly draftRevision: number;
  readonly appliedRevision: number;
  readonly dirty: boolean;
  readonly fieldErrors: readonly WorkspaceGlobalVariableFieldError[];
  readonly runtimeValues: readonly WorkspaceGlobalVariableRuntimeValueV1[];
  readonly runtimeOperation: 'idle' | 'loading' | 'writing' | 'resetting' | 'error' | 'disposed';
  readonly runtimeOutcome: 'idle' | 'pending' | 'committed' | 'rejected' | 'unknown-outcome' | 'reconciled';
  readonly runtimeHasPendingWrite: boolean;
  readonly runtimeErrorCode: string | null;
  readonly runtimeTargetVariableId: string | null;
  readonly message: string;
}

type MutableProjection = { -readonly [Key in keyof WorkspaceGlobalVariablesProjection]: WorkspaceGlobalVariablesProjection[Key] };

export interface GlobalVariableDefinitionInput {
  readonly id?: string;
  readonly name: string;
  readonly displayName: string;
  readonly description?: string | null;
  readonly valueType: WorkspaceGlobalVariableValueType;
  readonly initialValue: WorkspaceJsonValue;
  readonly min?: string | number | null;
  readonly max?: string | number | null;
  readonly manualWriteAllowed?: boolean;
  readonly includeInResultMetadata?: boolean;
}

export interface GlobalVariableSourceBindingInput {
  readonly id?: string;
  readonly variableId: string;
  readonly operatorId: string;
  readonly outputPortId: string;
  readonly operatorName: string;
  readonly outputPortName: string;
  readonly resultPathVersion?: number | null;
  readonly resultPath?: string | null;
  readonly conversionMode?: WorkspaceVariableConversionMode;
  readonly expression?: string | null;
}

export interface GlobalVariableTargetBindingInput {
  readonly id?: string;
  readonly variableId: string;
  readonly operatorId: string;
  readonly parameterId: string;
  readonly operatorName: string;
  readonly parameterName: string;
  readonly conversionMode?: WorkspaceVariableConversionMode;
  readonly expression?: string | null;
}

export interface WorkspaceGlobalVariablesOwner {
  readonly projection: DeepReadonly<WorkspaceGlobalVariablesProjection>;
  upsertDefinition(input: GlobalVariableDefinitionInput): string | null;
  removeDefinition(variableId: string): void;
  upsertSourceBinding(input: GlobalVariableSourceBindingInput): string | null;
  removeSourceBinding(bindingId: string): void;
  upsertTargetBinding(input: GlobalVariableTargetBindingInput): string | null;
  removeTargetBinding(bindingId: string): void;
  apply(): boolean;
  cancel(): void;
  refreshRuntimeValues(): Promise<void>;
  writeRuntimeValue(variableId: string, value: WorkspaceJsonValue, expectedVersion?: number | null): Promise<boolean>;
  resetRuntimeValue(variableId: string, expectedVersion?: number | null): Promise<boolean>;
  resetAllRuntimeValues(expectedVersions?: Readonly<Record<string, number>>): Promise<boolean>;
  getApplied(): WorkspaceGlobalVariablesV1;
  acceptServerBaseline(schema: WorkspaceGlobalVariablesV1, preserveApplied: boolean): void;
  replaceApplied(schema: WorkspaceGlobalVariablesV1): void;
  setServerDiagnostics(payload: unknown): void;
  setReadonly(reason: string): void;
  clearReadonly(): void;
  prepareForLeave(): Promise<boolean>;
  settle(): Promise<void>;
  dispose(): void;
}

function enumValue<T extends string>(value: T): Readonly<{ value: T; persistenceValue: T }> {
  return Object.freeze({ value, persistenceValue: value });
}

function cloneSchema(schema: WorkspaceGlobalVariablesV1): WorkspaceGlobalVariablesV1 {
  return Object.freeze({
    schemaVersion: schema.schemaVersion,
    variables: Object.freeze(schema.variables.map(item => Object.freeze({ ...item }))),
    sourceBindings: Object.freeze(schema.sourceBindings.map(item => Object.freeze({ ...item }))),
    targetBindings: Object.freeze(schema.targetBindings.map(item => Object.freeze({ ...item }))),
    opaqueReadOnly: schema.opaqueReadOnly
  });
}

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function field(source: Readonly<Record<string, unknown>>, camel: string): unknown {
  if (Object.prototype.hasOwnProperty.call(source, camel)) return source[camel];
  return source[`${camel.slice(0, 1).toUpperCase()}${camel.slice(1)}`];
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : value === null || value === undefined ? '' : String(value).trim();
}

function nullableText(value: unknown): string | null {
  return text(value) || null;
}

function enumText(value: unknown): string {
  if (typeof value === 'string' || typeof value === 'number') return String(value);
  const source = record(value);
  return text(field(source, 'value')) || text(field(source, 'persistenceValue'));
}

function flowOperator(flow: CanonicalFlowDraft, operatorId: string): Readonly<Record<string, unknown>> | null {
  return flow.operators.find(operator => enumText(field(operator, 'id')) === operatorId) ?? null;
}

function flowPort(operator: Readonly<Record<string, unknown>>, portId: string): Readonly<Record<string, unknown>> | null {
  const ports = field(operator, 'outputPorts');
  return Array.isArray(ports)
    ? ports.find(port => enumText(field(record(port), 'id')) === portId) as Readonly<Record<string, unknown>> | undefined ?? null
    : null;
}

function flowParameter(operator: Readonly<Record<string, unknown>>, parameterId: string): Readonly<Record<string, unknown>> | null {
  const parameters = field(operator, 'parameters');
  return Array.isArray(parameters)
    ? parameters.find(parameter => enumText(field(record(parameter), 'id')) === parameterId) as Readonly<Record<string, unknown>> | undefined ?? null
    : null;
}

function bindingError(
  errors: WorkspaceGlobalVariableFieldError[],
  input: Readonly<{
    code: string;
    message: string;
    field: string;
    variableId: string | null;
    operatorId: string | null;
    portId?: string | null;
    parameterId?: string | null;
  }>
): void {
  errors.push({
    code: input.code,
    message: input.message,
    field: input.field,
    variableId: input.variableId,
    operatorId: input.operatorId,
    portId: input.portId ?? null,
    parameterId: input.parameterId ?? null,
    severity: 'Error'
  });
}

function localErrors(
  schema: WorkspaceGlobalVariablesV1,
  flow: CanonicalFlowDraft | null = null
): readonly WorkspaceGlobalVariableFieldError[] {
  const errors: WorkspaceGlobalVariableFieldError[] = [];
  const ids = new Set<string>();
  const names = new Set<string>();
  for (const variable of schema.variables) {
    const base = {
      variableId: variable.id,
      operatorId: null,
      portId: null,
      parameterId: null,
      severity: 'Error'
    } as const;
    if (ids.has(variable.id)) errors.push({ code: 'GV003', message: '变量 ID 重复。', field: `variables.${variable.id}.id`, ...base });
    if (!variable.name.trim()) errors.push({ code: 'GV004', message: '变量名称不能为空。', field: `variables.${variable.id}.name`, ...base });
    if (names.has(variable.name.toLowerCase())) errors.push({ code: 'GV013', message: '变量名称重复。', field: `variables.${variable.id}.name`, ...base });
    if ((variable.valueType.value === 'Int64' || variable.valueType.value === 'Double') &&
      typeof variable.initialValue !== 'number' && typeof variable.initialValue !== 'string') {
      errors.push({ code: 'GV005', message: '数值变量的初始值必须是有限数值。', field: `variables.${variable.id}.initialValue`, ...base });
    }
    ids.add(variable.id);
    names.add(variable.name.toLowerCase());
  }
  for (const binding of schema.sourceBindings) {
    if (!ids.has(binding.variableId)) {
      bindingError(errors, {
        code: 'GV008', message: '来源绑定引用的变量不存在。',
        field: `sourceBindings.${binding.id}.variableId`, variableId: binding.variableId,
        operatorId: binding.operatorId, portId: binding.outputPortId
      });
      continue;
    }
    if (!flow) continue;
    const operator = flowOperator(flow, binding.operatorId);
    if (!operator) {
      bindingError(errors, {
        code: 'GV009', message: '来源绑定引用的算子不存在。',
        field: `sourceBindings.${binding.id}.operatorId`, variableId: binding.variableId,
        operatorId: binding.operatorId, portId: binding.outputPortId
      });
      continue;
    }
    const port = flowPort(operator, binding.outputPortId);
    if (!port) {
      bindingError(errors, {
        code: 'GV010', message: '来源绑定引用的输出端口不存在。',
        field: `sourceBindings.${binding.id}.outputPortId`, variableId: binding.variableId,
        operatorId: binding.operatorId, portId: binding.outputPortId
      });
      continue;
    }
    const variable = schema.variables.find(item => item.id === binding.variableId)!;
    if (!isGlobalVariableDataTypeCompatible(
      variable.valueType.value,
      enumText(field(port, 'dataType')),
      binding.conversionMode.value
    )) {
      bindingError(errors, {
        code: 'GV014', message: `来源端口类型与变量“${variable.displayName}”不兼容。`,
        field: `sourceBindings.${binding.id}.outputPortId`, variableId: binding.variableId,
        operatorId: binding.operatorId, portId: binding.outputPortId
      });
    }
  }
  for (const binding of schema.targetBindings) {
    if (!ids.has(binding.variableId)) {
      bindingError(errors, {
        code: 'GV008', message: '目标绑定引用的变量不存在。',
        field: `targetBindings.${binding.id}.variableId`, variableId: binding.variableId,
        operatorId: binding.operatorId, parameterId: binding.parameterId
      });
      continue;
    }
    if (!flow) continue;
    const operator = flowOperator(flow, binding.operatorId);
    if (!operator) {
      bindingError(errors, {
        code: 'GV009', message: '目标绑定引用的算子不存在。',
        field: `targetBindings.${binding.id}.operatorId`, variableId: binding.variableId,
        operatorId: binding.operatorId, parameterId: binding.parameterId
      });
      continue;
    }
    const parameter = flowParameter(operator, binding.parameterId);
    if (!parameter) {
      bindingError(errors, {
        code: 'GV011', message: '目标绑定引用的参数不存在。',
        field: `targetBindings.${binding.id}.parameterId`, variableId: binding.variableId,
        operatorId: binding.operatorId, parameterId: binding.parameterId
      });
      continue;
    }
    const variable = schema.variables.find(item => item.id === binding.variableId)!;
    if (!isGlobalVariableDataTypeCompatible(
      variable.valueType.value,
      enumText(field(parameter, 'dataType')),
      binding.conversionMode.value
    )) {
      bindingError(errors, {
        code: 'GV015', message: `目标参数类型与变量“${variable.displayName}”不兼容。`,
        field: `targetBindings.${binding.id}.parameterId`, variableId: binding.variableId,
        operatorId: binding.operatorId, parameterId: binding.parameterId
      });
    }
  }
  return Object.freeze(errors);
}

function decodeRuntimeValues(payload: unknown): readonly WorkspaceGlobalVariableRuntimeValueV1[] {
  if (!Array.isArray(payload)) throw new TypeError('运行值响应必须是数组。');
  return Object.freeze(payload.map(entry => {
    const source = record(entry);
    return Object.freeze({
      variableId: text(field(source, 'variableId')),
      name: text(field(source, 'name')),
      displayName: text(field(source, 'displayName')),
      valueType: text(field(source, 'valueType')),
      value: field(source, 'value'),
      version: Number(field(source, 'version')) || 0,
      updatedAtUtc: nullableText(field(source, 'updatedAtUtc')),
      updatedBy: text(field(source, 'updatedBy')),
      runId: nullableText(field(source, 'runId')),
      operatorId: nullableText(field(source, 'operatorId'))
    });
  }));
}

function errorDetails(error: unknown): Readonly<{ code: string | null; message: string }> {
  const source = error as { payload?: unknown; message?: unknown } | null;
  const payload = record(source?.payload);
  const code = nullableText(field(payload, 'code'));
  const message = nullableText(field(payload, 'error')) ?? nullableText(field(payload, 'message')) ??
    (typeof source?.message === 'string' ? source.message : null) ?? '运行值命令失败。';
  return Object.freeze({ code, message });
}

function isUnknownWriteOutcome(error: unknown): boolean {
  return error instanceof ApiAbortError || error instanceof ApiNetworkError ||
    error instanceof ApiHttpError && error.status === 401;
}

export function createWorkspaceGlobalVariablesOwner(options: {
  readonly projectId: string;
  readonly baseline: WorkspaceGlobalVariablesV1;
  readonly api: ApiTransport;
  readonly getFlowDraft?: () => CanonicalFlowDraft | null;
  readonly diagnostics?: WorkspaceLifecycleDiagnosticsOwner;
}): WorkspaceGlobalVariablesOwner {
  let applied = cloneSchema(options.baseline);
  let draft = cloneSchema(options.baseline);
  let disposed = false;
  let readonlyReason: string | null = null;
  let requestGeneration = 0;
  const lease: WorkspaceCapabilityDiagnosticsLease | undefined = options.diagnostics?.reserveCapability(
    options.projectId,
    'global-variables'
  );
  const readControllers = new Set<AbortController>();
  const writeControllers = new Set<AbortController>();
  const pending = new Set<Promise<unknown>>();
  const pendingWrites = new Set<Promise<unknown>>();
  let inFlightReads = 0;
  let inFlightWrites = 0;
  let pendingWrite: {
    readonly kind: 'write' | 'reset' | 'reset-all';
    readonly expectedValues: Readonly<Record<string, unknown>>;
    readonly baselineVersions: Readonly<Record<string, number | null>>;
  } | null = null;
  const state = reactive<MutableProjection>({
    phase: 'ready',
    applied,
    draft,
    draftRevision: 0,
    appliedRevision: 0,
    dirty: false,
    fieldErrors: Object.freeze([]),
    runtimeValues: Object.freeze([]),
    runtimeOperation: 'idle',
    runtimeOutcome: 'idle',
    runtimeHasPendingWrite: false,
    runtimeErrorCode: null,
    runtimeTargetVariableId: null,
    message: '变量定义与绑定已就绪。'
  });

  function syncDiagnostics(): void {
    lease?.update(Object.freeze({
      activeSubscriptions: 0,
      activeTimers: 0,
      activeAnimationFrames: 0,
      activeObservers: 0,
      activeAbortControllers: readControllers.size + writeControllers.size,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads,
      inFlightWrites,
      inFlightPreview: 0,
      inFlightExecute: 0
    } satisfies WorkspaceResourceSnapshot));
  }

  function track<T>(promise: Promise<T>, kind: 'read' | 'write'): Promise<T> {
    pending.add(promise);
    if (kind === 'write') pendingWrites.add(promise);
    promise.finally(() => pending.delete(promise)).catch(() => {});
    promise.finally(() => pendingWrites.delete(promise)).catch(() => {});
    return promise;
  }

  function sameValue(left: unknown, right: unknown): boolean {
    try {
      return JSON.stringify(left) === JSON.stringify(right);
    } catch {
      return Object.is(left, right);
    }
  }

  function settlePendingWrite(values: readonly WorkspaceGlobalVariableRuntimeValueV1[]): void {
    if (!pendingWrite) return;
    const allMatch = Object.entries(pendingWrite.expectedValues).every(([variableId, expected]) => {
      const actual = values.find(item => item.variableId === variableId);
      const baselineVersion = pendingWrite?.baselineVersions[variableId] ?? null;
      return Boolean(actual && sameValue(actual.value, expected) &&
        (baselineVersion === null || actual.version > baselineVersion));
    });
    if (allMatch) {
      pendingWrite = null;
      state.runtimeHasPendingWrite = false;
      state.runtimeOutcome = 'reconciled';
      state.message = '运行值读取已协调此前写入结果；没有自动重发命令。';
    } else {
      state.runtimeOutcome = 'unknown-outcome';
      state.message = '运行值已重新读取，但当前后端合同无法确认此前写入是否提交；禁止自动重试。';
    }
  }

  function beginRequest(kind: 'read' | 'write'): AbortController {
    const controller = new AbortController();
    (kind === 'read' ? readControllers : writeControllers).add(controller);
    if (kind === 'read') inFlightReads += 1;
    else inFlightWrites += 1;
    syncDiagnostics();
    return controller;
  }

  function endRequest(controller: AbortController, kind: 'read' | 'write'): void {
    (kind === 'read' ? readControllers : writeControllers).delete(controller);
    if (kind === 'read') inFlightReads = Math.max(0, inFlightReads - 1);
    else inFlightWrites = Math.max(0, inFlightWrites - 1);
    syncDiagnostics();
  }

  function updateDraft(next: WorkspaceGlobalVariablesV1): void {
    draft = cloneSchema(next);
    state.draft = draft;
    state.draftRevision += 1;
    state.dirty = JSON.stringify(draft) !== JSON.stringify(applied);
    state.fieldErrors = localErrors(draft, options.getFlowDraft?.() ?? null);
    state.message = state.fieldErrors.length > 0 ? '变量草稿存在字段错误。' : '变量草稿尚未应用。';
  }

  function variable(input: GlobalVariableDefinitionInput, order: number): WorkspaceGlobalVariableDefinitionV1 {
    return Object.freeze({
      id: input.id ?? globalThis.crypto.randomUUID(),
      name: input.name.trim(),
      displayName: input.displayName.trim() || input.name.trim(),
      description: input.description?.trim() || null,
      valueType: enumValue(input.valueType),
      initialValue: input.initialValue,
      min: input.valueType === 'Int64' || input.valueType === 'Double' ? input.min ?? null : null,
      max: input.valueType === 'Int64' || input.valueType === 'Double' ? input.max ?? null : null,
      manualWriteAllowed: input.manualWriteAllowed === true,
      includeInResultMetadata: input.includeInResultMetadata === true,
      order,
      opaqueReadOnly: Object.freeze({})
    });
  }

  const owner: WorkspaceGlobalVariablesOwner = Object.freeze({
    projection: readonly(state),
    upsertDefinition(input: GlobalVariableDefinitionInput): string | null {
      if (disposed || readonlyReason) return null;
      const id = input.id ?? globalThis.crypto.randomUUID();
      const index = draft.variables.findIndex(item => item.id === id);
      const variables = [...draft.variables];
      variables[index >= 0 ? index : variables.length] = variable({ ...input, id }, index >= 0 ? draft.variables[index]!.order : variables.length);
      updateDraft(Object.freeze({ ...draft, variables: Object.freeze(variables) }));
      return id;
    },
    removeDefinition(variableId: string): void {
      if (disposed || readonlyReason) return;
      updateDraft(Object.freeze({
        ...draft,
        variables: Object.freeze(draft.variables.filter(item => item.id !== variableId).map((item, order) => Object.freeze({ ...item, order }))),
        sourceBindings: Object.freeze(draft.sourceBindings.filter(item => item.variableId !== variableId)),
        targetBindings: Object.freeze(draft.targetBindings.filter(item => item.variableId !== variableId))
      }));
    },
    upsertSourceBinding(input: GlobalVariableSourceBindingInput): string | null {
      if (disposed || readonlyReason) return null;
      const id = input.id ?? globalThis.crypto.randomUUID();
      const binding: WorkspaceGlobalVariableSourceBindingV1 = Object.freeze({
        id,
        variableId: input.variableId,
        operatorId: input.operatorId,
        outputPortId: input.outputPortId,
        operatorName: input.operatorName,
        outputPortName: input.outputPortName,
        resultPathVersion: input.resultPathVersion ?? null,
        resultPath: input.resultPath?.trim() || null,
        conversionMode: enumValue(input.conversionMode ?? 'Exact'),
        expression: input.expression?.trim() || null,
        opaqueReadOnly: Object.freeze({})
      });
      const bindings = draft.sourceBindings.filter(item => item.id !== id && item.variableId !== input.variableId);
      updateDraft(Object.freeze({ ...draft, sourceBindings: Object.freeze([...bindings, binding]) }));
      return id;
    },
    removeSourceBinding(bindingId: string): void {
      if (!disposed && !readonlyReason) updateDraft(Object.freeze({ ...draft, sourceBindings: Object.freeze(draft.sourceBindings.filter(item => item.id !== bindingId)) }));
    },
    upsertTargetBinding(input: GlobalVariableTargetBindingInput): string | null {
      if (disposed || readonlyReason) return null;
      const id = input.id ?? globalThis.crypto.randomUUID();
      const binding: WorkspaceGlobalVariableTargetBindingV1 = Object.freeze({
        id,
        variableId: input.variableId,
        operatorId: input.operatorId,
        parameterId: input.parameterId,
        operatorName: input.operatorName,
        parameterName: input.parameterName,
        conversionMode: enumValue(input.conversionMode ?? 'Exact'),
        expression: input.expression?.trim() || null,
        opaqueReadOnly: Object.freeze({})
      });
      const bindings = draft.targetBindings.filter(item => item.id !== id &&
        !(item.operatorId === input.operatorId && item.parameterId === input.parameterId));
      updateDraft(Object.freeze({ ...draft, targetBindings: Object.freeze([...bindings, binding]) }));
      return id;
    },
    removeTargetBinding(bindingId: string): void {
      if (!disposed && !readonlyReason) updateDraft(Object.freeze({ ...draft, targetBindings: Object.freeze(draft.targetBindings.filter(item => item.id !== bindingId)) }));
    },
    apply(): boolean {
      if (disposed || readonlyReason) return false;
      const errors = localErrors(draft, options.getFlowDraft?.() ?? null);
      state.fieldErrors = errors;
      if (errors.some(item => item.severity.toLowerCase() === 'error')) {
        state.message = '变量草稿校验未通过，尚未进入工程保存草稿。';
        return false;
      }
      applied = cloneSchema(draft);
      state.applied = applied;
      state.appliedRevision += 1;
      state.dirty = false;
      state.message = '变量修改已应用，需保存工程后正式生效。';
      return true;
    },
    cancel(): void {
      if (disposed || readonlyReason) return;
      draft = cloneSchema(applied);
      state.draft = draft;
      state.draftRevision += 1;
      state.dirty = false;
      state.fieldErrors = Object.freeze([]);
      state.message = '已取消本次变量修改。';
    },
    refreshRuntimeValues(): Promise<void> {
      if (disposed || readonlyReason || !options.api.get) return Promise.resolve();
      if (state.runtimeOperation !== 'idle' && state.runtimeOperation !== 'error') return Promise.resolve();
      const operation = ++requestGeneration;
      const task = (async () => {
        const controller = beginRequest('read');
        state.phase = 'loading-runtime';
        state.runtimeOperation = 'loading';
        state.runtimeErrorCode = null;
        state.runtimeTargetVariableId = null;
        state.message = '正在读取运行值。';
        try {
          const values = decodeRuntimeValues(await options.api.get!(
            `projects/${encodeURIComponent(options.projectId)}/global-variable-values`,
            { signal: controller.signal }
          ));
          if (disposed || operation !== requestGeneration) return;
          state.runtimeValues = values;
          settlePendingWrite(values);
          state.phase = 'ready';
          state.runtimeOperation = 'idle';
          state.runtimeErrorCode = null;
          if (!pendingWrite && state.runtimeOutcome !== 'reconciled') {
            state.runtimeOutcome = 'idle';
            state.message = '运行值来自当前运行会话，不会写回变量定义。';
          }
        } catch (error) {
          if (disposed || operation !== requestGeneration) return;
          const details = errorDetails(error);
          state.phase = 'error';
          state.runtimeOperation = 'error';
          state.runtimeErrorCode = details.code;
          state.runtimeOutcome = pendingWrite ? 'unknown-outcome' : 'rejected';
          state.message = pendingWrite
            ? '运行值读取失败，之前写入结果仍未知；禁止自动重试。'
            : `运行值读取失败：${details.message}`;
        } finally {
          endRequest(controller, 'read');
        }
      })();
      return track(task, 'read');
    },
    writeRuntimeValue(variableId: string, value: WorkspaceJsonValue, expectedVersion?: number | null): Promise<boolean> {
      const put = options.api.put;
      if (disposed || readonlyReason || !put) return Promise.resolve(false);
      if ((state.runtimeOperation !== 'idle' && state.runtimeOperation !== 'error') || pendingWrite) {
        state.message = '已有运行值命令正在处理或等待协调，请先完成读取。';
        return Promise.resolve(false);
      }
      const definition = applied.variables.find(item => item.id === variableId);
      if (!definition) {
        state.runtimeOperation = 'error';
        state.runtimeErrorCode = 'GV_NOT_FOUND';
        state.runtimeOutcome = 'rejected';
        state.message = '运行值写入失败：变量不存在。';
        return Promise.resolve(false);
      }
      if (!definition.manualWriteAllowed) {
        state.runtimeOperation = 'error';
        state.runtimeErrorCode = 'GV030';
        state.runtimeOutcome = 'rejected';
        state.message = '运行值写入失败：该变量未允许手动写入。';
        return Promise.resolve(false);
      }
      const operation = ++requestGeneration;
      const previous = state.runtimeValues.find(item => item.variableId === variableId);
      pendingWrite = {
        kind: 'write',
        expectedValues: Object.freeze({ [variableId]: value }),
        baselineVersions: Object.freeze({ [variableId]: previous?.version ?? expectedVersion ?? null })
      };
      state.runtimeHasPendingWrite = true;
      state.runtimeOutcome = 'pending';
      const task = (async () => {
        const controller = beginRequest('write');
        state.phase = 'loading-runtime';
        state.runtimeOperation = 'writing';
        state.runtimeErrorCode = null;
        state.runtimeTargetVariableId = variableId;
        state.message = `正在写入变量“${definition.displayName}”的运行值。`;
        try {
          const response = await put(
            `projects/${encodeURIComponent(options.projectId)}/global-variable-values/${encodeURIComponent(variableId)}`,
            expectedVersion === null || expectedVersion === undefined ? { value } : { value, expectedVersion },
            { signal: controller.signal }
          );
          const values = decodeRuntimeValues(response);
          if (disposed || operation !== requestGeneration) return false;
          state.runtimeValues = values;
          pendingWrite = null;
          state.runtimeHasPendingWrite = false;
          state.phase = 'ready';
          state.runtimeOperation = 'idle';
          state.runtimeOutcome = 'committed';
          state.runtimeErrorCode = null;
          state.runtimeTargetVariableId = null;
          state.message = '运行值已写入；工程定义和保存状态未改变。';
          return true;
        } catch (error) {
          if (disposed || operation !== requestGeneration) return false;
          const details = errorDetails(error);
          state.phase = 'error';
          state.runtimeOperation = 'error';
          state.runtimeErrorCode = details.code;
          state.runtimeTargetVariableId = variableId;
          if (isUnknownWriteOutcome(error)) {
            state.runtimeOutcome = 'unknown-outcome';
            state.message = '运行值写入响应未知；后端没有 operation identity，请先重新读取协调，禁止自动重试。';
          } else {
            pendingWrite = null;
            state.runtimeHasPendingWrite = false;
            state.runtimeOutcome = 'rejected';
            state.runtimeTargetVariableId = null;
            state.message = `运行值写入失败：${details.message}`;
          }
          return false;
        } finally {
          endRequest(controller, 'write');
        }
      })();
      return track(task, 'write');
    },
    resetRuntimeValue(variableId: string, expectedVersion?: number | null): Promise<boolean> {
      if (disposed || readonlyReason || !options.api.post) return Promise.resolve(false);
      if ((state.runtimeOperation !== 'idle' && state.runtimeOperation !== 'error') || pendingWrite) {
        state.message = '已有运行值命令正在处理或等待协调，请先完成读取。';
        return Promise.resolve(false);
      }
      const definition = applied.variables.find(item => item.id === variableId);
      if (!definition) {
        state.runtimeOperation = 'error';
        state.runtimeErrorCode = 'GV_NOT_FOUND';
        state.runtimeOutcome = 'rejected';
        state.message = '运行值重置失败：变量不存在。';
        return Promise.resolve(false);
      }
      if (!definition.manualWriteAllowed) {
        state.runtimeOperation = 'error';
        state.runtimeErrorCode = 'GV030';
        state.runtimeOutcome = 'rejected';
        state.message = '运行值重置失败：该变量未允许手动写入。';
        return Promise.resolve(false);
      }
      const operation = ++requestGeneration;
      const previous = state.runtimeValues.find(item => item.variableId === variableId);
      pendingWrite = {
        kind: 'reset',
        expectedValues: Object.freeze({ [variableId]: definition.initialValue }),
        baselineVersions: Object.freeze({ [variableId]: previous?.version ?? expectedVersion ?? null })
      };
      state.runtimeHasPendingWrite = true;
      state.runtimeOutcome = 'pending';
      const task = (async () => {
        const controller = beginRequest('write');
        state.phase = 'loading-runtime';
        state.runtimeOperation = 'resetting';
        state.runtimeErrorCode = null;
        state.runtimeTargetVariableId = variableId;
        state.message = `正在将变量“${definition.displayName}”重置为定义初始值。`;
        try {
          const response = await options.api.post!(
            `projects/${encodeURIComponent(options.projectId)}/global-variable-values/${encodeURIComponent(variableId)}/reset`,
            expectedVersion === null || expectedVersion === undefined ? {} : { expectedVersion },
            { signal: controller.signal }
          );
          const values = decodeRuntimeValues(response);
          if (disposed || operation !== requestGeneration) return false;
          state.runtimeValues = values;
          pendingWrite = null;
          state.runtimeHasPendingWrite = false;
          state.phase = 'ready';
          state.runtimeOperation = 'idle';
          state.runtimeOutcome = 'committed';
          state.runtimeErrorCode = null;
          state.runtimeTargetVariableId = null;
          state.message = '运行值已重置为定义初始值；工程定义未改变。';
          return true;
        } catch (error) {
          if (disposed || operation !== requestGeneration) return false;
          const details = errorDetails(error);
          state.phase = 'error';
          state.runtimeOperation = 'error';
          state.runtimeErrorCode = details.code;
          state.runtimeTargetVariableId = variableId;
          if (isUnknownWriteOutcome(error)) {
            state.runtimeOutcome = 'unknown-outcome';
            state.message = '运行值重置响应未知；请先重新读取协调，禁止自动重试。';
          } else {
            pendingWrite = null;
            state.runtimeHasPendingWrite = false;
            state.runtimeOutcome = 'rejected';
            state.runtimeTargetVariableId = null;
            state.message = `运行值重置失败：${details.message}`;
          }
          return false;
        } finally {
          endRequest(controller, 'write');
        }
      })();
      return track(task, 'write');
    },
    resetAllRuntimeValues(expectedVersions?: Readonly<Record<string, number>>): Promise<boolean> {
      if (disposed || readonlyReason || !options.api.post) return Promise.resolve(false);
      if ((state.runtimeOperation !== 'idle' && state.runtimeOperation !== 'error') || pendingWrite) {
        state.message = '已有运行值命令正在处理或等待协调，请先完成读取。';
        return Promise.resolve(false);
      }
      const blocked = applied.variables.find(item => !item.manualWriteAllowed);
      if (blocked) {
        state.runtimeOperation = 'error';
        state.runtimeErrorCode = 'GV030';
        state.runtimeOutcome = 'rejected';
        state.message = `运行值重置失败：变量“${blocked.displayName}”未允许手动写入。`;
        return Promise.resolve(false);
      }
      const operation = ++requestGeneration;
      const expectedValues = Object.fromEntries(applied.variables.map(variable => [variable.id, variable.initialValue]));
      const baselineVersions = Object.fromEntries(applied.variables.map(variable => [
        variable.id,
        state.runtimeValues.find(item => item.variableId === variable.id)?.version ?? expectedVersions?.[variable.id] ?? null
      ]));
      pendingWrite = {
        kind: 'reset-all',
        expectedValues: Object.freeze(expectedValues),
        baselineVersions: Object.freeze(baselineVersions)
      };
      state.runtimeHasPendingWrite = true;
      state.runtimeOutcome = 'pending';
      const task = (async () => {
        const controller = beginRequest('write');
        state.phase = 'loading-runtime';
        state.runtimeOperation = 'resetting';
        state.runtimeErrorCode = null;
        state.runtimeTargetVariableId = null;
        state.message = '正在将全部运行值重置为定义初始值。';
        try {
          const response = await options.api.post!(
            `projects/${encodeURIComponent(options.projectId)}/global-variable-values/reset`,
            expectedVersions && Object.keys(expectedVersions).length > 0 ? { expectedVersions } : {},
            { signal: controller.signal }
          );
          const values = decodeRuntimeValues(response);
          if (disposed || operation !== requestGeneration) return false;
          state.runtimeValues = values;
          pendingWrite = null;
          state.runtimeHasPendingWrite = false;
          state.phase = 'ready';
          state.runtimeOperation = 'idle';
          state.runtimeOutcome = 'committed';
          state.runtimeErrorCode = null;
          state.message = '全部运行值已重置；工程定义未改变。';
          return true;
        } catch (error) {
          if (disposed || operation !== requestGeneration) return false;
          const details = errorDetails(error);
          state.phase = 'error';
          state.runtimeOperation = 'error';
          state.runtimeErrorCode = details.code;
          if (isUnknownWriteOutcome(error)) {
            state.runtimeOutcome = 'unknown-outcome';
            state.message = '全部运行值重置响应未知；请先重新读取协调，禁止自动重试。';
          } else {
            pendingWrite = null;
            state.runtimeHasPendingWrite = false;
            state.runtimeOutcome = 'rejected';
            state.message = `全部运行值重置失败：${details.message}`;
          }
          return false;
        } finally {
          endRequest(controller, 'write');
        }
      })();
      return track(task, 'write');
    },
    getApplied(): WorkspaceGlobalVariablesV1 {
      return applied;
    },
    acceptServerBaseline(schema: WorkspaceGlobalVariablesV1, preserveApplied: boolean): void {
      if (disposed) return;
      if (!preserveApplied) {
        applied = cloneSchema(schema);
        draft = cloneSchema(schema);
        state.applied = applied;
        state.draft = draft;
        state.dirty = false;
      }
      state.appliedRevision += 1;
      state.draftRevision += 1;
    },
    replaceApplied(schema: WorkspaceGlobalVariablesV1): void {
      if (disposed) return;
      applied = cloneSchema(schema);
      draft = cloneSchema(schema);
      state.applied = applied;
      state.draft = draft;
      state.appliedRevision += 1;
      state.draftRevision += 1;
      state.dirty = false;
      state.fieldErrors = Object.freeze([]);
    },
    setServerDiagnostics(payload: unknown): void {
      if (disposed) return;
      const source = record(payload);
      const raw = field(source, 'diagnostics');
      if (!Array.isArray(raw)) return;
      state.fieldErrors = Object.freeze(raw.map(entry => {
        const item = record(entry);
        return Object.freeze({
          code: text(field(item, 'code')) || 'GV_VALIDATION',
          message: text(field(item, 'message')) || '变量校验失败。',
          field: text(field(item, 'field')) || 'globalVariables',
          variableId: nullableText(field(item, 'variableId')),
          operatorId: nullableText(field(item, 'operatorId')),
          portId: nullableText(field(item, 'portId')),
          parameterId: nullableText(field(item, 'parameterId')),
          severity: text(field(item, 'severity')) || 'Error'
        });
      }));
      state.message = '后端变量校验未通过，请按字段错误修正。';
    },
    setReadonly(reason: string): void {
      if (disposed) return;
      readonlyReason = reason.trim() || '会话已失效；全局变量保持只读。';
      for (const controller of readControllers) controller.abort('session-expired');
      state.message = readonlyReason;
    },
    clearReadonly(): void {
      if (disposed) return;
      readonlyReason = null;
      if (state.runtimeOperation === 'idle') state.message = '会话已恢复；运行值可按权限重新操作。';
    },
    async prepareForLeave(): Promise<boolean> {
      for (const controller of readControllers) controller.abort('leave');
      await Promise.allSettled([...pendingWrites]);
      return pendingWrite === null && state.runtimeOutcome !== 'unknown-outcome' &&
        writeControllers.size === 0;
    },
    async settle(): Promise<void> {
      await Promise.allSettled([...pending]);
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      requestGeneration += 1;
      for (const controller of [...readControllers, ...writeControllers]) {
        controller.abort('global-variables-owner-disposed');
      }
      state.phase = 'disposed';
      state.runtimeOperation = 'disposed';
      state.runtimeTargetVariableId = null;
      state.runtimeValues = Object.freeze([]);
      state.runtimeHasPendingWrite = false;
      state.message = '全局变量 owner 已释放。';
      lease?.update(Object.freeze({
        activeSubscriptions: 0,
        activeTimers: 0,
        activeAnimationFrames: 0,
        activeObservers: 0,
        activeAbortControllers: 0,
        activeBlobUrls: 0,
        activePreviewArtifactIds: 0,
        activeHostSubscriptions: 0,
        inFlightReads: 0,
        inFlightWrites: 0,
        inFlightPreview: 0,
        inFlightExecute: 0
      }));
      lease?.dispose('global-variables-owner-disposed');
    }
  });
  syncDiagnostics();
  return owner;
}
