// ProjectJsonSerializer.cs
// ProjectJsonSerializer实现
// 作者：蘅芜君

using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;

namespace ClearVision.Product.Infrastructure.Services;

public class ProjectJsonSerializer : IProjectSerializer
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Task<byte[]> SerializeAsync(ProjectDto project)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, _options);
        return Task.FromResult(bytes);
    }

    public Task<ProjectDto?> DeserializeAsync(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var project = JsonSerializer.Deserialize<ProjectDto>(stream, _options);
        return Task.FromResult(project);
    }
}
