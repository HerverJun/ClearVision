import { chromium } from '@playwright/test';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const uiTestsRoot = resolve(scriptDirectory, '../../..');
const workspaceRoot = resolve(uiTestsRoot, '../../..');
const masterRoot = resolve(workspaceRoot, '_visual_master/option_D/screens');
const candidateRoot = resolve(workspaceRoot, '.tmp/studio-ui-next/option-d-g3/visual/candidate');
const candidateManifestPath = resolve(workspaceRoot, '.tmp/studio-ui-next/option-d-g3/visual/candidate.json');
const evidenceRoot = resolve(workspaceRoot, '.tmp/studio-ui-next/option-d-g3/master-comparison');
const manifestPath = resolve(evidenceRoot, 'manifest.json');
const perChannelDelta = 8;
const maxChangedPixelRatio = 0.01;
const minimumSsim = 0.99;
const expectedWidth = 3840;
const expectedHeight = 2160;
const sha256Pattern = /^[a-f0-9]{64}$/;

const captures = Object.freeze([
  { id: 'd02-overview-1920x1080', screen: 'D02', master: '02_overview.png', masterSha256: 'a6a902196b5486817f80c094d469fa4d96e8c934fb2a36c5e7947fc3d5f24769' },
  { id: 'd03-projects-data-1920x1080', screen: 'D03', master: '03_projects_data.png', masterSha256: 'fe6d5e6c368573de83d0a6a0ed46148a2f0e6f01d9e002565c63c6d4047c5e94' },
  { id: 'd04-projects-empty-1920x1080', screen: 'D04', master: '04_projects_empty.png', masterSha256: 'a0117dcd2b62a5cef6c499f4e0f658a4c3de255ae3da783640f6d6952f030087' },
  { id: 'd15-operators-1920x1080', screen: 'D15', master: '15_operator_catalog.png', masterSha256: 'a01ee6cfbcd1344c2340ce18ced5eb2cfce66450dd0509f5e87ccedead3a2d1f' },
  { id: 'd22-diagnostics-1920x1080', screen: 'D22', master: '22_diagnostics.png', masterSha256: '0415729663e19fb6b2527956eec74d9f64284cefa8b1ba7be7f8c4d5c9e6ee97' },
  { id: 'd23-about-1920x1080', screen: 'D23', master: '23_about.png', masterSha256: '4b085e25511b6fcffc72d6d6af33c39574baa9bfb7106729485b4035a77c8e84' }
]);

const candidateManifestBuffer = readFileSync(candidateManifestPath);
const candidateManifest = JSON.parse(candidateManifestBuffer.toString('utf8'));
const candidateCaptures = validateCandidateManifest(candidateManifest);
mkdirSync(evidenceRoot, { recursive: true });
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();
const results = [];

try {
  for (const capture of captures) {
    const masterPath = resolve(masterRoot, capture.master);
    const candidatePath = resolve(candidateRoot, `${capture.id}.png`);
    const master = readFileSync(masterPath);
    const candidate = readFileSync(candidatePath);
    const masterSha256 = sha256(master);
    const candidateSha256 = sha256(candidate);
    const candidateCapture = candidateCaptures.get(capture.id);
    if (masterSha256 !== capture.masterSha256) {
      throw new Error(`${capture.screen} raw Master SHA-256 differs from the frozen allowlist.`);
    }
    if (!candidateCapture
      || resolve(candidateCapture.screenshot) !== candidatePath
      || candidateCapture.sha256 !== candidateSha256
      || candidateCapture.masterSha256 !== capture.masterSha256) {
      throw new Error(`${capture.screen} candidate artifact is not bound to candidate.json.`);
    }
    const comparison = await compareWholeImage(page, master, candidate);
    if (comparison.width !== expectedWidth || comparison.height !== expectedHeight) {
      throw new Error(`${capture.screen} evidence dimensions must be ${expectedWidth}x${expectedHeight}.`);
    }
    const diffPath = resolve(evidenceRoot, `${capture.id}.master.diff.png`);
    const overlayPath = resolve(evidenceRoot, `${capture.id}.master.overlay.png`);
    const diff = writeDataUrl(diffPath, comparison.diffDataUrl);
    const overlay = writeDataUrl(overlayPath, comparison.overlayDataUrl);
    const result = comparison.changedPixelRatio <= maxChangedPixelRatio
      && comparison.ssim >= minimumSsim
      ? 'PASS'
      : 'FAIL';
    results.push({
      ...capture,
      result,
      masterPath,
      masterSha256,
      candidatePath,
      candidateSha256,
      width: comparison.width,
      height: comparison.height,
      changedPixels: comparison.changedPixels,
      changedPixelRatio: comparison.changedPixelRatio,
      maxChannelDelta: comparison.maxChannelDelta,
      meanAbsoluteChannelDelta: comparison.meanAbsoluteChannelDelta,
      ssim: comparison.ssim,
      diffPath,
      diffSha256: sha256(diff),
      overlayPath,
      overlaySha256: sha256(overlay)
    });
  }
} finally {
  await browser.close();
}

