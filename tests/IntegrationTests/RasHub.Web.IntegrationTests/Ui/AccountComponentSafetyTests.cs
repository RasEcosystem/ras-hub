using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace RasHub.Web.IntegrationTests.Ui;

public sealed class AccountComponentSafetyTests
{
    [Fact]
    public void Account_pages_do_not_share_mutable_attribute_dictionaries()
    {
        var accountPageTypes = typeof(Program).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                "RasHub.Web.Components.Account.Pages",
                StringComparison.Ordinal) == true)
            .Where(type => typeof(ComponentBase).IsAssignableFrom(type))
            .ToArray();

        var sharedDictionaries = accountPageTypes
            .SelectMany(type => type.GetFields(
                BindingFlags.NonPublic |
                BindingFlags.Static))
            .Where(field => field.FieldType ==
                            typeof(Dictionary<string, object>))
            .Select(field => $"{field.DeclaringType?.Name}.{field.Name}")
            .ToArray();

        Assert.NotEmpty(accountPageTypes);
        Assert.Empty(sharedDictionaries);
    }
}