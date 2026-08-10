import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import {
  authLifecycleRootKey,
  type AuthLifecycleRoot
} from '@/app/auth';
import LoginPage from '@/app/pages/auth/LoginPage.vue';

function createRoot(options: {
  rememberedUsername?: string | null;
  login: ReturnType<typeof vi.fn>;
  setRememberedUsername: ReturnType<typeof vi.fn>;
}): AuthLifecycleRoot {
  const authProjection = reactive({
    phase: 'unauthenticated' as const,
    user: null,
    setupPolicy: null,
    sessionGeneration: 0,
    message: '请输入账号和密码登录。',
    errorCode: null,
    updatedAt: null
  });
  return {
    auth: {
      projection: authProjection,
      login: options.login
    },
    preferences: {
      projection: reactive({
        theme: 'light' as const,
        density: 'compact' as const,
        reducedMotion: false,
        rememberedUsername: options.rememberedUsername ?? null
      }),
      setRememberedUsername: options.setRememberedUsername
    }
  } as unknown as AuthLifecycleRoot;
}

describe('LoginPage remembered username preference', () => {
  it('preloads the single preference owner and persists only after successful authentication', async () => {
    const login = vi.fn()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true);
    const setRememberedUsername = vi.fn();
    const root = createRoot({
      rememberedUsername: 'engineer',
      login,
      setRememberedUsername
    });
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/login', component: LoginPage },
        { path: '/projects', component: { template: '<div>工程库</div>' } }
      ]
    });
    await router.push('/login');
    await router.isReady();

    const wrapper = mount(LoginPage, {
      global: {
        plugins: [router],
        provide: { [authLifecycleRootKey as symbol]: root }
      }
    });

    expect(wrapper.get<HTMLInputElement>('#login-username').element.value).toBe('engineer');
    expect(wrapper.get<HTMLInputElement>('input[type="checkbox"]').element.checked).toBe(true);
    expect(wrapper.find('button[aria-label="显示登录密码"]').exists()).toBe(true);
    await wrapper.get('#login-password').setValue('wrong');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(setRememberedUsername).not.toHaveBeenCalled();
    expect(router.currentRoute.value.path).toBe('/login');

    await wrapper.get('#login-password').setValue('correct');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(setRememberedUsername).toHaveBeenCalledOnce();
    expect(setRememberedUsername).toHaveBeenCalledWith('engineer');
    expect(router.currentRoute.value.path).toBe('/projects');
    wrapper.unmount();
  });
});
