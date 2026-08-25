import { mount } from '@vue/test-utils';
import { reactive, shallowRef } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { authLifecycleRootKey, type AuthLifecycleRoot } from '@/app/auth';
import ForbiddenPage from '@/app/pages/ForbiddenPage.vue';
import SetupPage from '@/app/pages/auth/SetupPage.vue';

afterEach(() => {
  document.body.innerHTML = '';
});

function createPageRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/setup', component: SetupPage },
      { path: '/forbidden', component: ForbiddenPage },
      { path: '/projects', component: { template: '<div data-test-projects />' } },
      { path: '/login', component: { template: '<div data-test-login />' } }
    ]
  });
}

describe('G2 AuthShell derived pages', () => {
  it('lets setup recover an accepted session without adding another auth workflow', async () => {
    const router = createPageRouter();
    await router.push('/setup');
    await router.isReady();
    const projection = reactive({
      phase: 'protected-transition',
      user: null,
      setupPolicy: {
        usernameMinLength: 3,
        passwordMinLength: 6,
        requiresUppercase: false,
        requiresLowercase: false,
        requiresDigit: false
      },
      sessionGeneration: 1,
      message: '已重新认证，正在恢复未完成的工作。',
      errorCode: null,
      updatedAt: 1
    });
    const refreshSession = vi.fn(async () => {
      projection.phase = 'authenticated';
    });
    const authRoot = {
      auth: {
        projection,
        setupAdmin: vi.fn(async () => false),
        refreshSession
      },
      productRuntime: shallowRef(null)
    } as unknown as AuthLifecycleRoot;

    const wrapper = mount(SetupPage, {
      attachTo: document.body,
      global: {
        plugins: [router],
        provide: { [authLifecycleRootKey as symbol]: authRoot }
      }
    });
    const recovery = wrapper.findAll('button').find(button => button.text() === '重新确认会话');
    expect(recovery).toBeDefined();
    await recovery!.trigger('click');
    expect(refreshSession).toHaveBeenCalledTimes(1);
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/projects'));
    wrapper.unmount();
  });

  it('keeps D24 to the single approved return command', async () => {
    const router = createPageRouter();
    await router.push('/forbidden');
    await router.isReady();
    const wrapper = mount(ForbiddenPage, {
      attachTo: document.body,
      global: { plugins: [router] }
    });

    const controls = wrapper.findAll('a, button, input, select, textarea');
    expect(controls).toHaveLength(1);
    expect(controls[0]?.element.tagName).toBe('A');
    expect(controls[0]?.text()).toContain('返回工程库');
    expect(wrapper.text()).not.toMatch(/重试|申请权限|修改角色|权限编辑|支持聊天/);
    expect(wrapper.findAll('main')).toHaveLength(1);
    wrapper.unmount();
  });
});
