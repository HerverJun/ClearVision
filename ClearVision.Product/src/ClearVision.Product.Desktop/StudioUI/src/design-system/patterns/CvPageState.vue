<script setup lang="ts">
import { computed } from 'vue';
import CvIcon from '../icons/CvIcon.vue';
import type { CvIconName } from '../icons/types';
import type { CvPageStateKind } from './types';

const props = withDefaults(defineProps<{
  kind: CvPageStateKind;
  title?: string | undefined;
  description?: string | undefined;
  compact?: boolean;
  headingLevel?: 1 | 2 | 3;
}>(), {
  title: undefined,
  description: undefined,
  compact: false,
  headingLevel: 2
});

const defaultCopy: Readonly<Record<CvPageStateKind, { title: string; description: string; icon: CvIconName }>> = Object.freeze({
  loading: { title: '正在加载', description: '请稍候，正在读取最新数据。', icon: 'refresh' },
  empty: { title: '暂无数据', description: '当前没有可显示的内容。', icon: 'empty' },
  error: { title: '加载失败', description: '暂时无法读取数据，请稍后重试。', icon: 'error' },
  unauthorized: { title: '需要登录', description: '当前没有可用的预置会话，无法访问此页面。', icon: 'lock' },
  forbidden: { title: '无权访问', description: '当前用户没有访问此内容的权限。', icon: 'lock' },
  'not-found': { title: '页面不存在', description: '请求的页面不存在或已被移除。', icon: 'not-found' }
});

const copy = computed(() => defaultCopy[props.kind]);
const resolvedTitle = computed(() => props.title ?? copy.value.title);
const resolvedDescription = computed(() => props.description ?? copy.value.description);
const stateRole = computed(() => props.kind === 'error'
  ? 'alert'
  : props.kind === 'loading' ? 'status' : undefined
);
const liveMode = computed(() => props.kind === 'error'
  ? 'assertive'
  : props.kind === 'loading' ? 'polite' : undefined
);
</script>

<template>
  <section
    class="cv-page-state"
    :class="[
      `cv-page-state--${kind}`,
      { 'cv-page-state--compact': compact }
    ]"
    :role="stateRole"
    :aria-live="liveMode"
    :aria-busy="kind === 'loading' ? 'true' : undefined"
    data-design-pattern="page-state"
    :data-page-state="kind"
  >
    <div
      class="cv-page-state__icon"
      :class="{ 'cv-page-state__icon--loading': kind === 'loading' }"
    >
      <CvIcon
        :name="copy.icon"
        size="lg"
      />
    </div>
    <div class="cv-page-state__copy">
      <component
        :is="`h${headingLevel}`"
        class="cv-page-state__title"
      >
        {{ resolvedTitle }}
      </component>
      <p>{{ resolvedDescription }}</p>
    </div>
    <div
      v-if="$slots.actions"
      class="cv-page-state__actions"
    >
      <slot name="actions" />
    </div>
  </section>
</template>

<style scoped>
.cv-page-state {
  display: grid;
  min-height: 168px;
  place-items: center;
  align-content: center;
  gap: var(--cv-space-2);
  padding: var(--cv-space-6) var(--cv-space-4);
  border-radius: var(--cv-radius-md);
  background: var(--cv-surface-page);
  text-align: center;
}

.cv-page-state--compact {
  min-height: 76px;
  grid-template-columns: auto minmax(0, 1fr) auto;
  place-items: center start;
  align-content: center;
  padding: var(--cv-space-3);
  text-align: left;
}

.cv-page-state__icon {
  display: grid;
  width: 36px;
  height: 36px;
  place-items: center;
  border-radius: var(--cv-radius-md);
  background: var(--cv-color-status-info-soft);
  color: var(--cv-color-status-info-strong);
}

.cv-page-state--error .cv-page-state__icon { background: var(--cv-color-status-error-soft); color: var(--cv-color-status-error-strong); }
.cv-page-state--unauthorized .cv-page-state__icon,
.cv-page-state--forbidden .cv-page-state__icon { background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong); }
.cv-page-state__icon--loading { animation: cv-page-state-spin var(--cv-motion-duration-slow) linear infinite; }
.cv-page-state__copy { min-width: 0; }

.cv-page-state__title {
  margin: 0;
  color: var(--cv-text-primary);
  font-size: var(--cv-type-section-title-size);
  font-weight: var(--cv-font-weight-semibold);
  line-height: var(--cv-line-height-tight);
}

.cv-page-state p {
  max-width: 54ch;
  margin: var(--cv-space-1) 0 0;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
  line-height: var(--cv-line-height-normal);
}

.cv-page-state__actions { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--cv-space-2); }
.cv-page-state--compact .cv-page-state__actions { justify-content: flex-end; }

@keyframes cv-page-state-spin { to { transform: rotate(360deg); } }

@media (max-width: 560px) {
  .cv-page-state--compact { grid-template-columns: auto minmax(0, 1fr); }
  .cv-page-state--compact .cv-page-state__actions { grid-column: 1 / -1; justify-content: flex-start; }
}
</style>
