using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace UPBazaar.Api.OpenApi;

/// <summary>
/// Replaces the absolute server URL with a relative one. Without this the emitted document
/// carries whatever host it was fetched from, which makes the committed openapi.json
/// machine-specific and churns the generated client on every developer's machine.
/// </summary>
internal sealed class RelativeServerTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Servers = [new OpenApiServer { Url = "/", Description = "Same origin" }];

        return Task.CompletedTask;
    }
}
