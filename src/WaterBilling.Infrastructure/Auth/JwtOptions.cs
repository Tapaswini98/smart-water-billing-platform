using System.ComponentModel.DataAnnotations;

namespace WaterBilling.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    // HS256 with a 256-bit key. Symmetric is the right call for a single on-prem
    // service issuing and validating its own tokens; asymmetric buys nothing until
    // a second service needs to validate without the signing secret.
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters (256 bits).")]
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "smart-water-billing";

    [Required]
    public string Audience { get; set; } = "smart-water-billing-clients";

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 60;
}
