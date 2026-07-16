<script setup lang="ts">
import { computed, nextTick, onMounted } from 'vue';
import { CvButton, CvStatusBadge } from '@/design-system';
import type { ImageCanvasOwner } from './imageCanvasOwner';

const props = defineProps<{ owner: ImageCanvasOwner }>();
const projection = props.owner.projection;
const canvasId = `image-canvas-${props.owner.projectId.replaceAll('-', '')}`;
const dimensions = computed(() => projection.width > 0
  ? `${projection.width} × ${projection.height}`
  : '暂无图像');

onMounted(async () => {
  await nextTick();
  props.owner.mount(canvasId);
});
</script>

<template>
  <section
    class="image-viewport"
    data-capability="image-canvas"
    :data-image-phase="projection.phase"
    :data-image-generation="projection.imageGeneration"
    :data-image-identity="projection.imageIdentity ?? ''"
    :data-image-scale="projection.scale"
    :data-image-dpr="projection.dpr"
  >
    <header class="image-viewport__toolbar">
      <div class="image-viewport__status">
        <strong>图像</strong>
        <CvStatusBadge
          :tone="projection.phase === 'ready' ? 'ok' : projection.phase === 'error' ? 'ng' : 'idle'"
          :label="projection.phase === 'loading' ? '加载中' : dimensions"
        />
      </div>
      <div class="image-viewport__actions">
        <CvButton
          data-testid="image-zoom-out"
          size="sm"
          :disabled="projection.phase !== 'ready'"
          @click="owner.zoomOut()"
        >
          −
        </CvButton>
        <span>{{ Math.round(projection.scale * 100) }}%</span>
        <CvButton
          data-testid="image-zoom-in"
          size="sm"
          :disabled="projection.phase !== 'ready'"
          @click="owner.zoomIn()"
        >
          ＋
        </CvButton>
        <CvButton
          data-testid="image-fit"
          size="sm"
          :disabled="projection.phase !== 'ready'"
          @click="owner.fit()"
        >
          适应
        </CvButton>
        <CvButton
          data-testid="image-actual-size"
          size="sm"
          :disabled="projection.phase !== 'ready'"
          @click="owner.actualSize()"
        >
          1:1
        </CvButton>
      </div>
    </header>

    <div class="image-viewport__stage">
      <canvas
        :id="canvasId"
        data-testid="image-canvas"
        class="image-viewport__canvas"
        aria-label="Preview ImageCanvas"
      />
      <div
        v-if="projection.phase === 'empty' || projection.phase === 'unmounted'"
        class="image-viewport__empty"
      >
        当前结果没有图像输出
      </div>
      <div
        v-else-if="projection.phase === 'loading'"
        class="image-viewport__empty"
      >
        正在解码图像…
      </div>
      <div
        v-else-if="projection.phase === 'error'"
        class="image-viewport__empty image-viewport__empty--error"
      >
        {{ projection.errorMessage }}
      </div>
    </div>

    <footer
      class="image-viewport__probe"
      :data-probe-phase="projection.pixelProbe.phase"
    >
      <span>{{ projection.pixelProbe.message }}</span>
      <CvButton
        v-if="projection.pixelProbe.phase === 'locked' || projection.pixelProbe.phase === 'roi'"
        size="sm"
        @click="owner.clearPixelLock()"
      >
        清除探针
      </CvButton>
    </footer>
  </section>
</template>

<style scoped>
.image-viewport { min-width: 0; min-height: 0; display: grid; grid-template-rows: 34px minmax(150px, 1fr) 28px; overflow: hidden; background: var(--cv-surface-sunken); }
.image-viewport__toolbar, .image-viewport__probe { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); padding: 0 var(--cv-space-2); background: var(--cv-surface-raised); }
.image-viewport__toolbar { border-bottom: 1px solid var(--cv-border-subtle); }
.image-viewport__probe { border-top: 1px solid var(--cv-border-subtle); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.image-viewport__status, .image-viewport__actions { display: flex; align-items: center; gap: var(--cv-space-2); }
.image-viewport__status strong { font-size: var(--cv-font-size-xs); }
.image-viewport__actions span { min-width: 44px; text-align: center; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.image-viewport__stage { position: relative; min-width: 0; min-height: 0; overflow: hidden; background: var(--image-canvas-background, #10151d); }
.image-viewport__canvas { display: block; width: 100%; height: 100%; outline: none; }
.image-viewport__canvas:focus-visible { box-shadow: inset 0 0 0 2px var(--cv-color-focus-ring); }
.image-viewport__empty { position: absolute; inset: 0; display: grid; place-items: center; pointer-events: none; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); background: color-mix(in srgb, var(--cv-surface-sunken) 88%, transparent); }
.image-viewport__empty--error { color: var(--cv-color-status-ng-strong); }
</style>
