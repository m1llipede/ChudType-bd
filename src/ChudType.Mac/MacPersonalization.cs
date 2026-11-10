using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChudType.Mac;

internal sealed record DictionaryItem(string Spoken, string Written)
{
    public override string ToString() => Spoken == Written ? Written : $"{Spoken} → {Written}";
}

internal sealed record SnippetItem(string Trigger, string Content)
{
    public override string ToString() => $"{Trigger} → {Content}";
}

internal sealed record TransformItem(string Name, string Instruction)
{
    public override string ToString() => $"{Name} · {Instruction}";
}

internal sealed class MacPersonalization
{
    public List<DictionaryItem> Dictionary { get; set; } = [];
    public List<SnippetItem> Snippets { get; set; } = [];
    public List<TransformItem> Transforms { get; set; } =
    [
        new("Polish", "Improve clarity and conciseness without changing meaning"),
        new("Prompt engineer", "Restructure the text into a precise, useful AI prompt")
    ];

    public string Apply(string text)
    {
        string trimmed = text.Trim();
        foreach (var snippet in Snippets)
        {
            if (trimmed.Equals(snippet.Trigger, StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("insert " + snippet.Trigger, StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("insert snippet " + snippet.Trigger, StringComparison.OrdinalIgnoreCase))
                return snippet.Content;
        }
        foreach (var item in Dictionary.Where(item => !string.IsNullOrWhiteSpace(item.Spoken)))
            trimmed = Regex.Replace(trimmed, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(item.Spoken)}(?![\p{{L}}\p{{N}}])",
                item.Written, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return trimmed;
    }
}

internal static class MacPersonalizationStore
{
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChudType");
    private static string FilePath => Path.Combine(DirectoryPath, "personalization.json");

    public static MacPersonalization Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<MacPersonalization>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception) { return new(); }
    }

    public static void Save(MacPersonalization value)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
