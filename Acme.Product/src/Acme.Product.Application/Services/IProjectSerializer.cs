using Acme.Product.Application.DTOs;

namespace Acme.Product.Application.Services;

public interface IProjectSerializer
{
    Task<byte[]> SerializeAsync(ProjectDto project);
    Task<ProjectDto?> DeserializeAsync(byte[] data);
}