import { afterEach, describe, expect, it, vi } from 'vitest';
import { FlowEditorInteraction } from '@clearvision/canonical-flow-interaction';

interface ShortcutInteraction {
  cleanup: Array<() => void>;
  disposed: boolean;
  shortcutScopeElement: HTMLElement;
  getMutationGate: () => 'editable' | 'readonly' | 'running';
  onFeedback: ReturnType<typeof vi.fn>;
  copySelectedNodes: ReturnType<typeof vi.fn>;
  pasteNodes: ReturnType<typeof vi.fn>;
  deleteSelectedItems: ReturnType<typeof vi.fn>;
  undo: ReturnType<typeof vi.fn>;
  redo: ReturnType<typeof vi.fn>;
  selectAll: ReturnType<typeof vi.fn>;
  clearSelection: ReturnType<typeof vi.fn>;
  cancelConnection: ReturnType<typeof vi.fn>;
  bindKeyboardShortcuts(): void;
}

const mounted: ShortcutInteraction[] = [];

function createInteraction(gate: 'editable' | 'readonly' | 'running' = 'editable') {
  const scope = document.createElement('section');
  const canvas = document.createElement('canvas');
  canvas.tabIndex = 0;
  scope.appendChild(canvas);
  document.body.appendChild(scope);
  let currentGate = gate;
  const interaction = Object.assign(
    Object.create(FlowEditorInteraction.prototype) as ShortcutInteraction,
    {
      cleanup: [],
      disposed: false,
      shortcutScopeElement: scope,
      getMutationGate: () => currentGate,
      onFeedback: vi.fn(),
      copySelectedNodes: vi.fn(),
      pasteNodes: vi.fn(),
      deleteSelectedItems: vi.fn(),
      undo: vi.fn(),
      redo: vi.fn(),
      selectAll: vi.fn(),
      clearSelection: vi.fn(),
      cancelConnection: vi.fn()
    }
  );
  interaction.bindKeyboardShortcuts();
  mounted.push(interaction);
  return {
    interaction,
    scope,
    canvas,
    setGate(next: 'editable' | 'readonly' | 'running') { currentGate = next; }
  };
}

function key(target: Element, keyValue: string, options: KeyboardEventInit = {}): void {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key: keyValue,
    bubbles: true,
    cancelable: true,
    ...options
  }));
}

afterEach(() => {
  for (const interaction of mounted.splice(0)) {
    interaction.cleanup.splice(0).forEach(dispose => dispose());
    interaction.shortcutScopeElement.remove();
  }
});

describe('FlowEditorInteraction scoped shortcuts', () => {
  it('does not intercept inputs, contenteditable targets or IME composition', () => {
    const { interaction, scope, canvas } = createInteraction();
    const input = document.createElement('input');
    const editor = document.createElement('div');
    editor.setAttribute('contenteditable', 'true');
    scope.append(input, editor);

    key(input, 'c', { ctrlKey: true });
    key(editor, 'v', { ctrlKey: true });
    key(canvas, 'z', { ctrlKey: true, isComposing: true });

    expect(interaction.copySelectedNodes).not.toHaveBeenCalled();
    expect(interaction.pasteNodes).not.toHaveBeenCalled();
    expect(interaction.undo).not.toHaveBeenCalled();
  });

  it('does not react outside the mounted Workspace shortcut scope', () => {
    const { interaction } = createInteraction();
    const outside = document.createElement('button');
    document.body.appendChild(outside);

    key(outside, 'a', { ctrlKey: true });

    expect(interaction.selectAll).not.toHaveBeenCalled();
    outside.remove();
  });

  it('allows selection/copy but blocks draft mutations in readonly and running gates', () => {
    const { interaction, canvas, setGate } = createInteraction('readonly');

    key(canvas, 'c', { ctrlKey: true });
    key(canvas, 'a', { ctrlKey: true });
    key(canvas, 'v', { ctrlKey: true });
    key(canvas, 'Delete');
    key(canvas, 'z', { ctrlKey: true });
    setGate('running');
    key(canvas, 'y', { ctrlKey: true });

    expect(interaction.copySelectedNodes).toHaveBeenCalledTimes(1);
    expect(interaction.selectAll).toHaveBeenCalledTimes(1);
    expect(interaction.pasteNodes).not.toHaveBeenCalled();
    expect(interaction.deleteSelectedItems).not.toHaveBeenCalled();
    expect(interaction.undo).not.toHaveBeenCalled();
    expect(interaction.redo).not.toHaveBeenCalled();
    expect(interaction.onFeedback.mock.calls.map(call => call[0].code))
      .toEqual(['readonly', 'readonly', 'readonly', 'running']);
  });

  it('supports Ctrl/Cmd undo-redo and Escape only while mounted', () => {
    const { interaction, canvas } = createInteraction();

    key(canvas, 'z', { ctrlKey: true });
    key(canvas, 'z', { metaKey: true, shiftKey: true });
    key(canvas, 'y', { ctrlKey: true });
    key(canvas, 'Escape');
    interaction.cleanup.splice(0).forEach(dispose => dispose());
    key(canvas, 'a', { ctrlKey: true });

    expect(interaction.undo).toHaveBeenCalledTimes(1);
    expect(interaction.redo).toHaveBeenCalledTimes(2);
    expect(interaction.clearSelection).toHaveBeenCalledTimes(1);
    expect(interaction.cancelConnection).toHaveBeenCalledTimes(1);
    expect(interaction.selectAll).not.toHaveBeenCalled();
  });
});
