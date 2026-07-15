<script setup lang="ts">
import { nextTick, onUnmounted, ref, useId, watch } from 'vue';

const props = withDefaults(defineProps<{
  open: boolean;
  title: string;
  description?: string | undefined;
  closeLabel?: string;
  closeOnBackdrop?: boolean;
  size?: 'sm' | 'md' | 'lg';
}>(), {
  description: undefined,
  closeLabel: '关闭对话框',
  closeOnBackdrop: true,
  size: 'md'
});

const emit = defineEmits<{
  close: [];
}>();

const dialog = ref<HTMLElement>();
const generatedId = useId();
const titleId = `cv-modal-${generatedId}-title`;
const descriptionId = `cv-modal-${generatedId}-description`;
let previousFocus: HTMLElement | null = null;
let listening = false;

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])'
].join(',');

function focusableElements(): HTMLElement[] {
  if (!dialog.value) return [];
  return [...dialog.value.querySelectorAll<HTMLElement>(focusableSelector)]
    .filter(element => element.getAttribute('aria-hidden') !== 'true' && !element.hasAttribute('hidden'));
}

function focusInitialElement(): void {
  const preferred = dialog.value?.querySelector<HTMLElement>('[data-modal-initial-focus]');
  const target = preferred ?? focusableElements()[0] ?? dialog.value;
  target?.focus();
}

function handleKeydown(event: KeyboardEvent): void {
  if (!props.open) return;

  if (event.key === 'Escape') {
    event.preventDefault();
    emit('close');
    return;
  }

  if (event.key !== 'Tab') return;
  const elements = focusableElements();
  if (elements.length === 0) {
    event.preventDefault();
    dialog.value?.focus();
    return;
  }

  const first = elements[0];
  const last = elements[elements.length - 1];
  if (!first || !last) return;

  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

function startListening(): void {
  if (listening) return;
  listening = true;
  document.addEventListener('keydown', handleKeydown);
}

function stopListening(): void {
  if (!listening) return;
  listening = false;
  document.removeEventListener('keydown', handleKeydown);
}

function restoreFocus(): void {
  const target = previousFocus;
  previousFocus = null;
  if (!target?.isConnected) return;
  void nextTick(() => target.focus());
}

function requestBackdropClose(): void {
  if (props.closeOnBackdrop) emit('close');
}

watch(() => props.open, open => {
  if (open) {
    previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    startListening();
    void nextTick(focusInitialElement);
    return;
  }

  stopListening();
  restoreFocus();
}, { immediate: true, flush: 'post' });

onUnmounted(() => {
  stopListening();
  restoreFocus();
});
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="cv-modal-backdrop"
      data-design-primitive="modal"
      @mousedown.self="requestBackdropClose"
    >
      <section
        ref="dialog"
        class="cv-modal"
        :class="`cv-modal--${size}`"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="titleId"
        :aria-describedby="description ? descriptionId : undefined"
        tabindex="-1"
      >
        <header class="cv-modal__header">
          <div>
            <h2
              :id="titleId"
              class="cv-modal__title"
            >
              {{ title }}
            </h2>
            <p
              v-if="description"
              :id="descriptionId"
              class="cv-modal__description"
            >
              {{ description }}
            </p>
          </div>
          <button
            class="cv-modal__close"
            type="button"
            :aria-label="closeLabel"
            @click="emit('close')"
          >
            ×
          </button>
        </header>
        <div class="cv-modal__body">
          <slot />
        </div>
        <footer
          v-if="$slots.footer"
          class="cv-modal__footer"
        >
          <slot name="footer" />
        </footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.cv-modal-backdrop {
  position: fixed;
  z-index: var(--cv-z-modal);
  inset: 0;
  display: grid;
  place-items: center;
  padding: var(--cv-space-6);
  background: var(--cv-backdrop);
  animation: cv-modal-fade var(--cv-motion-duration-normal) var(--cv-motion-ease-standard);
}
.cv-modal {
  display: flex;
  max-width: calc(100vw - (2 * var(--cv-space-6)));
  max-height: calc(100vh - (2 * var(--cv-space-6)));
  flex-direction: column;
  overflow: hidden;
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-xl);
  background: var(--cv-surface-overlay);
  box-shadow: var(--cv-elevation-modal);
  animation: cv-modal-enter var(--cv-motion-duration-normal) var(--cv-motion-ease-emphasized);
}
.cv-modal--sm { width: min(420px, 100%); }
.cv-modal--md { width: min(600px, 100%); }
.cv-modal--lg { width: min(840px, 100%); }
.cv-modal__header { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); padding: var(--cv-space-5) var(--cv-space-6); border-bottom: 1px solid var(--cv-border-subtle); }
.cv-modal__title { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xl); line-height: var(--cv-line-height-tight); }
.cv-modal__description { margin: var(--cv-space-2) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-normal); }
.cv-modal__close { display: grid; width: var(--cv-density-control-height-sm); height: var(--cv-density-control-height-sm); flex: 0 0 auto; place-items: center; padding: 0; border: 0; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-text-secondary); cursor: pointer; font-size: 22px; line-height: 1; }
.cv-modal__close:hover { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.cv-modal__body { min-height: 0; overflow: auto; padding: var(--cv-space-6); }
.cv-modal__footer { display: flex; justify-content: flex-end; gap: var(--cv-space-2); padding: var(--cv-space-4) var(--cv-space-6); border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-2); }
@keyframes cv-modal-fade { from { opacity: 0; } }
@keyframes cv-modal-enter { from { opacity: 0; transform: translateY(var(--cv-motion-distance)) scale(0.985); } }
@media (max-height: 620px) {
  .cv-modal-backdrop { align-items: start; padding-block: var(--cv-space-3); }
  .cv-modal { max-height: calc(100vh - (2 * var(--cv-space-3))); }
}
</style>
