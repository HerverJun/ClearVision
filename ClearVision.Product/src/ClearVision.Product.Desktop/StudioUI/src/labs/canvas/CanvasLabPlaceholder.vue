<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, useTemplateRef } from 'vue';
import {
  getCanvasLabDiagnostics,
  mountCanvasLab,
  type CanvasLabController,
  type CanvasLabDiagnostics
} from '@/labs/canvas/canvasLabOwner';
import type { CanvasFixtureId } from '@/labs/canvas/operatorFlowFixtures';
import './canvasLab.css';

interface CanvasFixtureOption {
  readonly id: CanvasFixtureId;
  readonly action: string;
  readonly label: string;
  readonly detail: string;
}

const canvasId = 'studio-ui-canonical-flow-canvas';
const canvasStatusId = `${canvasId}-accessibility-status`;
const canvasHelpId = `${canvasId}-accessibility-help`;
const canvasDescriptionIds = `${canvasStatusId} ${canvasHelpId}`;
const canvasKeyboardShortcuts = [
  'Control+A',
  'Meta+A',
  'Control+C',
  'Meta+C',
  'Control+V',
  'Meta+V',
  'Control+Z',
  'Meta+Z',
  'Control+Shift+Z',
  'Meta+Shift+Z',
  'Control+Y',
  'Meta+Y',
  'Delete',
  'Backspace',
  'Escape'
].join(' ');
const canvasKeyboardHelp = '画布获得焦点后，可使用 Ctrl/Command+A 全选，Ctrl/Command+C 和 Ctrl/Command+V 复制粘贴，Ctrl/Command+Z 撤销，Ctrl/Command+Shift+Z 或 Ctrl/Command+Y 重做，Delete 或 Backspace 删除，Escape 取消选择。';
const fixtureOptions: readonly CanvasFixtureOption[] = Object.freeze([
  {
    id: 'canonical',
    action: 'load-canonical',
    label: '标准契约',
    detail: '5 个节点 · 3 条连线'
  },
  {
    id: 'interaction',
    action: 'load-interaction',
    label: '交互矩阵',
    detail: '5 个节点 · 端口开放'
  },
  {
    id: 'benchmark-100',
    action: 'load-benchmark-100',
    label: '基准 100',
    detail: '100 个节点 · 150 条连线'
  },
  {
    id: 'stress-300',
    action: 'load-stress-300',
    label: '压力 300',
    detail: '300 个节点 · 450 条连线'
  }
]);

const diagnostics = shallowRef<CanvasLabDiagnostics>(getCanvasLabDiagnostics());
const mountError = ref<string | null>(null);
const diagnosticsExpanded = ref(true);
const canvasElement = useTemplateRef<HTMLCanvasElement>('canvasElement');
let controller: CanvasLabController | undefined;

const runtime = computed(() => diagnostics.value.runtime);
const isMounted = computed(() =>
  diagnostics.value.status === 'mounted' && diagnostics.value.ownerCount === 1);
const labState = computed(() => {
  if (mountError.value) {
    return 'unavailable';
  }
  return isMounted.value ? 'ready' : diagnostics.value.status;
});
const validationPassCount = computed(() =>
  diagnostics.value.validation.filter(item => item.passed).length);
const validationSummary = computed(() => diagnostics.value.fixtureId === 'canonical'
  ? `${validationPassCount.value}/${diagnostics.value.validation.length}`
  : '不适用');
const canvasStatusText = computed(() => {
  if (mountError.value) {
    return `${diagnostics.value.fixtureName ?? '无活动夹具'}；画布运行时不可用；${mountError.value}；节点 ${runtime.value?.nodeCount ?? 0}；连线 ${runtime.value?.connectionCount ?? 0}。`;
  }
  const status = diagnostics.value.status === 'mounted'
    ? '已挂载'
    : diagnostics.value.status === 'error'
      ? '挂载失败'
      : diagnostics.value.status;
  return `${diagnostics.value.fixtureName ?? '无活动夹具'}；${status}；节点 ${runtime.value?.nodeCount ?? 0}；连线 ${runtime.value?.connectionCount ?? 0}；连接校验 ${validationSummary.value}。`;
});
const identityFingerprint = computed(() =>
  diagnostics.value.identity.afterFingerprint ?? diagnostics.value.identity.beforeFingerprint ?? '未运行');

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : '画布实验室发生未知错误。';
}

