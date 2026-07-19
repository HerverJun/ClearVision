import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const tokensSource = readFileSync(
  resolve(process.cwd(), 'src/design-system/tokens/tokens.css'),
  'utf8'
).replace(/\r\n?/g, '\n');

function readTokenBlock(start: string, end: string): ReadonlyMap<string, string> {
  const startIndex = tokensSource.indexOf(start);
  const endIndex = tokensSource.indexOf(end, startIndex + start.length);
  expect(startIndex).toBeGreaterThanOrEqual(0);
  expect(endIndex).toBeGreaterThan(startIndex);

  const tokens = new Map<string, string>();
  const block = tokensSource.slice(startIndex + start.length, endIndex);
  for (const match of block.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) {
    const [, name, value] = match;
    if (name && value) tokens.set(name, value.trim().toLowerCase());
  }
  return tokens;
}

function relativeLuminance(hex: string): number {
  const channels = [1, 3, 5].map(index => Number.parseInt(hex.slice(index, index + 2), 16) / 255);
  const linear = channels.map(channel => channel <= 0.04045
    ? channel / 12.92
    : ((channel + 0.055) / 1.055) ** 2.4);
  return 0.2126 * linear[0]! + 0.7152 * linear[1]! + 0.0722 * linear[2]!;
}

function contrastRatio(foreground: string, background: string): number {
  const foregroundLuminance = relativeLuminance(foreground);
  const backgroundLuminance = relativeLuminance(background);
  const lighter = Math.max(foregroundLuminance, backgroundLuminance);
  const darker = Math.min(foregroundLuminance, backgroundLuminance);
  return (lighter + 0.05) / (darker + 0.05);
}

describe('Design Foundation color tokens', () => {
  const light = readTokenBlock(':root {', '\n}\n\nhtml[data-theme="dark"]');
  const dark = readTokenBlock('html[data-theme="dark"] {', '\n}\n\nhtml[data-density="compact"]');
  const brandScale = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900];

  it('normalizes an equivalent CRLF token fixture before block parsing', () => {
    const normalizedCrlfSource = tokensSource.replace(/\n/g, '\r\n').replace(/\r\n?/g, '\n');
    expect(normalizedCrlfSource).toContain('\n}\n\nhtml[data-theme="dark"]');
    expect(normalizedCrlfSource).toContain('\n}\n\nhtml[data-density="compact"]');
  });

  it.each([
    ['light', light],
    ['dark', dark]
  ] as const)('keeps the %s theme on the complete cinnabar brand scale', (_theme, tokens) => {
    for (const step of brandScale) {
      expect(tokens.get(`--cv-color-brand-${step}`)).toMatch(/^#[0-9a-f]{6}$/);
    }
    expect(tokens.get('--cv-color-brand-500')).toBe('#b6453c');
    expect(tokens.get('--cv-color-brand-600')).toBe('#9f3932');
    expect(tokens.get('--cv-color-on-brand')).toBe('#ffffff');
    expect(contrastRatio(
      tokens.get('--cv-color-on-brand')!,
      tokens.get('--cv-color-brand-500')!
    )).toBeGreaterThanOrEqual(4.5);
  });

  it.each([
    ['light', light],
    ['dark', dark]
  ] as const)('separates %s brand, NG, execution error, Info and Canvas technical colors', (_theme, tokens) => {
    const brand = tokens.get('--cv-color-brand-500');
    expect(brand).not.toBe(tokens.get('--cv-color-status-ng'));
    expect(brand).not.toBe(tokens.get('--cv-color-status-error'));
    expect(brand).not.toBe(tokens.get('--cv-color-status-info'));
    expect(brand).not.toBe(tokens.get('--flow-canvas-connection'));
    expect(brand).not.toBe(tokens.get('--flow-canvas-selection-border'));
    expect(brand).not.toBe(tokens.get('--flow-canvas-guide'));
    expect(brand).not.toBe(tokens.get('--cv-color-status-warning'));
    expect(tokens.get('--cv-color-status-ng')).not.toBe(tokens.get('--cv-color-status-error'));
    expect(tokens.get('--cv-color-status-info')).not.toBe(tokens.get('--cv-focus-ring-color'));
  });

  it('freezes the light execution error color independently from NG', () => {
    expect(light.get('--cv-color-status-ng')).toBe('#d12f3f');
    expect(light.get('--cv-color-status-error')).toBe('#b85b16');
  });

  it.each([
    ['light', light],
    ['dark', dark]
  ] as const)('keeps %s readable text and control boundaries on product surfaces', (_theme, tokens) => {
    for (const foreground of ['--cv-text-primary', '--cv-text-secondary', '--cv-text-muted'] as const) {
      for (const background of ['--cv-surface-page', '--cv-surface-raised'] as const) {
        expect(contrastRatio(tokens.get(foreground)!, tokens.get(background)!)).toBeGreaterThanOrEqual(4.5);
      }
    }
    for (const background of ['--cv-surface-page', '--cv-surface-raised'] as const) {
      expect(contrastRatio(tokens.get('--cv-control-border')!, tokens.get(background)!)).toBeGreaterThanOrEqual(3);
      expect(contrastRatio(tokens.get('--cv-focus-ring-color')!, tokens.get(background)!)).toBeGreaterThanOrEqual(3);
    }
  });

  it('defines the V1.1 surface, typography and motion semantics once', () => {
    for (const token of [
      '--cv-surface-app',
      '--cv-surface-page',
      '--cv-surface-raised',
      '--cv-surface-floating',
      '--cv-type-display-size',
      '--cv-type-page-title-size',
      '--cv-type-section-title-size',
      '--cv-type-body-size',
      '--cv-type-secondary-size',
      '--cv-type-caption-size',
      '--cv-type-numeric-size'
    ]) {
      expect(light.has(token)).toBe(true);
    }
    expect(light.get('--cv-motion-duration-fast')).toBe('140ms');
    expect(light.get('--cv-motion-duration-normal')).toBe('180ms');
    expect(light.get('--cv-motion-duration-slow')).toBe('200ms');
  });
});
