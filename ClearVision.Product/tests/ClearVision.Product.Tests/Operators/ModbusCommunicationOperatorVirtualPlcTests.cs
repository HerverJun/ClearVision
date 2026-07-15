using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Trait("Category", "VirtualPLC")]
public class ModbusCommunicationOperatorVirtualPlcTests
{
    private readonly ModbusCommunicationOperator _operator;

    public ModbusCommunicationOperatorVirtualPlcTests()
    {
        _operator = new ModbusCommunicationOperator(Substitute.For<ILogger<ModbusCommunicationOperator>>());
    }

    [Fact]
    public async Task ReadHolding_HR10_ShouldReturnCurrentValue()
    {
        if (!ShouldRunVirtualPlcTests())
        {
            return;
        }

        await ExecuteAsync("WriteSingle", 10, writeValue: "1234");

        var response = await ExecuteAsync("ReadHolding", 10);

        ParseRegisters(response).Should().Equal(1234);
    }

    [Fact]
    public async Task WriteSingle_HR10_ShouldPersistValue()
    {
        if (!ShouldRunVirtualPlcTests())
        {
            return;
        }

        var writeResponse = await ExecuteAsync("WriteSingle", 10, writeValue: "5678");
        writeResponse.Should().Be("Write succeeded: 5678");

        var readResponse = await ExecuteAsync("ReadHolding", 10);
        ParseRegisters(readResponse).Should().Equal(5678);
    }

    [Fact]
    public async Task WriteMultiple_HR20ToHR22_ShouldPersistValues()
    {
        if (!ShouldRunVirtualPlcTests())
        {
            return;
        }

        var writeResponse = await ExecuteAsync("WriteMultiple", 20, registerCount: 3, writeValue: "101,202,303");
        writeResponse.Should().Be("Write succeeded: 3 registers");

        var readResponse = await ExecuteAsync("ReadHolding", 20, registerCount: 3);
        ParseRegisters(readResponse).Should().Equal(101, 202, 303);
    }

    [Fact]
    public async Task Handshake_ShouldAckEchoSequenceAndReset()
    {
        if (!ShouldRunVirtualPlcTests())
        {
            return;
        }

        await ExecuteAsync("WriteSingle", 0, writeValue: "9");
        await WaitForRegisterAsync(1, 0);

        await ExecuteAsync("WriteSingle", 2, writeValue: "123");
        await ExecuteAsync("WriteSingle", 0, writeValue: "1");
        await WaitForRegisterAsync(1, 200);

        var echoResponse = await ExecuteAsync("ReadHolding", 3);
        ParseRegisters(echoResponse).Should().Equal(123);

        await ExecuteAsync("WriteSingle", 0, writeValue: "9");
        await WaitForRegisterAsync(1, 0);
    }

    [Fact]
    public async Task WriteSingle_WithWiredDataInput_ShouldPersistUpstreamValue()
    {
        if (!ShouldRunVirtualPlcTests())
        {
            return;
        }

        // Data 输入端口连线的动态值应覆盖参数面板中的静态 WriteValue，实现"视觉结果驱动写入"。
        var writeResponse = await ExecuteAsync(
            "WriteSingle",
            10,
            writeValue: "1",
            inputs: new Dictionary<string, object> { ["Data"] = "4321" });
        writeResponse.Should().Be("Write succeeded: 4321");

        var readResponse = await ExecuteAsync("ReadHolding", 10);
        ParseRegisters(readResponse).Should().Equal(4321);
    }

    private async Task<string> ExecuteAsync(
        string functionCode,
        int registerAddress,
        int registerCount = 1,
        string writeValue = "",
        Dictionary<string, object>? inputs = null)
    {
        var op = new Operator("virtual-plc-modbus", OperatorType.ModbusCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Protocol", "TCP", "string"));
        op.AddParameter(TestHelpers.CreateParameter("IpAddress", GetHost(), "string"));
        op.AddParameter(TestHelpers.CreateParameter("Port", GetPort(), "int"));
        op.AddParameter(TestHelpers.CreateParameter("SlaveId", GetUnitId(), "int"));
        op.AddParameter(TestHelpers.CreateParameter("RegisterAddress", registerAddress, "int"));
        op.AddParameter(TestHelpers.CreateParameter("RegisterCount", registerCount, "int"));
        op.AddParameter(TestHelpers.CreateParameter("FunctionCode", functionCode, "string"));
        op.AddParameter(TestHelpers.CreateParameter("WriteValue", writeValue, "string"));
        op.AddParameter(TestHelpers.CreateParameter("TimeoutMs", 5000, "int"));

        var result = await _operator.ExecuteAsync(op, inputs ?? new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var outputData = result.OutputData ?? throw new InvalidOperationException("Modbus operator returned no output data.");
        outputData.Should().ContainKey("Response");

        return Convert.ToString(outputData["Response"]) ?? string.Empty;
    }

    private async Task WaitForRegisterAsync(int address, int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            var response = await ExecuteAsync("ReadHolding", address);
            var value = ParseRegisters(response).Single();

            if (value == expected)
            {
                return;
            }

            if (address == 1 && value == 500)
            {
                var errorCodeResponse = await ExecuteAsync("ReadHolding", 4);
                var errorCode = ParseRegisters(errorCodeResponse).Single();
                throw new InvalidOperationException($"Virtual PLC returned error status 500, error code {errorCode}.");
            }

            await Task.Delay(100, timeout.Token);
        }

        throw new TimeoutException($"Timed out waiting for HR{address}={expected}.");
    }

    private static ushort[] ParseRegisters(string response)
    {
        return response
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ushort.Parse)
            .ToArray();
    }

    private static bool ShouldRunVirtualPlcTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("CLEARVISION_RUN_VIRTUAL_PLC_TESTS"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHost()
    {
        return Environment.GetEnvironmentVariable("CLEARVISION_VIRTUAL_MODBUS_HOST") ?? "127.0.0.1";
    }

    private static int GetPort()
    {
        return TryGetIntEnvironmentVariable("CLEARVISION_VIRTUAL_MODBUS_PORT", 1502);
    }

    private static int GetUnitId()
    {
        return TryGetIntEnvironmentVariable("CLEARVISION_VIRTUAL_MODBUS_UNIT_ID", 1);
    }

    private static int TryGetIntEnvironmentVariable(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) ? value : fallback;
    }
}
