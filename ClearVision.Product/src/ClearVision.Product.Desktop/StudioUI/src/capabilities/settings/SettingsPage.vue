<script setup lang="ts">
import { computed, inject, onBeforeUnmount, shallowRef, watch } from 'vue';
import {
  CvButton,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvStatusBadge,
  type CvPageStateKind,
  type CvStatusTone
} from '@/design-system';
import { useProductRuntime, type ProductRuntime } from '@/app/productRuntime';
import { authLifecycleRootKey } from '@/app/auth';
import {
  createSettingsOwner,
  type SettingsOwner,
  type SettingsOwnerPhase,
  type SettingsOwnerProjection
} from './settingsOwner';
import type { SettingsProjectionV1 } from './decoder';
import SettingsGroupNavigation from './SettingsGroupNavigation.vue';
import SettingsOverview from './SettingsOverview.vue';
import type { SettingsNavigationTarget } from './settingsViewModel';

export type SettingsPageRuntime = Pick<ProductRuntime, 'api' | 'session'> & {
  readonly leaveGuard?: ProductRuntime['leaveGuard'];
};

const props = defineProps<{
  runtime?: SettingsPageRuntime;
}>();

const runtime = props.runtime ?? useProductRuntime();
const authRoot = inject(authLifecycleRootKey, null);
const sessionProjection = computed(() => runtime.session.projection);
const role = computed(() => sessionProjection.value.user?.role ?? null);
const sessionPhase = computed(() => sessionProjection.value.phase);
const activeGroup = shallowRef<SettingsNavigationTarget>('overview');
const navigationMessage = shallowRef<string | null>(null);
const owner = shallowRef<SettingsOwner | null>(null);
const ownerProjection = computed<Readonly<SettingsOwnerProjection> | null>(() => owner.value?.projection ?? null);
const mountedOwner = computed<SettingsOwner>(() => {
  if (!owner.value) throw new Error('Settings content cannot render without a mounted owner.');
  return owner.value;
});
const phase = computed<SettingsOwnerPhase>(() => ownerProjection.value?.phase ?? 'loading');
const settings = computed(() => ownerProjection.value?.settings ?? null);
const error = computed(() => ownerProjection.value?.error ?? null);
const pageStateKind = computed<CvPageStateKind | null>(() => {
  if (phase.value === 'idle' || phase.value === 'loading') return 'loading';
  if (phase.value === 'forbidden') return 'forbidden';
  if (phase.value === 'error' && error.value?.code === 'unauthorized') return 'unauthorized';
  if (phase.value === 'error') return 'error';
  return null;
});
const statusTone = computed<CvStatusTone>(() => {
  if (phase.value === 'ready') return 'ok';
  if (phase.value === 'stale') return 'warning';
  if (phase.value === 'loading') return 'info';
  if (phase.value === 'forbidden') return 'warning';
  return 'error';
});
const statusLabel = computed(() => {
  if (phase.value === 'ready') return settings.value?.safeSubset ? 'safe subset' : '完整投影';
  if (phase.value === 'stale') return '投影已过期';
  if (phase.value === 'loading') return '读取中';
  if (phase.value === 'forbidden') return '禁止访问';
  if (error.value?.code === 'unauthorized') return '会话失效';
  return '读取失败';
});
const stateTitle = computed(() => {
  if (pageStateKind.value === 'unauthorized') return '会话不可用';
  if (pageStateKind.value === 'forbidden') return '无权访问设置';
  if (error.value?.code === 'decode') return 'Settings 投影无法解析';
  return 'Settings 读取失败';
});
const stateDescription = computed(() => {
  if (pageStateKind.value === 'unauthorized') return '当前会话已失效，请重新登录后再读取 Settings。';
  if (pageStateKind.value === 'forbidden') return '当前账户没有进入 Settings 的权限；前端不会通过隐藏逻辑绕过后端授权。';
  if (error.value?.code === 'decode') return '服务端响应未通过已冻结 decoder 校验，未验证字段不会显示。';
  return error.value?.publicMessage ?? '本地 Settings 服务未返回可用投影。';
});
const showReadOnlyContent = computed(() =>
  (phase.value === 'ready' || phase.value === 'stale') && settings.value !== null
);
let disposed = false;
let detachLeaveParticipant: (() => void) | undefined;

function readOnlyProjection(): SettingsProjectionV1 {
  if (!settings.value) throw new Error('Settings read-only content requires a decoded projection.');
  return settings.value;
}

