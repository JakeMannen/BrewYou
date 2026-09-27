namespace BrewYou.ApiService.Data.Entities;

public class BreweryMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BrewerySetupId { get; set; }
    public BrewerySetup? BrewerySetup { get; set; }

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public BreweryRole Role { get; set; } = BreweryRole.Brewer;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}