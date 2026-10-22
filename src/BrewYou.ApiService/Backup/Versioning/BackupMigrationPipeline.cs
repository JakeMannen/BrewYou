using BrewYou.ApiService.Backup.Models;
using BrewYou.ApiService.Data.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Backup.Versioning;

public interface IBackupSchemaMigrator
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Migrate(JsonObject data, List<string> migrationNotes);
}

/// <summary>
/// Upgrades legacy V0 / pre-collaboration backups to V1 schema structure.
/// </summary>
public class V0ToV1BackupMigrator : IBackupSchemaMigrator
{
    public int FromVersion => 0;
    public int ToVersion => 1;

    public void Migrate(JsonObject data, List<string> migrationNotes)
    {
        // Map legacy 'settings' or 'preferences' key to 'userSettings'
        if (!data.ContainsKey("userSettings"))
        {
            if (data.TryGetPropertyValue("preferences", out var prefNode) && prefNode != null)
            {
                data["userSettings"] = prefNode.DeepClone();
                data.Remove("preferences");
                migrationNotes.Add("Migrated legacy 'preferences' section to 'userSettings' (v0 -> v1).");
            }
            else if (data.TryGetPropertyValue("settings", out var settingsNode) && settingsNode != null)
            {
                data["userSettings"] = settingsNode.DeepClone();
                data.Remove("settings");
                migrationNotes.Add("Migrated legacy 'settings' section to 'userSettings' (v0 -> v1).");
            }
        }

        // Ensure user has at least one brewery setup if equipment existed before multi-rig support
        var hasOwnedSetups = data.TryGetPropertyValue("ownedBrewerySetups", out var ownedNode) && ownedNode is JsonArray ownedArr && ownedArr.Count > 0;
        var hasLegacySetups = data.TryGetPropertyValue("brewerySetups", out var legNode) && legNode is JsonArray legArr && legArr.Count > 0;

        if (!hasOwnedSetups && !hasLegacySetups &&
            data.TryGetPropertyValue("equipment", out var eqNode) && eqNode is JsonArray eqArr && eqArr.Count > 0)
        {
            var defaultSetupId = Guid.NewGuid();
            var defaultSetup = new JsonObject
            {
                ["id"] = defaultSetupId.ToString(),
                ["name"] = "My brewery",
                ["description"] = "Auto-created during v0 backup migration",
                ["isDefault"] = true,
                ["defaultGrainAbsorptionRate"] = 0.96m,
                ["defaultBoilOffRatePerHour"] = 3.0m,
                ["defaultKettleTrubLossLiters"] = 1.5m,
                ["defaultFermenterLossLiters"] = 1.5m,
                ["defaultMashTunDeadSpaceLiters"] = 0.0m,
                ["coolingShrinkagePercent"] = 4.0m,
                ["defaultPackagingLossLiters"] = 0.5m,
                ["createdAt"] = DateTime.UtcNow.ToString("O"),
                ["updatedAt"] = DateTime.UtcNow.ToString("O")
            };

            data["ownedBrewerySetups"] = new JsonArray(defaultSetup);

            foreach (var item in eqArr)
            {
                if (item is JsonObject eqObj)
                {
                    if (!eqObj.ContainsKey("brewerySetupId") ||
                        string.IsNullOrWhiteSpace(eqObj["brewerySetupId"]?.GetValue<string>()) ||
                        eqObj["brewerySetupId"]?.GetValue<string>() == Guid.Empty.ToString())
                    {
                        eqObj["brewerySetupId"] = defaultSetupId.ToString();
                    }
                }
            }

            migrationNotes.Add("Synthesized default BrewerySetup and linked legacy v0 equipment items.");
        }
    }
}

