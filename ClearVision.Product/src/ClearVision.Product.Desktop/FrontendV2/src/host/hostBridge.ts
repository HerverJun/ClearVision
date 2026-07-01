import type { LegacyWebMessageBridge } from '@/adapters/legacyModules';

export interface Studio2HostBridge {
  readonly kind: 'legacy-web-message-bridge';
  onHostMessage(type: string, handler: (payload: unknown) => void): () => void;
  sendHostMessage(type: string, payload?: unknown, expectResponse?: boolean): Promise<unknown>;
  dispose(): void;
}

export function createHostBridge(legacyBridge: LegacyWebMessageBridge): Studio2HostBridge {
  const disposers = new Set<() => void>();

  return {
    kind: 'legacy-web-message-bridge',
    onHostMessage(type, handler) {
      const unsubscribe = legacyBridge.on(type, handler);
      disposers.add(unsubscribe);
      return () => {
        disposers.delete(unsubscribe);
        unsubscribe();
      };
    },
    sendHostMessage(type, payload, expectResponse = false) {
      return legacyBridge.sendMessage(type, payload, expectResponse);
    },
    dispose() {
      for (const unsubscribe of [...disposers]) {
        unsubscribe();
      }
      disposers.clear();
    }
  };
}
