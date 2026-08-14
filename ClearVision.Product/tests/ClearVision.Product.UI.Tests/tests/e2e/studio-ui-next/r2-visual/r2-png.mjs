import { inflateSync } from 'node:zlib';

const signature = Buffer.from('89504e470d0a1a0a', 'hex');
const channelsByColorType = Object.freeze({ 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 });
const knownCriticalChunks = new Set(['IHDR', 'PLTE', 'IDAT', 'IEND']);

export function inspectPngBuffer(buffer) {
  if (!Buffer.isBuffer(buffer) || buffer.length < 45 || !buffer.subarray(0, 8).equals(signature)) {
    throw new Error('PNG signature or minimum structure is invalid');
  }
  let offset = 8;
  let header = null;
  let sawEnd = false;
  let paletteEntries = null;
  let sawImageData = false;
  let imageDataEnded = false;
  const imageData = [];
  while (offset < buffer.length) {
    if (offset + 12 > buffer.length) throw new Error('PNG chunk header is truncated');
    const length = buffer.readUInt32BE(offset);
    const type = buffer.subarray(offset + 4, offset + 8).toString('ascii');
    if (!/^[A-Za-z]{2}[A-Z][A-Za-z]$/.test(type)) throw new Error('PNG chunk type is invalid');
    if (/^[A-Z]/.test(type) && !knownCriticalChunks.has(type)) {
      throw new Error(`PNG contains unknown critical chunk ${type}`);
    }
    const dataStart = offset + 8;
    const dataEnd = dataStart + length;
    const crcOffset = dataEnd;
    if (crcOffset + 4 > buffer.length) throw new Error(`PNG ${type} chunk is truncated`);
    const expectedCrc = buffer.readUInt32BE(crcOffset);
    const actualCrc = crc32(buffer.subarray(offset + 4, dataEnd));
    if (actualCrc !== expectedCrc) throw new Error(`PNG ${type} CRC is invalid`);
    const data = buffer.subarray(dataStart, dataEnd);
    if (type === 'IHDR') {
      if (header || length !== 13 || offset !== 8) throw new Error('PNG IHDR is invalid');
      header = {
        width: data.readUInt32BE(0),
        height: data.readUInt32BE(4),
        bitDepth: data[8],
        colorType: data[9],
        compression: data[10],
        filter: data[11],
        interlace: data[12]
      };
    } else if (!header) {
      throw new Error('PNG IHDR must be the first chunk');
    } else if (type === 'PLTE') {
      if (paletteEntries !== null || sawImageData || length === 0 || length % 3 !== 0 || length > 768) {
        throw new Error('PNG PLTE is invalid');
      }
      paletteEntries = length / 3;
    } else if (type === 'IDAT') {
      if (imageDataEnded) throw new Error('PNG IDAT chunks must be consecutive');
      sawImageData = true;
      imageData.push(data);
    } else if (type === 'IEND') {
      if (length !== 0 || !sawImageData) throw new Error('PNG IEND is invalid');
      sawEnd = true;
      offset = crcOffset + 4;
      break;
    } else if (sawImageData) {
      imageDataEnded = true;
    }
    offset = crcOffset + 4;
  }
  if (!header || !sawEnd || offset !== buffer.length || imageData.length === 0) {
    throw new Error('PNG is missing IHDR, IDAT, or terminal IEND');
  }
  validateHeader(header);
  validatePalette(header, paletteEntries);
  let decoded;
  try {
    decoded = inflateSync(Buffer.concat(imageData));
  } catch (error) {
    throw new Error(`PNG IDAT cannot be decompressed: ${error instanceof Error ? error.message : String(error)}`);
  }
  const channels = channelsByColorType[header.colorType];
  const rowBytes = Math.ceil(header.width * channels * header.bitDepth / 8);
  const expectedLength = (rowBytes + 1) * header.height;
  if (decoded.length !== expectedLength) throw new Error('PNG decoded scanline length is invalid');
  for (let row = 0; row < header.height; row += 1) {
    if (decoded[row * (rowBytes + 1)] > 4) throw new Error(`PNG row ${row} has an invalid filter type`);
  }
  return Object.freeze({ width: header.width, height: header.height });
}

function validatePalette(header, paletteEntries) {
  if (header.colorType === 3) {
    if (paletteEntries === null) throw new Error('Indexed PNG is missing PLTE');
    if (paletteEntries > 2 ** header.bitDepth) throw new Error('Indexed PNG PLTE has too many entries');
    return;
  }
  if ((header.colorType === 0 || header.colorType === 4) && paletteEntries !== null) {
    throw new Error('Grayscale PNG cannot contain PLTE');
  }
}

function validateHeader(header) {
  if (!Number.isInteger(header.width) || header.width < 1 || !Number.isInteger(header.height) || header.height < 1) {
    throw new Error('PNG dimensions are invalid');
  }
  if (!Object.hasOwn(channelsByColorType, header.colorType)) throw new Error('PNG color type is unsupported');
  const allowedDepths = header.colorType === 0
    ? [1, 2, 4, 8, 16]
    : header.colorType === 3
      ? [1, 2, 4, 8]
      : [8, 16];
  if (!allowedDepths.includes(header.bitDepth)) throw new Error('PNG bit depth is invalid for its color type');
  if (header.compression !== 0 || header.filter !== 0 || header.interlace !== 0) {
    throw new Error('PNG uses unsupported compression, filter, or interlace settings');
  }
}

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
