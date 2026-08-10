<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router';
import { useAuthLifecycleRoot } from '@/app/auth';
import { visibleProductNavigation } from '@/app/navigation';
import { useProductRuntime } from '@/app/productRuntime';
import { CvBrand } from '@/design-system/patterns';
import {
  CvButton,
  CvInlineAlert,
  CvMenu,
  CvMenuItem,
  CvModal,
  CvStatusBadge
} from '@/design-system/primitives';
import { CvIcon } from '@/design-system/icons';
import type { CvIconName } from '@/design-system/icons';
import './product-layout.css';

const route = useRoute();
const router = useRouter();
const runtime = useProductRuntime();
const authRoot = useAuthLifecycleRoot();
const session = runtime.session.projection;
const systemStatus = runtime.systemStatus.projection;
const preferences = runtime.preferences.projection;
const contentRoot = ref<HTMLElement>();
const appearanceOpen = ref(false);
const moreOpen = ref(false);
const userOpen = ref(false);
const workspaceMode = computed(() => route.meta.workspaceMode === true);
const routeViewKey = computed(() => route.name === 'project-workspace'
  ? 'project-workspace'
  : route.path);
const navigationIcons: Readonly<Record<string, CvIconName>> = Object.freeze({
  '/overview': 'overview',
  '/projects': 'projects',
  '/inspection': 'results',
  '/operators': 'operators',
  '/stations': 'stations',
  '/results': 'results',
  '/settings': 'sliders',
  '/diagnostics': 'diagnostics',
  '/about': 'about'
});

interface ProductTopNavigationItem {
  readonly label: string;
  readonly description: string;
  readonly to?: string;
  readonly current?: boolean;
  readonly disabled?: boolean;
}

const visibleNavigation = computed(() => visibleProductNavigation(
  session.user?.role,
  runtime.featureFlags
));

function isVisibleNavigation(path: string): boolean {
  return visibleNavigation.value.some(item => item.to === path);
}

