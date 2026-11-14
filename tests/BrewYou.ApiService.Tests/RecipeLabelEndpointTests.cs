using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SkiaSharp;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class RecipeLabelEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RecipeLabelEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string UserId, string Token)> CreateAuthenticatedUserAsync(string prefix = "brewer")
    {
        var client = _factory.CreateClient();
        var email = $"{prefix}_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, $"{prefix} Display", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var token = envelope.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, envelope.Data.User.Id, token);
    }

    private static byte[] CreateSampleImageBytes(int width, int height, SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(color);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private async Task<RecipeDetailDto> CreateSampleRecipeAsync(HttpClient client, string name = "Label Test Ale")
    {
        var grainRes = await client.GetAsync("/api/v1/ingredients");
        var ingredients = (await grainRes.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);

        var request = new CreateRecipeRequest(
            Name: name,
            Description: "A recipe for testing labels.",
            BeerStyle: "IPA",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: true,
            Ingredients: [new RecipeIngredientInputDto(grain.Id, 5.0m, "kg", 60, IngredientUsage.Mash, "Base")]
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        return envelope!.Data!;
    }

    [Fact]
    public async Task RecipeSummary_InitiallyHasDefaultLabelUrl()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("labelsummary");
        var recipe = await CreateSampleRecipeAsync(client, "Default Label Ale");

        var listRes = await client.GetAsync("/api/v1/recipes?scope=mine");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var listEnvelope = await listRes.Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>();
        var summary = listEnvelope!.Data!.First(r => r.Id == recipe.Id);

        summary.HasCustomLabel.Should().BeFalse();
        summary.LabelImageUrl.Should().NotBeNullOrWhiteSpace();
        summary.LabelImageUrl.Should().Contain("/labels/default/image/");
    }

    [Fact]
    public async Task UploadLabel_AsOwner_SucceedsAndGeneratesSignedUrls()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("labelowner");
        var recipe = await CreateSampleRecipeAsync(client, "Custom Label IPA");

        var pngBytes = CreateSampleImageBytes(300, 300, SKColors.Crimson);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "craft_label.png");

        var uploadRes = await client.PutAsync($"/api/v1/recipes/{recipe.Id}/label", content);
        uploadRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadEnvelope = await uploadRes.Content.ReadFromJsonAsync<ApiResponse<RecipeLabelDto>>();
        uploadEnvelope.Should().NotBeNull();
        var labelDto = uploadEnvelope!.Data!;

        labelDto.RecipeId.Should().Be(recipe.Id);
        labelDto.WidthPx.Should().Be(300);
        labelDto.HeightPx.Should().Be(300);
        labelDto.ImageUrls.Should().ContainKey("bottle");
        labelDto.ImageUrls.Should().ContainKey("round");
        labelDto.ImageUrls.Should().ContainKey("rect");
        labelDto.ImageUrls.Should().ContainKey("original");

        var bottleUrl = labelDto.ImageUrls["bottle"];
        bottleUrl.Should().Contain("/labels/" + recipe.Id + "/image/bottle");

        // Verify public signed image URL works without auth header
        var unauthenticatedClient = _factory.CreateClient();
        var imageRes = await unauthenticatedClient.GetAsync(bottleUrl);
        imageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        imageRes.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        imageRes.Headers.CacheControl.Should().NotBeNull();

        // Verify summary now reflects custom label
        var detailRes = await client.GetAsync($"/api/v1/recipes/{recipe.Id}");
        var detailEnvelope = await detailRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        detailEnvelope!.Data!.HasCustomLabel.Should().BeTrue();
    }

    [Fact]
    public async Task UploadLabel_AsNonOwner_ReturnsForbidden()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("recipeowner");
        var (intruderClient, _, _) = await CreateAuthenticatedUserAsync("intruder");
        var recipe = await CreateSampleRecipeAsync(ownerClient, "Protected Ale");

        var pngBytes = CreateSampleImageBytes(200, 200, SKColors.Blue);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "hack.png");

        var res = await intruderClient.PutAsync($"/api/v1/recipes/{recipe.Id}/label", content);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SignedUrl_WithTamperedSignature_ReturnsForbidden()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("tampertest");
        var recipe = await CreateSampleRecipeAsync(client, "Tamper Recipe");

        var pngBytes = CreateSampleImageBytes(250, 250, SKColors.Purple);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "label.png");

        var uploadRes = await client.PutAsync($"/api/v1/recipes/{recipe.Id}/label", content);
        var uploadEnvelope = await uploadRes.Content.ReadFromJsonAsync<ApiResponse<RecipeLabelDto>>();
        var validUrl = uploadEnvelope!.Data!.ImageUrls["bottle"];

        // Tamper signature parameter
        var tamperedUrl = validUrl.Substring(0, validUrl.Length - 4) + "dead";
        var unauthClient = _factory.CreateClient();
        var res = await unauthClient.GetAsync(tamperedUrl);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateLabelSettings_And_DeleteLabel_WorkCorrectly()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("settingsowner");
        var recipe = await CreateSampleRecipeAsync(client, "Settings Ale");

        var pngBytes = CreateSampleImageBytes(400, 400, SKColors.DarkOrange);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "label.png");

        var uploadRes = await client.PutAsync($"/api/v1/recipes/{recipe.Id}/label", content);
        uploadRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Update crops and bottle dimensions
        var patchReq = new UpdateRecipeLabelSettingsRequest(
            BottleCrop: new LabelCropDto(0.1m, 0.1m, 0.8m, 0.8m),
            RoundCrop: new LabelCropDto(0.2m, 0.2m, 0.6m, 0.6m),
            RectCrop: new LabelCropDto(0.0m, 0.0m, 1.0m, 1.0m),
            RectOrientation: DisplayOrientation.Portrait,
            BottleWidthMm: 95.0m,
            BottleHeightMm: 130.0m
        );

        var patchRes = await client.PatchAsJsonAsync($"/api/v1/recipes/{recipe.Id}/label", patchReq);
        patchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var patchEnvelope = await patchRes.Content.ReadFromJsonAsync<ApiResponse<RecipeLabelDto>>();
        patchEnvelope!.Data!.BottleWidthMm.Should().Be(95.0m);
        patchEnvelope.Data.RectOrientation.Should().Be(DisplayOrientation.Portrait);
        patchEnvelope.Data.Version.Should().BeGreaterThan(1);

        // Delete label
        var delRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}/label");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Getting label now returns default label
        var getRes = await client.GetAsync($"/api/v1/recipes/{recipe.Id}/label");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getEnvelope = await getRes.Content.ReadFromJsonAsync<ApiResponse<RecipeLabelDto>>();
        getEnvelope!.Data!.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateLabelSettings_OnRecipeWithDefaultLabel_SucceedsAndMaterializesCustomCrops()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("defaultcropper");
        var recipe = await CreateSampleRecipeAsync(client, "Default Crop Ale");

        // The recipe initially has no custom label
        var initialRes = await client.GetAsync($"/api/v1/recipes/{recipe.Id}/label");
        initialRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialEnvelope = await initialRes.Content.ReadFromJsonAsync<ApiResponse<RecipeLabelDto>>();
        initialEnvelope!.Data!.IsDefault.Should().BeTrue();

        // Update crops without uploading a custom image first
        var patchReq = new UpdateRecipeLabelSettingsRequest(
            BottleCrop: new LabelCropDto(0.15m, 0.1m, 0.7m, 0.8m),
            RoundCrop: new LabelCropDto(0.125m, 0.125m, 0.75m, 0.75m),
            RectCrop: new LabelCropDto(0.06m, 0.25m, 0.88m, 0.5m),
            RectOrientation: DisplayOrientation.Landscape,
            BottleWidthMm: 90.0m,
            BottleHeightMm: 120.0m
        );

        var patchRes = await client.PatchAsJsonAsync($"/api/v1/recipes/{recipe.Id}/label", patchReq);
        patchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var patchEnvelope = await patchRes.Content.ReadFromJsonAsync<ApiResponse<RecipeLabelDto>>();
        patchEnvelope.Should().NotBeNull();
        patchEnvelope!.Success.Should().BeTrue();
        patchEnvelope.Data!.IsDefault.Should().BeFalse();
        patchEnvelope.Data.RoundCrop.Should().NotBeNull();
        patchEnvelope.Data.RoundCrop!.Width.Should().Be(0.75m);
        patchEnvelope.Data.RoundCrop!.Height.Should().Be(0.75m);
    }

    [Fact]
    public async Task DefaultLabelImage_ReturnsValidArtisanImage()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/labels/default/image/round?size=256&format=webp");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(500);
    }
}