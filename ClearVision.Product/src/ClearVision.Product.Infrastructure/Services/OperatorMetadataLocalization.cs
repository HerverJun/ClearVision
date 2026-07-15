using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// Compatibility hook retained for the existing factory composition.
/// Operator identity now comes directly from <c>OperatorMetaAttribute</c>.
/// </summary>
internal static class OperatorMetadataLocalization
{
    public static void Apply(IEnumerable<OperatorMetadata> metadataItems)
    {
        ArgumentNullException.ThrowIfNull(metadataItems);
    }
}
