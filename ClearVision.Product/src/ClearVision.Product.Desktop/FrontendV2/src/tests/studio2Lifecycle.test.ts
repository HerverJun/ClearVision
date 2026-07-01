import { describe, expect, it } from 'vitest';
import { Studio2LifecycleScope } from '@/foundation/studio2Lifecycle';

describe('Studio2LifecycleScope', () => {
  it('does not create a second app for repeated mount calls', () => {
    const scope = new Studio2LifecycleScope();
    let created = 0;
    let unmounted = 0;

    const first = scope.mountApp(() => {
      created += 1;
      return { unmount: () => { unmounted += 1; } };
    });
    const second = scope.mountApp(() => {
      created += 1;
      return { unmount: () => { unmounted += 1; } };
    });

    expect(second).toBe(first);
    expect(created).toBe(1);
    expect(scope.getTelemetry().appInstanceCount).toBe(1);

    scope.dispose();
    scope.dispose();

    expect(unmounted).toBe(1);
    expect(scope.getTelemetry()).toEqual({
      appInstanceCount: 0,
      listenerCount: 0,
      timerCount: 0,
      observerCount: 0,
      abortControllerCount: 0,
      registryRegistrationCount: 0,
      blobUrlCount: 0,
      pendingRequestCount: 0
    });
  });

  it('cleans all owned resources across 20 mount and dispose cycles', () => {
    for (let index = 0; index < 20; index += 1) {
      const scope = new Studio2LifecycleScope();
      let listenerCount = 0;
      let timerCount = 0;
      let observerCount = 0;
      let registryCount = 0;
      const revokedUrls: string[] = [];

      scope.mountApp(() => ({ unmount: () => {} }));
      scope.trackListener(() => { listenerCount += 1; });
      scope.trackTimer(() => { timerCount += 1; });
      scope.trackObserver({ disconnect: () => { observerCount += 1; } });
      scope.createAbortController();
      scope.trackRegistryRegistration({ unregister: () => { registryCount += 1; } });
      const blobUrl = `blob:studio2-${String(index)}`;
      scope.trackBlobUrl(blobUrl, (url) => {
        revokedUrls.push(url);
      });
      void scope.trackPendingRequest(new Promise(() => {}));

      expect(scope.getTelemetry()).toEqual({
        appInstanceCount: 1,
        listenerCount: 1,
        timerCount: 1,
        observerCount: 1,
        abortControllerCount: 1,
        registryRegistrationCount: 1,
        blobUrlCount: 1,
        pendingRequestCount: 1
      });

      scope.dispose();
      scope.dispose();

      expect(listenerCount).toBe(1);
      expect(timerCount).toBe(1);
      expect(observerCount).toBe(1);
      expect(registryCount).toBe(1);
      expect(revokedUrls).toEqual([blobUrl]);
      expect(scope.getTelemetry()).toEqual({
        appInstanceCount: 0,
        listenerCount: 0,
        timerCount: 0,
        observerCount: 0,
        abortControllerCount: 0,
        registryRegistrationCount: 0,
        blobUrlCount: 0,
        pendingRequestCount: 0
      });
    }
  });

  it('cancels pending requests only through explicit callbacks', () => {
    const scope = new Studio2LifecycleScope();
    let cancelCount = 0;

    void scope.trackPendingRequest(new Promise(() => {}), () => {
      cancelCount += 1;
    });

    expect(scope.getTelemetry().pendingRequestCount).toBe(1);

    scope.dispose();
    scope.dispose();

    expect(cancelCount).toBe(1);
    expect(scope.getTelemetry().pendingRequestCount).toBe(0);
  });
});
