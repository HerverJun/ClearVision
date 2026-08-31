using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Collection(RuntimeConcurrencyCollection.Name)]
public class TcpCommunicationOperatorTests
{
    private readonly TcpCommunicationOperator _operator;

    public static TheoryData<string, object, string> NestedResultPayloads => new()
    {
        { "dictionary", new Dictionary<string, object> { ["Score"] = 96 }, "96" },
        { "object", new ResultPayload { Score = 97 }, "97" },
        { "json", "{\"Score\":98}", "98" }
    };

    public TcpCommunicationOperatorTests()
    {
        _operator = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeTcpCommunication()
    {
        _operator.OperatorType.Should().Be(OperatorType.TcpCommunication);
    }

    [Fact]
    public void ValidateParameters_Default_ShouldRequireServerOwnedProfile()
    {
        var op = new Operator("test", OperatorType.TcpCommunication, 0, 0);

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("TCP_PROFILE_REQUIRED:");
    }

    [Fact]
    public void Metadata_ShouldExposeProfileOnlyAuthorityAndPayloadDefaults()
    {
        var metadata = new OperatorFactory().GetMetadata(OperatorType.TcpCommunication)!;

        var parameter = metadata.Parameters.Single(p => p.Name == "FailOnUnresolvedPayloadPlaceholder");
        var profileParameter = metadata.Parameters.Single(p => p.Name == "ProfileId");

        parameter.DefaultValue.Should().Be(true);
        profileParameter.IsRequired.Should().BeTrue();
        foreach (var legacyTargetParameter in new[] { "UseGlobalProfile", "Mode", "IpAddress", "Port", "Timeout", "Encoding" })
        {
            metadata.Parameters.Should().NotContain(p => p.Name == legacyTargetParameter);
        }
    }

    [Fact]
    public void ValidateParameters_WithRawPort_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(new(Guid.NewGuid(), "Port", "Port", "", "int", 70000, 0, 65535, true));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("TCP_RAW_ENDPOINT_FORBIDDEN:");
    }

