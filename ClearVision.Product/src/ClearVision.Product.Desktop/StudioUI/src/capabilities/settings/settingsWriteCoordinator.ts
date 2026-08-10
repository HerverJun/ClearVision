import {
  ApiAbortError,
  ApiDecodeError,
  ApiNetworkError
} from '@/platform/api';
import {
  type SettingsSection,
  type SettingsOperationKind,
  type SettingsWriteCoordinatorDiagnostics,
  type SettingsWriteResult,
  type SettingsWriteTask,
  type SettingsWriteTaskContext,
  SettingsContractDecodeError,
  SettingsUnknownOutcomeError
} from './contracts';

export interface SettingsWriteCoordinator {
  enqueue<T>(
    section: SettingsSection,
    task: SettingsWriteTask<T>,
    operationKind?: SettingsOperationKind
  ): Promise<SettingsWriteResult<T>>;
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
  readonly globalGeneration: number;
  readonly operationKind: SettingsOperationKind;
  readonly resolve: (result: SettingsWriteResult<T>) => void;
  settled: boolean;
}

interface ActiveEntry {
  readonly entry: QueueEntry<unknown>;
  readonly controller: AbortController;
  cancellationStatus?: 'cancelled' | 'stale' | 'disposed';
}

function cancellationResult<T>(
  entry: QueueEntry<T>,
  status: 'cancelled' | 'stale' | 'disposed',
  message: string
): SettingsWriteResult<T> {
  return Object.freeze({
    status,
    section: entry.section,
    generation: entry.generation,
    operationKind: entry.operationKind,
    message
  });
}

function failureMessage(error: unknown): string {
  return error instanceof Error && error.message.trim()
    ? error.message
    : '设置保存失败。';
}

