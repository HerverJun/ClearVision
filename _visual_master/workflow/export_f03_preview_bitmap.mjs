import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';

const workflowDir = dirname(fileURLToPath(import.meta.url));
const root = resolve(workflowDir, '..', '..');
const contractPath = join(
  root,
  'ClearVision.Product',
  'tests',
  'ClearVision.Product.UI.Tests',
  'tests',
  'e2e',
  'studio-ui-next',
  'f03-preview-bitmap-contract.json'
);
const fixtureSourcePath = join(
  root,
  'ClearVision.Product',
  'tests',
  'ClearVision.Product.UI.Tests',
  'tests',
  'e2e',
  'studio-ui-next',
  'f03-preview-bitmap-fixture.ts'
);
const outputDir = join(root, '_visual_master', 'current', 'assets');
const outputPath = join(outputDir, 'f03-preview-bitmap-100x100.png');
const evidencePath = join(outputDir, 'f03-preview-bitmap-100x100.json');

const contract = JSON.parse(await readFile(contractPath, 'utf8'));
const { width, height, channels } = contract;

function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) {
      crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function pngChunk(type, payload) {
  const typeBytes = Buffer.from(type, 'ascii');
  const chunk = Buffer.allocUnsafe(payload.length + 12);
  chunk.writeUInt32BE(payload.length, 0);
  typeBytes.copy(chunk, 4);
  payload.copy(chunk, 8);
  chunk.writeUInt32BE(crc32(Buffer.concat([typeBytes, payload])), payload.length + 8);
  return chunk;
}

function pixelAt(x, y) {
  const conveyorBand = y >= 38 && y <= 78;
  let red = conveyorBand ? 47 : 24;
  let green = conveyorBand ? 55 : 31;
  let blue = conveyorBand ? 62 : 38;

  const dx = x - 58;
  const dy = y - 57;
  const radius = Math.sqrt(dx * dx + dy * dy);
  if (radius <= 29) {
    const highlight = Math.max(0, 1 - radius / 29);
    const ring = radius >= 22 ? 28 : 0;
    red = Math.round(118 + highlight * 86 + ring);
    green = Math.round(126 + highlight * 82 + ring);
    blue = Math.round(132 + highlight * 76 + ring);
  }
  if (radius >= 16 && radius <= 18) {
    red = 70;
    green = 79;
    blue = 86;
  }
  if (x >= 14 && x <= 36 && y >= 13 && y <= 28) {
    red = 176;
    green = 190;
    blue = 199;
  }
  if (x >= 21 && x <= 30 && y >= 18 && y <= 22) {
    red = 170;
    green = 45;
    blue = 52;
  }
  const crack = radius < 20 && Math.abs((x - 49) - Math.floor((y - 43) * 0.42)) <= 1;
  if (crack) {
    red = 116;
    green = 28;
    blue = 34;
  }
  return [red, green, blue, 255];
}

const scanlines = Buffer.alloc((width * channels + 1) * height);
for (let y = 0; y < height; y += 1) {
  const rowOffset = y * (width * channels + 1);
  scanlines[rowOffset] = 0;
  for (let x = 0; x < width; x += 1) {
    const pixelOffset = rowOffset + 1 + x * channels;
    const pixel = pixelAt(x, y);
    scanlines[pixelOffset] = pixel[0];
    scanlines[pixelOffset + 1] = pixel[1];
    scanlines[pixelOffset + 2] = pixel[2];
    scanlines[pixelOffset + 3] = pixel[3];
  }
}

const header = Buffer.alloc(13);
header.writeUInt32BE(width, 0);
header.writeUInt32BE(height, 4);
header[8] = 8;
header[9] = 6;
const bytes = Buffer.concat([
  Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
  pngChunk('IHDR', header),
  pngChunk('IDAT', deflateSync(scanlines, { level: 9 })),
  pngChunk('IEND', Buffer.alloc(0))
]);
const sha256 = createHash('sha256').update(bytes).digest('hex');
if (sha256 !== contract.sha256 || bytes.byteLength !== contract.byteLength) {
  throw new Error(`Fixture contract mismatch: ${sha256}, ${bytes.byteLength} bytes.`);
}

await mkdir(outputDir, { recursive: true });
await writeFile(outputPath, bytes);
const fixtureSourceSha256 = createHash('sha256')
  .update(await readFile(fixtureSourcePath))
  .digest('hex');
await writeFile(evidencePath, `${JSON.stringify({
  schema_version: 'clearvision-current-bitmap-picture-layer.v1',
  functional_authority: 'current deterministic F03 preview bitmap fixture',
  source: 'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-preview-bitmap-fixture.ts',
  source_sha256: fixtureSourceSha256,
  contract: 'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-preview-bitmap-contract.json',
  output: '_visual_master/current/assets/f03-preview-bitmap-100x100.png',
  output_sha256: sha256,
  width,
  height,
  channels
}, null, 2)}\n`, 'utf8');

console.log(`${outputPath} ${sha256}`);