    [Fact]
    public void ValidateParameters_WithServerModeAndNoProfile_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Mode", "Server", "string"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("TCP_PROFILE_REQUIRED:");
    }

    [Fact]
    public async Task PendingGlobalProfile_ShouldFailBeforeAnyNetworkCall()
    {
        var manager = CreateProfileManager();
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-pending-profile", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("UseGlobalProfile", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "<pending-tcp-profile>", "string"));

        sut.ValidateParameters(op).IsValid.Should().BeFalse();
        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Data"] = "PING" });

        result.IsSuccess.Should().BeFalse();
        await manager.DidNotReceive().GetConfigAsync(Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendAsync(
            Arg.Any<string>(),
            Arg.Any<TcpDeviceSendRequest>(),
            Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(
            Arg.Any<TcpCommunicationProfile>(),
            Arg.Any<TcpDeviceSendRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProfileMode_ShouldIgnoreDisabledLegacyModeTimeoutAndEncoding()
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-profile-stale", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Mode", "<pending-mode>", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Timeout", 1, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Encoding", "INVALID", "string"));

        sut.ValidateParameters(op).IsValid.Should().BeTrue();
        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Data"] = "PING" });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.IsHex.Should().BeFalse();
        await manager.Received(1).SendAsync(
            "robot-main",
            Arg.Any<TcpDeviceSendRequest>(),
            Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(
            Arg.Any<TcpCommunicationProfile>(),
            Arg.Any<TcpDeviceSendRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WaitResponseFalse_ShouldIgnoreAllStaleResponseRules()
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("sent", string.Empty)));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-no-response", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("WaitResponse", false, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseTimeoutMs", 1, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Regex", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseRegexPattern", string.Empty, "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnParseError", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseStartMarker", "BEGIN", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnMissingResponseFrame", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectedResponse", "ACK", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnUnexpectedResponse", true, "bool"));

        sut.ValidateParameters(op).IsValid.Should().BeTrue();
        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Data"] = "PING" });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.WaitResponse.Should().BeFalse();
        result.OutputData!["ResponseAccepted"].Should().Be(true);
        result.OutputData["ParseSuccess"].Should().Be(true);
    }

    [Fact]
    public void ValidateParameters_KeyValueDelimiterAlternatives_ShouldMatchAtLeastOneContract()
    {
        var valid = new Operator("tcp-key-value-valid", OperatorType.TcpCommunication, 0, 0);
        valid.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        valid.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        valid.AddParameter(TestHelpers.CreateParameter("ResponseKeyValuePairDelimiter", string.Empty, "string"));
        valid.AddParameter(TestHelpers.CreateParameter("ResponseKeyValuePairDelimiters", ",", "string"));
        valid.AddParameter(TestHelpers.CreateParameter("ResponseKeyValueSeparator", string.Empty, "string"));
        valid.AddParameter(TestHelpers.CreateParameter("ResponseKeyValueSeparators", ":", "string"));
        _operator.ValidateParameters(valid).IsValid.Should().BeTrue();

        var invalid = new Operator("tcp-key-value-invalid", OperatorType.TcpCommunication, 0, 0);
        invalid.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        invalid.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        invalid.AddParameter(TestHelpers.CreateParameter("ResponseKeyValuePairDelimiter", string.Empty, "string"));
        invalid.AddParameter(TestHelpers.CreateParameter("ResponseKeyValuePairDelimiters", string.Empty, "string"));
        invalid.AddParameter(TestHelpers.CreateParameter("ResponseKeyValueSeparator", string.Empty, "string"));
        invalid.AddParameter(TestHelpers.CreateParameter("ResponseKeyValueSeparators", string.Empty, "string"));
        _operator.ValidateParameters(invalid).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutProfile_ShouldReturnFailureBeforeNetworkCall()
    {
        var manager = CreateProfileManager();
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("test", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Mode", "Server", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("TCP_PROFILE_REQUIRED:");
        await manager.DidNotReceive().GetConfigAsync(Arg.Any<CancellationToken>());
        await AssertNoSendAsync(manager);
    }

    [Fact]
    public async Task ExecuteAsync_WithProfileId_ShouldSendThroughGlobalManager()
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-profile", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("UseGlobalProfile", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("SendData", "from-send-data", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "from-input"
        });

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be("from-input");
        result.OutputData!.Should().NotContainKey("IpAddress");
        result.OutputData!.Should().NotContainKey("Port");
        await manager.Received(1).GetConfigAsync(Arg.Any<CancellationToken>());
        await manager.Received(1).SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(Arg.Any<TcpCommunicationProfile>(), Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithPayloadTemplate_ShouldResolveNamedAndNestedInputs()
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "SN={Serial};CODE={Result.Code};OK={Result.Ok};RAW={Data};STATIC={SendData}", "string"));
        op.AddParameter(TestHelpers.CreateParameter("SendData", "fixed", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "raw",
            ["serial"] = "A001",
            ["Result"] = new Dictionary<string, object>
            {
                ["Code"] = 12,
                ["Ok"] = true
            }
        });

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be("SN=A001;CODE=12;OK=True;RAW=raw;STATIC=fixed");
        result.OutputData!["RequestPayload"].Should().Be("SN=A001;CODE=12;OK=True;RAW=raw;STATIC=fixed");
    }

    [Fact]
    public async Task ExecuteAsync_WithDefaultFailParameterAndMissingPlaceholder_ShouldFailBeforeSend()
    {
        var manager = CreateProfileManager();
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "OK,{Missing}", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnUnresolvedPayloadPlaceholder", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("PayloadTemplate contains unresolved placeholders: Missing.");
        await manager.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(Arg.Any<TcpCommunicationProfile>(), Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithOmittedFailParameterAndMissingPlaceholder_ShouldFailBeforeSend()
    {
        var manager = CreateProfileManager();
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "OK,{Missing}", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("PayloadTemplate contains unresolved placeholders: Missing.");
        await manager.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(Arg.Any<TcpCommunicationProfile>(), Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitFalseFailParameterAndMissingPlaceholder_ShouldPreserveAndSend()
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "OK,{Missing}", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnUnresolvedPayloadPlaceholder", false, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be("OK,{Missing}");
        result.OutputData!["RequestPayload"].Should().Be("OK,{Missing}");
        await manager.Received(1).SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithDataPlaceholder_ShouldResolveAndSendInputData()
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "J={Data}", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "NG"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be("J=NG");
        result.OutputData!["RequestPayload"].Should().Be("J=NG");
    }

    [Theory]
    [MemberData(nameof(NestedResultPayloads))]
    public async Task ExecuteAsync_WithNestedResultPlaceholder_ShouldResolveAndSend(string caseName, object resultInput, string expectedScore)
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "S={Result.Score}", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Result"] = resultInput
        });

        result.IsSuccess.Should().BeTrue($"{caseName}: {result.ErrorMessage}");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be($"S={expectedScore}");
        result.OutputData!["RequestPayload"].Should().Be($"S={expectedScore}");
    }

    [Fact]
    public async Task ExecuteAsync_WithSendDataPlaceholder_ShouldResolveAndSendStaticSendData()
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "P={SendData}", "string"));
        op.AddParameter(TestHelpers.CreateParameter("SendData", "READY", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be("P=READY");
        result.OutputData!["RequestPayload"].Should().Be("P=READY");
    }

    [Fact]
    public async Task ExecuteAsync_WithDecodedPayloadTemplate_ShouldSendControlCharacters()
    {
        var manager = CreateProfileManager();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "\\x02CMD={Serial}\\r\\n", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Serial"] = "A001"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be("\u0002CMD=A001\r\n");
        result.OutputData!["RequestPayload"].Should().Be("\u0002CMD=A001\r\n");
        result.OutputData["DecodeEscapeSequences"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithStrictPayloadTemplateAndMissingPlaceholder_ShouldFailBeforeSend()
    {
        var manager = CreateProfileManager();
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-template", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("PayloadTemplate", "SN={Serial};MISS={Missing}", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnUnresolvedPayloadPlaceholder", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Serial"] = "A001"
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("PayloadTemplate");
        result.ErrorMessage.Should().Contain("Missing");
        await manager.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(Arg.Any<TcpCommunicationProfile>(), Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
    }

    private sealed class ResultPayload
    {
        public int Score { get; init; }
    }

    [Fact]
    public async Task ExecuteAsync_WithKeyValueResponseParse_ShouldExposeSelectedField()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "code=OK;score=98.5;count=12")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "score", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "PING"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(98.5);
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["code"].Should().Be("OK");
        fields["count"].Should().Be(12L);
    }

    [Fact]
    public async Task ExecuteAsync_WithDecodedKeyValuePairDelimiter_ShouldParseLfSeparatedPairs()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "code=OK\nscore=98.5\nstatus=PASS")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-kv-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseKeyValuePairDelimiter", "\\n", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "PING"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(98.5);
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["code"].Should().Be("OK");
        fields["status"].Should().Be("PASS");
    }

    [Fact]
    public async Task ExecuteAsync_WithAdditionalKeyValueDelimiters_ShouldParseMixedResponse()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "code=OK,score:98.5\nstatus=PASS")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-kv-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseKeyValuePairDelimiter", ",", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseKeyValuePairDelimiters", "\\n", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseKeyValueSeparator", "=", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseKeyValueSeparators", ":", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "PING"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(98.5);
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["code"].Should().Be("OK");
        fields["score"].Should().Be(98.5);
        fields["status"].Should().Be("PASS");
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedRawResponse_ShouldExposeAcceptedStatus()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK:OK;score=98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-ack", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectedResponse", "ACK:OK", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Contains", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Status"].Should().Be(true);
        result.OutputData["ResponseAccepted"].Should().Be(true);
        result.OutputData["ResponseMatchValue"].Should().Be("ACK:OK;score=98.5");
        result.OutputData["ResponseMatchError"].Should().Be(string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_WithIgnoreCaseExpectedResponse_ShouldAcceptLowercaseAck()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ack:ok;score=98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-ack", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectedResponse", "ACK:OK", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Contains", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchIgnoreCase", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Status"].Should().Be(true);
        result.OutputData["ResponseAccepted"].Should().Be(true);
        result.OutputData["ResponseMatchIgnoreCase"].Should().Be(true);
        result.OutputData["ResponseMatchValue"].Should().Be("ack:ok;score=98.5");
    }

    [Fact]
    public async Task ExecuteAsync_WithDecodedExpectedResponse_ShouldMatchControlCharacters()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK\r\n")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-ack", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectedResponse", "ACK\\r\\n", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Equals", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Status"].Should().Be(true);
        result.OutputData["ResponseAccepted"].Should().Be(true);
        result.OutputData["ExpectedResponse"].Should().Be("ACK\r\n");
        result.OutputData["DecodeEscapeSequences"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithRejectedRawResponse_ShouldKeepTransportSuccessButStatusFalse()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK:NG;ERR=5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-nak", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RejectedResponse", "NG", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Status"].Should().Be(false);
        result.OutputData["ResponseAccepted"].Should().Be(false);
        result.OutputData["ResponseMatchError"].Should().Be("Response matched RejectedResponse.");
    }

    [Fact]
    public async Task ExecuteAsync_WithIgnoreCaseRejectedParsedValue_ShouldRejectLowercaseNg()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "code=ng;score=98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-nak", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "code", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RejectedResponse", "NG", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Equals", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchSource", "ParsedValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchIgnoreCase", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Status"].Should().Be(false);
        result.OutputData["ParsedValue"].Should().Be("ng");
        result.OutputData["ResponseAccepted"].Should().Be(false);
        result.OutputData["ResponseMatchIgnoreCase"].Should().Be(true);
        result.OutputData["ResponseMatchValue"].Should().Be("ng");
        result.OutputData["ResponseMatchError"].Should().Be("Response matched RejectedResponse.");
    }

    [Fact]
    public async Task ExecuteAsync_WithDecodedRejectedResponse_ShouldRejectControlCharacter()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "\u0015")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-nak", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RejectedResponse", "\\x15", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Equals", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Status"].Should().Be(false);
        result.OutputData["ResponseAccepted"].Should().Be(false);
        result.OutputData["RejectedResponse"].Should().Be("\u0015");
        result.OutputData["ResponseMatchError"].Should().Be("Response matched RejectedResponse.");
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedParsedValueAndFailOnUnexpectedResponse_ShouldReturnFailureWithDiagnostics()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "code=NG;score=98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-parsed-ack", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "code", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectedResponse", "OK", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Equals", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchSource", "ParsedValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnUnexpectedResponse", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Response did not match ExpectedResponse.");
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Status"].Should().Be(false);
        result.OutputData["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be("NG");
        result.OutputData["ResponseAccepted"].Should().Be(false);
        result.OutputData["ResponseMatchValue"].Should().Be("NG");
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonPathResponseParse_ShouldExposeNestedValue()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", """{"result":{"ok":true,"code":7}}""")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-json-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "JsonPath", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "$.result.ok", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "PING"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(true);
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields.Should().ContainKey("result");
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonPathRequiredArrayField_ShouldAcceptIndexedField()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", """{"results":[{"score":97.0},{"score":98.5,"status":"OK"}]}""")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-json-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "JsonPath", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "$.results[1].score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RequiredResponseFields", "results.1.score,results.1.status", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(98.5);
        var missingFields = result.OutputData["MissingResponseFields"].Should().BeAssignableTo<IReadOnlyList<string>>().Subject;
        missingFields.Should().BeEmpty();
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["results"].Should().BeAssignableTo<IEnumerable<object>>();
    }

    [Fact]
    public async Task ExecuteAsync_WithIgnoreCaseRegexResponseParse_ShouldExposeNamedField()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ack:ok;score=98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-regex-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Regex", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseRegexPattern", @"ACK:(?<Status>OK);SCORE=(?<Score>[0-9.]+)", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseRegexIgnoreCase", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Status", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be("ok");
        result.OutputData["ResponseRegexIgnoreCase"].Should().Be(true);
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Status"].Should().Be("ok");
        fields["Score"].Should().Be(98.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithDelimitedFieldNames_ShouldExposeNamedFields()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001,98.5,OK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-delimited-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Delimited", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseDelimiter", ",", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Score", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(98.5);
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields["Score"].Should().Be(98.5);
        fields["Status"].Should().Be("OK");
        fields["1"].Should().Be(98.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithDecodedCrLfDelimitedResponse_ShouldExposeNamedFields()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001\r\n98.5\r\nOK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-delimited-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Delimited", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseDelimiter", "\\r\\n", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be("OK");
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields["Score"].Should().Be(98.5);
        fields["Status"].Should().Be("OK");
    }

    [Fact]
    public async Task ExecuteAsync_WithAdditionalDelimitedResponseDelimiters_ShouldExposeNamedFields()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001,98.5\nOK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-delimited-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Delimited", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseDelimiter", ",", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseDelimiters", "\\n", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be("OK");
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields["Score"].Should().Be(98.5);
        fields["Status"].Should().Be("OK");
        fields["2"].Should().Be("OK");
    }

    [Fact]
    public async Task ExecuteAsync_WithFramedDelimitedResponse_ShouldParseNormalizedPayload()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "\u0002 SN001,98.5,OK \u0003\r\n")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-framed-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Delimited", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseDelimiter", ",", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("TrimResponseBeforeParse", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseStartMarker", "\\x02", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseEndMarker", "\\x03", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectedResponse", "SN001,98.5,OK", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Equals", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchSource", "NormalizedResponse", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Response"].Should().Be("\u0002 SN001,98.5,OK \u0003\r\n");
        result.OutputData["NormalizedResponse"].Should().Be("SN001,98.5,OK");
        result.OutputData["ResponseFrameFound"].Should().Be(true);
        result.OutputData["ResponseFrameError"].Should().Be(string.Empty);
        result.OutputData["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be("OK");
        result.OutputData["ResponseAccepted"].Should().Be(true);
        result.OutputData["ResponseMatchValue"].Should().Be("SN001,98.5,OK");
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields["Score"].Should().Be(98.5);
        fields["Status"].Should().Be("OK");
    }

    [Fact]
    public async Task ExecuteAsync_WithStrictResponseFrameAndMissingStartMarker_ShouldReturnFailureWithDiagnostics()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001,98.5,OK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-framed-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseStartMarker", "\\x02", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnMissingResponseFrame", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("DecodeEscapeSequences", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Response start marker was not found.");
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Status"].Should().Be(false);
        result.OutputData["Response"].Should().Be("SN001,98.5,OK");
        result.OutputData["NormalizedResponse"].Should().Be("SN001,98.5,OK");
        result.OutputData["ResponseFrameFound"].Should().Be(false);
        result.OutputData["ResponseFrameError"].Should().Be("Response start marker was not found.");
        result.OutputData["ParseSuccess"].Should().Be(false);
        result.OutputData["ResponseAccepted"].Should().Be(false);
    }

    [Fact]
    public async Task ExecuteAsync_WithDelimitedFieldsProjectVariableAndBranch_ShouldSupportVmStyleJudgmentChain()
    {
        var scoreVariableId = Guid.NewGuid();
        var thresholdVariableId = Guid.NewGuid();
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = scoreVariableId,
                    Name = "tcp.lastScore",
                    DisplayName = "Last TCP Score",
                    ValueType = ProjectGlobalVariableValueType.Double,
                    InitialValue = JsonSerializer.SerializeToElement(0.0)
                },
                new ProjectGlobalVariableDefinition
                {
                    Id = thresholdVariableId,
                    Name = "threshold.score",
                    DisplayName = "Score Threshold",
                    ValueType = ProjectGlobalVariableValueType.Double,
                    InitialValue = JsonSerializer.SerializeToElement(98.0)
                }
            ]
        };
        using var session = new ProjectVariableSession(schema);
        var accessor = new ProjectVariableExecutionContextAccessor();
        using var scope = accessor.BeginScope(new ProjectVariableExecutionContext(session, ProjectVariableBindingIndex.Build(schema), Guid.NewGuid()));

        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001,98.5,OK")));
        var tcpOperator = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var tcpOp = new Operator("tcp-delimited-parse", OperatorType.TcpCommunication, 0, 0);
        tcpOp.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        tcpOp.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Delimited", "string"));
        tcpOp.AddParameter(TestHelpers.CreateParameter("ResponseDelimiter", ",", "string"));
        tcpOp.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        tcpOp.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Score", "string"));

        var tcpResult = await tcpOperator.ExecuteAsync(tcpOp, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        tcpResult.IsSuccess.Should().BeTrue(tcpResult.ErrorMessage);
        tcpResult.OutputData!["RequestPayload"].Should().Be("READ");
        tcpResult.OutputData!["ParseSuccess"].Should().Be(true);
        tcpResult.OutputData["ParsedValue"].Should().Be(98.5);

        var writeOperator = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), new VariableContext(), accessor);
        var writeOp = new Operator("write-score", OperatorType.VariableWrite, 0, 0);
        writeOp.AddParameter(TestHelpers.CreateParameter("Scope", "Project", "enum"));
        writeOp.AddParameter(TestHelpers.CreateParameter("VariableId", scoreVariableId.ToString(), "string"));
        writeOp.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastScore", "string"));
        writeOp.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        writeOp.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.Score", "string"));

        var writeResult = await writeOperator.ExecuteAsync(writeOp, tcpResult.OutputData);

        writeResult.IsSuccess.Should().BeTrue(writeResult.ErrorMessage);
        writeResult.OutputData!["Value"].Should().Be(98.5);
        writeResult.OutputData["Version"].Should().Be(1L);
        session.TryGetSnapshot(scoreVariableId, out var scoreSnapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(scoreSnapshot.Value).Should().Be(98.5);

        var readOperator = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), new VariableContext(), accessor);
        var readThresholdOp = new Operator("read-threshold", OperatorType.VariableRead, 0, 0);
        readThresholdOp.AddParameter(TestHelpers.CreateParameter("Scope", "Project", "enum"));
        readThresholdOp.AddParameter(TestHelpers.CreateParameter("VariableId", thresholdVariableId.ToString(), "string"));
        readThresholdOp.AddParameter(TestHelpers.CreateParameter("VariableName", "threshold.score", "string"));
        readThresholdOp.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));

        var thresholdResult = await readOperator.ExecuteAsync(readThresholdOp);

        thresholdResult.IsSuccess.Should().BeTrue(thresholdResult.ErrorMessage);
        thresholdResult.OutputData!["Value"].Should().Be(98.0);
        thresholdResult.OutputData["Exists"].Should().Be(true);

        var branchOperator = new ConditionalBranchOperator(Substitute.For<ILogger<ConditionalBranchOperator>>());
        var branchOp = new Operator("judge-score", OperatorType.ConditionalBranch, 0, 0);
        branchOp.AddParameter(TestHelpers.CreateParameter("FieldName", "ParsedFields.Score", "string"));
        branchOp.AddParameter(TestHelpers.CreateParameter("CompareFieldName", "Value", "string"));
        branchOp.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        branchOp.AddParameter(TestHelpers.CreateParameter("FailOnMissingField", true, "bool"));

        var branchResult = await branchOperator.ExecuteAsync(branchOp, new Dictionary<string, object>
        {
            ["Value"] = tcpResult.OutputData,
            ["Compare"] = thresholdResult.OutputData
        });

        branchResult.IsSuccess.Should().BeTrue(branchResult.ErrorMessage);
        branchResult.OutputData!["Result"].Should().Be(true);
        branchResult.OutputData["ActualValue"].Should().Be(98.5);
        branchResult.OutputData["ActualSource"].Should().Be("Field");
        branchResult.OutputData["CompareValue"].Should().Be("98");
        branchResult.OutputData["CompareSource"].Should().Be("InputField");
        branchResult.OutputData["True"].Should().BeSameAs(tcpResult.OutputData);
    }

    [Fact]
    public async Task ExecuteAsync_WithDelimitedMissingNamedField_ShouldExposeParseFailure()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001,98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-delimited-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Delimited", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseDelimiter", ",", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Status", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(false);
        result.OutputData["ParseError"].Should().Be("Response field 'Status' was not found.");
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields["Score"].Should().Be(98.5);
        fields.Should().NotContainKey("Status");
    }

    [Fact]
    public async Task ExecuteAsync_WithRequiredDelimitedFieldsAndShortResponse_ShouldExposeMissingFields()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001,98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-delimited-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Delimited", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseDelimiter", ",", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RequiredResponseFields", "Serial,Score,Status", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(false);
        result.OutputData["ParseError"].Should().Be("Required response fields were not found: Status.");
        var missingFields = result.OutputData["MissingResponseFields"].Should().BeAssignableTo<IReadOnlyList<string>>().Subject;
        missingFields.Should().BeEquivalentTo("Status");
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields["Score"].Should().Be(98.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithRequiredKeyValueFields_ShouldRemainParseSuccess()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "code=OK;score=98.5;status=PASS")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-kv-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RequiredResponseFields", "code,score,status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "score", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(98.5);
        var missingFields = result.OutputData["MissingResponseFields"].Should().BeAssignableTo<IReadOnlyList<string>>().Subject;
        missingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithFailOnParseError_ShouldReturnFailureWithDiagnosticOutputs()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001,98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-delimited-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Delimited", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseDelimiter", ",", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnParseError", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Response field 'Status' was not found.");
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Status"].Should().Be(false);
        result.OutputData["RequestPayload"].Should().Be("READ");
        result.OutputData["Response"].Should().Be("SN001,98.5");
        result.OutputData["ParseSuccess"].Should().Be(false);
        result.OutputData["ParseError"].Should().Be("Response field 'Status' was not found.");
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields["Score"].Should().Be(98.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithFixedWidthResponseParse_ShouldExposeNamedFields()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN001098.5OK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-fixed-width-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "FixedWidth", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldWidths", "5,5,2", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Score", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(98.5);
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields["Score"].Should().Be(98.5);
        fields["Status"].Should().Be("OK");
        fields["1"].Should().Be(98.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithShortFixedWidthResponse_ShouldExposeParseFailure()
    {
        var manager = CreateProfileManager();
        manager
            .SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "SN00198")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-fixed-width-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "FixedWidth", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldWidths", "5,5,2", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldNames", "Serial,Score,Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "Status", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ParseSuccess"].Should().Be(false);
        result.OutputData["ParseError"].Should().Be("Fixed-width response is shorter than configured field widths at index 1.");
        var fields = result.OutputData["ParsedFields"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        fields["Serial"].Should().Be("SN001");
        fields.Should().NotContainKey("Status");
    }

    [Fact]
    public void ValidateParameters_WithInvalidRegexResponseParsePattern_ShouldReturnInvalid()
    {
        var op = new Operator("tcp-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "Regex", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseRegexPattern", "(", "string"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("ResponseRegexPattern", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateParameters_WithInvalidResponseMatchRegex_ShouldReturnInvalid()
    {
        var op = new Operator("tcp-match", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Regex", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectedResponse", "(", "string"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("ExpectedResponse", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateParameters_WithInvalidFixedWidthResponseFieldWidths_ShouldReturnInvalid()
    {
        var op = new Operator("tcp-parse", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "FixedWidth", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldWidths", "5,0,A", "string"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("ResponseFieldWidths", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithRawIpPortAndValidProfile_ShouldFailBeforeAnyDispatch()
    {
        var manager = CreateProfileManager();
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-raw-target", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("IpAddress", "203.0.113.77", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Port", 9100, "int"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Data"] = "PING" });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("TCP_RAW_ENDPOINT_FORBIDDEN:");
        await manager.DidNotReceive().GetConfigAsync(Arg.Any<CancellationToken>());
        await AssertNoSendAsync(manager);
    }

    [Fact]
    public async Task ExecuteAsync_WithAuthoritativeProfileResponse_ShouldReceiveFullResponseAndParse()
    {
        var manager = CreateProfileManager();
        manager.SendAsync(
                "robot-main",
                Arg.Any<TcpDeviceSendRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK:OK;score=98.5")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-complete", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseTimeoutMs", 2500, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectedResponse", "ACK:OK", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseMatchMode", "Contains", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnUnexpectedResponse", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseParseMode", "KeyValue", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResponseFieldName", "score", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "READ"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        GetResponse(result).Should().Be("ACK:OK;score=98.5");
        result.OutputData!["ResponseAccepted"].Should().Be(true);
        result.OutputData["ParseSuccess"].Should().Be(true);
        result.OutputData["ParsedValue"].Should().Be(98.5);
        await manager.Received(1).SendAsync(
            "robot-main",
            Arg.Is<TcpDeviceSendRequest>(request => request.Payload == "READ" && request.WaitResponse),
            Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(
            Arg.Any<TcpCommunicationProfile>(),
            Arg.Any<TcpDeviceSendRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownProfile_ShouldFailBeforeAnyDispatch()
    {
        var manager = CreateProfileManager();
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-forged-profile", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "forged-profile", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Data"] = "PING" });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("TCP_PROFILE_NOT_FOUND:");
        await manager.Received(1).GetConfigAsync(Arg.Any<CancellationToken>());
        await AssertNoSendAsync(manager);
    }

    [Fact]
    public async Task ExecuteAsync_WithDisabledProfile_ShouldFailBeforeAnyDispatch()
    {
        var manager = CreateProfileManager(CreateProfile(enabled: false));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-disabled-profile", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Data"] = "PING" });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("TCP_PROFILE_DISABLED_OR_INVALID:");
        await manager.Received(1).GetConfigAsync(Arg.Any<CancellationToken>());
        await AssertNoSendAsync(manager);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidProfile_ShouldFailBeforeAnyDispatch()
    {
        var invalidProfile = CreateProfile();
        invalidProfile.RemotePort = 0;
        var manager = CreateProfileManager(invalidProfile);
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-invalid-profile", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Data"] = "PING" });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("TCP_PROFILE_DISABLED_OR_INVALID:");
        await manager.Received(1).GetConfigAsync(Arg.Any<CancellationToken>());
        await AssertNoSendAsync(manager);
    }

    [Fact]
    public async Task ExecuteAsync_WithDuplicateProfileId_ShouldFailBeforeAnyDispatch()
    {
        var manager = CreateProfileManager(CreateProfile(), CreateProfile());
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-duplicate-profile", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Data"] = "PING" });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("TCP_PROFILE_AMBIGUOUS:");
        await manager.Received(1).GetConfigAsync(Arg.Any<CancellationToken>());
        await AssertNoSendAsync(manager);
    }

    private static string GetResponse(OperatorExecutionOutput result)
    {
        result.OutputData.Should().NotBeNull();
        var outputData = result.OutputData!;
        outputData.Should().ContainKey("Response");
        return outputData["Response"].Should().BeOfType<string>().Subject;
    }

    private static ITcpDeviceManager CreateProfileManager(params TcpCommunicationProfile[] profiles)
    {
        if (profiles.Length == 0)
        {
            profiles = [CreateProfile()];
        }

        var manager = Substitute.For<ITcpDeviceManager>();
        manager.GetConfigAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TcpCommunicationConfig
            {
                Profiles = profiles.ToList()
            }));
        return manager;
    }

    private static TcpCommunicationProfile CreateProfile(
        string id = "robot-main",
        bool enabled = true)
    {
        return new TcpCommunicationProfile
        {
            Id = id,
            Name = "Robot",
            Enabled = enabled,
            Mode = TcpCommunicationProfile.ModeClient,
            RemoteHost = "192.0.2.10",
            RemotePort = 9100,
            TimeoutMs = 5000
        };
    }

    private static async Task AssertNoSendAsync(ITcpDeviceManager manager)
    {
        await manager.DidNotReceive().SendAsync(
            Arg.Any<string>(),
            Arg.Any<TcpDeviceSendRequest>(),
            Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(
            Arg.Any<TcpCommunicationProfile>(),
            Arg.Any<TcpDeviceSendRequest>(),
            Arg.Any<CancellationToken>());
    }
}
