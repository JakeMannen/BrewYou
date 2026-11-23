namespace BrewYou.ApiService.Services;

public sealed record OffFlavorDescriptor(
    string Key,
    string DisplayName,
    string SensoryCue,
    IReadOnlyList<string> LikelyCauses,
    IReadOnlyList<string> RemediationFixes);

/// <summary>
/// Authoritative sensory troubleshooting catalog linking off-flavor flags to likely causes and fixes.
/// </summary>
public static class OffFlavorCatalog
{
    public static readonly IReadOnlyList<OffFlavorDescriptor> All =
    [
        new(
            Key: "Diacetyl",
            DisplayName: "Diacetyl",
            SensoryCue: "Butterscotch, buttered popcorn, or slick oily mouthfeel.",
            LikelyCauses:
            [
                "Rushed fermentation or crashing temperature before yeast reabsorbed VDK/diacetyl.",
                "Weak yeast health, low pitch rate, or premature racking off yeast cake.",
                "Pediococcus or Lactobacillus bacterial contamination in lines/packaging."
            ],
            RemediationFixes:
            [
                "Perform a 2–4 day diacetyl rest at 18–20°C (or +2–3°C above ale ferment temp) near ~75% attenuation before cold crashing.",
                "Pitch healthy yeast at target cell count with adequate wort aeration.",
                "Verify draft lines and kegs with deep caustic/acid sanitation."
            ]),
        new(
            Key: "Dms",
            DisplayName: "DMS (Dimethyl Sulfide)",
            SensoryCue: "Cooked sweet corn, creamed corn, canned vegetables, or cooked cabbage.",
            LikelyCauses:
            [
                "Covered kettle boil trapping volatile SMM/DMS precursors instead of venting steam.",
                "Weak or short boil (< 60 min with Pilsner malt).",
                "Slow post-boil chill leaving hot wort above 60°C for an extended period."
            ],
            RemediationFixes:
            [
                "Always boil uncovered with a vigorous rolling boil (60–90 min for pale Pilsner grists).",
                "Chill wort rapidly below 25°C right after flameout and keep hop-stand temps ≤ 78°C."
            ]),
        new(
            Key: "Acetaldehyde",
            DisplayName: "Acetaldehyde",
            SensoryCue: "Green apple, freshly cut pumpkin, latex paint, or cidery bite.",
            LikelyCauses:
            [
                "Packaging or racking too early before yeast finishes reducing acetaldehyde to ethanol.",
                "Under-pitching, insufficient wort oxygenation, or high fermentation temperature spikes.",
                "Post-fermentation oxygen pickup converting ethanol back into acetaldehyde."
            ],
            RemediationFixes:
            [
                "Allow an extra 4–7 days on healthy yeast after terminal gravity is reached.",
                "Use starter / proper pitch rate and oxygenate cooled wort before pitching.",
                "Minimize oxygen ingress during cold crash and closed-transfer packaging."
            ]),
        new(
            Key: "Oxidation",
            DisplayName: "Oxidation",
            SensoryCue: "Wet cardboard, stale paper, sherry, or darkened/dulled hop aroma.",
            LikelyCauses:
            [
                "Splashing fermented beer during transfers, dry hopping, or bottling.",
                "Air suck-back during cold crash or unpurged keg/bottle headspace.",
                "Extended warm storage of packaged hop-forward beers."
            ],
            RemediationFixes:
            [
                "Use CO2-purged closed transfers from fermenter to purged keg or use a CO2 balloon/capture lock on cold crash.",
                "Purge bottling wands/headspace and store finished beer cold (2–5°C)."
            ]),
        new(
            Key: "Phenolic",
            DisplayName: "Phenolic / Chlorophenol",
            SensoryCue: "Medicinal band-aid, clove spice, plastic, smoky, or antiseptic.",
            LikelyCauses:
            [
                "Unfiltered municipal chlorine/chloramine reacting with wort polyphenols.",
                "Over-sparging or sparge water pH > 6.0 / temp > 77°C extracting tannins.",
                "Wild yeast contamination or residual bleach/iodophor sanitizer."
            ],
            RemediationFixes:
            [
                "Treat brewing water with Campden tablet (potassium metabisulfite) or carbon filtration to remove chlorine/chloramine.",
                "Acidify sparge water below pH 5.8 and keep sparge temp ≤ 76°C.",
                "Use no-rinse acid sanitizer (Star San) and avoid chlorine cleaners on stainless."
            ]),
        new(
            Key: "Autolysis",
            DisplayName: "Autolysis",
            SensoryCue: "Brothy, soy sauce, Vegemite, meaty, or burnt rubber.",
            LikelyCauses:
            [
                "Leaving beer on old yeast trub for many weeks/months at warm temperatures.",
                "Stressed, high-generation, or severely nutrient-starved yeast under high hydrostatic pressure."
            ],
            RemediationFixes:
            [
                "Transfer off primary yeast cake within 3–4 weeks or cool to conditioning temperatures once FG is stable.",
                "Harvest and repitch fresh low-generation yeast with yeast nutrient on high-OG batches."
            ]),
        new(
            Key: "Lightstruck",
            DisplayName: "Lightstruck (Skunky)",
            SensoryCue: "Skunky musk from UV/blue light reacting with hop iso-alpha acids.",
            LikelyCauses:
            [
                "Exposure of hopped beer to sunlight or bright fluorescent lighting in clear, green, or unshielded vessels."
            ],
            RemediationFixes:
            [
                "Store and package beer in dark amber bottles, cans, or stainless kegs away from direct UV/daylight.",
                "Keep glass carboys covered with dark wraps during fermentation."
            ])
    ];

    public static readonly IReadOnlySet<string> ValidKeys = All
        .Select(d => d.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static List<string> NormalizeKeys(IEnumerable<string>? keys)
    {
        if (keys is null) return [];
        var lookup = All.ToDictionary(d => d.Key, d => d.Key, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (lookup.TryGetValue(key.Trim(), out var canonical) && !result.Contains(canonical, StringComparer.Ordinal))
            {
                result.Add(canonical);
            }
        }
        return result;
    }
}