/// <summary>
/// Upgrades V1 backups to V2 (granular units/formulas, collaboration memberships, and snapshot normalization).
/// </summary>
public class V1ToV2BackupMigrator : IBackupSchemaMigrator
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Migrate(JsonObject data, List<string> migrationNotes)
    {
        // 1. Rename 'brewerySetups' -> 'ownedBrewerySetups' if used in v1
        if (!data.ContainsKey("ownedBrewerySetups") && data.TryGetPropertyValue("brewerySetups", out var setupsNode) && setupsNode != null)
        {
            data["ownedBrewerySetups"] = setupsNode.DeepClone();
            data.Remove("brewerySetups");
            migrationNotes.Add("Normalized 'brewerySetups' to 'ownedBrewerySetups' (v1 -> v2).");
        }

        // 2. Backfill granular unit and formula preferences in userSettings
        if (data.TryGetPropertyValue("userSettings", out var usNode) && usNode is JsonObject usObj)
        {
            var weightUnit = GetCaseInsensitiveString(usObj, "preferredWeightUnit") ?? GetCaseInsensitiveString(usObj, "weightUnit") ?? "Metric";
            var isImperial = string.Equals(weightUnit, "Imperial", StringComparison.OrdinalIgnoreCase);

            if (!ContainsPropertyCaseInsensitive(usObj, "preferredGrainWeightUnit"))
            {
                usObj["preferredGrainWeightUnit"] = isImperial ? "lb" : "kg";
            }
            if (!ContainsPropertyCaseInsensitive(usObj, "preferredHopWeightUnit"))
            {
                usObj["preferredHopWeightUnit"] = isImperial ? "oz" : "g";
            }
            if (!ContainsPropertyCaseInsensitive(usObj, "preferredColorUnit"))
            {
                usObj["preferredColorUnit"] = "SRM";
            }
            if (!ContainsPropertyCaseInsensitive(usObj, "preferredCarbonationUnit"))
            {
                usObj["preferredCarbonationUnit"] = "Volumes";
            }
            if (!ContainsPropertyCaseInsensitive(usObj, "preferredBitternessFormula"))
            {
                usObj["preferredBitternessFormula"] = "Tinseth";
            }
            if (!ContainsPropertyCaseInsensitive(usObj, "preferredColorFormula"))
            {
                usObj["preferredColorFormula"] = "Morey";
            }
            if (!ContainsPropertyCaseInsensitive(usObj, "preferredAbvFormula"))
            {
                usObj["preferredAbvFormula"] = "Linear";
            }

            migrationNotes.Add("Backfilled granular unit and brewing formula defaults in userSettings (v1 -> v2).");
        }

        // 3. Ensure v2 collaboration collections exist
        if (!ContainsPropertyCaseInsensitive(data, "sharedSetupMemberships"))
        {
            data["sharedSetupMemberships"] = new JsonArray();
        }
        if (!ContainsPropertyCaseInsensitive(data, "referencedExternalRecipes"))
        {
            data["referencedExternalRecipes"] = new JsonArray();
        }

        // 4. Backfill coolingShrinkagePercent and defaultPackagingLossLiters on ownedBrewerySetups
        if (data.TryGetPropertyValue("ownedBrewerySetups", out var ownedSetups) && ownedSetups is JsonArray setupsArr)
        {
            foreach (var item in setupsArr)
            {
                if (item is JsonObject sObj)
                {
                    if (!ContainsPropertyCaseInsensitive(sObj, "coolingShrinkagePercent"))
                    {
                        sObj["coolingShrinkagePercent"] = 4.0m;
                    }
                    if (!ContainsPropertyCaseInsensitive(sObj, "defaultPackagingLossLiters"))
                    {
                        sObj["defaultPackagingLossLiters"] = 0.5m;
                    }
                    if (!ContainsPropertyCaseInsensitive(sObj, "defaultMashTunDeadSpaceLiters"))
                    {
                        sObj["defaultMashTunDeadSpaceLiters"] = 0.0m;
                    }
                }
            }
        }
    }

    private static bool ContainsPropertyCaseInsensitive(JsonObject obj, string propertyName)
    {
        return obj.Any(kvp => string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetCaseInsensitiveString(JsonObject obj, string propertyName)
    {
        var match = obj.FirstOrDefault(kvp => string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase));
        return match.Value?.GetValue<string>();
    }
}

/// <summary>
/// Tolerant enum converter that maps legacy aliases and falls back gracefully instead of failing deserialization
/// when loading backups created on older or slightly newer versions.
/// </summary>
public class TolerantEnumConverterFactory(List<string> warnings) : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        return underlying.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert);
        if (underlying != null)
        {
            var converterType = typeof(NullableTolerantEnumConverter<>).MakeGenericType(underlying);
            return (JsonConverter)Activator.CreateInstance(converterType, warnings)!;
        }
        else
        {
            var converterType = typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert);
            return (JsonConverter)Activator.CreateInstance(converterType, warnings)!;
        }
    }
}

