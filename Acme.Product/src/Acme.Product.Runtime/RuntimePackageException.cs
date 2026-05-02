using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Runtime;

public sealed class RuntimePackageException : Exception
{
    public RuntimePackageException(string message)
        : base(message)
    {
    }

    public RuntimePackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public RuntimePackageValidationResult? ValidationResult { get; init; }
}
