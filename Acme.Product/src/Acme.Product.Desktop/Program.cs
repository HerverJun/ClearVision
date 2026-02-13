using Acme.Product.Application.Services;
using Acme.Product.Desktop.Handlers;
using Acme.Product.Infrastructure.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Diagnostics;
using System.Reflection;
using Acme.Product.Desktop.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Acme.Product.Desktop;

static class Program
{
    private static IHost? _host;
    private static int _webPort = 0;

    /// <summary>
    /// 获取服务提供者
    /// </summary>
    public static IServiceProvider? ServiceProvider => _host?.Services;

    /// <summary>
    /// 应用程序的主入口点。
    /// </summary>
    [STAThread]
    static void Main()
    {
        try
        {
            // 添加全局异常处理
            System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (s, e) =>
            {
                MessageBox.Show($"UI线程异常:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show($"未处理异常:\n{ex?.Message}\n\n{ex?.StackTrace}",
                    "严重错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // 启动本地 Web 服务器
            StartWebServer();

            // 配置应用程序
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);

            // 启动主窗体
            var mainForm = new MainForm();
            System.Windows.Forms.Application.Run(mainForm);

            // 关闭 Web 服务器
            StopWebServer().Wait();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup Error: {ex}");
        }
    }

    /// <summary>
    /// 启动本地 Web 服务器
    /// </summary>
    static void StartWebServer()
    {
        try
        {
            // 查找可用端口
            _webPort = FindAvailablePort(5000, 6000);

            var builder = WebApplication.CreateBuilder();

            // 配置服务
            builder.Services.AddVisionServices();
            builder.Services.AddSingleton<WebMessageHandler>();

            // 注册文件存储服务 (Hybrid Persistence Strategy)
            builder.Services.AddSingleton<Acme.Product.Core.Interfaces.IProjectFlowStorage, Acme.Product.Infrastructure.Services.JsonFileProjectFlowStorage>();

            // 配置 JSON 序列化为 camelCase（前端 JavaScript 标准）且枚举序列化为字符串
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });

            // 注册 CORS 服务（必须在 UseCors 之前）
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            // 配置 Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(_webPort);
            });

            var app = builder.Build();

            // 初始化数据库
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<Acme.Product.Infrastructure.Data.VisionDbContext>();
                dbContext.Database.EnsureCreated();
            }

            // 配置静态文件
            var wwwrootPath = GetWwwRootPath();
            if (Directory.Exists(wwwrootPath))
            {
                var provider = new PhysicalFileProvider(wwwrootPath);
                app.UseDefaultFiles(new DefaultFilesOptions
                {
                    FileProvider = provider,
                    DefaultFileNames = new List<string> { "index.html" }
                });
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = provider
                });
            }

            // 配置 CORS
            app.UseCors();

            // 健康检查
            app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Port = _webPort }));

            // 演示工程API - Sprint 4新增
            app.MapPost("/api/demo/create", async (DemoProjectService demoService) =>
            {
                try
                {
                    var project = await demoService.CreateDemoProjectAsync();
                    return Results.Ok(project);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"创建演示工程失败: {ex.Message}");
                }
            });

            app.MapPost("/api/demo/create-simple", async (DemoProjectService demoService) =>
            {
                try
                {
                    var project = await demoService.CreateSimpleDemoProjectAsync();
                    return Results.Ok(project);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"创建简单演示工程失败: {ex.Message}");
                }
            });

            app.MapGet("/api/demo/guide", (DemoProjectService demoService) =>
            {
                var guide = demoService.GetDemoGuide();
                return Results.Ok(guide);
            });

            // 注册核心业务端点 (Projects, Inspection, Operators, Images)
            app.MapVisionApiEndpoints();

            // 设置功能端点
            app.MapSettingsEndpoints();

            // 结果分析API - S4-008新增
            app.MapGet("/api/analysis/statistics/{projectId}", async (
                Guid projectId,
                DateTime? startTime,
                DateTime? endTime,
                IResultAnalysisService analysisService) =>
            {
                var statistics = await analysisService.GetStatisticsAsync(projectId, startTime, endTime);
                return Results.Ok(statistics);
            });

            app.MapGet("/api/analysis/defect-distribution/{projectId}", async (
                Guid projectId,
                DateTime? startTime,
                DateTime? endTime,
                IResultAnalysisService analysisService) =>
            {
                var distribution = await analysisService.GetDefectDistributionAsync(projectId, startTime, endTime);
                return Results.Ok(distribution);
            });

            app.MapGet("/api/analysis/confidence-distribution/{projectId}", async (
                Guid projectId,
                DateTime? startTime,
                DateTime? endTime,
                IResultAnalysisService analysisService) =>
            {
                var distribution = await analysisService.GetConfidenceDistributionAsync(projectId, startTime, endTime);
                return Results.Ok(distribution);
            });

            app.MapGet("/api/analysis/trend/{projectId}", async (
                Guid projectId,
                string interval,
                DateTime startTime,
                DateTime endTime,
                IResultAnalysisService analysisService) =>
            {
                if (!Enum.TryParse<TrendInterval>(interval, true, out var trendInterval))
                {
                    return Results.BadRequest($"无效的时间间隔: {interval}. 支持: Hour, Day, Week, Month");
                }
                var trend = await analysisService.GetTrendAnalysisAsync(projectId, trendInterval, startTime, endTime);
                return Results.Ok(trend);
            });

            app.MapGet("/api/analysis/export/csv/{projectId}", async (
                Guid projectId,
                DateTime? startTime,
                DateTime? endTime,
                IResultAnalysisService analysisService) =>
            {
                var csv = await analysisService.ExportToCsvAsync(projectId, startTime, endTime);
                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(csv),
                    "text/csv",
                    $"inspection_results_{projectId}_{DateTime.UtcNow:yyyyMMdd}.csv");
            });

            app.MapGet("/api/analysis/export/json/{projectId}", async (
                Guid projectId,
                DateTime? startTime,
                DateTime? endTime,
                IResultAnalysisService analysisService) =>
            {
                var json = await analysisService.ExportToJsonAsync(projectId, startTime, endTime);
                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(json),
                    "application/json",
                    $"inspection_results_{projectId}_{DateTime.UtcNow:yyyyMMdd}.json");
            });

            app.MapGet("/api/analysis/report/{projectId}", async (
                Guid projectId,
                DateTime? startTime,
                DateTime? endTime,
                IResultAnalysisService analysisService) =>
            {
                var report = await analysisService.GenerateReportAsync(projectId, startTime, endTime);
                return Results.Ok(report);
            });

            app.MapGet("/api/analysis/compare/{projectId}", async (
                Guid projectId,
                DateTime period1Start,
                DateTime period1End,
                DateTime period2Start,
                DateTime period2End,
                IResultAnalysisService analysisService) =>
            {
                var comparison = await analysisService.ComparePeriodsAsync(
                    projectId, period1Start, period1End, period2Start, period2End);
                return Results.Ok(comparison);
            });

            app.MapGet("/api/analysis/heatmap/{projectId}", async (
                Guid projectId,
                DateTime? startTime,
                DateTime? endTime,
                IResultAnalysisService analysisService) =>
            {
                var heatmap = await analysisService.GetDefectHeatmapAsync(projectId, startTime, endTime);
                return Results.Ok(heatmap);
            });

            _host = app;

            // 在后台启动服务器
            Task.Run(async () =>
            {
                try
                {
                    await app.RunAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Web服务器错误: {ex.Message}");
                }
            });

            Debug.WriteLine($"Web服务器已启动: http://localhost:{_webPort}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动Web服务器失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 停止 Web 服务器
    /// </summary>
    static async Task StopWebServer()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    /// <summary>
    /// 查找可用端口
    /// </summary>
    static int FindAvailablePort(int startPort, int endPort)
    {
        for (int port = startPort; port <= endPort; port++)
        {
            if (IsPortAvailable(port))
            {
                return port;
            }
        }
        throw new Exception("未找到可用端口");
    }

    /// <summary>
    /// 检查端口是否可用
    /// </summary>
    static bool IsPortAvailable(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect("127.0.0.1", port);
            return false; // 端口被占用
        }
        catch
        {
            return true; // 端口可用
        }
    }

    /// <summary>
    /// 获取 wwwroot 路径
    /// </summary>
    static string GetWwwRootPath()
    {
        // 优先使用 AppContext.BaseDirectory，它在单文件发布模式下指向解压后的临时目录或exe所在目录
        var basePath = AppContext.BaseDirectory;

        // 开发环境检查：尝试向上查找项目源码目录
        var devPath = Path.Combine(basePath, "..", "..", "..", "wwwroot");
        if (Directory.Exists(devPath))
        {
            return Path.GetFullPath(devPath);
        }

        // 生产环境：使用执行目录下的 wwwroot
        var prodPath = Path.Combine(basePath, "wwwroot");

        // 确保返回绝对路径 (即使 Path.Combine 已经生成了绝对路径，GetFullPath 也是个双重保险)
        return Path.GetFullPath(prodPath);
    }

    /// <summary>
    /// 获取 Web 服务器端口
    /// </summary>
    public static int GetWebPort() => _webPort;
}
