// IProjectSerializer.cs
// ProjectSerializer接口定义
// 作者：蘅芜君

using ClearVision.Product.Application.DTOs;

namespace ClearVision.Product.Application.Services;

public interface IProjectSerializer
{
    Task<byte[]> SerializeAsync(ProjectDto project);
    Task<ProjectDto?> DeserializeAsync(byte[] data);
}
