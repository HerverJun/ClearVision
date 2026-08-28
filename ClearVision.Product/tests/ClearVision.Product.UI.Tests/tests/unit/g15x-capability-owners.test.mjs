import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';

function readRepoText(relativeUrl) {
  return readFileSync(new URL(relativeUrl, import.meta.url), 'utf8');
}

const appSource = () => readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/app.js');
const appSettingsSource = () => readRepoText('../../../../src/ClearVision.Product.Desktop/appsettings.json');

function countOccurrences(source, fragment) {
  return source.split(fragment).length - 1;
}

test('Wave 0 app composition retains the five promoted capability owners behind one startup flag read each', () => {
  const source = appSource();

  const retained = [
    ['Studio2.PropertyPanel', 'PROPERTY_PANEL_CAPABILITY_ENABLED', 'readPropertyPanelCapabilityFlagOnce', 'PropertyPanelCapabilityOwner'],
    ['Studio2.PreviewPanel', 'PREVIEW_PANEL_CAPABILITY_ENABLED', 'readPreviewPanelCapabilityFlagOnce', 'PreviewPanelCapabilityOwner'],
    ['Studio2.GlobalVariables', 'GLOBAL_VARIABLES_CAPABILITY_ENABLED', 'readGlobalVariablesCapabilityFlagOnce', 'GlobalVariablesCapabilityOwner'],
    ['Studio2.ProjectPage', 'PROJECT_PAGE_CAPABILITY_ENABLED', 'readProjectPageCapabilityFlagOnce', 'ProjectPageCapabilityOwner'],
    ['Studio2.ResultsReview', 'RESULTS_REVIEW_CAPABILITY_ENABLED', 'readResultsReviewCapabilityFlagOnce', 'ResultsReviewCapabilityOwner']
  ];

  for (const [flag, constant, reader, owner] of retained) {
    assert.equal(countOccurrences(source, `'${flag}'`), 1, `${flag} has one client-side flag authority`);
    assert.equal(
      countOccurrences(source, `const ${constant} = ${reader}();`),
      1,
      `${constant} is snapshotted once`
    );
    assert.equal(countOccurrences(source, `new ${owner}`), 1, `${owner} has one composition path`);
  }
});

test('Wave 0 retires incomplete Settings, Inspection, and AI owners and composes each legacy owner exactly once', () => {
  const source = appSource();
  const appSettings = JSON.parse(appSettingsSource());
  const retired = [
    ['SettingsCapabilityEnabled', 'SettingsCapabilityOwner', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsCapabilityOwner.mjs'],
    ['InspectionCapabilityEnabled', 'InspectionCapabilityOwner', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/inspectionCapabilityOwner.mjs'],
    ['AiPanelCapabilityEnabled', 'AiPanelCapabilityOwner', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelCapabilityOwner.mjs']
  ];

  for (const [option, owner, path] of retired) {
    assert.equal(appSettings.Studio[option], undefined, `${option} is not a runtime product switch`);
    assert.doesNotMatch(source, new RegExp(option));
    assert.doesNotMatch(source, new RegExp(owner));
    assert.equal(existsSync(new URL(path, import.meta.url)), false, `${owner} source is retired`);
  }

  assert.doesNotMatch(source, /__CLEARVISION_ENABLE_EXPERIMENTAL_(SETTINGS|INSPECTION|AI_PANEL)_CAPABILITY/);
  assert.equal(countOccurrences(source, "createLegacySettingsView('settings-view')"), 1);
  assert.equal(countOccurrences(source, "new InspectionPanel('inspection-control-panel')"), 1);
  assert.equal(countOccurrences(source, "new AiPanel('ai-view'"), 1);
});

test('retained owner source files define adapters, dispose paths, and do not use CSS-only hiding as migration', () => {
  const owners = [
    ['GlobalVariables', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablesCapabilityOwner.mjs'],
    ['ProjectPage', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectPageCapabilityOwner.mjs'],
    ['ResultsReview', '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/results/resultsReviewCapabilityOwner.mjs']
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

test('Legacy settings system tab saves scoped payloads and excludes retired no-op fields', () => {
  const source = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/tabs/systemTabs.js');

  assert.match(source, /saveScope:\s*'general'/);
  assert.match(source, /saveScope:\s*'storage'/);
  assert.match(source, /saveScope:\s*'users'/);
  assert.doesNotMatch(source, /autoStart:\s*this\.container\?\.querySelector\('#cfg-autoStart'\)/);
  assert.doesNotMatch(source, /minFreeSpaceGb:\s*this\.readFloatSetting\('#cfg-minFreeSpaceGb'/);
  assert.doesNotMatch(source, /sessionTimeoutMinutes:\s*this\.readIntegerSetting\('#cfg-sessionTimeoutMinutes'/);
  assert.match(source, /cfg-autoStart[\s\S]*暂未启用，需安装器支持/);
  assert.match(source, /cfg-minFreeSpaceGb[\s\S]*暂未启用，仅保留兼容/);
  assert.match(source, /id="cfg-sessionTimeoutMinutes"[\s\S]*disabled/);
  assert.match(source, /桌面端不适用/);
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

test('legacy Settings, Inspection, and AI owners clean their timers, subscriptions, and transports', () => {
  const settings = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsView.js');
  const inspection = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/inspectionPanel.js');
  const aiLifecycle = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelLifecycle.js');

  assert.match(settings, /destroy\(\)\s*\{[\s\S]*this\.deactivate\(\)/);
  assert.match(settings, /clearTransientResources\(\)\s*\{[\s\S]*_trackedTimeouts\.forEach[\s\S]*_trackedTimeouts\.clear\(\)/);
  assert.match(inspection, /this\.unsubscribeCompleted = inspectionController\.onInspectionCompleted/);
  assert.match(inspection, /this\.unsubscribeError = inspectionController\.onInspectionError/);
  assert.match(inspection, /dispose\(\)\s*\{[\s\S]*this\.unsubscribeCompleted\(\)[\s\S]*this\.unsubscribeError\(\)/);
  assert.match(aiLifecycle, /dispose\(\)\s*\{[\s\S]*_ownedTimeouts[\s\S]*_messageUnsubscribes[\s\S]*_closeAllAgentTransports/);
  assert.match(aiLifecycle, /window\.clearTimeout/);
  assert.match(aiLifecycle, /window\.cancelAnimationFrame/);
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

test('view manager uses the injected legacy settings owner and does not mount settings through globals', () => {
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
