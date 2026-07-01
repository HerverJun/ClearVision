<template>
  <main class="foundation-island">
    <header class="foundation-island__header">
      <p class="foundation-island__eyebrow">
        Studio 2.0 Foundation
      </p>
      <h1>G02B Host Island</h1>
    </header>

    <section
      class="foundation-island__grid"
      aria-label="Foundation status"
    >
      <div class="foundation-island__item">
        <span>Flag</span>
        <strong>{{ model.workspaceV2Enabled ? 'enabled' : 'disabled' }}</strong>
      </div>
      <div class="foundation-island__item">
        <span>API Base</span>
        <strong>{{ model.apiBaseUrl || 'not injected' }}</strong>
      </div>
      <div class="foundation-island__item">
        <span>Host</span>
        <strong>{{ model.hostKind }}</strong>
      </div>
      <div class="foundation-island__item">
        <span>V2 Base</span>
        <strong>{{ model.frontendV2BasePath }}</strong>
      </div>
      <div class="foundation-island__item">
        <span>httpClient</span>
        <strong>{{ model.httpClientStatus }}</strong>
      </div>
      <div class="foundation-island__item">
        <span>HostBridge</span>
        <strong>{{ model.hostBridgeStatus }}</strong>
      </div>
      <div class="foundation-island__item">
        <span>EventBus</span>
        <strong>{{ model.eventBusStatus }}</strong>
      </div>
      <div class="foundation-island__item">
        <span>ServiceRegistry</span>
        <strong>{{ model.serviceRegistryStatus }}</strong>
      </div>
      <div class="foundation-island__item foundation-island__item--wide">
        <span>Health</span>
        <strong>{{ model.healthStatus }} · {{ model.healthDetail }}</strong>
      </div>
      <div class="foundation-island__item foundation-island__item--wide">
        <span>Lifecycle</span>
        <strong>
          app={{ model.lifecycle.appInstanceCount }},
          listeners={{ model.lifecycle.listenerCount }},
          timers={{ model.lifecycle.timerCount }},
          observers={{ model.lifecycle.observerCount }},
          abort={{ model.lifecycle.abortControllerCount }},
          registry={{ model.lifecycle.registryRegistrationCount }},
          blobs={{ model.lifecycle.blobUrlCount }},
          pending={{ model.lifecycle.pendingRequestCount }}
        </strong>
      </div>
    </section>
  </main>
</template>

<script setup lang="ts">
import type { Studio2FoundationIslandViewModel } from '@/foundation/studio2FoundationIsland';

defineProps<{
  model: Studio2FoundationIslandViewModel;
}>();
</script>

<style scoped>
.foundation-island {
  min-height: 100vh;
  padding: 24px;
  background: #101418;
  color: #eef2f6;
  font-family: Inter, "Segoe UI", "Microsoft YaHei", sans-serif;
}

.foundation-island__header {
  max-width: 960px;
  margin: 0 auto 20px;
}

.foundation-island__eyebrow {
  margin: 0 0 6px;
  color: #9fb0c0;
  font-size: 12px;
  text-transform: uppercase;
}

.foundation-island h1 {
  margin: 0;
  font-size: 24px;
  font-weight: 650;
}

.foundation-island__grid {
  max-width: 960px;
  margin: 0 auto;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 8px;
}

.foundation-island__item {
  min-height: 72px;
  padding: 12px;
  border: 1px solid #26313b;
  border-radius: 6px;
  background: #151b21;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  gap: 8px;
}

.foundation-island__item--wide {
  grid-column: 1 / -1;
}

.foundation-island__item span {
  color: #9fb0c0;
  font-size: 12px;
}

.foundation-island__item strong {
  overflow-wrap: anywhere;
  color: #f8fafc;
  font-size: 14px;
  font-weight: 600;
}
</style>
