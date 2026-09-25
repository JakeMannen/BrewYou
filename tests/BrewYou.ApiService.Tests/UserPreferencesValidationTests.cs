using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Validation;
using FluentAssertions;

namespace BrewYou.ApiService.Tests;

public class UserPreferencesValidationTests
{
    private readonly UpdateUserPreferencesRequestValidator _preferencesValidator = new();
    private readonly UpdateProfileRequestValidator _profileValidator = new();

    [Fact]
    public async Task ValidatePreferences_ValidPayload_PassesValidation()
    {
        var request = new UpdateUserPreferencesRequest(
            Language: "sv",
            VolumeUnit: VolumeUnit.Gallons,
            WeightUnit: WeightUnit.Imperial,
            TemperatureUnit: TemperatureUnit.Fahrenheit,
            GravityUnit: GravityUnit.Plato,
            Theme: ThemePreference.Light,
            DefaultBatchSizeLiters: 25.0m,
            DefaultEfficiencyPercent: 78.5m,
            DefaultBoilTimeMinutes: 75
        );

        var result = await _preferencesValidator.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePreferences_ImperialStoutTheme_PassesValidation()
    {
        var request = new UpdateUserPreferencesRequest(
            Theme: ThemePreference.ImperialStout
        );

        var result = await _preferencesValidator.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePreferences_ChocolatePorterTheme_PassesValidation()
    {
        var request = new UpdateUserPreferencesRequest(
            Theme: ThemePreference.ChocolatePorter
        );

        var result = await _preferencesValidator.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePreferences_InvalidLanguage_FailsValidation()
    {
        var request = new UpdateUserPreferencesRequest(Language: "fr");
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Language");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10001)]
    public async Task ValidatePreferences_OutOfRangeBatchSize_FailsValidation(decimal invalidBatchSize)
    {
        var request = new UpdateUserPreferencesRequest(DefaultBatchSizeLiters: invalidBatchSize);
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DefaultBatchSizeLiters");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(105)]
    public async Task ValidatePreferences_OutOfRangeEfficiency_FailsValidation(decimal invalidEfficiency)
    {
        var request = new UpdateUserPreferencesRequest(DefaultEfficiencyPercent: invalidEfficiency);
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DefaultEfficiencyPercent");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(400)]
    public async Task ValidatePreferences_OutOfRangeBoilTime_FailsValidation(int invalidBoilTime)
    {
        var request = new UpdateUserPreferencesRequest(DefaultBoilTimeMinutes: invalidBoilTime);
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DefaultBoilTimeMinutes");
    }

    [Fact]
    public async Task ValidateProfile_ValidDisplayName_PassesValidation()
    {
        var request = new UpdateProfileRequest("Master Brewer 42");
        var result = await _profileValidator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateProfile_EmptyDisplayName_FailsValidation(string emptyName)
    {
        var request = new UpdateProfileRequest(emptyName);
        var result = await _profileValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DisplayName");
    }

    [Fact]
    public async Task ValidateProfile_HtmlTagsInDisplayName_FailsValidation()
    {
        var request = new UpdateProfileRequest("<script>alert('xss')</script>");
        var result = await _profileValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DisplayName");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task ValidatePreferences_InvalidMqttPort_FailsValidation(int invalidPort)
    {
        var request = new UpdateUserPreferencesRequest(MqttPort: invalidPort);
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MqttPort");
    }

    [Fact]
    public async Task ValidatePreferences_ValidMqttPayload_PassesValidation()
    {
        var request = new UpdateUserPreferencesRequest(
            MqttHost: "broker.emqx.io",
            MqttPort: 8883,
            MqttUsername: "sensor_admin",
            MqttPassword: "strong-password-here",
            MqttCertificate: "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----"
        );
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePreferences_InvalidPemCertificate_FailsValidation()
    {
        var request = new UpdateUserPreferencesRequest(
            MqttCertificate: "THIS_IS_NOT_A_VALID_PEM_CERTIFICATE"
        );
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MqttCertificate");
    }

    [Theory]
    [InlineData("brewyou/equipment")]
    [InlineData("brewery/fermenters")]
    [InlineData("homebrew/sensors/vessels")]
    public async Task ValidatePreferences_ValidMqttTopicPrefix_PassesValidation(string topicPrefix)
    {
        var request = new UpdateUserPreferencesRequest(MqttTopicPrefix: topicPrefix);
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("brewyou/+/equipment")]
    [InlineData("brewyou/#")]
    [InlineData("$SYS/broker")]
    public async Task ValidatePreferences_WildcardsOrReservedInMqttTopicPrefix_FailsValidation(string invalidPrefix)
    {
        var request = new UpdateUserPreferencesRequest(MqttTopicPrefix: invalidPrefix);
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MqttTopicPrefix");
    }

    [Fact]
    public async Task ValidatePreferences_TooLongMqttTopicPrefix_FailsValidation()
    {
        var request = new UpdateUserPreferencesRequest(MqttTopicPrefix: new string('a', 151));
        var result = await _preferencesValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MqttTopicPrefix");
    }
}