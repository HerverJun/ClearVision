import {
  createCanonicalFlowCanvasHost,
  type CanonicalCanvasRuntimeSnapshot,
  type CanonicalFlowCanvasHost
} from '@/platform/canvas';
import {
  CANVAS_FIXTURE_IDS,
  createFlowIdentityFingerprint,
  decodeOperatorFlowDto,
  getCanvasFixture,
  type CanvasFixtureId,
  type OperatorFlowDto
} from '@/labs/canvas/operatorFlowFixtures';
import { reportCanvasOwnerCountForDiagnostics } from '@/platform/diagnostics/studioUiLifecycleDiagnostics';

export interface CanvasValidationCase {
  readonly id: 'duplicate' | 'occupied' | 'self' | 'incompatible' | 'cycle';
  readonly expected: string;
  readonly actual: string | null;
  readonly passed: boolean;
}

type CanvasValidationExpectation = Omit<CanvasValidationCase, 'passed'>;

export interface CanvasIdentityResult {
  readonly state: 'not-run' | 'pass' | 'fail';
  readonly beforeFingerprint: string | null;
  readonly afterFingerprint: string | null;
}

export interface CanvasLabDiagnostics {
  readonly status: 'idle' | 'mounted' | 'disposed' | 'error';
  readonly ownerCount: 0 | 1;
  readonly generation: number;
  readonly totalMounts: number;
  readonly totalDisposals: number;
  readonly fixtureId: CanvasFixtureId | null;
  readonly fixtureName: string | null;
  readonly lastError: string | null;
  readonly identity: CanvasIdentityResult;
  readonly validation: readonly CanvasValidationCase[];
  readonly runtime: CanonicalCanvasRuntimeSnapshot | null;
}

export interface MountCanvasLabOptions {
  readonly canvasId: string;
  readonly initialFixtureId?: CanvasFixtureId;
  readonly onDiagnostics?: (diagnostics: CanvasLabDiagnostics) => void;
}

export interface CanvasLabController {
  readonly generation: number;
  loadFixture(fixtureId: CanvasFixtureId): void;
  runIdentityRoundTrip(): CanvasIdentityResult;
  resize(): void;
  getDiagnostics(): CanvasLabDiagnostics;
  dispose(): void;
}

export class CanvasLabOwnerConflictError extends Error {
  constructor() {
    super('Canvas Lab already has an active mounted owner.');
    this.name = 'CanvasLabOwnerConflictError';
  }
}

interface ActiveCanvasOwner {
  readonly token: symbol;
  readonly generation: number;
  readonly host: CanonicalFlowCanvasHost;
  readonly onDiagnostics?: (diagnostics: CanvasLabDiagnostics) => void;
  fixtureId: CanvasFixtureId;
  flowId: string;
  flowName: string;
  identity: CanvasIdentityResult;
  validation: readonly CanvasValidationCase[];
  unsubscribe: () => void;
  disposed: boolean;
}

declare global {
  interface Window {
    readonly __STUDIO_UI_CANVAS_DIAGNOSTICS__?: CanvasLabDiagnostics;
  }
}

const notRunIdentity: CanvasIdentityResult = Object.freeze({
  state: 'not-run',
  beforeFingerprint: null,
  afterFingerprint: null
});

let activeOwner: ActiveCanvasOwner | undefined;
let generation = 0;
let totalMounts = 0;
let totalDisposals = 0;
let lastDiagnostics: CanvasLabDiagnostics = Object.freeze({
  status: 'idle',
  ownerCount: 0,
  generation: 0,
  totalMounts: 0,
  totalDisposals: 0,
  fixtureId: null,
  fixtureName: null,
  lastError: null,
  identity: notRunIdentity,
  validation: Object.freeze([]),
  runtime: null
});

function freezeDiagnostics(value: CanvasLabDiagnostics): CanvasLabDiagnostics {
  Object.freeze(value.validation);
  return Object.freeze(value);
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unknown Canvas Lab failure.';
}