function supportsCanvasRuntime(): boolean {
  if (typeof window === 'undefined' || typeof document === 'undefined') {
    return false;
  }
  if (typeof window.requestAnimationFrame !== 'function') {
    return false;
  }
  return !navigator.userAgent.toLowerCase().includes('jsdom');
}

function refreshDiagnostics(): void {
  diagnostics.value = controller?.getDiagnostics() ?? getCanvasLabDiagnostics();
}

function runWithOwner(operation: (owner: CanvasLabController) => void): void {
  const owner = controller;
  if (!owner) {
    return;
  }

  mountError.value = null;
  try {
    operation(owner);
    refreshDiagnostics();
  } catch (error) {
    mountError.value = errorMessage(error);
    refreshDiagnostics();
  }
}

function loadFixture(fixtureId: CanvasFixtureId): void {
  runWithOwner(owner => owner.loadFixture(fixtureId));
}

function runIdentityRoundTrip(): void {
  runWithOwner(owner => {
    owner.runIdentityRoundTrip();
  });
}

function resizeCanvas(): void {
  runWithOwner(owner => owner.resize());
}

function mergeCanvasDescriptions(): void {
  const canvas = canvasElement.value;
  if (!canvas) {
    throw new Error('Canvas Lab accessibility target is unavailable.');
  }
  const descriptionIds = new Set([
    ...(canvas.getAttribute('aria-describedby')?.split(/\s+/).filter(Boolean) ?? []),
    canvasStatusId,
    canvasHelpId
  ]);
  canvas.setAttribute('aria-describedby', [...descriptionIds].join(' '));
}

function disposeOwnedController(reportError: boolean): void {
  const owner = controller;
  controller = undefined;
  if (!owner) {
    return;
  }

  try {
    owner.dispose();
  } catch (error) {
    if (reportError) {
      mountError.value = errorMessage(error);
    }
  } finally {
    diagnostics.value = getCanvasLabDiagnostics();
  }
}

onMounted(() => {
  if (!supportsCanvasRuntime()) {
    mountError.value = '当前测试环境不支持 Canvas 2D 运行时。';
    return;
  }

  try {
    controller = mountCanvasLab({
      canvasId,
      initialFixtureId: 'canonical',
      onDiagnostics(nextDiagnostics) {
        diagnostics.value = nextDiagnostics;
      }
    });
    mergeCanvasDescriptions();
    controller.resize();
    refreshDiagnostics();
  } catch (error) {
    mountError.value = errorMessage(error);
    disposeOwnedController(false);
  }
});

onBeforeUnmount(() => {
  disposeOwnedController(true);
});
</script>

