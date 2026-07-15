using ClearVision.PlcComm.Core;
using ClearVision.PlcComm.Siemens;
using FluentAssertions;

namespace ClearVision.Product.Tests.PlcComm;

[TestClassification(TestDomain.Plc, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "plc", Suites = "PlcRegression")]
public class SiemensS7ClientBehaviorTests
{
    [Fact]
    public async Task ReadCoreAsync_WordAddress_ShouldRequestTwoBytesForSingleElement()
    {
        var sut = new TestableSiemensS7Client();
        sut.EnqueueReadResponse(0x12, 0x34);

        var result = await sut.ReadCorePublicAsync("MW0", 1);

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Equal(0x12, 0x34);
        sut.LastReadByteCount.Should().Be(2);
        sut.LastReadAddress.Should().NotBeNull();
        sut.LastReadAddress!.AreaType.Should().Be("M");
        sut.LastReadAddress.StartAddress.Should().Be(0);
    }

    [Fact]
    public async Task ReadCoreAsync_FloatAddress_ShouldRequestFourBytesPerElement()
    {
        var sut = new TestableSiemensS7Client();
        sut.EnqueueReadResponse(0x41, 0x48, 0x00, 0x00);

        var result = await sut.ReadCorePublicAsync("DB1.DBR0", 1);

        result.IsSuccess.Should().BeTrue();
        sut.LastReadByteCount.Should().Be(4);
        sut.LastReadAddress.Should().NotBeNull();
        sut.LastReadAddress!.AreaType.Should().Be("DB");
        sut.LastReadAddress.DbNumber.Should().Be(1);
    }

    [Fact]
    public async Task ReadCoreAsync_BitAddress_ShouldReadSingleBool()
    {
        var sut = new TestableSiemensS7Client();
        sut.EnqueueBoolResponse(true);

        var result = await sut.ReadCorePublicAsync("DB1.DBX10.3", 1);

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Equal(0x01);
        sut.LastReadBoolLength.Should().Be(1);
        sut.LastReadAddress.Should().NotBeNull();
        sut.LastReadAddress!.BitOffset.Should().Be(3);
    }

    [Fact]
    public async Task ReadCoreAsync_BitAddressWithMultiLength_ShouldReadBoolArray()
    {
        var sut = new TestableSiemensS7Client();
        sut.EnqueueBoolResponse(true, false);

        var result = await sut.ReadCorePublicAsync("M10.3", 2);

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Equal(0x01, 0x00);
        sut.LastReadBoolLength.Should().Be(2);
    }

    [Fact]
    public async Task WriteCoreAsync_BitAddress_ShouldWriteBool()
    {
        var sut = new TestableSiemensS7Client();

        var result = await sut.WriteCorePublicAsync("M10.3", new byte[] { 0x01 });

        result.IsSuccess.Should().BeTrue();
        sut.LastWriteBoolAddress.Should().Be("M10.3");
        sut.LastWriteBoolValue.Should().BeTrue();
    }

    [Fact]
    public async Task WriteCoreAsync_BitAddress_ShouldWriteFalseBool()
    {
        var sut = new TestableSiemensS7Client();

        var result = await sut.WriteCorePublicAsync("M10.3", new byte[] { 0x00 });

        result.IsSuccess.Should().BeTrue();
        sut.LastWriteBoolAddress.Should().Be("M10.3");
        sut.LastWriteBoolValue.Should().BeFalse();
    }

    [Fact]
    public async Task WriteCoreAsync_WordAddress_ShouldWriteRawBytes()
    {
        var sut = new TestableSiemensS7Client();

        var result = await sut.WriteCorePublicAsync("MW0", new byte[] { 0x12, 0x34 });

        result.IsSuccess.Should().BeTrue();
        sut.LastWriteBytesAddress.Should().Be("MW0");
        sut.LastWriteBytes.Should().Equal(0x12, 0x34);
    }

