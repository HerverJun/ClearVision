import test from 'node:test';
import assert from 'node:assert/strict';
import { createViewManager } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/viewManager.js';

function createContainer() {
  const values = new Set();
  return {
    classList: {
      add(value) {
        values.add(value);
      },
      remove(value) {
        values.delete(value);
      },
      toggle(value, enabled) {
        if (enabled) {
          values.add(value);
        } else {
          values.delete(value);
        }
      }
    },
    setAttribute() {},
    removeAttribute() {}
  };
}

function createDocumentRef() {
  const containers = new Map([
    ['flow-editor', createContainer()],
    ['image-viewer', createContainer()],
    ['inspection-view', createContainer()],
    ['results-view', createContainer()],
    ['stations-view', createContainer()],
    ['project-view', createContainer()],
    ['ai-view', createContainer()],
    ['settings-view', createContainer()],
    ['main-content', createContainer()]
  ]);

  return {
    getElementById(id) {
      return containers.get(id) || null;
    },
    querySelector() {
      return null;
    },
    querySelectorAll() {
      return [];
    }
  };
}

test('image view delegates inspection image restoration without reading cached URL entries', async () => {
  const imageViewer = {
    imageCanvas: { resize() {} },
    loadImage() {
      throw new Error('The view manager should delegate image restoration.');
    }
  };
  const restoreCalls = [];
  const registryKeys = [];
  const manager = createViewManager({
    documentRef: createDocumentRef(),
    serviceRegistry: {
      get(key) {
        registryKeys.push(key);
        return key === 'imageViewer' ? imageViewer : null;
      }
    },
    restoreInspectionImageViewer(viewer) {
      restoreCalls.push(viewer);
    },
    setCurrentView() {},
    getFlowCanvas: () => null,
    getPropertySidebarController: () => null
  });

  await manager.switchView('image');
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.deepEqual(restoreCalls, [imageViewer]);
  assert.equal(registryKeys.includes('lastInspectionImageUrl'), false);
});
