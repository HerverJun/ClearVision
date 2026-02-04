using Acme.Product.Application.Services;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.Operators;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.Cameras;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Infrastructure.Logging;
using Acme.Product.Infrastructure.Operators;
using Acme.Product.Infrastructure.Repositories;
using Acme.Product.Infrastructure.Services;
using IConfigurationService = Acme.Product.Core.Interfaces.IConfigurationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IImageAcquisitionService = Acme.Product.Application.Services.IImageAcquisitionService;

namespace Acme.Product.Desktop;

/// <summary>
/// 依赖注入配置
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册服务
    /// </summary>
    public static IServiceCollection AddVisionServices(this IServiceCollection services)
    {
        // 配置日志
        var loggerFactory = SerilogConfiguration.ConfigureSerilog();
        services.AddSingleton<ILoggerFactory>(loggerFactory);

        // 数据库 - 使用 Singleton 生命周期以支持其他 Singleton 服务
        services.AddDbContext<VisionDbContext>(options =>
        {
            options.UseSqlite("Data Source=vision.db");
        }, ServiceLifetime.Singleton, ServiceLifetime.Singleton);

        // 仓储 - 全部使用 Singleton 以支持单线程桌面应用
        services.AddSingleton(typeof(IRepository<>), typeof(RepositoryBase<>));
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IOperatorRepository, OperatorRepository>();
        services.AddSingleton<IInspectionResultRepository, InspectionResultRepository>();
        services.AddSingleton<IImageCacheRepository, ImageCacheRepository>();

        // 应用服务
        services.AddSingleton<ProjectService>();
        services.AddSingleton<IInspectionService, InspectionService>();

        // 领域服务
        services.AddSingleton<IFlowExecutionService, FlowExecutionService>();
        services.AddSingleton<IOperatorFactory, OperatorFactory>();

        // 算子执行器
        services.AddSingleton<IOperatorExecutor, ImageAcquisitionOperator>();
        services.AddSingleton<IOperatorExecutor, GaussianBlurOperator>();
        services.AddSingleton<IOperatorExecutor, CannyEdgeOperator>();
        services.AddSingleton<IOperatorExecutor, ThresholdOperator>();
        services.AddSingleton<IOperatorExecutor, MorphologyOperator>();
        services.AddSingleton<IOperatorExecutor, BlobDetectionOperator>();
        services.AddSingleton<IOperatorExecutor, TemplateMatchOperator>();
        services.AddSingleton<IOperatorExecutor, FindContoursOperator>();
        services.AddSingleton<IOperatorExecutor, MeasureDistanceOperator>();

        // 应用服务 - Sprint 4新增
        services.AddSingleton<IOperatorService, OperatorService>();
        services.AddSingleton<IImageAcquisitionService, ImageAcquisitionService>();
        services.AddSingleton<DemoProjectService>();
        services.AddSingleton<IResultAnalysisService, ResultAnalysisService>();

        // 相机
        services.AddSingleton<ICameraManager, CameraManager>();

        // 注册 MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Acme.Product.Application.Commands.Projects.CreateProjectCommand).Assembly));

        // 注册 AutoMapper
        services.AddAutoMapper(typeof(Acme.Product.Application.Commands.Projects.CreateProjectCommand).Assembly);

        // 序列化与导出
        services.AddSingleton<IProjectSerializer, ProjectJsonSerializer>();
        services.AddSingleton<IResultExporter, CsvResultExporter>();

        // 配置服务
        services.AddSingleton<IConfigurationService, JsonConfigurationService>();

        return services;
    }
}

/// <summary>
/// 相机管理器实现
/// </summary>
public class CameraManager : ICameraManager
{
    private readonly Dictionary<string, ICamera> _cameras = new();

    public Task<IEnumerable<CameraInfo>> EnumerateCamerasAsync()
    {
        // 返回模拟相机和文件相机
        var cameras = new List<CameraInfo>
        {
            new() { CameraId = "mock_001", Name = "模拟相机 1", IsConnected = false },
            new() { CameraId = "file_001", Name = "文件相机", IsConnected = false }
        };

        return Task.FromResult<IEnumerable<CameraInfo>>(cameras);
    }

    public Task<ICamera> OpenCameraAsync(string cameraId)
    {
        ICamera camera;

        if (cameraId.StartsWith("mock_"))
        {
            camera = new MockCamera(cameraId, $"模拟相机 {cameraId}");
        }
        else if (cameraId.StartsWith("file_"))
        {
            camera = new FileCamera(cameraId, "文件相机", "sample.jpg");
        }
        else
        {
            throw new ArgumentException($"未知的相机ID: {cameraId}");
        }

        _cameras[cameraId] = camera;
        return Task.FromResult(camera);
    }

    public Task CloseCameraAsync(string cameraId)
    {
        if (_cameras.TryGetValue(cameraId, out var camera))
        {
            camera.Dispose();
            _cameras.Remove(cameraId);
        }

        return Task.CompletedTask;
    }

    public ICamera? GetCamera(string cameraId)
    {
        return _cameras.TryGetValue(cameraId, out var camera) ? camera : null;
    }
}
