import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const workspaceRoot = resolve(scriptDirectory, '../../../../../..');
const mastersDirectory = resolve(workspaceRoot, '_visual_master/option_D/masters');
const outputPath = resolve(
  workspaceRoot,
  '.tmp/studio-ui-next/option-d-g1/master-measurements.json'
);

const masterSpecs = Object.freeze([
  {
    id: 'D05',
    file: '05_flow_editor.png',
    sha256: '247efff95e87fdd626f36dfae2dced6d94465d0c697408dca62e69d6ccacedc3',
    verticalEdges: [
      { id: 'product-rail-end', range: [176, 220], sample: [520, 2020], expectedPixel: 200, minimumScore: 40 },
      { id: 'operator-pane-end', range: [700, 850], sample: [420, 2020], expectedPixel: 764, minimumScore: 40 },
      { id: 'inspector-pane-start', range: [2940, 3070], sample: [420, 2020], expectedPixel: 3009, minimumScore: 40 }
    ],
    horizontalEdges: [
      { id: 'global-header-end', range: [120, 170], sample: [240, 3720], expectedPixel: 138, minimumScore: 40 },
      { id: 'project-context-end', range: [220, 280], sample: [0, 3840], expectedPixel: 249, minimumScore: 40 },
      { id: 'workspace-command-end', range: [380, 440], sample: [200, 3780], expectedPixel: 403, minimumScore: 40 },
      { id: 'status-strip-start', range: [2010, 2080], sample: [0, 3840], expectedPixel: 2049, minimumScore: 40 }
    ],
    samples: [
      { id: 'rail-surface', x: 96, y: 1000, expectedRgba: [249, 250, 252, 255] },
      { id: 'operator-surface', x: 500, y: 1000, expectedRgba: [254, 254, 254, 255] },
      { id: 'canvas-surface', x: 1800, y: 1100, expectedRgba: [254, 253, 253, 255] },
      { id: 'inspector-surface', x: 3350, y: 1100, expectedRgba: [250, 252, 253, 255] },
      { id: 'primary-action', x: 1480, y: 350, expectedRgba: [3, 94, 168, 255] },
      { id: 'canvas-connection', x: 1710, y: 870, expectedRgba: [22, 111, 159, 255] }
    ]
  },
  {
    id: 'D13',
    file: '13_ai_workspace.png',
    sha256: '0e2875749de6fc6d1971517a530f6a9daae4f935456f035482dc85bf6cf91b1d',
    verticalEdges: [
      { id: 'product-rail-end', range: [170, 220], sample: [0, 2160], expectedPixel: 196, minimumScore: 100 },
      { id: 'readiness-pane-end', range: [920, 1040], sample: [350, 2020], expectedPixel: 986, minimumScore: 100 },
      { id: 'handoff-pane-start', range: [2750, 2860], sample: [350, 2020], expectedPixel: 2813, minimumScore: 100 }
    ],
    horizontalEdges: [
      { id: 'global-header-end', range: [130, 200], sample: [200, 3840], expectedPixel: 166, minimumScore: 100 },
      { id: 'local-title-end', range: [300, 380], sample: [190, 3840], expectedPixel: 344, minimumScore: 100 },
      { id: 'session-strip-start', range: [1990, 2070], sample: [190, 3840], expectedPixel: 1999, minimumScore: 100 }
    ],
    samples: [
      { id: 'rail-surface', x: 96, y: 1000, expectedRgba: [10, 20, 34, 255] },
      { id: 'page-surface', x: 1900, y: 1000, expectedRgba: [254, 254, 254, 255] },
      { id: 'readiness-surface', x: 600, y: 1000, expectedRgba: [244, 244, 245, 255] },
      { id: 'handoff-surface', x: 3300, y: 1000, expectedRgba: [255, 255, 255, 255] },
      { id: 'warning-surface', x: 1800, y: 750, expectedRgba: [254, 249, 239, 255] },
      { id: 'primary-action', x: 590, y: 1680, expectedRgba: [6, 81, 172, 255] }
    ]
  },
  {
    id: 'D16',
    file: '16_system_settings.png',
    sha256: '525b960075f34db309f2a4871afef54fb100474f2d6479f2447262a2ad98a35e',
    verticalEdges: [
      { id: 'product-rail-end', range: [170, 230], sample: [0, 2160], expectedPixel: 201, minimumScore: 100 },
      { id: 'settings-rail-end', range: [880, 990], sample: [180, 2160], expectedPixel: 934, minimumScore: 80 }
    ],
    horizontalEdges: [
      { id: 'global-header-end', range: [130, 205], sample: [190, 3840], expectedPixel: 174, minimumScore: 80 },
      { id: 'save-footer-start', range: [1810, 1900], sample: [930, 3840], expectedPixel: 1850, minimumScore: 60 }
    ],
    samples: [
      { id: 'rail-surface', x: 20, y: 1000, expectedRgba: [10, 22, 34, 255] },
      { id: 'settings-rail-surface', x: 560, y: 1000, expectedRgba: [250, 251, 252, 255] },
      { id: 'page-surface', x: 2100, y: 600, expectedRgba: [250, 251, 252, 255] },
      { id: 'selected-nav-surface', x: 550, y: 735, expectedRgba: [251, 227, 227, 255] },
      { id: 'field-surface', x: 2500, y: 1000, expectedRgba: [254, 254, 254, 255] },
      { id: 'primary-action', x: 3500, y: 1980, expectedRgba: [4, 76, 163, 255] }
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
    const path = resolve(mastersDirectory, spec.file);
    const buffer = readFileSync(path);
    const masterSha256 = sha256(buffer);
    if (masterSha256 !== spec.sha256) {
      throw new Error(`${spec.id} SHA-256 changed: ${masterSha256}.`);
    }
    const measured = await page.evaluate(async ({
      dataUrl,
      verticalEdges,
      horizontalEdges,
      samples
    }) => {
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
        const rgba = [
          pixels[index],
          pixels[index + 1],
          pixels[index + 2],
          pixels[index + 3]
        ];
        return {
          x,
          y,
          rgba,
          hex: `#${rgba.slice(0, 3).map(value => value.toString(16).padStart(2, '0')).join('')}`
        };
      }

      function sampleColor(sample) {
        return { ...colorAt(sample.x, sample.y), method: 'exact-pixel' };
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
        samples: samples.map(sample => ({ id: sample.id, ...sampleColor(sample) }))
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
    assertMeasurement(spec, measured);
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
  fixtureId: 'option-d-g1-master-measurements.v2',
  visualAuthority: '_visual_master/option_D/masters',
  scale: { cssToOutput: 2 },
  measurementMethod: 'bounded edge response plus exact glyph-safe RGBA sampling; no resampling or masks',
  assertionPolicy: 'exact SHA-256, exact edge pixel, minimum edge score and exact RGBA sample',
  assertionResult: 'PASS',
  measurements
}, null, 2)}\n`);
console.log(outputPath);
