using System;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Cameras;
using ClearVision.Product.Infrastructure.Communication.Gr;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Events;
using ClearVision.Product.Infrastructure.Logging;
using ClearVision.Product.Infrastructure.Metrics;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Repositories;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using IConfigurationService = ClearVision.Product.Core.Interfaces.IConfigurationService;
using IImageAcquisitionService = ClearVision.Product.Application.Services.IImageAcquisitionService;

namespace ClearVision.Product.Infrastructure.DependencyInjection;

public static class VisionRuntimeServiceCollectionExtensions
{
    public const string DatabasePathConfigKey = "Database:Path";

    /// <summary>
    /// 注册共享的 Runtime 核心服务（FlowExecutionService、算子执行器、Camera 等）。
    /// Station 和 Desktop 都应调用此方法获取执行核心。
    /// </summary>
    public static IServiceCollection AddVisionRuntimeCoreServices(this IServiceCollection services)
    {
        services.AddScoped<FlowExecutionService>();
        services.AddScoped<IFlowExecutionEngine>(provider => provider.GetRequiredService<FlowExecutionService>());
        services.AddScoped<GovernedFlowExecutionService>();
        services.AddScoped<IFlowExecutionService>(provider => provider.GetRequiredService<GovernedFlowExecutionService>());
        services.AddScoped<IFlowDefinitionValidator>(provider => provider.GetRequiredService<GovernedFlowExecutionService>());
        services.AddScoped<IExecutionAdmissionService, ExecutionAdmissionService>();
        services.AddSingleton<IOperatorFactory, OperatorFactory>();
        services.AddSingleton<ParameterRecommender>();
        services.AddSingleton<ITcpDeviceManager>(sp => new TcpDeviceManager(
            sp.GetService<IConfigurationService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TcpDeviceManager>>()));
        services.AddScoped<OperatorPreviewService>();

        services.AddSingleton<IOperatorExecutor, ImageAcquisitionOperator>();
        services.AddSingleton<IOperatorExecutor, GaussianBlurOperator>();
        services.AddSingleton<IOperatorExecutor, CannyEdgeOperator>();
        services.AddSingleton<IOperatorExecutor, ThresholdOperator>();
        services.AddSingleton<IOperatorExecutor, MorphologyOperator>();
        services.AddSingleton<IOperatorExecutor, BlobDetectionOperator>();
        services.AddSingleton<IOperatorExecutor, TemplateMatchOperator>();
        services.AddSingleton<IOperatorExecutor, FindContoursOperator>();
        services.AddSingleton<IOperatorExecutor, MeasureDistanceOperator>();
        services.AddSingleton<IOperatorExecutor, ResultOutputOperator>();
        services.AddSingleton<IOperatorExecutor, DeepLearningOperator>();
        services.AddSingleton<IOperatorExecutor, MedianBlurOperator>();
        services.AddSingleton<IOperatorExecutor, BilateralFilterOperator>();
        services.AddSingleton<IOperatorExecutor, MeanFilterOperator>();
        services.AddSingleton<IOperatorExecutor, ImageResizeOperator>();
        services.AddSingleton<IOperatorExecutor, ImageCropOperator>();
        services.AddSingleton<IOperatorExecutor, ImageRotateOperator>();
        services.AddSingleton<IOperatorExecutor, PerspectiveTransformOperator>();
        services.AddSingleton<IOperatorExecutor, CodeRecognitionOperator>();
        services.AddSingleton<IOperatorExecutor, CircleMeasurementOperator>();
        services.AddSingleton<IOperatorExecutor, LineMeasurementOperator>();
        services.AddSingleton<IOperatorExecutor, ContourMeasurementOperator>();
        services.AddSingleton<IOperatorExecutor, AngleMeasurementOperator>();
        services.AddSingleton<IOperatorExecutor, GeometricToleranceOperator>();
        services.AddSingleton<IOperatorExecutor, CameraCalibrationOperator>();
        services.AddSingleton<IOperatorExecutor, UndistortOperator>();
        services.AddSingleton<IOperatorExecutor, CoordinateTransformOperator>();
        services.AddSingleton<GrRegisterMapCatalog>();
        services.AddSingleton<GrStateDecoder>();
        services.AddSingleton<JsonCommunicationProfileStore>();
        services.AddSingleton<IOperatorExecutor, ModbusCommunicationOperator>();
        services.AddSingleton<ModbusCommunicationOperator>(provider => provider
            .GetServices<IOperatorExecutor>()
            .OfType<ModbusCommunicationOperator>()
            .Single());
        services.AddSingleton<IOperatorExecutor, TcpCommunicationOperator>();
        services.AddSingleton<IOperatorExecutor, DatabaseWriteOperator>();
        services.AddSingleton<IOperatorExecutor, ConditionalBranchOperator>();
        services.AddSingleton<IOperatorExecutor, SiemensS7CommunicationOperator>();
        services.AddSingleton<IOperatorExecutor, MitsubishiMcCommunicationOperator>();
        services.AddSingleton<IOperatorExecutor, OmronFinsCommunicationOperator>();
        services.AddSingleton<IOperatorExecutor, ResultJudgmentOperator>();
        services.AddSingleton<IOperatorExecutor, DetectionSequenceJudgeOperator>();
        services.AddSingleton<IOperatorExecutor, ClaheEnhancementOperator>();
        services.AddSingleton<IOperatorExecutor, MorphologicalOperationOperator>();
        services.AddSingleton<IOperatorExecutor, LaplacianSharpenOperator>();
        services.AddSingleton<IOperatorExecutor, ImageAddOperator>();
        services.AddSingleton<IOperatorExecutor, ImageSubtractOperator>();
        services.AddSingleton<IOperatorExecutor, ImageBlendOperator>();
        services.AddSingleton<IVariableContext, VariableContext>();
        services.AddSingleton<IProjectVariableExecutionContextAccessor, ProjectVariableExecutionContextAccessor>();
        services.AddSingleton<IOperatorExecutor, VariableReadOperator>();
        services.AddSingleton<IOperatorExecutor, VariableWriteOperator>();
        services.AddSingleton<IOperatorExecutor, VariableIncrementOperator>();
        services.AddSingleton<IOperatorExecutor, CycleCounterOperator>();
        services.AddSingleton<IOperatorExecutor, TryCatchOperator>();
        services.AddSingleton<IOperatorExecutor, ColorConversionOperator>();
        services.AddSingleton<IOperatorExecutor, AdaptiveThresholdOperator>();
        services.AddSingleton<IOperatorExecutor, HistogramEqualizationOperator>();
        services.AddSingleton<IOperatorExecutor, GeometricFittingOperator>();
        services.AddSingleton<IOperatorExecutor, RoiManagerOperator>();
        services.AddSingleton<IOperatorExecutor, ShapeMatchingOperator>();
        services.AddSingleton<IOperatorExecutor, SubpixelEdgeDetectionOperator>();
        services.AddSingleton<IOperatorExecutor, ColorDetectionOperator>();
        services.AddSingleton<IOperatorExecutor, SerialCommunicationOperator>();
        services.AddSingleton<IOperatorExecutor, AkazeFeatureMatchOperator>();
        services.AddSingleton<IOperatorExecutor, OrbFeatureMatchOperator>();
        services.AddSingleton<IOperatorExecutor, GradientShapeMatchOperator>();
        services.AddSingleton<IOperatorExecutor, PyramidShapeMatchOperator>();
        services.AddSingleton<IOperatorExecutor, DualModalVotingOperator>();
        services.AddSingleton<IOperatorExecutor, ForEachOperator>();
        services.AddSingleton<IOperatorExecutor, ArrayIndexerOperator>();
        services.AddSingleton<IOperatorExecutor, JsonExtractorOperator>();
        services.AddSingleton<IOperatorExecutor, MathOperationOperator>();
        services.AddSingleton<IOperatorExecutor, LogicGateOperator>();
        services.AddSingleton<IOperatorExecutor, TypeConvertOperator>();
        services.AddSingleton<IOperatorExecutor, HttpRequestOperator>();
        services.AddSingleton<IOperatorExecutor, MqttPublishOperator>();
        services.AddSingleton<IOperatorExecutor, StringFormatOperator>();
        services.AddSingleton<IOperatorExecutor, ImageSaveOperator>();
        services.AddSingleton<OcrEngineProvider>();
        services.AddSingleton<IOperatorExecutor, OcrRecognitionOperator>();
        services.AddSingleton<IOperatorExecutor, ImageDiffOperator>();
        services.AddSingleton<IOperatorExecutor, StatisticsOperator>();
        services.AddSingleton<IOperatorExecutor, CaliperToolOperator>();
        services.AddSingleton<IOperatorExecutor, WidthMeasurementOperator>();
        services.AddSingleton<IOperatorExecutor, PointLineDistanceOperator>();
        services.AddSingleton<IOperatorExecutor, LineLineDistanceOperator>();
        services.AddSingleton<IOperatorExecutor, BoxNmsOperator>();
        services.AddSingleton<IOperatorExecutor, BoundingBoxFilterOperator>();
        services.AddSingleton<IOperatorExecutor, SharpnessEvaluationOperator>();
        services.AddSingleton<IOperatorExecutor, PositionCorrectionOperator>();
        services.AddSingleton<IOperatorExecutor, NPointCalibrationOperator>();
        services.AddSingleton<IOperatorExecutor, CalibrationLoaderOperator>();
        services.AddSingleton<IOperatorExecutor, PixelToWorldTransformOperator>();
        services.AddSingleton<IOperatorExecutor, UnitConvertOperator>();
        services.AddSingleton<IOperatorExecutor, TimerStatisticsOperator>();
        services.AddSingleton<IOperatorExecutor, ScriptOperator>();
        services.AddSingleton<IOperatorExecutor, TriggerModuleOperator>();
        services.AddSingleton<IOperatorExecutor, FrameChangeTriggerOperator>();
        services.AddSingleton<IOperatorExecutor, PointAlignmentOperator>();
        services.AddSingleton<IOperatorExecutor, PointCorrectionOperator>();
        services.AddSingleton<IOperatorExecutor, GapMeasurementOperator>();
        services.AddSingleton<IOperatorExecutor, PolarUnwrapOperator>();
        services.AddSingleton<IOperatorExecutor, ShadingCorrectionOperator>();
        services.AddSingleton<IOperatorExecutor, FrameAveragingOperator>();
        services.AddSingleton<IOperatorExecutor, AffineTransformOperator>();
        services.AddSingleton<IOperatorExecutor, ColorMeasurementOperator>();
        services.AddSingleton<IOperatorExecutor, SurfaceDefectDetectionOperator>();
        services.AddSingleton<IOperatorExecutor, EdgePairDefectOperator>();
        services.AddSingleton<IOperatorExecutor, RectangleDetectionOperator>();
        services.AddSingleton<IOperatorExecutor, TranslationRotationCalibrationOperator>();
        services.AddSingleton<IOperatorExecutor, CornerDetectionOperator>();
        services.AddSingleton<IOperatorExecutor, EdgeIntersectionOperator>();
        services.AddSingleton<IOperatorExecutor, ParallelLineFindOperator>();
        services.AddSingleton<IOperatorExecutor, QuadrilateralFindOperator>();
        services.AddSingleton<IOperatorExecutor, GeoMeasurementOperator>();
        services.AddSingleton<IOperatorExecutor, ImageStitchingOperator>();
        services.AddSingleton<IOperatorExecutor, ImageTilingOperator>();
        services.AddSingleton<IOperatorExecutor, ImageNormalizeOperator>();
        services.AddSingleton<IOperatorExecutor, ImageComposeOperator>();
        services.AddSingleton<IOperatorExecutor, CopyMakeBorderOperator>();
        services.AddSingleton<IOperatorExecutor, TextSaveOperator>();
        services.AddSingleton<IOperatorExecutor, PointSetToolOperator>();
        services.AddSingleton<IOperatorExecutor, BlobLabelingOperator>();
        services.AddSingleton<IOperatorExecutor, HistogramAnalysisOperator>();
        services.AddSingleton<IOperatorExecutor, PixelStatisticsOperator>();
        services.AddSingleton<IOperatorExecutor, VoxelDownsampleOperator>();
        services.AddSingleton<IOperatorExecutor, StatisticalOutlierRemovalOperator>();
        services.AddSingleton<IOperatorExecutor, RansacPlaneSegmentationOperator>();
        services.AddSingleton<IOperatorExecutor, EuclideanClusterExtractionOperator>();
        services.AddSingleton<IOperatorExecutor, RectangleRegionOperator>();
        services.AddSingleton<IOperatorExecutor, PPFEstimationOperator>();
        services.AddSingleton<IOperatorExecutor, PPFMatchOperator>();
        services.AddSingleton<IOperatorExecutor, LawsTextureFilterOperator>();
        services.AddSingleton<IOperatorExecutor, GlcmTextureOperator>();
        services.AddSingleton<IOperatorExecutor, SemanticSegmentationOperator>();
        RegisterAllOperatorExecutors(services);

        services.AddSingleton<FlowLinter>();
        services.TryAddSingleton<ISerialPhotoelectricTriggerInputService>(NoOpSerialPhotoelectricTriggerInputService.Instance);
        services.AddSingleton<ICameraManager, CameraManager>();
        services.AddSingleton<ICameraFrameStreamCoordinator, CameraFrameStreamCoordinator>();

        return services;
    }

