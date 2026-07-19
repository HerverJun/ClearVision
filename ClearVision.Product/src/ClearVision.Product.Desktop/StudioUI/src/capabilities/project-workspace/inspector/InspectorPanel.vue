<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import type { InspectorOwner } from './inspectorOwner';
import ParameterEditor from './ParameterEditor.vue';

const props = defineProps<{
  owner: InspectorOwner;
}>();

const projection = props.owner.projection;
const nameDraft = ref('');
const lastMessage = ref<string | null>(null);
const editingDisabled = computed(() => projection.mutationGate !== 'editable');
const nameDirty = computed(() => projection.node !== null && nameDraft.value !== projection.node.name);
const modeLabel = computed(() => ({
  empty: '未选择',
  'multi-node': '多选',
  connection: '连线',
  node: '节点'
}[projection.mode] ?? '未选择'));
const mutationGateLabel = computed(() => ({
  editable: '可编辑',
  readonly: '只读',
  running: '运行中锁定'
}[projection.mutationGate] ?? projection.mutationGate));

watch(
  () => [projection.node?.id, projection.node?.name, projection.selectionRevision],
  () => {
    nameDraft.value = projection.node?.name ?? '';
    lastMessage.value = null;
  },
  { immediate: true }
);

watch(nameDirty, active => props.owner.setDraftActive('node:name', active), { immediate: true });

function commitName(): void {
  if (!nameDirty.value || editingDisabled.value) return;
  const result = props.owner.patchNodeProperties({ name: nameDraft.value });
  lastMessage.value = result.message;
  if (result.ok && projection.node) nameDraft.value = projection.node.name;
}

function toggleEnabled(event: Event): void {
  const target = event.target as HTMLInputElement;
  const result = props.owner.patchNodeProperties({ isEnabled: target.checked });
  lastMessage.value = result.message;
}

function commitParameter(name: string, value: unknown): void {
  const result = props.owner.patchNodeParameter(name, value);
  lastMessage.value = result.message;
}

function disconnect(): void {
  const result = props.owner.disconnectConnection();
  lastMessage.value = result.message;
}

onBeforeUnmount(() => props.owner.setDraftActive('node:name', false));
</script>

