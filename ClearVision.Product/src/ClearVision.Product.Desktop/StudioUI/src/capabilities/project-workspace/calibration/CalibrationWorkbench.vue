<script setup lang="ts">
import { computed } from 'vue';
import { CvButton, CvInlineAlert, CvStatusBadge } from '@/design-system';
import type { CalibrationOwner } from './calibrationOwner';

const props = defineProps<{
  owner: CalibrationOwner;
}>();

const projection = props.owner.projection;
const phaseTone = computed(() => {
  if (projection.phase === 'saved' || projection.lastSolveResult?.accepted === true) return 'ok';
  if (projection.phase === 'error' || projection.phase === 'stale' || projection.phase === 'unknown-outcome') return 'warning';
  if (projection.phase === 'solving' || projection.phase === 'saving') return 'info';
  return 'idle';
});
const phaseLabel = computed(() => ({
  unavailable: '等待图像', ready: '可采集', dirty: '草稿', solving: '拟合中', solved: '候选就绪',
  saving: '保存中', saved: '已保存', stale: '已过期', readonly: '只读', 'unknown-outcome': '结果待核对', error: '需处理', disposed: '已关闭'
}[projection.phase] ?? '状态未知'));
const accepted = computed(() => projection.lastSolveResult?.accepted === true);
const activeCount = computed(() => projection.samples.filter(sample => sample.enabled).length);
const completeCount = computed(() => projection.samples.filter(sample => sample.enabled && sample.valid).length);
const worldEditingDisabled = computed(() => [
  'unavailable', 'readonly', 'stale', 'unknown-outcome', 'disposed', 'solving', 'saving'
].includes(projection.phase));

function numericValue(value: number | null): string {
  return value === null ? '' : String(value);
}

function updateNumber(sampleId: string, key: 'worldX' | 'worldY', value: string): void {
  props.owner.updateSample(sampleId, { [key]: value.trim() === '' ? null : Number(value) });
}
</script>

<template>
  <section
    class="calibration-workbench"
    data-testid="next-npoint-calibration-workbench"
    :data-calibration-phase="projection.phase"
    :data-calibration-image-generation="projection.imageGeneration"
  >
    <div class="calibration-workbench__header">
      <div>
        <h3>N 点标定</h3>
        <small>服务端拟合 · 候选与正式资产分离</small>
      </div>
      <CvStatusBadge
        :tone="phaseTone"
        :label="phaseLabel"
      />
    </div>

    <CvInlineAlert
      v-if="projection.phase === 'error' || projection.phase === 'stale' || projection.phase === 'readonly' || projection.phase === 'unknown-outcome'"
      tone="warning"
      title="标定状态"
    >
      {{ projection.message }}
    </CvInlineAlert>
    <p
      v-else
      class="calibration-workbench__message"
      role="status"
    >
      {{ projection.message }}
    </p>

    <div class="calibration-workbench__toolbar">
      <CvButton
        size="sm"
        :variant="projection.captureArmed ? 'primary' : 'quiet'"
        :disabled="!projection.canCapture"
        data-testid="next-calibration-capture"
        @click="owner.toggleCapture"
      >
        {{ projection.captureArmed ? '点击图像采点' : '采集像素点' }}
      </CvButton>
      <CvButton
        size="sm"
        variant="quiet"
        :disabled="!projection.canSolve"
        :loading="projection.phase === 'solving'"
        loading-label="正在拟合…"
        data-testid="next-calibration-solve"
        @click="owner.solve"
      >
        计算候选
      </CvButton>
      <CvButton
        size="sm"
        variant="quiet"
        :disabled="!projection.canSave"
        :loading="projection.phase === 'saving'"
        loading-label="保存中…"
        data-testid="next-calibration-save"
        @click="owner.save"
      >
        保存资产
      </CvButton>
      <CvButton
        size="sm"
        variant="quiet"
        :disabled="projection.phase === 'solving' || projection.phase === 'saving'"
        title="放弃当前标定草稿并重新读取节点参数"
        @click="owner.reset"
      >
        重置
      </CvButton>
    </div>

    <div class="calibration-workbench__summary">
      <span>模式 <strong>{{ projection.mode }}</strong></span>
      <span>单位 <strong>{{ projection.unit }}</strong></span>
      <span>启用 <strong>{{ activeCount }}</strong></span>
      <span>完整 <strong>{{ completeCount }}</strong></span>
      <span v-if="projection.formalAssetId">资产 <strong :title="projection.formalAssetId">{{ projection.formalAssetId }}</strong></span>
    </div>

    <div class="calibration-workbench__table-wrap">
      <table class="calibration-workbench__table">
        <thead>
          <tr><th>#</th><th>像素 X</th><th>像素 Y</th><th>World X</th><th>World Y</th><th>状态</th><th aria-label="操作" /></tr>
        </thead>
        <tbody>
          <tr
            v-for="sample in projection.samples"
            :key="sample.sampleId"
            :data-sample-id="sample.sampleId"
            :data-enabled="sample.enabled"
          >
            <td>{{ sample.order }}</td>
            <td>{{ numericValue(sample.pixelX) }}</td>
            <td>{{ numericValue(sample.pixelY) }}</td>
            <td>
              <input
                :value="numericValue(sample.worldX)"
                type="number"
                step="any"
                :disabled="worldEditingDisabled"
                @change="updateNumber(sample.sampleId, 'worldX', ($event.target as HTMLInputElement).value)"
              >
            </td>
            <td>
              <input
                :value="numericValue(sample.worldY)"
                type="number"
                step="any"
                :disabled="worldEditingDisabled"
                @change="updateNumber(sample.sampleId, 'worldY', ($event.target as HTMLInputElement).value)"
              >
            </td>
            <td><span :data-inlier="sample.inlier === null ? 'unknown' : sample.inlier">{{ sample.inlier === false ? '外点' : sample.inlier === true ? '内点' : sample.valid ? '待拟合' : '待补全' }}</span></td>
            <td>
              <button
                type="button"
                :disabled="worldEditingDisabled"
                :aria-label="`删除第 ${sample.order} 个样本`"
                @click="owner.removeSample(sample.sampleId)"
              >
                删除
              </button>
            </td>
          </tr>
          <tr v-if="projection.samples.length === 0">
            <td colspan="7">
              尚无样本。点击“采集像素点”后在图像区添加。
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div
      v-if="projection.lastSolveResult"
      class="calibration-workbench__result"
      data-testid="next-calibration-result"
    >
      <span>平均残差 <strong>{{ projection.lastSolveResult.meanError ?? '-' }}</strong></span>
      <span>最大残差 <strong>{{ projection.lastSolveResult.maxError ?? '-' }}</strong></span>
      <span>内点率 <strong>{{ projection.lastSolveResult.inlierRatio === null ? '-' : `${Math.round(projection.lastSolveResult.inlierRatio * 100)}%` }}</strong></span>
      <span>质量门禁 <strong>{{ accepted ? '通过' : '未通过' }}</strong></span>
    </div>
  </section>
