<script setup lang="ts">
import { computed, nextTick, onMounted } from 'vue';
import { CvButton, CvIconButton } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { ImageCanvasOwner } from './imageCanvasOwner';

const props = withDefaults(defineProps<{
  owner: ImageCanvasOwner;
  expanded?: boolean;
}>(), {
  expanded: false
});
const emit = defineEmits<{
  toggleExpanded: [];
}>();
const projection = props.owner.projection;
const canvasId = `image-canvas-${props.owner.projectId.replaceAll('-', '')}`;
const dimensions = computed(() => projection.width > 0
  ? `${projection.width} × ${projection.height}`
  : '暂无图像');
const phaseLabel = computed(() => ({
  unmounted: '等待预览',
  empty: '无图像输出',
  loading: '正在加载…',
  ready: dimensions.value,
  error: '图像加载失败',
  disposed: '已关闭'
}[projection.phase] ?? dimensions.value));

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
        <span :data-phase="projection.phase">{{ phaseLabel }}</span>
      </div>
      <div
        class="image-viewport__actions"
        role="group"
        aria-label="图像视图工具"
      >
        <CvIconButton
          data-testid="image-zoom-out"
          size="sm"
          label="缩小图像"
          :disabled="projection.phase !== 'ready'"
          @click="owner.zoomOut()"
        >
          <CvIcon
            name="zoom-out"
            size="sm"
          />
        </CvIconButton>
        <span class="image-viewport__scale">{{ Math.round(projection.scale * 100) }}%</span>
        <CvIconButton
          data-testid="image-zoom-in"
          size="sm"
          label="放大图像"
          :disabled="projection.phase !== 'ready'"
          @click="owner.zoomIn()"
        >
          <CvIcon
            name="zoom-in"
            size="sm"
          />
        </CvIconButton>
        <CvButton
          data-testid="image-fit"
          size="sm"
          variant="quiet"
          aria-label="适应预览区"
          title="适应预览区"
          :disabled="projection.phase !== 'ready'"
          @click="owner.fit()"
        >
          <template #leading>
            <CvIcon
              name="fit"
              size="sm"
            />
          </template>
          适应
        </CvButton>
        <CvButton
          data-testid="image-actual-size"
          size="sm"
          variant="quiet"
          aria-label="按图像实际像素显示"
          title="按图像实际像素显示"
          :disabled="projection.phase !== 'ready'"
          @click="owner.actualSize()"
        >
          <template #leading>
            <CvIcon
              name="actual-size"
              size="sm"
            />
          </template>
          1:1
        </CvButton>
        <CvIconButton
          data-testid="image-expand"
          size="sm"
          :label="expanded ? '退出大图视图' : '打开大图视图'"
          :title="expanded ? '退出大图视图' : '打开大图视图'"
          @click="emit('toggleExpanded')"
        >
          <CvIcon
            :name="expanded ? 'minimize' : 'maximize'"
            size="sm"
          />
        </CvIconButton>
      </div>
    </header>

    <div class="image-viewport__stage">
      <canvas
        :id="canvasId"
        tabindex="0"
        data-testid="image-canvas"
        class="image-viewport__canvas"
        aria-label="节点预览图像画布"
      />
      <div
        v-if="projection.phase === 'empty' || projection.phase === 'unmounted'"
        class="image-viewport__empty"
        role="status"
      >
        <CvIcon
          name="empty"
          size="lg"
        />
        <strong>当前节点没有图像输出</strong>
        <span>结构化结果仍会显示在右侧结果区。</span>
      </div>
      <div
        v-else-if="projection.phase === 'loading'"
        class="image-viewport__empty"
        role="status"
      >
        <CvIcon
          name="refresh"
          size="lg"
        />
        <strong>正在解码预览图像…</strong>
      </div>
      <div
        v-else-if="projection.phase === 'error'"
        class="image-viewport__empty image-viewport__empty--error"
        role="alert"
      >
        <CvIcon
          name="error"
          size="lg"
        />
        <strong>图像加载失败</strong>
        <span>{{ projection.errorMessage || '请重新预览；若问题持续，请查看右侧诊断信息。' }}</span>
      </div>
    </div>

    <footer
      class="image-viewport__probe"
      :data-probe-phase="projection.pixelProbe.phase"
      :aria-live="projection.pixelProbe.phase === 'locked' || projection.pixelProbe.phase === 'roi' ? 'polite' : 'off'"
    >
      <span :title="projection.pixelProbe.message">{{ projection.pixelProbe.message }}</span>
      <CvButton
        v-if="projection.pixelProbe.phase === 'locked' || projection.pixelProbe.phase === 'roi'"
        size="sm"
        variant="quiet"
        aria-label="清除像素探针"
        title="清除像素探针"
        @click="owner.clearPixelLock()"
      >
        清除探针
      </CvButton>
    </footer>
  </section>
