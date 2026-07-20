using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class ModbusCommunicationOperatorTests
{
    private readonly ModbusCommunicationOperator _operator;

    public ModbusCommunicationOperatorTests()
    {
        _operator = new ModbusCommunicationOperator(Substitute.For<ILogger<ModbusCommunicationOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeModbusCommunication()
    {
        _operator.OperatorType.Should().Be(OperatorType.ModbusCommunication);
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("test", OperatorType.ModbusCommunication, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithInvalidPort_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.ModbusCommunication, 0, 0);
        op.AddParameter(new(Guid.NewGuid(), "Port", "Port", "", "int", 70000, 0, 65535, true));
        var result = _operator.ValidateParameters(op);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithInvalidSlaveId_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.ModbusCommunication, 0, 0);
        op.AddParameter(new(Guid.NewGuid(), "SlaveId", "SlaveId", "", "int", 256, 0, 255, true));
        var result = _operator.ValidateParameters(op);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithTcpGatewayUnitId255_ShouldBeValid()
    {
        var op = new Operator("test", OperatorType.ModbusCommunication, 0, 0);
        op.AddParameter(new(Guid.NewGuid(), "SlaveId", "SlaveId", "", "int", 255, 1, 255, true));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithUnsupportedRtuMode_ShouldReturnFailure()
    {
        var op = new Operator("test", OperatorType.ModbusCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Protocol", "RTU", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WithFailedTcpConnect_ShouldNotRetainPoolsOrKeyLocks()
    {
        ResetStaticModbusState();
        var port = GetUnusedLoopbackPort();
        var op = new Operator("test", OperatorType.ModbusCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Protocol", "TCP", "string"));
        op.AddParameter(TestHelpers.CreateParameter("IpAddress", "127.0.0.1", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Port", port, "int"));
        op.AddParameter(TestHelpers.CreateParameter("TimeoutMs", 100, "int"));

        try
        {
            var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

            result.IsSuccess.Should().BeFalse();
            GetStaticDictionaryCount("ConnectionPool").Should().Be(0);
            GetStaticDictionaryCount("MasterPool").Should().Be(0);
            GetStaticDictionaryCount("ConnectionLocks").Should().Be(0);
            GetStaticDictionaryCount("OperationLocks").Should().Be(0);
            GetStaticDictionaryCount("ConnectionLastUsed").Should().Be(0);
            GetStaticDictionaryCount("ActiveOperations").Should().Be(0);
        }
        finally
        {
            ResetStaticModbusState();
        }
    }

    private static int GetUnusedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static int GetStaticDictionaryCount(string fieldName)
    {
        var field = typeof(ModbusCommunicationOperator).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        var dictionary = field!.GetValue(null) as IDictionary;
        dictionary.Should().NotBeNull();
        return dictionary!.Count;
    }

    private static void ResetStaticModbusState()
    {
        ResetStaticDictionary("ConnectionPool");
        ResetStaticDictionary("MasterPool");
        ResetStaticDictionary("ConnectionLocks");
        ResetStaticDictionary("OperationLocks");
        ResetStaticDictionary("ConnectionLastUsed");
        ResetStaticDictionary("ActiveOperations");
    }

    private static void ResetStaticDictionary(string fieldName)
    {
        var field = typeof(ModbusCommunicationOperator).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        if (field!.GetValue(null) is not IDictionary dictionary)
        {
            return;
        }

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        dictionary.Clear();
    }
}
