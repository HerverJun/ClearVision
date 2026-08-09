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
        v-else-if="query.state.value.phase === 'unauthorized'"
        kind="unauthorized"
        title="会话已失效"
        description="当前会话已失效，不能读取可检测工程。请重新认证后再试。"
      />
      <CvPageState
        v-else-if="query.state.value.phase === 'forbidden'"
        kind="forbidden"
        title="无权读取检测工程"
        description="当前账号没有读取连续检测工程列表的权限。"
      />
      <CvPageState
        v-else-if="query.state.value.phase === 'aborted'"
        kind="error"
        title="工程读取已取消"
        description="当前读取请求已被新的页面生命周期取消，没有把取消结果当作空工程。"
      />
      <CvPageState
        v-else-if="query.state.value.phase === 'stale' && projects.length === 0"
        kind="error"
        title="工程列表已过期"
        description="服务暂时不可用，当前没有可安全展示的旧工程列表。请重试。"
      />
      <CvPageState
        v-else-if="query.state.value.phase === 'partial-failure' && projects.length === 0"
        kind="error"
        title="工程列表读取不完整"
        description="部分服务响应失败，未把不完整结果显示为暂无工程。请重试。"
      />
      <CvPageState
        v-else-if="query.state.value.phase === 'error' || query.state.value.phase === 'not-found'"
        kind="error"
        title="工程读取失败"
        :description="query.state.value.failure?.message"
      />
      <CvPageState
        v-else-if="(query.state.value.phase === 'empty' || query.state.value.phase === 'success') && projects.length === 0"
        kind="empty"
        title="暂无可检测工程"
        description="请先创建并保存工程。"
      />
      <CvInlineAlert
        v-if="query.state.value.phase === 'stale' && projects.length > 0"
        tone="warning"
        compact
      >
        当前显示的是上一次成功读取的工程列表，最新刷新失败；不会把过期列表当作最新权限结果。
      </CvInlineAlert>
      <CvInlineAlert
        v-if="query.state.value.phase === 'partial-failure' && projects.length > 0"
        tone="warning"
        compact
      >
        工程列表部分读取失败，当前内容可能不完整；请刷新后再进入连续检测。
      </CvInlineAlert>
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
