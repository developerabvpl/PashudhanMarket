using System.Reflection;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Api.Controllers;

/// <summary>What this build is and which modules it contains.</summary>
/// <param name="Version">Informational assembly version.</param>
/// <param name="Environment">Hosting environment name.</param>
/// <param name="Modules">Modules composed into this host, with the schema each owns.</param>
public sealed record SystemInfoResponse(
    string Version,
    string Environment,
    IReadOnlyList<ModuleInfo> Modules);

/// <summary>One module and the SQL schema it owns.</summary>
/// <param name="Name">Module name.</param>
/// <param name="Schema">SQL schema owned by the module.</param>
public sealed record ModuleInfo(string Name, string Schema);

/// <summary>
/// Platform-level endpoints that belong to no module.
///
/// Kept deliberately small: anything with domain meaning belongs in a module, not here.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/system")]
[Produces("application/json")]
public sealed class SystemController(
    IEnumerable<IModule> modules,
    IHostEnvironment environment) : ControllerBase
{
    /// <summary>Returns the build version and the modules this host has loaded.</summary>
    /// <returns>Build and module information.</returns>
    [HttpGet("info")]
    [AllowAnonymous]
    [EndpointSummary("Build and module information")]
    [EndpointDescription(
        "Reports the running version, the environment, and every module composed into this "
        + "host with the SQL schema it owns. Useful for confirming a deployment carries the "
        + "modules you expect.")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> GetInfo()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

        var loaded = modules
            .Select(m => new ModuleInfo(m.Name, m.Schema))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        return Ok(new SystemInfoResponse(version, environment.EnvironmentName, loaded));
    }
}
