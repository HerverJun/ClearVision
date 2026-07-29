<script setup lang="ts">
import { computed } from 'vue';
import AiWorkbenchStage from './AiWorkbenchStage.vue';
import { aiWorkbenchActionModel, type AiWorkbenchActionId } from './actionModel';
import type { AiWorkbenchProjection } from './projection';
import type { AiWorkbenchState } from './reducer';

const props = defineProps<{
  state: AiWorkbenchState;
  projection: AiWorkbenchProjection;
}>();

const emit = defineEmits<{
  action: [actionId: AiWorkbenchActionId];
}>();

const actionModel = computed(() => aiWorkbenchActionModel(props.state));
</script>

<template>
  <AiWorkbenchStage
    :projection="projection"
    :action-model="actionModel"
    @action="emit('action', $event)"
  />
</template>
