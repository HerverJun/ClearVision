using ClearVision.PlcComm.Core;
using ClearVision.PlcComm.Mitsubishi;
using ClearVision.PlcComm.Omron;
using ClearVision.PlcComm.Siemens;
using FluentAssertions;

namespace ClearVision.Product.Tests.PlcComm;

[TestClassification(TestDomain.Plc, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "plc", Suites = "PlcRegression")]
public class AddressParserTests
{
    [Fact]
    public void S7AddressParser_ShouldParse_DbBitAddress()
    {
        var parser = new S7AddressParser();

        var result = parser.Parse("DB1.DBX10.3");

        result.IsSuccess.Should().BeTrue(result.Message);
        result.Content.Should().NotBeNull();
        result.Content!.AreaType.Should().Be("DB");
        result.Content.DbNumber.Should().Be(1);
        result.Content.StartAddress.Should().Be(10);
        result.Content.BitOffset.Should().Be(3);
        result.Content.DataType.Should().Be(PlcDataType.Bit);
    }

    [Fact]
    public void S7AddressParser_ShouldParse_MWAddress()
    {
        var parser = new S7AddressParser();

        var result = parser.Parse("MW0");

        result.IsSuccess.Should().BeTrue(result.Message);
        result.Content.Should().NotBeNull();
        result.Content!.AreaType.Should().Be("M");
        result.Content.StartAddress.Should().Be(0);
        result.Content.BitOffset.Should().Be(-1);
        result.Content.DataType.Should().Be(PlcDataType.Word);
    }

    [Theory]
    [InlineData("D100", 100, PlcDataType.Word, 0xA8)]
    [InlineData("X10", 16, PlcDataType.Bit, 0x9C)]
    [InlineData("B1F", 31, PlcDataType.Bit, 0xA0)]
    [InlineData("CS10", 10, PlcDataType.Bit, 0xC4)]
    [InlineData("CC10", 10, PlcDataType.Bit, 0xC3)]
    [InlineData("CN10", 10, PlcDataType.Word, 0xC5)]
    public void McAddressParser_ShouldParse_ExpectedFormats(
        string address,
        int expectedStartAddress,
        PlcDataType expectedDataType,
        byte expectedDeviceCode)
    {
        var parser = new McAddressParser();

        var result = parser.Parse(address);

        result.IsSuccess.Should().BeTrue(result.Message);
        result.Content.Should().NotBeNull();
        result.Content!.StartAddress.Should().Be(expectedStartAddress);
        result.Content.DataType.Should().Be(expectedDataType);
        result.Content.DeviceCode.Should().Be(expectedDeviceCode);
    }

    [Theory]
    [InlineData("DB1.DBX10")]
    [InlineData("DB1.DBW10.3")]
    [InlineData("M0.8")]
    public void S7AddressParser_ShouldReject_InvalidBitFormats(string address)
    {
        var parser = new S7AddressParser();

        var result = parser.Parse(address);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void FinsAddressParser_ShouldParse_DmWordAddress()
    {
        var parser = new FinsAddressParser();

        var result = parser.Parse("DM100");

        result.IsSuccess.Should().BeTrue(result.Message);
        result.Content.Should().NotBeNull();
        result.Content!.AreaType.Should().Be("DM");
        result.Content.StartAddress.Should().Be(100);
        result.Content.BitOffset.Should().Be(-1);
        result.Content.DataType.Should().Be(PlcDataType.Word);
        result.Content.DeviceCode.Should().Be(0x82);
    }

    [Fact]
    public void FinsAddressParser_ShouldParse_CioBitAddress()
    {
        var parser = new FinsAddressParser();

        var result = parser.Parse("CIO10.3");

        result.IsSuccess.Should().BeTrue(result.Message);
        result.Content.Should().NotBeNull();
        result.Content!.AreaType.Should().Be("CIO");
        result.Content.StartAddress.Should().Be(10);
        result.Content.BitOffset.Should().Be(3);
        result.Content.DataType.Should().Be(PlcDataType.Bit);
        result.Content.DeviceCode.Should().Be(0x30);
    }

    [Fact]
    public void FinsAddressParser_ShouldParse_EmBankAddress()
    {
        var parser = new FinsAddressParser();

        var result = parser.Parse("EM1 100");

        result.IsSuccess.Should().BeTrue(result.Message);
        result.Content.Should().NotBeNull();
        result.Content!.AreaType.Should().Be("EM");
        result.Content.DbNumber.Should().Be(1);
        result.Content.StartAddress.Should().Be(100);
        result.Content.BitOffset.Should().Be(-1);
        result.Content.DataType.Should().Be(PlcDataType.Word);
        result.Content.DeviceCode.Should().Be(0xA1);
    }

    [Theory]
    [InlineData("EM1.100")]
    [InlineData("E1.100")]
    [InlineData("EM1 100")]
    public void FinsAddressParser_ShouldParse_EmBankAddressAliases(string address)
    {
        var parser = new FinsAddressParser();

        var result = parser.Parse(address);

        result.IsSuccess.Should().BeTrue(result.Message);
        result.Content.Should().NotBeNull();
        result.Content!.AreaType.Should().Be("EM");
        result.Content.DbNumber.Should().Be(1);
        result.Content.StartAddress.Should().Be(100);
        result.Content.DeviceCode.Should().Be(0xA1);
    }

    [Fact]
    public void FinsAddressParser_ShouldParse_DAliasAsDm()
    {
        var parser = new FinsAddressParser();

        var result = parser.Parse("D100");

        result.IsSuccess.Should().BeTrue(result.Message);
        result.Content.Should().NotBeNull();
        result.Content!.AreaType.Should().Be("DM");
        result.Content.DeviceCode.Should().Be(0x82);
    }
}
