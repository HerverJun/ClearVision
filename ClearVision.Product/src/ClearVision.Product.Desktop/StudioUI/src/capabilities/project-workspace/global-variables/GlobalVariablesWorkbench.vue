<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { CvButton, CvModal } from '@/design-system';
import type { FlowCanvasOwner } from '../flow';
import type { WorkspaceGlobalVariableValueType, WorkspaceJsonValue } from '../workspaceContracts';
import type { WorkspaceGlobalVariablesOwner } from './workspaceGlobalVariablesOwner';

const props = defineProps<{
  open: boolean;
  owner: WorkspaceGlobalVariablesOwner;
  flowOwner: FlowCanvasOwner;
  readonly: boolean;
}>();
const emit = defineEmits<{ close: [] }>();

const tab = ref<'definitions' | 'bindings' | 'runtime'>('definitions');
const editingId = ref<string | null>(null);
const definition = reactive({
  name: '', displayName: '', description: '', valueType: 'String' as WorkspaceGlobalVariableValueType,
  initialValue: '', min: '', max: '', manualWriteAllowed: false, includeInResultMetadata: false
});
const sourceDraft = reactive({ variableId: '', outputKey: '', resultPath: '', expression: '' });
const targetDraft = reactive({ variableId: '', parameterKey: '', expression: '' });

function field(source: Readonly<Record<string, unknown>>, camel: string): unknown {
  if (Object.prototype.hasOwnProperty.call(source, camel)) return source[camel];
  return source[`${camel.slice(0, 1).toUpperCase()}${camel.slice(1)}`];
}
function text(value: unknown): string { return typeof value === 'string' ? value : ''; }

const outputCandidates = computed(() => props.flowOwner.projection.draft.operators.flatMap(operator => {
  const operatorId = text(field(operator, 'id'));
  const operatorName = text(field(operator, 'name')) || operatorId;
  const ports = field(operator, 'outputPorts');
  return Array.isArray(ports) ? ports.map(raw => {
    const port = raw as Readonly<Record<string, unknown>>;
    const portId = text(field(port, 'id'));
    const portName = text(field(port, 'name')) || text(field(port, 'displayName')) || portId;
    return { key: `${operatorId}:${portId}`, operatorId, operatorName, portId, portName };
  }) : [];
}));
const parameterCandidates = computed(() => props.flowOwner.projection.draft.operators.flatMap(operator => {
  const operatorId = text(field(operator, 'id'));
  const operatorName = text(field(operator, 'name')) || operatorId;
  const parameters = field(operator, 'parameters');
  return Array.isArray(parameters) ? parameters.map(raw => {
    const parameter = raw as Readonly<Record<string, unknown>>;
    const parameterId = text(field(parameter, 'id'));
    const parameterName = text(field(parameter, 'name')) || parameterId;
    return { key: `${operatorId}:${parameterId}`, operatorId, operatorName, parameterId, parameterName };
  }).filter(item => item.parameterId) : [];
}));

function resetDefinition(): void {
  editingId.value = null;
  Object.assign(definition, { name: '', displayName: '', description: '', valueType: 'String', initialValue: '', min: '', max: '', manualWriteAllowed: false, includeInResultMetadata: false });
}

function editVariable(id: string): void {
  const value = props.owner.projection.draft.variables.find(item => item.id === id);
  if (!value) return;
  editingId.value = id;
  Object.assign(definition, {
    name: value.name, displayName: value.displayName, description: value.description ?? '',
    valueType: value.valueType.value, initialValue: String(value.initialValue ?? ''),
    min: String(value.min ?? ''), max: String(value.max ?? ''),
    manualWriteAllowed: value.manualWriteAllowed, includeInResultMetadata: value.includeInResultMetadata
  });
}

function typedInitialValue(): WorkspaceJsonValue {
  if (definition.valueType === 'Boolean') return definition.initialValue.trim().toLowerCase() === 'true';
  if (definition.valueType === 'Int64' || definition.valueType === 'Double') {
    const value = Number(definition.initialValue);
    return Number.isFinite(value) ? value : definition.initialValue;
  }
  return definition.initialValue;
}

function saveDefinition(): void {
  const input = {
    name: definition.name,
    displayName: definition.displayName,
    description: definition.description,
    valueType: definition.valueType,
    initialValue: typedInitialValue(),
    min: definition.min === '' ? null : Number(definition.min),
    max: definition.max === '' ? null : Number(definition.max),
    manualWriteAllowed: definition.manualWriteAllowed,
    includeInResultMetadata: definition.includeInResultMetadata
  };
  props.owner.upsertDefinition(editingId.value ? { ...input, id: editingId.value } : input);
  resetDefinition();
}

