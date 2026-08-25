<script lang="ts">
interface DesignLabRootAttributeSnapshot {
  readonly theme: string | null;
  readonly density: string | null;
  readonly reducedMotion: string | null;
}

let designLabRootProjectionOwner: symbol | undefined;
let designLabRootProjectionBaseline: DesignLabRootAttributeSnapshot | undefined;
const designLabRootProjectionQueue = new Map<symbol, () => void>();
let designLabDomIdSequence = 0;

function nextDesignLabDomId(vueId: string): string {
  designLabDomIdSequence += 1;
  return `${vueId}-${designLabDomIdSequence}`;
}
</script>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, useId, watch } from 'vue';
import {
  CvButton,
  CvDataTable,
  CvDescriptionList,
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
  CvToggle,
  CvToastRegion,
  CvTooltip,
  CvTypography,
  CvViewTabs,
  type CvDataTableColumn,
  type CvDescriptionItem,
  type CvSelectOption,
  type CvStatusTone,
  type CvToastItem
} from '@/design-system/primitives';
import { CvIcon } from '@/design-system/icons';
import {
  CvBreadcrumbs,
  CvPageHeader,
  CvPageState,
  CvToolbar,
  type CvBreadcrumbItem
} from '@/design-system/patterns';
import './designLab.css';

type Theme = 'light' | 'dark';
type Density = 'compact' | 'comfortable';

function currentRootTheme(): Theme {
  return typeof document !== 'undefined' && document.documentElement.dataset.theme === 'dark'
    ? 'dark'
    : 'light';
}

function currentRootDensity(): Density {
  return typeof document !== 'undefined' && document.documentElement.dataset.density === 'comfortable'
    ? 'comfortable'
    : 'compact';
}

function currentRootReducedMotion(): boolean {
  return typeof document !== 'undefined' && document.documentElement.dataset.reducedMotion === 'true';
}

const theme = ref<Theme>(currentRootTheme());
const density = ref<Density>(currentRootDensity());
const reducedMotion = ref(currentRootReducedMotion());
const modalOpen = ref(false);
const menuOpen = ref(false);
const showGuidance = ref(false);
const fieldValue = ref('CV-Station-01');
const searchValue = ref('edge');
const selectValue = ref('camera');
const samplePage = ref(1);
const readonlyView = ref('summary');
const inspectorWidth = ref(292);
const toasts = ref<CvToastItem[]>([]);
const rootProjectionToken = Symbol('design-lab-root-projection');
const designLabInstanceId = nextDesignLabDomId(useId());
const compositionsTitleId = `${designLabInstanceId}-compositions-title`;
const readonlySummaryTabId = `${designLabInstanceId}-readonly-tab-summary`;
const readonlySummaryPanelId = `${designLabInstanceId}-readonly-panel-summary`;
const readonlyStagesTabId = `${designLabInstanceId}-readonly-tab-stages`;
const readonlyStagesPanelId = `${designLabInstanceId}-readonly-panel-stages`;
let toastSequence = 0;
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
const readonlyFixture = Object.freeze({
  id: 'option-d-g1-design-system.v1',
  breadcrumbs: Object.freeze([
    { label: '内部实验室', href: '#/labs/design' },
    { label: 'Design System 2.0', current: true }
  ] satisfies readonly CvBreadcrumbItem[]),
  tabs: Object.freeze([
    {
      value: 'summary',
      label: '摘要投影',
      description: '查看冻结的只读摘要字段',
      id: readonlySummaryTabId,
      controls: readonlySummaryPanelId
    },
    {
      value: 'stages',
      label: '阶段明细',
      description: '查看冻结的阶段表格',
      id: readonlyStagesTabId,
      controls: readonlyStagesPanelId
    }
  ]),
  descriptionItems: Object.freeze([
    { key: 'source', label: '数据来源', value: '本地冻结 fixture' },
    { key: 'permission', label: '权限投影', value: '只读' },
    { key: 'revision', label: '持久化版本', value: 'G1 / v1' },
    { key: 'missing', label: '缺失值', value: null }
  ] satisfies readonly CvDescriptionItem[]),
  tableRows: Object.freeze([
    { id: 'acquire', stage: '图像采集', status: '完成', duration: '12.8 ms' },
    { id: 'locate', stage: '边缘定位', status: '完成', duration: '7.4 ms' },
    { id: 'decision', stage: '最终判定', status: '等待', duration: '—' }
  ] satisfies readonly DesignLabTableRow[])
});

