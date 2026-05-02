using Acme.Product.Infrastructure.DependencyInjection;
using Acme.Product.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.Product.Station;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddVisionRuntimeCoreServices();
                services.AddSingleton<RuntimePackageValidator>();
                services.AddSingleton<RuntimePackageLoader>();
                services.AddSingleton<RuntimeResultNormalizer>();
                services.AddSingleton<RuntimeHost>();
                services.AddSingleton<StationLocalSettingsStore>();
                services.AddSingleton<MainForm>();
            })
            .Build();

        var settingsStore = host.Services.GetRequiredService<StationLocalSettingsStore>();
        settingsStore.MarkStartup();

        try
        {
            System.Windows.Forms.Application.Run(host.Services.GetRequiredService<MainForm>());
            settingsStore.MarkCleanExit();
        }
        finally
        {
            host.Services.GetRequiredService<RuntimeHost>().DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
