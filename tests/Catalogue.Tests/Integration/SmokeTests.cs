// tests/Catalogue.Tests/Integration/SmokeTests.cs
// Integration smoke tests using WebApplicationFactory.
// These tests verify that the app starts up and protected routes reject
// unauthenticated requests. The real Entra ID OIDC handler is replaced with a
// stub that returns 401 Unauthorized immediately without hitting any network.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;

namespace Catalogue.Tests.Integration;

// ── Stub authentication handler ─────────────────────────────────────────────
/// <summary>
/// A test authentication handler that always returns no result (anonymous),
/// so protected routes will trigger the 401/challenge response without
/// trying to contact any real identity provider.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}

// ── Custom WebApplicationFactory ─────────────────────────────────────────────
public class CatalogueWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"]              = "00000000-0000-0000-0000-000000000001",
                ["AzureAd:ClientId"]              = "00000000-0000-0000-0000-000000000002",
                ["AzureAd:Domain"]                = "test.onmicrosoft.com",
                ["AzureAd:Instance"]              = "https://login.microsoftonline.com/",
                ["AzureAd:CallbackPath"]          = "/signin-oidc",
                ["ConnectionStrings:CatalogueDb"] = "DataSource=testcatalogue;Mode=Memory;Cache=Shared",
                ["KeyVaultName"]                  = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace SQL Server with SQLite in-memory
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<Catalogue.Infrastructure.Data.CatalogueDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<Catalogue.Infrastructure.Data.CatalogueDbContext>(opts =>
                opts.UseSqlite("DataSource=testcatalogue;Mode=Memory;Cache=Shared"));

            // Replace Entra ID OIDC with stub auth handler to avoid network calls
            services.AddAuthentication("TestScheme")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { });

            // Override the default challenge scheme so the stub handler is used
            services.Configure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(opts =>
            {
                opts.DefaultScheme          = "TestScheme";
                opts.DefaultChallengeScheme = "TestScheme";
            });
        });
    }
}

// ── Smoke Tests ──────────────────────────────────────────────────────────────
public class SmokeTests : IClassFixture<CatalogueWebAppFactory>
{
    private readonly HttpClient _client;

    public SmokeTests(CatalogueWebAppFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/servers")]
    [InlineData("/catalogue")]
    [InlineData("/import/dacpac")]
    [InlineData("/import/excel")]
    public async Task Protected_routes_do_not_return_server_error(string path)
    {
        var response = await _client.GetAsync(path);

        // Should be 401 (or any non-5xx) — not a server error
        ((int)response.StatusCode).Should().BeLessThan(500,
            because: $"route {path} should not cause a server error");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/servers")]
    [InlineData("/catalogue")]
    public async Task Unauthenticated_requests_are_rejected(string path)
    {
        var response = await _client.GetAsync(path);

        // Without auth, the fallback policy must reject the request
        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400,
            because: $"route {path} requires authentication");
    }
}
