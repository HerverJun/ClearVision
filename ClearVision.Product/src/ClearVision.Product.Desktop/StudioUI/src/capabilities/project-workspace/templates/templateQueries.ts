import type { ReadQueryClient, ReadQueryDefinition, ReadQueryOwner } from '@/platform/query';
import { decodeFlowTemplateList, type FlowTemplateV1 } from './templateContracts';

export const templateListPath = 'templates';

export function createTemplateListDefinition(industry: () => string): ReadQueryDefinition<readonly FlowTemplateV1[]> {
  return Object.freeze({
    key: () => `templates:list:${industry().trim()}`,
    path: () => {
      const value = industry().trim();
      return value ? `${templateListPath}?industry=${encodeURIComponent(value)}` : templateListPath;
    },
    decode: decodeFlowTemplateList,
    isEmpty: (items: readonly FlowTemplateV1[]) => items.length === 0,
    protected: true,
    cacheTimeMs: 10_000
  });
}

export function createTemplateListQuery(
  client: ReadQueryClient,
  industry: () => string
): ReadQueryOwner<readonly FlowTemplateV1[]> {
  return client.createQuery(createTemplateListDefinition(industry));
}

export function createTemplateDetailPath(id: string): string {
  const value = id.trim();
  if (!value || value.includes('/') || value.includes('\\')) throw new TypeError('模板标识无效。');
  return `templates/${encodeURIComponent(value)}`;
}
