using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class TelemetryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TelemetryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    private async Task<(HttpClient Client, string UserId, Guid DefaultSetupId)> CreateUserAsync()
    {
        var client = _factory.CreateClient();
        var email = $"telemetry_user_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "Telemetry Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        var token = envelope!.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        return (client, envelope.Data.User.Id, defaultSetupId);
    }

    [Fact]
    public void ParseTemperature_GenericJsonCelsius_ExtractsCorrectly()
    {
        var json = "{\"temperature\": 21.5, \"unit\": \"C\"}";
        using var doc = JsonDocument.Parse(json);

        var result = TelemetryService.ParseTemperature(doc.RootElement);

        result.Success.Should().BeTrue();
        result.TemperatureC.Should().Be(21.5m);
        result.DetectedFormat.Should().Be("GenericJson");
    }

    [Fact]
    public void ParseTemperature_GenericJsonFahrenheit_ConvertsToCelsius()
    {
        var json = "{\"temp\": 68.0, \"unit\": \"F\"}";
        using var doc = JsonDocument.Parse(json);

        var result = TelemetryService.ParseTemperature(doc.RootElement);

        result.Success.Should().BeTrue();
        result.TemperatureC.Should().Be(20.0m); // (68 - 32) * 5/9 = 20.0
        result.DetectedFormat.Should().Be("GenericJson");
    }

    [Fact]
    public void ParseTemperature_iSpindelFormat_ExtractsAndConverts()
    {
        var json = "{\"name\": \"iSpindel01\", \"temperature\": 18.3, \"temp_units\": \"C\", \"gravity\": 1.052, \"battery\": 4.12}";
        using var doc = JsonDocument.Parse(json);

        var result = TelemetryService.ParseTemperature(doc.RootElement);

        result.Success.Should().BeTrue();
        result.TemperatureC.Should().Be(18.3m);
        result.DetectedFormat.Should().Be("iSpindel");
    }

    [Fact]
    public void ParseTemperature_TiltFormat_AutoConvertsFromFahrenheit()
    {
        var json = "{\"color\": \"RED\", \"temp\": 68.0, \"gravity\": 1.045}";
        using var doc = JsonDocument.Parse(json);

        var result = TelemetryService.ParseTemperature(doc.RootElement);

        result.Success.Should().BeTrue();
        result.TemperatureC.Should().Be(20.0m);
        result.DetectedFormat.Should().Be("TiltHydrometer");
    }

    [Fact]
    public void ParseTemperature_CustomJsonPath_ExtractsNestedValue()
    {
        var json = "{\"sensor\": {\"readings\": {\"status\": 65.5}}, \"unit\": \"F\"}";
        using var doc = JsonDocument.Parse(json);

        var result = TelemetryService.ParseTemperature(doc.RootElement, "sensor.readings.status");

        result.Success.Should().BeTrue();
        result.TemperatureC.Should().Be(18.61m); // (65.5 - 32) * 5/9 ~= 18.61
        result.DetectedFormat.Should().Be("CustomPath");
    }

    [Fact]
    public void ValidatePollUrl_BlocksCloudMetadataAndBadSchemes()
    {
        TelemetryService.ValidatePollUrl("http://169.254.169.254/latest/meta-data/").IsValid.Should().BeFalse();
        TelemetryService.ValidatePollUrl("ftp://example.com/temp").IsValid.Should().BeFalse();
        TelemetryService.ValidatePollUrl("http://192.168.1.50/status").IsValid.Should().BeTrue();
        TelemetryService.ValidatePollUrl("https://api.brewery.test/temp").IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task IngestionEndpoint_WithValidToken_UpdatesTemperatureAndRecordsReading()
    {
        var (client, _, setupId) = await CreateUserAsync();

        // 1. Create equipment with HttpPush connection type
        var createReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Fermenter Telemetry {Guid.NewGuid():N}",
            Type: EquipmentType.Fermenter,
            Capacity: 30,
            ConnectionType: EquipmentConnectionType.HttpPush
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        created.ConnectionToken.Should().NotBeNullOrWhiteSpace();
        created.CurrentTemperatureC.Should().BeNull();

        // 2. Ingest telemetry using unauthenticated client and token
        var publicClient = _factory.CreateClient();
        var payload = new { temperature = 19.5, unit = "C" };
        var ingestRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{created.ConnectionToken}", payload);
        ingestRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Verify equipment reflects latest temperature
        var getRes = await client.GetAsync($"/api/v1/inventory/equipment/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = (await getRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        fetched.CurrentTemperatureC.Should().Be(19.5m);
        fetched.TemperatureUpdatedAt.Should().NotBeNull();

        // 4. Verify reading history contains the entry
        var readingsRes = await client.GetAsync($"/api/v1/inventory/equipment/{created.Id}/readings");
        readingsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readingsEnv = await readingsRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentReadingDto>>>();
        readingsEnv!.Data.Should().ContainSingle();
        readingsEnv.Data![0].TemperatureC.Should().Be(19.5m);
        readingsEnv.Data[0].Source.Should().Be("GenericJson");
    }

    [Fact]
    public async Task IngestionEndpoint_WithInvalidToken_ReturnsNotFound()
    {
        var publicClient = _factory.CreateClient();
        var payload = new { temperature = 20.0, unit = "C" };
        var ingestRes = await publicClient.PostAsJsonAsync("/api/v1/telemetry/equipment/non_existent_token_12345", payload);
        ingestRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IngestionEndpoint_WithOutOfRangeTemperature_ReturnsBadRequest()
    {
        var (client, _, setupId) = await CreateUserAsync();

        var createReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Boiler OutOfRange {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 50,
            ConnectionType: EquipmentConnectionType.HttpPush
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var publicClient = _factory.CreateClient();
        var payload = new { temperature = 250.0, unit = "C" };
        var ingestRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{created.ConnectionToken}", payload);
        ingestRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegenerateToken_RotatesTokenAndInvalidatesOldToken()
    {
        var (client, _, setupId) = await CreateUserAsync();

        var createReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Fermenter TokenRotation {Guid.NewGuid():N}",
            Type: EquipmentType.Fermenter,
            Capacity: 30,
            ConnectionType: EquipmentConnectionType.HttpPush
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var oldToken = created.ConnectionToken!;

        // Regenerate token
        var regenRes = await client.PostAsync($"/api/v1/inventory/equipment/{created.Id}/regenerate-token", null);
        regenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await regenRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var newToken = updated.ConnectionToken!;

        newToken.Should().NotBe(oldToken);

        var publicClient = _factory.CreateClient();
        var payload = new { temperature = 21.0, unit = "C" };

        // Old token should fail
        var oldRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{oldToken}", payload);
        oldRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // New token should succeed
        var newRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{newToken}", payload);
        newRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TelemetryManagement_EnforcesBOLAIsolation()
    {
        var (clientA, _, setupIdA) = await CreateUserAsync();
        var (clientB, _, _) = await CreateUserAsync();

        var createReq = new CreateEquipmentRequest(
            BrewerySetupId: setupIdA,
            Name: $"UserA Vessel {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 40,
            ConnectionType: EquipmentConnectionType.HttpPush
        );

        var createRes = await clientA.PostAsJsonAsync("/api/v1/inventory/equipment", createReq);
        var equipmentA = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // User B attempts to view readings of User A's equipment -> 404 Not Found
        var bReadingsRes = await clientB.GetAsync($"/api/v1/inventory/equipment/{equipmentA.Id}/readings");
        bReadingsRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User B attempts to regenerate token for User A's equipment -> 404 Not Found
        var bRegenRes = await clientB.PostAsync($"/api/v1/inventory/equipment/{equipmentA.Id}/regenerate-token", null);
        bRegenRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TelemetryIngest_WhenEquipmentUsedInActiveBatchMash_AutoAssociatesBatchAndMashStep()
    {
        var (client, _, setupId) = await CreateUserAsync();

        // 1. Create boiler equipment
        var createEqReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"AutoMash Kettle {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 50,
            ConnectionType: EquipmentConnectionType.HttpPush
        );
        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", createEqReq);
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var token = eq.ConnectionToken!;

        // 2. Create batch with custom mash steps assigning BoilerId
        var mashSteps = new List<BatchMashStepInputDto>
        {
            new(1, "Dough-In", MashStepType.Infusion, 45.0m, 15),
            new(2, "Saccharification", MashStepType.Temperature, 65.0m, 60)
        };
        var createBatchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Batch With Telemetry",
            BeerStyle: "IPA",
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: eq.Id,
            FermenterId: null,
            CustomMashSteps: mashSteps
        );
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", createBatchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 3. Ingest telemetry from external device
        var publicClient = _factory.CreateClient();
        var payload = new { temperature = 45.3, unit = "C" };
        var ingestRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}", payload);
        ingestRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Query batch equipment readings
        var batchReadingsRes = await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings");
        batchReadingsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readingsEnv = await batchReadingsRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>();

        readingsEnv!.Data.Should().NotBeNull();
        readingsEnv.Data.Should().HaveCount(1);
        var reading = readingsEnv.Data!.First();
        reading.BatchId.Should().Be(batch.Id);
        reading.EquipmentId.Should().Be(eq.Id);
        reading.Stage.Should().Be(BrewStage.Mash);
        reading.StepName.Should().Be("Dough-In");
        reading.BatchMashStepId.Should().Be(batch.MashSteps[0].Id);
        reading.TemperatureC.Should().Be(45.3m);
        reading.TargetTemperatureC.Should().Be(45.0m);

        // Verify the active mash step's ActualTemperatureC was updated
        var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        var freshBatch = (await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        freshBatch.MashSteps[0].ActualTemperatureC.Should().Be(45.3m);
    }

    [Fact]
    public async Task LogBatchTemperature_Manual_RecordsReadingForStep()
    {
        var (client, _, setupId) = await CreateUserAsync();

        var createEqReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Manual Test Kettle {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 30
        );
        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", createEqReq);
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var createBatchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Manual Log Batch",
            BeerStyle: null,
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: eq.Id,
            FermenterId: null
        );
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", createBatchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // Manually log reading for Boil stage
        var logReq = new LogBatchTemperatureRequest(
            TemperatureC: 99.8m,
            EquipmentId: eq.Id,
            Stage: BrewStage.Boil,
            StepName: "Vigorous Boil",
            Notes: "Rolling boil achieved"
        );
        var logRes = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings", logReq);
        logRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readingDto = (await logRes.Content.ReadFromJsonAsync<ApiResponse<BatchEquipmentReadingDto>>())!.Data!;

        readingDto.BatchId.Should().Be(batch.Id);
        readingDto.Stage.Should().Be(BrewStage.Boil);
        readingDto.StepName.Should().Be("Vigorous Boil");
        readingDto.TemperatureC.Should().Be(99.8m);
        readingDto.TargetTemperatureC.Should().Be(100.0m);
        readingDto.Notes.Should().Be("Rolling boil achieved");
    }

    [Fact]
    public async Task GetBatchEquipmentReadings_FiltersByStageAndStep()
    {
        var (client, _, setupId) = await CreateUserAsync();

        var createEqReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Filter Kettle {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 40
        );
        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", createEqReq);
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var mashSteps = new List<BatchMashStepInputDto>
        {
            new(1, "Step A", MashStepType.Temperature, 50.0m, 20),
            new(2, "Step B", MashStepType.Temperature, 65.0m, 45)
        };
        var createBatchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Filter Test Batch",
            BeerStyle: null,
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: eq.Id,
            FermenterId: null,
            CustomMashSteps: mashSteps
        );
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", createBatchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        var stepAId = batch.MashSteps[0].Id;
        var stepBId = batch.MashSteps[1].Id;

        // Log reading for Step A
        var logA = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings",
            new LogBatchTemperatureRequest(50.2m, eq.Id, BrewStage.Mash, stepAId, "Step A"));
        logA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Log reading for Step B
        var logB = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings",
            new LogBatchTemperatureRequest(65.1m, eq.Id, BrewStage.Mash, stepBId, "Step B"));
        logB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Log reading for Boil
        var logBoil = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings",
            new LogBatchTemperatureRequest(100.0m, eq.Id, BrewStage.Boil, null, "Boil"));
        logBoil.StatusCode.Should().Be(HttpStatusCode.OK);

        // 1. Filter by Mash stage -> should return 2 readings
        var mashRes = await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings?stage=Mash");
        var mashReadings = (await mashRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        mashReadings.Should().HaveCount(2);

        // 2. Filter by Step A -> should return 1 reading
        var stepARes = await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings?stepId={stepAId}");
        var stepAReadings = (await stepARes.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        stepAReadings.Should().HaveCount(1);
        stepAReadings[0].StepName.Should().Be("Step A");

        // 3. Filter by Boil stage -> should return 1 reading
        var boilRes = await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings?stage=Boil");
        var boilReadings = (await boilRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        boilReadings.Should().HaveCount(1);
        boilReadings[0].TemperatureC.Should().Be(100.0m);
    }

    [Fact]
    public async Task BatchEquipmentReadings_EnforcesBOLAIsolation()
    {
        var (clientA, _, setupIdA) = await CreateUserAsync();
        var (clientB, _, _) = await CreateUserAsync();

        var batchRes = await clientA.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "User A Private Batch",
            BeerStyle: null,
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: null,
            FermenterId: null
        ));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batchA = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // User B attempts to read User A's batch equipment readings -> 404
        var bReadRes = await clientB.GetAsync($"/api/v1/batches/{batchA.Id}/equipment-readings");
        bReadRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User B attempts to log reading on User A's batch -> 404
        var bLogRes = await clientB.PostAsJsonAsync($"/api/v1/batches/{batchA.Id}/equipment-readings",
            new LogBatchTemperatureRequest(65.0m));
        bLogRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TelemetryIngestion_WhenAdvancingFromBoilToFerment_BoilerStopsLoggingToBatchAndFermenterTakesOver()
    {
        var (client, _, setupId) = await CreateUserAsync();

        // 1. Create boiler and fermenter with connection tokens
        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Boiler {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 50,
            ConnectionType: EquipmentConnectionType.HttpPush
        ));
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var boilerToken = boiler.ConnectionToken!;

        var fermenterRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Fermenter {Guid.NewGuid():N}",
            Type: EquipmentType.Fermenter,
            Capacity: 30,
            ConnectionType: EquipmentConnectionType.HttpPush
        ));
        var fermenter = (await fermenterRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var fermenterToken = fermenter.ConnectionToken!;

        // 2. Create batch assigned to both equipment
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Progression Test Batch",
            BeerStyle: "IPA",
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: boiler.Id,
            FermenterId: fermenter.Id
        ));
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 3. Advance to Boil stage
        var advanceToBoil = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage",
            new AdvanceBatchStageRequest(BrewStage.Boil));
        advanceToBoil.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Ingest boiler telemetry during Boil
        var publicClient = _factory.CreateClient();
        var boilReadingRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{boilerToken}",
            new { temperature = 99.5, unit = "C" });
        boilReadingRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify boil reading is associated with the batch
        var readingsAfterBoil = (await (await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        readingsAfterBoil.Should().HaveCount(1);
        readingsAfterBoil[0].Stage.Should().Be(BrewStage.Boil);
        readingsAfterBoil[0].TemperatureC.Should().Be(99.5m);

        // 5. Advance batch to Ferment stage
        var advanceToFerment = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage",
            new AdvanceBatchStageRequest(BrewStage.Ferment, MeasuredOg: 1.055m, MeasuredBatchSizeLiters: 20));
        advanceToFerment.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Ingest boiler telemetry again AFTER advancing to Ferment (e.g. cooling kettle)
        var postAdvanceBoilerRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{boilerToken}",
            new { temperature = 40.0, unit = "C" });
        postAdvanceBoilerRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. Ingest fermenter telemetry during Ferment
        var fermenterReadingRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{fermenterToken}",
            new { temperature = 19.8, unit = "C" });
        fermenterReadingRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. Query batch equipment readings
        // The boiler reading sent after advancing MUST NOT be logged to the batch or boil stage!
        var finalBatchReadings = (await (await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;

        finalBatchReadings.Should().HaveCount(2);
        finalBatchReadings[0].Stage.Should().Be(BrewStage.Boil);
        finalBatchReadings[0].TemperatureC.Should().Be(99.5m);
        finalBatchReadings[1].Stage.Should().Be(BrewStage.Ferment);
        finalBatchReadings[1].TemperatureC.Should().Be(19.8m);

        // Query boiler equipment readings directly -> equipment-level reading exists but has no batch link
        var boilerReadingsRes = await client.GetAsync($"/api/v1/inventory/equipment/{boiler.Id}/readings");
        var boilerReadings = (await boilerReadingsRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentReadingDto>>>())!.Data!;
        boilerReadings.Should().HaveCount(2);
        var postAdvanceReading = boilerReadings.FirstOrDefault(r => r.TemperatureC == 40.0m);
        postAdvanceReading.Should().NotBeNull();
    }

    [Fact]
    public async Task TelemetryIngestion_WhenMashStepCompletes_NextStepReceivesReadingsAndPreviousStepStops()
    {
        var (client, _, setupId) = await CreateUserAsync();

        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Mash Tun {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 40,
            ConnectionType: EquipmentConnectionType.HttpPush
        ));
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var token = boiler.ConnectionToken!;

        var mashSteps = new List<BatchMashStepInputDto>
        {
            new(1, "Step 1 - Protein Rest", MashStepType.Temperature, 50.0m, 20),
            new(2, "Step 2 - Sacch Rest", MashStepType.Temperature, 65.0m, 60)
        };
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Mash Step Progression Batch",
            BeerStyle: "Pilsner",
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: boiler.Id,
            FermenterId: null,
            CustomMashSteps: mashSteps
        ));
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        var step1Id = batch.MashSteps[0].Id;
        var step2Id = batch.MashSteps[1].Id;

        var publicClient = _factory.CreateClient();

        // 1. Ingest reading during Step 1
        await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}", new { temperature = 50.4, unit = "C" });

        // 2. Mark Step 1 as completed
        var toggleRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/mash-steps/{step1Id}",
            new ToggleBatchMashStepRequest(true));
        toggleRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Ingest reading during Step 2
        await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}", new { temperature = 65.2, unit = "C" });

        // 4. Verify readings: Step 1 has 1 reading, Step 2 has 1 reading
        var step1Readings = (await (await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings?stepId={step1Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        step1Readings.Should().HaveCount(1);
        step1Readings[0].TemperatureC.Should().Be(50.4m);
        step1Readings[0].StepName.Should().Be("Step 1 - Protein Rest");

        var step2Readings = (await (await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings?stepId={step2Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        step2Readings.Should().HaveCount(1);
        step2Readings[0].TemperatureC.Should().Be(65.2m);
        step2Readings[0].StepName.Should().Be("Step 2 - Sacch Rest");
    }

    [Fact]
    public async Task LogBatchTemperature_Manual_RejectsCompletedStagesAndCompletedMashSteps()
    {
        var (client, _, setupId) = await CreateUserAsync();

        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Validation Kettle {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 30
        ));
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var mashSteps = new List<BatchMashStepInputDto>
        {
            new(1, "Step A", MashStepType.Temperature, 50.0m, 20),
            new(2, "Step B", MashStepType.Temperature, 65.0m, 45)
        };
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Rejection Test Batch",
            BeerStyle: "Stout",
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: boiler.Id,
            FermenterId: null,
            CustomMashSteps: mashSteps
        ));
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        var stepAId = batch.MashSteps[0].Id;

        // Complete Step A
        var toggleRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/mash-steps/{stepAId}",
            new ToggleBatchMashStepRequest(true));
        toggleRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Attempt manual log on completed Step A -> 409 Conflict
        var conflictStep = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings",
            new LogBatchTemperatureRequest(50.0m, boiler.Id, BrewStage.Mash, stepAId, "Step A"));
        conflictStep.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Advance to Boil
        var advanceRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage",
            new AdvanceBatchStageRequest(BrewStage.Boil));
        advanceRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Attempt manual log on completed Mash stage -> 409 Conflict
        var conflictStage = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings",
            new LogBatchTemperatureRequest(65.0m, boiler.Id, BrewStage.Mash));
        conflictStage.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TelemetryBroadcastService_SubscribersReceiveBroadcastReadings()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TelemetryBroadcastService>.Instance;
        var broadcastService = new TelemetryBroadcastService(logger);

        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var reading = new BatchEquipmentReadingDto(
            Id: Guid.NewGuid(),
            EquipmentId: Guid.NewGuid(),
            EquipmentName: "Grainfather G40",
            BatchId: batchId,
            Stage: BrewStage.Mash,
            BatchMashStepId: Guid.NewGuid(),
            StepName: "Saccharification",
            TemperatureC: 65.4m,
            Timestamp: DateTime.UtcNow,
            Source: "Mqtt",
            TargetTemperatureC: 65.0m,
            Notes: null
        );

        using var subscription = broadcastService.Subscribe(batchId);
        subscription.Should().NotBeNull();
        var reader = subscription!.Reader;

        broadcastService.BroadcastReading(reading);

        var hasItem = await reader.WaitToReadAsync(cts.Token);
        hasItem.Should().BeTrue();
        reader.TryRead(out var received).Should().BeTrue();
        received.Should().NotBeNull();
        received!.Id.Should().Be(reading.Id);
        received.TemperatureC.Should().Be(65.4m);
        received.StepName.Should().Be("Saccharification");

        subscription.Dispose();
        broadcastService.GetActiveSubscriberCount(batchId).Should().Be(0);
    }

    [Fact]
    public async Task TelemetryBroadcastService_Unsubscribe_CompletesChannelReader()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TelemetryBroadcastService>.Instance;
        var broadcastService = new TelemetryBroadcastService(logger);

        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var reader = broadcastService.Subscribe(batchId, out var subId);
        reader.Should().NotBeNull();

        broadcastService.GetActiveSubscriberCount(batchId).Should().Be(1);

        var unsubscribed = broadcastService.Unsubscribe(batchId, subId);
        unsubscribed.Should().BeTrue();
        broadcastService.GetActiveSubscriberCount(batchId).Should().Be(0);

        var hasMore = await reader!.WaitToReadAsync(cts.Token);
        hasMore.Should().BeFalse();
    }

    [Fact]
    public void TelemetryBroadcastService_MaxSubscribersLimit_ReturnsNull()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TelemetryBroadcastService>.Instance;
        var broadcastService = new TelemetryBroadcastService(logger);

        var batchId = Guid.NewGuid();
        var subscriptions = new List<TelemetrySubscription>();

        for (var i = 0; i < 20; i++)
        {
            var sub = broadcastService.Subscribe(batchId);
            sub.Should().NotBeNull();
            subscriptions.Add(sub!);
        }

        broadcastService.GetActiveSubscriberCount(batchId).Should().Be(20);

        // 21st subscriber should exceed limit and return null
        var excessSub = broadcastService.Subscribe(batchId);
        excessSub.Should().BeNull();

        foreach (var sub in subscriptions)
        {
            sub.Dispose();
        }

        broadcastService.GetActiveSubscriberCount(batchId).Should().Be(0);
    }

    [Fact]
    public void TelemetryBroadcastService_BufferFull_DropsOldestReading()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TelemetryBroadcastService>.Instance;
        var broadcastService = new TelemetryBroadcastService(logger);

        var batchId = Guid.NewGuid();
        using var subscription = broadcastService.Subscribe(batchId);
        subscription.Should().NotBeNull();
        var reader = subscription!.Reader;

        // Broadcast 105 readings into a bounded channel of 100
        for (var i = 1; i <= 105; i++)
        {
            broadcastService.BroadcastReading(new BatchEquipmentReadingDto(
                Id: Guid.NewGuid(),
                EquipmentId: Guid.NewGuid(),
                EquipmentName: "Probe",
                BatchId: batchId,
                Stage: BrewStage.Boil,
                BatchMashStepId: null,
                StepName: null,
                TemperatureC: i,
                Timestamp: DateTime.UtcNow,
                Source: "Manual",
                TargetTemperatureC: null,
                Notes: null
            ));
        }

        // Channel capacity is 100 with DropOldest, so readings 1..5 were dropped.
        // First item read should be temperature 6.
        reader.TryRead(out var firstReading).Should().BeTrue();
        firstReading!.TemperatureC.Should().Be(6m);
    }

    [Fact]
    public async Task TelemetryStream_EnforcesBOLAIsolation()
    {
        var (clientA, _, _) = await CreateUserAsync();
        var (clientB, _, _) = await CreateUserAsync();

        var batchRes = await clientA.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "BOLA Stream Test Batch",
            BeerStyle: "Porter",
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: null,
            FermenterId: null
        ));
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // User B attempts to access User A's telemetry stream -> 404
        var bStreamReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/batches/{batch.Id}/telemetry-stream");
        var bStreamRes = await clientB.SendAsync(bStreamReq);
        bStreamRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TelemetryStream_EmitsConnectedAndReceivesIngestedReading()
    {
        var (client, _, setupId) = await CreateUserAsync();

        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Stream Kettle {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 45,
            ConnectionType: EquipmentConnectionType.HttpPush
        ));
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var token = boiler.ConnectionToken!;

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Live Stream Test Batch",
            BeerStyle: "Hazy IPA",
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: boiler.Id,
            FermenterId: null
        ));
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 1. Open telemetry stream request
        var streamReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/batches/{batch.Id}/telemetry-stream");
        streamReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(streamReq, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // First message should be connected comment
        var initialLine = await reader.ReadLineAsync();
        initialLine.Should().Be(": connected");
        await reader.ReadLineAsync(); // empty line separator

        // 2. Ingest telemetry from external probe
        var publicClient = _factory.CreateClient();
        var ingestRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}",
            new { temperature = 66.8, unit = "C" });
        ingestRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Read the stream for the ingested reading event
        var eventLine = await reader.ReadLineAsync();
        eventLine.Should().Be("event: reading");

        var dataLine = await reader.ReadLineAsync();
        dataLine.Should().StartWith("data: ");
        var json = dataLine!["data: ".Length..];
        var receivedReading = JsonSerializer.Deserialize<BatchEquipmentReadingDto>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        receivedReading.Should().NotBeNull();
        receivedReading!.BatchId.Should().Be(batch.Id);
        receivedReading.EquipmentId.Should().Be(boiler.Id);
        receivedReading.TemperatureC.Should().Be(66.8m);
    }

    [Fact]
    public async Task AuxiliarySensor_AssignedToAllSteps_LogsTelemetryAndUpdatesGraph()
    {
        var (client, _, defaultSetupId) = await CreateUserAsync();

        // 1. Create iSpindel equipment under Sensor
        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            defaultSetupId,
            "My Ferment iSpindel",
            EquipmentType.Sensor,
            0m,
            EquipmentSubtype.ISpindel,
            null,
            VolumeUnit.Liters,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            EquipmentConnectionType.HttpPush,
            null
        ));
        eqRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ispindel = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var token = ispindel.ConnectionToken!;

        // 2. Create batch with iSpindel assigned to all steps
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Sensor Test Batch",
            BeerStyle: "IPA",
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: null,
            FermenterId: null,
            SensorAssignments: new List<BatchSensorAssignmentInput>
            {
                new(ispindel.Id, null, null)
            }
        ));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        batch.SensorAssignments.Should().HaveCount(1);
        batch.SensorAssignments[0].EquipmentId.Should().Be(ispindel.Id);
        batch.SensorAssignments[0].Stage.Should().BeNull(); // all stages

        // 3. Ingest telemetry from iSpindel in typical iSpindel format
        var publicClient = _factory.CreateClient();
        var ingestRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}", new
        {
            name = "iSpindel-001",
            temperature = 65.2,
            temp_units = "C",
            battery = 4.12,
            gravity = 1.054
        });
        ingestRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Verify reading is associated with the active batch mash stage
        var readingsRes = await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings");
        readingsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readings = (await readingsRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        readings.Should().ContainSingle(r => r.EquipmentId == ispindel.Id && r.TemperatureC == 65.2m && r.Stage == BrewStage.Mash);

        // 5. Update sensor assignments via PUT /api/v1/batches/{id}/sensors to scope to Fermentation stage only
        var putSensorsRes = await client.PutAsJsonAsync($"/api/v1/batches/{batch.Id}/sensors", new List<BatchSensorAssignmentInput>
        {
            new(ispindel.Id, BrewStage.Ferment, null)
        });
        putSensorsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedSensors = (await putSensorsRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchSensorAssignmentDto>>>())!.Data!;
        updatedSensors.Should().ContainSingle(s => s.EquipmentId == ispindel.Id && s.Stage == BrewStage.Ferment);

        // 6. Ingest another reading while batch is still in Mash stage -> should NOT associate because sensor is scoped to Ferment
        var ingestRes2 = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}", new
        {
            name = "iSpindel-001",
            temperature = 65.8,
            temp_units = "C"
        });
        ingestRes2.StatusCode.Should().Be(HttpStatusCode.OK);

        var readingsRes2 = await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings");
        var readings2 = (await readingsRes2.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        // Count should still be 1 (the new reading was not associated to the batch in Mash stage)
        readings2.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("{\"color\": \"RED\", \"temp\": 68.0, \"gravity\": 1.048}", 1.048)]
    [InlineData("{\"name\": \"iSpindel01\", \"temp\": 20.2, \"temp_units\": \"C\", \"gravity\": 1.052}", 1.052)]
    [InlineData("{\"temperature\": 21.0, \"sg\": 1.012}", 1.012)]
    [InlineData("{\"temperature\": 20.0, \"specific_gravity\": 1.025}", 1.025)]
    [InlineData("{\"temperature\": 20.0, \"gravity\": 1054}", 1.054)] // uncalibrated points
    [InlineData("{\"temp\": 20.0}", null)] // no gravity present
    [InlineData("{\"temp\": 20.0, \"gravity\": 1.500}", null)] // out of brewing range
    public void ParseGravity_DetectsAndNormalizesGravity(string json, double? expectedDouble)
    {
        using var doc = JsonDocument.Parse(json);
        var result = TelemetryService.ParseGravity(doc.RootElement);

        if (expectedDouble.HasValue)
        {
            result.Should().Be((decimal)expectedDouble.Value);
        }
        else
        {
            result.Should().BeNull();
        }
    }

    [Fact]
    public async Task FermentorSensor_IngestsGravityAndTemperature_RecordsBatchReadingAndUpdatesAbv()
    {
        var (client, _, defaultSetupId) = await CreateUserAsync();

        // 1. Create Tilt hydrometer
        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            defaultSetupId,
            "Yellow Tilt",
            EquipmentType.Sensor,
            0m,
            EquipmentSubtype.Tilt,
            null,
            VolumeUnit.Liters,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            EquipmentConnectionType.HttpPush,
            null
        ));
        eqRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var tilt = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var token = tilt.ConnectionToken!;

        // 2. Create batch with measured OG and Tilt sensor assignment
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "Fermenting IPA",
            BeerStyle: "IPA",
            BrewDate: null,
            TargetBatchSizeLiters: 20,
            TargetOg: 1.055m,
            TargetFg: 1.010m,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75,
            BoilerId: null,
            FermenterId: null,
            MeasuredOg: 1.052m,
            SensorAssignments: new List<BatchSensorAssignmentInput>
            {
                new(tilt.Id, BrewStage.Ferment, null)
            }
        ));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 3. Advance batch to Ferment stage
        var advRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", new AdvanceBatchStageRequest(
            BrewStage.Ferment,
            MeasuredOg: 1.052m
        ));
        advRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Ingest Tilt reading: temp 68F (20°C) and gravity 1.020
        var publicClient = _factory.CreateClient();
        var ingestRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}", new
        {
            color = "YELLOW",
            temp = 68.0,
            gravity = 1.020
        });
        ingestRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Fetch batch detail: should have updated CurrentGravity, calculated ABV, and a BatchReading
        var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        getBatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBatch = (await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        updatedBatch.CurrentGravity.Should().Be(1.020m);
        // ABV = (1.052 - 1.020) * 131.25 = 0.032 * 131.25 = 4.20
        updatedBatch.AlcoholByVolume.Should().Be(4.20m);
        updatedBatch.Readings.Should().ContainSingle(r => r.SpecificGravity == 1.020m && r.TemperatureC == 20.0m);

        // 6. Fetch equipment readings: should include SpecificGravity
        var eqReadingsRes = await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings");
        eqReadingsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var eqReadings = (await eqReadingsRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!.Data!;
        eqReadings.Should().ContainSingle(r => r.EquipmentId == tilt.Id && r.SpecificGravity == 1.020m);
    }

    [Fact]
    public async Task EquipmentTelemetryStream_RequiresAuthentication()
    {
        var unauthClient = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await unauthClient.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EquipmentTelemetryStream_EmitsConnectedAndReceivesEquipmentReading()
    {
        var (client, userId, setupId) = await CreateUserAsync();

        var fermenterRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Stream Fermenter {Guid.NewGuid():N}",
            Type: EquipmentType.Fermenter,
            Capacity: 30,
            ConnectionType: EquipmentConnectionType.HttpPush
        ));
        fermenterRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var fermenter = (await fermenterRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        var token = fermenter.ConnectionToken!;

        // 1. Open equipment telemetry stream
        var streamReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        streamReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(streamReq, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // Initial acknowledgment
        var initialLine = await reader.ReadLineAsync();
        initialLine.Should().Be(": connected");
        await reader.ReadLineAsync(); // empty line separator

        // 2. Ingest telemetry from external probe (even without an active batch!)
        var publicClient = _factory.CreateClient();
        var ingestRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}",
            new { temperature = 19.5, unit = "C", gravity = 1.042 });
        ingestRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Read the stream for the ingested reading event
        var eventLine = await reader.ReadLineAsync();
        eventLine.Should().Be("event: reading");

        var dataLine = await reader.ReadLineAsync();
        dataLine.Should().StartWith("data: ");
        var json = dataLine!["data: ".Length..];
        var receivedReading = JsonSerializer.Deserialize<EquipmentTelemetryUpdateDto>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        receivedReading.Should().NotBeNull();
        receivedReading!.EquipmentId.Should().Be(fermenter.Id);
        receivedReading.TemperatureC.Should().Be(19.5m);
        receivedReading.SpecificGravity.Should().Be(1.042m);
    }

    [Fact]
    public async Task TelemetryBroadcastService_EquipmentSubscription_IsIsolatedBetweenUsers()
    {
        var broadcastService = new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance);
        var userA = "user_A_" + Guid.NewGuid().ToString("N");
        var userB = "user_B_" + Guid.NewGuid().ToString("N");

        using var subA = broadcastService.SubscribeEquipment(userA)!;
        using var subB = broadcastService.SubscribeEquipment(userB)!;

        subA.Should().NotBeNull();
        subB.Should().NotBeNull();

        var updateA = new EquipmentTelemetryUpdateDto(
            Guid.NewGuid(),
            "Kettle 1",
            20.5m,
            DateTime.UtcNow,
            "HttpPush",
            null,
            1.050m,
            15m,
            50m);

        broadcastService.BroadcastEquipmentReading(userA, updateA);

        // subA should receive updateA
        var hasDataA = await subA.Reader.WaitToReadAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
        hasDataA.Should().BeTrue();
        subA.Reader.TryRead(out var receivedA).Should().BeTrue();
        receivedA.Should().BeEquivalentTo(updateA);

        // subB should NOT receive updateA
        subB.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task IngestTelemetry_UniversalBrewYouFormat_ExtractsAllMetricsAndCachesOnEquipment()
    {
        var (userClient, userId, defaultSetupId) = await CreateUserAsync();

        var eqReq = new CreateEquipmentRequest(
            defaultSetupId,
            "Conical Fermenter 1",
            EquipmentType.Fermenter,
            60m,
            EquipmentSubtype.ConicalFermenter,
            ConnectionType: EquipmentConnectionType.HttpPush);

        var createRes = await userClient.PostAsJsonAsync("/api/v1/inventory/equipment", eqReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var eqEnv = await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var token = eqEnv!.Data!.ConnectionToken;
        var eqId = eqEnv.Data.Id;

        var payload = new
        {
            temperature = 19.5,
            tempUnit = "C",
            gravity = 1.052,
            gravityUnit = "SG",
            pressure = 18.0,
            pressureUnit = "psi",
            battery = 4.15,
            batteryUnit = "V",
            tilt = 48.2,
            rssi = -68,
            raw = new { ph = 5.2, heaterActive = true, coolingLoop = "on" }
        };

        var pushRes = await _client.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}", payload);
        pushRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Equipment updated with cached metrics
        var getRes = await userClient.GetAsync($"/api/v1/inventory/equipment/{eqId}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getEnv = await getRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var equipment = getEnv!.Data!;

        equipment.CurrentTemperatureC.Should().Be(19.5m);
        equipment.CurrentSpecificGravity.Should().Be(1.052m);
        equipment.CurrentPressureBar.Should().Be(1.24m); // 18 psi * 0.0689476 ~= 1.24 bar
        equipment.CurrentBatteryPercent.Should().Be(95.0m); // (4.15 - 3.2) / 1.0 * 100 = 95%
        equipment.CurrentBatteryVoltage.Should().Be(4.15m);
        equipment.LatestMetricsJson.Should().NotBeNull();
        equipment.LatestMetricsJson.Should().Contain("coolingLoop");

        // Verify EquipmentReading stored in DB
        var readingsRes = await userClient.GetAsync($"/api/v1/inventory/equipment/{eqId}/readings");
        readingsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readingsEnv = await readingsRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentReadingDto>>>();
        var reading = readingsEnv!.Data!.First();

        reading.TemperatureC.Should().Be(19.5m);
        reading.SpecificGravity.Should().Be(1.052m);
        reading.PressureBar.Should().Be(1.24m);
        reading.BatteryPercent.Should().Be(95.0m);
        reading.BatteryVoltage.Should().Be(4.15m);
        reading.TiltDegrees.Should().Be(48.2m);
        reading.Rssi.Should().Be((short)-68);
        reading.MetricsJson.Should().Contain("heaterActive");
    }

    [Fact]
    public async Task IngestTelemetry_HeaderAuthentication_SucceedsWithXBrewYouDeviceToken()
    {
        var (userClient, userId, defaultSetupId) = await CreateUserAsync();

        var eqReq = new CreateEquipmentRequest(
            defaultSetupId,
            "iSpindle Wireless Sensor",
            EquipmentType.Sensor,
            0m,
            EquipmentSubtype.ISpindel,
            ConnectionType: EquipmentConnectionType.HttpPush);

        var createRes = await userClient.PostAsJsonAsync("/api/v1/inventory/equipment", eqReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var eqEnv = await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var token = eqEnv!.Data!.ConnectionToken;
        var eqId = eqEnv.Data.Id;

        // iSpindle payload format
        var payload = new
        {
            name = "iSpindel001",
            ID = 12345,
            temperature = 68.0,
            temp_units = "F",
            angle = 45.5,
            battery = 3.95,
            gravity = 1048, // raw gravity points
            RSSI = -62
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telemetry/equipment");
        request.Headers.Add("X-BrewYou-Device-Token", token);
        request.Content = JsonContent.Create(payload);

        var pushRes = await _client.SendAsync(request);
        pushRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Equipment updated with cached metrics
        var getRes = await userClient.GetAsync($"/api/v1/inventory/equipment/{eqId}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getEnv = await getRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var equipment = getEnv!.Data!;

        equipment.CurrentTemperatureC.Should().Be(20.0m); // (68 - 32) * 5/9 = 20.0 C
        equipment.CurrentSpecificGravity.Should().Be(1.048m); // 1048 / 1000 = 1.048
        equipment.CurrentBatteryVoltage.Should().Be(3.95m);
        equipment.CurrentBatteryPercent.Should().Be(75.0m); // (3.95 - 3.2) / 1.0 * 100 = 75%
        equipment.LatestMetricsJson.Should().Contain("12345");
    }
}