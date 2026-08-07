using Microsoft.AspNetCore.Identity;

namespace RasHub.Web.Data;

public class ApplicationUser : IdentityUser
{
    public const int ApiKeyMaxLength = 64;

    public string? ApiKey { get; set; }
}