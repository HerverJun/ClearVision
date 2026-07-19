<script setup lang="ts">
withDefaults(defineProps<{
  compact?: boolean;
}>(), {
  compact: false
});
</script>

<template>
  <span
    class="cv-brand"
    :class="{ 'cv-brand--compact': compact }"
    aria-label="ClearVision"
    translate="no"
    data-design-pattern="brand"
  >
    <svg
      class="cv-brand__mark"
      viewBox="0 0 48 48"
      aria-hidden="true"
      focusable="false"
    >
      <g
        fill="none"
        stroke="currentColor"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path d="M24 3v42M6 13l36 22M6 35l36-22" />
        <path d="m24 3-3 5m3-5 3 5m-3 37-3-5m3 5 3-5" />
        <path d="m6 13 6 .4M6 13l2.7 5.3M42 35l-6-.4m6 .4-2.7-5.3" />
        <path d="m6 35 6-.4M6 35l2.7-5.3M42 13l-6 .4m6-.4-2.7 5.3" />
        <path d="m24 12-4 4m4-4 4 4m-4 20-4-4m4 4 4-4" />
        <path d="m13.7 18.2 5.4.4-2.3 4.8m17.5 6.4-5.4-.4 2.3-4.8" />
        <path d="m13.7 29.8 5.4-.4-2.3-4.8m17.5-6.4-5.4.4 2.3 4.8" />
        <circle
          cx="24"
          cy="24"
          r="2.6"
          fill="currentColor"
          stroke="none"
        />
      </g>
    </svg>
    <span class="cv-brand__wordmark">ClearVision</span>
  </span>
</template>

<style scoped>
.cv-brand {
  min-width: 0;
  display: inline-flex;
  align-items: center;
  gap: 10px;
  color: var(--cv-color-brand-500);
  white-space: nowrap;
}

.cv-brand__mark {
  width: 34px;
  height: 34px;
  flex: 0 0 auto;
  stroke-width: 1.25;
}

.cv-brand__wordmark {
  font-family: "Segoe Script", "Brush Script MT", cursive;
  font-size: 25px;
  font-weight: 700;
  line-height: 1;
  letter-spacing: -0.055em;
}

.cv-brand--compact { gap: 0; }
.cv-brand--compact .cv-brand__wordmark { display: none; }
.cv-brand--compact .cv-brand__mark { width: 30px; height: 30px; }
</style>
