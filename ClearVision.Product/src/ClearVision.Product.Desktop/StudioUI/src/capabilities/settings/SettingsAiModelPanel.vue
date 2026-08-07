<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, reactive, shallowRef, watch } from 'vue';
import {
  CvButton,
  CvDescriptionList,
  CvField,
  CvInlineAlert,
  CvPanel,
  CvSelect,
  CvStatusBadge,
  type CvDescriptionItem,
  type CvSelectOption
} from '@/design-system';
import type {
  AiModelMutationRequestV1,
  AiApiKeyOperationV1,
  AiBaseUrlOperationV1,
  AiReasoningSupportRequestV1
} from './apiAdapter';
import type {
  AiModelProjectionV1,
  AiModelPublicProjectionV1,
  AiModelConnectionTestProjectionV1,
  AiReasoningSupportProjectionV1,
  JsonRecord
} from './decoder';
import type { SettingsWriteResult } from './contracts';
import type { SettingsOwner } from './settingsOwner';
import { settingsFeedbackForResult, type SettingsFeedback } from './settingsViewModel';

const props = defineProps<{
  owner: SettingsOwner;
  role: string | null;
}>();

type DraftState = {
  name: string;
  displayName: string;
  provider: string;
  protocol: string;
  wireApi: string;
  authMode: string;
  authHeaderName: string;
  model: string;
  baseUrl: string;
  timeoutMs: string;
  priority: string;
  isEnabled: boolean;
  roleBindings: string[];
  remark: string;
  reasoningMode: string;
  reasoningEffort: string;
};

const canRead = computed(() => props.role === 'Admin' || props.role === 'Engineer');
const canManage = computed(() => props.role === 'Admin');
const projection = computed(() => props.owner.projection.aiModels);
const models = computed(() => projection.value?.items ?? []);
const phase = shallowRef<'idle' | 'loading' | 'ready' | 'forbidden' | 'error'>('idle');
const readMessage = shallowRef<string | null>(null);
const feedback = shallowRef<SettingsFeedback | null>(null);
const connectionResult = shallowRef<AiModelConnectionTestProjectionV1 | null>(null);
const reasoningSupport = shallowRef<AiReasoningSupportProjectionV1 | null>(null);
const selectedId = shallowRef<string | null>(null);
const mutationBusy = shallowRef(false);
const readBusy = shallowRef(false);
const reasoningBusy = shallowRef(false);
const requestVersion = shallowRef(0);
const reasoningRequestVersion = shallowRef(0);
const apiKeyMode = shallowRef<AiApiKeyOperationV1>('keep');
const baseUrlMode = shallowRef<AiBaseUrlOperationV1>('preserve');
const apiKeyDraft = shallowRef('');
const apiKeyBaseline = shallowRef<AiApiKeyOperationV1>('keep');
const apiKeyOperationOverridden = shallowRef(false);
const reasoningSupportIdentity = shallowRef<string | null>(null);
const providerPreset = shallowRef('custom');
const draftBaseline = shallowRef<Readonly<Record<string, unknown>>>({});

const presetOverrides = reactive({
  protocol: false,
  wireApi: false,
  authMode: false,
  authHeaderName: false
});

const draft = reactive<DraftState>(emptyDraft());

const providerPresetOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'custom', label: 'Custom provider' },
  { value: 'openai', label: 'OpenAI' },
  { value: 'openai-compatible', label: 'OpenAI-compatible' },
  { value: 'anthropic', label: 'Anthropic' },
  { value: 'azure-openai', label: 'Azure OpenAI' },
  { value: 'ollama', label: 'Ollama' }
]);
const baseUrlOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'preserve', label: 'Preserve current URL (default)' },
  { value: 'replace', label: 'Replace URL' },
  { value: 'clear', label: 'Clear URL' }
]);
const protocolOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'openai_compatible', label: 'OpenAI Compatible' },
  { value: 'anthropic', label: 'Anthropic' },
  { value: 'azure_openai', label: 'Azure OpenAI' },
  { value: 'ollama_native', label: 'Ollama Native' }
]);
const wireApiOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'chat_completions', label: 'Chat Completions' },
  { value: 'responses', label: 'Responses' }
]);
const authModeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'bearer', label: 'Bearer' },
  { value: 'header_key', label: 'Header key' },
  { value: 'none', label: 'None' }
]);
const apiKeyOptions = computed<readonly CvSelectOption[]>(() => {
  if (draft.authMode === 'none') {
    return Object.freeze([{ value: 'clear', label: '不使用 API key' }]);
  }
  if (isNewModel.value) {
    return Object.freeze([
      { value: 'replace', label: '创建并写入 key' },
      { value: 'clear', label: '暂不配置 key（稍后补充）' }
    ]);
  }
  return Object.freeze([
    { value: 'keep', label: '保留当前 key（默认）' },
    { value: 'replace', label: '替换 key' },
    { value: 'clear', label: '清除 key' }
  ]);
});
const roleOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'generation', label: 'generation' },
  { value: 'planner', label: 'planner' },
  { value: 'vision-agent-shadow-eval', label: 'shadow-eval' },
  { value: 'reasoning', label: 'reasoning' },
  { value: 'vision', label: 'vision' },
  { value: 'fallback', label: 'fallback' },
  { value: 'validation', label: 'validation' }
]);
const reasoningModeOptions = computed<readonly CvSelectOption[]>(() => {
  const allowed = displayReasoningSupport.value?.allowedModes ?? ['auto', 'off', 'on'];
  return allowed.map(value => ({ value, label: value === 'auto' ? 'Auto' : value === 'off' ? 'Off' : 'On' }));
});
const reasoningEffortOptions = computed<readonly CvSelectOption[]>(() => {
  const allowed = displayReasoningSupport.value?.allowedEfforts ?? ['low', 'medium', 'high', 'xhigh'];
  return allowed.map(value => ({ value, label: value === 'xhigh' ? 'XHigh' : value[0]!.toUpperCase() + value.slice(1) }));
});

