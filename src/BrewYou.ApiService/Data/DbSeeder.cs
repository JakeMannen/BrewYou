using BrewYou.ApiService.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(BrewYouDbContext db)
    {
        if (await db.Ingredients.AnyAsync())
        {
            return;
        }

        var ingredients = new List<Ingredient>
        {
            // Fermentables
            new() {
                Name = "Pale Malt (2-Row)",
                Type = IngredientType.Fermentable,
                PotentialGravity = 1.037m,
                ColorSrm = 1.8m,
                Description = "Base malt for American and English ales, clean sweet malt character."
            },
            new() {
                Name = "Pilsner Malt",
                Type = IngredientType.Fermentable,
                PotentialGravity = 1.037m,
                ColorSrm = 1.6m,
                Description = "Lightest base malt, delicate malt flavor with honey/biscuit notes."
            },
            new() {
                Name = "Munich Malt",
                Type = IngredientType.Fermentable,
                PotentialGravity = 1.035m,
                ColorSrm = 9.0m,
                Description = "Adds deep amber color, rich bread crust and malt backbone."
            },
            new() {
                Name = "Caramel / Crystal 60L",
                Type = IngredientType.Fermentable,
                PotentialGravity = 1.034m,
                ColorSrm = 60.0m,
                Description = "Medium crystal malt providing caramel, toffee sweetness and body."
            },
            new() {
                Name = "Roasted Barley",
                Type = IngredientType.Fermentable,
                PotentialGravity = 1.025m,
                ColorSrm = 300.0m,
                Description = "Unmalted roasted grain imparting deep black color, espresso and dry roasty bite."
            },
            new() {
                Name = "Flaked Oats",
                Type = IngredientType.Fermentable,
                PotentialGravity = 1.033m,
                ColorSrm = 2.0m,
                Description = "Enhances mouthfeel, creamy haze and head retention."
            },

            // Hops
            new() {
                Name = "Citra",
                Type = IngredientType.Hop,
                AlphaAcidPercent = 12.5m,
                Description = "Intense citrus, tropical fruit (mango, lime, grapefruit) aromas."
            },
            new() {
                Name = "Mosaic",
                Type = IngredientType.Hop,
                AlphaAcidPercent = 11.5m,
                Description = "Complex aromas of berry, tangerine, papaya, and earthy pine."
            },
            new() {
                Name = "Cascade",
                Type = IngredientType.Hop,
                AlphaAcidPercent = 5.5m,
                Description = "Classic American hop with grapefruit, floral, and spicy undertones."
            },
            new() {
                Name = "Centennial",
                Type = IngredientType.Hop,
                AlphaAcidPercent = 9.5m,
                Description = "Versatile 'Super Cascade' with lemon-citrus and clean floral bitterness."
            },
            new() {
                Name = "Magnum",
                Type = IngredientType.Hop,
                AlphaAcidPercent = 14.0m,
                Description = "Clean, smooth bittering hop with low aromatic residue."
            },
            new() {
                Name = "Saaz",
                Type = IngredientType.Hop,
                AlphaAcidPercent = 3.5m,
                Description = "Noble Czech hop with delicate, spicy, herbal character."
            },

            // Yeasts
            new() {
                Name = "Fermentis SafAle US-05",
                Type = IngredientType.Yeast,
                AttenuationPercent = 81.0m,
                Description = "The quintessential clean American ale yeast producing crisp, hoppy beers."
            },
            new() {
                Name = "Fermentis SafLager W-34/70",
                Type = IngredientType.Yeast,
                AttenuationPercent = 83.0m,
                Description = "World-famous Weihenstephan lager strain for clean, neutral lagers and pilsners."
            },
            new() {
                Name = "Lallemand Belle Saison",
                Type = IngredientType.Yeast,
                AttenuationPercent = 90.0m,
                Description = "High attenuation Belgian ale yeast with spicy, peppery, and fruity esters."
            },
            new() {
                Name = "Lallemand Nottingham",
                Type = IngredientType.Yeast,
                AttenuationPercent = 77.0m,
                Description = "Fast fermenting, high flocculation British ale yeast."
            }
        };

        await db.Ingredients.AddRangeAsync(ingredients);
        await db.SaveChangesAsync();
    }
}
