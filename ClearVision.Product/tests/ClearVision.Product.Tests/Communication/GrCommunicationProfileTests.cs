using ClearVision.Product.Infrastructure.Communication.Gr;
using FluentAssertions;

namespace ClearVision.Product.Tests.Communication;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public sealed class GrCommunicationProfileTests
{
    [Fact]
    public void Catalog_ShouldLoadVersionedReadOnlyTemplateWithStableHash()
    {
        var template = new GrRegisterMapCatalog().GetTemplate();

        template.TemplateId.Should().Be("gr-robot");
        template.Version.Should().Be("3.0");
        template.StatusRange.StartAddress.Should().Be(437);
        template.StatusRange.Count.Should().Be(23);
        template.WritePolicy.EnabledByDefault.Should().BeFalse();
        template.WritePolicy.AllowedAddresses.Should().BeEmpty();
        template.WritePolicy.DisabledAddresses.Should().Contain([1000, 1001, 1002, 1003, 1004, 1005]);
        template.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Decoder_ShouldDecodeKnownGrSafetyState()
    {
        var catalog = new GrRegisterMapCatalog();
        var values = new ushort[23];
        values[0] = 1;
        values[1] = 0;
        values[5] = 2050;
        values[6] = 0;
        values[7] = 1;
        values[8] = 1;
        values[12] = 2;

        var decoded = new GrStateDecoder(catalog).Decode(437, values);

        decoded.Single(item => item.Key == "powered").Value.Should().Be(true);
        decoded.Single(item => item.Key == "enabled").Value.Should().Be(false);
        decoded.Single(item => item.Key == "alarmCode").RawValue.Should().Be(2050);
        decoded.Single(item => item.Key == "emergencyStop").Value.Should().Be(true);
        decoded.Single(item => item.Key == "operatingMode").Value.Should().Be("ManualHigh");
    }

    [Fact]
    public void Store_ShouldPersistReadOnlyProfileAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clearvision-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "modbus-profiles.json");
        try
        {
            var store = new JsonCommunicationProfileStore(path);
            var saved = store.Save(new ModbusDeviceProfile
            {
                Id = "gr-lab",
                Name = "GR Lab",
                Host = "172.16.87.12",
                Port = 502,
                UnitId = 255,
                TemplateId = "gr-robot",
                TemplateVersion = "3.0",
                TemplateHash = "hash",
                ReadOnly = false
            });

            saved.ReadOnly.Should().BeTrue();
            File.Exists(path).Should().BeTrue();
            Directory.EnumerateFiles(directory, "*.tmp").Should().BeEmpty();
            new JsonCommunicationProfileStore(path).Get("GR-LAB").Should().BeEquivalentTo(saved);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void Store_ShouldRemoveTemporaryFileWhenAtomicReplaceFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clearvision-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "profiles-target");
        Directory.CreateDirectory(path);
        try
        {
            var store = new JsonCommunicationProfileStore(path);

            var save = () => store.Save(new ModbusDeviceProfile
            {
                Id = "gr-lab",
                Name = "GR Lab",
                Host = "172.16.87.12",
                Port = 502,
                UnitId = 255
            });

            save.Should().Throw<Exception>();
            Directory.EnumerateFiles(directory, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
