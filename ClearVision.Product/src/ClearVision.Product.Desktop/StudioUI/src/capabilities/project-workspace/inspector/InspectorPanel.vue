<script setup lang="ts">
import { computed, onBeforeUnmount, shallowRef, watch } from 'vue';
import { CvStatusBadge, type CvStatusTone } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import WorkspacePaneHeader from '../WorkspacePaneHeader.vue';
import type { InspectorOwner, InspectorParameterProjection } from './inspectorOwner';
import ParameterEditor from './ParameterEditor.vue';
import { CameraBindingEditor, type CameraBindingEditorOwner } from '../camera';

const props = defineProps<{
  owner: InspectorOwner;
  cameraOwner: CameraBindingEditorOwner | null;
}>();

const projection = props.owner.projection;
const nameDraft = shallowRef('');
const lastMessage = shallowRef<string | null>(null);
const durationFormatter = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 });
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
const executionPresentation = computed<Readonly<{ label: string; tone: CvStatusTone }>>(() => {
  switch (projection.node?.executionStatus) {
    case 'Executing': return { label: '执行中', tone: 'info' };
    case 'Success': return { label: '执行成功', tone: 'ok' };
    case 'Failed': return { label: '执行失败', tone: 'error' };
    case 'Skipped': return { label: '已跳过', tone: 'warning' };
    default: return { label: '尚未执行', tone: 'idle' };
  }
});
const visibleParameters = computed(() => projection.node?.parameters.filter(parameter => parameter.visible) ?? []);
const resourceParameters = computed(() => visibleParameters.value.filter(parameter => parameter.extensionSlot !== null));
const advancedParameters = computed(() => visibleParameters.value.filter(parameter =>
  parameter.extensionSlot === null && (
    parameter.deprecated || parameter.ignored ||
    parameter.disabledByConstraint || parameter.editorKind === 'unsupported' || parameter.editorKind === 'extension'
  )));
const commonParameters = computed(() => visibleParameters.value.filter(parameter =>
  !resourceParameters.value.includes(parameter) && !advancedParameters.value.includes(parameter)));
const nodeDescription = computed(() => {
  const description = projection.node?.description?.trim() ?? '';
  if (/[一-鿿]/u.test(description)) return description;
  const type = projection.node?.type.toLocaleLowerCase() ?? '';
  if (type.includes('roimanager')) return '配置感兴趣区域的位置与尺寸，并输出后续算子可用的区域。';
  if (type.includes('imageacquisition')) return '选择图像采集资源，并管理节点预览使用的调试输入。';
  return '配置当前算子的输入、输出与运行参数。';
});

