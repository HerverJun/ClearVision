using Acme.Product.Core.Entities;
using Acme.Product.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Acme.Product.Desktop.Endpoints;

/// <summary>
/// 设置功能 API 端点
/// </summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // 获取当前配置
        app.MapGet("/api/settings", async (IConfigurationService configService) =>
        {
            var config = await configService.LoadAsync();
            return Results.Ok(config);
        });
        
        // 更新配置
        app.MapPut("/api/settings", async (AppConfig config, IConfigurationService configService) =>
        {
            try
            {
                await configService.SaveAsync(config);
                return Results.Ok(new { Message = "配置已保存" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });
        
        // 重置配置为默认值
        app.MapPost("/api/settings/reset", async (IConfigurationService configService) =>
        {
            var defaultConfig = new AppConfig();
            await configService.SaveAsync(defaultConfig);
            return Results.Ok(defaultConfig);
        });
        
        return app;
    }
}
