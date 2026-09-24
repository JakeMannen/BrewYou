using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;

namespace BrewYou.ApiService.Services;

public enum BatchAccessResult
{
    Success,
    NotFound,
    Forbidden,
    Conflict,
    ValidationFailed,
    QuotaExceeded
}

public interface IBatchService
{
    Task<(List<BatchSummaryDto> Items, PaginationMeta Pagination)> GetBatchesAsync(
        string userId, BatchStatus? status, string? search, int page, int limit);

    Task<(BatchAccessResult Result, BatchDetailDto? Batch)> GetBatchByIdAsync(Guid id, string userId);

    Task<string> GetNextBatchCodeAsync(string userId, DateOnly? date = null);

    Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> CreateBatchAsync(
        CreateBatchRequest request, string userId);

    Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> AdvanceStageAsync(
        Guid id, AdvanceBatchStageRequest request, string userId);

    Task<(BatchAccessResult Result, BatchReadingDto? Reading, string? ErrorMessage)> AddReadingAsync(
        Guid id, AddBatchReadingRequest request, string userId);

    Task<(BatchAccessResult Result, BatchIngredientDto? Ingredient, string? ErrorMessage)> ToggleIngredientAsync(
        Guid batchId, Guid ingredientId, bool isChecked, string userId);

    Task<(BatchAccessResult Result, BatchMashStepDto? Step, string? ErrorMessage)> ToggleMashStepAsync(
        Guid batchId, Guid stepId, ToggleBatchMashStepRequest request, string userId);

    Task<(BatchAccessResult Result, BatchFermentationStepDto? Step, string? ErrorMessage)> ToggleFermentationStepAsync(
        Guid batchId, Guid stepId, ToggleBatchFermentationStepRequest request, string userId);

    Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> UpdateBatchAsync(
        Guid id, UpdateBatchRequest request, string userId);

    Task<BatchAccessResult> DeleteBatchAsync(Guid id, string userId);

    Task<(BatchAccessResult Result, BatchEquipmentReadingDto? Reading, string? ErrorMessage)> LogTemperatureReadingAsync(
        Guid batchId, LogBatchTemperatureRequest request, string userId);

    Task<(BatchAccessResult Result, List<BatchSensorAssignmentDto>? Sensors, string? ErrorMessage)> UpdateBatchSensorsAsync(
        Guid batchId, List<BatchSensorAssignmentInput> sensorInputs, string userId);

    Task<(BatchAccessResult Result, List<BatchEquipmentReadingDto>? Readings, string? ErrorMessage)> GetBatchEquipmentReadingsAsync(
        Guid batchId, string userId, BrewStage? stage = null, Guid? stepId = null);

    Task<(BatchAccessResult Result, BatchStockCheckResult? CheckResult, string? ErrorMessage)> CheckRecipeStockAsync(
        Guid recipeId,
        decimal targetBatchSizeLiters,
        string userId,
        TargetVolumeBasis? targetBasis = null,
        decimal? fermenterLossLiters = null,
        decimal? packagingLossLiters = null);
}