export function createSettingsWriteCoordinator(): SettingsWriteCoordinator {
  const queues = new Map<SettingsSection, QueueEntry<unknown>[]>();
  const active = new Map<SettingsSection, ActiveEntry>();
  const sectionGenerations = new Map<SettingsSection, number>();
  let nextEntryId = 0;
  let globalGeneration = 0;
  let disposed = false;

  function currentSectionGeneration(section: SettingsSection): number {
    return sectionGenerations.get(section) ?? 0;
  }

  function bumpSectionGeneration(section: SettingsSection): void {
    sectionGenerations.set(section, currentSectionGeneration(section) + 1);
  }

  function isCurrent(entry: QueueEntry<unknown>): boolean {
    return entry.globalGeneration === globalGeneration &&
      entry.generation === currentSectionGeneration(entry.section);
  }

  function isAbort(error: unknown): boolean {
    return error instanceof ApiAbortError ||
      (typeof DOMException !== 'undefined' && error instanceof DOMException && error.name === 'AbortError');
  }

  function hasUnknownMutationOutcome(entry: QueueEntry<unknown>, error: unknown): boolean {
    if (entry.operationKind === 'read') return false;
    return error instanceof ApiNetworkError ||
      error instanceof ApiDecodeError ||
      error instanceof SettingsContractDecodeError ||
      isAbort(error);
  }

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
      const context: SettingsWriteTaskContext = Object.freeze({
      signal: controller.signal,
      generation: entry.generation,
      operationKind: entry.operationKind
    });
      void (async () => {
      try {
        const value = await entry.task(context);
        const current = active.get(section);
        if (disposed) {
          settle(entry, cancellationResult(entry, 'disposed', '设置保存通道已关闭。'));
        } else if (current?.entry.id === entry.id && current.cancellationStatus) {
          settle(entry, cancellationResult(entry, current.cancellationStatus, '设置保存完成前已取消。'));
        } else if (controller.signal.aborted || !isCurrent(entry as QueueEntry<unknown>)) {
          settle(entry, cancellationResult(entry, 'stale', '设置保存结果完成前已失效。'));
        } else {
          settle(entry, Object.freeze({
            status: 'completed', section: entry.section, generation: entry.generation,
            operationKind: entry.operationKind, value
          }));
        }
      } catch (error) {
        const current = active.get(section);
        if (disposed) {
          settle(entry, cancellationResult(entry, 'disposed', '设置保存通道已关闭。'));
        } else if (current?.entry.id === entry.id && current.cancellationStatus) {
          settle(entry, cancellationResult(entry, current.cancellationStatus, '设置保存完成前已取消。'));
        } else if (!isCurrent(entry as QueueEntry<unknown>)) {
          settle(entry, cancellationResult(entry, 'stale', '设置保存结果完成前已失效。'));
        } else if (controller.signal.aborted) {
          settle(entry, cancellationResult(entry, 'cancelled', '设置保存完成前已取消。'));
        } else if (hasUnknownMutationOutcome(entry as QueueEntry<unknown>, error)) {
          const unknown = new SettingsUnknownOutcomeError(error, entry.operationKind);
          settle(entry, Object.freeze({
            status: 'failed', section: entry.section, generation: entry.generation,
            operationKind: entry.operationKind, error: unknown, message: unknown.message
          }));
        } else {
          settle(entry, Object.freeze({
            status: 'failed', section: entry.section, generation: entry.generation,
            operationKind: entry.operationKind, error, message: failureMessage(error)
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

  function abortActive(
    section: SettingsSection,
    reason: string,
    cancellationStatus: 'cancelled' | 'stale' | 'disposed'
  ): void {
    const current = active.get(section);
    if (!current) return;
    current.cancellationStatus = cancellationStatus;
    current.controller.abort(reason);
  }

  const coordinator: SettingsWriteCoordinator = Object.freeze({
    enqueue<T>(
      section: SettingsSection,
      task: SettingsWriteTask<T>,
      operationKind: SettingsOperationKind = 'write'
    ): Promise<SettingsWriteResult<T>> {
      if (disposed) {
        return Promise.resolve(Object.freeze({
          status: 'disposed', section, generation: currentSectionGeneration(section), operationKind,
          message: '设置保存通道已关闭。'
        }));
      }
      if (typeof task !== 'function') throw new TypeError('设置保存任务无效。');
      const entryGeneration = currentSectionGeneration(section);
      return new Promise<SettingsWriteResult<T>>(resolve => {
        const entry: QueueEntry<T> = {
          id: ++nextEntryId,
          section,
          task,
          generation: entryGeneration,
          globalGeneration,
          operationKind,
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
      globalGeneration += 1;
      for (const section of active.keys()) abortActive(section, reason, 'stale');
      for (const section of queues.keys()) cancelQueue(section, 'stale', `设置保存结果已失效：${reason}。`);
    },
    cancel(section?: SettingsSection, reason = 'settings-write-cancelled'): void {
      if (disposed) return;
      if (section) {
        bumpSectionGeneration(section);
        abortActive(section, reason, 'cancelled');
        cancelQueue(section, 'cancelled', `设置保存已取消：${reason}。`);
        return;
      }
      globalGeneration += 1;
      for (const currentSection of active.keys()) abortActive(currentSection, reason, 'cancelled');
      for (const currentSection of queues.keys()) cancelQueue(currentSection, 'cancelled', `设置保存已取消：${reason}。`);
    },
    diagnostics(): SettingsWriteCoordinatorDiagnostics {
      const queuedTaskCount = [...queues.values()].reduce((total, queue) => total + queue.length, 0);
      const activeOperationKinds: Partial<Record<SettingsOperationKind, number>> = {};
      for (const { entry } of active.values()) {
        activeOperationKinds[entry.operationKind] = (activeOperationKinds[entry.operationKind] ?? 0) + 1;
      }
      return Object.freeze({
        generation: globalGeneration,
        activeSectionCount: active.size,
        activeAbortControllerCount: active.size,
        queuedTaskCount,
        activeOperationKinds: Object.freeze(activeOperationKinds),
        disposed
      });
    },
    dispose(reason = 'settings-write-coordinator-disposed'): void {
      if (disposed) return;
      disposed = true;
      globalGeneration += 1;
      for (const current of active.values()) {
        current.cancellationStatus = 'disposed';
        current.controller.abort(reason);
      }
      for (const section of queues.keys()) cancelQueue(section, 'disposed', `设置保存通道已关闭：${reason}。`);
    }
  });

  return coordinator;
}