<template>
  <aside
    class="inspector-panel"
    aria-label="工作区属性检查器"
    data-capability="workspace-inspector"
    data-evidence-surface="f03-g3-inspector"
    :data-inspector-mode="projection.mode"
    :data-inspector-phase="projection.phase"
    :data-flow-revision="projection.flowRevision"
    :data-selection-revision="projection.selectionRevision"
    :data-mutation-gate="projection.mutationGate"
    :data-active-drafts="projection.activeDraftCount"
    :data-metadata-phase="projection.node?.metadataPhase ?? 'idle'"
  >
    <header class="inspector-panel__header">
      <div>
        <strong>属性检查器</strong>
        <small>{{ modeLabel }}</small>
      </div>
      <span :data-gate="projection.mutationGate">{{ mutationGateLabel }}</span>
    </header>

    <div
      v-if="projection.mode === 'empty'"
      class="inspector-panel__empty"
    >
      <strong>未选择对象</strong>
      <p>选择节点或连线后，可在此查看并编辑属性。</p>
    </div>

    <div
      v-else-if="projection.mode === 'multi-node'"
      class="inspector-panel__body"
    >
      <section class="inspector-panel__section">
        <h3>已选择 {{ projection.nodes.length }} 个节点</h3>
        <p>多选仅显示摘要；选择单个节点后可编辑参数。</p>
        <button
          v-for="node in projection.nodes"
          :key="node.id"
          type="button"
          class="inspector-panel__summary-node"
          @click="owner.selectNode(node.id)"
        >
          <span><strong>{{ node.name }}</strong><small>{{ node.type }}</small></span>
          <em>{{ node.enabled ? '启用' : '禁用' }}</em>
        </button>
      </section>
    </div>

    <div
      v-else-if="projection.mode === 'connection' && projection.connection"
      class="inspector-panel__body"
    >
      <section class="inspector-panel__section">
        <h3>连接</h3>
        <code>{{ projection.connection.id }}</code>
      </section>
      <section class="inspector-panel__connection">
        <button
          type="button"
          @click="owner.selectNode(projection.connection!.source.nodeId)"
        >
          <small>来源</small>
          <strong>{{ projection.connection.source.nodeName }}</strong>
          <span>{{ projection.connection.source.portName }} · {{ projection.connection.source.dataType }}</span>
        </button>
        <span aria-hidden="true">→</span>
        <button
          type="button"
          @click="owner.selectNode(projection.connection!.target.nodeId)"
        >
          <small>目标</small>
          <strong>{{ projection.connection.target.nodeName }}</strong>
          <span>{{ projection.connection.target.portName }} · {{ projection.connection.target.dataType }}</span>
        </button>
      </section>
      <button
        type="button"
        class="inspector-panel__danger"
        :disabled="editingDisabled"
        @click="disconnect"
      >
        断开连接
      </button>
    </div>

    <div
      v-else-if="projection.mode === 'node' && projection.node"
      class="inspector-panel__body"
    >
      <section class="inspector-panel__section inspector-panel__identity">
        <div>
          <h3>{{ projection.node.name }}</h3>
          <code>{{ projection.node.type }}</code>
        </div>
        <span :data-status="projection.node.executionStatus">{{ projection.node.executionStatus }}</span>
        <p v-if="projection.node.description">
          {{ projection.node.description }}
        </p>
        <p v-if="projection.node.executionTimeMs !== null">
          耗时 {{ projection.node.executionTimeMs }} ms
        </p>
        <p
          v-if="projection.node.errorMessage"
          class="is-error"
        >
          {{ projection.node.errorMessage }}
        </p>
      </section>

      <section class="inspector-panel__section">
        <h3>基础属性</h3>
        <label class="inspector-panel__field">
          <span>节点名称</span>
          <input
            v-model="nameDraft"
            type="text"
            :disabled="editingDisabled"
            @blur="commitName"
            @keydown.enter.stop.prevent="commitName"
            @keydown.escape.stop.prevent="nameDraft = projection.node?.name ?? ''"
          >
        </label>
        <label class="inspector-panel__check">
          <input
            type="checkbox"
            :checked="projection.node.enabled"
            :disabled="editingDisabled"
            @change="toggleEnabled"
          >
          <span>启用节点</span>
        </label>
      </section>

      <section class="inspector-panel__section">
        <h3>端口</h3>
        <div class="inspector-panel__ports">
          <div
            v-for="port in projection.node.inputPorts"
            :key="`in-${port.id}`"
            :data-connected="port.connected"
          >
            <span>输入</span><strong>{{ port.displayName }}</strong><small>{{ port.dataType }}</small>
          </div>
          <div
            v-for="port in projection.node.outputPorts"
            :key="`out-${port.id}`"
            :data-available="port.available"
          >
            <span>输出</span><strong>{{ port.displayName }}</strong><small>{{ port.dataType }}</small>
          </div>
        </div>
      </section>

      <section class="inspector-panel__section inspector-panel__parameters">
        <div class="inspector-panel__section-heading">
          <h3>参数</h3>
          <small>{{ projection.node.parameters.filter(parameter => parameter.visible).length }}</small>
        </div>
        <p
          v-if="projection.node.metadataPhase !== 'ready'"
          class="inspector-panel__metadata-message"
          :data-phase="projection.node.metadataPhase"
        >
          {{ projection.node.metadataMessage ?? '正在读取参数定义。' }}
        </p>
        <ParameterEditor
          v-for="parameter in projection.node.parameters"
          :key="parameter.id ?? parameter.name"
          :parameter="parameter"
          :disabled="editingDisabled || projection.node.metadataPhase !== 'ready'"
          @commit="commitParameter(parameter.name, $event)"
          @draft-active="owner.setDraftActive(`parameter:${parameter.name}`, $event)"
        />
      </section>
    </div>

    <section
      v-if="projection.validationErrors.length"
      class="inspector-panel__validation"
      aria-live="polite"
    >
      <strong>校验失败</strong>
      <ul>
        <li
          v-for="error in projection.validationErrors"
          :key="`${error.code}-${error.message}`"
        >
          {{ error.message }}
        </li>
      </ul>
    </section>

    <footer class="inspector-panel__footer">
      <span>本地草稿 r{{ projection.flowRevision }}</span>
      <span>本地草稿 {{ projection.activeDraftCount }}</span>
      <span v-if="lastMessage">{{ lastMessage }}</span>
    </footer>
  </aside>
</template>

