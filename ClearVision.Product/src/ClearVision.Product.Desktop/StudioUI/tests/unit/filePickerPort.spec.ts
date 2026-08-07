import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createFilePickerPort,
  FilePickerPortDisposedError,
  FilePickerTimeoutError,
  resolveFilePickerFilter
} from '@/platform/host';
import { createBrowserHostFake } from '@/platform/host';

describe('FilePickerPort', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('owns one host subscription, posts the legacy command and decodes camelCase and PascalCase events', async () => {
    const host = createBrowserHostFake();
    const port = createFilePickerPort(host);
    const request = port.pick({
      parameterName: 'ImagePath',
      filter: resolveFilePickerFilter('ImagePath')
    });

    expect(host.postedMessages).toEqual([{
      messageType: 'PickFileCommand',
      parameterName: 'ImagePath',
      filter: 'Image Files|*.bmp;*.jpg;*.png;*.jpeg;*.tif;*.tiff|All Files|*.*'
    }]);
    expect(host.getDiagnostics().activeSubscriptionCount).toBe(1);

    host.emitMessage({
      MessageType: 'FilePickedEvent',
      ParameterName: 'ImagePath',
      FilePath: 'C:\\images\\sample.png',
      IsCancelled: false
    });
    await expect(request).resolves.toEqual({
      status: 'selected',
      parameterName: 'ImagePath',
      filePath: 'C:\\images\\sample.png'
    });

    port.dispose();
    expect(port.getDiagnostics()).toMatchObject({ disposed: true, activeSubscriptionCount: 0 });
    expect(host.getDiagnostics().activeSubscriptionCount).toBe(0);
  });

  it('serializes requests in FIFO order and preserves cancellation as a non-write result', async () => {
    const host = createBrowserHostFake();
    const port = createFilePickerPort(host);
    const first = port.pick({ parameterName: 'FirstPath', filter: 'All Files|*.*' });
    const second = port.pick({ parameterName: 'SecondPath', filter: 'All Files|*.*' });

    expect(host.postedMessages).toHaveLength(1);
    host.emitMessage({ type: 'FilePickedEvent', payload: {
      parameterName: 'FirstPath', filePath: null, isCancelled: true
    } });
    await expect(first).resolves.toEqual({ status: 'cancelled', parameterName: 'FirstPath' });
    expect(host.postedMessages).toHaveLength(2);

    host.emitMessage({ type: 'FilePickedEvent', payload: {
      parameterName: 'SecondPath', filePath: 'C:\\second.bin', isCancelled: false
    } });
    await expect(second).resolves.toEqual({
      status: 'selected', parameterName: 'SecondPath', filePath: 'C:\\second.bin'
    });
    port.dispose();
  });

  it('ignores wrong message types, mismatched parameters and invalid payloads', async () => {
    const host = createBrowserHostFake();
    const port = createFilePickerPort(host);
    const request = port.pick({ parameterName: 'ModelPath', filter: 'All Files|*.*' });

    host.emitMessage({ type: 'OtherEvent', parameterName: 'ModelPath', filePath: 'wrong', isCancelled: false });
    host.emitMessage({ type: 'FilePickedEvent', parameterName: 'OtherPath', filePath: 'wrong', isCancelled: false });
    host.emitMessage({ type: 'FilePickedEvent', parameterName: 'ModelPath', filePath: 'invalid', isCancelled: 'false' });
    expect(port.getDiagnostics()).toMatchObject({ activeRequest: true, ignoredResponseCount: 2 });

    host.emitMessage({ type: 'FilePickedEvent', parameterName: 'ModelPath', filePath: 'C:\\model.onnx', isCancelled: false });
    await expect(request).resolves.toMatchObject({ status: 'selected', filePath: 'C:\\model.onnx' });
    port.dispose();
  });

  it('times out without opening the next request until the late response drains', async () => {
    vi.useFakeTimers();
    const host = createBrowserHostFake();
    const port = createFilePickerPort(host);
    const first = port.pick({ parameterName: 'FirstPath', filter: 'All Files|*.*', timeoutMs: 10 });
    const second = port.pick({ parameterName: 'SecondPath', filter: 'All Files|*.*' });
    const firstOutcome = first.catch(error => error);

    await vi.advanceTimersByTimeAsync(10);
    await expect(firstOutcome).resolves.toBeInstanceOf(FilePickerTimeoutError);
    expect(port.getDiagnostics()).toMatchObject({ activeRequest: true, queuedRequestCount: 1 });

    host.emitMessage({ type: 'FilePickedEvent', parameterName: 'FirstPath', filePath: 'late', isCancelled: false });
    expect(host.postedMessages).toHaveLength(2);
    host.emitMessage({ type: 'FilePickedEvent', parameterName: 'SecondPath', filePath: 'C:\\second', isCancelled: false });
    await expect(second).resolves.toMatchObject({ status: 'selected', filePath: 'C:\\second' });
    expect(port.getDiagnostics()).toMatchObject({ activeRequest: false, lateResponseCount: 1 });
    port.dispose();
  });

  it('rejects active and queued requests on dispose and ignores later host messages', async () => {
    const host = createBrowserHostFake();
    const port = createFilePickerPort(host);
    const first = port.pick({ parameterName: 'FirstPath', filter: 'All Files|*.*' });
    const second = port.pick({ parameterName: 'SecondPath', filter: 'All Files|*.*' });
    const firstOutcome = first.catch(error => error);
    const secondOutcome = second.catch(error => error);
    port.dispose();

    await expect(firstOutcome).resolves.toBeInstanceOf(FilePickerPortDisposedError);
    await expect(secondOutcome).resolves.toBeInstanceOf(FilePickerPortDisposedError);
    expect(() => host.emitMessage({ type: 'FilePickedEvent' })).not.toThrow();
    expect(port.getDiagnostics()).toMatchObject({ disposed: true, activeRequest: false, queuedRequestCount: 0 });
  });
});
