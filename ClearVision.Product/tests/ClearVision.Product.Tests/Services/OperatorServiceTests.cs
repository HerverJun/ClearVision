using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using NSubstitute;
using ClearVision.Product.Tests.TestSupport;

namespace ClearVision.Product.Tests.Services;

public class OperatorServiceTests
{
    [Fact]
    public async Task GetLibraryAsync_ShouldIncludeLegacyAndFactoryBackfilledOperators()
    {
        var sut = CreateSut();

        var library = (await sut.GetLibraryAsync()).ToList();

        library.Should().Contain(item => item.Type == "ImageAcquisition");
        library.Should().Contain(item => item.Type == nameof(OperatorType.AnomalyDetection));
        library.Should().Contain(item => item.Type == nameof(OperatorType.DetectionSequenceJudge));
    }

    [Fact]
    public async Task GetMetadataAsync_ForAnomalyDetection_ShouldExposeFactoryParameters()
    {
        var sut = CreateSut();

        var metadata = await sut.GetMetadataAsync(OperatorType.AnomalyDetection);

        metadata.Should().NotBeNull();
        metadata!.Parameters.Should().Contain(parameter => parameter.Name == "FeatureExtractorId");
        metadata.Outputs.Should().Contain(output => output.Name == "Diagnostics");
    }

    [Fact]
    public async Task CreateAsync_WhenParameterIsInvalid_ShouldLogWarningAndContinue()
    {
        var repository = Substitute.For<IOperatorRepository>();
        var factory = new OperatorFactory();
        var logger = new RecordingLogger<OperatorService>();
        var sut = new OperatorService(repository, factory, logger);

        var created = await sut.CreateAsync(new CreateOperatorRequest
        {
            Type = OperatorType.ImageAcquisition,
            Name = "camera",
            Parameters =
            [
                new ParameterRequest { Name = "NotAParameter", Value = "value" }
            ]
        });

        created.Should().NotBeNull();
        logger.Entries.Should().Contain(entry =>
            entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            entry.Message.Contains("Ignored invalid operator parameter"));
    }

    [Fact]
    public async Task UpdateAsync_WhenParameterValueCannotSerialize_ShouldLogWarningAndContinue()
    {
        var id = Guid.NewGuid();
        var repository = Substitute.For<IOperatorRepository>();
        var factory = new OperatorFactory();
        var logger = new RecordingLogger<OperatorService>();
        var sut = new OperatorService(repository, factory, logger);
        var entity = new Operator(id, "camera", OperatorType.ImageAcquisition, 0, 0);
        entity.AddParameter(new Parameter(
            Guid.NewGuid(),
            "Payload",
            "Payload",
            string.Empty,
            "object",
            defaultValue: "initial"));
        repository.GetByIdAsync(id).Returns(entity);

        var cyclicValue = new CyclicValue();
        cyclicValue.Next = cyclicValue;

        var updated = await sut.UpdateAsync(id, new UpdateOperatorRequest
        {
            Parameters =
            [
                new ParameterRequest { Name = "Payload", Value = cyclicValue }
            ]
        });

        updated.Should().NotBeNull();
        await repository.Received(1).UpdateAsync(entity);
        logger.Entries.Should().Contain(entry =>
            entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            entry.Message.Contains("Ignored invalid operator parameter while updating"));
    }

    private static OperatorService CreateSut()
    {
        var repository = Substitute.For<IOperatorRepository>();
        var factory = new OperatorFactory();
        return new OperatorService(repository, factory);
    }

    private sealed class CyclicValue
    {
        public CyclicValue? Next { get; set; }
    }
}
