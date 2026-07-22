<script setup lang="ts">
import { CvButton, CvModal } from '@/design-system';
import type { WorkspaceProjectV1 } from '../workspaceContracts';
import type { RuntimePackageExportOwner } from './runtimePackageExportOwner';

defineProps<{
  open: boolean;
  project: WorkspaceProjectV1;
  dirty: boolean;
  owner: RuntimePackageExportOwner;
}>();
const emit = defineEmits<{ close: [] }>();
</script>

<template>
  <CvModal
    :open="open"
    title="导出运行包"
    description="运行包由服务端从已保存工程生成并注册；不会携带临时 Flow override。"
    size="md"
    :close-on-backdrop="false"
    @close="emit('close')"
  >
    <div
      class="package-dialog"
      data-capability="runtime-package-export"
      :data-phase="owner.projection.phase"
    >
      <dl><div><dt>工程</dt><dd>{{ project.name }}</dd></div><div><dt>工程 ID</dt><dd>{{ project.id }}</dd></div><div><dt>保存 revision</dt><dd>{{ project.persistenceRevision }}</dd></div><div><dt>修改状态</dt><dd>{{ dirty ? '将先正式保存' : '已保存' }}</dd></div></dl>
      <p role="status">
        {{ owner.projection.message }}
      </p>
      <dl
        v-if="owner.projection.result"
        class="package-dialog__result"
      >
        <div><dt>Package ID</dt><dd>{{ owner.projection.result.packageId }}</dd></div><div><dt>Package Name</dt><dd>{{ owner.projection.result.packageName }}</dd></div><div><dt>Flow Hash</dt><dd>{{ owner.projection.result.flowHash }}</dd></div><div><dt>Decision Hash</dt><dd>{{ owner.projection.result.decisionConfigurationHash }}</dd></div><div><dt>Station 注册</dt><dd>{{ owner.projection.result.registeredForStationDeployment ? '已注册' : '未注册' }}</dd></div><div><dt>服务端路径</dt><dd>{{ owner.projection.result.packageRootPath }}</dd></div>
      </dl>
      <p
        v-if="owner.projection.phase === 'unknown-outcome'"
        class="package-dialog__warning"
      >
        请求时间 {{ owner.projection.requestedAtUtc }}；revision {{ owner.projection.requestedRevision }}。在管理员核对前不要重复导出。
      </p>
    </div>
    <template #footer>
      <CvButton
        v-if="owner.projection.phase === 'exporting'"
        variant="quiet"
        @click="owner.cancel"
      >
        取消请求
      </CvButton><CvButton
        variant="quiet"
        @click="emit('close')"
      >
        关闭
      </CvButton><CvButton
        variant="primary"
        :disabled="!owner.projection.canExport"
        @click="owner.exportPackage"
      >
        {{ dirty ? '保存并导出' : '导出运行包' }}
      </CvButton>
    </template>
  </CvModal>
</template>

<style scoped>
.package-dialog { display:grid; gap:var(--cv-space-4); }.package-dialog dl { margin:0; display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); border:1px solid var(--cv-border-subtle); }.package-dialog dl div { min-width:0; padding:var(--cv-space-2) var(--cv-space-3); border-bottom:1px solid var(--cv-border-subtle); }.package-dialog dt { color:var(--cv-text-muted); font-size:var(--cv-font-size-2xs); }.package-dialog dd { margin:2px 0 0; overflow-wrap:anywhere; font-size:var(--cv-font-size-xs); }.package-dialog p { margin:0; color:var(--cv-text-secondary); font-size:var(--cv-font-size-xs); }.package-dialog__result { background:var(--cv-color-status-ok-soft); }.package-dialog__warning { padding:var(--cv-space-2); border:1px solid var(--cv-color-status-warning-border); background:var(--cv-color-status-warning-soft); color:var(--cv-color-status-warning-strong)!important; }
</style>
