#!/usr/bin/env node

import { execFile } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { performance } from 'node:perf_hooks';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);

function parseArgs(argv) {
  const options = {
    outDir: null,
    iterations: 180,
    warmup: 30
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === '--out-dir') {
      options.outDir = argv[++index] || null;
    } else if (arg === '--iterations') {
      options.iterations = Number(argv[++index]);
    } else if (arg === '--warmup') {
      options.warmup = Number(argv[++index]);
    }
  }

  if (!options.outDir) {
    throw new Error('Usage: node scripts/run-g16-release-benchmark.mjs --out-dir <directory>');
  }

  if (!Number.isInteger(options.iterations) || options.iterations < 30) {
    throw new Error('--iterations must be an integer >= 30.');
  }

  if (!Number.isInteger(options.warmup) || options.warmup < 0) {
    throw new Error('--warmup must be an integer >= 0.');
  }

  return options;
}

function percentile(values, p) {
  if (!values.length) {
    return 0;
  }

  const sorted = [...values].sort((left, right) => left - right);
  const index = Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1);
  return sorted[index];
}

function round(value, digits = 3) {
  return Number(value.toFixed(digits));
}

function createPrimitives(count) {
  return Array.from({ length: count }, (_, index) => {
    const column = index % 40;
    const row = Math.floor(index / 40);
    return {
      id: `primitive-${index}`,
      type: index % 5 === 0 ? 'polygon' : index % 3 === 0 ? 'circle' : 'rect',
      x: column * 11.25,
      y: row * 9.5,
      width: 7 + (index % 17),
      height: 5 + (index % 13),
      score: (index % 100) / 100,
      points: [
        [column * 11.25, row * 9.5],
        [column * 11.25 + 3, row * 9.5 + 2],
        [column * 11.25 + 5, row * 9.5 + 6]
      ]
    };
  });
}

function updateScene(primitives, frameIndex) {
  const phase = frameIndex / 9;
  let checksum = 0;
  const updated = new Array(primitives.length);

  for (let index = 0; index < primitives.length; index += 1) {
    const primitive = primitives[index];
    const drift = Math.sin(phase + index * 0.013) * 1.75;
    const confidence = Math.max(0, Math.min(1, primitive.score + Math.cos(phase + index) * 0.015));
    const x = primitive.x + drift;
    const y = primitive.y - drift;
    checksum += x * 0.17 + y * 0.13 + confidence;
    updated[index] = {
      ...primitive,
      x,
      y,
      confidence,
      selected: index === frameIndex % primitives.length
    };
  }

  return { updated, checksum };
}

function renderScenePayload(primitives, frameIndex, updateChecksum) {
  let svgPathBytes = 0;
  let selectedCount = 0;
  let visibleCount = 0;
  const maxWidth = 3840;
  const maxHeight = 2160;

  for (const primitive of primitives) {
    if (primitive.x < -100 || primitive.x > maxWidth + 100 || primitive.y < -100 || primitive.y > maxHeight + 100) {
      continue;
    }

    visibleCount += 1;
    if (primitive.selected) {
      selectedCount += 1;
    }

    if (primitive.type === 'polygon') {
      svgPathBytes += primitive.points.reduce((total, point) => total + point[0] + point[1], 0);
    } else {
      svgPathBytes += primitive.x + primitive.y + primitive.width + primitive.height;
    }
  }

  return {
    frameIndex,
    visibleCount,
    selectedCount,
    checksum: round(updateChecksum + svgPathBytes, 6)
  };
}

function runScenario(primitiveCount, { iterations, warmup }) {
  let primitives = createPrimitives(primitiveCount);
  const frameTimes = [];
  const updateTimes = [];
  let previewNotifications = 0;
  let subscriptionNotifications = 0;
  let payloadChecksum = 0;
  const subscriberCount = 6;

  for (let frame = 0; frame < warmup + iterations; frame += 1) {
    const frameStart = performance.now();
    const updateStart = performance.now();
    const update = updateScene(primitives, frame);
    const updateEnd = performance.now();
    const payload = renderScenePayload(update.updated, frame, update.checksum);
    const frameEnd = performance.now();

    primitives = update.updated;
    payloadChecksum += payload.checksum;

    if (frame % 12 === 0) {
      previewNotifications += 1;
    }
    subscriptionNotifications += subscriberCount;

    if (frame >= warmup) {
      updateTimes.push(updateEnd - updateStart);
      frameTimes.push(frameEnd - frameStart);
    }
  }

  const measuredSeconds = frameTimes.reduce((total, value) => total + value, 0) / 1000;

  return {
    primitiveCount,
    iterations,
    p95FrameTimeMs: round(percentile(frameTimes, 95)),
    p95UpdateTimeMs: round(percentile(updateTimes, 95)),
    averageFrameTimeMs: round(frameTimes.reduce((total, value) => total + value, 0) / frameTimes.length),
    maxFrameTimeMs: round(Math.max(...frameTimes)),
    previewNotifications,
    previewNotificationsPerSecond: measuredSeconds > 0 ? round(previewNotifications / measuredSeconds, 3) : 0,
    subscriptionNotifications,
    subscriptionNotificationsPerSecond: measuredSeconds > 0 ? round(subscriptionNotifications / measuredSeconds, 3) : 0,
    checksum: round(payloadChecksum, 3)
  };
}