</template>

<style scoped>
.calibration-workbench { display: grid; gap: var(--cv-space-2); padding: 12px 14px; border-top: 1px solid var(--cv-border-subtle); background: color-mix(in srgb, var(--cv-color-status-info-soft) 38%, var(--cv-surface-raised)); }
.calibration-workbench__header, .calibration-workbench__toolbar, .calibration-workbench__summary, .calibration-workbench__result { display: flex; align-items: center; gap: var(--cv-space-2); flex-wrap: wrap; }
.calibration-workbench__header { justify-content: space-between; }
.calibration-workbench h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.calibration-workbench small, .calibration-workbench__message { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.calibration-workbench__message { margin: 0; line-height: 1.45; }
.calibration-workbench__summary, .calibration-workbench__result { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-variant-numeric: tabular-nums; }
.calibration-workbench__summary strong, .calibration-workbench__result strong { color: var(--cv-text-primary); }
.calibration-workbench__table-wrap { overflow-x: auto; border: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.calibration-workbench__table { width: 100%; min-width: 430px; border-collapse: collapse; font-size: var(--cv-font-size-2xs); }
.calibration-workbench__table th, .calibration-workbench__table td { padding: 4px 5px; border-bottom: 1px solid var(--cv-border-subtle); text-align: left; white-space: nowrap; }
.calibration-workbench__table th { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-weight: var(--cv-font-weight-semibold); }
.calibration-workbench__table td { color: var(--cv-text-secondary); }
.calibration-workbench__table input { width: 68px; min-width: 0; height: 32px; padding: 0 6px; border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-raised); color: var(--cv-text-primary); font: inherit; }
.calibration-workbench__table button { border: 0; background: transparent; color: var(--cv-color-status-ng-strong); cursor: pointer; font: inherit; }
.calibration-workbench__table button:disabled { color: var(--cv-text-muted); cursor: not-allowed; }
.calibration-workbench__table [data-inlier="false"] { color: var(--cv-color-status-ng-strong); }
.calibration-workbench__table [data-inlier="true"] { color: var(--cv-color-status-ok-strong); }
.calibration-workbench__result { padding-top: var(--cv-space-1); }
</style>
