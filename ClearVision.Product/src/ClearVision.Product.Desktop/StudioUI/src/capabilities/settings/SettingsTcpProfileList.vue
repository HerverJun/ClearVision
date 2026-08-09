<script setup lang="ts">
import { CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { TcpModeV1, TcpProfileStatusV1 } from './deviceContracts';

interface TcpProfileListItem {
  readonly id: string;
  readonly name: string;
  readonly mode: TcpModeV1;
}

defineProps<{
  profiles: readonly TcpProfileListItem[];
  statuses: Readonly<Record<string, TcpProfileStatusV1 | null>>;
  selectedId: string;
  canWrite: boolean;
  busy: boolean;
}>();

const emit = defineEmits<{
  select: [id: string];
  add: [mode: TcpModeV1];
}>();
</script>

<template>
  <aside
    class="tcp-profile-list"
    aria-label="TCP 连接配置列表"
  >
    <div class="tcp-profile-list__header">
      <div><strong>连接配置</strong><small>{{ profiles.length }} 个配置</small></div>
      <div class="tcp-profile-list__actions">
        <button
          class="tcp-profile-list__icon-action"
          type="button"
          aria-label="添加客户端配置"
          title="添加客户端配置"
          :disabled="!canWrite || busy"
          @click="emit('add', 'Client')"
        >
          <CvIcon
            name="plus"
            size="sm"
          />
        </button>
        <button
          class="tcp-profile-list__icon-action"
          type="button"
          aria-label="添加服务端配置"
          title="添加服务端配置"
          :disabled="!canWrite || busy"
          @click="emit('add', 'Server')"
        >
          <CvIcon
            name="server"
            size="sm"
          />
        </button>
      </div>
    </div>
    <div class="tcp-profile-list__items">
      <button
        v-for="profile in profiles"
        :key="profile.id"
        class="tcp-profile-list__row"
        :class="{ 'is-active': profile.id === selectedId }"
        type="button"
        :title="profile.name || '未命名配置'"
        @click="emit('select', profile.id)"
      >
        <span class="tcp-profile-list__main">
          <strong>{{ profile.name || '未命名配置' }}</strong>
          <small>{{ profile.mode === 'Client' ? '客户端' : '服务端' }} · {{ profile.id }}</small>
        </span>
        <CvStatusBadge
          v-if="statuses[profile.id]?.isConnected || statuses[profile.id]?.isListening"
          tone="ok"
          label="运行"
        />
      </button>
      <p
        v-if="profiles.length === 0"
        class="tcp-profile-list__empty"
      >
        暂无连接配置；管理员可添加客户端或服务端配置。
      </p>
    </div>
  </aside>
</template>

<style scoped>
.tcp-profile-list { min-width: 0; padding-right: var(--cv-space-4); border-right: 1px solid var(--cv-border-subtle); }
.tcp-profile-list__header { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.tcp-profile-list__header strong { display: block; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.tcp-profile-list__header small { display: block; margin-top: 2px; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.tcp-profile-list__actions { display: flex; gap: var(--cv-space-1); }
.tcp-profile-list__items { display: grid; gap: 2px; margin-top: var(--cv-space-3); }
.tcp-profile-list__row { min-width: 0; min-height: 36px; padding: var(--cv-space-2); display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); border: 0; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-text-secondary); text-align: left; cursor: pointer; }
.tcp-profile-list__row:hover { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.tcp-profile-list__row.is-active { background: var(--cv-color-brand-soft); color: var(--cv-color-brand-text); }
.tcp-profile-list__main { display: grid; min-width: 0; gap: 2px; }
.tcp-profile-list__main strong,.tcp-profile-list__main small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.tcp-profile-list__main strong { font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.tcp-profile-list__main small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.tcp-profile-list__empty { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.tcp-profile-list__icon-action { width: 32px; height: 32px; display: inline-grid; place-items: center; border: 0; border-radius: var(--cv-radius-xs); background: transparent; color: var(--cv-text-secondary); cursor: pointer; }
.tcp-profile-list__icon-action:hover:not(:disabled) { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.tcp-profile-list__icon-action:disabled { opacity: 0.48; cursor: not-allowed; }
@media (max-width: 780px) {
  .tcp-profile-list { padding-right: 0; padding-bottom: var(--cv-space-3); border-right: 0; border-bottom: 1px solid var(--cv-border-subtle); }
}
</style>
