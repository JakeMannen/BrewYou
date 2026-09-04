using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Data;

public class BrewYouDbContext : IdentityDbContext<ApplicationUser>
{
    public BrewYouDbContext(DbContextOptions<BrewYouDbContext> options)
        : base(options)
    {
    }

    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Recipe>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Name).HasMaxLength(150).IsRequired();
            entity.Property(r => r.BeerStyle).HasMaxLength(100).IsRequired();
            entity.Property(r => r.BatchSizeLiters).HasPrecision(6, 2);
            entity.Property(r => r.EfficiencyPercent).HasPrecision(5, 2);
            entity.Property(r => r.OriginalGravity).HasPrecision(5, 4);
            entity.Property(r => r.FinalGravity).HasPrecision(5, 4);
            entity.Property(r => r.AlcoholByVolume).HasPrecision(4, 2);
            entity.Property(r => r.BitternessIbu).HasPrecision(6, 2);
            entity.Property(r => r.ColorSrm).HasPrecision(5, 2);

            entity.HasOne(r => r.User)
                  .WithMany(u => u.Recipes)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Ingredient>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Name).HasMaxLength(150).IsRequired();
            entity.Property(i => i.PotentialGravity).HasPrecision(5, 4);
            entity.Property(i => i.ColorSrm).HasPrecision(5, 2);
            entity.Property(i => i.AlphaAcidPercent).HasPrecision(4, 2);
            entity.Property(i => i.AttenuationPercent).HasPrecision(4, 2);
        });

        builder.Entity<RecipeIngredient>(entity =>
        {
            entity.HasKey(ri => ri.Id);
            entity.Property(ri => ri.Amount).HasPrecision(8, 3);
            entity.Property(ri => ri.Unit).HasMaxLength(20);

            entity.HasOne(ri => ri.Recipe)
                  .WithMany(r => r.Ingredients)
                  .HasForeignKey(ri => ri.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ri => ri.Ingredient)
                  .WithMany(i => i.RecipeIngredients)
                  .HasForeignKey(ri => ri.IngredientId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
