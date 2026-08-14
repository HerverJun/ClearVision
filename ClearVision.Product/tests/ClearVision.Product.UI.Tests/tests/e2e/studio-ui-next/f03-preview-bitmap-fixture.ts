import { createHash } from 'node:crypto';
import { deflateSync } from 'node:zlib';
import contract from './f03-preview-bitmap-contract.json';

const { width, height, channels } = contract;

function crc32(bytes: Buffer): number {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) {
      crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function pngChunk(type: string, payload: Buffer): Buffer {
  const typeBytes = Buffer.from(type, 'ascii');
  const chunk = Buffer.allocUnsafe(payload.length + 12);
  chunk.writeUInt32BE(payload.length, 0);
  typeBytes.copy(chunk, 4);
  payload.copy(chunk, 8);
  chunk.writeUInt32BE(crc32(Buffer.concat([typeBytes, payload])), payload.length + 8);
  return chunk;
}

function pixelAt(x: number, y: number): readonly [number, number, number, number] {
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

  const inInspectionPatch = x >= 14 && x <= 36 && y >= 13 && y <= 28;
  if (inInspectionPatch) {
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

function createPreviewPng(): Buffer {
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
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    pngChunk('IHDR', header),
    pngChunk('IDAT', deflateSync(scanlines, { level: 9 })),
    pngChunk('IEND', Buffer.alloc(0))
  ]);
}

const bytes = createPreviewPng();
const sha256 = createHash('sha256').update(bytes).digest('hex');
if (sha256 !== contract.sha256) {
  throw new Error(`F03 preview bitmap fixture drifted from its contract: ${sha256}.`);
}
if (bytes.byteLength !== contract.byteLength) {
  throw new Error(`F03 preview bitmap fixture length drifted from its contract: ${bytes.byteLength}.`);
}
for (const sample of contract.samples) {
  const expected = pixelAt(sample.x, sample.y);
  if (expected.some((channel, index) => channel !== sample.rgba[index])) {
    throw new Error(`F03 preview bitmap sample drifted at ${sample.x},${sample.y}.`);
  }
}

export const f03PreviewBitmapFixture = Object.freeze({
  schemaVersion: contract.schemaVersion,
  contentType: contract.contentType,
  width,
  height,
  channels,
  bytes,
  byteLength: bytes.byteLength,
  sha256,
  samples: Object.freeze(contract.samples.map(sample => Object.freeze({
    x: sample.x,
    y: sample.y,
    rgba: Object.freeze([...sample.rgba])
  }))),
  roi: Object.freeze({ ...contract.roi })
});
