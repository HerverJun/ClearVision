import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import { ApiAbortError, type ApiTransport } from '@/platform/api';
import type { FlowCanvasOwner } from '../flow';
import {
  encodeWorkspaceDecisionConfigurationV1,
  type WorkspaceDecisionComparator,
  type WorkspaceDecisionConfigurationV1,
  type WorkspaceDecisionInterpretationRule,
  type WorkspaceDecisionValueType,
  type WorkspaceFinalDecisionBindingV1,
  type WorkspaceMissingDecisionPolicy
} from '../workspaceContracts';

export interface FinalDecisionCandidateV1 {
  readonly operatorId: string;
  readonly operatorName: string;
  readonly outputPortId: string;
  readonly outputName: string;
  readonly dataType: WorkspaceDecisionValueType;
  readonly rule: WorkspaceDecisionInterpretationRule;
  readonly defaultTrueMeansOk: boolean | null;
  readonly defaultOkValue: string | null;
  readonly defaultNgValue: string | null;
  readonly requiredOkValue: string | null;
  readonly requiredNgValue: string | null;
}

export interface FinalDecisionIssueV1 {
  readonly code: string;
  readonly message: string;
  readonly field: string;
  readonly operatorId: string | null;
  readonly outputName: string | null;
}

export interface FinalDecisionProjection {
  readonly phase: 'idle' | 'validating' | 'valid' | 'invalid' | 'error' | 'disposed';
  readonly draft: WorkspaceDecisionConfigurationV1 | null;
  readonly candidates: readonly FinalDecisionCandidateV1[];
  readonly issues: readonly FinalDecisionIssueV1[];
  readonly draftRevision: number;
  readonly flowRevision: number;
  readonly dirty: boolean;
  readonly draftFingerprint: string;
  readonly message: string;
}

type MutableProjection = { -readonly [Key in keyof FinalDecisionProjection]: FinalDecisionProjection[Key] };

