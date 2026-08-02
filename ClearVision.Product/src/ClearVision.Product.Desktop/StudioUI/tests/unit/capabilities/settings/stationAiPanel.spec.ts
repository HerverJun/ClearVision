import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { reactive, ref, nextTick, defineComponent } from 'vue';
import SettingsAiModelPanel from '@/capabilities/settings/SettingsAiModelPanel.vue';
import SettingsStationPanel from '@/capabilities/settings/SettingsStationPanel.vue';
import type {
  AiModelsProjectionV1,
  AiReasoningSupportProjectionV1,
  SettingsOwner,
  SettingsPanelState,
  SettingsWriteResult,
  StationCommunicationProjectionV1
} from '@/capabilities/settings';

const mountedWrappers: Array<{ unmount: () => void }> = [];

afterEach(() => {
  while (mountedWrappers.length > 0) mountedWrappers.pop()?.unmount();
  vi.restoreAllMocks();
});

function completed<T>(value: T, section: 'station' | 'ai-model' = 'ai-model'): SettingsWriteResult<T> {
  return { status: 'completed', section, generation: 0, operationKind: 'write', value };
}

function stationProjection(): StationCommunicationProjectionV1 {
  return {
    mode: 'LocalLoopback',
    port: 5010,
    lanHost: '127.0.0.1',
    lanAddresses: ['127.0.0.1'],
    localStationSyncEnabled: true,
    token: { hasToken: true, mask: '******', last4: '9876' },
    paths: { studio: 'C:/settings/station-communication.json', localStation: 'C:/settings/station-sync-settings.json' },
    currentRunning: {
      studioEnabled: true,
      studioListenMode: 'Loopback',
      studioPort: 5000,
      studioToken: { hasToken: true, mask: '******', last4: '0000' }
    },
    requiresRestart: { studio: true, localStation: true },
    localStationBaseUrl: 'http://127.0.0.1:5010',
    remoteStationBaseUrl: '',
    localStationHubUrl: 'http://127.0.0.1:5010/hubs/station-sync',
    remoteStationHubUrl: '',
    diagnostics: ['Studio ingress is configured.', 'Restart is required to apply the saved ingress.']
  };
}

function reasoningSupport(): AiReasoningSupportProjectionV1 {
  return {
    familyId: 'openai_gpt5',
    familyName: 'OpenAI GPT-5',
    allowedModes: ['auto', 'off', 'on'],
    allowedEfforts: ['low', 'medium', 'high'],
    helpText: 'Server reasoning support fixture.',
    supportsExplicitMode: true,
    supportsEffort: true,
    isModelLockedOn: false,
    defaultMode: 'auto'
  };
}

function aiProjection(): AiModelsProjectionV1 {
  return {
    safeSubset: false,
    items: [{
      id: 'model-1',
      name: 'primary',
      displayName: 'Primary model',
      provider: 'OpenAI Compatible',
      model: 'gpt-5.1-mini',
      hasApiKey: true,
      apiKeyMasked: '••••••••',
      baseUrl: 'https://api.example.test/v1',
      timeoutMs: 120000,
      isActive: true,
      isEnabled: true,
      protocol: 'openai_compatible',
      wireApi: 'responses',
      authMode: 'bearer',
      authHeaderName: 'Authorization',
      roleBindings: ['generation', 'planner'],
      modelRole: 'generation',
      priority: 10,
      remark: 'fixture',
      lastTestStatus: 'ok',
      lastTestAt: '2026-08-02T00:00:00Z',
      lastTestLatencyMs: 40,
      extraHeaders: { authorization: '<redacted>' },
      extraQuery: null,
      extraBody: null,
      capabilities: { supportsVisionInput: true, supportsToolCall: true },
      reasoning: { mode: 'auto', effort: 'medium' },
      reasoningSupport: reasoningSupport() as unknown as Readonly<Record<string, unknown>>
    }]
  };
}