public class TolerantEnumConverter<TEnum>(List<string> warnings) : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString()?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return GetFallbackDefault();
            }

            if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            if (TryResolveLegacyAlias(raw, out var aliasValue))
            {
                warnings.Add($"Mapped legacy enum value '{raw}' to '{aliasValue}' ({typeof(TEnum).Name}).");
                return aliasValue;
            }

            var fallback = GetFallbackDefault();
            warnings.Add($"Unrecognized {typeof(TEnum).Name} value '{raw}'; defaulted to '{fallback}'.");
            return fallback;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var intVal))
        {
            if (Enum.IsDefined(typeof(TEnum), intVal))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), intVal);
            }
        }

        return GetFallbackDefault();
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    internal static bool TryResolveLegacyAlias(string raw, out TEnum result)
    {
        result = default;
        var norm = raw.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();

        if (typeof(TEnum) == typeof(ThemePreference))
        {
            ThemePreference? mapped = norm switch
            {
                "obsidianember" or "obsidianstout" => ThemePreference.Obsidian,
                "modernamber" => ThemePreference.Dark,
                "pilsnerclean" => ThemePreference.Light,
                _ => null
            };
            if (mapped.HasValue)
            {
                result = (TEnum)(object)mapped.Value;
                return true;
            }
        }
        else if (typeof(TEnum) == typeof(EquipmentType))
        {
            EquipmentType? mapped = norm switch
            {
                "kettle" or "boilkettle" or "mashtun" => EquipmentType.Boiler,
                "vessel" or "unitank" => EquipmentType.Fermenter,
                "packaging" or "bottle" => EquipmentType.Keg,
                "hydrometer" or "probe" => EquipmentType.Sensor,
                _ => null
            };
            if (mapped.HasValue)
            {
                result = (TEnum)(object)mapped.Value;
                return true;
            }
        }
        else if (typeof(TEnum) == typeof(EquipmentSubtype))
        {
            EquipmentSubtype? mapped = norm switch
            {
                "unitank" or "conical" => EquipmentSubtype.ConicalFermenter,
                "corny" or "corneliuskeg" => EquipmentSubtype.Cornelius,
                "ispindle" => EquipmentSubtype.ISpindel,
                _ => null
            };
            if (mapped.HasValue)
            {
                result = (TEnum)(object)mapped.Value;
                return true;
            }
        }
        else if (typeof(TEnum) == typeof(IngredientUsage))
        {
            IngredientUsage? mapped = norm switch
            {
                "aroma" or "hopstand" => IngredientUsage.Whirlpool,
                "firstwort" => IngredientUsage.Mash,
                _ => null
            };
            if (mapped.HasValue)
            {
                result = (TEnum)(object)mapped.Value;
                return true;
            }
        }
        else if (typeof(TEnum) == typeof(BrewStage))
        {
            BrewStage? mapped = norm switch
            {
                "planning" or "preparation" or "mashing" or "lautering" => BrewStage.Mash,
                "boiling" or "cooling" => BrewStage.Boil,
                "fermenting" => BrewStage.Ferment,
                "conditioning" => BrewStage.Condition,
                "bottling" or "kegging" or "bottlingkegging" or "completed" => BrewStage.Package,
                _ => null
            };
            if (mapped.HasValue)
            {
                result = (TEnum)(object)mapped.Value;
                return true;
            }
        }

        return false;
    }

    internal static TEnum GetFallbackDefault()
    {
        if (typeof(TEnum) == typeof(ThemePreference))
            return (TEnum)(object)ThemePreference.ImperialStout;
        if (typeof(TEnum) == typeof(EquipmentType))
            return (TEnum)(object)EquipmentType.Other;
        if (typeof(TEnum) == typeof(EquipmentSubtype))
            return (TEnum)(object)EquipmentSubtype.Other;
        if (typeof(TEnum) == typeof(IngredientType))
            return (TEnum)(object)IngredientType.Other;
        return default;
    }
}

public class NullableTolerantEnumConverter<TEnum>(List<string> warnings) : JsonConverter<TEnum?>
    where TEnum : struct, Enum
{
    private readonly TolerantEnumConverter<TEnum> _inner = new(warnings);

    public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        return _inner.Read(ref reader, typeof(TEnum), options);
    }

    public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }
        _inner.Write(writer, value.Value, options);
    }
}

public class BackupMigrationPipeline
{
    private readonly IReadOnlyList<IBackupSchemaMigrator> _migrators;

    public BackupMigrationPipeline(IEnumerable<IBackupSchemaMigrator>? migrators = null)
    {
        _migrators = (migrators ?? [new V0ToV1BackupMigrator(), new V1ToV2BackupMigrator()])
            .OrderBy(m => m.FromVersion)
            .ToList();
    }

