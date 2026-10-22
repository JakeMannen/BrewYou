using BrewYou.ApiService.Backup.Models;
using BrewYou.ApiService.Backup.Security;
using BrewYou.ApiService.Backup.Versioning;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrewYou.ApiService.Services;

public interface IBackupService
{
    Task<BrewYouBackupEnvelope> ExportUserBackupAsync(string userId, ExportBackupRequest request);
    Task<RestorePreviewReportDto> PreviewRestoreAsync(string userId, PreviewRestoreRequest request);
    Task<(bool Succeeded, RestoreResultDto? Result, string? ErrorCode, string? ErrorMessage)> ExecuteRestoreAsync(
        string userId, ExecuteRestoreRequest request);
}

public class BackupService : IBackupService
{
    private const int MaxExportedTelemetryReadings = 5000;

    private readonly BrewYouDbContext _db;
    private readonly BackupMigrationPipeline _migrationPipeline;
    private readonly IDataProtector? _protector;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        BrewYouDbContext db,
        ILogger<BackupService> logger,
        IDataProtectionProvider? dataProtectionProvider = null,
        BackupMigrationPipeline? migrationPipeline = null)
    {
        _db = db;
        _logger = logger;
        _protector = dataProtectionProvider?.CreateProtector("BrewYou.MqttCredentials");
        _migrationPipeline = migrationPipeline ?? new BackupMigrationPipeline();
    }

    public async Task<BrewYouBackupEnvelope> ExportUserBackupAsync(string userId, ExportBackupRequest request)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Authenticated user account was not found.");

        byte[]? encryptionKey = null;
        BackupEncryptionMetadataDto? encryptionMeta = null;

        var encryptSecrets = request.IncludeSecrets && !string.IsNullOrWhiteSpace(request.Passphrase);
        if (encryptSecrets)
        {
            var salt = BackupSecretProtector.GenerateSaltBase64();
            encryptionKey = BackupSecretProtector.DeriveKey(request.Passphrase!, salt);
            encryptionMeta = new BackupEncryptionMetadataDto(
                SecretsEncrypted: true,
                Algorithm: "AES-256-GCM",
                Kdf: "PBKDF2-SHA256",
                Iterations: BackupSecretProtector.DefaultIterations,
                SaltBase64: salt);
        }

        // 1. User Settings
        string? encryptedMqttPassword = null;
        if (encryptSecrets && encryptionKey != null && !string.IsNullOrWhiteSpace(user.MqttPassword))
        {
            var rawMqttPassword = BackupSecretProtector.UnprotectServerSecret(user.MqttPassword, _protector);
            if (!string.IsNullOrWhiteSpace(rawMqttPassword))
            {
                encryptedMqttPassword = BackupSecretProtector.EncryptSecret(rawMqttPassword, encryptionKey);
            }
        }

        var userSettingsDto = new UserSettingsBackupDto(
            OriginalUserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            PreferredLanguage: user.PreferredLanguage,
            PreferredVolumeUnit: user.PreferredVolumeUnit,
            PreferredWeightUnit: user.PreferredWeightUnit,
            PreferredGrainWeightUnit: user.PreferredGrainWeightUnit ?? (user.PreferredWeightUnit == WeightUnit.Imperial ? "lb" : "kg"),
            PreferredHopWeightUnit: user.PreferredHopWeightUnit ?? (user.PreferredWeightUnit == WeightUnit.Imperial ? "oz" : "g"),
            PreferredColorUnit: user.PreferredColorUnit ?? "SRM",
            PreferredCarbonationUnit: user.PreferredCarbonationUnit ?? "Volumes",
            PreferredTemperatureUnit: user.PreferredTemperatureUnit,
            PreferredGravityUnit: user.PreferredGravityUnit,
            PreferredTheme: user.PreferredTheme,
            PreferredBitternessFormula: user.PreferredBitternessFormula ?? "Tinseth",
            PreferredColorFormula: user.PreferredColorFormula ?? "Morey",
            PreferredAbvFormula: user.PreferredAbvFormula ?? "Linear",
            DefaultBatchSizeLiters: user.DefaultBatchSizeLiters,
            DefaultEfficiencyPercent: user.DefaultEfficiencyPercent,
            DefaultBoilTimeMinutes: user.DefaultBoilTimeMinutes,
            MqttHost: user.MqttHost,
            MqttPort: user.MqttPort,
            MqttUsername: user.MqttUsername,
            MqttPasswordEncrypted: encryptedMqttPassword,
            HasMqttPassword: !string.IsNullOrWhiteSpace(user.MqttPassword),
            MqttCertificate: user.MqttCertificate,
            MqttTopicPrefix: user.MqttTopicPrefix ?? "brewyou/equipment"
        );

        // 2. Custom Ingredients
        var customIngredients = await _db.Ingredients
            .AsNoTracking()
            .Where(i => !i.IsCatalogItem && i.CreatedByUserId == userId)
            .OrderBy(i => i.Name)
            .Select(i => new CustomIngredientBackupDto(
                i.Id,
                i.Name,
                i.Type,
                i.PotentialGravity,
                i.ColorSrm,
                i.AlphaAcidPercent,
                i.AttenuationPercent,
                i.Form,
                i.Description,
                i.CreatedAt))
            .ToListAsync();

        // 3. Pantry Stock
        var stocks = await _db.IngredientStocks
            .AsNoTracking()
            .Include(s => s.Ingredient)
            .Where(s => s.UserId == userId && s.Ingredient != null)
            .OrderBy(s => s.Ingredient!.Name)
            .Select(s => new IngredientStockBackupDto(
                s.Id,
                s.IngredientId,
                s.Ingredient!.Name,
                s.Ingredient.Type,
                s.Ingredient.IsCatalogItem,
                s.Amount,
                s.Unit,
                s.LowStockAlertThreshold,
                s.UpdatedAt,
                new IngredientSnapshotDto(
                    s.Ingredient.Name,
                    s.Ingredient.Type,
                    s.Ingredient.PotentialGravity,
                    s.Ingredient.ColorSrm,
                    s.Ingredient.AlphaAcidPercent,
                    s.Ingredient.AttenuationPercent,
                    s.Ingredient.Form,
                    s.Ingredient.Description)))
            .ToListAsync();

        // 4. Owned Brewery Setups & Collaboration
        var now = DateTime.UtcNow;
        var ownedSetupsEntities = await _db.BrewerySetups
            .AsNoTracking()
            .Include(s => s.Members)
                .ThenInclude(m => m.User)
            .Include(s => s.Invites)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync();

        var ownedSetups = ownedSetupsEntities.Select(s => new BrewerySetupBackupDto(
            s.Id,
            s.Name,
            s.Description,
            s.IsDefault,
            s.DefaultGrainAbsorptionRate,
            s.DefaultBoilOffRatePerHour,
            s.DefaultKettleTrubLossLiters,
            s.DefaultFermenterLossLiters,
            s.DefaultMashTunDeadSpaceLiters,
            s.CoolingShrinkagePercent,
            s.DefaultPackagingLossLiters,
            s.CreatedAt,
            s.UpdatedAt,
            s.Members
                .OrderBy(m => m.JoinedAt)
                .Select(m => new BreweryMemberBackupDto(
                    m.UserId,
                    m.User?.Email,
                    m.User?.DisplayName,
                    m.Role,
                    m.JoinedAt))
                .ToList(),
            s.Invites
                .Where(i => i.AcceptedAt == null && i.ExpiresAt > now)
                .OrderBy(i => i.CreatedAt)
                .Select(i => new BreweryInviteBackupDto(
                    i.InvitedEmail,
                    i.Role,
                    i.CreatedAt,
                    i.ExpiresAt))
                .ToList()
        )).ToList();

        var ownedSetupIds = ownedSetupsEntities.Select(s => s.Id).ToHashSet();

        // 5. Shared Setups where user is a Collaborator
        var sharedMembershipsEntities = await _db.BreweryMembers
            .AsNoTracking()
            .Include(m => m.BrewerySetup)
                .ThenInclude(s => s!.User)
            .Include(m => m.BrewerySetup)
                .ThenInclude(s => s!.Equipment)
            .Where(m => m.UserId == userId && m.BrewerySetup != null && m.BrewerySetup.UserId != userId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync();

        var sharedMemberships = sharedMembershipsEntities.Select(m =>
        {
            var s = m.BrewerySetup!;
            var isCoOwner = m.Role == BreweryRole.Owner;
            var canExportSharedSecrets = isCoOwner && encryptSecrets && encryptionKey != null;

            var eqSnapshots = s.Equipment
                .OrderBy(e => e.Name)
                .Select(e => MapEquipmentToBackupDto(e, isCoOwner && request.IncludeSecrets, encryptionKey))
                .ToList();

            return new SharedSetupMembershipBackupDto(
                OriginalSetupId: s.Id,
                SetupName: s.Name,
                OwnerUserId: s.UserId,
                OwnerEmail: isCoOwner ? s.User?.Email : MaskEmail(s.User?.Email),
                OwnerDisplayName: s.User?.DisplayName,
                MyRole: m.Role,
                JoinedAt: m.JoinedAt,
                SetupSnapshot: new BrewerySetupBackupDto(
                    s.Id,
                    s.Name,
                    s.Description,
                    IsDefault: false,
                    s.DefaultGrainAbsorptionRate,
                    s.DefaultBoilOffRatePerHour,
                    s.DefaultKettleTrubLossLiters,
                    s.DefaultFermenterLossLiters,
                    s.DefaultMashTunDeadSpaceLiters,
                    s.CoolingShrinkagePercent,
                    s.DefaultPackagingLossLiters,
                    s.CreatedAt,
                    s.UpdatedAt),
                ReferencedEquipmentSnapshots: eqSnapshots
            );
        }).ToList();

        // 6. Equipment in Owned Setups (or directly created by user)
        var equipmentEntities = await _db.Equipment
            .AsNoTracking()
            .Where(e => e.UserId == userId || ownedSetupIds.Contains(e.BrewerySetupId))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        var equipmentDtos = equipmentEntities
            .Select(e => MapEquipmentToBackupDto(e, request.IncludeSecrets, encryptionKey))
            .ToList();

        var ownedEquipmentIds = equipmentEntities.Select(e => e.Id).ToHashSet();

        // 7. Recipes (User-owned)
        var userRecipeEntities = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
            .Include(r => r.MashSteps)
            .Include(r => r.FermentationSteps)
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var recipeDtos = userRecipeEntities.Select(MapRecipeToBackupDto).ToList();
        var userRecipeIds = userRecipeEntities.Select(r => r.Id).ToHashSet();

        // 8. Batches
        var batchEntities = await _db.Batches
            .AsNoTracking()
            .Include(b => b.VolumeProfile)
            .Include(b => b.Ingredients)
            .Include(b => b.MashSteps)
            .Include(b => b.FermentationSteps)
            .Include(b => b.StageHistory)
            .Include(b => b.Readings)
            .Include(b => b.SensorAssignments)
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.BrewDate)
            .ThenBy(b => b.CreatedAt)
            .ToListAsync();

        var batchDtos = batchEntities.Select(MapBatchToBackupDto).ToList();
        var batchIds = batchEntities.Select(b => b.Id).ToHashSet();

        // 9. Referenced External Community Recipes
        var externalRecipeIds = batchEntities
            .Where(b => b.RecipeId.HasValue && !userRecipeIds.Contains(b.RecipeId.Value))
            .Select(b => b.RecipeId!.Value)
            .Distinct()
            .ToList();

        var referencedExternalRecipes = new List<RecipeBackupDto>();
        if (externalRecipeIds.Count > 0)
        {
            var extEntities = await _db.Recipes
                .AsNoTracking()
                .Include(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
                .Include(r => r.MashSteps)
                .Include(r => r.FermentationSteps)
                .Where(r => externalRecipeIds.Contains(r.Id) && r.IsPublic)
                .ToListAsync();

            referencedExternalRecipes = extEntities.Select(MapRecipeToBackupDto).ToList();
        }

        // 10. Equipment Telemetry Readings
        var equipmentReadings = new List<EquipmentReadingBackupDto>();
        if (request.IncludeTelemetryReadings && (ownedEquipmentIds.Count > 0 || batchIds.Count > 0))
        {
            var readingEntities = await _db.EquipmentReadings
                .AsNoTracking()
                .Where(er => ownedEquipmentIds.Contains(er.EquipmentId) || (er.BatchId.HasValue && batchIds.Contains(er.BatchId.Value)))
                .OrderByDescending(er => er.Timestamp)
                .Take(MaxExportedTelemetryReadings)
                .OrderBy(er => er.Timestamp)
                .ToListAsync();

            equipmentReadings = readingEntities.Select(er => new EquipmentReadingBackupDto(
                er.Id,
                er.EquipmentId,
                er.BatchId,
                er.Stage,
                er.BatchMashStepId,
                er.StepName,
                er.Timestamp,
                er.TemperatureC,
                er.SpecificGravity,
                er.PressureBar,
                er.BatteryPercent,
                er.BatteryVoltage,
                er.TiltDegrees,
                er.Rssi,
                er.MetricsJson,
                er.Source,
                er.RawPayload,
                er.Notes,
                er.CreatedAt
            )).ToList();
        }

        var payload = new BackupPayload
        {
            UserSettings = userSettingsDto,
            CustomIngredients = customIngredients,
            IngredientStocks = stocks,
            OwnedBrewerySetups = ownedSetups,
            SharedSetupMemberships = sharedMemberships,
            Equipment = equipmentDtos,
            Recipes = recipeDtos,
            ReferencedExternalRecipes = referencedExternalRecipes,
            Batches = batchDtos,
            EquipmentReadings = equipmentReadings
        };

        var serializerOptions = BackupMigrationPipeline.CreateCanonicalSerializerOptions();
        var dataElement = JsonSerializer.SerializeToElement(payload, serializerOptions);
        var checksum = BackupMigrationPipeline.ComputeChecksumSha256(dataElement);

        _logger.LogInformation(
            "Exported user backup for user {UserId}: {Setups} setups, {Equipment} equipment, {Recipes} recipes, {Batches} batches",
            userId, ownedSetups.Count, equipmentDtos.Count, recipeDtos.Count, batchDtos.Count);

        return new BrewYouBackupEnvelope(
            Format: BackupSchemaConstants.FormatIdentifier,
            SchemaVersion: BackupSchemaConstants.CurrentSchemaVersion,
            AppVersion: BackupSchemaConstants.CurrentAppVersion,
            ExportedAtUtc: DateTime.UtcNow,
            ChecksumSha256: checksum,
            Data: dataElement,
            Encryption: encryptionMeta);
    }

    public async Task<RestorePreviewReportDto> PreviewRestoreAsync(string userId, PreviewRestoreRequest request)
    {
        var options = request.Options ?? new RestoreOptionsDto();
        var envelope = request.Backup;

        bool checksumVerified = BackupMigrationPipeline.VerifyChecksum(envelope);
        var (payload, wasUpgraded, migrationNotes, warnings, errors) = _migrationPipeline.MigrateAndDeserialize(envelope);

        if (!checksumVerified)
        {
            warnings.Add("Backup checksum verification did not match. The file may have been manually edited.");
        }

        if (payload == null || errors.Count > 0)
        {
            return new RestorePreviewReportDto(
                IsValid: false,
                ChecksumVerified: checksumVerified,
                BackupSchemaVersion: envelope.SchemaVersion,
                CurrentSchemaVersion: BackupSchemaConstants.CurrentSchemaVersion,
                WasUpgraded: wasUpgraded,
                MigrationNotes: migrationNotes,
                BackupAppVersion: envelope.AppVersion,
                ExportedAtUtc: envelope.ExportedAtUtc,
                SecretsEncrypted: envelope.Encryption?.SecretsEncrypted ?? false,
                PassphraseRequired: false,
                PassphraseValid: false,
                Counts: new BackupItemCountsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                Conflicts: new BackupConflictSummaryDto(0, 0, 0, 0, 0),
                SharedSetups: [],
                Warnings: warnings,
                Errors: errors);
        }

        // Check passphrase & secret encryption status
        var (secretsEncrypted, passphraseRequired, passphraseValid, _) = ValidateAndDeriveEncryptionKey(
            envelope, payload, options.Passphrase, warnings);

        // Compute item counts
        var collaboratorCount = payload.OwnedBrewerySetups.Sum(s => s.Members?.Count ?? 0);
        var inviteCount = payload.OwnedBrewerySetups.Sum(s => s.PendingInvites?.Count ?? 0);

        var counts = new BackupItemCountsDto(
            OwnedSetups: payload.OwnedBrewerySetups.Count,
            SharedSetups: payload.SharedSetupMemberships.Count,
            Equipment: payload.Equipment.Count,
            CustomIngredients: payload.CustomIngredients.Count,
            IngredientStocks: payload.IngredientStocks.Count,
            Recipes: payload.Recipes.Count,
            Batches: payload.Batches.Count,
            TelemetryReadings: payload.EquipmentReadings.Count,
            Collaborators: collaboratorCount,
            PendingInvites: inviteCount
        );

        // Compute conflicts against current user database state
        var existingSetups = await _db.BrewerySetups
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Id, NameLower = s.Name.ToLower() })
            .ToListAsync();

        var existingEquipment = await _db.Equipment
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new { e.Id, NameLower = e.Name.ToLower() })
            .ToListAsync();

        var existingCustomIngredients = await _db.Ingredients
            .AsNoTracking()
            .Where(i => !i.IsCatalogItem && i.CreatedByUserId == userId)
            .Select(i => new { i.Id, NameLower = i.Name.ToLower(), i.Type })
            .ToListAsync();

        var existingRecipes = await _db.Recipes
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new { r.Id, NameLower = r.Name.ToLower() })
            .ToListAsync();

        var existingBatches = await _db.Batches
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => new { b.Id, CodeLower = b.BatchCode.ToLower() })
            .ToListAsync();

        int matchingSetups = payload.OwnedBrewerySetups.Count(s =>
            existingSetups.Any(es => es.Id == s.Id || es.NameLower == s.Name.Trim().ToLowerInvariant()));

        int matchingEquipment = payload.Equipment.Count(e =>
            existingEquipment.Any(ee => ee.Id == e.Id || ee.NameLower == e.Name.Trim().ToLowerInvariant()));

        int matchingIngredients = payload.CustomIngredients.Count(i =>
            existingCustomIngredients.Any(ei => ei.Id == i.Id || (ei.NameLower == i.Name.Trim().ToLowerInvariant() && ei.Type == i.Type)));

        int matchingRecipes = payload.Recipes.Count(r =>
            existingRecipes.Any(er => er.Id == r.Id || er.NameLower == r.Name.Trim().ToLowerInvariant()));

        int matchingBatches = payload.Batches.Count(b =>
            existingBatches.Any(eb => eb.Id == b.Id || eb.CodeLower == b.BatchCode.Trim().ToLowerInvariant()));

        var conflicts = new BackupConflictSummaryDto(
            MatchingSetups: matchingSetups,
            MatchingEquipment: matchingEquipment,
            MatchingCustomIngredients: matchingIngredients,
            MatchingRecipes: matchingRecipes,
            MatchingBatches: matchingBatches
        );

        // Shared setup preview
        var sharedPreviews = new List<SharedSetupPreviewItemDto>();
        foreach (var shared in payload.SharedSetupMemberships)
        {
            var liveSetupExists = await _db.BrewerySetups
                .AsNoTracking()
                .AnyAsync(s => s.Id == shared.OriginalSetupId &&
                               (s.UserId == userId || s.Members.Any(m => m.UserId == userId)));

            string plannedAction = !options.RestoreSharedSetups
                ? "Skip"
                : liveSetupExists
                    ? "RelinkToLiveSetup"
                    : options.SharedSetupFallback == SharedSetupFallbackMode.CloneAsPersonal
                        ? "CloneAsPersonalSetup"
                        : "DetachReferences";

            sharedPreviews.Add(new SharedSetupPreviewItemDto(
                shared.OriginalSetupId,
                shared.SetupName,
                shared.OwnerDisplayName,
                shared.MyRole,
                liveSetupExists,
                plannedAction));
        }

        return new RestorePreviewReportDto(
            IsValid: errors.Count == 0,
            ChecksumVerified: checksumVerified,
            BackupSchemaVersion: envelope.SchemaVersion,
            CurrentSchemaVersion: BackupSchemaConstants.CurrentSchemaVersion,
            WasUpgraded: wasUpgraded,
            MigrationNotes: migrationNotes,
            BackupAppVersion: envelope.AppVersion,
            ExportedAtUtc: envelope.ExportedAtUtc,
            SecretsEncrypted: secretsEncrypted,
            PassphraseRequired: passphraseRequired,
            PassphraseValid: passphraseValid,
            Counts: counts,
            Conflicts: conflicts,
            SharedSetups: sharedPreviews,
            Warnings: warnings,
            Errors: errors);
    }

    public async Task<(bool Succeeded, RestoreResultDto? Result, string? ErrorCode, string? ErrorMessage)> ExecuteRestoreAsync(
        string userId, ExecuteRestoreRequest request)
    {
        var options = request.Options ?? new RestoreOptionsDto();
        var envelope = request.Backup;

        var (payload, _, migrationNotes, warnings, errors) = _migrationPipeline.MigrateAndDeserialize(envelope);
        if (payload == null || errors.Count > 0)
        {
            return (false, null, "INVALID_BACKUP_PAYLOAD", string.Join(" ", errors));
        }

        var (secretsEncrypted, passphraseRequired, passphraseValid, encryptionKey) = ValidateAndDeriveEncryptionKey(
            envelope, payload, options.Passphrase, warnings);

        if (secretsEncrypted && !string.IsNullOrWhiteSpace(options.Passphrase) && !passphraseValid)
        {
            return (false, null, "INVALID_PASSPHRASE", "The provided backup passphrase is incorrect.");
        }

        foreach (var note in migrationNotes)
        {
            warnings.Add(note);
        }

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync<(bool Succeeded, RestoreResultDto? Result, string? ErrorCode, string? ErrorMessage)>(async () =>
        {
            _db.ChangeTracker.Clear();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (false, null, "USER_NOT_FOUND", "Authenticated user profile was not found.");
            }

            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync()
                : null;

            try
            {
                int setupsCreated = 0, setupsUpdated = 0, setupsSkipped = 0;
                int eqCreated = 0, eqUpdated = 0, eqSkipped = 0;
                int ingCreated = 0, ingUpdated = 0, ingSkipped = 0;
                int stocksUpserted = 0;
                int recipesCreated = 0, recipesUpdated = 0, recipesSkipped = 0;
                int batchesCreated = 0, batchesUpdated = 0, batchesSkipped = 0;
                int readingsRestored = 0;
                int collaboratorsRestored = 0;
                var generatedInvites = new List<RestoredInviteInfoDto>();

                // Handle ReplaceAll clean-slate mode
                if (options.ConflictResolution == RestoreConflictResolutionMode.ReplaceAll)
                {
                    await ExecuteReplaceAllCleanupAsync(userId);
                }

                // 1. Restore User Settings
                bool settingsRestored = false;
                if (options.RestoreUserSettings && payload.UserSettings != null)
                {
                    ApplyUserSettings(user, payload.UserSettings, encryptionKey, warnings);
                    settingsRestored = true;
                }

                // ID Translation Maps (Archive Guid -> Target Database Guid)
                var setupIdMap = new Dictionary<Guid, Guid>();
                var equipmentIdMap = new Dictionary<Guid, Guid>();
                var ingredientIdMap = new Dictionary<Guid, Guid>();
                var recipeIdMap = new Dictionary<Guid, Guid>();
                var batchIdMap = new Dictionary<Guid, Guid>();
                var batchMashStepIdMap = new Dictionary<Guid, Guid>();

                // 2. Restore Custom Ingredients
                var catalogIngredients = await _db.Ingredients
                    .Where(i => i.IsCatalogItem)
                    .ToListAsync();

                var userCustomIngredients = await _db.Ingredients
                    .Where(i => !i.IsCatalogItem && i.CreatedByUserId == userId)
                    .ToListAsync();

                foreach (var customDto in payload.CustomIngredients)
                {
                    var normName = customDto.Name.Trim();
                    if (string.IsNullOrEmpty(normName)) continue;

                    var existing = userCustomIngredients.FirstOrDefault(i =>
                        i.Id == customDto.Id ||
                        (string.Equals(i.Name, normName, StringComparison.OrdinalIgnoreCase) && i.Type == customDto.Type));

                    if (existing != null)
                    {
                        ingredientIdMap[customDto.Id] = existing.Id;
                        if (options.ConflictResolution == RestoreConflictResolutionMode.MergeOverwriteExisting)
                        {
                            existing.Name = normName;
                            existing.PotentialGravity = customDto.PotentialGravity;
                            existing.ColorSrm = customDto.ColorSrm;
                            existing.AlphaAcidPercent = customDto.AlphaAcidPercent;
                            existing.AttenuationPercent = customDto.AttenuationPercent;
                            existing.Form = customDto.Form;
                            existing.Description = customDto.Description;
                            ingUpdated++;
                        }
                        else
                        {
                            ingSkipped++;
                        }
                    }
                    else
                    {
                        var idConflict = await _db.Ingredients.AnyAsync(i => i.Id == customDto.Id);
                        var newIngredient = new Ingredient
                        {
                            Id = idConflict ? Guid.NewGuid() : customDto.Id,
                            Name = normName,
                            Type = customDto.Type,
                            PotentialGravity = customDto.PotentialGravity,
                            ColorSrm = customDto.ColorSrm,
                            AlphaAcidPercent = customDto.AlphaAcidPercent,
                            AttenuationPercent = customDto.AttenuationPercent,
                            Form = customDto.Form,
                            Description = customDto.Description,
                            IsCatalogItem = false,
                            CreatedByUserId = userId,
                            CreatedAt = customDto.CreatedAt == default ? DateTime.UtcNow : customDto.CreatedAt
                        };
                        _db.Ingredients.Add(newIngredient);
                        userCustomIngredients.Add(newIngredient);
                        ingredientIdMap[customDto.Id] = newIngredient.Id;
                        ingCreated++;
                    }
                }

                await _db.SaveChangesAsync();

                async Task<Guid> ResolveIngredientAsync(
                    Guid archiveIngredientId,
                    string ingredientName,
                    IngredientType ingredientType,
                    IngredientSnapshotDto? snapshot)
                {
                    if (ingredientIdMap.TryGetValue(archiveIngredientId, out var mappedId))
                    {
                        return mappedId;
                    }

                    var byCatalogId = catalogIngredients.FirstOrDefault(i => i.Id == archiveIngredientId);
                    if (byCatalogId != null)
                    {
                        ingredientIdMap[archiveIngredientId] = byCatalogId.Id;
                        return byCatalogId.Id;
                    }

                    var byCustomId = userCustomIngredients.FirstOrDefault(i => i.Id == archiveIngredientId);
                    if (byCustomId != null)
                    {
                        ingredientIdMap[archiveIngredientId] = byCustomId.Id;
                        return byCustomId.Id;
                    }

                    var cleanName = (snapshot?.Name ?? ingredientName).Trim();
                    var targetType = snapshot?.Type ?? ingredientType;

                    var byCatalogName = catalogIngredients.FirstOrDefault(i =>
                        string.Equals(i.Name, cleanName, StringComparison.OrdinalIgnoreCase) && i.Type == targetType)
                        ?? catalogIngredients.FirstOrDefault(i =>
                            string.Equals(i.Name, cleanName, StringComparison.OrdinalIgnoreCase));

                    if (byCatalogName != null)
                    {
                        ingredientIdMap[archiveIngredientId] = byCatalogName.Id;
                        return byCatalogName.Id;
                    }

                    var byCustomName = userCustomIngredients.FirstOrDefault(i =>
                        string.Equals(i.Name, cleanName, StringComparison.OrdinalIgnoreCase) && i.Type == targetType);

                    if (byCustomName != null)
                    {
                        ingredientIdMap[archiveIngredientId] = byCustomName.Id;
                        return byCustomName.Id;
                    }

                    // Fallback: synthesize custom ingredient from snapshot so recipes/stocks never break
                    var fallbackIngredient = new Ingredient
                    {
                        Id = Guid.NewGuid(),
                        Name = string.IsNullOrEmpty(cleanName) ? "Restored Ingredient" : cleanName,
                        Type = targetType,
                        PotentialGravity = snapshot?.PotentialGravity,
                        ColorSrm = snapshot?.ColorSrm,
                        AlphaAcidPercent = snapshot?.AlphaAcidPercent,
                        AttenuationPercent = snapshot?.AttenuationPercent,
                        Form = snapshot?.Form,
                        Description = snapshot?.Description ?? "Auto-created during backup restore",
                        IsCatalogItem = false,
                        CreatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Ingredients.Add(fallbackIngredient);
                    await _db.SaveChangesAsync();

                    userCustomIngredients.Add(fallbackIngredient);
                    ingredientIdMap[archiveIngredientId] = fallbackIngredient.Id;
                    ingCreated++;
                    return fallbackIngredient.Id;
                }

                // 3. Restore Ingredient Stocks
                var existingStocks = await _db.IngredientStocks
                    .Where(s => s.UserId == userId)
                    .ToListAsync();

                foreach (var stockDto in payload.IngredientStocks)
                {
                    var targetIngId = await ResolveIngredientAsync(
                        stockDto.IngredientId,
                        stockDto.IngredientName,
                        stockDto.IngredientType,
                        stockDto.IngredientSnapshot);

                    var existingStock = existingStocks.FirstOrDefault(s => s.IngredientId == targetIngId);
                    if (existingStock != null)
                    {
                        if (options.ConflictResolution != RestoreConflictResolutionMode.MergeSkipExisting)
                        {
                            existingStock.Amount = Math.Max(0m, stockDto.Amount);
                            existingStock.Unit = string.IsNullOrWhiteSpace(stockDto.Unit) ? "kg" : stockDto.Unit.Trim();
                            existingStock.LowStockAlertThreshold = stockDto.LowStockAlertThreshold;
                            existingStock.UpdatedAt = DateTime.UtcNow;
                            stocksUpserted++;
                        }
                    }
                    else
                    {
                        var newStock = new IngredientStock
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            IngredientId = targetIngId,
                            Amount = Math.Max(0m, stockDto.Amount),
                            Unit = string.IsNullOrWhiteSpace(stockDto.Unit) ? "kg" : stockDto.Unit.Trim(),
                            LowStockAlertThreshold = stockDto.LowStockAlertThreshold,
                            UpdatedAt = stockDto.UpdatedAt == default ? DateTime.UtcNow : stockDto.UpdatedAt
                        };
                        _db.IngredientStocks.Add(newStock);
                        existingStocks.Add(newStock);
                        stocksUpserted++;
                    }
                }

                await _db.SaveChangesAsync();

                // 4. Restore Owned Brewery Setups & Collaboration
                var userOwnedSetups = await _db.BrewerySetups
                    .Include(s => s.Members)
                    .Include(s => s.Invites)
                    .Where(s => s.UserId == userId)
                    .ToListAsync();

                foreach (var setupDto in payload.OwnedBrewerySetups)
                {
                    var normName = setupDto.Name.Trim();
                    if (string.IsNullOrEmpty(normName)) normName = "My brewery";

                    var existingSetup = userOwnedSetups.FirstOrDefault(s =>
                        s.Id == setupDto.Id ||
                        string.Equals(s.Name, normName, StringComparison.OrdinalIgnoreCase));

                    BrewerySetup targetSetup;
                    if (existingSetup != null)
                    {
                        targetSetup = existingSetup;
                        setupIdMap[setupDto.Id] = existingSetup.Id;

                        if (options.ConflictResolution == RestoreConflictResolutionMode.MergeOverwriteExisting)
                        {
                            existingSetup.Name = normName;
                            existingSetup.Description = setupDto.Description;
                            existingSetup.DefaultGrainAbsorptionRate = setupDto.DefaultGrainAbsorptionRate;
                            existingSetup.DefaultBoilOffRatePerHour = setupDto.DefaultBoilOffRatePerHour;
                            existingSetup.DefaultKettleTrubLossLiters = setupDto.DefaultKettleTrubLossLiters;
                            existingSetup.DefaultFermenterLossLiters = setupDto.DefaultFermenterLossLiters;
                            existingSetup.DefaultMashTunDeadSpaceLiters = setupDto.DefaultMashTunDeadSpaceLiters;
                            existingSetup.CoolingShrinkagePercent = setupDto.CoolingShrinkagePercent;
                            existingSetup.DefaultPackagingLossLiters = setupDto.DefaultPackagingLossLiters;
                            existingSetup.UpdatedAt = DateTime.UtcNow;
                            setupsUpdated++;
                        }
                        else
                        {
                            setupsSkipped++;
                        }
                    }
                    else
                    {
                        var idTaken = await _db.BrewerySetups.AnyAsync(s => s.Id == setupDto.Id);
                        targetSetup = new BrewerySetup
                        {
                            Id = idTaken ? Guid.NewGuid() : setupDto.Id,
                            UserId = userId,
                            Name = normName,
                            Description = setupDto.Description,
                            IsDefault = false, // Normalized after loop to guarantee unique default constraint
                            DefaultGrainAbsorptionRate = setupDto.DefaultGrainAbsorptionRate,
                            DefaultBoilOffRatePerHour = setupDto.DefaultBoilOffRatePerHour,
                            DefaultKettleTrubLossLiters = setupDto.DefaultKettleTrubLossLiters,
                            DefaultFermenterLossLiters = setupDto.DefaultFermenterLossLiters,
                            DefaultMashTunDeadSpaceLiters = setupDto.DefaultMashTunDeadSpaceLiters,
                            CoolingShrinkagePercent = setupDto.CoolingShrinkagePercent,
                            DefaultPackagingLossLiters = setupDto.DefaultPackagingLossLiters,
                            CreatedAt = setupDto.CreatedAt == default ? DateTime.UtcNow : setupDto.CreatedAt,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.BrewerySetups.Add(targetSetup);
                        userOwnedSetups.Add(targetSetup);
                        setupIdMap[setupDto.Id] = targetSetup.Id;
                        setupsCreated++;
                    }

                    // Restore Collaborators & Invites on Owned Setup
                    if (options.CollaboratorRestore != CollaboratorRestoreMode.SkipCollaborators)
                    {
                        var (restoredCount, invites) = await RestoreSetupCollaborationAsync(
                            targetSetup,
                            setupDto,
                            userId,
                            options.CollaboratorRestore);
                        collaboratorsRestored += restoredCount;
                        generatedInvites.AddRange(invites);
                    }
                }

                // Ensure exactly one default setup for userId
                await NormalizeDefaultSetupAsync(userId, userOwnedSetups, payload.OwnedBrewerySetups, setupIdMap, user.PreferredLanguage);
                await _db.SaveChangesAsync();

                // 5. Restore Shared Setup Memberships
                if (options.RestoreSharedSetups)
                {
                    foreach (var sharedDto in payload.SharedSetupMemberships)
                    {
                        var liveSharedSetup = await _db.BrewerySetups
                            .Include(s => s.Members)
                            .Include(s => s.Equipment)
                            .FirstOrDefaultAsync(s =>
                                s.Id == sharedDto.OriginalSetupId &&
                                (s.UserId == userId || s.Members.Any(m => m.UserId == userId)));

                        if (liveSharedSetup != null)
                        {
                            setupIdMap[sharedDto.OriginalSetupId] = liveSharedSetup.Id;
                            foreach (var snapEq in sharedDto.ReferencedEquipmentSnapshots)
                            {
                                var liveEq = liveSharedSetup.Equipment.FirstOrDefault(e =>
                                    e.Id == snapEq.Id ||
                                    string.Equals(e.Name, snapEq.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                                if (liveEq != null)
                                {
                                    equipmentIdMap[snapEq.Id] = liveEq.Id;
                                }
                            }
                        }
                        else if (options.SharedSetupFallback == SharedSetupFallbackMode.CloneAsPersonal)
                        {
                            var baseName = $"{sharedDto.SetupName.Trim()} (Restored)";
                            var cloneName = EnsureUniqueSetupName(baseName, userOwnedSetups);

                            var clonedSetup = new BrewerySetup
                            {
                                Id = Guid.NewGuid(),
                                UserId = userId,
                                Name = cloneName,
                                Description = sharedDto.SetupSnapshot.Description,
                                IsDefault = false,
                                DefaultGrainAbsorptionRate = sharedDto.SetupSnapshot.DefaultGrainAbsorptionRate,
                                DefaultBoilOffRatePerHour = sharedDto.SetupSnapshot.DefaultBoilOffRatePerHour,
                                DefaultKettleTrubLossLiters = sharedDto.SetupSnapshot.DefaultKettleTrubLossLiters,
                                DefaultFermenterLossLiters = sharedDto.SetupSnapshot.DefaultFermenterLossLiters,
                                DefaultMashTunDeadSpaceLiters = sharedDto.SetupSnapshot.DefaultMashTunDeadSpaceLiters,
                                CoolingShrinkagePercent = sharedDto.SetupSnapshot.CoolingShrinkagePercent,
                                DefaultPackagingLossLiters = sharedDto.SetupSnapshot.DefaultPackagingLossLiters,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };
                            _db.BrewerySetups.Add(clonedSetup);
                            userOwnedSetups.Add(clonedSetup);
                            setupIdMap[sharedDto.OriginalSetupId] = clonedSetup.Id;
                            setupsCreated++;

                            foreach (var snapEq in sharedDto.ReferencedEquipmentSnapshots)
                            {
                                var clonedEq = new Equipment
                                {
                                    Id = Guid.NewGuid(),
                                    UserId = userId,
                                    BrewerySetupId = clonedSetup.Id,
                                    Name = EquipmentService.NormalizeEquipmentName(snapEq.Name),
                                    Type = snapEq.Type,
                                    Subtype = snapEq.Subtype,
                                    Capacity = snapEq.Capacity,
                                    Unit = snapEq.Unit,
                                    CapacityLiters = snapEq.CapacityLiters,
                                    CurrentVolume = snapEq.CurrentVolume,
                                    CurrentVolumeLiters = snapEq.CurrentVolumeLiters,
                                    BoilOffRatePerHour = snapEq.BoilOffRatePerHour,
                                    TrubLossLiters = snapEq.TrubLossLiters,
                                    MashTunDeadSpaceLiters = snapEq.MashTunDeadSpaceLiters,
                                    PackagingLossLiters = snapEq.PackagingLossLiters,
                                    ConnectionType = EquipmentConnectionType.None,
                                    Description = snapEq.Description,
                                    Notes = snapEq.Notes,
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                };
                                _db.Equipment.Add(clonedEq);
                                equipmentIdMap[snapEq.Id] = clonedEq.Id;
                                eqCreated++;
                            }
                        }
                        else
                        {
                            warnings.Add($"Detached references to unavailable shared brewery setup '{sharedDto.SetupName}'.");
                        }
                    }

                    await _db.SaveChangesAsync();
                }

                // 6. Restore Equipment
                var defaultSetupId = userOwnedSetups.FirstOrDefault(s => s.IsDefault)?.Id
                                     ?? userOwnedSetups.First().Id;

                var existingUserEquipment = await _db.Equipment
                    .Where(e => e.UserId == userId || userOwnedSetups.Select(s => s.Id).Contains(e.BrewerySetupId))
                    .ToListAsync();

                foreach (var eqDto in payload.Equipment)
                {
                    var targetSetupId = setupIdMap.TryGetValue(eqDto.BrewerySetupId, out var mappedSetupId)
                        ? mappedSetupId
                        : defaultSetupId;

                    var normName = EquipmentService.NormalizeEquipmentName(eqDto.Name);
                    if (string.IsNullOrEmpty(normName)) normName = "Restored Vessel";

                    var existingEq = existingUserEquipment.FirstOrDefault(e =>
                        e.Id == eqDto.Id ||
                        (e.BrewerySetupId == targetSetupId && string.Equals(e.Name, normName, StringComparison.OrdinalIgnoreCase)));

                    var (restoredToken, restoredConfigJson) = await RestoreEquipmentSecretsAsync(
                        eqDto, encryptionKey, existingEq?.Id, warnings);

                    if (existingEq != null)
                    {
                        equipmentIdMap[eqDto.Id] = existingEq.Id;
                        if (options.ConflictResolution == RestoreConflictResolutionMode.MergeOverwriteExisting)
                        {
                            existingEq.BrewerySetupId = targetSetupId;
                            existingEq.Name = normName;
                            existingEq.Type = eqDto.Type;
                            existingEq.Subtype = eqDto.Subtype;
                            existingEq.Capacity = eqDto.Capacity;
                            existingEq.Unit = eqDto.Unit;
                            existingEq.CapacityLiters = eqDto.CapacityLiters;
                            existingEq.CurrentVolume = eqDto.CurrentVolume;
                            existingEq.CurrentVolumeLiters = eqDto.CurrentVolumeLiters;
                            existingEq.BoilOffRatePerHour = eqDto.BoilOffRatePerHour;
                            existingEq.TrubLossLiters = eqDto.TrubLossLiters;
                            existingEq.MashTunDeadSpaceLiters = eqDto.MashTunDeadSpaceLiters;
                            existingEq.PackagingLossLiters = eqDto.PackagingLossLiters;
                            existingEq.CurrentTemperatureC = eqDto.CurrentTemperatureC;
                            existingEq.CurrentSpecificGravity = eqDto.CurrentSpecificGravity;
                            existingEq.CurrentPressureBar = eqDto.CurrentPressureBar;
                            existingEq.CurrentBatteryPercent = eqDto.CurrentBatteryPercent;
                            existingEq.CurrentBatteryVoltage = eqDto.CurrentBatteryVoltage;
                            existingEq.TemperatureUpdatedAt = eqDto.TemperatureUpdatedAt;
                            existingEq.LastTelemetryAt = eqDto.LastTelemetryAt;
                            existingEq.LatestMetricsJson = eqDto.LatestMetricsJson;
                            existingEq.ConnectionType = eqDto.ConnectionType;
                            if (restoredToken != null) existingEq.ConnectionToken = restoredToken;
                            if (restoredConfigJson != null) existingEq.ConnectionConfigJson = restoredConfigJson;
                            existingEq.Description = eqDto.Description;
                            existingEq.Notes = eqDto.Notes;
                            existingEq.UpdatedAt = DateTime.UtcNow;
                            eqUpdated++;
                        }
                        else
                        {
                            eqSkipped++;
                        }
                    }
                    else
                    {
                        var idTaken = await _db.Equipment.AnyAsync(e => e.Id == eqDto.Id);
                        var uniqueName = EnsureUniqueEquipmentName(normName, targetSetupId, existingUserEquipment);

                        var newEq = new Equipment
                        {
                            Id = idTaken ? Guid.NewGuid() : eqDto.Id,
                            UserId = userId,
                            BrewerySetupId = targetSetupId,
                            Name = uniqueName,
                            Type = eqDto.Type,
                            Subtype = eqDto.Subtype,
                            Capacity = eqDto.Capacity,
                            Unit = eqDto.Unit,
                            CapacityLiters = eqDto.CapacityLiters,
                            CurrentVolume = eqDto.CurrentVolume,
                            CurrentVolumeLiters = eqDto.CurrentVolumeLiters,
                            BoilOffRatePerHour = eqDto.BoilOffRatePerHour,
                            TrubLossLiters = eqDto.TrubLossLiters,
                            MashTunDeadSpaceLiters = eqDto.MashTunDeadSpaceLiters,
                            PackagingLossLiters = eqDto.PackagingLossLiters,
                            CurrentTemperatureC = eqDto.CurrentTemperatureC,
                            CurrentSpecificGravity = eqDto.CurrentSpecificGravity,
                            CurrentPressureBar = eqDto.CurrentPressureBar,
                            CurrentBatteryPercent = eqDto.CurrentBatteryPercent,
                            CurrentBatteryVoltage = eqDto.CurrentBatteryVoltage,
                            TemperatureUpdatedAt = eqDto.TemperatureUpdatedAt,
                            LastTelemetryAt = eqDto.LastTelemetryAt,
                            LatestMetricsJson = eqDto.LatestMetricsJson,
                            ConnectionType = eqDto.ConnectionType,
                            ConnectionToken = restoredToken,
                            ConnectionConfigJson = restoredConfigJson,
                            Description = eqDto.Description,
                            Notes = eqDto.Notes,
                            CreatedAt = eqDto.CreatedAt == default ? DateTime.UtcNow : eqDto.CreatedAt,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.Equipment.Add(newEq);
                        existingUserEquipment.Add(newEq);
                        equipmentIdMap[eqDto.Id] = newEq.Id;
                        eqCreated++;
                    }
                }

                await _db.SaveChangesAsync();

                // 7. Resolve Referenced External Recipes & Restore User Recipes
                foreach (var extRecipe in payload.ReferencedExternalRecipes)
                {
                    var liveExt = await _db.Recipes
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == extRecipe.Id && (r.IsPublic || r.UserId == userId));
                    if (liveExt != null)
                    {
                        recipeIdMap[extRecipe.Id] = liveExt.Id;
                    }
                }

                var existingUserRecipes = await _db.Recipes
                    .Include(r => r.Ingredients)
                    .Include(r => r.MashSteps)
                    .Include(r => r.FermentationSteps)
                    .Where(r => r.UserId == userId)
                    .ToListAsync();

                foreach (var recipeDto in payload.Recipes)
                {
                    var normName = recipeDto.Name.Trim();
                    if (string.IsNullOrEmpty(normName)) normName = "Restored Recipe";

                    var existingRecipe = existingUserRecipes.FirstOrDefault(r =>
                        r.Id == recipeDto.Id ||
                        string.Equals(r.Name, normName, StringComparison.OrdinalIgnoreCase));

                    if (existingRecipe != null)
                    {
                        recipeIdMap[recipeDto.Id] = existingRecipe.Id;
                        if (options.ConflictResolution == RestoreConflictResolutionMode.MergeOverwriteExisting)
                        {
                            existingRecipe.Name = normName;
                            existingRecipe.Description = recipeDto.Description;
                            existingRecipe.BeerStyle = recipeDto.BeerStyle;
                            existingRecipe.BatchSizeLiters = recipeDto.BatchSizeLiters;
                            existingRecipe.BoilTimeMinutes = recipeDto.BoilTimeMinutes;
                            existingRecipe.EfficiencyPercent = recipeDto.EfficiencyPercent;
                            existingRecipe.OriginalGravity = recipeDto.OriginalGravity;
                            existingRecipe.FinalGravity = recipeDto.FinalGravity;
                            existingRecipe.AlcoholByVolume = recipeDto.AlcoholByVolume;
                            existingRecipe.BitternessIbu = recipeDto.BitternessIbu;
                            existingRecipe.ColorSrm = recipeDto.ColorSrm;
                            existingRecipe.IsPublic = recipeDto.IsPublic;
                            existingRecipe.UpdatedAt = DateTime.UtcNow;

                            _db.RecipeIngredients.RemoveRange(existingRecipe.Ingredients);
                            _db.RecipeMashSteps.RemoveRange(existingRecipe.MashSteps);
                            _db.RecipeFermentationSteps.RemoveRange(existingRecipe.FermentationSteps);
                            await _db.SaveChangesAsync();

                            var newRecipeIngredients = await BuildRecipeIngredientsAsync(existingRecipe.Id, recipeDto.Ingredients, ResolveIngredientAsync);
                            var newRecipeMashSteps = BuildRecipeMashSteps(existingRecipe.Id, recipeDto.MashSteps);
                            var newRecipeFermentationSteps = BuildRecipeFermentationSteps(existingRecipe.Id, recipeDto.FermentationSteps);

                            _db.RecipeIngredients.AddRange(newRecipeIngredients);
                            _db.RecipeMashSteps.AddRange(newRecipeMashSteps);
                            _db.RecipeFermentationSteps.AddRange(newRecipeFermentationSteps);
                            recipesUpdated++;
                        }
                        else
                        {
                            recipesSkipped++;
                        }
                    }
                    else
                    {
                        var idTaken = await _db.Recipes.AnyAsync(r => r.Id == recipeDto.Id);
                        var uniqueName = EnsureUniqueRecipeName(normName, existingUserRecipes);
                        var newRecipeId = idTaken ? Guid.NewGuid() : recipeDto.Id;

                        var newRecipe = new Recipe
                        {
                            Id = newRecipeId,
                            UserId = userId,
                            Name = uniqueName,
                            Description = recipeDto.Description,
                            BeerStyle = recipeDto.BeerStyle,
                            BatchSizeLiters = recipeDto.BatchSizeLiters,
                            BoilTimeMinutes = recipeDto.BoilTimeMinutes,
                            EfficiencyPercent = recipeDto.EfficiencyPercent,
                            OriginalGravity = recipeDto.OriginalGravity,
                            FinalGravity = recipeDto.FinalGravity,
                            AlcoholByVolume = recipeDto.AlcoholByVolume,
                            BitternessIbu = recipeDto.BitternessIbu,
                            ColorSrm = recipeDto.ColorSrm,
                            IsPublic = recipeDto.IsPublic,
                            CreatedAt = recipeDto.CreatedAt == default ? DateTime.UtcNow : recipeDto.CreatedAt,
                            UpdatedAt = DateTime.UtcNow,
                            Ingredients = await BuildRecipeIngredientsAsync(newRecipeId, recipeDto.Ingredients, ResolveIngredientAsync),
                            MashSteps = BuildRecipeMashSteps(newRecipeId, recipeDto.MashSteps),
                            FermentationSteps = BuildRecipeFermentationSteps(newRecipeId, recipeDto.FermentationSteps)
                        };
                        _db.Recipes.Add(newRecipe);
                        existingUserRecipes.Add(newRecipe);
                        recipeIdMap[recipeDto.Id] = newRecipe.Id;
                        recipesCreated++;
                    }
                }

                await _db.SaveChangesAsync();

                // 8. Restore Batches
                var existingUserBatches = await _db.Batches
                    .Include(b => b.VolumeProfile)
                    .Include(b => b.Ingredients)
                    .Include(b => b.MashSteps)
                    .Include(b => b.FermentationSteps)
                    .Include(b => b.StageHistory)
                    .Include(b => b.Readings)
                    .Include(b => b.SensorAssignments)
                    .Where(b => b.UserId == userId)
                    .ToListAsync();

                foreach (var batchDto in payload.Batches)
                {
                    var normCode = batchDto.BatchCode.Trim();
                    if (string.IsNullOrEmpty(normCode)) normCode = $"B-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}-1";

                    var existingBatch = existingUserBatches.FirstOrDefault(b =>
                        b.Id == batchDto.Id ||
                        string.Equals(b.BatchCode, normCode, StringComparison.OrdinalIgnoreCase));

                    Guid? mappedRecipeId = batchDto.RecipeId.HasValue && recipeIdMap.TryGetValue(batchDto.RecipeId.Value, out var rId)
                        ? rId
                        : null;
                    Guid? mappedSetupId = batchDto.BrewerySetupId.HasValue && setupIdMap.TryGetValue(batchDto.BrewerySetupId.Value, out var sId)
                        ? sId
                        : defaultSetupId;
                    Guid? mappedBoilerId = batchDto.BoilerId.HasValue && equipmentIdMap.TryGetValue(batchDto.BoilerId.Value, out var bId)
                        ? bId
                        : null;
                    Guid? mappedFermenterId = batchDto.FermenterId.HasValue && equipmentIdMap.TryGetValue(batchDto.FermenterId.Value, out var fId)
                        ? fId
                        : null;
                    Guid? mappedPkgId = batchDto.PackagingVesselId.HasValue && equipmentIdMap.TryGetValue(batchDto.PackagingVesselId.Value, out var pId)
                        ? pId
                        : null;

                    if (existingBatch != null)
                    {
                        batchIdMap[batchDto.Id] = existingBatch.Id;
                        if (options.ConflictResolution == RestoreConflictResolutionMode.MergeOverwriteExisting)
                        {
                            existingBatch.RecipeId = mappedRecipeId;
                            existingBatch.BrewerySetupId = mappedSetupId;
                            existingBatch.Name = batchDto.Name.Trim();
                            existingBatch.BeerStyle = batchDto.BeerStyle;
                            existingBatch.Status = batchDto.Status;
                            existingBatch.CurrentStage = batchDto.CurrentStage;
                            existingBatch.BrewDate = batchDto.BrewDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : batchDto.BrewDate;
                            existingBatch.TargetOg = batchDto.TargetOg;
                            existingBatch.TargetFg = batchDto.TargetFg;
                            existingBatch.TargetAbv = batchDto.TargetAbv;
                            existingBatch.TargetIbu = batchDto.TargetIbu;
                            existingBatch.TargetColorSrm = batchDto.TargetColorSrm;
                            existingBatch.TargetBatchSizeLiters = batchDto.TargetBatchSizeLiters;
                            existingBatch.BoilTimeMinutes = batchDto.BoilTimeMinutes;
                            existingBatch.EfficiencyPercent = batchDto.EfficiencyPercent;
                            existingBatch.MeasuredOg = batchDto.MeasuredOg;
                            existingBatch.CurrentGravity = batchDto.CurrentGravity;
                            existingBatch.MeasuredFg = batchDto.MeasuredFg;
                            existingBatch.AlcoholByVolume = batchDto.AlcoholByVolume;
                            existingBatch.BrewhouseEfficiency = batchDto.BrewhouseEfficiency;
                            existingBatch.MeasuredBatchSizeLiters = batchDto.MeasuredBatchSizeLiters;
                            existingBatch.PitchTemperatureC = batchDto.PitchTemperatureC;
                            existingBatch.BoilerId = mappedBoilerId;
                            existingBatch.FermenterId = mappedFermenterId;
                            existingBatch.PackagingVesselId = mappedPkgId;
                            existingBatch.Notes = batchDto.Notes;
                            existingBatch.UpdatedAt = DateTime.UtcNow;
                            existingBatch.CompletedAt = batchDto.CompletedAt;

                            if (existingBatch.VolumeProfile != null)
                            {
                                _db.BatchVolumeProfiles.Remove(existingBatch.VolumeProfile);
                                existingBatch.VolumeProfile = null;
                            }
                            _db.BatchIngredients.RemoveRange(existingBatch.Ingredients);
                            _db.BatchMashSteps.RemoveRange(existingBatch.MashSteps);
                            _db.BatchFermentationSteps.RemoveRange(existingBatch.FermentationSteps);
                            _db.BatchStageHistories.RemoveRange(existingBatch.StageHistory);
                            _db.BatchReadings.RemoveRange(existingBatch.Readings);
                            _db.BatchSensorAssignments.RemoveRange(existingBatch.SensorAssignments);
                            await _db.SaveChangesAsync();

                            PopulateBatchChildren(existingBatch, batchDto, ingredientIdMap, equipmentIdMap, batchMashStepIdMap);
                            if (existingBatch.VolumeProfile != null)
                            {
                                _db.BatchVolumeProfiles.Add(existingBatch.VolumeProfile);
                            }
                            _db.BatchIngredients.AddRange(existingBatch.Ingredients);
                            _db.BatchMashSteps.AddRange(existingBatch.MashSteps);
                            _db.BatchFermentationSteps.AddRange(existingBatch.FermentationSteps);
                            _db.BatchStageHistories.AddRange(existingBatch.StageHistory);
                            _db.BatchReadings.AddRange(existingBatch.Readings);
                            _db.BatchSensorAssignments.AddRange(existingBatch.SensorAssignments);
                            batchesUpdated++;
                        }
                        else
                        {
                            foreach (var ms in batchDto.MashSteps ?? [])
                            {
                                var matchMs = existingBatch.MashSteps.FirstOrDefault(x => x.StepOrder == ms.StepOrder);
                                if (matchMs != null) batchMashStepIdMap[ms.Id] = matchMs.Id;
                            }
                            batchesSkipped++;
                        }
                    }
                    else
                    {
                        var idTaken = await _db.Batches.AnyAsync(b => b.Id == batchDto.Id);
                        var uniqueCode = EnsureUniqueBatchCode(normCode, existingUserBatches);
                        var newBatchId = idTaken ? Guid.NewGuid() : batchDto.Id;

                        var newBatch = new Batch
                        {
                            Id = newBatchId,
                            UserId = userId,
                            RecipeId = mappedRecipeId,
                            BrewerySetupId = mappedSetupId,
                            BatchCode = uniqueCode,
                            Name = string.IsNullOrWhiteSpace(batchDto.Name) ? uniqueCode : batchDto.Name.Trim(),
                            BeerStyle = batchDto.BeerStyle,
                            Status = batchDto.Status,
                            CurrentStage = batchDto.CurrentStage,
                            BrewDate = batchDto.BrewDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : batchDto.BrewDate,
                            TargetOg = batchDto.TargetOg,
                            TargetFg = batchDto.TargetFg,
                            TargetAbv = batchDto.TargetAbv,
                            TargetIbu = batchDto.TargetIbu,
                            TargetColorSrm = batchDto.TargetColorSrm,
                            TargetBatchSizeLiters = batchDto.TargetBatchSizeLiters,
                            BoilTimeMinutes = batchDto.BoilTimeMinutes,
                            EfficiencyPercent = batchDto.EfficiencyPercent,
                            MeasuredOg = batchDto.MeasuredOg,
                            CurrentGravity = batchDto.CurrentGravity,
                            MeasuredFg = batchDto.MeasuredFg,
                            AlcoholByVolume = batchDto.AlcoholByVolume,
                            BrewhouseEfficiency = batchDto.BrewhouseEfficiency,
                            MeasuredBatchSizeLiters = batchDto.MeasuredBatchSizeLiters,
                            PitchTemperatureC = batchDto.PitchTemperatureC,
                            BoilerId = mappedBoilerId,
                            FermenterId = mappedFermenterId,
                            PackagingVesselId = mappedPkgId,
                            Notes = batchDto.Notes,
                            CreatedAt = batchDto.CreatedAt == default ? DateTime.UtcNow : batchDto.CreatedAt,
                            UpdatedAt = DateTime.UtcNow,
                            CompletedAt = batchDto.CompletedAt
                        };

                        PopulateBatchChildren(newBatch, batchDto, ingredientIdMap, equipmentIdMap, batchMashStepIdMap);
                        _db.Batches.Add(newBatch);
                        existingUserBatches.Add(newBatch);
                        batchIdMap[batchDto.Id] = newBatch.Id;
                        batchesCreated++;
                    }
                }

                await _db.SaveChangesAsync();

                // 9. Restore Equipment Telemetry Readings
                if (options.RestoreTelemetryReadings && payload.EquipmentReadings.Count > 0)
                {
                    var targetEqIds = equipmentIdMap.Values.Distinct().ToList();
                    var existingReadingKeys = await _db.EquipmentReadings
                        .AsNoTracking()
                        .Where(er => targetEqIds.Contains(er.EquipmentId))
                        .Select(er => new { er.EquipmentId, er.Timestamp })
                        .ToListAsync();

                    var existingSet = existingReadingKeys
                        .Select(k => (k.EquipmentId, k.Timestamp))
                        .ToHashSet();

                    foreach (var erDto in payload.EquipmentReadings)
                    {
                        if (!equipmentIdMap.TryGetValue(erDto.EquipmentId, out var targetEqId))
                        {
                            continue;
                        }

                        if (existingSet.Contains((targetEqId, erDto.Timestamp)))
                        {
                            continue;
                        }

                        Guid? targetBatchId = erDto.BatchId.HasValue && batchIdMap.TryGetValue(erDto.BatchId.Value, out var bId)
                            ? bId
                            : null;
                        Guid? targetMashStepId = erDto.BatchMashStepId.HasValue && batchMashStepIdMap.TryGetValue(erDto.BatchMashStepId.Value, out var msId)
                            ? msId
                            : null;

                        _db.EquipmentReadings.Add(new EquipmentReading
                        {
                            Id = Guid.NewGuid(),
                            EquipmentId = targetEqId,
                            BatchId = targetBatchId,
                            Stage = erDto.Stage,
                            BatchMashStepId = targetMashStepId,
                            StepName = erDto.StepName,
                            Timestamp = erDto.Timestamp,
                            TemperatureC = erDto.TemperatureC,
                            SpecificGravity = erDto.SpecificGravity,
                            PressureBar = erDto.PressureBar,
                            BatteryPercent = erDto.BatteryPercent,
                            BatteryVoltage = erDto.BatteryVoltage,
                            TiltDegrees = erDto.TiltDegrees,
                            Rssi = erDto.Rssi,
                            MetricsJson = erDto.MetricsJson,
                            Source = erDto.Source,
                            RawPayload = erDto.RawPayload,
                            Notes = erDto.Notes,
                            CreatedAt = erDto.CreatedAt == default ? DateTime.UtcNow : erDto.CreatedAt
                        });
                        existingSet.Add((targetEqId, erDto.Timestamp));
                        readingsRestored++;
                    }

                    await _db.SaveChangesAsync();
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                var summary = new RestoreActionCountsDto(
                    SetupsCreated: setupsCreated,
                    SetupsUpdated: setupsUpdated,
                    SetupsSkipped: setupsSkipped,
                    EquipmentCreated: eqCreated,
                    EquipmentUpdated: eqUpdated,
                    EquipmentSkipped: eqSkipped,
                    CustomIngredientsCreated: ingCreated,
                    CustomIngredientsUpdated: ingUpdated,
                    CustomIngredientsSkipped: ingSkipped,
                    StocksUpserted: stocksUpserted,
                    RecipesCreated: recipesCreated,
                    RecipesUpdated: recipesUpdated,
                    RecipesSkipped: recipesSkipped,
                    BatchesCreated: batchesCreated,
                    BatchesUpdated: batchesUpdated,
                    BatchesSkipped: batchesSkipped,
                    TelemetryReadingsRestored: readingsRestored,
                    CollaboratorsRestored: collaboratorsRestored
                );

                _logger.LogInformation(
                    "Completed backup restore for user {UserId}: Setups +{SetupsCreated}/~{SetupsUpdated}, Equipment +{EqCreated}/~{EqUpdated}, Recipes +{RecipesCreated}/~{RecipesUpdated}, Batches +{BatchesCreated}/~{BatchesUpdated}",
                    userId, setupsCreated, setupsUpdated, eqCreated, eqUpdated, recipesCreated, recipesUpdated, batchesCreated, batchesUpdated);

                var resultDto = new RestoreResultDto(
                    Succeeded: true,
                    BackupSchemaVersion: envelope.SchemaVersion,
                    CurrentSchemaVersion: BackupSchemaConstants.CurrentSchemaVersion,
                    SettingsRestored: settingsRestored,
                    Summary: summary,
                    GeneratedInvites: generatedInvites,
                    Warnings: warnings);

                return (true, resultDto, null, null);
            }
            catch (Exception ex)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                _logger.LogError(ex, "Backup restore failed for user {UserId}", userId);
                return (false, null, "RESTORE_FAILED", $"Restore failed: {ex.Message}");
            }
        });
    }

    private EquipmentBackupDto MapEquipmentToBackupDto(Equipment e, bool includeSecrets, byte[]? encryptionKey)
    {
        string? encryptedToken = null;
        if (includeSecrets && encryptionKey != null && !string.IsNullOrWhiteSpace(e.ConnectionToken))
        {
            encryptedToken = BackupSecretProtector.EncryptSecret(e.ConnectionToken, encryptionKey);
        }

        var configJson = BackupSecretProtector.PrepareConfigJsonForExport(
            e.ConnectionConfigJson,
            includeSecrets,
            encryptionKey,
            _protector);

        return new EquipmentBackupDto(
            e.Id,
            e.BrewerySetupId,
            e.Name,
            e.Type,
            e.Subtype,
            e.Capacity,
            e.Unit,
            e.CapacityLiters,
            e.CurrentVolume,
            e.CurrentVolumeLiters,
            e.BoilOffRatePerHour,
            e.TrubLossLiters,
            e.MashTunDeadSpaceLiters,
            e.PackagingLossLiters,
            e.CurrentTemperatureC,
            e.CurrentSpecificGravity,
            e.CurrentPressureBar,
            e.CurrentBatteryPercent,
            e.CurrentBatteryVoltage,
            e.TemperatureUpdatedAt,
            e.LastTelemetryAt,
            e.LatestMetricsJson,
            e.ConnectionType,
            encryptedToken,
            configJson,
            e.Description,
            e.Notes,
            e.CreatedAt,
            e.UpdatedAt
        );
    }

    private static RecipeBackupDto MapRecipeToBackupDto(Recipe r)
    {
        return new RecipeBackupDto(
            r.Id,
            r.Name,
            r.Description,
            r.BeerStyle,
            r.BatchSizeLiters,
            r.BoilTimeMinutes,
            r.EfficiencyPercent,
            r.OriginalGravity,
            r.FinalGravity,
            r.AlcoholByVolume,
            r.BitternessIbu,
            r.ColorSrm,
            r.IsPublic,
            r.CreatedAt,
            r.UpdatedAt,
            r.Ingredients.Select(ri => new RecipeIngredientBackupDto(
                ri.Id,
                ri.IngredientId,
                ri.Ingredient?.Name ?? "Ingredient",
                ri.Ingredient?.Type ?? IngredientType.Fermentable,
                ri.Ingredient?.IsCatalogItem ?? true,
                ri.Amount,
                ri.Unit,
                ri.DurationMinutes,
                ri.Usage,
                ri.Notes,
                ri.Form,
                ri.Ingredient != null
                    ? new IngredientSnapshotDto(
                        ri.Ingredient.Name,
                        ri.Ingredient.Type,
                        ri.Ingredient.PotentialGravity,
                        ri.Ingredient.ColorSrm,
                        ri.Ingredient.AlphaAcidPercent,
                        ri.Ingredient.AttenuationPercent,
                        ri.Ingredient.Form,
                        ri.Ingredient.Description)
                    : null
            )).ToList(),
            r.MashSteps.OrderBy(ms => ms.StepOrder).Select(ms => new RecipeMashStepBackupDto(
                ms.Id,
                ms.StepOrder,
                ms.Name,
                ms.Type,
                ms.TemperatureC,
                ms.DurationMinutes,
                ms.RampTimeMinutes,
                ms.InfuseAmountLiters,
                ms.Notes
            )).ToList(),
            r.FermentationSteps.OrderBy(fs => fs.StepOrder).Select(fs => new RecipeFermentationStepBackupDto(
                fs.Id,
                fs.StepOrder,
                fs.Name,
                fs.Type,
                fs.TargetTemperatureC,
                fs.DurationDays,
                fs.RampTimeHours,
                fs.TriggerGravity,
                fs.Notes
            )).ToList()
        );
    }

    private static BatchBackupDto MapBatchToBackupDto(Batch b)
    {
        BatchVolumeProfileBackupDto? vpDto = null;
        if (b.VolumeProfile != null)
        {
            var vp = b.VolumeProfile;
            vpDto = new BatchVolumeProfileBackupDto(
                vp.TotalWaterLiters,
                vp.StrikeWaterLiters,
                vp.SpargeWaterLiters,
                vp.TargetPreBoilVolumeLiters,
                vp.MeasuredPreBoilVolumeLiters,
                vp.MeasuredPreBoilGravity,
                vp.TargetPostBoilVolumeLiters,
                vp.MeasuredPostBoilVolumeLiters,
                vp.TargetFermenterVolumeLiters,
                vp.MeasuredFermenterVolumeLiters,
                vp.TargetPackagedVolumeLiters,
                vp.MeasuredPackagedVolumeLiters,
                vp.BoilOffRatePerHour,
                vp.GrainAbsorptionRateLPerKg,
                vp.KettleTrubLossLiters,
                vp.FermenterTrubLossLiters,
                vp.MashTunDeadSpaceLiters,
                vp.CoolingShrinkagePercent,
                vp.PackagingLossLiters,
                vp.CreatedAt,
                vp.UpdatedAt);
        }

        return new BatchBackupDto(
            b.Id,
            b.RecipeId,
            b.BrewerySetupId,
            b.BatchCode,
            b.Name,
            b.BeerStyle,
            b.Status,
            b.CurrentStage,
            b.BrewDate,
            b.TargetOg,
            b.TargetFg,
            b.TargetAbv,
            b.TargetIbu,
            b.TargetColorSrm,
            b.TargetBatchSizeLiters,
            b.BoilTimeMinutes,
            b.EfficiencyPercent,
            b.MeasuredOg,
            b.CurrentGravity,
            b.MeasuredFg,
            b.AlcoholByVolume,
            b.BrewhouseEfficiency,
            b.MeasuredBatchSizeLiters,
            b.PitchTemperatureC,
            b.BoilerId,
            b.FermenterId,
            b.PackagingVesselId,
            b.Notes,
            b.CreatedAt,
            b.UpdatedAt,
            b.CompletedAt,
            vpDto,
            b.Ingredients.Select(bi => new BatchIngredientBackupDto(
                bi.Id,
                bi.SourceIngredientId,
                bi.Name,
                bi.Type,
                bi.Amount,
                bi.Unit,
                bi.AdditionStage,
                bi.AdditionTimeMinutes,
                bi.IsChecked,
                bi.IsDeducted,
                bi.DeductedAt,
                bi.Notes,
                bi.Form
            )).ToList(),
            b.MashSteps.OrderBy(ms => ms.StepOrder).Select(ms => new BatchMashStepBackupDto(
                ms.Id,
                ms.RecipeMashStepId,
                ms.StepOrder,
                ms.Name,
                ms.Type,
                ms.TargetTemperatureC,
                ms.DurationMinutes,
                ms.RampTimeMinutes,
                ms.InfuseAmountLiters,
                ms.ActualTemperatureC,
                ms.ActualDurationMinutes,
                ms.IsCompleted,
                ms.CompletedAt,
                ms.Notes
            )).ToList(),
            b.FermentationSteps.OrderBy(fs => fs.StepOrder).Select(fs => new BatchFermentationStepBackupDto(
                fs.Id,
                fs.RecipeFermentationStepId,
                fs.StepOrder,
                fs.Name,
                fs.Type,
                fs.TargetTemperatureC,
                fs.ActualTemperatureC,
                fs.DurationDays,
                fs.RampTimeHours,
                fs.TriggerGravity,
                fs.Notes,
                fs.IsCompleted,
                fs.StartedAt,
                fs.CompletedAt
            )).ToList(),
            b.StageHistory.Select(sh => new BatchStageHistoryBackupDto(
                sh.Id,
                sh.Stage,
                sh.Status,
                sh.StartedAt,
                sh.CompletedAt,
                sh.DurationMinutes,
                sh.Notes
            )).ToList(),
            b.Readings.OrderBy(r => r.Timestamp).Select(r => new BatchReadingBackupDto(
                r.Id,
                r.Timestamp,
                r.SpecificGravity,
                r.TemperatureC,
                r.Notes,
                r.CreatedAt
            )).ToList(),
            b.SensorAssignments.Select(sa => new BatchSensorAssignmentBackupDto(
                sa.Id,
                sa.EquipmentId,
                sa.Stage,
                sa.BatchMashStepId,
                sa.CreatedAt
            )).ToList()
        );
    }

    private (bool SecretsEncrypted, bool PassphraseRequired, bool PassphraseValid, byte[]? EncryptionKey) ValidateAndDeriveEncryptionKey(
        BrewYouBackupEnvelope envelope,
        BackupPayload payload,
        string? passphrase,
        List<string> warnings)
    {
        bool secretsEncrypted = envelope.Encryption?.SecretsEncrypted == true &&
                                !string.IsNullOrWhiteSpace(envelope.Encryption.SaltBase64);

        if (!secretsEncrypted)
        {
            return (false, false, true, null);
        }

        string? sampleEncryptedSecret = payload.UserSettings?.MqttPasswordEncrypted
            ?? payload.Equipment.Select(e => e.ConnectionTokenEncrypted).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        if (sampleEncryptedSecret == null)
        {
            foreach (var eq in payload.Equipment)
            {
                if (!string.IsNullOrWhiteSpace(eq.ConnectionConfigJson))
                {
                    try
                    {
                        var node = JsonNode.Parse(eq.ConnectionConfigJson) as JsonObject;
                        var encPwd = node?["passwordEncrypted"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(encPwd))
                        {
                            sampleEncryptedSecret = encPwd;
                            break;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        bool passphraseRequired = !string.IsNullOrWhiteSpace(sampleEncryptedSecret);
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            if (passphraseRequired)
            {
                warnings.Add("This backup contains encrypted hardware/MQTT secrets. Provide the backup passphrase to restore them, or proceed to restore without secrets.");
            }
            return (true, passphraseRequired, false, null);
        }

        try
        {
            var key = BackupSecretProtector.DeriveKey(
                passphrase,
                envelope.Encryption!.SaltBase64!,
                envelope.Encryption.Iterations > 0 ? envelope.Encryption.Iterations : BackupSecretProtector.DefaultIterations);

            if (passphraseRequired && !BackupSecretProtector.TryDecryptSecret(sampleEncryptedSecret!, key, out _))
            {
                warnings.Add("The supplied passphrase could not decrypt the backup's secrets.");
                return (true, true, false, null);
            }

            return (true, passphraseRequired, true, key);
        }
        catch
        {
            warnings.Add("Could not derive encryption key from the supplied passphrase.");
            return (true, passphraseRequired, false, null);
        }
    }

    private void ApplyUserSettings(
        ApplicationUser user,
        UserSettingsBackupDto s,
        byte[]? encryptionKey,
        List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(s.DisplayName) && !s.DisplayName.Contains('<') && !s.DisplayName.Contains('>'))
        {
            user.DisplayName = s.DisplayName.Trim();
        }

        var validLangs = new[] { "en", "sv", "de", "fr", "es" };
        if (!string.IsNullOrWhiteSpace(s.PreferredLanguage) && validLangs.Contains(s.PreferredLanguage.Trim().ToLowerInvariant()))
        {
            user.PreferredLanguage = s.PreferredLanguage.Trim().ToLowerInvariant();
        }

        user.PreferredVolumeUnit = s.PreferredVolumeUnit;
        user.PreferredWeightUnit = s.PreferredWeightUnit;
        user.PreferredGrainWeightUnit = s.PreferredGrainWeightUnit is "kg" or "lb"
            ? s.PreferredGrainWeightUnit
            : (s.PreferredWeightUnit == WeightUnit.Imperial ? "lb" : "kg");
        user.PreferredHopWeightUnit = s.PreferredHopWeightUnit is "g" or "oz"
            ? s.PreferredHopWeightUnit
            : (s.PreferredWeightUnit == WeightUnit.Imperial ? "oz" : "g");
        user.PreferredColorUnit = s.PreferredColorUnit is "SRM" or "EBC" ? s.PreferredColorUnit : "SRM";
        user.PreferredCarbonationUnit = s.PreferredCarbonationUnit is "Volumes" or "GramsPerLiter" ? s.PreferredCarbonationUnit : "Volumes";
        user.PreferredTemperatureUnit = s.PreferredTemperatureUnit;
        user.PreferredGravityUnit = s.PreferredGravityUnit;
        user.PreferredTheme = s.PreferredTheme;
        user.PreferredBitternessFormula = s.PreferredBitternessFormula is "Tinseth" or "Rager" or "Daniels" ? s.PreferredBitternessFormula : "Tinseth";
        user.PreferredColorFormula = s.PreferredColorFormula is "Morey" or "Mosher" or "Daniels" ? s.PreferredColorFormula : "Morey";
        user.PreferredAbvFormula = s.PreferredAbvFormula is "Linear" or "Advanced" ? s.PreferredAbvFormula : "Linear";

        if (s.DefaultBatchSizeLiters is >= 1m and <= 10_000m)
        {
            user.DefaultBatchSizeLiters = s.DefaultBatchSizeLiters;
        }
        if (s.DefaultEfficiencyPercent is >= 10m and <= 100m)
        {
            user.DefaultEfficiencyPercent = s.DefaultEfficiencyPercent;
        }
        if (s.DefaultBoilTimeMinutes is >= 0 and <= 360)
        {
            user.DefaultBoilTimeMinutes = s.DefaultBoilTimeMinutes;
        }

        user.MqttHost = string.IsNullOrWhiteSpace(s.MqttHost) ? null : s.MqttHost.Trim();
        user.MqttPort = s.MqttPort is >= 1 and <= 65535 ? s.MqttPort : 1883;
        user.MqttUsername = string.IsNullOrWhiteSpace(s.MqttUsername) ? null : s.MqttUsername.Trim();
        user.MqttCertificate = string.IsNullOrWhiteSpace(s.MqttCertificate) ? null : s.MqttCertificate.Trim();
        user.MqttTopicPrefix = string.IsNullOrWhiteSpace(s.MqttTopicPrefix) ? "brewyou/equipment" : s.MqttTopicPrefix.Trim().Trim('/');

        if (!string.IsNullOrWhiteSpace(s.MqttPasswordEncrypted))
        {
            if (encryptionKey != null && BackupSecretProtector.TryDecryptSecret(s.MqttPasswordEncrypted, encryptionKey, out var plainPwd) && !string.IsNullOrWhiteSpace(plainPwd))
            {
                user.MqttPassword = BackupSecretProtector.ProtectServerSecret(plainPwd, _protector);
            }
            else
            {
                warnings.Add("Skipped encrypted MQTT password because no valid passphrase was provided.");
            }
        }
    }

    private async Task ExecuteReplaceAllCleanupAsync(string userId)
    {
        var userBatches = await _db.Batches.Where(b => b.UserId == userId).ToListAsync();
        var userBatchIds = userBatches.Select(b => b.Id).ToList();

        var batchSensorAssignments = await _db.BatchSensorAssignments
            .Where(sa => userBatchIds.Contains(sa.BatchId))
            .ToListAsync();
        _db.BatchSensorAssignments.RemoveRange(batchSensorAssignments);

        _db.Batches.RemoveRange(userBatches);

        var userRecipes = await _db.Recipes.Where(r => r.UserId == userId).ToListAsync();
        _db.Recipes.RemoveRange(userRecipes);

        var userStocks = await _db.IngredientStocks.Where(s => s.UserId == userId).ToListAsync();
        _db.IngredientStocks.RemoveRange(userStocks);

        await _db.SaveChangesAsync();

        // Remove custom ingredients created by user that are not referenced by any remaining recipes
        var customIngredients = await _db.Ingredients
            .Where(i => !i.IsCatalogItem && i.CreatedByUserId == userId && !i.RecipeIngredients.Any())
            .ToListAsync();
        _db.Ingredients.RemoveRange(customIngredients);

        // Remove personal equipment and personal non-shared setups
        var personalSetups = await _db.BrewerySetups
            .Include(s => s.Members)
            .Include(s => s.Equipment)
            .Where(s => s.UserId == userId && !s.Members.Any())
            .ToListAsync();

        foreach (var setup in personalSetups)
        {
            var eqIds = setup.Equipment.Select(e => e.Id).ToList();
            var eqSensorAssignments = await _db.BatchSensorAssignments
                .Where(sa => eqIds.Contains(sa.EquipmentId))
                .ToListAsync();
            _db.BatchSensorAssignments.RemoveRange(eqSensorAssignments);
            _db.Equipment.RemoveRange(setup.Equipment);
            _db.BrewerySetups.Remove(setup);
        }

        await _db.SaveChangesAsync();
    }

    private async Task<(int RestoredMembers, List<RestoredInviteInfoDto> GeneratedInvites)> RestoreSetupCollaborationAsync(
        BrewerySetup targetSetup,
        BrewerySetupBackupDto setupDto,
        string ownerUserId,
        CollaboratorRestoreMode mode)
    {
        int restoredMembers = 0;
        var generatedInvites = new List<RestoredInviteInfoDto>();

        if (setupDto.Members != null)
        {
            foreach (var mDto in setupDto.Members)
            {
                if (mDto.UserId == ownerUserId) continue;

                ApplicationUser? targetUser = null;
                if (mode == CollaboratorRestoreMode.RestoreMembershipsIfAvailable)
                {
                    targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == mDto.UserId);
                    if (targetUser == null && !string.IsNullOrWhiteSpace(mDto.Email))
                    {
                        var normEmail = mDto.Email.Trim().ToLowerInvariant();
                        targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normEmail);
                    }
                }

                if (targetUser != null && targetUser.Id != ownerUserId)
                {
                    var alreadyMember = await _db.BreweryMembers.AnyAsync(m =>
                        m.BrewerySetupId == targetSetup.Id && m.UserId == targetUser.Id);
                    if (!alreadyMember)
                    {
                        _db.BreweryMembers.Add(new BreweryMember
                        {
                            Id = Guid.NewGuid(),
                            BrewerySetupId = targetSetup.Id,
                            UserId = targetUser.Id,
                            Role = mDto.Role,
                            JoinedAt = mDto.JoinedAt == default ? DateTime.UtcNow : mDto.JoinedAt
                        });
                        restoredMembers++;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(mDto.Email) && !mDto.Email.Contains('*'))
                {
                    var normEmail = mDto.Email.Trim().ToLowerInvariant();
                    var invite = await CreatePendingInviteIfMissingAsync(targetSetup, normEmail, mDto.Role, ownerUserId);
                    if (invite != null)
                    {
                        generatedInvites.Add(invite);
                    }
                }
            }
        }

        if (setupDto.PendingInvites != null)
        {
            foreach (var invDto in setupDto.PendingInvites)
            {
                if (string.IsNullOrWhiteSpace(invDto.InvitedEmail) || invDto.InvitedEmail.Contains('*'))
                {
                    continue;
                }
                var normEmail = invDto.InvitedEmail.Trim().ToLowerInvariant();
                var invite = await CreatePendingInviteIfMissingAsync(targetSetup, normEmail, invDto.Role, ownerUserId);
                if (invite != null)
                {
                    generatedInvites.Add(invite);
                }
            }
        }

        return (restoredMembers, generatedInvites);
    }

    private async Task<RestoredInviteInfoDto?> CreatePendingInviteIfMissingAsync(
        BrewerySetup setup,
        string normalizedEmail,
        BreweryRole role,
        string invitedByUserId)
    {
        var now = DateTime.UtcNow;
        var exists = await _db.BreweryInvites.AnyAsync(i =>
            i.BrewerySetupId == setup.Id &&
            i.InvitedEmail.ToLower() == normalizedEmail &&
            i.AcceptedAt == null &&
            i.ExpiresAt > now);

        if (exists) return null;

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var invite = new BreweryInvite
        {
            Id = Guid.NewGuid(),
            BrewerySetupId = setup.Id,
            InvitedEmail = normalizedEmail,
            InviteCode = code,
            Role = role,
            InvitedByUserId = invitedByUserId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(7)
        };
        _db.BreweryInvites.Add(invite);
        return new RestoredInviteInfoDto(setup.Id, setup.Name, normalizedEmail, role, code);
    }

    private async Task NormalizeDefaultSetupAsync(
        string userId,
        List<BrewerySetup> userOwnedSetups,
        List<BrewerySetupBackupDto> backupSetups,
        Dictionary<Guid, Guid> setupIdMap,
        string? preferredLanguage)
    {
        if (userOwnedSetups.Count == 0)
        {
            var autoSetup = new BrewerySetup
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = BrewerySetupService.GetDefaultBreweryName(preferredLanguage),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.BrewerySetups.Add(autoSetup);
            userOwnedSetups.Add(autoSetup);
            return;
        }

        var preferredBackupDefault = backupSetups.FirstOrDefault(s => s.IsDefault);
        Guid? desiredDefaultId = null;
        if (preferredBackupDefault != null && setupIdMap.TryGetValue(preferredBackupDefault.Id, out var mappedDefaultId))
        {
            desiredDefaultId = mappedDefaultId;
        }

        desiredDefaultId ??= userOwnedSetups.FirstOrDefault(s => s.IsDefault)?.Id ?? userOwnedSetups[0].Id;

        bool clearedOldDefault = false;
        foreach (var s in userOwnedSetups.Where(s => s.Id != desiredDefaultId && s.IsDefault))
        {
            s.IsDefault = false;
            clearedOldDefault = true;
        }

        if (clearedOldDefault)
        {
            await _db.SaveChangesAsync();
        }

        var desiredSetup = userOwnedSetups.FirstOrDefault(s => s.Id == desiredDefaultId);
        if (desiredSetup != null && !desiredSetup.IsDefault)
        {
            desiredSetup.IsDefault = true;
        }
    }

    private async Task<(string? ConnectionToken, string? ConnectionConfigJson)> RestoreEquipmentSecretsAsync(
        EquipmentBackupDto eqDto,
        byte[]? encryptionKey,
        Guid? existingEquipmentId,
        List<string> warnings)
    {
        string? token = null;
        if (!string.IsNullOrWhiteSpace(eqDto.ConnectionTokenEncrypted))
        {
            if (encryptionKey != null && BackupSecretProtector.TryDecryptSecret(eqDto.ConnectionTokenEncrypted, encryptionKey, out var decryptedToken) && !string.IsNullOrWhiteSpace(decryptedToken))
            {
                var tokenTaken = await _db.Equipment.AnyAsync(e =>
                    e.ConnectionToken == decryptedToken &&
                    (!existingEquipmentId.HasValue || e.Id != existingEquipmentId.Value));

                if (tokenTaken)
                {
                    token = EquipmentService.GenerateConnectionToken();
                    warnings.Add($"Regenerated webhook connection token for '{eqDto.Name}' because the original token was already in use.");
                }
                else
                {
                    token = decryptedToken;
                }
            }
            else if (eqDto.ConnectionType == EquipmentConnectionType.HttpPush)
            {
                token = EquipmentService.GenerateConnectionToken();
                warnings.Add($"Generated a new webhook connection token for '{eqDto.Name}' (original secret was encrypted).");
            }
        }
        else if (eqDto.ConnectionType == EquipmentConnectionType.HttpPush && !existingEquipmentId.HasValue)
        {
            token = EquipmentService.GenerateConnectionToken();
        }

        var (restoredConfig, failedDecrypt) = BackupSecretProtector.RestoreConfigJsonFromBackup(
            eqDto.ConnectionConfigJson,
            encryptionKey,
            _protector);

        if (failedDecrypt)
        {
            warnings.Add($"Skipped encrypted connection password for '{eqDto.Name}' because no valid passphrase was provided.");
        }

        return (token, restoredConfig);
    }

    private static async Task<List<RecipeIngredient>> BuildRecipeIngredientsAsync(
        Guid recipeId,
        List<RecipeIngredientBackupDto>? items,
        Func<Guid, string, IngredientType, IngredientSnapshotDto?, Task<Guid>> resolveIngredientAsync)
    {
        var result = new List<RecipeIngredient>();
        if (items == null) return result;

        foreach (var item in items)
        {
            var targetIngredientId = await resolveIngredientAsync(
                item.IngredientId,
                item.IngredientName,
                item.IngredientType,
                item.IngredientSnapshot);

            result.Add(new RecipeIngredient
            {
                Id = Guid.NewGuid(),
                RecipeId = recipeId,
                IngredientId = targetIngredientId,
                Amount = Math.Max(0.001m, item.Amount),
                Unit = string.IsNullOrWhiteSpace(item.Unit) ? "kg" : item.Unit.Trim(),
                DurationMinutes = item.DurationMinutes,
                Usage = item.Usage,
                Notes = item.Notes,
                Form = item.Form
            });
        }

        return result;
    }

    private static List<RecipeMashStep> BuildRecipeMashSteps(Guid recipeId, List<RecipeMashStepBackupDto>? steps)
    {
        if (steps == null) return [];
        return steps
            .OrderBy(s => s.StepOrder)
            .Select((s, index) => new RecipeMashStep
            {
                Id = Guid.NewGuid(),
                RecipeId = recipeId,
                StepOrder = index + 1,
                Name = string.IsNullOrWhiteSpace(s.Name) ? $"Step {index + 1}" : s.Name.Trim(),
                Type = s.Type,
                TemperatureC = s.TemperatureC,
                DurationMinutes = s.DurationMinutes,
                RampTimeMinutes = s.RampTimeMinutes,
                InfuseAmountLiters = s.InfuseAmountLiters,
                Notes = s.Notes
            })
            .ToList();
    }

    private static List<RecipeFermentationStep> BuildRecipeFermentationSteps(Guid recipeId, List<RecipeFermentationStepBackupDto>? steps)
    {
        if (steps == null) return [];
        return steps
            .OrderBy(s => s.StepOrder)
            .Select((s, index) => new RecipeFermentationStep
            {
                Id = Guid.NewGuid(),
                RecipeId = recipeId,
                StepOrder = index + 1,
                Name = string.IsNullOrWhiteSpace(s.Name) ? $"Step {index + 1}" : s.Name.Trim(),
                Type = s.Type,
                TargetTemperatureC = s.TargetTemperatureC,
                DurationDays = Math.Max(1, s.DurationDays),
                RampTimeHours = s.RampTimeHours,
                TriggerGravity = s.TriggerGravity,
                Notes = s.Notes
            })
            .ToList();
    }

    private static void PopulateBatchChildren(
        Batch batch,
        BatchBackupDto dto,
        Dictionary<Guid, Guid> ingredientIdMap,
        Dictionary<Guid, Guid> equipmentIdMap,
        Dictionary<Guid, Guid> batchMashStepIdMap)
    {
        if (dto.VolumeProfile != null)
        {
            var vp = dto.VolumeProfile;
            batch.VolumeProfile = new BatchVolumeProfile
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                TotalWaterLiters = vp.TotalWaterLiters,
                StrikeWaterLiters = vp.StrikeWaterLiters,
                SpargeWaterLiters = vp.SpargeWaterLiters,
                TargetPreBoilVolumeLiters = vp.TargetPreBoilVolumeLiters,
                MeasuredPreBoilVolumeLiters = vp.MeasuredPreBoilVolumeLiters,
                MeasuredPreBoilGravity = vp.MeasuredPreBoilGravity,
                TargetPostBoilVolumeLiters = vp.TargetPostBoilVolumeLiters,
                MeasuredPostBoilVolumeLiters = vp.MeasuredPostBoilVolumeLiters,
                TargetFermenterVolumeLiters = vp.TargetFermenterVolumeLiters,
                MeasuredFermenterVolumeLiters = vp.MeasuredFermenterVolumeLiters,
                TargetPackagedVolumeLiters = vp.TargetPackagedVolumeLiters,
                MeasuredPackagedVolumeLiters = vp.MeasuredPackagedVolumeLiters,
                BoilOffRatePerHour = vp.BoilOffRatePerHour,
                GrainAbsorptionRateLPerKg = vp.GrainAbsorptionRateLPerKg,
                KettleTrubLossLiters = vp.KettleTrubLossLiters,
                FermenterTrubLossLiters = vp.FermenterTrubLossLiters,
                MashTunDeadSpaceLiters = vp.MashTunDeadSpaceLiters,
                CoolingShrinkagePercent = vp.CoolingShrinkagePercent,
                PackagingLossLiters = vp.PackagingLossLiters,
                CreatedAt = vp.CreatedAt == default ? DateTime.UtcNow : vp.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            };
        }

        batch.Ingredients = (dto.Ingredients ?? []).Select(bi => new BatchIngredient
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            SourceIngredientId = bi.SourceIngredientId.HasValue && ingredientIdMap.TryGetValue(bi.SourceIngredientId.Value, out var sIngId)
                ? sIngId
                : bi.SourceIngredientId,
            Name = string.IsNullOrWhiteSpace(bi.Name) ? "Ingredient" : bi.Name.Trim(),
            Type = bi.Type,
            Amount = Math.Max(0.001m, bi.Amount),
            Unit = string.IsNullOrWhiteSpace(bi.Unit) ? "kg" : bi.Unit.Trim(),
            AdditionStage = bi.AdditionStage,
            AdditionTimeMinutes = bi.AdditionTimeMinutes,
            IsChecked = bi.IsChecked,
            IsDeducted = bi.IsDeducted,
            DeductedAt = bi.DeductedAt,
            Notes = bi.Notes,
            Form = bi.Form
        }).ToList();

        var mashSteps = new List<BatchMashStep>();
        int mashOrder = 1;
        foreach (var ms in (dto.MashSteps ?? []).OrderBy(x => x.StepOrder))
        {
            var newMsId = Guid.NewGuid();
            batchMashStepIdMap[ms.Id] = newMsId;
            mashSteps.Add(new BatchMashStep
            {
                Id = newMsId,
                BatchId = batch.Id,
                RecipeMashStepId = ms.RecipeMashStepId,
                StepOrder = mashOrder++,
                Name = string.IsNullOrWhiteSpace(ms.Name) ? $"Step {mashOrder - 1}" : ms.Name.Trim(),
                Type = ms.Type,
                TargetTemperatureC = ms.TargetTemperatureC,
                DurationMinutes = ms.DurationMinutes,
                RampTimeMinutes = ms.RampTimeMinutes,
                InfuseAmountLiters = ms.InfuseAmountLiters,
                ActualTemperatureC = ms.ActualTemperatureC,
                ActualDurationMinutes = ms.ActualDurationMinutes,
                IsCompleted = ms.IsCompleted,
                CompletedAt = ms.CompletedAt,
                Notes = ms.Notes
            });
        }
        batch.MashSteps = mashSteps;

        batch.FermentationSteps = (dto.FermentationSteps ?? [])
            .OrderBy(x => x.StepOrder)
            .Select((fs, idx) => new BatchFermentationStep
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                RecipeFermentationStepId = fs.RecipeFermentationStepId,
                StepOrder = idx + 1,
                Name = string.IsNullOrWhiteSpace(fs.Name) ? $"Step {idx + 1}" : fs.Name.Trim(),
                Type = fs.Type,
                TargetTemperatureC = fs.TargetTemperatureC,
                ActualTemperatureC = fs.ActualTemperatureC,
                DurationDays = Math.Max(1, fs.DurationDays),
                RampTimeHours = fs.RampTimeHours,
                TriggerGravity = fs.TriggerGravity,
                Notes = fs.Notes,
                IsCompleted = fs.IsCompleted,
                StartedAt = fs.StartedAt,
                CompletedAt = fs.CompletedAt
            }).ToList();

        batch.StageHistory = (dto.StageHistory ?? []).Select(sh => new BatchStageHistory
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Stage = sh.Stage,
            Status = sh.Status,
            StartedAt = sh.StartedAt,
            CompletedAt = sh.CompletedAt,
            DurationMinutes = sh.DurationMinutes,
            Notes = sh.Notes
        }).ToList();

        batch.Readings = (dto.Readings ?? []).Select(r => new BatchReading
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Timestamp = r.Timestamp,
            SpecificGravity = r.SpecificGravity,
            TemperatureC = r.TemperatureC,
            Notes = r.Notes,
            CreatedAt = r.CreatedAt == default ? DateTime.UtcNow : r.CreatedAt
        }).ToList();

        var sensorAssignments = new List<BatchSensorAssignment>();
        foreach (var sa in dto.SensorAssignments ?? [])
        {
            if (!equipmentIdMap.TryGetValue(sa.EquipmentId, out var targetEqId))
            {
                continue;
            }
            if (sensorAssignments.Any(x => x.EquipmentId == targetEqId))
            {
                continue;
            }
            Guid? targetMsId = sa.BatchMashStepId.HasValue && batchMashStepIdMap.TryGetValue(sa.BatchMashStepId.Value, out var mId)
                ? mId
                : null;

            sensorAssignments.Add(new BatchSensorAssignment
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                EquipmentId = targetEqId,
                Stage = sa.Stage,
                BatchMashStepId = targetMsId,
                CreatedAt = sa.CreatedAt == default ? DateTime.UtcNow : sa.CreatedAt
            });
        }
        batch.SensorAssignments = sensorAssignments;
    }

    private static string EnsureUniqueSetupName(string desiredName, List<BrewerySetup> existing)
    {
        var candidate = desiredName;
        int suffix = 2;
        while (existing.Any(s => string.Equals(s.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{desiredName} ({suffix++})";
        }
        return candidate.Length > 100 ? candidate[..100] : candidate;
    }

    private static string EnsureUniqueEquipmentName(string desiredName, Guid setupId, List<Equipment> existing)
    {
        var candidate = desiredName;
        int suffix = 2;
        while (existing.Any(e => e.BrewerySetupId == setupId && string.Equals(e.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{desiredName} ({suffix++})";
        }
        return candidate.Length > 100 ? candidate[..100] : candidate;
    }

    private static string EnsureUniqueRecipeName(string desiredName, List<Recipe> existing)
    {
        var candidate = desiredName;
        int suffix = 2;
        while (existing.Any(r => string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{desiredName} ({suffix++})";
        }
        return candidate.Length > 150 ? candidate[..150] : candidate;
    }

    private static string EnsureUniqueBatchCode(string desiredCode, List<Batch> existing)
    {
        var candidate = desiredCode;
        int suffix = 2;
        while (existing.Any(b => string.Equals(b.BatchCode, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{desiredCode}-{suffix++}";
        }
        return candidate.Length > 30 ? candidate[..30] : candidate;
    }

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return "***" + (atIndex >= 0 ? email[atIndex..] : "");
        return string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(atIndex - 1));
    }
}