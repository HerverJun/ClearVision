using Acme.Product.Application.Services;
using Acme.Product.Desktop.Endpoints;
using Acme.Product.Desktop.Handlers;
using Acme.Product.Desktop.Hubs;
using Acme.Product.Desktop.Middleware;
using Acme.Product.Desktop.Station;
using Acme.Product.Infrastructure.AI;
using Acme.Product.Infrastructure.Logging;
using Acme.Product.Infrastructure.Services;
using Acme.Product.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;

namespace Acme.Product.Desktop;

static class Program
{
    private const int MinWebPort = 5000;
    private const int MaxWebPort = 5010;
    private static IHost? _host;
    private static int _webPort = 0;

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
    }

    static void StartWebServer()
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.Configure<StationIngressOptions>(builder.Configuration.GetSection(StationIngressOptions.SectionName));
            var stationIngressOptions = builder.Configuration
                .GetSection(StationIngressOptions.SectionName)
                .Get<StationIngressOptions>()
                ?? new StationIngressOptions();
            var ingressListenMode = ResolveStationIngressListenMode(stationIngressOptions);
            _webPort = ResolveWebPort(stationIngressOptions);

            builder.Services.AddVisionServices(builder.Configuration);
            builder.Services.AddAiFlowGeneration(builder.Configuration);
            builder.Services.AddScoped<RuntimePackageExporter>();
            builder.Services.AddScoped<RuntimePackageValidator>();
            builder.Services.AddScoped<RuntimePackageLoader>();
            builder.Services.AddSingleton<RuntimeResultNormalizer>();
            builder.Services.AddSingleton<WebMessageHandler>();
            builder.Services.AddSingleton<StationIngressAuthService>();
            builder.Services.AddSingleton<StationRegistryService>();
            builder.Services.AddSingleton<StationCentralStore>();
            builder.Services.AddSingleton<StationPackageStore>();
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
                var dbContext = services.GetRequiredService<Acme.Product.Infrastructure.Data.VisionDbContext>();
                if (dbContext.Database.GetMigrations().Any())
                {
                    dbContext.Database.Migrate();
                }
                else
                {
                    dbContext.Database.EnsureCreated();
                }

                if (dbContext.Database.IsSqlite())
                {
                    dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                    dbContext.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
                }

                EnsureInspectionResultAnalysisDataColumnAsync(dbContext).GetAwaiter().GetResult();
                EnsureStationSyncSchemaAsync(dbContext).GetAwaiter().GetResult();

                var cameraManager = services.GetRequiredService<Acme.Product.Core.Cameras.ICameraManager>();
                var configService = services.GetRequiredService<Acme.Product.Core.Interfaces.IConfigurationService>();
                var config = configService.LoadAsync().Result;
                cameraManager.LoadBindings(config.Cameras, config.ActiveCameraId);
            }

            var wwwrootPath = GetWwwRootPath();
            if (Directory.Exists(wwwrootPath))
            {
                var provider = new PhysicalFileProvider(wwwrootPath);
                app.UseDefaultFiles(new DefaultFilesOptions
                {
                    FileProvider = provider,
                    DefaultFileNames = new List<string> { "index.html" }
                });
                app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
            }

            app.UseCors();
            app.UseMiddleware<AuthMiddleware>();

            app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Port = _webPort }));
            app.MapHub<StationHub>("/hubs/station-ingest");
            app.MapAuthEndpoints();
            app.MapUserEndpoints();
            app.MapVisionApiEndpoints();
            app.MapSettingsEndpoints();
            app.MapPlcEndpoints();
            app.MapStationEndpoints();

            RegisterExtendedApiEndpoints(app);
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

    private static void RegisterExtendedApiEndpoints(WebApplication app)
    {
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
            return Results.Ok(demoService.GetDemoGuide());
        });

        app.MapGet("/api/analysis/statistics/{projectId}", async (
            Guid projectId,
            DateTime? startTime,
            DateTime? endTime,
            string? status,
            string? defectType,
            Acme.Product.Application.Services.IResultAnalysisService analysisService) =>
        {
            return Results.Ok(await analysisService.GetStatisticsAsync(projectId, startTime, endTime, status, defectType));
        });

        app.MapGet("/api/analysis/defect-distribution/{projectId}", async (
            Guid projectId,
            DateTime? startTime,
            DateTime? endTime,
            string? status,
            string? defectType,
            Acme.Product.Application.Services.IResultAnalysisService analysisService) =>
        {
            return Results.Ok(await analysisService.GetDefectDistributionAsync(projectId, startTime, endTime, status, defectType));
        });

        app.MapGet("/api/analysis/trend/{projectId}", async (
            Guid projectId,
            string interval,
            DateTime startTime,
            DateTime endTime,
            string? status,
            string? defectType,
            Acme.Product.Application.Services.IResultAnalysisService analysisService) =>
        {
            if (!Enum.TryParse<Acme.Product.Application.Services.TrendInterval>(interval, true, out var trendInterval))
            {
                return Results.BadRequest($"无效间隔: {interval}");
            }

            return Results.Ok(await analysisService.GetTrendAnalysisAsync(projectId, trendInterval, startTime, endTime, status, defectType));
        });

        app.MapGet("/api/analysis/report/{projectId}", async (
            Guid projectId,
            DateTime? startTime,
            DateTime? endTime,
            string? status,
            string? defectType,
            Acme.Product.Application.Services.IResultAnalysisService analysisService) =>
        {
            return Results.Ok(await analysisService.GenerateReportAsync(projectId, startTime, endTime, status, defectType));
        });
    }

    static async Task StopWebServer()
    {
        if (_host != null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _host.StopAsync(cts.Token);
            _host.Dispose();
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

    static string GetWwwRootPath()
    {
        var basePath = AppContext.BaseDirectory;
        var devPath = Path.Combine(basePath, "..", "..", "..", "wwwroot");
        if (Directory.Exists(devPath))
        {
            return Path.GetFullPath(devPath);
        }

        return Path.GetFullPath(Path.Combine(basePath, "wwwroot"));
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
            !options.AllowInsecureDevelopment)
        {
            return StationIngressListenMode.Loopback;
        }

        return options.ListenMode;
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

    private static async Task EnsureInspectionResultAnalysisDataColumnAsync(Acme.Product.Infrastructure.Data.VisionDbContext dbContext)
    {
        await EnsureTextColumnExistsAsync(dbContext, "InspectionResults", "AnalysisDataJson");
    }

    private static async Task EnsureStationSyncSchemaAsync(Acme.Product.Infrastructure.Data.VisionDbContext dbContext)
    {
        var statements = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS "StationNodes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationNodes" PRIMARY KEY AUTOINCREMENT,
                "StationId" TEXT NOT NULL,
                "StationName" TEXT NOT NULL DEFAULT '',
                "LineName" TEXT NULL,
                "AreaName" TEXT NULL,
                "WorkcellName" TEXT NULL,
                "InspectionNodeName" TEXT NULL,
                "CameraAlias" TEXT NULL,
                "StationRole" TEXT NOT NULL DEFAULT '',
                "Owner" TEXT NULL,
                "MachineName" TEXT NOT NULL DEFAULT '',
                "IpAddressHint" TEXT NULL,
                "MacAddressHash" TEXT NULL,
                "FirstSeenAtUtc" TEXT NOT NULL,
                "LastSeenAtUtc" TEXT NOT NULL,
                "LastHeartbeatAtUtc" TEXT NULL,
                "OnlineState" TEXT NOT NULL DEFAULT 'Unknown',
                "RuntimeState" TEXT NOT NULL DEFAULT 'Unknown',
                "CurrentPackageId" TEXT NULL,
                "CurrentPackageName" TEXT NULL,
                "CurrentPackageVersion" TEXT NULL,
                "IsEnabled" INTEGER NOT NULL DEFAULT 1,
                "Remark" TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationResultSummaries" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationResultSummaries" PRIMARY KEY AUTOINCREMENT,
                "StationId" TEXT NOT NULL,
                "SequenceId" INTEGER NOT NULL,
                "MessageId" TEXT NOT NULL,
                "RunId" TEXT NOT NULL,
                "PackageId" TEXT NOT NULL,
                "PackageName" TEXT NOT NULL,
                "PackageVersion" TEXT NOT NULL,
                "FlowHash" TEXT NOT NULL,
                "ImageId" TEXT NOT NULL,
                "Outcome" TEXT NOT NULL,
                "InspectionStatus" TEXT NULL,
                "ExecutionTimeMs" INTEGER NOT NULL,
                "DiagnosticCode" TEXT NOT NULL,
                "DiagnosticMessage" TEXT NULL,
                "PrimaryOutputsPreviewJson" TEXT NOT NULL,
                "StartedAtUtc" TEXT NOT NULL,
                "CompletedAtUtc" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "ReceivedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationHealthSnapshots" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationHealthSnapshots" PRIMARY KEY AUTOINCREMENT,
                "StationId" TEXT NOT NULL,
                "SequenceId" INTEGER NOT NULL,
                "MessageId" TEXT NOT NULL,
                "RuntimeState" TEXT NOT NULL,
                "ProcessUptimeSeconds" INTEGER NOT NULL,
                "CpuUsagePercent" REAL NULL,
                "WorkingSetMb" INTEGER NOT NULL,
                "PrivateMemoryMb" INTEGER NOT NULL,
                "DiskFreeMb" INTEGER NOT NULL,
                "DiskTotalMb" INTEGER NOT NULL,
                "SpoolPendingCount" INTEGER NOT NULL,
                "SpoolBytes" INTEGER NOT NULL,
                "CameraStatusSummary" TEXT NULL,
                "PlcStatusSummary" TEXT NULL,
                "CurrentPackageId" TEXT NULL,
                "CurrentPackageHealth" TEXT NULL,
                "LastErrorCode" TEXT NULL,
                "LastErrorMessage" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "ReceivedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationConnectionEvents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationConnectionEvents" PRIMARY KEY AUTOINCREMENT,
                "StationId" TEXT NOT NULL,
                "EventType" TEXT NOT NULL,
                "Message" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationAlarmEvents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationAlarmEvents" PRIMARY KEY AUTOINCREMENT,
                "AlarmId" TEXT NOT NULL,
                "StationId" TEXT NOT NULL,
                "Severity" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationCommandRecords" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationCommandRecords" PRIMARY KEY AUTOINCREMENT,
                "CommandId" TEXT NOT NULL,
                "StationId" TEXT NOT NULL,
                "CommandType" TEXT NOT NULL,
                "PayloadJson" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "ProgressPercent" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "ExpiresAtUtc" TEXT NOT NULL,
                "DeliveredAtUtc" TEXT NULL,
                "AcceptedAtUtc" TEXT NULL,
                "StartedAtUtc" TEXT NULL,
                "CompletedAtUtc" TEXT NULL,
                "IssuedBy" TEXT NOT NULL,
                "CorrelationId" TEXT NOT NULL,
                "ResultMessage" TEXT NULL,
                "ErrorCode" TEXT NULL,
                "ErrorDetail" TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationSyncCursors" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationSyncCursors" PRIMARY KEY AUTOINCREMENT,
                "StationId" TEXT NOT NULL,
                "LastPersistedSequenceId" INTEGER NOT NULL,
                "LastReceivedHealthSequenceId" INTEGER NOT NULL,
                "LastReceivedLogSequenceId" INTEGER NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationLogSummaries" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationLogSummaries" PRIMARY KEY AUTOINCREMENT,
                "StationId" TEXT NOT NULL,
                "SequenceId" INTEGER NOT NULL,
                "MessageId" TEXT NOT NULL,
                "TimestampUtc" TEXT NOT NULL,
                "Level" TEXT NOT NULL,
                "Source" TEXT NOT NULL,
                "EventId" TEXT NULL,
                "MessageTemplate" TEXT NULL,
                "RenderedMessage" TEXT NOT NULL,
                "ExceptionType" TEXT NULL,
                "ExceptionMessage" TEXT NULL,
                "CorrelationId" TEXT NULL,
                "RunId" TEXT NULL,
                "PackageId" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "ReceivedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationAuditRecords" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationAuditRecords" PRIMARY KEY AUTOINCREMENT,
                "AuditId" TEXT NOT NULL,
                "UserId" TEXT NULL,
                "UserName" TEXT NULL,
                "Action" TEXT NOT NULL,
                "TargetStationId" TEXT NULL,
                "CommandId" TEXT NULL,
                "PayloadSummary" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "Result" TEXT NULL,
                "ClientIp" TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "StationPackageRecords" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StationPackageRecords" PRIMARY KEY AUTOINCREMENT,
                "PackageId" TEXT NOT NULL,
                "PackageName" TEXT NOT NULL,
                "PackageVersion" TEXT NOT NULL,
                "FlowHash" TEXT NOT NULL,
                "FileName" TEXT NOT NULL,
                "FilePath" TEXT NOT NULL,
                "SizeBytes" INTEGER NOT NULL,
                "Sha256" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """,
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationNodes_StationId" ON "StationNodes" ("StationId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationResultSummaries_StationId_SequenceId" ON "StationResultSummaries" ("StationId", "SequenceId");""",
            """CREATE INDEX IF NOT EXISTS "IX_StationResultSummaries_StationId_CompletedAtUtc" ON "StationResultSummaries" ("StationId", "CompletedAtUtc");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationHealthSnapshots_StationId_SequenceId" ON "StationHealthSnapshots" ("StationId", "SequenceId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationAlarmEvents_AlarmId" ON "StationAlarmEvents" ("AlarmId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationCommandRecords_CommandId" ON "StationCommandRecords" ("CommandId");""",
            """CREATE INDEX IF NOT EXISTS "IX_StationCommandRecords_StationId_Status" ON "StationCommandRecords" ("StationId", "Status");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationSyncCursors_StationId" ON "StationSyncCursors" ("StationId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationLogSummaries_StationId_SequenceId" ON "StationLogSummaries" ("StationId", "SequenceId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationAuditRecords_AuditId" ON "StationAuditRecords" ("AuditId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationPackageRecords_PackageId" ON "StationPackageRecords" ("PackageId");"""
        };

        foreach (var statement in statements)
        {
            await dbContext.Database.ExecuteSqlRawAsync(statement);
        }
    }

    private static async Task EnsureTextColumnExistsAsync(
        Acme.Product.Infrastructure.Data.VisionDbContext dbContext,
        string tableName,
        string columnName)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var pragmaCommand = connection.CreateCommand();
            pragmaCommand.CommandText = $"PRAGMA table_info(\"{tableName}\");";

            await using var reader = await pragmaCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var existingColumn = reader["name"]?.ToString();
                if (string.Equals(existingColumn, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await reader.CloseAsync();
            var alterSql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" TEXT NULL;";
            await dbContext.Database.ExecuteSqlRawAsync(alterSql);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
