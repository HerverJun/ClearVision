import { mountStudio2FoundationIsland } from '@/foundation/studio2FoundationIsland';
import { readStartupConfig } from '@/startup/startupConfig';

const startup = readStartupConfig();
const container = document.getElementById('studio2-v2-root');

if (!container) {
  throw new Error('Studio 2.0 V2 root container is missing.');
}

if (startup.workspaceV2Enabled) {
  void mountStudio2FoundationIsland({
    startup,
    container
  }).catch((error: unknown) => {
    console.error('[Studio2] Workspace shell failed to mount:', error);
    container.textContent = error instanceof Error ? error.message : String(error);
  });
} else {
  container.textContent = 'Studio 2.0 V2 is disabled by host startup configuration.';
}
