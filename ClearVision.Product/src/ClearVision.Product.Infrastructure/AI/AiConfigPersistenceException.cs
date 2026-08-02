namespace ClearVision.Product.Infrastructure.AI;

public sealed class AiConfigPersistenceException : IOException
{
    public AiConfigPersistenceException(
        string message,
        Exception innerException,
        bool rollbackSucceeded = false)
        : base(message, innerException)
    {
        RollbackSucceeded = rollbackSucceeded;
    }

    public bool RollbackSucceeded { get; }
}
