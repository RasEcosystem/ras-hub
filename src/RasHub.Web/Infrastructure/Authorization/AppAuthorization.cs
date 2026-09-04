namespace RasHub.Web.Infrastructure.Authorization;

public static class AppRoles
{
    public const string Admin = "Admin";
}

public static class AppPolicies
{
    public const string ManageGlobalSettings =
        "ManageGlobalSettings";

    public const string ManageRasGates =
        "ManageRasGates";

    public const string ManageRasEndpoints =
        "ManageRasEndpoints";
}
