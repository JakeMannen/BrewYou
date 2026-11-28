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

        // Act 2: Modify completed batch succeeds (F6) and does not auto-advance
        batch.Status = BatchStatus.Completed;
        await _db.SaveChangesAsync();

        var (resCompleted, dtoCompleted, errCompleted) = await _batchService.ToggleMashStepAsync(batch.Id, mashStep.Id, toggleReq, userId);
        resCompleted.Should().Be(BatchAccessResult.Success);
        dtoCompleted.Should().NotBeNull();
        errCompleted.Should().BeNull();
        batch.Status.Should().Be(BatchStatus.Completed);

        // Act 3: Modify archived batch returns Conflict
        batch.Status = BatchStatus.Archived;
        await _db.SaveChangesAsync();

        var (resConflict, dtoConflict, errConflict) = await _batchService.ToggleMashStepAsync(batch.Id, mashStep.Id, toggleReq, userId);
        resConflict.Should().Be(BatchAccessResult.Conflict);
        dtoConflict.Should().BeNull();
        errConflict.Should().Contain("archived");
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

        // Act 3: Completed batch succeeds (F6)
        batch.Status = BatchStatus.Completed;
        await _db.SaveChangesAsync();
        var (resCompleted, dtoCompleted, _) = await _batchService.ToggleFermentationStepAsync(batch.Id, fermStep.Id, req, userId);
        resCompleted.Should().Be(BatchAccessResult.Success);
        dtoCompleted!.IsCompleted.Should().BeTrue();

        // Act 4: Archived batch returns Conflict
        batch.Status = BatchStatus.Archived;
        await _db.SaveChangesAsync();
        var (resArchived, _, errArchived) = await _batchService.ToggleFermentationStepAsync(batch.Id, fermStep.Id, req, userId);
        resArchived.Should().Be(BatchAccessResult.Conflict);
        errArchived.Should().Contain("archived");
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
            Subtype = EquipmentSubtype.GenericSensor,
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
    public async Task ToggleIngredientAsync_RefundsAdHocNamedStock_AllowsCompletedAndRejectsArchivedBatch()
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

        // Completed batch -> Allowed (F6)
        batch.Status = BatchStatus.Completed;
        await _db.SaveChangesAsync();
        var (completedRes, completedDto, _) = await _batchService.ToggleIngredientAsync(batch.Id, adHocHop.Id, true, userId);
        completedRes.Should().Be(BatchAccessResult.Success);
        completedDto!.IsDeducted.Should().BeTrue();
        (await _db.IngredientStocks.FirstAsync(s => s.UserId == userId && s.IngredientId == citra.Id)).Amount.Should().Be(60m);

        // Archived batch -> Conflict
        batch.Status = BatchStatus.Archived;
        await _db.SaveChangesAsync();
        var (conflictRes, _, conflictErr) = await _batchService.ToggleIngredientAsync(batch.Id, adHocHop.Id, false, userId);
        conflictRes.Should().Be(BatchAccessResult.Conflict);
        conflictErr.Should().Contain("archived");
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

    [Fact]
    public async Task AdvanceStageAsync_ToPackage_WithKeg_AssignsKegAndUpdatesVolume()
    {
        var userId = $"brewer_pkg_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Test Setup", IsDefault = true };
        var fermenter = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Conical", Type = EquipmentType.Fermenter, Capacity = 30m, CapacityLiters = 30m, Unit = VolumeUnit.Liters };
        var keg = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Cornelius 19L", Type = EquipmentType.Keg, Capacity = 19m, CapacityLiters = 19m, Unit = VolumeUnit.Liters };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(fermenter, keg);
        await _db.SaveChangesAsync();

        var (createRes, created, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{Guid.NewGuid():N}"[..18],
            Name: "Package Test IPA",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 19.0m,
            TargetOg: 1.055m,
            TargetFg: 1.010m,
            TargetAbv: 5.9m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: null,
            FermenterId: fermenter.Id
        ), userId);

        createRes.Should().Be(BatchAccessResult.Success);

        // Advance to Ferment
        await _batchService.AdvanceStageAsync(created!.Id, new AdvanceBatchStageRequest(BrewStage.Ferment, MeasuredBatchSizeLiters: 19.0m), userId);
        fermenter.CurrentVolumeLiters.Should().Be(19.0m);

        // Advance to Package with Keg
        var (pkgRes, pkgBatch, _) = await _batchService.AdvanceStageAsync(
            created.Id,
            new AdvanceBatchStageRequest(BrewStage.Package, PackagingVesselId: keg.Id, MeasuredPackagedVolumeLiters: 18.0m, MeasuredFg: 1.010m),
            userId);

        pkgRes.Should().Be(BatchAccessResult.Success);
        pkgBatch!.Status.Should().Be(BatchStatus.Completed);
        fermenter.CurrentVolumeLiters.Should().Be(0m);

        keg.AssignedBatchId.Should().Be(created.Id);
        keg.AssignedAt.Should().NotBeNull();
        keg.CurrentVolumeLiters.Should().Be(18.0m);

        // UpdateBatchAsync with new packaged volume
        var (updRes, _, _) = await _batchService.UpdateBatchAsync(
            created.Id,
            new UpdateBatchRequest(MeasuredPackagedVolumeLiters: 17.5m),
            userId);
        updRes.Should().Be(BatchAccessResult.Success);
        keg.CurrentVolumeLiters.Should().Be(17.5m);

        // Delete batch unassigns keg
        var delRes2 = await _batchService.DeleteBatchAsync(created.Id, userId);
        delRes2.Should().Be(BatchAccessResult.Success);
        keg.AssignedBatchId.Should().BeNull();
        keg.CurrentVolumeLiters.Should().Be(0m);
    }

    [Fact]
    public async Task SensorAssignments_RejectBrewLinkGatewayTapAndVessels_AcrossCreateUpdateAndUpdateSensors()
    {
        var userId = $"brewer_sensor_eligibility_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup", IsDefault = true };

        var brewLinkGateway = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BrewerySetup = setup,
            Name = "Garage BrewLink",
            Type = EquipmentType.Sensor,
            Subtype = EquipmentSubtype.BrewLink,
            ConnectionType = EquipmentConnectionType.BrewLink
        };
        var tap = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BrewerySetup = setup,
            Name = "Tap 1",
            Type = EquipmentType.Tap,
            Subtype = EquipmentSubtype.StandardTap
        };
        var vessel = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BrewerySetup = setup,
            Name = "Serving Vessel",
            Type = EquipmentType.Other,
            Subtype = EquipmentSubtype.Other,
            Capacity = 20m,
            CapacityLiters = 20m
        };
        var ispindel = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BrewerySetup = setup,
            Name = "iSpindel Blue",
            Type = EquipmentType.Sensor,
            Subtype = EquipmentSubtype.ISpindel
        };

        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(brewLinkGateway, tap, vessel, ispindel);
        await _db.SaveChangesAsync();

        var (_, batch, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, $"B-{Guid.NewGuid():N}"[..16], "Valid Batch", "IPA",
            DateOnly.FromDateTime(DateTime.UtcNow), 20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null,
            SensorAssignments: [new BatchSensorAssignmentInput(ispindel.Id, BrewStage.Ferment, null)]), userId);
        batch.Should().NotBeNull();
        batch!.SensorAssignments.Should().ContainSingle(sa => sa.EquipmentId == ispindel.Id);

        foreach (var ineligible in new[] { brewLinkGateway, tap, vessel })
        {
            var (createRes, _, createErr) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
                null, $"B-{Guid.NewGuid():N}"[..16], "Invalid Sensor Batch", "IPA",
                DateOnly.FromDateTime(DateTime.UtcNow), 20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null,
                SensorAssignments: [new BatchSensorAssignmentInput(ineligible.Id, BrewStage.Ferment, null)]), userId);
            createRes.Should().Be(BatchAccessResult.ValidationFailed);
            createErr.Should().NotBeNullOrWhiteSpace();

            var (updateRes, _, updateErr) = await _batchService.UpdateBatchAsync(
                batch.Id,
                new UpdateBatchRequest(SensorAssignments: [new BatchSensorAssignmentInput(ineligible.Id, BrewStage.Ferment, null)]),
                userId);
            updateRes.Should().Be(BatchAccessResult.ValidationFailed);
            updateErr.Should().NotBeNullOrWhiteSpace();

            var (sensorsRes, _, sensorsErr) = await _batchService.UpdateBatchSensorsAsync(
                batch.Id,
                [new BatchSensorAssignmentInput(ineligible.Id, BrewStage.Ferment, null)],
                userId);
            sensorsRes.Should().Be(BatchAccessResult.ValidationFailed);
            sensorsErr.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task CreateBatchAsync_WithMashMaltAndBoilSugar_CalculatesWaterVolumesOnlyFromMashGrain_AndSnapshotsCategory()
    {
        var userId = $"brewer_sugar_water_{Guid.NewGuid():N}";
        var pilsner = new Ingredient
        {
            Id = Guid.NewGuid(),
            Name = "Pilsner Malt",
            Type = IngredientType.Fermentable,
            FermentableCategory = FermentableCategory.Grain,
            PotentialGravity = 1.037m,
            ColorSrm = 1.8m
        };
        var candiSyrup = new Ingredient
        {
            Id = Guid.NewGuid(),
            Name = "D-90 Candi Syrup",
            Type = IngredientType.Fermentable,
            FermentableCategory = FermentableCategory.Sugar,
            PotentialGravity = 1.032m,
            ColorSrm = 90m,
            FermentabilityPercent = 100m
        };
        var yeast = new Ingredient
        {
            Id = Guid.NewGuid(),
            Name = "BE-256",
            Type = IngredientType.Yeast,
            AttenuationPercent = 82m
        };
        _db.Ingredients.AddRange(pilsner, candiSyrup, yeast);

        var recipeWithSugar = new Recipe
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Belgian Dark Strong",
            BeerStyle = "Belgian Dark Strong Ale",
            BatchSizeLiters = 20m,
            BoilTimeMinutes = 60,
            EfficiencyPercent = 75m,
            Ingredients =
            [
                new RecipeIngredient { IngredientId = pilsner.Id, Ingredient = pilsner, Amount = 5.0m, Unit = "kg", Usage = IngredientUsage.Mash },
                new RecipeIngredient { IngredientId = candiSyrup.Id, Ingredient = candiSyrup, Amount = 1.5m, Unit = "kg", Usage = IngredientUsage.Boil },
                new RecipeIngredient { IngredientId = yeast.Id, Ingredient = yeast, Amount = 11.5m, Unit = "g", Usage = IngredientUsage.Primary }
            ]
        };
        var grainOnlyRecipe = new Recipe
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Grain Only Reference",
            BeerStyle = "Belgian Golden",
            BatchSizeLiters = 20m,
            BoilTimeMinutes = 60,
            EfficiencyPercent = 75m,
            Ingredients =
            [
                new RecipeIngredient { IngredientId = pilsner.Id, Ingredient = pilsner, Amount = 5.0m, Unit = "kg", Usage = IngredientUsage.Mash },
                new RecipeIngredient { IngredientId = yeast.Id, Ingredient = yeast, Amount = 11.5m, Unit = "g", Usage = IngredientUsage.Primary }
            ]
        };
        _db.Recipes.AddRange(recipeWithSugar, grainOnlyRecipe);
        await _db.SaveChangesAsync();

        var (_, sugarBatch, sugarErr) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            RecipeId: recipeWithSugar.Id,
            BatchCode: $"B-SUG-{Guid.NewGuid():N}"[..16],
            Name: "Sugar Water Test",
            BeerStyle: "Belgian Dark Strong Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.078m,
            TargetFg: 1.012m,
            TargetAbv: 8.6m,
            TargetIbu: 25m,
            TargetColorSrm: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: null,
            FermenterId: null), userId);

        var (_, grainBatch, grainErr) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            RecipeId: grainOnlyRecipe.Id,
            BatchCode: $"B-GRN-{Guid.NewGuid():N}"[..16],
            Name: "Grain Only Reference Test",
            BeerStyle: "Belgian Golden",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.055m,
            TargetFg: 1.010m,
            TargetAbv: 5.9m,
            TargetIbu: 25m,
            TargetColorSrm: 3m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: null,
            FermenterId: null), userId);

        sugarErr.Should().BeNull();
        grainErr.Should().BeNull();
        sugarBatch!.VolumeProfile.Should().NotBeNull();
        grainBatch!.VolumeProfile.Should().NotBeNull();

        // Adding 1.5 kg boil sugar must NOT increase strike water, decrease sparge water, or increase total water
        sugarBatch.VolumeProfile!.TotalWaterLiters.Should().Be(grainBatch.VolumeProfile!.TotalWaterLiters);
        sugarBatch.VolumeProfile.StrikeWaterLiters.Should().Be(grainBatch.VolumeProfile.StrikeWaterLiters);
        sugarBatch.VolumeProfile.SpargeWaterLiters.Should().Be(grainBatch.VolumeProfile.SpargeWaterLiters);

        // Snapshots preserve FermentableCategory and FermentabilityPercent
        var sugarSnapshot = sugarBatch.Ingredients.First(i => i.Name == "D-90 Candi Syrup");
        sugarSnapshot.FermentableCategory.Should().Be(FermentableCategory.Sugar);
        sugarSnapshot.FermentabilityPercent.Should().Be(100m);
    }

    [Fact]
    public async Task ToggleMashStepAsync_OnCompletedBrewLinkBatch_SucceedsWithoutAutoAdvancing()
    {
        var userId = $"brewer_completed_bl_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup", IsDefault = true };
        var boiler = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BrewerySetup = setup,
            Name = "G30",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            ConnectionType = EquipmentConnectionType.BrewLink,
            Capacity = 30m,
            CapacityLiters = 30m
        };
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-BL-COMP",
            Name = "Completed BrewLink Batch",
            BeerStyle = "IPA",
            Status = BatchStatus.Completed,
            CurrentStage = BrewStage.Package,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            BoilerId = boiler.Id,
            Boiler = boiler,
            MashTunId = boiler.Id,
            MashTun = boiler,
            CompletedAt = DateTime.UtcNow
        };
        var step = new BatchMashStep
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Batch = batch,
            StepOrder = 1,
            Name = "Sacch Rest",
            Type = MashStepType.Temperature,
            TargetTemperatureC = 65m,
            DurationMinutes = 60,
            IsCompleted = false
        };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.Add(boiler);
        _db.Batches.Add(batch);
        _db.BatchMashSteps.Add(step);
        await _db.SaveChangesAsync();

        var (res, dto, err) = await _batchService.ToggleMashStepAsync(
            batch.Id, step.Id, new ToggleBatchMashStepRequest(true, 65.2m, 60), userId);

        res.Should().Be(BatchAccessResult.Success);
        err.Should().BeNull();
        dto!.IsCompleted.Should().BeTrue();
        batch.Status.Should().Be(BatchStatus.Completed);
        batch.CurrentStage.Should().Be(BrewStage.Package);
    }

    [Fact]
    public async Task UpdateBatchAsync_BrewDateAndStatusTransitions_ReopensReconcilesStagesAndDecouplesKegAndTap()
    {
        var userId = $"brewer_lifecycle_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup", IsDefault = true };
        var boiler = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Kettle", Type = EquipmentType.Boiler, Capacity = 30m, CapacityLiters = 30m, Unit = VolumeUnit.Liters };
        var fermenter = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "FV1", Type = EquipmentType.Fermenter, Capacity = 30m, CapacityLiters = 30m, Unit = VolumeUnit.Liters };
        var keg = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Keg1", Type = EquipmentType.Keg, Capacity = 19m, CapacityLiters = 19m, Unit = VolumeUnit.Liters };
        var tap = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Tap1", Type = EquipmentType.Tap, Subtype = EquipmentSubtype.StandardTap };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(boiler, fermenter, keg, tap);
        await _db.SaveChangesAsync();

        var (_, created, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-LIFE-01", "Lifecycle Ale", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.052m, 1.010m, 5.5m, 35m, 6m, 60, 75m, boiler.Id, fermenter.Id), userId);

        await _batchService.AdvanceStageAsync(created!.Id, new AdvanceBatchStageRequest(
            BrewStage.Package, PackagingVesselId: keg.Id, MeasuredOg: 1.052m, MeasuredFg: 1.010m, MeasuredPackagedVolumeLiters: 18.5m), userId);

        tap.AssignedBatchId = created.Id;
        tap.AssignedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        keg.AssignedBatchId.Should().Be(created.Id);
        keg.CurrentVolumeLiters.Should().Be(18.5m);

        // 1. Update BrewDate post-finish
        var editedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var (dateRes, dateDto, _) = await _batchService.UpdateBatchAsync(
            created.Id, new UpdateBatchRequest(BrewDate: editedDate), userId);
        dateRes.Should().Be(BatchAccessResult.Success);
        dateDto!.BrewDate.Should().Be(editedDate);

        // 2. Reopen Completed -> Fermenting
        var (reopenRes, reopened, _) = await _batchService.UpdateBatchAsync(
            created.Id, new UpdateBatchRequest(Status: BatchStatus.Fermenting), userId);
        reopenRes.Should().Be(BatchAccessResult.Success);
        reopened!.Status.Should().Be(BatchStatus.Fermenting);
        reopened.CurrentStage.Should().Be(BrewStage.Ferment);
        reopened.CompletedAt.Should().BeNull();
        reopened.StageHistory.First(sh => sh.Stage == BrewStage.Ferment).Status.Should().Be(StageStatus.InProgress);
        reopened.StageHistory.First(sh => sh.Stage == BrewStage.Package).Status.Should().Be(StageStatus.Pending);

        // Keg & Tap decoupled, Fermenter re-occupied on live reopen
        keg.AssignedBatchId.Should().BeNull();
        keg.CurrentVolumeLiters.Should().Be(0m);
        tap.AssignedBatchId.Should().BeNull();
        fermenter.CurrentVolumeLiters.Should().Be(20m);

        // 3. Re-advance forward Ferment -> Package works again
        var (rePkgRes, rePkgDto, _) = await _batchService.AdvanceStageAsync(
            created.Id, new AdvanceBatchStageRequest(BrewStage.Package, PackagingVesselId: keg.Id, MeasuredPackagedVolumeLiters: 18.0m), userId);
        rePkgRes.Should().Be(BatchAccessResult.Success);
        rePkgDto!.Status.Should().Be(BatchStatus.Completed);
        keg.AssignedBatchId.Should().Be(created.Id);

        // 4. Transition Completed -> Archived decouples keg and locks future mutations
        var (archRes, archDto, _) = await _batchService.UpdateBatchAsync(
            created.Id, new UpdateBatchRequest(Status: BatchStatus.Archived), userId);
        archRes.Should().Be(BatchAccessResult.Success);
        archDto!.Status.Should().Be(BatchStatus.Archived);
        keg.AssignedBatchId.Should().BeNull();
        keg.CurrentVolumeLiters.Should().Be(0m);

        var (lockedUpd, _, _) = await _batchService.UpdateBatchAsync(created.Id, new UpdateBatchRequest(Notes: "Locked"), userId);
        lockedUpd.Should().Be(BatchAccessResult.Conflict);

        var (lockedRead, _, _) = await _batchService.AddReadingAsync(created.Id, new AddBatchReadingRequest(1.010m), userId);
        lockedRead.Should().Be(BatchAccessResult.Conflict);
    }

    [Fact]
    public async Task UpdateBatchAsync_VesselValidationAndReplacement_ReconcilesVolumesAndAbvAndDeletePreservesCompletedVessels()
    {
        var userId = $"brewer_vessels_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup", IsDefault = true };
        var boiler1 = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Kettle 1", Type = EquipmentType.Boiler, Capacity = 30m, CapacityLiters = 30m, Unit = VolumeUnit.Liters };
        var boiler2 = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Kettle 2", Type = EquipmentType.Boiler, Capacity = 40m, CapacityLiters = 40m, Unit = VolumeUnit.Liters };
        var fv1 = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "FV1", Type = EquipmentType.Fermenter, Capacity = 30m, CapacityLiters = 30m, Unit = VolumeUnit.Liters };
        var keg1 = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Keg 1", Type = EquipmentType.Keg, Capacity = 19m, CapacityLiters = 19m, Unit = VolumeUnit.Liters };
        var keg2 = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Keg 2", Type = EquipmentType.Keg, Capacity = 19m, CapacityLiters = 19m, Unit = VolumeUnit.Liters };
        var tap = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Tap", Type = EquipmentType.Tap, Subtype = EquipmentSubtype.StandardTap };
        var sensor = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Tilt", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.Tilt };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(boiler1, boiler2, fv1, keg1, keg2, tap, sensor);
        await _db.SaveChangesAsync();

        var (_, created, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-VES-01", "Vessel Swap Ale", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.25m, 35m, 6m, 60, 75m, boiler1.Id, fv1.Id,
            CustomIngredients: [new("Pale Malt", IngredientType.Fermentable, 5.0m, "kg", IngredientUsage.Mash, null, null)]), userId);

        // Missing vessel ID -> NotFound
        var (missingRes, _, _) = await _batchService.UpdateBatchAsync(created!.Id, new UpdateBatchRequest(BoilerId: Guid.NewGuid()), userId);
        missingRes.Should().Be(BatchAccessResult.NotFound);

        // Sensor/Tap in vessel slot -> ValidationFailed
        var (sensorVesselRes, _, _) = await _batchService.UpdateBatchAsync(created.Id, new UpdateBatchRequest(FermenterId: sensor.Id), userId);
        sensorVesselRes.Should().Be(BatchAccessResult.ValidationFailed);
        var (tapVesselRes, _, _) = await _batchService.UpdateBatchAsync(created.Id, new UpdateBatchRequest(HltId: tap.Id), userId);
        tapVesselRes.Should().Be(BatchAccessResult.ValidationFailed);

        // Swap boiler1 -> boiler2 while MashTunId still points to boiler1 (AIO fallback): boiler1 not zeroed until MashTunId also swapped
        await _batchService.UpdateBatchAsync(created.Id, new UpdateBatchRequest(BoilerId: boiler2.Id, MashTunId: boiler2.Id), userId);
        boiler1.CurrentVolumeLiters.Should().Be(0m);

        // Advance to Package with keg1, then lower MeasuredOg below FG -> ABV resets to 0.0m (no stale ABV)
        await _batchService.AdvanceStageAsync(created.Id, new AdvanceBatchStageRequest(
            BrewStage.Package, PackagingVesselId: keg1.Id, MeasuredOg: 1.055m, MeasuredFg: 1.012m, MeasuredBatchSizeLiters: 20m), userId);

        var (abvResetRes, abvResetDto, _) = await _batchService.UpdateBatchAsync(
            created.Id, new UpdateBatchRequest(MeasuredOg: 1.010m, MeasuredFg: 1.015m), userId);
        abvResetRes.Should().Be(BatchAccessResult.Success);
        abvResetDto!.AlcoholByVolume.Should().Be(0.0m);

        // Swap keg1 -> keg2 even after setting BrewDate to past: keg1 must be decoupled cleanly (AGENTS.md 3.K)
        var (kegSwapRes, _, _) = await _batchService.UpdateBatchAsync(
            created.Id, new UpdateBatchRequest(BrewDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), PackagingVesselId: keg2.Id), userId);
        kegSwapRes.Should().Be(BatchAccessResult.Success);
        keg1.AssignedBatchId.Should().BeNull();
        keg1.CurrentVolumeLiters.Should().Be(0m);

        // Completed batch DeleteBatchAsync must NOT zero another live batch's boiler/fermenter
        boiler2.CurrentVolumeLiters = 15m;
        fv1.CurrentVolumeLiters = 19m;
        await _batchService.UpdateBatchAsync(created.Id, new UpdateBatchRequest(BrewDate: DateOnly.FromDateTime(DateTime.UtcNow)), userId);

        var delRes = await _batchService.DeleteBatchAsync(created.Id, userId);
        delRes.Should().Be(BatchAccessResult.Success);
        boiler2.CurrentVolumeLiters.Should().Be(15m);
        fv1.CurrentVolumeLiters.Should().Be(19m);
    }

    [Fact]
    public async Task AddReadingAsync_F8_And_F9_CalibrationOwnershipOfflineBrewLinkAbvAndSummaryParity()
    {
        var userId = $"brewer_f8_f9_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup", IsDefault = true };
        var offlineBrewLinkBoiler = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Offline G30",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            ConnectionType = EquipmentConnectionType.BrewLink,
            LatestMetricsJson = "{\"connected\":false}",
            Capacity = 30m,
            CapacityLiters = 30m
        };
        var tap = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "Tap", Type = EquipmentType.Tap, Subtype = EquipmentSubtype.StandardTap };
        var gateway = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, Name = "GW", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.BrewLink };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(offlineBrewLinkBoiler, tap, gateway);
        await _db.SaveChangesAsync();

        var (_, batch, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-F8F9-01", "Telemetry Parity Ale", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.055m, 1.010m, 5.9m, 40m, 6m, 60, 75m, offlineBrewLinkBoiler.Id, null, MeasuredOg: 1.055m), userId);

        var mashStepId = batch!.MashSteps[0].Id;

        // Bounds validation
        (await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(0.899m), userId)).Result.Should().Be(BatchAccessResult.ValidationFailed);
        (await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(1.251m), userId)).Result.Should().Be(BatchAccessResult.ValidationFailed);
        (await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(1.040m, -20.1m), userId)).Result.Should().Be(BatchAccessResult.ValidationFailed);
        (await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(1.040m, 120.1m), userId)).Result.Should().Be(BatchAccessResult.ValidationFailed);

        // Missing / ineligible equipment & foreign mash step
        (await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(null, 1.040m, 20m, null, EquipmentId: Guid.NewGuid()), userId)).Result.Should().Be(BatchAccessResult.NotFound);
        (await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(null, 1.040m, 20m, null, EquipmentId: tap.Id), userId)).Result.Should().Be(BatchAccessResult.ValidationFailed);
        (await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(null, 1.040m, 20m, null, EquipmentId: gateway.Id), userId)).Result.Should().Be(BatchAccessResult.ValidationFailed);
        (await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(null, 1.040m, 20m, null, BatchMashStepId: Guid.NewGuid()), userId)).Result.Should().Be(BatchAccessResult.NotFound);

        // Valid reading on disconnected BrewLink boiler with hydrometer calibration
        var rawSg = 1.048m;
        var sampleTemp = 45.0m;
        var expectedCorrectedSg = BrewingCalculator.CorrectHydrometerReading(rawSg, sampleTemp);

        var (okRes, readingDto, _) = await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(
            DateTime.UtcNow.AddMinutes(-10), rawSg, sampleTemp, "Warm mash sample",
            EquipmentId: offlineBrewLinkBoiler.Id, Stage: BrewStage.Mash, BatchMashStepId: mashStepId, CalibrateHydrometerTemp: true), userId);

        okRes.Should().Be(BatchAccessResult.Success);
        readingDto!.SpecificGravity.Should().Be(expectedCorrectedSg);
        readingDto.AlcoholByVolume.Should().Be(0.0m); // SG >= OG -> 0.0m, never null/stale
        offlineBrewLinkBoiler.LatestMetricsJson.Should().Be("{\"connected\":false}"); // Untouched connection flag

        var eqReadings = await _db.EquipmentReadings.Where(er => er.BatchId == batch.Id && er.Source == "Manual").ToListAsync();
        eqReadings.Should().ContainSingle(er => er.EquipmentId == offlineBrewLinkBoiler.Id && er.SpecificGravity == expectedCorrectedSg && er.BatchMashStepId == mashStepId);

        // Second reading with SG < OG computes reading-specific ABV in both AddReadingAsync and MapToDetailDto
        var (fgRes, fgDto, _) = await _batchService.AddReadingAsync(batch.Id, new AddBatchReadingRequest(
            DateTime.UtcNow, 1.012m, 20.0m, "FG sample"), userId);
        fgRes.Should().Be(BatchAccessResult.Success);
        fgDto!.AlcoholByVolume.Should().Be(BrewingCalculator.CalculateActualAbv(1.055m, 1.012m));

        var (_, detailDto) = await _batchService.GetBatchByIdAsync(batch.Id, userId);
        detailDto!.Readings.Should().HaveCount(2);
        detailDto.Readings[0].AlcoholByVolume.Should().Be(0.0m);
        detailDto.Readings[1].AlcoholByVolume.Should().Be(BrewingCalculator.CalculateActualAbv(1.055m, 1.012m));

        // Summary CurrentGravity fallback parity when Batch.CurrentGravity is null in DB
        var dbBatch = await _db.Batches.FindAsync(batch.Id);
        dbBatch!.CurrentGravity = null;
        await _db.SaveChangesAsync();

        var (summaries, _) = await _batchService.GetBatchesAsync(userId, null, null, 1, 20);
        summaries.Should().ContainSingle(s => s.Id == batch.Id && s.CurrentGravity == 1.012m);
    }

    [Fact]
    public async Task CreateBatchAsync_SendRecipeToBoiler_WhenBoilerDisconnectedOrThrows_ReturnsSuccessWithOfflineFallback()
    {
        var userId = $"brewer_bl_fallback_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup", IsDefault = true };
        var boiler = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "BrewLink Kettle",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            ConnectionType = EquipmentConnectionType.BrewLink,
            Capacity = 30m,
            CapacityLiters = 30m
        };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.Add(boiler);
        await _db.SaveChangesAsync();

        var disconnectedService = new StubBrewLinkService(BrewLinkSendResult.EquipmentDisconnected, "Boiler is offline");
        var svcOffline = new BatchService(_db, new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance), NullLogger<BatchService>.Instance, disconnectedService);

        var (resOffline, dtoOffline, _) = await svcOffline.CreateBatchAsync(new CreateBatchRequest(
            null, "B-BL-OFF", "Offline Fallback Ale", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.25m, 35m, 6m, 60, 75m, boiler.Id, null, SendRecipeToBoiler: true), userId);

        resOffline.Should().Be(BatchAccessResult.Success);
        dtoOffline!.BoilerSyncStatus.Should().Be("OfflineFallback");
        dtoOffline.BoilerSyncWarning.Should().Contain("Boiler is offline");

        // Advance the first batch out of hot-side so the boiler is free for the second batch
        (await svcOffline.AdvanceStageAsync(dtoOffline.Id, new AdvanceBatchStageRequest(BrewStage.Ferment, Force: true), userId)).Result
            .Should().Be(BatchAccessResult.Success);

        var sentService = new StubBrewLinkService(BrewLinkSendResult.Success, null);
        var svcSent = new BatchService(_db, new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance), NullLogger<BatchService>.Instance, sentService);

        var (resSent, dtoSent, _) = await svcSent.CreateBatchAsync(new CreateBatchRequest(
            null, "B-BL-ON", "Connected Boiler Ale", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.25m, 35m, 6m, 60, 75m, boiler.Id, null, SendRecipeToBoiler: true), userId);

        resSent.Should().Be(BatchAccessResult.Success);
        dtoSent!.BoilerSyncStatus.Should().Be("Sent");
        dtoSent.BoilerSyncWarning.Should().BeNull();
    }

    [Fact]
    public async Task GetBatchesAsync_ResolvesVesselTempForInProgressBatchesAcrossStagesSensorsAndReadings()
    {
        var userId = $"brewer_vtemp_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var mashTun = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Mash Tun 1",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.MashTun,
            CurrentTemperatureC = 66.4m,
            TemperatureUpdatedAt = now.AddMinutes(-1)
        };
        var boiler = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Boil Kettle 1",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            CurrentTemperatureC = 99.6m,
            TemperatureUpdatedAt = now.AddMinutes(-1)
        };
        var passiveFermenter = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Passive Conical",
            Type = EquipmentType.Fermenter,
            Subtype = EquipmentSubtype.ConicalFermenter,
            CurrentTemperatureC = null
        };
        var liveFermenter = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Glycol Conical",
            Type = EquipmentType.Fermenter,
            Subtype = EquipmentSubtype.ConicalFermenter,
            CurrentTemperatureC = 19.2m,
            TemperatureUpdatedAt = now,
            CurrentSpecificGravity = 1.032m,
            LastTelemetryAt = now
        };
        var staleFermenter = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Stale Conical",
            Type = EquipmentType.Fermenter,
            Subtype = EquipmentSubtype.ConicalFermenter,
            CurrentTemperatureC = 17.5m,
            TemperatureUpdatedAt = now.AddHours(-6),
            CurrentSpecificGravity = 1.040m,
            LastTelemetryAt = now.AddHours(-6)
        };
        var ispindel = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "iSpindel Red",
            Type = EquipmentType.Sensor,
            Subtype = EquipmentSubtype.ISpindel,
            CurrentTemperatureC = 20.8m,
            TemperatureUpdatedAt = now.AddMinutes(-2),
            CurrentSpecificGravity = 1.016m,
            LastTelemetryAt = now.AddMinutes(-2)
        };
        var silentSensor = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Tilt Pending",
            Type = EquipmentType.Sensor,
            Subtype = EquipmentSubtype.Tilt,
            CurrentTemperatureC = null
        };

        _db.Equipment.AddRange(mashTun, boiler, passiveFermenter, liveFermenter, staleFermenter, ispindel, silentSensor);

        // 1. Mash batch with MashTun live temp (prefers MashTun over Boiler)
        var mashBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-01",
            Name = "Mash Batch",
            Status = BatchStatus.Brewing,
            CurrentStage = BrewStage.Mash,
            BrewDate = today,
            MashTunId = mashTun.Id,
            BoilerId = boiler.Id
        };

        // 2. Boil batch with live Boiler temp
        var boilBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-02",
            Name = "Boil Batch",
            Status = BatchStatus.Brewing,
            CurrentStage = BrewStage.Boil,
            BrewDate = today,
            BoilerId = boiler.Id
        };

        // 3. Ferment batch with BOTH liveFermenter (newer) and assigned iSpindel sensor (older) -> sensor wins
        var sensorBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-03",
            Name = "Sensor Ferment Batch",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = today.AddDays(-2),
            FermenterId = liveFermenter.Id
        };
        sensorBatch.SensorAssignments.Add(new BatchSensorAssignment
        {
            BatchId = sensorBatch.Id,
            EquipmentId = ispindel.Id,
            Stage = BrewStage.Ferment
        });

        // 3b. Ferment batch with assigned sensor that has not reported yet + liveFermenter -> null (does not fall back to fermenter)
        var silentSensorBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-03B",
            Name = "Silent Sensor Ferment Batch",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = today.AddDays(-1),
            FermenterId = liveFermenter.Id
        };
        silentSensorBatch.SensorAssignments.Add(new BatchSensorAssignment
        {
            BatchId = silentSensorBatch.Id,
            EquipmentId = silentSensor.Id,
            Stage = BrewStage.Ferment
        });

        // 4. Ferment batch with newer manual BatchReading overriding older Fermenter live temp
        var manualReadingBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-04",
            Name = "Manual Reading Ferment Batch",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = today.AddDays(-3),
            FermenterId = staleFermenter.Id
        };

        // 5. Ferment batch with live Fermenter temp newer than older BatchReading
        var liveFermentBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-05",
            Name = "Live Ferment Batch",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = today.AddDays(-1),
            FermenterId = liveFermenter.Id
        };

        // 6. Ferment batch with only an earlier Mash EquipmentReading and no live ferment temp -> null
        var stageIsolatedBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-06",
            Name = "Stage Isolated Batch",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = today,
            FermenterId = passiveFermenter.Id
        };

        // 7. Completed batch referencing liveFermenter -> must NOT read live equipment temp (uses own last reading)
        var completedBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-07",
            Name = "Completed Batch",
            Status = BatchStatus.Completed,
            CurrentStage = BrewStage.Package,
            BrewDate = today.AddDays(-20),
            FermenterId = liveFermenter.Id
        };

        // 8. Planned batch referencing live Boiler -> must return null
        var plannedBatch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-VT-08",
            Name = "Planned Batch",
            Status = BatchStatus.Planned,
            CurrentStage = BrewStage.Mash,
            BrewDate = today.AddDays(5),
            BoilerId = boiler.Id
        };

        _db.Batches.AddRange(
            mashBatch, boilBatch, sensorBatch, silentSensorBatch, manualReadingBatch,
            liveFermentBatch, stageIsolatedBatch, completedBatch, plannedBatch);

        _db.BatchReadings.AddRange(
            new BatchReading
            {
                BatchId = manualReadingBatch.Id,
                Timestamp = now.AddMinutes(-5),
                SpecificGravity = 1.020m,
                TemperatureC = 21.3m
            },
            new BatchReading
            {
                BatchId = liveFermentBatch.Id,
                Timestamp = now.AddHours(-2),
                SpecificGravity = 1.025m,
                TemperatureC = 18.0m
            },
            new BatchReading
            {
                BatchId = completedBatch.Id,
                Timestamp = now.AddDays(-10),
                SpecificGravity = 1.010m,
                TemperatureC = 4.5m
            });

        _db.EquipmentReadings.Add(new EquipmentReading
        {
            BatchId = stageIsolatedBatch.Id,
            EquipmentId = boiler.Id,
            Stage = BrewStage.Mash,
            TemperatureC = 67.0m,
            Timestamp = now.AddHours(-3),
            Source = "Manual"
        });

        await _db.SaveChangesAsync();

        // Act
        var (summaries, _) = await _batchService.GetBatchesAsync(userId, null, null, 1, 20);
        var byId = summaries.ToDictionary(s => s.Id);

        // Assert
        byId[mashBatch.Id].VesselTempC.Should().Be(66.4m);
        byId[boilBatch.Id].VesselTempC.Should().Be(99.6m);
        byId[sensorBatch.Id].VesselTempC.Should().Be(20.8m);
        byId[sensorBatch.Id].CurrentGravity.Should().Be(1.016m);
        byId[sensorBatch.Id].FermentationSensorId.Should().Be(ispindel.Id);
        byId[sensorBatch.Id].FermentationSensorName.Should().Be("iSpindel Red");
        byId[silentSensorBatch.Id].VesselTempC.Should().BeNull();
        byId[silentSensorBatch.Id].CurrentGravity.Should().BeNull();
        byId[silentSensorBatch.Id].FermentationSensorId.Should().Be(silentSensor.Id);
        byId[manualReadingBatch.Id].VesselTempC.Should().Be(21.3m);
        byId[manualReadingBatch.Id].CurrentGravity.Should().Be(1.020m);
        byId[liveFermentBatch.Id].VesselTempC.Should().Be(19.2m);
        byId[liveFermentBatch.Id].CurrentGravity.Should().Be(1.032m);
        byId[liveFermentBatch.Id].FermentationSensorId.Should().BeNull();
        byId[stageIsolatedBatch.Id].VesselTempC.Should().BeNull();
        byId[completedBatch.Id].VesselTempC.Should().Be(4.5m);
        byId[plannedBatch.Id].VesselTempC.Should().BeNull();
    }

    [Fact]
    public async Task MultiDayLiveBatch_OnDayThreeAndFive_PreservesOccupancyMovesVolumesAndDecouplesKegs_WhileBoilerFreesAtFerment()
    {
        var startUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(startUtc);
        var broadcast = new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance);
        var service = new BatchService(_db, broadcast, NullLogger<BatchService>.Instance, timeProvider: clock);

        var ownerId = $"multiday_owner_{Guid.NewGuid():N}";
        var collabId = $"multiday_collab_{Guid.NewGuid():N}";
        var setup = new BrewerySetup
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            Name = "Shared Multi-Day Rig",
            IsDefault = true,
            Members = [new BreweryMember { UserId = collabId, Role = BreweryRole.Brewer }]
        };
        var boiler = new Equipment { Id = Guid.NewGuid(), UserId = ownerId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Boiler 1", Type = EquipmentType.Boiler, Subtype = EquipmentSubtype.AllInOne, Capacity = 40m, CapacityLiters = 40m, Unit = VolumeUnit.Liters };
        var fermA = new Equipment { Id = Guid.NewGuid(), UserId = ownerId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Fermenter A", Type = EquipmentType.Fermenter, Capacity = 30m, CapacityLiters = 30m, Unit = VolumeUnit.Liters };
        var fermB = new Equipment { Id = Guid.NewGuid(), UserId = ownerId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Fermenter B", Type = EquipmentType.Fermenter, Capacity = 30m, CapacityLiters = 30m, Unit = VolumeUnit.Liters };
        var keg1 = new Equipment { Id = Guid.NewGuid(), UserId = ownerId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Keg 1", Type = EquipmentType.Keg, Capacity = 19m, CapacityLiters = 19m, Unit = VolumeUnit.Liters };
        var keg2 = new Equipment { Id = Guid.NewGuid(), UserId = ownerId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Keg 2", Type = EquipmentType.Keg, Capacity = 19m, CapacityLiters = 19m, Unit = VolumeUnit.Liters };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(boiler, fermA, fermB, keg1, keg2);
        await _db.SaveChangesAsync();

        // Day 0: Start live batch in Mash
        var (createRes, created, err) = await service.CreateBatchAsync(new CreateBatchRequest(
            null, "B-MD-01", "Multi-Day IPA", "IPA", DateOnly.FromDateTime(startUtc.UtcDateTime),
            20m, 1.055m, 1.012m, 5.6m, 45m, 7m, 60, 75m, boiler.Id, fermA.Id), ownerId);
        createRes.Should().Be(BatchAccessResult.Success, err);
        created!.IsHistorical.Should().BeFalse();

        // Advance Mash -> Boil -> Ferment on Day 0
        (await service.AdvanceStageAsync(created.Id, new AdvanceBatchStageRequest(BrewStage.Boil), ownerId)).Result
            .Should().Be(BatchAccessResult.Success);
        (await service.AdvanceStageAsync(created.Id, new AdvanceBatchStageRequest(BrewStage.Ferment, MeasuredOg: 1.056m, MeasuredBatchSizeLiters: 20.5m), ownerId)).Result
            .Should().Be(BatchAccessResult.Success);

        // Stage-aware occupancy: Boiler is now FREE and can be claimed by another live batch while Batch 1 ferments
        boiler.CurrentVolumeLiters.Should().Be(0m);
        fermA.CurrentVolumeLiters.Should().Be(20.5m);
        var (secondBoilerRes, _, secondErr) = await service.CreateBatchAsync(new CreateBatchRequest(
            null, "B-MD-02", "Second Brew On Freed Boiler", "Pilsner", DateOnly.FromDateTime(startUtc.UtcDateTime),
            20m, 1.048m, 1.010m, 5.0m, 30m, 4m, 60, 75m, boiler.Id, null), collabId);
        secondBoilerRes.Should().Be(BatchAccessResult.Success, secondErr);

        // Advance clock to Day 3 (past UTC midnight; BrewDate is 3 days earlier)
        clock.Advance(TimeSpan.FromDays(3));

        // Collaborator trying to claim Fermenter A on Day 3 must hit 409 Conflict
        var (collabFermRes, _, _) = await service.CreateBatchAsync(new CreateBatchRequest(
            null, "B-MD-03", "Conflicting Batch", "Stout", DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            20m, 1.055m, 1.014m, 5.4m, 35m, 30m, 60, 75m, null, fermA.Id), collabId);
        collabFermRes.Should().Be(BatchAccessResult.Success); // Mash doesn't claim fermenter yet
        var collabBatchId = (await _db.Batches.FirstAsync(b => b.BatchCode == "B-MD-03")).Id;
        var (collabAdvRes, _, _) = await service.AdvanceStageAsync(collabBatchId, new AdvanceBatchStageRequest(BrewStage.Ferment), collabId);
        collabAdvRes.Should().Be(BatchAccessResult.Conflict);

        // Day 3: Swap Fermenter A -> Fermenter B on Batch 1 moves live volume
        var (swapRes, _, swapErr) = await service.UpdateBatchAsync(created.Id, new UpdateBatchRequest(FermenterId: fermB.Id), ownerId);
        swapRes.Should().Be(BatchAccessResult.Success, swapErr);
        fermA.CurrentVolumeLiters.Should().Be(0m);
        fermB.CurrentVolumeLiters.Should().Be(20.5m);

        // Day 4: Package into Keg 1 fills keg and sets AssignedBatchId
        clock.Advance(TimeSpan.FromDays(1));
        var (pkgRes, _, pkgErr) = await service.AdvanceStageAsync(created.Id, new AdvanceBatchStageRequest(
            BrewStage.Package, MeasuredFg: 1.011m, PackagingVesselId: keg1.Id, MeasuredPackagedVolumeLiters: 18.5m), ownerId);
        pkgRes.Should().Be(BatchAccessResult.Success, pkgErr);
        fermB.CurrentVolumeLiters.Should().Be(0m);
        keg1.CurrentVolumeLiters.Should().Be(18.5m);
        keg1.AssignedBatchId.Should().Be(created.Id);

        // Day 5: Swap Keg 1 -> Keg 2 unconditionally decouples Keg 1 and assigns Keg 2
        clock.Advance(TimeSpan.FromDays(1));
        var (kegSwapRes, _, kegSwapErr) = await service.UpdateBatchAsync(created.Id, new UpdateBatchRequest(
            PackagingVesselId: keg2.Id, MeasuredPackagedVolumeLiters: 18.0m), ownerId);
        kegSwapRes.Should().Be(BatchAccessResult.Success, kegSwapErr);
        keg1.AssignedBatchId.Should().BeNull();
        keg1.CurrentVolumeLiters.Should().Be(0m);
        keg2.AssignedBatchId.Should().Be(created.Id);
        keg2.CurrentVolumeLiters.Should().Be(18.0m);
    }

    [Fact]
    public async Task HistoricalBatch_GuardsLiveToggleAndEnforcesRevivalOccupancyChecks()
    {
        var userId = $"hist_owner_{Guid.NewGuid():N}";
        var otherUserId = $"hist_other_{Guid.NewGuid():N}";
        var setup = new BrewerySetup
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Historical Guard Rig",
            IsDefault = true,
            Members = [new BreweryMember { UserId = otherUserId, Role = BreweryRole.Brewer }]
        };
        var fermenter = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "FV-1", Type = EquipmentType.Fermenter, Capacity = 30m, CapacityLiters = 30m, Unit = VolumeUnit.Liters };
        var sensor = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Tilt Blue", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.Tilt };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(fermenter, sensor);
        await _db.SaveChangesAsync();

        // 1. Live batch occupies Fermenter + Sensor in Ferment stage
        var (_, liveBatch, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-HIST-LIVE", "Live Batch", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, fermenter.Id,
            SensorAssignments: [new BatchSensorAssignmentInput(sensor.Id, BrewStage.Ferment, null)]), userId);
        await _batchService.AdvanceStageAsync(liveBatch!.Id, new AdvanceBatchStageRequest(BrewStage.Ferment), userId);

        // Cannot flip an active live batch directly to historical without completing/archiving/planning first
        var (flipLiveRes, _, _) = await _batchService.UpdateBatchAsync(liveBatch.Id, new UpdateBatchRequest(IsHistorical: true), userId);
        flipLiveRes.Should().Be(BatchAccessResult.Conflict);

        // 2. Historical batch using the same occupied fermenter & sensor succeeds without mutating live volume
        var (histCreateRes, histBatch, histErr) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-HIST-OLD", "Historical Log", "Porter", new DateOnly(2025, 3, 1),
            18m, 1.054m, 1.014m, 5.2m, 30m, 25m, 60, 75m, null, fermenter.Id,
            SensorAssignments: [new BatchSensorAssignmentInput(sensor.Id, BrewStage.Ferment, null)],
            IsHistorical: true), otherUserId);
        histCreateRes.Should().Be(BatchAccessResult.Success, histErr);
        histBatch!.IsHistorical.Should().BeTrue();
        fermenter.CurrentVolumeLiters.Should().Be(20m);

        // Move historical batch to Ferment (still historical, no conflict)
        (await _batchService.AdvanceStageAsync(histBatch.Id, new AdvanceBatchStageRequest(BrewStage.Ferment), otherUserId)).Result
            .Should().Be(BatchAccessResult.Success);

        // 3. Reviving historical batch while live batch holds sensor/fermenter returns Conflict
        var (reviveConflictRes, _, _) = await _batchService.UpdateBatchAsync(histBatch.Id, new UpdateBatchRequest(IsHistorical: false), otherUserId);
        reviveConflictRes.Should().Be(BatchAccessResult.Conflict);

        // 4. Complete the live batch (which frees fermenter & sensor), then revival succeeds and fills fermenter
        (await _batchService.UpdateBatchAsync(liveBatch.Id, new UpdateBatchRequest(Status: BatchStatus.Completed), userId)).Result
            .Should().Be(BatchAccessResult.Success);

        var (reviveOkRes, revived, reviveErr) = await _batchService.UpdateBatchAsync(histBatch.Id, new UpdateBatchRequest(IsHistorical: false), otherUserId);
        reviveOkRes.Should().Be(BatchAccessResult.Success, reviveErr);
        revived!.IsHistorical.Should().BeFalse();
        fermenter.CurrentVolumeLiters.Should().Be(18m);
    }

    [Fact]
    public void BatchLifecycle_ActiveStageVesselIds_RespectsStageStatusAndHistorical()
    {
        var hltId = Guid.NewGuid();
        var mashTunId = Guid.NewGuid();
        var boilerId = Guid.NewGuid();
        var fermId = Guid.NewGuid();
        var pkgId = Guid.NewGuid();

        var batch = new Batch
        {
            UserId = "u1",
            BatchCode = "B-LC-1",
            Name = "Lifecycle Test",
            Status = BatchStatus.Brewing,
            CurrentStage = BrewStage.Mash,
            HltId = hltId,
            MashTunId = mashTunId,
            BoilerId = boilerId,
            FermenterId = fermId,
            PackagingVesselId = pkgId
        };

        BatchLifecycle.ActiveStageVesselIds(batch).Should().BeEquivalentTo([hltId, mashTunId, boilerId]);

        batch.CurrentStage = BrewStage.Boil;
        BatchLifecycle.ActiveStageVesselIds(batch).Should().BeEquivalentTo([boilerId]);

        batch.Status = BatchStatus.Fermenting;
        batch.CurrentStage = BrewStage.Ferment;
        BatchLifecycle.ActiveStageVesselIds(batch).Should().BeEquivalentTo([fermId]);

        batch.Status = BatchStatus.Conditioning;
        batch.CurrentStage = BrewStage.Condition;
        BatchLifecycle.ActiveStageVesselIds(batch).Should().BeEquivalentTo([fermId]);

        batch.Status = BatchStatus.Planned;
        BatchLifecycle.OccupiesLiveEquipment(batch).Should().BeFalse();
        BatchLifecycle.ActiveStageVesselIds(batch).Should().BeEmpty();

        batch.Status = BatchStatus.Fermenting;
        batch.IsHistorical = true;
        BatchLifecycle.OccupiesLiveEquipment(batch).Should().BeFalse();
        BatchLifecycle.ActiveStageVesselIds(batch).Should().BeEmpty();
    }

    [Fact]
    public async Task StageTransitionMatrix_UpdateBatchAsyncAndAdvanceStageAsync_ProduceIdenticalVolumesStageAndHistory()
    {
        var userId = $"matrix_user_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Matrix Rig", IsDefault = true };
        _db.BrewerySetups.Add(setup);

        Equipment MakeEq(string name, EquipmentType type, decimal cap) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BrewerySetup = setup,
            Name = name,
            Type = type,
            Subtype = type == EquipmentType.Boiler ? EquipmentSubtype.AllInOne : EquipmentService.ResolveDefaultSubtype(type, null),
            Capacity = cap,
            CapacityLiters = cap,
            Unit = VolumeUnit.Liters
        };

        // Two identical sets of vessels so AdvanceStageAsync and UpdateBatchAsync can run in parallel paths
        var boilerAdv = MakeEq("Boiler Adv", EquipmentType.Boiler, 40m);
        var fermAdv = MakeEq("Ferm Adv", EquipmentType.Fermenter, 30m);
        var kegAdv = MakeEq("Keg Adv", EquipmentType.Keg, 19m);

        var boilerUpd = MakeEq("Boiler Upd", EquipmentType.Boiler, 40m);
        var fermUpd = MakeEq("Ferm Upd", EquipmentType.Fermenter, 30m);
        var kegUpd = MakeEq("Keg Upd", EquipmentType.Keg, 19m);

        _db.Equipment.AddRange(boilerAdv, fermAdv, kegAdv, boilerUpd, fermUpd, kegUpd);
        await _db.SaveChangesAsync();

        var (_, batchAdv, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-MX-ADV", "Advance Path", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.055m, 1.012m, 5.6m, 40m, 7m, 60, 75m, boilerAdv.Id, fermAdv.Id, PackagingVesselId: kegAdv.Id), userId);
        var (_, batchUpd, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-MX-UPD", "Update Path", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.055m, 1.012m, 5.6m, 40m, 7m, 60, 75m, boilerUpd.Id, fermUpd.Id, PackagingVesselId: kegUpd.Id), userId);

        // 1. Brewing -> Fermenting
        var (_, advFerm, _) = await _batchService.AdvanceStageAsync(batchAdv!.Id, new AdvanceBatchStageRequest(BrewStage.Ferment), userId);
        var (_, updFerm, _) = await _batchService.UpdateBatchAsync(batchUpd!.Id, new UpdateBatchRequest(Status: BatchStatus.Fermenting), userId);

        updFerm!.Status.Should().Be(advFerm!.Status);
        updFerm.CurrentStage.Should().Be(advFerm.CurrentStage);
        boilerUpd.CurrentVolumeLiters.Should().Be(boilerAdv.CurrentVolumeLiters).And.Be(0m);
        fermUpd.CurrentVolumeLiters.Should().Be(fermAdv.CurrentVolumeLiters).And.Be(20m);
        updFerm.StageHistory.Select(h => (h.Stage, h.Status)).Should().BeEquivalentTo(advFerm.StageHistory.Select(h => (h.Stage, h.Status)));

        // 2. Fermenting -> Conditioning
        var (_, advCond, _) = await _batchService.AdvanceStageAsync(batchAdv.Id, new AdvanceBatchStageRequest(BrewStage.Condition), userId);
        var (_, updCond, _) = await _batchService.UpdateBatchAsync(batchUpd.Id, new UpdateBatchRequest(Status: BatchStatus.Conditioning), userId);

        updCond!.Status.Should().Be(advCond!.Status);
        updCond.CurrentStage.Should().Be(advCond.CurrentStage);
        fermUpd.CurrentVolumeLiters.Should().Be(fermAdv.CurrentVolumeLiters).And.Be(20m);
        updCond.StageHistory.Select(h => (h.Stage, h.Status)).Should().BeEquivalentTo(advCond.StageHistory.Select(h => (h.Stage, h.Status)));

        // 3. Conditioning -> Completed (Package)
        var (_, advDone, _) = await _batchService.AdvanceStageAsync(batchAdv.Id, new AdvanceBatchStageRequest(BrewStage.Package), userId);
        var (_, updDone, _) = await _batchService.UpdateBatchAsync(batchUpd.Id, new UpdateBatchRequest(Status: BatchStatus.Completed), userId);

        updDone!.Status.Should().Be(advDone!.Status).And.Be(BatchStatus.Completed);
        updDone.CurrentStage.Should().Be(advDone.CurrentStage).And.Be(BrewStage.Package);
        fermUpd.CurrentVolumeLiters.Should().Be(fermAdv.CurrentVolumeLiters).And.Be(0m);
        kegUpd.CurrentVolumeLiters.Should().Be(kegAdv.CurrentVolumeLiters).And.BeGreaterThan(0m);
        kegUpd.AssignedBatchId.Should().Be(batchUpd.Id);
        kegAdv.AssignedBatchId.Should().Be(batchAdv.Id);
        updDone.StageHistory.Select(h => (h.Stage, h.Status)).Should().BeEquivalentTo(advDone.StageHistory.Select(h => (h.Stage, h.Status)));

        // 4. Reopen Completed -> Brewing (Mash), then Brewing -> Planned resets vessel volumes
        var (reopenRes, reopened, _) = await _batchService.UpdateBatchAsync(batchUpd.Id, new UpdateBatchRequest(Status: BatchStatus.Brewing), userId);
        reopenRes.Should().Be(BatchAccessResult.Success);
        reopened!.CurrentStage.Should().Be(BrewStage.Mash);
        kegUpd.AssignedBatchId.Should().BeNull();
        kegUpd.CurrentVolumeLiters.Should().Be(0m);
        boilerUpd.CurrentVolumeLiters.Should().Be(20m);

        var (planRes, planned, _) = await _batchService.UpdateBatchAsync(batchUpd.Id, new UpdateBatchRequest(Status: BatchStatus.Planned), userId);
        planRes.Should().Be(BatchAccessResult.Success);
        planned!.Status.Should().Be(BatchStatus.Planned);
        boilerUpd.CurrentVolumeLiters.Should().Be(0m);
    }

    [Fact]
    public async Task UpdateBatchAsync_AioBoilerSwapAndVesselSwaps_SyncsMashTunRefreshesVolumeProfileClearsStaleManualAndUpdatesSetup()
    {
        var userId = $"swap_user_{Guid.NewGuid():N}";
        var setupA = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup A", IsDefault = true };
        var setupB = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Setup B", IsDefault = false };
        _db.BrewerySetups.AddRange(setupA, setupB);

        var aio1 = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setupA.Id,
            BrewerySetup = setupA,
            Name = "AIO 1",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            Capacity = 35m,
            CapacityLiters = 35m,
            BoilOffRatePerHour = 3.0m,
            TrubLossLiters = 1.5m,
            MashTunDeadSpaceLiters = 2.0m,
            CurrentTemperatureC = 66.5m,
            CurrentSpecificGravity = 1.045m,
            ConnectionType = EquipmentConnectionType.None
        };
        var aio2 = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setupB.Id,
            BrewerySetup = setupB,
            Name = "AIO 2",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            Capacity = 45m,
            CapacityLiters = 45m,
            BoilOffRatePerHour = 5.0m,
            TrubLossLiters = 3.5m,
            MashTunDeadSpaceLiters = 4.0m
        };
        var bottlingBucket1 = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setupA.Id,
            BrewerySetup = setupA,
            Name = "Bottling Bucket 1",
            Type = EquipmentType.Other,
            Subtype = EquipmentSubtype.Other,
            Capacity = 25m,
            CapacityLiters = 25m,
            PackagingLossLiters = 0.5m
        };
        var bottlingBucket2 = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setupB.Id,
            BrewerySetup = setupB,
            Name = "Bottling Bucket 2",
            Type = EquipmentType.Other,
            Subtype = EquipmentSubtype.Other,
            Capacity = 25m,
            CapacityLiters = 25m,
            PackagingLossLiters = 2.0m
        };
        _db.Equipment.AddRange(aio1, aio2, bottlingBucket1, bottlingBucket2);
        await _db.SaveChangesAsync();

        var (_, created, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-SWAP-01", "AIO Swap IPA", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.055m, 1.012m, 5.6m, 40m, 7m, 60, 75m, aio1.Id, null, PackagingVesselId: bottlingBucket1.Id), userId);

        var initialTotalWater = created!.VolumeProfile!.TotalWaterLiters;
        var initialTargetPackaged = created.VolumeProfile.TargetPackagedVolumeLiters;

        // Swap BoilerId aio1 -> aio2 during Mash without explicitly passing MashTunId
        var (swapRes, swapped, swapErr) = await _batchService.UpdateBatchAsync(created.Id, new UpdateBatchRequest(
            BoilerId: aio2.Id,
            PackagingVesselId: bottlingBucket2.Id), userId);
        swapRes.Should().Be(BatchAccessResult.Success, swapErr);

        // AIO MashTunId synced with BoilerId; aio1 emptied & stale manual temp/SG cleared (BE-M4); aio2 occupied via OccupyInitialMashVessels (BE-L1)
        swapped!.BoilerId.Should().Be(aio2.Id);
        swapped.MashTunId.Should().Be(aio2.Id);
        aio1.CurrentVolumeLiters.Should().Be(0m);
        aio1.CurrentTemperatureC.Should().BeNull();
        aio1.CurrentSpecificGravity.Should().BeNull();
        aio2.CurrentVolumeLiters.Should().Be(20m);

        // VolumeProfile losses & downstream targets refreshed (BE-H4)
        swapped.VolumeProfile!.BoilOffRatePerHour.Should().Be(5.0m);
        swapped.VolumeProfile.KettleTrubLossLiters.Should().Be(3.5m);
        swapped.VolumeProfile.PackagingLossLiters.Should().Be(2.0m);
        swapped.VolumeProfile.TotalWaterLiters.Should().BeGreaterThan(initialTotalWater);
        swapped.VolumeProfile.TargetPackagedVolumeLiters.Should().BeLessThan(initialTargetPackaged);

        // Batch.BrewerySetupId updated to setupB (BE-H4)
        var dbBatch = await _db.Batches.FindAsync(created.Id);
        dbBatch!.BrewerySetupId.Should().Be(setupB.Id);

        // Complete into bottlingBucket2 then swap packaging vessel back to bottlingBucket1 -> non-Keg oldPackagingVessel volume resets to 0 (BE-H2)
        (await _batchService.AdvanceStageAsync(created.Id, new AdvanceBatchStageRequest(BrewStage.Package, MeasuredPackagedVolumeLiters: 17.5m), userId)).Result
            .Should().Be(BatchAccessResult.Success);
        bottlingBucket2.CurrentVolumeLiters.Should().Be(17.5m);

        (await _batchService.UpdateBatchAsync(created.Id, new UpdateBatchRequest(PackagingVesselId: bottlingBucket1.Id), userId)).Result
            .Should().Be(BatchAccessResult.Success);
        bottlingBucket2.CurrentVolumeLiters.Should().Be(0m);
        bottlingBucket1.CurrentVolumeLiters.Should().Be(17.5m);

        // BE-L2: Sensor edits on Completed batch return 409 Conflict
        var (sensorRes, _, _) = await _batchService.UpdateBatchSensorsAsync(created.Id, [], userId);
        sensorRes.Should().Be(BatchAccessResult.Conflict);
    }

    [Fact]
    public async Task SameUserOccupancy_AndAdvanceStageWithOptionalFermenterAndSensor_EnforceConsistentPolicy()
    {
        var userId = $"occ_same_{Guid.NewGuid():N}";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Single User Rig", IsDefault = true };
        var boiler = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "Main Boiler", Type = EquipmentType.Boiler, Subtype = EquipmentSubtype.AllInOne, Capacity = 40m, CapacityLiters = 40m };
        var ferm1 = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "FV 1", Type = EquipmentType.Fermenter, Capacity = 30m, CapacityLiters = 30m };
        var ferm2 = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "FV 2", Type = EquipmentType.Fermenter, Capacity = 30m, CapacityLiters = 30m };
        var sensor = new Equipment { Id = Guid.NewGuid(), UserId = userId, BrewerySetupId = setup.Id, BrewerySetup = setup, Name = "iSpindel 1", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.ISpindel };
        _db.BrewerySetups.Add(setup);
        _db.Equipment.AddRange(boiler, ferm1, ferm2, sensor);
        await _db.SaveChangesAsync();

        // Batch 1 occupies boiler in Mash
        var (res1, b1, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-SU-01", "Batch 1", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.055m, 1.012m, 5.6m, 40m, 7m, 60, 75m, boiler.Id, ferm1.Id), userId);
        res1.Should().Be(BatchAccessResult.Success);

        // Same user trying to start Batch 2 on the same active boiler gets Conflict
        var (res2Conflict, _, _) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-SU-02", "Batch 2 Conflict", "Stout", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.055m, 1.012m, 5.6m, 40m, 30m, 60, 75m, boiler.Id, ferm2.Id), userId);
        res2Conflict.Should().Be(BatchAccessResult.Conflict);

        // Advance Batch 1 to Ferment while selecting ferm2 and sensor via AdvanceBatchStageRequest (FE-M3 backend support)
        var (adv1Res, b1Ferm, adv1Err) = await _batchService.AdvanceStageAsync(b1!.Id, new AdvanceBatchStageRequest(
            BrewStage.Ferment, FermenterId: ferm2.Id, FermentationSensorId: sensor.Id), userId);
        adv1Res.Should().Be(BatchAccessResult.Success, adv1Err);
        b1Ferm!.FermenterId.Should().Be(ferm2.Id);
        b1Ferm.SensorAssignments.Should().ContainSingle(sa => sa.EquipmentId == sensor.Id && sa.Stage == BrewStage.Ferment);
        ferm2.CurrentVolumeLiters.Should().Be(20m);
        boiler.CurrentVolumeLiters.Should().Be(0m);

        // Now Boiler is FREE so same user CAN start Batch 2 on boiler
        var (res2Ok, b2, res2Err) = await _batchService.CreateBatchAsync(new CreateBatchRequest(
            null, "B-SU-02", "Batch 2", "Stout", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.055m, 1.012m, 5.6m, 40m, 30m, 60, 75m, boiler.Id, ferm2.Id), userId);
        res2Ok.Should().Be(BatchAccessResult.Success, res2Err);

        // Advancing Batch 2 into Ferment on occupied ferm2 returns Conflict for same user too
        var (adv2Conflict, _, _) = await _batchService.AdvanceStageAsync(b2!.Id, new AdvanceBatchStageRequest(BrewStage.Ferment), userId);
        adv2Conflict.Should().Be(BatchAccessResult.Conflict);
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class StubBrewLinkService(BrewLinkSendResult result, string? errorMessage) : IBrewLinkService
    {
        public Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendRecipeToEquipmentAsync(
            string userId, Guid equipmentId, SendBrewLinkRecipeRequest request, CancellationToken ct = default)
        {
            var cmd = result == BrewLinkSendResult.Success
                ? new BrewLinkCommandResultDto("ok", "req-1", "GW1", "AA:BB:CC:DD:EE:FF", "Sent")
                : null;
            return Task.FromResult((result, cmd, errorMessage));
        }

        public Task<List<BrewLinkGatewayDto>> ListGatewaysAsync(string userId, CancellationToken ct = default) => Task.FromResult<List<BrewLinkGatewayDto>>([]);
        public Task<List<BrewLinkDiscoveredGatewayDto>> DiscoverGatewaysAsync(string userId, CancellationToken ct = default) => Task.FromResult<List<BrewLinkDiscoveredGatewayDto>>([]);
        public Task<(bool Succeeded, string? ErrorMessage)> StartPairingWindowAsync(string userId, StartBrewLinkPairingRequest request, CancellationToken ct = default) => Task.FromResult((false, (string?)null));
        public Task<(bool Succeeded, BrewLinkGatewayDto? Gateway, string? ErrorMessage)> PairGatewayAsync(string userId, PairBrewLinkRequest request, CancellationToken ct = default) => Task.FromResult((false, (BrewLinkGatewayDto?)null, (string?)null));
        public Task<(bool Succeeded, BrewLinkGatewayDto? Gateway, string? ErrorMessage)> UpdateGatewayAsync(string userId, Guid id, UpdateBrewLinkGatewayRequest request, CancellationToken ct = default) => Task.FromResult((false, (BrewLinkGatewayDto?)null, (string?)null));
        public Task<(bool Succeeded, bool Conflict, string? ErrorMessage)> DeleteGatewayAsync(string userId, Guid id, bool force = false, CancellationToken ct = default) => Task.FromResult((false, false, (string?)null));
        public Task<(bool Succeeded, List<BrewLinkEquipmentDeviceDto>? Equipment, string? ErrorMessage)> GetGatewayEquipmentAsync(string userId, Guid gatewayId, Guid? setupId = null, CancellationToken ct = default) => Task.FromResult((false, (List<BrewLinkEquipmentDeviceDto>?)null, (string?)null));
        public Task<(BrewLinkSendResult Result, ProvisionBrewLinkEquipmentResultDto? Data, string? ErrorMessage)> ProvisionGatewayEquipmentAsync(string userId, Guid gatewayId, ProvisionBrewLinkEquipmentRequest request, CancellationToken ct = default) => Task.FromResult((BrewLinkSendResult.BadRequest, (ProvisionBrewLinkEquipmentResultDto?)null, (string?)null));
        public Task RevokeGatewayTokenBestEffortAsync(BrewLinkGateway gateway, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendControlCommandAsync(string userId, Guid equipmentId, BrewLinkControlRequest request, CancellationToken ct = default) => Task.FromResult((BrewLinkSendResult.BadRequest, (BrewLinkCommandResultDto?)null, (string?)null));
        public Task<(BrewLinkSendResult Result, BrewLinkSyncResultDto? SyncResult, string? ErrorMessage)> SyncEquipmentStateAsync(string userId, Guid equipmentId, CancellationToken ct = default) => Task.FromResult((BrewLinkSendResult.BadRequest, (BrewLinkSyncResultDto?)null, (string?)null));
    }
}