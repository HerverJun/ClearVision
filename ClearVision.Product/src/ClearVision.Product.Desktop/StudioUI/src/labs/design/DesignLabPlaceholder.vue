<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import {
  CvButton,
  CvField,
  CvIconButton,
  CvModal,
  CvPanel,
  CvSelect,
  CvSplitter,
  CvStatusBadge,
  CvSurface,
  CvToastRegion,
  CvTypography,
  type CvSelectOption,
  type CvStatusTone,
  type CvToastItem
} from '@/design-system/primitives';
import './designLab.css';

type Theme = 'light' | 'dark';
type Density = 'compact' | 'comfortable';

interface RootAttributeSnapshot {
  readonly theme: string | null;
  readonly density: string | null;
  readonly reducedMotion: string | null;
}

const theme = ref<Theme>('light');
const density = ref<Density>('comfortable');
const reducedMotion = ref(false);
const modalOpen = ref(false);
const showGuidance = ref(false);
const fieldValue = ref('CV-Station-01');
const selectValue = ref('camera');
const inspectorWidth = ref(292);
const toasts = ref<CvToastItem[]>([]);
let toastSequence = 0;
let rootSnapshot: RootAttributeSnapshot | undefined;
let ownsRootProjection = false;

const selectOptions: readonly CvSelectOption[] = [
  { value: 'camera', label: 'Camera acquisition' },
  { value: 'preprocess', label: 'Image preprocessing' },
  { value: 'measurement', label: 'Precision measurement' },
  { value: 'decision', label: 'Final decision', disabled: true }
];

const statusSamples: readonly { tone: CvStatusTone; label: string; detail: string }[] = [
  { tone: 'ok', label: 'OK', detail: 'Inspection accepted' },
  { tone: 'ng', label: 'NG', detail: 'Inspection rejected' },
  { tone: 'warning', label: 'Warning', detail: 'Operator attention' },
  { tone: 'info', label: 'Info', detail: 'Neutral process fact' },
  { tone: 'idle', label: 'Idle', detail: 'No active execution' }
];

const activeModeLabel = computed(() =>
  `${theme.value} · ${density.value} · ${reducedMotion.value ? 'reduced motion' : 'standard motion'}`
);

function applyRootProjection(): void {
  if (!ownsRootProjection) return;
  const root = document.documentElement;
  root.dataset.theme = theme.value;
  root.dataset.density = density.value;
  root.dataset.reducedMotion = reducedMotion.value ? 'true' : 'false';
}

function restoreAttribute(name: 'data-theme' | 'data-density' | 'data-reduced-motion', value: string | null): void {
  if (value === null) {
    document.documentElement.removeAttribute(name);
  } else {
    document.documentElement.setAttribute(name, value);
  }
}

function setTheme(value: Theme): void {
  theme.value = value;
}

function setDensity(value: Density): void {
  density.value = value;
}

function showToast(tone: CvStatusTone = 'info'): void {
  toastSequence += 1;
  toasts.value = [
    ...toasts.value,
    {
      id: `design-toast-${toastSequence}`,
      title: tone === 'ok' ? 'Snapshot ready' : 'Design token applied',
      message: tone === 'ok'
        ? 'The evidence state is stable and ready to capture.'
        : `Current mode: ${activeModeLabel.value}.`,
      tone,
      durationMs: 0
    }
  ].slice(-3);
}

function dismissToast(id: string): void {
  toasts.value = toasts.value.filter(toast => toast.id !== id);
}

onMounted(() => {
  const root = document.documentElement;
  rootSnapshot = {
    theme: root.getAttribute('data-theme'),
    density: root.getAttribute('data-density'),
    reducedMotion: root.getAttribute('data-reduced-motion')
  };
  ownsRootProjection = true;
  applyRootProjection();
});

watch([theme, density, reducedMotion], applyRootProjection);

onUnmounted(() => {
  ownsRootProjection = false;
  if (!rootSnapshot) return;
  restoreAttribute('data-theme', rootSnapshot.theme);
  restoreAttribute('data-density', rootSnapshot.density);
  restoreAttribute('data-reduced-motion', rootSnapshot.reducedMotion);
});
</script>