function replaceOwner(nextRole: string | null): void {
  if (disposed) return;
  detachLeaveParticipant?.();
  detachLeaveParticipant = undefined;
  const previous = owner.value;
  owner.value = null;
  previous?.dispose('settings-role-changed');
  if (sessionPhase.value !== 'authenticated') return;
  const next = createSettingsOwner({ runtime, role: nextRole });
  owner.value = next;
  if (runtime.leaveGuard) {
    detachLeaveParticipant = runtime.leaveGuard.attachSettingsParticipant({
      inspect: () => next.leaveProtection()
    });
  }
  void next.start();
}

function disposeOwner(reason: string): void {
  const current = owner.value;
  owner.value = null;
  detachLeaveParticipant?.();
  detachLeaveParticipant = undefined;
  current?.dispose(reason);
}

async function refresh(): Promise<void> {
  await owner.value?.refresh();
}

function selectGroup(target: SettingsNavigationTarget): void {
  if (target === activeGroup.value) return;
  const protection = owner.value?.leaveProtection();
  if (protection === 'settings-pending') {
    navigationMessage.value = '当前 Settings 操作仍在执行，请等待完成或失败后再切换分组。';
    return;
  }
  if (protection === 'settings-unknown') {
    navigationMessage.value = '当前 Settings 操作结果未知，请先重新读取服务端状态后再切换分组。';
    return;
  }
  navigationMessage.value = null;
  activeGroup.value = target;
}

watch([role, sessionPhase], ([nextRole, nextPhase]) => {
  if (nextPhase !== 'authenticated') {
    disposeOwner('settings-session-ended');
    return;
  }
  replaceOwner(nextRole);
}, { immediate: true });

onBeforeUnmount(() => {
  disposed = true;
  disposeOwner('settings-route-leave');
});
</script>

<template>
  <section
    class="settings-page"
    data-capability="settings"
    :data-settings-phase="phase"
    :data-settings-safe-subset="settings?.safeSubset"
    :aria-busy="phase === 'loading' ? 'true' : undefined"
  >
    <CvPageHeader
      eyebrow="系统配置"
      title="设置"
      description="读取现有服务端 Settings 投影，核对 authority 范围与当前生效观察值。"
    >
      <template #meta>
        <CvStatusBadge
          :tone="statusTone"
          :label="statusLabel"
        />
        <span class="settings-page__role">{{ role ?? '未认证' }}</span>
      </template>
      <template #actions>
        <CvButton
          v-if="phase !== 'forbidden'"
          size="sm"
          :loading="phase === 'loading'"
          loading-label="正在读取设置"
          @click="refresh"
        >
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <div
      v-if="showReadOnlyContent"
      class="settings-page__workspace"
    >
      <SettingsGroupNavigation
        :active="activeGroup"
        :role="role"
        :projection="readOnlyProjection()"
        @select="selectGroup"
      />
      <div class="settings-page__content">
        <CvInlineAlert
          v-if="phase === 'stale'"
          class="settings-page__stale-alert"
          tone="warning"
          title="投影已过期"
        >
          当前内容仅供核对；请刷新服务端投影后再继续任何后续操作。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="navigationMessage"
          class="settings-page__navigation-alert"
          tone="warning"
          title="暂不能切换分组"
        >
          {{ navigationMessage }}
        </CvInlineAlert>
        <SettingsOverview
          :projection="readOnlyProjection()"
          :active-group="activeGroup"
          :owner="mountedOwner"
          :role="role"
          :auth="authRoot?.auth ?? null"
        />
      </div>
    </div>

    <CvPageState
      v-else
      :kind="pageStateKind ?? 'loading'"
      :title="pageStateKind === 'loading' ? '正在读取 Settings' : stateTitle"
      :description="pageStateKind === 'loading' ? '正在从现有服务端 endpoint 读取投影。' : stateDescription"
    >
      <template
        v-if="pageStateKind === 'error' || pageStateKind === 'unauthorized'"
        #actions
      >
        <CvButton
          size="sm"
          @click="refresh"
        >
          重新读取
        </CvButton>
      </template>
    </CvPageState>
  </section>
</template>

<style scoped>
.settings-page {
  display: grid;
  max-width: 1480px;
  min-width: 0;
  gap: var(--cv-density-page-gap);
}

.settings-page__role {
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
}

.settings-page__workspace {
  display: grid;
  min-width: 0;
  grid-template-columns: minmax(196px, 232px) minmax(0, 1fr);
  align-items: start;
  gap: var(--cv-density-page-gap);
}

.settings-page__content {
  display: grid;
  min-width: 0;
  gap: var(--cv-density-page-gap);
}

.settings-page__stale-alert { margin-bottom: 0; }

@media (max-width: 900px) {
  .settings-page__workspace { grid-template-columns: minmax(0, 1fr); }
}
</style>
