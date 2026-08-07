<script setup lang="ts">
import { computed, reactive, watch, type DeepReadonly } from 'vue';
import {
  CvButton,
  CvDataTable,
  CvField,
  CvInlineAlert,
  CvPageState,
  CvPanel,
  CvSelect,
  CvStatusBadge,
  type CvDataTableColumn,
  type CvSelectOption
} from '@/design-system';
import type { ReadQueryState } from '@/platform/query';
import type {
  StationAdminDetails,
  StationAudit,
  StationCommand,
  StationCommandType,
  StationLog,
  StationPackage
} from './stationContracts';
import type { StationAdminCommandOwner, StationIdentityUpdate } from './stationAdminCommandOwner';
import { mergeStationCommandProjection, projectStationDeployment } from './stationDeploymentProjection';
import { formatStationBytes, formatStationDateTime } from './stationViewModel';

const props = defineProps<{
  detailsState: DeepReadonly<ReadQueryState<StationAdminDetails>>;
  logsState: DeepReadonly<ReadQueryState<readonly StationLog[]>>;
  commandsState: DeepReadonly<ReadQueryState<readonly StationCommand[]>>;
  auditsState: DeepReadonly<ReadQueryState<readonly StationAudit[]>>;
  packagesState: DeepReadonly<ReadQueryState<readonly StationPackage[]>>;
  owner: StationAdminCommandOwner;
}>();

const emit = defineEmits<{ changed: [] }>();
const form = reactive({
  stationName: '', lineName: '', areaName: '', workcellName: '', inspectionNodeName: '',
  cameraAlias: '', stationRole: '', owner: '', isEnabled: true, remark: ''
});
const selectedCommand = reactive({ type: 'Ping' as StationCommandType, packageId: '' });

watch(() => props.detailsState.data, details => {
  if (!details) return;
  Object.assign(form, {
    stationName: details.stationName,
    lineName: details.lineName ?? '',
    areaName: details.areaName ?? '',
    workcellName: details.workcellName ?? '',
    inspectionNodeName: details.inspectionNodeName ?? '',
    cameraAlias: details.cameraAlias ?? '',
    stationRole: details.stationRole,
    owner: details.owner ?? '',
    isEnabled: details.isEnabled,
    remark: details.remark ?? ''
  });
}, { immediate: true });

const commandOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'Ping', label: '连通性检查' },
  { value: 'StartRuntime', label: '启动 Runtime' },
  { value: 'StopRuntime', label: '停止 Runtime' },
  { value: 'ReloadPackage', label: '重新加载运行包' },
  { value: 'ApplySiteProfile', label: '应用现场配置' },
  { value: 'CollectLogs', label: '采集日志' }
]);
const packageOptions = computed<readonly CvSelectOption[]>(() => [
  { value: '', label: '选择生产运行包' },
  ...(props.packagesState.data ?? [])
    .filter(item => item.packageKind === 'Production')
    .map(item => ({ value: item.packageId, label: `${item.packageName} · ${item.packageVersion}` }))
]);
const selectedPackage = computed(() => (props.packagesState.data ?? []).find(
  item => item.packageId === selectedCommand.packageId
) ?? null);
const projectedCommands = computed<readonly StationCommand[]>(() => {
  return mergeStationCommandProjection(
    props.commandsState.data ?? [],
    props.owner.projection.command
  );
});
const deployment = computed(() => projectStationDeployment({
  commands: projectedCommands.value,
  packages: props.packagesState.data ?? [],
  station: props.detailsState.data ?? null,
  resolution: props.owner.projection.operation === 'deploy-package'
    ? props.owner.projection.phase === 'pending'
      ? 'submitting'
      : props.owner.projection.phase === 'unknown-outcome'
        ? 'unknown'
        : props.owner.projection.phase === 'reconciling'
          ? 'reconciling'
          : 'idle'
    : 'idle',
  pendingPackageId: selectedCommand.packageId
}));
const identityPackage = computed(() => deployment.value.expectedIdentity ?? selectedPackage.value);
const busy = computed(() => ['pending', 'reconciling'].includes(props.owner.projection.phase));
const commandInFlight = computed(() => projectedCommands.value.some(command =>
  !['Succeeded', 'Failed', 'TimedOut', 'Cancelled', 'Rejected'].includes(command.status)
));
const submissionLocked = computed(() =>
  busy.value || props.owner.projection.phase === 'unknown-outcome' || commandInFlight.value
);
const operationTone = computed(() => {
  const phase = props.owner.projection.phase;
  if (phase === 'succeeded') return 'success';
  if (phase === 'conflict' || phase === 'unknown-outcome' || phase === 'reconciling') return 'warning';
  if (phase === 'failed') return 'error';
  return 'info';
});

