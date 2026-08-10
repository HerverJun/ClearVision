import { mountRuntimeStudioApp } from '@/app/createStudioApp';
import { createUiPreferencesOwner } from '@/app/preferences';
import { renderBootstrapDiagnostic } from '@/platform/diagnostics/bootstrapDiagnostic';
import { createStudioUiLifecycleDiagnosticsOwner } from '@/platform/diagnostics/studioUiLifecycleDiagnostics';
import '@/design-system/tokens/tokens.css';
import '@/design-system/patterns/workbench.css';
import '@/app/base.css';

const lifecycleDiagnostics = createStudioUiLifecycleDiagnosticsOwner();
const preferences = createUiPreferencesOwner();

void mountRuntimeStudioApp('#app', { preferences })
  .then(mounted => {
    lifecycleDiagnostics.markMounted(mounted.platform.startup.hostKind);
  })
  .catch(error => {
    preferences.dispose();
    lifecycleDiagnostics.markBootstrapFailed(error);
    renderBootstrapDiagnostic('#app', error);
  });
