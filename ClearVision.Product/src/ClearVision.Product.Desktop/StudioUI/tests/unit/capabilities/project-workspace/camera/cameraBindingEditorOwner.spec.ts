import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import type { ApiBlobResponse, ApiTransport } from '@/platform/api';
import type { FlowCanvasOwner } from '@/capabilities/project-workspace/flow';
import { createCameraBindingEditorOwner } from '@/capabilities/project-workspace/camera';

const projectId = '11111111-1111-4111-8111-111111111111';
const sourceId = '22222222-2222-4222-8222-222222222222';
const targetId = '33333333-3333-4333-8333-333333333333';

function node(id: string, type: string | number, parameters: unknown[] = []) {
  return { id, name: type, type, isEnabled: true, parameters, inputPorts: [], outputPorts: [] };
}

function harness() {
  const source = node(sourceId, 0, [
    { id: crypto.randomUUID(), name: 'SourceType', value: 'Camera' },
    { id: crypto.randomUUID(), name: 'CameraBindingId', value: 'camera-a' },
    { id: crypto.randomUUID(), name: 'ExposureTime', value: 1000 },
    { id: crypto.randomUUID(), name: 'Gain', value: 2 }
  ]);
  const target = node(targetId, 'Thresholding');
  const projection = reactive({
    phase: 'mounted', projectId, mutationGate: 'editable',
    draft: { id: crypto.randomUUID(), name: 'Flow', operators: [source, target], connections: [{ id: crypto.randomUUID(), sourceOperatorId: sourceId, targetOperatorId: targetId }], decisionConfiguration: null, opaquePassthrough: {} },
    runtime: { selectedNodeId: sourceId, flowRevision: 1 }, feedback: null,
    catalog: { phase: 'success', operators: [], isRefreshing: false, message: null }, error: null
  });
  const patchNodeParameter = vi.fn(() => ({ ok: true, code: 'ok', message: 'ok', flowRevision: projection.runtime.flowRevision }));
  const flowOwner = { projectId, projection, commands: { patchNodeParameter } } as unknown as FlowCanvasOwner;
  const api = {
    apiBaseUrl: 'http://localhost/api',
    get: vi.fn(async () => [{ id: 'camera-a', displayName: '工位相机', isEnabled: true, triggerMode: 'Software', connectionStatus: 'Connected' }]),
    post: vi.fn(),
    getBlob: vi.fn(),
    postBlob: vi.fn(async () => ({
      blob: new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' }),
      contentType: 'image/png', contentLength: 3, etag: null, sha256: null,
      headers: new Headers({ 'X-Camera-Id': 'camera-a', 'X-Image-Width': '640', 'X-Image-Height': '480' })
    }))
  } as unknown as ApiTransport;
  return { source, target, projection, flowOwner, api };
}

describe('cameraBindingEditorOwner', () => {
  it('captures an identified frame, exposes it only to reachable targets, and invalidates on config change', async () => {
    const value = harness();
    const owner = createCameraBindingEditorOwner({ projectId, flowOwner: value.flowOwner, api: value.api });
    await vi.waitFor(() => expect(owner.projection.phase).toBe('ready'));
    const frame = await owner.capture();
    expect(frame).toMatchObject({ projectId, sourceNodeId: sourceId, cameraBindingId: 'camera-a', width: 640, height: 480 });
    expect(owner.getPreviewInputContext(value.target)).toMatchObject({ sourceNodeId: sourceId, frameId: frame!.frameId });
    expect(owner.getPreviewInputContext(node(crypto.randomUUID(), 'Unreachable'))).toBeNull();

    (value.source.parameters[2] as { value: number }).value = 1200;
    value.projection.runtime.flowRevision += 1;
    await vi.waitFor(() => expect(owner.projection.frame).toBeNull());
    expect(owner.projection.message).toContain('失效');
    owner.dispose();
    expect(owner.projection.phase).toBe('disposed');
  });

  it('aborts capture and never accepts the late response after dispose', async () => {
    const value = harness();
    let resolveBlob!: (value: ApiBlobResponse) => void;
    value.api.postBlob = vi.fn((_path, _body, options) => new Promise<ApiBlobResponse>((resolve, reject) => {
      resolveBlob = resolve;
      options?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
    }));
    const owner = createCameraBindingEditorOwner({ projectId, flowOwner: value.flowOwner, api: value.api });
    await vi.waitFor(() => expect(owner.projection.phase).toBe('ready'));
    const pending = owner.capture();
    owner.dispose();
    resolveBlob({ blob: new Blob([new Uint8Array([1])]), contentType: 'image/png', contentLength: 1, etag: null, sha256: null, headers: new Headers() });
    await expect(pending).resolves.toBeNull();
    expect(owner.projection.frame).toBeNull();
  });
});