function serializeWithEnvelope(owner: ActiveCanvasOwner): OperatorFlowDto {
  const serialized = owner.host.serialize();
  if (typeof serialized !== 'object' || serialized === null || Array.isArray(serialized)) {
    throw new Error('Canonical FlowCanvas serialize() did not return an object.');
  }
  const record = serialized as Readonly<Record<string, unknown>>;
  return decodeOperatorFlowDto({
    id: owner.flowId,
    name: owner.flowName,
    operators: record.operators ?? record.Operators,
    connections: record.connections ?? record.Connections,
    decisionConfiguration: record.decisionConfiguration ?? record.DecisionConfiguration ?? null
  });
}

function buildValidationMatrix(host: CanonicalFlowCanvasHost): readonly CanvasValidationCase[] {
  const ids = CANVAS_FIXTURE_IDS;
  const cases: readonly CanvasValidationExpectation[] = [
    {
      id: 'duplicate',
      expected: 'duplicate-connection',
      actual: host.validateConnection(
        ids.acquisition.operator,
        0,
        ids.threshold.operator,
        0
      )
    },
    {
      id: 'occupied',
      expected: 'input-port-occupied',
      actual: host.validateConnection(
        ids.acquisition.operator,
        0,
        ids.blob.operator,
        0
      )
    },
    {
      id: 'self',
      expected: 'self-connection',
      actual: host.validateConnection(
        ids.acquisition.operator,
        0,
        ids.acquisition.operator,
        0
      )
    },
    {
      id: 'incompatible',
      expected: 'incompatible-port-type',
      actual: host.validateConnection(
        ids.threshold.operator,
        0,
        ids.regionErosion.operator,
        0
      )
    },
    {
      id: 'cycle',
      expected: 'cycle',
      actual: host.validateConnection(
        ids.blob.operator,
        0,
        ids.acquisition.operator,
        0
      )
    }
  ];

  return Object.freeze(cases.map(item => Object.freeze({
    ...item,
    passed: item.actual === item.expected
  })));
}

function diagnosticsFor(owner: ActiveCanvasOwner): CanvasLabDiagnostics {
  return freezeDiagnostics({
    status: 'mounted',
    ownerCount: 1,
    generation: owner.generation,
    totalMounts,
    totalDisposals,
    fixtureId: owner.fixtureId,
    fixtureName: owner.flowName,
    lastError: null,
    identity: owner.identity,
    validation: owner.validation,
    runtime: owner.host.getRuntimeSnapshot()
  });
}

function emitDiagnostics(owner: ActiveCanvasOwner): CanvasLabDiagnostics {
  const diagnostics = diagnosticsFor(owner);
  lastDiagnostics = diagnostics;
  owner.onDiagnostics?.(diagnostics);
  return diagnostics;
}

function assertCurrentOwner(owner: ActiveCanvasOwner): void {
  if (owner.disposed || activeOwner?.token !== owner.token) {
    throw new Error('Canvas Lab owner is no longer active.');
  }
}

export function getCanvasLabDiagnostics(): CanvasLabDiagnostics {
  return activeOwner ? diagnosticsFor(activeOwner) : lastDiagnostics;
}

function installBrowserDiagnosticsProjection(): void {
  if (typeof window === 'undefined') {
    return;
  }
  Object.defineProperty(window, '__STUDIO_UI_CANVAS_DIAGNOSTICS__', {
    configurable: true,
    enumerable: false,
    get: getCanvasLabDiagnostics
  });
}

installBrowserDiagnosticsProjection();

