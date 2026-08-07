<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { CvButton, CvField, CvModal, CvStatusBadge } from '@/design-system';
import type { TemplateOwner, TemplateWriteInput } from './templateOwner';

const props = defineProps<{
  open: boolean;
  owner: TemplateOwner;
  dirty: boolean;
  readonly?: boolean;
}>();

const emit = defineEmits<{ close: [] }>();
const description = ref('');
const industry = ref('');
const name = ref('');
const tags = ref('');
const selectedId = computed(() => props.owner.projection.selectedTemplateId);
const selected = computed(() => props.owner.projection.selectedTemplate);
const canWrite = computed(() => props.owner.projection.canWrite && !props.readonly);
const formInput = computed<TemplateWriteInput>(() => Object.freeze({
  name: name.value,
  description: description.value,
  industry: industry.value,
  tags: tags.value.split(',')
}));

watch(selected, value => {
  if (!value) return;
  name.value = value.name;
  description.value = value.description;
  industry.value = value.industry;
  tags.value = value.tags.join(', ');
}, { immediate: true });

function selectTemplate(id: string): void {
  void props.owner.select(id);
}

async function applyTemplate(): Promise<void> {
  const confirmed = props.dirty
    ? typeof window !== 'undefined' && window.confirm('当前流程存在未保存修改。应用模板会替换当前流程草稿，是否继续？')
    : true;
  if (!confirmed) return;
  const applied = await props.owner.applySelected({ confirmReplace: props.dirty });
  if (applied) emit('close');
}

function saveAs(): void {
  void props.owner.saveAs(formInput.value);
}

function updateSelected(): void {
  void props.owner.updateSelected(formInput.value);
}
</script>

<template>
  <CvModal
    :open="open"
    title="流程模板"
    description="搜索、预览并将模板装载到唯一流程草稿；模板操作不会自动保存工程。"
    size="lg"
    :close-on-backdrop="false"
    @close="emit('close')"
  >
    <div
      class="template-workbench"
      data-capability="template-workbench"
      :data-template-phase="owner.projection.phase"
      :data-template-write-status="owner.projection.writeStatus"
    >
      <div class="template-workbench__filters">
        <label>
          <span>搜索</span>
          <input
            :value="owner.projection.search"
            type="search"
            placeholder="名称、描述或标签"
            data-testid="template-search"
            @input="owner.setSearch(($event.target as HTMLInputElement).value)"
          >
        </label>
        <label>
          <span>行业</span>
          <select
            :value="owner.projection.industry"
            data-testid="template-industry"
            @change="owner.setIndustry(($event.target as HTMLSelectElement).value)"
          >
            <option value="">全部行业</option>
            <option
              v-for="option in owner.projection.industries"
              :key="option"
              :value="option"
            >{{ option }}</option>
          </select>
        </label>
        <CvButton
          size="sm"
          variant="quiet"
          :disabled="owner.projection.phase === 'loading'"
          @click="owner.refresh"
        >
          刷新
        </CvButton>
      </div>

      <div class="template-workbench__body">
        <section
          class="template-workbench__list"
          aria-label="模板列表"
        >
          <p
            v-if="owner.projection.listMessage && owner.projection.templates.length === 0"
            class="template-workbench__empty"
            role="status"
          >
            {{ owner.projection.listMessage }}
          </p>
          <button
            v-for="template in owner.projection.filteredTemplates"
            :key="template.id"
            type="button"
            class="template-workbench__item"
            :data-active="template.id === selectedId"
            @click="selectTemplate(template.id)"
          >
            <span>
              <strong>{{ template.name }}</strong>
              <small>{{ template.industry || '通用' }} · {{ template.tags.length }} 个标签</small>
            </span>
            <CvStatusBadge
              v-if="template.scenarioKey"
              tone="info"
              label="场景"
            />
          </button>
          <p
            v-if="owner.projection.templates.length > 0 && owner.projection.filteredTemplates.length === 0"
            class="template-workbench__empty"
          >
            没有匹配的模板。
          </p>
        </section>

        <section
          class="template-workbench__detail"
          aria-label="模板详情"
        >
          <template v-if="selected">
            <header>
              <div>
                <h3>{{ selected.name }}</h3>
                <p>{{ selected.description || '暂无描述。' }}</p>
              </div>
              <CvStatusBadge
                :tone="owner.projection.conversion?.flow ? 'ok' : 'idle'"
                :label="owner.projection.conversion?.flow ? `${owner.projection.conversion.operatorCount} 个算子` : '未预览'"
              />
            </header>
            <dl>
              <div><dt>行业</dt><dd>{{ selected.industry || '通用' }}</dd></div>
              <div><dt>版本</dt><dd>{{ selected.templateVersion || '1.0.0' }}</dd></div>
              <div><dt>场景</dt><dd>{{ selected.scenarioKey || '未绑定' }}</dd></div>
            </dl>
            <div
              v-if="owner.projection.diagnostics.length"
              class="template-workbench__diagnostics"
              role="status"
            >
              <strong>模板诊断</strong>
              <ul>
                <li
                  v-for="item in owner.projection.diagnostics"
                  :key="`${item.code}-${item.path}`"
                  :data-severity="item.severity"
                >
                  {{ item.message }}
                </li>
              </ul>
            </div>
            <div class="template-workbench__actions">
              <CvButton
                size="sm"
                variant="primary"
                :disabled="readonly || owner.projection.phase === 'applying'"
                :loading="owner.projection.phase === 'applying'"
                loading-label="正在应用"
                data-testid="template-apply"
                @click="applyTemplate"
              >
                应用到流程草稿
              </CvButton>
            </div>
          </template>
          <p
            v-else
            class="template-workbench__empty"
          >
            选择模板查看详情。
          </p>

          <form
            class="template-workbench__write"
            @submit.prevent="selectedId ? updateSelected() : saveAs()"
          >
            <h3>{{ selectedId ? '更新当前模板' : '另存为模板' }}</h3>
            <CvField
              v-model="name"
              label="模板名称"
              name="template-name"
              required
              :disabled="!canWrite"
            />
            <CvField
              v-model="description"
              label="描述"
              name="template-description"
              :disabled="!canWrite"
            />
            <div class="template-workbench__write-grid">
              <CvField
                v-model="industry"
                label="行业"
                name="template-industry-write"
                :disabled="!canWrite"
              />
              <CvField
                v-model="tags"
                label="标签（逗号分隔）"
                name="template-tags"
                :disabled="!canWrite"
              />
            </div>
            <small v-if="!canWrite">当前账号只能读取模板；创建和更新需要 Engineer/Admin 权限。</small>
            <CvButton
              size="sm"
              :disabled="!canWrite || owner.projection.phase === 'saving'"
              :loading="owner.projection.phase === 'saving'"
              loading-label="正在保存"
              type="submit"
            >
              {{ selectedId ? '更新模板' : '另存为模板' }}
            </CvButton>
          </form>
        </section>
      </div>

      <p
        class="template-workbench__message"
        :data-tone="owner.projection.phase === 'error' || owner.projection.phase === 'unknown-outcome' ? 'error' : 'info'"
        role="status"
        aria-live="polite"
      >
        {{ owner.projection.message }}
      </p>
    </div>
    <template #footer>
      <CvButton
        variant="quiet"
        @click="emit('close')"
      >
        关闭
      </CvButton>
    </template>
  </CvModal>
