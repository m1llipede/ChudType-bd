using System.Text.Json;

namespace ChudType.Mac;

internal sealed class MacPreferences
{
    public string Language { get; set; } = "English";
    public string AppLanguage { get; set; } = "English";
    public string Theme { get; set; } = "System";
    public bool LaunchAtLogin { get; set; }
    public bool ShowFloatingBar { get; set; } = true;
    public bool ShowInDock { get; set; } = true;
    public bool Sounds { get; set; } = true;
    public bool MuteAudioWhileDictating { get; set; }
    public bool Suggestions { get; set; } = true;
    public bool Announcements { get; set; } = true;
    public bool Milestones { get; set; } = true;
    public bool AutoAddDictionary { get; set; }
    public bool CreatorMode { get; set; }
    public bool ImproveModels { get; set; }
    public bool StoreHistory { get; set; }
    public bool ContextAwareness { get; set; }
    public bool StoreLocally { get; set; } = true;
    public string PersonalStyle { get; set; } = "Casual";
    public string WorkStyle { get; set; } = "Formal";
    public string EmailStyle { get; set; } = "Formal";
    public string OtherStyle { get; set; } = "Casual";
    public bool AutoCleanup { get; set; } = true;
}

internal static class MacPreferencesStore
{
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChudType");
    private static string FilePath => Path.Combine(DirectoryPath, "mac-preferences.json");

    public static MacPreferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<MacPreferences>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception) { return new(); }
    }

    public static void Save(MacPreferences value)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
