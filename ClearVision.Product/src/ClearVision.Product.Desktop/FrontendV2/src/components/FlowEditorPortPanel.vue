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
        v-if="scalarParameters.length === 0"
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
          v-for="parameter in scalarParameters"
          :key="parameter.name"
          class="studio2-flow-port-panel__field"
        >
          <span>{{ parameter.displayName || parameter.name }}</span>
          <input
            v-model="draftValues[parameter.name]"
            :name="parameter.name"
            :type="inputType(parameter.dataType)"
            @input="draftDirty = true"
          >
        </label>

        <div class="studio2-flow-port-panel__actions">
          <button
            type="submit"
            :disabled="!draftDirty"
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
import type {
  StudioFlowEditorParameterSnapshot,
  StudioFlowEditorSnapshot
} from '@/flowEditor/studioFlowEditorPort';

const props = defineProps<{
  model: Studio2FoundationIslandViewModel;
}>();

const draftValues = reactive<Record<string, string>>({});
const draftDirty = ref(false);
const lastDisposition = ref('');
let requestSequence = Date.now();

const snapshot = computed(() => props.model.workspaceState.flowEditorSnapshot);
const statusText = computed(() => props.model.workspaceState.flowEditorStatus);
const scalarParameters = computed(() => {
  const parameters = snapshot.value?.selectedNode?.parameters ?? [];
  return parameters
    .filter(isScalarParameter)
    .slice(0, 6);
});

watch(snapshot, (nextSnapshot) => {
  resetDraft(nextSnapshot);
}, { immediate: true });

function resetDraft(nextSnapshot: StudioFlowEditorSnapshot | null): void {
  for (const key of Object.keys(draftValues)) {
    Reflect.deleteProperty(draftValues, key);
  }

  for (const parameter of nextSnapshot?.selectedNode?.parameters ?? []) {
    if (isScalarParameter(parameter)) {
      draftValues[parameter.name] = stringifyValue(parameter.value);
    }
  }
  draftDirty.value = false;
}

function discardDraft(): void {
  resetDraft(snapshot.value);
  lastDisposition.value = '';
}

function applyDraft(): void {
  const current = snapshot.value;
  const port = props.model.workspaceRuntime.getFlowEditorPort();
  if (!current?.projectId || !current.selectedNodeId || !port) {
    lastDisposition.value = 'project_mismatch';
    return;
  }

  const parameters: Record<string, unknown> = {};
  for (const parameter of scalarParameters.value) {
    parameters[parameter.name] = parseValue(draftValues[parameter.name] ?? '', parameter.dataType);
  }

  requestSequence += 1;
  const result = port.patchParameters({
    projectId: current.projectId,
    requestSequence,
    expectedFlowRevision: current.flowRevision,
    expectedSelectionRevision: current.selectionRevision,
    nodeId: current.selectedNodeId,
    parameters
  });

  lastDisposition.value = result.disposition;
  if (result.accepted) {
    resetDraft(result.snapshot);
  }
}

function isScalarParameter(parameter: StudioFlowEditorParameterSnapshot): boolean {
  const dataType = parameter.dataType.toLowerCase();
  return !dataType ||
    dataType.includes('string') ||
    dataType.includes('int') ||
    dataType.includes('float') ||
    dataType.includes('double') ||
    dataType.includes('number') ||
    dataType.includes('bool');
}

function inputType(dataType: string): string {
  const normalized = dataType.toLowerCase();
  if (normalized.includes('bool')) {
    return 'text';
  }
  if (
    normalized.includes('int') ||
    normalized.includes('float') ||
    normalized.includes('double') ||
    normalized.includes('number')
  ) {
    return 'number';
  }
  return 'text';
}

function parseValue(rawValue: string, dataType: string): unknown {
  const normalized = dataType.toLowerCase();
  if (normalized.includes('bool')) {
    return rawValue.toLowerCase() === 'true';
  }
  if (
    normalized.includes('int') ||
    normalized.includes('float') ||
    normalized.includes('double') ||
    normalized.includes('number')
  ) {
    const numberValue = Number(rawValue);
    return Number.isFinite(numberValue) ? numberValue : rawValue;
  }
  return rawValue;
}

function stringifyValue(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (
    typeof value === 'string' ||
    typeof value === 'number' ||
    typeof value === 'boolean' ||
    typeof value === 'bigint'
  ) {
    return String(value);
  }

  const serialized = JSON.stringify(value);
  return typeof serialized === 'string' ? serialized : '';
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
