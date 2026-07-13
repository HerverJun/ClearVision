import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vite';

const studioUiBasePath = '/studio/';
const studioUiRoot = fileURLToPath(new URL('.', import.meta.url));
const desktopRoot = resolve(studioUiRoot, '..');

function resolveOutputDirectory(): string {
  const injectedOutput = process.env.VITE_OUT_DIR?.trim();
  if (injectedOutput) {
    return resolve(injectedOutput);
  }

  const configuration = process.env.CONFIGURATION?.trim() || 'Debug';
  const targetFramework = process.env.TARGET_FRAMEWORK?.trim() || 'net8.0-windows';
  return resolve(desktopRoot, 'obj', configuration, targetFramework, 'StudioUI', 'dist');
}

export default defineConfig(({ mode }) => ({
  base: studioUiBasePath,
  plugins: [vue()],
  resolve: {
    alias: {
      '@': resolve(studioUiRoot, 'src')
    }
  },
  define: {
    __STUDIO_UI_BUILD__: JSON.stringify({
      name: 'ClearVision StudioUI',
      version: process.env.npm_package_version || '0.1.0',
      basePath: studioUiBasePath,
      mode
    })
  },
  build: {
    outDir: resolveOutputDirectory(),
    emptyOutDir: true,
    assetsDir: 'assets',
    manifest: true,
    sourcemap: false,
    target: 'es2022',
    copyPublicDir: false
  }
}));
