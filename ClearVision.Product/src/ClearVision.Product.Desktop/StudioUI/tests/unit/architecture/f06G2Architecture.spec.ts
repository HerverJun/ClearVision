import { readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const capabilityRoot = join(studioRoot, 'src/capabilities/ai-workbench');

function sourceFiles(root: string): string[] {
  return readdirSync(root, { withFileTypes: true }).flatMap(entry => {
    const path = join(root, entry.name);
    return entry.isDirectory()
      ? sourceFiles(path)
      : ['.ts', '.vue'].includes(extname(path)) ? [path] : [];
  });
}

function read(path: string): string {
  return readFileSync(path, 'utf8');
}

describe('F06 G2 Intent Plan Clarification architecture guards', () => {
  const files = sourceFiles(capabilityRoot);
  const combined = files.map(read).join('\n');

  it('keeps one Session owner and one internal AgentRun stream adapter', () => {
    expect(files.filter(path => path.endsWith('aiSessionOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('agentRunStreamAdapter.ts'))).toHaveLength(1);
    expect(combined.match(/createAgentRunStreamAdapter\(/g)).toHaveLength(2);
    expect(combined).not.toMatch(/new\s+EventSource|defineStore\s*\(|createPinia\s*\(|localStorage/);
  });

  it('keeps Vue components behind owner actions instead of endpoint strings', () => {
    const components = files.filter(path => path.endsWith('.vue')).map(read).join('\n');
    expect(components).not.toMatch(/ai\/sessions|agent-plan-runs|agent-intent-router-runs|readiness-preview|workspace-snapshot/);
    expect(components).not.toMatch(/ApiTransport|createAiWorkbenchApi|getTextStream/);
  });

  it('does not introduce Build Handoff Canvas Project save or WebMessage behavior', () => {
    expect(combined).not.toMatch(/FlowCanvas|ImageCanvas|replaceFlow|ProjectSaveCoordinator|WebMessage|window\.chrome\.webview/);
    expect(combined).not.toMatch(/handoff|applyPreview|resourceBinding/i);
    expect(combined).not.toMatch(/['"]ai\/agent-runs['"]/);
    expect(read(join(capabilityRoot, 'actionModel.ts'))).not.toMatch(/startBuild|submitBuild|buildPlan/);
  });

  it('persists clarification through expected revision and client mutation identity', () => {
    const owner = read(join(capabilityRoot, 'aiSessionOwner.ts'));
    expect(owner).toContain('expectedRevision: session.snapshot.revision');
    expect(owner).toContain('clientMutationId: operationIdFactory()');
    expect(owner).toContain('latestSnapshotFromConflict');
    expect(owner).toContain('optimisticPlanAnswers');
    expect(owner).toContain('confirmedPlanAnswers: readiness.acceptedAnswers');
  });

  it('uses replay before SSE and never retries Plan create on an unknown outcome', () => {
    const owner = read(join(capabilityRoot, 'aiSessionOwner.ts'));
    const reducer = read(join(capabilityRoot, 'reducer.ts'));
    const stream = read(join(capabilityRoot, 'agentRunStreamAdapter.ts'));
    expect(owner).toContain("api.getOperation(planOperationId!, 'plan_run'");
    expect(stream.indexOf('options.api.getRunReplay')).toBeLessThan(stream.indexOf('openStream(expectedConnection)'));
    expect(reducer).toContain('event.sequence > state.run.lastSequence + 1');
    expect(stream).toContain('scheduleReconnect()');
  });
});
