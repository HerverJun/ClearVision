<script setup lang="ts">
import { computed, shallowRef } from 'vue';
import { RouterLink } from 'vue-router';
import {
  CvIcon,
  CvIconButton,
  CvInlineAlert,
  CvPanel,
  CvStatusBadge
} from '@/design-system';
import { formatInspectionOutcome } from '@/shared/inspectionOutcome';
import { createStationResultsDeepLink } from '@/shared/productionTraceLinks';
import type {
  StationAudit,
  StationCommand,
  StationPackage,
  StationResult,
  StationStatus
} from './stationContracts';
import {
  projectStationProductionTrace,
  summarizeTraceIdentity,
  type StationAdminEvidenceState
} from './stationProductionTrace';
import { formatStationDateTime } from './stationViewModel';

const props = defineProps<{
  station: StationStatus;
  results: readonly StationResult[];
  commands: readonly StationCommand[];
  audits: readonly StationAudit[];
  packages: readonly StationPackage[];
  adminEvidence: StationAdminEvidenceState;
  canOpenProject: boolean;
}>();

const copiedIdentity = shallowRef<string | null>(null);
const projection = computed(() => projectStationProductionTrace(props));
const status = computed(() => ({
  complete: { tone: 'ok' as const, label: '身份闭合' },
  partial: { tone: 'warning' as const, label: '身份不完整' },
  mismatch: { tone: 'error' as const, label: '身份不一致' }
})[projection.value.phase]);
const outcome = computed(() => projection.value.latestResult
  ? formatInspectionOutcome(projection.value.latestResult.outcome)
  : null);
const projectHref = computed(() => props.canOpenProject && projection.value.projectId
  ? `/projects/${encodeURIComponent(projection.value.projectId)}/workspace`
  : null);
const resultHref = computed(() => projection.value.latestResult
  ? createStationResultsDeepLink({
    stationId: props.station.stationId,
    resultId: projection.value.latestResult.messageId,
    returnTo: `/stations/${encodeURIComponent(props.station.stationId)}`
  })
  : null);
const hashItems = computed(() => [
  { key: 'package', label: '运行包 SHA-256', value: props.station.packageSha256 ?? projection.value.activePackage?.sha256 ?? null },
  { key: 'flow', label: '执行流程哈希', value: props.station.executionFlowHash ?? props.station.packageFlowHash },
  { key: 'decision', label: '判定配置哈希', value: props.station.decisionConfigurationHash }
]);
const mismatchItems = computed(() => {
  const occurrences = new Map<string, number>();
  return projection.value.mismatches.map(message => {
    const occurrence = (occurrences.get(message) ?? 0) + 1;
    occurrences.set(message, occurrence);
    return Object.freeze({ key: `${message}\u0000${occurrence}`, message });
  });
});

async function copyIdentity(key: string, value: string | null): Promise<void> {
  if (!value) return;
  try {
    await navigator.clipboard.writeText(value);
    copiedIdentity.value = key;
  } catch {
    copiedIdentity.value = null;
  }
}
</script>

