<script setup lang="ts">
import { computed, ref } from 'vue';
import { CvButton, CvIconButton, CvInlineAlert, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
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
const artifactMessage = ref<string | null>(null);
const artifactText = ref<string | null>(null);

const phaseTone = computed(() => {
  if (preview.phase === 'success') return 'ok';
  if (preview.phase === 'blocked' || preview.phase === 'auth-error') return 'warning';
  if (preview.phase === 'error') return 'ng';
  return 'idle';
});

const structuredText = computed(() => preview.outputData
  ? JSON.stringify(preview.outputData as unknown, null, 2)
  : null);

async function openArtifact(artifactId: string, isImage: boolean): Promise<void> {
  artifactMessage.value = '正在读取附加结果…';
  artifactText.value = null;
  try {
    const result = await props.owner.preview.readArtifact(artifactId);
    if (isImage) {
      await props.owner.image.showArtifact(result.blob, `${preview.requestIdentity?.requestKey}:artifact:${artifactId}`);
      artifactMessage.value = '已在图像预览区显示附加图像。';
    } else {
      artifactText.value = (await result.blob.text()).slice(0, 16_384);
      artifactMessage.value = '已读取结构化附加结果。';
    }
  } catch (error) {
    artifactMessage.value = error instanceof Error ? error.message : '附加结果已过期或不可用。';
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
    <header class="preview-panel__header">
      <div class="preview-panel__identity">
        <strong>{{ preview.title || '节点预览' }}</strong>
        <CvStatusBadge
          :tone="phaseTone"
          :label="preview.statusText"
        />
        <CvStatusBadge
          v-if="preview.isStale"
          tone="warning"
          label="已过期"
        />
        <small v-if="preview.requestIdentity">
          草稿 r{{ preview.requestIdentity.flowRevision }}
        </small>
      </div>
      <div class="preview-panel__actions">
        <span
          v-if="!preview.autoPreviewAllowed"
          class="preview-panel__manual-reason"
        >
          {{ preview.manualReason || '该算子仅允许手动预览' }}
        </span>
        <CvButton
          v-if="preview.canCancel"
          data-testid="preview-cancel"
          size="sm"
          @click="owner.preview.cancel()"
        >
          取消
        </CvButton>
        <CvButton
          data-testid="preview-run"
          size="sm"
          :disabled="!preview.canPreview || preview.canCancel"
          @click="owner.preview.previewNow()"
        >
          手动预览
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
      </div>
    </header>

    <div
      id="workspace-preview-body"
      class="preview-panel__body"
      :aria-hidden="collapsed ? 'true' : undefined"
      :inert="collapsed ? true : undefined"
    >
      <ImageViewport :owner="owner.image" />

      <div class="preview-panel__details">
        <CvInlineAlert
          v-if="preview.isStale"
          tone="warning"
          :title="preview.staleReason || '预览已过期'"
        />
        <CvInlineAlert
          v-if="preview.errorMessage"
          :tone="preview.phase === 'blocked' ? 'warning' : 'error'"
          :title="preview.errorMessage"
        />

        <section
          class="preview-panel__roi"
          :data-roi-phase="roi.phase"
        >
          <div>
            <strong>ROI / 图像参数</strong>
            <small>{{ roi.message }}</small>
          </div>
          <div class="preview-panel__roi-actions">
            <CvButton
              v-if="roi.phase !== 'editing'"
              data-testid="roi-start"
              size="sm"
              :disabled="!roi.canStart"
              @click="owner.roi.start()"
            >
              图上编辑
            </CvButton>
            <template v-else>
              <CvButton
                data-testid="roi-undo"
                size="sm"
                :disabled="!roi.canUndo"
                @click="owner.roi.undo()"
              >
                撤销草稿
              </CvButton>
              <CvButton
                data-testid="roi-redo"
                size="sm"
                :disabled="!roi.canRedo"
                @click="owner.roi.redo()"
              >
                重做草稿
              </CvButton>
              <CvButton
                data-testid="roi-cancel"
                size="sm"
                @click="owner.roi.cancel()"
              >
                取消
              </CvButton>
              <CvButton
                data-testid="roi-confirm"
                size="sm"
                :disabled="!roi.canConfirm"
                @click="owner.roi.confirm()"
              >
                确认
              </CvButton>
            </template>
          </div>
        </section>

        <section class="preview-panel__result">
          <strong>结构化结果</strong>
          <pre v-if="structuredText">{{ structuredText }}</pre>
          <p v-else-if="preview.phase === 'empty'">
            预览成功，但该算子没有可预览图像或结构化输出。
          </p>
          <p v-else-if="preview.phase === 'idle'">
            选择节点后可执行预览。
          </p>
          <p v-else-if="preview.phase === 'loading'">
            请求已绑定当前本地流程草稿，正在等待结果。
          </p>
          <p v-else>
            暂无结构化输出。
          </p>
        </section>

        <section
          v-if="preview.artifacts.length > 0"
          class="preview-panel__artifacts"
        >
          <strong>附加结果（{{ preview.artifacts.length }}）</strong>
          <ul>
            <li
              v-for="artifact in preview.artifacts"
              :key="artifact.artifactId"
            >
              <span>{{ artifact.role || artifact.kind }} · {{ artifact.contentType }} · {{ artifact.length }} B</span>
              <CvButton
                :data-testid="`artifact-read-${artifact.artifactId}`"
                size="sm"
                @click="openArtifact(artifact.artifactId, artifact.isImage)"
              >
                读取
              </CvButton>
            </li>
          </ul>
          <p v-if="artifactMessage">
            {{ artifactMessage }}
          </p>
          <pre v-if="artifactText">{{ artifactText }}</pre>
          <CvButton
            v-if="owner.image.projection.imageIdentity?.includes(':artifact:')"
            data-testid="artifact-restore-primary"
            size="sm"
            @click="owner.image.restorePrimary()"
          >
            恢复主图
          </CvButton>
        </section>

        <section
          v-if="preview.diagnostics.length > 0 || preview.missingResources.length > 0"
          class="preview-panel__diagnostics"
        >
          <strong>诊断</strong>
          <ul>
            <li
              v-for="(item, index) in preview.diagnostics"
              :key="`${item.code}-${index}`"
            >
              {{ item.code }} · {{ item.message }}
            </li>
            <li
              v-for="item in preview.missingResources"
              :key="`${item.resourceType}-${item.resourceKey}`"
            >
              {{ item.diagnosticCode }} · {{ item.description }}
            </li>
          </ul>
        </section>
      </div>
    </div>
  </section>
</template>

<style scoped>
.preview-panel { min-width: 0; min-height: 0; display: grid; grid-template-rows: 38px minmax(0, 1fr); overflow: hidden; border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.preview-panel--collapsed { grid-template-rows: 38px 0; }
.preview-panel--collapsed .preview-panel__body { display: none; }
.preview-panel__header { min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); padding: 0 var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.preview-panel__identity, .preview-panel__actions, .preview-panel__roi-actions { min-width: 0; display: flex; align-items: center; gap: var(--cv-space-2); }
.preview-panel__roi-actions { max-width: 100%; flex-wrap: wrap; justify-content: flex-end; }
.preview-panel__identity strong { font-size: var(--cv-font-size-xs); }
.preview-panel__collapse-icon { transform: rotate(90deg); transition: transform var(--cv-motion-duration-fast) var(--cv-motion-ease-standard); }
.preview-panel__collapse-icon.is-collapsed { transform: rotate(-90deg); }
.preview-panel__identity small, .preview-panel__manual-reason { overflow: hidden; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.preview-panel__body { min-width: 0; min-height: 0; display: grid; grid-template-columns: minmax(300px, 1.2fr) minmax(280px, .8fr); overflow: hidden; }
.preview-panel__details { min-width: 0; min-height: 0; padding: var(--cv-space-2); display: grid; align-content: start; gap: var(--cv-space-2); overflow: auto; border-left: 1px solid var(--cv-border-subtle); }
.preview-panel__roi { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-2); padding: var(--cv-space-2); flex-wrap: wrap; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.preview-panel__roi strong, .preview-panel__roi small { display: block; }
.preview-panel__roi strong, .preview-panel__result > strong, .preview-panel__artifacts > strong, .preview-panel__diagnostics > strong { font-size: var(--cv-font-size-xs); }
.preview-panel__roi small, .preview-panel__result p, .preview-panel__artifacts p { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.preview-panel__result pre, .preview-panel__artifacts pre { max-height: 150px; margin: var(--cv-space-1) 0 0; padding: var(--cv-space-2); overflow: auto; border-radius: var(--cv-radius-sm); background: var(--cv-surface-sunken); color: var(--cv-text-primary); font-size: var(--cv-font-size-2xs); white-space: pre-wrap; }
.preview-panel__artifacts ul, .preview-panel__diagnostics ul { margin: var(--cv-space-1) 0; padding: 0; display: grid; gap: var(--cv-space-1); list-style: none; font-size: var(--cv-font-size-2xs); }
.preview-panel__artifacts li { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
@media (max-width: 920px) { .preview-panel__body { grid-template-columns: minmax(0, 1fr); } .preview-panel__details { display: none; } }
</style>
