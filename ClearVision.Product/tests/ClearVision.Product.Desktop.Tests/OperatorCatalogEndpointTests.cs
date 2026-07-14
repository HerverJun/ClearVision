using ClearVision.Product.Core.Enums;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class OperatorCatalogEndpointTests
{
    [Fact]
    public void OperatorEndpointMetadata_ShouldHideCompatibilityByDefaultAndKeepStableOrdering()
    {
        var allMetadata = new OperatorFactory().GetAllMetadata().ToList();

        var defaultItems = ApiEndpoints.GetOperatorEndpointMetadata(allMetadata, includeCompatibility: false);
        var compatibilityItems = ApiEndpoints.GetOperatorEndpointMetadata(allMetadata, includeCompatibility: true);

        defaultItems.Should().OnlyContain(item => !item.DefaultHidden);
        defaultItems.Select(item => item.Type).Should().NotContain(OperatorType.Morphology);
        compatibilityItems.Select(item => item.Type).Should().Contain(OperatorType.Morphology);
        compatibilityItems.Should().HaveCount(allMetadata.Count);

        var expectedOrder = compatibilityItems
            .OrderBy(item => OperatorCategoryCatalog.GetOrder(item.CategoryId))
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .Select(item => item.Type);
        compatibilityItems.Select(item => item.Type).Should().Equal(expectedOrder);
    }
}
