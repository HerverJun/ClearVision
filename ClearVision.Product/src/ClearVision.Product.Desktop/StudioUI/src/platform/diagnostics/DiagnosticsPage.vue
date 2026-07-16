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
  { key: 'build', label: '构建', value: `${studioUiBuildMetadata.name} ${studioUiBuildMetadata.version}` },
  { key: 'schema', label: '启动协议版本', value: platform.startup.schemaVersion },
  { key: 'ui-kind', label: '界面类型', value: platform.startup.uiKind },
  { key: 'host-kind', label: '宿主类型', value: platform.startup.hostKind },
  { key: 'base', label: '界面基础路径', value: platform.startup.studioUiBasePath },
  { key: 'api-origin', label: '接口来源', value: apiOrigin },
  { key: 'host-channel', label: '宿主通道', value: hostDiagnostics.channel },
  { key: 'token', label: '会话令牌', value: platform.hasToken() ? '已提供' : '未提供' }
]);
const ownerItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'generation', label: '会话代次', value: queryDiagnostics.value.sessionGeneration },
  { key: 'owners', label: '活动查询所有者', value: queryDiagnostics.value.activeOwnerCount },
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
      eyebrow="运行诊断"
      title="StudioUI 诊断"
      description="查看统一会话、系统状态与只读查询服务的共享状态。"
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
      description="系统状态与会话状态由全应用唯一所有者提供。"
    >
      <div class="diagnostics-page__summary">
        <div>
          <span>本地服务</span>
          <CvStatusBadge
            :tone="systemStatus.phase === 'online' ? 'ok' : systemStatus.phase === 'stale' ? 'warning' : 'ng'"
            :label="systemStatus.message"
            :data-probe-state="healthProbeState"
          />
        </div>
        <div>
          <span>当前会话</span>
          <CvStatusBadge
            :tone="session.phase === 'authenticated' ? 'ok' : session.phase === 'stale' ? 'warning' : 'ng'"
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
        title="统一查询状态"
        description="用于确认活动请求、缓存与会话代次没有出现第二套所有者。"
      >
        <CvDescriptionList
          :items="ownerItems"
          label="统一查询状态"
        />
      </CvPanel>
    </div>

    <CvInlineAlert
      tone="info"
      title="证据范围"
    >
      当前阶段使用预置会话进行认证预览，不代表真实登录交接、默认入口切换或现场执行链路已经迁移。
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
