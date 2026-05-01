using Acme.PlcComm;
using FluentAssertions;

namespace Acme.Product.Tests.PlcComm;

[Trait("Category", "VirtualPLC")]
public class VirtualMcFinsPlcConnectionTests
{
    [Fact]
    public async Task MitsubishiMcClient_ShouldConnectAndPingVirtualPlc()
    {
        if (!ShouldRunVirtualMcFinsTests())
        {
            return;
        }

        using var client = PlcClientFactory.CreateFromConnectionString($"MC://{GetMcHost()}:{GetMcPort()}");
        client.ConnectTimeout = 3000;
        client.ReadTimeout = 3000;
        client.WriteTimeout = 3000;

        try
        {
            var connected = await client.ConnectAsync();
            connected.Should().BeTrue();

            var pingOk = await client.PingAsync();
            pingOk.Should().BeTrue();
        }
        finally
        {
            await client.DisconnectAsync();
        }
    }

    [Fact]
    public async Task OmronFinsClient_ShouldConnectAndPingVirtualPlc()
    {
        if (!ShouldRunVirtualMcFinsTests())
        {
            return;
        }

        using var client = PlcClientFactory.CreateFromConnectionString($"FINS://{GetFinsHost()}:{GetFinsPort()}");
        client.ConnectTimeout = 3000;
        client.ReadTimeout = 3000;
        client.WriteTimeout = 3000;

        try
        {
            var connected = await client.ConnectAsync();
            connected.Should().BeTrue();

            var pingOk = await client.PingAsync();
            pingOk.Should().BeTrue();
        }
        finally
        {
            await client.DisconnectAsync();
        }
    }

    private static bool ShouldRunVirtualMcFinsTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("CLEARVISION_RUN_VIRTUAL_MC_FINS_TESTS"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMcHost()
    {
        return Environment.GetEnvironmentVariable("CLEARVISION_VIRTUAL_MC_HOST") ?? "127.0.0.1";
    }

    private static int GetMcPort()
    {
        return TryGetIntEnvironmentVariable("CLEARVISION_VIRTUAL_MC_PORT", 5002);
    }

    private static string GetFinsHost()
    {
        return Environment.GetEnvironmentVariable("CLEARVISION_VIRTUAL_FINS_HOST") ?? "127.0.0.1";
    }

    private static int GetFinsPort()
    {
        return TryGetIntEnvironmentVariable("CLEARVISION_VIRTUAL_FINS_PORT", 9600);
    }

    private static int TryGetIntEnvironmentVariable(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) ? value : fallback;
    }
}
