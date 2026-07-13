using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Runtime;

public sealed class RuntimePackageExporter
{
    private static readonly string[] SecretLikeTokens =
    [
        "apikey",
        "api_key",
        "secret",
        "token",
        "password",
        "credential"
    ];

    private static string SanitizeLogValue(object? value)
    {
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static readonly HashSet<string> FileLikeParameterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "file",
        "filepath",
        "folder",
        "directory",
        "model",
        "weights",
        "onnx",
        "calibration"
    };

    private readonly IOperatorFactory _operatorFactory;
    private readonly IReadOnlyDictionary<OperatorType, IOperatorExecutor> _executorsByType;
    private readonly ILogger<RuntimePackageExporter> _logger;

    public RuntimePackageExporter(
        IOperatorFactory operatorFactory,
        ILogger<RuntimePackageExporter> logger,
        IEnumerable<IOperatorExecutor>? executors = null)
    {
        _operatorFactory = operatorFactory;
        _executorsByType = (executors ?? Array.Empty<IOperatorExecutor>())
            .GroupBy(executor => executor.OperatorType)
            .ToDictionary(group => group.Key, group => group.Last());
        _logger = logger;
    }

    public async Task<RuntimePackageExportResult> ExportAsync(
        RuntimePackageExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);

        var project = request.Project;
        var flow = project.Flow ?? throw new RuntimePackageException("The selected project does not contain an executable flow.");
        if (flow.Operators.Count == 0)
        {
            throw new RuntimePackageException("The selected project does not contain any operators.");
        }

        var decisionIssues = FinalDecisionResolver.Validate(flow.ToEntity());
        if (decisionIssues.Count > 0)
        {
            throw new RuntimePackageException(
                $"Export blocked: {decisionIssues[0].Code}: {decisionIssues[0].Message}");
        }

        var parameterValidationErrors = FindParameterValidationErrors(flow).ToList();
        var missingResources = FindMissingResources(flow).ToList();
        var secretFindings = FindSecretLikeFields(flow).ToList();
        if (secretFindings.Count > 0)
        {
            throw new RuntimePackageException(
                "导出被阻止：以下算子的参数包含疑似密钥、令牌或认证信息，" +
                "出于安全考虑不允许打包到 Runtime Package 中。" +
                "请将敏感信息移到环境变量或运行时配置，清空对应参数值后重试。\n" +
                "涉及参数：\n• " + string.Join("\n• ", secretFindings));
        }

        if (parameterValidationErrors.Count > 0)
        {
            throw new RuntimePackageException(
                "导出被阻止：以下算子的参数配置未通过校验，" +
                "请在 Studio 中检查对应算子配置后重新导出。\n" +
                "参数问题：\n• " + string.Join("\n• ", parameterValidationErrors));
        }

        if (missingResources.Count > 0)
        {
            throw new RuntimePackageException(
                "导出被阻止：以下算子引用的文件或目录在本机不存在，" +
                "请先确认路径是否正确、文件是否已就位，然后重新导出。\n" +
                "缺失资源：\n• " + string.Join("\n• ", missingResources));
        }

        var targetRoot = RuntimePathGuard.ResolveControlledExportRoot(request.TargetRootDirectory);
        var globalVariableValidationErrors = FindGlobalVariableValidationErrors(project.GlobalVariables, flow).ToList();
        if (globalVariableValidationErrors.Count > 0)
        {
            throw new RuntimePackageException(
                "Export blocked: project global variable validation failed.\n- " +
                string.Join("\n- ", globalVariableValidationErrors));
        }

        var packagedProjectAssets = PrepareProjectAssets(
            project,
            request.ProjectAssetStorageMetadata,
            request.RequireProjectAssetStorageMetadata);
        var packageId = $"cvpkg-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];