async function collectDisplayMetrics() {
  if (process.platform !== 'win32') {
    return {
      status: 'NOT_PERFORMED',
      reason: `Display metrics probe is Windows-only; current platform is ${process.platform}.`
    };
  }

  const script = `
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$screens = [System.Windows.Forms.Screen]::AllScreens | ForEach-Object {
  [pscustomobject]@{
    DeviceName = $_.DeviceName
    Width = $_.Bounds.Width
    Height = $_.Bounds.Height
    Primary = $_.Primary
  }
}
$graphics = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
try {
  [pscustomobject]@{
    Status = "PERFORMED"
    DpiX = [math]::Round($graphics.DpiX, 2)
    DpiY = [math]::Round($graphics.DpiY, 2)
    ScalePercent = [math]::Round(($graphics.DpiX / 96.0) * 100, 2)
    Screens = $screens
  } | ConvertTo-Json -Depth 5
} finally {
  $graphics.Dispose()
}
`;

  try {
    const { stdout } = await execFileAsync('powershell.exe', ['-NoProfile', '-Command', script], {
      windowsHide: true,
      maxBuffer: 1024 * 1024
    });
    return JSON.parse(stdout);
  } catch (error) {
    return {
      status: 'NOT_PERFORMED',
      reason: error.message
    };
  }
}

function buildMarkdown(report) {
  const lines = [
    '# G16 Release Benchmark',
    '',
    `- StartedAtUtc: ${report.startedAtUtc}`,
    `- Node: ${report.environment.node}`,
    `- OS: ${report.environment.platform} ${report.environment.release} ${report.environment.arch}`,
    `- CPU: ${report.environment.cpuModel}`,
    `- Logical cores: ${report.environment.logicalCores}`,
    `- Total memory MB: ${report.environment.totalMemoryMB}`,
    `- Display probe: ${report.display.status || report.display.Status}`,
    ''
  ];

  if ((report.display.status || report.display.Status) === 'PERFORMED') {
    lines.push(
      `- DPI: ${report.display.DpiX}x${report.display.DpiY}`,
      `- Scale percent: ${report.display.ScalePercent}`,
      `- Screens: ${JSON.stringify(report.display.Screens)}`,
      ''
    );
  } else {
    lines.push(`- Display probe reason: ${report.display.reason}`, '');
  }

  lines.push(
    '| Primitive count | p95 frame ms | p95 update ms | avg frame ms | max frame ms | heap delta MB | preview notifications/sec | subscription notifications/sec |',
    '| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |'
  );

  for (const scenario of report.scenarios) {
    lines.push(`| ${scenario.primitiveCount} | ${scenario.p95FrameTimeMs} | ${scenario.p95UpdateTimeMs} | ${scenario.averageFrameTimeMs} | ${scenario.maxFrameTimeMs} | ${scenario.heapDeltaMB} | ${scenario.previewNotificationsPerSecond} | ${scenario.subscriptionNotificationsPerSecond} |`);
  }

  lines.push(
    '',
    '## DPI And Resolution Matrix',
    '',
    `- Automated matrix status: ${report.dpiResolutionMatrix.status}`,
    `- Requested DPI: ${report.dpiResolutionMatrix.requestedDpi.join(', ')}`,
    `- Requested resolutions: ${report.dpiResolutionMatrix.requestedResolutions.join(', ')}`,
    `- Note: ${report.dpiResolutionMatrix.note}`,
    '',
    '## Scope',
    '',
    '- This benchmark is synthetic and hardware-independent. It exercises scene primitive update/render bookkeeping, preview notification cadence, and subscription fanout without camera or external device dependencies.',
    '- Real WebView2 startup, multi-DPI visual inspection, and no-Node target-machine launch remain manual/environmental release gates when not separately performed.'
  );

  return `${lines.join('\n')}\n`;
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  const outDir = path.resolve(options.outDir);
  await mkdir(outDir, { recursive: true });

  const startedAtUtc = new Date().toISOString();
  const environment = {
    node: process.version,
    platform: os.platform(),
    release: os.release(),
    arch: os.arch(),
    cpuModel: os.cpus()[0]?.model || 'unknown',
    logicalCores: os.cpus().length,
    totalMemoryMB: Math.round(os.totalmem() / 1024 / 1024)
  };

  const display = await collectDisplayMetrics();
  const scenarios = [];
  for (const primitiveCount of [300, 1000]) {
    const heapBefore = process.memoryUsage().heapUsed;
    const scenario = runScenario(primitiveCount, options);
    const heapAfter = process.memoryUsage().heapUsed;
    scenarios.push({
      ...scenario,
      heapDeltaMB: round((heapAfter - heapBefore) / 1024 / 1024, 3)
    });
  }

  const report = {
    startedAtUtc,
    environment,
    display,
    scenarios,
    dpiResolutionMatrix: {
      status: 'NOT_PERFORMED',
      requestedDpi: ['100%', '125%', '150%', '200%'],
      requestedResolutions: ['1366x768', '1920x1080', '2560x1440', '3840x2160'],
      note: 'The script records the current display when available, but does not mutate OS DPI or resolution settings.'
    }
  };

  const jsonPath = path.join(outDir, 'G16-benchmark-2026-07-04.json');
  const mdPath = path.join(outDir, 'G16-benchmark-2026-07-04.md');
  await writeFile(jsonPath, `${JSON.stringify(report, null, 2)}\n`, 'utf8');
  await writeFile(mdPath, buildMarkdown(report), 'utf8');

  console.log(`G16 benchmark JSON: ${jsonPath}`);
  console.log(`G16 benchmark report: ${mdPath}`);
  for (const scenario of report.scenarios) {
    console.log(`primitiveCount=${scenario.primitiveCount} p95FrameMs=${scenario.p95FrameTimeMs} p95UpdateMs=${scenario.p95UpdateTimeMs} heapDeltaMB=${scenario.heapDeltaMB}`);
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
