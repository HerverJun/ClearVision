using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.RuntimeAssets;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Runtime;

public sealed class RuntimePackageLoader
{
    private readonly RuntimePackageValidator _validator;
    private readonly ILogger<RuntimePackageLoader> _logger;

    public RuntimePackageLoader(
        RuntimePackageValidator validator,
        ILogger<RuntimePackageLoader> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    public async Task<RuntimePackage> LoadAsync(string packageRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            throw new RuntimePackageException("运行包路径不能为空。");
        }

        var normalizedRoot = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new RuntimePackageException($"运行包目录不存在：{normalizedRoot}");
        }

        try
        {
            var packageFile = Path.Combine(normalizedRoot, "package.json");
            var flowDefaultFile = Path.Combine(normalizedRoot, "flow.json");
            var profileFile = Path.Combine(normalizedRoot, "runtime-profile.json");
            var validationFile = Path.Combine(normalizedRoot, "quality", "validation-report.json");

            var manifest = await ReadJsonAsync<RuntimePackageManifest>(packageFile, cancellationToken)
                ?? throw new RuntimePackageException("无法解析 package.json。");

            var flowFile = RuntimePathGuard.ResolveChildPath(normalizedRoot, manifest.EntryFlow);
            var flowBytes = await File.ReadAllBytesAsync(flowFile, cancellationToken);
            var flow = JsonSerializer.Deserialize<OperatorFlowDto>(flowBytes, RuntimeJson.SerializerOptions)
                ?? throw new RuntimePackageException("无法解析 flow.json。");
            var runtimeProfile = await ReadJsonAsync<RuntimeProfile>(profileFile, cancellationToken)
                ?? new RuntimeProfile();
            var validationReport = await ReadJsonAsync<RuntimeValidationReport>(validationFile, cancellationToken)
                ?? new RuntimeValidationReport();
            var parameterSchema = await ReadJsonAsync<RuntimeParameterSchema>(
                    ResolveOptionalFieldPath(normalizedRoot, manifest.FieldExtensions.RuntimeParameters, "field/runtime-parameters.json"),
                    cancellationToken)
                ?? CreateEmptyParameterSchema(manifest);
            var defaultSiteProfile = await ReadJsonAsync<RuntimeSiteProfile>(
                    ResolveOptionalFieldPath(normalizedRoot, manifest.FieldExtensions.DefaultSiteProfile, "field/station-profile.default.json"),
                    cancellationToken)
                ?? CreateEmptyDefaultSiteProfile(manifest);
            var globalVariables = await ReadJsonAsync<ProjectGlobalVariableSchema>(
                    ResolveOptionalFieldPath(normalizedRoot, manifest.FieldExtensions.GlobalVariables, "field/global-variables.json"),
                    cancellationToken)
                ?? new ProjectGlobalVariableSchema();
            ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(globalVariables, flow.ToEntity());
            parameterSchema = NormalizeParameterSchema(parameterSchema, manifest);
            defaultSiteProfile = NormalizeDefaultSiteProfile(defaultSiteProfile, manifest);
            RuntimeProjectVariableConflictValidator.ThrowIfAnySiteProfileConflicts(globalVariables, parameterSchema, flow);

            var package = new RuntimePackage
            {
                RootPath = normalizedRoot,
                Manifest = manifest,
                Flow = flow,
                FlowBytes = flowBytes,
                RuntimeProfile = runtimeProfile,
                ValidationReport = validationReport,
                ParameterSchema = parameterSchema,
                DefaultSiteProfile = defaultSiteProfile,
                GlobalVariables = globalVariables
            };

            var validation = await _validator.ValidateAsync(package, cancellationToken);
            if (!validation.IsValid)
            {
                throw new RuntimePackageException(validation.ToUserMessage())
                {
                    ValidationResult = validation
                };
            }

            package.AssetContext = await LoadAssetContextAsync(manifest, normalizedRoot, cancellationToken);
            package.PackageFlow = CloneFlow(flow);
            RebasePackageRelativeFileParameters(package.Flow, normalizedRoot);
            package.ExecutionSnapshot = new ExecutionSnapshot(
                ResolveSnapshotProjectId(manifest),
                package.Flow.ToEntity(),
                manifest.SourceProjectRevision,
                ExecutionSnapshotSource.RuntimePackage,
                ExecutionRunMode.StationRuntime,
                new Dictionary<string, string> { ["PackageRoot"] = normalizedRoot },
                runtimePackageId: manifest.PackageId,
                globalVariables: globalVariables,
                executionIdentityFlow: package.PackageFlow.ToEntity());

            _logger.LogInformation(
                "Loaded runtime package {PackageId} from {PackageRoot}",
                package.Manifest.PackageId,
                normalizedRoot);

            return package;
        }
        catch (RuntimePackageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimePackageException("加载运行包失败。", ex);
        }
    }

    private static Guid ResolveSnapshotProjectId(RuntimePackageManifest manifest)
    {
        if (manifest.SourceProjectId != Guid.Empty)
        {
            return manifest.SourceProjectId;
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(manifest.PackageId ?? string.Empty));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, RuntimeJson.SerializerOptions, cancellationToken);
    }

    private static OperatorFlowDto CloneFlow(OperatorFlowDto flow)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(flow, RuntimeJson.SerializerOptions);
        return JsonSerializer.Deserialize<OperatorFlowDto>(bytes, RuntimeJson.SerializerOptions)
            ?? throw new RuntimePackageException("Unable to clone package semantic flow.");
    }

    private static async Task<IRuntimeAssetContext> LoadAssetContextAsync(
        RuntimePackageManifest manifest,
        string packageRoot,
        CancellationToken cancellationToken)
    {
        var calibrationAssets = manifest.Assets?.CalibrationAssets;
        if (calibrationAssets == null || calibrationAssets.Count == 0)
        {
            return RuntimeAssetContext.Empty;
        }

        var bundles = new List<RuntimeCalibrationBundleAsset>(calibrationAssets.Count);
        foreach (var manifestAsset in calibrationAssets)
        {
            var assetPath = RuntimePathGuard.ResolveAssetPath(packageRoot, manifestAsset.RelativePath);
            var bytes = await File.ReadAllBytesAsync(assetPath, cancellationToken);
            var projectAsset = JsonSerializer.Deserialize<ProjectCalibrationAssetDto>(bytes, ProjectAssetJson.Options)
                ?? throw new RuntimePackageException($"RuntimeAssetLoadFailed: calibration asset JSON could not be parsed ({manifestAsset.RelativePath}).");

            var bundleId = ReadPayloadString(projectAsset.Payload, "bundleId");
            bundles.Add(new RuntimeCalibrationBundleAsset(
                manifestAsset.AssetId,
                bundleId ?? string.Empty,
                manifestAsset.Kind,
                manifestAsset.Version,
                manifestAsset.ProjectRevision,
                manifestAsset.ContentHash,
                manifestAsset.FileHash,
                manifestAsset.RelativePath,
                projectAsset.Payload.GetRawText()));
        }

        return new RuntimeAssetContext(bundles);
    }

    private static string? ReadPayloadString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ResolveOptionalFieldPath(string packageRoot, string? manifestPath, string fallbackPath)
    {
        var relativePath = string.IsNullOrWhiteSpace(manifestPath) ? fallbackPath : manifestPath;
        return RuntimePathGuard.ResolveChildPath(packageRoot, relativePath);
    }

    private static RuntimeParameterSchema CreateEmptyParameterSchema(RuntimePackageManifest manifest)
    {
        return new RuntimeParameterSchema
        {
            PackageId = manifest.PackageId,
            FlowHash = manifest.FlowHash,
            Parameters = []
        };
    }

    private static RuntimeSiteProfile CreateEmptyDefaultSiteProfile(RuntimePackageManifest manifest)
    {
        return new RuntimeSiteProfile
        {
            ProfileId = "package-default",
            PackageId = manifest.PackageId,
            FlowHash = manifest.FlowHash,
            Revision = 0,
            UpdatedAtUtc = manifest.CreatedAt,
            UpdatedBy = string.IsNullOrWhiteSpace(manifest.CreatedBy) ? "ClearVision Studio" : manifest.CreatedBy,
            Overrides = []
        };
    }

    private static RuntimeParameterSchema NormalizeParameterSchema(
        RuntimeParameterSchema? schema,
        RuntimePackageManifest manifest)
    {
        schema ??= CreateEmptyParameterSchema(manifest);
        if (string.IsNullOrWhiteSpace(schema.PackageId))
        {
            schema.PackageId = manifest.PackageId;
        }

        if (string.IsNullOrWhiteSpace(schema.FlowHash))
        {
            schema.FlowHash = manifest.FlowHash;
        }

        schema.Parameters ??= [];
        return schema;
    }

    private static RuntimeSiteProfile NormalizeDefaultSiteProfile(
        RuntimeSiteProfile? profile,
        RuntimePackageManifest manifest)
    {
        profile ??= CreateEmptyDefaultSiteProfile(manifest);
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            profile.ProfileId = "package-default";
        }

        if (string.IsNullOrWhiteSpace(profile.PackageId))
        {
            profile.PackageId = manifest.PackageId;
        }

        if (string.IsNullOrWhiteSpace(profile.FlowHash))
        {
            profile.FlowHash = manifest.FlowHash;
        }

        if (string.IsNullOrWhiteSpace(profile.UpdatedBy))
        {
            profile.UpdatedBy = "ClearVision Studio";
        }

        profile.Overrides ??= [];
        return profile;
    }

    private static void RebasePackageRelativeFileParameters(OperatorFlowDto flow, string packageRoot)
    {
        foreach (var op in flow.Operators)
        {
            foreach (var parameter in op.Parameters)
            {
                if (!LooksLikeFileParameter(parameter))
                {
                    continue;
                }

                parameter.Value = RebasePackageRelativeValue(parameter.Value, packageRoot);
                parameter.DefaultValue = RebasePackageRelativeValue(parameter.DefaultValue, packageRoot);
            }
        }
    }

    private static object? RebasePackageRelativeValue(object? value, string packageRoot)
    {
        var text = NormalizeScalar(value);
        if (string.IsNullOrWhiteSpace(text) || Path.IsPathFullyQualified(text))
        {
            return value;
        }

        return RuntimePathGuard.ResolveChildPath(packageRoot, text);
    }

    private static bool LooksLikeFileParameter(ParameterDto parameter)
    {
        return parameter.DataType.Equals("file", StringComparison.OrdinalIgnoreCase) ||
               parameter.DataType.Equals("filepath", StringComparison.OrdinalIgnoreCase) ||
               parameter.DataType.Equals("folder", StringComparison.OrdinalIgnoreCase) ||
               parameter.DataType.Equals("directory", StringComparison.OrdinalIgnoreCase) ||
               parameter.DataType.Equals("model", StringComparison.OrdinalIgnoreCase) ||
               parameter.DataType.Equals("weights", StringComparison.OrdinalIgnoreCase) ||
               parameter.DataType.Equals("onnx", StringComparison.OrdinalIgnoreCase) ||
               parameter.DataType.Equals("calibration", StringComparison.OrdinalIgnoreCase) ||
               parameter.Name.EndsWith("Path", StringComparison.OrdinalIgnoreCase) ||
               parameter.Name.EndsWith("Directory", StringComparison.OrdinalIgnoreCase) ||
               parameter.Name.EndsWith("Folder", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeScalar(object? value)
    {
        return value switch
        {
            null => null,
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text => text.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } element when string.IsNullOrWhiteSpace(element.GetString()) => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()?.Trim(),
            JsonElement { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } element => element.ToString(),
            _ => value.ToString()
        };
    }
}