        var safeProjectName = RuntimePathGuard.SanitizeFileName(project.Name, "runtime-package");
        var packageRoot = Path.Combine(targetRoot, $"{safeProjectName}-{packageId}");
        var packageRootCreated = false;
        try
        {
            Directory.CreateDirectory(packageRoot);
            packageRootCreated = true;
            Directory.CreateDirectory(Path.Combine(packageRoot, "quality"));
            Directory.CreateDirectory(Path.Combine(packageRoot, "field"));

            var packagedFlow = CloneFlow(flow);
            var bundledAssets = await BundleFlowResourcesAsync(packagedFlow, packageRoot, cancellationToken);
            await WriteProjectAssetFilesAsync(packageRoot, packagedProjectAssets.Files, cancellationToken);
            var flowBytes = JsonSerializer.SerializeToUtf8Bytes(packagedFlow, RuntimeJson.StableSerializerOptions);
            // Flow identity is semantic and shared with Studio execution. A
            // formatting-only rewrite of flow.json must not create a second
            // identity for the same executable definition.
            var flowHash = ExecutionFlowIdentity.ComputeFlowHash(packagedFlow.ToEntity());
            var profile = new RuntimeProfile();
            var manifest = new RuntimePackageManifest
            {
                PackageId = packageId,
                PackageName = string.IsNullOrWhiteSpace(project.Name) ? packageId : project.Name.Trim(),
                RuntimeApiVersion = profile.RuntimeApiVersion,
                MinStationVersion = "0.1.0",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = request.CreatedBy,
                SourceProjectId = project.Id,
                SourceProjectRevision = project.PersistenceRevision,
                EntryFlow = "flow.json",
                FlowHash = flowHash,
                DecisionConfigurationHash = ExecutionFlowIdentity.ComputeDecisionConfigurationHash(
                    packagedFlow.ToEntity().DecisionConfiguration),
                OperatorCatalogVersion = BuildOperatorCatalogVersion(),
                ExportAllowed = true,
                PendingParameters = parameterValidationErrors,
                MissingResources = missingResources,
                FieldExtensions = new RuntimeFieldExtensions
                {
                    StationProfile = "field/station-profile.json",
                    TriggerProfile = "field/trigger-profile.json",
                    ResultMappingProfile = "field/result-mapping-profile.json",
                    ModelAssets = "field/model-assets.json",
                    RuntimeParameters = "field/runtime-parameters.json",
                    DefaultSiteProfile = "field/station-profile.default.json",
                    GlobalVariables = "field/global-variables.json",
                    ProjectAssets = packagedProjectAssets.Manifest == null ? null : "assets"
                },
                Assets = packagedProjectAssets.Manifest
            };
            var parameterSchema = BuildRuntimeParameterSchema(packageId, flowHash, packagedFlow);
            RuntimeProjectVariableConflictValidator.ThrowIfAnySiteProfileConflicts(
                project.GlobalVariables,
                parameterSchema,
                packagedFlow);
            var defaultSiteProfile = new RuntimeSiteProfile
            {
                ProfileId = "package-default",
                PackageId = packageId,
                FlowHash = flowHash,
                Revision = 0,
                UpdatedAtUtc = manifest.CreatedAt,
                UpdatedBy = string.IsNullOrWhiteSpace(manifest.CreatedBy) ? "ClearVision Studio" : manifest.CreatedBy,
                Overrides = []
            };

            var validationReport = new RuntimeValidationReport
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                IsValid = true,
                FlowHash = flowHash,
                Notes =
                [
                    $"ProjectId={project.Id:D}",
                    $"OperatorCount={flow.Operators.Count}",
                    $"ConnectionCount={flow.Connections.Count}",
                    $"CalibrationAssetCount={packagedProjectAssets.Manifest?.CalibrationAssets.Count ?? 0}",
                    $"SpatialAssetCount={packagedProjectAssets.Manifest?.SpatialAssets.Count ?? 0}"
                ]
            };

