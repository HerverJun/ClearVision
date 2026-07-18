<script setup lang="ts">
import { provide } from 'vue';
import { productRuntimeKey } from '@/app/productRuntime';
import { useAuthLifecycleRoot } from '@/app/auth';

const root = useAuthLifecycleRoot();
const runtime = root.productRuntime.value;
if (!runtime) throw new Error('Protected product route mounted without ProductRuntime.');
provide(productRuntimeKey, runtime);
</script>

<template>
  <RouterView />
</template>
