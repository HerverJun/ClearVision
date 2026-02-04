using Acme.Product.Application.DTOs;

namespace Acme.Product.Application.Services;

public interface IResultExporter
{
    Task<byte[]> ExportToCsvAsync(List<InspectionResultDto> results);
}