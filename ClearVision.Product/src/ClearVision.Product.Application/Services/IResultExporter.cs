// IResultExporter.cs
// ResultExporter接口定义
// 作者：蘅芜君

using ClearVision.Product.Application.DTOs;

namespace ClearVision.Product.Application.Services;

public interface IResultExporter
{
    Task<byte[]> ExportToCsvAsync(List<InspectionResultDto> results);
}