const commandColumns: readonly CvDataTableColumn<StationCommand>[] = Object.freeze([
  { key: 'createdAtUtc', label: '创建时间', width: '19%' },
  { key: 'commandType', label: '命令', width: '16%' },
  { key: 'status', label: '状态', width: '14%' },
  { key: 'progressPercent', label: '进度', align: 'end', width: '10%' },
  { key: 'issuedBy', label: '下发人', width: '14%' },
  { key: 'resultMessage', label: '结果 / 错误', width: '27%' }
]);
const logColumns: readonly CvDataTableColumn<StationLog>[] = Object.freeze([
  { key: 'timestampUtc', label: '时间', width: '20%' },
  { key: 'level', label: '级别', width: '10%' },
  { key: 'source', label: '来源', width: '18%' },
  { key: 'renderedMessage', label: '消息', width: '52%' }
]);
const auditColumns: readonly CvDataTableColumn<StationAudit>[] = Object.freeze([
  { key: 'createdAtUtc', label: '时间', width: '20%' },
  { key: 'action', label: '操作', width: '18%' },
  { key: 'userName', label: '用户', width: '14%' },
  { key: 'result', label: '结果', width: '14%' },
  { key: 'payloadSummary', label: '摘要', width: '34%' }
]);

function commandLabel(type: StationCommandType): string {
  return commandOptions.find(item => item.value === type)?.label ?? type;
}

function commandTone(status: StationCommand['status']) {
  if (status === 'Succeeded') return 'ok';
  if (status === 'Failed' || status === 'Rejected' || status === 'TimedOut') return 'error';
  if (status === 'Cancelled') return 'warning';
  if (status === 'Running' || status === 'Accepted' || status === 'Delivered') return 'info';
  return 'idle';
}

function commandStatusLabel(status: StationCommand['status']): string {
  const labels: Record<StationCommand['status'], string> = {
    Created: '已创建', Delivered: '已送达', Accepted: '已接受', Rejected: '已拒绝', Running: '执行中',
    Succeeded: '成功', Failed: '失败', TimedOut: '已过期', Cancelled: '已取消'
  };
  return labels[status];
}

async function issueCommand(): Promise<void> {
  const result = await props.owner.issueCommand(selectedCommand.type);
  if (result) emit('changed');
}

async function deployPackage(): Promise<void> {
  const result = await props.owner.deployPackage(selectedCommand.packageId);
  if (result) emit('changed');
}

async function saveIdentity(): Promise<void> {
  const input: StationIdentityUpdate = {
    stationName: form.stationName,
    lineName: form.lineName,
    areaName: form.areaName,
    workcellName: form.workcellName,
    inspectionNodeName: form.inspectionNodeName,
    cameraAlias: form.cameraAlias,
    stationRole: form.stationRole,
    owner: form.owner,
    isEnabled: form.isEnabled,
    remark: form.remark
  };
  const result = await props.owner.reviseIdentity(input);
  if (result) emit('changed');
}

async function recover(): Promise<void> {
  if (await props.owner.recover()) emit('changed');
}
</script>