const assertionResult = results.every(result => result.result === 'PASS') ? 'PASS' : 'FAIL';
const manifest = {
  schemaVersion: 2,
  fixtureId: 'option-d-g3-master-candidate-comparison.v2',
  visualAuthority: '_visual_master/option_D/screens',
  candidateAuthority: '.tmp/studio-ui-next/option-d-g3/visual/candidate',
  candidateManifestPath,
  candidateManifestSha256: sha256(candidateManifestBuffer),
  candidateGateInvocationId: candidateManifest.gateInvocationId,
  maskPolicy: 'NO_MASKS',
  expectedOutput: { width: expectedWidth, height: expectedHeight },
  thresholds: { perChannelDelta, maxChangedPixelRatio, minimumSsim },
  ssimMethod: 'GLOBAL_LUMINANCE_DIAGNOSTIC',
  assertionResult,
  captures: results
};
writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
console.log(manifestPath);
for (const result of results) {
  console.log(`${result.screen} ${result.result} changed=${result.changedPixelRatio.toFixed(6)} ssim=${result.ssim.toFixed(6)}`);
}
if (assertionResult !== 'PASS') process.exitCode = 2;

function validateCandidateManifest(manifest) {
  const manifestCaptures = Array.isArray(manifest.captures) ? manifest.captures : [];
  const expectedIds = captures.map(capture => capture.id).sort();
  const actualIds = manifestCaptures.map(capture => capture.id).sort();
  if (manifest.schemaVersion !== 2
    || manifest.fixtureId !== 'option-d-g3-read-surfaces.v1'
    || manifest.visualPhase !== 'candidate'
    || manifest.visualAuthority !== '_visual_master/option_D/screens'
    || manifest.complete !== true
    || manifest.maskPolicy !== 'NO_MASKS'
    || manifest.canonicalCssViewport?.width !== 1920
    || manifest.canonicalCssViewport?.height !== 1080
    || manifest.deviceScaleFactor !== 2
    || typeof manifest.gateInvocationId !== 'string'
    || manifest.gateInvocationId.length === 0
    || JSON.stringify(actualIds) !== JSON.stringify(expectedIds)) {
    throw new Error(`G3 candidate manifest is not a complete invocation-bound capture: ${candidateManifestPath}`);
  }
  const byId = new Map();
  for (const expected of captures) {
    const capture = manifestCaptures.find(item => item.id === expected.id);
    const expectedPath = resolve(candidateRoot, `${expected.id}.png`);
    if (!capture
      || byId.has(expected.id)
      || capture.screen !== expected.screen
      || capture.phase !== 'candidate'
      || capture.width !== expectedWidth
      || capture.height !== expectedHeight
      || capture.functionalAssertions !== 'PASS'
      || capture.functionalAudit?.result !== 'PASS'
      || capture.ownerCleanup?.result !== 'PASS'
      || !Array.isArray(capture.runtimeErrors)
      || capture.runtimeErrors.length !== 0
      || resolve(capture.screenshot) !== expectedPath
      || !sha256Pattern.test(capture.sha256)
      || capture.masterSha256 !== expected.masterSha256) {
      throw new Error(`${expected.screen} candidate manifest binding is invalid.`);
    }
    byId.set(expected.id, capture);
  }
  return byId;
}

