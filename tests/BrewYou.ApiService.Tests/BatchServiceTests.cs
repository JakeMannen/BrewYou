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

    [Fact]
    public async Task AdvanceStageAsync_RejectsBackwardTransition_MarksSkippedIntermediateStages_AndCompletesPackageCleanly()
    {
        var userId = $"brewer_stage_{Guid.NewGuid():N}";
        var dryHop = new Ingredient { Id = Guid.NewGuid(), Name = "Mosaic", Type = IngredientType.Hop };
        _db.Ingredients.Add(dryHop);
        _db.IngredientStocks.Add(new IngredientStock { UserId = userId, IngredientId = dryHop.Id, Amount = 200m, Unit = "g" });
        await _db.SaveChangesAsync();

        var (_, created, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-STAGE-01",
            Name: "Stage Machine Ale",
            BeerStyle: "Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.2m,
            TargetIbu: 35m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: null,
            FermenterId: null,
            CustomIngredients:
            [
                new BatchIngredientInputDto("Mosaic", IngredientType.Hop, 50m, "g", IngredientUsage.DryHop, 3, null)
            ]
        ), userId);

        // Advance Mash -> Boil
        var (boilRes, boilBatch, _) = await _batchService.AdvanceStageAsync(
            created!.Id, new AdvanceBatchStageRequest(BrewStage.Boil), userId);
        boilRes.Should().Be(BatchAccessResult.Success);
        boilBatch!.CurrentStage.Should().Be(BrewStage.Boil);

        // Backward Boil -> Mash must return Conflict
        var (backRes, _, backErr) = await _batchService.AdvanceStageAsync(
            created.Id, new AdvanceBatchStageRequest(BrewStage.Mash), userId);
        backRes.Should().Be(BatchAccessResult.Conflict);
        backErr.Should().Contain("backward");

        // Same-stage Boil -> Boil must return Conflict
        var (sameRes, _, _) = await _batchService.AdvanceStageAsync(
            created.Id, new AdvanceBatchStageRequest(BrewStage.Boil), userId);
        sameRes.Should().Be(BatchAccessResult.Conflict);

        // Skip Ferment & Condition: Boil -> Package
        var (pkgRes, pkgBatch, _) = await _batchService.AdvanceStageAsync(
            created.Id, new AdvanceBatchStageRequest(BrewStage.Package, MeasuredFg: 1.010m), userId);
        pkgRes.Should().Be(BatchAccessResult.Success);
        pkgBatch!.Status.Should().Be(BatchStatus.Completed);
        pkgBatch.CurrentStage.Should().Be(BrewStage.Package);

        pkgBatch.StageHistory.First(h => h.Stage == BrewStage.Mash).Status.Should().Be(StageStatus.Completed);
        pkgBatch.StageHistory.First(h => h.Stage == BrewStage.Boil).Status.Should().Be(StageStatus.Completed);
        pkgBatch.StageHistory.First(h => h.Stage == BrewStage.Ferment).Status.Should().Be(StageStatus.Skipped);
        pkgBatch.StageHistory.First(h => h.Stage == BrewStage.Condition).Status.Should().Be(StageStatus.Skipped);

        var packageHistory = pkgBatch.StageHistory.First(h => h.Stage == BrewStage.Package);
        packageHistory.Status.Should().Be(StageStatus.Completed);
        packageHistory.CompletedAt.Should().NotBeNull();

        // Skipped Ferment DryHop ingredient was auto-deducted
        var stock = await _db.IngredientStocks.FirstAsync(s => s.UserId == userId && s.IngredientId == dryHop.Id);
        stock.Amount.Should().Be(150m);
    }

    [Fact]
    public async Task ToggleIngredientAsync_RefundsAdHocNamedStock_AndRejectsCompletedOrArchivedBatch()
    {
        var userId = $"brewer_refund_{Guid.NewGuid():N}";
        var citra = new Ingredient { Id = Guid.NewGuid(), Name = "Citra", Type = IngredientType.Hop };
        _db.Ingredients.Add(citra);
        _db.IngredientStocks.Add(new IngredientStock { UserId = userId, IngredientId = citra.Id, Amount = 100m, Unit = "g" });

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-REFUND-01",
            Name = "Ad-hoc Citra IPA",
            Status = BatchStatus.Brewing,
            CurrentStage = BrewStage.Boil,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        var adHocHop = new BatchIngredient
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Batch = batch,
            SourceIngredientId = null, // Ad-hoc ingredient matched by name
            Name = "citra",
            Type = IngredientType.Hop,
            Amount = 40m,
            Unit = "g",
            AdditionStage = IngredientUsage.Boil
        };
        _db.Batches.Add(batch);
        _db.BatchIngredients.Add(adHocHop);
        await _db.SaveChangesAsync();

        // Check -> deducts 40g
        var (checkRes, checkedDto, _) = await _batchService.ToggleIngredientAsync(batch.Id, adHocHop.Id, true, userId);
        checkRes.Should().Be(BatchAccessResult.Success);
        checkedDto!.IsDeducted.Should().BeTrue();
        (await _db.IngredientStocks.FirstAsync(s => s.UserId == userId && s.IngredientId == citra.Id)).Amount.Should().Be(60m);

        // Uncheck -> refunds 40g via case-insensitive name fallback
        var (uncheckRes, uncheckedDto, _) = await _batchService.ToggleIngredientAsync(batch.Id, adHocHop.Id, false, userId);
        uncheckRes.Should().Be(BatchAccessResult.Success);
        uncheckedDto!.IsDeducted.Should().BeFalse();
        (await _db.IngredientStocks.FirstAsync(s => s.UserId == userId && s.IngredientId == citra.Id)).Amount.Should().Be(100m);

        // Completed batch -> Conflict
        batch.Status = BatchStatus.Completed;
        await _db.SaveChangesAsync();
        var (conflictRes, _, conflictErr) = await _batchService.ToggleIngredientAsync(batch.Id, adHocHop.Id, true, userId);
        conflictRes.Should().Be(BatchAccessResult.Conflict);
        conflictErr.Should().Contain("completed or archived");
    }

    [Fact]
    public async Task ToggleMashStepAsync_AutoDeductsOnlyMashAdditions_LeavingBoilAndBottlingFermentablesUndeducted()
    {
        var userId = $"brewer_mash_only_{Guid.NewGuid():N}";
        var pilsner = new Ingredient { Id = Guid.NewGuid(), Name = "Pilsner", Type = IngredientType.Fermentable };
        var candiSugar = new Ingredient { Id = Guid.NewGuid(), Name = "Candi Sugar", Type = IngredientType.Fermentable };
        var priming = new Ingredient { Id = Guid.NewGuid(), Name = "Dextrose", Type = IngredientType.Fermentable };
        _db.Ingredients.AddRange(pilsner, candiSugar, priming);
        _db.IngredientStocks.AddRange(
            new IngredientStock { UserId = userId, IngredientId = pilsner.Id, Amount = 10m, Unit = "kg" },
            new IngredientStock { UserId = userId, IngredientId = candiSugar.Id, Amount = 2m, Unit = "kg" },
            new IngredientStock { UserId = userId, IngredientId = priming.Id, Amount = 1000m, Unit = "g" }
        );

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-TRIPEL-01",
            Name = "Belgian Tripel",
            Status = BatchStatus.Brewing,
            CurrentStage = BrewStage.Mash,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        var mashIng = new BatchIngredient { Id = Guid.NewGuid(), BatchId = batch.Id, Batch = batch, SourceIngredientId = pilsner.Id, Name = "Pilsner", Type = IngredientType.Fermentable, Amount = 6m, Unit = "kg", AdditionStage = IngredientUsage.Mash };
        var boilIng = new BatchIngredient { Id = Guid.NewGuid(), BatchId = batch.Id, Batch = batch, SourceIngredientId = candiSugar.Id, Name = "Candi Sugar", Type = IngredientType.Fermentable, Amount = 1m, Unit = "kg", AdditionStage = IngredientUsage.Boil };
        var bottleIng = new BatchIngredient { Id = Guid.NewGuid(), BatchId = batch.Id, Batch = batch, SourceIngredientId = priming.Id, Name = "Dextrose", Type = IngredientType.Fermentable, Amount = 150m, Unit = "g", AdditionStage = IngredientUsage.Bottling };
        var mashStep = new BatchMashStep { Id = Guid.NewGuid(), BatchId = batch.Id, Batch = batch, StepOrder = 1, Name = "Beta Rest", Type = MashStepType.Temperature, TargetTemperatureC = 63m, DurationMinutes = 45 };

        _db.Batches.Add(batch);
        _db.BatchIngredients.AddRange(mashIng, boilIng, bottleIng);
        _db.BatchMashSteps.Add(mashStep);
        await _db.SaveChangesAsync();

        var (res, _, _) = await _batchService.ToggleMashStepAsync(batch.Id, mashStep.Id, new ToggleBatchMashStepRequest(true), userId);
        res.Should().Be(BatchAccessResult.Success);

        (await _db.BatchIngredients.FindAsync(mashIng.Id))!.IsDeducted.Should().BeTrue();
        (await _db.BatchIngredients.FindAsync(boilIng.Id))!.IsDeducted.Should().BeFalse();
        (await _db.BatchIngredients.FindAsync(bottleIng.Id))!.IsDeducted.Should().BeFalse();
    }

    [Fact]
    public async Task ConvertUnitAmount_AndGrainCalculations_HandleImperialAndGramUnitsAndCatalogPotentialGravity()
    {
        BatchService.ConvertUnitAmount(10m, "lb", "kg").Should().BeApproximately(4.5359m, 0.001m);
        BatchService.ConvertUnitAmount(1m, "lb", "oz").Should().BeApproximately(16m, 0.001m);
        BatchService.ConvertUnitAmount(500m, "g", "lb").Should().BeApproximately(1.1023m, 0.001m);
        BatchService.ConvertUnitAmount(500m, "ml", "L").Should().Be(0.5m);

        var userId = $"brewer_ppg_{Guid.NewGuid():N}";
        var marisOtter = new Ingredient { Id = Guid.NewGuid(), Name = "Maris Otter Pale", Type = IngredientType.Fermentable, PotentialGravity = 1.038m };
        var crystal60 = new Ingredient { Id = Guid.NewGuid(), Name = "Crystal 60L", Type = IngredientType.Fermentable, PotentialGravity = 1.034m };
        _db.Ingredients.AddRange(marisOtter, crystal60);
        await _db.SaveChangesAsync();

        // Create batch with 10 lb base malt + 500 g specialty malt
        var (createRes, created, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-IMP-01",
            Name: "ESB Imperial Units",
            BeerStyle: "Strong Bitter",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.052m,
            TargetFg: 1.012m,
            TargetAbv: 5.2m,
            TargetIbu: 38m,
            TargetColorSrm: 12m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: null,
            FermenterId: null,
            CustomIngredients:
            [
                new BatchIngredientInputDto("Maris Otter Pale", IngredientType.Fermentable, 10m, "lb", IngredientUsage.Mash, null, null),
                new BatchIngredientInputDto("Crystal 60L", IngredientType.Fermentable, 500m, "g", IngredientUsage.Mash, null, null)
            ]
        ), userId);

        createRes.Should().Be(BatchAccessResult.Success);
        // Total grain = ~5.036 kg -> strike water at 3 L/kg should be ~15.11 L (NOT 0 L!)
        created!.VolumeProfile!.StrikeWaterLiters.Should().BeApproximately(15.11m, 0.1m);

        // Update MeasuredOg = 1.052 at 20L (~5.284 gal) -> total points = 52 * 5.28344 = ~274.74
        // Max potential = (10 * 38) + (1.10231 * 34) = 380 + 37.48 = ~417.48 -> efficiency ~65.8%
        var (updRes, updated, _) = await _batchService.UpdateBatchAsync(
            created.Id, new UpdateBatchRequest(MeasuredOg: 1.052m, MeasuredBatchSizeLiters: 20m), userId);
        updRes.Should().Be(BatchAccessResult.Success);
        updated!.BrewhouseEfficiency.Should().NotBeNull();
        updated.BrewhouseEfficiency!.Value.Should().BeInRange(64.0m, 68.0m);
    }

    [Fact]
    public async Task SensorAssignments_RejectCrossBatchMashStepId_AcrossCreateUpdateAndUpdateSensors()
    {
        var userId = $"brewer_idor_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup", IsDefault = true };
        var probe = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Mash Probe", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.GenericSensor };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.Add(probe);
        await _db.SaveChangesAsync();

        var (_, batchA, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-IDOR-A", "Batch A", "IPA", DateOnly.FromDateTime(DateTime.UtcNow), 20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null), userId);
        var (_, batchB, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-IDOR-B", "Batch B", "IPA", DateOnly.FromDateTime(DateTime.UtcNow), 20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null), userId);

        var foreignStepId = batchA!.MashSteps[0].Id;
        var ownStepId = batchB!.MashSteps[0].Id;

        // 1. CreateBatchAsync with foreign mash step ID -> ValidationFailed
        var (createBadRes, _, createBadErr) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-IDOR-C", "Batch C", "IPA", DateOnly.FromDateTime(DateTime.UtcNow), 20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null,
            SensorAssignments: [new BatchSensorAssignmentInput(probe.Id, BrewStage.Mash, foreignStepId)]), userId);
        createBadRes.Should().Be(BatchAccessResult.ValidationFailed);
        createBadErr.Should().Contain("does not belong to this batch");

        // 2. UpdateBatchAsync with foreign mash step ID -> ValidationFailed
        var (updBadRes, _, _) = await _batchService.UpdateBatchAsync(
            batchB.Id, new UpdateBatchRequest(SensorAssignments: [new BatchSensorAssignmentInput(probe.Id, BrewStage.Mash, foreignStepId)]), userId);
        updBadRes.Should().Be(BatchAccessResult.ValidationFailed);

        // 3. UpdateBatchSensorsAsync with foreign mash step ID -> ValidationFailed
        var (sensBadRes, _, _) = await _batchService.UpdateBatchSensorsAsync(
            batchB.Id, [new BatchSensorAssignmentInput(probe.Id, BrewStage.Mash, foreignStepId)], userId);
        sensBadRes.Should().Be(BatchAccessResult.ValidationFailed);

        // 4. UpdateBatchSensorsAsync with own mash step ID -> Success and populates StepName!
        var (sensOkRes, sensors, _) = await _batchService.UpdateBatchSensorsAsync(
            batchB.Id, [new BatchSensorAssignmentInput(probe.Id, BrewStage.Mash, ownStepId)], userId);
        sensOkRes.Should().Be(BatchAccessResult.Success);
        sensors.Should().ContainSingle();
        sensors![0].StepName.Should().Be(batchB.MashSteps[0].Name);
    }

    [Fact]
    public async Task ThreeVesselOccupancy_FermentationStepStartedAt_AndBackdatedReadingSafeguard_WorkAcrossLifecycle()
    {
        var userId = $"brewer_3v_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "3V Brewhouse", IsDefault = true };
        var hlt = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "HLT 40L", Type = EquipmentType.Boiler, Subtype = EquipmentSubtype.Hlt, Capacity = 40m, CapacityLiters = 40m };
        var mlt = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "MLT 40L", Type = EquipmentType.Boiler, Subtype = EquipmentSubtype.MashTun, Capacity = 40m, CapacityLiters = 40m };
        var kettle = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Boil Kettle 50L", Type = EquipmentType.Boiler, Subtype = EquipmentSubtype.Pan, Capacity = 50m, CapacityLiters = 50m };
        var fermenter = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Conical 30L", Type = EquipmentType.Fermenter, Subtype = EquipmentSubtype.ConicalFermenter, Capacity = 30m, CapacityLiters = 30m };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(hlt, mlt, kettle, fermenter);
        await _db.SaveChangesAsync();

        var (createRes, created, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-3V-01",
            Name: "3V Pilsner",
            BeerStyle: "German Pils",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.048m,
            TargetFg: 1.008m,
            TargetAbv: 5.2m,
            TargetIbu: 34m,
            TargetColorSrm: 3.5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: kettle.Id,
            FermenterId: fermenter.Id,
            HltId: hlt.Id,
            MashTunId: mlt.Id,
            CustomIngredients:
            [
                new BatchIngredientInputDto("Pilsner Malt", IngredientType.Fermentable, 4.5m, "kg", IngredientUsage.Mash, null, null)
            ],
            CustomFermentationSteps:
            [
                new BatchFermentationStepInputDto(1, "Primary Lager Rest", FermentationStepType.Primary, 10.0m, 10, TriggerGravity: 1.020m),
                new BatchFermentationStepInputDto(2, "Diacetyl Rest", FermentationStepType.DiacetylRest, 16.0m, 3)
            ]
        ), userId);

        createRes.Should().Be(BatchAccessResult.Success);
        created!.HltName.Should().Be("HLT 40L");
        created.MashTunName.Should().Be("MLT 40L");
        created.BoilerName.Should().Be("Boil Kettle 50L");

        // During Mash in 3V: MLT holds strike water, HLT holds sparge water, Boil Kettle is still 0L
        mlt.CurrentVolumeLiters.Should().BeGreaterThan(0m);
        hlt.CurrentVolumeLiters.Should().BeGreaterThan(0m);
        kettle.CurrentVolumeLiters.Should().Be(0m);

        // Advance Mash -> Boil: MLT and HLT freed, Boil Kettle occupied
        var (boilRes, _, _) = await _batchService.AdvanceStageAsync(
            created.Id, new AdvanceBatchStageRequest(BrewStage.Boil, MeasuredPreBoilVolumeLiters: 25.5m), userId);
        boilRes.Should().Be(BatchAccessResult.Success);
        mlt.CurrentVolumeLiters.Should().Be(0m);
        hlt.CurrentVolumeLiters.Should().Be(0m);
        kettle.CurrentVolumeLiters.Should().Be(25.5m);

        // Advance Boil -> Ferment: Boil Kettle freed, Fermenter occupied, Fermentation Step 1 StartedAt initialized
        var (fermRes, fermBatch, _) = await _batchService.AdvanceStageAsync(
            created.Id, new AdvanceBatchStageRequest(BrewStage.Ferment, MeasuredOg: 1.050m, MeasuredBatchSizeLiters: 20.0m), userId);
        fermRes.Should().Be(BatchAccessResult.Success);
        kettle.CurrentVolumeLiters.Should().Be(0m);
        fermenter.CurrentVolumeLiters.Should().Be(20.0m);
        fermBatch!.FermentationSteps[0].StartedAt.Should().NotBeNull();
        fermBatch.FermentationSteps[1].StartedAt.Should().BeNull();

        // Complete Fermentation Step 1 -> Step 2 StartedAt automatically initialized
        var (toggleFermRes, _, _) = await _batchService.ToggleFermentationStepAsync(
            created.Id, fermBatch.FermentationSteps[0].Id, new ToggleBatchFermentationStepRequest(true, 10.2m), userId);
        toggleFermRes.Should().Be(BatchAccessResult.Success);

        var (_, afterStepToggle) = await _batchService.GetBatchByIdAsync(created.Id, userId);
        afterStepToggle!.FermentationSteps[1].StartedAt.Should().NotBeNull();

        // Manual temp log during Ferment uses active Step 2 TargetTemperatureC (16.0°C) and records ActualTemperatureC
        var (logRes, tempDto, _) = await _batchService.LogTemperatureReadingAsync(
            created.Id, new LogBatchTemperatureRequest(15.8m), userId);
        logRes.Should().Be(BatchAccessResult.Success);
        tempDto!.TargetTemperatureC.Should().Be(16.0m);
        tempDto.StepName.Should().Be("Diacetyl Rest");

        // Add current gravity reading (1.014 at T0), then add a backdated reading (1.032 at T0 - 2 days)
        var t0 = DateTime.UtcNow;
        await _batchService.AddReadingAsync(created.Id, new AddBatchReadingRequest(t0, 1.014m, 15.8m, "Latest"), userId);
        await _batchService.AddReadingAsync(created.Id, new AddBatchReadingRequest(t0.AddDays(-2), 1.032m, 10.5m, "Backdated"), userId);

        var (_, afterBackdated) = await _batchService.GetBatchByIdAsync(created.Id, userId);
        afterBackdated!.CurrentGravity.Should().Be(1.014m);

        // DeleteBatchAsync frees all vessels (including HLT and MashTun if occupied)
        hlt.CurrentVolumeLiters = 10m;
        mlt.CurrentVolumeLiters = 12m;
        await _db.SaveChangesAsync();

        var delRes = await _batchService.DeleteBatchAsync(created.Id, userId);
        delRes.Should().Be(BatchAccessResult.Success);
        hlt.CurrentVolumeLiters.Should().Be(0m);
        mlt.CurrentVolumeLiters.Should().Be(0m);
        fermenter.CurrentVolumeLiters.Should().Be(0m);
    }
}