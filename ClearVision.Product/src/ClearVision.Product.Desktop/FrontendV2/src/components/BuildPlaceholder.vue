<template>
  <section
    class="studio2-build-placeholder"
    aria-hidden="true"
  >
    <span>{{ label }}</span>
  </section>
</template>

<script setup lang="ts">
defineProps<{
  label: string;
}>();
</script>
