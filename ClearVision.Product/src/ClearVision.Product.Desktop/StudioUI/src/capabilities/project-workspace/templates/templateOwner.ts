import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import type { ApiTransport } from '@/platform/api';
import { ApiConflictError, ApiForbiddenError, ApiNetworkError, ApiAbortError, ApiHttpError } from '@/platform/api';
import type { ReadQueryClient } from '@/platform/query';
import type { FlowCanvasOwner } from '../flow';
import type { OperatorCatalogItem } from '@/capabilities/operators-read/operatorContracts';
import {
  convertTemplateFlow,
  decodeFlowTemplate,
  templateMatches,
  type FlowTemplateV1,
  type TemplateDiagnostic,
  type TemplateFlowConversion
} from './templateContracts';
import { createTemplateDetailPath, createTemplateListQuery } from './templateQueries';

export type TemplateOwnerPhase = 'idle' | 'loading' | 'ready' | 'applying' | 'saving' | 'unknown-outcome' | 'error' | 'disposed';
export type TemplateWriteStatus = 'idle' | 'saving' | 'saved' | 'unknown-outcome' | 'failed';

export interface TemplateOwnerProjection {
  readonly phase: TemplateOwnerPhase;
  readonly templates: readonly FlowTemplateV1[];
  readonly filteredTemplates: readonly FlowTemplateV1[];
  readonly search: string;
  readonly industry: string;
  readonly industries: readonly string[];
  readonly selectedTemplateId: string | null;
  readonly selectedTemplate: FlowTemplateV1 | null;
  readonly conversion: TemplateFlowConversion | null;
  readonly diagnostics: readonly TemplateDiagnostic[];
  readonly listMessage: string | null;
  readonly message: string;
  readonly writeStatus: TemplateWriteStatus;
  readonly canWrite: boolean;
  readonly errorCode: string | null;
}

type MutableProjection = { -readonly [Key in keyof TemplateOwnerProjection]: TemplateOwnerProjection[Key] };

export interface TemplateWriteInput {
  readonly name: string;
  readonly description: string;
  readonly industry: string;
  readonly tags: readonly string[];
}

export interface TemplateOwner {
  readonly projection: DeepReadonly<TemplateOwnerProjection>;
  setSearch(value: string): void;
  setIndustry(value: string): void;
  select(id: string | null): Promise<void>;
  refresh(): Promise<void>;
  applySelected(options?: Readonly<{ confirmReplace?: boolean }>): Promise<boolean>;
  saveAs(input: TemplateWriteInput): Promise<boolean>;
  updateSelected(input: TemplateWriteInput): Promise<boolean>;
  dispose(reason?: string): void;
}

function queryMessage(phase: string, failure: { readonly message: string } | null | undefined): string | null {
  if (phase === 'loading') return '正在读取流程模板。';
  if (phase === 'unauthorized') return '当前会话不可用，无法读取模板。';
  if (phase === 'forbidden') return '当前账号无权读取模板。';
  if (phase === 'empty') return '当前没有可用的流程模板。';
  return failure?.message ?? null;
}

function errorCode(error: unknown): string | null {
  if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return null;
  const source = error.payload as Readonly<Record<string, unknown>>;
  const value = source.code ?? source.Code;
  return typeof value === 'string' && value.trim() ? value.trim().toUpperCase() : null;
}

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message.trim() ? error.message : '模板操作失败。';
}

function normalizeWriteInput(input: TemplateWriteInput): TemplateWriteInput | null {
  const name = input.name.trim();
  if (!name) return null;
  return Object.freeze({
    name,
    description: input.description.trim(),
    industry: input.industry.trim(),
    tags: Object.freeze([...new Set(input.tags.map(tag => tag.trim()).filter(Boolean))])
  });
}

