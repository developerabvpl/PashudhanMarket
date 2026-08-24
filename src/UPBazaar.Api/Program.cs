using System.Text;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using UPBazaar.Api.Extensions;
using UPBazaar.Api.OpenApi;
using UPBazaar.Infrastructure;
using UPBazaar.Infrastructure.Logging;
using UPBazaar.Infrastructure.Messaging;
using UPBazaar.Modules.Catalog;
using UPBazaar.Modules.Ordering;
using UPBazaar.Modules.Payments;
using UPBazaar.Modules.Shipping;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    // Rule 7: nothing that looks like PII or a secret reaches a sink.
    .Destructure.With(new SensitiveDataDestructuringPolicy())
    .WriteTo.Console());

builder.Services.AddControllers()
    .AddCatalogApplicationPart()
    .AddOrderingApplicationPart()
    .AddPaymentsApplicationPart()
    .AddShippingApplicationPart();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer<RelativeServerTransformer>();
});

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddCatalogModule()
    .AddOrderingModule()
    .AddPaymentsModule()
    .AddShippingModule();

builder.Services.AddJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddBackgroundJobs(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // The fallback policy would otherwise put the documentation behind a token.
    app.MapOpenApi().AllowAnonymous();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "UP Bazaar API v1");
        options.RoutePrefix = "swagger";
    });

    app.MapScalarApiReference(options => options
            .WithTitle("UP Bazaar API")
            .WithOpenApiRoutePattern("/openapi/{documentName}.json"))
        .AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.ScheduleRecurringJobs();

app.Run();

/// <summary>Exposed so the integration test host can reference this entry point.</summary>
public partial class Program;
