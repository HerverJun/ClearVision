<script setup lang="ts">
import { computed, shallowRef } from 'vue';
import { CvButton, CvModal, CvStatusBadge, type CvStatusTone } from '@/design-system';
import type { WorkspaceProjectV1 } from '../workspaceContracts';
import type { RuntimePackageExportOwner } from './runtimePackageExportOwner';

const props = defineProps<{
  open: boolean;
  project: WorkspaceProjectV1;
  dirty: boolean;
  owner: RuntimePackageExportOwner;
}>();
const emit = defineEmits<{ close: [] }>();
const copiedField = shallowRef<string | null>(null);
const statusTone = computed<CvStatusTone>(() => ({
  success: 'ok', forbidden: 'warning', 'unknown-outcome': 'warning', error: 'error',
  saving: 'info', exporting: 'info', idle: 'idle', disposed: 'idle'
}[props.owner.projection.phase] as CvStatusTone ?? 'idle'));
const statusLabel = computed(() => ({
  idle: '等待导出', saving: '正在保存工程', exporting: '正在生成运行包', success: '运行包已生成',
  forbidden: '需要管理员权限', error: '导出未完成', 'unknown-outcome': '导出结果未知', disposed: '导出已关闭'
}[props.owner.projection.phase] ?? '状态未知'));

async function copyField(key: string, value: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value);
    copiedField.value = key;
  } catch {
    copiedField.value = null;
  }
}
</script>

<template>
  <CvModal
    :open="open"
    title="导出运行包"
    description="确认工程保存状态与导出资格后，由服务端生成可部署运行包。"
    size="md"
    :close-on-backdrop="false"
    @close="emit('close')"
  >
    <div
      class="package-dialog cv-workbench"
      data-capability="runtime-package-export"
      :data-phase="owner.projection.phase"
    >
      <section
        class="package-dialog__eligibility"
        aria-labelledby="package-eligibility-title"
      >
        <div class="package-dialog__section-heading">
          <h3 id="package-eligibility-title">
            导出资格
          </h3>
          <CvStatusBadge
            :tone="dirty ? 'warning' : 'ok'"
            :label="dirty ? '需要先保存' : '可以导出'"
          />
        </div>
        <dl>
          <div><dt>工程</dt><dd>{{ project.name }}</dd></div>
          <div><dt>正式保存修订</dt><dd>r{{ project.persistenceRevision }}</dd></div>
          <div><dt>保存状态</dt><dd>{{ dirty ? '存在未保存修改，将先正式保存' : '工程已正式保存' }}</dd></div>
          <div><dt>输出资格</dt><dd>{{ dirty ? '保存成功后继续生成' : '使用当前正式修订生成' }}</dd></div>
        </dl>
      </section>
      <div
        class="package-dialog__status cv-workbench-status"
        :data-phase="owner.projection.phase"
        :data-tone="statusTone"
        role="status"
        aria-live="polite"
      >
        <CvStatusBadge
          :tone="statusTone"
          :label="statusLabel"
        />
        <p>{{ owner.projection.message }}</p>
      </div>
      <section
        v-if="owner.projection.result"
        class="package-dialog__generated"
        aria-labelledby="package-generated-title"
      >
        <div class="package-dialog__section-heading">
          <h3 id="package-generated-title">
            生成结果
          </h3>
          <span>服务端正式输出</span>
        </div>
        <dl
          v-if="owner.projection.result"
          class="package-dialog__result"
        >
          <div><dt>运行包标识</dt><dd><strong translate="no">{{ owner.projection.result.packageId }}</strong></dd></div>
          <div><dt>运行包名称</dt><dd>{{ owner.projection.result.packageName }}</dd></div>
          <div><dt>判定配置身份</dt><dd>{{ owner.projection.result.decisionConfigurationHash ? '已写入并校验' : '未提供' }}</dd></div>
          <div><dt>工作站注册</dt><dd>{{ owner.projection.result.registeredForStationDeployment ? '已注册，可供部署' : '未注册' }}</dd></div>
        </dl>
      </section>
      <details class="package-dialog__technical cv-technical-detail">
        <summary>技术详情</summary>
        <dl>
          <div>
            <dt>工程标识</dt><dd class="cv-copyable-value">
              <code translate="no">{{ project.id }}</code><CvButton
                size="sm"
                variant="quiet"
                @click="copyField('project', project.id)"
              >
                {{ copiedField === 'project' ? '已复制' : '复制' }}
              </CvButton>
            </dd>
          </div>
          <template v-if="owner.projection.result">
            <div>
              <dt>流程哈希</dt><dd class="cv-copyable-value">
                <code translate="no">{{ owner.projection.result.flowHash }}</code><CvButton
                  size="sm"
                  variant="quiet"
                  @click="copyField('flow', owner.projection.result.flowHash)"
                >
                  {{ copiedField === 'flow' ? '已复制' : '复制' }}
                </CvButton>
              </dd>
            </div>
            <div>
              <dt>判定哈希</dt><dd class="cv-copyable-value">
                <code translate="no">{{ owner.projection.result.decisionConfigurationHash }}</code><CvButton
                  size="sm"
                  variant="quiet"
                  @click="copyField('decision', owner.projection.result.decisionConfigurationHash)"
                >
                  {{ copiedField === 'decision' ? '已复制' : '复制' }}
                </CvButton>
              </dd>
            </div>
            <div>
              <dt>输出位置</dt><dd class="cv-copyable-value">
                <code translate="no">{{ owner.projection.result.packageRootPath }}</code><CvButton
                  size="sm"
                  variant="quiet"
                  @click="copyField('path', owner.projection.result.packageRootPath)"
                >
                  {{ copiedField === 'path' ? '已复制' : '复制' }}
                </CvButton>
              </dd>
            </div>
          </template>
        </dl>
      </details>
      <p
        v-if="owner.projection.phase === 'unknown-outcome'"
        class="package-dialog__warning"
      >
        服务端是否已生成运行包尚未确认。当前工程不能自动重试；请按请求时间、工程标识和修订号核对工作站注册记录后，再决定下一步。
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
.package-dialog h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); }
.package-dialog__section-heading { min-height: 28px; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.package-dialog__section-heading > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.package-dialog dl { margin: var(--cv-space-2) 0 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); border-block: 1px solid var(--cv-border-subtle); }
.package-dialog dl div { min-width: 0; padding: var(--cv-space-2) var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.package-dialog dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.package-dialog dd { margin: 2px 0 0; overflow-wrap: anywhere; font-size: var(--cv-font-size-xs); }
.package-dialog__status p { margin: 0; min-width: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
.package-dialog__result { background: color-mix(in srgb, var(--cv-color-status-ok-soft) 62%, var(--cv-surface-raised)); }
.package-dialog__result strong { color: var(--cv-color-status-ok-strong); }
.package-dialog__technical dl { grid-template-columns: 1fr; }
.package-dialog__warning { margin: 0; padding: var(--cv-space-2); border: 1px solid var(--cv-color-status-warning-border); background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong); font-size: var(--cv-font-size-xs); line-height: 1.45; }
</style>
