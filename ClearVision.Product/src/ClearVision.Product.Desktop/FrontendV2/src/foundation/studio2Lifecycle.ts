export interface MountedStudio2App {
  unmount(): void;
}

export interface Studio2LifecycleTelemetry {
  readonly appInstanceCount: number;
  readonly listenerCount: number;
  readonly timerCount: number;
  readonly observerCount: number;
  readonly abortControllerCount: number;
  readonly registryRegistrationCount: number;
  readonly blobUrlCount: number;
  readonly pendingRequestCount: number;
}

export interface DisconnectableObserver {
  disconnect(): void;
}

export interface RegistryRegistration {
  unregister(): void;
}

export class Studio2LifecycleScope {
  private mountedApp: MountedStudio2App | null = null;
  private readonly listeners = new Set<() => void>();
  private readonly timers = new Set<() => void>();
  private readonly observers = new Set<DisconnectableObserver>();
  private readonly abortControllers = new Set<AbortController>();
  private readonly registryRegistrations = new Set<RegistryRegistration>();
  private readonly blobUrls = new Map<string, (url: string) => void>();
  private readonly pendingRequests = new Set<Promise<unknown>>();
  private disposed = false;

  mountApp(factory: () => MountedStudio2App): MountedStudio2App {
    if (this.mountedApp) {
      return this.mountedApp;
    }

    if (this.disposed) {
      throw new Error('Studio2 lifecycle scope has been disposed.');
    }

    this.mountedApp = factory();
    return this.mountedApp;
  }

  trackListener(unsubscribe: () => void): () => void {
    if (this.disposed) {
      unsubscribe();
      return () => {};
    }

    this.listeners.add(unsubscribe);
    return () => {
      if (this.listeners.delete(unsubscribe)) {
        unsubscribe();
      }
    };
  }

  trackTimer(clearTimer: () => void): void {
    if (this.disposed) {
      clearTimer();
      return;
    }

    this.timers.add(clearTimer);
  }

  trackObserver(observer: DisconnectableObserver): DisconnectableObserver {
    if (this.disposed) {
      observer.disconnect();
      return observer;
    }

    this.observers.add(observer);
    return observer;
  }

  createAbortController(): AbortController {
    const controller = new AbortController();
    if (this.disposed) {
      controller.abort();
      return controller;
    }

    this.abortControllers.add(controller);
    return controller;
  }

  trackRegistryRegistration(registration: RegistryRegistration): void {
    if (this.disposed) {
      registration.unregister();
      return;
    }

    this.registryRegistrations.add(registration);
  }

  trackBlobUrl(
    url: string,
    revoke: (urlToRevoke: string) => void = (urlToRevoke) => {
      URL.revokeObjectURL(urlToRevoke);
    }
  ): string {
    if (this.disposed) {
      revoke(url);
      return url;
    }

    this.blobUrls.set(url, revoke);
    return url;
  }

  trackPendingRequest<T>(request: Promise<T>): Promise<T> {
    const tracked = request.finally(() => {
      this.pendingRequests.delete(tracked);
    });

    if (this.disposed) {
      return tracked;
    }

    this.pendingRequests.add(tracked);
    return tracked;
  }

  getTelemetry(): Studio2LifecycleTelemetry {
    return {
      appInstanceCount: this.mountedApp ? 1 : 0,
      listenerCount: this.listeners.size,
      timerCount: this.timers.size,
      observerCount: this.observers.size,
      abortControllerCount: this.abortControllers.size,
      registryRegistrationCount: this.registryRegistrations.size,
      blobUrlCount: this.blobUrls.size,
      pendingRequestCount: this.pendingRequests.size
    };
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }

    this.disposed = true;

    this.mountedApp?.unmount();
    this.mountedApp = null;

    for (const unsubscribe of [...this.listeners]) {
      unsubscribe();
    }
    this.listeners.clear();

    for (const clearTimer of [...this.timers]) {
      clearTimer();
    }
    this.timers.clear();

    for (const observer of [...this.observers]) {
      observer.disconnect();
    }
    this.observers.clear();

    for (const controller of [...this.abortControllers]) {
      controller.abort();
    }
    this.abortControllers.clear();

    for (const registration of [...this.registryRegistrations]) {
      registration.unregister();
    }
    this.registryRegistrations.clear();

    for (const [url, revoke] of [...this.blobUrls]) {
      revoke(url);
    }
    this.blobUrls.clear();

    this.pendingRequests.clear();
  }
}