async function compareWholeImage(page, master, candidate) {
  return await page.evaluate(async ({ masterBase64, candidateBase64, threshold }) => {
    const decode = async base64 => {
      const binary = atob(base64);
      const bytes = new Uint8Array(binary.length);
      for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
      return await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    };
    const masterImage = await decode(masterBase64);
    const candidateImage = await decode(candidateBase64);
    if (masterImage.width !== candidateImage.width || masterImage.height !== candidateImage.height) {
      throw new Error(`Image dimensions differ: ${masterImage.width}x${masterImage.height} vs ${candidateImage.width}x${candidateImage.height}.`);
    }
    const { width, height } = masterImage;
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (!context) throw new Error('2D comparison context is unavailable.');
    context.drawImage(masterImage, 0, 0);
    const masterPixels = context.getImageData(0, 0, width, height);
    context.clearRect(0, 0, width, height);
    context.drawImage(candidateImage, 0, 0);
    const candidatePixels = context.getImageData(0, 0, width, height);
    const diff = context.createImageData(width, height);
    const overlay = context.createImageData(width, height);
    let changedPixels = 0;
    let maxChannelDelta = 0;
    let absoluteChannelDelta = 0;
    let sumMasterLuma = 0;
    let sumCandidateLuma = 0;
    let sumMasterLumaSquared = 0;
    let sumCandidateLumaSquared = 0;
    let sumLumaProduct = 0;
    for (let index = 0; index < masterPixels.data.length; index += 4) {
      const redDelta = Math.abs(masterPixels.data[index] - candidatePixels.data[index]);
      const greenDelta = Math.abs(masterPixels.data[index + 1] - candidatePixels.data[index + 1]);
      const blueDelta = Math.abs(masterPixels.data[index + 2] - candidatePixels.data[index + 2]);
      const alphaDelta = Math.abs(masterPixels.data[index + 3] - candidatePixels.data[index + 3]);
      const delta = Math.max(redDelta, greenDelta, blueDelta, alphaDelta);
      maxChannelDelta = Math.max(maxChannelDelta, delta);
      absoluteChannelDelta += redDelta + greenDelta + blueDelta + alphaDelta;
      const changed = delta > threshold;
      if (changed) changedPixels += 1;
      const masterLuma = masterPixels.data[index] * 0.2126
        + masterPixels.data[index + 1] * 0.7152
        + masterPixels.data[index + 2] * 0.0722;
      const candidateLuma = candidatePixels.data[index] * 0.2126
        + candidatePixels.data[index + 1] * 0.7152
        + candidatePixels.data[index + 2] * 0.0722;
      sumMasterLuma += masterLuma;
      sumCandidateLuma += candidateLuma;
      sumMasterLumaSquared += masterLuma * masterLuma;
      sumCandidateLumaSquared += candidateLuma * candidateLuma;
      sumLumaProduct += masterLuma * candidateLuma;
      const luma = Math.round(masterLuma);
      diff.data[index] = changed ? 229 : luma;
      diff.data[index + 1] = changed ? 42 : luma;
      diff.data[index + 2] = changed ? 50 : luma;
      diff.data[index + 3] = 255;
      overlay.data[index] = Math.round((masterPixels.data[index] + candidatePixels.data[index]) / 2);
      overlay.data[index + 1] = Math.round((masterPixels.data[index + 1] + candidatePixels.data[index + 1]) / 2);
      overlay.data[index + 2] = Math.round((masterPixels.data[index + 2] + candidatePixels.data[index + 2]) / 2);
      overlay.data[index + 3] = 255;
    }
    const pixelCount = width * height;
    const masterMean = sumMasterLuma / pixelCount;
    const candidateMean = sumCandidateLuma / pixelCount;
    const masterVariance = sumMasterLumaSquared / pixelCount - masterMean * masterMean;
    const candidateVariance = sumCandidateLumaSquared / pixelCount - candidateMean * candidateMean;
    const covariance = sumLumaProduct / pixelCount - masterMean * candidateMean;
    const c1 = (0.01 * 255) ** 2;
    const c2 = (0.03 * 255) ** 2;
    const ssim = ((2 * masterMean * candidateMean + c1) * (2 * covariance + c2))
      / ((masterMean ** 2 + candidateMean ** 2 + c1) * (masterVariance + candidateVariance + c2));
    const toDataUrl = imageData => {
      const output = document.createElement('canvas');
      output.width = width;
      output.height = height;
      output.getContext('2d')?.putImageData(imageData, 0, 0);
      return output.toDataURL('image/png');
    };
    masterImage.close();
    candidateImage.close();
    return {
      changedPixels,
      changedPixelRatio: changedPixels / pixelCount,
      maxChannelDelta,
      meanAbsoluteChannelDelta: absoluteChannelDelta / (pixelCount * 4),
      ssim,
      width,
      height,
      diffDataUrl: toDataUrl(diff),
      overlayDataUrl: toDataUrl(overlay)
    };
  }, {
    masterBase64: master.toString('base64'),
    candidateBase64: candidate.toString('base64'),
    threshold: perChannelDelta
  });
}

function writeDataUrl(path, dataUrl) {
  const comma = dataUrl.indexOf(',');
  if (comma < 0) throw new Error('Canvas evidence data URL is invalid.');
  const buffer = Buffer.from(dataUrl.slice(comma + 1), 'base64');
  writeFileSync(path, buffer);
  return buffer;
}

function sha256(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}
