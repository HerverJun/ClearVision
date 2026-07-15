<script setup lang="ts">
import { useStudioPlatform } from '@/app/studioPlatform';
import { studioUiBuildMetadata } from '@/platform/diagnostics/buildMetadata';

const platform = useStudioPlatform();
</script>

<template>
  <article
    class="about-page"
    data-studio-page="about"
  >
    <header>
      <p>产品信息</p>
      <h1>关于 ClearVision Studio</h1>
      <span>面向工业视觉工程配置与调试的桌面平台。</span>
    </header>
    <section>
      <h2>当前构建</h2>
      <dl>
        <div><dt>前端</dt><dd>{{ studioUiBuildMetadata.name }} {{ studioUiBuildMetadata.version }}</dd></div>
        <div><dt>启动合同</dt><dd>schemaVersion {{ platform.startup.schemaVersion }}</dd></div>
        <div><dt>宿主</dt><dd>{{ platform.startup.hostKind }}</dd></div>
        <div><dt>入口状态</dt><dd>StudioUiEnabled 默认关闭</dd></div>
        <div><dt>认证范围</dt><dd>预置会话 authenticated preview</dd></div>
      </dl>
    </section>
    <section>
      <h2>权威边界</h2>
      <ul>
        <li>Project、Flow、GlobalVariables 与 assets 仍由现有 Application Service 和保存协调器负责。</li>
        <li>执行、检测结果、Runtime Package 与 Station 仍由既有后端和现场链路负责。</li>
        <li>本阶段产品页面只发起冻结合同内的 GET 请求，不提供业务写操作。</li>
      </ul>
    </section>
  </article>
</template>

<style scoped>
.about-page { max-width: 900px; display: grid; gap: var(--cv-space-4); }
.about-page header p { margin: 0; color: var(--cv-color-brand-text); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.about-page header h1 { margin: var(--cv-space-1) 0; font-size: var(--cv-font-size-2xl); }
.about-page header span { color: var(--cv-text-secondary); }
.about-page section { padding: var(--cv-space-5); border: 1px solid var(--cv-border-default); border-radius: var(--cv-radius-md); background: var(--cv-surface-1); }
.about-page h2 { margin: 0 0 var(--cv-space-3); font-size: var(--cv-font-size-lg); }
.about-page dl { margin: 0; display: grid; gap: 1px; background: var(--cv-border-subtle); }
.about-page dl div { padding: var(--cv-space-2) var(--cv-space-3); display: grid; grid-template-columns: 160px 1fr; background: var(--cv-surface-1); }
.about-page dt { color: var(--cv-text-secondary); }
.about-page dd { margin: 0; }
.about-page li { margin-block: var(--cv-space-2); color: var(--cv-text-secondary); line-height: var(--cv-line-height-relaxed); }
</style>
