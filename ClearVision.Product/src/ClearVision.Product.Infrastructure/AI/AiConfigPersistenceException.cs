namespace ClearVision.Product.Infrastructure.AI;

public sealed class AiConfigPersistenceException : IOException
{
    public AiConfigPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
