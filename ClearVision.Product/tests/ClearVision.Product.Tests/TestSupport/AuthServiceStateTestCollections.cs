namespace ClearVision.Product.Tests.TestSupport;

public static class AuthServiceStateTestCollections
{
    public const string InMemorySessionState = "AuthServiceInMemorySessionState";
}

[CollectionDefinition(AuthServiceStateTestCollections.InMemorySessionState, DisableParallelization = true)]
public sealed class AuthServiceInMemorySessionStateCollectionDefinition
{
}
