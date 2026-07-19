<script setup lang="ts">
import { computed, shallowRef } from 'vue';
import { CvButton, CvIconButton, CvInlineAlert, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import WorkspacePaneHeader from '../WorkspacePaneHeader.vue';
import ImageViewport from '../image/ImageViewport.vue';
import type { PreviewWorkbenchOwner } from './previewWorkbenchOwner';

const props = withDefaults(defineProps<{
  owner: PreviewWorkbenchOwner;
  collapsed?: boolean;
}>(), {
  collapsed: false
});
const emit = defineEmits<{
  toggleCollapsed: [];
}>();
const preview = props.owner.preview.projection;
const roi = props.owner.roi.projection;
const artifactMessage = shallowRef<string | null>(null);
const artifactText = shallowRef<string | null>(null);
const numberFormatter = new Intl.NumberFormat('zh-CN');
const durationFormatter = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 });

const phaseTone = computed(() => {
  if (preview.phase === 'success') return 'ok';
  if (preview.phase === 'blocked' || preview.phase === 'auth-error') return 'warning';
  if (preview.phase === 'error') return 'ng';
  if (preview.phase === 'loading') return 'info';
  return 'idle';
});
const phaseLabel = computed(() => ({
  idle: '等待选择',
  loading: '预览中',
  success: '预览完成',
  empty: '无输出',
  blocked: '条件未满足',
  cancelled: '已取消',
  'auth-error': '会话失效',
  error: '预览失败',
  disposed: '已关闭'
}[preview.phase] ?? '等待预览'));
const structuredText = computed(() => preview.outputData
  ? JSON.stringify(preview.outputData as unknown, null, 2)
  : null);
const previewActionTitle = computed(() => preview.autoPreviewAllowed
  ? '使用当前本地流程执行节点预览'
  : preview.manualReason || '该算子仅允许手动预览');

async function openArtifact(artifactId: string, isImage: boolean): Promise<void> {
  artifactMessage.value = '正在读取附加结果…';
  artifactText.value = null;
  try {
    const result = await props.owner.preview.readArtifact(artifactId);
    if (isImage) {
      await props.owner.image.showArtifact(result.blob, `${preview.requestIdentity?.requestKey}:artifact:${artifactId}`);
      artifactMessage.value = '已在图像区显示附加图像。';
    } else {
      artifactText.value = (await result.blob.text()).slice(0, 16_384);
      artifactMessage.value = '已读取结构化附加结果。';
    }
  } catch (error) {
    artifactMessage.value = error instanceof Error ? error.message : '附加结果已过期或不可用，请重新预览。';
  }
}
</script>

