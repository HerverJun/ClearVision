<script setup lang="ts">
import { nextTick, onUnmounted, ref, useId, watch, type CSSProperties } from 'vue';

const props = withDefaults(defineProps<{
  text: string;
  placement?: 'top' | 'bottom' | 'left' | 'right';
  disabled?: boolean;
  maxWidth?: number;
}>(), {
  placement: 'top',
  disabled: false,
  maxWidth: 320
});

const anchor = ref<HTMLElement>();
const tooltip = ref<HTMLElement>();
const visible = ref(false);
const position = ref<CSSProperties>({});
const tooltipId = `cv-tooltip-${useId()}`;
let listening = false;

function updatePosition(): void {
  if (!anchor.value || !tooltip.value || !visible.value) return;
  const anchorRect = anchor.value.getBoundingClientRect();
  const tooltipRect = tooltip.value.getBoundingClientRect();
  const viewportGap = 8;
  const anchorGap = 7;
  let placement = props.placement;

  if (placement === 'top' && anchorRect.top - tooltipRect.height - anchorGap < viewportGap) placement = 'bottom';
  if (placement === 'bottom' && anchorRect.bottom + tooltipRect.height + anchorGap > window.innerHeight - viewportGap) placement = 'top';
  if (placement === 'left' && anchorRect.left - tooltipRect.width - anchorGap < viewportGap) placement = 'right';
  if (placement === 'right' && anchorRect.right + tooltipRect.width + anchorGap > window.innerWidth - viewportGap) placement = 'left';

  let top = anchorRect.top + (anchorRect.height - tooltipRect.height) / 2;
  let left = anchorRect.left + (anchorRect.width - tooltipRect.width) / 2;
  if (placement === 'top') top = anchorRect.top - tooltipRect.height - anchorGap;
  if (placement === 'bottom') top = anchorRect.bottom + anchorGap;
  if (placement === 'left') left = anchorRect.left - tooltipRect.width - anchorGap;
  if (placement === 'right') left = anchorRect.right + anchorGap;

  position.value = {
    top: `${Math.round(Math.min(window.innerHeight - tooltipRect.height - viewportGap, Math.max(viewportGap, top)))}px`,
    left: `${Math.round(Math.min(window.innerWidth - tooltipRect.width - viewportGap, Math.max(viewportGap, left)))}px`,
    maxWidth: `${Math.min(420, Math.max(160, props.maxWidth))}px`
  };
}

function startListening(): void {
  if (listening) return;
  listening = true;
  window.addEventListener('resize', updatePosition);
  window.addEventListener('scroll', updatePosition, true);
}

function stopListening(): void {
  if (!listening) return;
  listening = false;
  window.removeEventListener('resize', updatePosition);
  window.removeEventListener('scroll', updatePosition, true);
}

function show(): void {
  if (props.disabled || !props.text) return;
  visible.value = true;
  startListening();
  void nextTick(updatePosition);
}

function hide(): void {
  visible.value = false;
  stopListening();
}

function handleFocusOut(event: FocusEvent): void {
  if (event.relatedTarget instanceof Node && anchor.value?.contains(event.relatedTarget)) return;
  hide();
}

watch(() => props.disabled, disabled => {
  if (disabled) hide();
});

onUnmounted(stopListening);
</script>

<template>
  <span
    ref="anchor"
    class="cv-tooltip-anchor"
    data-design-primitive="tooltip"
    @mouseenter="show"
    @mouseleave="hide"
    @focusin="show"
    @focusout="handleFocusOut"
    @keydown.esc.stop="hide"
  >
    <slot :tooltip-id="tooltipId" />
  </span>
  <Teleport to="body">
    <span
      v-if="visible"
      :id="tooltipId"
      ref="tooltip"
      class="cv-tooltip"
      role="tooltip"
      :style="position"
    >{{ text }}</span>
  </Teleport>
</template>

<style scoped>
.cv-tooltip-anchor { display: inline-flex; min-width: 0; }
.cv-tooltip {
  position: fixed;
  z-index: var(--cv-z-tooltip);
  width: max-content;
  padding: var(--cv-space-2) var(--cv-space-3);
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-floating);
  box-shadow: var(--cv-elevation-floating);
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-xs);
  line-height: var(--cv-line-height-normal);
  overflow-wrap: anywhere;
  pointer-events: none;
}
</style>