function parameterKey(parameter: InspectorParameterProjection): string {
  return parameter.id ?? parameter.name;
}

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
    <WorkspacePaneHeader
      title="属性检查器"
      :detail="modeLabel"
    >
      <span
        class="inspector-panel__gate"
        :data-gate="projection.mutationGate"
      >{{ mutationGateLabel }}</span>
    </WorkspacePaneHeader>

    <div
      v-if="projection.mode === 'empty'"
      class="inspector-panel__empty"
    >
      <CvIcon
        name="empty"
        size="lg"
      />
      <strong>未选择对象</strong>
      <p>选择节点或连线后，可在此查看并编辑属性。</p>
    </div>

    <div
      v-else-if="projection.mode === 'multi-node'"
      class="inspector-panel__body"
    >
      <section class="inspector-panel__section">
        <div class="inspector-panel__section-heading">
          <h3>已选择 {{ projection.nodes.length }} 个节点</h3>
          <small>选择单个节点可编辑参数</small>
        </div>
        <button
          v-for="node in projection.nodes"
          :key="node.id"
          type="button"
          class="inspector-panel__summary-node"
          :title="`查看 ${node.name}`"
          @click="owner.selectNode(node.id)"
        >
          <span><strong :title="node.name">{{ node.name }}</strong><small translate="no">{{ node.type }}</small></span>
          <em :data-enabled="node.enabled">{{ node.enabled ? '启用' : '禁用' }}</em>
        </button>
      </section>
    </div>

    <div
      v-else-if="projection.mode === 'connection' && projection.connection"
      class="inspector-panel__body"
    >
      <section class="inspector-panel__section inspector-panel__connection-identity">
        <div class="inspector-panel__section-heading">
          <h3>连线属性</h3>
          <small>选择端点可查看节点</small>
        </div>
        <code
          translate="no"
          :title="projection.connection.id"
        >{{ projection.connection.id }}</code>
      </section>
      <section class="inspector-panel__connection">
        <button
          type="button"
          @click="owner.selectNode(projection.connection!.source.nodeId)"
        >
          <small>来源</small>
          <strong :title="projection.connection.source.nodeName">{{ projection.connection.source.nodeName }}</strong>
          <span>{{ projection.connection.source.portName }} · {{ projection.connection.source.dataType }}</span>
        </button>
        <span aria-hidden="true">→</span>
        <button
          type="button"
          @click="owner.selectNode(projection.connection!.target.nodeId)"
        >
          <small>目标</small>
          <strong :title="projection.connection.target.nodeName">{{ projection.connection.target.nodeName }}</strong>
          <span>{{ projection.connection.target.portName }} · {{ projection.connection.target.dataType }}</span>
        </button>
      </section>
      <button
        type="button"
        class="inspector-panel__danger"
        :disabled="editingDisabled"
        @click="disconnect"
      >
        断开当前连线
      </button>
    </div>

    <div
      v-else-if="projection.mode === 'node' && projection.node"
      class="inspector-panel__body"
    >
      <section class="inspector-panel__section inspector-panel__identity">
        <div class="inspector-panel__node-title">
          <h3 :title="projection.node.name">
            {{ projection.node.name }}
          </h3>
          <CvStatusBadge
            :tone="executionPresentation.tone"
            :label="executionPresentation.label"
          />
        </div>
        <div class="inspector-panel__technical-identity">
          <small>算子类型</small>
          <code
            translate="no"
            :title="projection.node.type"
          >{{ projection.node.type }}</code>
        </div>
        <p>{{ nodeDescription }}</p>
        <p
          v-if="projection.node.executionTimeMs !== null"
          class="inspector-panel__metric"
        >
          最近耗时 {{ durationFormatter.format(projection.node.executionTimeMs) }} ms
        </p>
        <p
          v-if="projection.node.errorMessage"
          class="inspector-panel__node-error"
          role="alert"
        >
          <strong>节点执行失败</strong>
          <span>{{ projection.node.errorMessage }}</span>
        </p>
      </section>

      <section class="inspector-panel__section">
        <div class="inspector-panel__section-heading">
          <h3>基础属性</h3>
        </div>
        <label class="inspector-panel__field">
          <span>节点名称</span>
          <input
            v-model="nameDraft"
            type="text"
            name="node-name"
            autocomplete="off"
            :title="nameDraft"
            :disabled="editingDisabled"
            @blur="commitName"
            @keydown.enter.stop.prevent="commitName"
            @keydown.escape.stop.prevent="nameDraft = projection.node?.name ?? ''"
          >
        </label>
        <label class="inspector-panel__check">
          <input
            type="checkbox"
            name="node-enabled"
            :checked="projection.node.enabled"
            :disabled="editingDisabled"
            @change="toggleEnabled"
          >
          <span>启用节点</span>
        </label>
      </section>

      <section class="inspector-panel__section">
        <div class="inspector-panel__section-heading">
          <h3>端口</h3>
          <small>{{ projection.node.inputPorts.length }} 入 · {{ projection.node.outputPorts.length }} 出</small>
        </div>
        <div class="inspector-panel__ports">
          <div
            v-for="port in projection.node.inputPorts"
            :key="`in-${port.id}`"
            :data-connected="port.connected"
          >
            <span>输入</span><strong :title="port.displayName">{{ port.displayName }}</strong><small translate="no">{{ port.dataType }}</small>
          </div>
          <div
            v-for="port in projection.node.outputPorts"
            :key="`out-${port.id}`"
            :data-available="port.available"
          >
            <span>输出</span><strong :title="port.displayName">{{ port.displayName }}</strong><small translate="no">{{ port.dataType }}</small>
          </div>
        </div>
      </section>

      <section
        v-if="resourceParameters.length"
        class="inspector-panel__section inspector-panel__parameters inspector-panel__resources"
      >
        <div class="inspector-panel__section-heading">
          <h3>资源绑定</h3>
          <small>专用工作台 · {{ resourceParameters.length }} 项</small>
        </div>
        <div class="inspector-panel__parameter-list">
          <template
            v-for="parameter in resourceParameters"
            :key="parameterKey(parameter)"
          >
            <CameraBindingEditor
              v-if="parameter.extensionSlot === 'camera-binding' && cameraOwner"
              :owner="cameraOwner"
              :parameter-name="parameter.name"
              :disabled="editingDisabled || projection.node.metadataPhase !== 'ready'"
            />
            <p
              v-else-if="parameter.extensionSlot === 'camera-binding'"
              class="inspector-panel__resource-unavailable"
            >
              已保留候选资源标识；创建工程后才能访问相机绑定服务。
            </p>
            <ParameterEditor
              v-else
              :parameter="parameter"
              :disabled="editingDisabled || projection.node.metadataPhase !== 'ready'"
              @commit="commitParameter(parameter.name, $event)"
              @draft-active="owner.setDraftActive(`parameter:${parameter.name}`, $event)"
            />
          </template>
        </div>
      </section>

      <section class="inspector-panel__section inspector-panel__parameters">
        <div class="inspector-panel__section-heading">
          <h3>常用参数</h3>
          <small>{{ commonParameters.length }} 项</small>
        </div>
        <p
          v-if="projection.node.metadataPhase !== 'ready'"
          class="inspector-panel__metadata-message"
          :data-phase="projection.node.metadataPhase"
          role="status"
        >
          {{ projection.node.metadataMessage ?? '正在读取参数定义…' }}
        </p>
        <div class="inspector-panel__parameter-list">
          <template
            v-for="parameter in commonParameters"
            :key="parameterKey(parameter)"
          >
            <ParameterEditor
              :parameter="parameter"
              :disabled="editingDisabled || projection.node.metadataPhase !== 'ready'"
              @commit="commitParameter(parameter.name, $event)"
              @draft-active="owner.setDraftActive(`parameter:${parameter.name}`, $event)"
            />
          </template>
        </div>
      </section>

      <details
        v-if="advancedParameters.length"
        class="inspector-panel__advanced"
      >
        <summary>
          <span>高级参数</span>
          <small>{{ advancedParameters.length }} 项 · 按需展开</small>
        </summary>
        <div class="inspector-panel__parameter-list">
          <ParameterEditor
            v-for="parameter in advancedParameters"
            :key="parameterKey(parameter)"
            :parameter="parameter"
            :disabled="editingDisabled || projection.node.metadataPhase !== 'ready'"
            @commit="commitParameter(parameter.name, $event)"
            @draft-active="owner.setDraftActive(`parameter:${parameter.name}`, $event)"
          />
        </div>
      </details>
    </div>

    <section
      v-if="projection.validationErrors.length"
      class="inspector-panel__validation"
      aria-live="polite"
    >
      <strong>参数校验未通过</strong>
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
      <span>本地流程 r{{ projection.flowRevision }}</span>
      <span v-if="projection.activeDraftCount > 0">未提交字段 {{ projection.activeDraftCount }}</span>
      <span
        v-if="lastMessage"
        role="status"
        aria-live="polite"
        :title="lastMessage"
      >{{ lastMessage }}</span>
    </footer>
  </aside>
