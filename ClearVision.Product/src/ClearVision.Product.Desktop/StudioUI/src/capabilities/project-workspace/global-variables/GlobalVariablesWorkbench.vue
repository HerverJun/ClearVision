<script setup lang="ts">
import { computed, reactive, shallowRef, watch } from 'vue';
import { CvButton, CvModal, CvStatusBadge, type CvStatusTone } from '@/design-system';
import type { FlowCanvasOwner } from '../flow';
import type { WorkspaceGlobalVariableValueType, WorkspaceJsonValue } from '../workspaceContracts';
import {
  globalVariableDataTypeLabel,
  isGlobalVariableDataTypeCompatible,
  normalizeGlobalVariableDataType
} from './globalVariablesContracts';
import type { WorkspaceGlobalVariablesOwner } from './workspaceGlobalVariablesOwner';

const props = defineProps<{
  open: boolean;
  owner: WorkspaceGlobalVariablesOwner;
  flowOwner: FlowCanvasOwner;
  readonly: boolean;
}>();
const emit = defineEmits<{ close: [] }>();

const tab = shallowRef<'definitions' | 'bindings' | 'runtime'>('definitions');
const editingId = shallowRef<string | null>(null);
const copiedField = shallowRef<string | null>(null);
const definition = reactive({
  name: '', displayName: '', description: '', valueType: 'String' as WorkspaceGlobalVariableValueType,
  initialValue: '', min: '', max: '', manualWriteAllowed: false, includeInResultMetadata: false
});
const sourceDraft = reactive({ variableId: '', outputKey: '', resultPath: '', expression: '' });
const targetDraft = reactive({ variableId: '', parameterKey: '', expression: '' });
const runtimeDrafts = reactive<Record<string, string>>({});
const runtimeInputError = shallowRef<string | null>(null);
const selectedDefinition = computed(() => props.owner.projection.draft.variables.find(item => item.id === editingId.value) ?? null);
const statusTone = computed<CvStatusTone>(() => {
  if (props.owner.projection.phase === 'error' || props.owner.projection.fieldErrors.length > 0) return 'error';
  if (props.readonly) return 'warning';
  if (props.owner.projection.dirty) return 'warning';
  return 'ok';
});
const statusLabel = computed(() => {
  if (props.readonly) return '只读';
  if (props.owner.projection.fieldErrors.length > 0) return '需要修正';
  if (props.owner.projection.dirty) return '存在未应用修改';
  return '已同步到工程草稿';
});

function valueTypeLabel(value: string): string {
  return ({ String: '文本', Int64: '整数', Double: '数值', Boolean: '布尔值' } as Readonly<Record<string, string>>)[value] ?? value;
}

function formatTimestamp(value: string | null): string {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit'
  }).format(date);
}

async function copyField(key: string, value: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value);
    copiedField.value = key;
  } catch {
    copiedField.value = null;
  }
}

function field(source: Readonly<Record<string, unknown>>, camel: string): unknown {
  if (Object.prototype.hasOwnProperty.call(source, camel)) return source[camel];
  return source[`${camel.slice(0, 1).toUpperCase()}${camel.slice(1)}`];
}
function text(value: unknown): string { return typeof value === 'string' ? value : ''; }

function dataType(value: unknown): string | number {
  if (typeof value === 'string' || typeof value === 'number') return value;
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    const source = value as Readonly<Record<string, unknown>>;
    const semanticValue = source.value;
    if (typeof semanticValue === 'string' || typeof semanticValue === 'number') return semanticValue;
    const persistenceValue = source.persistenceValue;
    if (typeof persistenceValue === 'string' || typeof persistenceValue === 'number') return persistenceValue;
  }
  return '';
}

