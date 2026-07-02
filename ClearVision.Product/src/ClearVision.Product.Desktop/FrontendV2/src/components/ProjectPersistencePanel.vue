<template>
  <section class="studio2-project-port-panel">
    <header class="studio2-project-port-panel__header">
      <h2>Project Save</h2>
      <span>{{ snapshot.status }}</span>
    </header>

    <dl class="studio2-project-port-panel__meta">
      <div>
        <dt>projectId</dt>
        <dd>{{ snapshot.projectId || 'none' }}</dd>
      </div>
      <div>
        <dt>revision</dt>
        <dd>{{ snapshot.persistenceRevision ?? 'none' }}</dd>
      </div>
      <div>
        <dt>dirty</dt>
        <dd>{{ snapshot.dirty ? 'true' : 'false' }}</dd>
      </div>
      <div>
        <dt>saving</dt>
        <dd>{{ snapshot.saving ? 'true' : 'false' }}</dd>
      </div>
    </dl>

    <button
      type="button"
      class="studio2-project-port-panel__save"
      :disabled="!snapshot.loaded || snapshot.saving"
      @click="requestSave"
    >
      Save
    </button>

    <p
      v-if="snapshot.lastDisposition !== 'idle'"
      class="studio2-project-port-panel__disposition"
    >
      {{ snapshot.lastDisposition }}
    </p>
    <p
      v-if="snapshot.error"
      class="studio2-project-port-panel__error"
    >
      {{ snapshot.error }}
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { Studio2FoundationIslandViewModel } from '@/foundation/studio2FoundationIsland';

const props = defineProps<{
  model: Studio2FoundationIslandViewModel;
}>();

const snapshot = computed(() => props.model.workspaceState.projectPersistenceSnapshot);

async function requestSave(): Promise<void> {
  const port = props.model.workspaceRuntime.getProjectPersistencePort();
  if (!port) {
    return;
  }

  await port.save();
}
</script>

<style scoped>
.studio2-project-port-panel {
  display: grid;
  gap: 10px;
  padding-top: 12px;
  border-top: 1px solid #26312d;
}

.studio2-project-port-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.studio2-project-port-panel__header h2 {
  margin: 0;
  color: #edf3ef;
  font-size: 14px;
  font-weight: 650;
}

.studio2-project-port-panel__header span {
  color: #8fb9a6;
  font-size: 12px;
}

.studio2-project-port-panel__meta {
  display: grid;
  gap: 6px;
  margin: 0;
}

.studio2-project-port-panel__meta div {
  min-width: 0;
  display: grid;
  grid-template-columns: 86px minmax(0, 1fr);
  gap: 8px;
}

.studio2-project-port-panel__meta dt,
.studio2-project-port-panel__meta dd {
  min-width: 0;
  margin: 0;
  overflow: hidden;
  color: #9fb0a8;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.studio2-project-port-panel__meta dd {
  color: #edf3ef;
}

.studio2-project-port-panel__save {
  width: max-content;
  height: 28px;
  border: 1px solid #31423a;
  border-radius: 4px;
  background: #203129;
  color: #edf3ef;
  padding: 0 10px;
  cursor: pointer;
}

.studio2-project-port-panel__save:disabled {
  opacity: 0.5;
  cursor: default;
}

.studio2-project-port-panel__disposition,
.studio2-project-port-panel__error {
  margin: 0;
  color: #e0b66b;
  font-size: 12px;
}

.studio2-project-port-panel__error {
  color: #e36f61;
}
</style>
