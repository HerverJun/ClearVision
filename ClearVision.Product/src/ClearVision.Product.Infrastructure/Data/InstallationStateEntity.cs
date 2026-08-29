namespace ClearVision.Product.Infrastructure.Data;

/// <summary>
/// Durable, singleton installation latch. Once completed, the database trigger prevents it from
/// returning to the incomplete state.
/// </summary>
public sealed class InstallationStateEntity
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;

    public bool IsCompleted { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public long Revision { get; private set; }
}
