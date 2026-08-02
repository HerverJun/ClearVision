using ClearVision.Product.Station;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationLocalSettingsStoreTests
{
    [Fact]
    public void ActivePackageIdentity_ShouldPersistAcrossRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationLocalSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new StationLocalSettingsStore(root);
            store.UpdateActivePackage("C:\\station\\active", "1.2.3", "sha256:artifact");

            var restarted = new StationLocalSettingsStore(root);
            restarted.Current.LastGoodPackagePath.Should().Be("C:\\station\\active");
            restarted.Current.CurrentPackageVersion.Should().Be("1.2.3");
            restarted.Current.CurrentPackageSha256.Should().Be("sha256:artifact");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ShouldRecoverFromBackup_WhenPrimarySettingsJsonIsCorrupt()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationLocalSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new StationLocalSettingsStore(root);
            store.UpdateStationIdentity("station-stable", "line-a");
            File.Copy(
                Path.Combine(root, "station-settings.json"),
                Path.Combine(root, "station-settings.json.bak"),
                overwrite: true);
            File.WriteAllText(Path.Combine(root, "station-settings.json"), "{not-valid-json");

            var recovered = new StationLocalSettingsStore(root);

            recovered.Current.StationId.Should().Be("station-stable");
            recovered.Current.LineName.Should().Be("line-a");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
