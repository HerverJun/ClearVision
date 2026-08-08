using ClearVision.Product.Infrastructure.Repositories;
using FluentAssertions;

namespace ClearVision.Product.Tests.Repositories;

[TestClassification(TestDomain.Data, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "data-platform")]
public class LruImageCacheRepositoryTests
{
    [Fact]
    public async Task AddAsync_WithoutBackgroundDrain_ShouldKeepRetainedImagesWithinCacheBudget()
    {
        const int maxCacheBytes = 1024;
        const int imageBytes = 128;
        var cache = new LruImageCacheRepository(maxCacheBytes, queueCapacity: 512);
        var ids = new List<Guid>();

        for (var index = 0; index < 20; index++)
        {
            var image = Enumerable.Repeat((byte)index, imageBytes).ToArray();
            ids.Add(await cache.AddAsync(image, "png"));
        }

        var statistics = cache.GetStatistics();

        statistics.CurrentSizeInBytes.Should().BeLessThanOrEqualTo(maxCacheBytes);
        statistics.TotalEntries.Should().BeLessThanOrEqualTo(maxCacheBytes / imageBytes);
        (await cache.GetAsync(ids[0])).Should().BeNull("old images should be evicted when pending pressure reaches the budget");
        (await cache.GetAsync(ids[^1])).Should().NotBeNull("the latest inspection image should remain available for preview");
    }
}
