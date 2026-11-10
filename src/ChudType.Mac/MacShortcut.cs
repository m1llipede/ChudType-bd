using System.Text.Json;

namespace ChudType.Mac;

internal sealed record MacShortcut(string Modifier, string Trigger, string Mode = "Hold to talk")
{
    public static MacShortcut Default { get; } = new("None", "F8", "Hold to talk");
    public override string ToString() => Modifier == "None" ? Trigger : Modifier + "+" + Trigger;
}

internal static class MacShortcutStore
{
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChudType");
    private static string FilePath => Path.Combine(DirectoryPath, "mac-settings.json");

    public static MacShortcut Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return MacShortcut.Default;
            return JsonSerializer.Deserialize<MacShortcut>(File.ReadAllText(FilePath)) ?? MacShortcut.Default;
        }
        catch (Exception) { return MacShortcut.Default; }
    }

    public static void Save(MacShortcut value)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
