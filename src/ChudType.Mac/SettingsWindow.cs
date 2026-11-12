using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChudType.Mac;

internal sealed class SettingsWindow : Window
{
    private readonly MacPreferences preferences;
    private readonly Action<MacShortcut> shortcutChanged;
    private MacShortcut shortcut = MacShortcutStore.Load();

    public SettingsWindow(MacPreferences preferences, Action<MacShortcut> shortcutChanged)
    {
        this.preferences = preferences;
        this.shortcutChanged = shortcutChanged;
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
                Page("Data & Privacy", PrivacySettings())
            }
        };
    }

    private static TabItem Page(string name, Control content) => new() { Header = name, Content = new ScrollViewer { Content = content } };

    private StackPanel General()
    {
        var panel = Section("General", "Core dictation preferences");
        panel.Children.Add(Choice("Theme", "Follow macOS or choose a light or dark appearance; applies when ChudType reopens", preferences.Theme,
            new[] { "System", "Dark", "Light" }, value =>
            {
                preferences.Theme = value;
            }));
        panel.Children.Add(new TextBlock { Text = "Dictation shortcut", FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(Choice("Activation behavior", "Hold a shortcut while speaking, or press once to start and again to stop", shortcut.Mode,
            new[] { "Hold to talk", "Toggle hands-free" }, value => SaveShortcut(shortcut with { Mode = value })));
        panel.Children.Add(Choice("Modifier key", "Optional key held together with the trigger", shortcut.Modifier,
            new[] { "None", "Control", "Command", "Option", "Shift" }, value => SaveShortcut(shortcut with { Modifier = value })));
        panel.Children.Add(Choice("Trigger key or mouse button", "The key or mouse button that starts dictation", shortcut.Trigger,
            new[] { "Middle Mouse", "Mouse Button 4", "Mouse Button 5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Space" },
            value => SaveShortcut(shortcut with { Trigger = value })));
        return panel;
    }

    private StackPanel SystemSettings()
    {
        var panel = Section("System", "Startup, insertion, and permissions");
        panel.Children.Add(Toggle("Launch app at login", "Start ChudType after you sign in", preferences.LaunchAtLogin, value =>
        {
            MacSystemIntegration.SetLaunchAtLogin(value);
            preferences.LaunchAtLogin = value;
        }));
        panel.Children.Add(Toggle("Insert when dictation finishes", "Return the transcript to the app where dictation started", preferences.AutoInsert, value => preferences.AutoInsert = value));
        panel.Children.Add(Toggle("Show app in Dock", "Keep ChudType visible in the macOS Dock", preferences.ShowInDock, value =>
        {
            MacSystemIntegration.SetDockVisible(value);
            preferences.ShowInDock = value;
        }));
        var accessibility = new Button { Content = "Open Accessibility permission" };
        accessibility.Click += (_, _) => MacInput.OpenAccessibilitySettings();
        panel.Children.Add(Row("Text insertion", "Accessibility lets ChudType type into the app where dictation started.", accessibility));
        return panel;
    }

    private StackPanel PrivacySettings()
    {
        var panel = Section("Data & Privacy", "Local processing with minimal retention");
        panel.Children.Add(new TextBlock
        {
            Text = "Microphone audio is transcribed locally and is not uploaded. ChudType keeps only your preferences, shortcut, dictionary, and snippets on this Mac. The compact Mac app does not store transcript history or recordings.",
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

    private void SaveShortcut(MacShortcut value)
    {
        if (!value.IsSafe) return;
        shortcut = value;
        MacShortcutStore.Save(value);
        shortcutChanged(value);
    }
}