function safeAiProjection(): AiModelsProjectionV1 {
  return {
    safeSubset: true,
    items: [{
      id: 'safe-1',
      displayName: 'Safe model',
      provider: 'OpenAI Compatible',
      model: 'gpt-5.1-mini',
      modelRole: 'planner',
      isEnabled: true,
      isActive: false,
      capabilities: { supportsVisionInput: true }
    }]
  };
}

function baseProjection(extra: Record<string, unknown> = {}): SettingsOwner['projection'] {
  return reactive({
    phase: 'ready', role: 'Admin', settings: null, error: null, message: '', generation: 0, started: true,
    dirtySectionCount: 0, pendingSectionCount: 0, unknownOutcomeKeys: [], station: null, aiModels: null,
    ...extra
  }) as unknown as SettingsOwner['projection'];
}

function ownerFixture(options: {
  readonly role?: string;
  readonly station?: StationCommunicationProjectionV1 | null;
  readonly aiModels?: AiModelsProjectionV1 | null;
  readonly unknownOutcomeKeys?: readonly string[];
  readonly stationSave?: (request: unknown) => Promise<SettingsWriteResult<StationCommunicationProjectionV1>>;
  readonly aiUpdate?: (id: string, request: unknown) => Promise<SettingsWriteResult<{
    message: string; modelId: string | null; projection: AiModelsProjectionV1;
  }>>;
  readonly aiDelete?: (id: string) => Promise<SettingsWriteResult<{
    message: string; modelId: string | null; projection: AiModelsProjectionV1;
  }>>;
}) {
  const readers = new Set<() => SettingsPanelState>();
  const projection = baseProjection({
    role: options.role ?? 'Admin',
    unknownOutcomeKeys: options.unknownOutcomeKeys ?? [],
    station: options.station ?? null,
    aiModels: options.aiModels ?? null
  });
  const stationRead = vi.fn(async () => completed(projection.station as StationCommunicationProjectionV1, 'station'));
  const stationSave = vi.fn(options.stationSave ?? (async () => completed(projection.station as StationCommunicationProjectionV1, 'station')));
  const stationToken = vi.fn(async () => completed({
    success: true,
    operation: 'regenerate' as const,
    tokenInfo: projection.station?.token ?? { hasToken: false, mask: '', last4: '' },
    settings: projection.station,
    message: 'regenerated',
    issues: []
  }, 'station'));
  const aiRead = vi.fn(async () => completed(projection.aiModels as AiModelsProjectionV1));
  const aiUpdate = vi.fn(options.aiUpdate ?? (async () => completed({
    message: 'updated', modelId: 'model-1', projection: projection.aiModels as AiModelsProjectionV1
  })));
  const aiCreate = vi.fn(async () => completed({
    message: 'created', modelId: 'model-1', projection: projection.aiModels as AiModelsProjectionV1
  }));
  const aiDelete = vi.fn(options.aiDelete ?? (async () => completed({
    message: 'deleted', modelId: 'model-1', projection: projection.aiModels as AiModelsProjectionV1
  })));
  const aiActivate = vi.fn(async () => completed({
    message: 'activated', modelId: 'model-1', projection: projection.aiModels as AiModelsProjectionV1
  }));
  const aiDefault = vi.fn(async () => completed({
    message: 'defaulted', modelId: 'model-1', projection: projection.aiModels as AiModelsProjectionV1
  }));
  const aiReasoning = vi.fn(async () => completed(reasoningSupport()));
  const aiTest = vi.fn(async () => completed({
    connectionOk: true, success: true, statusCode: 200, errorCode: '', latencyMs: 21,
    sanitizedMessage: 'Connection verified.', message: 'Connection verified.', provider: 'OpenAI Compatible',
    modelName: 'gpt-5.1-mini', protocol: 'openai_compatible', wireApi: 'responses'
  }));
  const owner = {
    projection,
    registerPanelState: vi.fn((_section: string, reader: () => SettingsPanelState) => {
      readers.add(reader);
      return () => readers.delete(reader);
    }),
    refreshPanelState: vi.fn(),
    readStationCommunication: stationRead,
    saveStationCommunication: stationSave,
    runStationTokenOperation: stationToken,
    readAiModels: aiRead,
    createAiModel: aiCreate,
    updateAiModel: aiUpdate,
    deleteAiModel: aiDelete,
    activateAiModel: aiActivate,
    setAiModelDefault: aiDefault,
    readAiReasoningSupport: aiReasoning,
    testAiModel: aiTest
  } as unknown as SettingsOwner;
  return {
    owner,
    projection,
    readers,
    stationRead,
    stationSave,
    stationToken,
    aiRead,
    aiUpdate,
    aiDelete,
    aiReasoning,
    aiTest
  };
}

