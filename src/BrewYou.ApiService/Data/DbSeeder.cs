using BrewYou.ApiService.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Data;

public static class DbSeeder
{
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public static async Task SeedAsync(BrewYouDbContext db)
    {
        await _seedLock.WaitAsync();
        try
        {
            var catalogIngredients = GetDefaultCatalogIngredients();
            var existingNames = await db.Ingredients
                .Where(i => i.IsCatalogItem)
                .Select(i => i.Name)
                .ToListAsync();

            var missingIngredients = catalogIngredients
                .Where(i => !existingNames.Contains(i.Name))
                .ToList();

            if (missingIngredients.Count > 0)
            {
                await db.Ingredients.AddRangeAsync(missingIngredients);
                await db.SaveChangesAsync();
            }

            // Ensure every existing user has at least one default brewery setup
            var usersWithoutSetups = await db.Users
                .Where(u => !db.BrewerySetups.Any(s => s.UserId == u.Id))
                .ToListAsync();

            if (usersWithoutSetups.Count > 0)
            {
                foreach (var user in usersWithoutSetups)
                {
                    var defaultName = user.PreferredLanguage?.StartsWith("sv", StringComparison.OrdinalIgnoreCase) == true
                        ? "Mitt bryggeri"
                        : "My brewery";

                    db.BrewerySetups.Add(new BrewerySetup
                    {
                        UserId = user.Id,
                        Name = defaultName,
                        IsDefault = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync();
            }

            // Backfill any equipment missing BrewerySetupId or loss parameters
            var orphanedEquipment = await db.Equipment
                .Where(e => e.BrewerySetupId == Guid.Empty)
                .ToListAsync();

            if (orphanedEquipment.Count > 0)
            {
                var userIds = orphanedEquipment.Select(e => e.UserId).Distinct().ToList();
                var defaultSetups = await db.BrewerySetups
                    .Where(s => userIds.Contains(s.UserId) && s.IsDefault)
                    .ToDictionaryAsync(s => s.UserId, s => s.Id);

                foreach (var eq in orphanedEquipment)
                {
                    if (defaultSetups.TryGetValue(eq.UserId, out var setupId))
                    {
                        eq.BrewerySetupId = setupId;
                    }
                }
                await db.SaveChangesAsync();
            }

            // Backfill default loss values for equipment where losses are not set
            var equipmentMissingLosses = await db.Equipment
                .Include(e => e.BrewerySetup)
                .Where(e =>
                    (e.Type == EquipmentType.Boiler && (e.BoilOffRatePerHour == null || e.TrubLossLiters == null || e.MashTunDeadSpaceLiters == null)) ||
                    (e.Type == EquipmentType.Fermenter && e.TrubLossLiters == null) ||
                    ((e.Type == EquipmentType.Keg || e.Type == EquipmentType.Other) && e.PackagingLossLiters == null))
                .ToListAsync();

            if (equipmentMissingLosses.Count > 0)
            {
                foreach (var eq in equipmentMissingLosses)
                {
                    var setup = eq.BrewerySetup;
                    if (setup == null) continue;

                    if (eq.Type == EquipmentType.Boiler)
                    {
                        eq.BoilOffRatePerHour ??= setup.DefaultBoilOffRatePerHour;
                        eq.TrubLossLiters ??= setup.DefaultKettleTrubLossLiters;
                        eq.MashTunDeadSpaceLiters ??= setup.DefaultMashTunDeadSpaceLiters;
                    }
                    else if (eq.Type == EquipmentType.Fermenter)
                    {
                        eq.TrubLossLiters ??= setup.DefaultFermenterLossLiters;
                    }
                    else if (eq.Type == EquipmentType.Keg || eq.Type == EquipmentType.Other)
                    {
                        eq.PackagingLossLiters ??= setup.DefaultPackagingLossLiters;
                    }
                }
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            _seedLock.Release();
        }
    }

    public static List<Ingredient> GetDefaultCatalogIngredients() =>
    [
        // ==========================================
        // 1. Fermentables & Grains (31 items)
        // ==========================================
        // Base Malts
        new()
        {
            Name = "Pale Malt (2-Row)",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.037m,
            ColorSrm = 1.8m,
            Description = "Clean, neutral sweet base malt canvas for American IPAs, Pale Ales, and general craft ales."
        },
        new()
        {
            Name = "Pilsner Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.037m,
            ColorSrm = 1.6m,
            Description = "Lightest European base malt, delicate sweet cracker, honey, and fresh bread dough notes."
        },
        new()
        {
            Name = "Maris Otter",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.038m,
            ColorSrm = 3.0m,
            Description = "Premium British heritage pale ale malt imparting rich biscuit, toasted bread, and nutty depth."
        },
        new()
        {
            Name = "Golden Promise",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.038m,
            ColorSrm = 2.5m,
            Description = "Premium heritage Scottish pale ale malt delivering sweet, bready, and silky grain foundation for ales and hazies."
        },
        new()
        {
            Name = "Vienna Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.036m,
            ColorSrm = 4.0m,
            Description = "Imparts a golden to amber hue with luscious toasted bread crust character and gentle sweetness."
        },
        new()
        {
            Name = "Munich Malt (Light)",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.035m,
            ColorSrm = 9.0m,
            Description = "Adds rich malty backbone, amber color, and crusty bread notes without caramel sweetness."
        },
        new()
        {
            Name = "Munich Malt (Dark)",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.035m,
            ColorSrm = 15.0m,
            Description = "Deep maltiness, intensely toasted bread crust, and copper coloration for bocks and dunkels."
        },

        // Specialty Crystal / Caramel Malts
        new()
        {
            Name = "Carapils / Dextrine Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.033m,
            ColorSrm = 1.5m,
            Description = "Boosts body, mouthfeel, and foam stability through unfermentable dextrins with minimal color."
        },
        new()
        {
            Name = "Caramel / Crystal 20L",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.035m,
            ColorSrm = 20.0m,
            Description = "Sweet candied sugar, light honey, and fresh caramel notes with a light golden hue."
        },
        new()
        {
            Name = "Caramel / Crystal 40L",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.034m,
            ColorSrm = 40.0m,
            Description = "Classic caramel, toasted marshmallow, and light toffee with a deep golden/light copper hue."
        },
        new()
        {
            Name = "Caramel / Crystal 60L",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.034m,
            ColorSrm = 60.0m,
            Description = "Medium crystal malt providing pronounced toffee sweetness, body, and reddish-amber color."
        },
        new()
        {
            Name = "Caramel / Crystal 120L",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.033m,
            ColorSrm = 120.0m,
            Description = "Dark fruit, raisin, prune, burnt sugar, and dark reddish-brown color for porters and barleywines."
        },
        new()
        {
            Name = "CaraMunich II",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.034m,
            ColorSrm = 48.0m,
            Description = "Benchmark German caramel malt imparting rich toffee, bread crust, and deep copper hue for Märzen and Bocks."
        },
        new()
        {
            Name = "Special B",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.030m,
            ColorSrm = 150.0m,
            Description = "Belgian dark crystal imparting heavy dark raisins, black figs, plums, and brown sugar notes."
        },
        new()
        {
            Name = "Melanoidin Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.034m,
            ColorSrm = 28.0m,
            Description = "Provides intensely malty, crusty bread and honeyed biscuit notes simulating traditional decoction mashing."
        },
        new()
        {
            Name = "Biscuit / Victory Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.035m,
            ColorSrm = 25.0m,
            Description = "Warm toasted bread, saltine crackers, and nutty grain flavor without adding caramel or crystal sweetness."
        },
        new()
        {
            Name = "Smoked Malt (Rauchmalz)",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.037m,
            ColorSrm = 2.5m,
            Description = "Beechwood-smoked malt contributing authentic campfire and savory aroma for classic Bamberg Rauchbier."
        },
        new()
        {
            Name = "Acidulated Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.034m,
            ColorSrm = 2.0m,
            Description = "Naturally soured malt reducing mash pH to 5.2-5.6 for Reinheitsgebot compliance and enhanced enzyme activity."
        },

        // Roasted Malts
        new()
        {
            Name = "Chocolate Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.028m,
            ColorSrm = 400.0m,
            Description = "Rich cocoa powder, dark unsweetened chocolate, coffee grounds, and nutty roast."
        },
        new()
        {
            Name = "Pale Chocolate Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.030m,
            ColorSrm = 220.0m,
            Description = "Gentle roast malt offering hazelnut, smooth cocoa, and milk chocolate character without sharp acrid bitterness."
        },
        new()
        {
            Name = "Roasted Barley",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.025m,
            ColorSrm = 300.0m,
            Description = "Unmalted roasted grain imparting deep black color, espresso, and signature dry roasty bite."
        },
        new()
        {
            Name = "Black Patent / Black Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.025m,
            ColorSrm = 500.0m,
            Description = "Intensely roasted malt providing sharp, ashen charcoal bitterness and deep jet-black coloration."
        },
        new()
        {
            Name = "Carafa Special III",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.032m,
            ColorSrm = 520.0m,
            Description = "De-husked roasted malt providing pitch-black color and smooth coffee notes without harsh astringency."
        },

        // Wheat, Rye & Adjuncts
        new()
        {
            Name = "Malted Wheat",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.037m,
            ColorSrm = 2.5m,
            Description = "High protein grain adding pale haze, fluffy head retention, and bready tart wheat flour notes."
        },
        new()
        {
            Name = "Dark Wheat Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.037m,
            ColorSrm = 7.0m,
            Description = "Deep bready crust, rich wheat dough, light caramel, and soft sweetness for Dunkelweizen and Weizenbock."
        },
        new()
        {
            Name = "Flaked Wheat",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.036m,
            ColorSrm = 2.0m,
            Description = "Unmalted wheat imparting raw wheat tang, creamy mouthfeel, and stable protein haze for Witbiers and NEIPAs."
        },
        new()
        {
            Name = "Torrified Wheat",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.036m,
            ColorSrm = 1.8m,
            Description = "Puffed wheat boosting foam stability, creamy head retention, and lacing without adding cloudiness."
        },
        new()
        {
            Name = "Rye Malt",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.030m,
            ColorSrm = 3.5m,
            Description = "Imparts a distinct peppery spice, crisp rye bread flavor, and full viscous mouthfeel."
        },
        new()
        {
            Name = "Flaked Oats",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.033m,
            ColorSrm = 2.0m,
            Description = "Enhances mouthfeel, silky body, and pillowy haze for Oatmeal Stouts and Hazy IPAs."
        },
        new()
        {
            Name = "Flaked Corn (Maize)",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.037m,
            ColorSrm = 1.0m,
            Description = "Lightens beer body and color while contributing a subtle, sweet corn character for Cream Ales and Lagers."
        },
        new()
        {
            Name = "Flaked Rice",
            Type = IngredientType.Fermentable,
            PotentialGravity = 1.038m,
            ColorSrm = 1.0m,
            Description = "Extremely neutral adjunct that dries out finish and thins body for clean, crisp dry lagers."
        },

        // ==========================================
        // 2. Hops (30 items)
        // ==========================================
        // Continental Noble & German Cultivars
        new()
        {
            Name = "Hallertau Mittelfrüh",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 4.0m,
            Description = "Classic German noble hop with delicate floral, herbal, tea-like, and sweet spice characteristics."
        },
        new()
        {
            Name = "Hallertauer Tradition",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 6.0m,
            Description = "Modern German noble hop bred from Mittelfrüh with clean herbal, floral, and subtle sweet spice notes."
        },
        new()
        {
            Name = "Hersbrucker",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 3.5m,
            Description = "Historic German Landrasse aroma hop with delicate floral blossom, sweet hay, and mild spicy nuances."
        },
        new()
        {
            Name = "Perle",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 8.0m,
            Description = "Premier German dual-purpose hop providing mint, cedar, peppery spice, and clean refined bittering."
        },
        new()
        {
            Name = "Saaz",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 3.5m,
            Description = "Noble Czech hop with delicate, distinctly spicy, earthy, and herbal character."
        },
        new()
        {
            Name = "Spalt",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 4.5m,
            Description = "Traditional German noble hop with woody, peppery spice, herbal, and floral accents."
        },
        new()
        {
            Name = "Spalter Select",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 4.5m,
            Description = "Refined noble aroma cross of Spalt and Mittelfrüh featuring peppery spice, evergreen, and dried flowers."
        },
        new()
        {
            Name = "Hallertau Blanc",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 10.5m,
            Description = "Modern German flavor hop imparting Sauvignon Blanc white wine, gooseberry, and elderflower aromas."
        },
        new()
        {
            Name = "Mandarina Bavaria",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 8.5m,
            Description = "Modern German hop delivering bright sweet tangerine, mandarin orange peel, and fresh citrus notes."
        },
        new()
        {
            Name = "Tettnang",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 4.0m,
            Description = "Mild German noble hop with floral, herbal, and faint ginger/black pepper spice."
        },

        // Classic American C-Hops
        new()
        {
            Name = "Cascade",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 5.5m,
            Description = "Classic American hop with vibrant floral, grapefruit citrus zest, and pine undertones."
        },
        new()
        {
            Name = "Centennial",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 10.0m,
            Description = "Versatile 'Super Cascade' with intense lemon-citrus, clean floral aroma, and smooth bitterness."
        },
        new()
        {
            Name = "Columbus / CTZ",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 15.0m,
            Description = "Pungent, dank, resinous pine, black pepper, and earthy citrus with firm bittering power."
        },
        new()
        {
            Name = "Chinook",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 13.0m,
            Description = "Distinct pine forest resin, grapefruit rind, and woody spice with assertive bittering potential."
        },

        // Modern High-Oil / Tropical Varieties
        new()
        {
            Name = "Citra",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 12.5m,
            Description = "Intense citrus and tropical fruit punch (mango, passionfruit, lime, grapefruit) aromas."
        },
        new()
        {
            Name = "Mosaic",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 12.0m,
            Description = "Complex aromas of ripe blueberry, stone fruit, tangerine, papaya, and earthy pine."
        },
        new()
        {
            Name = "Simcoe",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 13.0m,
            Description = "Bright pine sap, passionfruit, apricot, and clean woodsy dankness with excellent dual-purpose utility."
        },
        new()
        {
            Name = "Galaxy",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 14.5m,
            Description = "Australian hop with massive, concentrated passionfruit, juicy peach, and citrus aromas."
        },
        new()
        {
            Name = "Nelson Sauvin",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 12.5m,
            Description = "New Zealand hop with unique crushed white grape, gooseberry, and fruity white wine must nuances."
        },
        new()
        {
            Name = "Amarillo",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 9.5m,
            Description = "Sweet orange marmalade, candied tangerine, and lemon with a soft, round bitterness profile."
        },
        new()
        {
            Name = "El Dorado",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 15.0m,
            Description = "Intensely sweet tropical notes of candied pineapple, watermelon, and stone fruit."
        },
        new()
        {
            Name = "Sabro",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 14.0m,
            Description = "Distinctive sweet coconut cream, piña colada, tropical fruit, and cedar woodiness."
        },
        new()
        {
            Name = "Idaho 7",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 13.0m,
            Description = "High-oil powerhouse hop packed with sweet apricot, papaya, zesty tangerine, and dank resinous pine."
        },
        new()
        {
            Name = "Motueka",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 7.0m,
            Description = "Iconic New Zealand hop with vibrant crushed lime, lemon zest, and tropical mojito freshness."
        },

        // Classic European Bittering & Dual-Purpose
        new()
        {
            Name = "Magnum",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 13.5m,
            Description = "Clean, smooth, neutral bittering hop with low cohumulone and minimal lingering harshness."
        },
        new()
        {
            Name = "Warrior",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 16.0m,
            Description = "High-alpha bittering hop with clean, smooth finish and mild citrus-herbal tones."
        },
        new()
        {
            Name = "Northern Brewer",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 8.5m,
            Description = "Classic woody mint, evergreen pine, and herbal spice hop signature to California Common and continental lagers."
        },
        new()
        {
            Name = "East Kent Goldings",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 5.0m,
            Description = "Quintessential English hop imparting sweet floral lavender, honey, and earthy spice."
        },
        new()
        {
            Name = "Fuggle",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 4.5m,
            Description = "Classic English aroma hop with earthy, woody, mossy, and mild tea-like characteristics."
        },
        new()
        {
            Name = "Styrian Goldings (Celeia)",
            Type = IngredientType.Hop,
            Form = "Pellet",
            AlphaAcidPercent = 5.0m,
            Description = "Slovenian classic aroma hop delivering earthy noble resin, floral lavender, and gentle herbal spice."
        },

        // ==========================================
        // 3. Yeasts (16 items)
        // ==========================================
        new()
        {
            Name = "Fermentis SafAle US-05",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 81.0m,
            Description = "The quintessential clean American ale yeast producing crisp, hoppy beers with neutral profile."
        },
        new()
        {
            Name = "Lallemand LalBrew BRY-97",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 80.0m,
            Description = "American West Coast ale yeast with high flocculation that accentuates bright hop aroma and clean bitterness."
        },
        new()
        {
            Name = "Lallemand LalBrew Verdant IPA",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 79.0m,
            Description = "Specialty ale strain accentuating juicy apricot, soft peach, and tropical esters with full silky mouthfeel."
        },
        new()
        {
            Name = "Fermentis SafAle S-04",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 75.0m,
            Description = "Classic English ale strain producing fast fermentation, fruity esters, and rapid compact flocculation."
        },
        new()
        {
            Name = "Lallemand LalBrew Nottingham",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 78.0m,
            Description = "Fast-fermenting, versatile British ale yeast with neutral profile and excellent flocculation."
        },
        new()
        {
            Name = "Fermentis SafAle K-97",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 82.0m,
            Description = "Traditional German top-cropping ale yeast fermenting crisp, clean, and floral for authentic Kölsch and Altbier."
        },
        new()
        {
            Name = "Fermentis SafLager W-34/70",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 83.0m,
            Description = "World-famous Weihenstephan lager strain for clean, crisp, neutral European lagers and pilsners."
        },
        new()
        {
            Name = "Fermentis SafLager S-189",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 82.0m,
            Description = "Swiss lager strain producing very crisp, dry, elegant lagers with high drinkability."
        },
        new()
        {
            Name = "Fermentis SafLager S-23",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 82.0m,
            Description = "Berlin VLB lager strain producing crisp, clean lagers with subtle fruity and floral continental notes."
        },
        new()
        {
            Name = "Lallemand LalBrew Diamond Lager",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 81.0m,
            Description = "True German lager strain with clean fermentation, excellent reduction of sulfur, and crisp finish."
        },
        new()
        {
            Name = "Fermentis SafAle BE-256 (Abbaye)",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 84.0m,
            Description = "Belgian Abbey ale yeast producing rich stone fruit, raisin, and spicy phenolic clove notes."
        },
        new()
        {
            Name = "Lallemand LalBrew Belle Saison",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 90.0m,
            Description = "High-attenuation Belgian Saison yeast imparting peppery spice, fruity esters, and super-dry finish."
        },
        new()
        {
            Name = "Wyeast 3068 / WLP300 Weihenstephan Weizen",
            Type = IngredientType.Yeast,
            Form = "Liquid",
            AttenuationPercent = 75.0m,
            Description = "Historic Weihenstephan strain producing the classic harmony of ripe banana (isoamyl acetate) and spicy clove (4-VG)."
        },
        new()
        {
            Name = "Lallemand LalBrew Munich Classic",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 75.0m,
            Description = "Bavarian wheat beer strain expressing signature banana (isoamyl acetate) and clove (4-VG) profile."
        },
        new()
        {
            Name = "Fermentis SafAle WB-06",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 88.0m,
            Description = "Specialty wheat yeast producing subtle clove phenols and tart, dry, refreshing finish."
        },
        new()
        {
            Name = "Lallemand LalBrew Voss Kveik",
            Type = IngredientType.Yeast,
            Form = "Dry",
            AttenuationPercent = 79.0m,
            Description = "Norwegian farmhouse strain fermenting cleanly at high temperatures (25-40°C) with delicate citrus notes."
        },

        // ==========================================
        // 4. Miscellaneous & Water Agents (10 items)
        // ==========================================
        new()
        {
            Name = "Irish Moss",
            Type = IngredientType.Other,
            Description = "Natural red seaweed kettle fining agent added late in the boil to promote protein coagulation and wort clarity."
        },
        new()
        {
            Name = "Whirlfloc",
            Type = IngredientType.Other,
            Description = "Purified carrageenan tablet fining agent enhancing cold break precipitation in the boil kettle."
        },
        new()
        {
            Name = "Gypsum (Calcium Sulfate)",
            Type = IngredientType.Other,
            Description = "Brewing water salt lowering mash pH and accentuating crisp hop bitterness and sulfur character."
        },
        new()
        {
            Name = "Calcium Chloride",
            Type = IngredientType.Other,
            Description = "Brewing water salt lowering mash pH and accentuating round, sweet malt fullness."
        },
        new()
        {
            Name = "Epsom Salt (Magnesium Sulfate)",
            Type = IngredientType.Other,
            Description = "Brewing water salt providing essential magnesium for yeast metabolism and sulfate for hop crispness."
        },
        new()
        {
            Name = "Lactic Acid (88%)",
            Type = IngredientType.Other,
            Description = "Concentrated brewing acid used for precise sparge and mash pH reduction."
        },
        new()
        {
            Name = "Yeast Nutrient",
            Type = IngredientType.Other,
            Description = "Blend of amino acids, zinc, and vitamins ensuring vigorous fermentation and complete attenuation."
        },
        new()
        {
            Name = "Coriander Seed",
            Type = IngredientType.Other,
            Description = "Crushed aromatic spice imparting herbal citrus, coriander, and gentle peppery warmth to Belgian styles."
        },
        new()
        {
            Name = "Sweet Orange Peel",
            Type = IngredientType.Other,
            Description = "Dried citrus peel adding bright orange aroma and subtle sweetness to wheat beers and Belgian ales."
        },
        new()
        {
            Name = "Gelatin",
            Type = IngredientType.Other,
            Description = "Post-fermentation fining collagen clarifying cold beer by dropping yeast and haze particles."
        }
    ];
}