const outputCandidates = computed(() => props.flowOwner.projection.draft.operators.flatMap(operator => {
  const operatorId = text(field(operator, 'id'));
  const operatorName = text(field(operator, 'name')) || operatorId;
  const ports = field(operator, 'outputPorts');
  return Array.isArray(ports) ? ports.map(raw => {
    const port = raw as Readonly<Record<string, unknown>>;
    const portId = text(field(port, 'id'));
    const portName = text(field(port, 'name')) || text(field(port, 'displayName')) || portId;
    const portDataType = dataType(field(port, 'dataType'));
    return {
      key: `${operatorId}:${portId}`,
      operatorId,
      operatorName,
      portId,
      portName,
      dataType: portDataType,
      bindableType: normalizeGlobalVariableDataType(portDataType)
    };
  }).filter(item => item.portId) : [];
}));
const parameterCandidates = computed(() => props.flowOwner.projection.draft.operators.flatMap(operator => {
  const operatorId = text(field(operator, 'id'));
  const operatorName = text(field(operator, 'name')) || operatorId;
  const parameters = field(operator, 'parameters');
  return Array.isArray(parameters) ? parameters.map(raw => {
    const parameter = raw as Readonly<Record<string, unknown>>;
    const parameterId = text(field(parameter, 'id'));
    const parameterName = text(field(parameter, 'name')) || parameterId;
    const parameterDataType = dataType(field(parameter, 'dataType'));
    return {
      key: `${operatorId}:${parameterId}`,
      operatorId,
      operatorName,
      parameterId,
      parameterName,
      dataType: parameterDataType,
      bindableType: normalizeGlobalVariableDataType(parameterDataType)
    };
  }).filter(item => item.parameterId) : [];
}));
const selectedSourceVariable = computed(() => props.owner.projection.draft.variables
  .find(item => item.id === sourceDraft.variableId) ?? null);
const selectedTargetVariable = computed(() => props.owner.projection.draft.variables
  .find(item => item.id === targetDraft.variableId) ?? null);
const compatibleOutputCandidates = computed(() => outputCandidates.value.filter(item =>
  selectedSourceVariable.value && isGlobalVariableDataTypeCompatible(
    selectedSourceVariable.value.valueType.value,
    item.bindableType
  )));
const compatibleParameterCandidates = computed(() => parameterCandidates.value.filter(item =>
  selectedTargetVariable.value && isGlobalVariableDataTypeCompatible(
    selectedTargetVariable.value.valueType.value,
    item.bindableType
  )));
const runtimeRows = computed(() => {
  const values = new Map(props.owner.projection.runtimeValues.map(value => [value.variableId, value]));
  return props.owner.projection.draft.variables.map(variable => ({
    variable,
    runtime: values.get(variable.id) ?? null
  }));
});
const runtimeBusy = computed(() => ['loading', 'writing', 'resetting'].includes(props.owner.projection.runtimeOperation));

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

function formatRuntimeValue(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  try { return JSON.stringify(value); } catch { return String(value); }
}

function runtimeInput(row: (typeof runtimeRows.value)[number]): string {
  const existing = runtimeDrafts[row.variable.id];
  if (existing !== undefined) return existing;
  return formatRuntimeValue(row.runtime?.value ?? row.variable.initialValue);
}

function parseRuntimeValue(row: (typeof runtimeRows.value)[number], raw: string): Readonly<{ value: WorkspaceJsonValue } | { error: string }> {
  const value = raw.trim();
  if (row.variable.valueType.value === 'String') return { value: raw };
  if (row.variable.valueType.value === 'Boolean') {
    if (value.toLowerCase() === 'true' || value === '1') return { value: true };
    if (value.toLowerCase() === 'false' || value === '0') return { value: false };
    return { error: '布尔值只能填写 true、false、1 或 0。' };
  }
  const number = Number(value);
  if (!value || !Number.isFinite(number)) return { error: '请输入有限数值。' };
  if (row.variable.valueType.value === 'Int64' && !/^[+-]?\d+$/.test(value)) {
    return { error: '整数变量只能填写整数。' };
  }
  const min = row.variable.min === null ? null : Number(row.variable.min);
  const max = row.variable.max === null ? null : Number(row.variable.max);
  if (min !== null && number < min) return { error: `运行值不能小于 ${min}。` };
  if (max !== null && number > max) return { error: `运行值不能大于 ${max}。` };
  if (row.variable.valueType.value === 'Int64' && !Number.isSafeInteger(number)) return { value };
  return { value: number };
}

async function writeRuntime(row: (typeof runtimeRows.value)[number]): Promise<void> {
  runtimeInputError.value = null;
  const parsed = parseRuntimeValue(row, runtimeInput(row));
  if ('error' in parsed) {
    runtimeInputError.value = parsed.error;
    return;
  }
  await props.owner.writeRuntimeValue(row.variable.id, parsed.value, row.runtime?.version ?? null);
}

async function resetRuntime(row: (typeof runtimeRows.value)[number]): Promise<void> {
  runtimeInputError.value = null;
  if (typeof window !== 'undefined' && !window.confirm(`将“${row.variable.displayName}”的运行值重置为定义初始值？`)) return;
  await props.owner.resetRuntimeValue(row.variable.id, row.runtime?.version ?? null);
}

