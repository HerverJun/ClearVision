#!/usr/bin/env node

import { readFile } from 'node:fs/promises';
import path from 'node:path';

const DEFAULT_ARTIFACT_DIR = path.join('docs', '进行中', 'Studio2', 'release', 'artifacts');
const JSON_NAME = 'G16-benchmark-2026-07-04.json';
const MARKDOWN_NAME = 'G16-benchmark-2026-07-04.md';

function fail(message) {
  throw new Error(`G16 benchmark artifact schema validation failed: ${message}`);
}

function assertScreen(screen, index) {
  if (!screen || typeof screen !== 'object' || Array.isArray(screen)) {
    fail(`environment.Screens[${index}] must be an object.`);
  }

  if (typeof screen.DeviceName !== 'string' || screen.DeviceName.trim().length === 0) {
    fail(`environment.Screens[${index}].DeviceName is required.`);
  }

  if (!Number.isFinite(screen.Width) || screen.Width <= 0) {
    fail(`environment.Screens[${index}].Width must be a positive number.`);
  }

  if (!Number.isFinite(screen.Height) || screen.Height <= 0) {
    fail(`environment.Screens[${index}].Height must be a positive number.`);
  }

  if (typeof screen.Primary !== 'boolean') {
    fail(`environment.Screens[${index}].Primary must be boolean.`);
  }
}

function readMarkdownValue(markdown, label) {
  const match = markdown.match(new RegExp(`^- ${label}: (.+)$`, 'm'));
  return match?.[1] ?? null;
}

function validateArtifacts(report, markdown) {
  if (!report || typeof report !== 'object') {
    fail('JSON root must be an object.');
  }

  const displayStatus = report.display?.status || report.display?.Status;
  if (!report.startedAtUtc || !report.environment?.node || !report.environment?.platform) {
    fail('JSON artifact is missing startedAtUtc or environment metadata.');
  }

  if (!Array.isArray(report.environment.Screens)) {
    fail('environment.Screens must be an array.');
  }

  if (displayStatus === 'PERFORMED' && report.environment.Screens.length === 0) {
    fail('performed display probe must include at least one environment.Screens entry.');
  }

  report.environment.Screens.forEach(assertScreen);

  if (!Array.isArray(report.display?.Screens)) {
    fail('display.Screens must be an array mirror for existing display consumers.');
  }

  if (JSON.stringify(report.display.Screens) !== JSON.stringify(report.environment.Screens)) {
    fail('display.Screens must match environment.Screens exactly.');
  }

  const markdownScreens = readMarkdownValue(markdown, 'Screens');
  if (markdownScreens !== JSON.stringify(report.environment.Screens)) {
    fail('Markdown Screens line must render the same JSON as environment.Screens.');
  }

  const schemaNote = readMarkdownValue(markdown, 'Screens schema');
  if (!schemaNote || !schemaNote.includes('array') || !schemaNote.includes('historical multi-screen consumers')) {
    fail('Markdown must explain why Screens remains an array schema.');
  }

  if (report.dpiResolutionMatrix?.status !== 'NOT_PERFORMED') {
    fail('dpiResolutionMatrix.status must remain NOT_PERFORMED when the matrix is not executed.');
  }

  const markdownMatrixStatus = readMarkdownValue(markdown, 'Automated matrix status');
  if (markdownMatrixStatus !== report.dpiResolutionMatrix.status) {
    fail('Markdown DPI matrix status must match JSON.');
  }
}

async function main() {
  const artifactDir = process.argv[2] ? path.resolve(process.argv[2]) : path.resolve(DEFAULT_ARTIFACT_DIR);
  const jsonPath = path.join(artifactDir, JSON_NAME);
  const markdownPath = path.join(artifactDir, MARKDOWN_NAME);
  const [jsonText, markdown] = await Promise.all([
    readFile(jsonPath, 'utf8'),
    readFile(markdownPath, 'utf8')
  ]);
  validateArtifacts(JSON.parse(jsonText), markdown);
  console.log(`G16 benchmark artifact schema validation passed: ${jsonPath}`);
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
