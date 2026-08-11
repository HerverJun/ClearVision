import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import {
  authLifecycleRootKey,
  type AuthLifecycleRoot
} from '@/app/auth';
import ChangePasswordPage from '@/app/pages/auth/ChangePasswordPage.vue';

function createRoot(changePassword: ReturnType<typeof vi.fn>): AuthLifecycleRoot {
  return {
    auth: {
      projection: reactive({
        phase: 'authenticated' as const,
        user: null,
        setupPolicy: { passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false },
        sessionGeneration: 1,
        message: '请输入当前密码和新密码。',
        errorCode: null,
        updatedAt: null
      }),
      changePassword
    },
    preferences: { projection: reactive({ theme: 'light', density: 'compact', reducedMotion: false }) }
  } as unknown as AuthLifecycleRoot;
}

describe('ChangePasswordPage validation accessibility', () => {
  it('associates a confirmation mismatch with the field and moves focus to it', async () => {
    const changePassword = vi.fn();
    const root = createRoot(changePassword);
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/change-password', component: ChangePasswordPage },
        { path: '/projects', component: { template: '<div>工程库</div>' } }
      ]
    });
    await router.push('/change-password');
    await router.isReady();
    const host = document.createElement('div');
    document.body.append(host);
    const wrapper = mount(ChangePasswordPage, {
      attachTo: host,
      global: {
        plugins: [router],
        provide: { [authLifecycleRootKey as symbol]: root }
      }
    });

    await wrapper.get('#old-password').setValue('old-password');
    await wrapper.get('#new-password').setValue('new-password');
    await wrapper.get('#confirm-new-password').setValue('different-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    const confirmation = wrapper.get<HTMLInputElement>('#confirm-new-password');
    expect(changePassword).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(confirmation.element);
    expect(confirmation.attributes('aria-invalid')).toBe('true');
    expect(confirmation.attributes('aria-describedby')).toBe('confirm-new-password-error');
    const error = wrapper.get('#confirm-new-password-error');
    expect(error.attributes('role')).toBe('alert');
    expect(error.text()).toBe('两次输入的新密码不一致。');

    await wrapper.get('#old-password').setValue('corrected-old-password');
    await wrapper.get('#confirm-new-password').setValue('still-different');
    expect(wrapper.get('#confirm-new-password-error').text()).toBe('两次输入的新密码不一致。');
    expect(confirmation.attributes('aria-invalid')).toBe('true');

    await wrapper.get('#confirm-new-password').setValue('new-password');
    expect(wrapper.find('#confirm-new-password-error').exists()).toBe(false);
    expect(confirmation.attributes('aria-invalid')).toBeUndefined();

    wrapper.unmount();
    host.remove();
  });
});