const selectedModel = computed(() => models.value.find(item => item.id === selectedId.value) ?? null);
const selectedFullModel = computed(() => {
  const value = selectedModel.value;
  return isFullModel(value) ? value : null;
});
const isNewModel = computed(() => selectedId.value === null);
const canEditSelected = computed(() => canManage.value && (isNewModel.value || selectedFullModel.value !== null));
const dirty = computed(() => {
  const current = snapshotDraft();
  return JSON.stringify(current) !== JSON.stringify(draftBaseline.value) ||
    apiKeyMode.value !== apiKeyBaseline.value ||
    apiKeyDraft.value.length > 0 ||
    baseUrlMode.value !== 'preserve';
});
const pending = computed(() => mutationBusy.value);
const hasAiMutationUnknown = computed(() => props.owner.projection.unknownOutcomeKeys.includes('ai-models'));
const hasSelectedTestUnknown = computed(() => selectedId.value !== null && props.owner.projection.unknownOutcomeKeys.includes(`ai-model-test:${selectedId.value}`));
const selectedSafeDetails = computed<readonly CvDescriptionItem[]>(() => {
  const model = selectedModel.value;
  if (!model) return [];
  return [
    { key: 'provider', label: 'Provider', value: model.provider },
    { key: 'model', label: '模型名', value: model.model || '未设置' },
    { key: 'role', label: '主角色', value: model.modelRole ?? '未声明' },
    { key: 'enabled', label: '状态', value: model.isEnabled ? '启用' : '停用' },
    { key: 'active', label: '激活', value: model.isActive ? '当前激活' : '未激活' },
    { key: 'capabilities', label: '能力元数据', value: capabilitySummary(model.capabilities) }
  ];
});
const displayReasoningSupport = computed(() => {
  const identity = currentReasoningIdentity();
  if (reasoningSupportIdentity.value !== identity) return null;
  return reasoningSupport.value ?? supportFromRecord(selectedFullModel.value?.reasoningSupport);
});

let detachPanelState = props.owner.registerPanelState('ai-model', () => ({
  dirty: dirty.value,
  pending: pending.value
}));

function emptyDraft(): DraftState {
  return {
    name: '',
    displayName: '',
    provider: 'OpenAI Compatible',
    protocol: 'openai_compatible',
    wireApi: 'chat_completions',
    authMode: 'bearer',
    authHeaderName: 'Authorization',
    model: '',
    baseUrl: '',
    timeoutMs: '120000',
    priority: '100',
    isEnabled: true,
    roleBindings: ['generation'],
    remark: '',
    reasoningMode: 'auto',
    reasoningEffort: 'medium'
  };
}

type ProviderPreset = {
  readonly provider: string;
  readonly protocol: string;
  readonly wireApi: string;
  readonly authMode: string;
  readonly authHeaderName: string;
};

const providerPresets: Readonly<Record<string, ProviderPreset>> = Object.freeze({
  openai: { provider: 'OpenAI', protocol: 'openai_compatible', wireApi: 'chat_completions', authMode: 'bearer', authHeaderName: 'Authorization' },
  'openai-compatible': { provider: 'OpenAI Compatible', protocol: 'openai_compatible', wireApi: 'chat_completions', authMode: 'bearer', authHeaderName: 'Authorization' },
  anthropic: { provider: 'Anthropic', protocol: 'anthropic', wireApi: 'chat_completions', authMode: 'header_key', authHeaderName: 'x-api-key' },
  'azure-openai': { provider: 'Azure OpenAI', protocol: 'azure_openai', wireApi: 'chat_completions', authMode: 'header_key', authHeaderName: 'api-key' },
  ollama: { provider: 'Ollama', protocol: 'ollama_native', wireApi: 'chat_completions', authMode: 'none', authHeaderName: '' }
});

function currentReasoningIdentity(): string {
  return JSON.stringify([
    draft.provider.trim(),
    draft.model.trim(),
    baseUrlMode.value,
    baseUrlMode.value === 'replace' ? draft.baseUrl.trim() : '',
    draft.protocol
  ]);
}

