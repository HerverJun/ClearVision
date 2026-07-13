<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue';
import { useStudioPlatform } from '@/app/studioPlatform';
import { studioUiBuildMetadata } from '@/platform/diagnostics/buildMetadata';
import {
  createStudioRuntimeDiagnosticsProbe,
  type DiagnosticProbeResult
} from '@/platform/diagnostics/runtimeDiagnostics';

const routeLinks = [
  { to: '/diagnostics', label: 'Diagnostics' },
  { to: '/labs/design', label: 'Design placeholder' },
  { to: '/labs/canvas', label: 'Canvas placeholder' }
] as const;

const pendingProbe: DiagnosticProbeResult = Object.freeze({
  state: 'pending',
  summary: 'Pending'
});
const platform = useStudioPlatform();
const startup = platform.startup;
const apiOrigin = new URL(startup.apiBaseUrl).origin;
const hostDiagnostics = platform.host.getDiagnostics();
const health = ref<DiagnosticProbeResult>(pendingProbe);
const setupStatus = ref<DiagnosticProbeResult>(pendingProbe);
const diagnosticsProbe = createStudioRuntimeDiagnosticsProbe(platform.api);

onMounted(async () => {
  const result = await diagnosticsProbe.read();
  health.value = result.health;
  setupStatus.value = result.setupStatus;
});

onUnmounted(() => {
  diagnosticsProbe.dispose();
});
</script>

<template>
  <main
    class="foundation-page"
    data-studio-page="diagnostics"
  >
    <h1>StudioUI platform diagnostics</h1>
    <p>
      Prompt 2 exposes only startup, Host channel, and read-only transport facts.
    </p>

    <dl>
      <dt>Build</dt>
      <dd>{{ studioUiBuildMetadata.name }} {{ studioUiBuildMetadata.version }}</dd>
      <dt>schemaVersion</dt>
      <dd>{{ startup.schemaVersion }}</dd>
      <dt>uiKind</dt>
      <dd>{{ startup.uiKind }}</dd>
      <dt>hostKind</dt>
      <dd>{{ startup.hostKind }}</dd>
      <dt>StudioUI base</dt>
      <dd>{{ startup.studioUiBasePath }}</dd>
      <dt>API origin</dt>
      <dd>{{ apiOrigin }}</dd>
      <dt>WebView2 channel</dt>
      <dd>{{ hostDiagnostics.channel }}</dd>
      <dt>Token present</dt>
      <dd>{{ platform.hasToken() ? 'yes' : 'no' }}</dd>
      <dt>GET /health</dt>
      <dd :data-probe-state="health.state">
        {{ health.summary }}
      </dd>
      <dt>GET /api/auth/setup-status</dt>
      <dd :data-probe-state="setupStatus.state">
        {{ setupStatus.summary }}
      </dd>
    </dl>

    <nav aria-label="StudioUI foundation routes">
      <ul>
        <li
          v-for="route in routeLinks"
          :key="route.to"
        >
          <RouterLink :to="route.to">
            {{ route.label }}
          </RouterLink>
        </li>
      </ul>
    </nav>
  </main>
</template>
