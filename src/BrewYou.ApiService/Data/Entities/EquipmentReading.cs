namespace BrewYou.ApiService.Data.Entities;

public class EquipmentReading
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    /// <summary>
    /// Optional batch association when this reading occurred while the equipment was actively used in a batch.
    /// </summary>
    public Guid? BatchId { get; set; }
    public Batch? Batch { get; set; }

    /// <summary>
    /// The brewing stage at the time of the reading (e.g. Mash, Boil, Ferment, Condition, Package).
    /// </summary>
    public BrewStage? Stage { get; set; }

    /// <summary>
    /// Optional mash step association when the reading occurred during a specific mash profile step.
    /// </summary>
    public Guid? BatchMashStepId { get; set; }
    public BatchMashStep? BatchMashStep { get; set; }

    /// <summary>
    /// User-friendly name of the step or rest (e.g. "Saccharification Rest", "Boil", "Primary Fermentation").
    /// </summary>
    public string? StepName { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Recorded temperature value in Celsius.
    /// </summary>
    public decimal TemperatureC { get; set; }

    /// <summary>
    /// Optional recorded specific gravity value from hydrometer sensors (Tilt, iSpindel).
    /// </summary>
    public decimal? SpecificGravity { get; set; }

    /// <summary>
    /// Optional recorded head pressure in bar from pressure-capable fermenters, unitanks, or kegs.
    /// </summary>
    public decimal? PressureBar { get; set; }

    /// <summary>
    /// Optional battery charge percentage (0.0% to 100.0%) for wireless sensors.
    /// </summary>
    public decimal? BatteryPercent { get; set; }

    /// <summary>
    /// Optional battery voltage in Volts (e.g. 3.2V to 4.2V).
    /// </summary>
    public decimal? BatteryVoltage { get; set; }

    /// <summary>
    /// Optional hydrometer tilt angle in degrees (0.00° to 90.00°).
    /// </summary>
    public decimal? TiltDegrees { get; set; }

    /// <summary>
    /// Optional Wi-Fi / BLE Received Signal Strength Indicator in dBm (e.g. -65).
    /// </summary>
    public short? Rssi { get; set; }

    /// <summary>
    /// Arbitrary or vendor-specific telemetry attributes stored as JSON (e.g. pH, flow rate, heater duty cycle).
    /// </summary>
    public string? MetricsJson { get; set; }

    /// <summary>
    /// Source or protocol of this reading (e.g. "HttpPush", "HttpPoll", "Manual", "Mqtt").
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Optional truncated raw payload received from the sensor for audit / debugging.
    /// </summary>
    public string? RawPayload { get; set; }

    /// <summary>
    /// Optional manual notes or annotations for this reading.
    /// </summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}