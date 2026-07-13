import { mountDesktopStudioApp } from '@/app/createStudioApp';
import { renderBootstrapDiagnostic } from '@/platform/diagnostics/bootstrapDiagnostic';
import '@/app/base.css';

void mountDesktopStudioApp('#app').catch(error => {
  renderBootstrapDiagnostic('#app', error);
});