</template>

<style scoped>
.image-viewport { min-width: 0; min-height: 0; display: grid; grid-template-rows: 34px minmax(150px, 1fr) 26px; overflow: hidden; background: var(--cv-surface-sunken); container-type: inline-size; }
.image-viewport__toolbar,
.image-viewport__probe { min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); padding: 0 var(--cv-space-2); background: var(--cv-surface-raised); }
.image-viewport__toolbar { border-bottom: 1px solid var(--cv-border-subtle); }
.image-viewport__probe { border-top: 1px solid var(--cv-border-subtle); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.image-viewport__probe > span { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.image-viewport__status,
.image-viewport__actions { min-width: 0; display: flex; align-items: center; gap: 2px; }
.image-viewport__status strong { font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.image-viewport__status span { overflow: hidden; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.image-viewport__status span::before { content: ""; width: 5px; height: 5px; margin-right: var(--cv-space-1); display: inline-block; border-radius: 50%; background: var(--cv-color-status-idle); vertical-align: 1px; }
.image-viewport__status span[data-phase="ready"] { color: var(--cv-color-status-ok-strong); }
.image-viewport__status span[data-phase="ready"]::before { background: var(--cv-color-status-ok); }
.image-viewport__status span[data-phase="loading"] { color: var(--cv-color-status-info-strong); }
.image-viewport__status span[data-phase="loading"]::before { background: var(--cv-color-status-info); }
.image-viewport__status span[data-phase="error"] { color: var(--cv-color-status-ng-strong); }
.image-viewport__status span[data-phase="error"]::before { background: var(--cv-color-status-ng); }
.image-viewport__scale { min-width: 38px; text-align: center; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); font-variant-numeric: tabular-nums; }
.image-viewport__actions :deep(.cv-button--sm) { padding-inline: var(--cv-space-2); }
.image-viewport__stage { position: relative; min-width: 0; min-height: 0; overflow: hidden; background: var(--cv-surface-image-canvas); }
.image-viewport__canvas { display: block; width: 100%; height: 100%; outline: none; }
.image-viewport__canvas:focus-visible { box-shadow: inset 0 0 0 2px var(--cv-focus-ring-color); }
.image-viewport__empty { position: absolute; inset: 0; padding: var(--cv-space-4); display: grid; place-content: center; justify-items: center; gap: var(--cv-space-1); text-align: center; pointer-events: none; color: var(--cv-text-muted); background: color-mix(in srgb, var(--cv-surface-sunken) 90%, transparent); }
.image-viewport__empty :deep(svg) { margin-bottom: var(--cv-space-1); color: var(--cv-border-strong); }
.image-viewport__empty strong { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.image-viewport__empty span { max-width: 42ch; font-size: var(--cv-font-size-2xs); line-height: 1.45; overflow-wrap: anywhere; }
.image-viewport__empty--error,
.image-viewport__empty--error strong,
.image-viewport__empty--error :deep(svg) { color: var(--cv-color-status-ng-strong); }

@container (max-width: 390px) {
  .image-viewport__status strong { display: none; }
  .image-viewport__actions :deep(.cv-button__visual-label) { display: none; }
  .image-viewport__actions :deep(.cv-button--sm) { width: 28px; padding-inline: 0; }
}
</style>
