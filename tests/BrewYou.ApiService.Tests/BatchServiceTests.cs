using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrewYou.ApiService.Tests;

public class BatchServiceTests
{
    private readonly BrewYouDbContext _db;
    private readonly BatchService _batchService;

    public BatchServiceTests()
    {
        var options = new DbContextOptionsBuilder<BrewYouDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BrewYouDbContext(options);
        var broadcast = new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance);
        _batchService = new BatchService(_db, broadcast, NullLogger<BatchService>.Instance);
    }

    [Fact]
    public async Task ToggleIngredientAsync_CheckAndUncheck_DeductsAndRefundsStock()
    {
        // Arrange
        var userId = "brewer_user_1";
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-001",
            Name = "Hazy IPA",
            BeerStyle = "NEIPA",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        _db.Batches.Add(batch);

        var ingredient = new Ingredient
        {
            Id = Guid.NewGuid(),
            Name = "Citra Hops",
            Type = IngredientType.Hop
        };
        _db.Ingredients.Add(ingredient);

        var stock = new IngredientStock
        {
            UserId = userId,
            IngredientId = ingredient.Id,
            Amount = 500m,
            Unit = "g",
            UpdatedAt = DateTime.UtcNow
        };
        _db.IngredientStocks.Add(stock);

        var batchIng = new BatchIngredient
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Batch = batch,
            SourceIngredientId = ingredient.Id,
            Name = "Citra",
            Type = IngredientType.Hop,
            Amount = 100m,
            Unit = "g",
            AdditionStage = IngredientUsage.DryHop,
            IsChecked = false,
            IsDeducted = false
        };
        _db.BatchIngredients.Add(batchIng);
        await _db.SaveChangesAsync();

        // Act 1: Toggle Checked -> should deduct stock
        var (res1, dto1, err1) = await _batchService.ToggleIngredientAsync(batch.Id, batchIng.Id, true, userId);

        // Assert 1
        res1.Should().Be(BatchAccessResult.Success);
        dto1.Should().NotBeNull();
        dto1!.IsChecked.Should().BeTrue();
        dto1.IsDeducted.Should().BeTrue();
        err1.Should().BeNull();

        var updatedStock = await _db.IngredientStocks.FirstAsync(s => s.IngredientId == ingredient.Id);
        updatedStock.Amount.Should().Be(400m);

        // Act 2: Toggle Unchecked -> should refund stock
        var (res2, dto2, err2) = await _batchService.ToggleIngredientAsync(batch.Id, batchIng.Id, false, userId);

        // Assert 2
        res2.Should().Be(BatchAccessResult.Success);
        dto2.Should().NotBeNull();
        dto2!.IsChecked.Should().BeFalse();
        dto2.IsDeducted.Should().BeFalse();
        err2.Should().BeNull();

        updatedStock = await _db.IngredientStocks.FirstAsync(s => s.IngredientId == ingredient.Id);
        updatedStock.Amount.Should().Be(500m);
    }

    [Theory]
    [InlineData("kg", "g", 1.0, 1000.0)]
    [InlineData("g", "kg", 1000.0, 1.0)]
    [InlineData("lbs", "kg", 10.0, 4.5359237)]
    [InlineData("kg", "lbs", 4.5359237, 10.0)]
    [InlineData("oz", "g", 2.0, 56.6990462)]
    [InlineData("g", "oz", 56.6990462, 2.0)]
    [InlineData("custom", "custom", 5.0, 5.0)]
    [InlineData("unsupported_unit", "other_unit", 12.0, 12.0)]
    public async Task ToggleIngredientAsync_UnitConversions_DeductsAndRefundsAccurately(
        string ingredientUnit, string stockUnit, decimal ingredientAmount, decimal expectedDeduction)
    {
        // Arrange
        var userId = $"brewer_{Guid.NewGuid():N}";
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-002",
            Name = "Unit Test Ale",
            Status = BatchStatus.Fermenting,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        _db.Batches.Add(batch);

        var ingredient = new Ingredient
        {
            Id = Guid.NewGuid(),
            Name = $"Malt_{Guid.NewGuid():N}",
            Type = IngredientType.Fermentable
        };
        _db.Ingredients.Add(ingredient);

        var initialStockAmount = 10000m;
        var stock = new IngredientStock
        {
            UserId = userId,
            IngredientId = ingredient.Id,
            Amount = initialStockAmount,
            Unit = stockUnit,
            UpdatedAt = DateTime.UtcNow
        };
        _db.IngredientStocks.Add(stock);

        var batchIng = new BatchIngredient
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Batch = batch,
            SourceIngredientId = ingredient.Id,
            Name = ingredient.Name,
            Type = IngredientType.Fermentable,
            Amount = ingredientAmount,
            Unit = ingredientUnit,
            AdditionStage = IngredientUsage.Mash,
            IsChecked = false,
            IsDeducted = false
        };
        _db.BatchIngredients.Add(batchIng);
        await _db.SaveChangesAsync();

        // Act & Assert Deduct
        var (resDeduct, dtoDeduct, _) = await _batchService.ToggleIngredientAsync(batch.Id, batchIng.Id, true, userId);
        resDeduct.Should().Be(BatchAccessResult.Success);
        dtoDeduct!.IsDeducted.Should().BeTrue();

        var stockAfterDeduct = await _db.IngredientStocks.FirstAsync(s => s.IngredientId == ingredient.Id && s.UserId == userId);
        stockAfterDeduct.Amount.Should().BeApproximately(initialStockAmount - expectedDeduction, 0.01m);

        // Act & Assert Refund
        var (resRefund, dtoRefund, _) = await _batchService.ToggleIngredientAsync(batch.Id, batchIng.Id, false, userId);
        resRefund.Should().Be(BatchAccessResult.Success);
        dtoRefund!.IsDeducted.Should().BeFalse();

        var stockAfterRefund = await _db.IngredientStocks.FirstAsync(s => s.IngredientId == ingredient.Id && s.UserId == userId);
        stockAfterRefund.Amount.Should().BeApproximately(initialStockAmount, 0.01m);
    }

    [Fact]
    public async Task ToggleIngredientAsync_WhenIngredientNotFoundOrZeroAmount_HandlesGracefully()
    {
        var userId = "brewer_user_edge";
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-003",
            Name = "Edge Ale",
            Status = BatchStatus.Fermenting,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        _db.Batches.Add(batch);

        var zeroIng = new BatchIngredient
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Batch = batch,
            Name = "Water Salts",
            Type = IngredientType.Other,
            Amount = 0m,
            Unit = "g",
            AdditionStage = IngredientUsage.Mash,
            IsChecked = false,
            IsDeducted = false
        };
        _db.BatchIngredients.Add(zeroIng);
        await _db.SaveChangesAsync();

        // 1. Zero amount toggle
        var (resZero, dtoZero, errZero) = await _batchService.ToggleIngredientAsync(batch.Id, zeroIng.Id, true, userId);
        resZero.Should().Be(BatchAccessResult.Success);
        dtoZero!.IsChecked.Should().BeTrue();

        // 2. Not found ingredient ID
        var (resNotFound, dtoNotFound, errNotFound) = await _batchService.ToggleIngredientAsync(batch.Id, Guid.NewGuid(), true, userId);
        resNotFound.Should().Be(BatchAccessResult.NotFound);
        dtoNotFound.Should().BeNull();
        errNotFound.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ToggleMashStepAsync_CompletesAndDeductsMashIngredients_AndHandlesConflict()
    {
        // Arrange
        var userId = "brewer_mash_user";
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-004",
            Name = "Mash Test Batch",
            Status = BatchStatus.Brewing,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        _db.Batches.Add(batch);

        var malt = new Ingredient { Id = Guid.NewGuid(), Name = "Pilsner Malt", Type = IngredientType.Fermentable };
        _db.Ingredients.Add(malt);
        _db.IngredientStocks.Add(new IngredientStock { UserId = userId, IngredientId = malt.Id, Amount = 10m, Unit = "kg" });

        var mashIng = new BatchIngredient
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Batch = batch,
            SourceIngredientId = malt.Id,
            Name = "Pilsner Malt",
            Type = IngredientType.Fermentable,
            Amount = 5m,
            Unit = "kg",
            AdditionStage = IngredientUsage.Mash,
            IsChecked = false,
            IsDeducted = false
        };
        _db.BatchIngredients.Add(mashIng);

        var mashStep = new BatchMashStep
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Batch = batch,
            StepOrder = 1,
            Name = "Saccharification",
            Type = MashStepType.Temperature,
            TargetTemperatureC = 65m,
            DurationMinutes = 60,
            IsCompleted = false
        };
        _db.BatchMashSteps.Add(mashStep);
        await _db.SaveChangesAsync();

        // Act 1: Complete step
        var toggleReq = new ToggleBatchMashStepRequest(true, 65.5m, 62, "Smooth conversion");
        var (res1, dto1, _) = await _batchService.ToggleMashStepAsync(batch.Id, mashStep.Id, toggleReq, userId);

        // Assert 1
        res1.Should().Be(BatchAccessResult.Success);
        dto1!.IsCompleted.Should().BeTrue();
        dto1.ActualTemperatureC.Should().Be(65.5m);
        dto1.ActualDurationMinutes.Should().Be(62);
        dto1.Notes.Should().Be("Smooth conversion");

        var updatedIng = await _db.BatchIngredients.FirstAsync(bi => bi.Id == mashIng.Id);
        updatedIng.IsDeducted.Should().BeTrue();
        updatedIng.IsChecked.Should().BeTrue();

        // Act 2: Modify completed/archived batch returns Conflict
        batch.Status = BatchStatus.Completed;
        await _db.SaveChangesAsync();

        var (resConflict, dtoConflict, errConflict) = await _batchService.ToggleMashStepAsync(batch.Id, mashStep.Id, toggleReq, userId);
        resConflict.Should().Be(BatchAccessResult.Conflict);
        dtoConflict.Should().BeNull();
        errConflict.Should().Contain("completed or archived");
    }

    [Fact]
    public async Task ToggleFermentationStepAsync_CompletesAndUncompletes_WithConflictCheck()
    {
        // Arrange
        var userId = "brewer_ferm_user";
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-005",
            Name = "Ferm Test Batch",
            Status = BatchStatus.Fermenting,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        _db.Batches.Add(batch);

        var fermStep = new BatchFermentationStep
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Batch = batch,
            StepOrder = 1,
            Name = "Primary Fermentation",
            Type = FermentationStepType.Primary,
            TargetTemperatureC = 19m,
            DurationDays = 14,
            IsCompleted = false
        };
        _db.BatchFermentationSteps.Add(fermStep);
        await _db.SaveChangesAsync();

        // Act 1: Complete step
        var req = new ToggleBatchFermentationStepRequest(true, 19.2m, "Active krausen");
        var (res1, dto1, _) = await _batchService.ToggleFermentationStepAsync(batch.Id, fermStep.Id, req, userId);

        res1.Should().Be(BatchAccessResult.Success);
        dto1!.IsCompleted.Should().BeTrue();
        dto1.CompletedAt.Should().NotBeNull();
        dto1.ActualTemperatureC.Should().Be(19.2m);

        // Act 2: Uncomplete step
        var uncompleteReq = new ToggleBatchFermentationStepRequest(false, null, null);
        var (res2, dto2, _) = await _batchService.ToggleFermentationStepAsync(batch.Id, fermStep.Id, uncompleteReq, userId);
        res2.Should().Be(BatchAccessResult.Success);
        dto2!.IsCompleted.Should().BeFalse();
        dto2.CompletedAt.Should().BeNull();

        // Act 3: Archived batch returns Conflict
        batch.Status = BatchStatus.Archived;
        await _db.SaveChangesAsync();
        var (resArchived, _, errArchived) = await _batchService.ToggleFermentationStepAsync(batch.Id, fermStep.Id, req, userId);
        resArchived.Should().Be(BatchAccessResult.Conflict);
        errArchived.Should().Contain("completed or archived");
    }

    [Fact]
    public async Task CheckRecipeStockAsync_ComputesShortagesAndScalingCorrectly()
    {
        // Arrange
        var userId = "brewer_stock_checker";
        var malt = new Ingredient { Id = Guid.NewGuid(), Name = "Pale Ale Malt", Type = IngredientType.Fermentable };
        var hops = new Ingredient { Id = Guid.NewGuid(), Name = "Centennial", Type = IngredientType.Hop };
        _db.Ingredients.AddRange(malt, hops);

        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Centennial Blonde",
            BatchSizeLiters = 20m,
            Ingredients =
            [
                new RecipeIngredient { IngredientId = malt.Id, Ingredient = malt, Amount = 4m, Unit = "kg", Usage = IngredientUsage.Mash },
                new RecipeIngredient { IngredientId = hops.Id, Ingredient = hops, Amount = 50m, Unit = "g", Usage = IngredientUsage.Boil }
            ]
        };
        _db.Recipes.Add(recipe);

        // Stock has sufficient malt (10kg) but zero hops
        _db.IngredientStocks.Add(new IngredientStock { UserId = userId, IngredientId = malt.Id, Amount = 10m, Unit = "kg" });
        await _db.SaveChangesAsync();

        // Act: Check stock for a 40L scaled batch (double recipe size)
        var (res, result, err) = await _batchService.CheckRecipeStockAsync(
            recipe.Id, 40m, userId, TargetVolumeBasis.Packaged, fermenterLossLiters: 1.0m, packagingLossLiters: 1.0m);

        // Assert
        res.Should().Be(BatchAccessResult.Success);
        result.Should().NotBeNull();
        result!.HasShortage.Should().BeTrue();
        result.Shortages.Should().ContainSingle(s => s.IngredientId == hops.Id);

        var hopShortage = result.Shortages.First(s => s.IngredientId == hops.Id);
        hopShortage.StockAmount.Should().Be(0m);
        hopShortage.Deficit.Should().BeGreaterThan(50m); // Scaled up due to 40L + 2L losses
    }

    [Fact]
    public async Task GetBatchesAsync_WithStatusFilterAndPagination_ReturnsAccurateMetadata()
    {
        // Arrange
        var userId = "brewer_pagination_user";
        for (int i = 1; i <= 5; i++)
        {
            _db.Batches.Add(new Batch
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BatchCode = $"PAGE-{i:D3}",
                Name = $"Batch #{i}",
                BeerStyle = "Stout",
                Status = i <= 2 ? BatchStatus.Fermenting : BatchStatus.Completed,
                BrewDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i))
            });
        }
        await _db.SaveChangesAsync();

        // Act 1: Filter by Fermenting status
        var (items1, meta1) = await _batchService.GetBatchesAsync(userId, BatchStatus.Fermenting, null, page: 1, limit: 10);
        items1.Should().HaveCount(2);
        meta1.Total.Should().Be(2);
        meta1.TotalPages.Should().Be(1);

        // Act 2: Pagination clamping (page -1 -> 1, limit 200 -> 100)
        var (items2, meta2) = await _batchService.GetBatchesAsync(userId, null, null, page: -5, limit: 200);
        items2.Should().HaveCount(5);
        meta2.Page.Should().Be(1);
        meta2.Limit.Should().Be(100);
    }

    [Fact]
    public void OrderIngredientsChronologically_OrdersByStageAndChronology()
    {
        var ingredients = new List<BatchIngredient>
        {
            new() { Name = "DryHop Citra", AdditionStage = IngredientUsage.DryHop, Amount = 50m, Unit = "g" },
            new() { Name = "Bittering Magnum", AdditionStage = IngredientUsage.Boil, AdditionTimeMinutes = 60, Amount = 20m, Unit = "g" },
            new() { Name = "Base Pale Malt", AdditionStage = IngredientUsage.Mash, Type = IngredientType.Fermentable, Amount = 5m, Unit = "kg" },
            new() { Name = "Specialty Crystal", AdditionStage = IngredientUsage.Mash, Type = IngredientType.Fermentable, Amount = 0.5m, Unit = "kg" },
            new() { Name = "Flameout Mosaic", AdditionStage = IngredientUsage.Boil, AdditionTimeMinutes = 0, Amount = 30m, Unit = "g" },
            new() { Name = "English Ale Yeast", AdditionStage = IngredientUsage.Primary, Type = IngredientType.Yeast, Amount = 1m, Unit = "pkg" },
            new() { Name = "Priming Sugar", AdditionStage = IngredientUsage.Bottling, Amount = 100m, Unit = "g" }
        };

        var ordered = BatchService.OrderIngredientsChronologically(ingredients).ToList();

        ordered[0].Name.Should().Be("Base Pale Malt"); // largest grain first
        ordered[1].Name.Should().Be("Specialty Crystal");
        ordered[2].Name.Should().Be("Bittering Magnum"); // 60 min boil before 0 min
        ordered[3].Name.Should().Be("Flameout Mosaic");
        ordered[4].Name.Should().Be("English Ale Yeast");
        ordered[5].Name.Should().Be("DryHop Citra");
        ordered[6].Name.Should().Be("Priming Sugar");

        // Verify GetStageOrderPriority values
        BatchService.GetStageOrderPriority(IngredientUsage.Mash).Should().Be(1);
        BatchService.GetStageOrderPriority(IngredientUsage.Boil).Should().Be(2);
        BatchService.GetStageOrderPriority(IngredientUsage.Whirlpool).Should().Be(3);
        BatchService.GetStageOrderPriority(IngredientUsage.Primary).Should().Be(4);
        BatchService.GetStageOrderPriority(IngredientUsage.Secondary).Should().Be(5);
        BatchService.GetStageOrderPriority(IngredientUsage.DryHop).Should().Be(6);
        BatchService.GetStageOrderPriority(IngredientUsage.Bottling).Should().Be(7);
        BatchService.GetStageOrderPriority((IngredientUsage)999).Should().Be(8);
    }

    [Fact]
    public async Task DeleteBatchAsync_ResetsEquipmentVolumes_AndDeletesBatch()
    {
        // Arrange
        var userId = $"brewer_del_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Del Setup" };
        _db.BrewerySetups.Add(setup);

        var boiler = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Boiler", Type = EquipmentType.Boiler, CurrentVolumeLiters = 25m };
        var fermenter = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Fermenter", Type = EquipmentType.Fermenter, CurrentVolumeLiters = 20m };
        var keg = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Keg", Type = EquipmentType.Keg, CurrentVolumeLiters = 19m };
        _db.Equipment.AddRange(boiler, fermenter, keg);

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "DEL-001",
            Name = "Batch To Delete",
            Status = BatchStatus.Fermenting,
            BoilerId = boiler.Id,
            Boiler = boiler,
            FermenterId = fermenter.Id,
            Fermenter = fermenter,
            PackagingVesselId = keg.Id,
            PackagingVessel = keg
        };
        _db.Batches.Add(batch);
        await _db.SaveChangesAsync();

        // Act
        var result = await _batchService.DeleteBatchAsync(batch.Id, userId);

        // Assert
        result.Should().Be(BatchAccessResult.Success);
        boiler.CurrentVolumeLiters.Should().Be(0m);
        fermenter.CurrentVolumeLiters.Should().Be(0m);
        keg.CurrentVolumeLiters.Should().Be(0m);

        var deleted = await _db.Batches.FindAsync(batch.Id);
        deleted.Should().BeNull();

        // Deleting non-existent batch returns NotFound
        var notFoundResult = await _batchService.DeleteBatchAsync(Guid.NewGuid(), userId);
        notFoundResult.Should().Be(BatchAccessResult.NotFound);
    }

    [Fact]
    public async Task UpdateBatchAsync_UpdatesProperties_CalculatesEfficiency_AndSyncsSensors()
    {
        // Arrange
        var userId = $"brewer_update_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Update Setup" };
        _db.BrewerySetups.Add(setup);

        var sensor = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Chamber Probe",
            Type = EquipmentType.Sensor,
            ConnectionType = EquipmentConnectionType.HttpPush
        };
        _db.Equipment.Add(sensor);

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "UPD-001",
            Name = "Initial Name",
            BeerStyle = "Pale Ale",
            Status = BatchStatus.Brewing,
            TargetBatchSizeLiters = 20m,
            BoilTimeMinutes = 60,
            Ingredients =
            [
                new BatchIngredient
                {
                    Name = "Pale Malt",
                    Type = IngredientType.Fermentable,
                    Amount = 5m,
                    Unit = "kg",
                    AdditionStage = IngredientUsage.Mash
                }
            ],
            VolumeProfile = new BatchVolumeProfile
            {
                BatchId = Guid.NewGuid(),
                TargetPreBoilVolumeLiters = 25m,
                TargetPostBoilVolumeLiters = 22m,
                TargetFermenterVolumeLiters = 20m,
                TargetPackagedVolumeLiters = 19m
            }
        };
        _db.Batches.Add(batch);
        await _db.SaveChangesAsync();

        // Act
        var updateReq = new UpdateBatchRequest(
            Name: "Updated Name",
            BeerStyle: "IPA",
            Notes: "Smooth boil",
            MeasuredOg: 1.054m,
            MeasuredFg: null,
            MeasuredBatchSizeLiters: 21m,
            PitchTemperatureC: 18.5m,
            BoilerId: null,
            FermenterId: null,
            PackagingVesselId: null,
            MeasuredPreBoilVolumeLiters: 26m,
            MeasuredPreBoilGravity: 1.045m,
            MeasuredPostBoilVolumeLiters: 22.5m,
            MeasuredPackagedVolumeLiters: null,
            SensorAssignments:
            [
                new BatchSensorAssignmentInput(sensor.Id, BrewStage.Ferment, null)
            ]
        );

        var (res, dto, err) = await _batchService.UpdateBatchAsync(batch.Id, updateReq, userId);

        // Assert
        res.Should().Be(BatchAccessResult.Success);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Updated Name");
        dto.BeerStyle.Should().Be("IPA");
        dto.BrewhouseEfficiency.Should().NotBeNull();
        dto.BrewhouseEfficiency.Should().BeGreaterThan(60m);
        dto.SensorAssignments.Should().ContainSingle(sa => sa.EquipmentId == sensor.Id);

        // Updating non-existent batch returns NotFound
        var (notFoundRes, _, _) = await _batchService.UpdateBatchAsync(Guid.NewGuid(), updateReq, userId);
        notFoundRes.Should().Be(BatchAccessResult.NotFound);
    }

    [Fact]
    public async Task GetBatchByIdAsync_ReturnsDetailedDto_WithAllCollectionsMapped()
    {
        // Arrange
        var userId = $"brewer_get_{Guid.NewGuid():N}";
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "GET-001",
            Name = "Full Detail Batch",
            BeerStyle = "Saison",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
            Ingredients =
            [
                new BatchIngredient { Name = "Pilsner", Type = IngredientType.Fermentable, Amount = 4m, Unit = "kg", AdditionStage = IngredientUsage.Mash }
            ],
            MashSteps =
            [
                new BatchMashStep { Name = "Sacc", Type = MashStepType.Temperature, StepOrder = 1, TargetTemperatureC = 64m, DurationMinutes = 60 }
            ],
            FermentationSteps =
            [
                new BatchFermentationStep { Name = "Primary", Type = FermentationStepType.Primary, StepOrder = 1, TargetTemperatureC = 20m, DurationDays = 14 }
            ],
            Readings =
            [
                new BatchReading { Timestamp = DateTime.UtcNow, SpecificGravity = 1.010m, TemperatureC = 20.5m, Notes = "Almost dry" }
            ],
            VolumeProfile = new BatchVolumeProfile
            {
                BatchId = Guid.NewGuid(),
                TotalWaterLiters = 30m,
                TargetPreBoilVolumeLiters = 26m
            }
        };
        _db.Batches.Add(batch);
        await _db.SaveChangesAsync();

        // Act
        var (res, dto) = await _batchService.GetBatchByIdAsync(batch.Id, userId);

        // Assert
        res.Should().Be(BatchAccessResult.Success);
        dto.Should().NotBeNull();
        dto!.BatchCode.Should().Be("GET-001");
        dto.Ingredients.Should().HaveCount(1);
        dto.MashSteps.Should().HaveCount(1);
        dto.FermentationSteps.Should().HaveCount(1);
        dto.Readings.Should().HaveCount(1);
        dto.VolumeProfile.Should().NotBeNull();
        dto.DaysActive.Should().BeGreaterThanOrEqualTo(2);

        // Not found check
        var (notFoundRes, notFoundDto) = await _batchService.GetBatchByIdAsync(Guid.NewGuid(), userId);
        notFoundRes.Should().Be(BatchAccessResult.NotFound);
        notFoundDto.Should().BeNull();
    }

    [Fact]
    public async Task LogTemperatureReadingAsync_LogsManualReading_Successfully()
    {
        // Arrange
        var userId = $"brewer_log_temp_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Temp Setup" };
        _db.BrewerySetups.Add(setup);

        var boiler = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Test Boiler", Type = EquipmentType.Boiler };
        _db.Equipment.Add(boiler);

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "TEMP-001",
            Name = "Temperature Batch",
            Status = BatchStatus.Brewing,
            CurrentStage = BrewStage.Mash,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            BoilerId = boiler.Id,
            Boiler = boiler
        };
        _db.Batches.Add(batch);
        await _db.SaveChangesAsync();

        // Act
        var req = new LogBatchTemperatureRequest(
            Stage: BrewStage.Mash,
            TemperatureC: 66.2m,
            StepName: "Alpha Amylase Rest",
            BatchMashStepId: null,
            Notes: "Checked with calibrated probe"
        );

        var (res, reading, err) = await _batchService.LogTemperatureReadingAsync(batch.Id, req, userId);

        // Assert
        res.Should().Be(BatchAccessResult.Success);
        reading.Should().NotBeNull();
        reading!.TemperatureC.Should().Be(66.2m);
        reading.Notes.Should().Be("Checked with calibrated probe");

        // Non-existent batch returns NotFound
        var (notFoundRes, _, notFoundErr) = await _batchService.LogTemperatureReadingAsync(Guid.NewGuid(), req, userId);
        notFoundRes.Should().Be(BatchAccessResult.NotFound);
        notFoundErr.Should().Contain("Batch not found");
    }

    [Fact]
    public async Task AddReadingAsync_CalculatesLiveAbv_UsingMeasuredOg()
    {
        // Arrange
        var userId = "brewer_user_1";
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-001",
            Name = "IPA",
            BeerStyle = "IPA",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            MeasuredOg = 1.054m,
            TargetOg = 1.050m
        };
        _db.Batches.Add(batch);
        await _db.SaveChangesAsync();

        var req = new AddBatchReadingRequest(1.012m, 20.0m, "Day 4 gravity check");

        // Act
        var (res, reading, err) = await _batchService.AddReadingAsync(batch.Id, req, userId);

        // Assert
        res.Should().Be(BatchAccessResult.Success);
        reading.Should().NotBeNull();
        reading!.SpecificGravity.Should().Be(1.012m);
        reading.AlcoholByVolume.Should().Be(5.51m);

        var updated = await _db.Batches.FindAsync(batch.Id);
        updated!.CurrentGravity.Should().Be(1.012m);
        updated.AlcoholByVolume.Should().Be(5.51m);
    }

    [Fact]
    public async Task AddReadingAsync_CalculatesLiveAbv_FallbackToTargetOg_WhenMeasuredOgIsNull()
    {
        // Arrange
        var userId = "brewer_user_2";
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-002",
            Name = "Pale Ale",
            BeerStyle = "Pale Ale",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            MeasuredOg = null,
            TargetOg = 1.050m
        };
        _db.Batches.Add(batch);
        await _db.SaveChangesAsync();

        var req = new AddBatchReadingRequest(1.010m, 19.5m, "Day 5 check without measuredOg");

        // Act
        var (res, reading, err) = await _batchService.AddReadingAsync(batch.Id, req, userId);

        // Assert
        res.Should().Be(BatchAccessResult.Success);
        reading.Should().NotBeNull();
        reading!.SpecificGravity.Should().Be(1.010m);
        reading.AlcoholByVolume.Should().Be(5.25m);

        var updated = await _db.Batches.FindAsync(batch.Id);
        updated!.CurrentGravity.Should().Be(1.010m);
        updated.AlcoholByVolume.Should().Be(5.25m);
    }
}