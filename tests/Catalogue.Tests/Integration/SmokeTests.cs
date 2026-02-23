// tests/Catalogue.Tests/Integration/SmokeTests.cs
// Integration smoke tests using WebApplicationFactory.
// These tests verify that the app starts up and routes respond without server errors.

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;

namespace Catalogue.Tests.Integration;

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
    public async Task Routes_do_not_return_server_error(string path)
    {
        var response = await _client.GetAsync(path);

        ((int)response.StatusCode).Should().BeLessThan(500,
            because: $"route {path} should not cause a server error");
    }
}
