using Xunit;

namespace ClearVision.Product.Tests.TestSupport;

public static class PointCloudMatchingTestCollections
{
    public const string PointCloudMatching = "PointCloudMatching";
}

[CollectionDefinition(PointCloudMatchingTestCollections.PointCloudMatching, DisableParallelization = true)]
public sealed class PointCloudMatchingCollectionDefinition
{
}
