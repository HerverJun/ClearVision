using Microsoft.Extensions.DependencyInjection;

namespace ClearVision.Product.Desktop.PreviewArtifacts;

public static class PreviewArtifactServiceCollectionExtensions
{
    public static IServiceCollection AddPreviewArtifactServices(this IServiceCollection services)
    {
        services.AddSingleton<IPreviewArtifactClock, SystemPreviewArtifactClock>();
        services.AddSingleton<PreviewArtifactStore>();
        services.AddSingleton<PreviewArtifactMaterializer>();
        return services;
    }
}
