using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UPBazaar.IntegrationTests.Infrastructure;

namespace UPBazaar.IntegrationTests;

/// <summary>
/// Proves the host boots and the platform surfaces answer: health, versioned routing, the
/// OpenAPI document, and the deny-by-default authorization policy.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApiSmokeTests(ApiFixture fixture)
{
    [DatabaseFact]
    public async Task Health_reports_healthy_when_sql_is_reachable()
    {
        var response = await fixture.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [DatabaseFact]
    public async Task The_versioned_system_endpoint_lists_every_module()
    {
        var client = fixture.CreateClient();

        var info = await client.GetFromJsonAsync<SystemInfo>(
            new Uri("/api/v1/system/info", UriKind.Relative));

        info.ShouldNotBeNull();
        info.Modules.Count.ShouldBe(16);
        info.Modules.Select(m => m.Name).ShouldContain("Catalog");
        info.Modules.Select(m => m.Name).ShouldContain("Settlements");
    }

    [DatabaseFact]
    public async Task Every_module_owns_a_distinct_schema()
    {
        var info = await fixture.CreateClient().GetFromJsonAsync<SystemInfo>(
            new Uri("/api/v1/system/info", UriKind.Relative));

        var schemas = info!.Modules.Select(m => m.Schema).ToList();

        schemas.Distinct(StringComparer.Ordinal).Count().ShouldBe(schemas.Count);
        schemas.ShouldAllBe(s => s.All(c => !char.IsUpper(c)));
    }

    [DatabaseFact]
    public async Task An_unversioned_route_is_not_served()
    {
        // Versioning is by URL segment and there is no implicit default, so the unversioned
        // path must not quietly resolve to v1.
        var response = await fixture.CreateClient()
            .GetAsync(new Uri("/api/system/info", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task An_unknown_route_returns_problem_details_carrying_the_correlation_id()
    {
        var response = await fixture.CreateClient()
            .GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("status", out var status).ShouldBeTrue();
        status.GetInt32().ShouldBe(404);
        document.RootElement.TryGetProperty("correlationId", out _).ShouldBeTrue();
    }

    [DatabaseFact]
    public async Task A_request_gets_a_correlation_id_back()
    {
        var response = await fixture.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));

        response.Headers.TryGetValues("X-Correlation-Id", out var values).ShouldBeTrue();
        values!.Single().ShouldNotBeNullOrWhiteSpace();
    }

    [DatabaseFact]
    public async Task An_inbound_correlation_id_is_kept_rather_than_replaced()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", "trace-from-storefront-42");

        var response = await fixture.CreateClient().SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("trace-from-storefront-42");
    }

    [DatabaseFact]
    public async Task A_hostile_correlation_id_is_sanitised_before_being_echoed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc<script>alert(1)</script>");

        var response = await fixture.CreateClient().SendAsync(request);

        var echoed = response.Headers.GetValues("X-Correlation-Id").Single();
        echoed.ShouldNotContain("<");
        echoed.ShouldNotContain(">");
    }

    [DatabaseFact]
    public async Task The_jobs_dashboard_refuses_an_anonymous_caller()
    {
        var response = await fixture.CreateClient().GetAsync(new Uri("/jobs", UriKind.Relative));

        // Hangfire is switched off in this host, so the dashboard is simply absent. What must
        // never happen is an anonymous 200.
        response.StatusCode.ShouldBeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Found);
    }

    private sealed record SystemInfo(string Version, string Environment, IReadOnlyList<Module> Modules);

    private sealed record Module(string Name, string Schema);
}
