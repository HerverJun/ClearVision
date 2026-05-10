using Acme.Product.Application.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Acme.Product.Desktop.Endpoints;

public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/demo/create", async (DemoProjectService demoService) =>
        {
            try
            {
                var project = await demoService.CreateDemoProjectAsync();
                return Results.Ok(project);
            }
            catch (Exception ex)
            {
                return Results.Problem($"创建演示工程失败: {ex.Message}");
            }
        });

        app.MapPost("/api/demo/create-simple", async (DemoProjectService demoService) =>
        {
            try
            {
                var project = await demoService.CreateSimpleDemoProjectAsync();
                return Results.Ok(project);
            }
            catch (Exception ex)
            {
                return Results.Problem($"创建简单演示工程失败: {ex.Message}");
            }
        });

        app.MapGet("/api/demo/guide", (DemoProjectService demoService) =>
        {
            return Results.Ok(demoService.GetDemoGuide());
        });

        return app;
    }
}