async function resetAllRuntime(): Promise<void> {
  runtimeInputError.value = null;
  if (typeof window !== 'undefined' && !window.confirm('将全部允许运行值手动写入的变量重置为定义初始值？')) return;
  const expectedVersions = Object.fromEntries(
    runtimeRows.value.filter(row => row.runtime).map(row => [row.variable.id, row.runtime!.version])
  );
  await props.owner.resetAllRuntimeValues(expectedVersions);
}

watch(() => props.open, open => { if (open && tab.value === 'runtime') void props.owner.refreshRuntimeValues(); });
watch(() => sourceDraft.variableId, () => {
  if (!compatibleOutputCandidates.value.some(item => item.key === sourceDraft.outputKey)) sourceDraft.outputKey = '';
});
watch(() => targetDraft.variableId, () => {
  if (!compatibleParameterCandidates.value.some(item => item.key === targetDraft.parameterKey)) targetDraft.parameterKey = '';
});
watch(() => props.owner.projection.runtimeValues, values => {
  values.forEach(value => {
    if (runtimeDrafts[value.variableId] === undefined) runtimeDrafts[value.variableId] = formatRuntimeValue(value.value);
  });
}, { immediate: true });
</script>

<template>
  <CvModal
    :open="open"
    title="全局变量"
    description="定义、来源与参数绑定随本工程统一保存；运行值可单独写入或重置，不会改动工程定义。"
    size="lg"
    :close-on-backdrop="false"
    @close="emit('close')"
  >
    <div
      class="variables-workbench cv-workbench"
      data-capability="global-variables-workbench"
      :data-dirty="owner.projection.dirty"
    >
      <nav aria-label="全局变量视图">
        <button
          v-for="item in [{ key: 'definitions', label: '定义' }, { key: 'bindings', label: '绑定' }, { key: 'runtime', label: '运行值' }]"
          :key="item.key"
          type="button"
          :data-active="tab === item.key"
          :aria-pressed="tab === item.key"
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
            :aria-pressed="editingId === item.id"
            @click="editVariable(item.id)"
          >
            <span><strong>{{ item.displayName }}</strong><small>{{ valueTypeLabel(item.valueType.value) }}</small></span>
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
          <label>显示名称<input
            v-model="definition.displayName"
            name="global-variable-display-name"
            autocomplete="off"
            required
            :disabled="readonly"
          ></label>
          <label>类型<select
            v-model="definition.valueType"
            name="global-variable-value-type"
            :disabled="readonly"
          ><option value="String">文本</option><option value="Int64">整数</option><option value="Double">数值</option><option value="Boolean">布尔值</option></select></label>
          <label>默认 / 手动初始值<input
            v-model="definition.initialValue"
            name="global-variable-initial-value"
            autocomplete="off"
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
              name="global-variable-min"
              :disabled="readonly"
            ></label><label>最大值<input
              v-model="definition.max"
              type="number"
              name="global-variable-max"
              :disabled="readonly"
            ></label>
          </div>
          <label>说明<textarea
            v-model="definition.description"
            name="global-variable-description"
            autocomplete="off"
            rows="2"
            :disabled="readonly"
          /></label>
          <details
            class="variables-workbench__technical cv-technical-detail"
            :open="editingId === null"
          >
            <summary>技术详情</summary>
            <div class="variables-workbench__technical-grid">
              <label>名称<input
                v-model="definition.name"
                name="global-variable-name"
                aria-label="名称"
                autocomplete="off"
                spellcheck="false"
                required
                pattern="[A-Za-z_][A-Za-z0-9_]*"
                :disabled="readonly"
              ><small>工程内部标识，仅允许字母、数字和下划线。</small></label>
              <div v-if="selectedDefinition">
                <span>变量标识</span>
                <div class="cv-copyable-value">
                  <code translate="no">{{ selectedDefinition.id }}</code><CvButton
                    size="sm"
                    variant="quiet"
                    @click="copyField('variable', selectedDefinition.id)"
                  >
                    {{ copiedField === 'variable' ? '已复制' : '复制' }}
                  </CvButton>
                </div>
              </div>
            </div>
          </details>
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
              name="global-variable-source-variable"
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
              name="global-variable-source-output"
              :disabled="readonly"
            >
              <option value="">
                选择结构化输出
              </option><option
                v-for="item in compatibleOutputCandidates"
                :key="item.key"
                :value="item.key"
              >
                {{ item.operatorName }} / {{ item.portName }} · {{ globalVariableDataTypeLabel(item.bindableType) }}
              </option><option
                v-if="sourceDraft.variableId && !compatibleOutputCandidates.length"
                disabled
                value=""
              >
                当前变量没有兼容的输出端口
              </option>
            </select><input
              v-model="sourceDraft.resultPath"
              name="global-variable-result-path"
              autocomplete="off"
              spellcheck="false"
              placeholder="结果路径（可选）…"
              :disabled="readonly"
            ><input
              v-model="sourceDraft.expression"
              name="global-variable-source-expression"
              autocomplete="off"
              spellcheck="false"
              placeholder="转换表达式（可选）…"
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
              name="global-variable-target-variable"
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
              name="global-variable-target-parameter"
              :disabled="readonly"
            >
              <option value="">
                选择算子参数
              </option><option
                v-for="item in compatibleParameterCandidates"
                :key="item.key"
                :value="item.key"
              >
                {{ item.operatorName }} / {{ item.parameterName }} · {{ globalVariableDataTypeLabel(item.bindableType) }}
              </option><option
                v-if="targetDraft.variableId && !compatibleParameterCandidates.length"
                disabled
                value=""
              >
                当前变量没有兼容的参数
              </option>
            </select><input
              v-model="targetDraft.expression"
              name="global-variable-target-expression"
              autocomplete="off"
              spellcheck="false"
              placeholder="转换表达式（可选）…"
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
          <p>运行值来自当前运行会话。手动写入和重置需要变量允许，并且不会标记工程未保存。</p><div class="variables-workbench__row">
            <CvButton
              size="sm"
              variant="danger"
              :disabled="readonly || runtimeBusy || !runtimeRows.length"
              @click="resetAllRuntime"
            >
              全部重置
            </CvButton><CvButton
              size="sm"
              :disabled="runtimeBusy"
              @click="owner.refreshRuntimeValues"
            >
              刷新运行值
            </CvButton>
          </div>
        </div>
        <table>
          <thead><tr><th>变量</th><th>定义初始值</th><th>当前运行值</th><th>版本</th><th>来源</th><th>更新时间</th><th>操作</th></tr></thead><tbody>
            <tr
              v-for="row in runtimeRows"
              :key="row.variable.id"
            >
              <td><strong>{{ row.variable.displayName }}</strong><small>{{ valueTypeLabel(row.variable.valueType.value) }}</small></td>
              <td><code>{{ formatRuntimeValue(row.variable.initialValue) }}</code></td>
              <td>
                <input
                  v-model="runtimeDrafts[row.variable.id]"
                  :disabled="readonly || runtimeBusy || !row.variable.manualWriteAllowed"
                  :aria-label="`${row.variable.displayName}运行值`"
                  autocomplete="off"
                  @keyup.enter="writeRuntime(row)"
                >
              </td>
              <td>{{ row.runtime?.version ?? '—' }}</td>
              <td>{{ row.runtime?.updatedBy || '尚未运行' }}</td>
              <td>{{ formatTimestamp(row.runtime?.updatedAtUtc ?? null) }}</td>
              <td class="variables-workbench__runtime-actions">
                <CvButton
                  size="sm"
                  :disabled="readonly || runtimeBusy || !row.variable.manualWriteAllowed"
                  @click="writeRuntime(row)"
                >
                  写入
                </CvButton><CvButton
                  size="sm"
                  variant="quiet"
                  :disabled="readonly || runtimeBusy || !row.variable.manualWriteAllowed"
                  @click="resetRuntime(row)"
                >
                  重置
                </CvButton>
              </td>
            </tr>
            <tr v-if="!runtimeRows.length">
              <td colspan="7">
                当前工程没有全局变量。
              </td>
            </tr>
          </tbody>
        </table>
        <p
          v-if="runtimeInputError"
          class="variables-workbench__runtime-error"
          role="alert"
        >
          {{ runtimeInputError }}
        </p>
        <p
          v-if="owner.projection.runtimeErrorCode"
          class="variables-workbench__runtime-error"
          role="alert"
        >
          诊断码：{{ owner.projection.runtimeErrorCode }}
        </p>
      </section>

      <div
        v-if="owner.projection.fieldErrors.length"
        class="variables-workbench__errors cv-workbench-error"
        role="alert"
      >
        <strong>字段校验未通过</strong><ul>
          <li
            v-for="error in owner.projection.fieldErrors"
            :key="`${error.code}-${error.field}`"
          >
            <span>{{ error.message }}</span>
            <details class="variables-workbench__error-detail">
              <summary>诊断信息</summary>
              <code translate="no">{{ error.code }} · {{ error.field }}</code>
            </details>
          </li>
        </ul>
      </div>
      <div
        class="variables-workbench__status cv-workbench-status"
        role="status"
        aria-live="polite"
        :data-tone="statusTone"
      >
        <CvStatusBadge
          :tone="statusTone"
          :label="statusLabel"
        />
        <span>{{ owner.projection.message }}</span>
      </div>
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
.variables-workbench { min-height: 420px; grid-template-rows: auto minmax(0, 1fr) auto auto; }
.variables-workbench > nav { display: flex; border-bottom: 1px solid var(--cv-border-subtle); }
.variables-workbench > nav button { min-height: 32px; padding: 0 var(--cv-space-3); border: 0; border-bottom: 2px solid transparent; background: transparent; color: var(--cv-text-secondary); cursor: pointer; }
.variables-workbench > nav button[data-active="true"] { border-bottom-color: var(--cv-color-industrial-blue); color: var(--cv-color-industrial-blue); font-weight: 600; }
.variables-workbench__split { min-height: 0; display: grid; grid-template-columns: 280px minmax(0, 1fr); border: 1px solid var(--cv-border-subtle); }
.variables-workbench__list { overflow: auto; border-right: 1px solid var(--cv-border-subtle); }
.variables-workbench__list > button { width: 100%; min-height: 48px; padding: var(--cv-space-2) var(--cv-space-3); display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); text-align: left; border: 0; border-bottom: 1px solid var(--cv-border-subtle); background: transparent; color: var(--cv-text-primary); cursor: pointer; }
.variables-workbench__list > button[data-active="true"] { background: var(--cv-interactive-selected); }
.variables-workbench__list span strong,.variables-workbench__list span small { display: block; }.variables-workbench__list small,.variables-workbench__list em { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-style: normal; }
.variables-workbench__form { padding: var(--cv-space-3); display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); align-content: start; gap: var(--cv-space-3); }.variables-workbench__form > label { display: grid; gap: 4px; font-size: var(--cv-font-size-2xs); }.variables-workbench__form > label:nth-last-of-type(-n+3),.variables-workbench__form .variables-workbench__row,.variables-workbench__technical { grid-column: 1/-1; }
.variables-workbench__range,.variables-workbench__row { display: flex; align-items: center; gap: var(--cv-space-2); }.variables-workbench__range label { flex: 1; display: grid; gap: 4px; font-size: var(--cv-font-size-2xs); }.variables-workbench__check { display: flex!important; align-items: center; }.variables-workbench__check input { width: auto; min-height: auto; }
.variables-workbench__technical-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-3); padding-top: var(--cv-space-2); }.variables-workbench__technical-grid > label { display: grid; gap: var(--cv-space-1); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }.variables-workbench__technical-grid small,.variables-workbench__technical-grid > div > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.variables-workbench__bindings { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: var(--cv-space-4); }.variables-workbench__bindings h3 { margin: 0 0 var(--cv-space-2); font-size: var(--cv-font-size-sm); }.variables-workbench__binding-form { display: grid; gap: var(--cv-space-2); }.variables-workbench__bindings ul { margin: var(--cv-space-3) 0 0; padding: 0; list-style: none; }.variables-workbench__bindings li { min-height: 34px; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); font-size: var(--cv-font-size-xs); }.variables-workbench__bindings li button { border: 0; background: transparent; color: var(--cv-color-status-ng-strong); cursor: pointer; }
.variables-workbench__runtime table { width: 100%; border-collapse: collapse; font-size: var(--cv-font-size-xs); }.variables-workbench__runtime th,.variables-workbench__runtime td { padding: 7px 8px; text-align: left; border-bottom: 1px solid var(--cv-border-subtle); }.variables-workbench__runtime p { margin: 0; flex: 1; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.variables-workbench__errors ul { margin: 4px 0 0; padding-left: 18px; }.variables-workbench__errors li { margin-top: var(--cv-space-1); }.variables-workbench__error-detail summary { cursor: pointer; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }.variables-workbench__error-detail code { overflow-wrap: anywhere; font-size: var(--cv-font-size-2xs); }.variables-workbench__status > span { min-width: 0; overflow-wrap: anywhere; }
@media(max-width:720px){.variables-workbench__split,.variables-workbench__bindings,.variables-workbench__technical-grid{grid-template-columns:1fr}.variables-workbench__list{max-height:160px;border-right:0;border-bottom:1px solid var(--cv-border-subtle)}}
</style>
