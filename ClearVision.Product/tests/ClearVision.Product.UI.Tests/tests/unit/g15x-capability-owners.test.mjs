import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

function readRepoText(relativeUrl) {
  return readFileSync(new URL(relativeUrl, import.meta.url), 'utf8');
}

const appSource = () => readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/app.js');
const appSettingsSource = () => readRepoText('../../../../src/ClearVision.Product.Desktop/appsettings.json');

test('G15X app composition reads all remaining capability flags once and defaults to legacy paths', () => {
  const source = appSource();

  for (const flag of [
    'Studio2.GlobalVariables',
    'Studio2.Settings',
    'Studio2.ProjectPage',
    'Studio2.Inspection',
    'Studio2.ResultsReview',
    'Studio2.AiPanel'
  ]) {
    assert.match(source, new RegExp(flag.replace('.', '\\.')));
  }

  assert.match(source, /const GLOBAL_VARIABLES_CAPABILITY_ENABLED = readGlobalVariablesCapabilityFlagOnce\(\);/);
  assert.match(source, /const SETTINGS_CAPABILITY_ENABLED = readSettingsCapabilityFlagOnce\(\);/);
  assert.match(source, /const PROJECT_PAGE_CAPABILITY_ENABLED = readProjectPageCapabilityFlagOnce\(\);/);
  assert.match(source, /const INSPECTION_CAPABILITY_ENABLED = readInspectionCapabilityFlagOnce\(\);/);
  assert.match(source, /const RESULTS_REVIEW_CAPABILITY_ENABLED = readResultsReviewCapabilityFlagOnce\(\);/);
  assert.match(source, /const AI_PANEL_CAPABILITY_ENABLED = readAiPanelCapabilityFlagOnce\(\);/);

  assert.match(source, /if \(isGlobalVariablesCapabilityEnabled\(\)\) \{[\s\S]*new GlobalVariablesCapabilityOwner/);
  assert.match(source, /if \(isSettingsCapabilityEnabled\(\)\) \{[\s\S]*new SettingsCapabilityOwner/);
  assert.match(source, /if \(isProjectPageCapabilityEnabled\(\)\) \{[\s\S]*new ProjectPageCapabilityOwner/);
  assert.match(source, /if \(isInspectionCapabilityEnabled\(\)\) \{[\s\S]*new InspectionCapabilityOwner/);
  assert.match(source, /if \(isResultsReviewCapabilityEnabled\(\)\) \{[\s\S]*new ResultsReviewCapabilityOwner/);
  assert.match(source, /if \(isAiPanelCapabilityEnabled\(\)\) \{[\s\S]*new AiPanelCapabilityOwner/);

  assert.match(source, /loadGlobalVariablePanelModule\(\)[\s\S]*new module\.default\('global-variable-panel'\)/);
  assert.match(source, /loadSettingsViewModule\(\)[\s\S]*createLegacySettingsView\('settings-view'\)/);
  assert.match(source, /loadProjectViewModule\(\)[\s\S]*new ProjectView\('project-view'\)/);
  assert.match(source, /loadInspectionPanelModule\(\)[\s\S]*new InspectionPanel\('inspection-control-panel'\)/);
  assert.match(source, /loadResultPanelModule\(\)[\s\S]*new ResultPanel\('results-list-container'\)/);
  assert.match(source, /loadAiPanelModule\(\)[\s\S]*new AiPanel\('ai-view'/);
});

test('G15X owner source files define adapters, dispose paths, and do not use CSS-only hide as the migration mechanism', () => {
  const owners = [
    ['GlobalVariables', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablesCapabilityOwner.mjs'],
    ['Settings', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsCapabilityOwner.mjs'],
    ['ProjectPage', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectPageCapabilityOwner.mjs'],
    ['Inspection', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/inspectionCapabilityOwner.mjs'],
    ['ResultsReview', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/results/resultsReviewCapabilityOwner.mjs'],
    ['AiPanel', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelCapabilityOwner.mjs']
  ];

  for (const [name, path] of owners) {
    const source = readRepoText(path);
    assert.match(source, new RegExp(`class ${name}CapabilityAdapter|class ${name.replace('Page', 'Page').replace('Review', 'Review')}CapabilityAdapter`), `${name} adapter`);
    assert.match(source, /dispose\(\)/, `${name} dispose`);
    assert.match(source, /removeEventListener|closeEventStream|unsubscribes/, `${name} cleanup`);
    assert.doesNotMatch(source, /classList\.add\('hidden'\)[\s\S]*legacy/i, `${name} must not be CSS-only hide`);
  }
});

test('Global Variables capability keeps preview/property binding compatibility through the project manager authority', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablesCapabilityOwner.mjs');

  assert.match(source, /bindPreviewField/);
  assert.match(source, /setSchemaFromExternal/);
  assert.match(source, /projectManagerRef/);
  assert.match(source, /saveGlobalVariables/);
  assert.match(source, /sourceBindings/);
  assert.match(source, /targetBindings/);
  assert.doesNotMatch(source, /localStorage|indexedDB|new\s+ProjectManager/);
});

test('Settings capability saves only the active tab through the existing settings API', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsCapabilityOwner.mjs');

  assert.match(source, /SETTINGS_TABS/);
  assert.match(source, /saveCurrentTab/);
  assert.match(source, /nextConfig\[tab\.configKey\] = parsed/);
  assert.match(source, /settingsApiRef = settingsApi/);
  assert.doesNotMatch(source, /localStorage\.setItem|indexedDB/);
});

test('Settings and AI capability gates fail closed behind explicit experimental window switches', () => {
  const source = appSource();
  const appSettings = JSON.parse(appSettingsSource());

  assert.equal(appSettings.Studio.SettingsCapabilityEnabled, false);
  assert.equal(appSettings.Studio.AiPanelCapabilityEnabled, false);

  assert.match(
    source,
    /function isSettingsCapabilityEnabled\(\) \{[\s\S]*return SETTINGS_CAPABILITY_ENABLED\s*&&\s*window\.__CLEARVISION_ENABLE_EXPERIMENTAL_SETTINGS_CAPABILITY === true;[\s\S]*\}/
  );
  assert.match(
    source,
    /function isAiPanelCapabilityEnabled\(\) \{[\s\S]*return AI_PANEL_CAPABILITY_ENABLED\s*&&\s*window\.__CLEARVISION_ENABLE_EXPERIMENTAL_AI_PANEL_CAPABILITY === true;[\s\S]*\}/
  );
});

test('Project Page capability routes list, search, create, open, save, import and export through project manager or existing globals', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectPageCapabilityOwner.mjs');

  for (const expected of [
    'getProjectList',
    'getRecentProjects',
    'searchProjects',
    'createProject',
    'openProject',
    'saveProject',
    'showImportDialog',
    'showProjectExportDialog'
  ]) {
    assert.match(source, new RegExp(expected));
  }

  assert.doesNotMatch(source, /fetch\(|localStorage\.setItem|indexedDB|ProjectSaveCoordinator/);
});

test('Inspection capability sends commands through inspectionController and cleans result subscriptions', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/inspectionCapabilityOwner.mjs');

  assert.match(source, /executeSingle/);
  assert.match(source, /startRealtime/);
  assert.match(source, /stopRealtime/);
  assert.match(source, /onInspectionCompleted|onCompleted/);
  assert.match(source, /onInspectionError|onError/);
  assert.match(source, /unsubscribes/);
  assert.doesNotMatch(source, /EvidenceManifest|retention|PreviewArtifact/);
});

test('Results Review capability uses formal history loaders and avoids preview artifact/cache authority', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/results/resultsReviewCapabilityOwner.mjs');

  assert.match(source, /loadHistory/);
  assert.match(source, /loadDetail/);
  assert.match(source, /loadComparison/);
  assert.match(source, /loadPreviousSuccess/);
  assert.match(source, /exportEvidence/);
  assert.match(source, /pageSize/);
  assert.doesNotMatch(source, /PreviewArtifact|preview cache|previewCache|direct artifact client/i);
});

test('AI Panel capability is backend AgentRun projection with cancel and stream cleanup only', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelCapabilityOwner.mjs');

  assert.match(source, /agent-runs\/latest/);
  assert.match(source, /agent-runs\/\$?\{?encodeURIComponent\(runId\)\}?\/cancel|cancelRun/);
  assert.match(source, /EventSource/);
  assert.match(source, /closeEventStream/);
  assert.match(source, /dispose\(\)/);
  assert.doesNotMatch(source, /class\s+AgentRunEventStore|EventStore|terminal recovery|Workspace Snapshot authority/i);
});

test('view manager uses injected settings owner and no longer mounts settings through globals', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/viewManager.js');

  assert.match(source, /ensureSettingsView/);
  assert.match(source, /getSettingsView/);
  assert.doesNotMatch(source, /initializeSettingsView|cvSettingsView/);
});

test('index no longer statically loads legacy panel modules governed by G15X owners', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/index.html');

  for (const module of [
    'src/features/inspection/inspectionPanel.js',
    'src/features/results/resultPanel.js',
    'src/features/ai/aiPanel.js',
    'src/features/settings/settingsView.js'
  ]) {
    assert.doesNotMatch(source, new RegExp(module.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }
});
