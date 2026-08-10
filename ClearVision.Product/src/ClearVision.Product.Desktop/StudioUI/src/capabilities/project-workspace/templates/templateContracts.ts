import type {
  OperatorCatalogItem,
  OperatorParameter,
  OperatorPort
} from '@/capabilities/operators-read/operatorContracts';

export class TemplateContractDecodeError extends Error {
  readonly path: string;
  readonly expectation: string;

  constructor(path: string, expectation: string) {
    super('模板服务返回的数据格式不符合要求，请刷新后重试。');
    this.name = 'TemplateContractDecodeError';
    this.path = path;
    this.expectation = expectation;
  }
}

export type TemplateDiagnosticSeverity = 'warning' | 'error';

export interface TemplateDiagnostic {
  readonly severity: TemplateDiagnosticSeverity;
  readonly code: string;
  readonly path: string;
  readonly message: string;
}

export interface FlowTemplateV1 {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly industry: string;
  readonly tags: readonly string[];
  readonly flowJson: string;
  readonly templateVersion: string;
  readonly scenarioKey: string | null;
  readonly createdAt: string | null;
}

export interface TemplateFlowConversion {
  readonly flow: Readonly<Record<string, unknown>> | null;
  readonly diagnostics: readonly TemplateDiagnostic[];
  readonly operatorCount: number;
  readonly connectionCount: number;
}

type JsonRecord = Readonly<Record<string, unknown>>;

function isRecord(value: unknown): value is JsonRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function record(value: unknown, path: string): JsonRecord {
  if (!isRecord(value)) throw new TemplateContractDecodeError(path, '对象');
  return value;
}

function field(source: JsonRecord, name: string): unknown {
  const pascal = `${name.slice(0, 1).toUpperCase()}${name.slice(1)}`;
  return source[name] ?? source[pascal];
}

function string(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim().length === 0)) {
    throw new TemplateContractDecodeError(path, allowEmpty ? '字符串' : '非空字符串');
  }
  return value;
}

function optionalString(value: unknown, path: string): string | null {
  if (value === undefined || value === null) return null;
  return string(value, path, true);
}

function stringArray(value: unknown, path: string): readonly string[] {
  if (value === undefined || value === null) return Object.freeze([]);
  if (!Array.isArray(value)) throw new TemplateContractDecodeError(path, '数组');
  return Object.freeze(value.map((item, index) => string(item, `${path}[${index}]`, true).trim()).filter(Boolean));
}

function uuidLike(value: unknown, path: string): string {
  return string(value, path).trim();
}

function parseFlowJson(value: unknown): string {
  if (typeof value === 'string' && value.trim()) {
    try {
      JSON.parse(value);
    } catch (error) {
      throw new TemplateContractDecodeError('$.flowJson', `有效 JSON（${String(error)}）`);
    }
    return value;
  }
  if (isRecord(value) || Array.isArray(value)) {
    return JSON.stringify(value);
  }
  throw new TemplateContractDecodeError('$.flowJson', 'JSON 字符串');
}

function decodeTemplate(value: unknown, path: string): FlowTemplateV1 {
  const source = record(value, path);
  return Object.freeze({
    id: uuidLike(field(source, 'id'), `${path}.id`),
    name: string(field(source, 'name'), `${path}.name`),
    description: string(field(source, 'description') ?? '', `${path}.description`, true),
    industry: string(field(source, 'industry') ?? '', `${path}.industry`, true),
    tags: stringArray(field(source, 'tags'), `${path}.tags`),
    flowJson: parseFlowJson(field(source, 'flowJson') ?? field(source, 'flowData')),
    templateVersion: string(field(source, 'templateVersion') ?? '1.0.0', `${path}.templateVersion`, true),
    scenarioKey: optionalString(field(source, 'scenarioKey'), `${path}.scenarioKey`),
    createdAt: optionalString(field(source, 'createdAt'), `${path}.createdAt`)
  });
}

export function decodeFlowTemplate(value: unknown, path = '$'): FlowTemplateV1 {
  return decodeTemplate(value, path);
}

export function decodeFlowTemplateList(value: unknown): readonly FlowTemplateV1[] {
  if (!Array.isArray(value)) throw new TemplateContractDecodeError('$', '数组');
  const ids = new Set<string>();
  const templates = value.map((item, index) => {
    const template = decodeTemplate(item, `$[${index}]`);
    if (ids.has(template.id)) throw new TemplateContractDecodeError(`$[${index}].id`, '唯一模板标识');
    ids.add(template.id);
    return template;
  });
  return Object.freeze(templates);
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : value === null || value === undefined ? '' : String(value).trim();
}

function normalized(value: unknown): string {
  return text(value).toLocaleLowerCase();
}