export interface FinalDecisionOwner {
  readonly projection: DeepReadonly<FinalDecisionProjection>;
  selectCandidate(key: string): void;
  patchBinding(patch: Readonly<{
    trueMeansOk?: boolean;
    okValue?: string | null;
    ngValue?: string | null;
    comparator?: WorkspaceDecisionComparator | null;
    threshold?: number | null;
  }>): void;
  setMissingPolicy(policy: WorkspaceMissingDecisionPolicy): void;
  clearBinding(): void;
  validate(): Promise<boolean>;
  apply(): Promise<boolean>;
  cancel(): void;
  dispose(): void;
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

function enumValue<T extends string>(value: T): Readonly<{ value: T; persistenceValue: T }> {
  return Object.freeze({ value, persistenceValue: value });
}

function fingerprint(value: WorkspaceDecisionConfigurationV1 | null): string {
  const source = JSON.stringify(encodeWorkspaceDecisionConfigurationV1(value));
  let hash = 5381;
  for (let index = 0; index < source.length; index += 1) {
    hash = (((hash << 5) + hash) + source.charCodeAt(index)) >>> 0;
  }
  return hash.toString(16).padStart(8, '0');
}

function cloneDecision(value: WorkspaceDecisionConfigurationV1 | null): WorkspaceDecisionConfigurationV1 | null {
  if (!value) return null;
  return Object.freeze({
    ...value,
    finalDecisionBinding: value.finalDecisionBinding ? Object.freeze({ ...value.finalDecisionBinding }) : null
  });
}

function decodeCandidates(value: unknown): readonly FinalDecisionCandidateV1[] {
  if (!Array.isArray(value)) throw new TypeError('最终判定候选必须是数组。');
  return Object.freeze(value.map(entry => {
    const source = record(entry);
    const dataType = text(field(source, 'dataType')) as WorkspaceDecisionValueType;
    const rule = text(field(source, 'rule')) as WorkspaceDecisionInterpretationRule;
    if (!['Boolean', 'String', 'Integer', 'Float'].includes(dataType) ||
      !['Boolean', 'StringMap', 'NumericComparison'].includes(rule)) {
      throw new TypeError('后端返回了不支持的最终判定候选类型。');
    }
    return Object.freeze({
      operatorId: text(field(source, 'operatorId')),
      operatorName: text(field(source, 'operatorName')),
      outputPortId: text(field(source, 'outputPortId')),
      outputName: text(field(source, 'outputName')),
      dataType,
      rule,
      defaultTrueMeansOk: typeof field(source, 'defaultTrueMeansOk') === 'boolean'
        ? field(source, 'defaultTrueMeansOk') as boolean
        : null,
      defaultOkValue: nullableText(field(source, 'defaultOkValue')),
      defaultNgValue: nullableText(field(source, 'defaultNgValue')),
      requiredOkValue: nullableText(field(source, 'requiredOkValue')),
      requiredNgValue: nullableText(field(source, 'requiredNgValue'))
    });
  }));
}

function decodeIssues(value: unknown): readonly FinalDecisionIssueV1[] {
  if (!Array.isArray(value)) throw new TypeError('最终判定问题必须是数组。');
  return Object.freeze(value.map(entry => {
    const source = record(entry);
    return Object.freeze({
      code: text(field(source, 'code')) || 'DECISION_INVALID',
      message: text(field(source, 'message')) || '最终判定配置无效。',
      field: text(field(source, 'field')) || 'decisionConfiguration.finalDecisionBinding',
      operatorId: nullableText(field(source, 'operatorId')),
      outputName: nullableText(field(source, 'outputName'))
    });
  }));
}

function candidateKey(candidate: FinalDecisionCandidateV1): string {
  return `${candidate.operatorId}:${candidate.outputPortId}`;
}

export function createFinalDecisionOwner(options: {
  readonly flowOwner: FlowCanvasOwner;
  readonly api: ApiTransport;
  readonly initial: WorkspaceDecisionConfigurationV1 | null;
}): FinalDecisionOwner {
  if (!options.api.post) throw new TypeError('最终判定需要 shared ApiTransport POST。');
  let applied = cloneDecision(options.initial);
  let draft = cloneDecision(options.initial);
  let disposed = false;
  let generation = 0;
  let validationController: AbortController | null = null;
  const state = reactive<MutableProjection>({
    phase: 'idle',
    draft,
    candidates: Object.freeze([]),
    issues: Object.freeze([]),
    draftRevision: 0,
    flowRevision: options.flowOwner.projection.runtime?.flowRevision ?? 0,
    dirty: false,
    draftFingerprint: fingerprint(draft),
    message: '正在读取后端最终判定候选。'
  });

  function updateDraft(next: WorkspaceDecisionConfigurationV1 | null): void {
    draft = cloneDecision(next);
    state.draft = draft;
    state.draftRevision += 1;
    state.dirty = fingerprint(draft) !== fingerprint(applied);
    state.draftFingerprint = fingerprint(draft);
    state.phase = 'idle';
    state.message = '最终判定草稿已修改，应用前需通过后端校验。';
  }

  async function validate(): Promise<boolean> {
    if (disposed) return false;
    const operation = ++generation;
    validationController?.abort('decision-validation-superseded');
    const controller = new AbortController();
    validationController = controller;
    state.phase = 'validating';
    state.message = '正在由后端校验候选与规则。';
    try {
      const flow = {
        ...options.flowOwner.projection.draft,
        decisionConfiguration: encodeWorkspaceDecisionConfigurationV1(draft)
      };
      const payload = record(await options.api.post!('inspection/decision-configuration/validate', flow, {
        signal: controller.signal
      }));
      if (disposed || operation !== generation) return false;
      state.candidates = decodeCandidates(field(payload, 'eligibleOutputs'));
      state.issues = decodeIssues(field(payload, 'issues'));
      const valid = field(payload, 'isValid') === true && state.issues.length === 0;
      state.phase = valid ? 'valid' : 'invalid';
      state.message = valid
        ? '后端校验通过，可应用到工程草稿。'
        : state.issues[0]?.message ?? '最终判定未配置或配置无效。';
      return valid;
    } catch (error) {
      if (disposed || operation !== generation || error instanceof ApiAbortError || controller.signal.aborted) return false;
      state.phase = 'error';
      state.message = `最终判定校验失败：${error instanceof Error ? error.message : '响应不可用。'}`;
      return false;
    } finally {
      if (validationController === controller) validationController = null;
    }
  }

  const stopWatch = watch(
    () => options.flowOwner.projection.runtime?.flowRevision ?? 0,
    revision => {
      if (disposed || revision === state.flowRevision) return;
      state.flowRevision = revision;
      if (state.phase === 'valid') {
        state.phase = 'idle';
        state.message = '流程已变化，候选与配置需要重新校验。';
      }
      void validate();
    },
    { flush: 'post' }
  );

  const owner: FinalDecisionOwner = Object.freeze({
    projection: readonly(state),
    selectCandidate(key: string): void {
      if (disposed) return;
      const candidate = state.candidates.find(item => candidateKey(item) === key);
      if (!candidate) {
        state.message = '所选候选已失效，请重新校验流程。';
        return;
      }
      const binding: WorkspaceFinalDecisionBindingV1 = Object.freeze({
        sourceOperatorId: candidate.operatorId,
        sourceOutputPortId: candidate.outputPortId,
        sourceOutputName: candidate.outputName,
        dataType: enumValue(candidate.dataType),
        rule: enumValue(candidate.rule),
        trueMeansOk: candidate.defaultTrueMeansOk ?? true,
        okValue: candidate.requiredOkValue ?? candidate.defaultOkValue,
        ngValue: candidate.requiredNgValue ?? candidate.defaultNgValue,
        comparator: candidate.rule === 'NumericComparison' ? enumValue('GreaterThanOrEqual') : null,
        threshold: candidate.rule === 'NumericComparison' ? 0 : null,
        opaquePassthrough: Object.freeze({})
      });
      updateDraft(Object.freeze({
        finalDecisionBinding: binding,
        missingDecisionPolicy: draft?.missingDecisionPolicy ?? enumValue('Undetermined'),
        opaquePassthrough: draft?.opaquePassthrough ?? Object.freeze({})
      }));
      void validate();
    },
    patchBinding(patch: Readonly<{
      trueMeansOk?: boolean;
      okValue?: string | null;
      ngValue?: string | null;
      comparator?: WorkspaceDecisionComparator | null;
      threshold?: number | null;
    }>): void {
      if (disposed || !draft?.finalDecisionBinding) return;
      const nextComparator = Object.prototype.hasOwnProperty.call(patch, 'comparator')
        ? patch.comparator === null || patch.comparator === undefined
          ? null
          : enumValue(patch.comparator)
        : draft.finalDecisionBinding.comparator;
      updateDraft(Object.freeze({
        ...draft,
        finalDecisionBinding: Object.freeze({
          ...draft.finalDecisionBinding,
          ...patch,
          comparator: nextComparator
        })
      }));
    },
    setMissingPolicy(policy: WorkspaceMissingDecisionPolicy): void {
      if (disposed) return;
      updateDraft(Object.freeze({
        finalDecisionBinding: draft?.finalDecisionBinding ?? null,
        missingDecisionPolicy: enumValue(policy),
        opaquePassthrough: draft?.opaquePassthrough ?? Object.freeze({})
      }));
    },
    clearBinding(): void {
      if (disposed) return;
      updateDraft(Object.freeze({
        finalDecisionBinding: null,
        missingDecisionPolicy: draft?.missingDecisionPolicy ?? enumValue('Undetermined'),
        opaquePassthrough: draft?.opaquePassthrough ?? Object.freeze({})
      }));
    },
    validate,
    async apply(): Promise<boolean> {
      if (disposed || !(await validate())) return false;
      const result = options.flowOwner.commands.patchDecisionConfiguration(draft);
      if (!result.ok && result.code !== 'no-change') {
        state.phase = 'error';
        state.message = result.message;
        return false;
      }
      applied = cloneDecision(draft);
      state.dirty = false;
      state.flowRevision = options.flowOwner.projection.runtime?.flowRevision ?? state.flowRevision;
      state.message = '最终判定已应用，需保存工程后正式生效。';
      return true;
    },
    cancel(): void {
      if (disposed) return;
      updateDraft(applied);
      state.dirty = false;
      state.issues = Object.freeze([]);
      state.message = '已取消本次最终判定修改。';
      void validate();
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      stopWatch();
      validationController?.abort('final-decision-owner-disposed');
      validationController = null;
      state.phase = 'disposed';
      state.candidates = Object.freeze([]);
      state.message = '最终判定 owner 已释放。';
    }
  });
  void validate();
  return owner;
}

export { candidateKey as finalDecisionCandidateKey };
