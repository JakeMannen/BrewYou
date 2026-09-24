using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

public class BrewerySetupService : IBrewerySetupService
{
    private const int MaxSetupsPerUser = 50;

    private readonly BrewYouDbContext _db;
    private readonly ILogger<BrewerySetupService> _logger;

    public BrewerySetupService(BrewYouDbContext db, ILogger<BrewerySetupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<BrewerySetupDto>> GetSetupsAsync(string userId)
    {
        var setups = await _db.BrewerySetups
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.CreatedAt)
            .Select(s => new BrewerySetupDto(
                s.Id,
                s.Name,
                s.Description,
                s.IsDefault,
                s.Equipment.Count,
                s.DefaultGrainAbsorptionRate,
                s.DefaultBoilOffRatePerHour,
                s.DefaultKettleTrubLossLiters,
                s.DefaultFermenterLossLiters,
                s.DefaultMashTunDeadSpaceLiters,
                s.CoolingShrinkagePercent,
                s.DefaultPackagingLossLiters,
                s.CreatedAt,
                s.UpdatedAt
            ))
            .ToListAsync();

        if (setups.Count == 0)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                _logger.LogWarning("Cannot auto-provision brewery setup: user {UserId} does not exist in AspNetUsers", userId);
                return [];
            }

            var defaultName = user.PreferredLanguage?.StartsWith("sv", StringComparison.OrdinalIgnoreCase) == true
                ? "Mitt bryggeri"
                : "My brewery";

            var defaultSetup = new BrewerySetup
            {
                UserId = userId,
                Name = defaultName,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.BrewerySetups.Add(defaultSetup);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Auto-provisioned default {DefaultName} setup for user {UserId}", defaultName, userId);
            return [new BrewerySetupDto(
                defaultSetup.Id,
                defaultSetup.Name,
                defaultSetup.Description,
                defaultSetup.IsDefault,
                0,
                defaultSetup.DefaultGrainAbsorptionRate,
                defaultSetup.DefaultBoilOffRatePerHour,
                defaultSetup.DefaultKettleTrubLossLiters,
                defaultSetup.DefaultFermenterLossLiters,
                defaultSetup.DefaultMashTunDeadSpaceLiters,
                defaultSetup.CoolingShrinkagePercent,
                defaultSetup.DefaultPackagingLossLiters,
                defaultSetup.CreatedAt,
                defaultSetup.UpdatedAt
            )];
        }

