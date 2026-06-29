using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Desktop.Configuration;
using ClearVision.Product.Desktop.Data;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Handlers;
using ClearVision.Product.Desktop.Hubs;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Desktop.Triggers;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.Logging;
using ClearVision.Product.Infrastructure.Metrics;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;

namespace ClearVision.Product.Desktop;

static class Program
{
    private const int MinWebPort = 5000;
    private const int MaxWebPort = 5010;
    private static IHost? _host;
    private static int _webPort = 0;
    private static IReadOnlyList<Mutex> _singleInstanceLeases = Array.Empty<Mutex>();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    public static IServiceProvider? ServiceProvider => _host?.Services;

    [STAThread]
    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var assemblyName = new AssemblyName(args.Name).Name;
            var candidatePath = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
            if (File.Exists(candidatePath))
            {
                try
                {
                    return Assembly.LoadFrom(candidatePath);
                }
                catch
                {
                    // Fall back to default resolution.
                }
            }

            return null;
        };

        SetDllDirectory(AppContext.BaseDirectory);

        try
        {
            System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (s, e) =>
            {
                MessageBox.Show(
                    $"UI线程异常:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show(
                    $"未处理异常:\n{ex?.Message}\n\n{ex?.StackTrace}",
                    "严重错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            if (!TryAcquireSingleInstanceMutex(out _singleInstanceLeases))
            {
                MessageBox.Show("ClearVision 已在运行", "ClearVision", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StartWebServer();

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var mainForm = new MainForm();
            System.Windows.Forms.Application.Run(mainForm);

            StopWebServer().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Application Error: {ex}");
        }
        finally
        {
            if (_host != null)
            {
                try
                {
                    StopWebServer().GetAwaiter().GetResult();
                }
                catch (Exception stopEx)
                {
                    Debug.WriteLine($"Stop web server failed: {stopEx}");
                }
            }

            ReleaseSingleInstanceMutex();
        }
    }

    internal static string BuildSingleInstanceMutexName(
        string? userName = null,
        string? dataPath = null,
        string? installPath = null)
    {
        var defaultStorePath = string.IsNullOrWhiteSpace(dataPath)
            ? ConversationalFlowService.ResolveStoragePath()
            : dataPath.Trim();
        return BuildStoreLeaseMutexName("conversation", defaultStorePath, userName);
    }

    internal static string BuildStoreLeaseMutexName(
        string storeKind,
        string storePath,
        string? userName = null)
    {
        var normalizedStorePath = Path.GetFullPath(storePath.Trim());
        var source = normalizedStorePath.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        return $"Global\\ClearVision.Desktop.StoreLease.{hash}";
    }

    private static bool TryAcquireSingleInstanceMutex(out IReadOnlyList<Mutex> mutexes)
    {
        return TryAcquireStoreLeaseMutexes(
            [
                ConversationalFlowService.ResolveStoragePath(),
                AgentRunEventStore.GetDefaultDirectory()
            ],
            out mutexes);
    }

    internal static bool TryAcquireStoreLeaseMutexes(
        IEnumerable<string> storePaths,
        out IReadOnlyList<Mutex> mutexes)
    {
        var leaseNames = storePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => BuildStoreLeaseMutexName("store", path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        var acquired = new List<Mutex>();
        foreach (var leaseName in leaseNames)
        {
            var mutex = new Mutex(initiallyOwned: true, leaseName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                ReleaseSingleInstanceLeases(acquired);
                mutexes = Array.Empty<Mutex>();
                return false;
            }

            acquired.Add(mutex);
        }

        mutexes = acquired;
        return true;
    }

    internal static void ReleaseStoreLeaseMutexes(IEnumerable<Mutex> leases)
    {
        ReleaseSingleInstanceLeases(leases);
    }

    private static void ReleaseSingleInstanceMutex()
    {
        ReleaseSingleInstanceLeases(_singleInstanceLeases);
        _singleInstanceLeases = Array.Empty<Mutex>();
    }

    private static void ReleaseSingleInstanceLeases(IEnumerable<Mutex> leases)
    {
        foreach (var lease in leases)
        {
            try
            {
                lease.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was not owned by this process anymore.
            }
            finally
            {
                lease.Dispose();
            }
        }
    }

    static void StartWebServer()
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddJsonFile(
                StationSettingsPaths.GetStudioCommunicationSettingsPath(),
                optional: true,
                reloadOnChange: false);
            builder.Services.AddSingleton<IValidateOptions<StationIngressOptions>, StationIngressOptionsValidator>();
            builder.Services.AddSingleton<IValidateOptions<AiGenerationOptions>, AiGenerationOptionsValidator>();
            builder.Services.AddOptions<StationIngressOptions>()
                .Bind(builder.Configuration.GetSection(StationIngressOptions.SectionName))
                .ValidateOnStart();
            builder.Services.AddOptions<AiGenerationOptions>()
                .Bind(builder.Configuration.GetSection(AiGenerationOptions.SectionName))
                .ValidateOnStart();
            var stationIngressOptions = builder.Configuration
                .GetSection(StationIngressOptions.SectionName)
                .Get<StationIngressOptions>()
                ?? new StationIngressOptions();
            var ingressListenMode = ResolveStationIngressListenMode(stationIngressOptions);
            _webPort = ResolveWebPort(stationIngressOptions);

            builder.Services.AddVisionServices(builder.Configuration);
            builder.Services.AddAiFlowGeneration(builder.Configuration);
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: "ClearVision.Desktop",
                        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown")
                    .AddAttributes(new[]
                    {
                        new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName),
                        new KeyValuePair<string, object>("runtime.host", "desktop")
                    }))
                .WithMetrics(metrics => metrics
                    .AddMeter(InspectionMetrics.MeterName)
                    .AddConsoleExporter());
            builder.Services.AddScoped<RuntimePackageExporter>();
            builder.Services.AddScoped<RuntimePackageValidator>();
            builder.Services.AddScoped<RuntimePackageLoader>();
            builder.Services.AddSingleton<RuntimeResultNormalizer>();
            builder.Services.AddSingleton<WebMessageHandler>();
            builder.Services.AddSingleton<StationIngressAuthService>();
            builder.Services.AddSingleton<StationCommunicationSettingsStore>();
            builder.Services.AddSingleton<StationRegistryService>();
            builder.Services.AddSingleton<StationCentralStore>();
            builder.Services.AddSingleton<StationPackageStore>();
            builder.Services.AddSingleton<VisionDatabaseMaintenanceService>();
            builder.Services.AddSignalR();
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: true));
            });

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy
                        .SetIsOriginAllowed(origin => IsAllowedApiOrigin(origin, _webPort))
                        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                        .WithHeaders("Authorization", "Content-Type", "Last-Event-ID", "X-Auth-Token", "X-Requested-With", "X-ClearVision-Station-Token");
                });
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                if (ingressListenMode == StationIngressListenMode.Lan)
                {
                    options.ListenAnyIP(_webPort);
                }
                else
                {
                    options.ListenLocalhost(_webPort);
                }
            });

            var app = builder.Build();
            app.UseMiddleware<StationIngressIsolationMiddleware>();

            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                InitializeVisionDatabase(services);
                services.GetRequiredService<ProjectSaveCoordinator>()
                    .RunStartupRecoveryAsync()
                    .GetAwaiter()
                    .GetResult();

                var cameraManager = services.GetRequiredService<ClearVision.Product.Core.Cameras.ICameraManager>();
                var configService = services.GetRequiredService<ClearVision.Product.Core.Interfaces.IConfigurationService>();
                var config = configService.LoadAsync().Result;
                cameraManager.LoadBindings(config.Cameras, config.ActiveCameraId);
                services.GetRequiredService<SerialPhotoelectricTriggerInputService>()
                    .ConfigureBindings(config.Cameras);
            }

            var wwwrootPath = DesktopWebRootResolver.Resolve();
            if (Directory.Exists(wwwrootPath))
            {
                var provider = new PhysicalFileProvider(wwwrootPath);
                app.UseDefaultFiles(new DefaultFilesOptions
                {
                    FileProvider = provider,
                    DefaultFileNames = new List<string> { "index.html" }
                });

                var staticFileOptions = new StaticFileOptions { FileProvider = provider };
#if DEBUG
                staticFileOptions.OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                    context.Context.Response.Headers.Pragma = "no-cache";
                    context.Context.Response.Headers.Expires = "0";
                };
