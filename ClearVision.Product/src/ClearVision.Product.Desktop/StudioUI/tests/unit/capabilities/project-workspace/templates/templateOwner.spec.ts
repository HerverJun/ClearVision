import { flushPromises } from '@vue/test-utils';
import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import {
  ApiConflictError,
  ApiForbiddenError,
  ApiNetworkError,
  type ApiTransport
} from '@/platform/api';
import type { OperatorCatalogItem } from '@/capabilities/operators-read/operatorContracts';
import type { FlowCanvasOwner } from '@/capabilities/project-workspace/flow';
import { createReadQueryClient } from '@/platform/query';
import {
  createTemplateOwner,
  type TemplateOwner,
  type TemplateWriteInput
} from '@/capabilities/project-workspace/templates';

const projectId = '11111111-1111-4111-8111-111111111111';
const templateId = '22222222-2222-4222-8222-222222222222';

function catalogItem(): OperatorCatalogItem {
  return {
    operatorType: 'Source',
    displayName: 'Source',
    description: '',
    categoryId: 'DataProcessing',
    category: 'DataProcessing',
    lifecycle: 'Stable',
    lifecycleNote: null,
    defaultHidden: false,
    iconName: null,
    keywords: [],
    tags: [],
    version: '1.0.0',
    qualityState: null,
    inputPorts: [],
    outputPorts: [{
      name: 'out',
      displayName: 'Output',
      dataType: 'Image',
      isRequired: false,
      description: null
    }],
    parameters: [],
    parameterConstraints: [],
    outputAvailabilityRules: [],
    imageInputContracts: [],
    imageInputContractPresentations: []
  };
}

function templatePayload(id = templateId, name = 'Inspection Template') {
  return {
    id,
    name,
    description: 'Template description',
    industry: 'Electronics',
    tags: ['inspection'],
    flowJson: JSON.stringify({
      operators: [{ tempId: 'source', operatorType: 'Source' }],
      connections: []
    }),
    templateVersion: '1.0.0',
    scenarioKey: null,
    createdAt: '2026-08-07T00:00:00Z'
  };
}

function createFlowOwner(mutationGate: 'editable' | 'readonly' | 'running' = 'editable') {
  const projection = reactive({
    mutationGate,
    catalog: {
      operators: [catalogItem()]
    },
    draft: {
      id: 'draft-flow',
      name: 'Draft flow',
      operators: [],
      connections: [],
      decisionConfiguration: null
    }
  });
  const replaceFlow = vi.fn();
  const refreshOperators = vi.fn(async () => undefined);
  const owner = {
    projection,
    replaceFlow,
    refreshOperators
  } as unknown as FlowCanvasOwner;
  return { owner, projection, replaceFlow, refreshOperators };
}

function createHarness(options: {
  canWrite?: boolean;
  dirty?: boolean;
  mutationGate?: 'editable' | 'readonly' | 'running';
  get?: ApiTransport['get'];
  post?: ApiTransport['post'];
  put?: ApiTransport['put'];
} = {}) {
  const flow = createFlowOwner(options.mutationGate);
  const defaultGet: ApiTransport['get'] = async <T = unknown>(path: string) => {
    if (path === 'templates') return [templatePayload()] as T;
    if (path === `templates/${templateId}`) return templatePayload() as T;
    throw new Error(`Unexpected GET ${path}`);
  };
  const api: ApiTransport = {
    apiBaseUrl: 'http://localhost:5000/api',
    get: options.get ?? defaultGet,
    ...(options.post ? { post: options.post } : {}),
    ...(options.put ? { put: options.put } : {})
  };
  const queries = createReadQueryClient(api);
  const owner = createTemplateOwner({
    projectId,
    projectName: 'Project',
    flowOwner: flow.owner,
    queries,
    api,
    canWrite: options.canWrite ?? true,
    isDirty: () => options.dirty ?? false
  });
  return { owner, flow, api, queries };
}

async function ready(owner: TemplateOwner): Promise<void> {
  await flushPromises();
  expect(owner.projection.templates).toHaveLength(1);
}

const writeInput: TemplateWriteInput = {
  name: '  Saved template  ',
  description: '  Saved description  ',
  industry: '  Electronics  ',
  tags: [' inspection ', 'inspection', '']
};