    public (BackupPayload? Payload, bool WasUpgraded, List<string> MigrationNotes, List<string> Warnings, List<string> Errors)
        MigrateAndDeserialize(BrewYouBackupEnvelope envelope)
    {
        var migrationNotes = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        if (!string.Equals(envelope.Format, BackupSchemaConstants.FormatIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Invalid backup format '{envelope.Format}'. Expected '{BackupSchemaConstants.FormatIdentifier}'.");
            return (null, false, migrationNotes, warnings, errors);
        }

        if (envelope.SchemaVersion < 0)
        {
            errors.Add($"Unsupported negative backup schema version ({envelope.SchemaVersion}).");
            return (null, false, migrationNotes, warnings, errors);
        }

        if (envelope.SchemaVersion > BackupSchemaConstants.CurrentSchemaVersion)
        {
            warnings.Add(
                $"Backup was created with a newer schema version (v{envelope.SchemaVersion}) than this server (v{BackupSchemaConstants.CurrentSchemaVersion}). " +
                "Unrecognized fields will be ignored safely.");
        }

        JsonObject? dataObject;
        try
        {
            var rawJson = envelope.Data.GetRawText();
            dataObject = JsonNode.Parse(rawJson, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject;
            if (dataObject == null)
            {
                errors.Add("Backup 'data' payload must be a valid JSON object.");
                return (null, false, migrationNotes, warnings, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to parse backup JSON payload: {ex.Message}");
            return (null, false, migrationNotes, warnings, errors);
        }

        bool wasUpgraded = false;
        int currentVersion = envelope.SchemaVersion;

        while (currentVersion < BackupSchemaConstants.CurrentSchemaVersion)
        {
            var migrator = _migrators.FirstOrDefault(m => m.FromVersion == currentVersion);
            if (migrator == null)
            {
                errors.Add($"No backup schema migrator found from v{currentVersion} to v{BackupSchemaConstants.CurrentSchemaVersion}.");
                return (null, wasUpgraded, migrationNotes, warnings, errors);
            }

            migrator.Migrate(dataObject, migrationNotes);
            currentVersion = migrator.ToVersion;
            wasUpgraded = true;
        }

        try
        {
            var options = CreateTolerantDeserializerOptions(warnings);
            var payload = dataObject.Deserialize<BackupPayload>(options);
            if (payload == null)
            {
                errors.Add("Backup payload deserialized to null.");
                return (null, wasUpgraded, migrationNotes, warnings, errors);
            }

            if (payload.ExtensionData is { Count: > 0 })
            {
                warnings.Add($"Ignored {payload.ExtensionData.Count} unrecognized top-level field(s) during deserialization.");
            }

            return (payload, wasUpgraded, migrationNotes, warnings, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to deserialize backup payload after schema migration: {ex.Message}");
            return (null, wasUpgraded, migrationNotes, warnings, errors);
        }
    }

    public static JsonSerializerOptions CreateTolerantDeserializerOptions(List<string> warnings)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = 32
        };
        options.Converters.Add(new TolerantEnumConverterFactory(warnings));
        return options;
    }

    public static JsonSerializerOptions CreateCanonicalSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string ComputeChecksumSha256(JsonElement dataElement)
    {
        var node = JsonNode.Parse(dataElement.GetRawText());
        var canonicalJson = CanonicalizeNode(node);
        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool VerifyChecksum(BrewYouBackupEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.ChecksumSha256))
        {
            return false;
        }

        try
        {
            var computed = ComputeChecksumSha256(envelope.Data);
            var expectedBytes = Encoding.UTF8.GetBytes(envelope.ChecksumSha256.Trim().ToLowerInvariant());
            var computedBytes = Encoding.UTF8.GetBytes(computed);
            return CryptographicOperations.FixedTimeEquals(expectedBytes, computedBytes);
        }
        catch
        {
            return false;
        }
    }

    private static string CanonicalizeNode(JsonNode? node)
    {
        if (node == null) return "null";

        if (node is JsonObject obj)
        {
            var sortedProps = obj
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => $"{JsonSerializer.Serialize(kvp.Key)}:{CanonicalizeNode(kvp.Value)}");
            return "{" + string.Join(",", sortedProps) + "}";
        }

        if (node is JsonArray arr)
        {
            var items = arr.Select(CanonicalizeNode);
            return "[" + string.Join(",", items) + "]";
        }

        return node.ToJsonString();
    }
}