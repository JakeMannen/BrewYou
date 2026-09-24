using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class UserPreferencesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public UserPreferencesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string Token, UserDto User)> RegisterTestUserAsync(string? prefix = null)
    {
        var email = $"{prefix ?? "brewer"}_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Brew Master"));
        reg.EnsureSuccessStatusCode();

        var auth = (await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        return (auth.AccessToken, auth.User);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsPreferencesWithDefaultValues()
    {
        var (token, _) = await RegisterTestUserAsync("default_pref");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Preferences.Should().NotBeNull();
        envelope.Data.Preferences.Language.Should().Be("en");
        envelope.Data.Preferences.VolumeUnit.Should().Be(VolumeUnit.Liters);
        envelope.Data.Preferences.WeightUnit.Should().Be(WeightUnit.Metric);
        envelope.Data.Preferences.TemperatureUnit.Should().Be(TemperatureUnit.Celsius);
        envelope.Data.Preferences.GravityUnit.Should().Be(GravityUnit.SpecificGravity);
        envelope.Data.Preferences.Theme.Should().Be(ThemePreference.Dark);
        envelope.Data.Preferences.DefaultBatchSizeLiters.Should().Be(20.0m);
        envelope.Data.Preferences.DefaultEfficiencyPercent.Should().Be(75.0m);
        envelope.Data.Preferences.DefaultBoilTimeMinutes.Should().Be(60);
        envelope.Data.Preferences.MqttTopicPrefix.Should().Be("brewyou/equipment");
    }

    [Fact]
    public async Task UpdateUserPreferences_Authenticated_PersistsAndReturnsUpdatedUser()
    {
        var (token, _) = await RegisterTestUserAsync("update_pref");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateReq = new UpdateUserPreferencesRequest(
            Language: "sv",
            VolumeUnit: VolumeUnit.Gallons,
            WeightUnit: WeightUnit.Imperial,
            TemperatureUnit: TemperatureUnit.Fahrenheit,
            GravityUnit: GravityUnit.Plato,
            Theme: ThemePreference.Light,
            DefaultBatchSizeLiters: 40.0m,
            DefaultEfficiencyPercent: 82.0m,
            DefaultBoilTimeMinutes: 90
        );

        var putResponse = await _client.PutAsJsonAsync("/api/v1/auth/me/preferences", updateReq);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var putEnvelope = await putResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        putEnvelope!.Success.Should().BeTrue();
        var updated = putEnvelope.Data!;

        updated.PreferredLanguage.Should().Be("sv");
        updated.PreferredVolumeUnit.Should().Be(VolumeUnit.Gallons);
        updated.Preferences.Language.Should().Be("sv");
        updated.Preferences.VolumeUnit.Should().Be(VolumeUnit.Gallons);
        updated.Preferences.WeightUnit.Should().Be(WeightUnit.Imperial);
        updated.Preferences.TemperatureUnit.Should().Be(TemperatureUnit.Fahrenheit);
        updated.Preferences.GravityUnit.Should().Be(GravityUnit.Plato);
        updated.Preferences.Theme.Should().Be(ThemePreference.Light);
        updated.Preferences.DefaultBatchSizeLiters.Should().Be(40.0m);
        updated.Preferences.DefaultEfficiencyPercent.Should().Be(82.0m);
        updated.Preferences.DefaultBoilTimeMinutes.Should().Be(90);

        // Verify persistence via GET /me
        var getResponse = await _client.GetAsync("/api/v1/auth/me");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getEnvelope = await getResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        getEnvelope!.Data!.Preferences.GravityUnit.Should().Be(GravityUnit.Plato);
        getEnvelope.Data.Preferences.DefaultBatchSizeLiters.Should().Be(40.0m);
    }

    [Fact]
    public async Task UpdateUserPreferences_WithMqttConnectivity_PersistsAndReturnsMqttSettings()
    {
        var (token, _) = await RegisterTestUserAsync("mqtt_pref");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateReq = new UpdateUserPreferencesRequest(
            MqttHost: "mqtt.brewery.local",
            MqttPort: 8883,
            MqttUsername: "sensor_user",
            MqttPassword: "SecretSensorPassword123!",
            MqttCertificate: "-----BEGIN CERTIFICATE-----\nMIIDXTCCAkWgAwIBAgIJA...\n-----END CERTIFICATE-----",
            MqttTopicPrefix: "/mybrew/equipment/"
        );

        var putResponse = await _client.PutAsJsonAsync("/api/v1/auth/me/preferences", updateReq);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var putEnvelope = await putResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        putEnvelope!.Success.Should().BeTrue();
        var updated = putEnvelope.Data!;

        updated.Preferences.MqttHost.Should().Be("mqtt.brewery.local");
        updated.Preferences.MqttPort.Should().Be(8883);
        updated.Preferences.MqttUsername.Should().Be("sensor_user");
        updated.Preferences.MqttPassword.Should().BeNull();
        updated.Preferences.HasMqttPassword.Should().BeTrue();
        updated.Preferences.MqttCertificate.Should().Contain("BEGIN CERTIFICATE");
        updated.Preferences.MqttTopicPrefix.Should().Be("mybrew/equipment");

        // Verify GET /api/v1/auth/me retains MQTT credentials and masks password
        var getResponse = await _client.GetAsync("/api/v1/auth/me");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getEnvelope = await getResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        getEnvelope!.Data!.Preferences.MqttHost.Should().Be("mqtt.brewery.local");
        getEnvelope.Data.Preferences.MqttPort.Should().Be(8883);
        getEnvelope.Data.Preferences.MqttUsername.Should().Be("sensor_user");
        getEnvelope.Data.Preferences.MqttPassword.Should().BeNull();
        getEnvelope.Data.Preferences.HasMqttPassword.Should().BeTrue();
        getEnvelope.Data.Preferences.MqttTopicPrefix.Should().Be("mybrew/equipment");
    }

    [Fact]
    public async Task UpdateUserProfile_Authenticated_UpdatesDisplayName()
    {
        var (token, _) = await RegisterTestUserAsync("update_name");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var profileReq = new UpdateProfileRequest("HopHead Artisan");
        var putResponse = await _client.PutAsJsonAsync("/api/v1/auth/me/profile", profileReq);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await putResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.DisplayName.Should().Be("HopHead Artisan");

        // Verify via GET /me
        var getResponse = await _client.GetAsync("/api/v1/auth/me");
        var getEnvelope = await getResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        getEnvelope!.Data!.DisplayName.Should().Be("HopHead Artisan");
    }

    [Fact]
    public async Task UpdateUserPreferences_Unauthenticated_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var updateReq = new UpdateUserPreferencesRequest(Language: "sv");
        var response = await _client.PutAsJsonAsync("/api/v1/auth/me/preferences", updateReq);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateUserProfile_Unauthenticated_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var req = new UpdateProfileRequest("Hacker");
        var response = await _client.PutAsJsonAsync("/api/v1/auth/me/profile", req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateUserPreferences_InvalidValues_ReturnsBadRequestValidationEnvelope()
    {
        var (token, _) = await RegisterTestUserAsync("invalid_pref");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateReq = new UpdateUserPreferencesRequest(
            Language: "invalid-code",
            DefaultEfficiencyPercent: 120m
        );

        var response = await _client.PutAsJsonAsync("/api/v1/auth/me/preferences", updateReq);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("VALIDATION_ERROR");
        envelope.Error.Details.Should().Contain(d => d.Field == "Language");
        envelope.Error.Details.Should().Contain(d => d.Field == "DefaultEfficiencyPercent");
    }

    [Fact]
    public async Task GetMqttStatus_Unauthenticated_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/auth/me/mqtt-status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMqttStatus_NotConfigured_ReturnsConfiguredFalse()
    {
        var (token, _) = await RegisterTestUserAsync("mqtt_status_none");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/auth/me/mqtt-status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<MqttStatusResult>>();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Configured.Should().BeFalse();
        envelope.Data.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task GetMqttStatus_ConfiguredUnreachable_ReturnsConfiguredTrueAndConnectedFalse()
    {
        var (token, _) = await RegisterTestUserAsync("mqtt_status_unreach");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Configure an unreachable loopback port
        var updateReq = new UpdateUserPreferencesRequest(
            MqttHost: "127.0.0.1",
            MqttPort: 59998
        );
        var putResponse = await _client.PutAsJsonAsync("/api/v1/auth/me/preferences", updateReq);
        putResponse.EnsureSuccessStatusCode();

        var response = await _client.GetAsync("/api/v1/auth/me/mqtt-status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<MqttStatusResult>>();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Configured.Should().BeTrue();
        envelope.Data.Connected.Should().BeFalse();
        envelope.Data.Host.Should().Be("127.0.0.1");
        envelope.Data.Port.Should().Be(59998);
    }

    [Fact]
    public async Task GetMqttStatus_WithQueryOverride_ChecksSpecifiedHostAndPort()
    {
        var (token, _) = await RegisterTestUserAsync("mqtt_status_query");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/auth/me/mqtt-status?host=127.0.0.1&port=59997");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<MqttStatusResult>>();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Configured.Should().BeTrue();
        envelope.Data.Connected.Should().BeFalse();
        envelope.Data.Host.Should().Be("127.0.0.1");
        envelope.Data.Port.Should().Be(59997);
    }
}