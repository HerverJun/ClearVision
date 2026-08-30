using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Repositories;
using FluentAssertions;

namespace ClearVision.Product.Tests.Repositories;

[TestClassification(TestDomain.Data, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "data-platform")]
public class LruImageCacheRepositoryTests
{
    [Fact]
    public async Task AddResultAsync_ShouldRetainProjectAndResultAuthority()
    {
        var cache = new LruImageCacheRepository();
        var authority = new ResultImageCacheAuthority(Guid.NewGuid(), Guid.NewGuid());
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var imageId = await cache.AddResultAsync(bytes, "png", authority);
        var entry = await cache.GetEntryAsync(imageId);

        entry.Should().NotBeNull();
        entry!.Data.Should().Equal(bytes);
        entry.Format.Should().Be("png");
        entry.Authority.Should().Be(authority);
    }

    [Fact]
    public async Task AddAsync_ForUploadImage_ShouldRemainUnbound()
    {
        var cache = new LruImageCacheRepository();

        var imageId = await cache.AddAsync(new byte[] { 1, 2, 3 }, "png");
        var entry = await cache.GetEntryAsync(imageId);

        entry.Should().NotBeNull();
        entry!.Authority.Should().BeNull();
    }

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