function number(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function randomId(prefix: string): string {
  try {
    return globalThis.crypto.randomUUID();
  } catch {
    return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
  }
}

function readTempId(value: unknown, index: number): string {
  const source = isRecord(value) ? value : {};
  return text(field(source, 'tempId') ?? field(source, 'id')) || `template-node-${index}`;
}

function readOperatorType(value: unknown): string {
  if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  const source = isRecord(value) ? value : {};
  return text(field(source, 'operatorType') ?? field(source, 'type'));
}

function readParameterValue(parameters: unknown, name: string): { found: boolean; value: unknown } {
  if (Array.isArray(parameters)) {
    const item = parameters.find(entry => isRecord(entry) && normalized(field(entry, 'name')) === normalized(name));
    if (!item || !isRecord(item)) return { found: false, value: undefined };
    return { found: field(item, 'value') !== undefined, value: field(item, 'value') };
  }
  if (!isRecord(parameters)) return { found: false, value: undefined };
  const exact = Object.keys(parameters).find(key => key === name);
  const insensitive = exact ?? Object.keys(parameters).find(key => normalized(key) === normalized(name));
  return insensitive === undefined
    ? { found: false, value: undefined }
    : { found: true, value: parameters[insensitive] };
}

function convertValue(value: unknown, dataType: string): unknown {
  if (value === null || value === undefined) return value ?? null;
  const type = normalized(dataType);
  if (['int', 'integer', 'int32', 'int64', 'long'].includes(type)) {
    const parsed = typeof value === 'number' ? value : Number.parseInt(String(value), 10);
    return Number.isFinite(parsed) ? Math.trunc(parsed) : value;
  }
  if (['double', 'float', 'decimal', 'number'].includes(type)) {
    const parsed = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(parsed) ? parsed : value;
  }
  if (['bool', 'boolean'].includes(type)) {
    if (typeof value === 'boolean') return value;
    return normalized(value) === 'true';
  }
  return value;
}

function buildPorts(
  metadata: readonly OperatorPort[],
  requiredNames: readonly string[],
  direction: 0 | 1
): readonly JsonRecord[] {
  const ports: JsonRecord[] = [];
  const names = new Set<string>();
  for (const port of metadata) {
    const name = text(port.name);
    if (!name) continue;
    names.add(normalized(name));
    ports.push(Object.freeze({
      id: randomId('template-port'),
      name,
      displayName: text(port.displayName) || name,
      description: port.description ?? '',
      dataType: text(port.dataType) || 'Any',
      direction,
      isRequired: port.isRequired
    }));
  }
  for (const requiredName of requiredNames) {
    const name = text(requiredName);
    if (!name || names.has(normalized(name))) continue;
    names.add(normalized(name));
    ports.push(Object.freeze({
      id: randomId('template-port'),
      name,
      displayName: name,
      description: '',
      dataType: 'Any',
      direction,
      isRequired: false
    }));
  }
  if (ports.length === 0) {
    const name = direction === 0 ? 'Input' : 'Output';
    ports.push(Object.freeze({
      id: randomId('template-port'),
      name,
      displayName: name,
      description: '',
      dataType: 'Any',
      direction,
      isRequired: false
    }));
  }
  return Object.freeze(ports);
}

function buildParameters(
  metadata: readonly OperatorParameter[],
  templateParameters: unknown,
  diagnostics: TemplateDiagnostic[],
  path: string
): readonly JsonRecord[] {
  const parameters: JsonRecord[] = [];
  const known = new Set<string>();
  for (const parameter of metadata) {
    const name = text(parameter.name);
    if (!name) continue;
    known.add(normalized(name));
    const resolved = readParameterValue(templateParameters, name);
    parameters.push(Object.freeze({
      id: randomId('template-parameter'),
      name,
      displayName: text(parameter.displayName) || name,
      description: parameter.description ?? '',
      dataType: text(parameter.dataType) || 'string',
      defaultValue: parameter.defaultValue ?? null,
      minValue: parameter.minValue ?? null,
      maxValue: parameter.maxValue ?? null,
      isRequired: parameter.isRequired,
      options: parameter.options,
      ...(resolved.found ? { value: convertValue(resolved.value, parameter.dataType) } : {})
    }));
  }
  if (isRecord(templateParameters)) {
    for (const [name, value] of Object.entries(templateParameters)) {
      if (known.has(normalized(name))) continue;
      diagnostics.push(Object.freeze({
        severity: 'warning',
        code: 'template-parameter-metadata-missing',
        path: `${path}.parameters.${name}`,
        message: `模板参数 ${name} 未出现在当前算子元数据中，已按字符串保留。`
      }));
      parameters.push(Object.freeze({
        id: randomId('template-parameter'),
        name,
        displayName: name,
        description: '模板保留参数；当前元数据未提供定义。',
        dataType: 'string',
        defaultValue: '',
        minValue: null,
        maxValue: null,
        isRequired: false,
        options: null,
        value
      }));
    }
  }
  return Object.freeze(parameters);
}

function layoutFor(
  operators: readonly JsonRecord[],
  connections: readonly JsonRecord[]
): ReadonlyMap<string, Readonly<{ x: number; y: number }>> {
  const depth = new Map<string, number>();
  operators.forEach((operator, index) => depth.set(readTempId(operator, index), 0));
  for (let round = 0; round < operators.length; round += 1) {
    for (const connection of connections) {
      const source = text(field(connection, 'sourceTempId') ?? field(connection, 'sourceOperatorId'));
      const target = text(field(connection, 'targetTempId') ?? field(connection, 'targetOperatorId'));
      if (!source || !target) continue;
      depth.set(target, Math.max(depth.get(target) ?? 0, (depth.get(source) ?? 0) + 1));
    }
  }
  const lanes = new Map<number, number>();
  return new Map(operators.map((operator, index) => {
    const tempId = readTempId(operator, index);
    const level = depth.get(tempId) ?? 0;
    const lane = lanes.get(level) ?? 0;
    lanes.set(level, lane + 1);
    return [tempId, Object.freeze({ x: 120 + level * 250, y: 100 + lane * 150 })] as const;
  }));
}

function canonicalFlow(parsed: JsonRecord): boolean {
  const operators = field(parsed, 'operators');
  if (!Array.isArray(operators) || operators.length === 0 || !isRecord(operators[0])) return false;
  return Boolean(field(operators[0], 'id') && (field(operators[0], 'type') ?? field(operators[0], 'operatorType')));
}

export function convertTemplateFlow(
  template: FlowTemplateV1,
  catalog: readonly OperatorCatalogItem[]
): TemplateFlowConversion {
  const diagnostics: TemplateDiagnostic[] = [];
  let parsed: JsonRecord;
  try {
    parsed = record(JSON.parse(template.flowJson) as unknown, '$.flowJson');
  } catch (error) {
    diagnostics.push(Object.freeze({
      severity: 'error',
      code: 'template-flow-json-invalid',
      path: '$.flowJson',
      message: error instanceof Error ? error.message : '模板流程 JSON 无法解析。'
    }));
    return Object.freeze({ flow: null, diagnostics: Object.freeze(diagnostics), operatorCount: 0, connectionCount: 0 });
  }

  const rawOperators = field(parsed, 'operators');
  const rawConnections = field(parsed, 'connections');
  if (!Array.isArray(rawOperators) || rawOperators.length === 0) {
    diagnostics.push(Object.freeze({ severity: 'error', code: 'template-flow-empty', path: '$.flowJson.operators', message: '模板未包含任何算子。' }));
    return Object.freeze({ flow: null, diagnostics: Object.freeze(diagnostics), operatorCount: 0, connectionCount: 0 });
  }
  const operators = rawOperators.filter(isRecord);
  if (operators.length !== rawOperators.length) {
    diagnostics.push(Object.freeze({ severity: 'error', code: 'template-operator-invalid', path: '$.flowJson.operators', message: '模板包含无法解析的算子条目。' }));
  }
  const connections = (Array.isArray(rawConnections) ? rawConnections : []).filter(isRecord);
  const catalogByType = new Map(catalog.map(item => [normalized(item.operatorType), item]));

  if (canonicalFlow(parsed)) {
    for (const [index, operator] of operators.entries()) {
      const type = readOperatorType(operator);
      if (!catalogByType.has(normalized(type))) {
        diagnostics.push(Object.freeze({ severity: 'warning', code: 'template-operator-not-in-catalog', path: `$.flowJson.operators[${index}]`, message: `算子 ${type} 未出现在当前算子目录中，已保留原始节点。` }));
      }
    }
    return Object.freeze({
      flow: Object.freeze({ ...parsed, operators: Object.freeze(operators), connections: Object.freeze(connections) }),
      diagnostics: Object.freeze(diagnostics),
      operatorCount: operators.length,
      connectionCount: connections.length
    });
  }

  const requiredInputs = new Map<string, string[]>();
  const requiredOutputs = new Map<string, string[]>();
  for (const connection of connections) {
    const source = text(field(connection, 'sourceTempId'));
    const target = text(field(connection, 'targetTempId'));
    const sourcePort = text(field(connection, 'sourcePortName') ?? field(connection, 'sourcePortId'));
    const targetPort = text(field(connection, 'targetPortName') ?? field(connection, 'targetPortId'));
    if (source && sourcePort) requiredOutputs.set(source, [...(requiredOutputs.get(source) ?? []), sourcePort]);
    if (target && targetPort) requiredInputs.set(target, [...(requiredInputs.get(target) ?? []), targetPort]);
  }
  const layout = layoutFor(operators, connections);
  const nodeMapping = new Map<string, { id: string; inputs: readonly JsonRecord[]; outputs: readonly JsonRecord[] }>();
  const convertedOperators: JsonRecord[] = [];

  for (const [index, operator] of operators.entries()) {
    const tempId = readTempId(operator, index);
    const type = readOperatorType(operator);
    if (!type) {
      diagnostics.push(Object.freeze({ severity: 'error', code: 'template-operator-type-missing', path: `$.flowJson.operators[${index}]`, message: '模板算子缺少类型。' }));
      continue;
    }
    const metadata = catalogByType.get(normalized(type));
    if (!metadata) {
      diagnostics.push(Object.freeze({ severity: 'error', code: 'template-operator-unknown', path: `$.flowJson.operators[${index}]`, message: `当前算子目录不包含 ${type}，为避免丢失元数据，模板未应用。` }));
      continue;
    }
    const inputs = buildPorts(metadata.inputPorts, requiredInputs.get(tempId) ?? [], 0);
    const outputs = buildPorts(metadata.outputPorts, requiredOutputs.get(tempId) ?? [], 1);
    const parameters = buildParameters(metadata.parameters, field(operator, 'parameters'), diagnostics, `$.flowJson.operators[${index}]`);
    const id = randomId('template-node');
    const position = layout.get(tempId) ?? Object.freeze({ x: 120 + index * 220, y: 100 });
    const node = Object.freeze({
      id,
      name: text(field(operator, 'displayName') ?? field(operator, 'name')) || metadata.displayName || type,
      type,
      x: number(field(operator, 'x'), position.x),
      y: number(field(operator, 'y'), position.y),
      isEnabled: field(operator, 'isEnabled') !== false,
      inputPorts: inputs,
      outputPorts: outputs,
      parameters
    });
    nodeMapping.set(tempId, { id, inputs, outputs });
    convertedOperators.push(node);
  }

  const findPort = (ports: readonly JsonRecord[], expected: string): JsonRecord | null =>
    ports.find(port => normalized(field(port, 'name')) === normalized(expected)) ?? null;
  const convertedConnections: JsonRecord[] = [];
  for (const [index, connection] of connections.entries()) {
    const sourceKey = text(field(connection, 'sourceTempId'));
    const targetKey = text(field(connection, 'targetTempId'));
    const source = nodeMapping.get(sourceKey);
    const target = nodeMapping.get(targetKey);
    const sourcePortName = text(field(connection, 'sourcePortName') ?? field(connection, 'sourcePortId'));
    const targetPortName = text(field(connection, 'targetPortName') ?? field(connection, 'targetPortId'));
    const sourcePort = source ? findPort(source.outputs, sourcePortName) : null;
    const targetPort = target ? findPort(target.inputs, targetPortName) : null;
    if (!source || !target || !sourcePort || !targetPort) {
      diagnostics.push(Object.freeze({ severity: 'error', code: 'template-connection-unresolved', path: `$.flowJson.connections[${index}]`, message: `连接 ${sourceKey}.${sourcePortName} -> ${targetKey}.${targetPortName} 无法匹配当前元数据，未静默丢弃。` }));
      continue;
    }
    convertedConnections.push(Object.freeze({
      id: randomId('template-connection'),
      sourceOperatorId: source.id,
      sourcePortId: text(field(sourcePort, 'id')),
      targetOperatorId: target.id,
      targetPortId: text(field(targetPort, 'id'))
    }));
  }

  const hasError = diagnostics.some(item => item.severity === 'error');
  return Object.freeze({
    flow: hasError ? null : Object.freeze({
      id: null,
      name: `${template.name} 流程`,
      operators: Object.freeze(convertedOperators),
      connections: Object.freeze(convertedConnections),
      decisionConfiguration: field(parsed, 'decisionConfiguration') ?? null
    }),
    diagnostics: Object.freeze(diagnostics),
    operatorCount: convertedOperators.length,
    connectionCount: convertedConnections.length
  });
}

export function templateMatches(template: FlowTemplateV1, search: string, industry: string): boolean {
  const keyword = search.trim().toLocaleLowerCase();
  const matchesIndustry = !industry || template.industry === industry;
  if (!matchesIndustry) return false;
  if (!keyword) return true;
  return [template.name, template.description, template.industry, ...template.tags]
    .some(value => value.toLocaleLowerCase().includes(keyword));
}