<template>
  <section
    class="preview-panel"
    :class="{ 'preview-panel--collapsed': collapsed }"
    data-capability="preview-workbench"
    :data-preview-phase="preview.phase"
    :data-preview-stale="preview.isStale"
    :data-preview-owner="1"
    :data-preview-collapsed="collapsed"
  >
    <WorkspacePaneHeader
      class="preview-panel__header"
      :title="preview.title || '节点预览'"
      :title-tooltip="preview.title || '节点预览'"
    >
      <template #meta>
        <CvStatusBadge
          :tone="phaseTone"
          :label="phaseLabel"
        />
        <CvStatusBadge
          v-if="preview.isStale"
          tone="warning"
          label="结果已过期"
        />
        <small
          v-if="preview.requestIdentity"
          class="preview-panel__revision"
        >流程 r{{ preview.requestIdentity.flowRevision }}</small>
      </template>
      <span
        v-if="!preview.autoPreviewAllowed"
        class="preview-panel__manual-reason"
        :title="preview.manualReason || '该算子仅允许手动预览'"
      >
        {{ preview.manualReason || '仅允许手动预览' }}
      </span>
      <CvButton
        v-if="preview.canCancel"
        data-testid="preview-cancel"
        size="sm"
        variant="quiet"
        @click="owner.preview.cancel()"
      >
        取消预览
      </CvButton>
      <CvButton
        data-testid="preview-run"
        size="sm"
        :title="previewActionTitle"
        :loading="preview.phase === 'loading'"
        loading-label="正在预览…"
        :disabled="!preview.canPreview || preview.canCancel"
        @click="owner.preview.previewNow()"
      >
        预览节点
      </CvButton>
      <CvIconButton
        data-testid="preview-collapse-toggle"
        size="sm"
        aria-controls="workspace-preview-body"
        :aria-expanded="!collapsed"
        :label="collapsed ? '展开预览区' : '折叠预览区'"
        :title="collapsed ? '展开预览区并恢复上次高度' : '折叠预览区，为流程画布释放空间'"
        @click="emit('toggleCollapsed')"
      >
        <CvIcon
          name="chevron-right"
          size="sm"
          class="preview-panel__collapse-icon"
          :class="{ 'is-collapsed': collapsed }"
        />
      </CvIconButton>
    </WorkspacePaneHeader>

    <div
      id="workspace-preview-body"
      class="preview-panel__body"
      :aria-hidden="collapsed ? 'true' : undefined"
      :inert="collapsed ? true : undefined"
    >
      <ImageViewport :owner="owner.image" />

      <div class="preview-panel__details">
        <div
          v-if="preview.isStale || preview.errorMessage"
          class="preview-panel__alerts"
        >
          <CvInlineAlert
            v-if="preview.isStale"
            tone="warning"
            :title="preview.staleReason || '当前结果对应旧的本地流程，请重新预览。'"
          />
          <CvInlineAlert
            v-if="preview.errorMessage"
            :tone="preview.phase === 'blocked' ? 'warning' : 'error'"
            :title="preview.errorMessage"
          />
        </div>

        <section
          class="preview-panel__roi"
          :data-roi-phase="roi.phase"
        >
          <div class="preview-panel__section-heading">
            <div>
              <strong>感兴趣区域（ROI）</strong>
              <small :title="roi.message">{{ roi.message }}</small>
            </div>
            <div class="preview-panel__roi-actions">
              <CvButton
                v-if="roi.phase !== 'editing'"
                data-testid="roi-start"
                size="sm"
                variant="quiet"
                :disabled="!roi.canStart"
                @click="owner.roi.start()"
              >
                编辑 ROI
              </CvButton>
              <template v-else>
                <CvButton
                  data-testid="roi-undo"
                  size="sm"
                  variant="quiet"
                  :disabled="!roi.canUndo"
                  @click="owner.roi.undo()"
                >
                  撤销
                </CvButton>
                <CvButton
                  data-testid="roi-redo"
                  size="sm"
                  variant="quiet"
                  :disabled="!roi.canRedo"
                  @click="owner.roi.redo()"
                >
                  重做
                </CvButton>
                <CvButton
                  data-testid="roi-cancel"
                  size="sm"
                  variant="quiet"
                  @click="owner.roi.cancel()"
                >
                  放弃
                </CvButton>
                <CvButton
                  data-testid="roi-confirm"
                  size="sm"
                  :disabled="!roi.canConfirm"
                  @click="owner.roi.confirm()"
                >
                  应用 ROI
                </CvButton>
              </template>
            </div>
          </div>
        </section>

        <section
          class="preview-panel__result"
          :data-result-phase="preview.phase"
        >
          <div class="preview-panel__section-heading">
            <strong>结构化结果</strong>
            <small v-if="preview.executionTimeMs !== null">{{ durationFormatter.format(preview.executionTimeMs) }} ms</small>
          </div>
          <pre
            v-if="structuredText"
            translate="no"
          >{{ structuredText }}</pre>
          <div
            v-else
            class="preview-panel__empty-result"
          >
            <CvIcon
              :name="preview.phase === 'error' ? 'error' : preview.phase === 'loading' ? 'refresh' : 'empty'"
              size="md"
            />
            <p v-if="preview.phase === 'empty'">
              预览已完成，但该节点没有图像或结构化输出。
            </p>
            <p v-else-if="preview.phase === 'idle'">
              选择节点后可在此查看预览结果。
            </p>
            <p v-else-if="preview.phase === 'loading'">
              正在使用当前本地流程生成预览…
            </p>
            <p v-else-if="preview.phase === 'cancelled'">
              预览已取消，可再次执行预览。
            </p>
            <p v-else>
              暂无结构化输出。
            </p>
          </div>
        </section>

        <section
          v-if="preview.artifacts.length > 0"
          class="preview-panel__artifacts"
        >
          <div class="preview-panel__section-heading">
            <strong>附加结果</strong>
            <small>{{ preview.artifacts.length }} 项</small>
          </div>
          <ul>
            <li
              v-for="artifact in preview.artifacts"
              :key="artifact.artifactId"
            >
              <span :title="`${artifact.role || artifact.kind} · ${artifact.contentType}`">
                <strong>{{ artifact.role || artifact.kind }}</strong>
                <small translate="no">{{ artifact.contentType }} · {{ numberFormatter.format(artifact.length) }} B</small>
              </span>
              <CvButton
                :data-testid="`artifact-read-${artifact.artifactId}`"
                size="sm"
                variant="quiet"
                @click="openArtifact(artifact.artifactId, artifact.isImage)"
              >
                查看
              </CvButton>
            </li>
          </ul>
          <p
            v-if="artifactMessage"
            aria-live="polite"
          >
            {{ artifactMessage }}
          </p>
          <pre
            v-if="artifactText"
            translate="no"
          >{{ artifactText }}</pre>
          <CvButton
            v-if="owner.image.projection.imageIdentity?.includes(':artifact:')"
            data-testid="artifact-restore-primary"
            size="sm"
            variant="quiet"
            @click="owner.image.restorePrimary()"
          >
            恢复主图
          </CvButton>
        </section>

        <section
          v-if="preview.diagnostics.length > 0 || preview.missingResources.length > 0"
          class="preview-panel__diagnostics"
        >
          <div class="preview-panel__section-heading">
            <strong>诊断信息</strong>
            <small>{{ preview.diagnostics.length + preview.missingResources.length }} 项</small>
          </div>
          <ul>
            <li
              v-for="(item, index) in preview.diagnostics"
              :key="`${item.code}-${index}`"
            >
              <code translate="no">{{ item.code }}</code>
              <span>{{ item.message }}</span>
            </li>
            <li
              v-for="item in preview.missingResources"
              :key="`${item.resourceType}-${item.resourceKey}`"
            >
              <code translate="no">{{ item.diagnosticCode }}</code>
              <span>{{ item.description }}</span>
            </li>
          </ul>
        </section>
      </div>
    </div>
  </section>
