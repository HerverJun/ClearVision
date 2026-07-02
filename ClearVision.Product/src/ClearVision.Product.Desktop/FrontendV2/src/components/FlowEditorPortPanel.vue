<template>
  <section class="studio2-flow-port-panel">
    <header class="studio2-flow-port-panel__header">
      <h2>Flow 编辑端口验证</h2>
      <span>{{ statusText }}</span>
    </header>

    <div
      v-if="!snapshot?.projectId"
      class="studio2-flow-port-panel__empty"
    >
      无 active project
    </div>

    <div
      v-else
      class="studio2-flow-port-panel__body"
    >
      <dl class="studio2-flow-port-panel__meta">
        <div>
          <dt>projectId</dt>
          <dd>{{ snapshot.projectId }}</dd>
        </div>
        <div>
          <dt>flowRevision</dt>
          <dd>{{ snapshot.flowRevision }}</dd>
        </div>
        <div>
          <dt>selected</dt>
          <dd>{{ snapshot.selectedNodeId || 'none' }}</dd>
        </div>
        <div>
          <dt>type</dt>
          <dd>{{ snapshot.selectedNode?.type || 'none' }}</dd>
        </div>
      </dl>

      <div
        v-if="displayedParameters.length === 0"
        class="studio2-flow-port-panel__empty"
      >
        无可编辑标量参数
      </div>

      <form
        v-else
        class="studio2-flow-port-panel__form"
        @submit.prevent="applyDraft"
      >
        <label
          v-for="parameter in displayedParameters"
          :key="parameter.name"
          class="studio2-flow-port-panel__field"
        >
          <span>{{ parameter.displayName || parameter.name }}</span>
          <input
            v-if="isFlowEditorBooleanParameter(parameter)"
            :checked="draftValues[parameter.name] === 'true'"
            :name="parameter.name"
            type="checkbox"
            @change="updateBooleanDraft(parameter.name, $event)"
          >
          <input
            v-else
            :name="parameter.name"
            :type="getFlowEditorInputType(parameter)"
            :value="draftValues[parameter.name] ?? ''"
            @input="updateTextDraft(parameter.name, $event)"
          >
          <small
            v-if="validationErrors[parameter.name]"
            class="studio2-flow-port-panel__error"
          >
            {{ validationErrors[parameter.name] }}
          </small>
        </label>

        <p
          v-if="draftStale"
          class="studio2-flow-port-panel__stale"
        >
          流程或选择已变化，请放弃草稿或重新加载
        </p>

        <div class="studio2-flow-port-panel__actions">
          <button
            type="submit"
            :disabled="!draftDirty || hasValidationErrors"
          >
            应用
          </button>
          <button
            type="button"
            :disabled="!draftDirty"
            @click="discardDraft"
          >
            放弃
          </button>
        </div>
      </form>

      <p
        v-if="lastDisposition"
        class="studio2-flow-port-panel__disposition"
      >
        {{ lastDisposition }}
      </p>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import type { Studio2FoundationIslandViewModel } from '@/foundation/studio2FoundationIsland';
import {
  createFlowEditorDraftBaseline,
  getFlowEditorInputType,
  getFlowEditorScalarParameters,
  isFlowEditorBooleanParameter,
  isFlowEditorDraftBaselineStale,
  parseFlowEditorDraftValue,
  stringifyFlowEditorDraftValue,
  type FlowEditorDraftBaseline
} from '@/flowEditor/flowEditorDraft';
import type {
  StudioFlowEditorParameterSnapshot,
  StudioFlowEditorSnapshot
} from '@/flowEditor/studioFlowEditorPort';

const props = defineProps<{
  model: Studio2FoundationIslandViewModel;
}>();

const draftValues = reactive<Record<string, string>>({});
const validationErrors = reactive<Record<string, string>>({});
const draftParameters = ref<StudioFlowEditorParameterSnapshot[]>([]);
const draftBaseline = ref<FlowEditorDraftBaseline | null>(null);
const draftDirty = ref(false);
const draftStale = ref(false);
const lastDisposition = ref('');

const snapshot = computed(() => props.model.workspaceState.flowEditorSnapshot);
const statusText = computed(() => props.model.workspaceState.flowEditorStatus);
const scalarParameters = computed(() =>
  getFlowEditorScalarParameters(snapshot.value?.selectedNode?.parameters ?? []).slice(0, 6)
);
const displayedParameters = computed(() =>
  draftDirty.value ? draftParameters.value : scalarParameters.value
);
const hasValidationErrors = computed(() =>
  Object.values(validationErrors).some((error) => Boolean(error))
);

watch(snapshot, (nextSnapshot) => {
  if (draftDirty.value && draftBaseline.value) {
    if (!nextSnapshot || nextSnapshot.projectId !== draftBaseline.value.projectId) {
      resetDraft(nextSnapshot);
      return;
    }

    if (isFlowEditorDraftBaselineStale(draftBaseline.value, nextSnapshot)) {
      draftStale.value = true;
    }
    return;
  }

  resetDraft(nextSnapshot);
}, { immediate: true });

function resetDraft(nextSnapshot: StudioFlowEditorSnapshot | null): void {
  clearDraftMaps();

  const parameters = getFlowEditorScalarParameters(nextSnapshot?.selectedNode?.parameters ?? []).slice(0, 6);
  draftParameters.value = parameters;
  for (const parameter of parameters) {
    draftValues[parameter.name] = stringifyFlowEditorDraftValue(parameter.value);
  }

  draftBaseline.value = createFlowEditorDraftBaseline(nextSnapshot);
  draftDirty.value = false;
  draftStale.value = false;
}

function discardDraft(): void {
  resetDraft(snapshot.value);
  lastDisposition.value = '';
}

