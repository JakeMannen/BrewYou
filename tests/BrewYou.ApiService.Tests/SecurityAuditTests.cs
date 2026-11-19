using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class SecurityAuditTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SecurityAuditTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
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

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://metadata.google.internal/computeMetadata/v1")]
    [InlineData("http://100.100.100.200/latest/meta-data")]
    [InlineData("http://127.0.0.1:5000/api/v1/auth/me")]
    [InlineData("http://localhost:5432/")]
    [InlineData("http://api.localhost:5000/")]
    [InlineData("http://[::]:5000/api/v1/auth/me")]
    [InlineData("http://[::1]:5000/api/v1/auth/me")]
    [InlineData("http://[::127.0.0.1]:5000/")]
    [InlineData("http://[::ffff:169.254.169.254]/latest/meta-data")]
    [InlineData("http://[64:ff9b::a9fe:a9fe]/latest/meta-data")]
    [InlineData("http://[2002:7f00:0001::]/")]
    [InlineData("http://[fec0::1]/")]
    [InlineData("http://10.0.0.1/admin")]
    [InlineData("http://172.16.0.1/status")]
    [InlineData("http://192.168.1.1/router")]
    [InlineData("ftp://evil.com/payload")]
    public async Task NetworkSecurityValidator_BlocksSsrfAndPrivateTargets(string targetUrl)
    {
        var (isValid, errorMessage, _) = await Services.NetworkSecurityValidator.ValidateUrlAsync(targetUrl);
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("::", true)]
    [InlineData("::1", true)]
    [InlineData("::127.0.0.1", true)]
    [InlineData("::10.0.0.1", true)]
    [InlineData("64:ff9b::a9fe:a9fe", true)]
    [InlineData("64:ff9b:1::1", true)]
    [InlineData("2002:a00:1::1", true)]
    [InlineData("fec0::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("2606:4700:4700::1111", false)]
    public void NetworkSecurityValidator_IsForbiddenAddress_EnforcesIpv6Rules(string ipString, bool expectedForbidden)
    {
        var ip = System.Net.IPAddress.Parse(ipString);
        Services.NetworkSecurityValidator.IsForbiddenAddress(ip, allowPrivateNetworks: false)
            .Should().Be(expectedForbidden);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("169.254.169.254")]
    public async Task NetworkSecurityValidator_ConnectCallback_BlocksDnsRebindingToForbiddenIp(string rebindingIp)
    {
        var act = async () => await Services.NetworkSecurityValidator.ConnectToValidatedHostAsync(
            "rebind.attacker.example",
            80,
            allowPrivateNetworks: false,
            allowLoopback: false,
            dnsResolver: (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse(rebindingIp) }),
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task BreweryInvite_CannotBeAccepted_ByDifferentEmailAddress()
    {
        // Arrange: User A registers
        var clientA = _factory.CreateClient();
        var emailA = $"owner_{Guid.NewGuid():N}@brewyou.test";
        var regA = await clientA.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(emailA, "Password123!", "Owner A"));
        regA.StatusCode.Should().Be(HttpStatusCode.OK);
        var authA = (await regA.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        clientA.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authA.AccessToken);

        var setupsRes = await clientA.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        // User A invites intended@brewyou.test
        var intendedEmail = $"intended_{Guid.NewGuid():N}@brewyou.test";
        var inviteRes = await clientA.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest(intendedEmail, BreweryRole.Brewer));
        inviteRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var inviteData = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!;

        // User B (interceptor with different email) tries to accept User A's invite code
        var clientB = _factory.CreateClient();
        var emailB = $"attacker_{Guid.NewGuid():N}@brewyou.test";
        var regB = await clientB.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(emailB, "Password123!", "Attacker B"));
        regB.StatusCode.Should().Be(HttpStatusCode.OK);
        var authB = (await regB.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        clientB.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authB.AccessToken);

        var acceptRes = await clientB.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept",
            new AcceptInviteRequest(inviteData.InviteCode));

        // Assert: Interception must be rejected with 403 Forbidden
        acceptRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var acceptBody = await acceptRes.Content.ReadFromJsonAsync<ApiResponse>();
        acceptBody!.Success.Should().BeFalse();
        acceptBody.Error!.Code.Should().Be("FORBIDDEN");
    }
}