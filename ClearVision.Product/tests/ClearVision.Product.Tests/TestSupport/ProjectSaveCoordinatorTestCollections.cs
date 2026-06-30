namespace ClearVision.Product.Tests.TestSupport;

public static class ProjectSaveCoordinatorTestCollections
{
    public const string ProjectSaveCoordinatorState = "ProjectSaveCoordinatorState";
}

[CollectionDefinition(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState, DisableParallelization = true)]
public sealed class ProjectSaveCoordinatorStateCollectionDefinition
{
}
