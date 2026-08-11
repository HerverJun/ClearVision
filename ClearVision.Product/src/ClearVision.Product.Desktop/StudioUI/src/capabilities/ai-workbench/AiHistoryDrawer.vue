<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue';
import { CvIcon } from '@/design-system/icons';
import {
  CvButton,
  CvIconButton,
  CvInlineAlert,
  CvModal,
  CvPagination,
  CvStatusBadge,
  CvViewTabs,
  type CvStatusTone
} from '@/design-system/primitives';
import type { AiHistoryState } from './aiHistoryController';
import type { AiRunHistorySummaryV1, AiSessionSummaryV1 } from './contracts';
import AiWorkbenchDrawer from './AiWorkbenchDrawer.vue';

const props = defineProps<{
  open: boolean;
  history: AiHistoryState;
  currentSessionId: string | null;
  routeProjectId: string | null;
}>();

const emit = defineEmits<{
  close: [];
  loadSessions: [offset: number];
  loadRuns: [offset: number, sessionId: string | null];
  restore: [session: AiSessionSummaryV1];
  delete: [session: AiSessionSummaryV1];
  reconcileDelete: [];
}>();

const activeTab = shallowRef<'sessions' | 'runs'>('sessions');
const runScope = shallowRef<'all' | 'current'>('all');
const deleteCandidate = shallowRef<AiSessionSummaryV1 | null>(null);
const historyTabOptions = Object.freeze([
  {
    value: 'sessions',
    label: '会话',
    description: '查看并恢复历史会话',
    id: 'ai-history-session-tab',
    controls: 'ai-history-session-panel'
  },
  {
    value: 'runs',
    label: '运行',
    description: '查看规划与构建运行记录',
    id: 'ai-history-run-tab',
    controls: 'ai-history-run-panel'
  }
]);
const dateFormatter = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit'
});

const sessionPage = computed(() => Math.floor(props.history.sessions.offset / props.history.sessions.limit) + 1);
const runPage = computed(() => Math.floor(props.history.runs.offset / props.history.runs.limit) + 1);
const deleteBusy = computed(() => props.history.deletePhase === 'deleting');
const deleteNeedsReconcile = computed(() => props.history.deletePhase === 'unknown-outcome');

watch(() => props.open, open => {
  if (!open) return;
  emit('loadSessions', 0);
  emit('loadRuns', 0, runScope.value === 'current' ? props.currentSessionId : null);
});

watch(() => props.history.deletePhase, phase => {
  if (phase === 'deleted') deleteCandidate.value = null;
});

function formatDate(value: string): string {
  return dateFormatter.format(new Date(value));
}

function lifecycleLabel(value: string): string {
  const labels: Readonly<Record<string, string>> = {
    idle: '等待任务',
    plan_ready: '方案就绪',
    plan_blocked: '方案受阻',
    build_ready: '候选就绪',
    build_failed: '构建失败',
    build_inputs_changed: '输入已更新',
    plan_cancelled: '规划已取消',
    build_cancelled: '构建已取消'
  };
  return labels[value] ?? '可恢复会话';
}

function runKindLabel(value: AiRunHistorySummaryV1['kind']): string {
  if (value === 'plan') return '方案规划';
  if (value === 'build') return '候选构建';
  return '历史运行';
}

function runStatus(value: AiRunHistorySummaryV1['status']): Readonly<{ label: string; tone: CvStatusTone }> {
  const statuses: Readonly<Record<AiRunHistorySummaryV1['status'], Readonly<{ label: string; tone: CvStatusTone }>>> = {
    pending: { label: '等待中', tone: 'idle' },
    running: { label: '运行中', tone: 'info' },
    completed: { label: '已完成', tone: 'ok' },
    failed: { label: '失败', tone: 'error' },
    cancelled: { label: '已取消', tone: 'idle' },
    blocked: { label: '受阻', tone: 'warning' },
    warning: { label: '有警告', tone: 'warning' }
  };
  return statuses[value];
}

function recoveryLabel(value: AiRunHistorySummaryV1['recoveryState']): string {
  if (value === 'active') return '服务端运行中';
  if (value === 'reconciling') return '正在恢复终态';
  if (value === 'terminal') return '终态已确认';
  return '状态待核对';
}

