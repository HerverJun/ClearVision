<script setup lang="ts">
import { computed, shallowRef } from 'vue';
import { useProductRuntime } from '@/app/productRuntime';
import { useStudioPlatform } from '@/app/studioPlatform';
import {
  CvButton,
  CvDescriptionList,
  CvIcon,
  CvInlineAlert,
  CvPageHeader,
  CvPanel,
  CvStatusBadge,
  type CvDescriptionItem
} from '@/design-system';
import { studioUiBuildMetadata } from '@/platform/diagnostics/buildMetadata';

const platform = useStudioPlatform();
const runtime = useProductRuntime();
const session = runtime.session.projection;
const systemStatus = runtime.systemStatus.projection;
const hostDiagnostics = platform.host.getDiagnostics();
const apiOrigin = new URL(platform.startup.apiBaseUrl).origin;
const copyState = shallowRef<'idle' | 'success' | 'error'>('idle');
const queryDiagnostics = computed(() => runtime.queries.getDiagnostics());
const healthProbeState = computed(() => systemStatus.phase === 'online' ? 'ok' : 'error');
const sessionProbeState = computed(() =>
  session.phase === 'authenticated' || session.phase === 'stale' ? 'ok' : 'error'
);
const environmentLabel = computed(() => platform.startup.hostKind === 'desktop-webview2'
  ? 'Windows 桌面宿主'
  : '浏览器测试环境');
const supportItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'product-version', label: '产品版本', value: platform.startup.productVersion ?? '宿主未提供' },
  { key: 'build', label: '界面版本', value: studioUiBuildMetadata.version },
  { key: 'host-version', label: '桌面宿主版本', value: platform.startup.hostVersion ?? '浏览器环境不可用' },
  { key: 'backend-version', label: '本地服务版本', value: systemStatus.health?.version ?? '尚未读取' },
  { key: 'environment', label: '运行环境', value: environmentLabel.value },
  { key: 'mode', label: '启动模式', value: formatStartupMode(platform.startup.startupProfile) }
]);
const startupItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'schema', label: '启动协议版本', value: platform.startup.schemaVersion },
  {
    key: 'profile-roles',
    label: '可用角色',
    value: platform.startup.profileAllowedRoles.map(formatRole).join('、')
  },
  { key: 'ui-kind', label: '界面类型', value: 'Studio 工程界面' },
  { key: 'host-kind', label: '宿主类型', value: environmentLabel.value },
  { key: 'base', label: '界面基础路径', value: platform.startup.studioUiBasePath },
  { key: 'api-origin', label: '接口来源', value: apiOrigin },
  { key: 'host-channel', label: '宿主通道', value: hostDiagnostics.channel },
  { key: 'token', label: '会话令牌', value: platform.hasToken() ? '已提供' : '未提供' }
]);
const ownerItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'generation', label: '会话代次', value: queryDiagnostics.value.sessionGeneration },
  { key: 'owners', label: '活动查询组', value: queryDiagnostics.value.activeOwnerCount },
  { key: 'requests', label: '活动请求', value: queryDiagnostics.value.activeRequestCount },
  { key: 'cache', label: '缓存条目', value: queryDiagnostics.value.cacheEntryCount },
  { key: 'protected-cache', label: '受保护缓存', value: queryDiagnostics.value.protectedCacheEntryCount },
  { key: 'port', label: '系统端口', value: systemStatus.health?.port ?? '不可用' },
  { key: 'user', label: '会话用户', value: session.user?.username ?? '未认证' },
  { key: 'role', label: '会话角色', value: formatRole(session.user?.role) }
]);
const diagnosticText = computed(() => JSON.stringify({
  product: {
    productVersion: platform.startup.productVersion ?? null,
    interfaceVersion: studioUiBuildMetadata.version,
    hostVersion: platform.startup.hostVersion ?? null,
    serviceVersion: systemStatus.health?.version ?? null
  },
  environment: {
    hostKind: platform.startup.hostKind,
    startupMode: platform.startup.startupProfile,
    schemaVersion: platform.startup.schemaVersion,
    basePath: platform.startup.studioUiBasePath,
    apiOrigin,
    host: hostDiagnostics
  },
  service: {
    phase: systemStatus.phase,
    message: systemStatus.message,
    health: systemStatus.health
  },
  session: {
    phase: session.phase,
    message: session.message,
    username: session.user?.username ?? null,
    role: session.user?.role ?? null,
    tokenPresent: platform.hasToken()
  },
  queries: queryDiagnostics.value
}, null, 2));

