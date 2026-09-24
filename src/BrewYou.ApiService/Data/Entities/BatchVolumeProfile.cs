namespace BrewYou.ApiService.Data.Entities;

public class BatchVolumeProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }

    // Planned Water Schedule (Liters)
    public decimal TotalWaterLiters { get; set; }
    public decimal StrikeWaterLiters { get; set; }
    public decimal SpargeWaterLiters { get; set; }

    // Pre-Boil Checkpoint
    public decimal TargetPreBoilVolumeLiters { get; set; }
    public decimal? MeasuredPreBoilVolumeLiters { get; set; }
    public decimal? MeasuredPreBoilGravity { get; set; }

    // Post-Boil Checkpoint
    public decimal TargetPostBoilVolumeLiters { get; set; }
    public decimal? MeasuredPostBoilVolumeLiters { get; set; }

    // Fermenter Checkpoint
    public decimal TargetFermenterVolumeLiters { get; set; }
    public decimal? MeasuredFermenterVolumeLiters { get; set; }

    // Packaging Checkpoint
    public decimal TargetPackagedVolumeLiters { get; set; }
    public decimal? MeasuredPackagedVolumeLiters { get; set; }

    // Snapshotted Loss Parameters (frozen at batch creation)
    public decimal BoilOffRatePerHour { get; set; }
    public decimal GrainAbsorptionRateLPerKg { get; set; }
    public decimal KettleTrubLossLiters { get; set; }
    public decimal FermenterTrubLossLiters { get; set; }
    public decimal MashTunDeadSpaceLiters { get; set; }
    public decimal CoolingShrinkagePercent { get; set; }
    public decimal PackagingLossLiters { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}