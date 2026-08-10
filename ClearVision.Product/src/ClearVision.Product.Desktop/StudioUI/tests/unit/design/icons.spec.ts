import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { CvIcon } from '@/design-system/icons';
import type { CvIconName } from '@/design-system/icons';

describe('CvIcon', () => {
  it.each([
    'search',
    'close',
    'chevron-left',
    'chevron-right',
    'more-horizontal',
    'info',
    'warning',
    'error',
    'success',
    'lock',
    'empty',
    'not-found',
    'refresh'
  ] as const)('renders the minimal %s glyph without focus ownership', name => {
    const wrapper = mount(CvIcon, { props: { name: name as CvIconName } });
    expect(wrapper.attributes('aria-hidden')).toBe('true');
    expect(wrapper.attributes('focusable')).toBe('false');
  });

  it('supports an explicit accessible label for standalone use', () => {
    const wrapper = mount(CvIcon, { props: { name: 'info', label: '系统信息' } });
    expect(wrapper.attributes('role')).toBe('img');
    expect(wrapper.attributes('aria-label')).toBe('系统信息');
    expect(wrapper.attributes('aria-hidden')).toBeUndefined();
  });
});
