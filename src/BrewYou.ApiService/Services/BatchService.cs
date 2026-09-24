using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace BrewYou.ApiService.Services;

public class BatchService : IBatchService
{
    private const decimal GallonsToLitersFactor = 3.785411784m;
    private const int MaxActiveBatchesPerUser = 50;
    private const int MaxReadingsPerBatch = 1000;

    private readonly BrewYouDbContext _db;
    private readonly ITelemetryBroadcastService _broadcastService;
    private readonly ILogger<BatchService> _logger;

    public BatchService(BrewYouDbContext db, ITelemetryBroadcastService broadcastService, ILogger<BatchService> logger)
    {
        _db = db;
        _broadcastService = broadcastService;
        _logger = logger;
    }

    public async Task<(List<BatchSummaryDto> Items, PaginationMeta Pagination)> GetBatchesAsync(
        string userId, BatchStatus? status, string? search, int page, int limit)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = _db.Batches
            .AsNoTracking()
            .Include(b => b.Fermenter)
            .Where(b => b.UserId == userId);

        if (status.HasValue)
        {
            query = query.Where(b => b.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b =>
                EF.Functions.ILike(b.Name, $"%{term}%") ||
                EF.Functions.ILike(b.BatchCode, $"%{term}%") ||
                EF.Functions.ILike(b.BeerStyle, $"%{term}%"));
        }

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)limit);

        var batches = await query
            .OrderByDescending(b => b.BrewDate)
            .ThenByDescending(b => b.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = batches.Select(b => new BatchSummaryDto(
            b.Id,
            b.BatchCode,
            b.Name,
            b.RecipeId,
            b.BeerStyle,
            b.Status,
            b.CurrentStage,
            b.BrewDate,
            Math.Max(0, today.DayNumber - b.BrewDate.DayNumber),
            b.TargetOg,
            b.MeasuredOg,
            b.CurrentGravity,
            b.TargetFg,
            b.MeasuredFg,
            b.AlcoholByVolume,
            b.TargetColorSrm,
            null, // vessel temp computed from latest reading
            b.FermenterId,
            b.Fermenter?.Name,
            b.TargetBatchSizeLiters,
            b.MeasuredBatchSizeLiters,
            b.CreatedAt,
            b.UpdatedAt
        )).ToList();

        var meta = new PaginationMeta(page, limit, total, totalPages);
        return (items, meta);
    }

    public async Task<(BatchAccessResult Result, BatchDetailDto? Batch)> GetBatchByIdAsync(Guid id, string userId)
    {
        var batch = await _db.Batches
            .AsNoTracking()
            .Include(b => b.Recipe)
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .Include(b => b.Readings)
            .Include(b => b.Ingredients)
            .Include(b => b.MashSteps)
            .Include(b => b.FermentationSteps)
            .Include(b => b.StageHistory)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.Equipment)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.BatchMashStep)
            .Include(b => b.VolumeProfile)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null);
        }

        return (BatchAccessResult.Success, MapToDetailDto(batch));
    }

    public async Task<string> GetNextBatchCodeAsync(string userId, DateOnly? date = null)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var prefix = $"B-{targetDate:yyyy-MM-dd}-";

        var existingCodes = await _db.Batches
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.BatchCode.StartsWith(prefix))
            .Select(b => b.BatchCode)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var code in existingCodes)
        {
            var suffix = code[prefix.Length..];
            if (int.TryParse(suffix, out var seq) && seq > maxSeq)
            {
                maxSeq = seq;
            }
        }

        return $"{prefix}{(maxSeq + 1):D2}";
    }

    public async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> CreateBatchAsync(
        CreateBatchRequest request, string userId)
    {
        // 1. Quota check
        var activeCount = await _db.Batches
            .CountAsync(b => b.UserId == userId && b.Status != BatchStatus.Completed && b.Status != BatchStatus.Archived);

        if (activeCount >= MaxActiveBatchesPerUser)
        {
            return (BatchAccessResult.QuotaExceeded, null, $"Active batch limit reached ({MaxActiveBatchesPerUser}). Complete or archive older batches.");
        }

        // 2. Resolve Recipe if provided
        Recipe? recipe = null;
        if (request.RecipeId.HasValue)
        {
            recipe = await _db.Recipes
                .Include(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
                .Include(r => r.MashSteps)
                .Include(r => r.FermentationSteps)
                .FirstOrDefaultAsync(r => r.Id == request.RecipeId.Value && (r.UserId == userId || r.IsPublic));

            if (recipe == null)
            {
                return (BatchAccessResult.NotFound, null, "Recipe not found.");
            }
        }

        // 3. Resolve Equipment & Verify Ownership
        Equipment? boiler = null;
        if (request.BoilerId.HasValue)
        {
            boiler = await _db.Equipment
                .Include(e => e.BrewerySetup)
                .FirstOrDefaultAsync(e => e.Id == request.BoilerId.Value && e.UserId == userId);

            if (boiler == null)
            {
                return (BatchAccessResult.NotFound, null, "Boiler not found.");
            }

            if (boiler.Type == EquipmentType.Sensor || boiler.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor)
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors cannot be used as boilers.");
            }
        }

        Equipment? fermenter = null;
        if (request.FermenterId.HasValue)
        {
            fermenter = await _db.Equipment
                .Include(e => e.BrewerySetup)
                .FirstOrDefaultAsync(e => e.Id == request.FermenterId.Value && e.UserId == userId);

            if (fermenter == null)
            {
                return (BatchAccessResult.NotFound, null, "Fermenter not found.");
            }

            if (fermenter.Type == EquipmentType.Sensor || fermenter.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor)
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors cannot be used as fermenters.");
            }
        }

        Equipment? packagingVessel = null;
        if (request.PackagingVesselId.HasValue)
        {
            packagingVessel = await _db.Equipment
                .Include(e => e.BrewerySetup)
                .FirstOrDefaultAsync(e => e.Id == request.PackagingVesselId.Value && e.UserId == userId);

            if (packagingVessel == null)
            {
                return (BatchAccessResult.NotFound, null, "Packaging vessel not found.");
            }

            if (packagingVessel.Type == EquipmentType.Sensor || packagingVessel.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor)
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors cannot be used as packaging vessels.");
            }
        }

        var plannedVolume = request.TargetBatchSizeLiters ?? recipe?.BatchSizeLiters ?? 20.0m;
        var brewDate = request.BrewDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Resolve loss parameters & setup early: Request override -> Equipment override -> BrewerySetup default -> System constant
        var setup = boiler?.BrewerySetup ?? (request.BoilerId.HasValue ? null :
            await _db.BrewerySetups.FirstOrDefaultAsync(s => s.UserId == userId && s.IsDefault));

        var boilOffRate = request.BoilOffRatePerHour ?? boiler?.BoilOffRatePerHour ?? setup?.DefaultBoilOffRatePerHour ?? 3.0m;
        var kettleTrub = request.KettleTrubLossLiters ?? boiler?.TrubLossLiters ?? setup?.DefaultKettleTrubLossLiters ?? 1.5m;
        var mashDeadSpace = request.MashTunDeadSpaceLiters ?? boiler?.MashTunDeadSpaceLiters ?? setup?.DefaultMashTunDeadSpaceLiters ?? 0.0m;
        var fermenterLoss = request.FermenterLossLiters ?? fermenter?.TrubLossLiters ?? setup?.DefaultFermenterLossLiters ?? 1.5m;
        var grainAbsorption = setup?.DefaultGrainAbsorptionRate ?? 0.96m;
        var shrinkage = setup?.CoolingShrinkagePercent ?? 4.0m;
        var packagingLoss = request.PackagingLossLiters ?? packagingVessel?.PackagingLossLiters ?? setup?.DefaultPackagingLossLiters ?? 0.5m;
        var targetBasis = request.TargetVolumeBasis ?? TargetVolumeBasis.Fermenter;

        // Calculate required volume into fermenter.
        // BeerXML/BeerJSON recipes define ingredients for volume into fermenter.
        // When TargetBasis is Packaged, plannedVolume represents the packaged beer target,
        // so we work backwards to determine the required fermenter volume to scale ingredients
        // accurately and preserve recipe target gravity, bitterness, and ABV.
        var targetFermenterVolume = targetBasis == TargetVolumeBasis.Packaged
            ? plannedVolume + fermenterLoss + packagingLoss
            : plannedVolume;

        // 4. Concurrency-safe Batch Code Generation
        var batchCode = string.IsNullOrWhiteSpace(request.BatchCode)
            ? await GetNextBatchCodeAsync(userId, brewDate)
            : request.BatchCode.Trim();

        var codeExists = await _db.Batches.AnyAsync(b => b.UserId == userId && b.BatchCode == batchCode);
        if (codeExists)
        {
            batchCode = await GetNextBatchCodeAsync(userId, brewDate);
        }

        // 5. Build Batch Entity
        var batch = new Batch
        {
            UserId = userId,
            RecipeId = recipe?.Id,
            BatchCode = batchCode,
            Name = request.Name.Trim(),
            BeerStyle = request.BeerStyle?.Trim() ?? recipe?.BeerStyle ?? "American IPA",
            Status = BatchStatus.Brewing,
            CurrentStage = BrewStage.Mash,
            BrewDate = brewDate,
            TargetBatchSizeLiters = plannedVolume,
            BoilTimeMinutes = request.BoilTimeMinutes ?? recipe?.BoilTimeMinutes ?? 60,
            EfficiencyPercent = request.EfficiencyPercent ?? recipe?.EfficiencyPercent ?? 72.0m,
            TargetOg = request.TargetOg ?? recipe?.OriginalGravity ?? 1.050m,
            TargetFg = request.TargetFg ?? recipe?.FinalGravity ?? 1.010m,
            TargetAbv = request.TargetAbv ?? recipe?.AlcoholByVolume ?? 5.25m,
            TargetIbu = request.TargetIbu ?? recipe?.BitternessIbu ?? 35.0m,
            TargetColorSrm = request.TargetColorSrm ?? recipe?.ColorSrm ?? 6.0m,
            MeasuredOg = request.MeasuredOg,
            PitchTemperatureC = request.PitchTemperatureC,
            Notes = request.Notes?.Trim(),
            BoilerId = boiler?.Id,
            FermenterId = fermenter?.Id,
            PackagingVesselId = packagingVessel?.Id,
            BrewerySetupId = setup?.Id
        };

        // 6. Snapshot ingredients scaled against target fermenter volume and ordered chronologically
        if (recipe != null && recipe.Ingredients.Count > 0)
        {
            var scale = recipe.BatchSizeLiters > 0 ? targetFermenterVolume / recipe.BatchSizeLiters : 1.0m;
            foreach (var ri in OrderIngredientsChronologically(recipe.Ingredients))
            {
                batch.Ingredients.Add(new BatchIngredient
                {
                    BatchId = batch.Id,
                    SourceIngredientId = ri.IngredientId,
                    Name = ri.Ingredient?.Name ?? "Ingredient",
                    Type = ri.Ingredient?.Type ?? IngredientType.Other,
                    Amount = Math.Round(ri.Amount * scale, 3),
                    Unit = ri.Unit,
                    AdditionStage = ri.Usage,
                    AdditionTimeMinutes = ri.DurationMinutes,
                    Notes = ri.Notes,
                    Form = ri.Form ?? ri.Ingredient?.Form
                });
            }
        }
        else if (request.CustomIngredients != null)
        {
            var customIngredients = request.CustomIngredients.Select(ci => new BatchIngredient
            {
                BatchId = batch.Id,
                Name = ci.Name.Trim(),
                Type = ci.Type,
                Amount = ci.Amount,
                Unit = ci.Unit.Trim(),
                AdditionStage = ci.AdditionStage,
                AdditionTimeMinutes = ci.AdditionTimeMinutes,
                Notes = ci.Notes?.Trim(),
                Form = ci.Form
            });

            foreach (var ci in OrderIngredientsChronologically(customIngredients))
            {
                batch.Ingredients.Add(ci);
            }
        }

        // 6.2 Snapshot mash steps
        if (recipe != null && recipe.MashSteps.Count > 0)
        {
            var scale = recipe.BatchSizeLiters > 0 ? targetFermenterVolume / recipe.BatchSizeLiters : 1.0m;
            foreach (var rms in recipe.MashSteps.OrderBy(s => s.StepOrder))
            {
                batch.MashSteps.Add(new BatchMashStep
                {
                    BatchId = batch.Id,
                    RecipeMashStepId = rms.Id,
                    StepOrder = rms.StepOrder,
                    Name = rms.Name,
                    Type = rms.Type,
                    TargetTemperatureC = rms.TemperatureC,
                    DurationMinutes = rms.DurationMinutes,
                    RampTimeMinutes = rms.RampTimeMinutes,
                    InfuseAmountLiters = rms.InfuseAmountLiters.HasValue ? Math.Round(rms.InfuseAmountLiters.Value * scale, 2) : null,
                    IsCompleted = false
                });
            }
        }
        else if (request.CustomMashSteps != null && request.CustomMashSteps.Count > 0)
        {
            foreach (var cms in request.CustomMashSteps.OrderBy(s => s.StepOrder))
            {
                batch.MashSteps.Add(new BatchMashStep
                {
                    BatchId = batch.Id,
                    StepOrder = cms.StepOrder,
                    Name = cms.Name.Trim(),
                    Type = cms.Type,
                    TargetTemperatureC = cms.TargetTemperatureC,
                    DurationMinutes = cms.DurationMinutes,
                    RampTimeMinutes = cms.RampTimeMinutes,
                    InfuseAmountLiters = cms.InfuseAmountLiters,
                    Notes = cms.Notes?.Trim(),
                    IsCompleted = false
                });
            }
        }
        else
        {
            // Default single infusion rest so every batch has a valid mash step
            batch.MashSteps.Add(new BatchMashStep
            {
                BatchId = batch.Id,
                StepOrder = 1,
                Name = "Saccharification Rest",
                Type = MashStepType.Infusion,
                TargetTemperatureC = 65.0m,
                DurationMinutes = 60,
                IsCompleted = false
            });
        }

        // 6.3 Snapshot fermentation steps
        if (recipe != null && recipe.FermentationSteps.Count > 0)
        {
            foreach (var rfs in recipe.FermentationSteps.OrderBy(s => s.StepOrder))
            {
                batch.FermentationSteps.Add(new BatchFermentationStep
                {
                    BatchId = batch.Id,
                    RecipeFermentationStepId = rfs.Id,
                    StepOrder = rfs.StepOrder,
                    Name = rfs.Name,
                    Type = rfs.Type,
                    TargetTemperatureC = rfs.TargetTemperatureC,
                    DurationDays = rfs.DurationDays,
                    RampTimeHours = rfs.RampTimeHours,
                    TriggerGravity = rfs.TriggerGravity,
                    Notes = rfs.Notes,
                    IsCompleted = false
                });
            }
        }
        else if (request.CustomFermentationSteps != null && request.CustomFermentationSteps.Count > 0)
        {
            foreach (var cfs in request.CustomFermentationSteps.OrderBy(s => s.StepOrder))
            {
                batch.FermentationSteps.Add(new BatchFermentationStep
                {
                    BatchId = batch.Id,
                    StepOrder = cfs.StepOrder,
                    Name = cfs.Name.Trim(),
                    Type = cfs.Type,
                    TargetTemperatureC = cfs.TargetTemperatureC,
                    DurationDays = cfs.DurationDays,
                    RampTimeHours = cfs.RampTimeHours,
                    TriggerGravity = cfs.TriggerGravity,
                    Notes = cfs.Notes?.Trim(),
                    IsCompleted = false
                });
            }
        }
        else
        {
            // Default primary fermentation step so every batch has a valid fermentation profile
            batch.FermentationSteps.Add(new BatchFermentationStep
            {
                BatchId = batch.Id,
                StepOrder = 1,
                Name = "Primary Fermentation",
                Type = FermentationStepType.Primary,
                TargetTemperatureC = batch.PitchTemperatureC ?? 19.0m,
                DurationDays = 14,
                IsCompleted = false
            });
        }

        // 6.5 Calculate and attach Volume Profile
        var totalGrainKg = batch.Ingredients
            .Where(i => i.Type == IngredientType.Fermentable)
            .Sum(i => i.Unit.Equals("kg", StringComparison.OrdinalIgnoreCase) ? i.Amount : 0m);

        var volumeInput = new VolumeCalculationInput(
            TargetBatchSizeLiters: plannedVolume,
            BoilTimeMinutes: batch.BoilTimeMinutes,
            TotalGrainWeightKg: totalGrainKg,
            TargetBasis: targetBasis,
            SpargeEnabled: request.SpargeEnabled ?? true,
            BoilOffRatePerHour: boilOffRate,
            GrainAbsorptionRate: grainAbsorption,
            KettleTrubLossLiters: kettleTrub,
            FermenterLossLiters: fermenterLoss,
            MashTunDeadSpaceLiters: mashDeadSpace,
            CoolingShrinkagePercent: shrinkage,
            PackagingLossLiters: packagingLoss
        );

        var volumeResult = VolumeCalculator.CalculateWaterRequirements(volumeInput);

        batch.VolumeProfile = new BatchVolumeProfile
        {
            BatchId = batch.Id,
            TotalWaterLiters = volumeResult.TotalWaterLiters,
            StrikeWaterLiters = volumeResult.StrikeWaterLiters,
            SpargeWaterLiters = volumeResult.SpargeWaterLiters,
            TargetPreBoilVolumeLiters = volumeResult.TargetPreBoilVolumeLiters,
            TargetPostBoilVolumeLiters = volumeResult.TargetPostBoilVolumeLiters,
            TargetFermenterVolumeLiters = volumeResult.TargetFermenterVolumeLiters,
            TargetPackagedVolumeLiters = volumeResult.TargetPackagedVolumeLiters,
            BoilOffRatePerHour = boilOffRate,
            GrainAbsorptionRateLPerKg = grainAbsorption,
            KettleTrubLossLiters = kettleTrub,
            FermenterTrubLossLiters = fermenterLoss,
            MashTunDeadSpaceLiters = mashDeadSpace,
            CoolingShrinkagePercent = shrinkage,
            PackagingLossLiters = packagingLoss
        };

        // 7. Seed Initial Stages
        var stages = new[]
        {
            (BrewStage.Mash, StageStatus.InProgress, (DateTime?)DateTime.UtcNow),
            (BrewStage.Boil, StageStatus.Pending, (DateTime?)null),
            (BrewStage.Ferment, StageStatus.Pending, (DateTime?)null),
            (BrewStage.Condition, StageStatus.Pending, (DateTime?)null),
            (BrewStage.Package, StageStatus.Pending, (DateTime?)null)
        };

        foreach (var (stage, status, startedAt) in stages)
        {
            batch.StageHistory.Add(new BatchStageHistory
            {
                BatchId = batch.Id,
                Stage = stage,
                Status = status,
                StartedAt = startedAt
            });
        }

        // 7.5 Attach Auxiliary Sensor Assignments
        if (request.SensorAssignments != null)
        {
            foreach (var sa in request.SensorAssignments)
            {
                var sensorEq = await _db.Equipment.FirstOrDefaultAsync(e => e.Id == sa.EquipmentId && e.UserId == userId);
                if (sensorEq != null)
                {
                    batch.SensorAssignments.Add(new BatchSensorAssignment
                    {
                        BatchId = batch.Id,
                        EquipmentId = sensorEq.Id,
                        Stage = sa.Stage,
                        BatchMashStepId = sa.BatchMashStepId
                    });
                }
            }
        }

        // 8. Transactional Equipment Occupancy
        if (_db.Database.IsInMemory())
        {
            if (boiler != null)
            {
                boiler.CurrentVolumeLiters = plannedVolume;
                boiler.CurrentVolume = boiler.Unit == VolumeUnit.Gallons
                    ? Math.Round(plannedVolume / GallonsToLitersFactor, 2)
                    : plannedVolume;
                boiler.UpdatedAt = DateTime.UtcNow;
            }

            _db.Batches.Add(batch);
            await _db.SaveChangesAsync();
        }
        else
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                try
                {
                    if (boiler != null)
                    {
                        boiler.CurrentVolumeLiters = plannedVolume;
                        boiler.CurrentVolume = boiler.Unit == VolumeUnit.Gallons
                            ? Math.Round(plannedVolume / GallonsToLitersFactor, 2)
                            : plannedVolume;
                        boiler.UpdatedAt = DateTime.UtcNow;
                    }

                    _db.Batches.Add(batch);
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Failed to start batch for user {UserId}", userId);
                    throw;
                }
            });
        }

        return (BatchAccessResult.Success, MapToDetailDto(batch), null);
    }

    public async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> AdvanceStageAsync(
        Guid id, AdvanceBatchStageRequest request, string userId)
    {
        async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> ExecuteAdvanceAsync()
        {
            var batch = await _db.Batches
                .Include(b => b.Recipe)
                .Include(b => b.Boiler)
                .Include(b => b.Fermenter)
                .Include(b => b.PackagingVessel)
                .Include(b => b.Readings)
                .Include(b => b.Ingredients)
                .Include(b => b.MashSteps)
                .Include(b => b.FermentationSteps)
                .Include(b => b.StageHistory)
                .Include(b => b.SensorAssignments)
                    .ThenInclude(sa => sa.Equipment)
                .Include(b => b.VolumeProfile)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

            if (batch == null)
            {
                return (BatchAccessResult.NotFound, null, "Batch not found.");
            }

            if (batch.Status == BatchStatus.Completed || batch.Status == BatchStatus.Archived)
            {
                return (BatchAccessResult.Conflict, null, "Cannot advance stage of a completed or archived batch.");
            }

            var oldStage = batch.CurrentStage;
            var newStage = request.TargetStage;
            var now = DateTime.UtcNow;

            // Complete old stage in history
            var currentHistory = batch.StageHistory.FirstOrDefault(sh => sh.Stage == oldStage && sh.Status == StageStatus.InProgress);
            if (currentHistory != null)
            {
                currentHistory.Status = StageStatus.Completed;
                currentHistory.CompletedAt = now;
                if (currentHistory.StartedAt.HasValue)
                {
                    currentHistory.DurationMinutes = (int)Math.Max(0, (now - currentHistory.StartedAt.Value).TotalMinutes);
                }
            }

            // Start new stage in history
            var newHistory = batch.StageHistory.FirstOrDefault(sh => sh.Stage == newStage);
            if (newHistory != null)
            {
                newHistory.Status = StageStatus.InProgress;
                newHistory.StartedAt = now;
            }
            else
            {
                batch.StageHistory.Add(new BatchStageHistory
                {
                    BatchId = batch.Id,
                    Stage = newStage,
                    Status = StageStatus.InProgress,
                    StartedAt = now
                });
            }

            // Apply optional measurements
            if (request.MeasuredOg.HasValue) batch.MeasuredOg = request.MeasuredOg.Value;
            if (request.MeasuredBatchSizeLiters.HasValue) batch.MeasuredBatchSizeLiters = request.MeasuredBatchSizeLiters.Value;
            if (request.PitchTemperatureC.HasValue) batch.PitchTemperatureC = request.PitchTemperatureC.Value;
            if (request.MeasuredFg.HasValue)
            {
                batch.MeasuredFg = request.MeasuredFg.Value;
                batch.CurrentGravity = request.MeasuredFg.Value;
            }
            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                batch.Notes = string.IsNullOrEmpty(batch.Notes)
                    ? request.Notes.Trim()
                    : $"{batch.Notes}\n[{newStage} - {now:g}]: {request.Notes.Trim()}";
            }

            // Update Volume Profile with stage-specific measurements
            if (batch.VolumeProfile != null)
            {
                if (request.MeasuredPreBoilVolumeLiters.HasValue)
                    batch.VolumeProfile.MeasuredPreBoilVolumeLiters = request.MeasuredPreBoilVolumeLiters.Value;
                if (request.MeasuredPreBoilGravity.HasValue)
                    batch.VolumeProfile.MeasuredPreBoilGravity = request.MeasuredPreBoilGravity.Value;
                if (request.MeasuredPostBoilVolumeLiters.HasValue)
                    batch.VolumeProfile.MeasuredPostBoilVolumeLiters = request.MeasuredPostBoilVolumeLiters.Value;
                if (request.MeasuredBatchSizeLiters.HasValue)
                    batch.VolumeProfile.MeasuredFermenterVolumeLiters = request.MeasuredBatchSizeLiters.Value;
                if (request.MeasuredPackagedVolumeLiters.HasValue)
                    batch.VolumeProfile.MeasuredPackagedVolumeLiters = request.MeasuredPackagedVolumeLiters.Value;
                VolumeCalculator.RecalculatePostStepMilestones(batch.VolumeProfile, batch.BoilTimeMinutes);
                batch.VolumeProfile.UpdatedAt = DateTime.UtcNow;
            }

            // Handle Equipment Volumetric Transitions
            if (oldStage is BrewStage.Mash or BrewStage.Boil && newStage == BrewStage.Ferment)
            {
                // Free Boiler
                if (batch.Boiler != null)
                {
                    batch.Boiler.CurrentVolumeLiters = 0m;
                    batch.Boiler.CurrentVolume = 0m;
                    batch.Boiler.UpdatedAt = now;
                }

                // Fill Fermenter
                if (batch.Fermenter != null)
                {
                    var vol = batch.MeasuredBatchSizeLiters ?? batch.TargetBatchSizeLiters;
                    batch.Fermenter.CurrentVolumeLiters = vol;
                    batch.Fermenter.CurrentVolume = batch.Fermenter.Unit == VolumeUnit.Gallons
                        ? Math.Round(vol / GallonsToLitersFactor, 2)
                        : vol;
                    batch.Fermenter.UpdatedAt = now;
                }

                batch.Status = BatchStatus.Fermenting;

                // Brewhouse efficiency calculation
                if (batch.MeasuredOg.HasValue)
                {
                    var vol = batch.MeasuredBatchSizeLiters ?? batch.TargetBatchSizeLiters;
                    var totalPotentialPoints = CalculateTotalGrainPotentialPoints(batch.Ingredients);
                    batch.BrewhouseEfficiency = BrewingCalculator.CalculateBrewhouseEfficiency(vol, batch.MeasuredOg.Value, totalPotentialPoints);
                }
            }
            else if (newStage == BrewStage.Condition)
            {
                batch.Status = BatchStatus.Conditioning;
            }
            else if (newStage == BrewStage.Package)
            {
                // Free Fermenter
                if (batch.Fermenter != null)
                {
                    batch.Fermenter.CurrentVolumeLiters = 0m;
                    batch.Fermenter.CurrentVolume = 0m;
                    batch.Fermenter.UpdatedAt = now;
                }

                // Assign & Fill Packaging Vessel if selected
                if (request.PackagingVesselId.HasValue)
                {
                    var vessel = await _db.Equipment
                        .FirstOrDefaultAsync(e => e.Id == request.PackagingVesselId.Value && e.UserId == userId);

                    if (vessel != null)
                    {
                        if (vessel.Type == EquipmentType.Sensor || vessel.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor)
                        {
                            return (BatchAccessResult.ValidationFailed, null, "Sensors cannot be used as packaging vessels.");
                        }

                        batch.PackagingVesselId = vessel.Id;
                        batch.PackagingVessel = vessel;
                        var vol = request.MeasuredPackagedVolumeLiters
                            ?? batch.VolumeProfile?.TargetPackagedVolumeLiters
                            ?? batch.MeasuredBatchSizeLiters
                            ?? batch.TargetBatchSizeLiters;
                        vessel.CurrentVolumeLiters = vol;
                        vessel.CurrentVolume = vessel.Unit == VolumeUnit.Gallons
                            ? Math.Round(vol / GallonsToLitersFactor, 2)
                            : vol;
                        vessel.UpdatedAt = now;
                    }
                }

                batch.Status = BatchStatus.Completed;
                batch.CompletedAt = now;

                var effectiveOg = batch.MeasuredOg ?? (batch.TargetOg > 1.000m ? batch.TargetOg : (decimal?)null);
                if (effectiveOg.HasValue && batch.CurrentGravity.HasValue && effectiveOg.Value > batch.CurrentGravity.Value)
                {
                    batch.AlcoholByVolume = BrewingCalculator.CalculateActualAbv(effectiveOg.Value, batch.CurrentGravity.Value);
                }
            }

            // Auto-deduct ingredients for oldStage that haven't been deducted yet
            var stageIngredients = GetIngredientsForStage(batch.Ingredients, oldStage);
            if (newStage == BrewStage.Package)
            {
                stageIngredients = batch.Ingredients.Where(bi => !bi.IsDeducted).ToList();
            }

            foreach (var si in stageIngredients)
            {
                if (!si.IsDeducted)
                {
                    await DeductIngredientStockAsync(userId, si);
                    si.IsDeducted = true;
                    si.DeductedAt = now;
                    si.IsChecked = true;
                }
            }

            batch.CurrentStage = newStage;
            batch.UpdatedAt = now;

            await _db.SaveChangesAsync();
            return (BatchAccessResult.Success, MapToDetailDto(batch), null);
        }

        if (_db.Database.IsInMemory())
        {
            return await ExecuteAdvanceAsync();
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var result = await ExecuteAdvanceAsync();
                if (result.Result == BatchAccessResult.Success)
                {
                    await tx.CommitAsync();
                }
                else
                {
                    await tx.RollbackAsync();
                }
                return result;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Failed to advance stage for batch {BatchId}", id);
                throw;
            }
        });
    }

    public async Task<(BatchAccessResult Result, BatchReadingDto? Reading, string? ErrorMessage)> AddReadingAsync(
        Guid id, AddBatchReadingRequest request, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.Readings)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        if (batch.Readings.Count >= MaxReadingsPerBatch)
        {
            return (BatchAccessResult.QuotaExceeded, null, $"Max readings limit ({MaxReadingsPerBatch}) reached for this batch.");
        }

        var reading = new BatchReading
        {
            BatchId = batch.Id,
            Timestamp = request.Timestamp ?? DateTime.UtcNow,
            SpecificGravity = request.SpecificGravity,
            TemperatureC = request.TemperatureC,
            Notes = request.Notes?.Trim()
        };

        batch.Readings.Add(reading);
        batch.CurrentGravity = request.SpecificGravity;

        // Recalculate live ABV
        var effectiveOg = batch.MeasuredOg ?? (batch.TargetOg > 1.000m ? batch.TargetOg : (decimal?)null);
        if (effectiveOg.HasValue && effectiveOg.Value > request.SpecificGravity)
        {
            batch.AlcoholByVolume = BrewingCalculator.CalculateActualAbv(effectiveOg.Value, request.SpecificGravity);
        }

        // If completed or packaging, also update MeasuredFg
        if (batch.CurrentStage is BrewStage.Condition or BrewStage.Package)
        {
            batch.MeasuredFg = request.SpecificGravity;
        }

        batch.UpdatedAt = DateTime.UtcNow;

        _db.BatchReadings.Add(reading);
        await _db.SaveChangesAsync();

        var dto = new BatchReadingDto(
            reading.Id,
            reading.BatchId,
            reading.Timestamp,
            reading.SpecificGravity,
            reading.TemperatureC,
            reading.Notes,
            reading.CreatedAt,
            batch.AlcoholByVolume
        );

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, BatchIngredientDto? Ingredient, string? ErrorMessage)> ToggleIngredientAsync(
        Guid batchId, Guid ingredientId, bool isChecked, string userId)
    {
        var ingredient = await _db.BatchIngredients
            .Include(bi => bi.Batch)
            .FirstOrDefaultAsync(bi => bi.Id == ingredientId && bi.BatchId == batchId && bi.Batch!.UserId == userId);

        if (ingredient == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch ingredient not found.");
        }

        ingredient.IsChecked = isChecked;
        if (isChecked)
        {
            if (!ingredient.IsDeducted)
            {
                await DeductIngredientStockAsync(userId, ingredient);
                ingredient.IsDeducted = true;
                ingredient.DeductedAt = DateTime.UtcNow;
            }
        }
        else
        {
            if (ingredient.IsDeducted)
            {
                await RefundIngredientStockAsync(userId, ingredient);
                ingredient.IsDeducted = false;
                ingredient.DeductedAt = null;
            }
        }

        ingredient.Batch!.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = new BatchIngredientDto(
            ingredient.Id,
            ingredient.BatchId,
            ingredient.SourceIngredientId,
            ingredient.Name,
            ingredient.Type,
            ingredient.Amount,
            ingredient.Unit,
            ingredient.AdditionStage,
            ingredient.AdditionTimeMinutes,
            ingredient.IsChecked,
            ingredient.IsDeducted,
            ingredient.Notes,
            ingredient.Form
        );

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, BatchMashStepDto? Step, string? ErrorMessage)> ToggleMashStepAsync(
        Guid batchId, Guid stepId, ToggleBatchMashStepRequest request, string userId)
    {
        var step = await _db.BatchMashSteps
            .Include(s => s.Batch)
                .ThenInclude(b => b!.Ingredients)
            .FirstOrDefaultAsync(s => s.Id == stepId && s.BatchId == batchId && s.Batch!.UserId == userId);

        if (step == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch mash step not found.");
        }

        if (step.Batch!.Status == BatchStatus.Completed || step.Batch.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Cannot modify mash steps on completed or archived batches.");
        }

        step.IsCompleted = request.IsCompleted;
        step.CompletedAt = request.IsCompleted ? DateTime.UtcNow : null;
        if (request.ActualTemperatureC.HasValue)
            step.ActualTemperatureC = request.ActualTemperatureC.Value;
        if (request.ActualDurationMinutes.HasValue)
            step.ActualDurationMinutes = request.ActualDurationMinutes.Value;
        if (!string.IsNullOrWhiteSpace(request.Notes))
            step.Notes = request.Notes.Trim();

        if (request.IsCompleted && step.Batch.Ingredients.Count > 0)
        {
            var mashIngredients = step.Batch.Ingredients
                .Where(bi => !bi.IsDeducted && (bi.AdditionStage == IngredientUsage.Mash || bi.Type == IngredientType.Fermentable))
                .ToList();

            foreach (var mi in mashIngredients)
            {
                await DeductIngredientStockAsync(userId, mi);
                mi.IsDeducted = true;
                mi.DeductedAt = DateTime.UtcNow;
                mi.IsChecked = true;
            }
        }

        step.Batch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = new BatchMashStepDto(
            step.Id,
            step.BatchId,
            step.RecipeMashStepId,
            step.StepOrder,
            step.Name,
            step.Type,
            step.TargetTemperatureC,
            step.DurationMinutes,
            step.RampTimeMinutes,
            step.InfuseAmountLiters,
            step.ActualTemperatureC,
            step.ActualDurationMinutes,
            step.IsCompleted,
            step.CompletedAt,
            step.Notes
        );

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, BatchFermentationStepDto? Step, string? ErrorMessage)> ToggleFermentationStepAsync(
        Guid batchId, Guid stepId, ToggleBatchFermentationStepRequest request, string userId)
    {
        var step = await _db.BatchFermentationSteps
            .Include(s => s.Batch)
            .FirstOrDefaultAsync(s => s.Id == stepId && s.BatchId == batchId && s.Batch!.UserId == userId);

        if (step == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch fermentation step not found.");
        }

        if (step.Batch!.Status == BatchStatus.Completed || step.Batch.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Cannot modify fermentation steps on completed or archived batches.");
        }

        step.IsCompleted = request.IsCompleted;
        step.CompletedAt = request.IsCompleted ? DateTime.UtcNow : null;
        if (request.ActualTemperatureC.HasValue)
            step.ActualTemperatureC = request.ActualTemperatureC.Value;
        if (!string.IsNullOrWhiteSpace(request.Notes))
            step.Notes = request.Notes.Trim();

        step.Batch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = new BatchFermentationStepDto(
            step.Id,
            step.BatchId,
            step.RecipeFermentationStepId,
            step.StepOrder,
            step.Name,
            step.Type,
            step.TargetTemperatureC,
            step.ActualTemperatureC,
            step.DurationDays,
            step.RampTimeHours,
            step.TriggerGravity,
            step.IsCompleted,
            step.StartedAt,
            step.CompletedAt,
            step.Notes
        );

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> UpdateBatchAsync(
        Guid id, UpdateBatchRequest request, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.Recipe)
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .Include(b => b.Readings)
            .Include(b => b.Ingredients)
            .Include(b => b.MashSteps)
            .Include(b => b.FermentationSteps)
            .Include(b => b.StageHistory)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.Equipment)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.BatchMashStep)
            .Include(b => b.VolumeProfile)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        if (batch.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Archived batches cannot be modified.");
        }

        if (!string.IsNullOrWhiteSpace(request.Name)) batch.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.BeerStyle)) batch.BeerStyle = request.BeerStyle.Trim();
        if (request.Notes != null) batch.Notes = request.Notes.Trim();
        if (request.MeasuredOg.HasValue) batch.MeasuredOg = request.MeasuredOg.Value;
        if (request.MeasuredFg.HasValue)
        {
            batch.MeasuredFg = request.MeasuredFg.Value;
            batch.CurrentGravity = request.MeasuredFg.Value;
        }
        if (request.MeasuredBatchSizeLiters.HasValue) batch.MeasuredBatchSizeLiters = request.MeasuredBatchSizeLiters.Value;
        if (request.PitchTemperatureC.HasValue) batch.PitchTemperatureC = request.PitchTemperatureC.Value;

        if (request.PackagingVesselId.HasValue)
        {
            if (request.PackagingVesselId.Value == Guid.Empty)
            {
                batch.PackagingVesselId = null;
                batch.PackagingVessel = null;
            }
            else
            {
                var vessel = await _db.Equipment.FirstOrDefaultAsync(e => e.Id == request.PackagingVesselId.Value && e.UserId == userId);
                if (vessel != null)
                {
                    if (vessel.Type == EquipmentType.Sensor || vessel.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor)
                    {
                        return (BatchAccessResult.ValidationFailed, null, "Sensors cannot be used as packaging vessels.");
                    }

                    batch.PackagingVesselId = vessel.Id;
                    batch.PackagingVessel = vessel;
                }
            }
        }

        var effectiveOg = batch.MeasuredOg ?? (batch.TargetOg > 1.000m ? batch.TargetOg : (decimal?)null);
        if (effectiveOg.HasValue && batch.CurrentGravity.HasValue && effectiveOg.Value > batch.CurrentGravity.Value)
        {
            batch.AlcoholByVolume = BrewingCalculator.CalculateActualAbv(effectiveOg.Value, batch.CurrentGravity.Value);
        }

        if (batch.MeasuredOg.HasValue && (request.MeasuredOg.HasValue || request.MeasuredBatchSizeLiters.HasValue))
        {
            var vol = batch.MeasuredBatchSizeLiters ?? batch.TargetBatchSizeLiters;
            var totalPotentialPoints = CalculateTotalGrainPotentialPoints(batch.Ingredients);
            batch.BrewhouseEfficiency = BrewingCalculator.CalculateBrewhouseEfficiency(vol, batch.MeasuredOg.Value, totalPotentialPoints);
        }

        if (batch.VolumeProfile != null)
        {
            if (request.MeasuredPreBoilVolumeLiters.HasValue)
                batch.VolumeProfile.MeasuredPreBoilVolumeLiters = request.MeasuredPreBoilVolumeLiters.Value;
            if (request.MeasuredPreBoilGravity.HasValue)
                batch.VolumeProfile.MeasuredPreBoilGravity = request.MeasuredPreBoilGravity.Value;
            if (request.MeasuredPostBoilVolumeLiters.HasValue)
                batch.VolumeProfile.MeasuredPostBoilVolumeLiters = request.MeasuredPostBoilVolumeLiters.Value;
            if (request.MeasuredBatchSizeLiters.HasValue)
                batch.VolumeProfile.MeasuredFermenterVolumeLiters = request.MeasuredBatchSizeLiters.Value;
            if (request.MeasuredPackagedVolumeLiters.HasValue)
                batch.VolumeProfile.MeasuredPackagedVolumeLiters = request.MeasuredPackagedVolumeLiters.Value;
            VolumeCalculator.RecalculatePostStepMilestones(batch.VolumeProfile, batch.BoilTimeMinutes);
            batch.VolumeProfile.UpdatedAt = DateTime.UtcNow;
        }

        if (request.SensorAssignments != null)
        {
            _db.BatchSensorAssignments.RemoveRange(batch.SensorAssignments);
            batch.SensorAssignments.Clear();

            foreach (var sa in request.SensorAssignments)
            {
                var sensorEq = await _db.Equipment.FirstOrDefaultAsync(e => e.Id == sa.EquipmentId && e.UserId == userId);
                if (sensorEq != null)
                {
                    var assignment = new BatchSensorAssignment
                    {
                        BatchId = batch.Id,
                        EquipmentId = sensorEq.Id,
                        Equipment = sensorEq,
                        Stage = sa.Stage,
                        BatchMashStepId = sa.BatchMashStepId
                    };
                    _db.BatchSensorAssignments.Add(assignment);
                }
            }
        }

        batch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (BatchAccessResult.Success, MapToDetailDto(batch), null);
    }

    public async Task<BatchAccessResult> DeleteBatchAsync(Guid id, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (batch == null)
        {
            return BatchAccessResult.NotFound;
        }

        // Free associated equipment
        if (batch.Boiler != null)
        {
            batch.Boiler.CurrentVolumeLiters = 0m;
            batch.Boiler.CurrentVolume = 0m;
        }
        if (batch.Fermenter != null)
        {
            batch.Fermenter.CurrentVolumeLiters = 0m;
            batch.Fermenter.CurrentVolume = 0m;
        }
        if (batch.PackagingVessel != null)
        {
            batch.PackagingVessel.CurrentVolumeLiters = 0m;
            batch.PackagingVessel.CurrentVolume = 0m;
        }

        _db.Batches.Remove(batch);
        await _db.SaveChangesAsync();

        return BatchAccessResult.Success;
    }

    private static decimal CalculateTotalGrainPotentialPoints(IEnumerable<BatchIngredient> ingredients)
    {
        const double kgToLbs = 2.20462262;
        double totalPoints = 0.0;

        foreach (var ing in ingredients.Where(i => i.Type == IngredientType.Fermentable))
        {
            var weightLbs = (double)ing.Amount * kgToLbs;
            var ppg = 37.0; // default standard potential gravity
            totalPoints += weightLbs * ppg;
        }

        return (decimal)Math.Max(1.0, totalPoints);
    }

    private static BatchDetailDto MapToDetailDto(Batch b)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latestReading = b.Readings.OrderByDescending(r => r.Timestamp).FirstOrDefault();

        return new BatchDetailDto(
            Id: b.Id,
            UserId: b.UserId,
            RecipeId: b.RecipeId,
            RecipeName: b.Recipe?.Name,
            BatchCode: b.BatchCode,
            Name: b.Name,
            BeerStyle: b.BeerStyle,
            Status: b.Status,
            CurrentStage: b.CurrentStage,
            BrewDate: b.BrewDate,
            DaysActive: Math.Max(0, today.DayNumber - b.BrewDate.DayNumber),
            TargetOg: b.TargetOg,
            TargetFg: b.TargetFg,
            TargetAbv: b.TargetAbv,
            TargetIbu: b.TargetIbu,
            TargetColorSrm: b.TargetColorSrm,
            TargetBatchSizeLiters: b.TargetBatchSizeLiters,
            BoilTimeMinutes: b.BoilTimeMinutes,
            EfficiencyPercent: b.EfficiencyPercent,
            MeasuredOg: b.MeasuredOg,
            CurrentGravity: b.CurrentGravity ?? latestReading?.SpecificGravity,
            MeasuredFg: b.MeasuredFg,
            AlcoholByVolume: b.AlcoholByVolume,
            BrewhouseEfficiency: b.BrewhouseEfficiency,
            MeasuredBatchSizeLiters: b.MeasuredBatchSizeLiters,
            PitchTemperatureC: b.PitchTemperatureC,
            BoilerId: b.BoilerId,
            BoilerName: b.Boiler?.Name,
            FermenterId: b.FermenterId,
            FermenterName: b.Fermenter?.Name,
            PackagingVesselId: b.PackagingVesselId,
            PackagingVesselName: b.PackagingVessel?.Name,
            Notes: b.Notes,
            CreatedAt: b.CreatedAt,
            UpdatedAt: b.UpdatedAt,
            CompletedAt: b.CompletedAt,
            Readings: b.Readings.OrderBy(r => r.Timestamp).Select(r => new BatchReadingDto(
                r.Id,
                r.BatchId,
                r.Timestamp,
                r.SpecificGravity,
                r.TemperatureC,
                r.Notes,
                r.CreatedAt
            )).ToList(),
            Ingredients: OrderIngredientsChronologically(b.Ingredients).Select(i => new BatchIngredientDto(
                i.Id,
                i.BatchId,
                i.SourceIngredientId,
                i.Name,
                i.Type,
                i.Amount,
                i.Unit,
                i.AdditionStage,
                i.AdditionTimeMinutes,
                i.IsChecked,
                i.IsDeducted,
                i.Notes,
                i.Form
            )).ToList(),
            MashSteps: b.MashSteps.OrderBy(s => s.StepOrder).Select(s => new BatchMashStepDto(
                s.Id,
                s.BatchId,
                s.RecipeMashStepId,
                s.StepOrder,
                s.Name,
                s.Type,
                s.TargetTemperatureC,
                s.DurationMinutes,
                s.RampTimeMinutes,
                s.InfuseAmountLiters,
                s.ActualTemperatureC,
                s.ActualDurationMinutes,
                s.IsCompleted,
                s.CompletedAt,
                s.Notes
            )).ToList(),
            FermentationSteps: b.FermentationSteps.OrderBy(s => s.StepOrder).Select(s => new BatchFermentationStepDto(
                s.Id,
                s.BatchId,
                s.RecipeFermentationStepId,
                s.StepOrder,
                s.Name,
                s.Type,
                s.TargetTemperatureC,
                s.ActualTemperatureC,
                s.DurationDays,
                s.RampTimeHours,
                s.TriggerGravity,
                s.IsCompleted,
                s.StartedAt,
                s.CompletedAt,
                s.Notes
            )).ToList(),
            StageHistory: b.StageHistory.OrderBy(sh => sh.Stage).Select(sh => new BatchStageHistoryDto(
                sh.Id,
                sh.BatchId,
                sh.Stage,
                sh.Status,
                sh.StartedAt,
                sh.CompletedAt,
                sh.DurationMinutes,
                sh.Notes
            )).ToList(),
            SensorAssignments: b.SensorAssignments.Select(sa => new BatchSensorAssignmentDto(
                sa.Id,
                sa.EquipmentId,
                sa.Equipment?.Name,
                sa.Equipment?.Subtype,
                sa.Stage,
                sa.BatchMashStepId,
                sa.BatchMashStep?.Name
            )).ToList(),
            VolumeProfile: b.VolumeProfile != null ? new BatchVolumeProfileDto(
                b.VolumeProfile.TotalWaterLiters,
                b.VolumeProfile.StrikeWaterLiters,
                b.VolumeProfile.SpargeWaterLiters,
                b.VolumeProfile.TargetPreBoilVolumeLiters,
                b.VolumeProfile.MeasuredPreBoilVolumeLiters,
                b.VolumeProfile.MeasuredPreBoilGravity,
                b.VolumeProfile.TargetPostBoilVolumeLiters,
                b.VolumeProfile.MeasuredPostBoilVolumeLiters,
                b.VolumeProfile.TargetFermenterVolumeLiters,
                b.VolumeProfile.MeasuredFermenterVolumeLiters,
                b.VolumeProfile.TargetPackagedVolumeLiters,
                b.VolumeProfile.MeasuredPackagedVolumeLiters,
                b.VolumeProfile.BoilOffRatePerHour,
                b.VolumeProfile.GrainAbsorptionRateLPerKg,
                b.VolumeProfile.KettleTrubLossLiters,
                b.VolumeProfile.FermenterTrubLossLiters,
                b.VolumeProfile.MashTunDeadSpaceLiters,
                b.VolumeProfile.CoolingShrinkagePercent,
                b.VolumeProfile.PackagingLossLiters
            ) : null
        );
    }

    public async Task<(BatchAccessResult Result, BatchEquipmentReadingDto? Reading, string? ErrorMessage)> LogTemperatureReadingAsync(
        Guid batchId, LogBatchTemperatureRequest request, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .Include(b => b.MashSteps)
            .Include(b => b.StageHistory)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        var stage = request.Stage ?? batch.CurrentStage;
        var stepName = request.StepName;
        Guid? stepId = request.BatchMashStepId;
        decimal? targetTempC = null;

        // Prevent logging to completed stages
        var stageOrder = new[] { BrewStage.Mash, BrewStage.Boil, BrewStage.Ferment, BrewStage.Condition, BrewStage.Package };
        var currentIndex = Array.IndexOf(stageOrder, batch.CurrentStage);
        var requestedIndex = Array.IndexOf(stageOrder, stage);

        if (batch.Status is BatchStatus.Completed or BatchStatus.Archived ||
            requestedIndex < currentIndex ||
            batch.StageHistory.Any(sh => sh.Stage == stage && sh.Status == StageStatus.Completed))
        {
            return (BatchAccessResult.Conflict, null, "Cannot log readings for a completed stage.");
        }

        // If stage is Mash, resolve mash step
        if (stage == BrewStage.Mash)
        {
            BatchMashStep? mashStep = null;
            if (stepId.HasValue)
            {
                mashStep = batch.MashSteps.FirstOrDefault(s => s.Id == stepId.Value);
                if (mashStep == null)
                {
                    return (BatchAccessResult.NotFound, null, "Specified mash step not found.");
                }
                if (mashStep.IsCompleted)
                {
                    return (BatchAccessResult.Conflict, null, "Cannot log readings for a completed mash step.");
                }
            }
            else
            {
                mashStep = batch.MashSteps.OrderBy(s => s.StepOrder).FirstOrDefault(s => !s.IsCompleted);
            }

            if (mashStep != null)
            {
                stepId = mashStep.Id;
                stepName ??= mashStep.Name;
                targetTempC = mashStep.TargetTemperatureC;
                mashStep.ActualTemperatureC = request.TemperatureC;
            }
            else
            {
                stepId = null;
                stepName ??= "Mash";
            }
        }
        else if (stage == BrewStage.Boil)
        {
            stepName ??= "Boil";
            targetTempC = 100.0m;
        }
        else if (stage == BrewStage.Ferment)
        {
            stepName ??= "Ferment";
            targetTempC = batch.PitchTemperatureC ?? 20.0m;
        }
        else if (stage == BrewStage.Condition)
        {
            stepName ??= "Condition";
            targetTempC = 2.0m;
        }
        else if (stage == BrewStage.Package)
        {
            stepName ??= "Package";
        }

        // Determine equipment
        Guid equipmentId;
        string equipmentName;

        if (request.EquipmentId.HasValue)
        {
            var eq = await _db.Equipment.FirstOrDefaultAsync(e => e.Id == request.EquipmentId.Value && e.UserId == userId);
            if (eq == null)
            {
                return (BatchAccessResult.Conflict, null, "Specified equipment not found or not owned by user.");
            }
            equipmentId = eq.Id;
            equipmentName = eq.Name;
            eq.CurrentTemperatureC = request.TemperatureC;
            eq.TemperatureUpdatedAt = request.Timestamp ?? DateTime.UtcNow;
        }
        else
        {
            // Default equipment based on stage
            Equipment? defaultEq = stage switch
            {
                BrewStage.Mash or BrewStage.Boil => batch.Boiler,
                BrewStage.Ferment or BrewStage.Condition => batch.Fermenter,
                BrewStage.Package => batch.PackagingVessel,
                _ => null
            };

            if (defaultEq != null)
            {
                equipmentId = defaultEq.Id;
                equipmentName = defaultEq.Name;
                defaultEq.CurrentTemperatureC = request.TemperatureC;
                defaultEq.TemperatureUpdatedAt = request.Timestamp ?? DateTime.UtcNow;
            }
            else
            {
                var firstEq = await _db.Equipment.FirstOrDefaultAsync(e => e.UserId == userId);
                if (firstEq != null)
                {
                    equipmentId = firstEq.Id;
                    equipmentName = firstEq.Name;
                    firstEq.CurrentTemperatureC = request.TemperatureC;
                    firstEq.TemperatureUpdatedAt = request.Timestamp ?? DateTime.UtcNow;
                }
                else
                {
                    return (BatchAccessResult.Conflict, null, "No equipment found. Please assign equipment to your batch or setup first.");
                }
            }
        }

        var timestamp = request.Timestamp ?? DateTime.UtcNow;
        var reading = new EquipmentReading
        {
            EquipmentId = equipmentId,
            BatchId = batch.Id,
            Stage = stage,
            BatchMashStepId = stepId,
            StepName = stepName,
            TemperatureC = request.TemperatureC,
            Timestamp = timestamp,
            Source = "Manual",
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.EquipmentReadings.Add(reading);
        await _db.SaveChangesAsync();

        var dto = new BatchEquipmentReadingDto(
            reading.Id,
            equipmentId,
            equipmentName,
            batch.Id,
            stage,
            stepId,
            stepName,
            reading.TemperatureC,
            reading.Timestamp,
            reading.Source,
            targetTempC,
            reading.Notes
        );

        _broadcastService.BroadcastReading(dto);

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, List<BatchEquipmentReadingDto>? Readings, string? ErrorMessage)> GetBatchEquipmentReadingsAsync(
        Guid batchId, string userId, BrewStage? stage = null, Guid? stepId = null)
    {
        var batch = await _db.Batches.AsNoTracking()
            .Include(b => b.MashSteps)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        var query = _db.EquipmentReadings.AsNoTracking()
            .Include(r => r.Equipment)
            .Include(r => r.BatchMashStep)
            .Where(r => r.BatchId == batchId);

        if (stage.HasValue)
        {
            query = query.Where(r => r.Stage == stage.Value);
        }

        if (stepId.HasValue)
        {
            query = query.Where(r => r.BatchMashStepId == stepId.Value);
        }

        var readings = await query
            .OrderBy(r => r.Timestamp)
            .Select(r => new BatchEquipmentReadingDto(
                r.Id,
                r.EquipmentId,
                r.Equipment != null ? r.Equipment.Name : "Equipment",
                r.BatchId!.Value,
                r.Stage,
                r.BatchMashStepId,
                r.StepName,
                r.TemperatureC,
                r.Timestamp,
                r.Source,
                r.BatchMashStep != null
                    ? r.BatchMashStep.TargetTemperatureC
                    : (r.Stage == BrewStage.Boil ? 100.0m : (r.Stage == BrewStage.Ferment ? (batch.PitchTemperatureC ?? 20.0m) : (r.Stage == BrewStage.Condition ? 2.0m : null))),
                r.Notes,
                r.SpecificGravity
            ))
            .ToListAsync();

        return (BatchAccessResult.Success, readings, null);
    }

    public async Task<(BatchAccessResult Result, List<BatchSensorAssignmentDto>? Sensors, string? ErrorMessage)> UpdateBatchSensorsAsync(
        Guid batchId, List<BatchSensorAssignmentInput> sensorInputs, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.Equipment)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.BatchMashStep)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        if (batch.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Archived batches cannot be modified.");
        }

        _db.BatchSensorAssignments.RemoveRange(batch.SensorAssignments);
        batch.SensorAssignments.Clear();

        foreach (var sa in sensorInputs)
        {
            var sensorEq = await _db.Equipment.FirstOrDefaultAsync(e => e.Id == sa.EquipmentId && e.UserId == userId);
            if (sensorEq != null)
            {
                var assignment = new BatchSensorAssignment
                {
                    BatchId = batch.Id,
                    EquipmentId = sensorEq.Id,
                    Equipment = sensorEq,
                    Stage = sa.Stage,
                    BatchMashStepId = sa.BatchMashStepId
                };

                _db.BatchSensorAssignments.Add(assignment);
            }
        }

        batch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dtoList = batch.SensorAssignments.Select(sa => new BatchSensorAssignmentDto(
            sa.Id,
            sa.EquipmentId,
            sa.Equipment?.Name,
            sa.Equipment?.Subtype,
            sa.Stage,
            sa.BatchMashStepId,
            sa.BatchMashStep?.Name
        )).ToList();

        return (BatchAccessResult.Success, dtoList, null);
    }

    public async Task<(BatchAccessResult Result, BatchStockCheckResult? CheckResult, string? ErrorMessage)> CheckRecipeStockAsync(
        Guid recipeId,
        decimal targetBatchSizeLiters,
        string userId,
        TargetVolumeBasis? targetBasis = null,
        decimal? fermenterLossLiters = null,
        decimal? packagingLossLiters = null)
    {
        var recipe = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
            .FirstOrDefaultAsync(r => r.Id == recipeId && (r.UserId == userId || r.IsPublic));

        if (recipe == null)
        {
            return (BatchAccessResult.NotFound, null, "Recipe not found.");
        }

        var effectiveFermenterVolume = targetBasis == TargetVolumeBasis.Packaged
            ? targetBatchSizeLiters + (fermenterLossLiters ?? 1.5m) + (packagingLossLiters ?? 0.5m)
            : targetBatchSizeLiters;

        var scale = recipe.BatchSizeLiters > 0 ? effectiveFermenterVolume / recipe.BatchSizeLiters : 1.0m;
        var shortages = new List<IngredientShortageDto>();

        var userStocks = await _db.IngredientStocks
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync();

        var groupedIngredients = recipe.Ingredients
            .GroupBy(ri => ri.IngredientId);

        foreach (var group in groupedIngredients)
        {
            var first = group.First();
            var baseUnit = first.Unit;
            decimal totalRequiredAmount = 0m;
            foreach (var ri in group)
            {
                var scaled = Math.Round(ri.Amount * scale, 3);
                totalRequiredAmount += ConvertUnitAmount(scaled, ri.Unit, baseUnit);
            }
            totalRequiredAmount = Math.Round(totalRequiredAmount, 3);

            var stock = userStocks.FirstOrDefault(s => s.IngredientId == group.Key);

            decimal availableStockInRecipeUnit = 0m;
            if (stock != null)
            {
                availableStockInRecipeUnit = ConvertUnitAmount(stock.Amount, stock.Unit, baseUnit);
            }

            if (availableStockInRecipeUnit < totalRequiredAmount)
            {
                var deficit = Math.Round(totalRequiredAmount - availableStockInRecipeUnit, 3);
                shortages.Add(new IngredientShortageDto(
                    group.Key,
                    first.Ingredient?.Name ?? "Unknown Ingredient",
                    first.Ingredient?.Type ?? IngredientType.Other,
                    totalRequiredAmount,
                    Math.Round(availableStockInRecipeUnit, 3),
                    baseUnit,
                    deficit
                ));
            }
        }

        var result = new BatchStockCheckResult(shortages.Count > 0, shortages);
        return (BatchAccessResult.Success, result, null);
    }

    private async Task DeductIngredientStockAsync(string userId, BatchIngredient item)
    {
        if (item.Amount <= 0) return;

        var stock = item.SourceIngredientId.HasValue
            ? (_db.IngredientStocks.Local.FirstOrDefault(s => s.UserId == userId && s.IngredientId == item.SourceIngredientId.Value)
               ?? await _db.IngredientStocks.FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == item.SourceIngredientId.Value))
            : null;

        if (stock == null)
        {
            var ingredient = item.SourceIngredientId.HasValue
                ? await _db.Ingredients.FindAsync(item.SourceIngredientId.Value)
                : await _db.Ingredients.FirstOrDefaultAsync(i => i.Name.ToLower() == item.Name.ToLower());

            if (ingredient != null)
            {
                stock = _db.IngredientStocks.Local.FirstOrDefault(s => s.UserId == userId && s.IngredientId == ingredient.Id)
                    ?? await _db.IngredientStocks.FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == ingredient.Id);
                if (stock == null)
                {
                    stock = new IngredientStock
                    {
                        UserId = userId,
                        IngredientId = ingredient.Id,
                        Amount = 0.0m,
                        Unit = item.Unit,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.IngredientStocks.Add(stock);
                }
            }
        }

        if (stock != null)
        {
            var amountToDeduct = ConvertUnitAmount(item.Amount, item.Unit, stock.Unit);
            stock.Amount = Math.Max(0.0m, stock.Amount - amountToDeduct);
            stock.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task RefundIngredientStockAsync(string userId, BatchIngredient item)
    {
        if (item.Amount <= 0) return;

        var stock = item.SourceIngredientId.HasValue
            ? (_db.IngredientStocks.Local.FirstOrDefault(s => s.UserId == userId && s.IngredientId == item.SourceIngredientId.Value)
               ?? await _db.IngredientStocks.FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == item.SourceIngredientId.Value))
            : null;

        if (stock != null)
        {
            var amountToRestore = ConvertUnitAmount(item.Amount, item.Unit, stock.Unit);
            stock.Amount += amountToRestore;
            stock.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static decimal ConvertUnitAmount(decimal amount, string sourceUnit, string targetUnit)
    {
        if (string.Equals(sourceUnit, targetUnit, StringComparison.OrdinalIgnoreCase))
            return amount;

        var s = sourceUnit.Trim().ToLowerInvariant();
        var t = targetUnit.Trim().ToLowerInvariant();

        if (s == "kg" && t == "g") return amount * 1000m;
        if (s == "g" && t == "kg") return amount / 1000m;
        if (s == "lbs" && t == "kg") return amount * 0.45359237m;
        if (s == "kg" && t == "lbs") return amount / 0.45359237m;
        if (s == "oz" && t == "g") return amount * 28.3495231m;
        if (s == "g" && t == "oz") return amount / 28.3495231m;

        return amount;
    }

    public static int GetStageOrderPriority(IngredientUsage usage) => usage switch
    {
        IngredientUsage.Mash => 1,
        IngredientUsage.Boil => 2,
        IngredientUsage.Whirlpool => 3,
        IngredientUsage.Primary => 4,
        IngredientUsage.Secondary => 5,
        IngredientUsage.DryHop => 6,
        IngredientUsage.Bottling => 7,
        _ => 8
    };

    public static IEnumerable<BatchIngredient> OrderIngredientsChronologically(IEnumerable<BatchIngredient> ingredients)
    {
        return ingredients
            .OrderBy(i => GetStageOrderPriority(i.AdditionStage))
            // For Boil & Whirlpool: time is minutes remaining until flameout -> highest time is pitched first
            .ThenByDescending(i => (i.AdditionStage == IngredientUsage.Boil || i.AdditionStage == IngredientUsage.Whirlpool) ? (i.AdditionTimeMinutes ?? -1) : 0)
            // For Mash: grains first, largest amount first
            .ThenBy(i => i.AdditionStage == IngredientUsage.Mash && i.Type == IngredientType.Fermentable ? 0 : 1)
            .ThenByDescending(i => i.AdditionStage == IngredientUsage.Mash ? i.Amount : 0)
            // For Primary: yeast first
            .ThenBy(i => i.AdditionStage == IngredientUsage.Primary && i.Type == IngredientType.Yeast ? 0 : 1)
            // DryHop / Secondary: days/minutes ascending
            .ThenBy(i => (i.AdditionStage == IngredientUsage.DryHop || i.AdditionStage == IngredientUsage.Secondary) ? (i.AdditionTimeMinutes ?? 0) : 0)
            .ThenByDescending(i => i.Amount)
            .ThenBy(i => i.Name);
    }

    public static IEnumerable<RecipeIngredient> OrderIngredientsChronologically(IEnumerable<RecipeIngredient> ingredients)
    {
        return ingredients
            .OrderBy(i => GetStageOrderPriority(i.Usage))
            // For Boil & Whirlpool: time is minutes remaining until flameout -> highest time is pitched first
            .ThenByDescending(i => (i.Usage == IngredientUsage.Boil || i.Usage == IngredientUsage.Whirlpool) ? (i.DurationMinutes ?? -1) : 0)
            // For Mash: grains first, largest amount first
            .ThenBy(i => i.Usage == IngredientUsage.Mash && i.Ingredient?.Type == IngredientType.Fermentable ? 0 : 1)
            .ThenByDescending(i => i.Usage == IngredientUsage.Mash ? i.Amount : 0)
            // For Primary: yeast first
            .ThenBy(i => i.Usage == IngredientUsage.Primary && i.Ingredient?.Type == IngredientType.Yeast ? 0 : 1)
            // DryHop / Secondary: days/minutes ascending
            .ThenBy(i => (i.Usage == IngredientUsage.DryHop || i.Usage == IngredientUsage.Secondary) ? (i.DurationMinutes ?? 0) : 0)
            .ThenByDescending(i => i.Amount)
            .ThenBy(i => i.Ingredient?.Name ?? string.Empty);
    }

    private static List<BatchIngredient> GetIngredientsForStage(IEnumerable<BatchIngredient> ingredients, BrewStage stage)
    {
        var filtered = stage switch
        {
            BrewStage.Mash => ingredients.Where(i => i.AdditionStage == IngredientUsage.Mash ||
                                                    (i.Type == IngredientType.Fermentable &&
                                                     i.AdditionStage != IngredientUsage.Boil &&
                                                     i.AdditionStage != IngredientUsage.Bottling)),
            BrewStage.Boil => ingredients.Where(i => i.AdditionStage == IngredientUsage.Boil ||
                                                    i.AdditionStage == IngredientUsage.Whirlpool ||
                                                    (i.Type == IngredientType.Hop &&
                                                     i.AdditionStage != IngredientUsage.DryHop &&
                                                     i.AdditionStage != IngredientUsage.Mash)),
            BrewStage.Ferment => ingredients.Where(i => i.AdditionStage == IngredientUsage.Primary ||
                                                       i.AdditionStage == IngredientUsage.DryHop ||
                                                       i.Type == IngredientType.Yeast),
            BrewStage.Condition => ingredients.Where(i => i.AdditionStage == IngredientUsage.Secondary),
            BrewStage.Package => ingredients.Where(i => i.AdditionStage == IngredientUsage.Bottling),
            _ => ingredients
        };

        return OrderIngredientsChronologically(filtered).ToList();
    }
}