<style scoped>
.inspector-panel { min-width: 0; min-height: 0; display: grid; grid-template-rows: auto minmax(0, 1fr) auto auto; overflow: hidden; border-left: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.inspector-panel__header, .inspector-panel__footer { min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); padding: var(--cv-space-2) var(--cv-space-3); border-color: var(--cv-border-subtle); background: var(--cv-surface-overlay); }
.inspector-panel__header { border-bottom: 1px solid var(--cv-border-subtle); }
.inspector-panel__header div { display: flex; align-items: baseline; gap: var(--cv-space-2); }
.inspector-panel__header strong { font-size: var(--cv-font-size-sm); }
.inspector-panel__header small, .inspector-panel__header span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.inspector-panel__header span[data-gate="readonly"], .inspector-panel__header span[data-gate="running"] { color: var(--cv-color-status-warning-strong); }
.inspector-panel__body, .inspector-panel__empty { min-height: 0; padding: var(--cv-space-3); overflow-y: auto; overflow-x: hidden; scrollbar-gutter: stable; }
.inspector-panel__empty { align-content: start; display: grid; gap: var(--cv-space-2); color: var(--cv-text-secondary); }
.inspector-panel__empty > strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.inspector-panel__empty p, .inspector-panel__section p { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: 1.5; }
.inspector-panel__empty dl { margin: var(--cv-space-3) 0 0; display: grid; gap: var(--cv-space-2); }
.inspector-panel__empty dl div { display: flex; justify-content: space-between; gap: var(--cv-space-2); }
.inspector-panel__empty dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.inspector-panel__empty dd { margin: 0; font-size: var(--cv-font-size-xs); }
.inspector-panel__section { display: grid; gap: var(--cv-space-2); padding-bottom: var(--cv-space-3); }
.inspector-panel__section + .inspector-panel__section { padding-top: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); }
.inspector-panel__section h3 { margin: 0; font-size: var(--cv-font-size-xs); }
.inspector-panel__identity > div, .inspector-panel__section-heading { display: flex; align-items: baseline; justify-content: space-between; gap: var(--cv-space-2); }
.inspector-panel__identity code { color: var(--cv-text-muted); font-size: 9px; }
.inspector-panel__identity > span { justify-self: start; padding: 2px 6px; border-radius: 999px; background: var(--cv-surface-sunken); color: var(--cv-text-secondary); font-size: 9px; }
.inspector-panel__identity .is-error { color: var(--cv-color-status-ng-strong); }
.inspector-panel__field { display: grid; gap: 5px; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.inspector-panel__field input { width: 100%; min-width: 0; height: 30px; padding: 0 var(--cv-space-2); border: 1px solid var(--cv-border-default); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); }
.inspector-panel__check { display: flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.inspector-panel__ports { display: grid; gap: 5px; }
.inspector-panel__ports div { min-width: 0; display: grid; grid-template-columns: 34px minmax(0, 1fr) auto; gap: var(--cv-space-2); padding: 6px; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.inspector-panel__ports div[data-available="false"] { opacity: 0.55; }
.inspector-panel__ports span, .inspector-panel__ports small { color: var(--cv-text-muted); font-size: 9px; }
.inspector-panel__ports strong { overflow: hidden; font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.inspector-panel__parameters { gap: var(--cv-space-2); }
.inspector-panel__section-heading small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.inspector-panel__metadata-message { padding: var(--cv-space-2); border-radius: var(--cv-radius-sm); background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong) !important; }
.inspector-panel__summary-node { width: 100%; padding: var(--cv-space-2); display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); text-align: left; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); cursor: pointer; }
.inspector-panel__summary-node span { min-width: 0; }
.inspector-panel__summary-node strong, .inspector-panel__summary-node small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.inspector-panel__summary-node small, .inspector-panel__summary-node em { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-style: normal; }
.inspector-panel__connection { display: grid; grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr); align-items: center; gap: var(--cv-space-2); padding: var(--cv-space-3); }
.inspector-panel__connection button { min-width: 0; padding: var(--cv-space-2); display: grid; gap: 4px; text-align: left; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); cursor: pointer; }
.inspector-panel__connection small, .inspector-panel__connection span { color: var(--cv-text-muted); font-size: 9px; }
.inspector-panel__connection strong { overflow: hidden; font-size: var(--cv-font-size-xs); text-overflow: ellipsis; white-space: nowrap; }
.inspector-panel__danger { margin: var(--cv-space-3); height: 30px; border: 1px solid var(--cv-color-status-ng-strong); border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-color-status-ng-strong); cursor: pointer; }
.inspector-panel__danger:disabled { opacity: 0.45; cursor: not-allowed; }
.inspector-panel__validation { margin: 0 var(--cv-space-3) var(--cv-space-2); padding: var(--cv-space-2); border: 1px solid var(--cv-color-status-ng-strong); border-radius: var(--cv-radius-sm); background: var(--cv-color-status-ng-soft); color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-2xs); }
.inspector-panel__validation ul { margin: 4px 0 0; padding-left: 16px; }
.inspector-panel__footer { justify-content: flex-start; overflow: hidden; border-top: 1px solid var(--cv-border-subtle); color: var(--cv-text-muted); font-size: 9px; white-space: nowrap; }
.inspector-panel__footer span:last-child { overflow: hidden; text-overflow: ellipsis; }
</style>
