<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, shallowRef } from 'vue';
import { RouterLink } from 'vue-router';
import { useProductRuntime } from '@/app/productRuntime';
import { CvInlineAlert, CvPageHeader, CvPageState, CvPanel } from '@/design-system';
import { createProjectsListQuery } from '@/capabilities/projects-read/projectQueries';

const runtime = useProductRuntime();
const search = shallowRef('');
const query = createProjectsListQuery(runtime.queries, () => search.value);
const projects = computed(() => query.state.value.data ?? []);

onMounted(() => query.refresh());
onBeforeUnmount(() => query.dispose());
</script>

<template>
  <section
    class="inspection-projects"
    data-testid="inspection-projects-page"
  >
    <CvPageHeader
      title="连续检测"
      description="选择一个已保存工程，进入唯一的连续检测运行页。"
    />
    <CvInlineAlert
      tone="info"
      compact
    >
      连续检测只运行后端确认的保存快照；本页不会发送草稿流程数据。
    </CvInlineAlert>
    <CvPanel
      title="选择工程"
      description="仅工程师和管理员可进入连续检测。"
    >
      <CvPageState
        v-if="query.state.value.phase === 'loading'"
        kind="loading"
        title="正在读取工程"
        description="请稍候。"
      />
      <CvPageState
        v-else-if="query.state.value.phase === 'error'"
        kind="error"
        title="工程读取失败"
        :description="query.state.value.failure?.message"
      />
      <CvPageState
        v-else-if="projects.length === 0"
        kind="empty"
        title="暂无可检测工程"
        description="请先创建并保存工程。"
      />
      <ul
        v-else
        class="inspection-projects__list"
      >
        <li
          v-for="project in projects"
          :key="project.id"
        >
          <div>
            <strong>{{ project.name }}</strong>
            <span>保存修订 {{ project.persistenceRevision }} · {{ project.description || '无描述' }}</span>
          </div>
          <RouterLink
            :to="`/projects/${project.id}/inspection`"
            data-testid="inspection-project-open"
          >
            进入连续检测
          </RouterLink>
        </li>
      </ul>
    </CvPanel>
  </section>
</template>

<style scoped>
.inspection-projects { display: grid; max-width: 1180px; gap: var(--cv-density-page-gap); }
.inspection-projects__list { margin: 0; padding: 0; list-style: none; }
.inspection-projects__list li { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-4); padding: var(--cv-space-3) var(--cv-density-panel-padding); border-top: 1px solid var(--cv-border-subtle); }
.inspection-projects__list div { display: grid; min-width: 0; gap: 2px; }
.inspection-projects__list strong { color: var(--cv-text-primary); }
.inspection-projects__list strong, .inspection-projects__list span { overflow-wrap: anywhere; }
.inspection-projects__list span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.inspection-projects__list a { color: var(--cv-color-link); font-weight: var(--cv-font-weight-medium); white-space: nowrap; }
</style>
