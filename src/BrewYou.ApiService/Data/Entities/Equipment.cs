using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EquipmentType
{
    Boiler,
    Fermenter,
    Keg,
    Sensor,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EquipmentSubtype
{
    // Boilers
    AllInOne,
    Pan,
    Hlt,
    HermsRims,

    // Fermenters
    Bucket,
    ConicalFermenter,
    Carboy,
    PressureFermenter,
    StainlessBucket,

    // Kegs
    Cornelius,
    Minikeg,
    MiniBarrel,
    PetKeg,

    // Sensors
    ISpindel,
    Tilt,
    GenericSensor,

    // Generic / Fallback
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VolumeUnit
{
    Liters,
    Gallons
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EquipmentConnectionType
{
    None,
    HttpPush,
    HttpPoll,
    Mqtt
}

public class Equipment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public Guid BrewerySetupId { get; set; }
    public BrewerySetup? BrewerySetup { get; set; }

    public required string Name { get; set; }
    public EquipmentType Type { get; set; }
    public EquipmentSubtype Subtype { get; set; } = EquipmentSubtype.Other;

    /// <summary>
    /// Entered capacity value.
    /// </summary>
    public decimal Capacity { get; set; }

    /// <summary>
    /// Volume unit of the entered capacity.
    /// </summary>
    public VolumeUnit Unit { get; set; } = VolumeUnit.Liters;

    /// <summary>
    /// Canonical container volume stored in Liters.
    /// </summary>
    public decimal CapacityLiters { get; set; }

    /// <summary>
    /// Entered current volume value in the entered Unit.
    /// </summary>
    public decimal CurrentVolume { get; set; } = 0.0m;

    /// <summary>
    /// Canonical current volume stored in Liters.
    /// </summary>
    public decimal CurrentVolumeLiters { get; set; } = 0.0m;

    /// <summary>
    /// Boil-off evaporation rate override in liters per hour. Null = inherit from BrewerySetup.
    /// </summary>
    public decimal? BoilOffRatePerHour { get; set; }

    /// <summary>
    /// Trub loss override in liters (kettle trub for boilers, yeast cake for fermenters). Null = inherit from BrewerySetup.
    /// </summary>
    public decimal? TrubLossLiters { get; set; }

    /// <summary>
    /// Mash tun dead space override in liters (for all-in-one/BIAB boilers). Null = inherit from BrewerySetup.
    /// </summary>
    public decimal? MashTunDeadSpaceLiters { get; set; }

    /// <summary>
    /// Packaging loss override in liters (for kegs/packaging vessels). Null = inherit from BrewerySetup.
    /// </summary>
    public decimal? PackagingLossLiters { get; set; }

    /// <summary>
    /// Current live temperature in Celsius. Null if sensor not attached or reading not yet received.
    /// </summary>
    public decimal? CurrentTemperatureC { get; set; }

    /// <summary>
    /// Current live specific gravity (e.g. 1.054). Null if not applicable or not reported.
    /// </summary>
    public decimal? CurrentSpecificGravity { get; set; }

    /// <summary>
    /// Current live head pressure in bar (e.g. 1.20). Null if not applicable or not reported.
    /// </summary>
    public decimal? CurrentPressureBar { get; set; }

    /// <summary>
    /// Battery charge percentage (0.0% to 100.0%). Null if mains powered.
    /// </summary>
    public decimal? CurrentBatteryPercent { get; set; }

    /// <summary>
    /// Battery voltage in Volts (e.g. 3.82V). Null if not reported.
    /// </summary>
    public decimal? CurrentBatteryVoltage { get; set; }

    /// <summary>
    /// Timestamp when CurrentTemperatureC was last updated.
    /// </summary>
    public DateTime? TemperatureUpdatedAt { get; set; }

    /// <summary>
    /// Timestamp when any telemetry reading was last ingested.
    /// </summary>
    public DateTime? LastTelemetryAt { get; set; }

    /// <summary>
    /// Cached JSON payload of all latest telemetry attributes from the sensor for instant display.
    /// </summary>
    public string? LatestMetricsJson { get; set; }

    /// <summary>
    /// Configured external connectivity protocol.
    /// </summary>
    public EquipmentConnectionType ConnectionType { get; set; } = EquipmentConnectionType.None;

    /// <summary>
    /// Secret unique ingestion token used for HTTP Push / Webhook telemetry.
    /// </summary>
    public string? ConnectionToken { get; set; }

    /// <summary>
    /// Serialized JSON configuration for connection details (e.g. poll URL, interval, MQTT settings).
    /// </summary>
    public string? ConnectionConfigJson { get; set; }

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}