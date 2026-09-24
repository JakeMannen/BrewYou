namespace BrewYou.ApiService.Data.Entities;

public class BrewerySetup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Default grain absorption rate in liters per kilogram of grain.
    /// </summary>
    public decimal DefaultGrainAbsorptionRate { get; set; } = 0.96m;

    /// <summary>
    /// Default boil-off evaporation rate in liters per hour.
    /// </summary>
    public decimal DefaultBoilOffRatePerHour { get; set; } = 3.0m;

    /// <summary>
    /// Default kettle trub and chiller loss in liters.
    /// </summary>
    public decimal DefaultKettleTrubLossLiters { get; set; } = 1.5m;

    /// <summary>
    /// Default fermenter yeast cake and trub loss in liters.
    /// </summary>
    public decimal DefaultFermenterLossLiters { get; set; } = 1.5m;

    /// <summary>
    /// Default mash tun dead space in liters (undrainable volume below outlet).
    /// </summary>
    public decimal DefaultMashTunDeadSpaceLiters { get; set; } = 0.0m;

    /// <summary>
    /// Wort thermal contraction percentage from boiling to room temperature.
    /// </summary>
    public decimal CoolingShrinkagePercent { get; set; } = 4.0m;

    /// <summary>
    /// Default packaging vessel loss in liters (bottling/kegging transfer loss).
    /// </summary>
    public decimal DefaultPackagingLossLiters { get; set; } = 0.5m;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Equipment> Equipment { get; set; } = new List<Equipment>();
}