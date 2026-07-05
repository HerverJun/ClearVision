import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const unitDir = dirname(fileURLToPath(import.meta.url));
const wwwroot = path.resolve(unitDir, '../../../..', 'src/ClearVision.Product.Desktop/wwwroot');

async function createHarness() {
  const server = http.createServer((request, response) => {
    const url = new URL(request.url || '/', 'http://127.0.0.1');
    if (url.pathname === '/' || url.pathname === '/harness.html') {
      response.writeHead(200, { 'content-type': 'text/html' });
      response.end(`<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <script>window.__API_BASE_URL__ = location.origin + "/api";</script>
</head>
<body>
  <div id="property-root"></div>
</body>
</html>`);
      return;
    }

    const relativePath = decodeURIComponent(url.pathname.replace(/^\/+/, ''));
    const filePath = path.resolve(wwwroot, relativePath);
    const relativeFromRoot = path.relative(wwwroot, filePath);
    if (relativeFromRoot.startsWith('..') || path.isAbsolute(relativeFromRoot) ||
      !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
      response.writeHead(404);
      response.end('not found');
      return;
    }

    const contentType = filePath.endsWith('.js') || filePath.endsWith('.mjs')
      ? 'text/javascript'
      : filePath.endsWith('.css') ? 'text/css' : 'text/plain';
    response.writeHead(200, { 'content-type': contentType });
    fs.createReadStream(filePath).pipe(response);
  });

  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.goto(`http://127.0.0.1:${server.address().port}/harness.html`);

  return {
    page,
    async close() {
      await browser.close();
      await new Promise(resolve => server.close(resolve));
    }
  };
}

