import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import FlowCanvasSurface from '@/capabilities/project-workspace/flow/FlowCanvasSurface.vue';
import type { FlowCanvasOwnerProjection } from '@/capabilities/project-workspace/flow';
import type { CanonicalCanvasRuntimeSnapshot } from '@/platform/canvas';

function runtime(overrides: Partial<CanonicalCanvasRuntimeSnapshot> = {}): CanonicalCanvasRuntimeSnapshot {
  return Object.freeze({
    nodeCount: 3,
    connectionCount: 2,
    flowRevision: 4,
    selectionRevision: 1,
    selectedNodeId: 'node-1',
    selectedNodeIds: Object.freeze(['node-1']),
    selectedConnectionId: null,
    multiSelectionCount: 1,
    scale: 1,
    offsetX: 0,
    offsetY: 0,
    logicalWidth: 800,
    logicalHeight: 600,
    backingWidth: 800,
    backingHeight: 600,
    dpr: 1,
    isConnecting: false,
    isDraggingNodes: false,
    isPanning: false,
    isSelecting: false,
    canUndo: true,
    canRedo: false,
    mutationGate: 'editable',
    nodes: Object.freeze([{
      id: 'node-1', type: 'Threshold', title: '阈值', x: 40, y: 40,
      width: 160, height: 72, disabled: false, inputs: Object.freeze([]), outputs: Object.freeze([])
    }]),
    resources: Object.freeze({
      adapterDisposed: false, canvasDestroyed: false, interactionDisposed: false,
      resizeObserverActive: true, themeObserverActive: true, drawFramePending: false,
      resizeFramePending: false, interactionFramePending: false, contextMenuTimerActive: false,
      structureListenerCount: 1, viewListenerCount: 1, selectionListenerCount: 1,
      interactionCleanupCount: 1, facadeListenerCount: 1
    }),
    ...overrides
  });
}

function projection(canvasRuntime: CanonicalCanvasRuntimeSnapshot): FlowCanvasOwnerProjection {
  return Object.freeze({
    phase: 'mounted',
    projectId: '11111111-1111-4111-8111-111111111111',
    mutationGate: 'editable',
    draft: Object.freeze({
      id: '22222222-2222-4222-8222-222222222222',
      name: '辅助技术流程',
      operators: Object.freeze([{}, {}, {}]),
      connections: Object.freeze([{}, {}]),
      decisionConfiguration: null,
      opaquePassthrough: Object.freeze({})
    }),
    runtime: canvasRuntime,
    feedback: null,
    catalog: Object.freeze({
      phase: 'success',
      operators: Object.freeze([]),
      isRefreshing: false,
      message: null
    }),
    error: null
  });
}

describe('FlowCanvasSurface accessibility', () => {
  it('associates the focusable canvas with dynamic state and implemented keyboard commands', async () => {
    const wrapper = mount(FlowCanvasSurface, {
      props: {
        canvasId: 'workspace-flow-canvas',
        projection: projection(runtime())
      }
    });

    const canvas = wrapper.get('[data-testid="flow-canvas"]');
    expect(canvas.attributes('aria-label')).toBe('流程编辑画布');
    expect(canvas.attributes('role')).toBeUndefined();
    expect(canvas.attributes('aria-describedby')).toBe(
      'workspace-flow-canvas-accessibility-status workspace-flow-canvas-accessibility-help'
    );
    expect(canvas.attributes('aria-keyshortcuts')).toContain('Control+A');
    expect(canvas.attributes('aria-keyshortcuts')).toContain('Delete');
    expect(canvas.attributes('aria-keyshortcuts')).toContain('Meta+Shift+Z');
    expect(canvas.attributes('aria-keyshortcuts')).toContain('Backspace');
    expect(wrapper.get('[data-flow-command="undo"]').attributes('aria-keyshortcuts'))
      .toBe('Control+Z Meta+Z');
    expect(wrapper.get('[data-flow-command="redo"]').attributes('aria-keyshortcuts'))
      .toBeUndefined();
    expect(wrapper.get('[data-flow-command="copy"]').attributes('aria-keyshortcuts'))
      .toBe('Control+C Meta+C');
    expect(wrapper.get('[data-flow-command="paste"]').attributes('aria-keyshortcuts'))
      .toBe('Control+V Meta+V');
    expect(wrapper.get('[data-flow-command="delete"]').attributes('aria-keyshortcuts'))
      .toBe('Delete Backspace');
    expect(wrapper.get('#workspace-flow-canvas-accessibility-status').text())
      .toBe('流程包含 3 个节点和 2 条连线；已选中 1 个节点。');
    expect(wrapper.get('#workspace-flow-canvas-accessibility-help').text())
      .toContain('Ctrl/Command+A 全选');
    expect(wrapper.get('#workspace-flow-canvas-accessibility-help').text())
      .toContain('Ctrl/Command+Shift+Z');
    expect(wrapper.get('#workspace-flow-canvas-accessibility-help').text())
      .toContain('Delete 或 Backspace');

    await wrapper.setProps({
      projection: projection(runtime({
        nodes: Object.freeze([{
          id: 'node-1', type: 'Threshold', title: '阈值', x: 40, y: 40,
          width: 160, height: 72, disabled: true, inputs: Object.freeze([]), outputs: Object.freeze([])
        }])
      }))
    });

    expect(wrapper.get('#workspace-flow-canvas-accessibility-status').text())
      .toBe('流程包含 3 个节点和 2 条连线；已选中 1 个节点，其中 1 个已禁用。');

    await wrapper.setProps({
      projection: projection(runtime({
        nodeCount: 5,
        connectionCount: 4,
        selectedNodeId: null,
        selectedNodeIds: Object.freeze([]),
        selectedConnectionId: 'connection-1',
        multiSelectionCount: 0,
        selectionRevision: 2
      }))
    });

    expect(wrapper.get('#workspace-flow-canvas-accessibility-status').text())
      .toBe('流程包含 5 个节点和 4 条连线；已选中 1 条连线。');

    await wrapper.setProps({
      projection: Object.freeze({
        ...projection(runtime()),
        mutationGate: 'readonly'
      })
    });

    expect(canvas.attributes('aria-keyshortcuts')).toContain('Control+A');
    expect(canvas.attributes('aria-keyshortcuts')).not.toContain('Delete');
    expect(wrapper.get('#workspace-flow-canvas-accessibility-help').text())
      .toContain('画布当前仅可查看');
    expect(wrapper.get('[data-flow-command="undo"]').attributes('aria-keyshortcuts'))
      .toBeUndefined();
    expect(wrapper.get('[data-flow-command="redo"]').attributes('aria-keyshortcuts'))
      .toBeUndefined();
    expect(wrapper.get('[data-flow-command="copy"]').attributes('aria-keyshortcuts'))
      .toBe('Control+C Meta+C');
    expect(wrapper.get('[data-flow-command="paste"]').attributes('aria-keyshortcuts'))
      .toBeUndefined();
    expect(wrapper.get('[data-flow-command="delete"]').attributes('aria-keyshortcuts'))
      .toBeUndefined();
  });
});