describe('TemplateOwner', () => {
  it('requires dirty confirmation and writes an applied template through the Flow owner', async () => {
    const harness = createHarness({ dirty: true });
    await ready(harness.owner);
    await harness.owner.select(templateId);

    await expect(harness.owner.applySelected()).resolves.toBe(false);
    expect(harness.flow.replaceFlow).not.toHaveBeenCalled();
    expect(harness.owner.projection.diagnostics[0]?.code).toBe('template-dirty-replace-confirmation');

    await expect(harness.owner.applySelected({ confirmReplace: true })).resolves.toBe(true);
    expect(harness.flow.replaceFlow).toHaveBeenCalledTimes(1);
    expect(harness.owner.projection.conversion?.operatorCount).toBe(1);

    harness.owner.dispose('unit-test');
    harness.queries.dispose();
  });

  it('does not write for readonly users and normalizes save/update payloads for writers', async () => {
    const readonlyPost = vi.fn();
    const readonlyHarness = createHarness({ canWrite: false, post: readonlyPost });
    await ready(readonlyHarness.owner);
    await expect(readonlyHarness.owner.saveAs(writeInput)).resolves.toBe(false);
    expect(readonlyPost).not.toHaveBeenCalled();
    readonlyHarness.owner.dispose();
    readonlyHarness.queries.dispose();

    const postMock = vi.fn(async (path: string, body: unknown) => {
      void path;
      void body;
      return templatePayload();
    });
    const putMock = vi.fn(async (path: string, body: unknown) => {
      void path;
      void body;
      return templatePayload(templateId, 'Updated template');
    });
    const harness = createHarness({
      post: postMock as unknown as ApiTransport['post'],
      put: putMock as unknown as ApiTransport['put']
    });
    await ready(harness.owner);

    await expect(harness.owner.saveAs(writeInput)).resolves.toBe(true);
    expect(postMock).toHaveBeenCalledWith('templates', expect.objectContaining({
      name: 'Saved template',
      description: 'Saved description',
      industry: 'Electronics',
      tags: ['inspection']
    }));
    expect(postMock.mock.calls[0]?.[0]).toBe('templates');
    expect(postMock.mock.calls[0]?.[1]).toEqual(expect.objectContaining({ flowData: harness.flow.projection.draft }));

    await harness.owner.select(templateId);
    await expect(harness.owner.updateSelected(writeInput)).resolves.toBe(true);
    expect(putMock).toHaveBeenCalledWith('templates/' + templateId, expect.objectContaining({
      name: 'Saved template',
      tags: ['inspection'],
      flowData: harness.flow.projection.draft
    }));

    harness.owner.dispose();
    harness.queries.dispose();
  });

  it('preserves unknown outcomes and releases query ownership on disposal', async () => {
    const post = vi.fn(async () => {
      throw new ApiNetworkError('http://localhost:5000/api/templates', new Error('offline'));
    });
    const harness = createHarness({ post });
    await ready(harness.owner);
    await expect(harness.owner.saveAs(writeInput)).resolves.toBe(false);
    expect(harness.owner.projection.phase).toBe('unknown-outcome');
    expect(harness.owner.projection.writeStatus).toBe('unknown-outcome');

    harness.owner.dispose('unit-test-dispose');
    expect(harness.owner.projection.phase).toBe('disposed');
    expect(harness.queries.getDiagnostics().activeOwnerCount).toBe(0);
    expect(() => harness.owner.setSearch('after-dispose')).toThrow();
    harness.queries.dispose();
  });

  it('surfaces backend permission and conflict outcomes without claiming success', async () => {
    const forbidden = vi.fn(async () => {
      throw new ApiForbiddenError({
        url: 'http://localhost:5000/api/templates/' + templateId,
        status: 403,
        statusText: 'Forbidden',
        payload: { code: 'FORBIDDEN' },
        responseBody: ''
      });
    });
    const conflict = vi.fn(async () => {
      throw new ApiConflictError({
        url: 'http://localhost:5000/api/templates/' + templateId,
        status: 409,
        statusText: 'Conflict',
        payload: { code: 'CONFLICT' },
        responseBody: ''
      });
    });
    const harness = createHarness({ put: forbidden });
    await ready(harness.owner);
    await harness.owner.select(templateId);
    await expect(harness.owner.updateSelected(writeInput)).resolves.toBe(false);
    expect(harness.owner.projection.phase).toBe('error');
    expect(harness.owner.projection.errorCode).toBe('FORBIDDEN');
    harness.owner.dispose();
    harness.queries.dispose();

    const conflictHarness = createHarness({ put: conflict });
    await ready(conflictHarness.owner);
    await conflictHarness.owner.select(templateId);
    await expect(conflictHarness.owner.updateSelected(writeInput)).resolves.toBe(false);
    expect(conflictHarness.owner.projection.phase).toBe('error');
    expect(conflictHarness.owner.projection.errorCode).toBe('CONFLICT');
    conflictHarness.owner.dispose();
    conflictHarness.queries.dispose();
  });
});
