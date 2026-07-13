import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vitest/config';

const studioUiRoot = fileURLToPath(new URL('.', import.meta.url));

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': resolve(studioUiRoot, 'src')
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
