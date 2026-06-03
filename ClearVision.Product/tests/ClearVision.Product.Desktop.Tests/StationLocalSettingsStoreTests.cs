using ClearVision.Product.Station;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationLocalSettingsStoreTests
{
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
