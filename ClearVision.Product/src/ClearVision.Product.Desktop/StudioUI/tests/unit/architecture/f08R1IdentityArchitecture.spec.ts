import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const root = join(import.meta.dirname, '../../..');
const read = (relative: string) => readFileSync(join(root, relative), 'utf8');

describe('F08-R1 result identity guards', () => {
  it('keeps the Workspace quarantine result identity named as ResultId', () => {
    const owner = read('src/capabilities/project-workspace/workspaceOwner.ts');

    expect(owner).toContain('readonly resultId: string | null;');
    expect(owner).toContain('resultId: runOwner?.projection.result?.id ?? null');
    expect(owner).not.toContain('runId: runOwner?.projection.result?.id');
  });

  it('keeps legacy local result SessionId and RunId projections independent', () => {
    const app = read('../wwwroot/src/app.js');
    const resultPanel = read('../wwwroot/src/features/results/resultPanel.js');

    expect(app).not.toMatch(/normalized\.sessionId\s*=[^;]*\?\? normalized\.(?:runId|RunId)/);
    expect(app).not.toContain('normalized.runId = normalized.runId ?? normalized.RunId ?? normalized.sessionId;');
    expect(app).toContain('?? normalized.traceability?.runId');
    expect(resultPanel).toContain("['SessionId', result.sessionId || legacy]");
    expect(resultPanel).toContain("['RunId', result.runId || 'Run ID 未记录，旧结果身份不完整']");
    expect(resultPanel).not.toContain('SessionId / RunId');
  });
});
