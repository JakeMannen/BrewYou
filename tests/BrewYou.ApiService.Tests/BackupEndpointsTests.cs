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
}