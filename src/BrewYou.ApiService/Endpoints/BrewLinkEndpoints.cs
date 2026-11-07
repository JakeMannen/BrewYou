using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public static class BrewLinkEndpoints
{
    public static RouteGroupBuilder MapBrewLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/brewlink/gateways")
            .RequireAuthorization()
            .WithTags("BrewLink");

        group.MapGet("/", async (
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var gateways = await brewLinkService.ListGatewaysAsync(userId, ct);
            return Results.Ok(ApiResponse<List<BrewLinkGatewayDto>>.Ok(gateways));
        })
        .WithName("ListBrewLinkGateways")
        .Produces<ApiResponse<List<BrewLinkGatewayDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/discover", async (
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var discovered = await brewLinkService.DiscoverGatewaysAsync(userId, ct);
            return Results.Ok(ApiResponse<List<BrewLinkDiscoveredGatewayDto>>.Ok(discovered));
        })
        .WithName("DiscoverBrewLinkGateways")
        .Produces<ApiResponse<List<BrewLinkDiscoveredGatewayDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}/equipment", async (
            Guid id,
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (succeeded, equipment, error) = await brewLinkService.GetGatewayEquipmentAsync(userId, id, ct);
            if (!succeeded || equipment == null)
            {
                return Results.BadRequest(ApiResponse.Fail("EQUIPMENT_LIST_FAILED", error ?? "Failed to list equipment from BrewLink gateway."));
            }

            return Results.Ok(ApiResponse<List<BrewLinkEquipmentDeviceDto>>.Ok(equipment));
        })
        .WithName("GetBrewLinkGatewayEquipment")
        .Produces<ApiResponse<List<BrewLinkEquipmentDeviceDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/pair-start", async (
            StartBrewLinkPairingRequest request,
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (succeeded, error) = await brewLinkService.StartPairingWindowAsync(userId, request, ct);
            if (!succeeded)
            {
                return Results.BadRequest(ApiResponse.Fail("PAIRING_WINDOW_FAILED", error ?? "Failed to trigger pairing window."));
            }

            return Results.Ok(ApiResponse.Ok());
        })
        .WithName("StartBrewLinkPairing")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/pair", async (
            PairBrewLinkRequest request,
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (succeeded, gateway, error) = await brewLinkService.PairGatewayAsync(userId, request, ct);
            if (!succeeded || gateway == null)
            {
                return Results.BadRequest(ApiResponse.Fail("PAIRING_FAILED", error ?? "Failed to pair with BrewLink gateway."));
            }

            return Results.Ok(ApiResponse<BrewLinkGatewayDto>.Ok(gateway));
        })
        .WithName("PairBrewLinkGateway")
        .Produces<ApiResponse<BrewLinkGatewayDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateBrewLinkGatewayRequest request,
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (succeeded, gateway, error) = await brewLinkService.UpdateGatewayAsync(userId, id, request, ct);
            if (!succeeded || gateway == null)
            {
                return Results.BadRequest(ApiResponse.Fail("UPDATE_FAILED", error ?? "Failed to update BrewLink gateway."));
            }

            return Results.Ok(ApiResponse<BrewLinkGatewayDto>.Ok(gateway));
        })
        .WithName("UpdateBrewLinkGateway")
        .Produces<ApiResponse<BrewLinkGatewayDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (succeeded, error) = await brewLinkService.DeleteGatewayAsync(userId, id, ct);
            if (!succeeded)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "BrewLink gateway not found."));
            }

            return Results.NoContent();
        })
        .WithName("DeleteBrewLinkGateway")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return group;
    }
}