using Acme.Product.Infrastructure.DependencyInjection;
using Acme.Product.Core.Cameras;
using Acme.Product.Desktop.Triggers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.Product.Desktop;

public static class DependencyInjection
{
    internal const string DatabasePathConfigKey = VisionRuntimeServiceCollectionExtensions.DatabasePathConfigKey;

    public static IServiceCollection AddVisionServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddSingleton<EnterPhotoelectricTriggerInputService>();
        services.AddSingleton<ITriggerInputService>(sp =>
            sp.GetRequiredService<EnterPhotoelectricTriggerInputService>());

        return services.AddVisionRuntimeServices(configuration);
    }

    internal static string ResolveVisionDatabasePath(
        string? configuredPath,
        string baseDirectory,
        string currentDirectory,
        string localApplicationDataRoot)
    {
        return VisionRuntimeServiceCollectionExtensions.ResolveVisionDatabasePath(
            configuredPath,
            baseDirectory,
            currentDirectory,
            localApplicationDataRoot);
    }

    internal static string NormalizeDatabasePath(string configuredPath, string baseDirectory)
    {
        return VisionRuntimeServiceCollectionExtensions.NormalizeDatabasePath(configuredPath, baseDirectory);
    }

    internal static void MigrateLegacyDatabaseIfNeeded(
        string targetPath,
        string baseDirectory,
        string currentDirectory)
    {
        VisionRuntimeServiceCollectionExtensions.MigrateLegacyDatabaseIfNeeded(
            targetPath,
            baseDirectory,
            currentDirectory);
    }
}
