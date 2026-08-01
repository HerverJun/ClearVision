namespace ClearVision.Product.Desktop.Tests;

public static class PhotoelectricTriggerTestCollections
{
    public const string TriggerInput = "Photoelectric trigger input";
}

[CollectionDefinition(PhotoelectricTriggerTestCollections.TriggerInput, DisableParallelization = true)]
public sealed class PhotoelectricTriggerTestCollectionDefinition
{
}
