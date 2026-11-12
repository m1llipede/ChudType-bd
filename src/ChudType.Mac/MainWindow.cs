using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ChudType.Speech;
using ChudType.Speech.Sherpa;

namespace ChudType.Mac;

internal sealed class MainWindow : Window
{
    private readonly Button dictate = new()
    {
        Content = "Hold F8 to talk", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false,
        FontSize = 18, FontWeight = FontWeight.SemiBold, MinHeight = 54,
        Background = new SolidColorBrush(Color.Parse("#725CFF")), Foreground = Brushes.White,
        CornerRadius = new CornerRadius(14)
    };
    private readonly Button insert = new() { Content = "Insert transcript", IsEnabled = false };
    private readonly Button copy = new() { Content = "Copy" };
    private readonly TextBlock status = new() { Text = "Preparing local speech model…", Foreground = new SolidColorBrush(Color.Parse("#BBC1CE")), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock transcript = new() { Text = "Your words will appear here.", FontSize = 24, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    private readonly DispatcherTimer shortcutTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly MacPreferences preferences = MacPreferencesStore.Load();
    private readonly MacPersonalization personalization = MacPersonalizationStore.Load();
    private readonly ComboBox shortcutModifier;
    private readonly ComboBox shortcutTrigger;
    private readonly ComboBox shortcutMode;
    private MacShortcut shortcut = MacShortcutStore.Load();
    private SherpaRealtimeSpeechEngine? engine;
    private CancellationTokenSource? recording;
    private bool shortcutWasDown;
    private string finalText = "";
    private int targetProcessId;

    public MainWindow()
    {
        shortcutModifier = new ComboBox { ItemsSource = new[] { "None", "Control", "Command", "Option", "Shift" }, SelectedItem = shortcut.Modifier };
        shortcutTrigger = new ComboBox
        {
            ItemsSource = new[] { "Middle Mouse", "Mouse Button 4", "Mouse Button 5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Space" }
                .Concat("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(character => character.ToString()))
                .Concat(Enumerable.Range(0, 10).Select(number => number.ToString())).ToArray(),
            SelectedItem = shortcut.Trigger
        };
        shortcutMode = new ComboBox { ItemsSource = new[] { "Hold to talk", "Toggle hands-free" }, SelectedItem = shortcut.Mode };
        UpdateDictateLabel();
        Title = "ChudType for Mac";
        Width = 430;
        Height = 220;
        MinWidth = 390;
        MinHeight = 210;
        MaxWidth = 520;
        MaxHeight = 260;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#101017"));
        Content = BuildUi();
        dictate.Click += async (_, _) => await ToggleAsync();
        insert.Click += async (_, _) => await InsertAsync();
        copy.Click += async (_, _) => await CopyAsync();
        shortcutModifier.SelectionChanged += (_, _) => SaveShortcut();
        shortcutTrigger.SelectionChanged += (_, _) => SaveShortcut();
        shortcutMode.SelectionChanged += (_, _) => SaveShortcut();
        KeyDown += async (_, args) => { if (args.Key == Key.F8) { args.Handled = true; await ToggleAsync(); } };
        shortcutTimer.Tick += async (_, _) =>
        {
            bool down = MacInput.ShortcutDown(shortcut);
            if (down && !shortcutWasDown) await ToggleAsync();
            else if (!down && shortcutWasDown && shortcut.Mode == "Hold to talk" && recording is not null) await ToggleAsync();
            shortcutWasDown = down;
        };
        shortcutTimer.Start();
        Opened += async (_, _) => await PrepareModelAsync();
        Closing += (_, _) =>
        {
            recording?.Cancel();
            shortcutTimer.Stop();
            engine?.Dispose();
            http.Dispose();
        };
    }

    private Control BuildUi()
    {
        var title = new TextBlock
        {
            Text = "ChudType", FontSize = 20, FontWeight = FontWeight.Bold,
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
        };
        var privacy = new TextBlock
        {
            Text = "LOCAL · AUDIO STAYS ON THIS MAC", FontSize = 10,
            FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#8E97A8")),
            VerticalAlignment = VerticalAlignment.Center
        };
        var personalize = new Button
        {
            Content = "Aa", FontSize = 13, FontWeight = FontWeight.SemiBold, MinWidth = 38, Padding = new Thickness(8, 4),
            Background = new SolidColorBrush(Color.Parse("#242532")), Foreground = Brushes.White
        };
        personalize.Click += (_, _) => new PersonalizationWindow(personalization).Show(this);
        ToolTip.SetTip(personalize, "Dictionary and snippets");
        var settings = new Button
        {
            Content = "⚙", FontSize = 15, MinWidth = 38, Padding = new Thickness(8, 4),
            Background = new SolidColorBrush(Color.Parse("#242532")), Foreground = Brushes.White
        };
        settings.Click += (_, _) => new SettingsWindow(preferences).Show(this);
        ToolTip.SetTip(settings, "Settings");
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { personalize, settings } };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(title, 0); Grid.SetColumn(privacy, 1); Grid.SetColumn(headerActions, 2);
        header.Children.Add(title); header.Children.Add(privacy); header.Children.Add(headerActions);

        shortcutMode.MinWidth = 130;
        shortcutModifier.MinWidth = 95;
        shortcutTrigger.MinWidth = 80;
        var shortcutRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(shortcutMode, 0); Grid.SetColumn(shortcutModifier, 1); Grid.SetColumn(shortcutTrigger, 2);
        shortcutRow.Children.Add(shortcutMode); shortcutRow.Children.Add(shortcutModifier); shortcutRow.Children.Add(shortcutTrigger);

        return new Border
        {
            Padding = new Thickness(18, 15),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { header, dictate, progress, status, shortcutRow }
            }
        };
    }

    private async Task PrepareModelAsync()
    {
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChudType", "Models");
            var installer = new SherpaModelInstaller(http, root);
            var package = SherpaModelPackage.InstantEnglish;
            var model = installer.TryOpen(package);
            if (model is null)
            {
                progress.IsVisible = true;
                status.Text = "Downloading verified offline speech model…";
                var report = new Progress<double>(value => progress.Value = value);
                model = await installer.InstallAsync(package, report);
            }
            engine = new SherpaRealtimeSpeechEngine(model);
            progress.IsVisible = false;
            dictate.IsEnabled = true;
            status.Text = MacInput.AccessibilityGranted
                ? $"Ready · {shortcut} starts dictation"
                : "Ready · allow Accessibility to insert into other apps";
        }
        catch (Exception error)
        {
            progress.IsVisible = false;
            dictate.IsEnabled = false;
            status.Text = "Speech model setup failed: " + error.Message;
        }
    }