const activeModeLabel = computed(() =>
  `${theme.value === 'light' ? '浅色' : '深色'} · ${density.value === 'compact' ? '紧凑' : '舒适'} · ${reducedMotion.value ? '减少动效' : '标准动效'}`
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
      title: tone === 'ok' ? '快照已就绪' : '设计令牌已应用',
      message: tone === 'ok'
        ? '证据状态已稳定，可以采集。'
        : `当前模式：${activeModeLabel.value}。`,
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
  if (designLabRootProjectionQueue.size === 0) {
    designLabRootProjectionBaseline = {
      theme: root.getAttribute('data-theme'),
      density: root.getAttribute('data-density'),
      reducedMotion: root.getAttribute('data-reduced-motion')
    };
  }
  designLabRootProjectionQueue.set(rootProjectionToken, () => {
    designLabRootProjectionOwner = rootProjectionToken;
    ownsRootProjection = true;
    applyRootProjection();
  });
  if (!designLabRootProjectionOwner) designLabRootProjectionQueue.get(rootProjectionToken)?.();
});

watch([theme, density, reducedMotion], applyRootProjection);

onUnmounted(() => {
  designLabRootProjectionQueue.delete(rootProjectionToken);
  if (designLabRootProjectionOwner !== rootProjectionToken) return;

  ownsRootProjection = false;
  designLabRootProjectionOwner = undefined;
  const nextOwner = designLabRootProjectionQueue.values().next().value as (() => void) | undefined;
  if (nextOwner) {
    nextOwner();
    return;
  }

  if (designLabRootProjectionBaseline) {
    restoreAttribute('data-theme', designLabRootProjectionBaseline.theme);
    restoreAttribute('data-density', designLabRootProjectionBaseline.density);
    restoreAttribute('data-reduced-motion', designLabRootProjectionBaseline.reducedMotion);
  }
  designLabRootProjectionBaseline = undefined;
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
          as="h1"
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
        aria-label="设计偏好"
      >
        <div class="design-lab__preference-group">
          <CvTypography
            as="span"
            variant="label"
            tone="muted"
            weight="semibold"
          >
            主题
          </CvTypography>
          <div
            class="design-lab__segmented"
            role="group"
            aria-label="主题"
          >
            <CvButton
              size="sm"
              :variant="theme === 'light' ? 'secondary' : 'quiet'"
              :aria-pressed="theme === 'light'"
              data-design-theme="light"
              @click="setTheme('light')"
            >
              浅色
            </CvButton>
            <CvButton
              size="sm"
              :variant="theme === 'dark' ? 'secondary' : 'quiet'"
              :aria-pressed="theme === 'dark'"
              data-design-theme="dark"
              @click="setTheme('dark')"
            >
              深色
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
            密度
          </CvTypography>
          <div
            class="design-lab__segmented"
            role="group"
            aria-label="密度"
          >
            <CvButton
              size="sm"
              :variant="density === 'comfortable' ? 'secondary' : 'quiet'"
              :aria-pressed="density === 'comfortable'"
              data-design-density="comfortable"
              @click="setDensity('comfortable')"
            >
              舒适
            </CvButton>
            <CvButton
              size="sm"
              :variant="density === 'compact' ? 'secondary' : 'quiet'"
              :aria-pressed="density === 'compact'"
              data-design-density="compact"
              @click="setDensity('compact')"
            >
              紧凑
            </CvButton>
          </div>
        </div>

        <CvToggle
          v-model="reducedMotion"
          label="减少动效"
          :input-attributes="{ 'data-design-reduced-motion': true }"
        />
      </div>
    </CvSurface>

    <section
      class="design-lab__compositions"
      :aria-labelledby="compositionsTitleId"
      data-design-compositions="six"
    >
      <header class="design-lab__section-heading">
        <div>
          <CvTypography
            as="p"
            variant="label"
            tone="muted"
            weight="semibold"
          >
            构图语言
          </CvTypography>
          <CvTypography
            :id="compositionsTitleId"
            as="h2"
            variant="title"
            weight="semibold"
          >
            六类任务，六种主舞台
          </CvTypography>
        </div>
        <CvTypography
          as="p"
          variant="body"
          tone="secondary"
        >
          同一套排版、状态和表面语法，不强迫登录、密集列表、画布、调查、长表单与 AI 工作流使用同一种卡片骨架。
        </CvTypography>
      </header>

      <div class="design-lab__composition-grid">
        <article
          class="design-lab__composition design-lab__composition--auth"
          data-design-composition="auth"
        >
          <div class="design-lab__composition-stage">
            <span class="design-lab__composition-kicker">身份入口</span>
            <strong>ClearVision Studio</strong>
            <p>在真实检测工作内容旁完成身份验证，产品画面与登录任务共同占据首屏。</p>
            <div
              class="design-lab__auth-scene"
              aria-hidden="true"
            >
              <span>A 线 · 焊点定位</span><b>OK</b>
            </div>
          </div>
          <div class="design-lab__composition-rail">
            <span>本地工作站</span><strong>欢迎回来</strong><small>使用预置账户继续</small>
            <div class="design-lab__field-skeleton" /><div class="design-lab__field-skeleton" />
            <span class="design-lab__action-skeleton">登录</span>
          </div>
        </article>

        <article
          class="design-lab__composition design-lab__composition--list"
          data-design-composition="dense-list"
        >
          <header><span class="design-lab__composition-kicker">工程扫描</span><strong>最近工程</strong><small>12 个项目 · 2 个只读</small></header>
          <div class="design-lab__dense-row is-selected">
            <b>电池极耳焊点检测线</b><span>今天 14:32</span><em>已保存</em>
          </div>
          <div class="design-lab__dense-row">
            <b>轴承外观复检工作站</b><span>昨天 18:06</span><em>只读</em>
          </div>
          <div class="design-lab__dense-row">
            <b>超长中文工程名称用于验证扫描与换行不会挤压状态</b><span>08-11 09:20</span><em>待确认</em>
          </div>
        </article>

        <article
          class="design-lab__composition design-lab__composition--workspace"
          data-design-composition="workspace"
        >
          <header><span class="design-lab__composition-kicker">流程画布</span><strong>焊点定位.flow</strong><small>本地更改</small></header>
          <div class="design-lab__workspace-prototype">
            <nav aria-label="算子样本">
              <span>采集</span><span>定位</span><span>测量</span>
            </nav>
            <div
              class="design-lab__workspace-canvas"
              aria-label="画布样本"
            >
              <span>图像采集</span><i /><span>边缘定位</span>
            </div>
            <aside><b>边缘定位</b><small>阈值</small><strong>128 px</strong><small>极性</small><strong>由暗到亮</strong></aside>
          </div>
        </article>

        <article
          class="design-lab__composition design-lab__composition--investigation"
          data-design-composition="investigation"
        >
          <header><span class="design-lab__composition-kicker">结果调查</span><strong>NG-240812-1842</strong><small>执行完成 · 判定 NG</small></header>
          <div class="design-lab__investigation-body">
            <div><span>缺陷类型</span><b>焊点偏移</b><span>测量值</span><b>0.42 mm</b><span>阈值</span><b>0.30 mm</b></div>
            <aside><strong>需要复核</strong><p>诊断证据仍有效，建议对比上一件 OK 样本。</p><span>打开证据</span></aside>
          </div>
        </article>

        <article
          class="design-lab__composition design-lab__composition--form"
          data-design-composition="long-form"
        >
          <nav aria-label="设置组样本">
            <b>运行时</b><span>数据库</span><span>安全</span><span>相机设备</span>
          </nav>
          <div><span class="design-lab__composition-kicker">当前设置对象</span><strong>正式运行与资源限制</strong><p>配置作用于当前工作站，保存前会执行服务端验证。</p><dl><div><dt>并行执行上限</dt><dd>4</dd></div><div><dt>结果保留天数</dt><dd>30 天</dd></div><div><dt>超长中文字段名称仍需在紧凑密度下完整可读</dt><dd>已启用</dd></div></dl></div>
        </article>

        <article
          class="design-lab__composition design-lab__composition--ai"
          data-design-composition="ai-stage"
        >
          <header><span class="design-lab__composition-kicker">当前任务</span><strong>构建表面缺陷检测流程</strong><small>阶段 3 / 5</small></header>
          <ol>
            <li class="is-complete">
              意图确认
            </li><li class="is-complete">
              方案规划
            </li><li class="is-current">
              生成流程
            </li><li>验证</li><li>应用预览</li>
          </ol>
          <div class="design-lab__ai-focus">
            <span>正在生成流程</span><strong>已创建 6 个算子，等待验证资源</strong><small>当前任务上下文持续可见，阶段事件不逐条动画。</small>
          </div>
        </article>
      </div>
    </section>

    <div class="design-lab__grid">
      <CvPanel
        title="交互状态"
        description="统一默认、悬停、激活、键盘聚焦、禁用、加载与错误语义。"
      >
        <template #actions>
          <CvTooltip
            text="显示或隐藏键盘状态说明"
            placement="bottom"
          >
            <template #default="{ tooltipId }">
              <CvIconButton
                label="切换状态说明"
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
            主要操作
          </CvButton>
          <CvButton variant="secondary">
            次要操作
          </CvButton>
          <CvButton variant="quiet">
            静默操作
          </CvButton>
          <CvButton variant="destructive">
            拒绝
          </CvButton>
          <CvButton loading>
            处理中
          </CvButton>
          <CvButton disabled>
            已禁用
          </CvButton>
        </div>
        <CvTypography
          v-if="showGuidance"
          as="p"
          variant="caption"
          tone="secondary"
          class="design-lab__guidance"
        >
          使用 Tab 显示键盘焦点环，按空格键或回车键触发当前控件。
        </CvTypography>
      </CvPanel>

      <CvPanel
        title="字段与选择"
        description="紧凑标签、明确错误和可预期的禁用状态。"
      >
        <div class="design-lab__form-grid">
          <CvField
            v-model="fieldValue"
            label="工作站标识"
            hint="仅保留本地界面草稿"
            autocomplete="off"
          />
          <CvSelect
            v-model="selectValue"
            label="算子类别"
            :options="selectOptions"
          />
          <CvField
            model-value="192.168.0.999"
            label="相机地址"
            error="请输入有效的 IPv4 地址。"
            autocomplete="off"
          />
          <CvField
            model-value="由运行时管理"
            label="正式运行状态来源"
            disabled
          />
        </div>
      </CvPanel>

      <CvPanel
        title="工业状态语法"
        description="品牌丹红只表达产品意图，不兼作检测结果状态。"
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
            <div><strong>品牌</strong><small>导航与操作意图</small></div>
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
        title="分层反馈"
        description="菜单、提示、弹窗与通知的监听、焦点和计时器会随组件卸载释放。"
      >
        <div class="design-lab__feedback-actions">
          <CvButton
            data-modal-trigger
            variant="primary"
            @click="modalOpen = true"
          >
            打开评审对话框
          </CvButton>
          <CvButton
            variant="secondary"
            @click="showToast('info')"
          >
            显示通知
          </CvButton>
          <CvButton
            variant="quiet"
            @click="showToast('ok')"
          >
            显示就绪通知
          </CvButton>
          <CvMenu
            v-model="menuOpen"
            label="Design Lab 操作"
            trigger-label="打开样本操作菜单"
            @select="handleSampleMenu"
          >
            <template #trigger>
              <CvIcon name="sliders" />
              <span>更多操作</span>
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
      title="产品模式"
      description="Page header、筛选、提示、表格与分页共享同一套 2.0 表面和密度语义。"
      class="design-lab__wide-panel"
      variant="section"
      :data-design-fixture="readonlyFixture.id"
    >
      <CvBreadcrumbs
        :items="readonlyFixture.breadcrumbs"
        label="Design Lab 只读样本位置"
      />
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
          placeholder="例如：检测流程或已保存…"
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
      <CvViewTabs
        v-model="readonlyView"
        :options="readonlyFixture.tabs"
        label="只读样本视图"
      />
      <section
        :id="readonlySummaryPanelId"
        role="tabpanel"
        :aria-labelledby="readonlySummaryTabId"
        :hidden="readonlyView !== 'summary'"
      >
        <CvDescriptionList
          :items="readonlyFixture.descriptionItems"
          label="检测流程只读摘要"
        />
      </section>
      <section
        :id="readonlyStagesPanelId"
        role="tabpanel"
        :aria-labelledby="readonlyStagesTabId"
        :hidden="readonlyView !== 'stages'"
      >
        <CvDataTable
          :rows="readonlyFixture.tableRows"
          :columns="tableColumns"
          row-key="id"
          caption="Design System 2.0 表格样本"
        />
      </section>
      <CvPagination
        v-model:page="samplePage"
        :page-size="3"
        :total-items="12"
        label="Design Lab 分页样本"
      />
    </CvPanel>

    <CvPanel
      title="产品状态"
      description="加载、空数据、错误、离线、状态过期、部分可用、冲突、状态未知、权限与 404 使用统一状态轴线。"
      class="design-lab__wide-panel"
      variant="section"
    >
      <div class="design-lab__state-grid">
        <CvPageState
          compact
          kind="loading"
          title="正在读取…"
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
      title="分隔条生命周期工作台"
      description="只有已挂载的分隔条持有活动调整任务时，才保留指针和键盘监听。"
      class="design-lab__splitter-panel"
      :padded="false"
      variant="tool"
    >
      <div
        class="design-lab__split-layout"
        :style="{ '--design-inspector-width': `${inspectorWidth}px` }"
        data-design-splitter-workbench
      >
        <div
          class="design-lab__canvas-sample"
        >
          <div
            class="design-lab__canvas-grid"
            aria-hidden="true"
          />
          <div class="design-lab__node design-lab__node--source">
            <strong>图像采集</strong><span>图像</span>
          </div>
          <div
            class="design-lab__connection"
            aria-hidden="true"
          />
          <div class="design-lab__node design-lab__node--target">
            <strong>二值化</strong><span>掩膜</span>
          </div>
        </div>

        <CvSplitter
          v-model="inspectorWidth"
          :min="220"
          :max="420"
          :step="8"
          label="调整属性检查器预览宽度"
        />

        <aside class="design-lab__inspector-sample">
          <CvTypography
            as="h3"
            variant="heading"
            weight="semibold"
          >
            属性检查器
          </CvTypography>
          <CvTypography
            as="p"
            variant="caption"
            tone="muted"
            mono
            data-design-numeric-sample
          >
            {{ inspectorWidth }} px
          </CvTypography>
          <dl>
            <div><dt>阈值</dt><dd>128</dd></div>
            <div><dt>模式</dt><dd>二值</dd></div>
            <div><dt>启用</dt><dd>是</dd></div>
          </dl>
        </aside>
      </div>
    </CvPanel>

    <CvModal
      :open="modalOpen"
      title="确认视觉基础"
      description="关闭此对话框前，键盘焦点会始终留在其中。"
      @close="modalOpen = false"
    >
      <CvTypography
        as="p"
        variant="body"
        tone="secondary"
      >
        安静的表面、克制的层级和明确的状态色，让高密度工业工作流保持清晰。
      </CvTypography>
      <template #footer>
        <CvButton
          variant="quiet"
          @click="modalOpen = false"
        >
          取消
        </CvButton>
        <CvButton
          data-modal-initial-focus
          variant="primary"
          @click="modalOpen = false; showToast('ok')"
        >
          接受此方向
        </CvButton>
      </template>
    </CvModal>

    <CvToastRegion
      :toasts="toasts"
      @dismiss="dismissToast"
    />
  </main>
</template>
