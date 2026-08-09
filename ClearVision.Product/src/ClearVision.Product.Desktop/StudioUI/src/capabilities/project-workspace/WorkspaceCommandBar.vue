<script setup lang="ts">
import { RouterLink } from 'vue-router';
import { CvButton, CvStatusBadge, type CvStatusTone } from '@/design-system';
import { CvIcon } from '@/design-system/icons';

defineProps<{
  projectId: string;
  projectName: string;
  projectSubtitle: string | null;
  projectTitle: string;
  showProjectDetails: boolean;
  showSaveState: boolean;
  saveStateTone: CvStatusTone;
  saveStateLabel: string;
  canOpenDecision: boolean;
  showSave: boolean;
  canSave: boolean;
  saveLabel: string;
  canOpenVariables: boolean;
  showRuntimePackage: boolean;
  canOpenRuntimePackage: boolean;
  canOpenTemplates: boolean;
  showResults: boolean;
  resultsLink: string;
  canRetrySave: boolean;
  canReconcileSave: boolean;
  canReapplyConflict: boolean;
  canDiscardConflict: boolean;
}>();

const emit = defineEmits<{
  openDecision: [];
  requestSave: [];
  openVariables: [];
  openRuntimePackage: [];
  openTemplates: [];
  retrySave: [];
  reconcileSave: [];
  reapplyConflict: [];
  discardConflict: [];
}>();
</script>

<template>
  <header class="workspace-shell__toolbar">
    <div class="workspace-shell__identity">
      <nav
        class="workspace-shell__back-nav"
        aria-label="工程导航"
      >
        <RouterLink
          class="workspace-shell__back"
          to="/projects"
        >
          工程列表
        </RouterLink>
        <RouterLink
          v-if="showProjectDetails"
          class="workspace-shell__back"
          :to="`/projects/${projectId}`"
        >
          工程详情
        </RouterLink>
      </nav>
      <span
        class="workspace-shell__divider"
        aria-hidden="true"
      />
      <div
        class="workspace-shell__project"
        :title="projectTitle"
      >
        <strong>{{ projectName }}</strong>
        <small v-if="projectSubtitle">{{ projectSubtitle }}</small>
      </div>
      <CvStatusBadge
        v-if="showSaveState"
        class="workspace-shell__save-state"
        :tone="saveStateTone"
        :label="saveStateLabel"
      />
    </div>

    <div class="workspace-shell__commands">
      <div class="workspace-shell__command-group workspace-shell__command-group--primary">
        <CvButton
          data-capability="final-decision"
          data-testid="final-decision"
          size="sm"
          variant="secondary"
          :disabled="!canOpenDecision"
          title="配置正式运行使用的最终判定"
          @click="emit('openDecision')"
        >
          <template #leading>
            <CvIcon
              name="decision"
              size="sm"
            />
          </template>
          最终判定
        </CvButton>
        <CvButton
          v-if="showSave"
          data-testid="workspace-save"
          size="sm"
          variant="secondary"
          :disabled="!canSave"
          @click="emit('requestSave')"
        >
          <template #leading>
            <CvIcon
              name="save"
              size="sm"
            />
          </template>
          {{ saveLabel }}
        </CvButton>
      </div>
      <span
        class="workspace-shell__command-divider"
        aria-hidden="true"
      />
      <div class="workspace-shell__command-group workspace-shell__command-group--tools">
        <CvButton
          data-capability="global-variables"
          data-testid="global-variables"
          size="sm"
          variant="quiet"
          :disabled="!canOpenVariables"
          title="管理本工程的变量定义与绑定"
          @click="emit('openVariables')"
        >
          <template #leading>
            <CvIcon
              name="variables"
              size="sm"
            />
          </template>
          全局变量
        </CvButton>
        <CvButton
          v-if="showRuntimePackage"
          data-testid="runtime-package-export"
          size="sm"
          variant="quiet"
          :disabled="!canOpenRuntimePackage"
          title="从已正式保存的工程导出运行包"
          @click="emit('openRuntimePackage')"
        >
          运行包
        </CvButton>
        <CvButton
          data-testid="workspace-templates"
          size="sm"
          variant="quiet"
          :disabled="!canOpenTemplates"
          title="搜索、应用和维护流程模板"
          @click="emit('openTemplates')"
        >
          <template #leading>
            <CvIcon
              name="copy"
              size="sm"
            />
          </template>
          模板
        </CvButton>
        <RouterLink
          v-if="showResults"
          class="workspace-shell__results-link"
          :to="resultsLink"
          data-testid="workspace-results"
        >
          本次结果
        </RouterLink>
      </div>
      <CvButton
        v-if="canRetrySave"
        data-testid="workspace-save-retry"
        size="sm"
        @click="emit('retrySave')"
      >
        重试
      </CvButton>
      <CvButton
        v-if="canReconcileSave"
        data-testid="workspace-save-reconcile"
        size="sm"
        @click="emit('reconcileSave')"
      >
        核对保存结果
      </CvButton>
      <CvButton
        v-if="canReapplyConflict"
        data-testid="workspace-conflict-reapply"
        size="sm"
        @click="emit('reapplyConflict')"
      >
        重新应用本地草稿
      </CvButton>
      <CvButton
        v-if="canDiscardConflict"
        data-testid="workspace-conflict-discard"
        size="sm"
        @click="emit('discardConflict')"
      >
        放弃本地草稿
      </CvButton>
    </div>
  </header>