<template>
  <main
    class="design-lab"
    data-studio-page="design-placeholder"
    data-design-lab="ready"
  >
    <header class="design-lab__topbar">
      <div class="design-lab__identity">
        <span
          class="design-lab__mark"
          aria-hidden="true"
        >CV</span>
        <div>
          <CvTypography
            as="p"
            variant="label"
            tone="muted"
            weight="semibold"
          >
            ClearVision Studio
          </CvTypography>
          <CvTypography
            as="h1"
            variant="display"
            weight="semibold"
          >
            Design Foundation Lab
          </CvTypography>
        </div>
      </div>

      <div
        class="design-lab__mode-readout"
        aria-live="polite"
      >
        <CvStatusBadge
          tone="info"
          :label="activeModeLabel"
        />
        <RouterLink
          class="design-lab__diagnostics-link"
          to="/diagnostics"
        >
          Diagnostics
        </RouterLink>
      </div>
    </header>

    <CvSurface
      as="section"
      :level="1"
      :elevation="2"
      padding="lg"
      class="design-lab__hero"
    >
      <div class="design-lab__hero-copy">
        <CvTypography
          as="p"
          variant="label"
          tone="secondary"
          weight="semibold"
        >
          Quiet Precision · F01
        </CvTypography>
        <CvTypography
          as="h2"
          variant="title"
          weight="semibold"
        >
          A restrained blue-white foundation for industrial vision work.
        </CvTypography>
        <CvTypography
          as="p"
          variant="body"
          tone="secondary"
        >
          Brand emphasis, industrial outcomes, dense controls and Canvas semantics remain deliberately separate.
        </CvTypography>
      </div>

      <div
        class="design-lab__preferences"
        aria-label="Design preferences"
      >
        <div class="design-lab__preference-group">
          <CvTypography
            as="span"
            variant="label"
            tone="muted"
            weight="semibold"
          >
            Theme
          </CvTypography>
          <div
            class="design-lab__segmented"
            role="group"
            aria-label="Theme"
          >
            <CvButton
              size="sm"
              :variant="theme === 'light' ? 'primary' : 'quiet'"
              :aria-pressed="theme === 'light'"
              data-design-theme="light"
              @click="setTheme('light')"
            >
              Light
            </CvButton>
            <CvButton
              size="sm"
              :variant="theme === 'dark' ? 'primary' : 'quiet'"
              :aria-pressed="theme === 'dark'"
              data-design-theme="dark"
              @click="setTheme('dark')"
            >
              Dark
            </CvButton>
          </div>
        </div>

        <div class="design-lab__preference-group">
          <CvTypography
            as="span"
            variant="label"
            tone="muted"
            weight="semibold"
          >
            Density
          </CvTypography>
          <div
            class="design-lab__segmented"
            role="group"
            aria-label="Density"
          >
            <CvButton
              size="sm"
              :variant="density === 'comfortable' ? 'primary' : 'quiet'"
              :aria-pressed="density === 'comfortable'"
              data-design-density="comfortable"
              @click="setDensity('comfortable')"
            >
              Comfortable
            </CvButton>
            <CvButton
              size="sm"
              :variant="density === 'compact' ? 'primary' : 'quiet'"
              :aria-pressed="density === 'compact'"
              data-design-density="compact"
              @click="setDensity('compact')"
            >
              Compact
            </CvButton>
          </div>
        </div>

        <label class="design-lab__motion-toggle">
          <input
            v-model="reducedMotion"
            type="checkbox"
            data-design-reduced-motion
          >
          <span
            aria-hidden="true"
            class="design-lab__toggle-track"
          ><span /></span>
          <span>Reduced motion</span>
        </label>
      </div>
    </CvSurface>

    <div class="design-lab__grid">
      <CvPanel
        title="Interaction states"
        description="Default, hover, active, focus-visible, disabled, loading and error semantics."
      >
        <template #actions>
          <CvIconButton
            label="Toggle state guidance"
            @click="showGuidance = !showGuidance"
          >
            <svg viewBox="0 0 20 20"><circle
              cx="10"
              cy="10"
              r="7.5"
              fill="none"
              stroke="currentColor"
              stroke-width="1.5"
            /><path
              d="M10 9v5M10 6.3v.2"
              stroke="currentColor"
              stroke-linecap="round"
              stroke-width="1.7"
            /></svg>
          </CvIconButton>
        </template>

        <div
          class="design-lab__button-matrix"
          data-design-state-matrix
        >
          <CvButton variant="primary">
            Primary action
          </CvButton>
          <CvButton variant="secondary">
            Secondary
          </CvButton>
          <CvButton variant="quiet">
            Quiet action
          </CvButton>
          <CvButton variant="danger">
            Reject
          </CvButton>
          <CvButton loading>
            Processing
          </CvButton>
          <CvButton disabled>
            Disabled
          </CvButton>
        </div>
        <CvTypography
          v-if="showGuidance"
          as="p"
          variant="caption"
          tone="secondary"
          class="design-lab__guidance"
        >
          Use Tab to reveal focus-visible rings; press Space or Enter to activate the focused control.
        </CvTypography>
      </CvPanel>

      <CvPanel
        title="Fields and selection"
        description="Dense labels, explicit errors and predictable disabled states."
      >
        <div class="design-lab__form-grid">
          <CvField
            v-model="fieldValue"
            label="Station identifier"
            hint="Local UI draft only"
            autocomplete="off"
          />
          <CvSelect
            v-model="selectValue"
            label="Operator family"
            :options="selectOptions"
          />
          <CvField
            model-value="192.168.0.999"
            label="Camera address"
            error="Enter a valid IPv4 address."
            autocomplete="off"
          />
          <CvField
            model-value="Runtime-owned"
            label="Execution authority"
            disabled
          />
        </div>
      </CvPanel>

      <CvPanel
        title="Industrial status language"
        description="Brand blue never doubles as an inspection outcome."
      >
        <div
          class="design-lab__status-grid"
          data-design-status-palette
        >
          <div
            class="design-lab__status-card design-lab__status-card--brand"
            data-color-token="brand"
          >
            <span class="design-lab__swatch" />
            <div><strong>Brand</strong><small>Navigation and intent</small></div>
          </div>
          <div
            v-for="status in statusSamples"
            :key="status.tone"
            class="design-lab__status-card"
            :class="`design-lab__status-card--${status.tone}`"
            :data-color-token="status.tone"
          >
            <span class="design-lab__swatch" />
            <div>
              <CvStatusBadge
                :tone="status.tone"
                :label="status.label"
              />
              <small>{{ status.detail }}</small>
            </div>
          </div>
        </div>
      </CvPanel>

      <CvPanel
        title="Layered feedback"
        description="Modal focus ownership and local Toast timers dispose with their mounted owner."
      >
        <div class="design-lab__feedback-actions">
          <CvButton
            data-modal-trigger
            variant="primary"
            @click="modalOpen = true"
          >
            Open review modal
          </CvButton>
          <CvButton
            variant="secondary"
            @click="showToast('info')"
          >
            Show toast
          </CvButton>
          <CvButton
            variant="quiet"
            @click="showToast('ok')"
          >
            Ready toast
          </CvButton>
        </div>
      </CvPanel>
    </div>

    <CvPanel
      title="Splitter lifecycle workbench"
      description="Pointer and keyboard listeners exist only while the mounted separator owns an active resize."
      class="design-lab__splitter-panel"
      :padded="false"
    >
      <div
        class="design-lab__split-layout"
        :style="{ '--design-inspector-width': `${inspectorWidth}px` }"
        data-design-splitter-workbench
      >
        <CvSurface
          :level="2"
          :bordered="false"
          class="design-lab__canvas-sample"
        >
          <div
            class="design-lab__canvas-grid"
            aria-hidden="true"
          />
          <div class="design-lab__node design-lab__node--source">
            <strong>Image acquisition</strong><span>Image</span>
          </div>
          <div
            class="design-lab__connection"
            aria-hidden="true"
          />
          <div class="design-lab__node design-lab__node--target">
            <strong>Threshold</strong><span>Mask</span>
          </div>
        </CvSurface>

        <CvSplitter
          v-model="inspectorWidth"
          :min="220"
          :max="420"
          :step="8"
          label="Resize inspector preview"
        />

        <aside class="design-lab__inspector-sample">
          <CvTypography
            as="h3"
            variant="heading"
            weight="semibold"
          >
            Inspector
          </CvTypography>
          <CvTypography
            as="p"
            variant="caption"
            tone="muted"
            mono
          >
            {{ inspectorWidth }} px
          </CvTypography>
          <dl>
            <div><dt>Threshold</dt><dd>128</dd></div>
            <div><dt>Mode</dt><dd>Binary</dd></div>
            <div><dt>Enabled</dt><dd>Yes</dd></div>
          </dl>
        </aside>
      </div>
    </CvPanel>

    <CvModal
      :open="modalOpen"
      title="Confirm visual foundation"
      description="Keyboard focus remains inside this dialog until it closes."
      @close="modalOpen = false"
    >
      <CvTypography
        as="p"
        variant="body"
        tone="secondary"
      >
        Quiet surfaces, restrained elevation and explicit status colors keep dense industrial workflows legible.
      </CvTypography>
      <template #footer>
        <CvButton
          variant="quiet"
          @click="modalOpen = false"
        >
          Cancel
        </CvButton>
        <CvButton
          data-modal-initial-focus
          variant="primary"
          @click="modalOpen = false; showToast('ok')"
        >
          Accept direction
        </CvButton>
      </template>
    </CvModal>

    <CvToastRegion
      :toasts="toasts"
      @dismiss="dismissToast"
    />
  </main>
</template>