</template>

<style scoped>
.template-workbench { min-width: 0; display: grid; gap: var(--cv-space-4); }
.template-workbench__filters { display: grid; grid-template-columns: minmax(0, 1fr) 180px auto; align-items: end; gap: var(--cv-space-3); }
.template-workbench__filters label,.template-workbench__write :deep(.cv-field) { display: grid; gap: var(--cv-space-1); }
.template-workbench__filters span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.template-workbench__filters input,.template-workbench__filters select { min-width: 0; height: var(--cv-density-control-height); padding: 0 var(--cv-space-2); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); font: inherit; font-size: var(--cv-font-size-xs); }
.template-workbench__body { min-height: 420px; display: grid; grid-template-columns: minmax(240px, .78fr) minmax(0, 1.22fr); gap: var(--cv-space-4); }
.template-workbench__list { min-height: 0; overflow: auto; border: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.template-workbench__item { width: 100%; min-height: 60px; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); padding: var(--cv-space-3); text-align: left; border: 0; border-bottom: 1px solid var(--cv-border-subtle); background: transparent; color: var(--cv-text-primary); cursor: pointer; }
.template-workbench__item:hover,.template-workbench__item[data-active='true'] { background: var(--cv-interactive-selected); }
.template-workbench__item span { min-width: 0; }
.template-workbench__item strong,.template-workbench__item small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.template-workbench__item small,.template-workbench__empty,.template-workbench__detail p,.template-workbench__write > small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.template-workbench__empty { margin: 0; padding: var(--cv-space-5); line-height: 1.5; }
.template-workbench__detail { min-width: 0; display: grid; align-content: start; gap: var(--cv-space-3); }
.template-workbench__detail header { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-3); }
.template-workbench__detail h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-md); }
.template-workbench__detail p { margin: var(--cv-space-1) 0 0; line-height: 1.5; }
.template-workbench__detail dl { margin: 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); border-block: 1px solid var(--cv-border-subtle); }
.template-workbench__detail dl div { min-width: 0; padding: var(--cv-space-2); }
.template-workbench__detail dt { color: var(--cv-text-muted); font-size: 9px; }
.template-workbench__detail dd { margin: var(--cv-space-1) 0 0; overflow: hidden; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.template-workbench__diagnostics { padding: var(--cv-space-3); border: 1px solid var(--cv-color-status-warning-border); background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong); font-size: var(--cv-font-size-2xs); }
.template-workbench__diagnostics ul { margin: var(--cv-space-1) 0 0; padding-left: 18px; }
.template-workbench__diagnostics li[data-severity='error'] { color: var(--cv-color-status-ng-strong); }
.template-workbench__actions { display: flex; justify-content: flex-end; }
.template-workbench__write { display: grid; gap: var(--cv-space-2); padding-top: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); }
.template-workbench__write h3 { font-size: var(--cv-font-size-sm); }
.template-workbench__write-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-2); }
.template-workbench__message { margin: 0; padding: var(--cv-space-2) var(--cv-space-3); border-left: 3px solid var(--cv-color-status-info-strong); background: var(--cv-color-status-info-soft); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
.template-workbench__message[data-tone='error'] { border-left-color: var(--cv-color-status-ng-strong); background: var(--cv-color-status-ng-soft); color: var(--cv-color-status-ng-strong); }
@media (max-width: 720px) { .template-workbench__filters,.template-workbench__body,.template-workbench__write-grid { grid-template-columns: 1fr; } .template-workbench__body { min-height: 0; } .template-workbench__list { max-height: 220px; } .template-workbench__detail dl { grid-template-columns: 1fr; } }
</style>
