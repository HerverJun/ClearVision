<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import { RouterLink, RouterView, useRoute } from 'vue-router';
import { visibleProductNavigation } from '@/app/navigation';
import { useAuthLifecycleRoot } from '@/app/auth';
import { useProductRuntime } from '@/app/productRuntime';
import { useStudioPlatform } from '@/app/studioPlatform';
import { CvButton, CvInlineAlert, CvModal, CvStatusBadge } from '@/design-system/primitives';
import { CvIcon } from '@/design-system/icons';
import type { CvIconName } from '@/design-system/icons';
import './product-layout.css';

const route = useRoute();
const runtime = useProductRuntime();
const authRoot = useAuthLifecycleRoot();
const platform = useStudioPlatform();
const session = runtime.session.projection;
const systemStatus = runtime.systemStatus.projection;
const preferences = runtime.preferences.projection;
const contentRoot = ref<HTMLElement>();
const appearanceDetails = ref<HTMLDetailsElement>();
const workspaceMode = computed(() => route.meta.workspaceMode === true);
const navigation = computed(() => visibleProductNavigation(
  session.user?.role,
  platform.startup.featureFlags
));
const navigationIcons: Readonly<Record<string, CvIconName>> = Object.freeze({
  '/overview': 'overview',
  '/projects': 'projects',
  '/operators': 'operators',
  '/stations': 'stations',
  '/results': 'results',
  '/diagnostics': 'diagnostics',
  '/about': 'about'
});

function resolveBreadcrumbPath(path: string): string {
  return path.replace(/:([A-Za-z0-9_]+)/g, (_match, parameterName: string) => {
    const value = route.params[parameterName];
    return encodeURIComponent(Array.isArray(value) ? value[0] ?? '' : String(value ?? ''));
  });
}

const breadcrumbs = computed(() => route.matched
  .filter(record => !record.meta.internal && record.meta.breadcrumb)
  .map(record => ({
    label: record.meta.breadcrumb,
    to: record.path === '/' ? '/overview' : resolveBreadcrumbPath(record.path)
  }))
);
const statusTone = computed(() => {
  if (systemStatus.phase === 'online') return 'ok';
  if (systemStatus.phase === 'stale') return 'warning';
  return 'ng';
});
const statusLabel = computed(() => {
  if (systemStatus.phase === 'loading') return '服务检查中';
  if (systemStatus.phase === 'online') return '本地服务在线';
  if (systemStatus.phase === 'stale') return '状态已过期';
  return '本地服务不可用';
});
const roleLabel = computed(() => {
  const role = session.user?.role;
  if (role === 'Admin') return '管理员';
  if (role === 'Engineer') return '工程师';
  if (role === 'Operator') return '操作员';
  return role ?? '预置会话不可用';
});
const themeLabel = computed(() => preferences.theme === 'dark' ? '深色' : '浅色');
const densityLabel = computed(() => preferences.density === 'comfortable' ? '舒适' : '紧凑');
const userInitial = computed(() => (session.user?.username?.trim().charAt(0) || '未').toLocaleUpperCase());
const leaveGuard = runtime.leaveGuard;
const leavePromptOpen = computed(() => leaveGuard.projection.phase === 'prompting');
const leavePromptTitle = computed(() => leaveGuard.projection.protectionKind === 'project-update-conflict'
  ? '放弃未解决的工程编辑？'
  : '放弃本地工作区修改？');

function closeAppearance(): void {
  const details = appearanceDetails.value;
  if (!details?.open) return;
  details.open = false;
  details.querySelector<HTMLElement>('summary')?.focus();
}

function focusContent(): void {
  contentRoot.value?.focus({ preventScroll: true });
}

watch(() => route.path, async (current, previous) => {
  if (!previous || current === previous) return;
  await nextTick();
  contentRoot.value?.focus({ preventScroll: true });
});

onMounted(() => runtime.preferences.apply());
</script>

