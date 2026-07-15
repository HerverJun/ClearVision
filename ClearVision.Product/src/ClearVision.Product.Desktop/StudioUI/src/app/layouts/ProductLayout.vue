<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { RouterLink, RouterView, useRoute } from 'vue-router';
import { productNavigation } from '@/app/navigation';
import { useProductRuntime } from '@/app/productRuntime';
import { CvButton, CvInlineAlert, CvStatusBadge } from '@/design-system/primitives';
import './product-layout.css';

const route = useRoute();
const runtime = useProductRuntime();
const session = runtime.session.projection;
const systemStatus = runtime.systemStatus.projection;
const preferences = runtime.preferences.projection;

const breadcrumbs = computed(() => route.matched
  .filter(record => !record.meta.internal && record.meta.breadcrumb)
  .map(record => ({
    label: record.meta.breadcrumb,
    to: record.path === '/' ? '/overview' : record.path
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

onMounted(() => runtime.preferences.apply());
</script>

<template>
  <div
    class="product-layout"
    data-product-shell="ready"
  >
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
          <small>工业视觉 Studio</small>
        </span>
      </RouterLink>

      <nav aria-label="产品主导航">
        <RouterLink
          v-for="item in productNavigation"
          :key="item.to"
          :to="item.to"
          class="product-layout__nav-item"
          :data-product-nav="item.to"
        >
          <span>{{ item.label }}</span>
          <small>{{ item.description }}</small>
        </RouterLink>
      </nav>

      <div class="product-layout__sidebar-note">
        <strong>只读工作区</strong>
        <span>正式保存、执行与现场控制继续由现有后端权威链路负责。</span>
      </div>
    </aside>

    <div class="product-layout__workspace">
      <header class="product-layout__topbar">
        <nav
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
          <CvStatusBadge
            :tone="statusTone"
            :label="statusLabel"
          />
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
          <div class="product-layout__user">
            <strong>{{ session.user?.username ?? '未认证' }}</strong>
            <small>{{ roleLabel }}</small>
          </div>
        </div>
      </header>

      <main class="product-layout__content">
        <CvInlineAlert
          v-if="session.phase === 'unauthorized'"
          class="product-layout__session-alert"
          tone="warning"
          title="需要预置会话"
          data-product-state="unauthorized"
        >
          {{ session.message }} 当前只支持宿主预置会话，不提供新的登录跳转或首次启动流程。
          <template #actions>
            <CvButton
              size="sm"
              variant="secondary"
              @click="runtime.session.refresh()"
            >
              重新检查
            </CvButton>
          </template>
        </CvInlineAlert>
        <CvInlineAlert
          v-else-if="session.phase === 'error'"
          class="product-layout__session-alert"
          tone="error"
          title="会话读取失败"
          data-product-state="error"
        >
          {{ session.message }} 页面仍会挂载，但后端授权继续是唯一安全边界。
        </CvInlineAlert>
        <CvInlineAlert
          v-else-if="session.phase === 'stale'"
          class="product-layout__session-alert"
          tone="warning"
          title="会话投影已过期"
          data-product-state="stale"
        >
          {{ session.message }}
        </CvInlineAlert>
        <RouterView />
      </main>
    </div>
  </div>
</template>
