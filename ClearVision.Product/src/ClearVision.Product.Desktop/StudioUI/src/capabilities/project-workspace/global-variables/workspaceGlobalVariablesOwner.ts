import { reactive, readonly, type DeepReadonly } from 'vue';
import type { ApiTransport } from '@/platform/api';
import type {
  WorkspaceGlobalVariableDefinitionV1,
  WorkspaceGlobalVariableSourceBindingV1,
  WorkspaceGlobalVariableTargetBindingV1,
  WorkspaceGlobalVariablesV1,
  WorkspaceGlobalVariableValueType,
  WorkspaceJsonValue,
  WorkspaceVariableConversionMode
} from '../workspaceContracts';

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
  getApplied(): WorkspaceGlobalVariablesV1;
  acceptServerBaseline(schema: WorkspaceGlobalVariablesV1, preserveApplied: boolean): void;
  replaceApplied(schema: WorkspaceGlobalVariablesV1): void;
  setServerDiagnostics(payload: unknown): void;
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

function localErrors(schema: WorkspaceGlobalVariablesV1): readonly WorkspaceGlobalVariableFieldError[] {
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
  for (const binding of [...schema.sourceBindings, ...schema.targetBindings]) {
    if (!ids.has(binding.variableId)) {
      errors.push({
        code: 'GV008',
        message: 'Binding 引用的变量不存在。',
        field: `bindings.${binding.id}.variableId`,
        variableId: binding.variableId,
        operatorId: binding.operatorId,
        portId: 'outputPortId' in binding ? binding.outputPortId : null,
        parameterId: 'parameterId' in binding ? binding.parameterId : null,
        severity: 'Error'
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

export function createWorkspaceGlobalVariablesOwner(options: {
  readonly projectId: string;
  readonly baseline: WorkspaceGlobalVariablesV1;
  readonly api: ApiTransport;
}): WorkspaceGlobalVariablesOwner {
  let applied = cloneSchema(options.baseline);
  let draft = cloneSchema(options.baseline);
  let disposed = false;
  let requestGeneration = 0;
  const state = reactive<MutableProjection>({
    phase: 'ready',
    applied,
    draft,
    draftRevision: 0,
    appliedRevision: 0,
    dirty: false,
    fieldErrors: Object.freeze([]),
    runtimeValues: Object.freeze([]),
    message: '变量定义与绑定已就绪。'
  });

  function updateDraft(next: WorkspaceGlobalVariablesV1): void {
    draft = cloneSchema(next);
    state.draft = draft;
    state.draftRevision += 1;
    state.dirty = JSON.stringify(draft) !== JSON.stringify(applied);
    state.fieldErrors = localErrors(draft);
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
      if (disposed) return null;
      const id = input.id ?? globalThis.crypto.randomUUID();
      const index = draft.variables.findIndex(item => item.id === id);
      const variables = [...draft.variables];
      variables[index >= 0 ? index : variables.length] = variable({ ...input, id }, index >= 0 ? draft.variables[index]!.order : variables.length);
      updateDraft(Object.freeze({ ...draft, variables: Object.freeze(variables) }));
      return id;
    },
    removeDefinition(variableId: string): void {
      if (disposed) return;
      updateDraft(Object.freeze({
        ...draft,
        variables: Object.freeze(draft.variables.filter(item => item.id !== variableId).map((item, order) => Object.freeze({ ...item, order }))),
        sourceBindings: Object.freeze(draft.sourceBindings.filter(item => item.variableId !== variableId)),
        targetBindings: Object.freeze(draft.targetBindings.filter(item => item.variableId !== variableId))
      }));
    },
    upsertSourceBinding(input: GlobalVariableSourceBindingInput): string | null {
      if (disposed) return null;
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
      if (!disposed) updateDraft(Object.freeze({ ...draft, sourceBindings: Object.freeze(draft.sourceBindings.filter(item => item.id !== bindingId)) }));
    },
    upsertTargetBinding(input: GlobalVariableTargetBindingInput): string | null {
      if (disposed) return null;
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
      if (!disposed) updateDraft(Object.freeze({ ...draft, targetBindings: Object.freeze(draft.targetBindings.filter(item => item.id !== bindingId)) }));
    },
    apply(): boolean {
      if (disposed) return false;
      const errors = localErrors(draft);
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
      if (disposed) return;
      draft = cloneSchema(applied);
      state.draft = draft;
      state.draftRevision += 1;
      state.dirty = false;
      state.fieldErrors = Object.freeze([]);
      state.message = '已取消本次变量修改。';
    },
    async refreshRuntimeValues(): Promise<void> {
      if (disposed || !options.api.get) return;
      const operation = ++requestGeneration;
      state.phase = 'loading-runtime';
      state.message = '正在读取运行值。';
      try {
        const values = decodeRuntimeValues(await options.api.get(`projects/${encodeURIComponent(options.projectId)}/global-variable-values`));
        if (disposed || operation !== requestGeneration) return;
        state.runtimeValues = values;
        state.phase = 'ready';
        state.message = '运行值为只读投影，不会写回变量定义。';
      } catch (error) {
        if (disposed || operation !== requestGeneration) return;
        state.phase = 'error';
        state.message = `运行值读取失败：${error instanceof Error ? error.message : '响应不可用。'}`;
      }
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
    dispose(): void {
      if (disposed) return;
      disposed = true;
      requestGeneration += 1;
      state.phase = 'disposed';
      state.runtimeValues = Object.freeze([]);
      state.message = '全局变量 owner 已释放。';
    }
  });
  return owner;
}