const productTopNavigation = computed<readonly ProductTopNavigationItem[]>(() => {
  const items: ProductTopNavigationItem[] = [];
  if (isVisibleNavigation('/projects')) {
    items.push({
      label: '工程',
      to: '/projects',
      description: '工程管理',
      current: !workspaceMode.value && route.path.startsWith('/projects') &&
        !route.path.endsWith('/inspection') && !route.path.endsWith('/ai')
    });
  }
  if (workspaceMode.value) {
    items.push({ label: '流程', to: route.fullPath, description: '当前工程流程', current: true });
  }
  if (isVisibleNavigation('/results')) {
    items.push({
      label: '检测结果',
      to: '/results',
      description: '正式检测结果与历史追溯',
      current: route.path.startsWith('/results')
    });
  }
  if (isVisibleNavigation('/inspection')) {
    items.splice(1, 0, {
      label: '连续检测',
      to: '/inspection',
      description: '选择工程并运行连续检测',
      current: route.path === '/inspection' || route.path.endsWith('/inspection')
    });
  }
  if (isVisibleNavigation('/ai')) {
    const boundProjectId = typeof route.params.id === 'string' ? route.params.id : null;
    const aiPath = boundProjectId ? `/projects/${encodeURIComponent(boundProjectId)}/ai` : '/ai';
    items.push({
      label: 'AI 工程工作台',
      to: aiPath,
      description: 'AI 工程工作台',
      current: route.path === '/ai' || route.path.endsWith('/ai')
    });
  }
  if (isVisibleNavigation('/settings')) {
    items.push({
      label: '设置',
      to: '/settings',
      description: '服务端配置投影与设置分组',
      current: route.path === '/settings'
    });
  }
  return Object.freeze(items);
});
const productMoreNavigation = computed<readonly Readonly<{ to: string; label: string }>[]>(() => Object.freeze(
  visibleNavigation.value
    .filter(item => ['/overview', '/operators', '/stations', '/diagnostics', '/about'].includes(item.to))
    .map(item => Object.freeze({ to: item.to, label: item.label }))
));
const statusTone = computed(() => {
  if (systemStatus.phase === 'online') return 'ok';
  if (systemStatus.phase === 'stale') return 'warning';
  if (systemStatus.phase === 'loading') return 'info';
  return 'error';
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
  appearanceOpen.value = false;
}

function closeMore(): void {
  moreOpen.value = false;
}

function closeUser(): void {
  userOpen.value = false;
}

function openAppearance(): void {
  closeMore();
  closeUser();
}

function openMore(): void {
  closeAppearance();
  closeUser();
}

function openUser(): void {
  closeAppearance();
  closeMore();
}

function selectAppearance(value: string): void {
  if (value === 'theme-light' || value === 'theme-dark') {
    runtime.preferences.setTheme(value === 'theme-dark' ? 'dark' : 'light');
  } else if (value === 'density-compact' || value === 'density-comfortable') {
    runtime.preferences.setDensity(value === 'density-comfortable' ? 'comfortable' : 'compact');
  }
}

function selectMore(value: string): void {
  if (productMoreNavigation.value.some(item => item.to === value)) void router.push(value);
}

function selectUser(value: string): void {
  if (value === 'change-password') {
    void router.push('/change-password');
  } else if (value === 'logout') {
    void authRoot.auth.logout();
  }
}

function focusContent(): void {
  contentRoot.value?.focus({ preventScroll: true });
}

watch(() => route.path, async (current, previous) => {
  if (!previous || current === previous) return;
  closeAppearance();
  closeMore();
  closeUser();
  await nextTick();
  contentRoot.value?.focus({ preventScroll: true });
});

onMounted(() => {
  runtime.preferences.apply();
});
</script>

<template>
  <div
    class="product-layout"
    :class="{ 'product-layout--workspace': workspaceMode }"
    data-product-shell="ready"
    :data-workspace-mode="workspaceMode"
    :data-leave-guard-phase="leaveGuard.projection.phase"
    :data-leave-guard-owner-count="leaveGuard.diagnostics.ownerCount"
    :data-project-command-phase="runtime.projectLifecycle?.projection.phase ?? 'unmounted'"
  >
    <a
      class="product-layout__skip-link"
      href="#product-main"
      @click.prevent="focusContent"
    >跳到主要内容</a>

    <div class="product-layout__workspace">
      <header class="product-layout__topbar">
        <div class="product-layout__workspace-chrome">
          <RouterLink
            class="product-layout__workspace-brand"
            to="/projects"
            aria-label="ClearVision Studio 工程"
          >
            <CvBrand />
          </RouterLink>
          <span
            class="product-layout__workspace-divider"
            aria-hidden="true"
          />
          <nav
            class="product-layout__workspace-nav"
            aria-label="产品主导航"
          >
            <template
              v-for="item in productTopNavigation"
              :key="item.label"
            >
              <RouterLink
                v-if="item.to && !item.current"
                class="product-layout__workspace-nav-item"
                :to="item.to"
                :data-product-nav="item.to"
                :title="item.description"
              >
                {{ item.label }}
              </RouterLink>
              <span
                v-else-if="item.current"
                class="product-layout__workspace-nav-item is-current"
                aria-current="page"
                :data-product-nav="item.to"
                :title="item.description"
              >{{ item.label }}</span>
              <button
                v-else
                type="button"
                class="product-layout__workspace-nav-item"
                disabled
                :title="item.description"
              >
                {{ item.label }}
              </button>
            </template>
          </nav>
        </div>

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

          <span
            class="product-layout__appearance"
            data-product-appearance
          >
            <CvMenu
              v-model="appearanceOpen"
              label="外观设置"
              :trigger-label="`外观设置，当前${themeLabel}主题，${densityLabel}密度`"
              align="end"
              @open="openAppearance"
              @select="selectAppearance"
            >
              <template #trigger>
                <CvIcon
                  name="theme"
                  size="sm"
                />
                <span class="product-layout__menu-trigger-label">外观</span>
                <small>{{ themeLabel }} · {{ densityLabel }}</small>
              </template>
              <span class="product-layout__menu-label">主题</span>
              <CvMenuItem
                value="theme-light"
                label="浅色"
                :checked="preferences.theme === 'light'"
              />
              <CvMenuItem
                value="theme-dark"
                label="深色"
                :checked="preferences.theme === 'dark'"
              />
              <span class="product-layout__menu-label">界面密度</span>
              <CvMenuItem
                value="density-compact"
                label="紧凑"
                :checked="preferences.density === 'compact'"
              />
              <CvMenuItem
                value="density-comfortable"
                label="舒适"
                :checked="preferences.density === 'comfortable'"
              />
            </CvMenu>
          </span>

          <span
            v-if="productMoreNavigation.length"
            class="product-layout__more"
            data-product-more
          >
            <CvMenu
              v-model="moreOpen"
              label="更多产品入口"
              trigger-label="更多产品入口"
              align="end"
              @open="openMore"
              @select="selectMore"
            >
              <template #trigger>
                <span class="product-layout__menu-trigger-label">更多</span>
                <CvIcon
                  name="chevron-right"
                  size="sm"
                />
              </template>
              <CvMenuItem
                v-for="item in productMoreNavigation"
                :key="item.to"
                :value="item.to"
                :label="item.label"
                :data-product-nav="item.to"
              >
                <template #leading>
                  <CvIcon
                    :name="navigationIcons[item.to] ?? 'overview'"
                    size="sm"
                  />
                </template>
              </CvMenuItem>
            </CvMenu>
          </span>

          <span
            class="product-layout__user-menu"
            data-product-user-menu
          >
            <CvMenu
              v-model="userOpen"
              label="会话菜单"
              :trigger-label="`会话菜单，${session.user?.username ?? '未认证'}，${roleLabel}`"
              align="end"
              @open="openUser"
              @select="selectUser"
            >
              <template #trigger>
                <span
                  class="product-layout__user-avatar"
                  aria-hidden="true"
                >{{ userInitial }}</span>
                <span class="product-layout__user-copy">
                  <strong>{{ session.user?.username ?? '未认证' }}</strong>
                  <small>{{ roleLabel }}</small>
                </span>
                <CvIcon
                  name="chevron-right"
                  size="sm"
                />
              </template>
              <CvMenuItem
                value="change-password"
                label="修改密码"
                data-session-command="change-password"
              >
                <template #leading>
                  <CvIcon
                    name="lock"
                    size="sm"
                  />
                </template>
              </CvMenuItem>
              <CvMenuItem
                value="logout"
                label="退出"
                tone="destructive"
                data-auth-command="logout"
              >
                <template #leading>
                  <CvIcon
                    name="power"
                    size="sm"
                  />
                </template>
              </CvMenuItem>
            </CvMenu>
          </span>
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
          title="离开操作已被保护"
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
            :key="routeViewKey"
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
