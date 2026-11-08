using BrewYou.ApiService.Backup.Security;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services.Telemetry;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BrewYou.ApiService.Services;

public partial class BrewLinkService : IBrewLinkService
{
    private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "set_pump", "set_heat", "set_target_temp", "set_timer",
        "pause_timer", "resume_timer", "cancel_timer", "press_set",
        "ack_interaction", "dismiss_alert", "dismiss_sparge_alert", "cancel_session"
    };

    private readonly BrewYouDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IDataProtector _protector;
    private readonly IBrewLinkDiscoveryService? _discoveryService;
    private readonly ITelemetryBroadcastService? _broadcastService;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<BrewLinkService> _logger;

    [GeneratedRegex(@"^[0-9A-Za-z]{6}$")]
    private static partial Regex PinRegex();

    [GeneratedRegex(@"[^0-9A-Fa-f]")]
    private static partial Regex NonHexRegex();

    public BrewLinkService(
        BrewYouDbContext db,
        HttpClient httpClient,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<BrewLinkService> logger,
        IBrewLinkDiscoveryService? discoveryService = null,
        ITelemetryBroadcastService? broadcastService = null,
        IServiceProvider? serviceProvider = null)
    {
        _db = db;
        _httpClient = httpClient;
        _protector = dataProtectionProvider.CreateProtector("BrewYou.BrewLinkCredentials");
        _discoveryService = discoveryService;
        _broadcastService = broadcastService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<List<BrewLinkGatewayDto>> ListGatewaysAsync(string userId, CancellationToken ct = default)
    {
        var entities = await _db.BrewLinkGateways
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.Name)
            .ThenByDescending(g => g.PairedAt)
            .ToListAsync(ct);

        return entities.Select(ToDto).ToList();
    }

    public async Task<List<BrewLinkDiscoveredGatewayDto>> DiscoverGatewaysAsync(string userId, CancellationToken ct = default)
    {
        if (_discoveryService == null)
        {
            return [];
        }

        var pairedIds = await _db.BrewLinkGateways
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => g.GatewayId)
            .ToListAsync(ct);

        var pairedSet = new HashSet<string>(pairedIds, StringComparer.OrdinalIgnoreCase);
        return _discoveryService.GetActiveBeacons(pairedSet);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> StartPairingWindowAsync(
        string userId,
        StartBrewLinkPairingRequest request,
        CancellationToken ct = default)
    {
        var (isValidHost, hostError, normalizedHost) = await ValidateAndNormalizeHostAsync(request.Host, ct);
        if (!isValidHost || normalizedHost == null)
        {
            return (false, hostError ?? "Invalid gateway host.");
        }

        try
        {
            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"{normalizedHost}/api/pair/start");
            using var response = await _httpClient.SendAsync(reqMsg, ct);

            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var errBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Failed to trigger pairing window on {Host}: {StatusCode} - {Body}", normalizedHost, response.StatusCode, errBody);
            return (false, $"Gateway returned status {response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not contact BrewLink gateway at {Host} to start pairing", normalizedHost);
            return (false, $"Could not contact BrewLink gateway: {ex.Message}");
        }
    }

    public async Task<(bool Succeeded, BrewLinkGatewayDto? Gateway, string? ErrorMessage)> PairGatewayAsync(
        string userId,
        PairBrewLinkRequest request,
        CancellationToken ct = default)
    {
        var rawPin = request.Pin?.Trim() ?? string.Empty;
        string? cleanPin = null;
        if (!string.IsNullOrWhiteSpace(rawPin))
        {
            cleanPin = Regex.Replace(rawPin, @"[\s-_]", "").Trim();
            if (!PinRegex().IsMatch(cleanPin))
            {
                return (false, null, "Pairing PIN must be a 6-digit code.");
            }
        }

        var (isValidHost, hostError, normalizedHost) = await ValidateAndNormalizeHostAsync(request.Host, ct);
        if (!isValidHost || normalizedHost == null)
        {
            return (false, null, hostError ?? "Invalid gateway host.");
        }

        try
        {
            object claimPayload = string.IsNullOrEmpty(cleanPin)
                ? new { client_name = "BrewYou" }
                : new { pin = cleanPin, client_name = "BrewYou" };

            using var content = new StringContent(
                JsonSerializer.Serialize(claimPayload),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.PostAsync($"{normalizedHost}/api/pair/claim", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("BrewLink pair claim failed on {Host} with {StatusCode}: {Body}", normalizedHost, response.StatusCode, errContent);

                if (response.StatusCode == HttpStatusCode.Gone)
                {
                    return (false, null, "Pairing window expired. Hold the BOOT button on the gateway for 3 seconds (or request a new PIN) and try again.");
                }

                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                {
                    return string.IsNullOrEmpty(cleanPin)
                        ? (false, null, "Pairing window is not active on the gateway. Hold the physical BOOT button for 3 seconds to open the 60-second pairing window.")
                        : (false, null, "Invalid or expired pairing PIN. Please request a new PIN from the gateway.");
                }

                return (false, null, $"Pairing failed with status {response.StatusCode}.");
            }

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            var gatewayId = root.TryGetProperty("gateway_id", out var gwIdProp) ? gwIdProp.GetString() : null;
            var apiToken = root.TryGetProperty("api_token", out var tokenProp) ? tokenProp.GetString() : null;
            var mqttTopicKey = root.TryGetProperty("mqtt_topic_key", out var keyProp) ? keyProp.GetString() : null;
            var clientName = root.TryGetProperty("client_name", out var clientNameProp) ? clientNameProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(gatewayId) || string.IsNullOrWhiteSpace(apiToken))
            {
                return (false, null, "Gateway response did not contain required credentials.");
            }

            var cleanGatewayId = gatewayId.Trim().ToUpperInvariant();
            var encryptedToken = _protector.Protect(apiToken.Trim());
            var encryptedTopicKey = !string.IsNullOrWhiteSpace(mqttTopicKey)
                ? _protector.Protect(mqttTopicKey.Trim())
                : null;
            var cleanClientName = !string.IsNullOrWhiteSpace(clientName)
                ? clientName.Trim()
                : "BrewYou";

            // Host stored cleanly without scheme for display/consistency, e.g. "192.168.1.50" or "192.168.1.50:8080"
            var cleanHost = GetCleanHostString(normalizedHost);

            var existing = await _db.BrewLinkGateways
                .FirstOrDefaultAsync(g => g.UserId == userId && g.GatewayId == cleanGatewayId, ct);

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    existing.Name = request.Name.Trim();
                }
                existing.Host = cleanHost;
                existing.ApiToken = encryptedToken;
                existing.MqttTopicKey = encryptedTopicKey;
                existing.ClientName = cleanClientName;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var defaultName = !string.IsNullOrWhiteSpace(request.Name)
                    ? request.Name.Trim()
                    : $"BrewLink ({cleanGatewayId})";

                existing = new BrewLinkGateway
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Name = defaultName,
                    Host = cleanHost,
                    GatewayId = cleanGatewayId,
                    ApiToken = encryptedToken,
                    MqttTopicKey = encryptedTopicKey,
                    ClientName = cleanClientName,
                    PairedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.BrewLinkGateways.Add(existing);
            }

            await _db.SaveChangesAsync(ct);
            _discoveryService?.RemoveBeacon(cleanGatewayId);
            _logger.LogInformation("Successfully paired BrewLink gateway {GatewayId} for user {UserId}", cleanGatewayId, userId);

            return (true, ToDto(existing), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to complete pairing with BrewLink gateway at {Host}", normalizedHost);
            return (false, null, $"Could not complete pairing: {ex.Message}");
        }
    }

    public async Task<(bool Succeeded, BrewLinkGatewayDto? Gateway, string? ErrorMessage)> UpdateGatewayAsync(
        string userId,
        Guid id,
        UpdateBrewLinkGatewayRequest request,
        CancellationToken ct = default)
    {
        var gateway = await _db.BrewLinkGateways
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);

        if (gateway == null)
        {
            return (false, null, "BrewLink gateway not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (false, null, "Gateway name is required.");
        }

        var (isValidHost, hostError, normalizedHost) = await ValidateAndNormalizeHostAsync(request.Host, ct);
        if (!isValidHost || normalizedHost == null)
        {
            return (false, null, hostError ?? "Invalid gateway host.");
        }

        gateway.Name = request.Name.Trim();
        gateway.Host = GetCleanHostString(normalizedHost);
        gateway.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return (true, ToDto(gateway), null);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteGatewayAsync(
        string userId,
        Guid id,
        CancellationToken ct = default)
    {
        var gateway = await _db.BrewLinkGateways
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);

        if (gateway == null)
        {
            return (false, "BrewLink gateway not found.");
        }

        // Best-effort revoke client token on the ESP32 gateway (/api/pair/delete)
        try
        {
            var rawToken = _protector.Unprotect(gateway.ApiToken);
            var baseHost = gateway.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           gateway.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? gateway.Host.TrimEnd('/')
                : $"http://{gateway.Host.TrimEnd('/')}";

            using var revokeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            revokeCts.CancelAfter(TimeSpan.FromSeconds(3));

            var deleteBody = JsonSerializer.Serialize(new
            {
                client_name = !string.IsNullOrWhiteSpace(gateway.ClientName) ? gateway.ClientName : "BrewYou"
            });

            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"{baseHost}/api/pair/delete")
            {
                Content = new StringContent(deleteBody, Encoding.UTF8, "application/json")
            };
            reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
            reqMsg.Headers.Add("X-BrewLink-Token", rawToken);

            using var _ = await _httpClient.SendAsync(reqMsg, revokeCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Best-effort token revocation on BrewLink gateway {GatewayId} failed (gateway may be offline).", gateway.GatewayId);
        }

        _db.BrewLinkGateways.Remove(gateway);
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Succeeded, List<BrewLinkEquipmentDeviceDto>? Equipment, string? ErrorMessage)> GetGatewayEquipmentAsync(
        string userId,
        Guid gatewayId,
        CancellationToken ct = default)
    {
        var gateway = await _db.BrewLinkGateways
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gatewayId && g.UserId == userId, ct);

        if (gateway == null)
        {
            return (false, null, "BrewLink gateway not found.");
        }

        string rawToken;
        try
        {
            rawToken = _protector.Unprotect(gateway.ApiToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect API token for BrewLink gateway {GatewayId}", gateway.GatewayId);
            return (false, null, "Gateway credentials could not be decrypted. Please re-pair the gateway.");
        }

        var baseHost = gateway.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       gateway.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? gateway.Host.TrimEnd('/')
            : $"http://{gateway.Host.TrimEnd('/')}";

        try
        {
            using var reqMsg = new HttpRequestMessage(HttpMethod.Get, $"{baseHost}/api/equipment");
            reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
            reqMsg.Headers.Add("X-BrewLink-Token", rawToken);

            using var response = await _httpClient.SendAsync(reqMsg, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Failed to query equipment on BrewLink gateway {GatewayId} ({StatusCode}): {Body}",
                    gateway.GatewayId, response.StatusCode, errBody);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return (false, null, "BrewLink gateway authorization failed. Please re-pair the gateway.");
                }

                return (false, null, $"Gateway returned HTTP {(int)response.StatusCode} when listing equipment.");
            }

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            JsonElement itemsArray = default;
            var hasArray = false;
            if (root.ValueKind == JsonValueKind.Array)
            {
                itemsArray = root;
                hasArray = true;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("equipment", out var eqProp) && eqProp.ValueKind == JsonValueKind.Array)
                {
                    itemsArray = eqProp;
                    hasArray = true;
                }
                else if (root.TryGetProperty("slots", out var slotsProp) && slotsProp.ValueKind == JsonValueKind.Array)
                {
                    itemsArray = slotsProp;
                    hasArray = true;
                }
            }

            var devices = new List<BrewLinkEquipmentDeviceDto>();
            if (hasArray)
            {
                var fallbackSlot = 1;
                foreach (var item in itemsArray.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var slot = item.TryGetProperty("slot", out var slotProp) && slotProp.TryGetInt32(out var s)
                        ? s
                        : fallbackSlot++;

                    var mac = item.TryGetProperty("mac", out var macProp) ? macProp.GetString()?.Trim() ?? string.Empty : string.Empty;
                    var paired = item.TryGetProperty("paired", out var pairedProp)
                        ? pairedProp.ValueKind == JsonValueKind.True
                        : !string.IsNullOrWhiteSpace(mac);

                    if (!paired && string.IsNullOrWhiteSpace(mac))
                    {
                        continue;
                    }

                    var connected = item.TryGetProperty("connected", out var connProp) && connProp.ValueKind == JsonValueKind.True;
                    var name = item.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString())
                        ? nameProp.GetString()!.Trim()
                        : $"BrewLink Slot {slot}";
                    var model = item.TryGetProperty("model", out var modelProp) ? modelProp.GetString()?.Trim() : null;
                    var eqType = item.TryGetProperty("equipment_type", out var typeProp) && !string.IsNullOrWhiteSpace(typeProp.GetString())
                        ? typeProp.GetString()!.Trim().ToLowerInvariant()
                        : "boiler";
                    int? rssi = item.TryGetProperty("rssi", out var rssiProp) && rssiProp.TryGetInt32(out var parsedRssi)
                        ? parsedRssi
                        : null;

                    var capabilities = new BrewLinkEquipmentCapabilitiesDto(
                        RecipeUpload: eqType == "boiler",
                        MashProfile: eqType == "boiler",
                        FermentationProfile: eqType == "fermenter",
                        PumpControl: eqType == "boiler",
                        HeatControl: eqType is "boiler" or "fermenter"
                    );

                    if (item.TryGetProperty("capabilities", out var capProp) && capProp.ValueKind == JsonValueKind.Object)
                    {
                        capabilities = new BrewLinkEquipmentCapabilitiesDto(
                            RecipeUpload: capProp.TryGetProperty("recipe_upload", out var ru) && ru.ValueKind == JsonValueKind.True,
                            MashProfile: capProp.TryGetProperty("mash_profile", out var mp) && mp.ValueKind == JsonValueKind.True,
                            FermentationProfile: capProp.TryGetProperty("fermentation_profile", out var fp) && fp.ValueKind == JsonValueKind.True,
                            PumpControl: capProp.TryGetProperty("pump_control", out var pc) && pc.ValueKind == JsonValueKind.True,
                            HeatControl: capProp.TryGetProperty("heat_control", out var hc) && hc.ValueKind == JsonValueKind.True
                        );
                    }

                    BrewLinkEquipmentStateDto? state = null;
                    if (item.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.Object)
                    {
                        state = new BrewLinkEquipmentStateDto(
                            TempC: stateProp.TryGetProperty("temp_c", out var tc) && tc.TryGetDecimal(out var tcVal) ? tcVal : null,
                            TargetTempC: stateProp.TryGetProperty("target_temp_c", out var ttc) && ttc.TryGetDecimal(out var ttcVal) ? ttcVal : null,
                            HeatPct: stateProp.TryGetProperty("heat_pct", out var hp) && hp.TryGetDecimal(out var hpVal) ? hpVal : null,
                            PumpOn: stateProp.TryGetProperty("pump_on", out var po) && po.ValueKind is JsonValueKind.True or JsonValueKind.False ? po.GetBoolean() : null,
                            StepIndex: stateProp.TryGetProperty("step_index", out var si) && si.TryGetInt32(out var siVal) ? siVal : null,
                            TimerRemainingSec: stateProp.TryGetProperty("timer_remaining_sec", out var tr) && tr.TryGetInt32(out var trVal) ? trVal : null
                        );
                    }

                    devices.Add(new BrewLinkEquipmentDeviceDto(
                        Slot: slot,
                        Paired: paired,
                        Connected: connected,
                        Name: name,
                        Model: model,
                        EquipmentType: eqType,
                        Mac: mac,
                        Rssi: rssi,
                        Capabilities: capabilities,
                        State: state
                    ));
                }
            }

            return (true, devices, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch paired equipment from BrewLink gateway {GatewayId} at {Host}", gateway.GatewayId, baseHost);
            return (false, null, $"Could not reach BrewLink gateway: {ex.Message}");
        }
    }

    public async Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendRecipeToEquipmentAsync(
        string userId,
        Guid equipmentId,
        SendBrewLinkRecipeRequest request,
        CancellationToken ct = default)
    {
        if (!request.RecipeId.HasValue && !request.BatchId.HasValue)
        {
            return (BrewLinkSendResult.BadRequest, null, "Either RecipeId or BatchId must be specified.");
        }

        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == equipmentId, ct);

        if (equipment == null)
        {
            return (BrewLinkSendResult.NotFound, null, "Equipment not found.");
        }

        var isOwner = equipment.UserId == userId ||
                      (equipment.BrewerySetup != null &&
                       (equipment.BrewerySetup.UserId == userId ||
                        equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));

        var isBrewer = equipment.BrewerySetup != null &&
                       equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);

        if (!isOwner && !isBrewer)
        {
            return (BrewLinkSendResult.Forbidden, null, "You do not have permission to control this equipment.");
        }

        if (equipment.Type != EquipmentType.Boiler ||
            equipment.Subtype != EquipmentSubtype.AllInOne ||
            equipment.ConnectionType != EquipmentConnectionType.BrewLink)
        {
            return (BrewLinkSendResult.BadRequest, null, "BrewLink recipe upload is only supported on All-in-One Boilers configured with BrewLink.");
        }

        if (string.IsNullOrWhiteSpace(equipment.ConnectionConfigJson))
        {
            return (BrewLinkSendResult.BadRequest, null, "Equipment does not have BrewLink configuration configured.");
        }

        Guid gatewayId;
        string? targetMac = null;
        string? targetName = null;
        int? targetSlot = null;
        try
        {
            var configNode = JsonNode.Parse(equipment.ConnectionConfigJson) as JsonObject;
            var gwIdStr = configNode?["brewLinkGatewayId"]?.GetValue<string>();
            var macStr = configNode?["targetMac"]?.GetValue<string>();
            targetName = configNode?["targetName"]?.GetValue<string>()?.Trim();
            if (configNode?["slot"] != null && int.TryParse(configNode["slot"]!.ToString(), out var parsedSlot) && parsedSlot is >= 1 and <= 3)
            {
                targetSlot = parsedSlot;
            }

            if (!Guid.TryParse(gwIdStr, out gatewayId))
            {
                return (BrewLinkSendResult.BadRequest, null, "Invalid BrewLink connection configuration in equipment.");
            }

            if (!string.IsNullOrWhiteSpace(macStr))
            {
                targetMac = NormalizeMac(macStr);
            }

            if (string.IsNullOrWhiteSpace(targetMac) && !targetSlot.HasValue && string.IsNullOrWhiteSpace(targetName))
            {
                return (BrewLinkSendResult.BadRequest, null, "Invalid BrewLink connection configuration in equipment.");
            }
        }
        catch
        {
            return (BrewLinkSendResult.BadRequest, null, "Could not parse BrewLink configuration.");
        }

        // Look up paired gateway (owned by caller or setup owner)
        var gateway = await _db.BrewLinkGateways
            .FirstOrDefaultAsync(g => g.Id == gatewayId && (g.UserId == userId || g.UserId == equipment.UserId), ct);

        if (gateway == null)
        {
            return (BrewLinkSendResult.BadRequest, null, "Configured BrewLink gateway was not found or is no longer paired.");
        }

        string rawToken;
        try
        {
            rawToken = _protector.Unprotect(gateway.ApiToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect API token for BrewLink gateway {GatewayId}", gateway.GatewayId);
            return (BrewLinkSendResult.Unauthorized, null, "Gateway credentials could not be decrypted. Please re-pair the gateway.");
        }

        // Build BrewLink Recipe JSON
        JsonObject recipePayload;
        if (request.BatchId.HasValue)
        {
            var batch = await _db.Batches
                .Include(b => b.VolumeProfile)
                .Include(b => b.Ingredients)
                .Include(b => b.MashSteps)
                .Include(b => b.FermentationSteps)
                .FirstOrDefaultAsync(b => b.Id == request.BatchId.Value, ct);

            if (batch == null)
            {
                return (BrewLinkSendResult.NotFound, null, "Batch not found.");
            }

            recipePayload = BuildRecipePayloadFromBatch(batch, equipment);
        }
        else
        {
            var recipe = await _db.Recipes
                .Include(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
                .Include(r => r.MashSteps)
                .Include(r => r.FermentationSteps)
                .FirstOrDefaultAsync(r => r.Id == request.RecipeId!.Value, ct);

            if (recipe == null)
            {
                return (BrewLinkSendResult.NotFound, null, "Recipe not found.");
            }

            recipePayload = BuildRecipePayloadFromRecipe(recipe, equipment);
        }

        var reqId = $"req-{Guid.NewGuid():N}"[..12];
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var action = "load_recipe";
        var signingInput = $"{reqId}:{action}:{timestamp}";
        var sig = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(rawToken),
            Encoding.UTF8.GetBytes(signingInput)));

        var commandEnvelope = new JsonObject
        {
            ["req_id"] = reqId,
            ["action"] = action,
            ["equipment_type"] = "boiler",
            ["timestamp"] = timestamp,
            ["sig"] = sig,
            ["params"] = new JsonObject
            {
                ["recipe"] = recipePayload
            }
        };

        if (targetSlot.HasValue)
        {
            commandEnvelope["slot"] = targetSlot.Value;
        }
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            commandEnvelope["target_name"] = targetName;
        }
        if (!string.IsNullOrWhiteSpace(targetMac))
        {
            commandEnvelope["target_mac"] = targetMac;
        }

        var baseHost = gateway.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       gateway.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? gateway.Host.TrimEnd('/')
            : $"http://{gateway.Host.TrimEnd('/')}";

        // The authoritative HTTP REST recipe upload endpoint from brewlink-external-client is /api/equipment/recipe
        var recipeUrl = $"{baseHost}/api/equipment/recipe";

        try
        {
            var jsonPayload = commandEnvelope.ToJsonString();
            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, recipeUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
            reqMsg.Headers.Add("X-BrewLink-Token", rawToken);

            var response = await _httpClient.SendAsync(reqMsg, ct);

            // Backward compatibility fallback: if older firmware returns 404 for /api/equipment/recipe, try /api/equipment/cmd
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("BrewLink endpoint /api/equipment/recipe returned 404, trying fallback /api/equipment/cmd on {Host}", baseHost);
                response.Dispose();

                var fallbackUrl = $"{baseHost}/api/equipment/cmd";
                using var fallbackReq = new HttpRequestMessage(HttpMethod.Post, fallbackUrl)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                fallbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
                fallbackReq.Headers.Add("X-BrewLink-Token", rawToken);

                response = await _httpClient.SendAsync(fallbackReq, ct);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var reportedMac = targetMac ?? string.Empty;
                    try
                    {
                        using var respDoc = JsonDocument.Parse(body);
                        if (respDoc.RootElement.TryGetProperty("mac", out var macProp) && !string.IsNullOrWhiteSpace(macProp.GetString()))
                        {
                            reportedMac = macProp.GetString()!;
                        }
                    }
                    catch
                    {
                        // Ignore non-JSON or missing mac property
                    }

                    _logger.LogInformation("Successfully sent recipe to boiler {Mac} via gateway {GatewayId}: {Body}", reportedMac, gateway.GatewayId, body);

                    return (BrewLinkSendResult.Success, new BrewLinkCommandResultDto(
                        Status: "queued",
                        ReqId: reqId,
                        GatewayId: gateway.GatewayId,
                        TargetMac: reportedMac,
                        Message: "Recipe successfully queued on boiler."
                    ), null);
                }

                var errBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("BrewLink command failed with status {StatusCode}: {Body}", response.StatusCode, errBody);

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    return (BrewLinkSendResult.EquipmentDisconnected, null, "Boiler is disconnected from BrewLink gateway (EQUIPMENT_OFFLINE). Make sure the boiler is powered on and within BLE range.");
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return (BrewLinkSendResult.Unauthorized, null, "BrewLink gateway authorization failed. Please re-pair the gateway.");
                }

                return (BrewLinkSendResult.GatewayError, null, $"BrewLink gateway returned HTTP {(int)response.StatusCode}: {errBody}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP failure contacting BrewLink gateway at {Url}", recipeUrl);
            return (BrewLinkSendResult.GatewayError, null, $"Failed to reach BrewLink gateway: {ex.Message}");
        }
    }

    public static string NormalizeMac(string rawMac)
    {
        var cleaned = NonHexRegex().Replace(rawMac, string.Empty).ToUpperInvariant();
        if (cleaned.Length != 12)
        {
            throw new ArgumentException("BLE MAC address must consist of 12 hexadecimal characters.", nameof(rawMac));
        }
        return cleaned;
    }

    public static async Task<(bool IsValid, string? ErrorMessage, string? NormalizedUrl)> ValidateAndNormalizeHostAsync(
        string hostInput,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostInput))
        {
            return (false, "Gateway host or IP is required.", null);
        }

        var trimmed = hostInput.Trim().TrimEnd('/');
        var candidateUrl = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"http://{trimmed}";

        if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri))
        {
            return (false, "Invalid host format.", null);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (false, "Only HTTP and HTTPS gateway endpoints are supported.", null);
        }

        var hostOnly = uri.Host.Trim().ToLowerInvariant();

        // Prohibit local loopback and cloud metadata
        if (hostOnly is "localhost" or "127.0.0.1" or "::1" or "[::1]")
        {
            return (false, "Access to local loopback endpoints is prohibited.", null);
        }

        if (IPAddress.TryParse(hostOnly, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
            {
                return (false, "Access to local loopback addresses is prohibited.", null);
            }
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            {
                return (false, "Access to unspecified network address is prohibited.", null);
            }
        }

        var (isValid, errorMessage, validatedUri) = await NetworkSecurityValidator.ValidateUrlAsync(
            candidateUrl,
            allowPrivateNetworks: true,
            cancellationToken: ct);

        if (!isValid || validatedUri == null)
        {
            return (false, errorMessage ?? "Prohibited host address.", null);
        }

        return (true, null, validatedUri.GetLeftPart(UriPartial.Authority));
    }

    private static string GetCleanHostString(string authorityUrl)
    {
        if (Uri.TryCreate(authorityUrl, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }
        return authorityUrl.Replace("http://", "").Replace("https://", "").TrimEnd('/');
    }

    private static BrewLinkGatewayDto ToDto(BrewLinkGateway g) => new(
        g.Id,
        g.Name,
        g.Host,
        g.GatewayId,
        g.PairedAt,
        g.UpdatedAt
    );

    private static JsonObject BuildRecipePayloadFromBatch(Batch batch, Equipment boiler)
    {
        var boilTime = batch.BoilTimeMinutes;

        // Hop stand minutes from whirlpool additions
        var hopStandMin = batch.Ingredients
            .Where(i => i.AdditionStage == IngredientUsage.Whirlpool && i.AdditionTimeMinutes.HasValue)
            .Select(i => i.AdditionTimeMinutes!.Value)
            .DefaultIfEmpty(0)
            .Max();

        decimal strikeWaterL;
        decimal spargeWaterL;
        if (batch.VolumeProfile != null && batch.VolumeProfile.StrikeWaterLiters > 0m)
        {
            strikeWaterL = Math.Round(batch.VolumeProfile.StrikeWaterLiters, 1);
            spargeWaterL = Math.Round(batch.VolumeProfile.SpargeWaterLiters, 1);
        }
        else
        {
            var totalGrainKg = batch.Ingredients
                .Where(i => i.Type == IngredientType.Fermentable && BatchService.IsMassUnit(i.Unit))
                .Sum(i => BatchService.ConvertUnitAmount(i.Amount, i.Unit, "kg"));

            var boilOffRate = boiler.BoilOffRatePerHour ?? 3.0m;
            var trubLoss = boiler.TrubLossLiters ?? 1.5m;
            var mashTunDeadSpace = boiler.MashTunDeadSpaceLiters ?? 0.0m;

            var volCalc = VolumeCalculator.CalculateWaterRequirements(new VolumeCalculationInput(
                TargetBatchSizeLiters: batch.TargetBatchSizeLiters,
                BoilTimeMinutes: boilTime,
                TotalGrainWeightKg: totalGrainKg,
                TargetBasis: TargetVolumeBasis.Fermenter,
                SpargeEnabled: true,
                BoilOffRatePerHour: boilOffRate,
                GrainAbsorptionRate: 0.96m,
                KettleTrubLossLiters: trubLoss,
                FermenterLossLiters: 1.5m,
                MashTunDeadSpaceLiters: mashTunDeadSpace));

            strikeWaterL = Math.Round(volCalc.StrikeWaterLiters, 1);
            spargeWaterL = Math.Round(volCalc.SpargeWaterLiters, 1);
        }

        var mashStepsArray = new JsonArray();
        foreach (var ms in batch.MashSteps.OrderBy(s => s.StepOrder))
        {
            mashStepsArray.Add(new JsonObject
            {
                ["name"] = ms.Name,
                ["temp_c"] = Math.Round(ms.TargetTemperatureC, 1),
                ["duration_min"] = ms.DurationMinutes
            });
        }

        var boilAdditionsArray = new JsonArray();
        var distinctBoilMinutes = batch.Ingredients
            .Where(i => i.AdditionStage == IngredientUsage.Boil && i.AdditionTimeMinutes.HasValue && i.AdditionTimeMinutes.Value > 0 && i.AdditionTimeMinutes.Value <= boilTime)
            .Select(i => i.AdditionTimeMinutes!.Value)
            .Distinct()
            .OrderByDescending(m => m);

        foreach (var m in distinctBoilMinutes)
        {
            boilAdditionsArray.Add(m);
        }

        var fermentationStepsArray = new JsonArray();
        foreach (var fs in batch.FermentationSteps.OrderBy(s => s.StepOrder))
        {
            fermentationStepsArray.Add(new JsonObject
            {
                ["name"] = fs.Name,
                ["temp_c"] = Math.Round(fs.TargetTemperatureC, 1),
                ["duration_hours"] = fs.DurationDays * 24
            });
        }

        return new JsonObject
        {
            ["name"] = batch.Name.Trim(),
            ["boil_time_min"] = boilTime,
            ["hop_stand_min"] = hopStandMin,
            ["mash_water_l"] = strikeWaterL,
            ["sparge_water_l"] = spargeWaterL,
            ["mash_steps"] = mashStepsArray,
            ["boil_additions_min"] = boilAdditionsArray,
            ["fermentation_steps"] = fermentationStepsArray,
            ["target_og_sg"] = Math.Round(batch.TargetOg, 3),
            ["target_fg_sg"] = Math.Round(batch.TargetFg, 3)
        };
    }

    private static JsonObject BuildRecipePayloadFromRecipe(Recipe recipe, Equipment boiler)
    {
        var boilTime = recipe.BoilTimeMinutes;

        // Hop stand minutes from whirlpool additions
        var hopStandMin = recipe.Ingredients
            .Where(i => i.Usage == IngredientUsage.Whirlpool && i.DurationMinutes.HasValue)
            .Select(i => i.DurationMinutes!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var totalGrainKg = recipe.Ingredients
            .Where(i => i.Ingredient?.Type == IngredientType.Fermentable && BatchService.IsMassUnit(i.Unit))
            .Sum(i => BatchService.ConvertUnitAmount(i.Amount, i.Unit, "kg"));

        var boilOffRate = boiler.BoilOffRatePerHour ?? 3.0m;
        var trubLoss = boiler.TrubLossLiters ?? 1.5m;
        var mashTunDeadSpace = boiler.MashTunDeadSpaceLiters ?? 0.0m;

        var volCalc = VolumeCalculator.CalculateWaterRequirements(new VolumeCalculationInput(
            TargetBatchSizeLiters: recipe.BatchSizeLiters,
            BoilTimeMinutes: boilTime,
            TotalGrainWeightKg: totalGrainKg,
            TargetBasis: TargetVolumeBasis.Fermenter,
            SpargeEnabled: true,
            BoilOffRatePerHour: boilOffRate,
            GrainAbsorptionRate: 0.96m,
            KettleTrubLossLiters: trubLoss,
            FermenterLossLiters: 1.5m,
            MashTunDeadSpaceLiters: mashTunDeadSpace));

        var strikeWaterL = Math.Round(volCalc.StrikeWaterLiters, 1);
        var spargeWaterL = Math.Round(volCalc.SpargeWaterLiters, 1);

        var mashStepsArray = new JsonArray();
        foreach (var ms in recipe.MashSteps.OrderBy(s => s.StepOrder))
        {
            mashStepsArray.Add(new JsonObject
            {
                ["name"] = ms.Name,
                ["temp_c"] = Math.Round(ms.TemperatureC, 1),
                ["duration_min"] = ms.DurationMinutes
            });
        }

        var boilAdditionsArray = new JsonArray();
        var distinctBoilMinutes = recipe.Ingredients
            .Where(i => i.Usage == IngredientUsage.Boil && i.DurationMinutes.HasValue && i.DurationMinutes.Value > 0 && i.DurationMinutes.Value <= boilTime)
            .Select(i => i.DurationMinutes!.Value)
            .Distinct()
            .OrderByDescending(m => m);

        foreach (var m in distinctBoilMinutes)
        {
            boilAdditionsArray.Add(m);
        }

        var fermentationStepsArray = new JsonArray();
        foreach (var fs in recipe.FermentationSteps.OrderBy(s => s.StepOrder))
        {
            fermentationStepsArray.Add(new JsonObject
            {
                ["name"] = fs.Name,
                ["temp_c"] = Math.Round(fs.TargetTemperatureC, 1),
                ["duration_hours"] = fs.DurationDays * 24
            });
        }

        return new JsonObject
        {
            ["name"] = recipe.Name.Trim(),
            ["boil_time_min"] = boilTime,
            ["hop_stand_min"] = hopStandMin,
            ["mash_water_l"] = strikeWaterL,
            ["sparge_water_l"] = spargeWaterL,
            ["mash_steps"] = mashStepsArray,
            ["boil_additions_min"] = boilAdditionsArray,
            ["fermentation_steps"] = fermentationStepsArray,
            ["target_og_sg"] = Math.Round(recipe.OriginalGravity, 3),
            ["target_fg_sg"] = Math.Round(recipe.FinalGravity, 3)
        };
    }

    public async Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendControlCommandAsync(
        string userId,
        Guid equipmentId,
        BrewLinkControlRequest request,
        CancellationToken ct = default)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == equipmentId, ct);

        if (equipment == null)
        {
            return (BrewLinkSendResult.NotFound, null, "Equipment not found.");
        }

        var isOwner = equipment.UserId == userId ||
                      (equipment.BrewerySetup != null &&
                       (equipment.BrewerySetup.UserId == userId ||
                        equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));

        var isBrewer = equipment.BrewerySetup != null &&
                       equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);

        if (!isOwner && !isBrewer)
        {
            return (BrewLinkSendResult.Forbidden, null, "You do not have permission to control this equipment.");
        }

        if (equipment.ConnectionType != EquipmentConnectionType.BrewLink || string.IsNullOrWhiteSpace(equipment.ConnectionConfigJson))
        {
            return (BrewLinkSendResult.EquipmentDisconnected, null, "Equipment is not connected to a BrewLink gateway.");
        }

        var (isValidConfig, configError, gatewayId, targetMac, targetSlot, targetName) =
            ParseEquipmentConnectionConfig(equipment.ConnectionConfigJson);
        if (!isValidConfig)
        {
            return (BrewLinkSendResult.BadRequest, null, configError ?? "Invalid BrewLink connection configuration.");
        }

        var (isValidAction, actionError, payloadParams) = ValidateAndBuildParams(
            request.Action,
            request.Params,
            request.State,
            request.PowerPct,
            request.TempC,
            request.DurationMin);
        if (!isValidAction)
        {
            return (BrewLinkSendResult.BadRequest, null, actionError ?? "Invalid control action.");
        }

        var gateway = await _db.BrewLinkGateways
            .FirstOrDefaultAsync(g => g.Id == gatewayId && (g.UserId == userId || g.UserId == equipment.UserId), ct);

        if (gateway == null)
        {
            return (BrewLinkSendResult.BadRequest, null, "Configured BrewLink gateway was not found or is no longer paired.");
        }

        var (isValidHost, hostError, normalizedHost) = await ValidateAndNormalizeHostAsync(gateway.Host, ct);
        if (!isValidHost || normalizedHost == null)
        {
            return (BrewLinkSendResult.BadRequest, null, hostError ?? "Invalid gateway host.");
        }

        string rawToken;
        try
        {
            rawToken = _protector.Unprotect(gateway.ApiToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect API token for BrewLink gateway {GatewayId}", gateway.GatewayId);
            return (BrewLinkSendResult.Unauthorized, null, "Gateway credentials could not be decrypted. Please re-pair the gateway.");
        }

        var reqId = $"req-{Guid.NewGuid():N}"[..12];
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var action = request.Action.Trim().ToLowerInvariant();
        var signingInput = $"{reqId}:{action}:{timestamp}";
        var sig = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(rawToken),
            Encoding.UTF8.GetBytes(signingInput)));

        var commandEnvelope = new JsonObject
        {
            ["req_id"] = reqId,
            ["action"] = action,
            ["equipment_type"] = equipment.Type == EquipmentType.Fermenter ? "fermenter" : equipment.Type == EquipmentType.Sensor ? "sensor" : "boiler",
            ["timestamp"] = timestamp,
            ["sig"] = sig,
            ["params"] = payloadParams
        };

        if (targetSlot.HasValue)
        {
            commandEnvelope["slot"] = targetSlot.Value;
        }
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            commandEnvelope["target_name"] = targetName;
        }
        if (!string.IsNullOrWhiteSpace(targetMac))
        {
            commandEnvelope["target_mac"] = targetMac;
        }

        var primaryUrl = $"{normalizedHost}/api/equipment/control";
        var fallbackUrl = $"{normalizedHost}/api/equipment/cmd";

        try
        {
            var jsonPayload = commandEnvelope.ToJsonString();
            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, primaryUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
            reqMsg.Headers.Add("X-BrewLink-Token", rawToken);

            var response = await _httpClient.SendAsync(reqMsg, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("BrewLink endpoint /api/equipment/control returned 404, trying fallback /api/equipment/cmd on {Host}", normalizedHost);
                response.Dispose();

                using var fallbackReq = new HttpRequestMessage(HttpMethod.Post, fallbackUrl)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                fallbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
                fallbackReq.Headers.Add("X-BrewLink-Token", rawToken);

                response = await _httpClient.SendAsync(fallbackReq, ct);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var reportedMac = targetMac ?? string.Empty;
                    var reportedReqId = reqId;
                    var reportedMessage = $"Action {action} successfully executed.";
                    var reportedStatus = "ok";

                    try
                    {
                        using var respDoc = JsonDocument.Parse(body);
                        if ((respDoc.RootElement.TryGetProperty("mac", out var macProp) ||
                             respDoc.RootElement.TryGetProperty("target_mac", out macProp)) &&
                            !string.IsNullOrWhiteSpace(macProp.GetString()))
                        {
                            reportedMac = macProp.GetString()!;
                        }

                        if (respDoc.RootElement.TryGetProperty("req_id", out var reqIdProp) && !string.IsNullOrWhiteSpace(reqIdProp.GetString()))
                        {
                            reportedReqId = reqIdProp.GetString()!;
                        }

                        if (respDoc.RootElement.TryGetProperty("message", out var msgProp) && !string.IsNullOrWhiteSpace(msgProp.GetString()))
                        {
                            reportedMessage = msgProp.GetString()!;
                        }

                        if (respDoc.RootElement.TryGetProperty("status", out var stProp) && !string.IsNullOrWhiteSpace(stProp.GetString()))
                        {
                            reportedStatus = stProp.GetString()!;
                        }
                    }
                    catch
                    {
                        // Ignore non-JSON or missing properties
                    }

                    // Optimistic state update
                    var existingMetrics = BrewLinkTelemetryParser.FromMetricsJson(equipment.LatestMetricsJson)
                                          ?? new BrewLinkCanonicalMetrics(
                                              CurrentTempC: equipment.CurrentTemperatureC,
                                              TargetTempC: equipment.CurrentTemperatureC,
                                              BoilTempC: 100.0m,
                                              HeatActive: false,
                                              HeatPowerPct: 0m,
                                              RampActive: false,
                                              PumpActive: false,
                                              AutoMode: false,
                                              Timer: new BrewLinkTimerMetrics(false, false, 0, 0),
                                              Stage: null,
                                              Interaction: new BrewLinkInteractionMetrics(false, 0, null),
                                              Mac: reportedMac,
                                              Rssi: null,
                                              UpdatedAt: DateTime.UtcNow
                                          );

                    var mutatedMetrics = MutateCanonicalMetrics(existingMetrics, action, payloadParams);
                    var newMetricsJson = BrewLinkTelemetryParser.ToMetricsJson(mutatedMetrics);

                    equipment.LatestMetricsJson = newMetricsJson;
                    equipment.LastTelemetryAt = DateTime.UtcNow;
                    if (mutatedMetrics.CurrentTempC.HasValue)
                    {
                        equipment.CurrentTemperatureC = mutatedMetrics.CurrentTempC.Value;
                        equipment.TemperatureUpdatedAt = mutatedMetrics.UpdatedAt;
                    }

                    await _db.SaveChangesAsync(ct);

                    BroadcastEquipmentState(equipment, newMetricsJson, "BrewLinkControl");

                    return (BrewLinkSendResult.Success, new BrewLinkCommandResultDto(
                        Status: reportedStatus,
                        ReqId: reportedReqId,
                        GatewayId: gateway.GatewayId,
                        TargetMac: reportedMac,
                        Message: reportedMessage,
                        LatestMetricsJson: newMetricsJson
                    ), null);
                }

                var errBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("BrewLink control command failed with status {StatusCode}: {Body}", response.StatusCode, errBody);

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    return (BrewLinkSendResult.EquipmentDisconnected, null, "Equipment is disconnected from BrewLink gateway (EQUIPMENT_OFFLINE).");
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return (BrewLinkSendResult.Unauthorized, null, "BrewLink gateway authorization failed. Please re-pair the gateway.");
                }

                return (BrewLinkSendResult.GatewayError, null, $"BrewLink gateway returned HTTP {(int)response.StatusCode}: {errBody}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP failure contacting BrewLink gateway at {Url}", primaryUrl);
            return (BrewLinkSendResult.GatewayError, null, $"Failed to reach BrewLink gateway: {ex.Message}");
        }
    }

    public async Task<(BrewLinkSendResult Result, BrewLinkSyncResultDto? SyncResult, string? ErrorMessage)> SyncEquipmentStateAsync(
        string userId,
        Guid equipmentId,
        CancellationToken ct = default)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == equipmentId, ct);

        if (equipment == null)
        {
            return (BrewLinkSendResult.NotFound, null, "Equipment not found.");
        }

        var isOwner = equipment.UserId == userId ||
                      (equipment.BrewerySetup != null &&
                       (equipment.BrewerySetup.UserId == userId ||
                        equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));

        var isBrewer = equipment.BrewerySetup != null &&
                       equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);

        if (!isOwner && !isBrewer)
        {
            return (BrewLinkSendResult.Forbidden, null, "You do not have permission to control this equipment.");
        }

        if (equipment.ConnectionType != EquipmentConnectionType.BrewLink || string.IsNullOrWhiteSpace(equipment.ConnectionConfigJson))
        {
            return (BrewLinkSendResult.EquipmentDisconnected, null, "Equipment is not connected to a BrewLink gateway.");
        }

        var (isValidConfig, configError, gatewayId, targetMac, targetSlot, targetName) =
            ParseEquipmentConnectionConfig(equipment.ConnectionConfigJson);
        if (!isValidConfig)
        {
            return (BrewLinkSendResult.BadRequest, null, configError ?? "Invalid BrewLink connection configuration.");
        }

        var gateway = await _db.BrewLinkGateways
            .FirstOrDefaultAsync(g => g.Id == gatewayId && (g.UserId == userId || g.UserId == equipment.UserId), ct);

        if (gateway == null)
        {
            return (BrewLinkSendResult.BadRequest, null, "Configured BrewLink gateway was not found or is no longer paired.");
        }

        var (isValidHost, hostError, normalizedHost) = await ValidateAndNormalizeHostAsync(gateway.Host, ct);
        if (!isValidHost || normalizedHost == null)
        {
            return (BrewLinkSendResult.BadRequest, null, hostError ?? "Invalid gateway host.");
        }

        string rawToken;
        try
        {
            rawToken = _protector.Unprotect(gateway.ApiToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect API token for BrewLink gateway {GatewayId}", gateway.GatewayId);
            return (BrewLinkSendResult.Unauthorized, null, "Gateway credentials could not be decrypted. Please re-pair the gateway.");
        }

        var syncUrl = $"{normalizedHost}/api/equipment";

        try
        {
            using var reqMsg = new HttpRequestMessage(HttpMethod.Get, syncUrl);
            reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
            reqMsg.Headers.Add("X-BrewLink-Token", rawToken);

            using var response = await _httpClient.SendAsync(reqMsg, ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return (BrewLinkSendResult.Unauthorized, null, "BrewLink gateway authorization failed.");
                }
                return (BrewLinkSendResult.GatewayError, null, $"BrewLink gateway returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (TelemetryHelper.TryGetPropertyInsensitive(doc.RootElement, "equipment", out var eqArray) &&
                eqArray.ValueKind == JsonValueKind.Array)
            {
                var targetDevice = FindMatchingDevice(eqArray, targetMac, targetSlot, targetName);
                if (targetDevice.ValueKind == JsonValueKind.Object &&
                    TelemetryHelper.TryGetPropertyInsensitive(targetDevice, "connected", out var connProp) &&
                    connProp.ValueKind == JsonValueKind.False)
                {
                    return (BrewLinkSendResult.EquipmentDisconnected, null, "Equipment is disconnected from BrewLink gateway (EQUIPMENT_OFFLINE).");
                }
            }

            var canonical = BrewLinkTelemetryParser.ParseCanonical(doc.RootElement, equipment);
            if (canonical == null || !canonical.CurrentTempC.HasValue)
            {
                return (BrewLinkSendResult.EquipmentDisconnected, null, "Equipment is disconnected from BrewLink gateway (EQUIPMENT_OFFLINE).");
            }

            var metricsJson = BrewLinkTelemetryParser.ToMetricsJson(canonical);
            equipment.CurrentTemperatureC = canonical.CurrentTempC.Value;
            equipment.TemperatureUpdatedAt = canonical.UpdatedAt;
            equipment.LastTelemetryAt = DateTime.UtcNow;
            equipment.LatestMetricsJson = metricsJson;

            if (_serviceProvider != null)
            {
                var telemetryService = _serviceProvider.GetService<ITelemetryService>();
                if (telemetryService != null)
                {
                    await telemetryService.IngestBrewLinkTelemetryAsync(equipment.Id, doc.RootElement, source: "BrewLinkSync");
                }
            }

            await _db.SaveChangesAsync(ct);

            BroadcastEquipmentState(equipment, metricsJson, "BrewLinkSync");

            var syncResult = new BrewLinkSyncResultDto(
                Status: "ok",
                Synced: true,
                TemperatureC: canonical.CurrentTempC.Value,
                MetricsJson: metricsJson,
                EquipmentId: equipment.Id
            );

            return (BrewLinkSendResult.Success, syncResult, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync equipment {EquipmentId} from gateway {Host}", equipmentId, normalizedHost);
            return (BrewLinkSendResult.GatewayError, null, $"Failed to reach BrewLink gateway: {ex.Message}");
        }
    }

    private static (bool IsValid, string? ErrorMessage, Guid GatewayId, string? TargetMac, int? TargetSlot, string? TargetName)
        ParseEquipmentConnectionConfig(string? connectionConfigJson)
    {
        if (string.IsNullOrWhiteSpace(connectionConfigJson))
        {
            return (false, "Equipment does not have BrewLink configuration configured.", Guid.Empty, null, null, null);
        }

        try
        {
            var configNode = JsonNode.Parse(connectionConfigJson) as JsonObject;
            var gwIdStr = configNode?["brewLinkGatewayId"]?.GetValue<string>();
            var macStr = configNode?["targetMac"]?.GetValue<string>();
            var targetName = configNode?["targetName"]?.GetValue<string>()?.Trim();
            int? targetSlot = null;
            if (configNode?["slot"] != null && int.TryParse(configNode["slot"]!.ToString(), out var parsedSlot) && parsedSlot is >= 1 and <= 3)
            {
                targetSlot = parsedSlot;
            }

            if (!Guid.TryParse(gwIdStr, out var gatewayId))
            {
                return (false, "Invalid BrewLink connection configuration in equipment.", Guid.Empty, null, null, null);
            }

            string? targetMac = null;
            if (!string.IsNullOrWhiteSpace(macStr))
            {
                targetMac = NormalizeMac(macStr);
            }

            if (string.IsNullOrWhiteSpace(targetMac) && !targetSlot.HasValue && string.IsNullOrWhiteSpace(targetName))
            {
                return (false, "Invalid BrewLink connection configuration in equipment.", Guid.Empty, null, null, null);
            }

            return (true, null, gatewayId, targetMac, targetSlot, targetName);
        }
        catch
        {
            return (false, "Could not parse BrewLink configuration.", Guid.Empty, null, null, null);
        }
    }

    private static JsonElement FindMatchingDevice(
        JsonElement eqArray,
        string? targetMac,
        int? targetSlot,
        string? targetName)
    {
        foreach (var item in eqArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            if (!string.IsNullOrEmpty(targetMac) &&
                TelemetryHelper.TryGetPropertyInsensitive(item, "mac", out var mElem) &&
                mElem.ValueKind == JsonValueKind.String &&
                string.Equals(BrewLinkTelemetryParser.CleanMac(mElem.GetString()), targetMac, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            if (targetSlot.HasValue &&
                TelemetryHelper.TryGetPropertyInsensitive(item, "slot", out var slElem) &&
                slElem.TryGetInt32(out var sVal) && sVal == targetSlot.Value)
            {
                return item;
            }

            if (!string.IsNullOrEmpty(targetName) &&
                TelemetryHelper.TryGetPropertyInsensitive(item, "name", out var nElem) &&
                nElem.ValueKind == JsonValueKind.String &&
                string.Equals(nElem.GetString(), targetName, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        foreach (var item in eqArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object) return item;
        }

        return default;
    }

    private void BroadcastEquipmentState(Equipment equipment, string metricsJson, string source)
    {
        if (_broadcastService == null) return;

        var fillPercentage = equipment.CapacityLiters > 0
            ? Math.Round(Math.Clamp((equipment.CurrentVolumeLiters / equipment.CapacityLiters) * 100m, 0m, 100m), 1)
            : 0m;

        var update = new EquipmentTelemetryUpdateDto(
            EquipmentId: equipment.Id,
            EquipmentName: equipment.Name,
            TemperatureC: equipment.CurrentTemperatureC ?? 0m,
            Timestamp: DateTime.UtcNow,
            Source: source,
            BatchId: equipment.AssignedBatchId,
            SpecificGravity: equipment.CurrentSpecificGravity,
            CurrentVolumeLiters: equipment.CurrentVolumeLiters,
            FillPercentage: fillPercentage,
            PressureBar: equipment.CurrentPressureBar,
            BatteryPercent: equipment.CurrentBatteryPercent,
            BatteryVoltage: equipment.CurrentBatteryVoltage,
            TiltDegrees: null,
            Rssi: null,
            MetricsJson: metricsJson
        );

        var targetUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { equipment.UserId };

        if (equipment.BrewerySetup != null)
        {
            if (!string.IsNullOrEmpty(equipment.BrewerySetup.UserId))
            {
                targetUserIds.Add(equipment.BrewerySetup.UserId);
            }
            if (equipment.BrewerySetup.Members != null)
            {
                foreach (var member in equipment.BrewerySetup.Members)
                {
                    if (!string.IsNullOrEmpty(member.UserId))
                    {
                        targetUserIds.Add(member.UserId);
                    }
                }
            }
        }

        foreach (var uid in targetUserIds)
        {
            _broadcastService.BroadcastEquipmentReading(uid, update);
        }

        if (equipment.AssignedBatchId.HasValue)
        {
            var batchReading = new BatchEquipmentReadingDto(
                Id: Guid.NewGuid(),
                EquipmentId: equipment.Id,
                EquipmentName: equipment.Name,
                BatchId: equipment.AssignedBatchId.Value,
                Stage: null,
                BatchMashStepId: null,
                StepName: null,
                TemperatureC: equipment.CurrentTemperatureC ?? 0m,
                Timestamp: DateTime.UtcNow,
                Source: source,
                TargetTemperatureC: null,
                Notes: null,
                SpecificGravity: equipment.CurrentSpecificGravity,
                PressureBar: equipment.CurrentPressureBar,
                BatteryPercent: equipment.CurrentBatteryPercent,
                BatteryVoltage: equipment.CurrentBatteryVoltage,
                TiltDegrees: null,
                Rssi: null,
                MetricsJson: metricsJson
            );
            _broadcastService.BroadcastReading(batchReading);
        }
    }

    private static (bool IsValid, string? ErrorMessage, JsonObject PayloadParams) ValidateAndBuildParams(
        string action,
        JsonElement? rawParams,
        bool? state = null,
        decimal? powerPct = null,
        decimal? tempC = null,
        int? durationMin = null)
    {
        var act = action?.Trim() ?? string.Empty;
        if (!SupportedActions.Contains(act))
        {
            return (false, $"Unsupported control action: '{act}'.", new JsonObject());
        }

        var payload = new JsonObject();
        var actLower = act.ToLowerInvariant();

        switch (actLower)
        {
            case "set_pump":
                {
                    var pumpState = state ?? true;
                    if (rawParams.HasValue && rawParams.Value.ValueKind == JsonValueKind.Object)
                    {
                        var obj = rawParams.Value;
                        if (TelemetryHelper.TryGetPropertyInsensitive(obj, "state", out var stElem))
                        {
                            if (stElem.ValueKind == JsonValueKind.True || stElem.ValueKind == JsonValueKind.False)
                                pumpState = stElem.GetBoolean();
                            else if (bool.TryParse(stElem.ToString(), out var b))
                                pumpState = b;
                        }
                        else if (TelemetryHelper.TryGetPropertyInsensitive(obj, "enabled", out var enElem))
                        {
                            if (enElem.ValueKind == JsonValueKind.True || enElem.ValueKind == JsonValueKind.False)
                                pumpState = enElem.GetBoolean();
                            else if (bool.TryParse(enElem.ToString(), out var b))
                                pumpState = b;
                        }
                    }
                    payload["state"] = pumpState;
                    payload["enabled"] = pumpState;
                    return (true, null, payload);
                }

            case "set_heat":
                {
                    var heatState = state ?? true;
                    decimal? effectivePower = powerPct;

                    if (rawParams.HasValue && rawParams.Value.ValueKind == JsonValueKind.Object)
                    {
                        var obj = rawParams.Value;
                        if (TelemetryHelper.TryGetPropertyInsensitive(obj, "state", out var stElem))
                        {
                            if (stElem.ValueKind == JsonValueKind.True || stElem.ValueKind == JsonValueKind.False)
                                heatState = stElem.GetBoolean();
                            else if (bool.TryParse(stElem.ToString(), out var b))
                                heatState = b;
                        }
                        else if (TelemetryHelper.TryGetPropertyInsensitive(obj, "enabled", out var enElem))
                        {
                            if (enElem.ValueKind == JsonValueKind.True || enElem.ValueKind == JsonValueKind.False)
                                heatState = enElem.GetBoolean();
                            else if (bool.TryParse(enElem.ToString(), out var b))
                                heatState = b;
                        }

                        if (TelemetryHelper.TryGetPropertyInsensitive(obj, "power_pct", out var ppElem) ||
                            TelemetryHelper.TryGetPropertyInsensitive(obj, "power", out ppElem))
                        {
                            if (ppElem.TryGetDecimal(out var pVal))
                                effectivePower = pVal;
                            else if (decimal.TryParse(ppElem.ToString(), CultureInfo.InvariantCulture, out var pParsed))
                                effectivePower = pParsed;
                        }
                    }

                    if (effectivePower.HasValue)
                    {
                        if (effectivePower.Value < 0m || effectivePower.Value > 100m)
                        {
                            return (false, "Power percentage must be between 0 and 100.", payload);
                        }
                    }

                    var finalPower = heatState ? (effectivePower ?? 100m) : 0m;
                    payload["state"] = heatState;
                    payload["enabled"] = heatState;
                    payload["power_pct"] = finalPower;
                    payload["power"] = finalPower;
                    return (true, null, payload);
                }

            case "set_target_temp":
                {
                    decimal targetTemp = tempC ?? 65.0m;
                    if (rawParams.HasValue && rawParams.Value.ValueKind == JsonValueKind.Object)
                    {
                        var obj = rawParams.Value;
                        if (TelemetryHelper.TryGetPropertyInsensitive(obj, "temp_c", out var tElem) ||
                            TelemetryHelper.TryGetPropertyInsensitive(obj, "temp", out tElem) ||
                            TelemetryHelper.TryGetPropertyInsensitive(obj, "target_temp", out tElem))
                        {
                            if (tElem.TryGetDecimal(out var dVal))
                                targetTemp = dVal;
                            else if (decimal.TryParse(tElem.ToString(), CultureInfo.InvariantCulture, out var dParsed))
                                targetTemp = dParsed;
                        }
                    }

                    if (targetTemp < 0.0m || targetTemp > 105.0m)
                    {
                        return (false, "Target temperature must be between 0 and 105 degrees Celsius.", payload);
                    }

                    payload["temp_c"] = targetTemp;
                    payload["temp"] = targetTemp;
                    payload["target_temp"] = targetTemp;
                    return (true, null, payload);
                }

            case "set_timer":
                {
                    var durationMinVal = durationMin ?? 60;
                    if (rawParams.HasValue && rawParams.Value.ValueKind == JsonValueKind.Object)
                    {
                        var obj = rawParams.Value;
                        if (TelemetryHelper.TryGetPropertyInsensitive(obj, "duration_min", out var dElem) ||
                            TelemetryHelper.TryGetPropertyInsensitive(obj, "duration", out dElem))
                        {
                            if (dElem.TryGetInt32(out var dVal))
                                durationMinVal = dVal;
                            else if (int.TryParse(dElem.ToString(), out var dParsed))
                                durationMinVal = dParsed;
                        }
                        else if (TelemetryHelper.TryGetPropertyInsensitive(obj, "seconds", out var sElem))
                        {
                            if (sElem.TryGetInt32(out var sVal))
                                durationMinVal = sVal / 60;
                            else if (int.TryParse(sElem.ToString(), out var sParsed))
                                durationMinVal = sParsed / 60;
                        }
                    }

                    if (durationMinVal < 0 || durationMinVal > 1440)
                    {
                        return (false, "Timer duration must be between 0 and 1440 minutes.", payload);
                    }

                    payload["duration_min"] = durationMinVal;
                    payload["duration"] = durationMinVal;
                    payload["seconds"] = durationMinVal * 60;
                    return (true, null, payload);
                }

            case "ack_interaction":
            case "dismiss_alert":
                {
                    if (rawParams.HasValue && rawParams.Value.ValueKind == JsonValueKind.Object)
                    {
                        var obj = rawParams.Value;
                        if (TelemetryHelper.TryGetPropertyInsensitive(obj, "id", out var idElem) && idElem.ValueKind == JsonValueKind.String)
                            payload["id"] = idElem.GetString();
                        if (TelemetryHelper.TryGetPropertyInsensitive(obj, "choice", out var chElem) && chElem.ValueKind == JsonValueKind.String)
                            payload["choice"] = chElem.GetString();
                    }
                    return (true, null, payload);
                }

            default:
                // pause_timer, resume_timer, cancel_timer, press_set, dismiss_sparge_alert, cancel_session
                return (true, null, payload);
        }
    }

    private static BrewLinkCanonicalMetrics MutateCanonicalMetrics(
        BrewLinkCanonicalMetrics existing,
        string action,
        JsonObject payloadParams)
    {
        var now = DateTime.UtcNow;
        var act = action.ToLowerInvariant();

        switch (act)
        {
            case "set_pump":
                {
                    var pumpState = true;
                    if (payloadParams.TryGetPropertyValue("state", out var st) && st != null)
                    {
                        if (bool.TryParse(st.ToString(), out var b)) pumpState = b;
                    }
                    else if (payloadParams.TryGetPropertyValue("enabled", out var en) && en != null)
                    {
                        if (bool.TryParse(en.ToString(), out var b)) pumpState = b;
                    }
                    return existing with { PumpActive = pumpState, UpdatedAt = now };
                }

            case "set_heat":
                {
                    var heatState = true;
                    if (payloadParams.TryGetPropertyValue("state", out var st) && st != null)
                    {
                        if (bool.TryParse(st.ToString(), out var b)) heatState = b;
                    }
                    else if (payloadParams.TryGetPropertyValue("enabled", out var en) && en != null)
                    {
                        if (bool.TryParse(en.ToString(), out var b)) heatState = b;
                    }

                    decimal? heatPower = null;
                    if (payloadParams.TryGetPropertyValue("power_pct", out var pp) && pp != null)
                    {
                        if (decimal.TryParse(pp.ToString(), CultureInfo.InvariantCulture, out var p)) heatPower = p;
                    }
                    else if (payloadParams.TryGetPropertyValue("power", out var pNode) && pNode != null)
                    {
                        if (decimal.TryParse(pNode.ToString(), CultureInfo.InvariantCulture, out var p)) heatPower = p;
                    }

                    var powerPct = heatState ? (heatPower ?? existing.HeatPowerPct ?? 100m) : 0m;
                    return existing with
                    {
                        HeatActive = heatState,
                        HeatPowerPct = powerPct,
                        UpdatedAt = now
                    };
                }

            case "set_target_temp":
                {
                    decimal? targetTemp = null;
                    if (payloadParams.TryGetPropertyValue("temp_c", out var tc) && tc != null)
                    {
                        if (decimal.TryParse(tc.ToString(), CultureInfo.InvariantCulture, out var t)) targetTemp = t;
                    }
                    else if (payloadParams.TryGetPropertyValue("temp", out var tNode) && tNode != null)
                    {
                        if (decimal.TryParse(tNode.ToString(), CultureInfo.InvariantCulture, out var t)) targetTemp = t;
                    }
                    else if (payloadParams.TryGetPropertyValue("target_temp", out var tt) && tt != null)
                    {
                        if (decimal.TryParse(tt.ToString(), CultureInfo.InvariantCulture, out var t)) targetTemp = t;
                    }

                    return existing with
                    {
                        TargetTempC = targetTemp ?? existing.TargetTempC,
                        UpdatedAt = now
                    };
                }

            case "set_timer":
                {
                    var durationMin = 60;
                    if (payloadParams.TryGetPropertyValue("duration_min", out var dm) && dm != null)
                    {
                        if (int.TryParse(dm.ToString(), out var d)) durationMin = d;
                    }
                    else if (payloadParams.TryGetPropertyValue("duration", out var dur) && dur != null)
                    {
                        if (int.TryParse(dur.ToString(), out var d)) durationMin = d;
                    }
                    else if (payloadParams.TryGetPropertyValue("seconds", out var sec) && sec != null)
                    {
                        if (int.TryParse(sec.ToString(), out var s)) durationMin = s / 60;
                    }

                    return existing with
                    {
                        Timer = new BrewLinkTimerMetrics(
                            Active: durationMin > 0,
                            Paused: false,
                            RemainingSeconds: durationMin * 60,
                            TotalSeconds: durationMin * 60
                        ),
                        UpdatedAt = now
                    };
                }

            case "pause_timer":
                {
                    var currentTimer = existing.Timer ?? new BrewLinkTimerMetrics(true, false, 0, 0);
                    return existing with
                    {
                        Timer = currentTimer with { Paused = true },
                        UpdatedAt = now
                    };
                }

            case "resume_timer":
                {
                    var currentTimer = existing.Timer ?? new BrewLinkTimerMetrics(true, true, 0, 0);
                    return existing with
                    {
                        Timer = currentTimer with { Paused = false },
                        UpdatedAt = now
                    };
                }

            case "cancel_timer":
                {
                    return existing with
                    {
                        Timer = new BrewLinkTimerMetrics(Active: false, Paused: false, RemainingSeconds: 0, TotalSeconds: 0),
                        UpdatedAt = now
                    };
                }

            case "press_set":
                {
                    return existing with { UpdatedAt = now };
                }

            case "ack_interaction":
            case "dismiss_alert":
            case "dismiss_sparge_alert":
                {
                    return existing with
                    {
                        Interaction = new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null),
                        UpdatedAt = now
                    };
                }

            case "cancel_session":
                {
                    return existing with
                    {
                        AutoMode = false,
                        HeatActive = false,
                        HeatPowerPct = 0m,
                        PumpActive = false,
                        Timer = new BrewLinkTimerMetrics(Active: false, Paused: false, RemainingSeconds: 0, TotalSeconds: 0),
                        Interaction = new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null),
                        UpdatedAt = now
                    };
                }

            default:
                return existing with { UpdatedAt = now };
        }
    }
}