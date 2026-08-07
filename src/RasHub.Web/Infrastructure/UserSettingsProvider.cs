using Nava.Settings.Abstractions;
using RasHub.Web.Settings;

namespace RasHub.Web.Infrastructure;

public sealed class UserSettingsProvider(
    IScopedSettingsProvider<UserSettings> settingsProvider,
    CurrentUserAccessor currentUserAccessor)
    : IUserSettingsProvider
{
    public event Action<UserSettings>? SettingsChanged;

    public async Task<UserSettings> GetAsync()
    {
        var scopeId = await currentUserAccessor.GetUserIdAsync();

        return await settingsProvider.GetAsync(
                   scopeId)
               ?? new UserSettings();
    }

    public async Task UpdateAsync(UserSettings settings)
    {
        var scopeId = await currentUserAccessor.GetUserIdAsync();

        await settingsProvider.UpdateAsync(
            settings,
            scopeId);

        SettingsChanged?.Invoke(settings);
    }
}