    private static void RegisterAllOperatorExecutors(IServiceCollection services)
    {
        var rootNamespace = typeof(ImageAcquisitionOperator).Namespace!;
        var executorTypes = typeof(ImageAcquisitionOperator).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                typeof(IOperatorExecutor).IsAssignableFrom(type) &&
                type.Namespace != null &&
                (string.Equals(type.Namespace, rootNamespace, StringComparison.Ordinal) ||
                    type.Namespace.StartsWith(rootNamespace + ".", StringComparison.Ordinal)))
            .OrderBy(type => type.Name);

        foreach (var executorType in executorTypes)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IOperatorExecutor), executorType));
        }
    }

    /// <summary>
    /// 注册完整的 Studio / Desktop 服务栈。包含 Runtime 核心 + 数据库 + 仓储 + HostedService + Auth 等。
    /// 仅供 ClearVision.Product.Desktop 调用。
    /// </summary>
    public static IServiceCollection AddVisionRuntimeServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var loggerFactory = SerilogConfiguration.ConfigureSerilog();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(loggerFactory);

        var databasePath = ResolveVisionDatabasePath(
            configuration?[DatabasePathConfigKey],
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        services.AddDbContext<VisionDbContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath}");
        });

        services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IOperatorRepository, OperatorRepository>();
        services.AddScoped<IInspectionResultRepository, InspectionResultRepository>();
        var imageCacheQueueCapacity = ResolveConfiguredInt(
            configuration?["Performance:Persistence:ImageCacheQueueCapacity"],
            "Performance__Persistence__ImageCacheQueueCapacity",
            "CV_IMAGE_CACHE_QUEUE_CAPACITY",
            fallback: 512,
            min: 1,
            max: 100_000);
        services.AddSingleton(_ => new LruImageCacheRepository(queueCapacity: imageCacheQueueCapacity));
        services.AddSingleton<IImageCacheRepository>(sp => sp.GetRequiredService<LruImageCacheRepository>());
        services.AddHostedService(sp => sp.GetRequiredService<LruImageCacheRepository>());
        services.AddSingleton<IProjectFlowStorage, JsonFileProjectFlowStorage>();
        services.AddSingleton<IProjectAssetStorage, JsonFileProjectAssetStorage>();
        services.AddSingleton<IProjectVariableStateStore, JsonFileProjectVariableStateStore>();

        services.AddSingleton<InspectionResultBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<InspectionResultBackgroundService>());
        services.AddSingleton<IInspectionResultChannelWriter>(sp => sp.GetRequiredService<InspectionResultBackgroundService>());
        services.AddSingleton<InspectionImagePersistenceService>();
        services.AddSingleton<QueuedInspectionImagePersistenceService>();
        services.AddHostedService(sp => sp.GetRequiredService<QueuedInspectionImagePersistenceService>());
        services.AddSingleton<IInspectionImagePersistenceService>(sp => sp.GetRequiredService<QueuedInspectionImagePersistenceService>());

        services.AddSingleton<ProjectVariableSessionRegistry>();
        services.AddScoped<ProjectSaveCoordinator>();
        services.AddHostedService<ProjectSaveRecoveryHostedService>();

        services.AddSingleton<IInspectionRuntimeCoordinator, InspectionRuntimeCoordinator>();
        services.AddSingleton<InspectionWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<InspectionWorker>());
        services.AddSingleton<IInspectionWorker>(sp => sp.GetRequiredService<InspectionWorker>());

        services.AddSingleton<InspectionMetrics>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        services.AddSingleton<IInspectionEventBus, InMemoryInspectionEventBus>();
        services.AddSingleton<SseHeartbeatService>();
        services.AddHostedService(sp => sp.GetRequiredService<SseHeartbeatService>());

        services.AddScoped<IPreviewMetricsAnalyzer, PreviewMetricsAnalyzer>();
        services.AddScoped<IFlowNodePreviewService, FlowNodePreviewService>();
        services.AddScoped<IAutoTuneService, AutoTuneService>();

        services.AddScoped<ProjectService>();
        services.AddSingleton<IAnalysisDataBuilder, AnalysisDataBuilder>();
        services.AddScoped<IInspectionEvidenceManifestService, InspectionEvidenceManifestService>();
        services.AddScoped<IInspectionService, InspectionService>();

        // 共享 Runtime 核心（算子注册、FlowExecutionService 等）
        services.AddVisionRuntimeCoreServices();

        services.AddScoped<ClearVision.Product.Infrastructure.AI.DryRun.DryRunService>();

        services.AddScoped<ClearVision.Product.Infrastructure.AI.PromptBuilder>();
