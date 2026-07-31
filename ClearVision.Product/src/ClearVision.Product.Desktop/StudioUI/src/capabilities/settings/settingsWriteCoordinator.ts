import {
  type SettingsSection,
  type SettingsWriteCoordinatorDiagnostics,
  type SettingsWriteResult,
  type SettingsWriteTask,
  type SettingsWriteTaskContext
} from './contracts';

export interface SettingsWriteCoordinator {
  enqueue<T>(section: SettingsSection, task: SettingsWriteTask<T>): Promise<SettingsWriteResult<T>>;
  invalidate(reason?: string): void;
  cancel(section?: SettingsSection, reason?: string): void;
  diagnostics(): SettingsWriteCoordinatorDiagnostics;
  dispose(reason?: string): void;
}

interface QueueEntry<T> {
  readonly id: number;
  readonly section: SettingsSection;
  readonly task: SettingsWriteTask<T>;
  readonly generation: number;
  readonly resolve: (result: SettingsWriteResult<T>) => void;
  settled: boolean;
}

interface ActiveEntry {
  readonly entry: QueueEntry<unknown>;
  readonly controller: AbortController;
}

function cancellationResult<T>(
  entry: QueueEntry<T>,
  status: 'cancelled' | 'stale' | 'disposed',
  message: string
): SettingsWriteResult<T> {
  return Object.freeze({ status, section: entry.section, generation: entry.generation, message });
}

function failureMessage(error: unknown): string {
  return error instanceof Error && error.message.trim()
    ? error.message
    : 'Settings section write failed.';
}

export function createSettingsWriteCoordinator(): SettingsWriteCoordinator {
  const queues = new Map<SettingsSection, QueueEntry<unknown>[]>();
  const active = new Map<SettingsSection, ActiveEntry>();
  let nextEntryId = 0;
  let generation = 0;
  let disposed = false;

  function settle<T>(entry: QueueEntry<T>, result: SettingsWriteResult<T>): void {
    if (entry.settled) return;
    entry.settled = true;
    entry.resolve(result);
  }

  function cancelQueue(section: SettingsSection, status: 'cancelled' | 'stale' | 'disposed', message: string): void {
    const queue = queues.get(section);
    if (!queue) return;
    queues.delete(section);
    for (const entry of queue) {
      settle(entry, cancellationResult(entry, status, message));
    }
  }

  function pump(section: SettingsSection): void {
    if (disposed || active.has(section)) return;
    const queue = queues.get(section);
    const entry = queue?.shift();
    if (!entry) {
      queues.delete(section);
      return;
    }
    if (queue && queue.length === 0) queues.delete(section);

    const controller = new AbortController();
    active.set(section, { entry: entry as QueueEntry<unknown>, controller });
    const context: SettingsWriteTaskContext = Object.freeze({ signal: controller.signal, generation: entry.generation });
    void (async () => {
      try {
        const value = await entry.task(context);
        if (disposed) {
          settle(entry, cancellationResult(entry, 'disposed', 'Settings write coordinator has been disposed.'));
        } else if (controller.signal.aborted || entry.generation !== generation) {
          settle(entry, cancellationResult(entry, 'stale', 'Settings write result was invalidated before completion.'));
        } else {
          settle(entry, Object.freeze({
            status: 'completed', section: entry.section, generation: entry.generation, value
          }));
        }
      } catch (error) {
        if (disposed) {
          settle(entry, cancellationResult(entry, 'disposed', 'Settings write coordinator has been disposed.'));
        } else if (entry.generation !== generation) {
          settle(entry, cancellationResult(entry, 'stale', 'Settings write result was invalidated before completion.'));
        } else if (controller.signal.aborted) {
          settle(entry, cancellationResult(entry, 'cancelled', 'Settings write was cancelled before completion.'));
        } else {
          settle(entry, Object.freeze({
            status: 'failed', section: entry.section, generation: entry.generation,
            error, message: failureMessage(error)
          }));
        }
      } finally {
        const current = active.get(section);
        if (current?.entry.id === entry.id) {
          active.delete(section);
          pump(section);
        }
      }
    })();
  }

  function abortActive(section: SettingsSection, reason: string): void {
    const current = active.get(section);
    current?.controller.abort(reason);
  }

  const coordinator: SettingsWriteCoordinator = Object.freeze({
    enqueue<T>(section: SettingsSection, task: SettingsWriteTask<T>): Promise<SettingsWriteResult<T>> {
      if (disposed) {
        return Promise.resolve(Object.freeze({
          status: 'disposed', section, generation, message: 'Settings write coordinator has been disposed.'
        }));
      }
      if (typeof task !== 'function') throw new TypeError('Settings write task must be a function.');
      const entryGeneration = generation;
      return new Promise<SettingsWriteResult<T>>(resolve => {
        const entry: QueueEntry<T> = {
          id: ++nextEntryId,
          section,
          task,
          generation: entryGeneration,
          resolve,
          settled: false
        };
        const queue = queues.get(section) ?? [];
        queue.push(entry as QueueEntry<unknown>);
        queues.set(section, queue);
        pump(section);
      });
    },
    invalidate(reason = 'settings-write-invalidated'): void {
      if (disposed) return;
      generation += 1;
      for (const section of active.keys()) abortActive(section, reason);
      for (const section of queues.keys()) cancelQueue(section, 'stale', `Settings writes invalidated: ${reason}.`);
    },
    cancel(section?: SettingsSection, reason = 'settings-write-cancelled'): void {
      if (disposed) return;
      generation += 1;
      if (section) {
        abortActive(section, reason);
        cancelQueue(section, 'cancelled', `Settings write cancelled: ${reason}.`);
        return;
      }
      for (const currentSection of active.keys()) abortActive(currentSection, reason);
      for (const currentSection of queues.keys()) cancelQueue(currentSection, 'cancelled', `Settings writes cancelled: ${reason}.`);
    },
    diagnostics(): SettingsWriteCoordinatorDiagnostics {
      const queuedTaskCount = [...queues.values()].reduce((total, queue) => total + queue.length, 0);
      return Object.freeze({
        generation,
        activeSectionCount: active.size,
        activeAbortControllerCount: active.size,
        queuedTaskCount,
        disposed
      });
    },
    dispose(reason = 'settings-write-coordinator-disposed'): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      for (const current of active.values()) current.controller.abort(reason);
      for (const section of queues.keys()) cancelQueue(section, 'disposed', `Settings write coordinator has been disposed: ${reason}.`);
    }
  });

  return coordinator;
}
