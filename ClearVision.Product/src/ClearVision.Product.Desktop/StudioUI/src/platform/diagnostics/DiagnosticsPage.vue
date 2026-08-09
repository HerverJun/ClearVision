<script setup lang="ts">
import { computed } from 'vue';
import { useProductRuntime } from '@/app/productRuntime';
import { useStudioPlatform } from '@/app/studioPlatform';
import {
  CvButton,
  CvDescriptionList,
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
const queryDiagnostics = computed(() => runtime.queries.getDiagnostics());
const healthProbeState = computed(() => systemStatus.phase === 'online' ? 'ok' : 'error');
const sessionProbeState = computed(() =>
  session.phase === 'authenticated' || session.phase === 'stale' ? 'ok' : 'error'
);
const startupItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'product-version', label: '产品版本', value: platform.startup.productVersion ?? '宿主未提供' },
  { key: 'build', label: '界面版本', value: `${studioUiBuildMetadata.name} ${studioUiBuildMetadata.version}` },
  { key: 'host-version', label: '桌面宿主版本', value: platform.startup.hostVersion ?? '浏览器环境不可用' },
  { key: 'backend-version', label: '本地服务版本', value: systemStatus.health?.version ?? '尚未读取' },
  { key: 'schema', label: '启动协议版本', value: platform.startup.schemaVersion },
  { key: 'profile', label: '启动模式', value: formatStartupMode(platform.startup.startupProfile) },
  {
    key: 'profile-roles',
    label: '可用角色',
    value: platform.startup.profileAllowedRoles.map(formatRole).join('、')
  },
  { key: 'ui-kind', label: '界面类型', value: 'Studio 工程界面' },
  { key: 'host-kind', label: '运行环境', value: platform.startup.hostKind === 'desktop-webview2' ? 'Windows 桌面宿主' : '浏览器测试环境' },
  { key: 'default-entry', label: '默认入口', value: '工程库（/projects）' },
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
  await Promise.all([
    runtime.systemStatus.refresh(),
    runtime.session.refresh()
  ]);
}
</script>

<template>
  <section
    class="diagnostics-page"
    data-studio-page="diagnostics"
  >
    <CvPageHeader
      title="运行诊断"
      description="查看本地服务、当前会话、版本与查询资源状态。"
    >
      <template #actions>
        <CvButton
          size="sm"
          @click="refreshSharedOwners"
        >
          刷新共享状态
        </CvButton>
      </template>
    </CvPageHeader>

    <CvPanel
      title="共享状态"
      description="本地服务与当前会话的最近一次确认结果。"
    >
      <div class="diagnostics-page__summary">
        <div>
          <span>本地服务</span>
          <CvStatusBadge
            :tone="systemStatus.phase === 'online' ? 'ok' : systemStatus.phase === 'stale' ? 'warning' : 'error'"
            :label="systemStatus.message"
            :data-probe-state="healthProbeState"
          />
        </div>
        <div>
          <span>当前会话</span>
          <CvStatusBadge
            :tone="session.phase === 'authenticated' ? 'ok' : session.phase === 'stale' ? 'warning' : 'error'"
            :label="session.message"
            :data-probe-state="sessionProbeState"
          />
        </div>
      </div>
    </CvPanel>

    <div class="diagnostics-page__details">
      <CvPanel
        title="启动与宿主"
        description="当前页面使用的启动配置与宿主适配信息。"
      >
        <CvDescriptionList
          :items="startupItems"
          label="启动与宿主诊断"
        />
      </CvPanel>

      <CvPanel
        title="查询资源"
        description="用于排查活动请求、缓存与会话刷新状态。"
      >
        <CvDescriptionList
          :items="ownerItems"
          label="查询资源状态"
        />
      </CvPanel>
    </div>

    <CvInlineAlert
      tone="info"
      title="诊断范围"
    >
      浏览器测试环境不能替代真实桌面宿主、Windows 缩放或现场设备验证。
    </CvInlineAlert>
  </section>
</template>

<style scoped>
.diagnostics-page { max-width: 1120px; display: grid; min-width: 0; gap: var(--cv-space-5); }
.diagnostics-page__summary { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-3); }
.diagnostics-page__summary > div { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-space-3); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-2); }
.diagnostics-page__summary > div > span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.diagnostics-page__details { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); align-items: start; }
@media (max-width: 760px) {
  .diagnostics-page__summary,
  .diagnostics-page__details { grid-template-columns: 1fr; }
}
</style>
