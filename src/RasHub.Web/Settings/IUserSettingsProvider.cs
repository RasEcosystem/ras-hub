namespace RasHub.Web.Settings;

public interface IUserSettingsProvider
{
    event Action<UserSettings>? SettingsChanged;

    Task<UserSettings> GetAsync();

    Task UpdateAsync(UserSettings settings);
}