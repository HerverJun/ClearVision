using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Core.Attributes;

/// <summary>
/// Declares the authoritative provider for an operator's image input domain contracts.
/// The provider is consumed by metadata scanning, runtime admission, generated docs,
/// the UI/application catalog, the AI read-only catalog, and OperatorLibrary adapters.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OperatorImageContractProviderAttribute : Attribute
{
    public OperatorImageContractProviderAttribute(Type providerType)
    {
        ProviderType = providerType ?? throw new ArgumentNullException(nameof(providerType));
    }

    public Type ProviderType { get; }
}
