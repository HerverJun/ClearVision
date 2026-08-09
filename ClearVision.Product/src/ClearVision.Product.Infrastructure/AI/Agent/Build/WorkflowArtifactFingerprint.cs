using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed record ArtifactFingerprintObservation(
    string ComputedFingerprint,
    string ExpectedFingerprint)
{
    public bool IsConsistent =>
        string.IsNullOrWhiteSpace(ExpectedFingerprint) ||
        string.Equals(ComputedFingerprint, ExpectedFingerprint, StringComparison.OrdinalIgnoreCase);
}

internal static class WorkflowArtifactFingerprint
{
    public static string Compute(
        string? planHash,
        string? catalogVersion,
        string? buildIntent,
        CanonicalWorkflowGraph graph)
    {
        var payload = new
        {
            planHash = Clean(planHash),
            catalogVersion = Clean(catalogVersion),
            buildIntent = Clean(buildIntent),
            entryOperatorTempId = Clean(graph.EntryOperatorTempId),
            nodes = graph.Nodes
                .OrderBy(node => node.TempId, StringComparer.OrdinalIgnoreCase)
                .Select(node => new
            {
                tempId = Clean(node.TempId),
                operatorType = Clean(node.OperatorType),
                parameters = node.Parameters
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new { name = Clean(item.Key), value = NormalizeValue(item.Value) })
                    .ToList(),
                inputPorts = node.InputPorts
                    .OrderBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(port => new
                    {
                        name = Clean(port.Name),
                        dataType = Clean(port.DataType),
                        required = port.Required
                    })
                    .ToList(),
                outputPorts = node.OutputPorts
                    .OrderBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(port => new
                    {
                        name = Clean(port.Name),
                        dataType = Clean(port.DataType),
                        required = port.Required
                    })
                    .ToList()
            }).ToList(),
            connections = graph.Connections
                .OrderBy(connection => connection.SourceTempId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.SourcePortName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.TargetTempId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.TargetPortName, StringComparer.OrdinalIgnoreCase)
                .Select(connection => new
                {
                    sourceTempId = Clean(connection.SourceTempId),
                    sourcePortName = Clean(connection.SourcePortName),
                    targetTempId = Clean(connection.TargetTempId),
                    targetPortName = Clean(connection.TargetPortName)
                })
                .ToList()
        };

        var json = JsonSerializer.Serialize(payload, VisionAgentBuildSupport.JsonOptions);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static ArtifactFingerprintObservation Observe(
        JsonElement arguments,
        VisionAgentFlowDraft flow,
        IVisionAgentOperatorContractCatalog contractCatalog)
    {
        var planHash = ReadString(arguments, "planHash");
        var catalogVersion = ReadString(arguments, "catalogVersion");
        var buildIntent = ReadString(arguments, "buildIntent");
        var graph = ToGraph(flow, contractCatalog);
        return new ArtifactFingerprintObservation(
            Compute(planHash, catalogVersion, buildIntent, graph),
            ReadString(arguments, "artifactFingerprint") ?? string.Empty);
    }

