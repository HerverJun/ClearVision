using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Services;

public sealed class OperatorPreviewServiceAdmissionTests
{
    [Fact]
    public async Task PreviewAsync_WithSideEffectOperator_ShouldReturnBlockedWithoutExecuting()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var admission = new ExecutionAdmissionService(Substitute.For<IProjectRepository>());
        using var image = new Mat(4, 4, MatType.CV_8UC3, Scalar.All(0));
        var service = new OperatorPreviewService(
            new OperatorFactory(),
            flowExecution,
            admission,
            NullLogger<OperatorPreviewService>.Instance);

        var result = await service.PreviewAsync(OperatorType.TextSave, parameters: null, image, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("OperatorPreviewSideEffectBlocked");
        result.ErrorMessage.Should().Contain("TextSave");
        await flowExecution.DidNotReceiveWithAnyArgs().ExecuteOperatorAsync(
            Arg.Any<Operator>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }
}