<template>
  <div
    class="station-admin"
    data-capability="station-admin-control"
  >
    <CvInlineAlert
      v-if="owner.projection.phase !== 'idle' && owner.projection.phase !== 'disposed'"
      :tone="operationTone"
      :title="owner.projection.phase === 'unknown-outcome' ? '操作结果未知' : owner.projection.phase === 'reconciling' ? '正在核对操作结果' : owner.projection.phase === 'conflict' ? '操作冲突' : undefined"
    >
      {{ owner.projection.message }}
      <span v-if="owner.projection.errorCode">（{{ owner.projection.errorCode }}）</span>
      <template
        v-if="owner.projection.canRecover"
        #actions
      >
        <CvButton
          size="sm"
          :loading="busy"
          loading-label="正在确认后端状态"
          @click="recover"
        >
          读取后端状态
        </CvButton>
      </template>
    </CvInlineAlert>

    <div class="station-admin__control-grid">
      <CvPanel
        title="运行控制"
        description="命令只通过现有工作站管理 HTTP 合同创建；创建成功不代表执行成功。"
      >
        <div class="station-admin__command-row">
          <CvSelect
            v-model="selectedCommand.type"
            label="工作站命令"
            name="stationCommandType"
            :options="commandOptions"
            :disabled="submissionLocked"
          />
          <CvButton
            variant="primary"
            :loading="busy && owner.projection.operation === 'command'"
            :disabled="submissionLocked"
            data-testid="station-issue-command"
            @click="issueCommand"
          >
            下发命令
          </CvButton>
        </div>
        <div class="station-admin__command-row">
          <CvSelect
            v-model="selectedCommand.packageId"
            label="生产运行包"
            name="stationPackageId"
            :options="packageOptions"
            :disabled="submissionLocked || packagesState.phase === 'loading'"
          />
          <CvButton
            :loading="busy && owner.projection.operation === 'deploy-package'"
            :disabled="submissionLocked || !selectedCommand.packageId"
            data-testid="station-deploy-package"
            @click="deployPackage"
          >
            下发运行包
          </CvButton>
        </div>
        <section
          class="station-admin__deployment"
          aria-labelledby="station-deployment-title"
          aria-live="polite"
          data-testid="station-deployment-status"
        >
          <div class="station-admin__deployment-heading">
            <h3 id="station-deployment-title">
              部署核对
            </h3>
            <CvStatusBadge :tone="deployment.tone">
              {{ deployment.label }}
            </CvStatusBadge>
          </div>
          <p>{{ deployment.message }}</p>
          <dl
            v-if="identityPackage"
            class="station-admin__identity-grid"
          >
            <div><dt>运行包 ID</dt><dd>{{ identityPackage.packageId }}</dd></div>
            <div><dt>版本</dt><dd>{{ identityPackage.packageVersion }}</dd></div>
            <div class="station-admin__identity-wide">
              <dt>SHA-256</dt><dd>{{ identityPackage.sha256 || '身份不完整' }}</dd>
            </div>
            <div><dt>来源工程</dt><dd>{{ identityPackage.sourceProjectId || '身份不完整' }}</dd></div>
            <div><dt>来源修订</dt><dd>{{ identityPackage.sourceProjectRevision ?? '身份不完整' }}</dd></div>
            <div class="station-admin__identity-wide">
              <dt>流程哈希</dt><dd>{{ identityPackage.flowHash || '身份不完整' }}</dd>
            </div>
            <div class="station-admin__identity-wide">
              <dt>判定配置哈希</dt><dd>{{ identityPackage.decisionConfigurationHash || '身份不完整' }}</dd>
            </div>
          </dl>
        </section>
      </CvPanel>

      <CvPanel
        title="身份修订"
        description="修订工作站业务身份；连接与运行状态仍由后端权威维护。"
      >
        <CvPageState
          v-if="detailsState.phase === 'loading' && !detailsState.data"
          compact
          kind="loading"
          title="正在读取工作站身份"
        />
        <CvPageState
          v-else-if="detailsState.phase === 'forbidden'"
          compact
          kind="forbidden"
          title="无工作站管理员权限"
        />
        <form
          v-else-if="detailsState.data"
          class="station-admin__identity-form"
          @submit.prevent="saveIdentity"
        >
          <CvField
            v-model="form.stationName"
            label="工作站名称"
            name="stationName"
            autocomplete="off"
            required
            :disabled="submissionLocked"
          />
          <CvField
            v-model="form.lineName"
            label="产线"
            name="lineName"
            autocomplete="off"
            :disabled="submissionLocked"
          />
          <CvField
            v-model="form.areaName"
            label="区域"
            name="areaName"
            autocomplete="off"
            :disabled="submissionLocked"
          />
          <CvField
            v-model="form.workcellName"
            label="工作单元"
            name="workcellName"
            autocomplete="off"
            :disabled="submissionLocked"
          />
          <CvField
            v-model="form.inspectionNodeName"
            label="检测节点"
            name="inspectionNodeName"
            autocomplete="off"
            :disabled="submissionLocked"
          />
          <CvField
            v-model="form.cameraAlias"
            label="相机别名"
            name="cameraAlias"
            autocomplete="off"
            :disabled="submissionLocked"
          />
          <CvField
            v-model="form.stationRole"
            label="工作站角色"
            name="stationRole"
            autocomplete="off"
            required
            :disabled="submissionLocked"
          />
          <CvField
            v-model="form.owner"
            label="负责人"
            name="stationOwner"
            autocomplete="off"
            :disabled="submissionLocked"
          />
          <CvField
            v-model="form.remark"
            class="station-admin__wide"
            label="备注"
            name="stationRemark"
            autocomplete="off"
            :disabled="submissionLocked"
          />
          <label class="station-admin__enabled">
            <input
              v-model="form.isEnabled"
              type="checkbox"
              name="stationEnabled"
              :disabled="submissionLocked"
            >
            启用工作站
          </label>
          <div class="station-admin__identity-actions">
            <CvButton
              type="submit"
              variant="primary"
              :loading="busy && owner.projection.operation === 'identity'"
              :disabled="submissionLocked"
              data-testid="station-save-identity"
            >
              保存身份修订
            </CvButton>
          </div>
        </form>
      </CvPanel>
    </div>

    <CvPanel
      title="命令记录"
      :padded="false"
    >
      <CvPageState
        v-if="commandsState.phase === 'empty'"
        compact
        kind="empty"
        title="暂无命令记录"
      />
      <CvPageState
        v-else-if="commandsState.phase === 'error'"
        compact
        kind="error"
        title="命令记录读取失败"
        :description="commandsState.failure?.message"
      />
      <CvDataTable
        v-if="commandsState.data?.length"
        :rows="commandsState.data"
        :columns="commandColumns"
        :row-key="row => row.commandId"
        caption="工作站命令记录"
        :busy="commandsState.isRefreshing"
      >
        <template #cell-createdAtUtc="{ row }">
          {{ formatStationDateTime(row.createdAtUtc) }}
        </template>
        <template #cell-commandType="{ row }">
          {{ commandLabel(row.commandType) }}
        </template>
        <template #cell-status="{ row }">
          <CvStatusBadge :tone="commandTone(row.status)">
            {{ commandStatusLabel(row.status) }}
          </CvStatusBadge>
        </template>
        <template #cell-progressPercent="{ row }">
          {{ row.progressPercent }}%
        </template>
        <template #cell-resultMessage="{ row }">
          {{ row.resultMessage || row.errorCode || '—' }}
        </template>
      </CvDataTable>
    </CvPanel>

    <div class="station-admin__records-grid">
      <CvPanel
        title="工作站日志"
        :padded="false"
      >
        <CvPageState
          v-if="logsState.phase === 'empty'"
          compact
          kind="empty"
          title="暂无日志"
        />
        <CvPageState
          v-else-if="logsState.phase === 'error'"
          compact
          kind="error"
          title="日志读取失败"
          :description="logsState.failure?.message"
        />
        <CvDataTable
          v-if="logsState.data?.length"
          :rows="logsState.data"
          :columns="logColumns"
          :row-key="row => `${row.sequenceId}:${row.messageId}`"
          caption="工作站日志"
          :busy="logsState.isRefreshing"
        >
          <template #cell-timestampUtc="{ row }">
            {{ formatStationDateTime(row.timestampUtc) }}
          </template>
          <template #cell-level="{ row }">
            <CvStatusBadge :tone="row.level === 'ERROR' || row.level === 'FATAL' ? 'error' : 'warning'">
              {{ row.level || '—' }}
            </CvStatusBadge>
          </template>
        </CvDataTable>
      </CvPanel>

      <CvPanel
        title="审计记录"
        :padded="false"
      >
        <CvPageState
          v-if="auditsState.phase === 'empty'"
          compact
          kind="empty"
          title="暂无审计记录"
        />
        <CvPageState
          v-else-if="auditsState.phase === 'error'"
          compact
          kind="error"
          title="审计记录读取失败"
          :description="auditsState.failure?.message"
        />
        <CvDataTable
          v-if="auditsState.data?.length"
          :rows="auditsState.data"
          :columns="auditColumns"
          :row-key="row => row.auditId"
          caption="工作站审计记录"
          :busy="auditsState.isRefreshing"
        >
          <template #cell-createdAtUtc="{ row }">
            {{ formatStationDateTime(row.createdAtUtc) }}
          </template>
          <template #cell-userName="{ row }">
            {{ row.userName || '系统' }}
          </template>
          <template #cell-result="{ row }">
            {{ row.result || '—' }}
          </template>
          <template #cell-payloadSummary="{ row }">
            <span class="station-admin__wrap">{{ row.payloadSummary || '—' }}</span>
          </template>
        </CvDataTable>
      </CvPanel>
    </div>

    <p class="station-admin__package-note">
      可用生产运行包 {{ packagesState.data?.filter(item => item.packageKind === 'Production').length ?? 0 }} 个；列表合计
      {{ formatStationBytes(packagesState.data?.reduce((sum, item) => sum + item.sizeBytes, 0) ?? 0) }}。
    </p>
  </div>
