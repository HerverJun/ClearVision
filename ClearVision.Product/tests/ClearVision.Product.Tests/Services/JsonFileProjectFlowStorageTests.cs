using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

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
    public async Task SaveFlowJsonAsync_ShouldWriteMetadataAndLastGoodCopy()
    {
        var basePath = CreateTempPath();

        try
        {
            var storage = new JsonFileProjectFlowStorage(basePath);
            var projectId = Guid.NewGuid();
            const string first = """{"nodes":[{"id":"a"}],"connections":[]}""";
            const string second = """{"nodes":[{"id":"b"}],"connections":[]}""";

            await storage.SaveFlowJsonAsync(projectId, first);
            await storage.SaveFlowJsonAsync(projectId, second);

            (await storage.LoadFlowJsonAsync(projectId)).Should().Be(second);
            File.ReadAllText(Path.Combine(basePath, $"{projectId}.last-good.json")).Should().Be(first);
            File.ReadAllText(Path.Combine(basePath, $"{projectId}.metadata.json"))
                .Should().Contain("flowHash")
                .And.Contain(projectId.ToString());
        }
        finally
        {
            DeleteDirectoryIfExists(basePath);
        }
    }

    [Fact]
    public async Task SaveFlowJsonAsync_WithPersistenceRevision_ShouldWriteReadableMetadataRevision()
    {
        var basePath = CreateTempPath();

        try
        {
            var storage = new JsonFileProjectFlowStorage(basePath);
            var projectId = Guid.NewGuid();
            const string flowJson = """{"nodes":[{"id":"rev"}],"connections":[]}""";

            await storage.SaveFlowJsonAsync(projectId, flowJson, 7);

            var metadata = await storage.LoadMetadataAsync(projectId);
            metadata.Should().NotBeNull();
            metadata!.ProjectId.Should().Be(projectId);
            metadata.PersistenceRevision.Should().Be(7);
            metadata.FlowHash.Should().StartWith("sha256:");
        }
        finally
        {
            DeleteDirectoryIfExists(basePath);
        }
    }

    [Fact]
    public async Task LoadFlowJsonAsync_ShouldRestoreLastGoodAndMarkCorruptFile()
    {
        var basePath = CreateTempPath();

        try
        {
            var storage = new JsonFileProjectFlowStorage(basePath);
            var projectId = Guid.NewGuid();
            const string first = """{"nodes":[{"id":"a"}],"connections":[]}""";
            const string second = """{"nodes":[{"id":"b"}],"connections":[]}""";

            await storage.SaveFlowJsonAsync(projectId, first);
            await storage.SaveFlowJsonAsync(projectId, second);
            await File.WriteAllTextAsync(Path.Combine(basePath, $"{projectId}.json"), "{broken");

            (await storage.LoadFlowJsonAsync(projectId)).Should().Be(first);
            File.Exists(Path.Combine(basePath, $"{projectId}.json.corrupt")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryIfExists(basePath);
        }
    }

    [Fact]
    public async Task SaveFlowJsonAsync_ShouldSerializeConcurrentWriters()
    {
        var basePath = CreateTempPath();

        try
        {
            var storage = new JsonFileProjectFlowStorage(basePath);
            var projectId = Guid.NewGuid();
            var writes = Enumerable.Range(0, 20)
                .Select(i => storage.SaveFlowJsonAsync(projectId, $$"""{"nodes":[{"id":"{{i}}"}],"connections":[]}"""));

            await Task.WhenAll(writes);

            var loaded = await storage.LoadFlowJsonAsync(projectId);
            loaded.Should().NotBeNull();
            Action parse = () => System.Text.Json.JsonDocument.Parse(loaded!).Dispose();
            parse.Should().NotThrow();
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