#endif
                app.UseStaticFiles(staticFileOptions);
                Debug.WriteLine($"静态资源目录: {wwwrootPath}");
            }

            app.UseCors();
            app.UseMiddleware<AuthMiddleware>();

            app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Port = _webPort }));
            app.MapHub<StationHub>(StationSyncContractDefaults.HubPath);
            app.MapAuthEndpoints();
            app.MapUserEndpoints();
            app.MapVisionApiEndpoints();
            app.MapSettingsEndpoints();
            app.MapAgentRunEndpoints();
            app.MapPlcEndpoints();
            app.MapStationCommunicationEndpoints();
            app.MapStationEndpoints();
            app.MapDemoEndpoints();
            app.MapAnalysisEndpoints();
            app.MapAutoTuneEndpoints();
            app.MapInspectionEventEndpoints();

            // Start Kestrel before loading the desktop UI so WebView2 does not
            // race the embedded backend on slower industrial PCs.
            app.StartAsync().GetAwaiter().GetResult();
            _host = app;

            Debug.WriteLine($"Web服务器已启动: http://localhost:{_webPort}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动Web服务器失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void InitializeVisionDatabase(IServiceProvider services)
    {
        var dbContext = services.GetRequiredService<ClearVision.Product.Infrastructure.Data.VisionDbContext>();
        var options = new VisionDatabaseInitializationOptions
        {
            OutdatedDatabaseDecisionProvider = PromptForOutdatedDatabaseDecisionAsync
        };

        VisionDatabaseInitializer.InitializeAsync(dbContext, options).GetAwaiter().GetResult();
    }

    private static Task<OutdatedVisionDatabaseDecision> PromptForOutdatedDatabaseDecisionAsync(
        OutdatedVisionDatabase database,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var missingPreview = database.MissingSchemaItems.Count == 0
            ? "未能自动识别缺失项"
            : string.Join(Environment.NewLine, database.MissingSchemaItems.Take(8).Select(item => "- " + item));
        var suffix = database.MissingSchemaItems.Count > 8
            ? $"{Environment.NewLine}- 以及另外 {database.MissingSchemaItems.Count - 8} 项"
            : string.Empty;

        var result = MessageBox.Show(
            "检测到本地数据库版本过旧或结构不兼容，ClearVision 无法自动迁移。" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"数据库：{database.DatabasePath}" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"缺失项：{Environment.NewLine}{missingPreview}{suffix}" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "选择“是”会丢弃旧数据库并创建新的空数据库；选择“否”会保留旧数据库并停止启动。",
            "数据库需要更新",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        return Task.FromResult(result == DialogResult.Yes
            ? OutdatedVisionDatabaseDecision.Discard
            : OutdatedVisionDatabaseDecision.Keep);
    }

    static async Task StopWebServer()
    {
        if (_host != null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _host.StopAsync(cts.Token);
            _host.Dispose();
            _host = null;
        }
    }

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

    static bool IsPortAvailable(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect("127.0.0.1", port);
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static int GetWebPort() => _webPort;

    private static int ResolveWebPort(StationIngressOptions options)
    {
        if (options.Enabled && options.Port > 0)
        {
            return options.Port;
        }

        return FindAvailablePort(MinWebPort, MaxWebPort);
    }

    private static StationIngressListenMode ResolveStationIngressListenMode(StationIngressOptions options)
    {
        if (!options.Enabled)
        {
            return StationIngressListenMode.Loopback;
        }

        if (options.ListenMode == StationIngressListenMode.Lan &&
            string.IsNullOrWhiteSpace(options.SharedToken) &&
            !IsStationIngressInsecureDevelopmentAllowed(options))
        {
            return StationIngressListenMode.Loopback;
        }

        return options.ListenMode;
    }

    private static bool IsStationIngressInsecureDevelopmentAllowed(StationIngressOptions options)
    {
#if DEBUG
        return options.AllowInsecureDevelopment;
#else
        return false;
#endif
    }

    internal static bool IsAllowedApiOrigin(string? origin, int webPort)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        if (originUri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (string.Equals(originUri.Host, "app.local", StringComparison.OrdinalIgnoreCase))
        {
            return originUri.Scheme == Uri.UriSchemeHttp &&
                   (originUri.IsDefaultPort || originUri.Port == 80);
        }

        if (!IsLoopbackHost(originUri.Host))
        {
            return false;
        }

        return originUri.Port == webPort ||
               originUri.Port is >= MinWebPort and <= MaxWebPort;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Net.IPAddress.TryParse(host, out var address) &&
               System.Net.IPAddress.IsLoopback(address);
    }

}
