using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Bella.Wms.Platform.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Bella.Wms.Characterization;

/// <summary>
/// Drives real payloads through the whole inbound pipeline in-process.
/// </summary>
/// <remarks>
/// <para>
/// This is the first test that exercises the stack the way Locus will: HTTP in,
/// authentication, ambient context established from headers and <c>empmst</c>, envelope
/// detection, event registry lookup, handler, unit of work, audit write, HTTP out.
/// Nothing between the socket and the audit row is stubbed — only the database itself is
/// replaced.
/// </para>
/// <para>
/// It cannot tell you the SQL is right, because no SQL runs. It can tell you the plumbing
/// is right, which until now nothing did.
/// </para>
/// </remarks>
public sealed class LocusEndpointTests : IClassFixture<LocusEndpointTests.Factory>, IDisposable
{
    private const string ValidKey = "dev-locus-key-not-a-real-secret";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public LocusEndpointTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    /// <summary>Hosts the real <c>Bella.Wms.Api</c> with the in-memory warehouse.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        /// <summary>The seeded warehouse, so tests can assert what was written to it.</summary>
        public InMemoryWarehouse Warehouse { get; } = InMemoryWarehouse.CreateSeeded();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);

            // Re-register the fakes against *this* warehouse instance so assertions see
            // the same data the request touched.
            builder.ConfigureServices(services =>
                services.AddWmsFakes(isDevelopment: true, warehouse: Warehouse));
        }
    }

    private HttpRequestMessage BuildRequest(string payload, string? key = ValidKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/locus")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        if (key is not null)
        {
            // The ABL header name is AUTHENTICATION, not Authorization — preserved
            // because Locus is sending it (wsLocusAPI.p:83).
            request.Headers.TryAddWithoutValidation("AUTHENTICATION", key);
        }

        request.Headers.TryAddWithoutValidation("HTTP_COMPANY", "ALO");
        request.Headers.TryAddWithoutValidation("HTTP_WAREHOUSE", "AV");

        return request;
    }

    [Fact]
    public async Task HealthEndpointReportsFakeMode()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"fakeData\":true");
    }

    /// <summary>
    /// The happy path: ACCEPT for a carton that exists writes a <c>JA</c> audit row and
    /// returns the ABL's exact success message.
    /// </summary>
    [Fact]
    public async Task AcceptForKnownCartonWritesAuditAndReturnsTheAblMessage()
    {
        const string payload = """
            {
              "OrderJobResult": {
                "EventType": "ACCEPT",
                "JobId": "C0001234",
                "JobStatus": "COMPLETED",
                "JobDate": "2026-08-31T09:15:00+05:30"
              }
            }
            """;

        var response = await _client.SendAsync(BuildRequest(payload));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Byte-identical to locusAPI.cls:1799.
        body.Should().Be("OrderJobResult accept successful for job id C0001234");

        var audit = _factory.Warehouse.AuditTrail
            .Where(a => a.CartonId == "C0001234" && a.TransactionType == "JA")
            .ToList();

        audit.Should().ContainSingle("locusAPI.cls:1783 creates exactly one JA row");
        audit[0].Company.Should().Be("ALO");
        audit[0].Warehouse.Should().Be("AV");
        audit[0].OrderNumber.Should().Be("SO12345");
        audit[0].OrderSuffix.Should().Be("01");
        audit[0].Comments.Should().Be("OrderJobResult accepted by Locus");

        // Employee comes from the empmst prefix lookup, not the payload.
        audit[0].EmployeeNumber.Should().Be("LOCUS01");

        // The origin marker Phase 6 §9 requires, so a production incident can be traced
        // to the stack that caused it.
        audit[0].OriginStack.Should().Be("DOTNET");
    }

    /// <summary>
    /// An unknown carton returns <b>HTTP 200</b> with an error body — the ABL behaviour at
    /// <c>locusAPI.cls:1774-1780</c>. Preserved deliberately; Locus retry logic may
    /// depend on it.
    /// </summary>
    [Fact]
    public async Task AcceptForUnknownCartonReturnsTwoHundredWithAnErrorBody()
    {
        const string payload = """
            {
              "OrderJobResult": {
                "EventType": "ACCEPT",
                "JobId": "NOSUCHCARTON",
                "JobStatus": "COMPLETED"
              }
            }
            """;

        var response = await _client.SendAsync(BuildRequest(payload));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the ABL returns 200 even for application-level failures");

        (await response.Content.ReadAsStringAsync())
            .Should().Be("Unable to find carton matching job id NOSUCHCARTON");
    }

    /// <summary>
    /// An unregistered event returns the ABL's exact "Invalid Endpoint Action" response,
    /// also with 200. <c>wsLocusAPI.p:179-180</c>.
    /// </summary>
    [Fact]
    public async Task UnregisteredEventReturnsTheAblInvalidActionMessage()
    {
        const string payload = """
            {
              "OrderJobResult": {
                "EventType": "NOTAREALEVENT",
                "JobId": "C0001234"
              }
            }
            """;

        var response = await _client.SendAsync(BuildRequest(payload));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync())
            .Should().Be("Invalid Endpoint Action NOTAREALEVENT");
    }

    /// <summary>
    /// <c>testtotemove</c> is a method on <c>locusAPI.cls</c> (line 3188) that reflection
    /// dispatch made reachable from production traffic. It is not registered here, so it
    /// must be rejected as unknown.
    /// </summary>
    [Fact]
    public async Task TestHookIsNotReachableFromTheWire()
    {
        const string payload = """
            {
              "OrderJobResult": {
                "EventType": "TESTTOTEMOVE",
                "JobId": "C0001234"
              }
            }
            """;

        var response = await _client.SendAsync(BuildRequest(payload));

        (await response.Content.ReadAsStringAsync())
            .Should().Be("Invalid Endpoint Action TESTTOTEMOVE",
                "a test hook must not be callable by an external system");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task BadOrMissingKeyIsRejected(string? key)
    {
        const string payload = """
            { "OrderJobResult": { "EventType": "ACCEPT", "JobId": "C0001234" } }
            """;

        var response = await _client.SendAsync(BuildRequest(payload, key));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Body text matches the ABL exactly (wsLocusAPI.p:98) — partners may match on it.
        (await response.Content.ReadAsStringAsync()).Should().Be("Invalid Authentication");
    }

    /// <summary>
    /// The ABL also accepts <c>basic &lt;base64&gt;</c> and decodes it
    /// (<c>wsLocusAPI.p:86-90</c>). Both forms must work.
    /// </summary>
    [Fact]
    public async Task Base64BasicFormIsAccepted()
    {
        const string payload = """
            { "OrderJobResult": { "EventType": "ACCEPT", "JobId": "C0001234" } }
            """;

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(ValidKey));

        var response = await _client.SendAsync(BuildRequest(payload, $"basic {encoded}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A warehouse with no LOCUS employee is rejected at the boundary.
    /// </summary>
    /// <remarks>
    /// <b>Deliberate difference from the ABL.</b> <c>wsLocusAPI.p:124-125</c> logs
    /// "employee master ... CAN NOT BE FOUND" and carries on with an uninitialised
    /// <c>static_connect</c> — so the request runs against whatever company and warehouse
    /// the previous request on that PASOE agent left behind. That is a cross-tenant data
    /// hazard, and it is not carried forward.
    /// </remarks>
    [Fact]
    public async Task WarehouseWithNoApiUserIsRejected()
    {
        const string payload = """
            { "OrderJobResult": { "EventType": "ACCEPT", "JobId": "C0001234" } }
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/locus")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("AUTHENTICATION", ValidKey);
        request.Headers.TryAddWithoutValidation("HTTP_COMPANY", "ALO");
        request.Headers.TryAddWithoutValidation("HTTP_WAREHOUSE", "ZZ");   // no employee

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the ABL would continue with stale ambient context; we refuse instead");
    }

    [Fact]
    public async Task MissingCompanyAndWarehouseHeadersAreRejected()
    {
        const string payload = """
            { "OrderJobResult": { "EventType": "ACCEPT", "JobId": "C0001234" } }
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/locus")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("AUTHENTICATION", ValidKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnparseablePayloadIsRejected()
    {
        var response = await _client.SendAsync(BuildRequest("{ this is not json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("Unrecognized JSON payload");
    }

    /// <summary>
    /// An audit row must never be written outside a transaction. If the handler ever
    /// stops opening one, this catches it — the fake keeps the real service's guard.
    /// </summary>
    [Fact]
    public async Task AuditRowsAreWrittenInsideATransaction()
    {
        const string payload = """
            {
              "OrderJobResult": {
                "EventType": "ACCEPT",
                "JobId": "C0001234",
                "JobStatus": "COMPLETED"
              }
            }
            """;

        await _client.SendAsync(BuildRequest(payload));

        // FakeAuditService refuses to append without an active unit of work, so the
        // presence of any row proves the handler opened and committed one.
        _factory.Warehouse.AuditTrail.Should().NotBeEmpty(
            "a written audit row proves the transaction was open at the time");
    }
}
