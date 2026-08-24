namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// Decides where the integration tests get SQL Server from, in priority order:
/// <list type="number">
///   <item>the UPBAZAAR_TEST_SQL connection string, pointing at a server you already run;</item>
///   <item>a throwaway SQL Server container, when a Docker daemon is reachable;</item>
///   <item>nothing, in which case the database tests report themselves as skipped.</item>
/// </list>
/// </summary>
public static class TestDatabase
{
    public const string ConnectionStringVariable = "UPBAZAAR_TEST_SQL";

    /// <summary>Server-level connection string supplied by the developer or CI, if any.</summary>
    public static string? ConfiguredConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public static bool DockerAvailable { get; } = ProbeDocker();

    public static bool IsAvailable => !string.IsNullOrWhiteSpace(ConfiguredConnectionString) || DockerAvailable;

    public static string SkipReason =>
        $"No SQL Server available: set {ConnectionStringVariable} to a server connection string, "
        + "or start Docker so a SQL Server container can be created.";

    /// <summary>
    /// Cheap probe: look for the daemon endpoint rather than shelling out to the CLI, so a
    /// machine without Docker costs nothing at collection time.
    /// </summary>
    private static bool ProbeDocker()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Directory.GetFiles(@"\\.\pipe\").Any(pipe =>
                    pipe.Contains("docker_engine", StringComparison.OrdinalIgnoreCase));
            }

            return File.Exists("/var/run/docker.sock");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A fact that needs a real database, skipped rather than failed when there is none.</summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!TestDatabase.IsAvailable)
        {
            Skip = TestDatabase.SkipReason;
        }
    }
}

/// <summary>A theory that needs a real database, skipped rather than failed when there is none.</summary>
public sealed class DatabaseTheoryAttribute : TheoryAttribute
{
    public DatabaseTheoryAttribute()
    {
        if (!TestDatabase.IsAvailable)
        {
            Skip = TestDatabase.SkipReason;
        }
    }
}