<template>
  <div
    class="product-layout"
    :class="{ 'product-layout--workspace': workspaceMode }"
    data-product-shell="ready"
    :data-workspace-mode="workspaceMode"
    :data-leave-guard-phase="leaveGuard.projection.phase"
    :data-leave-guard-owner-count="leaveGuard.diagnostics.ownerCount"
    :data-project-command-phase="runtime.projectLifecycle.projection.phase"
  >
    <a
      class="product-layout__skip-link"
      href="#product-main"
      @click.prevent="focusContent"
    >跳到主要内容</a>

    <aside class="product-layout__sidebar">
      <RouterLink
        class="product-layout__brand"
        to="/overview"
        aria-label="ClearVision Studio 概览"
      >
        <span
          class="product-layout__brand-mark"
          aria-hidden="true"
        >CV</span>
        <span>
          <strong>ClearVision</strong>
          <small>Vision Studio</small>
        </span>
      </RouterLink>

      <div class="product-layout__nav-heading">
        产品工作区
      </div>
      <nav aria-label="产品主导航">
        <RouterLink
          v-for="item in navigation"
          :key="item.to"
          :to="item.to"
          class="product-layout__nav-item"
          :data-product-nav="item.to"
          :aria-label="`${item.label}，${item.description}`"
          :title="item.description"
        >
          <CvIcon
            v-if="workspaceMode"
            :name="navigationIcons[item.to] ?? 'overview'"
            size="md"
          />
          <span class="product-layout__nav-label">{{ item.label }}</span>
          <small>{{ item.description }}</small>
        </RouterLink>
      </nav>

      <div class="product-layout__sidebar-note">
        <small>工作模式</small>
        <strong>可编辑工程工作台</strong>
        <span>Save 与 Formal Run 由后端权威链负责；Preview 不代表正式运行可执行。</span>
      </div>
    </aside>

    <div class="product-layout__workspace">
      <header class="product-layout__topbar">
        <div
          v-if="workspaceMode"
          class="product-layout__workspace-mode-label"
        >
          <span>Studio</span>
          <strong>工程工作台</strong>
        </div>
        <nav
          v-else
          class="product-layout__breadcrumbs"
          aria-label="面包屑"
        >
          <ol>
            <li
              v-for="(crumb, index) in breadcrumbs"
              :key="`${crumb.to}-${index}`"
            >
              <RouterLink
                v-if="index < breadcrumbs.length - 1"
                :to="crumb.to"
              >
                {{ crumb.label }}
              </RouterLink>
              <span
                v-else
                aria-current="page"
              >{{ crumb.label }}</span>
            </li>
          </ol>
        </nav>

        <div class="product-layout__topbar-actions">
          <div
            class="product-layout__service-status"
            role="status"
            aria-live="polite"
            aria-atomic="true"
          >
            <CvStatusBadge
              :tone="statusTone"
              :label="statusLabel"
            />
          </div>

          <details
            ref="appearanceDetails"
            class="product-layout__appearance"
            data-product-appearance
            @keydown.esc.stop.prevent="closeAppearance"
          >
            <summary
              class="product-layout__appearance-trigger"
              :aria-label="`外观设置，当前${themeLabel}主题，${densityLabel}密度`"
            >
              <span>外观</span>
              <small>{{ themeLabel }} · {{ densityLabel }}</small>
              <CvIcon
                name="chevron-right"
                size="sm"
              />
            </summary>
            <div
              class="product-layout__appearance-popover"
              aria-label="外观设置"
            >
              <div class="product-layout__appearance-section">
                <span>主题</span>
                <div
                  class="product-layout__preference-group"
                  role="group"
                  aria-label="主题"
                >
                  <CvButton
                    size="sm"
                    :variant="preferences.theme === 'light' ? 'secondary' : 'quiet'"
                    :aria-pressed="preferences.theme === 'light'"
                    @click="runtime.preferences.setTheme('light')"
                  >
                    浅色
                  </CvButton>
                  <CvButton
                    size="sm"
                    :variant="preferences.theme === 'dark' ? 'secondary' : 'quiet'"
                    :aria-pressed="preferences.theme === 'dark'"
                    @click="runtime.preferences.setTheme('dark')"
                  >
                    深色
                  </CvButton>
                </div>
              </div>
              <div class="product-layout__appearance-section">
                <span>界面密度</span>
                <div
                  class="product-layout__preference-group"
                  role="group"
                  aria-label="界面密度"
                >
                  <CvButton
                    size="sm"
                    :variant="preferences.density === 'compact' ? 'secondary' : 'quiet'"
                    :aria-pressed="preferences.density === 'compact'"
                    @click="runtime.preferences.setDensity('compact')"
                  >
                    紧凑
                  </CvButton>
                  <CvButton
                    size="sm"
                    :variant="preferences.density === 'comfortable' ? 'secondary' : 'quiet'"
                    :aria-pressed="preferences.density === 'comfortable'"
                    @click="runtime.preferences.setDensity('comfortable')"
                  >
                    舒适
                  </CvButton>
                </div>
              </div>
            </div>
          </details>

          <div class="product-layout__user">
            <span
              class="product-layout__user-avatar"
              aria-hidden="true"
            >{{ userInitial }}</span>
            <span class="product-layout__user-copy">
              <strong>{{ session.user?.username ?? '未认证' }}</strong>
              <small>{{ roleLabel }}</small>
            </span>
          </div>
          <RouterLink
            class="product-layout__session-command"
            to="/change-password"
          >
            修改密码
          </RouterLink>
          <CvButton
            size="sm"
            variant="quiet"
            data-auth-command="logout"
            @click="authRoot.auth.logout()"
          >
            退出
          </CvButton>
        </div>
      </header>

      <main
        id="product-main"
        ref="contentRoot"
        class="product-layout__content"
        :class="{ 'product-layout__content--workspace': workspaceMode }"
        tabindex="-1"
      >
        <CvInlineAlert
          v-if="leaveGuard.projection.phase === 'blocked'"
          class="product-layout__session-alert"
          tone="warning"
          compact
          title="当前操作已被 Leave Guard 阻止"
          data-product-state="leave-blocked"
        >
          {{ leaveGuard.projection.message }}
        </CvInlineAlert>
        <CvInlineAlert
          v-if="authRoot.auth.projection.errorCode === 'CHANGE_PASSWORD_BLOCKED'"
          class="product-layout__session-alert"
          tone="warning"
          compact
          title="会话操作已阻止"
        >
          {{ authRoot.auth.projection.message }}
        </CvInlineAlert>
        <CvInlineAlert
          v-if="!workspaceMode && session.phase === 'error'"
          class="product-layout__session-alert"
          tone="error"
          compact
          title="会话读取失败"
          data-product-state="error"
        >
          {{ session.message }} 后端授权继续是唯一安全边界。
        </CvInlineAlert>
        <CvInlineAlert
          v-else-if="!workspaceMode && session.phase === 'stale'"
          class="product-layout__session-alert"
          tone="warning"
          compact
          title="会话投影已过期"
          data-product-state="stale"
        >
          {{ session.message }}
        </CvInlineAlert>
        <RouterView v-slot="{ Component }">
          <component
            :is="Component"
            :key="route.path"
          />
        </RouterView>
      </main>
    </div>

    <CvModal
      :open="leavePromptOpen"
      :title="leavePromptTitle"
      :description="leaveGuard.projection.message"
      size="sm"
      :close-on-backdrop="false"
      @close="leaveGuard.cancelPrompt"
    >
      <CvInlineAlert tone="warning">
        后端状态已经明确；继续只会放弃本地未保存投影，不会把 UI 状态当作服务端结果。
      </CvInlineAlert>
      <template #footer>
        <CvButton
          size="sm"
          variant="secondary"
          data-modal-initial-focus
          data-testid="leave-guard-stay"
          @click="leaveGuard.cancelPrompt"
        >
          继续留在此页
        </CvButton>
        <CvButton
          size="sm"
          variant="danger"
          data-testid="leave-guard-discard"
          @click="leaveGuard.confirmPrompt"
        >
          放弃并离开
        </CvButton>
      </template>
    </CvModal>
  </div>
</template>
