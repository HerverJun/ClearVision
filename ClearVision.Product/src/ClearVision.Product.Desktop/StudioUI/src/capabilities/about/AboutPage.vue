<script setup lang="ts">
import { computed } from 'vue';
import { useStudioPlatform } from '@/app/studioPlatform';
import {
  CvDescriptionList,
  CvPageHeader,
  CvPanel,
  type CvDescriptionItem
} from '@/design-system';
import { studioUiBuildMetadata } from '@/platform/diagnostics/buildMetadata';

const platform = useStudioPlatform();
const buildItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'frontend', label: '前端', value: `${studioUiBuildMetadata.name} ${studioUiBuildMetadata.version}` },
  { key: 'schema', label: '启动协议', value: `版本 ${platform.startup.schemaVersion}` },
  { key: 'host', label: '宿主类型', value: platform.startup.hostKind },
  { key: 'entry', label: '入口状态', value: 'StudioUiEnabled 默认关闭' },
  { key: 'auth', label: '认证范围', value: '预置会话 authenticated preview', span: 2 }
]);
</script>

<template>
  <section
    class="about-page"
    data-studio-page="about"
  >
    <CvPageHeader
      eyebrow="产品信息"
      title="关于 ClearVision Studio"
      description="面向工业视觉工程配置与调试的桌面平台。"
    />

    <CvPanel
      title="当前构建"
      description="当前前端构建与宿主启动范围。"
    >
      <CvDescriptionList
        :items="buildItems"
        label="当前构建信息"
      />
    </CvPanel>

    <CvPanel
      title="权威边界"
      description="界面只呈现既有后端权威，不建立第二套业务状态。"
    >
      <ul>
        <li>工程、流程、全局变量与正式资源仍由现有应用服务和保存协调器负责。</li>
        <li>执行、检测结果、运行包与 Station 仍由既有后端和现场链路负责。</li>
        <li>本阶段产品页面只发起冻结合同内的 GET 请求，不提供业务写操作。</li>
      </ul>
    </CvPanel>
  </section>
</template>

<style scoped>
.about-page { max-width: 960px; display: grid; min-width: 0; gap: var(--cv-space-5); }
.about-page ul { margin: 0; padding-left: var(--cv-space-5); }
.about-page li { margin-block: var(--cv-space-2); color: var(--cv-text-secondary); line-height: var(--cv-line-height-relaxed); }
</style>
