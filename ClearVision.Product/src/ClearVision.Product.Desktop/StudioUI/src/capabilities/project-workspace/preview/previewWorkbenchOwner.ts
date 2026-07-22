import type { ApiTransport } from '@/platform/api';
import type { FlowCanvasOwner } from '../flow';
import { createImageCanvasOwner, type ImageCanvasOwner } from '../image/imageCanvasOwner';
import type { InspectorOwner } from '../inspector/inspectorOwner';
import { createRoiInteractionOwner, type RoiInteractionOwner } from '../roi/roiInteractionOwner';
import type { WorkspaceLifecycleDiagnosticsOwner } from '../workspaceLifecycleDiagnostics';
import { createPreviewOwner, type PreviewOwner } from './previewOwner';

export interface PreviewWorkbenchOwner {
  readonly preview: PreviewOwner;
  readonly image: ImageCanvasOwner;
  readonly roi: RoiInteractionOwner;
  dispose(reason?: string): void;
}

export function createPreviewWorkbenchOwner(options: {
  readonly projectId: string;
  readonly flowOwner: FlowCanvasOwner;
  readonly inspectorOwner: InspectorOwner;
  readonly api: ApiTransport;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
  readonly featureFlags: Readonly<Record<string, boolean>>;
  readonly getInputImageContext?: (
    targetNode: Readonly<Record<string, unknown>>
  ) => Readonly<Record<string, unknown>> | null;
}): PreviewWorkbenchOwner {
  const preview = createPreviewOwner(options);
  let image: ImageCanvasOwner | undefined;
  let roi: RoiInteractionOwner | undefined;
  try {
    image = createImageCanvasOwner({
      projectId: options.projectId,
      previewOwner: preview,
      diagnostics: options.diagnostics
    });
    roi = createRoiInteractionOwner({
      projectId: options.projectId,
      flowOwner: options.flowOwner,
      inspectorOwner: options.inspectorOwner,
      previewOwner: preview,
      imageOwner: image,
      diagnostics: options.diagnostics,
      startupFlags: options.featureFlags
    });
  } catch (error) {
    roi?.dispose('preview-workbench-construction-failed');
    image?.dispose('preview-workbench-construction-failed');
    preview.dispose('preview-workbench-construction-failed');
    throw error;
  }

  if (!image || !roi) {
    preview.dispose('preview-workbench-construction-incomplete');
    throw new Error('Preview workbench construction did not produce all owners.');
  }

  const ownedImage = image;
  const ownedRoi = roi;
  let disposed = false;
  return Object.freeze({
    preview,
    image: ownedImage,
    roi: ownedRoi,
    dispose(reason = 'preview-workbench-disposed'): void {
      if (disposed) return;
      disposed = true;
      let disposalError: unknown;
      try {
        preview.dispose(reason);
      } catch (error) {
        disposalError = error;
      }
      try {
        ownedRoi.dispose(reason);
      } catch (error) {
        disposalError ??= error;
      }
      try {
        ownedImage.dispose(reason);
      } catch (error) {
        disposalError ??= error;
      }
      if (disposalError !== undefined) throw disposalError;
    }
  });
}