#pragma warning disable CS0618
        services.AddScoped<ClearVision.Product.Infrastructure.AI.AIGeneratedFlowParser>();
        services.AddSingleton<ClearVision.Product.Infrastructure.AI.DryRun.DryRunStubRegistry>();
        services.AddSingleton<ClearVision.Product.Infrastructure.AI.DryRun.StubRegistryBuilder>();
        services.AddScoped<ClearVision.Product.Infrastructure.AI.AIWorkflowService>();
#pragma warning restore CS0618

        services.AddScoped<IOperatorService, OperatorService>();
        services.AddScoped<IImageAcquisitionService, ImageAcquisitionService>();
        services.AddScoped<IPlanarScaleOffsetCalibrationService, PlanarScaleOffsetCalibrationService>();
        services.AddScoped<DemoProjectService>();
        services.AddScoped<IResultAnalysisService, ResultAnalysisService>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ClearVision.Product.Application.Commands.Projects.CreateProjectCommand).Assembly));
        services.AddAutoMapper(
            _ => { },
            typeof(ClearVision.Product.Application.Commands.Projects.CreateProjectCommand).Assembly);

        services.AddSingleton<IProjectSerializer, ProjectJsonSerializer>();
        services.AddSingleton<IResultExporter, CsvResultExporter>();
        services.AddSingleton<IConfigurationService, JsonConfigurationService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<UserManagementService>();

        return services;
    }

    public static string ResolveVisionDatabasePath(
        string? configuredPath,
        string baseDirectory,
        string currentDirectory,
        string localApplicationDataRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var overridePath = NormalizeDatabasePath(configuredPath, baseDirectory);
            EnsureParentDirectory(overridePath);
            return overridePath;
        }

        var defaultPath = Path.GetFullPath(Path.Combine(localApplicationDataRoot, "ClearVision", "vision.db"));
        MigrateLegacyDatabaseIfNeeded(defaultPath, baseDirectory, currentDirectory);
        EnsureParentDirectory(defaultPath);
        return defaultPath;
    }

    public static string NormalizeDatabasePath(string configuredPath, string baseDirectory)
    {
        var trimmedPath = configuredPath.Trim();
        var combinedPath = Path.IsPathRooted(trimmedPath)
            ? trimmedPath
            : Path.Combine(baseDirectory, trimmedPath);

        return Path.GetFullPath(combinedPath);
    }

    public static void MigrateLegacyDatabaseIfNeeded(
        string targetPath,
        string baseDirectory,
        string currentDirectory)
    {
        if (File.Exists(targetPath))
        {
            return;
        }

        var candidates = new[]
            {
                Path.Combine(baseDirectory, "vision.db"),
                Path.Combine(currentDirectory, "vision.db")
            }
            .Select(Path.GetFullPath)
            .Where(path => !string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Length)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        EnsureParentDirectory(targetPath);
        File.Copy(candidates[0].FullName, targetPath);
    }

    private static void EnsureParentDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static int ResolveConfiguredInt(
        string? configuredValue,
        string environmentKey,
        string fallbackEnvironmentKey,
        int fallback,
        int min,
        int max)
    {
        var raw = configuredValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Environment.GetEnvironmentVariable(environmentKey);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Environment.GetEnvironmentVariable(fallbackEnvironmentKey);
        }

        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }
}
