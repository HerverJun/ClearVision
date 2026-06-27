using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.DependencyInjection;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IConfigurationService = ClearVision.Product.Core.Interfaces.IConfigurationService;

namespace ClearVision.Product.Station;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, configuration) =>
            {
                configuration.AddJsonFile(
                    StationSettingsPaths.GetStationSyncSettingsPath(),
                    optional: true,
                    reloadOnChange: false);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddVisionRuntimeCoreServices();
                services.AddSingleton<RuntimePackageValidator>();
                services.AddSingleton<RuntimePackageLoader>();
                services.AddSingleton<RuntimeResultNormalizer>();
                services.AddSingleton<IProjectVariableStateStore>(_ => new JsonFileProjectVariableStateStore(
                    StationSettingsPaths.GetStationProjectVariableStatesPath()));
                services.AddSingleton<RuntimeHost>();
                services.AddSingleton<IConfigurationService, JsonConfigurationService>();
                services.AddSingleton<StationHardwareSettingsService>();
                services.AddSingleton<StationLocalSettingsStore>();
                services.AddSingleton<StationSiteProfileStore>();
                services.Configure<Sync.StationSyncOptions>(context.Configuration.GetSection(Sync.StationSyncOptions.SectionName));
                services.AddSingleton<Sync.StationSyncSettingsStore>();
                services.AddSingleton<Sync.StationStudioConnectionTester>();
                services.AddSingleton<Sync.StationIdentityResolver>();
                services.AddSingleton<Sync.StationSpoolStore>();
                services.AddSingleton<Sync.StationCommandResultSpoolStore>();
                services.AddSingleton<Sync.StationCommandExecutionJournalStore>();
                services.AddSingleton<Sync.StationHubClient>();
                services.AddSingleton<Sync.StationPackageDeploymentService>();
                services.AddSingleton<Sync.StationLogRelayService>();
                services.AddHostedService<Sync.StationSyncHostedService>();
                services.AddSingleton<MainForm>();
            })
            .Build();

        var settingsStore = host.Services.GetRequiredService<StationLocalSettingsStore>();
        settingsStore.MarkStartup();
        host.Services.GetRequiredService<StationHardwareSettingsService>()
            .ApplyCurrentAsync()
            .GetAwaiter()
            .GetResult();

        try
        {
            host.StartAsync().GetAwaiter().GetResult();
            MarkStationSyncSettingsApplied();
            System.Windows.Forms.Application.Run(host.Services.GetRequiredService<MainForm>());
            settingsStore.MarkCleanExit();
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.Services.GetRequiredService<RuntimeHost>().DisposeAsync().AsTask().GetAwaiter().GetResult();
            ClearVision.Product.Infrastructure.Operators.PlcCommunicationOperatorBase.StopHeartbeat();
        }
    }

    private static void MarkStationSyncSettingsApplied()
    {
        var markerPath = StationSettingsPaths.GetStationSyncSettingsAppliedMarkerPath();
        var directory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(markerPath, DateTimeOffset.UtcNow.ToString("O"));
    }
}
