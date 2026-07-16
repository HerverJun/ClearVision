import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vitest/config';

const studioUiRoot = fileURLToPath(new URL('.', import.meta.url));
const desktopRoot = resolve(studioUiRoot, '..');

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': resolve(studioUiRoot, 'src'),
      '@clearvision/canonical-flow-canvas': resolve(
        desktopRoot,
        'wwwroot',
        'src',
        'core',
        'canvas',
        'flowCanvasAdapter.js'
      ),
      '@clearvision/canonical-flow-interaction': resolve(
        desktopRoot,
        'wwwroot',
        'src',
        'features',
        'flow-editor',
        'flowEditorInteraction.js'
      ),
      '@clearvision/canonical-preview-coordinator': resolve(
        desktopRoot,
        'wwwroot',
        'src',
        'features',
        'flow-editor',
        'previewCoordinator.js'
      ),
      '@clearvision/canonical-image-canvas': resolve(
        desktopRoot,
        'wwwroot',
        'src',
        'core',
        'canvas',
        'imageCanvas.js'
      ),
      '@clearvision/canonical-roi-support': resolve(
        desktopRoot,
        'wwwroot',
        'src',
        'features',
        'flow-editor',
        'roiEditorSupport.mjs'
      ),
      '@clearvision/canonical-roi-geometry': resolve(
        desktopRoot,
        'wwwroot',
        'src',
        'features',
        'flow-editor',
        'roiGeometry.mjs'
      ),
      '@clearvision/canonical-image-pixel-probe': resolve(
        desktopRoot,
        'wwwroot',
        'src',
        'features',
        'flow-editor',
        'imagePixelProbe.mjs'
      )
    }
  },
  define: {
    __STUDIO_UI_BUILD__: JSON.stringify({
      name: 'ClearVision StudioUI',
      version: '0.1.0',
      basePath: '/studio/',
      mode: 'test'
    })
  },
  test: {
    environment: 'jsdom',
    include: ['tests/unit/**/*.spec.ts'],
    clearMocks: true,
    restoreMocks: true
  }
});