function restoreLabel(session: AiSessionSummaryV1): string {
  if (session.sessionId === props.currentSessionId) return '当前会话';
  if (session.projectId && session.projectId !== props.routeProjectId) return '前往绑定工程';
  if (!session.projectId && props.routeProjectId) return '前往独立工作台';
  return '恢复会话';
}

function selectRunScope(scope: 'all' | 'current'): void {
  runScope.value = scope;
  emit('loadRuns', 0, scope === 'current' ? props.currentSessionId : null);
}

function changeSessionPage(page: number): void {
  emit('loadSessions', (page - 1) * props.history.sessions.limit);
}

function changeRunPage(page: number): void {
  emit('loadRuns', (page - 1) * props.history.runs.limit,
    runScope.value === 'current' ? props.currentSessionId : null);
}

function confirmDelete(): void {
  const candidate = deleteCandidate.value;
  if (!candidate) return;
  emit('delete', candidate);
}
</script>

<template>
  <AiWorkbenchDrawer
    :open="open"
    title="历史与恢复"
    description="历史来自当前用户的服务端记录。恢复会替换当前会话并释放旧请求与事件流。"
    @close="emit('close')"
  >
    <CvViewTabs
      v-model="activeTab"
      class="ai-history__tabs"
      :options="historyTabOptions"
      label="历史类型"
    />

    <CvInlineAlert
      v-if="history.message && history.deletePhase !== 'idle'"
      :tone="history.deletePhase === 'deleted' ? 'success' : history.deletePhase === 'blocked' ? 'warning' : 'error'"
      :title="history.deletePhase === 'deleted' ? '删除完成' : history.deletePhase === 'blocked' ? '删除已阻断' : '删除结果待核对'"
      aria-live="polite"
    >
      <p>{{ history.message }}</p>
      <CvButton
        v-if="deleteNeedsReconcile"
        size="sm"
        variant="secondary"
        @click="emit('reconcileDelete')"
      >
        核对删除结果
      </CvButton>
    </CvInlineAlert>

    <section
      v-show="activeTab === 'sessions'"
      id="ai-history-session-panel"
      role="tabpanel"
      aria-labelledby="ai-history-session-tab"
      tabindex="0"
      :aria-busy="history.sessionsPhase === 'loading' ? 'true' : undefined"
    >
      <div class="ai-history__section-heading">
        <div>
          <h3>会话历史</h3>
          <p>仅显示当前认证用户可访问的公开摘要。</p>
        </div>
        <CvIconButton
          label="刷新会话历史"
          size="sm"
          :loading="history.sessionsPhase === 'loading'"
          @click="emit('loadSessions', history.sessions.offset)"
        >
          <CvIcon
            name="refresh"
            size="sm"
          />
        </CvIconButton>
      </div>

      <p
        v-if="history.sessionsPhase === 'loading' && history.sessions.items.length === 0"
        class="ai-history__loading"
        role="status"
      >
        正在读取会话历史…
      </p>
      <CvInlineAlert
        v-else-if="history.sessionsPhase === 'error'"
        tone="error"
        title="会话历史不可用"
      >
        {{ history.message }}
      </CvInlineAlert>
      <div
        v-else-if="history.sessions.items.length === 0"
        class="ai-history__empty"
      >
        <CvIcon
          name="empty"
          size="lg"
          aria-hidden="true"
        />
        <h3>暂无历史会话</h3>
        <p>当前安全会话会在产生服务端状态后出现在这里。</p>
      </div>
      <ul
        v-else
        class="ai-history__list"
      >
        <li
          v-for="session in history.sessions.items"
          :key="session.sessionId"
          class="ai-history__item"
          :aria-current="session.sessionId === currentSessionId ? 'true' : undefined"
        >
          <div class="ai-history__item-main">
            <div class="ai-history__item-title">
              <strong>{{ session.projectId ? '工程绑定会话' : '独立方案会话' }}</strong>
              <CvStatusBadge
                :tone="session.sessionId === currentSessionId ? 'info' : 'idle'"
                :label="lifecycleLabel(session.lifecycleState)"
              />
            </div>
            <p>更新于 {{ formatDate(session.updatedAtUtc) }} · 会话版本 {{ session.revision }}</p>
            <small v-if="session.projectId && session.projectId !== routeProjectId">需从该会话绑定的工程入口恢复。</small>
          </div>
          <div class="ai-history__item-actions">
            <CvButton
              size="sm"
              variant="secondary"
              :disabled="session.sessionId === currentSessionId"
              @click="emit('restore', session)"
            >
              {{ restoreLabel(session) }}
            </CvButton>
            <CvIconButton
              :label="`删除${session.projectId ? '工程绑定' : '独立方案'}会话`"
              size="sm"
              :disabled="deleteBusy || deleteNeedsReconcile"
              @click="deleteCandidate = session"
            >
              <CvIcon
                name="trash"
                size="sm"
              />
            </CvIconButton>
          </div>
        </li>
      </ul>

      <CvPagination
        v-if="history.sessions.total > history.sessions.limit"
        :page="sessionPage"
        :page-size="history.sessions.limit"
        :total-items="history.sessions.total"
        label="会话历史分页"
        @update:page="changeSessionPage"
      />
    </section>

    <section
      v-show="activeTab === 'runs'"
      id="ai-history-run-panel"
      role="tabpanel"
      aria-labelledby="ai-history-run-tab"
      tabindex="0"
      :aria-busy="history.runsPhase === 'loading' ? 'true' : undefined"
    >
      <div class="ai-history__section-heading">
        <div>
          <h3>运行历史</h3>
          <p>分页显示公开的规划与构建摘要。</p>
        </div>
        <CvIconButton
          label="刷新运行历史"
          size="sm"
          :loading="history.runsPhase === 'loading'"
          @click="emit('loadRuns', history.runs.offset, runScope === 'current' ? currentSessionId : null)"
        >
          <CvIcon
            name="refresh"
            size="sm"
          />
        </CvIconButton>
      </div>
      <div
        class="ai-history__scope"
        role="group"
        aria-label="运行历史范围"
      >
        <button
          type="button"
          :aria-pressed="runScope === 'all'"
          @click="selectRunScope('all')"
        >
          全部会话
        </button>
        <button
          type="button"
          :disabled="!currentSessionId"
          :aria-pressed="runScope === 'current'"
          @click="selectRunScope('current')"
        >
          当前会话
        </button>
      </div>

      <p
        v-if="history.runsPhase === 'loading' && history.runs.items.length === 0"
        class="ai-history__loading"
        role="status"
      >
        正在读取运行历史…
      </p>
      <CvInlineAlert
        v-else-if="history.runsPhase === 'error'"
        tone="error"
        title="运行历史不可用"
      >
        {{ history.message }}
      </CvInlineAlert>
      <div
        v-else-if="history.runs.items.length === 0"
        class="ai-history__empty"
      >
        <CvIcon
          name="clock"
          size="lg"
          aria-hidden="true"
        />
        <h3>暂无运行记录</h3>
        <p>开始方案规划或候选构建后，公开摘要会出现在这里。</p>
      </div>
      <ol
        v-else
        class="ai-history__list"
      >
        <li
          v-for="run in history.runs.items"
          :key="run.runId"
          class="ai-history__item ai-history__item--run"
        >
          <div class="ai-history__item-main">
            <div class="ai-history__item-title">
              <strong>{{ runKindLabel(run.kind) }}</strong>
              <CvStatusBadge
                :tone="runStatus(run.status).tone"
                :label="runStatus(run.status).label"
              />
            </div>
            <p>{{ run.summary || run.title }}</p>
            <small>{{ recoveryLabel(run.recoveryState) }} · {{ formatDate(run.updatedAtUtc) }} · {{ run.eventCount }} 个公开事件</small>
            <p
              v-if="run.firstFixRecommendation"
              class="ai-history__recommendation"
            >
              <strong>首要建议：</strong>{{ run.firstFixRecommendation }}
            </p>
          </div>
        </li>
      </ol>

      <CvPagination
        v-if="history.runs.total > history.runs.limit"
        :page="runPage"
        :page-size="history.runs.limit"
        :total-items="history.runs.total"
        label="运行历史分页"
        @update:page="changeRunPage"
      />
    </section>

    <CvModal
      :open="deleteCandidate !== null"
      title="删除会话"
      description="删除不会删除工程，但服务端会拒绝删除仍有关联运行、操作、交接候选或工作区暂存草稿的会话。"
      size="sm"
      @close="deleteCandidate = null"
    >
      <p class="ai-history__delete-copy">
        确认删除这条会话历史？此操作只作用于当前用户，且不能撤销。
      </p>
      <template #footer>
        <CvButton
          size="sm"
          variant="secondary"
          :disabled="deleteBusy"
          @click="deleteCandidate = null"
        >
          取消
        </CvButton>
        <CvButton
          data-modal-initial-focus
          size="sm"
          variant="danger"
          :loading="deleteBusy"
          @click="confirmDelete"
        >
          删除会话
        </CvButton>
      </template>
    </CvModal>
  </AiWorkbenchDrawer>
