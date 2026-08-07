using Microsoft.AspNetCore.Identity;

namespace RasHub.Web.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public const int ApiKeyMaxLength = 64;

    public string? ApiKey { get; set; }
}