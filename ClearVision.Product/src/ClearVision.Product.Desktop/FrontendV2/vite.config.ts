import { fileURLToPath, URL } from 'node:url';
import { resolve } from 'node:path';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vite';

const defaultConfiguration = process.env.Configuration ?? process.env.CONFIGURATION ?? 'Debug';
const defaultTargetFramework = process.env.TargetFramework ?? process.env.TARGET_FRAMEWORK ?? 'net8.0-windows';
const defaultOutDir = resolve(__dirname, '..', 'obj', defaultConfiguration, defaultTargetFramework, 'FrontendV2', 'dist');

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  build: {
    outDir: process.env.VITE_OUT_DIR ?? defaultOutDir,
    emptyOutDir: true,
    sourcemap: false,
    manifest: true
  }
});