export function createTemplateOwner(options: {
  readonly projectId: string;
  readonly projectName: string;
  readonly flowOwner: FlowCanvasOwner;
  readonly queries: ReadQueryClient;
  readonly api: ApiTransport;
  readonly canWrite: boolean;
  readonly isDirty: () => boolean;
}): TemplateOwner {
  const industryRef = { value: '' };
  const query = createTemplateListQuery(options.queries, () => industryRef.value);
  const state = reactive<MutableProjection>({
    phase: 'idle',
    templates: Object.freeze([]),
    filteredTemplates: Object.freeze([]),
    search: '',
    industry: '',
    industries: Object.freeze([]),
    selectedTemplateId: null,
    selectedTemplate: null,
    conversion: null,
    diagnostics: Object.freeze([]),
    listMessage: null,
    message: '选择模板后可将其装载到当前流程草稿。',
    writeStatus: 'idle',
    canWrite: options.canWrite,
    errorCode: null
  });
  let disposed = false;
  let detailGeneration = 0;
  let writeGeneration = 0;

  function assertActive(): void {
    if (disposed) throw new Error('模板工作台已关闭。');
  }

  function updateList(value = query.state.value): void {
    if (disposed) return;
    const templates = value.data ?? Object.freeze([]);
    state.templates = templates;
    state.industries = Object.freeze([...new Set(templates.map(template => template.industry).filter(Boolean))].sort());
    state.listMessage = queryMessage(value.phase, value.failure);
    state.filteredTemplates = Object.freeze(templates.filter(template => templateMatches(template, state.search, state.industry)));
    if (state.selectedTemplateId && !templates.some(template => template.id === state.selectedTemplateId)) {
      state.selectedTemplateId = null;
      state.selectedTemplate = null;
      state.conversion = null;
    }
    if (value.phase === 'loading') state.phase = 'loading';
    else if (value.phase === 'success' || value.phase === 'empty' || value.phase === 'stale') state.phase = 'ready';
    else if (value.phase !== 'idle') state.phase = 'error';
  }

  function currentCatalog(): readonly OperatorCatalogItem[] {
    return options.flowOwner.projection.catalog.operators;
  }

  const stopQueryWatch = watch(() => query.state.value, updateList, { immediate: true });

  const owner: TemplateOwner = Object.freeze({
    projection: readonly(state),
    setSearch(value: string): void {
      assertActive();
      state.search = value;
      state.filteredTemplates = Object.freeze(state.templates.filter(template => templateMatches(template, value, state.industry)));
    },
    setIndustry(value: string): void {
      assertActive();
      state.industry = value;
      industryRef.value = value;
      state.filteredTemplates = Object.freeze(state.templates.filter(template => templateMatches(template, state.search, value)));
      void query.refresh({ force: true });
    },
    async select(id: string | null): Promise<void> {
      assertActive();
      const generation = ++detailGeneration;
      state.selectedTemplateId = id;
      state.selectedTemplate = null;
      state.conversion = null;
      state.diagnostics = Object.freeze([]);
      if (!id) {
        state.message = '选择模板后可查看内容并装载到流程草稿。';
        return;
      }
      const listed = state.templates.find(template => template.id === id);
      try {
        state.phase = 'loading';
        const payload = typeof options.api.get === 'function'
          ? await options.api.get<unknown>(createTemplateDetailPath(id))
          : listed;
        if (disposed || generation !== detailGeneration) return;
        const template = decodeFlowTemplate(payload ?? listed);
        state.selectedTemplate = template;
        state.message = `已选择模板：${template.name}`;
        state.phase = 'ready';
      } catch (error) {
        if (disposed || generation !== detailGeneration) return;
        state.phase = 'error';
        state.errorCode = errorCode(error);
        state.message = `模板详情读取失败：${errorMessage(error)}`;
      }
    },
    async refresh(): Promise<void> {
      assertActive();
      await query.refresh({ force: true });
    },
    async applySelected(applyOptions: Readonly<{ confirmReplace?: boolean }> = {}): Promise<boolean> {
      assertActive();
      const template = state.selectedTemplate;
      if (!template) {
        state.message = '请先选择一个模板。';
        return false;
      }
      if (options.flowOwner.projection.mutationGate !== 'editable') {
        state.message = '当前流程为只读或运行中，不能应用模板。';
        return false;
      }
      if (options.isDirty() && applyOptions.confirmReplace !== true) {
        state.message = '当前流程存在未保存修改；确认后才会替换草稿。';
        state.diagnostics = Object.freeze([{ severity: 'warning', code: 'template-dirty-replace-confirmation', path: '$', message: state.message }]);
        return false;
      }
      try {
        state.phase = 'applying';
        state.errorCode = null;
        if (currentCatalog().length === 0) await options.flowOwner.refreshOperators(true);
        const conversion = convertTemplateFlow(template, options.flowOwner.projection.catalog.operators);
        state.conversion = conversion;
        state.diagnostics = conversion.diagnostics;
        if (!conversion.flow) {
          state.phase = 'error';
          state.message = '模板无法安全应用；请先处理诊断信息。';
          return false;
        }
        options.flowOwner.replaceFlow(conversion.flow, options.projectName);
        state.message = `模板已应用到流程草稿：${template.name}。请显式保存工程。`;
        state.phase = 'ready';
        return true;
      } catch (error) {
        state.phase = 'error';
        state.errorCode = errorCode(error);
        state.message = `应用模板失败：${errorMessage(error)}`;
        return false;
      }
    },
    async saveAs(input: TemplateWriteInput): Promise<boolean> {
      assertActive();
      if (!state.canWrite) {
        state.message = '当前账号没有创建模板的权限。';
        return false;
      }
      if (typeof options.api.post !== 'function') {
        state.message = '当前 API 不支持创建模板。';
        return false;
      }
      const normalizedInput = normalizeWriteInput(input);
      if (!normalizedInput) {
        state.message = '模板名称不能为空。';
        return false;
      }
      const generation = ++writeGeneration;
      state.phase = 'saving';
      state.writeStatus = 'saving';
      state.errorCode = null;
      try {
        const flowData = options.flowOwner.projection.draft as unknown as Readonly<Record<string, unknown>>;
        const response = await options.api.post<unknown>('templates', { ...normalizedInput, flowData });
        if (disposed || generation !== writeGeneration) return false;
        const created = decodeFlowTemplate(response);
        state.selectedTemplateId = created.id;
        state.selectedTemplate = created;
        state.writeStatus = 'saved';
        state.phase = 'ready';
        state.message = `模板已保存：${created.name}。工程草稿未自动保存。`;
        await query.refresh({ force: true });
        return true;
      } catch (error) {
        if (disposed || generation !== writeGeneration) return false;
        state.writeStatus = error instanceof ApiNetworkError || error instanceof ApiAbortError ? 'unknown-outcome' : 'failed';
        state.phase = state.writeStatus === 'unknown-outcome' ? 'unknown-outcome' : 'error';
        state.errorCode = errorCode(error);
        state.message = state.writeStatus === 'unknown-outcome'
          ? '模板保存结果未知；后端没有提供可安全重放的 operation identity，请先刷新模板列表核对。'
          : `模板保存失败：${errorMessage(error)}`;
        return false;
      }
    },
    async updateSelected(input: TemplateWriteInput): Promise<boolean> {
      assertActive();
      if (!state.canWrite) {
        state.message = '当前账号没有更新模板的权限。';
        return false;
      }
      const id = state.selectedTemplateId;
      if (!id || typeof options.api.put !== 'function') {
        state.message = '请先选择可更新的模板。';
        return false;
      }
      const normalizedInput = normalizeWriteInput(input);
      if (!normalizedInput) {
        state.message = '模板名称不能为空。';
        return false;
      }
      const generation = ++writeGeneration;
      state.phase = 'saving';
      state.writeStatus = 'saving';
      state.errorCode = null;
      try {
        const flowData = options.flowOwner.projection.draft as unknown as Readonly<Record<string, unknown>>;
        const response = await options.api.put<unknown>(createTemplateDetailPath(id), { ...normalizedInput, flowData });
        if (disposed || generation !== writeGeneration) return false;
        const updated = decodeFlowTemplate(response);
        state.selectedTemplate = updated;
        state.writeStatus = 'saved';
        state.phase = 'ready';
        state.message = `模板已更新：${updated.name}。工程草稿未自动保存。`;
        await query.refresh({ force: true });
        return true;
      } catch (error) {
        if (disposed || generation !== writeGeneration) return false;
        state.writeStatus = error instanceof ApiNetworkError || error instanceof ApiAbortError ? 'unknown-outcome' : 'failed';
        state.phase = state.writeStatus === 'unknown-outcome' ? 'unknown-outcome' : 'error';
        state.errorCode = errorCode(error);
        state.message = state.writeStatus === 'unknown-outcome'
          ? '模板更新结果未知；请刷新列表核对服务器状态。'
          : error instanceof ApiForbiddenError
            ? '后端拒绝模板更新；请确认 Engineer/Admin 权限。'
            : error instanceof ApiConflictError
              ? '模板更新发生冲突；请重新读取模板后再操作。'
              : `模板更新失败：${errorMessage(error)}`;
        return false;
      }
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      detailGeneration += 1;
      writeGeneration += 1;
      stopQueryWatch();
      query.dispose();
      state.phase = 'disposed';
      state.message = '模板工作台已关闭。';
      state.templates = Object.freeze([]);
      state.filteredTemplates = Object.freeze([]);
      state.selectedTemplate = null;
      state.conversion = null;
    }
  });
  void query.refresh();
  return owner;
}
