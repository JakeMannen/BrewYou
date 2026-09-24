using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Data;

public class BrewYouDbContext : IdentityDbContext<ApplicationUser>
{
    public BrewYouDbContext(DbContextOptions<BrewYouDbContext> options)
        : base(options)
    {
    }

    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<IngredientStock> IngredientStocks => Set<IngredientStock>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<BrewerySetup> BrewerySetups => Set<BrewerySetup>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<BatchReading> BatchReadings => Set<BatchReading>();
    public DbSet<BatchIngredient> BatchIngredients => Set<BatchIngredient>();
    public DbSet<BatchStageHistory> BatchStageHistories => Set<BatchStageHistory>();
    public DbSet<BatchVolumeProfile> BatchVolumeProfiles => Set<BatchVolumeProfile>();
    public DbSet<RecipeMashStep> RecipeMashSteps => Set<RecipeMashStep>();
    public DbSet<BatchMashStep> BatchMashSteps => Set<BatchMashStep>();
    public DbSet<RecipeFermentationStep> RecipeFermentationSteps => Set<RecipeFermentationStep>();
    public DbSet<BatchFermentationStep> BatchFermentationSteps => Set<BatchFermentationStep>();
    public DbSet<BatchSensorAssignment> BatchSensorAssignments => Set<BatchSensorAssignment>();
    public DbSet<EquipmentReading> EquipmentReadings => Set<EquipmentReading>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.PreferredLanguage).HasMaxLength(10).HasDefaultValue("en");
            entity.Property(u => u.PreferredVolumeUnit).HasConversion<string>().HasMaxLength(20);
            entity.Property(u => u.PreferredWeightUnit).HasConversion<string>().HasMaxLength(20);
            entity.Property(u => u.PreferredTemperatureUnit).HasConversion<string>().HasMaxLength(20);
            entity.Property(u => u.PreferredGravityUnit).HasConversion<string>().HasMaxLength(30);
            entity.Property(u => u.PreferredTheme).HasConversion<string>().HasMaxLength(20);
            entity.Property(u => u.DefaultBatchSizeLiters).HasPrecision(6, 2);
            entity.Property(u => u.DefaultEfficiencyPercent).HasPrecision(5, 2);
            entity.Property(u => u.MqttHost).HasMaxLength(255);
            entity.Property(u => u.MqttPort);
            entity.Property(u => u.MqttUsername).HasMaxLength(100);
            entity.Property(u => u.MqttPassword).HasMaxLength(1024);
            entity.Property(u => u.MqttCertificate).HasMaxLength(16384);
            entity.Property(u => u.MqttTopicPrefix).HasMaxLength(150).HasDefaultValue("brewyou/equipment");
        });

        builder.Entity<BrewerySetup>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Description).HasMaxLength(500);
            entity.Property(s => s.IsDefault).HasDefaultValue(false);

            entity.HasOne(s => s.User)
                  .WithMany(u => u.BrewerySetups)
                  .HasForeignKey(s => s.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.UserId, s.IsDefault })
                  .HasFilter("\"IsDefault\" = TRUE")
                  .IsUnique();
        });

        builder.Entity<Recipe>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Name).HasMaxLength(150).IsRequired();
            entity.Property(r => r.BeerStyle).HasMaxLength(100).IsRequired();
            entity.Property(r => r.BatchSizeLiters).HasPrecision(6, 2);
            entity.Property(r => r.EfficiencyPercent).HasPrecision(5, 2);
            entity.Property(r => r.OriginalGravity).HasPrecision(5, 4);
            entity.Property(r => r.FinalGravity).HasPrecision(5, 4);
            entity.Property(r => r.AlcoholByVolume).HasPrecision(4, 2);
            entity.Property(r => r.BitternessIbu).HasPrecision(6, 2);
            entity.Property(r => r.ColorSrm).HasPrecision(5, 2);

            entity.HasOne(r => r.User)
                  .WithMany(u => u.Recipes)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.UserId, r.Name }).IsUnique();
            entity.HasIndex(r => r.IsPublic);
        });

        builder.Entity<Ingredient>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Name).HasMaxLength(150).IsRequired();
            entity.Property(i => i.Description).HasMaxLength(1000);
            entity.Property(i => i.PotentialGravity).HasPrecision(5, 4);
            entity.Property(i => i.ColorSrm).HasPrecision(5, 2);
            entity.Property(i => i.AlphaAcidPercent).HasPrecision(4, 2);
            entity.Property(i => i.AttenuationPercent).HasPrecision(4, 2);
            entity.Property(i => i.Form).HasMaxLength(50);

            entity.HasIndex(i => i.IsCatalogItem);
            entity.HasIndex(i => new { i.CreatedByUserId, i.IsCatalogItem });
            entity.HasIndex(i => new { i.CreatedByUserId, i.Name });
        });

        builder.Entity<IngredientStock>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Amount).HasPrecision(8, 3).HasDefaultValue(0.0m).IsRequired();
            entity.Property(s => s.Unit).HasMaxLength(20).IsRequired();
            entity.Property(s => s.LowStockAlertThreshold).HasPrecision(8, 3);

            entity.HasOne(s => s.User)
                  .WithMany()
                  .HasForeignKey(s => s.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.Ingredient)
                  .WithMany()
                  .HasForeignKey(s => s.IngredientId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.UserId, s.IngredientId }).IsUnique();
        });

        builder.Entity<RecipeIngredient>(entity =>
        {
            entity.HasKey(ri => ri.Id);
            entity.Property(ri => ri.Amount).HasPrecision(8, 3);
            entity.Property(ri => ri.Unit).HasMaxLength(20);
            entity.Property(ri => ri.Form).HasMaxLength(50);

            entity.HasOne(ri => ri.Recipe)
                  .WithMany(r => r.Ingredients)
                  .HasForeignKey(ri => ri.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ri => ri.Ingredient)
                  .WithMany(i => i.RecipeIngredients)
                  .HasForeignKey(ri => ri.IngredientId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BatchIngredient>(entity =>
        {
            entity.HasKey(bi => bi.Id);
            entity.Property(bi => bi.Name).HasMaxLength(150).IsRequired();
            entity.Property(bi => bi.Amount).HasPrecision(8, 3);
            entity.Property(bi => bi.Unit).HasMaxLength(20);
            entity.Property(bi => bi.Form).HasMaxLength(50);
            entity.Property(bi => bi.Notes).HasMaxLength(1000);

            entity.HasOne(bi => bi.Batch)
                  .WithMany(b => b.Ingredients)
                  .HasForeignKey(bi => bi.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Equipment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(e => e.Subtype).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(e => e.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Capacity).HasPrecision(8, 2).IsRequired();
            entity.Property(e => e.CapacityLiters).HasPrecision(8, 2).IsRequired();
            entity.Property(e => e.CurrentVolume).HasPrecision(8, 2).HasDefaultValue(0.0m).IsRequired();
            entity.Property(e => e.CurrentVolumeLiters).HasPrecision(8, 2).HasDefaultValue(0.0m).IsRequired();
            entity.Property(e => e.CurrentTemperatureC).HasPrecision(5, 2);
            entity.Property(e => e.CurrentSpecificGravity).HasPrecision(5, 4);
            entity.Property(e => e.CurrentPressureBar).HasPrecision(4, 2);
            entity.Property(e => e.CurrentBatteryPercent).HasPrecision(4, 1);
            entity.Property(e => e.CurrentBatteryVoltage).HasPrecision(4, 2);
            entity.Property(e => e.LatestMetricsJson).HasMaxLength(4000);
            entity.Property(e => e.ConnectionType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(EquipmentConnectionType.None).IsRequired();
            entity.Property(e => e.ConnectionToken).HasMaxLength(100);
            entity.Property(e => e.ConnectionConfigJson).HasMaxLength(4000);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Notes).HasMaxLength(2000);

            entity.HasOne(e => e.User)
                  .WithMany(u => u.Equipment)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.BrewerySetup)
                  .WithMany(s => s.Equipment)
                  .HasForeignKey(e => e.BrewerySetupId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.BrewerySetupId });
            entity.HasIndex(e => new { e.UserId, e.BrewerySetupId, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.Type });
            entity.HasIndex(e => new { e.UserId, e.Type, e.Subtype });
            entity.HasIndex(e => new { e.UserId, e.Name });
            entity.HasIndex(e => e.ConnectionToken).IsUnique();
        });

        builder.Entity<EquipmentReading>(entity =>
        {
            entity.HasKey(er => er.Id);
            entity.Property(er => er.TemperatureC).HasPrecision(5, 2).IsRequired();
            entity.Property(er => er.SpecificGravity).HasPrecision(5, 4);
            entity.Property(er => er.PressureBar).HasPrecision(4, 2);
            entity.Property(er => er.BatteryPercent).HasPrecision(4, 1);
            entity.Property(er => er.BatteryVoltage).HasPrecision(4, 2);
            entity.Property(er => er.TiltDegrees).HasPrecision(5, 2);
            entity.Property(er => er.MetricsJson).HasMaxLength(4000);
            entity.Property(er => er.Source).HasMaxLength(50);
            entity.Property(er => er.RawPayload).HasMaxLength(2000);
            entity.Property(er => er.Stage).HasConversion<string>().HasMaxLength(30);
            entity.Property(er => er.StepName).HasMaxLength(100);
            entity.Property(er => er.Notes).HasMaxLength(1000);

            entity.HasOne(er => er.Equipment)
                  .WithMany()
                  .HasForeignKey(er => er.EquipmentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(er => er.Batch)
                  .WithMany(b => b.EquipmentReadings)
                  .HasForeignKey(er => er.BatchId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(er => er.BatchMashStep)
                  .WithMany()
                  .HasForeignKey(er => er.BatchMashStepId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(er => new { er.EquipmentId, er.Timestamp });
            entity.HasIndex(er => new { er.BatchId, er.Timestamp });
            entity.HasIndex(er => new { er.BatchId, er.Stage, er.Timestamp });
            entity.HasIndex(er => new { er.BatchMashStepId, er.Timestamp });
        });

        builder.Entity<Batch>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.BatchCode).HasMaxLength(30).IsRequired();
            entity.Property(b => b.Name).HasMaxLength(150).IsRequired();
            entity.Property(b => b.BeerStyle).HasMaxLength(100).IsRequired();
            entity.Property(b => b.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(b => b.CurrentStage).HasConversion<string>().HasMaxLength(30).IsRequired();

            entity.Property(b => b.TargetOg).HasPrecision(5, 4);
            entity.Property(b => b.TargetFg).HasPrecision(5, 4);
            entity.Property(b => b.TargetAbv).HasPrecision(4, 2);
            entity.Property(b => b.TargetIbu).HasPrecision(6, 2);
            entity.Property(b => b.TargetColorSrm).HasPrecision(5, 2);
            entity.Property(b => b.TargetBatchSizeLiters).HasPrecision(6, 2);
            entity.Property(b => b.EfficiencyPercent).HasPrecision(5, 2);

            entity.Property(b => b.MeasuredOg).HasPrecision(5, 4);
            entity.Property(b => b.CurrentGravity).HasPrecision(5, 4);
            entity.Property(b => b.MeasuredFg).HasPrecision(5, 4);
            entity.Property(b => b.AlcoholByVolume).HasPrecision(4, 2);
            entity.Property(b => b.BrewhouseEfficiency).HasPrecision(5, 2);
            entity.Property(b => b.MeasuredBatchSizeLiters).HasPrecision(6, 2);
            entity.Property(b => b.PitchTemperatureC).HasPrecision(5, 2);

            entity.Property(b => b.Notes).HasMaxLength(4000);

            entity.HasOne(b => b.User)
                  .WithMany()
                  .HasForeignKey(b => b.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(b => b.Recipe)
                  .WithMany()
                  .HasForeignKey(b => b.RecipeId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(b => b.BrewerySetup)
                  .WithMany()
                  .HasForeignKey(b => b.BrewerySetupId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(b => b.Boiler)
                  .WithMany()
                  .HasForeignKey(b => b.BoilerId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(b => b.Fermenter)
                  .WithMany()
                  .HasForeignKey(b => b.FermenterId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(b => b.PackagingVessel)
                  .WithMany()
                  .HasForeignKey(b => b.PackagingVesselId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(b => new { b.UserId, b.BatchCode }).IsUnique();
            entity.HasIndex(b => new { b.UserId, b.Status });
            entity.HasIndex(b => new { b.UserId, b.BrewDate });
            entity.HasIndex(b => b.BoilerId);
            entity.HasIndex(b => b.FermenterId);
            entity.HasIndex(b => b.PackagingVesselId);
        });

        builder.Entity<BatchReading>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.SpecificGravity).HasPrecision(5, 4).IsRequired();
            entity.Property(r => r.TemperatureC).HasPrecision(5, 2);
            entity.Property(r => r.Notes).HasMaxLength(1000);

            entity.HasOne(r => r.Batch)
                  .WithMany(b => b.Readings)
                  .HasForeignKey(r => r.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.BatchId, r.Timestamp });
        });

        builder.Entity<BatchIngredient>(entity =>
        {
            entity.HasKey(bi => bi.Id);
            entity.Property(bi => bi.Name).HasMaxLength(150).IsRequired();
            entity.Property(bi => bi.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(bi => bi.AdditionStage).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(bi => bi.Amount).HasPrecision(8, 3).IsRequired();
            entity.Property(bi => bi.Unit).HasMaxLength(20).IsRequired();
            entity.Property(bi => bi.Notes).HasMaxLength(1000);

            entity.HasOne(bi => bi.Batch)
                  .WithMany(b => b.Ingredients)
                  .HasForeignKey(bi => bi.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(bi => new { bi.BatchId, bi.AdditionStage });
        });

        builder.Entity<BatchStageHistory>(entity =>
        {
            entity.HasKey(sh => sh.Id);
            entity.Property(sh => sh.Stage).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(sh => sh.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(sh => sh.Notes).HasMaxLength(1000);

            entity.HasOne(sh => sh.Batch)
                  .WithMany(b => b.StageHistory)
                  .HasForeignKey(sh => sh.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(sh => new { sh.BatchId, sh.Stage });
        });

        builder.Entity<BatchVolumeProfile>(entity =>
        {
            entity.HasOne(p => p.Batch)
                  .WithOne(b => b.VolumeProfile)
                  .HasForeignKey<BatchVolumeProfile>(p => p.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecipeMashStep>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(s => s.TemperatureC).HasPrecision(5, 2).IsRequired();
            entity.Property(s => s.InfuseAmountLiters).HasPrecision(6, 2);
            entity.Property(s => s.Notes).HasMaxLength(1000);

            entity.HasOne(s => s.Recipe)
                  .WithMany(r => r.MashSteps)
                  .HasForeignKey(s => s.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.RecipeId, s.StepOrder }).IsUnique();
        });

        builder.Entity<BatchMashStep>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(s => s.TargetTemperatureC).HasPrecision(5, 2).IsRequired();
            entity.Property(s => s.ActualTemperatureC).HasPrecision(5, 2);
            entity.Property(s => s.InfuseAmountLiters).HasPrecision(6, 2);
            entity.Property(s => s.Notes).HasMaxLength(1000);

            entity.HasOne(s => s.Batch)
                  .WithMany(b => b.MashSteps)
                  .HasForeignKey(s => s.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.BatchId, s.StepOrder }).IsUnique();
        });

        builder.Entity<RecipeFermentationStep>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(s => s.TargetTemperatureC).HasPrecision(5, 2).IsRequired();
            entity.Property(s => s.TriggerGravity).HasPrecision(5, 4);
            entity.Property(s => s.Notes).HasMaxLength(1000);

            entity.HasOne(s => s.Recipe)
                  .WithMany(r => r.FermentationSteps)
                  .HasForeignKey(s => s.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.RecipeId, s.StepOrder }).IsUnique();
        });

        builder.Entity<BatchFermentationStep>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(s => s.TargetTemperatureC).HasPrecision(5, 2).IsRequired();
            entity.Property(s => s.ActualTemperatureC).HasPrecision(5, 2);
            entity.Property(s => s.TriggerGravity).HasPrecision(5, 4);
            entity.Property(s => s.Notes).HasMaxLength(1000);

            entity.HasOne(s => s.Batch)
                  .WithMany(b => b.FermentationSteps)
                  .HasForeignKey(s => s.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.BatchId, s.StepOrder }).IsUnique();
        });

        builder.Entity<BatchSensorAssignment>(entity =>
        {
            entity.HasKey(sa => sa.Id);
            entity.Property(sa => sa.Stage).HasConversion<string>().HasMaxLength(30);

            entity.HasOne(sa => sa.Batch)
                  .WithMany(b => b.SensorAssignments)
                  .HasForeignKey(sa => sa.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(sa => sa.Equipment)
                  .WithMany()
                  .HasForeignKey(sa => sa.EquipmentId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(sa => sa.BatchMashStep)
                  .WithMany()
                  .HasForeignKey(sa => sa.BatchMashStepId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(sa => new { sa.BatchId, sa.EquipmentId });
        });
    }
}