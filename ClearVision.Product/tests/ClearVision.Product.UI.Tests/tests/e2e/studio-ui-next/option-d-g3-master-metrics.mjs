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
  '.tmp/studio-ui-next/option-d-g3/master-measurements.json'
);
const probeMode = process.argv.includes('--probe');

const masterSpecs = Object.freeze([
  {
    id: 'D02',
    file: '02_overview.png',
    sha256: 'a6a902196b5486817f80c094d469fa4d96e8c934fb2a36c5e7947fc3d5f24769',
    verticalEdges: [
      { id: 'content-start', range: [100, 240], sample: [360, 2040], expectedPixel: 166, minimumScore: 10 },
      { id: 'content-end', range: [3580, 3760], sample: [360, 2040], expectedPixel: 3692, minimumScore: 10 }
    ],
    horizontalEdges: [
      { id: 'masthead-end', range: [110, 220], sample: [0, 3840], expectedPixel: 145, minimumScore: 10 },
      { id: 'resume-start', range: [520, 760], sample: [120, 3740], expectedPixel: 624, minimumScore: 10 },
      { id: 'environment-start', range: [900, 1260], sample: [120, 3740], expectedPixel: 1212, minimumScore: 10 }
    ],
    samples: [{ id: 'page-surface', x: 1900, y: 400, expectedRgba: [248, 249, 251, 255] }]
  },
  {
    id: 'D03',
    file: '03_projects_data.png',
    sha256: 'fe6d5e6c368573de83d0a6a0ed46148a2f0e6f01d9e002565c63c6d4047c5e94',
    verticalEdges: [
      { id: 'library-start', range: [30, 160], sample: [560, 2040], expectedPixel: 66, minimumScore: 10 },
      { id: 'library-end', range: [3660, 3810], sample: [560, 2040], expectedPixel: 3766, minimumScore: 10 }
    ],
    horizontalEdges: [
      { id: 'masthead-end', range: [150, 270], sample: [0, 3840], expectedPixel: 204, minimumScore: 10 },
      { id: 'library-start', range: [500, 760], sample: [40, 3800], expectedPixel: 604, minimumScore: 10 },
      { id: 'pagination-start', range: [1800, 2050], sample: [40, 3800], expectedPixel: 1833, minimumScore: 10 }
    ],
    samples: [{ id: 'library-surface', x: 1900, y: 1500, expectedRgba: [254, 255, 254, 255] }]
  },
  {
    id: 'D04',
    file: '04_projects_empty.png',
    sha256: 'a0117dcd2b62a5cef6c499f4e0f658a4c3de255ae3da783640f6d6952f030087',
    verticalEdges: [
      { id: 'library-start', range: [30, 160], sample: [560, 2040], expectedPixel: 71, minimumScore: 10 },
      { id: 'library-end', range: [3660, 3810], sample: [560, 2040], expectedPixel: 3766, minimumScore: 10 }
    ],
    horizontalEdges: [
      { id: 'masthead-end', range: [140, 250], sample: [0, 3840], expectedPixel: 189, minimumScore: 10 },
      { id: 'library-start', range: [500, 760], sample: [40, 3800], expectedPixel: 577, minimumScore: 10 },
      { id: 'pagination-start', range: [1820, 2070], sample: [40, 3800], expectedPixel: 2001, minimumScore: 10 }
    ],
    samples: [{ id: 'empty-surface', x: 1900, y: 1320, expectedRgba: [249, 248, 248, 255] }]
  },
  {
    id: 'D15',
    file: '15_operator_catalog.png',
    sha256: 'a01ee6cfbcd1344c2340ce18ced5eb2cfce66450dd0509f5e87ccedead3a2d1f',
    verticalEdges: [
      { id: 'content-start', range: [70, 190], sample: [330, 2050], expectedPixel: 190, minimumScore: 10 },
      { id: 'content-end', range: [3650, 3780], sample: [330, 2050], expectedPixel: 3716, minimumScore: 10 }
    ],
    horizontalEdges: [
      { id: 'masthead-end', range: [90, 180], sample: [0, 3840], expectedPixel: 130, minimumScore: 10 },
      { id: 'filters-start', range: [300, 520], sample: [60, 3780], expectedPixel: 499, minimumScore: 10 },
      { id: 'table-start', range: [600, 760], sample: [60, 3780], expectedPixel: 710, minimumScore: 10 }
    ],
    samples: [{ id: 'table-surface', x: 1900, y: 1120, expectedRgba: [254, 254, 254, 255] }]
  },
  {
    id: 'D22',
    file: '22_diagnostics.png',
    sha256: '0415729663e19fb6b2527956eec74d9f64284cefa8b1ba7be7f8c4d5c9e6ee97',
    verticalEdges: [
      { id: 'content-start', range: [90, 250], sample: [320, 2050], expectedPixel: 226, minimumScore: 10 },
      { id: 'content-end', range: [3570, 3740], sample: [320, 2050], expectedPixel: 3679, minimumScore: 10 }
    ],
    horizontalEdges: [
      { id: 'masthead-end', range: [140, 200], sample: [0, 3840], expectedPixel: 169, minimumScore: 10 },
      { id: 'status-start', range: [650, 760], sample: [90, 3740], expectedPixel: 707, minimumScore: 10 },
      { id: 'version-start', range: [930, 1260], sample: [90, 3740], expectedPixel: 1196, minimumScore: 10 }
    ],
    samples: [{ id: 'page-surface', x: 1900, y: 520, expectedRgba: [252, 253, 253, 255] }]
  },
  {
    id: 'D23',
    file: '23_about.png',
    sha256: '4b085e25511b6fcffc72d6d6af33c39574baa9bfb7106729485b4035a77c8e84',
    verticalEdges: [
      { id: 'content-start', range: [250, 430], sample: [360, 2050], expectedPixel: 426, minimumScore: 10 },
      { id: 'content-end', range: [3320, 3520], sample: [360, 2050], expectedPixel: 3401, minimumScore: 10 }
    ],
    horizontalEdges: [
      { id: 'masthead-end', range: [100, 220], sample: [0, 3840], expectedPixel: 162, minimumScore: 10 },
      { id: 'product-grid-start', range: [620, 900], sample: [250, 3520], expectedPixel: 896, minimumScore: 10 },
      { id: 'support-start', range: [1320, 1660], sample: [250, 3520], expectedPixel: 1473, minimumScore: 10 }
    ],
    samples: [{ id: 'page-surface', x: 1900, y: 500, expectedRgba: [254, 254, 254, 255] }]
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
  return { ...evidence, cssPixel: toCssPixels(edge.pixel), score: Number(edge.score.toFixed(3)) };
}

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();
const measurements = [];

try {
  for (const spec of masterSpecs) {
    const path = resolve(screensDirectory, spec.file);
    const buffer = readFileSync(path);
    const masterSha256 = sha256(buffer);
    if (masterSha256 !== spec.sha256) throw new Error(`${spec.id} SHA-256 changed: ${masterSha256}.`);
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
        return { x, y, rgba: [pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]] };
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
  fixtureId: 'option-d-g3-master-measurements.v2',
  visualAuthority: '_visual_master/option_D/screens',
  scale: { cssToOutput: 2 },
  measurementMethod: 'bounded edge response plus exact glyph-safe RGBA sampling; no resampling or masks',
  assertionPolicy: 'exact SHA-256, exact edge pixel, minimum edge score and exact RGBA sample',
  assertionResult: probeMode ? 'PROBE_ONLY' : 'PASS',
  measurements
}, null, 2)}\n`);
console.log(outputPath);
