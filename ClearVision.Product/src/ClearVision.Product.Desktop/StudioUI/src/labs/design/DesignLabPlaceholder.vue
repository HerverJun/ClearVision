<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import {
  CvButton,
  CvDataTable,
  CvField,
  CvIconButton,
  CvInlineAlert,
  CvMenu,
  CvMenuItem,
  CvModal,
  CvPagination,
  CvPanel,
  CvSearchField,
  CvSelect,
  CvSplitter,
  CvStatusBadge,
  CvSurface,
  CvToastRegion,
  CvTooltip,
  CvTypography,
  type CvDataTableColumn,
  type CvSelectOption,
  type CvStatusTone,
  type CvToastItem
} from '@/design-system/primitives';
import { CvIcon } from '@/design-system/icons';
import {
  CvPageHeader,
  CvPageState,
  CvToolbar
} from '@/design-system/patterns';
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
const menuOpen = ref(false);
const showGuidance = ref(false);
const fieldValue = ref('CV-Station-01');
const searchValue = ref('edge');
const selectValue = ref('camera');
const samplePage = ref(1);
const inspectorWidth = ref(292);
const toasts = ref<CvToastItem[]>([]);
let toastSequence = 0;
let rootSnapshot: RootAttributeSnapshot | undefined;
let ownsRootProjection = false;

const selectOptions: readonly CvSelectOption[] = [
  { value: 'camera', label: '相机采集' },
  { value: 'preprocess', label: '图像预处理' },
  { value: 'measurement', label: '精密测量' },
  { value: 'decision', label: '最终判定', disabled: true }
];

const statusSamples: readonly { tone: CvStatusTone; label: string; detail: string }[] = [
  { tone: 'ok', label: 'OK', detail: '检测通过' },
  { tone: 'ng', label: 'NG', detail: '检测未通过' },
  { tone: 'error', label: '执行错误', detail: '执行未完成' },
  { tone: 'warning', label: '警告', detail: '需要操作员处理' },
  { tone: 'info', label: '信息', detail: '一般过程信息' },
  { tone: 'idle', label: '空闲', detail: '当前没有执行任务' },
  { tone: 'offline', label: '离线', detail: '连接不可用' },
  { tone: 'unknown', label: '未判定', detail: '需要核对结果' },
  { tone: 'disabled', label: '已禁用', detail: '操作当前不可用' }
];

interface DesignLabTableRow {
  readonly id: string;
  readonly stage: string;
  readonly status: string;
  readonly duration: string;
}

const tableColumns: readonly CvDataTableColumn<DesignLabTableRow>[] = Object.freeze([
  { key: 'stage', label: '阶段', width: '42%' },
  { key: 'status', label: '状态', width: '28%' },
  { key: 'duration', label: '耗时', align: 'end', width: '30%' }
]);
const tableRows: readonly DesignLabTableRow[] = Object.freeze([
  { id: 'acquire', stage: '图像采集', status: '完成', duration: '12.8 ms' },
  { id: 'locate', stage: '边缘定位', status: '完成', duration: '7.4 ms' },
  { id: 'decision', stage: '最终判定', status: '等待', duration: '—' }
]);

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