</template>

<style scoped>
.station-admin { display: grid; min-width: 0; gap: var(--cv-density-page-gap); }
.station-admin__control-grid,
.station-admin__records-grid { display: grid; min-width: 0; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-density-page-gap); align-items: start; }
.station-admin__command-row { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: var(--cv-space-3); align-items: end; }
.station-admin__command-row + .station-admin__command-row { margin-top: var(--cv-space-4); }
.station-admin__deployment { margin-top: var(--cv-space-4); padding-top: var(--cv-space-4); border-top: 1px solid var(--cv-border-subtle); }
.station-admin__deployment-heading { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.station-admin__deployment-heading h3 { margin: 0; font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); letter-spacing: 0; }
.station-admin__deployment p { margin: var(--cv-space-2) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.station-admin__identity-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-2) var(--cv-space-4); margin: var(--cv-space-3) 0 0; }
.station-admin__identity-grid div { min-width: 0; }
.station-admin__identity-grid dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.station-admin__identity-grid dd { margin: var(--cv-space-1) 0 0; color: var(--cv-text-primary); font-family: var(--cv-font-mono); font-size: var(--cv-font-size-xs); font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
.station-admin__identity-wide { grid-column: 1 / -1; }
.station-admin__identity-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-3); }
.station-admin__wide,
.station-admin__identity-actions { grid-column: 1 / -1; }
.station-admin__enabled { display: inline-flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.station-admin__identity-actions { display: flex; justify-content: flex-end; }
.station-admin__wrap { overflow-wrap: anywhere; }
.station-admin__package-note { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
@media (max-width: 1180px) {
  .station-admin__control-grid,
  .station-admin__records-grid { grid-template-columns: 1fr; }
}
</style>
