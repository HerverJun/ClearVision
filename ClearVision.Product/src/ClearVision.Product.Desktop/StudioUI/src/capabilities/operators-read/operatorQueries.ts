import type { ReadQueryClient, ReadQueryDefinition, ReadQueryOwner } from '@/platform/query';
import {
  decodeOperatorCatalog,
  decodeOperatorCatalogItem,
  isOperatorType,
  type OperatorCatalogItem
} from './operatorContracts';

export const operatorCatalogPath = 'operators/library?includeCompatibility=true';

export function createOperatorDetailPath(operatorType: string): string {
  if (!isOperatorType(operatorType)) throw new TypeError('Operator type must be a numeric or enum identifier.');
  return `operators/${encodeURIComponent(operatorType)}/metadata`;
}

export function createOperatorCatalogDefinition(): ReadQueryDefinition<readonly OperatorCatalogItem[]> {
  return Object.freeze({
    key: 'operators:catalog:compatibility',
    path: operatorCatalogPath,
    decode: decodeOperatorCatalog,
    isEmpty: (items: readonly OperatorCatalogItem[]) => items.length === 0,
    protected: true,
    cacheTimeMs: 30_000
  });
}

export function createOperatorCatalogQuery(
  client: ReadQueryClient
): ReadQueryOwner<readonly OperatorCatalogItem[]> {
  return client.createQuery(createOperatorCatalogDefinition());
}

export function createOperatorDetailDefinition(
  operatorType: () => string
): ReadQueryDefinition<OperatorCatalogItem> {
  return Object.freeze({
    key: () => `operators:detail:${operatorType()}`,
    path: () => createOperatorDetailPath(operatorType()),
    decode: decodeOperatorCatalogItem,
    protected: true,
    cacheTimeMs: 30_000
  });
}

export function createOperatorDetailQuery(
  client: ReadQueryClient,
  operatorType: () => string
): ReadQueryOwner<OperatorCatalogItem> {
  return client.createQuery(createOperatorDetailDefinition(operatorType));
}
