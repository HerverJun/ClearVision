import { mountRuntimeStudioApp } from '@/app/createStudioApp';
import { renderBootstrapDiagnostic } from '@/platform/diagnostics/bootstrapDiagnostic';
import { createStudioUiLifecycleDiagnosticsOwner } from '@/platform/diagnostics/studioUiLifecycleDiagnostics';
import '@/design-system/tokens/tokens.css';
import '@/design-system/patterns/workbench.css';
import '@/app/base.css';

const lifecycleDiagnostics = createStudioUiLifecycleDiagnosticsOwner();

void mountRuntimeStudioApp('#app')
  .then(mounted => {
    lifecycleDiagnostics.markMounted(mounted.platform.startup.hostKind);
  })
  .catch(error => {
    lifecycleDiagnostics.markBootstrapFailed(error);
    renderBootstrapDiagnostic('#app', error);
  });