function handleSampleMenu(value: string): void {
  showToast(value === 'remove' ? 'warning' : 'info');
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
    <CvSurface
      as="section"
      :level="1"
      :elevation="2"
      padding="lg"
      class="design-lab__hero"
    >
      <div class="design-lab__hero-copy">
        <div
          class="design-lab__hero-meta"
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
            查看诊断
          </RouterLink>
        </div>
        <CvTypography
          as="p"
          variant="label"
          tone="secondary"
          weight="semibold"
        >
          Quiet Precision · F02.1
        </CvTypography>
        <CvTypography
          as="h2"
          variant="page-title"
          weight="semibold"
        >
          Design System 2.0 / Product Shell Calibration
        </CvTypography>
        <CvTypography
          as="p"
          variant="body"
          tone="secondary"
        >
          以中性表面、精密排版和严格状态色承载高密度工业视觉工作流，并为后续画布、属性与预览工作台保持稳定扩展边界。
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
              :variant="theme === 'light' ? 'secondary' : 'quiet'"
              :aria-pressed="theme === 'light'"
              data-design-theme="light"
              @click="setTheme('light')"
            >
              Light
            </CvButton>
            <CvButton
              size="sm"
              :variant="theme === 'dark' ? 'secondary' : 'quiet'"
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
              :variant="density === 'comfortable' ? 'secondary' : 'quiet'"
              :aria-pressed="density === 'comfortable'"
              data-design-density="comfortable"
              @click="setDensity('comfortable')"
            >
              Comfortable
            </CvButton>
            <CvButton
              size="sm"
              :variant="density === 'compact' ? 'secondary' : 'quiet'"
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
          <CvTooltip
            text="显示或隐藏键盘状态说明"
            placement="bottom"
          >
            <template #default="{ tooltipId }">
              <CvIconButton
                label="Toggle state guidance"
                :aria-describedby="tooltipId"
                @click="showGuidance = !showGuidance"
              >
                <CvIcon name="info" />
              </CvIconButton>
            </template>
          </CvTooltip>
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
          <CvButton variant="destructive">
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
            hint="仅保留本地界面草稿"
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
            label="正式运行状态来源"
            disabled
          />
        </div>
      </CvPanel>

      <CvPanel
        title="Industrial status language"
        description="Cinnabar brand intent never doubles as an inspection outcome."
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
        description="菜单、提示、弹窗与通知的监听、焦点和计时器会随组件卸载释放。"
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
          <CvMenu
            v-model="menuOpen"
            label="Design Lab 操作"
            trigger-label="打开样本操作菜单"
            @select="handleSampleMenu"
          >
            <template #trigger>
              <CvIcon name="sliders" />
              <span>More actions</span>
            </template>
            <CvMenuItem value="refresh">
              刷新样本
              <template #trailing>
                Ctrl+R
              </template>
            </CvMenuItem>
            <CvMenuItem
              value="readonly"
              :checked="true"
            >
              只读投影
            </CvMenuItem>
            <CvMenuItem
              value="disabled"
              disabled
            >
              不可用操作
            </CvMenuItem>
            <CvMenuItem
              value="remove"
              tone="destructive"
            >
              移除样本
            </CvMenuItem>
          </CvMenu>
        </div>
      </CvPanel>
    </div>

    <CvPanel
      title="Product patterns"
      description="Page header、筛选、提示、表格与分页共享同一套 2.0 表面和密度语义。"
      class="design-lab__wide-panel"
      variant="section"
    >
      <CvPageHeader
        eyebrow="只读样本"
        title="检测流程摘要"
        description="以高信息密度展示稳定投影，不依靠重复外框制造层级。"
        :heading-level="2"
      >
        <template #actions>
          <CvButton size="sm">
            刷新摘要
          </CvButton>
        </template>
      </CvPageHeader>
      <CvToolbar
        interaction="group"
        label="Design Lab 筛选样本"
      >
        <CvSearchField
          v-model="searchValue"
          label="搜索阶段"
          :hide-label="false"
          placeholder="名称或状态"
        />
        <CvSelect
          v-model="selectValue"
          label="算子族"
          :options="selectOptions"
        />
      </CvToolbar>
      <CvInlineAlert
        class="design-lab__pattern-alert"
        tone="info"
        title="技术信息"
      >
        Info、Link 与 Focus 使用技术蓝；品牌丹红不承担普通系统事实。
      </CvInlineAlert>
      <CvDataTable
        :rows="tableRows"
        :columns="tableColumns"
        row-key="id"
        caption="Design System 2.0 表格样本"
      />
      <CvPagination
        v-model:page="samplePage"
        :page-size="3"
        :total-items="12"
        label="Design Lab 分页样本"
      />
    </CvPanel>

    <CvPanel
      title="Product states"
      description="加载、空数据、错误、离线、状态过期、部分可用、冲突、状态未知、权限与 404 使用统一状态轴线。"
      class="design-lab__wide-panel"
      variant="section"
    >
      <div class="design-lab__state-grid">
        <CvPageState
          compact
          kind="loading"
          title="正在读取"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="offline"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="stale"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="partial"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="conflict"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="unknown"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="empty"
          title="暂无数据"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="error"
          title="读取失败"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="unauthorized"
          title="需要预置会话"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="forbidden"
          title="无权访问"
          :heading-level="3"
        />
        <CvPageState
          compact
          kind="not-found"
          title="页面不存在"
          :heading-level="3"
        />
      </div>
    </CvPanel>

    <CvPanel
      title="Splitter lifecycle workbench"
      description="Pointer and keyboard listeners exist only while the mounted separator owns an active resize."
      class="design-lab__splitter-panel"
      :padded="false"
      variant="tool"
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
