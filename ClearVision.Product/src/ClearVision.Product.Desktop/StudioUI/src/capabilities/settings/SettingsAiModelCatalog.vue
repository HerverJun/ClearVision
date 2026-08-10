<script setup lang="ts">
import { CvButton, CvInlineAlert, CvPanel, CvStatusBadge } from '@/design-system';
import type { AiModelProjectionV1, AiModelPublicProjectionV1 } from './decoder';

defineProps<{
  models: readonly AiModelPublicProjectionV1[];
  phase: 'idle' | 'loading' | 'ready' | 'forbidden' | 'error';
  readMessage: string | null;
  safeSubset: boolean;
  selectedId: string | null;
  readBusy: boolean;
  mutationBusy: boolean;
  dirty: boolean;
  canManage: boolean;
}>();

const emit = defineEmits<{
  refresh: [];
  create: [];
  select: [id: string];
  activate: [id: string];
  setDefault: [id: string, role: 'planner' | 'shadow-eval'];
  delete: [id: string];
}>();

function isFullModel(value: AiModelPublicProjectionV1): value is AiModelProjectionV1 {
  return 'hasApiKey' in value;
}

function providerDisplayLabel(value: string): string {
  return value.trim().toLowerCase() === 'openai compatible' ? 'OpenAI 兼容服务' : value || '未声明';
}
</script>

<template>
  <CvPanel
    title="AI 模型目录"
    description="用于智能助手规划、推理和生成的语言模型连接配置；这里不管理视觉算法模型文件。"
    data-settings-ai-model-list
  >
    <template #actions>
      <CvStatusBadge
        :tone="safeSubset ? 'info' : 'ok'"
        :label="safeSubset ? '工程师只读' : '管理员视图'"
      />
    </template>

    <CvInlineAlert
      v-if="phase === 'error'"
      tone="error"
      title="AI 模型读取失败"
    >
      {{ readMessage }}
    </CvInlineAlert>
    <CvInlineAlert
      v-else-if="phase === 'loading'"
      tone="info"
      title="正在读取模型配置"
    >
      正在从 AI 模型服务读取配置。
    </CvInlineAlert>

    <div class="ai-model-catalog__toolbar">
      <CvButton
        size="sm"
        variant="quiet"
        :loading="readBusy"
        :disabled="readBusy || mutationBusy || dirty"
        loading-label="正在刷新模型配置"
        data-settings-ai-authority-refresh
        @click="emit('refresh')"
      >
        刷新模型配置
      </CvButton>
      <span class="ai-model-catalog__count">{{ models.length }} 个模型配置</span>
      <CvButton
        v-if="canManage"
        size="sm"
        variant="primary"
        :disabled="mutationBusy"
        data-settings-ai-model-new
        @click="emit('create')"
      >
        新增模型
      </CvButton>
    </div>

    <div
      v-if="models.length"
      class="ai-model-catalog__list"
    >
      <div
        v-for="model in models"
        :key="model.id"
        class="ai-model-catalog__row"
        :class="{ 'is-selected': selectedId === model.id }"
        data-settings-ai-model-row
      >
        <button
          class="ai-model-catalog__select"
          type="button"
          :title="model.displayName"
          data-settings-ai-model-select
          @click="emit('select', model.id)"
        >
          <span class="ai-model-catalog__row-title">
            <strong>{{ model.displayName }}</strong>
            <CvStatusBadge
              v-if="model.isActive"
              tone="ok"
              label="当前激活"
            />
          </span>
          <span class="ai-model-catalog__row-meta">{{ providerDisplayLabel(model.provider) }} · {{ model.model || '未设置模型名' }}</span>
          <span class="ai-model-catalog__row-meta">{{ model.isEnabled ? '已启用' : '已停用' }} · {{ isFullModel(model) ? (model.hasApiKey ? 'API 密钥已配置' : 'API 密钥未配置') : '敏感信息已隐藏' }}</span>
        </button>
        <div
          v-if="canManage && isFullModel(model)"
          class="ai-model-catalog__row-actions"
        >
          <CvButton
            size="sm"
            variant="quiet"
            :disabled="mutationBusy || model.isActive"
            @click="emit('activate', model.id)"
          >
            激活
          </CvButton>
          <CvButton
            size="sm"
            variant="quiet"
            :disabled="mutationBusy"
            @click="emit('setDefault', model.id, 'planner')"
          >
            设为规划模型
          </CvButton>
          <CvButton
            size="sm"
            variant="quiet"
            :disabled="mutationBusy"
            @click="emit('setDefault', model.id, 'shadow-eval')"
          >
            设为影子评估
          </CvButton>
          <CvButton
            size="sm"
            variant="danger"
            :loading="mutationBusy"
            :disabled="mutationBusy || models.length <= 1"
            :data-model-id="model.id"
            data-settings-ai-model-delete
            @click="emit('delete', model.id)"
          >
            删除
          </CvButton>
        </div>
      </div>
    </div>
    <p
      v-else-if="phase === 'ready'"
      class="ai-model-catalog__empty"
    >
      当前没有模型配置。
    </p>
  </CvPanel>
</template>

<style scoped>
.ai-model-catalog__toolbar { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); margin-bottom: var(--cv-space-3); }
.ai-model-catalog__count { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.ai-model-catalog__list { display: grid; gap: 2px; }
.ai-model-catalog__row { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: var(--cv-space-3); padding: var(--cv-space-2); border: 1px solid transparent; border-bottom-color: var(--cv-border-subtle); }
.ai-model-catalog__row.is-selected { border-color: var(--cv-color-action-border); background: var(--cv-color-action-soft); }
.ai-model-catalog__select { min-width: 0; min-height: 36px; padding: 0; display: grid; gap: 3px; border: 0; background: transparent; color: inherit; text-align: left; cursor: pointer; }
.ai-model-catalog__row-title { min-width: 0; display: flex; align-items: center; gap: var(--cv-space-2); }
.ai-model-catalog__row-title strong { overflow: hidden; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); text-overflow: ellipsis; white-space: nowrap; }
.ai-model-catalog__row-meta { overflow: hidden; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.ai-model-catalog__row-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-1); }
.ai-model-catalog__empty { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-sm); }
@media (max-width: 900px) {
  .ai-model-catalog__row { grid-template-columns: 1fr; }
  .ai-model-catalog__row-actions { justify-content: flex-start; }
}
</style>