    public static string ComputeCanvasProjection(
        OperatorFlowDto flow,
        string planHash,
        string catalogVersion,
        string buildIntent,
        CanonicalWorkflowGraph expectedGraph,
        IVisionAgentOperatorContractCatalog contractCatalog)
    {
        var expectedTempIds = expectedGraph.Nodes
            .Select(node => node.TempId)
            .Where(tempId => !string.IsNullOrWhiteSpace(tempId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byTempId = flow.Operators
            .Select(op => (Operator: op, TempId: ReadAgentTempId(op, expectedTempIds)))
            .Where(item => !string.IsNullOrWhiteSpace(item.TempId))
            .GroupBy(item => item.TempId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (byTempId.Any(group => group.Count() > 1))
        {
            return string.Empty;
        }

        var operatorsByTempId = byTempId.ToDictionary(
            group => group.Key,
            group => group.First().Operator,
            StringComparer.OrdinalIgnoreCase);
        var nodes = flow.Operators
            .Select(op => ReadAgentTempId(op, expectedTempIds))
            .Where(tempId => !string.IsNullOrWhiteSpace(tempId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(tempId =>
            {
                var operatorDto = operatorsByTempId[tempId];
                var actualType = contractCatalog.CanonicalizeOperatorType(operatorDto.Type.ToString());
                var parameters = operatorDto.Parameters
                    .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => NormalizeValue(group.First().Value ?? group.First().DefaultValue),
                        StringComparer.OrdinalIgnoreCase);
                var inputPorts = operatorDto.InputPorts.Select(port => new VisionAgentPortFingerprint
                {
                    Name = port.Name,
                    DataType = port.DataType.ToString(),
                    Required = port.IsRequired
                }).ToList();
                var outputPorts = operatorDto.OutputPorts.Select(port => new VisionAgentPortFingerprint
                {
                    Name = port.Name,
                    DataType = port.DataType.ToString(),
                    Required = false
                }).ToList();
                var expected = expectedGraph.Nodes.FirstOrDefault(node =>
                    node.TempId.Equals(tempId, StringComparison.OrdinalIgnoreCase));
                return new CanonicalWorkflowNode(
                    tempId,
                    actualType,
                    expected?.DisplayName ?? operatorDto.Name,
                    parameters.ToDictionary(item => item.Key, item => (string?)item.Value, StringComparer.OrdinalIgnoreCase),
                    inputPorts,
                    outputPorts);
            })
            .ToList();

        var operatorIds = operatorsByTempId.ToDictionary(
            item => item.Key,
            item => item.Value.Id,
            StringComparer.OrdinalIgnoreCase);
        var portNames = flow.Operators
            .Where(op => operatorIds.ContainsValue(op.Id))
            .SelectMany(op => op.InputPorts.Concat(op.OutputPorts)
                .Select(port => (operatorId: op.Id, portId: port.Id, portName: port.Name)))
            .GroupBy(item => item.portId)
            .ToDictionary(group => group.Key, group => group.First().portName);
        var connections = flow.Connections
            .Where(connection =>
                operatorIds.ContainsValue(connection.SourceOperatorId) &&
                operatorIds.ContainsValue(connection.TargetOperatorId) &&
                portNames.ContainsKey(connection.SourcePortId) &&
                portNames.ContainsKey(connection.TargetPortId))
            .Select(connection => new CanonicalWorkflowConnection(
                operatorIds.First(pair => pair.Value == connection.SourceOperatorId).Key,
                portNames[connection.SourcePortId],
                operatorIds.First(pair => pair.Value == connection.TargetOperatorId).Key,
                portNames[connection.TargetPortId]))
            .ToList();
        var graph = new CanonicalWorkflowGraph(
            nodes,
            connections,
            expectedGraph.EntryOperatorTempId);
        return Compute(planHash, catalogVersion, buildIntent, graph);
    }

    internal static CanonicalWorkflowGraph ToGraph(
        VisionAgentFlowDraft flow,
        IVisionAgentOperatorContractCatalog contractCatalog)
    {
        var nodes = flow.Operators.Select(op =>
        {
            var canonicalType = contractCatalog.CanonicalizeOperatorType(op.OperatorType);
            if (!contractCatalog.TryGet(canonicalType, out var contract))
            {
                return new CanonicalWorkflowNode(
                    op.TempId,
                    canonicalType,
                    canonicalType,
                    op.Parameters,
                    [],
                    []);
            }

            var parameters = contract.Parameters.ToDictionary(
                parameter => parameter.Name,
                parameter => op.Parameters.TryGetValue(parameter.Name, out var value) &&
                             !string.IsNullOrWhiteSpace(value)
                    ? value
                    : NormalizeValue(parameter.DefaultValue),
                StringComparer.OrdinalIgnoreCase);
            return new CanonicalWorkflowNode(
                op.TempId,
                canonicalType,
                contract.OperatorType,
                parameters.ToDictionary(item => item.Key, item => (string?)item.Value, StringComparer.OrdinalIgnoreCase),
                contract.InputPorts.Select(port => new VisionAgentPortFingerprint
                {
                    Name = port.Name,
                    DataType = port.DataType.ToString(),
                    Required = port.IsRequired
                }).ToList(),
                contract.OutputPorts.Select(port => new VisionAgentPortFingerprint
                {
                    Name = port.Name,
                    DataType = port.DataType.ToString(),
                    Required = false
                }).ToList());
        }).ToList();
        var connections = flow.Connections.Select(connection => new CanonicalWorkflowConnection(
            connection.SourceTempId,
            connection.SourcePortName,
            connection.TargetTempId,
            connection.TargetPortName)).ToList();
        return new CanonicalWorkflowGraph(
            nodes,
            connections,
            string.IsNullOrWhiteSpace(flow.EntryOperatorTempId)
                ? nodes.FirstOrDefault()?.TempId ?? string.Empty
                : flow.EntryOperatorTempId!);
    }

    private static string ReadAgentTempId(
        OperatorDto op,
        IReadOnlySet<string>? expectedTempIds = null)
    {
        if (op.Metadata is { } metadata && metadata.TryGetValue("agentTempId", out var value))
        {
            return value switch
            {
                JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? string.Empty,
                _ => value?.ToString() ?? string.Empty
            };
        }

        var legacyName = op.Name?.Trim() ?? string.Empty;
        return expectedTempIds?.Contains(legacyName) == true ? legacyName : string.Empty;
    }

    private static string NormalizeValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            JsonElement json => json.ValueKind == JsonValueKind.String ? json.GetString() ?? string.Empty : json.GetRawText(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return VisionAgentBuildSupport.TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
