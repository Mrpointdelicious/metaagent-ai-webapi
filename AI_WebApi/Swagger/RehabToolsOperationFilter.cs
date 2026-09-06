using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AI_WebApi.Swagger;

public sealed class RehabToolsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.GroupName != "ai-rehab-tools-v1" ||
            operation.RequestBody?.Content == null)
        {
            return;
        }
        var json = operation.RequestBody.Content
            .FirstOrDefault(item => item.Key.Contains(
                "json",
                StringComparison.OrdinalIgnoreCase));
        if (json.Value == null)
        {
            return;
        }
        operation.RequestBody.Content =
            new Dictionary<string, OpenApiMediaType>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["application/json"] = json.Value
            };
    }
}
