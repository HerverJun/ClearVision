using System.Reflection;
using System.Security.Cryptography;
using Acme.Product.Station.Sync;
using FluentAssertions;

namespace Acme.Product.Desktop.Tests;

public sealed class StationPackageDeploymentServiceTests
{
    [Fact]
    public async Task VerifyHashAsync_ShouldRejectDownloadedPackageHashMismatch()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "ClearVisionPackageHashTests", $"{Guid.NewGuid():N}.cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, "package-bytes");

        try
        {
            var method = typeof(StationPackageDeploymentService).GetMethod(
                "VerifyHashAsync",
                BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var wrongHash = "sha256:" + new string('0', 64);
            var task = (Task)method!.Invoke(null, [packagePath, wrongHash, CancellationToken.None])!;
            Func<Task> act = async () => await task;

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*hash does not match*");
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public async Task VerifyHashAsync_ShouldRejectMissingHash()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "ClearVisionPackageHashTests", $"{Guid.NewGuid():N}.cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, "package-bytes");

        try
        {
            var method = typeof(StationPackageDeploymentService).GetMethod(
                "VerifyHashAsync",
                BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var task = (Task)method!.Invoke(null, [packagePath, null, CancellationToken.None])!;
            Func<Task> act = async () => await task;

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*missing sha256*");
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public async Task VerifyHashAsync_ShouldAcceptMatchingSha256WithOrWithoutPrefix()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "ClearVisionPackageHashTests", $"{Guid.NewGuid():N}.cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, "package-bytes");

        try
        {
            await using var stream = File.OpenRead(packagePath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            var method = typeof(StationPackageDeploymentService).GetMethod(
                "VerifyHashAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            await ((Task)method!.Invoke(null, [packagePath, hash, CancellationToken.None])!);
            await ((Task)method.Invoke(null, [packagePath, $"sha256:{hash}", CancellationToken.None])!);
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public void SanitizePackageFileSegment_ShouldRemovePathSeparators()
    {
        var method = typeof(StationPackageDeploymentService).GetMethod(
            "SanitizePackageFileSegment",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, ["../pkg\\evil"])!;

        result.Should().NotContain("..");
        result.Should().NotContain(Path.DirectorySeparatorChar.ToString());
        result.Should().NotContain(Path.AltDirectorySeparatorChar.ToString());
        result.Should().Be("_pkg_evil");
    }
}
