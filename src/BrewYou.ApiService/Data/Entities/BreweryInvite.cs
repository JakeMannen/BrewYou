namespace BrewYou.ApiService.Data.Entities;

public class BreweryInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BrewerySetupId { get; set; }
    public BrewerySetup? BrewerySetup { get; set; }

    public required string InvitedEmail { get; set; }
    public required string InviteCode { get; set; }

    public BreweryRole Role { get; set; } = BreweryRole.Brewer;

    public required string InvitedByUserId { get; set; }
    public ApplicationUser? InvitedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime? AcceptedAt { get; set; }
}