test('NPoint draft workbench keeps Formal Save behind candidate and writes only formal assets on save', async (t) => {
  const harness = await createHarness();
  t.after(() => harness.close());

  const result = await harness.page.evaluate(async () => {
    const waitFor = async (predicate, message) => {
      for (let attempt = 0; attempt < 80; attempt += 1) {
        if (predicate()) {
          return;
        }

        await new Promise(resolve => setTimeout(resolve, 25));
      }

      throw new Error(message);
    };

    const [propertyPanelModule, projectModule, httpModule] = await Promise.all([
      import('/src/features/flow-editor/propertyPanel.js'),
      import('/src/features/project/projectManager.js'),
      import('/src/core/messaging/httpClient.js')
    ]);

    const projectManager = projectModule.default;
    const httpClient = httpModule.default;
    const originalPointPairs = JSON.stringify([
      { ImageX: 10, ImageY: 20, WorldX: 1, WorldY: 2, Enabled: true },
      { ImageX: 30, ImageY: 40, WorldX: 3, WorldY: 4, Enabled: true },
      { ImageX: 50, ImageY: 60, WorldX: 5, WorldY: 6, Enabled: true }
    ]);
    const project = {
      id: '11111111-1111-1111-1111-111111111111',
      name: 'NPoint Project',
      persistenceRevision: 41,
      assets: {
        calibrationAssets: [],
        spatialAssets: []
      }
    };
    const operator = {
      id: 'npoint-node-1',
      title: 'NPointCalibration',
      type: 'NPointCalibration',
      parameters: [
        { name: 'CalibrationMode', value: 'Affine', dataType: 'enum' },
        { name: 'CalibrationUnit', value: 'mm', dataType: 'string' },
        { name: 'PointPairs', value: originalPointPairs, dataType: 'string' }
      ]
    };

    projectManager.currentProject = project;
    window.__requests = [];
    httpClient.post = async (url, body) => {
      window.__requests.push({
        url,
        body: JSON.parse(JSON.stringify(body || {}))
      });

      if (url === '/calibration/npoint-draft/solve') {
        return {
          success: true,
          status: 'Solved',
          samples: body.samples.map((sample, index) => ({
            ...sample,
            order: index + 1,
            inlier: true,
            reprojectionX: sample.pixelX,
            reprojectionY: sample.pixelY,
            error: 0.01
          })),
          lastSolveResult: {
            accepted: true,
            meanError: 0.01,
            maxError: 0.02
          },
          candidateBundle: {
            schemaVersion: 2,
            transformKind: 'Affine'
          },
          candidateBundleJson: JSON.stringify({
            schemaVersion: 2,
            transformKind: 'Affine',
            matrix: [1, 0, 0, 0, 1, 0]
          }),
          artifacts: [],
          diagnostics: []
        };
      }

      if (url === `/projects/${project.id}/calibration-assets/from-draft`) {
        return {
          projectId: project.id,
          persistenceRevision: 42,
          asset: {
            assetId: 'asset-calibration-1',
            projectRevision: 42,
            contentHash: 'sha256:formal-candidate'
          },
          assets: {
            calibrationAssets: [
              {
                assetId: 'asset-calibration-1',
                targetNodeId: 'npoint-node-1',
                contentHash: 'sha256:formal-candidate'
              }
            ],
            spatialAssets: []
          }
        };
      }

      throw new Error(`Unexpected POST ${url}`);
    };

    const panel = new propertyPanelModule.PropertyPanel('property-root', {
      nPointCalibrationWorkbenchEnabled: true
    });
    panel.setOperator(operator);

    const workbenchMounted = Boolean(document.querySelector('[data-testid="npoint-calibration-workbench"]'));
    const legacyRoiMounted = Boolean(document.querySelector('.roi-editor-panel'));
    const formalSaveButton = document.querySelector('[data-action="formal-save"]');
    const initialFormalSaveDisabled = formalSaveButton?.disabled === true;
    const sampleInputs = Array.from(document.querySelectorAll('.calibration-draft-table tbody tr:first-child .calibration-draft-cell-input'));
    sampleInputs[0].value = '15';
    sampleInputs[0].dispatchEvent(new Event('change', { bubbles: true }));

    const formalSaveDisabledAfterDraftEdit = formalSaveButton.disabled === true;
    formalSaveButton.click();
    await new Promise(resolve => setTimeout(resolve, 0));
    const formalSaveCallsBeforeSolve = window.__requests.filter(item => item.url.includes('/calibration-assets/from-draft')).length;
    const pointPairsAfterDraftEdit = operator.parameters.find(item => item.name === 'PointPairs')?.value;

    document.querySelector('[data-action="solve"]').click();
    await waitFor(() => formalSaveButton.disabled === false, 'Formal Save did not enable after draft solve.');
    const solveRequest = window.__requests.find(item => item.url === '/calibration/npoint-draft/solve');
    const formalSaveEnabledAfterSolve = formalSaveButton.disabled === false;

    formalSaveButton.click();
    await waitFor(() => projectManager.currentProject.persistenceRevision === 42, 'Formal Save did not update current project revision.');
    const formalSaveRequest = window.__requests.find(item => item.url.includes('/calibration-assets/from-draft'));
    const statusText = document.querySelector('.calibration-draft-status')?.textContent || '';
    const pointPairsAfterFormalSave = operator.parameters.find(item => item.name === 'PointPairs')?.value;

    panel.destroy();

    return {
      workbenchMounted,
      legacyRoiMounted,
      initialFormalSaveDisabled,
      formalSaveDisabledAfterDraftEdit,
      formalSaveCallsBeforeSolve,
      pointPairsAfterDraftEdit,
      solveRequest,
      formalSaveEnabledAfterSolve,
      formalSaveRequest,
      currentProjectRevision: projectManager.currentProject.persistenceRevision,
      currentProjectAssets: projectManager.currentProject.assets,
      statusText,
      pointPairsAfterFormalSave,
      originalPointPairs
    };
  });

  assert.equal(result.workbenchMounted, true);
  assert.equal(result.legacyRoiMounted, false);
  assert.equal(result.initialFormalSaveDisabled, true);
  assert.equal(result.formalSaveDisabledAfterDraftEdit, true);
  assert.equal(result.formalSaveCallsBeforeSolve, 0);
  assert.equal(result.pointPairsAfterDraftEdit, result.originalPointPairs);
  assert.equal(result.solveRequest.body.samples[0].pixelX, 15);
  assert.equal(result.formalSaveEnabledAfterSolve, true);
  assert.equal(result.formalSaveRequest.body.sessionId.startsWith('calibration-draft-'), true);
  assert.equal(result.formalSaveRequest.body.targetNodeId, 'npoint-node-1');
  assert.equal(result.formalSaveRequest.body.expectedPersistenceRevision, 41);
  assert.match(result.formalSaveRequest.body.candidateBundleJson, /"schemaVersion":2/);
  assert.equal(result.currentProjectRevision, 42);
  assert.equal(result.currentProjectAssets.calibrationAssets[0].assetId, 'asset-calibration-1');
  assert.match(result.statusText, /FormalSaved|asset-calibration-1|r42/);
  assert.equal(result.pointPairsAfterFormalSave, result.originalPointPairs);
});