function formatRole(value: string | undefined): string {
  if (!value) return '无';
  const labels: Readonly<Record<string, string>> = Object.freeze({
    admin: '管理员',
    engineer: '工程师',
    operator: '操作员',
    viewer: '查看者'
  });
  return labels[value.toLocaleLowerCase()] ?? value;
}

function formatStartupMode(value: string): string {
  if (value === 'LEGACY_FALLBACK') return '兼容回退';
  if (value === 'NEXT_DEFAULT') return '标准模式';
  if (value.includes('PILOT')) return '受控试用';
  if (value.includes('CANDIDATE')) return '候选模式';
  return '受控启动';
}

async function refreshSharedOwners(): Promise<void> {
  copyState.value = 'idle';
  await Promise.all([
    runtime.systemStatus.refresh(),
    runtime.session.refresh()
  ]);
}

async function copyDiagnostics(): Promise<void> {
  try {
    await navigator.clipboard.writeText(diagnosticText.value);
    copyState.value = 'success';
  } catch {
    copyState.value = 'error';
  }
}
</script>

<template>
  <section
    class="diagnostics-page"
    data-studio-page="diagnostics"
  >
    <CvPageHeader
      title="运行诊断"
      description="确认 Studio、本地服务和当前会话是否可以正常协同。"
    >
      <template #actions>
        <CvButton
          size="sm"
          variant="quiet"
          @click="copyDiagnostics"
        >
          <template #leading>
            <CvIcon name="copy" />
          </template>
          {{ copyState === 'success' ? '已复制' : '复制诊断信息' }}
        </CvButton>
        <CvButton
          size="sm"
          :loading="systemStatus.phase === 'loading' || session.phase === 'loading'"
          loading-label="正在刷新诊断状态"
          @click="refreshSharedOwners"
        >
          <template #leading>
            <CvIcon name="refresh" />
          </template>
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <CvPanel
      title="当前状态"
      description="最近一次确认结果；异常状态会同时给出文字说明。"
    >
      <div class="diagnostics-page__status-list">
        <div>
          <span><strong>本地服务</strong><small>工程数据与运行接口</small></span>
          <CvStatusBadge
            :tone="systemStatus.phase === 'online' ? 'ok' : systemStatus.phase === 'stale' ? 'warning' : 'error'"
            :label="systemStatus.message"
            :data-probe-state="healthProbeState"
          />
        </div>
        <div>
          <span><strong>当前会话</strong><small>{{ session.user?.username ?? '尚未确认用户' }}</small></span>
          <CvStatusBadge
            :tone="session.phase === 'authenticated' ? 'ok' : session.phase === 'stale' ? 'warning' : 'error'"
            :label="session.message"
            :data-probe-state="sessionProbeState"
          />
        </div>
        <div>
          <span><strong>桌面宿主</strong><small>窗口与本机能力</small></span>
          <CvStatusBadge
            :tone="hostDiagnostics.disposed ? 'error' : 'ok'"
            :label="hostDiagnostics.disposed ? '宿主连接已停止' : environmentLabel"
          />
        </div>
      </div>
    </CvPanel>

    <CvInlineAlert
      v-if="systemStatus.phase !== 'online'"
      :tone="systemStatus.phase === 'stale' ? 'warning' : 'error'"
      title="本地服务需要处理"
    >
      {{ systemStatus.message }} 请确认本地服务已启动，然后刷新状态。
    </CvInlineAlert>

    <CvPanel
      title="版本与环境"
      description="用于确认 Studio 各组成部分是否来自同一套发布。"
    >
      <CvDescriptionList
        :items="supportItems"
        label="版本与运行环境摘要"
      />
    </CvPanel>

    <details class="diagnostics-page__technical">
      <summary>
        <span>技术诊断</span>
        <small>启动协议、接口来源和查询资源</small>
      </summary>
      <div class="diagnostics-page__details">
        <CvPanel
          title="启动与宿主"
          description="供现场支持定位启动配置和宿主通道。"
          variant="tool"
        >
          <CvDescriptionList
            :items="startupItems"
            label="启动与宿主诊断"
          />
        </CvPanel>

        <CvPanel
          title="查询资源"
          description="供现场支持检查请求、缓存与会话刷新状态。"
          variant="tool"
        >
          <CvDescriptionList
            :items="ownerItems"
            label="查询资源状态"
          />
        </CvPanel>
      </div>
    </details>

    <p
      class="diagnostics-page__copy-status"
      :data-tone="copyState"
      role="status"
      aria-live="polite"
    >
      <template v-if="copyState === 'success'">
        诊断信息已复制，可粘贴给技术支持。
      </template>
      <template v-else-if="copyState === 'error'">
        无法访问系统剪贴板，请展开技术诊断后手动记录。
      </template>
      <template v-else>
        复制内容不包含密码或会话令牌。
      </template>
    </p>

    <CvInlineAlert
      v-if="platform.startup.hostKind === 'browser-test'"
      tone="info"
      title="测试环境"
    >
      当前页面运行在浏览器测试环境，宿主与设备信息仅用于界面验证。
    </CvInlineAlert>
  </section>
