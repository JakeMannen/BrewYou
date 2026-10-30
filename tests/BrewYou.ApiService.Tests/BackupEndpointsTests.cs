using BrewYou.ApiService.Backup.Models;
using BrewYou.ApiService.Backup.Versioning;
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class BackupEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BackupEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string UserId, string Email)> CreateAuthenticatedUserAsync(string? displayName = null)
    {
        var client = _factory.CreateClient();
        var email = $"backup_brewer_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, displayName ?? "Backup Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var token = envelope.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, envelope.Data.User.Id, email);
    }

    [Fact]
    public async Task BackupEndpoints_RequireAuthentication()
    {
        var client = _factory.CreateClient();

        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var dummyEnvelope = new BrewYouBackupEnvelope(
            BackupSchemaConstants.FormatIdentifier,
            BackupSchemaConstants.CurrentSchemaVersion,
            "1.6.1",
            DateTime.UtcNow,
            "abc",
            JsonSerializer.SerializeToElement(new { }));

        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", new PreviewRestoreRequest(dummyEnvelope));
        previewRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(dummyEnvelope, new RestoreOptionsDto()));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FullRoundTrip_ExportPreviewAndRestore_RestoresEquipmentIngredientsRecipesBatchesSettingsAndSecrets()
    {
        var (sourceClient, _, _) = await CreateAuthenticatedUserAsync("Master Source Brewer");

        // 1. Update granular preferences + MQTT password
        var prefUpdate = new UpdateUserPreferencesRequest(
            Language: "sv",
            VolumeUnit: VolumeUnit.Gallons,
            WeightUnit: WeightUnit.Imperial,
            TemperatureUnit: TemperatureUnit.Fahrenheit,
            GravityUnit: GravityUnit.Plato,
            Theme: ThemePreference.ChocolatePorter,
            DefaultBatchSizeLiters: 25.0m,
            DefaultEfficiencyPercent: 78.0m,
            DefaultBoilTimeMinutes: 75,
            MqttHost: "mqtt.craftbrew.local",
            MqttPort: 8883,
            MqttUsername: "hop_sensor",
            MqttPassword: "SuperSecretMqttPassword!",
            MqttTopicPrefix: "brewyou/cellar",
            GrainWeightUnit: "kg",
            HopWeightUnit: "oz",
            ColorUnit: "EBC",
            CarbonationUnit: "GramsPerLiter",
            BitternessFormula: "Rager",
            ColorFormula: "Mosher",
            AbvFormula: "Advanced");

        var prefRes = await sourceClient.PutAsJsonAsync("/api/v1/auth/me/preferences", prefUpdate);
        prefRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Get default setup and add equipment
        var setupsRes = await sourceClient.GetAsync("/api/v1/brewery-setups");
        var setups = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var defaultSetupId = setups[0].Id;

        var boilerRes = await sourceClient.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            defaultSetupId,
            "BrewZilla 65L Gen4",
            EquipmentType.Boiler,
            65.0m,
            Subtype: EquipmentSubtype.AllInOne,
            BoilOffRatePerHour: 4.2m,
            TrubLossLiters: 2.0m));
        boilerRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var fermenterRes = await sourceClient.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            defaultSetupId,
            "FermZilla Conical 27L",
            EquipmentType.Fermenter,
            27.0m,
            Subtype: EquipmentSubtype.PressureFermenter,
            ConnectionType: EquipmentConnectionType.HttpPush));
        fermenterRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var fermenter = (await fermenterRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 3. Create custom ingredient with initial stock
        var customIngRes = await sourceClient.PostAsJsonAsync("/api/v1/ingredients", new CreateIngredientRequest(
            Name: "Nelson Sauvin Reserve 2026",
            Type: IngredientType.Hop,
            PotentialGravity: null,
            ColorSrm: null,
            AlphaAcidPercent: 12.8m,
            AttenuationPercent: null,
            Description: "White wine and gooseberry aroma hop",
            InitialStock: 500m,
            StockUnit: "g",
            Form: "Pellet"));
        customIngRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var customHop = (await customIngRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;

        // 4. Create recipe using custom ingredient
        var recipeRes = await sourceClient.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Nordic Nelson Pale Ale",
            Description: "Crisp pale ale showcasing Nelson Sauvin.",
            BeerStyle: "American Pale Ale",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients:
            [
                new RecipeIngredientInputDto(customHop.Id, 50m, "g", 15, IngredientUsage.Boil, "Late hop", "Pellet")
            ],
            MashSteps:
            [
                new RecipeMashStepInputDto(1, "Saccharification", MashStepType.Infusion, 66.0m, 60)
            ],
            FermentationSteps:
            [
                new RecipeFermentationStepInputDto(1, "Primary", FermentationStepType.Primary, 19.0m, 10)
            ]));
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 5. Create batch linked to recipe and equipment + log gravity reading
        var batchRes = await sourceClient.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: null,
            Name: "Nordic Nelson Batch #1",
            BeerStyle: "American Pale Ale",
            BrewDate: null,
            TargetBatchSizeLiters: 20.0m,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: boiler.Id,
            FermenterId: fermenter.Id,
            PackagingVesselId: null,
            TargetVolumeBasis: null,
            PackagingLossLiters: null,
            MeasuredOg: 1.052m,
            PitchTemperatureC: 19.0m,
            Notes: "Clean brew day"));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        var readingRes = await sourceClient.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/readings",
            new AddBatchReadingRequest(DateTime.UtcNow, 1.020m, 19.5m, "Active krausen"));
        readingRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 6. Export backup with encrypted secrets
        const string passphrase = "CorrectHorseBatteryStaple!";
        var exportRes = await sourceClient.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest(
            IncludeTelemetryReadings: true,
            IncludeSecrets: true,
            Passphrase: passphrase));
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var exportEnvelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;
        exportEnvelope.Should().NotBeNull();
        exportEnvelope.SchemaVersion.Should().Be(BackupSchemaConstants.CurrentSchemaVersion);
        exportEnvelope.Encryption.Should().NotBeNull();
        exportEnvelope.Encryption!.SecretsEncrypted.Should().BeTrue();

        // 7. Create a fresh target user and preview the restore
        var (targetClient, _, _) = await CreateAuthenticatedUserAsync("Target Restore Brewer");

        var restoreOptions = new RestoreOptionsDto(
            ConflictResolution: RestoreConflictResolutionMode.MergeOverwriteExisting,
            RestoreUserSettings: true,
            RestoreSharedSetups: true,
            SharedSetupFallback: SharedSetupFallbackMode.CloneAsPersonal,
            CollaboratorRestore: CollaboratorRestoreMode.RestoreMembershipsIfAvailable,
            RestoreTelemetryReadings: true,
            Passphrase: passphrase);

        var previewRes = await targetClient.PostAsJsonAsync("/api/v1/backup/preview",
            new PreviewRestoreRequest(exportEnvelope, restoreOptions));
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = (await previewRes.Content.ReadFromJsonAsync<ApiResponse<RestorePreviewReportDto>>())!.Data!;
        preview.IsValid.Should().BeTrue();
        preview.ChecksumVerified.Should().BeTrue();
        preview.SecretsEncrypted.Should().BeTrue();
        preview.PassphraseValid.Should().BeTrue();
        preview.Counts.Equipment.Should().Be(2);
        preview.Counts.CustomIngredients.Should().Be(1);
        preview.Counts.Recipes.Should().Be(1);
        preview.Counts.Batches.Should().Be(1);

        // 8. Execute the restore
        var executeRes = await targetClient.PostAsJsonAsync("/api/v1/backup/restore",
            new ExecuteRestoreRequest(exportEnvelope, restoreOptions));
        executeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await executeRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        result.Succeeded.Should().BeTrue();
        result.SettingsRestored.Should().BeTrue();
        result.Summary.EquipmentCreated.Should().Be(2);
        result.Summary.RecipesCreated.Should().Be(1);
        result.Summary.BatchesCreated.Should().Be(1);

        // 9. Verify restored preferences (including UK/hybrid granular units & MQTT password)
        var targetMeRes = await targetClient.GetAsync("/api/v1/auth/me");
        var targetMe = (await targetMeRes.Content.ReadFromJsonAsync<ApiResponse<UserDto>>())!.Data!;
        targetMe.PreferredLanguage.Should().Be("sv");
        targetMe.Preferences!.VolumeUnit.Should().Be(VolumeUnit.Gallons);
        targetMe.Preferences.GrainWeightUnit.Should().Be("kg");
        targetMe.Preferences.HopWeightUnit.Should().Be("oz");
        targetMe.Preferences.ColorUnit.Should().Be("EBC");
        targetMe.Preferences.CarbonationUnit.Should().Be("GramsPerLiter");
        targetMe.Preferences.BitternessFormula.Should().Be("Rager");
        targetMe.Preferences.ColorFormula.Should().Be("Mosher");
        targetMe.Preferences.AbvFormula.Should().Be("Advanced");
        targetMe.Preferences.Theme.Should().Be(ThemePreference.ChocolatePorter);
        targetMe.Preferences.MqttHost.Should().Be("mqtt.craftbrew.local");
        targetMe.Preferences.HasMqttPassword.Should().BeTrue();

        // 10. Verify restored equipment, recipe, and batch links
        var targetEquipRes = await targetClient.GetAsync("/api/v1/inventory/equipment");
        var targetEquip = (await targetEquipRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        targetEquip.Should().HaveCount(2);
        targetEquip.Should().Contain(e => e.Name == "BrewZilla 65L Gen4" && e.BoilOffRatePerHour == 4.2m);
        targetEquip.Should().Contain(e => e.Name == "FermZilla Conical 27L" && !string.IsNullOrEmpty(e.ConnectionToken));

        var targetBatchesRes = await targetClient.GetAsync("/api/v1/batches");
        var targetBatches = (await targetBatchesRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchSummaryDto>>>())!.Data!;
        targetBatches.Should().ContainSingle(b => b.Name == "Nordic Nelson Batch #1");

        var restoredBatchDetailRes = await targetClient.GetAsync($"/api/v1/batches/{targetBatches[0].Id}");
        var restoredBatchDetail = (await restoredBatchDetailRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        restoredBatchDetail.Readings.Should().ContainSingle(r => r.SpecificGravity == 1.020m);
        restoredBatchDetail.BoilerName.Should().Be("BrewZilla 65L Gen4");
        restoredBatchDetail.FermenterName.Should().Be("FermZilla Conical 27L");
    }

    [Fact]
    public async Task SharedSetupsAndCollaboration_ExportAndRestore_HandlesOrphanedSharedSetupsAndCollaboratorInvites()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Club President");
        var (memberClient, _, memberEmail) = await CreateAuthenticatedUserAsync("Club Brewer");

        // Owner gets setup and adds shared equipment
        var ownerSetups = (await (await ownerClient.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var sharedSetupId = ownerSetups[0].Id;

        await ownerClient.PutAsJsonAsync($"/api/v1/brewery-setups/{sharedSetupId}",
            new UpdateBrewerySetupRequest("Stockholm Craft Collective", "Shared club system", true));

        await ownerClient.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            sharedSetupId,
            "Club 100L HERMS Kettle",
            EquipmentType.Boiler,
            100.0m,
            Subtype: EquipmentSubtype.HermsRims));

        // Owner invites member as Brewer and member accepts
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{sharedSetupId}/invites",
            new InviteMemberRequest(memberEmail, BreweryRole.Brewer));
        var invite = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!;

        var acceptRes = await memberClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept",
            new AcceptInviteRequest(invite.InviteCode));
        acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Member exports their backup (contains shared setup snapshot)
        var memberExportRes = await memberClient.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        var memberBackup = (await memberExportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        // Owner deletes the shared setup (making it orphaned for the member)
        await ownerClient.PostAsJsonAsync("/api/v1/brewery-setups", new CreateBrewerySetupRequest("Owner Solo Setup"));
        var delRes = await ownerClient.DeleteAsync($"/api/v1/brewery-setups/{sharedSetupId}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Member restores their backup with CloneAsPersonal fallback
        var restoreRes = await memberClient.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(
            memberBackup,
            new RestoreOptionsDto(
                ConflictResolution: RestoreConflictResolutionMode.MergeSkipExisting,
                RestoreSharedSetups: true,
                SharedSetupFallback: SharedSetupFallbackMode.CloneAsPersonal)));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreData = (await restoreRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        restoreData.Succeeded.Should().BeTrue();
        restoreData.Summary.SetupsCreated.Should().BeGreaterThanOrEqualTo(1);
        restoreData.Summary.EquipmentCreated.Should().Be(1);

        var memberSetupsAfter = (await (await memberClient.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        memberSetupsAfter.Should().Contain(s => s.Name.Contains("Stockholm Craft Collective") && s.IsOwner);
    }

    [Fact]
    public async Task BackwardsCompatibility_LegacyV0AndV1Backups_MigrateAndRestoreCleanlyOnNewerVersion()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Legacy Migrator");

        // Construct a legacy v0 backup JSON with:
        // - schemaVersion: 0
        // - legacy 'preferences' key instead of 'userSettings'
        // - equipment without ownedBrewerySetups array and with legacy enum alias ("Kettle")
        // - recipe referencing a non-existent old catalog ingredient GUID, plus legacy usage alias ("HopStand")
        var legacyEquipId = Guid.NewGuid();
        var staleCatalogGuid = Guid.NewGuid();
        var legacyRecipeId = Guid.NewGuid();

        var legacyDataJson = $$"""
        {
          "preferences": {
            "originalUserId": "legacy-user-1",
            "preferredLanguage": "en",
            "preferredVolumeUnit": "Liters",
            "preferredWeightUnit": "Imperial",
            "preferredTemperatureUnit": "Celsius",
            "preferredGravityUnit": "SpecificGravity",
            "preferredTheme": "ObsidianEmber",
            "defaultBatchSizeLiters": 23.0,
            "defaultEfficiencyPercent": 72.0,
            "defaultBoilTimeMinutes": 60
          },
          "equipment": [
            {
              "id": "{{legacyEquipId}}",
              "brewerySetupId": "00000000-0000-0000-0000-000000000000",
              "name": "Legacy 30L Kettle",
              "type": "Kettle",
              "subtype": "AllInOne",
              "capacity": 30.0,
              "unit": "Liters",
              "capacityLiters": 30.0,
              "connectionType": "None"
            }
          ],
          "recipes": [
            {
              "id": "{{legacyRecipeId}}",
              "name": "Vintage 2025 West Coast IPA",
              "beerStyle": "American IPA",
              "batchSizeLiters": 23.0,
              "boilTimeMinutes": 60,
              "efficiencyPercent": 72.0,
              "originalGravity": 1.065,
              "finalGravity": 1.012,
              "alcoholByVolume": 6.9,
              "bitternessIbu": 65.0,
              "colorSrm": 7.5,
              "isPublic": false,
              "ingredients": [
                {
                  "id": "{{Guid.NewGuid()}}",
                  "ingredientId": "{{staleCatalogGuid}}",
                  "ingredientName": "Citra",
                  "ingredientType": "Hop",
                  "isCatalogItem": true,
                  "amount": 60.0,
                  "unit": "g",
                  "durationMinutes": 20,
                  "usage": "HopStand"
                }
              ],
              "mashSteps": [],
              "fermentationSteps": []
            }
          ]
        }
        """;

        var dataElem = JsonSerializer.Deserialize<JsonElement>(legacyDataJson);
        var checksum = BackupMigrationPipeline.ComputeChecksumSha256(dataElem);

        var v0Envelope = new BrewYouBackupEnvelope(
            Format: BackupSchemaConstants.FormatIdentifier,
            SchemaVersion: 0,
            AppVersion: "0.9.0",
            ExportedAtUtc: DateTime.UtcNow.AddMonths(-6),
            ChecksumSha256: checksum,
            Data: dataElem);

        // 1. Preview the legacy v0 backup
        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", new PreviewRestoreRequest(v0Envelope));
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = (await previewRes.Content.ReadFromJsonAsync<ApiResponse<RestorePreviewReportDto>>())!.Data!;
        preview.IsValid.Should().BeTrue();
        preview.ChecksumVerified.Should().BeTrue();
        preview.BackupSchemaVersion.Should().Be(0);
        preview.CurrentSchemaVersion.Should().Be(BackupSchemaConstants.CurrentSchemaVersion);
        preview.WasUpgraded.Should().BeTrue();
        preview.MigrationNotes.Should().NotBeEmpty();
        preview.Counts.Equipment.Should().Be(1);
        preview.Counts.Recipes.Should().Be(1);

        // 2. Execute restore of legacy v0 backup
        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore",
            new ExecuteRestoreRequest(v0Envelope, new RestoreOptionsDto()));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Verify that v0 -> v1 -> v2 migrations populated granular units from Imperial WeightUnit and mapped legacy Theme alias
        var me = (await (await client.GetAsync("/api/v1/auth/me"))
            .Content.ReadFromJsonAsync<ApiResponse<UserDto>>())!.Data!;
        me.Preferences!.VolumeUnit.Should().Be(VolumeUnit.Liters);
        me.Preferences.Theme.Should().Be(ThemePreference.Obsidian);
        me.Preferences.GrainWeightUnit.Should().Be("lb");
        me.Preferences.HopWeightUnit.Should().Be("oz");

        // 4. Verify equipment & recipe were restored with remapped enum values and natural-key catalog match
        var equipList = (await (await client.GetAsync("/api/v1/inventory/equipment"))
            .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        equipList.Should().ContainSingle(e => e.Name == "Legacy 30L Kettle" && e.Type == EquipmentType.Boiler);

        var recipesRes = await client.GetAsync("/api/v1/recipes?scope=mine");
        var recipes = (await recipesRes.Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        recipes.Should().ContainSingle(r => r.Name == "Vintage 2025 West Coast IPA");

        var recipeDetail = (await (await client.GetAsync($"/api/v1/recipes/{recipes[0].Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        recipeDetail.Ingredients.Should().ContainSingle(i =>
            i.IngredientName == "Citra" && i.Usage == IngredientUsage.Whirlpool);
    }

    [Fact]
    public async Task PreviewRestore_ToleratesForwardSchemaVersionWithWarningAndIgnoresUnknownFields()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Forward Check Brewer");

        var forwardData = JsonSerializer.SerializeToElement(new
        {
            futureFeatureFlag = "quantum-fermentation",
            recipes = Array.Empty<object>()
        });
        var checksum = BackupMigrationPipeline.ComputeChecksumSha256(forwardData);

        var futureEnvelope = new BrewYouBackupEnvelope(
            Format: BackupSchemaConstants.FormatIdentifier,
            SchemaVersion: 99,
            AppVersion: "99.0.0",
            ExportedAtUtc: DateTime.UtcNow,
            ChecksumSha256: checksum,
            Data: forwardData);

        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", new PreviewRestoreRequest(futureEnvelope));
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = (await previewRes.Content.ReadFromJsonAsync<ApiResponse<RestorePreviewReportDto>>())!.Data!;
        preview.IsValid.Should().BeTrue();
        preview.Warnings.Should().Contain(w => w.Contains("newer schema version"));
    }

    [Fact]
    public async Task ConflictResolutionModes_SkipOverwriteAndReplaceAll_BehaveAsExpected()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Conflict Tester");
        var ingredients = (await (await client.GetAsync("/api/v1/ingredients"))
            .Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var citra = ingredients.First(i => i.Name == "Citra");

        // Create initial recipe ("House Pilsner", 20L)
        var createRes = await client.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "House Pilsner",
            Description: "Original 20L version",
            BeerStyle: "German Pils",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients:
            [
                new RecipeIngredientInputDto(citra.Id, 30m, "g", 60, IngredientUsage.Boil, "Bittering")
            ]));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var originalRecipe = (await createRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // Export backup containing "House Pilsner" (20L)
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        var backup = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        // Modify "House Pilsner" in live DB to 45L and add a second recipe "Extra Stout"
        await client.PutAsJsonAsync($"/api/v1/recipes/{originalRecipe.Id}", new UpdateRecipeRequest(
            Name: "House Pilsner",
            Description: "Modified 45L version",
            BeerStyle: "German Pils",
            BatchSizeLiters: 45.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients:
            [
                new RecipeIngredientInputDto(citra.Id, 60m, "g", 60, IngredientUsage.Boil, "Bittering")
            ]));

        await client.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Extra Stout",
            Description: "Added after backup",
            BeerStyle: "Irish Stout",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients:
            [
                new RecipeIngredientInputDto(citra.Id, 25m, "g", 60, IngredientUsage.Boil, "Bittering")
            ]));

        // 1. MergeSkipExisting should keep 45L version and skip
        var skipRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(
            backup,
            new RestoreOptionsDto(ConflictResolution: RestoreConflictResolutionMode.MergeSkipExisting)));
        var skipResult = (await skipRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        skipResult.Summary.RecipesSkipped.Should().Be(1);

        var pilsAfterSkip = (await (await client.GetAsync($"/api/v1/recipes/{originalRecipe.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        pilsAfterSkip.BatchSizeLiters.Should().Be(45.0m);

        // 2. MergeOverwriteExisting should revert "House Pilsner" to 20L while keeping "Extra Stout"
        var overwriteRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(
            backup,
            new RestoreOptionsDto(ConflictResolution: RestoreConflictResolutionMode.MergeOverwriteExisting)));
        var overwriteResult = (await overwriteRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        overwriteResult.Summary.RecipesUpdated.Should().Be(1);

        var pilsAfterOverwrite = (await (await client.GetAsync($"/api/v1/recipes/{originalRecipe.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        pilsAfterOverwrite.BatchSizeLiters.Should().Be(20.0m);

        var allAfterOverwrite = (await (await client.GetAsync("/api/v1/recipes?scope=mine"))
            .Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        allAfterOverwrite.Should().HaveCount(2);

        // 3. ReplaceAll should remove "Extra Stout" and leave only "House Pilsner"
        var replaceRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(
            backup,
            new RestoreOptionsDto(ConflictResolution: RestoreConflictResolutionMode.ReplaceAll)));
        replaceRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var allAfterReplace = (await (await client.GetAsync("/api/v1/recipes?scope=mine"))
            .Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        allAfterReplace.Should().ContainSingle(r => r.Name == "House Pilsner");
    }

    [Fact]
    public async Task ExportDeleteRecipeAndRestore_PreservesChecksumAfterBrowserNumberNormalizationAndRestoresDeletedRecipe()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Sommer Öl Bryggeri");

        var ingredients = (await (await client.GetAsync("/api/v1/ingredients"))
            .Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var pilsnerMalt = ingredients.First(i => i.Name == "Pilsner Malt");

        // 1. Create a recipe with trailing decimal zeros (as returned by PostgreSQL numeric(6,2)) and Unicode text
        var createRes = await client.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Märzen Festbier °C",
            Description: "Brewed at 66.00 °C",
            BeerStyle: "Märzen",
            BatchSizeLiters: 20.00m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.00m,
            IsPublic: false,
            Ingredients:
            [
                new RecipeIngredientInputDto(pilsnerMalt.Id, 4.500m, "kg", 60, IngredientUsage.Mash, "Base")
            ]));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdRecipe = (await createRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 2. Export backup
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportedEnvelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        // Simulate browser JSON.parse + JSON.stringify (which strips trailing decimal zeros 20.00 -> 20, 4.500 -> 4.5)
        var rawDataWithBrowserNormalizedNumbers = exportedEnvelope.Data.GetRawText()
            .Replace("20.00", "20")
            .Replace("75.00", "75")
            .Replace("4.500", "4.5")
            .Replace("0.9600", "0.96");
        using var normalizedDoc = JsonDocument.Parse(rawDataWithBrowserNormalizedNumbers);
        var browserNormalizedEnvelope = exportedEnvelope with { Data = normalizedDoc.RootElement.Clone() };

        // 3. Delete the recipe
        var delRes = await client.DeleteAsync($"/api/v1/recipes/{createdRecipe.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var recipesAfterDelete = (await (await client.GetAsync("/api/v1/recipes?scope=mine"))
            .Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        recipesAfterDelete.Should().BeEmpty();

        // 4. Preview restore using browser-normalized JSON and verify checksum still matches
        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", new PreviewRestoreRequest(browserNormalizedEnvelope));
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = (await previewRes.Content.ReadFromJsonAsync<ApiResponse<RestorePreviewReportDto>>())!.Data!;
        preview.IsValid.Should().BeTrue();
        preview.ChecksumVerified.Should().BeTrue();
        preview.Warnings.Should().NotContain(w => w.Contains("checksum", StringComparison.OrdinalIgnoreCase));

        // 5. Execute restore and verify the deleted recipe is restored
        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(browserNormalizedEnvelope));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoreResult = (await restoreRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        restoreResult.Succeeded.Should().BeTrue();
        restoreResult.Summary.RecipesCreated.Should().Be(1);

        var recipesAfterRestore = (await (await client.GetAsync("/api/v1/recipes?scope=mine"))
            .Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        recipesAfterRestore.Should().ContainSingle(r => r.Name == "Märzen Festbier °C");
    }

    [Fact]
    public async Task RestoreBackup_WithCustomNamedDefaultSetupAndEquipment_AdoptsUntouchedInitialSetupAndPopulatesEquipment()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Fresh Brewer");

        // Verify initial state: exactly one auto-generated setup with 0 equipment
        var initialSetupsRes = await client.GetAsync("/api/v1/brewery-setups");
        var initialSetups = (await initialSetupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        initialSetups.Should().ContainSingle();
        var initialSetupId = initialSetups[0].Id;

        var initialEquipRes = await client.GetAsync($"/api/v1/inventory/equipment?setupId={initialSetupId}");
        var initialEquip = (await initialEquipRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        initialEquip.Should().BeEmpty();

        // Load the backup fixture (contains setup 'Bryggan' and 3 equipment items)
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "base-2026-10-24.brewyou.json");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath("../../../Fixtures/base-2026-10-24.brewyou.json");
        }
        var rawJson = await File.ReadAllTextAsync(fixturePath);
        var envelope = JsonSerializer.Deserialize<BrewYouBackupEnvelope>(rawJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        // Execute restore
        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(envelope, new RestoreOptionsDto
        {
            ConflictResolution = RestoreConflictResolutionMode.MergeSkipExisting
        }));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoreResult = (await restoreRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        restoreResult.Succeeded.Should().BeTrue();
        restoreResult.Summary.EquipmentCreated.Should().Be(3);

        // Verify setups: the untouched placeholder was adopted into "Bryggan", no zombie duplicate setup
        var postSetupsRes = await client.GetAsync("/api/v1/brewery-setups");
        var postSetups = (await postSetupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        postSetups.Should().ContainSingle();
        postSetups[0].Name.Should().Be("Bryggan");
        postSetups[0].IsDefault.Should().BeTrue();
        postSetups[0].Id.Should().Be(initialSetupId); // Preserved ID matches client active setup

        // Verify Equipment tab query (with setupId): returns all 3 vessels
        var setupEquipRes = await client.GetAsync($"/api/v1/inventory/equipment?setupId={postSetups[0].Id}");
        var setupEquip = (await setupEquipRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        setupEquip.Should().HaveCount(3);
        setupEquip.Should().Contain(e => e.Name == "Grainfather G30" && e.Type == EquipmentType.Boiler);
        setupEquip.Should().Contain(e => e.Name == "Grainfather GF30" && e.Type == EquipmentType.Fermenter);
        setupEquip.Should().Contain(e => e.Name == "Corneliusfat 1" && e.Type == EquipmentType.Keg);

        // Verify global equipment query (without setupId, e.g. start batch wizard): returns all 3 vessels
        var globalEquipRes = await client.GetAsync("/api/v1/inventory/equipment");
        var globalEquip = (await globalEquipRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        globalEquip.Should().HaveCount(3);
    }

    [Fact]
    public async Task RestoreBackup_WhenUserAlreadyHasEquipmentInExistingSetup_PreservesExistingSetupAndCreatesNewSetup()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Experienced Brewer");

        // User already created equipment in their initial setup
        var initialSetups = (await (await client.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var existingSetupId = initialSetups[0].Id;

        await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            existingSetupId,
            "Existing Kettle 30L",
            EquipmentType.Boiler,
            30.0m));

        // Load backup fixture containing "Bryggan" and 3 equipment
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "base-2026-10-24.brewyou.json");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath("../../../Fixtures/base-2026-10-24.brewyou.json");
        }
        var rawJson = await File.ReadAllTextAsync(fixturePath);
        var envelope = JsonSerializer.Deserialize<BrewYouBackupEnvelope>(rawJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        // Execute restore
        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(envelope, new RestoreOptionsDto
        {
            ConflictResolution = RestoreConflictResolutionMode.MergeSkipExisting
        }));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // User now has 2 setups: their existing setup + the restored "Bryggan" setup
        var postSetups = (await (await client.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        postSetups.Should().HaveCount(2);

        var existingSetup = postSetups.First(s => s.Id == existingSetupId);
        var brygganSetup = postSetups.First(s => s.Name == "Bryggan");
        brygganSetup.IsDefault.Should().BeTrue();

        // Equipment in existing setup is preserved
        var existingEquip = (await (await client.GetAsync($"/api/v1/inventory/equipment?setupId={existingSetup.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        existingEquip.Should().ContainSingle(e => e.Name == "Existing Kettle 30L");

        // Equipment in restored setup is present
        var brygganEquip = (await (await client.GetAsync($"/api/v1/inventory/equipment?setupId={brygganSetup.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        brygganEquip.Should().HaveCount(3);

        // Global query returns all 4 equipment across both setups
        var allEquip = (await (await client.GetAsync("/api/v1/inventory/equipment"))
            .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        allEquip.Should().HaveCount(4);
    }

    [Fact]
    public async Task FullRoundTrip_MultiVesselBatch_PreservesHltMashTunAndMultiVesselLosses()
    {
        var (client, userId, email) = await CreateAuthenticatedUserAsync("Multi-Vessel Brewer");

        // 1. Fetch default brewery setup
        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        var setups = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = setups.First(s => s.IsDefault).Id;

        // 2. Create HLT, MashTun, and Boiler equipment
        var hltRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "DigiBoil 35L HLT",
            Type: EquipmentType.Boiler,
            Capacity: 35.0m,
            Subtype: EquipmentSubtype.Hlt
        ));
        hltRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var hlt = (await hltRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var mtRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Insulated 38L Mash Tun",
            Type: EquipmentType.Boiler,
            Capacity: 38.0m,
            Subtype: EquipmentSubtype.MashTun,
            MashTunDeadSpaceLiters: 1.2m
        ));
        mtRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var mashTun = (await mtRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "BrewZilla 35L Gen 4",
            Type: EquipmentType.Boiler,
            Capacity: 35.0m,
            Subtype: EquipmentSubtype.AllInOne
        ));
        boilerRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 3. Create Multi-Vessel Batch
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-MV-01",
            Name: "West Coast IPA (3V Setup)",
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.065m,
            TargetFg: 1.012m,
            TargetAbv: 7.0m,
            TargetIbu: 60.0m,
            TargetColorSrm: 6.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 78.0m,
            BoilerId: boiler.Id,
            MashTunId: mashTun.Id,
            HltId: hlt.Id,
            FermenterId: null,
            HltDeadSpaceLiters: 0.8m,
            PlumbingLossLiters: 0.4m,
            SpargeMethod: "Fly"
        ));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBatch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        createdBatch.HltId.Should().Be(hlt.Id);
        createdBatch.MashTunId.Should().Be(mashTun.Id);
        createdBatch.BoilerId.Should().Be(boiler.Id);
        createdBatch.VolumeProfile.Should().NotBeNull();
        createdBatch.VolumeProfile!.HltDeadSpaceLiters.Should().Be(0.8m);
        createdBatch.VolumeProfile!.PlumbingLossLiters.Should().Be(0.4m);
        createdBatch.VolumeProfile!.SpargeMethod.Should().Be("Fly");

        // 4. Export backup
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportEnvelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;
        exportEnvelope.Should().NotBeNull();

        // 5. Restore backup onto fresh user
        var (client2, userId2, email2) = await CreateAuthenticatedUserAsync("Restored Multi-Vessel Brewer");
        var restoreRes = await client2.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(
            exportEnvelope,
            new RestoreOptionsDto(ConflictResolution: RestoreConflictResolutionMode.ReplaceAll)
        ));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Verify restored batch preserves HLT, MashTun, and volume profile
        var batchesRes2 = await client2.GetAsync("/api/v1/batches");
        var batches2 = (await batchesRes2.Content.ReadFromJsonAsync<ApiResponse<List<BatchSummaryDto>>>())!.Data!;
        batches2.Should().ContainSingle(b => b.BatchCode == "B-MV-01");

        var restoredBatchDetailRes = await client2.GetAsync($"/api/v1/batches/{batches2[0].Id}");
        var restoredBatch = (await restoredBatchDetailRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        restoredBatch.HltId.Should().NotBeNull();
        restoredBatch.MashTunId.Should().NotBeNull();
        restoredBatch.BoilerId.Should().NotBeNull();
        restoredBatch.HltName.Should().Be("DigiBoil 35L HLT");
        restoredBatch.MashTunName.Should().Be("Insulated 38L Mash Tun");
        restoredBatch.BoilerName.Should().Be("BrewZilla 35L Gen 4");
        restoredBatch.VolumeProfile.Should().NotBeNull();
        restoredBatch.VolumeProfile!.HltDeadSpaceLiters.Should().Be(0.8m);
        restoredBatch.VolumeProfile!.PlumbingLossLiters.Should().Be(0.4m);
        restoredBatch.VolumeProfile!.SpargeMethod.Should().Be("Fly");
    }

    [Fact]
    public async Task RoundTrip_RecipeWithFirstWortUsageAndNotes_RestoresUsageAndNotesAccurately()
    {
        var (sourceClient, _, _) = await CreateAuthenticatedUserAsync("FWH Source Brewer");
        var ingredientsRes = await sourceClient.GetAsync("/api/v1/ingredients");
        var ingredients = (await ingredientsRes.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var pilsner = ingredients.First(i => i.Type == IngredientType.Fermentable);
        var hop = ingredients.First(i => i.Type == IngredientType.Hop);

        var createRes = await sourceClient.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Bohemian First Wort Pilsner",
            Description: "Traditional FWH noble hop lager",
            BeerStyle: "Czech Premium Pale Lager",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 74.0m,
            IsPublic: false,
            Ingredients:
            [
                new RecipeIngredientInputDto(pilsner.Id, 4.6m, "kg", 90, IngredientUsage.Mash, "Floor-malted base"),
                new RecipeIngredientInputDto(hop.Id, 35.0m, "g", 90, IngredientUsage.FirstWort, "First wort kettle addition", "Leaf")
            ]));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var exportRes = await sourceClient.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportEnvelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        var (targetClient, _, _) = await CreateAuthenticatedUserAsync("FWH Restored Brewer");
        var restoreRes = await targetClient.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(
            exportEnvelope,
            new RestoreOptionsDto(ConflictResolution: RestoreConflictResolutionMode.ReplaceAll)));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var listRes = await targetClient.GetAsync("/api/v1/recipes?scope=mine");
        var list = (await listRes.Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        var summary = list.Should().ContainSingle(r => r.Name == "Bohemian First Wort Pilsner").Subject;

        var detailRes = await targetClient.GetAsync($"/api/v1/recipes/{summary.Id}");
        var detail = (await detailRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        var fwhHop = detail.Ingredients.Should().ContainSingle(i => i.Usage == IngredientUsage.FirstWort).Subject;
        fwhHop.Notes.Should().Be("First wort kettle addition");
        fwhHop.Form.Should().Be("Leaf");
    }
}