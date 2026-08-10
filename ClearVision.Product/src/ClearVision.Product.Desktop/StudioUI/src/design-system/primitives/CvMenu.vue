<script setup lang="ts">
import { nextTick, onUnmounted, ref, useId, watch, type CSSProperties } from 'vue';
import CvIcon from '../icons/CvIcon.vue';

const props = withDefaults(defineProps<{
  modelValue: boolean;
  label: string;
  triggerLabel?: string;
  triggerTitle?: string | undefined;
  align?: 'start' | 'end';
}>(), {
  triggerLabel: '打开菜单',
  triggerTitle: undefined,
  align: 'end'
});

const emit = defineEmits<{
  'update:modelValue': [open: boolean];
  open: [];
  close: [];
  select: [value: string];
}>();

const trigger = ref<HTMLButtonElement>();
const menu = ref<HTMLElement>();
const menuStyle = ref<CSSProperties>({});
const menuId = `cv-menu-${useId()}`;
let listening = false;
let restoreFocusOnClose = true;

function menuItems(): HTMLElement[] {
  if (!menu.value) return [];
  return [...menu.value.querySelectorAll<HTMLElement>('[role="menuitem"], [role="menuitemcheckbox"], [role="menuitemradio"]')]
    .filter(item => item.getAttribute('aria-disabled') !== 'true' && !item.hasAttribute('disabled'));
}

function updatePosition(): void {
  if (!trigger.value || !menu.value || !props.modelValue) return;
  const anchorRect = trigger.value.getBoundingClientRect();
  const menuRect = menu.value.getBoundingClientRect();
  const viewportGap = 8;
  const anchorGap = 5;
  let top = anchorRect.bottom + anchorGap;
  if (top + menuRect.height > window.innerHeight - viewportGap) {
    top = Math.max(viewportGap, anchorRect.top - menuRect.height - anchorGap);
  }
  let left = props.align === 'start' ? anchorRect.left : anchorRect.right - menuRect.width;
  left = Math.min(window.innerWidth - menuRect.width - viewportGap, Math.max(viewportGap, left));
  menuStyle.value = { top: `${Math.round(top)}px`, left: `${Math.round(left)}px` };
}

function requestOpen(): void {
  if (props.modelValue) return;
  restoreFocusOnClose = true;
  emit('update:modelValue', true);
  emit('open');
}

function requestClose(restoreFocus: boolean): void {
  if (!props.modelValue) return;
  restoreFocusOnClose = restoreFocus;
  emit('update:modelValue', false);
  emit('close');
}

function toggle(): void {
  if (props.modelValue) requestClose(true);
  else requestOpen();
}

function handleDocumentPointerDown(event: PointerEvent): void {
  const target = event.target;
  if (!(target instanceof Node)) return;
  if (trigger.value?.contains(target) || menu.value?.contains(target)) return;
  requestClose(false);
}

function startListening(): void {
  if (listening) return;
  listening = true;
  document.addEventListener('pointerdown', handleDocumentPointerDown, true);
  window.addEventListener('resize', updatePosition);
  window.addEventListener('scroll', updatePosition, true);
}

function stopListening(): void {
  if (!listening) return;
  listening = false;
  document.removeEventListener('pointerdown', handleDocumentPointerDown, true);
  window.removeEventListener('resize', updatePosition);
  window.removeEventListener('scroll', updatePosition, true);
}

function focusItem(index: number): void {
  const items = menuItems();
  if (items.length === 0) {
    menu.value?.focus();
    return;
  }
  items[(index + items.length) % items.length]?.focus();
}

function handleMenuKeydown(event: KeyboardEvent): void {
  const items = menuItems();
  const activeIndex = items.findIndex(item => item === document.activeElement);
  if (event.key === 'Escape') {
    event.preventDefault();
    requestClose(true);
  } else if (event.key === 'ArrowDown') {
    event.preventDefault();
    focusItem(activeIndex + 1);
  } else if (event.key === 'ArrowUp') {
    event.preventDefault();
    focusItem(activeIndex <= 0 ? items.length - 1 : activeIndex - 1);
  } else if (event.key === 'Home') {
    event.preventDefault();
    focusItem(0);
  } else if (event.key === 'End') {
    event.preventDefault();
    focusItem(items.length - 1);
  } else if (event.key === 'Tab') {
    requestClose(false);
  }
}

function handleMenuClick(event: MouseEvent): void {
  const target = event.target;
  if (!(target instanceof Element)) return;
  const item = target.closest<HTMLElement>('[role="menuitem"], [role="menuitemcheckbox"], [role="menuitemradio"]');
  if (!item || item.getAttribute('aria-disabled') === 'true' || item.hasAttribute('disabled')) return;
  const value = item.dataset.menuValue;
  if (value) emit('select', value);
  requestClose(true);
}

watch(() => props.modelValue, open => {
  if (open) {
    startListening();
    void nextTick(() => {
      updatePosition();
      focusItem(0);
    });
    return;
  }
  stopListening();
  if (restoreFocusOnClose) void nextTick(() => trigger.value?.focus());
}, { immediate: true, flush: 'post' });

onUnmounted(stopListening);
</script>

<template>
  <span
    class="cv-menu-anchor"
    data-design-primitive="menu"
  >
    <button
      ref="trigger"
      class="cv-menu-trigger"
      type="button"
      :aria-label="triggerLabel"
      :title="triggerTitle ?? triggerLabel"
      aria-haspopup="menu"
      :aria-expanded="modelValue ? 'true' : 'false'"
      :aria-controls="modelValue ? menuId : undefined"
      @click="toggle"
      @keydown.down.prevent="requestOpen"
      @keydown.up.prevent="requestOpen"
    >
      <slot name="trigger">
        <span>{{ triggerLabel }}</span>
        <CvIcon
          class="cv-menu-trigger__chevron"
          name="chevron-right"
          size="sm"
        />
      </slot>
    </button>
  </span>
  <Teleport to="body">
    <div
      v-if="modelValue"
      :id="menuId"
      ref="menu"
      class="cv-menu"
      role="menu"
      :aria-label="label"
      :style="menuStyle"
      tabindex="-1"
      @keydown="handleMenuKeydown"
      @click="handleMenuClick"
    >
      <slot />
    </div>
  </Teleport>
</template>

<style scoped>
.cv-menu-anchor { display: inline-flex; min-width: 0; }
.cv-menu-trigger {
  display: inline-flex;
  min-height: var(--cv-density-control-height);
  align-items: center;
  justify-content: center;
  gap: var(--cv-space-2);
  padding: 0 var(--cv-space-3);
  border: 1px solid transparent;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-secondary);
  cursor: pointer;
  font-size: var(--cv-font-size-sm);
}
.cv-menu-trigger:hover,
.cv-menu-trigger[aria-expanded="true"] { border-color: var(--cv-border-subtle); background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.cv-menu-trigger:focus-visible { outline: 0; box-shadow: var(--cv-focus-ring); }
.cv-menu-trigger__chevron { transform: rotate(90deg); }
.cv-menu {
  position: fixed;
  z-index: var(--cv-z-dropdown);
  display: grid;
  width: max-content;
  min-width: 196px;
  max-width: min(340px, calc(100vw - 16px));
  max-height: min(420px, calc(100vh - 16px));
  gap: 2px;
  overflow: auto;
  overscroll-behavior: contain;
  padding: var(--cv-space-1);
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-md);
  background: var(--cv-surface-floating);
  box-shadow: var(--cv-elevation-floating);
}
.cv-menu:focus-visible { outline: 0; box-shadow: var(--cv-focus-ring), var(--cv-elevation-floating); }
</style>
