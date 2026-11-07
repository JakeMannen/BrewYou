using BrewYou.ApiService.Backup.Security;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
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
    private readonly BrewYouDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IDataProtector _protector;
    private readonly IBrewLinkDiscoveryService? _discoveryService;
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
        IBrewLinkDiscoveryService? discoveryService = null)
    {
        _db = db;
        _httpClient = httpClient;
        _protector = dataProtectionProvider.CreateProtector("BrewYou.BrewLinkCredentials");
        _discoveryService = discoveryService;
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
        if (hostOnly is "localhost" or "127.0.0.1" or "::1")
        {
            return (false, "Access to local loopback endpoints is prohibited.", null);
        }

        if (IPAddress.TryParse(hostOnly, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
            {
                return (false, "Access to local loopback addresses is prohibited.", null);
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
}