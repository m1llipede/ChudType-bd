using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChudType.Mac;

internal sealed class PersonalizationWindow : Window
{
    private readonly MacPersonalization data;
    private readonly ListBox dictionary = new();
    private readonly ListBox snippets = new();
    private readonly ListBox transforms = new();

    public PersonalizationWindow(MacPersonalization data)
    {
        this.data = data;
        Title = "ChudType Personalization";
        Width = 820;
        Height = 620;
        MinWidth = 700;
        MinHeight = 500;
        Content = new TabControl
        {
            Margin = new Thickness(18),
            ItemsSource = new[]
            {
                new TabItem { Header = "Dictionary", Content = DictionaryPage() },
                new TabItem { Header = "Snippets", Content = SnippetsPage() },
                new TabItem { Header = "Transforms", Content = TransformsPage() }
            }
        };
        Refresh();
    }

    private Control DictionaryPage()
    {
        var spoken = new TextBox { PlaceholderText = "What ChudType hears" };
        var written = new TextBox { PlaceholderText = "Correct spelling" };
        var add = new Button { Content = "Add word" };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(spoken.Text)) return;
            string output = string.IsNullOrWhiteSpace(written.Text) ? spoken.Text.Trim() : written.Text.Trim();
            data.Dictionary.Add(new(spoken.Text.Trim(), output));
            spoken.Clear(); written.Clear(); Save();
        };
        var remove = new Button { Content = "Remove selected" };
        remove.Click += (_, _) => { if (dictionary.SelectedItem is DictionaryItem item) { data.Dictionary.Remove(item); Save(); } };
        return Page("Dictionary", "Teach names, jargon, acronyms, and spelling corrections.", spoken, written, add, remove, dictionary);
    }

    private Control SnippetsPage()
    {
        var trigger = new TextBox { PlaceholderText = "Spoken trigger, e.g. my email" };
        var content = new TextBox { PlaceholderText = "Text to insert", AcceptsReturn = true, MinHeight = 80 };
        var add = new Button { Content = "Add snippet" };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(trigger.Text) || string.IsNullOrWhiteSpace(content.Text)) return;
            data.Snippets.Add(new(trigger.Text.Trim(), content.Text));
            trigger.Clear(); content.Clear(); Save();
        };
        var remove = new Button { Content = "Remove selected" };
        remove.Click += (_, _) => { if (snippets.SelectedItem is SnippetItem item) { data.Snippets.Remove(item); Save(); } };
        return Page("Snippets", "Say a short trigger to insert text you use often.", trigger, content, add, remove, snippets);
    }

    private Control TransformsPage()
    {
        var name = new TextBox { PlaceholderText = "Transform name" };
        var instruction = new TextBox { PlaceholderText = "Rewrite instruction", AcceptsReturn = true, MinHeight = 80 };
        var add = new Button { Content = "Create transform" };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(instruction.Text)) return;
            data.Transforms.Add(new(name.Text.Trim(), instruction.Text.Trim()));
            name.Clear(); instruction.Clear(); Save();
        };
        var remove = new Button { Content = "Remove selected" };
        remove.Click += (_, _) => { if (transforms.SelectedItem is TransformItem item) { data.Transforms.Remove(item); Save(); } };
        return Page("Transforms", "Reusable rewrite instructions. Applying them locally is the next model milestone.", name, instruction, add, remove, transforms);
    }

    private static Control Page(string title, string detail, Control first, Control second, Button add, Button remove, ListBox list)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, remove } };
        return new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold },
                new TextBlock { Text = detail, Foreground = Brushes.Gray }, first, second, buttons,
                new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), MinHeight = 220, Child = list }
            }
        };
    }

    private void Save()
    {
        MacPersonalizationStore.Save(data);
        Refresh();
    }

    private void Refresh()
    {
        dictionary.ItemsSource = data.Dictionary.ToArray();
        snippets.ItemsSource = data.Snippets.ToArray();
        transforms.ItemsSource = data.Transforms.ToArray();
    }
}