</template>

<style scoped>
.ai-history__scope { display: inline-flex; min-width: 0; padding: 2px; border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-md); background: var(--cv-surface-2); }
.ai-history__tabs { margin-block-end: var(--cv-space-4); }
.ai-history__scope button { min-height: var(--cv-density-control-height-sm); padding: 0 var(--cv-space-3); border: 0; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-text-secondary); cursor: pointer; font: inherit; font-size: var(--cv-font-size-xs); }
.ai-history__scope button[aria-pressed="true"] { background: var(--cv-surface-raised); color: var(--cv-text-primary); box-shadow: var(--cv-elevation-raised); }
.ai-history__scope button:hover:not(:disabled) { color: var(--cv-color-link); }
.ai-history__scope button:disabled { cursor: not-allowed; opacity: 0.48; }
.ai-history__section-heading { display: flex; min-width: 0; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-3); margin-block-end: var(--cv-space-3); }
.ai-history__section-heading h3 { margin: 0; font-size: var(--cv-font-size-sm); }
.ai-history__section-heading p { margin: 2px 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
.ai-history__scope { margin-block-end: var(--cv-space-3); }
.ai-history__list { display: grid; min-width: 0; gap: 0; margin: 0; padding: 0; border-block: 1px solid var(--cv-border-subtle); list-style: none; }
.ai-history__item { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-4); padding: var(--cv-space-3) 0; border-block-end: 1px solid var(--cv-border-subtle); }
.ai-history__item:last-child { border-block-end: 0; }
.ai-history__item[aria-current="true"] { background: color-mix(in srgb, var(--cv-color-status-info-soft) 46%, transparent); }
.ai-history__item-main { min-width: 0; }
.ai-history__item-title { display: flex; min-width: 0; flex-wrap: wrap; align-items: center; gap: var(--cv-space-2); }
.ai-history__item-title strong { font-size: var(--cv-font-size-sm); }
.ai-history__item-main p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); overflow-wrap: anywhere; }
.ai-history__item-main small { display: block; margin-block-start: var(--cv-space-1); color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); overflow-wrap: anywhere; }
.ai-history__item-actions { display: flex; flex: 0 0 auto; align-items: center; gap: var(--cv-space-2); }
.ai-history__recommendation { color: var(--cv-color-status-warning-strong) !important; }
.ai-history__loading { margin: var(--cv-space-6) 0; color: var(--cv-text-secondary); text-align: center; }
.ai-history__empty { display: grid; justify-items: center; gap: var(--cv-space-2); padding: var(--cv-space-8) var(--cv-space-4); color: var(--cv-text-secondary); text-align: center; }
.ai-history__empty h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.ai-history__empty p { max-width: 42ch; margin: 0; font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-history__delete-copy { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-normal); overflow-wrap: anywhere; }

@media (max-width: 520px) {
  .ai-history__item { align-items: flex-start; flex-direction: column; }
  .ai-history__item-actions { width: 100%; justify-content: flex-end; }
}
</style>
