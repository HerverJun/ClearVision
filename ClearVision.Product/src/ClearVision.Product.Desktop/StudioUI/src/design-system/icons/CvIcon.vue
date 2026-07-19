<script setup lang="ts">
import type { CvIconName } from './types';

withDefaults(defineProps<{
  name: CvIconName;
  size?: 'sm' | 'md' | 'lg';
  label?: string | undefined;
}>(), {
  size: 'md',
  label: undefined
});
</script>

<template>
  <svg
    class="cv-icon"
    :class="`cv-icon--${size}`"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.8"
    stroke-linecap="round"
    stroke-linejoin="round"
    :role="label ? 'img' : undefined"
    :aria-label="label"
    :aria-hidden="label ? undefined : 'true'"
    focusable="false"
    data-design-icon
  >
    <template v-if="name === 'search'">
      <circle
        cx="10.5"
        cy="10.5"
        r="6.5"
      />
      <path d="m16 16 4 4" />
    </template>
    <template v-else-if="name === 'close'">
      <path d="m6 6 12 12M18 6 6 18" />
    </template>
    <path
      v-else-if="name === 'chevron-left'"
      d="m15 18-6-6 6-6"
    />
    <path
      v-else-if="name === 'chevron-right'"
      d="m9 18 6-6-6-6"
    />
    <template v-else-if="name === 'overview'">
      <rect
        x="4"
        y="4"
        width="6"
        height="6"
        rx="1"
      />
      <rect
        x="14"
        y="4"
        width="6"
        height="6"
        rx="1"
      />
      <rect
        x="4"
        y="14"
        width="6"
        height="6"
        rx="1"
      />
      <rect
        x="14"
        y="14"
        width="6"
        height="6"
        rx="1"
      />
    </template>
    <template v-else-if="name === 'projects'">
      <path d="M3.5 7.5h6l2-2h9v13h-17z" />
      <path d="M3.5 10h17" />
    </template>
    <template v-else-if="name === 'operators'">
      <rect
        x="3.5"
        y="5"
        width="6"
        height="5"
        rx="1"
      />
      <rect
        x="14.5"
        y="14"
        width="6"
        height="5"
        rx="1"
      />
      <path d="M9.5 7.5h3a3 3 0 0 1 3 3V14M14.5 16.5h-3a3 3 0 0 1-3-3V10" />
    </template>
    <template v-else-if="name === 'stations'">
      <rect
        x="4"
        y="4"
        width="16"
        height="12"
        rx="2"
      />
      <path d="M9 20h6M12 16v4M7 8h3M7 12h6" />
    </template>
    <template v-else-if="name === 'results'">
      <path d="M6 3.5h9l3 3V20H6z" />
      <path d="M15 3.5V7h3M9 11h6M9 15h4" />
      <path d="m8.5 18 1 1 2-2" />
    </template>
    <template v-else-if="name === 'diagnostics'">
      <path d="M4 17h3l2.2-7 3.2 10 2.2-7H20" />
      <path d="M4 5h16" />
    </template>
    <template v-else-if="name === 'about'">
      <circle
        cx="12"
        cy="12"
        r="9"
      />
      <path d="M12 11v5M12 8h.01" />
    </template>
    <template v-else-if="name === 'info'">
      <circle
        cx="12"
        cy="12"
        r="9"
      />
      <path d="M12 11v5M12 8h.01" />
    </template>
    <template v-else-if="name === 'warning'">
      <path d="M10.3 4.4 2.8 17.5A2 2 0 0 0 4.5 20h15a2 2 0 0 0 1.7-2.5L13.7 4.4a2 2 0 0 0-3.4 0Z" />
      <path d="M12 9v4M12 16h.01" />
    </template>
    <template v-else-if="name === 'error'">
      <circle
        cx="12"
        cy="12"
        r="9"
      />
      <path d="m9 9 6 6M15 9l-6 6" />
    </template>
    <template v-else-if="name === 'success'">
      <circle
        cx="12"
        cy="12"
        r="9"
      />
      <path d="m8 12 2.7 2.7L16.5 9" />
    </template>
    <template v-else-if="name === 'lock'">
      <rect
        x="5"
        y="10"
        width="14"
        height="10"
        rx="2"
      />
      <path d="M8 10V7a4 4 0 0 1 8 0v3" />
    </template>
    <template v-else-if="name === 'empty'">
      <path d="M4 7.5 7 4h10l3 3.5V19a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1Z" />
      <path d="M4 13h4l1.5 2h5L16 13h4" />
    </template>
    <template v-else-if="name === 'not-found'">
      <path d="M6 3h8l4 4v14H6z" />
      <path d="M14 3v5h4M9.5 13.5a2.5 2.5 0 1 1 4.3 1.7L16 17.5" />
    </template>
    <template v-else-if="name === 'refresh'">
      <path d="M20 11a8 8 0 1 0-2.3 5.7" />
      <path d="M20 4v7h-7" />
    </template>
    <template v-else-if="name === 'undo'">
      <path d="M9 7 4 12l5 5" />
      <path d="M5 12h8a6 6 0 0 1 6 6" />
    </template>
    <template v-else-if="name === 'redo'">
      <path d="m15 7 5 5-5 5" />
      <path d="M19 12h-8a6 6 0 0 0-6 6" />
    </template>
    <template v-else-if="name === 'copy'">
      <rect
        x="8"
        y="8"
        width="11"
        height="11"
        rx="2"
      />
      <path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2" />
    </template>
    <template v-else-if="name === 'paste'">
      <path d="M9 5h6M9 3h6v4H9z" />
      <path d="M8 5H6a2 2 0 0 0-2 2v12h16V7a2 2 0 0 0-2-2h-2" />
    </template>
    <template v-else-if="name === 'duplicate'">
      <rect
        x="7"
        y="7"
        width="12"
        height="12"
        rx="2"
      />
      <path d="M5 16H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h11a1 1 0 0 1 1 1v1M13 10v6M10 13h6" />
    </template>
    <template v-else-if="name === 'power'">
      <path d="M12 3v9" />
      <path d="M7.1 6.8a7 7 0 1 0 9.8 0" />
    </template>
    <template v-else-if="name === 'trash'">
      <path d="M4 7h16M9 7V4h6v3M7 7l1 13h8l1-13M10 11v5M14 11v5" />
    </template>
    <template v-else-if="name === 'zoom-in'">
      <circle
        cx="10.5"
        cy="10.5"
        r="6.5"
      />
      <path d="m16 16 4 4M10.5 7.5v6M7.5 10.5h6" />
    </template>
    <template v-else-if="name === 'zoom-out'">
      <circle
        cx="10.5"
        cy="10.5"
        r="6.5"
      />
      <path d="m16 16 4 4M7.5 10.5h6" />
    </template>
    <template v-else-if="name === 'fit'">
      <path d="M8 4H4v4M16 4h4v4M8 20H4v-4M16 20h4v-4" />
      <rect
        x="8"
        y="8"
        width="8"
        height="8"
        rx="1"
      />
    </template>
    <template v-else-if="name === 'actual-size'">
      <path d="M7 6H4v14h14v-3" />
      <path d="M10 4h10v10M20 4 10 14" />
    </template>
    <template v-else-if="name === 'maximize'">
      <path d="M8 4H4v4M16 4h4v4M8 20H4v-4M16 20h4v-4" />
    </template>
    <template v-else-if="name === 'minimize'">
      <path d="M4 9h5V4M20 9h-5V4M4 15h5v5M20 15h-5v5" />
    </template>
    <path
      v-else-if="name === 'star'"
      d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2L12 17.3l-5.6 2.9 1.1-6.2L3 9.6l6.2-.9z"
    />
    <template v-else-if="name === 'clock'">
      <circle
        cx="12"
        cy="12"
        r="9"
      />
      <path d="M12 7v5l3 2" />
    </template>
    <template v-else-if="name === 'camera'">
      <path d="M4 8h3l1.5-2h7L17 8h3v10H4z" />
      <circle
        cx="12"
        cy="13"
        r="3.5"
      />
    </template>
    <template v-else-if="name === 'sliders'">
      <path d="M5 4v16M12 4v16M19 4v16" />
      <path d="M2 9h6M9 15h6M16 8h6" />
      <circle
        cx="5"
        cy="9"
        r="1.5"
        fill="currentColor"
        stroke="none"
      />
      <circle
        cx="12"
        cy="15"
        r="1.5"
        fill="currentColor"
        stroke="none"
      />
      <circle
        cx="19"
        cy="8"
        r="1.5"
        fill="currentColor"
        stroke="none"
      />
    </template>
    <template v-else-if="name === 'region'">
      <path d="M4 8V4h4M16 4h4v4M4 16v4h4M20 16v4h-4" />
      <rect
        x="8"
        y="8"
        width="8"
        height="8"
        rx="1"
      />
    </template>
    <template v-else-if="name === 'measure'">
      <path d="m5 18 13-13 3 3L8 21H5z" />
      <path d="m12 11 2 2m1-5 2 2m-8 4 2 2" />
    </template>
    <template v-else-if="name === 'filter'">
      <path d="M4 5h16l-6 7v5l-4 2v-7z" />
    </template>
    <template v-else-if="name === 'spark'">
      <path d="m12 3 1.4 4.1L18 8.5l-4.6 1.4L12 14l-1.4-4.1L6 8.5l4.6-1.4z" />
      <path d="m18.5 15 .8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z" />
    </template>
    <template v-else-if="name === 'save'">
      <path d="M5 4h12l2 2v14H5z" />
      <path d="M8 4v6h8V4M8 20v-6h8v6" />
    </template>
    <template v-else-if="name === 'play'">
      <circle
        cx="12"
        cy="12"
        r="9"
      />
      <path d="m10 8 6 4-6 4z" />
    </template>
    <template v-else-if="name === 'variables'">
      <path d="M5 6h5M5 18h5M14 6h5M14 18h5" />
      <path d="m8 6 8 12M16 6 8 18" />
    </template>
    <template v-else-if="name === 'decision'">
      <circle
        cx="12"
        cy="12"
        r="9"
      />
      <path d="m8 12 2.5 2.5L16.5 9" />
    </template>
    <template v-else-if="name === 'theme'">
      <path d="M12 3a9 9 0 1 0 9 9c0-1-.2-2-.5-2.8A7 7 0 0 1 12 3Z" />
    </template>
    <template v-else-if="name === 'drag'">
      <circle
        cx="9"
        cy="6"
        r="1"
        fill="currentColor"
        stroke="none"
      />
      <circle
        cx="15"
        cy="6"
        r="1"
        fill="currentColor"
        stroke="none"
      />
      <circle
        cx="9"
        cy="12"
        r="1"
        fill="currentColor"
        stroke="none"
      />
      <circle
        cx="15"
        cy="12"
        r="1"
        fill="currentColor"
        stroke="none"
      />
      <circle
        cx="9"
        cy="18"
        r="1"
        fill="currentColor"
        stroke="none"
      />
      <circle
        cx="15"
        cy="18"
        r="1"
        fill="currentColor"
        stroke="none"
      />
    </template>
  </svg>
</template>

<style scoped>
.cv-icon {
  display: block;
  flex: 0 0 auto;
}

.cv-icon--sm { width: 14px; height: 14px; }
.cv-icon--md { width: var(--cv-density-icon-size); height: var(--cv-density-icon-size); }
.cv-icon--lg { width: 24px; height: 24px; }
</style>
