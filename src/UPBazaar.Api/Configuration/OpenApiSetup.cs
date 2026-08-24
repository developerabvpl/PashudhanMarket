using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using UPBazaar.Api.OpenApi;

namespace UPBazaar.Api.Configuration;

/// <summary>API versioning plus the two documentation surfaces.</summary>
public static class OpenApiSetup
{
    /// <summary>
    /// Versioning by URL segment: <c>/api/v1/...</c>. A segment rather than a header because
    /// it survives being pasted into a browser, a curl example or a bug report.
    ///
    /// There is no "assume default version" fallback: this API is new, so every route states
    /// its version and an unversioned request is a mistake worth surfacing rather than
    /// silently binding to v1 forever.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddApiVersioningAndDocs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        })
        .AddMvc()
        // Generates one OpenAPI document per discovered API version, so adding v2 later needs
        // no change here. Transformers go through Document rather than a separate
        // services.AddOpenApi call, which would register a second, unversioned document.
        .AddOpenApi(options =>
        {
            options.Document.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.Document.AddSchemaTransformer<NumericSchemaTransformer>();
            options.Document.AddDocumentTransformer((document, context, _) =>
            {
                document.Info.Title = "UP Bazaar API";
                document.Info.Description = "Modular monolith API for the UP Bazaar marketplace.";

                return Task.CompletedTask;
            });
        });

        return services;
    }

    /// <summary>
    /// Serves the OpenAPI documents plus Swagger UI at /swagger and Scalar at /scalar.
    ///
    /// Both are anonymous: the fallback authorization policy would otherwise put the API's own
    /// documentation behind a token nobody can obtain without reading the documentation.
    /// </summary>
    /// <param name="app">Web application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOpenApi("/openapi/{documentName}.json")
            .WithDocumentPerVersion()
            .AllowAnonymous();

        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "swagger";

            foreach (var description in app.DescribeApiVersions())
            {
                options.SwaggerEndpoint(
                    $"/openapi/{description.GroupName}.json",
                    $"UP Bazaar API {description.GroupName}");
            }
        });

        app.MapScalarApiReference(options => options
                .WithTitle("UP Bazaar API")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json"))
            .AllowAnonymous();

        return app;
    }
}

/// <summary>
/// Advertises bearer authentication on the document, so Swagger UI and Scalar both offer an
/// Authorize box rather than returning 401 for every try-it-out.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var schemes = await schemeProvider.GetAllSchemesAsync();

        if (!schemes.Any(scheme => string.Equals(scheme.Name, "Bearer", StringComparison.Ordinal)))
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            BearerFormat = "JSON Web Token",
            Description = "Paste a JWT carrying the permission claims the endpoint requires.",
        };

        // In Microsoft.OpenApi 2.x a requirement is keyed by a reference to the scheme rather
        // than by an inline copy of it.
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document, null)] = [],
        });
    }
}