</template>

<style scoped>
.workspace-shell__toolbar {
  min-width: 0;
  min-height: var(--cv-workspace-toolbar-height, 44px);
  display: grid;
  grid-template-columns: minmax(260px, auto) minmax(0, 1fr);
  align-items: center;
  gap: var(--cv-space-2);
  padding: 4px 10px;
  border-bottom: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-page);
}
.workspace-shell__identity,
.workspace-shell__commands {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: var(--cv-space-1);
}
.workspace-shell__commands :deep(.cv-button) { flex: 0 0 auto; }
.workspace-shell__commands :deep(.cv-button:not(.cv-button--primary)) { background: var(--cv-surface-raised); }
.workspace-shell__commands :deep(.cv-button--primary) { background: var(--cv-color-industrial-blue); border-color: var(--cv-color-industrial-blue); }
.workspace-shell__commands :deep(.cv-button--primary:hover:not(:disabled)) { background: var(--cv-color-industrial-blue-hover); border-color: var(--cv-color-industrial-blue-hover); }
.workspace-shell__identity > div { min-width: 0; }
.workspace-shell__identity strong,
.workspace-shell__identity small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.workspace-shell__identity strong { font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); letter-spacing: 0; }
.workspace-shell__identity small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.workspace-shell__project { display: flex; align-items: baseline; gap: var(--cv-space-2); }
.workspace-shell__save-state { flex: 0 0 auto; }
.workspace-shell__back-nav { display: flex; align-items: center; gap: var(--cv-space-2); }
.workspace-shell__back { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); text-decoration: none; white-space: nowrap; }
.workspace-shell__back:hover { color: var(--cv-color-link); }
.workspace-shell__commands {
  justify-content: flex-end;
  overflow-x: auto;
  scrollbar-width: none;
}
.workspace-shell__command-group { display: flex; align-items: center; gap: 3px; }
.workspace-shell__command-divider { width: 1px; height: 24px; flex: 0 0 auto; background: var(--cv-border-subtle); }
.workspace-shell__commands::-webkit-scrollbar { display: none; }
.workspace-shell__results-link {
  display: inline-flex;
  align-items: center;
  height: var(--cv-density-control-height-sm);
  padding: 0 var(--cv-space-2);
  border: 1px solid transparent;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-medium);
  text-decoration: none;
  white-space: nowrap;
}
.workspace-shell__results-link:hover { border-color: var(--cv-control-border-hover); background: var(--cv-interactive-hover); color: var(--cv-color-link); }
.workspace-shell__results-link:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.workspace-shell__divider { width: 1px; height: 18px; background: var(--cv-border-subtle); }

@media (max-width: 1420px) {
  .workspace-shell__toolbar { grid-template-columns: minmax(210px, auto) minmax(0, 1fr); }
  .workspace-shell__back-nav .workspace-shell__back:first-child,
  .workspace-shell__project small,
  .workspace-shell__save-state { display: none; }
}

@media (max-width: 920px) {
  .workspace-shell__back-nav .workspace-shell__back:first-child { display: none; }
}
</style>
