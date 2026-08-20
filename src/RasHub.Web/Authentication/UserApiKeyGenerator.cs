using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace RasHub.Web.Authentication;

public static class UserApiKeyGenerator
{
    public static string Generate()
    {
        return $"rsh_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
    }
}
