using ClearVision.Product.Core.Entities;
using FluentAssertions;

namespace ClearVision.Product.Tests.Configuration;

public class TcpCommunicationConfigTests
{
    [Fact]
    public void AppConfigNormalize_ShouldCreateLegacySafeTcpDefaults()
    {
        var config = new AppConfig
        {
            TcpCommunication = null!
        };

        config.Normalize();

        config.TcpCommunication.Should().NotBeNull();
        config.TcpCommunication.Profiles.Should().BeEmpty();
    }

    [Fact]
    public void TcpProfileNormalize_ShouldUseLoopbackAndSafeModes()
    {
        var profile = new TcpCommunicationProfile
        {
            Id = " robot ",
            Name = " Main Robot ",
            Mode = "server",
            RemoteHost = "",
            LocalHost = "",
            RemotePort = 70000,
            LocalPort = -1,
            Encoding = "utf-8",
            FrameMode = "fixed-length",
            LineEnding = "crlf",
            TimeoutMs = 10
        };

        profile.Normalize();

        profile.Id.Should().Be("robot");
        profile.Name.Should().Be("Main Robot");
        profile.Mode.Should().Be(TcpCommunicationProfile.ModeServer);
        profile.RemoteHost.Should().Be("127.0.0.1");
        profile.LocalHost.Should().Be("127.0.0.1");
        profile.RemotePort.Should().Be(0);
        profile.LocalPort.Should().Be(0);
        profile.Encoding.Should().Be(TcpCommunicationProfile.EncodingUtf8);
        profile.FrameMode.Should().Be(TcpCommunicationProfile.FrameModeFixedLength);
        profile.LineEnding.Should().Be(TcpCommunicationProfile.LineEndingCrlf);
        profile.TimeoutMs.Should().Be(TcpCommunicationProfile.MinTimeoutMs);
    }

    [Fact]
    public void TcpConfigValidator_ShouldRejectInvalidClientProfile()
    {
        var config = new TcpCommunicationConfig
        {
            Profiles =
            [
                new TcpCommunicationProfile
                {
                    Id = "client",
                    Name = "Client",
                    Enabled = true,
                    Mode = "Client",
                    RemoteHost = "999.1.1.1",
                    RemotePort = 0,
                    TimeoutMs = 5000
                }
            ]
        };

        var result = TcpCommunicationConfigValidator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Field == "remoteHost");
        result.Errors.Should().Contain(error => error.Field == "remotePort");
    }

    [Fact]
    public void TcpConfigValidator_ShouldRejectDuplicateProfileIds()
    {
        var config = new TcpCommunicationConfig
        {
            Profiles =
            [
                CreateValidClientProfile("robot"),
                CreateValidClientProfile("ROBOT")
            ]
        };

        var result = TcpCommunicationConfigValidator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Field == "id");
    }

    [Fact]
    public void ValidateProfileForOperation_ShouldRejectDisabledProfile()
    {
        var profile = CreateValidClientProfile("robot");
        profile.Enabled = false;

        var result = TcpCommunicationConfigValidator.ValidateProfileForOperation(profile);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Field == "enabled");
    }

    private static TcpCommunicationProfile CreateValidClientProfile(string id)
    {
        return new TcpCommunicationProfile
        {
            Id = id,
            Name = id,
            Enabled = true,
            Mode = TcpCommunicationProfile.ModeClient,
            RemoteHost = "127.0.0.1",
            RemotePort = 9000,
            TimeoutMs = 5000
        };
    }
}
