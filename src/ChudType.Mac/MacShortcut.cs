using System.Text.Json;

namespace ChudType.Mac;

internal sealed record MacShortcut(string Modifier, string Trigger, string Mode = "Hold to talk")
{
    public static MacShortcut Default { get; } = new("None", "F8", "Hold to talk");
    public bool IsSafe => Modifier != "None" || Trigger.Length != 1 || !char.IsLetterOrDigit(Trigger[0]);
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
            var shortcut = JsonSerializer.Deserialize<MacShortcut>(File.ReadAllText(FilePath)) ?? MacShortcut.Default;
            return shortcut.IsSafe ? shortcut : MacShortcut.Default;
        }
        catch (Exception) { return MacShortcut.Default; }
    }

    public static void Save(MacShortcut value)
    {
        if (!value.IsSafe) throw new InvalidOperationException("Letters and numbers require Control, Command, Option, or Shift.");
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