function updateTextDraft(parameterName: string, event: Event): void {
  const target = event.target as HTMLInputElement | null;
  setDraftValue(parameterName, target?.value ?? '');
}

function updateBooleanDraft(parameterName: string, event: Event): void {
  const target = event.target as HTMLInputElement | null;
  setDraftValue(parameterName, target?.checked === true ? 'true' : 'false');
}

function setDraftValue(parameterName: string, value: string): void {
  ensureDraftStarted();
  draftValues[parameterName] = value;

  const parameter = displayedParameters.value.find((item) => item.name === parameterName);
  if (parameter) {
    validateParameter(parameter);
  }
}

function applyDraft(): void {
  const baseline = draftBaseline.value;
  const port = props.model.workspaceRuntime.getFlowEditorPort();
  if (!baseline || !port || snapshot.value?.projectId !== baseline.projectId) {
    lastDisposition.value = 'project_mismatch';
    return;
  }

  const parameters = validateDraft();
  if (!parameters) {
    lastDisposition.value = 'validation_error';
    return;
  }

  const result = port.patchParameters({
    projectId: baseline.projectId,
    requestSequence: port.nextRequestSequence(baseline.projectId),
    expectedFlowRevision: baseline.flowRevision,
    expectedSelectionRevision: baseline.selectionRevision,
    nodeId: baseline.nodeId,
    parameters
  });

  lastDisposition.value = result.disposition;
  if (result.accepted) {
    resetDraft(result.snapshot);
    return;
  }

  if (result.disposition === 'stale_flow_revision' || result.disposition === 'stale_selection') {
    draftStale.value = true;
  }
}

function ensureDraftStarted(): void {
  if (!draftDirty.value) {
    draftParameters.value = scalarParameters.value;
    draftBaseline.value = createFlowEditorDraftBaseline(snapshot.value);
  }

  draftDirty.value = true;
  if (draftBaseline.value && isFlowEditorDraftBaselineStale(draftBaseline.value, snapshot.value)) {
    draftStale.value = true;
  }
}

function validateDraft(): Record<string, unknown> | null {
  clearValidationErrors();

  const parameters: Record<string, unknown> = {};
  let valid = true;
  for (const parameter of displayedParameters.value) {
    const result = parseFlowEditorDraftValue(parameter, draftValues[parameter.name] ?? '');
    if (!result.ok) {
      validationErrors[parameter.name] = result.error ?? '参数无效';
      valid = false;
      continue;
    }

    parameters[parameter.name] = result.value;
  }

  return valid ? parameters : null;
}

function validateParameter(parameter: StudioFlowEditorParameterSnapshot): void {
  const result = parseFlowEditorDraftValue(parameter, draftValues[parameter.name] ?? '');
  if (result.ok) {
    Reflect.deleteProperty(validationErrors, parameter.name);
    return;
  }

  validationErrors[parameter.name] = result.error ?? '参数无效';
}

function clearDraftMaps(): void {
  for (const key of Object.keys(draftValues)) {
    Reflect.deleteProperty(draftValues, key);
  }
  clearValidationErrors();
}

function clearValidationErrors(): void {
  for (const key of Object.keys(validationErrors)) {
    Reflect.deleteProperty(validationErrors, key);
  }
}
</script>

<style scoped>
.studio2-flow-port-panel {
  display: grid;
  gap: 10px;
}

.studio2-flow-port-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.studio2-flow-port-panel__header h2 {
  margin: 0;
  color: #edf3ef;
  font-size: 14px;
  font-weight: 650;
}

.studio2-flow-port-panel__header span {
  color: #8fb9a6;
  font-size: 12px;
}

.studio2-flow-port-panel__body {
  display: grid;
  gap: 10px;
}

.studio2-flow-port-panel__empty {
  color: #9fb0a8;
  font-size: 12px;
}

.studio2-flow-port-panel__meta {
  display: grid;
  gap: 6px;
  margin: 0;
}

.studio2-flow-port-panel__meta div {
  min-width: 0;
  display: grid;
  grid-template-columns: 86px minmax(0, 1fr);
  gap: 8px;
}

.studio2-flow-port-panel__meta dt,
.studio2-flow-port-panel__meta dd {
  min-width: 0;
  margin: 0;
  overflow: hidden;
  color: #9fb0a8;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.studio2-flow-port-panel__meta dd {
  color: #edf3ef;
}

.studio2-flow-port-panel__form {
  display: grid;
  gap: 8px;
}

.studio2-flow-port-panel__field {
  display: grid;
  gap: 4px;
  color: #b8c8c0;
  font-size: 12px;
}

.studio2-flow-port-panel__field input {
  width: 100%;
  height: 28px;
  border: 1px solid #31423a;
  border-radius: 4px;
  background: #101411;
  color: #edf3ef;
  padding: 0 8px;
}

.studio2-flow-port-panel__field input[type='checkbox'] {
  width: 16px;
  height: 16px;
  padding: 0;
}

.studio2-flow-port-panel__error,
.studio2-flow-port-panel__stale {
  margin: 0;
  color: #e36f61;
  font-size: 12px;
}

.studio2-flow-port-panel__stale {
  color: #e0b66b;
}

.studio2-flow-port-panel__actions {
  display: flex;
  gap: 8px;
}

.studio2-flow-port-panel__actions button {
  height: 28px;
  border: 1px solid #31423a;
  border-radius: 4px;
  background: #203129;
  color: #edf3ef;
  padding: 0 10px;
  cursor: pointer;
}

.studio2-flow-port-panel__actions button:disabled {
  opacity: 0.5;
  cursor: default;
}

.studio2-flow-port-panel__disposition {
  margin: 0;
  color: #e0b66b;
  font-size: 12px;
}
</style>
