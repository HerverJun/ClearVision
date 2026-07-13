<script setup lang="ts">
import { studioUiBuildMetadata } from '@/platform/diagnostics/buildMetadata';

const routeLinks = [
  { to: '/diagnostics', label: 'Diagnostics' },
  { to: '/labs/design', label: 'Design placeholder' },
  { to: '/labs/canvas', label: 'Canvas placeholder' }
] as const;
</script>

<template>
  <main
    class="foundation-page"
    data-studio-page="diagnostics"
  >
    <h1>StudioUI build foundation</h1>
    <p>
      Prompt 1 verifies the independent build, router, manifest, and Desktop asset pipeline only.
    </p>

    <dl>
      <dt>Name</dt>
      <dd>{{ studioUiBuildMetadata.name }}</dd>
      <dt>Version</dt>
      <dd>{{ studioUiBuildMetadata.version }}</dd>
      <dt>Base path</dt>
      <dd>{{ studioUiBuildMetadata.basePath }}</dd>
      <dt>Mode</dt>
      <dd>{{ studioUiBuildMetadata.mode }}</dd>
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
