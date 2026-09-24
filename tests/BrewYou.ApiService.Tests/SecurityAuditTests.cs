using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace BrewYou.ApiService.Tests;

public class SecurityAuditTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SecurityAuditTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Response_IncludesMandatorySecurityHeaders()
    {
        var response = await _client.GetAsync("/api/v1/ingredients");

        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");

        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");

        response.Headers.Should().ContainKey("Referrer-Policy");
        response.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task Cors_DisallowsArbitraryUntrustedOrigins()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/recipes");
        request.Headers.Add("Origin", "https://malicious-untrusted-site.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        // Untrusted origin should NOT receive Access-Control-Allow-Origin matching the attacker
        if (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values))
        {
            values.Should().NotContain("https://malicious-untrusted-site.com");
        }
    }
}