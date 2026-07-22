import { describe, expect, it } from 'vitest';
import {
  createWorkspaceLayoutOwner,
  workspaceInspectorDefaultWidth,
  workspaceInspectorMaxWidth,
  workspaceInspectorMinWidth,
  workspaceLayoutStorageKey,
  workspacePreviewDefaultHeight,
  workspacePreviewDefaultWidth,
  workspacePreviewMinHeight,
  type WorkspaceLayoutObserver
} from '@/capabilities/project-workspace/flow';

function createMemoryStorage(initial?: string) {
  const values = new Map<string, string>();
  if (initial !== undefined) values.set(workspaceLayoutStorageKey, initial);
  return {
    getItem(key: string) { return values.get(key) ?? null; },
    setItem(key: string, value: string) { values.set(key, value); },
    read() { return values.get(workspaceLayoutStorageKey) ?? null; }
  };
}

function createObserverHarness() {
  let onSize: ((width: number, height: number) => void) | null = null;
  let disconnected = false;
  return {
    createObserver(callback: (width: number, height: number) => void): WorkspaceLayoutObserver {
      onSize = callback;
      return {
        observe() {},
        disconnect() { disconnected = true; }
      };
    },
    resize(width: number, height: number) { onSize?.(width, height); },
    get disconnected() { return disconnected; }
  };
}

describe('Workspace layout owner', () => {
  it('clamps Inspector and Preview to preserve the Canvas budget', () => {
    const observer = createObserverHarness();
    const owner = createWorkspaceLayoutOwner({
      storage: createMemoryStorage(),
      createObserver: observer.createObserver
    });
    owner.attach(document.createElement('section'));

    observer.resize(1290, 600);
    expect(owner.projection).toMatchObject({
      inspectorMinWidth: 240,
      inspectorMaxWidth: 240,
      inspectorWidth: 240,
      previewMinHeight: 160,
      previewMaxHeight: 240,
      previewHeight: 220,
      previewWidth: 300
    });

    owner.setInspectorWidth(999);
    owner.setPreviewHeight(999);
    expect(owner.projection.inspectorWidth).toBe(240);
    expect(owner.projection.previewHeight).toBe(240);
    expect(owner.projection.previewWidth).toBe(300);

    observer.resize(980, 520);
    expect(owner.projection.inspectorMaxWidth).toBe(240);
    expect(owner.projection.inspectorWidth).toBe(240);
    expect(owner.projection.previewMaxHeight).toBe(160);
    expect(owner.projection.previewHeight).toBe(160);
    expect(owner.projection.previewWidth).toBe(300);

    owner.dispose();
    expect(observer.disconnected).toBe(true);
  });

  it('persists only disposable Workspace layout preferences across owners', () => {
    const storage = createMemoryStorage();
    const firstObserver = createObserverHarness();
    const first = createWorkspaceLayoutOwner({ storage, createObserver: firstObserver.createObserver });
    first.attach(document.createElement('section'));
    firstObserver.resize(1860, 980);
    first.setInspectorWidth(384);
    first.setPreviewHeight(320);
    first.setPreviewWidth(448);
    first.setPreviewCollapsed(true);
    first.commit();
    first.dispose();

    expect(JSON.parse(storage.read() ?? '{}')).toEqual({
      schemaVersion: 2,
      inspectorWidth: 380,
      previewHeight: 320,
      previewWidth: 448,
      previewCollapsed: true
    });

    const secondObserver = createObserverHarness();
    const second = createWorkspaceLayoutOwner({ storage, createObserver: secondObserver.createObserver });
    second.attach(document.createElement('section'));
    secondObserver.resize(1860, 980);
    expect(second.projection).toMatchObject({
      inspectorWidth: 380,
      previewHeight: 320,
      previewWidth: 448,
      previewCollapsed: true
    });
    second.dispose();
  });

  it('fails closed to stable defaults when stored layout JSON is invalid', () => {
    const observer = createObserverHarness();
    const owner = createWorkspaceLayoutOwner({
      storage: createMemoryStorage('{invalid'),
      createObserver: observer.createObserver
    });
    owner.attach(document.createElement('section'));
    observer.resize(1860, 980);

    expect(owner.projection).toMatchObject({
      inspectorWidth: workspaceInspectorDefaultWidth,
      inspectorMinWidth: workspaceInspectorMinWidth,
      inspectorMaxWidth: workspaceInspectorMaxWidth,
      previewHeight: workspacePreviewDefaultHeight,
      previewWidth: workspacePreviewDefaultWidth,
      previewMinHeight: workspacePreviewMinHeight,
      previewCollapsed: false
    });
    owner.dispose();
  });
});