        return setups;
    }

    public async Task<(BrewerySetupAccessResult Result, BrewerySetupDto? Setup)> GetSetupByIdAsync(Guid id, string userId)
    {
        var setup = await _db.BrewerySetups
            .AsNoTracking()
            .Where(s => s.Id == id && s.UserId == userId)
            .Select(s => new BrewerySetupDto(
                s.Id,
                s.Name,
                s.Description,
                s.IsDefault,
                s.Equipment.Count,
                s.DefaultGrainAbsorptionRate,
                s.DefaultBoilOffRatePerHour,
                s.DefaultKettleTrubLossLiters,
                s.DefaultFermenterLossLiters,
                s.DefaultMashTunDeadSpaceLiters,
                s.CoolingShrinkagePercent,
                s.DefaultPackagingLossLiters,
                s.CreatedAt,
                s.UpdatedAt
            ))
            .FirstOrDefaultAsync();

        if (setup == null)
        {
            return (BrewerySetupAccessResult.NotFound, null);
        }

        return (BrewerySetupAccessResult.Success, setup);
    }

    public async Task<(BrewerySetupAccessResult Result, BrewerySetupDto? Setup, string? ErrorMessage)> CreateSetupAsync(
        CreateBrewerySetupRequest request, string userId)
    {
        var count = await _db.BrewerySetups.CountAsync(s => s.UserId == userId);
        if (count >= MaxSetupsPerUser)
        {
            return (BrewerySetupAccessResult.QuotaExceeded, null, $"Brewery setup limit reached (max {MaxSetupsPerUser} setups).");
        }

        bool isDefault = request.IsDefault || count == 0;

        if (isDefault)
        {
            var existingDefaults = await _db.BrewerySetups
                .Where(s => s.UserId == userId && s.IsDefault)
                .ToListAsync();

            foreach (var d in existingDefaults)
            {
                d.IsDefault = false;
                d.UpdatedAt = DateTime.UtcNow;
            }
        }

        var name = request.Name.Trim();
        if (name.Length > 100) name = name[..100];
        var description = request.Description?.Trim();
        if (description?.Length > 500) description = description[..500];

        var setup = new BrewerySetup
        {
            UserId = userId,
            Name = name,
            Description = description,
            IsDefault = isDefault,
            DefaultGrainAbsorptionRate = request.DefaultGrainAbsorptionRate ?? 0.96m,
            DefaultBoilOffRatePerHour = request.DefaultBoilOffRatePerHour ?? 3.0m,
            DefaultKettleTrubLossLiters = request.DefaultKettleTrubLossLiters ?? 1.5m,
            DefaultFermenterLossLiters = request.DefaultFermenterLossLiters ?? 1.5m,
            DefaultMashTunDeadSpaceLiters = request.DefaultMashTunDeadSpaceLiters ?? 0.0m,
            CoolingShrinkagePercent = request.CoolingShrinkagePercent ?? 4.0m,
            DefaultPackagingLossLiters = request.DefaultPackagingLossLiters ?? 0.5m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.BrewerySetups.Add(setup);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Brewery setup {SetupId} ('{SetupName}') created for user {UserId}", setup.Id, setup.Name, userId);

        var dto = new BrewerySetupDto(setup.Id, setup.Name, setup.Description, setup.IsDefault, 0,
            setup.DefaultGrainAbsorptionRate, setup.DefaultBoilOffRatePerHour, setup.DefaultKettleTrubLossLiters,
            setup.DefaultFermenterLossLiters, setup.DefaultMashTunDeadSpaceLiters, setup.CoolingShrinkagePercent,
            setup.DefaultPackagingLossLiters, setup.CreatedAt, setup.UpdatedAt);
        return (BrewerySetupAccessResult.Success, dto, null);
    }

    public async Task<(BrewerySetupAccessResult Result, BrewerySetupDto? Setup, string? ErrorMessage)> UpdateSetupAsync(
        Guid id, UpdateBrewerySetupRequest request, string userId)
    {
        var setup = await _db.BrewerySetups
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (setup == null)
        {
            return (BrewerySetupAccessResult.NotFound, null, "Brewery setup not found.");
        }

        var name = request.Name.Trim();
        if (name.Length > 100) name = name[..100];
        setup.Name = name;

        var description = request.Description?.Trim();
        if (description?.Length > 500) description = description[..500];
        setup.Description = description;

        if (request.DefaultGrainAbsorptionRate.HasValue) setup.DefaultGrainAbsorptionRate = request.DefaultGrainAbsorptionRate.Value;
        if (request.DefaultBoilOffRatePerHour.HasValue) setup.DefaultBoilOffRatePerHour = request.DefaultBoilOffRatePerHour.Value;
        if (request.DefaultKettleTrubLossLiters.HasValue) setup.DefaultKettleTrubLossLiters = request.DefaultKettleTrubLossLiters.Value;
        if (request.DefaultFermenterLossLiters.HasValue) setup.DefaultFermenterLossLiters = request.DefaultFermenterLossLiters.Value;
        if (request.DefaultMashTunDeadSpaceLiters.HasValue) setup.DefaultMashTunDeadSpaceLiters = request.DefaultMashTunDeadSpaceLiters.Value;
        if (request.CoolingShrinkagePercent.HasValue) setup.CoolingShrinkagePercent = request.CoolingShrinkagePercent.Value;
        if (request.DefaultPackagingLossLiters.HasValue) setup.DefaultPackagingLossLiters = request.DefaultPackagingLossLiters.Value;

        if (request.IsDefault.HasValue && request.IsDefault.Value && !setup.IsDefault)
        {
            var existingDefaults = await _db.BrewerySetups
                .Where(s => s.UserId == userId && s.IsDefault && s.Id != id)
                .ToListAsync();

            foreach (var d in existingDefaults)
            {
                d.IsDefault = false;
                d.UpdatedAt = DateTime.UtcNow;
            }

            setup.IsDefault = true;
        }

        setup.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var equipmentCount = await _db.Equipment.CountAsync(e => e.BrewerySetupId == id);
        var dto = new BrewerySetupDto(
            setup.Id,
            setup.Name,
            setup.Description,
            setup.IsDefault,
            equipmentCount,
            setup.DefaultGrainAbsorptionRate,
            setup.DefaultBoilOffRatePerHour,
            setup.DefaultKettleTrubLossLiters,
            setup.DefaultFermenterLossLiters,
            setup.DefaultMashTunDeadSpaceLiters,
            setup.CoolingShrinkagePercent,
            setup.DefaultPackagingLossLiters,
            setup.CreatedAt,
            setup.UpdatedAt);

        _logger.LogInformation("Brewery setup {SetupId} updated for user {UserId}", setup.Id, userId);
        return (BrewerySetupAccessResult.Success, dto, null);
    }

    public async Task<(BrewerySetupAccessResult Result, string? ErrorMessage)> DeleteSetupAsync(Guid id, string userId)
    {
        var setup = await _db.BrewerySetups
            .Include(s => s.Equipment)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (setup == null)
        {
            return (BrewerySetupAccessResult.NotFound, "Brewery setup not found.");
        }

        var totalSetups = await _db.BrewerySetups.CountAsync(s => s.UserId == userId);
        if (totalSetups <= 1)
        {
            return (BrewerySetupAccessResult.CannotDeleteLastSetup, "Cannot delete the last remaining brewery setup.");
        }

        bool wasDefault = setup.IsDefault;
        _db.BrewerySetups.Remove(setup);

        if (wasDefault)
        {
            var nextDefault = await _db.BrewerySetups
                .Where(s => s.UserId == userId && s.Id != id)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (nextDefault != null)
            {
                nextDefault.IsDefault = true;
                nextDefault.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Brewery setup {SetupId} deleted for user {UserId}", id, userId);
        return (BrewerySetupAccessResult.Success, null);
    }

    public async Task<(BrewerySetupAccessResult Result, BrewerySetupDto? Setup, string? ErrorMessage)> SetDefaultSetupAsync(
        Guid id, string userId)
    {
        var setup = await _db.BrewerySetups
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (setup == null)
        {
            return (BrewerySetupAccessResult.NotFound, null, "Brewery setup not found.");
        }

        if (!setup.IsDefault)
        {
            var otherDefaults = await _db.BrewerySetups
                .Where(s => s.UserId == userId && s.IsDefault && s.Id != id)
                .ToListAsync();

            foreach (var d in otherDefaults)
            {
                d.IsDefault = false;
                d.UpdatedAt = DateTime.UtcNow;
            }

            setup.IsDefault = true;
            setup.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var equipmentCount = await _db.Equipment.CountAsync(e => e.BrewerySetupId == id);
        var dto = new BrewerySetupDto(
            setup.Id,
            setup.Name,
            setup.Description,
            setup.IsDefault,
            equipmentCount,
            setup.DefaultGrainAbsorptionRate,
            setup.DefaultBoilOffRatePerHour,
            setup.DefaultKettleTrubLossLiters,
            setup.DefaultFermenterLossLiters,
            setup.DefaultMashTunDeadSpaceLiters,
            setup.CoolingShrinkagePercent,
            setup.DefaultPackagingLossLiters,
            setup.CreatedAt,
            setup.UpdatedAt);

        return (BrewerySetupAccessResult.Success, dto, null);
    }
}