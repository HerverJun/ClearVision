using ClearVision.Product.Application.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class InspectionImagePersistencePathsTests
{
    [Fact]
    public void ResolveImageSaveRoots_WhenConfiguredPathIsEmpty_ShouldReturnFallbackOnly()
    {
        var fallbackRoot = InspectionImagePersistencePaths.GetFallbackImageSaveRoot();

        var roots = InspectionImagePersistencePaths.ResolveImageSaveRoots(" ");

        roots.Should().Equal(fallbackRoot);
    }

    [Fact]
    public void ResolveImageSaveRoots_WhenConfiguredPathIsInvalid_ShouldReturnFallbackOnly()
    {
        var fallbackRoot = InspectionImagePersistencePaths.GetFallbackImageSaveRoot();

        var roots = InspectionImagePersistencePaths.ResolveImageSaveRoots("\0invalid-path");

        roots.Should().Equal(fallbackRoot);
    }

    [Fact]
    public void ResolveImageSaveRoots_WhenConfiguredPathIsFallbackWithTrailingSeparator_ShouldReturnFallbackOnly()
    {
        var fallbackRoot = InspectionImagePersistencePaths.GetFallbackImageSaveRoot();
        var configuredPath = fallbackRoot + Path.DirectorySeparatorChar;

        var roots = InspectionImagePersistencePaths.ResolveImageSaveRoots(configuredPath);

        roots.Should().Equal(fallbackRoot);
    }

    [Fact]
    public void ResolveImageSaveRoots_WhenConfiguredPathHasWhitespace_ShouldTrimBeforeComparing()
    {
        var fallbackRoot = InspectionImagePersistencePaths.GetFallbackImageSaveRoot();
        var configuredPath = $"  {fallbackRoot}{Path.DirectorySeparatorChar}  ";

        var roots = InspectionImagePersistencePaths.ResolveImageSaveRoots(configuredPath);

        roots.Should().Equal(fallbackRoot);
    }

    [Fact]
    public void ResolveImageSaveRoots_WhenConfiguredPathDiffers_ShouldTryConfiguredBeforeFallback()
    {
        var configuredPath = Path.Combine(
            Path.GetTempPath(),
            "ClearVisionImagePersistencePathsTests",
            Guid.NewGuid().ToString("N"));
        var fallbackRoot = InspectionImagePersistencePaths.GetFallbackImageSaveRoot();

        var roots = InspectionImagePersistencePaths.ResolveImageSaveRoots(configuredPath);

        roots.Should().Equal(Path.GetFullPath(configuredPath), fallbackRoot);
    }
}