            var packageBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, RuntimeJson.SerializerOptions);
            var profileBytes = JsonSerializer.SerializeToUtf8Bytes(profile, RuntimeJson.SerializerOptions);
            var validationBytes = JsonSerializer.SerializeToUtf8Bytes(validationReport, RuntimeJson.SerializerOptions);

            await File.WriteAllBytesAsync(Path.Combine(packageRoot, "package.json"), packageBytes, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(packageRoot, "flow.json"), flowBytes, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(packageRoot, "runtime-profile.json"), profileBytes, cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(packageRoot, "quality", "validation-report.json"),
                validationBytes,
                cancellationToken);

            await WriteFieldSchemaDraftsAsync(
                packageRoot,
                bundledAssets,
                parameterSchema,
                defaultSiteProfile,
                project.GlobalVariables,
                cancellationToken);

            var readmePath = Path.Combine(packageRoot, "README.runtime.md");
            await File.WriteAllTextAsync(readmePath, BuildReadme(manifest, validationReport), cancellationToken);

            _logger.LogInformation(
                "Exported runtime package {PackageId} for project {ProjectId} to {PackageRoot}",
                SanitizeLogValue(manifest.PackageId),
                SanitizeLogValue(project.Id),
                SanitizeLogValue(packageRoot));

            return new RuntimePackageExportResult
            {
                PackageRootPath = packageRoot,
                Manifest = manifest,
                ValidationReport = validationReport,
                ReadmePath = readmePath
            };
        }
        catch
        {
            if (packageRootCreated)
            {
                TryDeleteDirectory(packageRoot);
            }

            throw;
        }
    }

    private string BuildOperatorCatalogVersion()
    {
        var names = _operatorFactory
            .GetSupportedOperatorTypes()
            .OrderBy(item => item.ToString(), StringComparer.Ordinal)
            .Select(item => item.ToString());
        var payload = string.Join("|", names);
        var hash = RuntimePathGuard.ComputeSha256(Encoding.UTF8.GetBytes(payload));
        return $"{payload.Count(ch => ch == '|') + 1}+{hash[7..19]}";
    }

    private static RuntimePackagedProjectAssets PrepareProjectAssets(
        ProjectDto project,
        ProjectAssetStorageMetadata? storageMetadata,
        bool requireStorageMetadata)
    {
        var assets = ProjectAssetJson.Normalize(ProjectAssetJson.Clone(project.Assets ?? new ProjectAssetsDto()));
        if (!ProjectAssetJson.HasAssets(assets))
        {
            return RuntimePackagedProjectAssets.Empty;
        }

        ValidateProjectAssetStorageMetadata(project, assets, storageMetadata, requireStorageMetadata);

        var manifest = new RuntimePackageAssets
        {
            SchemaVersion = assets.SchemaVersion <= 0 ? 1 : assets.SchemaVersion
        };
        var files = new List<RuntimeProjectAssetFile>();
        var usedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets.CalibrationAssets.OrderBy(asset => asset.AssetId, StringComparer.Ordinal))
        {
            ValidateCalibrationAuthorityAsset(project, asset);
            var relativePath = BuildProjectAssetRelativePath("calibration", asset.AssetId, usedRelativePaths);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(asset, ProjectAssetJson.Options);
            var fileHash = RuntimePathGuard.ComputeSha256(bytes);
            manifest.CalibrationAssets.Add(ToRuntimeAsset(asset, relativePath, fileHash));
            files.Add(new RuntimeProjectAssetFile(relativePath, bytes, fileHash));
        }

        foreach (var asset in assets.SpatialAssets.OrderBy(asset => asset.AssetId, StringComparer.Ordinal))
        {
            ValidateSpatialAuthorityAsset(project, asset);
            var relativePath = BuildProjectAssetRelativePath("spatial", asset.AssetId, usedRelativePaths);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(asset, ProjectAssetJson.Options);
            var fileHash = RuntimePathGuard.ComputeSha256(bytes);
            manifest.SpatialAssets.Add(ToRuntimeAsset(asset, relativePath, fileHash));
            files.Add(new RuntimeProjectAssetFile(relativePath, bytes, fileHash));
        }

        return new RuntimePackagedProjectAssets(manifest, files);
    }

    private static void ValidateProjectAssetStorageMetadata(
        ProjectDto project,
        ProjectAssetsDto assets,
        ProjectAssetStorageMetadata? storageMetadata,
        bool requireStorageMetadata)
    {
        if (requireStorageMetadata && storageMetadata == null)
        {
            throw new RuntimePackageException("RPA001: project asset storage metadata is required for runtime package export.");
        }

        if (storageMetadata == null)
        {
            return;
        }

        if (storageMetadata.ProjectId != project.Id)
        {
            throw new RuntimePackageException("RPA002: project asset storage metadata belongs to a different project.");
        }

        if (storageMetadata.PersistenceRevision != project.PersistenceRevision)
        {
            throw new RuntimePackageException(
                $"RPA003: project asset storage revision {storageMetadata.PersistenceRevision} does not match project revision {project.PersistenceRevision}.");
        }

        var assetsHash = ProjectAssetJson.ComputeAssetsHash(assets);
        if (!string.Equals(storageMetadata.AssetsHash, assetsHash, StringComparison.Ordinal))
        {
            throw new RuntimePackageException("RPA004: project asset storage hash does not match the Project DTO assets.");
        }
    }

    private static void ValidateCalibrationAuthorityAsset(ProjectDto project, ProjectCalibrationAssetDto asset)
    {
        ValidateCommonAuthorityAsset(
            project,
            asset.AssetId,
            asset.Kind,
            asset.ProjectRevision,
            asset.Status,
            asset.ContentHash,
            asset.Payload);

        if (!string.Equals(asset.Kind, "CalibrationBundleV2", StringComparison.Ordinal))
        {
            throw new RuntimePackageException($"RPA005: calibration asset '{asset.AssetId}' must be CalibrationBundleV2.");
        }

        if (!TryReadPayloadInt32(asset.Payload, "schemaVersion", out var schemaVersion) ||
            schemaVersion != 2)
        {
            throw new RuntimePackageException(
                $"RPA014: calibration asset '{asset.AssetId}' payload schemaVersion must be 2.");
        }
    }

    private static void ValidateSpatialAuthorityAsset(ProjectDto project, ProjectSpatialAssetDto asset)
    {
        ValidateCommonAuthorityAsset(
            project,
            asset.AssetId,
            asset.Kind,
            asset.ProjectRevision,
            asset.Status,
            asset.ContentHash,
            asset.Payload);
    }

    private static void ValidateCommonAuthorityAsset(
        ProjectDto project,
        string assetId,
        string kind,
        long projectRevision,
        string status,
        string contentHash,
        JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            throw new RuntimePackageException("RPA006: project asset id is required.");
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new RuntimePackageException($"RPA007: project asset '{assetId}' kind is required.");
        }

        if (!string.Equals(status, "authority", StringComparison.OrdinalIgnoreCase))
        {
            throw new RuntimePackageException($"RPA008: project asset '{assetId}' is not an authority asset.");
        }

        if (projectRevision != project.PersistenceRevision)
        {
            throw new RuntimePackageException(
                $"RPA009: project asset '{assetId}' revision {projectRevision} does not match project revision {project.PersistenceRevision}.");
        }

        string payloadHash;
        try
        {
            payloadHash = ProjectAssetJson.ComputePayloadHash(payload);
        }
        catch (InvalidOperationException ex)
        {
            throw new RuntimePackageException($"RPA013: project asset '{assetId}' payload is invalid: {ex.Message}", ex);
        }

        if (!string.Equals(contentHash, payloadHash, StringComparison.Ordinal))
        {
            throw new RuntimePackageException($"RPA010: project asset '{assetId}' content hash does not match its payload.");
        }

        if (!IsSha256Hash(contentHash))
        {
            throw new RuntimePackageException($"RPA011: project asset '{assetId}' content hash is not a sha256 hash.");
        }
    }

    private static RuntimePackageProjectAsset ToRuntimeAsset(
        ProjectCalibrationAssetDto asset,
        string relativePath,
        string fileHash) =>
        new()
        {
            AssetId = asset.AssetId,
            Kind = asset.Kind,
            Version = asset.Version,
            ProjectRevision = asset.ProjectRevision,
            ContentHash = asset.ContentHash,
            FileHash = fileHash,
            RelativePath = relativePath,
            Required = false,
            Status = asset.Status
        };

    private static RuntimePackageProjectAsset ToRuntimeAsset(
        ProjectSpatialAssetDto asset,
        string relativePath,
        string fileHash) =>
        new()
        {
            AssetId = asset.AssetId,
            Kind = asset.Kind,
            Version = asset.Version,
            ProjectRevision = asset.ProjectRevision,
            ContentHash = asset.ContentHash,
            FileHash = fileHash,
            RelativePath = relativePath,
            Required = false,
            Status = asset.Status
        };

    private static string BuildProjectAssetRelativePath(
        string category,
        string assetId,
        HashSet<string> usedRelativePaths)
    {
        var sanitizedAssetId = RuntimePathGuard.SanitizeFileName(assetId, $"{category}-asset")
            .Replace("..", "-", StringComparison.Ordinal)
            .Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(sanitizedAssetId))
        {
            sanitizedAssetId = $"{category}-asset";
        }

        if (sanitizedAssetId.Length > 80)
        {
            sanitizedAssetId = sanitizedAssetId[..80];
        }

        var idHash = RuntimePathGuard.ComputeSha256(Encoding.UTF8.GetBytes(assetId));
        var suffix = idHash[7..19];
        var fileName = $"{sanitizedAssetId}-{suffix}.json";
        var relativePath = ToPackageRelativePath(Path.Combine("assets", category, fileName));
        var collision = 1;
        while (!usedRelativePaths.Add(relativePath))
        {
            relativePath = ToPackageRelativePath(Path.Combine("assets", category, $"{sanitizedAssetId}-{suffix}-{collision}.json"));
            collision += 1;
        }

        RuntimePathGuard.ValidateStrictRelativeAssetPath(relativePath);
        return relativePath;
    }

    private RuntimeParameterSchema BuildRuntimeParameterSchema(
        string packageId,
        string flowHash,
        OperatorFlowDto flow)
    {
        var schema = new RuntimeParameterSchema
        {
            PackageId = packageId,
            FlowHash = flowHash,
            Parameters = []
        };

        var order = 10;
        foreach (var op in flow.Operators)
        {
            if (op.Type != OperatorType.DeepLearning || !MayResolveToObjectDetection(op))
            {
                continue;
            }

            var confidence = FindParameter(op, "Confidence");
            if (confidence == null)
            {
                continue;
            }

            var defaultValue = TryReadDouble(confidence.Value, out var value)
                ? value
                : (TryReadDouble(confidence.DefaultValue, out var fallback) ? fallback : 0.5d);
            var min = TryReadDouble(confidence.MinValue, out var minValue) ? minValue : 0.0d;
            var max = TryReadDouble(confidence.MaxValue, out var maxValue) ? maxValue : 1.0d;
            var displayName = !string.IsNullOrWhiteSpace(confidence.DisplayName)
                ? confidence.DisplayName.Trim()
                : (!string.IsNullOrWhiteSpace(op.Name) ? $"{op.Name.Trim()}置信度" : "检测置信度");

            schema.Parameters.Add(new RuntimeParameterDefinition
            {
                Id = BuildParameterId(op.Id, confidence.Name),
                OperatorId = op.Id,
                OperatorName = op.Name,
                OperatorType = op.Type.ToString(),
                ParameterName = confidence.Name,
                DisplayName = displayName,
                Description = string.IsNullOrWhiteSpace(confidence.Description)
                    ? "低于该置信度的检测结果不参与判定。"
                    : confidence.Description,
                GroupName = "现场参数",
                ValueType = RuntimeParameterValueType.Number,
                UiKind = RuntimeParameterUiKind.NumericInput,
                DefaultValue = JsonSerializer.SerializeToElement(defaultValue, RuntimeJson.SerializerOptions),
                Min = min,
                Max = max,
                Step = 0.01d,
                SiteTunable = true,
                RequiresEngineerMode = true,
                ApplyMode = RuntimeParameterApplyMode.NextRun,
                Order = order
            });
            order += 10;
        }

        foreach (var op in flow.Operators)
        {
            if (!op.IsEnabled)
            {
                continue;
            }

            var disabledParameters = ResolveDisabledRuntimeParameters(op);

            foreach (var parameter in op.Parameters)
            {
                var parameterId = BuildParameterId(op.Id, parameter.Name);
                if (schema.Parameters.Any(existing => existing.Id.Equals(parameterId, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!TryBuildRuntimeParameterDefinition(
                        op,
                        parameter,
                        disabledParameters,
                        order,
                        out var definition))
                {
                    continue;
                }

                schema.Parameters.Add(definition);
                order += 10;
            }
        }

        return schema;
    }

    private static bool TryBuildRuntimeParameterDefinition(
        OperatorDto op,
        ParameterDto parameter,
        IReadOnlySet<string> disabledParameters,
        int order,
        out RuntimeParameterDefinition definition)
    {
        definition = null!;
        if (!ShouldExposeRuntimeParameter(op, parameter, disabledParameters))
        {
            return false;
        }

        var defaultValue = TryReadDouble(parameter.Value, out var current)
            ? current
            : (TryReadDouble(parameter.DefaultValue, out var fallback) ? fallback : 0.0d);

        if (!TryInferRuntimeParameterBounds(parameter, defaultValue, out var min, out var max))
        {
            return false;
        }

        var displayName = !string.IsNullOrWhiteSpace(parameter.DisplayName)
            ? parameter.DisplayName.Trim()
            : $"{ResolveOperatorName(op)}.{parameter.Name}";

        definition = new RuntimeParameterDefinition
        {
            Id = BuildParameterId(op.Id, parameter.Name),
            OperatorId = op.Id,
            OperatorName = op.Name,
            OperatorType = op.Type.ToString(),
            ParameterName = parameter.Name,
            DisplayName = displayName,
            Description = parameter.Description,
            GroupName = ResolveOperatorName(op),
            ValueType = RuntimeParameterValueType.Number,
            UiKind = RuntimeParameterUiKind.NumericInput,
            DefaultValue = JsonSerializer.SerializeToElement(defaultValue, RuntimeJson.SerializerOptions),
            Min = min,
            Max = max,
            Step = InferRuntimeParameterStep(parameter, min, max),
            RequiresInteger = IsIntegerParameterType(parameter.DataType),
            SiteTunable = true,
            RequiresEngineerMode = true,
            ApplyMode = RuntimeParameterApplyMode.NextRun,
            Order = order
        };
        return true;
    }

    private static bool ShouldExposeRuntimeParameter(
        OperatorDto op,
        ParameterDto parameter,
        IReadOnlySet<string> disabledParameters)
    {
        if (!op.IsEnabled ||
            disabledParameters.Contains(parameter.Name) ||
            string.IsNullOrWhiteSpace(parameter.Name) ||
            LooksLikeFileParameter(parameter) ||
            LooksLikeSecretParameter(parameter) ||
            !IsNumericParameterType(parameter.DataType))
        {
            return false;
        }

        if (!TryReadDouble(parameter.Value, out _) &&
            !TryReadDouble(parameter.DefaultValue, out _))
        {
            return false;
        }

        if (TryReadDouble(parameter.MinValue, out _) ||
            TryReadDouble(parameter.MaxValue, out _))
        {
            return true;
        }

        return LooksLikeCoordinateParameter(parameter.Name) ||
               LooksLikeNormalizedParameter(parameter.Name) ||
               LooksLikeByteThresholdParameter(parameter.Name);
    }

    private IReadOnlySet<string> ResolveDisabledRuntimeParameters(OperatorDto op)
    {
        var metadata = _operatorFactory.GetMetadata(op.Type);
        if (metadata == null || metadata.ParameterConstraints.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = op.Parameters
            .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.OrdinalIgnoreCase);

        return OperatorParameterConstraintEvaluator.ResolveStates(metadata, values)
            .Where(state => state.EffectiveDisabled)
            .Select(state => state.Constraint.Parameter)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryInferRuntimeParameterBounds(
        ParameterDto parameter,
        double defaultValue,
        out double min,
        out double max)
    {
        var hasMin = TryReadDouble(parameter.MinValue, out min);
        var hasMax = TryReadDouble(parameter.MaxValue, out max);

        if (!hasMin && !hasMax)
        {
            if (LooksLikeNormalizedParameter(parameter.Name) && defaultValue is >= 0.0d and <= 1.0d)
            {
                min = 0.0d;
                max = 1.0d;
            }
            else if (LooksLikeByteThresholdParameter(parameter.Name))
            {
                min = 0.0d;
                max = 255.0d;
            }
            else if (LooksLikeCoordinateParameter(parameter.Name))
            {
                min = 0.0d;
                max = 10_000.0d;
            }
            else
            {
                return false;
            }
        }
        else if (!hasMin)
        {
            min = Math.Min(0.0d, defaultValue);
        }
        else if (!hasMax)
        {
            if (LooksLikeNormalizedParameter(parameter.Name) && defaultValue is >= 0.0d and <= 1.0d)
            {
                max = 1.0d;
            }
            else
            {
                var spanSeed = Math.Max(1.0d, Math.Max(Math.Abs(defaultValue), Math.Abs(defaultValue - min)));
                max = min + spanSeed * 10.0d;
            }
        }

        if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max))
        {
            return false;
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (Math.Abs(max - min) < double.Epsilon)
        {
            max = min + 1.0d;
        }

        if (defaultValue < min)
        {
            min = defaultValue;
        }

        if (defaultValue > max)
        {
            max = defaultValue;
        }

        return true;
    }

    private static double InferRuntimeParameterStep(ParameterDto parameter, double min, double max)
    {
        if (IsIntegerParameterType(parameter.DataType))
        {
            return 1.0d;
        }

        var range = Math.Abs(max - min);
        if (range <= 1.0d || LooksLikeNormalizedParameter(parameter.Name))
        {
            return 0.01d;
        }

        if (range <= 10.0d)
        {
            return 0.1d;
        }

        return 1.0d;
    }

    private static string ResolveOperatorName(OperatorDto op)
    {
        return string.IsNullOrWhiteSpace(op.Name) ? op.Type.ToString() : op.Name.Trim();
    }

    private static OperatorFlowDto CloneFlow(OperatorFlowDto flow)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(flow, RuntimeJson.SerializerOptions);
        return JsonSerializer.Deserialize<OperatorFlowDto>(bytes, RuntimeJson.SerializerOptions)
            ?? throw new RuntimePackageException("Unable to clone flow for runtime packaging.");
    }

    private static async Task<IReadOnlyList<RuntimeBundledAsset>> BundleFlowResourcesAsync(
        OperatorFlowDto flow,
        string packageRoot,
        CancellationToken cancellationToken)
    {
        var assets = new List<RuntimeBundledAsset>();
        var bundledBySource = new Dictionary<string, RuntimeBundledAsset>(StringComparer.OrdinalIgnoreCase);

        foreach (var op in flow.Operators)
        {
            var originalDeepLearningModelPath = op.Type == OperatorType.DeepLearning
                ? NormalizeScalar(FindParameter(op, "ModelPath")?.Value)
                : null;

            foreach (var parameter in op.Parameters)
            {
                if (!LooksLikeFileParameter(parameter))
                {
                    continue;
                }

                var sourcePath = NormalizeScalar(parameter.Value);
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    continue;
                }

                var asset = await BundleResourceAsync(
                    packageRoot,
                    op,
                    parameter.Name,
                    sourcePath,
                    bundledBySource,
                    assets,
                    cancellationToken);

                parameter.Value = asset.RelativePath;
            }

            await BundleDeepLearningAutoLabelsAsync(
                packageRoot,
                op,
                originalDeepLearningModelPath,
                bundledBySource,
                assets,
                cancellationToken);
        }

        return assets;
    }

    private static async Task WriteProjectAssetFilesAsync(
        string packageRoot,
        IReadOnlyList<RuntimeProjectAssetFile> files,
        CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            var targetPath = RuntimePathGuard.ResolveAssetPath(packageRoot, file.RelativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            await File.WriteAllBytesAsync(targetPath, file.Bytes, cancellationToken);
            var writtenHash = await ComputeFileSha256Async(targetPath, cancellationToken);
            if (!string.Equals(writtenHash, file.FileHash, StringComparison.Ordinal))
            {
                throw new RuntimePackageException($"RPA012: packaged project asset checksum changed while writing '{file.RelativePath}'.");
            }
        }
    }

    private static async Task BundleDeepLearningAutoLabelsAsync(
        string packageRoot,
        OperatorDto op,
        string? originalModelPath,
        Dictionary<string, RuntimeBundledAsset> bundledBySource,
        List<RuntimeBundledAsset> assets,
        CancellationToken cancellationToken)
    {
        if (op.Type != OperatorType.DeepLearning)
        {
            return;
        }

        if (!MayResolveToObjectDetection(op))
        {
            return;
        }

        var labelsParameter = FindParameter(op, "LabelsPath");
        if (!string.IsNullOrWhiteSpace(NormalizeScalar(labelsParameter?.Value)))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(originalModelPath))
        {
            return;
        }

        var resolvedModelPath = Path.GetFullPath(originalModelPath);
        var originalModelDirectory = Path.GetDirectoryName(resolvedModelPath);
        if (string.IsNullOrWhiteSpace(originalModelDirectory))
        {
            return;
        }

        var siblingLabelsPath = Path.Combine(originalModelDirectory, "labels.txt");
        if (!File.Exists(siblingLabelsPath))
        {
            return;
        }

        var modelAsset = bundledBySource.TryGetValue(resolvedModelPath, out var bundledModel)
            ? bundledModel
            : null;
        if (modelAsset == null)
        {
            return;
        }

        var modelRelativeDirectory = Path.GetDirectoryName(modelAsset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(modelRelativeDirectory))
        {
            return;
        }

        var labelsRelativePath = ToPackageRelativePath(Path.Combine(modelRelativeDirectory, "labels.txt"));
        var labelsAsset = await BundleResourceAsync(
            packageRoot,
            op,
            "LabelsPath",
            siblingLabelsPath,
            bundledBySource,
            assets,
            cancellationToken,
            labelsRelativePath);

        if (labelsParameter != null)
        {
            labelsParameter.Value = labelsAsset.RelativePath;
        }
    }

    private static bool MayResolveToObjectDetection(OperatorDto op)
    {
        var taskType = NormalizeScalar(FindParameter(op, "TaskType")?.Value);
        return string.IsNullOrWhiteSpace(taskType) ||
               taskType.Equals("ObjectDetection", StringComparison.OrdinalIgnoreCase) ||
               taskType.Equals("Detection", StringComparison.OrdinalIgnoreCase) ||
               taskType.Equals("Auto", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<RuntimeBundledAsset> BundleResourceAsync(
        string packageRoot,
        OperatorDto op,
        string parameterName,
        string sourcePath,
        Dictionary<string, RuntimeBundledAsset> bundledBySource,
        List<RuntimeBundledAsset> assets,
        CancellationToken cancellationToken,
        string? preferredRelativePath = null)
    {
        var resolvedSourcePath = Path.GetFullPath(sourcePath);
        if (bundledBySource.TryGetValue(resolvedSourcePath, out var existing))
        {
            return existing;
        }

        var isDirectory = Directory.Exists(resolvedSourcePath);
        var relativePath = preferredRelativePath ?? BuildAssetRelativePath(op, parameterName, resolvedSourcePath, isDirectory);
        var targetPath = RuntimePathGuard.ResolveChildPath(packageRoot, relativePath);

        if (isDirectory)
        {
            await CopyDirectoryAsync(resolvedSourcePath, targetPath, cancellationToken);
        }
        else
        {
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            await using var source = File.OpenRead(resolvedSourcePath);
            await using var target = File.Create(targetPath);
            await source.CopyToAsync(target, cancellationToken);
        }

        var asset = new RuntimeBundledAsset
        {
            OperatorId = op.Id,
            OperatorName = op.Name,
            OperatorType = op.Type.ToString(),
            ParameterName = parameterName,
            FileName = Path.GetFileName(resolvedSourcePath),
            RelativePath = ToPackageRelativePath(relativePath),
            Kind = isDirectory ? "directory" : "file",
            LengthBytes = isDirectory ? null : new FileInfo(resolvedSourcePath).Length,
            Sha256 = isDirectory ? null : await ComputeFileSha256Async(resolvedSourcePath, cancellationToken)
        };

        bundledBySource[resolvedSourcePath] = asset;
        assets.Add(asset);
        return asset;
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static async Task CopyDirectoryAsync(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            var normalizedTargetRoot = Path.GetFullPath(targetDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!targetFile.StartsWith(normalizedTargetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new RuntimePackageException($"Resource path escapes bundled asset directory: {relative}");
            }

            var parent = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await using var source = File.OpenRead(sourceFile);
            await using var target = File.Create(targetFile);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private static string BuildAssetRelativePath(
        OperatorDto op,
        string parameterName,
        string sourcePath,
        bool isDirectory)
    {
        var operatorFolder = $"{RuntimePathGuard.SanitizeFileName(op.Name, op.Type.ToString())}-{op.Id:N}";
        var parameterFolder = RuntimePathGuard.SanitizeFileName(parameterName, "resource");
        var assetName = RuntimePathGuard.SanitizeFileName(
            Path.GetFileName(sourcePath),
            isDirectory ? "directory" : "file");

        return ToPackageRelativePath(Path.Combine("assets", "resources", operatorFolder, parameterFolder, assetName));
    }

    private static ParameterDto? FindParameter(OperatorDto op, string parameterName)
    {
        return op.Parameters.FirstOrDefault(parameter =>
            parameter.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildParameterId(Guid operatorId, string parameterName)
    {
        return $"node.{operatorId:D}.{parameterName}";
    }

    private IEnumerable<string> FindParameterValidationErrors(OperatorFlowDto flow)
    {
        OperatorFlow entityFlow;
        try
        {
            entityFlow = flow.ToEntity();
        }
        catch (Exception ex)
        {
            return [$"Flow: {ex.Message}"];
        }

        var errors = new List<string>();
        var dtoOperatorsById = flow.Operators.ToDictionary(op => op.Id);
        foreach (var op in entityFlow.Operators)
        {
            if (_executorsByType.TryGetValue(op.Type, out var executor))
            {
                var validation = executor.ValidateParameters(op);
                if (validation.IsValid)
                {
                    continue;
                }

                foreach (var error in validation.Errors.Where(error => !string.IsNullOrWhiteSpace(error)))
                {
                    errors.Add($"{op.Name}: {error}");
                }
                continue;
            }

            if (!dtoOperatorsById.TryGetValue(op.Id, out var opDto))
            {
                continue;
            }

            foreach (var parameter in opDto.Parameters)
            {
                if (!parameter.IsRequired)
                {
                    continue;
                }

                if (NormalizeScalar(parameter.Value) != null)
                {
                    continue;
                }

                if (NormalizeScalar(parameter.DefaultValue) != null)
                {
                    continue;
                }

                errors.Add($"{op.Name}.{parameter.Name}");
            }
        }

        return errors;
    }

    private static IEnumerable<string> FindGlobalVariableValidationErrors(
        ProjectGlobalVariableSchema? globalVariables,
        OperatorFlowDto flow)
    {
        OperatorFlow entityFlow;
        try
        {
            entityFlow = flow.ToEntity();
        }
        catch (Exception ex)
        {
            return [$"Flow: {ex.Message}"];
        }

        return ProjectGlobalVariableSchemaValidator
            .Validate(globalVariables, entityFlow)
            .Where(diagnostic => diagnostic.Severity == ProjectGlobalVariableDiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
            .ToList();
    }

    private static IEnumerable<string> FindMissingResources(OperatorFlowDto flow)
    {
        foreach (var op in flow.Operators)
        {
            foreach (var parameter in op.Parameters)
            {
                if (!LooksLikeFileParameter(parameter))
                {
                    continue;
                }

                var text = NormalizeScalar(parameter.Value);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (!File.Exists(text) && !Directory.Exists(text))
                {
                    yield return $"{op.Name}.{parameter.Name}";
                }
            }
        }
    }

    private static IEnumerable<string> FindSecretLikeFields(OperatorFlowDto flow)
    {
        foreach (var op in flow.Operators)
        {
            foreach (var parameter in op.Parameters)
            {
                if (SecretLikeTokens.Any(token =>
                        parameter.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.IsNullOrWhiteSpace(NormalizeScalar(parameter.Value)))
                    {
                        yield return $"{op.Name}.{parameter.Name}";
                    }

                    continue;
                }

                var text = NormalizeScalar(parameter.Value);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (text.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{op.Name}.{parameter.Name}";
                }
            }
        }
    }

    private static bool LooksLikeFileParameter(ParameterDto parameter)
    {
        if (FileLikeParameterTypes.Contains(parameter.DataType))
        {
            return true;
        }

        return parameter.Name.EndsWith("Path", StringComparison.OrdinalIgnoreCase) ||
               parameter.Name.EndsWith("Directory", StringComparison.OrdinalIgnoreCase) ||
               parameter.Name.EndsWith("Folder", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSecretParameter(ParameterDto parameter)
    {
        return SecretLikeTokens.Any(token =>
            parameter.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            parameter.DataType.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNumericParameterType(string? dataType)
    {
        return dataType is not null &&
               (dataType.Equals("int", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("integer", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("long", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("float", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("double", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("number", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("numeric", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIntegerParameterType(string? dataType)
    {
        return dataType is not null &&
               (dataType.Equals("int", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("integer", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("long", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeNormalizedParameter(string parameterName)
    {
        return parameterName.Contains("confidence", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Contains("score", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Contains("iou", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Contains("ratio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeByteThresholdParameter(string parameterName)
    {
        return parameterName.Contains("threshold", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCoordinateParameter(string parameterName)
    {
        return parameterName.Equals("x", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Equals("x1", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Equals("y1", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Equals("x2", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Equals("y2", StringComparison.OrdinalIgnoreCase) ||
               parameterName.EndsWith("X", StringComparison.OrdinalIgnoreCase) ||
               parameterName.EndsWith("Y", StringComparison.OrdinalIgnoreCase);
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

    private static bool TryReadDouble(object? value, out double number)
    {
        number = 0;
        switch (value)
        {
            case null:
                return false;
            case double doubleValue:
                number = doubleValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetDouble(out number);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return double.TryParse(element.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
            case string text:
                return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) ||
                       double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out number);
            default:
                var scalar = NormalizeScalar(value);
                return double.TryParse(scalar, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) ||
                       double.TryParse(scalar, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out number);
        }
    }

    private static string ToPackageRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool IsSha256Hash(string? value)
    {
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != prefix.Length + 64 ||
            !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return value[prefix.Length..].All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'a' and <= 'f');
    }

    private static bool TryReadPayloadInt32(JsonElement payload, string propertyName, out int value)
    {
        value = 0;
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(propertyName, out var property) &&
               property.TryGetInt32(out value);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Export callers receive the original failure; cleanup best-effort avoids masking it.
        }
    }

    private static async Task WriteFieldSchemaDraftsAsync(
        string packageRoot,
        IReadOnlyList<RuntimeBundledAsset> bundledAssets,
        RuntimeParameterSchema parameterSchema,
        RuntimeSiteProfile defaultSiteProfile,
        ProjectGlobalVariableSchema globalVariables,
        CancellationToken cancellationToken)
    {
        var fieldRoot = Path.Combine(packageRoot, "field");
        var drafts = new Dictionary<string, object>
        {
            ["station-profile.json"] = new
            {
                stationId = "",
                lineName = "",
                notes = "Reserved for field deployment. Station MVP may ignore this file."
            },
            ["trigger-profile.json"] = new
            {
                mode = "Manual",
                intervalMs = 0,
                notes = "Reserved for V1.1 trigger integration."
            },
            ["result-mapping-profile.json"] = new
            {
                okCode = "OK",
                ngCode = "NG",
                errorCode = "ERROR",
                notes = "Reserved for V1.1 result writeback mapping."
            },
            ["model-assets.json"] = new RuntimeModelAssetsDraft
            {
                Assets = bundledAssets.ToList()
            },
            ["runtime-parameters.json"] = parameterSchema,
            ["station-profile.default.json"] = defaultSiteProfile,
            ["global-variables.json"] = globalVariables
        };

        foreach (var (fileName, payload) in drafts)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, RuntimeJson.SerializerOptions);
            await File.WriteAllBytesAsync(Path.Combine(fieldRoot, fileName), bytes, cancellationToken);
        }
    }

    private static string BuildReadme(RuntimePackageManifest manifest, RuntimeValidationReport validationReport)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ClearVision Runtime Package");
        builder.AppendLine();
        builder.AppendLine($"- PackageId: `{manifest.PackageId}`");
        builder.AppendLine($"- PackageName: `{manifest.PackageName}`");
        builder.AppendLine($"- RuntimeApiVersion: `{manifest.RuntimeApiVersion}`");
        builder.AppendLine($"- MinStationVersion: `{manifest.MinStationVersion}`");
        builder.AppendLine($"- FlowHash: `{manifest.FlowHash}`");
        builder.AppendLine($"- CreatedAt: `{manifest.CreatedAt:O}`");
        builder.AppendLine($"- CreatedBy: `{manifest.CreatedBy}`");
        builder.AppendLine();
        builder.AppendLine("## Notes");
        foreach (var note in validationReport.Notes)
        {
            builder.AppendLine($"- {note}");
        }

        builder.AppendLine();
        builder.AppendLine("## Field Extensions");
        builder.AppendLine("- `field/` contains Station deployment drafts, runtime parameter schema, and bundled asset metadata.");
        builder.AppendLine("- File-based resources are bundled under `assets/resources/` and referenced by package-relative paths.");
        return builder.ToString();
    }

    private sealed class RuntimeBundledAsset
    {
        public Guid OperatorId { get; init; }

        public string OperatorName { get; init; } = string.Empty;

        public string OperatorType { get; init; } = string.Empty;

        public string ParameterName { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string RelativePath { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public long? LengthBytes { get; init; }

        public string? Sha256 { get; init; }
    }

    private sealed class RuntimeModelAssetsDraft
    {
        public List<RuntimeBundledAsset> Assets { get; init; } = [];

        public string Notes { get; init; } = "Bundled resources required by this runtime package.";
    }

    private sealed record RuntimePackagedProjectAssets(
        RuntimePackageAssets? Manifest,
        IReadOnlyList<RuntimeProjectAssetFile> Files)
    {
        public static RuntimePackagedProjectAssets Empty { get; } = new(null, []);
    }

    private sealed record RuntimeProjectAssetFile(
        string RelativePath,
        byte[] Bytes,
        string FileHash);
}
