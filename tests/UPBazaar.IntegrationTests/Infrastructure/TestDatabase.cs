namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// Decides where these tests get SQL Server from, in priority order:
///
/// <list type="number">
///   <item>the <c>UPBAZAAR_TEST_SQL</c> connection string, pointing at a server already running;</item>
///   <item>a throwaway SQL Server container, when a Docker daemon is reachable;</item>
///   <item>neither, in which case the database tests report as skipped rather than failed.</item>
/// </list>
///
/// The container is the documented default. The override exists because plenty of Windows
/// developers have SQL Server Express and no Docker, and a suite that cannot run on their
/// machine is a suite they will stop running.
/// </summary>
public static class TestDatabase
{
    public const string ConnectionStringVariable = "UPBAZAAR_TEST_SQL";

    /// <summary>Server-level connection string supplied by a developer or by CI, if any.</summary>
    public static string? ConfiguredConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public static bool DockerAvailable { get; } = ProbeDocker();

    public static bool IsAvailable =>
        !string.IsNullOrWhiteSpace(ConfiguredConnectionString) || DockerAvailable;

    public static string SkipReason =>
        $"No SQL Server available: set {ConnectionStringVariable} to a server connection string, "
        + "or start Docker so a SQL Server container can be created.";

    /// <summary>
    /// Looks for the daemon endpoint rather than shelling out to the CLI, so a machine without
    /// Docker costs nothing at test-collection time.
    /// </summary>
    private static bool ProbeDocker()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Directory.GetFiles(@"\\.\pipe\")
                    .Any(pipe => pipe.Contains("docker_engine", StringComparison.OrdinalIgnoreCase));
            }

            return File.Exists("/var/run/docker.sock");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A fact needing a real database: skipped, not failed, when there is none.</summary>
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

/// <summary>A theory needing a real database: skipped, not failed, when there is none.</summary>
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
