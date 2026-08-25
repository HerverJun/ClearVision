import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const workspaceRoot = resolve(scriptDirectory, '../../../../../..');
const screensDirectory = resolve(workspaceRoot, '_visual_master/option_D/screens');
const outputPath = resolve(
  workspaceRoot,
  '.tmp/studio-ui-next/option-d-g2/master-measurements.json'
);
const probeMode = process.argv.includes('--probe');

const masterSpecs = Object.freeze([
  {
    id: 'D01',
    file: '01_login.png',
    sha256: 'bf3adebb2451161ca76902d531f9953c38bd6c6f1484145f4b4818935b50a241',
    verticalEdges: [
      { id: 'auth-frame-start', range: [1260, 1440], sample: [500, 1640], expectedPixel: 1367, minimumScore: 20 },
      { id: 'auth-frame-end', range: [2380, 2560], sample: [500, 1640], expectedPixel: 2472, minimumScore: 20 }
    ],
    horizontalEdges: [
      { id: 'auth-frame-start', range: [430, 620], sample: [1360, 2480], expectedPixel: 510, minimumScore: 20 },
      { id: 'username-control-start', range: [790, 930], sample: [1440, 2400], expectedPixel: 861, minimumScore: 20 },
      { id: 'primary-action-start', range: [1370, 1510], sample: [1440, 2400], expectedPixel: 1438, minimumScore: 20 },
      { id: 'auth-frame-end', range: [1600, 1720], sample: [1360, 2480], expectedPixel: 1643, minimumScore: 20 }
    ],
    samples: [
      { id: 'page-surface', x: 640, y: 1080, expectedRgba: [242, 241, 245, 255] },
      { id: 'frame-surface', x: 1540, y: 620, expectedRgba: [255, 254, 254, 255] },
      { id: 'control-surface', x: 1900, y: 900, expectedRgba: [254, 254, 254, 255] },
      { id: 'primary-action', x: 1900, y: 1460, expectedRgba: [14, 86, 145, 255] }
    ]
  },
  {
    id: 'D24',
    file: '24_forbidden.png',
    sha256: 'e6171bedda03d2c06ae5bb6c66241a8993b08f360657ff35c8245ad7eeb208ca',
    verticalEdges: [
      { id: 'authority-frame-start', range: [1060, 1240], sample: [560, 1540], expectedPixel: 1169, minimumScore: 20 },
      { id: 'authority-frame-end', range: [2580, 2760], sample: [560, 1540], expectedPixel: 2669, minimumScore: 20 }
    ],
    horizontalEdges: [
      { id: 'masthead-end', range: [130, 190], sample: [0, 3840], expectedPixel: 155, minimumScore: 20 },
      { id: 'authority-frame-start', range: [500, 760], sample: [1160, 2680], expectedPixel: 592, minimumScore: 20 },
      { id: 'warning-start', range: [1010, 1220], sample: [1280, 2560], expectedPixel: 1175, minimumScore: 20 },
      { id: 'authority-frame-end', range: [1380, 1600], sample: [1160, 2680], expectedPixel: 1487, minimumScore: 20 }
    ],
    samples: [
      { id: 'page-surface', x: 640, y: 1080, expectedRgba: [251, 250, 251, 255] },
      { id: 'masthead-surface', x: 2000, y: 80, expectedRgba: [253, 253, 253, 255] },
      { id: 'frame-surface', x: 2400, y: 760, expectedRgba: [255, 255, 255, 255] },
      { id: 'warning-surface', x: 1900, y: 1160, expectedRgba: [253, 245, 242, 255] }
    ]
  }
]);

rmSync(outputPath, { force: true });

function sha256(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}

function toCssPixels(value) {
  return Number((value / 2).toFixed(1));
}

function assertMeasurement(spec, measured) {
  for (const [axis, expectations] of [
    ['verticalEdges', spec.verticalEdges],
    ['horizontalEdges', spec.horizontalEdges]
  ]) {
    for (const expectation of expectations) {
      const actual = measured[axis].find(edge => edge.id === expectation.id);
      if (!actual) throw new Error(`${spec.id}/${expectation.id} measurement is missing.`);
      if (actual.pixel !== expectation.expectedPixel) {
        throw new Error(
          `${spec.id}/${expectation.id} moved to output pixel ${actual.pixel}; expected ${expectation.expectedPixel}.`
        );
      }
      if (actual.score < expectation.minimumScore) {
        throw new Error(
          `${spec.id}/${expectation.id} edge score ${actual.score} is below ${expectation.minimumScore}.`
        );
      }
    }
  }

  for (const expectation of spec.samples) {
    const actual = measured.samples.find(sample => sample.id === expectation.id);
    if (!actual) throw new Error(`${spec.id}/${expectation.id} color sample is missing.`);
    if (JSON.stringify(actual.rgba) !== JSON.stringify(expectation.expectedRgba)) {
      throw new Error(
        `${spec.id}/${expectation.id} is rgba(${actual.rgba.join(',')}); expected rgba(${expectation.expectedRgba.join(',')}).`
      );
    }
  }
}