function isRedactedPlaceholder(value: string | null | undefined): boolean {
  const normalized = value?.trim() ?? '';
  if (!normalized) return false;
  const lower = normalized.toLowerCase();
  return lower.includes('<redacted') ||
    lower.includes('[redacted') ||
    lower.includes('<masked') ||
    lower.includes('[masked') ||
    lower === 'redacted' ||
    lower === 'masked' ||
    /[*#\u2022\u00b7\u25cf\u25cb]{4}/u.test(normalized);
}

function resetPresetOverrides(): void {
  presetOverrides.protocol = false;
  presetOverrides.wireApi = false;
  presetOverrides.authMode = false;
  presetOverrides.authHeaderName = false;
}

function inferProviderPreset(provider: string, protocol: string): string {
  const normalized = provider.trim().toLowerCase();
  if (normalized.includes('anthropic') || protocol === 'anthropic') return 'anthropic';
  if (normalized.includes('azure') || protocol === 'azure_openai') return 'azure-openai';
  if (normalized.includes('ollama') || protocol === 'ollama_native') return 'ollama';
  if (normalized === 'openai' || normalized === 'openai api') return 'openai';
  if (normalized.includes('openai')) return 'openai-compatible';
  return 'custom';
}

function setProviderPreset(value: string): void {
  const preset = providerPresets[value];
  providerPreset.value = value in providerPresets ? value : 'custom';
  if (!preset) return;
  draft.provider = preset.provider;
  applyProviderPresetDefaults(value);
  apiKeyOperationOverridden.value = false;
  synchronizeApiKeyOperation();
}

function applyProviderPresetDefaults(value: string): void {
  const preset = providerPresets[value];
  if (!preset) return;
  if (!presetOverrides.protocol) draft.protocol = preset.protocol;
  if (!presetOverrides.wireApi) draft.wireApi = preset.wireApi;
  if (!presetOverrides.authMode) draft.authMode = preset.authMode;
  if (!presetOverrides.authHeaderName) draft.authHeaderName = preset.authHeaderName;
}

function defaultApiKeyOperation(newModel: boolean, authMode: string): AiApiKeyOperationV1 {
  if (authMode === 'none') return 'clear';
  return newModel ? 'replace' : 'keep';
}

function synchronizeApiKeyOperation(): void {
  if (draft.authMode === 'none' || !apiKeyOperationOverridden.value) {
    apiKeyMode.value = defaultApiKeyOperation(isNewModel.value, draft.authMode);
    apiKeyDraft.value = '';
  }
}

function onProviderInput(value: string): void {
  draft.provider = value;
  const inferred = inferProviderPreset(value, '');
  providerPreset.value = inferred;
  if (inferred !== 'custom') applyProviderPresetDefaults(inferred);
  apiKeyOperationOverridden.value = false;
  synchronizeApiKeyOperation();
}

function markPresetOverride(field: keyof typeof presetOverrides): void {
  presetOverrides[field] = true;
}

function onAuthModeChanged(): void {
  markPresetOverride('authMode');
  apiKeyOperationOverridden.value = false;
  synchronizeApiKeyOperation();
}

function isFullModel(value: AiModelPublicProjectionV1 | null | undefined): value is AiModelProjectionV1 {
  return value !== null && value !== undefined && 'name' in value;
}

function invalidateReasoningRequest(): void {
  reasoningRequestVersion.value += 1;
  reasoningBusy.value = false;
}

function stringFromRecord(value: JsonRecord | null | undefined, key: string, fallback: string): string {
  const item = value?.[key];
  return typeof item === 'string' && item.trim() ? item : fallback;
}

function stringArrayFromRecord(value: JsonRecord | null | undefined, key: string): readonly string[] {
  const item = value?.[key];
  return Array.isArray(item) ? item.filter((entry): entry is string => typeof entry === 'string') : [];
}

function supportFromRecord(value: JsonRecord | null | undefined): AiReasoningSupportProjectionV1 | null {
  if (!value) return null;
  return {
    familyId: stringFromRecord(value, 'familyId', 'unknown'),
    familyName: stringFromRecord(value, 'familyName', 'Unknown'),
    allowedModes: stringArrayFromRecord(value, 'allowedModes'),
    allowedEfforts: stringArrayFromRecord(value, 'allowedEfforts'),
    helpText: stringFromRecord(value, 'helpText', ''),
    supportsExplicitMode: value.supportsExplicitMode === true,
    supportsEffort: value.supportsEffort === true,
    isModelLockedOn: value.isModelLockedOn === true,
    defaultMode: stringFromRecord(value, 'defaultMode', 'auto')
  };
}

function capabilitySummary(value: JsonRecord | null): string {
  if (!value) return '未返回';
  const entries = Object.entries(value).map(([key, item]) => `${key}=${String(item)}`);
  return entries.length > 0 ? entries.join(', ') : '未声明';
}

function snapshotDraft(): Readonly<Record<string, unknown>> {
  return {
    name: draft.name,
    displayName: draft.displayName,
    provider: draft.provider,
    protocol: draft.protocol,
    wireApi: draft.wireApi,
    authMode: draft.authMode,
    authHeaderName: draft.authHeaderName,
    model: draft.model,
    baseUrl: draft.baseUrl,
    timeoutMs: draft.timeoutMs,
    priority: draft.priority,
    isEnabled: draft.isEnabled,
    roleBindings: [...draft.roleBindings],
    remark: draft.remark,
    reasoningMode: draft.reasoningMode,
    reasoningEffort: draft.reasoningEffort
  };
}

function resetDraft(value: AiModelPublicProjectionV1 | null): void {
  invalidateReasoningRequest();
  const next = emptyDraft();
  baseUrlMode.value = 'preserve';
  resetPresetOverrides();
  if (isFullModel(value)) {
    next.name = value.name ?? value.displayName;
    next.displayName = value.displayName;
    next.provider = value.provider;
    next.protocol = value.protocol ?? next.protocol;
    next.wireApi = value.wireApi ?? next.wireApi;
    next.authMode = value.authMode ?? next.authMode;
    next.authHeaderName = value.authHeaderName ?? next.authHeaderName;
    next.model = value.model;
    next.baseUrl = isRedactedPlaceholder(value.baseUrl) ? '' : value.baseUrl ?? '';
    next.timeoutMs = String(value.timeoutMs ?? 120000);
    next.priority = String(value.priority ?? 100);
    next.isEnabled = value.isEnabled;
    next.roleBindings = value.roleBindings.length > 0 ? [...value.roleBindings] : ['generation'];
    next.remark = value.remark ?? '';
    next.reasoningMode = stringFromRecord(value.reasoning, 'mode', 'auto');
    next.reasoningEffort = stringFromRecord(value.reasoning, 'effort', 'medium');
    reasoningSupport.value = supportFromRecord(value.reasoningSupport);
  } else if (value) {
    next.displayName = value.displayName;
    next.provider = value.provider;
    next.model = value.model;
    next.roleBindings = value.modelRole ? [value.modelRole] : ['generation'];
    next.isEnabled = value.isEnabled;
    reasoningSupport.value = null;
  } else {
    reasoningSupport.value = null;
  }
  Object.assign(draft, next);
  providerPreset.value = inferProviderPreset(draft.provider, draft.protocol);
  draftBaseline.value = snapshotDraft();
  apiKeyDraft.value = '';
  apiKeyMode.value = defaultApiKeyOperation(value === null, next.authMode);
  apiKeyBaseline.value = apiKeyMode.value;
  apiKeyOperationOverridden.value = false;
  connectionResult.value = null;
  reasoningSupportIdentity.value = currentReasoningIdentity();
  feedback.value = null;
}

function bindPanelState(owner: SettingsOwner): void {
  detachPanelState();
  detachPanelState = owner.registerPanelState('ai-model', () => ({
    dirty: dirty.value,
    pending: pending.value
  }));
}

function clearSecret(): void {
  apiKeyDraft.value = '';
  apiKeyOperationOverridden.value = false;
  apiKeyMode.value = defaultApiKeyOperation(isNewModel.value, draft.authMode);
}

function resultFeedback<T>(result: SettingsWriteResult<T>): SettingsFeedback {
  return settingsFeedbackForResult(result);
}

function reportUnknownMutation(message = 'AI model mutation outcome is unknown; refresh AI authority before retrying the mutation.'): void {
  feedback.value = {
    kind: 'unknown',
    message,
    savedLabel: 'unknown',
    effectiveLabel: 'unknown',
    restartLabel: 'unknown'
  };
}

function chooseModel(id: string): void {
  if (id === selectedId.value) return;
  if (dirty.value && !window.confirm('当前 AI 模型存在未保存修改，切换将放弃这些修改。继续？')) return;
  selectedId.value = id;
  resetDraft(models.value.find(item => item.id === id) ?? null);
}

function beginNew(): void {
  if (!canManage.value || mutationBusy.value) return;
  if (dirty.value && !window.confirm('当前 AI 模型存在未保存修改，创建新模型将放弃这些修改。继续？')) return;
  selectedId.value = null;
  resetDraft(null);
}

function toggleRole(role: string, event: Event): void {
  const checked = (event.target as HTMLInputElement).checked;
  const next = new Set(draft.roleBindings);
  if (checked) next.add(role);
  else next.delete(role);
  draft.roleBindings = next.size > 0 ? [...next] : ['generation'];
}

function setApiKeyMode(value: string): void {
  const next = draft.authMode === 'none'
    ? 'clear'
    : value === 'replace' || value === 'clear'
      ? value
      : isNewModel.value ? 'replace' : 'keep';
  apiKeyMode.value = next;
  apiKeyOperationOverridden.value = true;
  if (next !== 'replace') apiKeyDraft.value = '';
}

function setBaseUrlMode(value: string): void {
  const next: AiBaseUrlOperationV1 = value === 'replace' || value === 'clear' ? value : 'preserve';
  baseUrlMode.value = next;
  if (next !== 'replace') draft.baseUrl = '';
}

function validateDraft(): string | null {
  if (!draft.displayName.trim() && !draft.name.trim()) return '请填写模型名称。';
  if (!draft.provider.trim()) return '请填写 Provider。';
  if (!draft.model.trim()) return '请填写模型名。';
  const timeout = Number(draft.timeoutMs);
  if (!Number.isInteger(timeout) || timeout <= 0) return 'Timeout 必须是正整数毫秒。';
  const priority = Number(draft.priority);
  if (!Number.isInteger(priority) || priority < 0) return 'Priority 必须是非负整数。';
  if (draft.authMode === 'none' && apiKeyMode.value !== 'clear') return '不使用认证的模型必须明确清除 API key。';
  if (isNewModel.value && apiKeyMode.value === 'keep') return '新模型没有可保留的 API key，请选择写入 key 或暂不配置。';
  if (apiKeyMode.value === 'replace') {
    if (!apiKeyDraft.value.trim()) return '选择替换 API key 后必须输入新 key。';
    if (isRedactedPlaceholder(apiKeyDraft.value) || apiKeyDraft.value === selectedFullModel.value?.apiKeyMasked) {
      return '不能把 masked API key 当作真实 key 回写。请重新输入真实 key，或选择保留。';
    }
  }
  if (baseUrlMode.value === 'replace') {
    const value = draft.baseUrl.trim();
    if (!value || isRedactedPlaceholder(value)) return '服务地址替换必须填写有效 URL。';
    try {
      new URL(value);
    } catch {
      return '服务地址必须是绝对 URL。';
    }
  }
  return null;
}

function buildRequest(): AiModelMutationRequestV1 {
  const baseUrl = baseUrlMode.value === 'replace'
    ? { baseUrlOperation: 'replace' as const, baseUrl: draft.baseUrl.trim() }
    : baseUrlMode.value === 'clear'
      ? { baseUrlOperation: 'clear' as const }
      : { baseUrlOperation: 'preserve' as const };
  return {
    name: draft.name.trim() || draft.displayName.trim(),
    displayName: draft.displayName.trim() || draft.name.trim(),
    provider: draft.provider.trim(),
    model: draft.model.trim(),
    ...baseUrl,
    timeoutMs: Number(draft.timeoutMs),
    protocol: draft.protocol,
    wireApi: draft.wireApi,
    authMode: draft.authMode,
    authHeaderName: draft.authHeaderName.trim() || null,
    apiKeyOperation: apiKeyMode.value,
    ...(apiKeyMode.value === 'replace' ? { apiKey: apiKeyDraft.value } : {}),
    roleBindings: [...draft.roleBindings],
    modelRole: draft.roleBindings[0] ?? 'generation',
    priority: Number(draft.priority),
    isEnabled: draft.isEnabled,
    remark: draft.remark.trim(),
    reasoning: { mode: draft.reasoningMode, effort: draft.reasoningEffort }
  };
}

function completedFeedback<T>(result: SettingsWriteResult<T>, message: string): SettingsFeedback {
  const base = resultFeedback(result);
  return result.status === 'completed' && message.trim()
    ? Object.freeze({ ...base, message })
    : base;
}

async function loadModels(): Promise<void> {
  const owner = props.owner;
  const version = requestVersion.value + 1;
  requestVersion.value = version;
  invalidateReasoningRequest();
  clearSecret();
  feedback.value = null;
  connectionResult.value = null;
  if (!canRead.value) {
    readBusy.value = false;
    phase.value = 'forbidden';
    readMessage.value = '当前角色没有读取 AI 模型 safe projection 的权限。';
    selectedId.value = null;
    resetDraft(null);
    return;
  }
  phase.value = 'loading';
  readBusy.value = true;
  try {
    const result = await owner.readAiModels();
    if (version !== requestVersion.value || owner !== props.owner) return;
    if (result.status === 'completed') {
      phase.value = 'ready';
      readMessage.value = null;
      const current = selectedId.value && result.value.items.some(item => item.id === selectedId.value)
        ? selectedId.value
        : result.value.items[0]?.id ?? null;
      selectedId.value = current;
      resetDraft(result.value.items.find(item => item.id === current) ?? null);
    } else {
      phase.value = 'error';
      readMessage.value = resultFeedback(result).message;
    }
  } finally {
    if (version === requestVersion.value && owner === props.owner) {
      readBusy.value = false;
      owner.refreshPanelState();
    }
  }
}

async function refreshAuthority(): Promise<void> {
  if (readBusy.value || mutationBusy.value) return;
  if (dirty.value) {
    feedback.value = {
      kind: 'error',
      message: 'AI model has unsaved changes. Save or discard the draft before refreshing its authority.',
      savedLabel: 'not saved',
      effectiveLabel: 'not effective',
      restartLabel: 'not applicable'
    };
    return;
  }
  await loadModels();
}

async function save(): Promise<void> {
  if (!canEditSelected.value || mutationBusy.value || !dirty.value) return;
  if (hasAiMutationUnknown.value) {
    reportUnknownMutation();
    return;
  }
  const validation = validateDraft();
  if (validation) {
    clearSecret();
    feedback.value = {
      kind: 'error',
      message: validation,
      savedLabel: '未保存',
      effectiveLabel: '未生效',
      restartLabel: '不适用'
    };
    return;
  }
  const owner = props.owner;
  mutationBusy.value = true;
  feedback.value = null;
  try {
    const result = selectedId.value
      ? await owner.updateAiModel(selectedId.value, buildRequest())
      : await owner.createAiModel(buildRequest());
    if (owner !== props.owner) return;
    const nextFeedback = result.status === 'completed'
      ? completedFeedback(result, result.value.message)
      : resultFeedback(result);
    if (result.status === 'completed') {
      selectedId.value = result.value.modelId;
      resetDraft(result.value.projection.items.find(item => item.id === result.value.modelId) ?? null);
    }
    feedback.value = nextFeedback;
  } finally {
    clearSecret();
    mutationBusy.value = false;
    owner.refreshPanelState();
  }
}

async function deleteModel(id: string): Promise<void> {
  const model = models.value.find(item => item.id === id) ?? null;
  if (!canManage.value || !model || mutationBusy.value || dirty.value) return;
  if (hasAiMutationUnknown.value) {
    reportUnknownMutation();
    return;
  }
  if (models.value.length <= 1) {
    feedback.value = { kind: 'error', message: '后端要求至少保留一个 AI 模型配置。', savedLabel: '未删除', effectiveLabel: '未生效', restartLabel: '不适用' };
    return;
  }
  if (!window.confirm(`确认删除 AI 模型“${model.displayName}”？`)) return;
  const owner = props.owner;
  mutationBusy.value = true;
  feedback.value = null;
  try {
    const result = await owner.deleteAiModel(id);
    if (owner !== props.owner) return;
    const nextFeedback = result.status === 'completed'
      ? completedFeedback(result, result.value.message)
      : resultFeedback(result);
    if (result.status === 'completed') {
      const selectedWasDeleted = selectedId.value === id;
      const next = selectedWasDeleted
        ? result.value.projection.items[0] ?? null
        : result.value.projection.items.find(item => item.id === selectedId.value) ?? selectedModel.value;
      selectedId.value = next?.id ?? null;
      resetDraft(next);
    }
    feedback.value = nextFeedback;
  } finally {
    clearSecret();
    mutationBusy.value = false;
    owner.refreshPanelState();
  }
}

async function activateModel(id: string): Promise<void> {
  if (!canManage.value || mutationBusy.value || dirty.value) return;
  if (hasAiMutationUnknown.value) {
    reportUnknownMutation();
    return;
  }
  const owner = props.owner;
  mutationBusy.value = true;
  feedback.value = null;
  try {
    const result = await owner.activateAiModel(id);
    if (owner !== props.owner) return;
    const nextFeedback = result.status === 'completed'
      ? completedFeedback(result, result.value.message)
      : resultFeedback(result);
    if (result.status === 'completed') {
      resetDraft(result.value.projection.items.find(item => item.id === selectedId.value) ?? selectedModel.value);
    }
    feedback.value = nextFeedback;
  } finally {
    clearSecret();
    mutationBusy.value = false;
    owner.refreshPanelState();
  }
}

async function setDefault(id: string, role: 'planner' | 'shadow-eval'): Promise<void> {
  if (!canManage.value || mutationBusy.value || dirty.value) return;
  if (hasAiMutationUnknown.value) {
    reportUnknownMutation();
    return;
  }
  const owner = props.owner;
  mutationBusy.value = true;
  feedback.value = null;
  try {
    const result = await owner.setAiModelDefault(id, role);
    if (owner !== props.owner) return;
    const nextFeedback = result.status === 'completed'
      ? completedFeedback(result, result.value.message)
      : resultFeedback(result);
    if (result.status === 'completed') {
      resetDraft(result.value.projection.items.find(item => item.id === selectedId.value) ?? selectedModel.value);
    }
    feedback.value = nextFeedback;
  } finally {
    clearSecret();
    mutationBusy.value = false;
    owner.refreshPanelState();
  }
}

async function queryReasoningSupport(): Promise<void> {
  if (!canRead.value || readBusy.value || reasoningBusy.value || mutationBusy.value || !draft.model.trim()) return;
  const owner = props.owner;
  const request: AiReasoningSupportRequestV1 = {
    provider: draft.provider.trim(),
    model: draft.model.trim(),
    baseUrl: baseUrlMode.value === 'replace' ? draft.baseUrl.trim() : null,
    protocol: draft.protocol
  };
  const version = reasoningRequestVersion.value + 1;
  reasoningRequestVersion.value = version;
  reasoningBusy.value = true;
  try {
    const result = await owner.readAiReasoningSupport(request);
    if (version !== reasoningRequestVersion.value || owner !== props.owner) return;
    if (result.status === 'completed') {
      reasoningSupport.value = result.value;
      reasoningSupportIdentity.value = currentReasoningIdentity();
      const allowedModes = result.value.allowedModes;
      const allowedEfforts = result.value.allowedEfforts;
      if (!allowedModes.includes(draft.reasoningMode)) draft.reasoningMode = allowedModes[0] ?? 'auto';
      if (!allowedEfforts.includes(draft.reasoningEffort)) draft.reasoningEffort = allowedEfforts[0] ?? 'medium';
    } else {
      feedback.value = resultFeedback(result);
    }
  } finally {
    if (version === reasoningRequestVersion.value && owner === props.owner) {
      reasoningBusy.value = false;
    }
  }
}

async function testSelectedModel(): Promise<void> {
  if (!canManage.value || !selectedId.value || mutationBusy.value) return;
  if (hasSelectedTestUnknown.value) {
    reportUnknownMutation('AI connection-test outcome is unknown; refresh AI authority before testing this model again.');
    return;
  }
  if (dirty.value) {
    feedback.value = { kind: 'error', message: '请先保存模型配置，再执行连接测试。连接测试只验证通信合同，不代表真实 LLM 产品质量。', savedLabel: '未保存', effectiveLabel: '未测试', restartLabel: '不适用' };
    return;
  }
  const owner = props.owner;
  mutationBusy.value = true;
  feedback.value = null;
  connectionResult.value = null;
  try {
    const result = await owner.testAiModel(selectedId.value);
    if (owner !== props.owner) return;
    feedback.value = resultFeedback(result);
    if (result.status === 'completed') connectionResult.value = result.value;
  } finally {
    clearSecret();
    mutationBusy.value = false;
    owner.refreshPanelState();
  }
}

watch(
  [() => draft.provider, () => draft.model, () => draft.baseUrl, () => draft.protocol, baseUrlMode],
  () => {
    const identity = currentReasoningIdentity();
    if (reasoningSupportIdentity.value === identity) return;
    reasoningSupport.value = null;
    reasoningSupportIdentity.value = null;
    invalidateReasoningRequest();
  }
);

watch(projection, value => {
  if (!canRead.value || dirty.value || mutationBusy.value) return;
  const selected = selectedId.value
    ? value?.items.find(item => item.id === selectedId.value) ?? null
    : null;
  if (selectedId.value && !selected) {
    selectedId.value = value?.items[0]?.id ?? null;
    resetDraft(value?.items[0] ?? null);
    return;
  }
  if (selected) {
    resetDraft(selected);
    return;
  }
  if (value?.items.length) {
    selectedId.value = value.items[0]!.id;
    resetDraft(value.items[0]!);
  }
}, { immediate: true });

watch([() => props.owner, canRead], ([owner]) => {
  bindPanelState(owner);
  void loadModels();
}, { immediate: true });

watch([dirty, pending, apiKeyDraft, apiKeyMode], () => props.owner.refreshPanelState());

onBeforeUnmount(() => {
  detachPanelState();
  clearSecret();
  requestVersion.value += 1;
  invalidateReasoningRequest();
});

onDeactivated(() => {
  clearSecret();
  invalidateReasoningRequest();
  props.owner.refreshPanelState();
});
</script>

<template>
  <div
    class="settings-ai-model"
    data-settings-ai-model
  >
    <CvInlineAlert
      v-if="!canRead"
      tone="info"
      title="当前角色不能读取 AI 模型"
    >
      AI 模型管理遵循现有 endpoint 权限；操作员不进入设置页面，工程师只读取安全投影和推理支持信息。
    </CvInlineAlert>

    <template v-else>
      <CvPanel
        title="AI 模型目录"
        description="模型元数据由 ai/models authority 返回；管理员看到脱敏完整投影，工程师只看到安全投影。"
        data-settings-ai-model-list
      >
        <template #actions>
          <CvStatusBadge
            :tone="projection?.safeSubset ? 'info' : 'ok'"
            :label="projection?.safeSubset ? '安全投影' : '完整投影（已脱敏）'"
          />
        </template>

        <CvInlineAlert
          v-if="phase === 'error'"
          tone="error"
          title="AI 模型读取失败"
        >
          {{ readMessage }}
        </CvInlineAlert>
        <CvInlineAlert
          v-else-if="phase === 'loading'"
          tone="info"
          title="正在读取模型 authority"
        >
          只接受服务端 safe/full projection，不使用本地默认模型补齐列表。
        </CvInlineAlert>

        <div class="settings-ai-model__toolbar">
          <CvButton
            size="sm"
            variant="quiet"
            :loading="readBusy"
            :disabled="readBusy || mutationBusy || dirty"
            loading-label="正在刷新 AI 权威"
            data-settings-ai-authority-refresh
            @click="refreshAuthority"
          >
            刷新 AI 权威
          </CvButton>
          <span class="settings-ai-model__count">{{ models.length }} 个模型配置</span>
          <CvButton
            v-if="canManage"
            size="sm"
            variant="primary"
            :disabled="mutationBusy"
            data-settings-ai-model-new
            @click="beginNew"
          >
            新增模型
          </CvButton>
        </div>

        <div
          v-if="models.length"
          class="settings-ai-model__list"
        >
          <div
            v-for="model in models"
            :key="model.id"
            class="settings-ai-model__row"
            :class="{ 'is-selected': selectedId === model.id }"
            data-settings-ai-model-row
          >
            <button
              class="settings-ai-model__select"
              type="button"
              @click="chooseModel(model.id)"
            >
              <span class="settings-ai-model__row-title">
                <strong>{{ model.displayName }}</strong>
                <CvStatusBadge
                  v-if="model.isActive"
                  tone="ok"
                  label="当前激活"
                />
              </span>
              <span class="settings-ai-model__row-meta">{{ model.provider }} · {{ model.model || '未设置模型名' }}</span>
              <span class="settings-ai-model__row-meta">{{ model.isEnabled ? '已启用' : '已停用' }} · {{ isFullModel(model) && model.hasApiKey ? 'API key 已配置' : '安全投影' }}</span>
            </button>
            <div
              v-if="canManage && isFullModel(model)"
              class="settings-ai-model__row-actions"
            >
              <CvButton
                size="sm"
                variant="quiet"
                :disabled="mutationBusy || model.isActive"
                @click="activateModel(model.id)"
              >
                激活
              </CvButton>
              <CvButton
                size="sm"
                variant="quiet"
                :disabled="mutationBusy"
                @click="setDefault(model.id, 'planner')"
              >
                设为 Planner
              </CvButton>
              <CvButton
                size="sm"
                variant="quiet"
                :disabled="mutationBusy"
                @click="setDefault(model.id, 'shadow-eval')"
              >
                设为 Shadow
              </CvButton>
              <CvButton
                size="sm"
                variant="danger"
                :loading="mutationBusy"
                :disabled="mutationBusy || models.length <= 1"
                :data-model-id="model.id"
                data-settings-ai-model-delete
                @click="deleteModel(model.id)"
              >
                删除
              </CvButton>
            </div>
          </div>
        </div>
        <p
          v-else-if="phase === 'ready'"
          class="settings-ai-model__empty"
        >
          服务端没有返回模型配置。
        </p>
      </CvPanel>

      <CvPanel
        v-if="canEditSelected"
        title="模型配置"
        description="保存后 owner 会重新读取 ai/models authority；API key 只在明确选择 replace/clear 时提交。"
        data-settings-ai-model-editor
      >
        <form
          class="settings-ai-model__form"
          autocomplete="off"
          @submit.prevent="save"
        >
          <CvField
            v-model="draft.name"
            label="内部名称"
            placeholder="Local alias"
            :readonly="mutationBusy"
          />
          <CvField
            v-model="draft.displayName"
            label="显示名称"
            required
            :readonly="mutationBusy"
          />
          <CvSelect
            :model-value="providerPreset"
            label="服务商预设"
            :options="providerPresetOptions"
            :disabled="mutationBusy"
            data-settings-ai-provider-preset
            @update:model-value="setProviderPreset"
          />
          <CvField
            :model-value="draft.provider"
            label="服务商"
            name="aiProvider"
            :readonly="mutationBusy"
            @update:model-value="onProviderInput"
          />
          <CvSelect
            v-model="draft.protocol"
            label="协议"
            :options="protocolOptions"
            :disabled="mutationBusy"
            @update:model-value="markPresetOverride('protocol')"
          />
          <CvSelect
            v-model="draft.wireApi"
            label="接口协议"
            :options="wireApiOptions"
            :disabled="mutationBusy"
            @update:model-value="markPresetOverride('wireApi')"
          />
          <CvSelect
            v-model="draft.authMode"
            label="认证方式"
            :options="authModeOptions"
            :disabled="mutationBusy"
            @update:model-value="onAuthModeChanged"
          />
          <CvField
            v-model="draft.authHeaderName"
            label="认证请求头"
            :readonly="mutationBusy || draft.authMode === 'none'"
            @update:model-value="markPresetOverride('authHeaderName')"
          />
          <CvField
            v-model="draft.model"
            label="模型名"
            name="aiModel"
            required
            placeholder="例如 gpt-4o-mini"
            :readonly="mutationBusy"
          />
          <CvSelect
            :model-value="baseUrlMode"
            label="服务地址操作"
            :options="baseUrlOptions"
            :disabled="mutationBusy"
            data-settings-ai-base-url-operation
            @update:model-value="setBaseUrlMode"
          />
          <CvField
            v-if="baseUrlMode === 'replace'"
            v-model="draft.baseUrl"
            label="服务地址替换"
            name="aiBaseUrl"
            placeholder="可选；服务端返回值可能已脱敏"
            :readonly="mutationBusy"
          />
          <CvField
            v-model="draft.timeoutMs"
            label="超时（毫秒）"
            type="number"
            min="1"
            :readonly="mutationBusy"
          />
          <CvField
            v-model="draft.priority"
            label="优先级"
            type="number"
            min="0"
            :readonly="mutationBusy"
          />
          <label class="settings-ai-model__toggle">
            <input
              v-model="draft.isEnabled"
              type="checkbox"
              :disabled="mutationBusy"
            >
            <span><strong>启用模型</strong><small>停用只影响模型选择，不删除配置或 secret authority。</small></span>
          </label>
          <CvSelect
            :model-value="apiKeyMode"
            label="API key 操作"
            :options="apiKeyOptions"
            :disabled="mutationBusy || draft.authMode === 'none'"
            data-settings-ai-key-operation
            @update:model-value="setApiKeyMode"
          />
          <CvField
            v-if="apiKeyMode === 'replace'"
            v-model="apiKeyDraft"
            label="新 API key"
            name="aiApiKey"
            type="password"
            autocomplete="new-password"
            placeholder="不显示、不回填 masked 值"
            :readonly="mutationBusy"
          />
          <CvField
            v-model="draft.remark"
            label="备注"
            :readonly="mutationBusy"
          />
        </form>

        <div
          v-if="selectedFullModel"
          class="settings-ai-model__last-test"
          data-settings-ai-last-test
        >
          <span>最近测试状态：{{ selectedFullModel.lastTestStatus || '未测试' }}</span>
          <span>最近测试时间：{{ selectedFullModel.lastTestAt || '不可用' }}</span>
          <span>最近测试延迟：{{ selectedFullModel.lastTestLatencyMs ?? '不可用' }} ms</span>
        </div>
        <div
          v-if="selectedFullModel"
          class="settings-ai-model__readonly-secrets"
          data-settings-ai-readonly-extra
        >
          额外请求头、查询参数和请求体在设置中仅以脱敏投影只读展示，不支持编辑。
        </div>

        <div class="settings-ai-model__roles">
          <span class="settings-ai-model__eyebrow">角色绑定</span>
          <label
            v-for="option in roleOptions"
            :key="String(option.value)"
            class="settings-ai-model__role"
          >
            <input
              type="checkbox"
              :checked="draft.roleBindings.includes(String(option.value))"
              :disabled="mutationBusy"
              @change="toggleRole(String(option.value), $event)"
            >
            <span>{{ option.label }}</span>
          </label>
        </div>

        <div class="settings-ai-model__reasoning">
          <div class="settings-ai-model__reasoning-heading">
            <div>
              <span class="settings-ai-model__eyebrow">推理支持</span>
              <strong>{{ displayReasoningSupport?.familyName ?? '尚未查询' }}</strong>
              <small>{{ displayReasoningSupport?.familyId ?? '点击读取服务端支持矩阵' }}</small>
            </div>
            <CvButton
              size="sm"
              variant="quiet"
              :loading="reasoningBusy"
              :disabled="readBusy || reasoningBusy || mutationBusy || !draft.model.trim()"
              loading-label="正在查询"
              data-settings-ai-reasoning-support
              @click="queryReasoningSupport"
            >
              读取推理支持
            </CvButton>
          </div>
          <div class="settings-ai-model__reasoning-fields">
            <CvSelect
              v-model="draft.reasoningMode"
              label="推理模式"
              :options="reasoningModeOptions"
              :disabled="mutationBusy"
            />
            <CvSelect
              v-model="draft.reasoningEffort"
              label="推理强度"
              :options="reasoningEffortOptions"
              :disabled="mutationBusy || draft.reasoningMode === 'off'"
            />
          </div>
          <p class="settings-ai-model__help">
            {{ displayReasoningSupport?.helpText ?? '推理支持只描述服务端能力，不评价真实 LLM 产品质量。' }}
          </p>
        </div>

        <template #footer>
          <div class="settings-ai-model__footer">
            <span>{{ dirty ? '有未保存修改' : '与服务端模型 projection 一致' }}</span>
            <div class="settings-ai-model__actions">
              <CvButton
                size="sm"
                variant="quiet"
                :disabled="!dirty || mutationBusy"
                @click="resetDraft(selectedModel)"
              >
                放弃修改
              </CvButton>
              <CvButton
                v-if="selectedId"
                size="sm"
                variant="quiet"
                :disabled="mutationBusy || dirty"
                data-settings-ai-model-test
                @click="testSelectedModel"
              >
                测试连接
              </CvButton>
              <CvButton
                size="sm"
                variant="primary"
                :loading="mutationBusy"
                :disabled="!dirty || mutationBusy"
                loading-label="正在保存 AI 模型"
                data-settings-ai-model-save
                @click="save"
              >
                保存模型配置
              </CvButton>
            </div>
          </div>
        </template>
      </CvPanel>

      <CvPanel
        v-else-if="selectedModel"
        title="安全模型投影"
        description="工程师只读取后端安全投影；不会看到服务地址、认证请求头或 API key。"
        data-settings-ai-model-safe
      >
        <CvDescriptionList
          :items="selectedSafeDetails"
          :columns="2"
          label="AI 模型安全投影"
        />
        <div class="settings-ai-model__safe-reasoning">
          <div class="settings-ai-model__reasoning-heading">
            <div>
              <span class="settings-ai-model__eyebrow">推理支持诊断</span>
              <strong>{{ displayReasoningSupport?.familyName ?? '尚未查询' }}</strong>
              <small>{{ displayReasoningSupport?.familyId ?? 'Engineer 可读取服务端支持矩阵' }}</small>
            </div>
            <CvButton
              size="sm"
              variant="quiet"
              :loading="reasoningBusy"
              :disabled="readBusy || reasoningBusy || !draft.model.trim()"
              loading-label="正在查询"
              data-settings-ai-reasoning-support
              @click="queryReasoningSupport"
            >
              读取推理支持
            </CvButton>
          </div>
          <p class="settings-ai-model__help">
            {{ displayReasoningSupport?.helpText ?? '该诊断只验证服务端能力合同，不评价真实 LLM 产品质量。' }}
          </p>
        </div>
      </CvPanel>

      <CvPanel
        v-if="connectionResult"
        title="连接测试结果"
        description="此结果只说明通信合同与 endpoint 响应，不代表真实 LLM 产品质量；F06 结论保持 NOT_EVALUATED。"
        data-settings-ai-model-test-result
      >
        <CvInlineAlert
          :tone="connectionResult.connectionOk ? 'success' : 'error'"
          :title="connectionResult.connectionOk ? '通信测试成功' : '通信测试失败'"
        >
          {{ connectionResult.sanitizedMessage || connectionResult.message || connectionResult.errorCode }}
          <span v-if="connectionResult.latencyMs > 0">（{{ connectionResult.latencyMs }} ms）</span>
        </CvInlineAlert>
      </CvPanel>
    </template>

    <CvInlineAlert
      v-if="feedback"
      :tone="feedback.kind === 'saved' ? 'success' : feedback.kind === 'unknown' ? 'warning' : 'error'"
      :title="feedback.kind === 'saved' ? 'AI 模型操作已完成' : feedback.kind === 'unknown' ? 'AI 模型操作结果未知' : 'AI 模型操作未完成'"
      data-settings-ai-model-feedback
    >
      {{ feedback.message }}
    </CvInlineAlert>
  </div>
</template>

<style scoped>
.settings-ai-model { display: grid; min-width: 0; gap: var(--cv-density-page-gap); }
.settings-ai-model__toolbar { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); margin-bottom: var(--cv-space-3); }
.settings-ai-model__count { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.settings-ai-model__list { display: grid; gap: 2px; }
.settings-ai-model__row { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: var(--cv-space-3); padding: var(--cv-space-2); border: 1px solid transparent; border-bottom-color: var(--cv-border-subtle); }
.settings-ai-model__row.is-selected { border-color: var(--cv-color-brand-300); background: var(--cv-color-brand-soft); }
.settings-ai-model__select { display: grid; min-width: 0; gap: 3px; padding: 0; border: 0; background: transparent; color: inherit; text-align: left; cursor: pointer; }
.settings-ai-model__row-title { display: flex; min-width: 0; align-items: center; gap: var(--cv-space-2); }
.settings-ai-model__row-title strong { overflow: hidden; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); text-overflow: ellipsis; white-space: nowrap; }
.settings-ai-model__row-meta { overflow: hidden; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.settings-ai-model__row-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-1); }
.settings-ai-model__empty { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-sm); }
.settings-ai-model__form { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); align-items: end; gap: var(--cv-space-4); }
.settings-ai-model__toggle { display: flex; min-height: var(--cv-density-control-height); align-items: center; gap: var(--cv-space-2); }
.settings-ai-model__toggle input, .settings-ai-model__role input { width: 16px; height: 16px; accent-color: var(--cv-color-brand-500); }
.settings-ai-model__toggle span { display: grid; gap: 2px; }
.settings-ai-model__toggle strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-ai-model__toggle small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-ai-model__last-test, .settings-ai-model__readonly-secrets { display: flex; flex-wrap: wrap; gap: var(--cv-space-3); margin-top: var(--cv-space-4); padding: var(--cv-space-3); border: 1px solid var(--cv-border-subtle); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.settings-ai-model__readonly-secrets { color: var(--cv-text-muted); }
.settings-ai-model__roles { display: flex; flex-wrap: wrap; align-items: center; gap: var(--cv-space-2) var(--cv-space-3); margin-top: var(--cv-space-4); padding-top: var(--cv-space-4); border-top: 1px solid var(--cv-border-subtle); }
.settings-ai-model__eyebrow { color: var(--cv-color-brand-text); font-size: var(--cv-font-size-2xs); font-weight: var(--cv-font-weight-semibold); }
.settings-ai-model__role { display: inline-flex; align-items: center; gap: var(--cv-space-1); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.settings-ai-model__reasoning { display: grid; gap: var(--cv-space-3); margin-top: var(--cv-space-4); padding-top: var(--cv-space-4); border-top: 1px solid var(--cv-border-subtle); }
.settings-ai-model__reasoning-heading { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.settings-ai-model__reasoning-heading > div { display: grid; gap: 2px; }
.settings-ai-model__reasoning-heading strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-ai-model__reasoning-heading small { color: var(--cv-text-muted); font-family: var(--cv-font-family-mono); font-size: var(--cv-font-size-2xs); }
.settings-ai-model__reasoning-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); }
.settings-ai-model__help { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.settings-ai-model__safe-reasoning { display: grid; gap: var(--cv-space-3); margin-top: var(--cv-space-4); padding-top: var(--cv-space-4); border-top: 1px solid var(--cv-border-subtle); }
.settings-ai-model__footer { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.settings-ai-model__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
@media (max-width: 900px) {
  .settings-ai-model__form { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .settings-ai-model__row { grid-template-columns: 1fr; }
  .settings-ai-model__row-actions { justify-content: flex-start; }
}
@media (max-width: 560px) {
  .settings-ai-model__form, .settings-ai-model__reasoning-fields { grid-template-columns: 1fr; }
  .settings-ai-model__reasoning-heading, .settings-ai-model__footer { align-items: stretch; flex-direction: column; }
  .settings-ai-model__actions { justify-content: flex-start; }
}
</style>
