using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using Serilog;
using UPBazaar.Api.Configuration;
using UPBazaar.Api.Middleware;
using UPBazaar.Infrastructure;
using UPBazaar.Infrastructure.Identity;
using UPBazaar.Infrastructure.Logging;
using UPBazaar.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// UPBAZAAR_-prefixed environment variables override configuration, so a deployment can set
// UPBAZAAR_ConnectionStrings__UPBazaar without colliding with anything else on the host. The
// design-time migration factory reads the same variable, so both agree about which database
// a command targets.
builder.Configuration.AddEnvironmentVariables("UPBAZAAR_");

// Secrets from Azure Key Vault when KeyVault:Uri is set. The vault is read after the variables
// above, since they may name it, and they are added again after it so that a variable still
// overrides a vault secret - which is how a test run points at its own database.
builder.Configuration.AddUpBazaarKeyVault(builder.Configuration);
builder.Configuration.AddEnvironmentVariables("UPBAZAAR_");

// Serilog: console for a terminal, a rolling file for anything after the fact. Both carry the
// correlation id pushed by the middleware, and the destructuring policy masks PII on the way.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "UPBazaar.Api")
    .Destructure.With(new SensitiveDataDestructuringPolicy())
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .WriteTo.File(
        path: Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "upbazaar-.log"),
        formatProvider: CultureInfo.InvariantCulture,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true));

builder.Services.AddControllers();
builder.Services.AddApiVersioningAndDocs();

// ProblemDetails for framework-generated responses (404, 405, 415); the exception handler
// covers the rest, so every failure leaves through the same shape.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Instance =
        $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddModules(builder.Configuration, builder.Environment);

builder.Services.AddJwtAuthentication(builder.Configuration, builder.Environment);

// A per-address cap on the sign-in endpoints, on top of each account's own attempt limits.
builder.Services.AddSignInRateLimiting(builder.Configuration);

// One policy per catalogued permission, plus a deny-by-default fallback so an endpoint that
// forgets to state its policy is unreachable rather than accidentally public.
builder.Services.AddPermissionPolicies();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy())
    .AddSqlServer(
        builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)
            ?? string.Empty,
        name: "sql",
        tags: ["ready"]);

builder.Services.AddBackgroundJobs(builder.Configuration);

var app = builder.Build();

// Correlation first: everything downstream, including the request log line and the exception
// handler, reads the id it sets.
app.UseCorrelationId();
app.UseSerilogRequestLogging(options =>
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
        diagnosticContext.Set("UserId", httpContext.User.Identity?.Name);
    });

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapApiDocumentation();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapJobsDashboard();

// The deny-by-default fallback policy is also applied to requests that match no endpoint,
// which would turn every typo into a 401 and leave a caller unable to tell a missing route
// from a forbidden one. This terminal endpoint is anonymous, so an unknown path gets an honest
// 404 in problem-details form while real endpoints keep their 401 and 403.
app.MapFallback(() => Results.Problem(
        title: "No endpoint matches this route.",
        statusCode: StatusCodes.Status404NotFound,
        type: "https://upbazaar.dev/errors/http.404"))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.ScheduleRecurringJobs();

// Permissions, roles and the first administrator, reconciled before the first request.
await app.SeedIdentityAsync();

// The sample catalogue, in environments that configure one and only while it is empty.
await app.SeedCatalogAsync();

await app.RunAsync();

/// <summary>Exposed so the integration test host can reference this entry point.</summary>
public partial class Program;