</template>

<style scoped>
.inspector-panel {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto auto;
  overflow: hidden;
  border-right: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
  container-name: inspector;
  container-type: inline-size;
}
.inspector-panel :deep(.workspace-pane-header) { min-height: 42px; padding-inline: 14px; background: var(--cv-surface-raised); }
.inspector-panel :deep(.workspace-pane-header__identity strong) { font-size: var(--cv-font-size-sm); }
.inspector-panel__gate { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); white-space: nowrap; }
.inspector-panel__gate[data-gate="readonly"],
.inspector-panel__gate[data-gate="running"] { color: var(--cv-color-status-warning-strong); }
.inspector-panel__body,
.inspector-panel__empty {
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}
.inspector-panel__empty {
  padding: var(--cv-space-6) 14px;
  display: grid;
  align-content: start;
  justify-items: start;
  gap: var(--cv-space-2);
  color: var(--cv-text-muted);
}
.inspector-panel__empty :deep(svg) { color: var(--cv-color-industrial-blue); opacity: .62; }
.inspector-panel__empty > strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.inspector-panel__empty p,
.inspector-panel__section p { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: 1.5; overflow-wrap: anywhere; }
.inspector-panel__section { display: grid; gap: var(--cv-space-2); padding: 12px 14px; }
.inspector-panel__section + .inspector-panel__section { border-top: 1px solid var(--cv-border-subtle); }
.inspector-panel__section h3 { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); line-height: 1.35; }
.inspector-panel__section-heading { min-width: 0; display: flex; align-items: baseline; justify-content: space-between; gap: var(--cv-space-2); }
.inspector-panel__section-heading small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.inspector-panel__node-title { min-width: 0; display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-2); }
.inspector-panel__node-title h3 { min-width: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-md); overflow-wrap: anywhere; }
.inspector-panel__identity code,
.inspector-panel__connection-identity code {
  overflow: hidden;
  color: var(--cv-text-muted);
  font-family: var(--cv-font-mono);
  font-size: 9px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.inspector-panel__technical-identity { min-width: 0; display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: var(--cv-space-2); }
.inspector-panel__technical-identity small { color: var(--cv-text-muted); font-size: 9px; }
.inspector-panel__identity > p[title] {
  display: -webkit-box;
  overflow: hidden;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}
.inspector-panel__metric { color: var(--cv-text-secondary) !important; font-variant-numeric: tabular-nums; }
.inspector-panel__node-error {
  padding: var(--cv-space-2);
  display: grid;
  gap: var(--cv-space-1);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-color-status-ng-soft);
  color: var(--cv-color-status-ng-strong) !important;
}
.inspector-panel__node-error strong { font-size: var(--cv-font-size-2xs); }
.inspector-panel__field { display: grid; gap: var(--cv-space-1); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.inspector-panel__field input {
  width: 100%;
  min-width: 0;
  height: var(--cv-density-control-height);
  padding: 0 var(--cv-space-2);
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-page);
  color: var(--cv-text-primary);
  font: inherit;
  font-size: var(--cv-font-size-xs);
}
.inspector-panel__field input:hover:not(:disabled) { border-color: var(--cv-control-border-hover); }
.inspector-panel__field input:focus-visible { border-color: var(--cv-color-industrial-blue); outline: 2px solid color-mix(in srgb, var(--cv-color-industrial-blue) 20%, transparent); outline-offset: 1px; }
.inspector-panel__check { min-height: 24px; display: flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); cursor: pointer; }
.inspector-panel__ports { display: grid; }
.inspector-panel__ports div {
  min-width: 0;
  display: grid;
  grid-template-columns: 34px minmax(0, 1fr) auto;
  align-items: baseline;
  gap: var(--cv-space-2);
  padding: 6px 0;
  border-bottom: 1px solid var(--cv-border-subtle);
}
.inspector-panel__ports div:last-child { border-bottom: 0; }
.inspector-panel__ports div[data-available="false"] { opacity: 0.55; }
.inspector-panel__ports span,
.inspector-panel__ports small { color: var(--cv-text-muted); font-size: 9px; }
.inspector-panel__ports small { padding: 1px 5px; border-radius: var(--cv-radius-pill); background: var(--cv-color-status-info-soft); color: var(--cv-color-status-info-strong); }
.inspector-panel__ports strong { overflow: hidden; font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.inspector-panel__parameters { gap: var(--cv-space-1); }
.inspector-panel__parameter-list { display: grid; }
.inspector-panel__resources { background: color-mix(in srgb, var(--cv-color-status-info-soft) 42%, var(--cv-surface-raised)); }
.inspector-panel__advanced { border-top: 1px solid var(--cv-border-subtle); }
.inspector-panel__advanced summary { min-height: 36px; padding: 0 14px; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); color: var(--cv-text-primary); cursor: pointer; font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); list-style-position: inside; }
.inspector-panel__advanced summary:hover { background: var(--cv-interactive-hover); }
.inspector-panel__advanced summary:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }
.inspector-panel__advanced summary small { color: var(--cv-text-muted); font-size: 9px; font-weight: var(--cv-font-weight-normal); }
.inspector-panel__advanced > .inspector-panel__parameter-list { padding: 0 14px var(--cv-space-2); }
.inspector-panel__metadata-message { padding: var(--cv-space-2); border-radius: var(--cv-radius-sm); background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong) !important; }
.inspector-panel__summary-node {
  width: 100%;
  padding: var(--cv-space-2) 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-2);
  text-align: left;
  border: 0;
  border-bottom: 1px solid var(--cv-border-subtle);
  background: transparent;
  color: var(--cv-text-primary);
  cursor: pointer;
}
.inspector-panel__summary-node:hover { background: var(--cv-interactive-hover); }
.inspector-panel__summary-node:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }
.inspector-panel__summary-node span { min-width: 0; }
.inspector-panel__summary-node strong,
.inspector-panel__summary-node small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.inspector-panel__summary-node small,
.inspector-panel__summary-node em { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-style: normal; }
.inspector-panel__summary-node em[data-enabled="false"] { color: var(--cv-color-status-warning-strong); }
.inspector-panel__connection { display: grid; grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr); align-items: center; gap: var(--cv-space-2); padding: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); }
.inspector-panel__connection button { min-width: 0; padding: var(--cv-space-2); display: grid; gap: var(--cv-space-1); text-align: left; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); cursor: pointer; }
.inspector-panel__connection button:hover { border-color: var(--cv-border-default); background: var(--cv-interactive-hover); }
.inspector-panel__connection button:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.inspector-panel__connection small,
.inspector-panel__connection span { overflow: hidden; color: var(--cv-text-muted); font-size: 9px; text-overflow: ellipsis; white-space: nowrap; }
.inspector-panel__connection strong { overflow: hidden; font-size: var(--cv-font-size-xs); text-overflow: ellipsis; white-space: nowrap; }
.inspector-panel__danger { margin: 0 var(--cv-space-3) var(--cv-space-3); height: var(--cv-density-control-height); border: 1px solid var(--cv-color-status-ng-border); border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-color-status-ng-strong); cursor: pointer; }
.inspector-panel__danger:hover:not(:disabled) { background: var(--cv-color-status-ng-soft); }
.inspector-panel__danger:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.inspector-panel__danger:disabled { opacity: 0.45; cursor: not-allowed; }
.inspector-panel__validation { margin: 0 var(--cv-space-3) var(--cv-space-2); padding: 9px 10px; border: 1px solid var(--cv-color-status-ng-border); border-radius: var(--cv-radius-sm); background: var(--cv-color-status-ng-soft); color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-2xs); overflow-wrap: anywhere; }
.inspector-panel__validation ul { margin: var(--cv-space-1) 0 0; padding-left: 16px; }
.inspector-panel__footer { min-width: 0; min-height: 22px; padding: 0 var(--cv-space-3); display: flex; align-items: center; gap: var(--cv-space-2); overflow: hidden; border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); color: var(--cv-text-muted); font-size: 9px; white-space: nowrap; }
.inspector-panel__footer span { overflow: hidden; text-overflow: ellipsis; }
.inspector-panel__footer span:last-child { color: var(--cv-text-secondary); }

@container inspector (max-width: 280px) {
  .inspector-panel__node-title { display: grid; }
  .inspector-panel__node-title :deep([data-design-primitive="status-badge"]) { justify-self: start; }
  .inspector-panel__connection { grid-template-columns: minmax(0, 1fr); }
  .inspector-panel__connection > span { transform: rotate(90deg); justify-self: center; }
}
</style>