    private void SaveShortcut()
    {
        if (shortcutModifier.SelectedItem is not string modifier || shortcutTrigger.SelectedItem is not string trigger ||
            shortcutMode.SelectedItem is not string mode) return;
        shortcut = new MacShortcut(modifier, trigger, mode);
        MacShortcutStore.Save(shortcut);
        UpdateDictateLabel();
        status.Text = MacInput.AccessibilityGranted
            ? $"Shortcut saved · {shortcut} starts dictation"
            : $"Shortcut saved · allow Accessibility for system-wide activation";
    }

    private async Task ToggleAsync()
    {
        if (engine is null) return;
        if (recording is not null)
        {
            recording.Cancel();
            return;
        }
        recording = new CancellationTokenSource();
        var cancellation = recording;
        int frontmost = MacSystemIntegration.FrontmostProcessId;
        targetProcessId = frontmost == Environment.ProcessId ? 0 : frontmost;
        finalText = "";
        transcript.Text = "Listening…";
        dictate.Content = "Listening… release to insert";
        status.Text = "Listening locally";
        insert.IsEnabled = false;
        try
        {
            await using var microphone = new MacMicrophone();
            var coordinator = new StreamingDictation(engine);
            // End capture normally on Stop so the recognizer can flush buffered
            // audio and produce a final transcript.
            await foreach (var update in coordinator.RunAsync(microphone.CaptureAsync(cancellation.Token)))
            {
                if (!string.IsNullOrWhiteSpace(update.Text))
                {
                    finalText = update.IsFinal ? personalization.Apply(update.Text) : update.Text;
                    transcript.Text = finalText;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            status.Text = "Dictation failed: " + error.Message;
        }
        finally
        {
            recording.Dispose();
            recording = null;
            UpdateDictateLabel();
            insert.IsEnabled = !string.IsNullOrWhiteSpace(finalText);
            if (string.IsNullOrWhiteSpace(finalText)) status.Text = "No speech recognized";
            else if (preferences.AutoInsert && targetProcessId > 0) await InsertIntoTargetAsync();
            else status.Text = "Transcript ready";
        }
    }

    private void UpdateDictateLabel() => dictate.Content = recording is not null
        ? "Listening… release to insert"
        : shortcut.Mode == "Hold to talk" ? $"Hold {shortcut} to talk" : $"Press {shortcut} to start";

    private async Task InsertAsync()
    {
        if (string.IsNullOrWhiteSpace(finalText)) return;
        if (targetProcessId > 0) await InsertIntoTargetAsync();
        else if (MacInput.TypeText(finalText)) status.Text = "Inserted";
        else
        {
            await CopyAsync();
            status.Text = "Copied · allow Accessibility, then paste into the target app";
        }
    }

    private async Task InsertIntoTargetAsync()
    {
        if (!MacInput.AccessibilityGranted || !MacSystemIntegration.Activate(targetProcessId))
        {
            await CopyAsync();
            status.Text = "Copied · allow Accessibility to insert automatically";
            return;
        }
        await Task.Delay(120);
        if (MacInput.TypeText(finalText)) status.Text = "Inserted into your previous app";
        else
        {
            await CopyAsync();
            status.Text = "Copied · paste into your previous app";
        }
    }

    private async Task CopyAsync()
    {
        if (string.IsNullOrWhiteSpace(finalText)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(finalText);
        status.Text = "Copied to clipboard";
    }
}
