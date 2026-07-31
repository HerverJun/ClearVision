<script setup lang="ts">
import { nextTick, onBeforeUnmount, useId, useTemplateRef, watch } from 'vue';
import { CvIcon } from '@/design-system/icons';
import { CvIconButton } from '@/design-system/primitives';

const props = defineProps<{
  open: boolean;
  title: string;
  description: string;
}>();

const emit = defineEmits<{
  close: [];
}>();

const panel = useTemplateRef<HTMLElement>('panel');
const generatedId = useId();
const titleId = `ai-drawer-${generatedId}-title`;
const descriptionId = `ai-drawer-${generatedId}-description`;
let restoreTarget: HTMLElement | null = null;

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])'
].join(',');

function focusableElements(): HTMLElement[] {
  return panel.value
    ? [...panel.value.querySelectorAll<HTMLElement>(focusableSelector)].filter(element => !element.hidden)
    : [];
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault();
    emit('close');
    return;
  }
  if (event.key !== 'Tab') return;
  const focusable = focusableElements();
  if (focusable.length === 0) {
    event.preventDefault();
    panel.value?.focus();
    return;
  }
  const first = focusable[0];
  const last = focusable.at(-1);
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last?.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first?.focus();
  }
}

watch(() => props.open, async (open) => {
  if (open) {
    restoreTarget = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    await nextTick();
    const first = focusableElements()[0];
    if (first) first.focus();
    else panel.value?.focus();
    return;
  }
  restoreTarget?.focus();
  restoreTarget = null;
}, { flush: 'post' });

onBeforeUnmount(() => {
  restoreTarget?.focus();
  restoreTarget = null;
});
</script>

<template>
  <Teleport to="body">
    <Transition name="ai-drawer">
      <div
        v-if="open"
        class="ai-drawer-backdrop"
        @click.self="emit('close')"
      >
        <aside
          ref="panel"
          class="ai-drawer"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="titleId"
          :aria-describedby="descriptionId"
          tabindex="-1"
          @keydown="handleKeydown"
        >
          <header class="ai-drawer__header">
            <div class="ai-drawer__heading">
              <h2 :id="titleId">
                {{ title }}
              </h2>
              <p :id="descriptionId">
                {{ description }}
              </p>
            </div>
            <CvIconButton
              label="关闭抽屉"
              size="sm"
              @click="emit('close')"
            >
              <CvIcon
                name="close"
                size="sm"
              />
            </CvIconButton>
          </header>
          <div class="ai-drawer__body">
            <slot />
          </div>
          <footer
            v-if="$slots.footer"
            class="ai-drawer__footer"
          >
            <slot name="footer" />
          </footer>
        </aside>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.ai-drawer-backdrop {
  position: fixed;
  z-index: var(--cv-z-modal);
  inset: 0;
  display: flex;
  justify-content: flex-end;
  background: color-mix(in srgb, var(--cv-surface-app) 54%, transparent);
}

.ai-drawer {
  display: grid;
  width: min(560px, calc(100vw - var(--cv-space-5)));
  min-width: 0;
  height: 100%;
  grid-template-rows: auto minmax(0, 1fr) auto;
  border-inline-start: 1px solid var(--cv-border-strong);
  background: var(--cv-surface-raised);
  box-shadow: var(--cv-elevation-modal);
  color: var(--cv-text-primary);
  overscroll-behavior: contain;
}

.ai-drawer__header {
  display: flex;
  min-width: 0;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--cv-space-4);
  padding: var(--cv-space-4) var(--cv-space-5);
  border-block-end: 1px solid var(--cv-border-subtle);
}

.ai-drawer__heading { min-width: 0; }
.ai-drawer__heading h2 { margin: 0; font-size: var(--cv-font-size-lg); line-height: var(--cv-line-height-tight); text-wrap: balance; }
.ai-drawer__heading p { max-width: 64ch; margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); overflow-wrap: anywhere; }
.ai-drawer__body { min-width: 0; min-height: 0; overflow: auto; overscroll-behavior: contain; padding: var(--cv-space-4) var(--cv-space-5); }
.ai-drawer__footer { display: flex; min-width: 0; justify-content: flex-end; gap: var(--cv-space-2); padding: var(--cv-space-3) var(--cv-space-5); border-block-start: 1px solid var(--cv-border-subtle); background: var(--cv-surface-2); }

.ai-drawer-enter-active,
.ai-drawer-leave-active { transition: opacity var(--cv-motion-duration-normal) var(--cv-motion-ease-standard); }
.ai-drawer-enter-active .ai-drawer,
.ai-drawer-leave-active .ai-drawer { transition: transform var(--cv-motion-duration-normal) var(--cv-motion-ease-emphasized); }
.ai-drawer-enter-from,
.ai-drawer-leave-to { opacity: 0; }
.ai-drawer-enter-from .ai-drawer,
.ai-drawer-leave-to .ai-drawer { transform: translateX(24px); }

@media (max-width: 640px) {
  .ai-drawer { width: 100vw; }
  .ai-drawer__header,
  .ai-drawer__body,
  .ai-drawer__footer { padding-inline: var(--cv-space-4); }
}

@media (prefers-reduced-motion: reduce) {
  .ai-drawer-enter-active,
  .ai-drawer-leave-active,
  .ai-drawer-enter-active .ai-drawer,
  .ai-drawer-leave-active .ai-drawer { transition-duration: 1ms; }
  .ai-drawer-enter-from .ai-drawer,
  .ai-drawer-leave-to .ai-drawer { transform: none; }
}
</style>
