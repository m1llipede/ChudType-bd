using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace ChudType.Mac;

internal sealed class SettingsWindow : Window
{
    private readonly MacPreferences preferences;

    public SettingsWindow(MacPreferences preferences)
    {
        this.preferences = preferences;
        Title = "ChudType Settings";
        Width = 780;
        Height = 620;
        MinWidth = 680;
        MinHeight = 500;
        Content = new TabControl
        {
            Margin = new Thickness(18),
            ItemsSource = new[]
            {
                Page("General", General()),
                Page("System", SystemSettings()),
                Page("Style", StyleSettings()),
                Page("Data & Privacy", PrivacySettings())
            }
        };
    }

    private static TabItem Page(string name, Control content) => new() { Header = name, Content = new ScrollViewer { Content = content } };

    private StackPanel General()
    {
        var panel = Section("General", "Core dictation preferences");
        panel.Children.Add(Choice("My languages", "Languages recognized during dictation", preferences.Language,
            new[] { "English", "Spanish", "French", "German", "Italian", "Portuguese", "Japanese", "Korean", "Chinese" },
            value => preferences.Language = value));
        panel.Children.Add(Choice("App language", "Language used by the ChudType interface", preferences.AppLanguage,
            new[] { "English", "Spanish", "French", "German" }, value => preferences.AppLanguage = value));
        panel.Children.Add(Choice("Theme", "Follow macOS or choose a light or dark appearance", preferences.Theme,
            new[] { "System", "Dark", "Light" }, value =>
            {
                preferences.Theme = value;
                if (Application.Current is { } app)
                    app.RequestedThemeVariant = value switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default };
            }));
        return panel;
    }

    private StackPanel SystemSettings()
    {
        var panel = Section("System", "Behavior, sounds, and notifications");
        panel.Children.Add(Toggle("Launch app at login", "Start ChudType after you sign in", preferences.LaunchAtLogin, value =>
        {
            MacSystemIntegration.SetLaunchAtLogin(value);
            preferences.LaunchAtLogin = value;
        }));
        panel.Children.Add(Toggle("Show floating bar", "Keep dictation status visible", preferences.ShowFloatingBar, value => preferences.ShowFloatingBar = value));
        panel.Children.Add(Toggle("Show app in Dock", "Keep ChudType visible in the macOS Dock", preferences.ShowInDock, value =>
        {
            MacSystemIntegration.SetDockVisible(value);
            preferences.ShowInDock = value;
        }));
        panel.Children.Add(Toggle("Dictation and notification sounds", "Play start, stop, and completion cues", preferences.Sounds, value => preferences.Sounds = value));
        panel.Children.Add(Toggle("Mute all audio while dictating", "Reduce interference from speakers", preferences.MuteAudioWhileDictating, value => preferences.MuteAudioWhileDictating = value));
        panel.Children.Add(Toggle("Suggestions", "Tips about setup and usage", preferences.Suggestions, value => preferences.Suggestions = value));
        panel.Children.Add(Toggle("Announcements", "New local features and capabilities", preferences.Announcements, value => preferences.Announcements = value));
        panel.Children.Add(Toggle("Milestones", "Word-count and usage milestones", preferences.Milestones, value => preferences.Milestones = value));
        panel.Children.Add(Toggle("Auto-add corrected words", "Learn explicit spelling corrections locally", preferences.AutoAddDictionary, value => preferences.AutoAddDictionary = value));
        panel.Children.Add(Toggle("Creator mode", "Show a small ChudType badge while recording", preferences.CreatorMode, value => preferences.CreatorMode = value));
        return panel;
    }

    private StackPanel StyleSettings()
    {
        var panel = Section("Style", "Context-specific capitalization and punctuation");
        string[] styles = ["Formal", "Casual", "Very casual", "Raw"];
        panel.Children.Add(Choice("Personal messages", "Messages, chats, and social apps", preferences.PersonalStyle, styles, value => preferences.PersonalStyle = value));
        panel.Children.Add(Choice("Work messages", "Team chat and collaboration tools", preferences.WorkStyle, styles, value => preferences.WorkStyle = value));
        panel.Children.Add(Choice("Email", "Longer professional correspondence", preferences.EmailStyle, styles, value => preferences.EmailStyle = value));
        panel.Children.Add(Choice("Other apps", "Default when no app rule matches", preferences.OtherStyle, styles, value => preferences.OtherStyle = value));
        panel.Children.Add(Toggle("Automatic cleanup", "Remove filler words and normalize spacing locally", preferences.AutoCleanup, value => preferences.AutoCleanup = value));
        return panel;
    }

    private StackPanel PrivacySettings()
    {
        var panel = Section("Data & Privacy", "ChudType defaults to local processing and minimal retention");
        panel.Children.Add(Toggle("Improve models", "Allow diagnostic examples to be shared only after explicit review", preferences.ImproveModels, value => preferences.ImproveModels = value));
        panel.Children.Add(Toggle("Store dictation history", "Retain transcripts on this Mac", preferences.StoreHistory, value => preferences.StoreHistory = value));
        panel.Children.Add(Toggle("Context awareness", "Use limited text around the cursor to improve names and terminology", preferences.ContextAwareness, value => preferences.ContextAwareness = value));
        panel.Children.Add(Toggle("Store settings locally", "Keep preferences, dictionary, and snippets on this Mac", preferences.StoreLocally, value => preferences.StoreLocally = value));
        panel.Children.Add(new TextBlock
        {
            Text = "Unlike cloud-first dictation services, local speech audio is not uploaded. Any future online cleanup provider must be separately enabled and clearly identified.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 18, 0, 0)
        });
        return panel;
    }

    private static StackPanel Section(string heading, string detail) => new()
    {
        Margin = new Thickness(18),
        Spacing = 12,
        Children =
        {
            new TextBlock { Text = heading, FontSize = 28, FontWeight = FontWeight.Bold },
            new TextBlock { Text = detail, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) }
        }
    };

    private Control Toggle(string title, string detail, bool value, Action<bool> update)
    {
        var checkbox = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        checkbox.IsCheckedChanged += (_, _) => { update(checkbox.IsChecked == true); Save(); };
        return Row(title, detail, checkbox);
    }

    private Control Choice(string title, string detail, string value, string[] values, Action<string> update)
    {
        var choice = new ComboBox { ItemsSource = values, SelectedItem = value, MinWidth = 150 };
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is string selected) { update(selected); Save(); }
        };
        return Row(title, detail, choice);
    }

    private static Control Row(string title, string detail, Control control)
    {
        var text = new StackPanel
        {
            Spacing = 3,
            Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = detail, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap } }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 18, Margin = new Thickness(0, 5) };
        Grid.SetColumn(text, 0); Grid.SetColumn(control, 1);
        grid.Children.Add(text); grid.Children.Add(control);
        return grid;
    }

    private void Save() => MacPreferencesStore.Save(preferences);
}
