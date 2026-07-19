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
    <template v-else>
      <path d="M20 11a8 8 0 1 0-2.3 5.7" />
      <path d="M20 4v7h-7" />
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
