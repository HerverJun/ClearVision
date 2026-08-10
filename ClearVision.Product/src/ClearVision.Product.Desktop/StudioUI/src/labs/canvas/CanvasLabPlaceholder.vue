<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef } from 'vue';
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
const fixtureOptions: readonly CanvasFixtureOption[] = Object.freeze([
  {
    id: 'canonical',
    action: 'load-canonical',
    label: 'Canonical contract',
    detail: '5 nodes · 3 connections'
  },
  {
    id: 'interaction',
    action: 'load-interaction',
    label: 'Interaction matrix',
    detail: '5 nodes · open ports'
  },
  {
    id: 'benchmark-100',
    action: 'load-benchmark-100',
    label: 'Benchmark 100',
    detail: '100 nodes · 150 connections'
  },
  {
    id: 'stress-300',
    action: 'load-stress-300',
    label: 'Stress 300',
    detail: '300 nodes · 450 connections'
  }
]);

const diagnostics = shallowRef<CanvasLabDiagnostics>(getCanvasLabDiagnostics());
const mountError = ref<string | null>(null);
const diagnosticsExpanded = ref(true);
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
const identityFingerprint = computed(() =>
  diagnostics.value.identity.afterFingerprint ?? diagnostics.value.identity.beforeFingerprint ?? 'not run');

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unknown Canvas Lab failure.';
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
    mountError.value = 'Canvas 2D runtime is unavailable in this test environment.';
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
        <span class="canvas-lab__eyebrow">F01 · canonical host boundary</span>
        <h1>FlowCanvas verification lab</h1>
        <p>
          The StudioUI shell owns one narrow adapter while the existing FlowCanvas remains the
          only drawing and pointer kernel.
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
          Diagnostics
        </RouterLink>
      </div>
    </header>

    <section
      class="canvas-lab__toolbar"
      aria-label="Canvas fixtures and verification actions"
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
          Identity round-trip
        </button>
        <button
          type="button"
          class="canvas-lab__action-button"
          data-canvas-action="resize"
          :disabled="!isMounted"
          @click="resizeCanvas"
        >
          Re-measure
        </button>
        <button
          type="button"
          class="canvas-lab__action-button"
          data-canvas-action="toggle-diagnostics"
          :aria-expanded="diagnosticsExpanded"
          @click="diagnosticsExpanded = !diagnosticsExpanded"
        >
          {{ diagnosticsExpanded ? 'Hide' : 'Show' }} diagnostics
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
            data-canvas-surface
            tabindex="0"
          >
            FlowCanvas requires Canvas 2D support.
          </canvas>
          <div
            v-if="!isMounted"
            class="canvas-lab__canvas-state"
            aria-live="polite"
          >
            <strong>{{ mountError ? 'Canvas runtime unavailable' : 'Mounting canonical Canvas…' }}</strong>
            <span>{{ mountError ?? '正在等待唯一画布实例。' }}</span>
          </div>
        </div>

        <footer class="canvas-lab__interaction-guide">
          <span><kbd>Drag</kbd> node</span>
          <span><kbd>Drag blank</kbd> pan</span>
          <span><kbd>Wheel</kbd> zoom</span>
          <span><kbd>Shift + drag</kbd> select</span>
          <span><kbd>Port → port</kbd> connect</span>
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
              <h2>{{ diagnostics.fixtureName ?? 'No active fixture' }}</h2>
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
            <div><dt>Generation</dt><dd>{{ diagnostics.generation }}</dd></div>
            <div><dt>Mounts / disposals</dt><dd>{{ diagnostics.totalMounts }} / {{ diagnostics.totalDisposals }}</dd></div>
            <div>
              <dt>Nodes</dt><dd data-canvas-node-count>
                {{ runtime?.nodeCount ?? 0 }}
              </dd>
            </div>
            <div>
              <dt>Connections</dt><dd data-canvas-connection-count>
                {{ runtime?.connectionCount ?? 0 }}
              </dd>
            </div>
            <div><dt>Scale</dt><dd>{{ runtime?.scale.toFixed(3) ?? '—' }}</dd></div>
            <div><dt>Offset</dt><dd>{{ runtime ? `${runtime.offsetX.toFixed(1)}, ${runtime.offsetY.toFixed(1)}` : '—' }}</dd></div>
          </dl>
        </section>

        <section class="canvas-lab__diagnostic-section">
          <div class="canvas-lab__section-heading canvas-lab__section-heading--compact">
            <h2>Identity</h2>
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
            <h2>Connection rejection matrix</h2>
            <span>{{ validationPassCount }}/{{ diagnostics.validation.length }}</span>
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
            <div><dt>Logical</dt><dd>{{ runtime ? `${runtime.logicalWidth} × ${runtime.logicalHeight}` : '—' }}</dd></div>
            <div><dt>Backing</dt><dd>{{ runtime ? `${runtime.backingWidth} × ${runtime.backingHeight}` : '—' }}</dd></div>
            <div><dt>Observers</dt><dd>{{ runtime ? `${Number(runtime.resources.resizeObserverActive)} / ${Number(runtime.resources.themeObserverActive)}` : '—' }}</dd></div>
            <div><dt>Subscriptions</dt><dd>{{ runtime ? `${runtime.resources.structureListenerCount} / ${runtime.resources.viewListenerCount} / ${runtime.resources.selectionListenerCount}` : '—' }}</dd></div>
            <div><dt>Interaction cleanup</dt><dd>{{ runtime?.resources.interactionCleanupCount ?? '—' }}</dd></div>
            <div><dt>Pending frames</dt><dd>{{ runtime ? `${Number(runtime.resources.drawFramePending)} / ${Number(runtime.resources.resizeFramePending)} / ${Number(runtime.resources.interactionFramePending)}` : '—' }}</dd></div>
          </dl>
        </section>
      </aside>
    </section>
  </main>
</template>