<template>
  <main
    class="canvas-lab"
    data-studio-page="canvas-placeholder"
    :data-canvas-lab="labState"
  >
    <header class="canvas-lab__header">
      <div class="canvas-lab__title-block">
        <span class="canvas-lab__eyebrow">F01 · 标准宿主边界</span>
        <h1>FlowCanvas 验证实验室</h1>
        <p>
          StudioUI 外壳只持有一个窄适配器，现有 FlowCanvas 仍是唯一的绘制与指针内核。
        </p>
      </div>

      <div class="canvas-lab__header-status">
        <span
          class="canvas-lab__owner-badge"
          :class="{ 'canvas-lab__owner-badge--active': isMounted }"
          data-canvas-owner-count
        >
          控制实例 {{ diagnostics.ownerCount }}/1
        </span>
        <RouterLink to="/diagnostics">
          诊断
        </RouterLink>
      </div>
    </header>

    <section
      class="canvas-lab__toolbar"
      aria-label="画布夹具与验证操作"
    >
      <div class="canvas-lab__fixture-group">
        <button
          v-for="fixture in fixtureOptions"
          :key="fixture.id"
          type="button"
          class="canvas-lab__fixture-button"
          :class="{ 'canvas-lab__fixture-button--active': diagnostics.fixtureId === fixture.id }"
          :data-canvas-action="fixture.action"
          :aria-pressed="diagnostics.fixtureId === fixture.id"
          :disabled="!isMounted"
          @click="loadFixture(fixture.id)"
        >
          <strong>{{ fixture.label }}</strong>
          <small>{{ fixture.detail }}</small>
        </button>
      </div>

      <div class="canvas-lab__action-group">
        <button
          type="button"
          class="canvas-lab__action-button canvas-lab__action-button--primary"
          data-canvas-action="identity-roundtrip"
          :disabled="!isMounted"
          @click="runIdentityRoundTrip"
        >
          身份往返验证
        </button>
        <button
          type="button"
          class="canvas-lab__action-button"
          data-canvas-action="resize"
          :disabled="!isMounted"
          @click="resizeCanvas"
        >
          重新测量
        </button>
        <button
          type="button"
          class="canvas-lab__action-button"
          data-canvas-action="toggle-diagnostics"
          :aria-expanded="diagnosticsExpanded"
          @click="diagnosticsExpanded = !diagnosticsExpanded"
        >
          {{ diagnosticsExpanded ? '隐藏诊断' : '显示诊断' }}
        </button>
      </div>
    </section>

    <p
      v-if="mountError"
      class="canvas-lab__error"
      role="alert"
      data-canvas-error
    >
      {{ mountError }}
    </p>

    <section
      class="canvas-lab__workspace"
      :class="{ 'canvas-lab__workspace--diagnostics-hidden': !diagnosticsExpanded }"
    >
      <div class="canvas-lab__canvas-panel">
        <div
          class="canvas-lab__stage"
          data-canvas-stage
        >
          <canvas
            :id="canvasId"
            ref="canvasElement"
            data-canvas-surface
            tabindex="0"
            aria-label="流程编辑画布"
            :aria-describedby="canvasDescriptionIds"
            :aria-keyshortcuts="canvasKeyboardShortcuts"
          >
            FlowCanvas 需要 Canvas 2D 支持。
          </canvas>
          <span
            :id="canvasStatusId"
            class="canvas-lab__visually-hidden"
            role="status"
            aria-live="polite"
            aria-atomic="true"
            data-canvas-live-status
          >{{ canvasStatusText }}</span>
          <span
            :id="canvasHelpId"
            class="canvas-lab__visually-hidden"
          >{{ canvasKeyboardHelp }}</span>
          <div
            v-if="!isMounted"
            class="canvas-lab__canvas-state"
            aria-live="polite"
          >
            <strong>{{ mountError ? '画布运行时不可用' : '正在挂载标准画布…' }}</strong>
            <span>{{ mountError ?? '正在等待唯一画布实例。' }}</span>
          </div>
        </div>

        <footer class="canvas-lab__interaction-guide">
          <span><kbd>拖动</kbd>节点</span>
          <span><kbd>拖动空白处</kbd>平移</span>
          <span><kbd>滚轮</kbd>缩放</span>
          <span><kbd>Shift + 拖动</kbd>选择</span>
          <span><kbd>端口 → 端口</kbd>连接</span>
        </footer>
      </div>

      <aside
        v-show="diagnosticsExpanded"
        class="canvas-lab__diagnostics"
        data-canvas-diagnostics
        aria-label="画布运行诊断"
      >
        <section class="canvas-lab__diagnostic-section">
          <div class="canvas-lab__section-heading">
            <div>
              <span class="canvas-lab__eyebrow">当前挂载状态</span>
              <h2>{{ diagnostics.fixtureName ?? '无活动夹具' }}</h2>
            </div>
            <span
              class="canvas-lab__state-pill"
              :data-state="diagnostics.status"
              data-canvas-status
            >
              {{ diagnostics.status }}
            </span>
          </div>

          <dl class="canvas-lab__metric-grid">
            <div><dt>代数</dt><dd>{{ diagnostics.generation }}</dd></div>
            <div><dt>挂载 / 释放</dt><dd>{{ diagnostics.totalMounts }} / {{ diagnostics.totalDisposals }}</dd></div>
            <div>
              <dt>节点</dt><dd data-canvas-node-count>
                {{ runtime?.nodeCount ?? 0 }}
              </dd>
            </div>
            <div>
              <dt>连线</dt><dd data-canvas-connection-count>
                {{ runtime?.connectionCount ?? 0 }}
              </dd>
            </div>
            <div><dt>缩放</dt><dd>{{ runtime?.scale.toFixed(3) ?? '—' }}</dd></div>
            <div><dt>偏移</dt><dd>{{ runtime ? `${runtime.offsetX.toFixed(1)}, ${runtime.offsetY.toFixed(1)}` : '—' }}</dd></div>
          </dl>
        </section>

        <section class="canvas-lab__diagnostic-section">
          <div class="canvas-lab__section-heading canvas-lab__section-heading--compact">
            <h2>身份一致性</h2>
            <span
              class="canvas-lab__state-pill"
              :data-state="diagnostics.identity.state"
              data-canvas-identity-state
            >
              {{ diagnostics.identity.state }}
            </span>
          </div>
          <output
            class="canvas-lab__fingerprint"
            data-canvas-identity-fingerprint
          >{{ identityFingerprint }}</output>
        </section>

        <section class="canvas-lab__diagnostic-section">
          <div class="canvas-lab__section-heading canvas-lab__section-heading--compact">
            <h2>连线拒绝矩阵</h2>
            <span data-canvas-validation-summary>
              {{ validationSummary }}
            </span>
          </div>
          <ul class="canvas-lab__validation-list">
            <li
              v-for="item in diagnostics.validation"
              :key="item.id"
              :data-canvas-validation-case="item.id"
              :data-canvas-validation-result="item.passed ? 'pass' : 'fail'"
            >
              <span class="canvas-lab__validation-dot" />
              <span><strong>{{ item.id }}</strong><small>{{ item.actual ?? 'accepted' }}</small></span>
            </li>
          </ul>
        </section>

        <section class="canvas-lab__diagnostic-section">
          <h2>像素与生命周期状态</h2>
          <dl class="canvas-lab__resource-list">
            <div>
              <dt>DPR</dt><dd data-canvas-dpr>
                {{ runtime?.dpr ?? '—' }}
              </dd>
            </div>
            <div><dt>逻辑尺寸</dt><dd>{{ runtime ? `${runtime.logicalWidth} × ${runtime.logicalHeight}` : '—' }}</dd></div>
            <div><dt>后备尺寸</dt><dd>{{ runtime ? `${runtime.backingWidth} × ${runtime.backingHeight}` : '—' }}</dd></div>
            <div><dt>观察者</dt><dd>{{ runtime ? `${Number(runtime.resources.resizeObserverActive)} / ${Number(runtime.resources.themeObserverActive)}` : '—' }}</dd></div>
            <div><dt>订阅</dt><dd>{{ runtime ? `${runtime.resources.structureListenerCount} / ${runtime.resources.viewListenerCount} / ${runtime.resources.selectionListenerCount}` : '—' }}</dd></div>
            <div><dt>交互清理</dt><dd>{{ runtime?.resources.interactionCleanupCount ?? '—' }}</dd></div>
            <div><dt>待处理帧</dt><dd>{{ runtime ? `${Number(runtime.resources.drawFramePending)} / ${Number(runtime.resources.resizeFramePending)} / ${Number(runtime.resources.interactionFramePending)}` : '—' }}</dd></div>
          </dl>
        </section>
      </aside>
    </section>
  </main>
</template>