</template>

<style scoped>
.preview-panel {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: 38px minmax(0, 1fr);
  overflow: hidden;
  border-top: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
  container-name: preview;
  container-type: inline-size;
}
.preview-panel--collapsed { grid-template-rows: 38px 0; }
.preview-panel--collapsed .preview-panel__body { display: none; }
.preview-panel__revision,
.preview-panel__manual-reason { overflow: hidden; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.preview-panel__manual-reason { max-width: 180px; }
.preview-panel__collapse-icon { transform: rotate(90deg); transition: transform var(--cv-motion-duration-fast) var(--cv-motion-ease-standard); }
.preview-panel__collapse-icon.is-collapsed { transform: rotate(-90deg); }
.preview-panel__body { min-width: 0; min-height: 0; display: grid; grid-template-columns: minmax(0, 1.25fr) minmax(240px, .75fr); overflow: hidden; }
.preview-panel__details { min-width: 0; min-height: 0; overflow-y: auto; overflow-x: hidden; overscroll-behavior: contain; border-left: 1px solid var(--cv-border-subtle); scrollbar-gutter: stable; }
.preview-panel__alerts { padding: var(--cv-space-2) var(--cv-space-3) 0; display: grid; gap: var(--cv-space-2); }
.preview-panel__roi,
.preview-panel__result,
.preview-panel__artifacts,
.preview-panel__diagnostics { min-width: 0; padding: var(--cv-space-2) var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.preview-panel__section-heading { min-width: 0; display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-2); }
.preview-panel__section-heading > div { min-width: 0; display: grid; gap: 2px; }
.preview-panel__section-heading strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.preview-panel__section-heading small { overflow: hidden; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.preview-panel__roi-actions { max-width: 100%; display: flex; align-items: center; justify-content: flex-end; flex-wrap: wrap; gap: var(--cv-space-1); }
.preview-panel__result pre,
.preview-panel__artifacts pre { margin: var(--cv-space-2) 0 0; padding: var(--cv-space-2); overflow: visible; border-radius: var(--cv-radius-sm); background: var(--cv-surface-sunken); color: var(--cv-text-primary); font-family: var(--cv-font-mono); font-size: var(--cv-font-size-2xs); line-height: 1.45; white-space: pre-wrap; overflow-wrap: anywhere; }
.preview-panel__empty-result { min-height: 58px; display: flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-muted); }
.preview-panel__empty-result p { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: 1.45; overflow-wrap: anywhere; }
.preview-panel__empty-result :deep(svg) { flex: 0 0 auto; color: var(--cv-border-strong); }
.preview-panel__artifacts ul,
.preview-panel__diagnostics ul { margin: var(--cv-space-2) 0 0; padding: 0; display: grid; list-style: none; }
.preview-panel__artifacts li { min-width: 0; padding: var(--cv-space-1) 0; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); }
.preview-panel__artifacts li > span { min-width: 0; display: grid; gap: 2px; }
.preview-panel__artifacts li strong,
.preview-panel__artifacts li small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.preview-panel__artifacts li strong { font-size: var(--cv-font-size-2xs); font-weight: var(--cv-font-weight-medium); }
.preview-panel__artifacts li small { color: var(--cv-text-muted); font-size: 9px; }
.preview-panel__artifacts > p { margin: var(--cv-space-2) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: 1.45; overflow-wrap: anywhere; }
.preview-panel__diagnostics li { min-width: 0; padding: var(--cv-space-1) 0; display: grid; grid-template-columns: minmax(72px, auto) minmax(0, 1fr); gap: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); font-size: var(--cv-font-size-2xs); line-height: 1.45; }
.preview-panel__diagnostics code { color: var(--cv-color-status-warning-strong); font-family: var(--cv-font-mono); font-size: 9px; overflow-wrap: anywhere; }
.preview-panel__diagnostics span { color: var(--cv-text-secondary); overflow-wrap: anywhere; }

@container preview (max-width: 680px) {
  .preview-panel__manual-reason,
  .preview-panel__revision { display: none; }
}
</style>
