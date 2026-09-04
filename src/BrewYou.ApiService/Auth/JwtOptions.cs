namespace BrewYou.ApiService.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "BrewYou";
    public string Audience { get; set; } = "BrewYouApp";
    public string SecretKey { get; set; } = "SuperSecretBrewYouKey_AtLeast32BytesLong!";
    public int AccessTokenExpirationMinutes { get; set; } = 60;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