    [Fact]
    public async Task PingCoreAsync_ShouldUseMerkerHeartbeatAddress()
    {
        var sut = new TestableSiemensS7Client();
        sut.EnqueueReadResponse(0x12, 0x34);

        var result = await sut.PingCorePublicAsync();

        result.Should().BeTrue();
        sut.LastReadAddress.Should().NotBeNull();
        sut.LastReadAddress!.AreaType.Should().Be("M");
        sut.LastReadAddress.StartAddress.Should().Be(0);
        sut.LastReadByteCount.Should().Be(2);
    }

    private sealed class TestableSiemensS7Client : SiemensS7Client
    {
        private readonly Queue<byte[]?> _readResponses = new();
        private readonly Queue<bool[]> _boolResponses = new();
        private readonly S7AddressParser _parser = new();

        public TestableSiemensS7Client()
            : base("127.0.0.1")
        {
        }

        public PlcAddress? LastReadAddress { get; private set; }
        public int LastReadByteCount { get; private set; }
        public ushort LastReadBoolLength { get; private set; }
        public string? LastWriteBoolAddress { get; private set; }
        public bool? LastWriteBoolValue { get; private set; }
        public string? LastWriteBytesAddress { get; private set; }
        public byte[]? LastWriteBytes { get; private set; }

        public void EnqueueReadResponse(params byte[] data)
        {
            _readResponses.Enqueue(data);
        }

        public void EnqueueBoolResponse(params bool[] data)
        {
            _boolResponses.Enqueue(data);
        }

        public Task<OperateResult<byte[]>> ReadCorePublicAsync(string address, ushort length)
        {
            return ReadCoreAsync(address, length, CancellationToken.None);
        }

        public Task<OperateResult> WriteCorePublicAsync(string address, byte[] value)
        {
            return WriteCoreAsync(address, value, CancellationToken.None);
        }

        public Task<bool> PingCorePublicAsync()
        {
            return PingCoreAsync(CancellationToken.None);
        }

        protected override Task<OperateResult<byte[]>> ReadBytesProtocolAsync(
            string address,
            ushort byteCount,
            CancellationToken ct)
        {
            LastReadAddress = _parser.Parse(address).Content;
            LastReadByteCount = byteCount;
            var payload = _readResponses.Count > 0 ? _readResponses.Dequeue() : Array.Empty<byte>();
            return Task.FromResult(OperateResult<byte[]>.Success(payload ?? Array.Empty<byte>()));
        }

        protected override Task<OperateResult<bool>> ReadBoolProtocolAsync(string address, CancellationToken ct)
        {
            LastReadAddress = _parser.Parse(address).Content;
            LastReadBoolLength = 1;
            var payload = _boolResponses.Count > 0 ? _boolResponses.Dequeue() : new[] { true };
            return Task.FromResult(OperateResult<bool>.Success(payload[0]));
        }

        protected override Task<OperateResult<bool[]>> ReadBoolProtocolAsync(
            string address,
            ushort length,
            CancellationToken ct)
        {
            LastReadAddress = _parser.Parse(address).Content;
            LastReadBoolLength = length;
            var payload = _boolResponses.Count > 0
                ? _boolResponses.Dequeue()
                : Enumerable.Repeat(true, length).ToArray();
            return Task.FromResult(OperateResult<bool[]>.Success(payload));
        }

        protected override Task<OperateResult> WriteBoolProtocolAsync(string address, bool value, CancellationToken ct)
        {
            LastWriteBoolAddress = address;
            LastWriteBoolValue = value;
            return Task.FromResult(OperateResult.Success());
        }

        protected override Task<OperateResult> WriteBytesProtocolAsync(string address, byte[] value, CancellationToken ct)
        {
            LastWriteBytesAddress = address;
            LastWriteBytes = value.ToArray();
            return Task.FromResult(OperateResult.Success());
        }
    }
}