</template>

<style scoped>
.diagnostics-page { max-width: 1180px; display: grid; min-width: 0; gap: var(--cv-space-5); }
.diagnostics-page__status-list { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); border-block: 1px solid var(--cv-border-subtle); }
.diagnostics-page__status-list > div { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-space-3); border-right: 1px solid var(--cv-border-subtle); }
.diagnostics-page__status-list > div:last-child { border-right: 0; }
.diagnostics-page__status-list > div > span { display: grid; min-width: 0; gap: 2px; }
.diagnostics-page__status-list strong { font-size: var(--cv-font-size-sm); }
.diagnostics-page__status-list small { overflow: hidden; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.diagnostics-page__technical { border-top: 1px solid var(--cv-border-subtle); }
.diagnostics-page__technical > summary { display: flex; align-items: baseline; gap: var(--cv-space-3); padding: var(--cv-space-3) 0; color: var(--cv-color-link); cursor: pointer; font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); list-style-position: inside; }
.diagnostics-page__technical > summary small { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-regular); }
.diagnostics-page__technical > summary:focus-visible { border-radius: var(--cv-radius-xs); outline: none; box-shadow: var(--cv-focus-ring); }
.diagnostics-page__details { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); align-items: start; }
.diagnostics-page__copy-status { min-height: 20px; margin: calc(var(--cv-space-3) * -1) 0 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.diagnostics-page__copy-status[data-tone="success"] { color: var(--cv-color-status-ok-strong); }
.diagnostics-page__copy-status[data-tone="error"] { color: var(--cv-color-status-error-strong); }
@media (max-width: 760px) {
  .diagnostics-page__status-list,
  .diagnostics-page__details { grid-template-columns: 1fr; }
  .diagnostics-page__status-list > div { border-right: 0; border-bottom: 1px solid var(--cv-border-subtle); }
  .diagnostics-page__status-list > div:last-child { border-bottom: 0; }
  .diagnostics-page__technical > summary { align-items: flex-start; flex-direction: column; gap: var(--cv-space-1); }
}
</style>