<template>
  <CvPanel
    class="production-trace"
    title="生产追溯链"
    description="串联来源工程、正式运行包、部署记录、当前运行身份和最近结果。"
    data-testid="station-production-trace"
  >
    <div class="production-trace__heading">
      <CvStatusBadge :tone="status.tone">
        {{ status.label }}
      </CvStatusBadge>
      <span>{{ station.stationId }}</span>
    </div>

    <ol class="production-trace__steps">
      <li>
        <span>1</span>
        <div><small>来源工程</small><strong>{{ projection.projectId || '身份未上报' }}</strong><em>{{ projection.projectRevision === null ? '修订未上报' : `正式保存 r${projection.projectRevision}` }}</em></div>
        <RouterLink
          v-if="projectHref"
          class="production-trace__nav-link"
          :to="projectHref"
        >
          打开工作区
        </RouterLink>
        <span
          v-else-if="projection.projectId"
          class="production-trace__restricted"
        >当前角色不可进入工作区</span>
      </li>
      <li>
        <span>2</span>
        <div><small>正式运行包</small><strong>{{ station.packageName || '运行包名称未上报' }}<template v-if="station.packageVersion"> · {{ station.packageVersion }}</template></strong><em>{{ station.packageId || '当前激活运行包未上报' }}</em></div>
      </li>
      <li>
        <span>3</span>
        <div>
          <small>部署命令与审计</small>
          <strong>{{ projection.deploymentCommand ? `${projection.deploymentCommand.issuedBy || '系统'} · ${formatStationDateTime(projection.deploymentCommand.createdAtUtc)}` : '当前窗口无可关联命令' }}</strong>
          <em>{{ projection.deploymentCommand ? `${projection.deploymentCommand.commandId} · ${projection.deploymentCommand.status}` : adminEvidence === 'restricted' ? '当前账号无权查看命令详情' : '未找到可确认的命令关联' }}</em>
          <em v-if="projection.deploymentAudit">审计 {{ projection.deploymentAudit.auditId }} · {{ projection.deploymentAudit.userName || '系统' }} · {{ projection.deploymentAudit.result || '结果未记录' }} · {{ formatStationDateTime(projection.deploymentAudit.createdAtUtc) }}</em>
        </div>
      </li>
      <li>
        <span>4</span>
        <div><small>工作站当前身份 / 运行</small><strong>{{ station.isOnline ? '服务端状态已读取' : '工作站离线，显示最后上报事实' }}</strong><em>{{ station.executionSnapshotId || '执行快照未上报' }} · {{ station.currentRunId || '无活动运行' }}</em></div>
      </li>
      <li>
        <span>5</span>
        <div><small>最近结果</small><strong>{{ outcome?.label ?? '当前窗口无结果' }}<template v-if="projection.latestResult"> · {{ formatStationDateTime(projection.latestResult.completedAtUtc) }}</template></strong><em>{{ projection.latestResult ? `${projection.latestResult.runId} · ${projection.latestResult.messageId}` : '结果身份未读取' }}</em></div>
        <RouterLink
          v-if="resultHref"
          class="production-trace__nav-link"
          :to="resultHref"
        >
          查看结果
        </RouterLink>
      </li>
    </ol>

    <dl class="production-trace__hashes">
      <div
        v-for="item in hashItems"
        :key="item.key"
      >
        <dt>{{ item.label }}</dt>
        <dd>
          <code
            :title="item.value ?? undefined"
            translate="no"
          >{{ summarizeTraceIdentity(item.value) }}</code>
          <CvIconButton
            v-if="item.value"
            size="sm"
            :label="`复制完整${item.label}`"
            :title="copiedIdentity === item.key ? '已复制' : `复制完整${item.label}`"
            @click="copyIdentity(item.key, item.value)"
          >
            <CvIcon
              name="copy"
              size="sm"
            />
          </CvIconButton>
        </dd>
      </div>
    </dl>

    <CvInlineAlert
      v-for="item in mismatchItems"
      :key="item.key"
      tone="error"
      title="生产身份不一致"
    >
      {{ item.message }}
    </CvInlineAlert>
    <CvInlineAlert
      v-if="projection.gaps.length"
      tone="warning"
      title="追溯链保留缺口"
    >
      {{ projection.gaps.join(' ') }}
    </CvInlineAlert>
    <CvInlineAlert
      tone="info"
      title="远程图像未上传"
      data-remote-image-status="not-uploaded"
    >
      工作站当前只同步结果摘要，现场图像不可查看。图像上传范围、留存和加密策略尚未配置。
    </CvInlineAlert>
  </CvPanel>
</template>

<style scoped>
.production-trace { min-width: 0; }
.production-trace__heading { display: flex; align-items: center; gap: var(--cv-space-2); margin-bottom: var(--cv-space-3); }
.production-trace__heading > span { color: var(--cv-text-muted); font-family: var(--cv-font-mono); font-size: var(--cv-font-size-xs); }
.production-trace__steps { margin: 0; padding: 0; display: grid; list-style: none; border-block: 1px solid var(--cv-border-subtle); }
.production-trace__steps li { min-width: 0; min-height: 54px; display: grid; grid-template-columns: 24px minmax(0, 1fr) auto; align-items: center; gap: var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.production-trace__steps li:last-child { border-bottom: 0; }
.production-trace__steps li > span:first-child { width: 20px; height: 20px; display: grid; place-items: center; border: 1px solid var(--cv-control-border); border-radius: 50%; color: var(--cv-text-muted); font-family: var(--cv-font-mono); font-size: var(--cv-font-size-xs); }
.production-trace__steps li > div { min-width: 0; display: grid; gap: 1px; }
.production-trace__steps small,.production-trace__steps em { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); font-style: normal; overflow-wrap: anywhere; }
.production-trace__steps strong { font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); overflow-wrap: anywhere; }
.production-trace__restricted { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.production-trace__nav-link { min-height: var(--cv-density-control-height-sm); padding: 0 var(--cv-space-2); display: inline-flex; align-items: center; justify-content: center; border-radius: var(--cv-radius-sm); color: var(--cv-color-link); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); text-decoration: none; touch-action: manipulation; }
.production-trace__nav-link:hover { background: var(--cv-interactive-hover); color: var(--cv-color-link-hover); }
.production-trace__nav-link:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.production-trace__hashes { margin: var(--cv-space-3) 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--cv-space-2); }
.production-trace__hashes > div { min-width: 0; padding: var(--cv-space-2); background: var(--cv-surface-page); }
.production-trace__hashes dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.production-trace__hashes dd { min-width: 0; margin: 2px 0 0; display: flex; align-items: center; gap: var(--cv-space-1); }
.production-trace__hashes code { min-width: 0; flex: 1; overflow: hidden; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); text-overflow: ellipsis; white-space: nowrap; }
.production-trace :deep(.cv-inline-alert) { margin-top: var(--cv-space-2); }
@media (max-width: 880px) {
  .production-trace__hashes { grid-template-columns: 1fr; }
}
</style>
