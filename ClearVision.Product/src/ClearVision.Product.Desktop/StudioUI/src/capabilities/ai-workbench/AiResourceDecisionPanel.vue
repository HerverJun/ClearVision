<script setup lang="ts">
import { computed, reactive } from 'vue';
import { CvButton, CvStatusBadge } from '@/design-system/primitives';
import type {
  AiCameraBindingOptionV1,
  AiResourceDecisionSelectionV1,
  AiResourceRequirementV1
} from './contracts';

const props = defineProps<{
  resources: readonly AiResourceRequirementV1[];
  cameraBindings: readonly AiCameraBindingOptionV1[];
  busy: boolean;
}>();

const emit = defineEmits<{
  save: [decisions: readonly AiResourceDecisionSelectionV1[]];
}>();

const selections = reactive<Record<string, string>>({});
const selectableCount = computed(() => props.resources.filter(item => item.resourceType === 'camera_binding').length);
const canSave = computed(() => props.resources.some(resource => resource.resourceType === 'camera_binding' && selections[resource.canonicalId]));

function typeLabel(type: string): string {
  if (type === 'camera_binding') return '相机绑定';
  if (type === 'model_resource') return '模型资源';
  if (type === 'template_artifact') return '模板 / 图像资源';
  if (type === 'calibration_resource') return '标定资源';
  return '工程资源';
}

function save(): void {
  const decisions = props.resources.flatMap(resource => {
    const value = selections[resource.canonicalId];
    if (resource.resourceType !== 'camera_binding' || !value) return [];
    return [Object.freeze({
      canonicalId: resource.canonicalId,
      resourceKey: value
    })];
  });
  if (decisions.length) emit('save', Object.freeze(decisions));
}
</script>

<template>
  <section
    class="ai-resources"
    aria-labelledby="ai-resources-title"
    data-ai-resource-decisions
  >
    <header class="ai-resources__header">
      <div>
        <h2 id="ai-resources-title">
          资源决策
        </h2><p>不接受自由文本路径或前端猜测。</p>
      </div>
      <span>{{ resources.length }} 项</span>
    </header>
    <ul class="ai-resources__list">
      <li
        v-for="resource in resources"
        :key="resource.canonicalId"
      >
        <div class="ai-resources__title">
          <strong>{{ resource.resourceName || typeLabel(resource.resourceType) }}</strong>
          <CvStatusBadge
            tone="warning"
            label="阻断"
          />
        </div>
        <dl>
          <div><dt>类型</dt><dd>{{ typeLabel(resource.resourceType) }}</dd></div>
          <div><dt>所属算子</dt><dd>{{ resource.operatorType || resource.operatorKey }}</dd></div>
          <div><dt>参数</dt><dd>{{ resource.parameterName }}</dd></div>
        </dl>
        <p>{{ resource.description || '缺少必要资源身份。' }}</p>
        <label v-if="resource.resourceType === 'camera_binding'">
          <span>选择已配置相机</span>
          <select
            v-model="selections[resource.canonicalId]"
            :disabled="busy || cameraBindings.length === 0"
          >
            <option value="">请选择相机绑定</option>
            <option
              v-for="binding in cameraBindings"
              :key="binding.id"
              :value="binding.id"
              :disabled="!binding.isEnabled"
            >
              {{ binding.displayName }}
            </option>
          </select>
        </label>
        <p
          v-else
          class="ai-resources__blocked"
        >
          当前没有获批的安全选择合同。本项保持阻断；解决位置：{{ resource.resolutionTarget || '对应设置' }}。
        </p>
        <p
          v-if="resource.resourceType === 'camera_binding' && cameraBindings.length === 0"
          class="ai-resources__blocked"
        >
          尚未查询到可用相机绑定，请先在相机设置中完成配置。
        </p>
      </li>
    </ul>
    <footer>
      <span v-if="selectableCount < resources.length">{{ resources.length - selectableCount }} 项因缺少安全选择合同继续阻断</span>
      <CvButton
        size="sm"
        variant="primary"
        :disabled="!canSave"
        :loading="busy"
        loading-label="正在保存资源决策"
        @click="save"
      >
        保存资源决策
      </CvButton>
    </footer>
  </section>
</template>

<style scoped>
.ai-resources { min-width: 0; overflow: hidden; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-lg); background: var(--cv-surface-raised); }
.ai-resources__header { display: flex; align-items: start; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-resources h2 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.ai-resources__header p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.ai-resources__header > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.ai-resources__list { display: grid; margin: 0; padding: 0; list-style: none; }
.ai-resources__list > li { display: grid; gap: var(--cv-space-3); padding: var(--cv-space-4) var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-resources__title { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); }
.ai-resources__title strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.ai-resources dl { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); margin: 0; }
.ai-resources dl div { padding-inline: var(--cv-space-2); border-inline-start: 1px solid var(--cv-border-subtle); }
.ai-resources dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.ai-resources dd { margin: 2px 0 0; overflow-wrap: anywhere; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.ai-resources__list p, .ai-resources label span { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.ai-resources label { display: grid; gap: var(--cv-space-2); }
.ai-resources select { width: 100%; height: var(--cv-density-control-height); padding: 0 var(--cv-space-3); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); font: inherit; font-size: var(--cv-font-size-xs); }
.ai-resources select:focus-visible { border-color: var(--cv-focus-ring-color); outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.ai-resources__blocked { color: var(--cv-color-status-warning) !important; }
.ai-resources footer { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-space-3) var(--cv-density-panel-padding); background: var(--cv-surface-page); }
.ai-resources footer span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
@media (max-width: 600px) {
  .ai-resources dl { grid-template-columns: 1fr; gap: var(--cv-space-2); }
  .ai-resources footer { align-items: stretch; flex-direction: column; }
}
</style>