function addSourceBinding(): void {
  const candidate = outputCandidates.value.find(item => item.key === sourceDraft.outputKey);
  if (!candidate || !sourceDraft.variableId) return;
  props.owner.upsertSourceBinding({
    variableId: sourceDraft.variableId,
    operatorId: candidate.operatorId,
    outputPortId: candidate.portId,
    operatorName: candidate.operatorName,
    outputPortName: candidate.portName,
    resultPath: sourceDraft.resultPath,
    expression: sourceDraft.expression
  });
}

function addTargetBinding(): void {
  const candidate = parameterCandidates.value.find(item => item.key === targetDraft.parameterKey);
  if (!candidate || !targetDraft.variableId) return;
  props.owner.upsertTargetBinding({
    variableId: targetDraft.variableId,
    operatorId: candidate.operatorId,
    parameterId: candidate.parameterId,
    operatorName: candidate.operatorName,
    parameterName: candidate.parameterName,
    expression: targetDraft.expression
  });
}

watch(() => props.open, open => { if (open && tab.value === 'runtime') void props.owner.refreshRuntimeValues(); });
</script>

<template>
  <CvModal
    :open="open"
    title="全局变量"
    description="定义、来源与参数绑定随本工程统一保存；运行值仅供查看。"
    size="lg"
    :close-on-backdrop="false"
    @close="emit('close')"
  >
    <div
      class="variables-workbench"
      data-capability="global-variables-workbench"
      :data-dirty="owner.projection.dirty"
    >
      <nav aria-label="全局变量视图">
        <button
          v-for="item in [{ key: 'definitions', label: '定义' }, { key: 'bindings', label: '绑定' }, { key: 'runtime', label: '运行值' }]"
          :key="item.key"
          type="button"
          :data-active="tab === item.key"
          @click="tab = item.key as typeof tab"
        >
          {{ item.label }}
        </button>
      </nav>

      <section
        v-if="tab === 'definitions'"
        class="variables-workbench__split"
      >
        <div class="variables-workbench__list">
          <button
            v-for="item in owner.projection.draft.variables"
            :key="item.id"
            type="button"
            :data-active="editingId === item.id"
            @click="editVariable(item.id)"
          >
            <span><strong>{{ item.displayName }}</strong><small>{{ item.name }} · {{ item.valueType.value }}</small></span>
            <em>{{ item.initialValue }}</em>
          </button>
          <button
            type="button"
            @click="resetDefinition"
          >
            + 新建变量
          </button>
        </div>
        <form
          class="variables-workbench__form"
          @submit.prevent="saveDefinition"
        >
          <label>名称<input
            v-model="definition.name"
            required
            pattern="[A-Za-z_][A-Za-z0-9_]*"
            :disabled="readonly"
          ></label>
          <label>显示名称<input
            v-model="definition.displayName"
            required
            :disabled="readonly"
          ></label>
          <label>类型<select
            v-model="definition.valueType"
            :disabled="readonly"
          ><option>String</option><option>Int64</option><option>Double</option><option>Boolean</option></select></label>
          <label>默认 / 手动初始值<input
            v-model="definition.initialValue"
            required
            :disabled="readonly"
          ></label>
          <div
            v-if="definition.valueType === 'Int64' || definition.valueType === 'Double'"
            class="variables-workbench__range"
          >
            <label>最小值<input
              v-model="definition.min"
              type="number"
              :disabled="readonly"
            ></label><label>最大值<input
              v-model="definition.max"
              type="number"
              :disabled="readonly"
            ></label>
          </div>
          <label>说明<textarea
            v-model="definition.description"
            rows="2"
            :disabled="readonly"
          /></label>
          <label class="variables-workbench__check"><input
            v-model="definition.manualWriteAllowed"
            type="checkbox"
            :disabled="readonly"
          >允许手动初始值</label>
          <label class="variables-workbench__check"><input
            v-model="definition.includeInResultMetadata"
            type="checkbox"
            :disabled="readonly"
          >写入结果元数据</label>
          <div class="variables-workbench__row">
            <CvButton
              size="sm"
              type="submit"
              :disabled="readonly"
            >
              {{ editingId ? '更新定义' : '添加定义' }}
            </CvButton><CvButton
              v-if="editingId"
              size="sm"
              variant="danger"
              :disabled="readonly"
              @click="owner.removeDefinition(editingId); resetDefinition()"
            >
              删除
            </CvButton>
          </div>
        </form>
      </section>

      <section
        v-else-if="tab === 'bindings'"
        class="variables-workbench__bindings"
      >
        <div>
          <h3>输出来源</h3><div class="variables-workbench__binding-form">
            <select
              v-model="sourceDraft.variableId"
              :disabled="readonly"
            >
              <option value="">
                选择变量
              </option><option
                v-for="item in owner.projection.draft.variables"
                :key="item.id"
                :value="item.id"
              >
                {{ item.displayName }}
              </option>
            </select><select
              v-model="sourceDraft.outputKey"
              :disabled="readonly"
            >
              <option value="">
                选择结构化输出
              </option><option
                v-for="item in outputCandidates"
                :key="item.key"
                :value="item.key"
              >
                {{ item.operatorName }} / {{ item.portName }}
              </option>
            </select><input
              v-model="sourceDraft.resultPath"
              placeholder="ResultPath（可选）"
              :disabled="readonly"
            ><input
              v-model="sourceDraft.expression"
              placeholder="表达式（可选）"
              :disabled="readonly"
            ><CvButton
              size="sm"
              :disabled="readonly"
              @click="addSourceBinding"
            >
              添加来源
            </CvButton>
          </div><ul>
            <li
              v-for="item in owner.projection.draft.sourceBindings"
              :key="item.id"
            >
              <span>{{ item.operatorName }}.{{ item.outputPortName }} → {{ owner.projection.draft.variables.find(v => v.id === item.variableId)?.displayName }}</span><button
                type="button"
                :disabled="readonly"
                @click="owner.removeSourceBinding(item.id)"
              >
                移除
              </button>
            </li>
          </ul>
        </div>
        <div>
          <h3>目标参数</h3><div class="variables-workbench__binding-form">
            <select
              v-model="targetDraft.variableId"
              :disabled="readonly"
            >
              <option value="">
                选择变量
              </option><option
                v-for="item in owner.projection.draft.variables"
                :key="item.id"
                :value="item.id"
              >
                {{ item.displayName }}
              </option>
            </select><select
              v-model="targetDraft.parameterKey"
              :disabled="readonly"
            >
              <option value="">
                选择算子参数
              </option><option
                v-for="item in parameterCandidates"
                :key="item.key"
                :value="item.key"
              >
                {{ item.operatorName }} / {{ item.parameterName }}
              </option>
            </select><input
              v-model="targetDraft.expression"
              placeholder="表达式（可选）"
              :disabled="readonly"
            ><CvButton
              size="sm"
              :disabled="readonly"
              @click="addTargetBinding"
            >
              添加绑定
            </CvButton>
          </div><ul>
            <li
              v-for="item in owner.projection.draft.targetBindings"
              :key="item.id"
            >
              <span>{{ owner.projection.draft.variables.find(v => v.id === item.variableId)?.displayName }} → {{ item.operatorName }}.{{ item.parameterName }}</span><button
                type="button"
                :disabled="readonly"
                @click="owner.removeTargetBinding(item.id)"
              >
                移除
              </button>
            </li>
          </ul>
        </div>
      </section>

      <section
        v-else
        class="variables-workbench__runtime"
      >
        <div class="variables-workbench__row">
          <p>运行值来自当前 session，只读且不会成为工程初始值。</p><CvButton
            size="sm"
            :disabled="owner.projection.phase === 'loading-runtime'"
            @click="owner.refreshRuntimeValues"
          >
            刷新运行值
          </CvButton>
        </div>
        <table>
          <thead><tr><th>变量</th><th>值</th><th>版本</th><th>来源</th><th>更新时间</th></tr></thead><tbody>
            <tr
              v-for="item in owner.projection.runtimeValues"
              :key="item.variableId"
            >
              <td>{{ item.displayName }}</td><td><code>{{ item.value }}</code></td><td>{{ item.version }}</td><td>{{ item.updatedBy }}</td><td>{{ item.updatedAtUtc ?? '—' }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <div
        v-if="owner.projection.fieldErrors.length"
        class="variables-workbench__errors"
        role="alert"
      >
        <strong>字段校验未通过</strong><ul>
          <li
            v-for="error in owner.projection.fieldErrors"
            :key="`${error.code}-${error.field}`"
          >
            <code>{{ error.code }}</code> {{ error.message }} <small>{{ error.field }}</small>
          </li>
        </ul>
      </div>
      <p
        class="variables-workbench__status"
        role="status"
      >
        {{ owner.projection.message }}
      </p>
    </div>
    <template #footer>
      <CvButton
        variant="quiet"
        @click="owner.cancel(); emit('close')"
      >
        取消
      </CvButton><CvButton
        variant="primary"
        :disabled="readonly || !owner.projection.dirty"
        @click="owner.apply() && emit('close')"
      >
        应用到工程草稿
      </CvButton>
    </template>
  </CvModal>
</template>

<style scoped>
.variables-workbench { min-height: 420px; display: grid; grid-template-rows: auto minmax(0, 1fr) auto auto; gap: var(--cv-space-3); }
.variables-workbench > nav { display: flex; border-bottom: 1px solid var(--cv-border-subtle); }
.variables-workbench > nav button { min-height: 32px; padding: 0 var(--cv-space-3); border: 0; border-bottom: 2px solid transparent; background: transparent; color: var(--cv-text-secondary); cursor: pointer; }
.variables-workbench > nav button[data-active="true"] { border-bottom-color: var(--cv-color-industrial-blue); color: var(--cv-color-industrial-blue); font-weight: 600; }
.variables-workbench__split { min-height: 0; display: grid; grid-template-columns: 280px minmax(0, 1fr); border: 1px solid var(--cv-border-subtle); }
.variables-workbench__list { overflow: auto; border-right: 1px solid var(--cv-border-subtle); }
.variables-workbench__list > button { width: 100%; min-height: 48px; padding: var(--cv-space-2) var(--cv-space-3); display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); text-align: left; border: 0; border-bottom: 1px solid var(--cv-border-subtle); background: transparent; color: var(--cv-text-primary); cursor: pointer; }
.variables-workbench__list > button[data-active="true"] { background: var(--cv-interactive-selected); }
.variables-workbench__list span strong,.variables-workbench__list span small { display: block; }.variables-workbench__list small,.variables-workbench__list em { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-style: normal; }
.variables-workbench__form { padding: var(--cv-space-3); display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); align-content: start; gap: var(--cv-space-3); }.variables-workbench__form > label { display: grid; gap: 4px; font-size: var(--cv-font-size-2xs); }.variables-workbench__form > label:nth-last-of-type(-n+3),.variables-workbench__form .variables-workbench__row { grid-column: 1/-1; }
.variables-workbench input,.variables-workbench select,.variables-workbench textarea { width: 100%; min-width: 0; min-height: 30px; padding: 4px 8px; border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); font: inherit; }.variables-workbench__range,.variables-workbench__row { display: flex; align-items: center; gap: var(--cv-space-2); }.variables-workbench__range label { flex: 1; display: grid; gap: 4px; font-size: var(--cv-font-size-2xs); }.variables-workbench__check { display: flex!important; align-items: center; }.variables-workbench__check input { width: auto; min-height: auto; }
.variables-workbench__bindings { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: var(--cv-space-4); }.variables-workbench__bindings h3 { margin: 0 0 var(--cv-space-2); font-size: var(--cv-font-size-sm); }.variables-workbench__binding-form { display: grid; gap: var(--cv-space-2); }.variables-workbench__bindings ul { margin: var(--cv-space-3) 0 0; padding: 0; list-style: none; }.variables-workbench__bindings li { min-height: 34px; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); font-size: var(--cv-font-size-xs); }.variables-workbench__bindings li button { border: 0; background: transparent; color: var(--cv-color-status-ng-strong); cursor: pointer; }
.variables-workbench__runtime table { width: 100%; border-collapse: collapse; font-size: var(--cv-font-size-xs); }.variables-workbench__runtime th,.variables-workbench__runtime td { padding: 7px 8px; text-align: left; border-bottom: 1px solid var(--cv-border-subtle); }.variables-workbench__runtime p { margin: 0; flex: 1; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.variables-workbench__errors { padding: var(--cv-space-2); border: 1px solid var(--cv-color-status-ng-border); background: var(--cv-color-status-ng-soft); color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-xs); }.variables-workbench__errors ul { margin: 4px 0 0; padding-left: 18px; }.variables-workbench__errors small { color: var(--cv-text-muted); }.variables-workbench__status { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
@media(max-width:720px){.variables-workbench__split,.variables-workbench__bindings{grid-template-columns:1fr}.variables-workbench__list{max-height:160px;border-right:0;border-bottom:1px solid var(--cv-border-subtle)}}
</style>