describe('F07 G7 Station communication panel', () => {
  it('preserves a masked token by default and submits only an explicit replacement', async () => {
    const fixture = ownerFixture({ station: stationProjection() });
    fixture.stationSave.mockImplementation(async request => {
      const next = { ...stationProjection(), port: (request as { port: number }).port, requiresRestart: { studio: true, localStation: true } };
      (fixture.projection as unknown as { station: StationCommunicationProjectionV1 | null }).station = next;
      return completed(next, 'station');
    });
    const wrapper = mount(SettingsStationPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('input[type="number"]').setValue('5033');
    await wrapper.get('[data-settings-station-save]').trigger('click');
    expect(fixture.stationSave).toHaveBeenLastCalledWith(expect.objectContaining({ port: 5033 }));
    expect(fixture.stationSave.mock.calls.at(-1)?.[0]).not.toHaveProperty('sharedToken');

    await wrapper.get('[data-settings-station-token-operation] select').setValue('replace');
    await wrapper.get('input[type="password"]').setValue('station-secret-replacement');
    await wrapper.get('[data-settings-station-save]').trigger('click');
    expect(fixture.stationSave.mock.calls.at(-1)?.[0]).toMatchObject({ sharedToken: 'station-secret-replacement' });
    await flushPromises();
    expect(wrapper.text()).not.toContain('station-secret-replacement');
    expect(wrapper.text()).toContain('******');
  });

  it('clears the token field as soon as the KeepAlive panel is deactivated', async () => {
    const fixture = ownerFixture({ station: stationProjection() });
    const active = ref<'station' | 'other'>('station');
    // eslint-disable-next-line vue/one-component-per-file
    const host = defineComponent({
      components: { SettingsStationPanel },
      setup: () => ({ active, owner: fixture.owner }),
      template: `<KeepAlive><SettingsStationPanel v-if="active === 'station'" :owner="owner" role="Admin" /><div v-else data-other /></KeepAlive>`
    });
    const wrapper = mount(host);
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('[data-settings-station-token-operation] select').setValue('replace');
    await wrapper.get('input[type="password"]').setValue('station-secret');
    active.value = 'other';
    await nextTick();
    active.value = 'station';
    await nextTick();

    expect(wrapper.find('input[type="password"]').exists()).toBe(false);
    expect(wrapper.text()).not.toContain('station-secret');
  });
});

  it('blocks LanController regenerate without a secure handoff and protects dirty refresh', async () => {
    const fixture = ownerFixture({ station: stationProjection() });
    const wrapper = mount(SettingsStationPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('select[name="stationMode"]').setValue('LanController');
    const regenerate = wrapper.get('[data-settings-station-regenerate]');
    expect(regenerate.attributes('disabled')).toBeDefined();
    expect(wrapper.get('[data-settings-station-token-hint]').text()).toContain('LanController');
    await regenerate.trigger('click');
    expect(fixture.stationToken).not.toHaveBeenCalled();

    await wrapper.get('input[type="number"]').setValue('5033');
    const readCount = fixture.stationRead.mock.calls.length;
    await wrapper.get('[data-settings-station-authority-refresh]').trigger('click');
    expect(fixture.stationRead).toHaveBeenCalledTimes(readCount);
    expect((wrapper.get('input[name="stationPort"]').element as HTMLInputElement).value).toBe('5033');
  });

  it('does not regenerate a token while a LocalLoopback draft is dirty', async () => {
    const fixture = ownerFixture({ station: stationProjection() });
    const wrapper = mount(SettingsStationPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('input[name="stationPort"]').setValue('5033');
    const regenerate = wrapper.get('[data-settings-station-regenerate]');
    expect(regenerate.attributes('disabled')).toBeDefined();
    expect(wrapper.get('[data-settings-station-token-hint]').text()).toContain('unsaved');
    await regenerate.trigger('click');

    expect(fixture.stationToken).not.toHaveBeenCalled();
  });

describe('F07 G8 AI model administration panel', () => {
  it('implements API key keep, replace and clear without rendering the key after submission', async () => {
    const fixture = ownerFixture({ aiModels: aiProjection() });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('input[placeholder="例如 gpt-4o-mini"]').setValue('gpt-5.1-mini-updated');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');
    expect(fixture.aiUpdate.mock.calls[0]?.[1]).toMatchObject({ apiKeyOperation: 'keep', baseUrlOperation: 'preserve' });
    expect(fixture.aiUpdate.mock.calls[0]?.[1]).not.toHaveProperty('apiKey');
    expect(fixture.aiUpdate.mock.calls[0]?.[1]).not.toHaveProperty('baseUrl');

    await wrapper.get('[data-settings-ai-key-operation] select').setValue('replace');
    await wrapper.get('input[type="password"]').setValue('ai-secret-replacement');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');
    expect(fixture.aiUpdate.mock.calls[1]?.[1]).toMatchObject({ apiKeyOperation: 'replace', apiKey: 'ai-secret-replacement' });
    await flushPromises();
    expect(wrapper.text()).not.toContain('ai-secret-replacement');
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);

    await wrapper.get('[data-settings-ai-key-operation] select').setValue('clear');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');
    expect(fixture.aiUpdate.mock.calls[2]?.[1]).toMatchObject({ apiKeyOperation: 'clear' });
    expect(fixture.aiUpdate.mock.calls[2]?.[1]).not.toHaveProperty('apiKey');
  });

  it('preserves a redacted BaseUrl and supports explicit preserve, replace and clear operations', async () => {
    const source = aiProjection();
    const redacted = {
      ...source,
      items: [{ ...source.items[0]!, baseUrl: 'https://<redacted-host>/<redacted-path>' }]
    };
    const fixture = ownerFixture({ aiModels: redacted });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    expect(wrapper.find('[name="aiBaseUrl"]').exists()).toBe(false);
    await wrapper.get('[name="aiModel"]').setValue('gpt-5.1-mini-updated');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');
    expect(fixture.aiUpdate.mock.calls[0]?.[1]).toMatchObject({ baseUrlOperation: 'preserve' });
    expect(fixture.aiUpdate.mock.calls[0]?.[1]).not.toHaveProperty('baseUrl');

    await wrapper.get('[data-settings-ai-base-url-operation] select').setValue('replace');
    await wrapper.get('[name="aiBaseUrl"]').setValue('https://replacement.example/v2');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');
    expect(fixture.aiUpdate.mock.calls[1]?.[1]).toMatchObject({
      baseUrlOperation: 'replace', baseUrl: 'https://replacement.example/v2'
    });

    await wrapper.get('[data-settings-ai-base-url-operation] select').setValue('clear');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');
    expect(fixture.aiUpdate.mock.calls[2]?.[1]).toMatchObject({ baseUrlOperation: 'clear' });
    expect(fixture.aiUpdate.mock.calls[2]?.[1]).not.toHaveProperty('baseUrl');
  });

  it('applies provider presets without removing the custom Provider path and displays LastTest metadata', async () => {
    const fixture = ownerFixture({ aiModels: aiProjection() });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    expect(wrapper.get('[data-settings-ai-last-test]').text()).toContain('LastTestStatus: ok');
    await wrapper.get('[data-settings-ai-provider-preset] select').setValue('anthropic');
    expect((wrapper.get('[name="aiProvider"]').element as HTMLInputElement).value).toBe('Anthropic');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');
    expect(fixture.aiUpdate.mock.calls[0]?.[1]).toMatchObject({
      provider: 'Anthropic', protocol: 'anthropic', wireApi: 'chat_completions',
      authMode: 'header_key', authHeaderName: 'x-api-key'
    });
  });

  it('links a manually entered known Provider to defaults unless fields were overridden', async () => {
    const fixture = ownerFixture({ aiModels: aiProjection() });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('[name="aiProvider"]').setValue('Anthropic');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');

    expect(fixture.aiUpdate.mock.calls[0]?.[1]).toMatchObject({
      provider: 'Anthropic', protocol: 'anthropic', wireApi: 'chat_completions',
      authMode: 'header_key', authHeaderName: 'x-api-key'
    });
  });

  it('invalidates old reasoning support immediately when the reasoning identity changes and protects refresh drafts', async () => {
    const fixture = ownerFixture({ aiModels: aiProjection() });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('[data-settings-ai-reasoning-support]').trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('OpenAI GPT-5');

    await wrapper.get('[name="aiProvider"]').setValue('Custom Proxy');
    await nextTick();
    expect(wrapper.text()).not.toContain('OpenAI GPT-5');

    const readCount = fixture.aiRead.mock.calls.length;
    await wrapper.get('[name="aiModel"]').setValue('dirty-model');
    await wrapper.get('[data-settings-ai-authority-refresh]').trigger('click');
    expect(fixture.aiRead).toHaveBeenCalledTimes(readCount);
    expect((wrapper.get('[name="aiModel"]').element as HTMLInputElement).value).toBe('dirty-model');
  });

  it('rebuilds the selected draft from a new authoritative model projection when clean', async () => {
    const fixture = ownerFixture({ aiModels: aiProjection() });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    const next = {
      ...aiProjection(),
      items: [{ ...aiProjection().items[0]!, model: 'server-normalized-model' }]
    };
    (fixture.projection as unknown as { aiModels: AiModelsProjectionV1 }).aiModels = next;
    await nextTick();

    expect((wrapper.get('[name="aiModel"]').element as HTMLInputElement).value).toBe('server-normalized-model');
  });

  it('does not retry an AI mutation before its unknown outcome is reconciled', async () => {
    const fixture = ownerFixture({ aiModels: aiProjection(), unknownOutcomeKeys: ['ai-models'] });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('[name="aiModel"]').setValue('dirty-model');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');

    expect(fixture.aiUpdate).not.toHaveBeenCalled();
    expect(wrapper.get('[data-settings-ai-model-feedback]').text()).toContain('refresh AI authority');
  });

  it('shows safe projection to Engineer and allows reasoning support diagnosis only', async () => {
    const fixture = ownerFixture({ role: 'Engineer', aiModels: safeAiProjection() });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Engineer' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    expect(wrapper.find('[data-settings-ai-model-safe]').exists()).toBe(true);
    expect(wrapper.find('[data-settings-ai-model-editor]').exists()).toBe(false);
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);
    await wrapper.find('[data-settings-ai-model-safe] button').trigger('click');
    await flushPromises();
    expect(fixture.aiReasoning).toHaveBeenCalledWith(expect.objectContaining({ model: 'gpt-5.1-mini' }));
    expect(wrapper.text()).toContain('OpenAI GPT-5');
  });

  it('does not submit duplicate reasoning support reads while the first read is pending', async () => {
    const pending = new Promise<SettingsWriteResult<AiReasoningSupportProjectionV1>>(() => undefined);
    const fixture = ownerFixture({ role: 'Engineer', aiModels: safeAiProjection() });
    fixture.aiReasoning.mockReturnValue(pending);
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Engineer' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    const button = wrapper.get('[data-settings-ai-reasoning-support]');
    await button.trigger('click');
    await button.trigger('click');

    expect(fixture.aiReasoning).toHaveBeenCalledTimes(1);
    expect(button.attributes('disabled')).toBeDefined();
  });

  it('does not apply reasoning support from a model that was replaced while the read was pending', async () => {
    let resolveReasoning!: (result: SettingsWriteResult<AiReasoningSupportProjectionV1>) => void;
    const pending = new Promise<SettingsWriteResult<AiReasoningSupportProjectionV1>>(resolve => {
      resolveReasoning = resolve;
    });
    const projection = safeAiProjection();
    const second = { ...projection.items[0]!, id: 'safe-2', displayName: 'Second safe model', model: 'gpt-5.1-large' };
    const fixture = ownerFixture({
      role: 'Engineer',
      aiModels: { ...projection, items: [projection.items[0]!, second] }
    });
    fixture.aiReasoning.mockReturnValue(pending);
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Engineer' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('[data-settings-ai-reasoning-support]').trigger('click');
    await wrapper.findAll('button.settings-ai-model__select')[1]!.trigger('click');
    resolveReasoning(completed({ ...reasoningSupport(), familyName: 'Stale model support' }));
    await flushPromises();

    expect(wrapper.text()).not.toContain('Stale model support');
  });

  it('reports model mutation pending and clears the API key on KeepAlive deactivation', async () => {
    const pending = new Promise<SettingsWriteResult<{
      message: string; modelId: string | null; projection: AiModelsProjectionV1;
    }>>(() => undefined);
    const fixture = ownerFixture({ aiModels: aiProjection(), aiUpdate: async () => pending });
    const active = ref<'ai-model' | 'other'>('ai-model');
    // eslint-disable-next-line vue/one-component-per-file
    const host = defineComponent({
      components: { SettingsAiModelPanel },
      setup: () => ({ active, owner: fixture.owner }),
      template: `<KeepAlive><SettingsAiModelPanel v-if="active === 'ai-model'" :owner="owner" role="Admin" /><div v-else data-other /></KeepAlive>`
    });
    const wrapper = mount(host);
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('[data-settings-ai-key-operation] select').setValue('replace');
    await wrapper.get('input[type="password"]').setValue('ai-secret-pending');
    await wrapper.get('[data-settings-ai-model-save]').trigger('click');
    await nextTick();
    const reader = [...fixture.readers][0]!;
    expect(reader()).toMatchObject({ pending: true });
    expect(wrapper.get('[data-settings-ai-model-save]').attributes('disabled')).toBeDefined();
    active.value = 'other';
    await nextTick();
    active.value = 'ai-model';
    await nextTick();
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);
  });

  it('does not delete another model when a dirty draft blocks selection changes', async () => {
    const projection = aiProjection();
    const second = { ...projection.items[0]!, id: 'model-2', displayName: 'Second model', isActive: false };
    const fixture = ownerFixture({
      aiModels: { ...projection, items: [projection.items[0]!, second] }
    });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('[data-settings-ai-model-editor] input').setValue('dirty draft');
    await wrapper.get('[data-settings-ai-model-delete][data-model-id="model-2"]').trigger('click');

    expect(fixture.aiDelete).not.toHaveBeenCalled();
    expect(wrapper.get('[data-settings-ai-model-editor] input').element).toHaveProperty('value', 'dirty draft');
  });

  it('does not submit duplicate AI deletes while the first delete is pending', async () => {
    const projection = aiProjection();
    const second = { ...projection.items[0]!, id: 'model-2', displayName: 'Second model', isActive: false };
    const pending = new Promise<SettingsWriteResult<{
      message: string; modelId: string | null; projection: AiModelsProjectionV1;
    }>>(() => undefined);
    const fixture = ownerFixture({
      aiModels: { ...projection, items: [projection.items[0]!, second] },
      aiDelete: async () => pending
    });
    const wrapper = mount(SettingsAiModelPanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    const deleteButton = wrapper.get('[data-settings-ai-model-delete][data-model-id="model-2"]');
    await deleteButton.trigger('click');
    await deleteButton.trigger('click');

    expect(fixture.aiDelete).toHaveBeenCalledTimes(1);
    expect(deleteButton.attributes('disabled')).toBeDefined();
  });
});
