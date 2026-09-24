using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class MqttTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MqttTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("169.254.169.254", 1883)]
    [InlineData("::ffff:169.254.169.254", 1883)]
    [InlineData("0.0.0.0", 1883)]
    [InlineData("::ffff:0.0.0.0", 1883)]
    [InlineData("224.0.0.1", 1883)]
    [InlineData("localhost", 0)]
    [InlineData("localhost", 65536)]
    [InlineData("", 1883)]
    public async Task MqttConnectivityChecker_BlocksInvalidOrForbiddenAddresses(string host, int port)
    {
        var checker = new MqttConnectivityChecker();
        var result = await checker.CanConnectAsync(host, port, TimeSpan.FromMilliseconds(500));

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("21.5", 21.5, "MqttScalarC")]
    [InlineData("20.0 C", 20.0, "MqttScalarC")]
    [InlineData("22.3°C", 22.3, "MqttScalarC")]
    [InlineData("68 F", 20.0, "MqttScalarF")]
    [InlineData("77°F", 25.0, "MqttScalarF")]
    public void ParseScalarTemperature_ValidNumericInputs_ParsesCorrectly(string raw, decimal expectedC, string expectedFormat)
    {
        var result = TelemetryService.ParseScalarTemperature(raw);

        result.Success.Should().BeTrue();
        result.TemperatureC.Should().Be(expectedC);
        result.DetectedFormat.Should().Be(expectedFormat);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("abc 25 C")]
    [InlineData("7e28 F")]
    [InlineData("9999999999999999999999999999 F")]
    [InlineData("500 C")]
    [InlineData("-200 C")]
    public void ParseScalarTemperature_InvalidInputs_FailsGracefully(string raw)
    {
        var result = TelemetryService.ParseScalarTemperature(raw);

        result.Success.Should().BeFalse();
        result.TemperatureC.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"topic\": \"brewery/kettle/temp\"}", "brewery/kettle/temp")]
    [InlineData("{\"Topic\": \"sensors/tilt\"}", "sensors/tilt")]
    [InlineData("{}", null)]
    [InlineData("", null)]
    [InlineData("not-json", null)]
    public void MqttService_ExtractTopic_ExtractsConfiguredTopic(string configJson, string? expectedTopic)
    {
        var topic = MqttService.ExtractTopic(configJson);
        topic.Should().Be(expectedTopic);
    }

    [Fact]
    public async Task IngestMqttTelemetryAsync_ValidJsonPayload_UpdatesEquipmentTemperature()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var user = new ApplicationUser
        {
            UserName = $"mqtt_test_{Guid.NewGuid():N}@brewyou.test",
            Email = $"mqtt_test_{Guid.NewGuid():N}@brewyou.test"
        };
        db.Users.Add(user);

        var setup = new BrewerySetup
        {
            UserId = user.Id,
            Name = "MQTT Test Setup"
        };
        db.BrewerySetups.Add(setup);

        var equipment = new Equipment
        {
            UserId = user.Id,
            BrewerySetupId = setup.Id,
            Name = "MQTT Test Kettle",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            ConnectionType = EquipmentConnectionType.Mqtt,
            ConnectionConfigJson = "{\"topic\":\"brewery/kettle/temp\"}"
        };
        db.Equipment.Add(equipment);
        await db.SaveChangesAsync();

        var jsonPayload = "{\"temperature\": 65.4, \"gravity\": 1.052}";
        var result = await telemetryService.IngestMqttTelemetryAsync(equipment.Id, jsonPayload);

        result.Status.Should().Be(TelemetryIngestStatus.Success);
        result.TemperatureC.Should().Be(65.4m);

        // Verify updated in DB
        var updated = await db.Equipment.AsNoTracking().FirstAsync(e => e.Id == equipment.Id);
        updated.CurrentTemperatureC.Should().Be(65.4m);
        updated.TemperatureUpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task IngestMqttTelemetryAsync_ValidScalarPayload_UpdatesEquipmentTemperature()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var user = new ApplicationUser
        {
            UserName = $"mqtt_scalar_{Guid.NewGuid():N}@brewyou.test",
            Email = $"mqtt_scalar_{Guid.NewGuid():N}@brewyou.test"
        };
        db.Users.Add(user);

        var setup = new BrewerySetup
        {
            UserId = user.Id,
            Name = "MQTT Scalar Setup"
        };
        db.BrewerySetups.Add(setup);

        var equipment = new Equipment
        {
            UserId = user.Id,
            BrewerySetupId = setup.Id,
            Name = "MQTT Scalar Sensor",
            Type = EquipmentType.Sensor,
            Subtype = EquipmentSubtype.GenericSensor,
            ConnectionType = EquipmentConnectionType.Mqtt,
            ConnectionConfigJson = "{\"topic\":\"brewery/sensor/temp\"}"
        };
        db.Equipment.Add(equipment);
        await db.SaveChangesAsync();

        var scalarPayload = "23.8 C";
        var result = await telemetryService.IngestMqttTelemetryAsync(equipment.Id, scalarPayload);

        result.Status.Should().Be(TelemetryIngestStatus.Success);
        result.TemperatureC.Should().Be(23.8m);

        var updated = await db.Equipment.AsNoTracking().FirstAsync(e => e.Id == equipment.Id);
        updated.CurrentTemperatureC.Should().Be(23.8m);
    }

    [Fact]
    public async Task IngestMqttTelemetryAsync_OversizedPayload_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var hugePayload = new string('A', 20000);
        var result = await telemetryService.IngestMqttTelemetryAsync(Guid.NewGuid(), hugePayload);

        result.Status.Should().Be(TelemetryIngestStatus.InvalidPayload);
        result.ErrorMessage.Should().Contain("16KB");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("not json", null)]
    [InlineData("{}", null)]
    [InlineData("{\"other\":\"value\"}", null)]
    [InlineData("{\"topic\":\"brewery/mash/temp\"}", "brewery/mash/temp")]
    [InlineData("{\"Topic\":\"brewery/fermenter/1\"}", "brewery/fermenter/1")]
    public void ExtractTopic_HandlesVariousInputsCorrectly(string? configJson, string? expectedTopic)
    {
        var topic = MqttService.ExtractTopic(configJson);
        topic.Should().Be(expectedTopic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IngestMqttTelemetryAsync_EmptyOrWhitespacePayload_ReturnsInvalidPayload(string payload)
    {
        using var scope = _factory.Services.CreateScope();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var result = await telemetryService.IngestMqttTelemetryAsync(Guid.NewGuid(), payload);
        result.Status.Should().Be(TelemetryIngestStatus.InvalidPayload);
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task IngestMqttTelemetryAsync_NonExistentEquipment_ReturnsNotFound()
    {
        using var scope = _factory.Services.CreateScope();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var result = await telemetryService.IngestMqttTelemetryAsync(Guid.NewGuid(), "21.5");
        result.Status.Should().Be(TelemetryIngestStatus.NotFound);
        result.ErrorMessage.Should().Contain("Equipment not found");
    }

    [Theory]
    [InlineData("130.0 C")] // Above max 120 C
    [InlineData("-25.0 C")] // Below min -20 C
    public async Task IngestMqttTelemetryAsync_OutOfRangeTemperature_ReturnsOutOfRange(string outOfRangePayload)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = $"mqtt_range_{Guid.NewGuid():N}@brewyou.test",
            UserName = $"mqtt_range_{Guid.NewGuid():N}@brewyou.test"
        };
        db.Users.Add(user);

        var setup = new BrewerySetup { UserId = user.Id, Name = "Range Setup" };
        db.BrewerySetups.Add(setup);

        var eq = new Equipment
        {
            UserId = user.Id,
            BrewerySetupId = setup.Id,
            Name = "Range Sensor",
            Type = EquipmentType.Sensor,
            ConnectionType = EquipmentConnectionType.Mqtt
        };
        db.Equipment.Add(eq);
        await db.SaveChangesAsync();

        var result = await telemetryService.IngestMqttTelemetryAsync(eq.Id, outOfRangePayload);
        result.Status.Should().Be(TelemetryIngestStatus.OutOfRange);
        result.ErrorMessage.Should().Contain("outside plausible brewing range");
    }

    [Fact]
    public async Task MqttService_DisconnectAsync_WhenNotConnected_ExecutesSafely()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMqttService>();

        var act = () => service.DisconnectAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}