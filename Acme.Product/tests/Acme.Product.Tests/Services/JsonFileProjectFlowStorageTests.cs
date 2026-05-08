using Acme.Product.Infrastructure.Services;
using FluentAssertions;

namespace Acme.Product.Tests.Services;

public class JsonFileProjectFlowStorageTests
{
    [Fact]
    public async Task LoadFlowJsonAsync_ShouldReturnNull_WhenFlowFileDoesNotExist()
    {
        var basePath = CreateTempPath();

        try
        {
            var storage = new JsonFileProjectFlowStorage(basePath);

            var result = await storage.LoadFlowJsonAsync(Guid.NewGuid());

            result.Should().BeNull();
        }
        finally
        {
            DeleteDirectoryIfExists(basePath);
        }
    }

    [Fact]
    public async Task SaveFlowJsonAsync_ShouldRecreateBaseDirectory_WhenItWasRemoved()
    {
        var basePath = CreateTempPath();

        try
        {
            var storage = new JsonFileProjectFlowStorage(basePath);
            Directory.Delete(basePath, recursive: true);
            var projectId = Guid.NewGuid();
            const string flowJson = """{"nodes":[],"connections":[]}""";

            await storage.SaveFlowJsonAsync(projectId, flowJson);
            var loaded = await storage.LoadFlowJsonAsync(projectId);

            loaded.Should().Be(flowJson);
        }
        finally
        {
            DeleteDirectoryIfExists(basePath);
        }
    }

    [Fact]
    public async Task SaveFlowJsonAsync_ShouldThrowArgumentNullException_WhenFlowJsonIsNull()
    {
        var basePath = CreateTempPath();

        try
        {
            var storage = new JsonFileProjectFlowStorage(basePath);

            var act = () => storage.SaveFlowJsonAsync(Guid.NewGuid(), null!);

            await act.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("flowJson");
        }
        finally
        {
            DeleteDirectoryIfExists(basePath);
        }
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentException_WhenBasePathIsEmpty()
    {
        var act = () => new JsonFileProjectFlowStorage(" ");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("basePath");
    }

    private static string CreateTempPath()
    {
        return Path.Combine(Path.GetTempPath(), "ClearVision.ProjectFlows.Tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
