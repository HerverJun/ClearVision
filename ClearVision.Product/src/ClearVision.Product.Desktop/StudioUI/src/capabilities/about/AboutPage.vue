<script setup lang="ts">
import { computed } from 'vue';
import { useProductRuntime } from '@/app/productRuntime';
import { useStudioPlatform } from '@/app/studioPlatform';
import {
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
const systemStatus = runtime.systemStatus.projection;
const buildItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'product', label: '产品版本', value: platform.startup.productVersion ?? '未由桌面宿主提供' },
  { key: 'frontend', label: '界面版本', value: studioUiBuildMetadata.version },
  { key: 'host-version', label: '桌面宿主版本', value: platform.startup.hostVersion ?? '当前环境不适用' },
  { key: 'backend-version', label: '本地服务版本', value: systemStatus.health?.version ?? '尚未读取' },
  { key: 'mode', label: '启动模式', value: formatStartupMode(platform.startup.startupProfile) },
  { key: 'host', label: '运行环境', value: platform.startup.hostKind === 'desktop-webview2' ? 'Windows 桌面宿主' : '浏览器测试环境' },
  { key: 'service-state', label: '本地服务', value: systemStatus.message },
  { key: 'auth', label: '账号验证', value: '由本地服务统一管理' }
]);

const supportItems: readonly CvDescriptionItem[] = Object.freeze([
  { key: 'license', label: '产品许可证', value: '界面未收到许可证摘要，请以交付记录为准' },
  { key: 'third-party', label: '第三方许可', value: '以安装包随附的许可清单为准' },
  { key: 'support', label: '技术支持', value: '请联系系统管理员或实施交付方', span: 2 }
]);

function formatStartupMode(value: string): string {
  if (value === 'LEGACY_FALLBACK') return '兼容回退';
  if (value === 'NEXT_DEFAULT') return '标准模式';
  if (value.includes('PILOT')) return '受控试用';
  if (value.includes('CANDIDATE')) return '候选模式';
  return '受控启动';
}
</script>

<template>
  <section
    class="about-page"
    data-studio-page="about"
  >
    <CvPageHeader
      eyebrow="产品信息"
      title="关于 ClearVision Studio"
      description="工业视觉工程配置、调试与现场协同平台。"
    />

    <CvPanel
      title="产品与版本"
      description="当前进程实际读取到的界面、宿主和本地服务信息。"
    >
      <template #actions>
        <CvStatusBadge
          :tone="systemStatus.phase === 'online' ? 'ok' : systemStatus.phase === 'stale' ? 'warning' : 'error'"
          :label="systemStatus.message"
        />
      </template>
      <CvDescriptionList
        :items="buildItems"
        label="当前构建信息"
      />
    </CvPanel>

    <CvPanel
      title="许可与支持"
      description="发布和现场支持信息。"
    >
      <CvDescriptionList
        :items="supportItems"
        label="许可与支持信息"
      />
    </CvPanel>

    <CvInlineAlert
      tone="info"
      title="产品组成"
    >
      Studio 用于工程配置与调试；正式检测由 Runtime 和现场工作站承载。
    </CvInlineAlert>
  </section>
</template>

<style scoped>
.about-page { max-width: 1040px; display: grid; min-width: 0; gap: var(--cv-space-5); }
</style>
