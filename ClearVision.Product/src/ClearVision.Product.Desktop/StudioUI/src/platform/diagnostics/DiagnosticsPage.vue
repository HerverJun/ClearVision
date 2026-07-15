<script setup lang="ts">
import { computed } from 'vue';
import { useProductRuntime } from '@/app/productRuntime';
import { useStudioPlatform } from '@/app/studioPlatform';
import { CvButton, CvStatusBadge } from '@/design-system/primitives';
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

async function refreshSharedOwners(): Promise<void> {
  await Promise.all([
    runtime.systemStatus.refresh(),
    runtime.session.refresh()
  ]);
}
</script>

<template>
  <article
    class="diagnostics-page"
    data-studio-page="diagnostics"
  >
    <header class="diagnostics-page__header">
      <div>
        <p>运行诊断</p>
        <h1>StudioUI 诊断</h1>
        <span>只消费唯一 session、system status 与 query owner 的共享投影。</span>
      </div>
      <CvButton
        variant="secondary"
        @click="refreshSharedOwners"
      >
        刷新共享状态
      </CvButton>
    </header>

    <section class="diagnostics-page__summary">
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
    </section>

    <section class="diagnostics-page__section">
      <h2>启动与宿主</h2>
      <dl>
        <div><dt>构建</dt><dd>{{ studioUiBuildMetadata.name }} {{ studioUiBuildMetadata.version }}</dd></div>
        <div><dt>schemaVersion</dt><dd>{{ platform.startup.schemaVersion }}</dd></div>
        <div><dt>uiKind</dt><dd>{{ platform.startup.uiKind }}</dd></div>
        <div><dt>hostKind</dt><dd>{{ platform.startup.hostKind }}</dd></div>
        <div><dt>StudioUI base</dt><dd>{{ platform.startup.studioUiBasePath }}</dd></div>
        <div><dt>API origin</dt><dd>{{ apiOrigin }}</dd></div>
        <div><dt>Host channel</dt><dd>{{ hostDiagnostics.channel }}</dd></div>
        <div><dt>Token present</dt><dd>{{ platform.hasToken() ? '是' : '否' }}</dd></div>
      </dl>
    </section>

    <section class="diagnostics-page__section">
      <h2>唯一 owner 状态</h2>
      <dl>
        <div><dt>Session generation</dt><dd>{{ queryDiagnostics.sessionGeneration }}</dd></div>
        <div><dt>Query owner</dt><dd>{{ queryDiagnostics.activeOwnerCount }}</dd></div>
        <div><dt>Active request</dt><dd>{{ queryDiagnostics.activeRequestCount }}</dd></div>
        <div><dt>Cache entry</dt><dd>{{ queryDiagnostics.cacheEntryCount }}</dd></div>
        <div><dt>Protected cache</dt><dd>{{ queryDiagnostics.protectedCacheEntryCount }}</dd></div>
        <div><dt>System port</dt><dd>{{ systemStatus.health?.port ?? '不可用' }}</dd></div>
        <div><dt>Session user</dt><dd>{{ session.user?.username ?? '未认证' }}</dd></div>
        <div><dt>Session role</dt><dd>{{ session.user?.role ?? '无' }}</dd></div>
      </dl>
    </section>

    <aside class="diagnostics-page__scope">
      <strong>证据范围</strong>
      <span>当前阶段是预置会话的 authenticated preview，不代表真实登录 handoff、默认入口切换或现场执行链路已经迁移。</span>
    </aside>
  </article>
</template>

<style scoped>
.diagnostics-page { max-width: 980px; display: grid; gap: var(--cv-space-4); }
.diagnostics-page__header { display: flex; align-items: flex-end; justify-content: space-between; gap: var(--cv-space-4); }
.diagnostics-page__header p { margin: 0; color: var(--cv-color-brand-text); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.diagnostics-page__header h1 { margin: var(--cv-space-1) 0; font-size: var(--cv-font-size-2xl); }
.diagnostics-page__header span { color: var(--cv-text-secondary); }
.diagnostics-page__summary { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-3); }
.diagnostics-page__summary > div,
.diagnostics-page__section { padding: var(--cv-space-4); border: 1px solid var(--cv-border-default); border-radius: var(--cv-radius-md); background: var(--cv-surface-1); }
.diagnostics-page__summary > div { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.diagnostics-page__summary > div > span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.diagnostics-page__section h2 { margin: 0 0 var(--cv-space-3); font-size: var(--cv-font-size-lg); }
.diagnostics-page__section dl { margin: 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1px; background: var(--cv-border-subtle); }
.diagnostics-page__section dl div { min-width: 0; padding: var(--cv-space-2) var(--cv-space-3); display: grid; grid-template-columns: minmax(120px, .7fr) minmax(0, 1.3fr); background: var(--cv-surface-1); }
.diagnostics-page__section dt { color: var(--cv-text-secondary); }
.diagnostics-page__section dd { min-width: 0; margin: 0; overflow-wrap: anywhere; }
.diagnostics-page__scope { padding: var(--cv-space-3) var(--cv-space-4); display: grid; gap: var(--cv-space-1); border-left: 3px solid var(--cv-color-status-info); background: var(--cv-color-status-info-soft); }
.diagnostics-page__scope span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-relaxed); }
@media (max-width: 760px) {
  .diagnostics-page__summary,
  .diagnostics-page__section dl { grid-template-columns: 1fr; }
  .diagnostics-page__header { align-items: flex-start; flex-direction: column; }
}
</style>