export function mountCanvasLab(options: MountCanvasLabOptions): CanvasLabController {
  if (activeOwner) {
    throw new CanvasLabOwnerConflictError();
  }

  const fixtureId = options.initialFixtureId ?? 'canonical';
  const fixture = getCanvasFixture(fixtureId);
  let host: CanonicalFlowCanvasHost | undefined;

  try {
    host = createCanonicalFlowCanvasHost(options.canvasId, fixture);
    const owner: ActiveCanvasOwner = {
      token: Symbol(`canvas-lab-owner-${generation + 1}`),
      generation: generation + 1,
      host,
      ...(options.onDiagnostics ? { onDiagnostics: options.onDiagnostics } : {}),
      fixtureId,
      flowId: fixture.id,
      flowName: fixture.name,
      identity: notRunIdentity,
      validation: Object.freeze([]),
      unsubscribe: () => {},
      disposed: false
    };

    generation = owner.generation;
    totalMounts += 1;
    activeOwner = owner;
    reportCanvasOwnerCountForDiagnostics(1);
    owner.validation = buildValidationMatrix(host);
    owner.unsubscribe = host.subscribe(() => {
      if (!owner.disposed && activeOwner?.token === owner.token) {
        emitDiagnostics(owner);
      }
    });
    emitDiagnostics(owner);

    return Object.freeze({
      generation: owner.generation,
      loadFixture(nextFixtureId: CanvasFixtureId): void {
        assertCurrentOwner(owner);
        const nextFixture = getCanvasFixture(nextFixtureId);
        owner.host.replaceFlow(nextFixture);
        owner.fixtureId = nextFixtureId;
        owner.flowId = nextFixture.id;
        owner.flowName = nextFixture.name;
        owner.identity = notRunIdentity;
        emitDiagnostics(owner);
      },
      runIdentityRoundTrip(): CanvasIdentityResult {
        assertCurrentOwner(owner);
        const before = serializeWithEnvelope(owner);
        const beforeFingerprint = createFlowIdentityFingerprint(before);
        owner.host.replaceFlow(before);
        const after = serializeWithEnvelope(owner);
        const afterFingerprint = createFlowIdentityFingerprint(after);
        owner.identity = Object.freeze({
          state: beforeFingerprint === afterFingerprint ? 'pass' : 'fail',
          beforeFingerprint,
          afterFingerprint
        });
        emitDiagnostics(owner);
        return owner.identity;
      },
      resize(): void {
        assertCurrentOwner(owner);
        owner.host.resize();
        emitDiagnostics(owner);
      },
      getDiagnostics(): CanvasLabDiagnostics {
        assertCurrentOwner(owner);
        return diagnosticsFor(owner);
      },
      dispose(): void {
        if (owner.disposed || activeOwner?.token !== owner.token) {
          return;
        }

        owner.disposed = true;
        let disposalError: unknown;
        try {
          owner.unsubscribe();
        } catch (error) {
          disposalError = error;
        }

        try {
          owner.host.disposeInteraction();
        } catch (error) {
          disposalError ??= error;
        }

        try {
          owner.host.disposeAdapter();
        } catch (error) {
          disposalError ??= error;
        }

        activeOwner = undefined;
        reportCanvasOwnerCountForDiagnostics(0);
        totalDisposals += 1;
        lastDiagnostics = freezeDiagnostics({
          status: disposalError === undefined ? 'disposed' : 'error',
          ownerCount: 0,
          generation: owner.generation,
          totalMounts,
          totalDisposals,
          fixtureId: owner.fixtureId,
          fixtureName: owner.flowName,
          lastError: disposalError === undefined ? null : errorMessage(disposalError),
          identity: owner.identity,
          validation: owner.validation,
          runtime: owner.host.getRuntimeSnapshot()
        });
        owner.onDiagnostics?.(lastDiagnostics);

        if (disposalError !== undefined) {
          throw disposalError;
        }
      }
    });
  } catch (error) {
    try {
      host?.disposeInteraction();
    } finally {
      host?.disposeAdapter();
    }
    if (activeOwner?.host === host) {
      activeOwner = undefined;
      reportCanvasOwnerCountForDiagnostics(0);
      totalDisposals += 1;
    }
    lastDiagnostics = freezeDiagnostics({
      status: 'error',
      ownerCount: 0,
      generation,
      totalMounts,
      totalDisposals,
      fixtureId,
      fixtureName: fixture.name,
      lastError: errorMessage(error),
      identity: notRunIdentity,
      validation: Object.freeze([]),
      runtime: host?.getRuntimeSnapshot() ?? null
    });
    options.onDiagnostics?.(lastDiagnostics);
    throw error;
  }
}