function serializeEdge(edge) {
  const { expectedPixel: _expectedPixel, minimumScore: _minimumScore, ...evidence } = edge;
  return {
    ...evidence,
    cssPixel: toCssPixels(edge.pixel),
    score: Number(edge.score.toFixed(3))
  };
}

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();
const measurements = [];

try {
  for (const spec of masterSpecs) {
    const path = resolve(screensDirectory, spec.file);
    const buffer = readFileSync(path);
    const masterSha256 = sha256(buffer);
    if (masterSha256 !== spec.sha256) {
      throw new Error(`${spec.id} SHA-256 changed: ${masterSha256}.`);
    }
    const measured = await page.evaluate(async ({ dataUrl, verticalEdges, horizontalEdges, samples }) => {
      const response = await fetch(dataUrl);
      const bitmap = await createImageBitmap(await response.blob());
      const canvas = document.createElement('canvas');
      canvas.width = bitmap.width;
      canvas.height = bitmap.height;
      const context = canvas.getContext('2d', { willReadFrequently: true });
      if (!context) throw new Error('Master measurement canvas is unavailable.');
      context.drawImage(bitmap, 0, 0);
      bitmap.close();
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;

      function offset(x, y) {
        return (y * canvas.width + x) * 4;
      }

      function colorAt(x, y) {
        const index = offset(x, y);
        const rgba = [pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]];
        return {
          x,
          y,
          rgba,
          hex: `#${rgba.slice(0, 3).map(value => value.toString(16).padStart(2, '0')).join('')}`
        };
      }

      function channelDelta(left, right) {
        const leftIndex = left * 4;
        const rightIndex = right * 4;
        return Math.abs(pixels[leftIndex] - pixels[rightIndex]) +
          Math.abs(pixels[leftIndex + 1] - pixels[rightIndex + 1]) +
          Math.abs(pixels[leftIndex + 2] - pixels[rightIndex + 2]);
      }

      function bestVerticalEdge(edge) {
        let best = { pixel: edge.range[0], score: -1 };
        for (let x = edge.range[0]; x <= edge.range[1]; x += 1) {
          let score = 0;
          let count = 0;
          for (let y = edge.sample[0]; y < edge.sample[1]; y += 8) {
            score += channelDelta(
              y * canvas.width + Math.max(0, x - 2),
              y * canvas.width + Math.min(canvas.width - 1, x + 2)
            );
            count += 1;
          }
          const average = count === 0 ? 0 : score / count;
          if (average > best.score) best = { pixel: x, score: average };
        }
        return { ...edge, ...best };
      }

      function bestHorizontalEdge(edge) {
        let best = { pixel: edge.range[0], score: -1 };
        for (let y = edge.range[0]; y <= edge.range[1]; y += 1) {
          let score = 0;
          let count = 0;
          for (let x = edge.sample[0]; x < edge.sample[1]; x += 8) {
            score += channelDelta(
              Math.max(0, y - 2) * canvas.width + x,
              Math.min(canvas.height - 1, y + 2) * canvas.width + x
            );
            count += 1;
          }
          const average = count === 0 ? 0 : score / count;
          if (average > best.score) best = { pixel: y, score: average };
        }
        return { ...edge, ...best };
      }

      return {
        width: canvas.width,
        height: canvas.height,
        verticalEdges: verticalEdges.map(bestVerticalEdge),
        horizontalEdges: horizontalEdges.map(bestHorizontalEdge),
        samples: samples.map(sample => ({ id: sample.id, ...colorAt(sample.x, sample.y), method: 'exact-pixel' }))
      };
    }, {
      dataUrl: `data:image/png;base64,${buffer.toString('base64')}`,
      verticalEdges: spec.verticalEdges,
      horizontalEdges: spec.horizontalEdges,
      samples: spec.samples
    });

    if (measured.width !== 3840 || measured.height !== 2160) {
      throw new Error(`${spec.id} has unexpected dimensions ${measured.width}x${measured.height}.`);
    }
    if (!probeMode) assertMeasurement(spec, measured);
    measurements.push({
      id: spec.id,
      file: spec.file,
      sha256: masterSha256,
      outputPixels: { width: measured.width, height: measured.height },
      cssViewport: { width: 1920, height: 1080 },
      verticalEdges: measured.verticalEdges.map(serializeEdge),
      horizontalEdges: measured.horizontalEdges.map(serializeEdge),
      samples: measured.samples.map(sample => ({
        ...sample,
        cssX: toCssPixels(sample.x),
        cssY: toCssPixels(sample.y)
      }))
    });
  }
} finally {
  await browser.close();
}

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, `${JSON.stringify({
  schemaVersion: 2,
  fixtureId: 'option-d-g2-master-measurements.v2',
  visualAuthority: '_visual_master/option_D/screens',
  scale: { cssToOutput: 2 },
  measurementMethod: 'bounded edge response plus exact glyph-safe RGBA sampling; no resampling or masks',
  assertionPolicy: 'exact SHA-256, exact edge pixel, minimum edge score and exact RGBA sample',
  assertionResult: probeMode ? 'PROBE_ONLY' : 'PASS',
  measurements
}, null, 2)}\n`);
console.log(outputPath);
