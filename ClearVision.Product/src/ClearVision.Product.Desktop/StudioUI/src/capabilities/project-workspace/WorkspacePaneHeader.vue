<script setup lang="ts">
withDefaults(defineProps<{
  title: string;
  detail?: string | undefined;
  titleTooltip?: string | undefined;
}>(), {
  detail: undefined,
  titleTooltip: undefined
});
</script>

<template>
  <header class="workspace-pane-header">
    <div class="workspace-pane-header__identity">
      <strong :title="titleTooltip ?? title">{{ title }}</strong>
      <small
        v-if="detail"
        :title="detail"
      >{{ detail }}</small>
      <slot name="meta" />
    </div>
    <div
      v-if="$slots.default"
      class="workspace-pane-header__actions"
    >
      <slot />
    </div>
  </header>
</template>

<style scoped>
.workspace-pane-header {
  min-width: 0;
  min-height: 38px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-2);
  padding: 0 var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}

.workspace-pane-header__identity,
.workspace-pane-header__actions {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: var(--cv-space-2);
}

.workspace-pane-header__identity { flex: 1 1 auto; }
.workspace-pane-header__actions { flex: 0 0 auto; }
.workspace-pane-header__identity strong,
.workspace-pane-header__identity small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.workspace-pane-header__identity strong {
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-semibold);
}
.workspace-pane-header__identity small {
  max-width: 46%;
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
}
.workspace-pane-header :deep([data-design-primitive="status-badge"]) {
  min-height: 20px;
  padding-block: 1px;
}
</style>
