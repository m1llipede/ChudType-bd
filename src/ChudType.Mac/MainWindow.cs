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
    private readonly Button dictate = new() { Content = "Start dictation (F8)", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly Button insert = new() { Content = "Insert transcript", IsEnabled = false };
    private readonly Button copy = new() { Content = "Copy" };
    private readonly TextBlock status = new() { Text = "Preparing offline speech model…", Foreground = Brushes.LightGray };
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

    public MainWindow()
    {
        shortcutModifier = new ComboBox { ItemsSource = new[] { "None", "Control", "Command", "Option", "Shift" }, SelectedItem = shortcut.Modifier };
        shortcutTrigger = new ComboBox { ItemsSource = new[] { "Middle Mouse", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Space" }, SelectedItem = shortcut.Trigger };
        shortcutMode = new ComboBox { ItemsSource = new[] { "Hold to talk", "Toggle hands-free" }, SelectedItem = shortcut.Mode };
        Title = "ChudType for Mac";
        Width = 660;
        Height = 440;
        MinWidth = 520;
        MinHeight = 360;
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
        var title = new TextBlock { Text = "ChudType", FontSize = 34, FontWeight = FontWeight.Bold };
        var subtitle = new TextBlock
        {
            Text = "Private, offline dictation. Use your shortcut anywhere to start or stop.",
            Foreground = Brushes.LightGray
        };
        var transcriptPanel = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1B1B27")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3B3550")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            MinHeight = 150,
            Child = transcript
        };
        var personalize = new Button { Content = "Personalize" };
        personalize.Click += (_, _) => new PersonalizationWindow(personalization).Show(this);
        var settings = new Button { Content = "Settings" };
        settings.Click += (_, _) => new SettingsWindow(preferences).Show(this);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(dictate, 0); Grid.SetColumn(insert, 1); Grid.SetColumn(copy, 2); Grid.SetColumn(personalize, 3); Grid.SetColumn(settings, 4);
        actions.Children.Add(dictate); actions.Children.Add(insert); actions.Children.Add(copy); actions.Children.Add(personalize); actions.Children.Add(settings);
        var permission = new Button { Content = "Open Accessibility settings", HorizontalAlignment = HorizontalAlignment.Left };
        permission.Click += (_, _) => MacInput.OpenAccessibilitySettings();
        var shortcutRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*,*"), ColumnSpacing = 10 };
        var shortcutTitle = new TextBlock { Text = "Activation", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(shortcutTitle, 0); Grid.SetColumn(shortcutMode, 1); Grid.SetColumn(shortcutModifier, 2); Grid.SetColumn(shortcutTrigger, 3);
        shortcutRow.Children.Add(shortcutTitle); shortcutRow.Children.Add(shortcutMode); shortcutRow.Children.Add(shortcutModifier); shortcutRow.Children.Add(shortcutTrigger);
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 14,
                Children = { title, subtitle, transcriptPanel, progress, status, actions, shortcutRow, permission,
                    new TextBlock { Text = "Audio and transcripts stay on this Mac. The first launch downloads a verified local model.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap } }
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
        dictate.Content = recording is null ? $"Start dictation ({shortcut})" : $"Stop dictation ({shortcut})";
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
        finalText = "";
        transcript.Text = "Listening…";
        dictate.Content = $"Stop dictation ({shortcut})";
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
            dictate.Content = $"Start dictation ({shortcut})";
            insert.IsEnabled = !string.IsNullOrWhiteSpace(finalText);
            status.Text = string.IsNullOrWhiteSpace(finalText) ? "No speech recognized" : "Transcript ready";
        }
    }

    private async Task InsertAsync()
    {
        if (string.IsNullOrWhiteSpace(finalText)) return;
        if (MacInput.TypeText(finalText)) status.Text = "Inserted";
        else
        {
            await CopyAsync();
            status.Text = "Copied · allow Accessibility, then paste into the target app";
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
