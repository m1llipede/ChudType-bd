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
    private readonly TextBlock dictateLabel = new()
    {
        Text = "Hold F8 to talk", FontSize = 18, FontWeight = FontWeight.SemiBold,
        Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Border dictate = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false, MinHeight = 54,
        Background = new SolidColorBrush(Color.Parse("#725CFF")), CornerRadius = new CornerRadius(14),
        Padding = new Thickness(14, 8), Cursor = new Cursor(StandardCursorType.Hand)
    };
    private readonly Button insert = new() { Content = "Insert transcript", IsEnabled = false };
    private readonly Button copy = new() { Content = "Copy" };
    private readonly TextBlock status = new() { Text = "Preparing local speech model…", Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock transcript = new() { Text = "Your words will appear here.", FontSize = 24, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    private readonly DispatcherTimer shortcutTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly MacPreferences preferences = MacPreferencesStore.Load();
    private readonly MacPersonalization personalization = MacPersonalizationStore.Load();
    private MacShortcut shortcut = MacShortcutStore.Load();
    private SherpaRealtimeSpeechEngine? engine;
    private CancellationTokenSource? recording;
    private bool shortcutWasDown;
    private string finalText = "";
    private int targetProcessId;

    public MainWindow()
    {
        dictate.Child = dictateLabel;
        UpdateDictateLabel();
        Title = "ChudType for Mac";
        Width = 430;
        Height = 175;
        MinWidth = 390;
        MinHeight = 165;
        MaxWidth = 520;
        MaxHeight = 210;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#101017"));
        Content = BuildUi();
        dictate.PointerPressed += async (_, args) =>
        {
            if (!dictate.IsEnabled || args.GetCurrentPoint(dictate).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            args.Handled = true;
            await ToggleAsync();
        };
        insert.Click += async (_, _) => await InsertAsync();
        copy.Click += async (_, _) => await CopyAsync();
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
            FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#C7CDDA")),
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
        settings.Click += (_, _) => new SettingsWindow(preferences, value =>
        {
            shortcut = value;
            UpdateDictateLabel();
            status.Text = $"Ready · {ShortcutInstruction()}";
        }).Show(this);
        ToolTip.SetTip(settings, "Settings");
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { personalize, settings } };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(title, 0); Grid.SetColumn(privacy, 1); Grid.SetColumn(headerActions, 2);
        header.Children.Add(title); header.Children.Add(privacy); header.Children.Add(headerActions);

        return new Border
        {
            Padding = new Thickness(18, 15),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { header, dictate, progress, status }
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
                ? $"Ready · {ShortcutInstruction()}"
                : $"Ready · {ShortcutInstruction()} · clipboard fallback until Accessibility is enabled";
        }
        catch (Exception error)
        {
            progress.IsVisible = false;
            dictate.IsEnabled = false;
            status.Text = "Speech model setup failed: " + error.Message;
        }
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
        SetRecordingAppearance(true);
        status.Text = "Recording locally…";
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
            SetRecordingAppearance(false);
            insert.IsEnabled = !string.IsNullOrWhiteSpace(finalText);
            if (string.IsNullOrWhiteSpace(finalText)) status.Text = "No speech recognized";
            else if (preferences.AutoInsert && targetProcessId > 0) await InsertIntoTargetAsync();
            else status.Text = "Transcript ready";
        }
    }

    private string ShortcutInstruction() => shortcut.Mode == "Hold to talk"
        ? $"hold {shortcut}, speak, then release"
        : $"press {shortcut} to start or stop";

    private void UpdateDictateLabel() => dictateLabel.Text = shortcut.Mode == "Hold to talk"
        ? $"Hold {shortcut} to talk"
        : $"Press {shortcut} to start";

    private void SetRecordingAppearance(bool active)
    {
        if (active)
        {
            dictateLabel.Text = shortcut.Mode == "Hold to talk" ? $"Recording — release {shortcut}" : $"Recording — press {shortcut} to stop";
            dictate.Background = new SolidColorBrush(Color.Parse("#FF3F70"));
            status.Text = "Recording locally…";
        }
        else
        {
            dictate.Background = new SolidColorBrush(Color.Parse("#725CFF"));
            UpdateDictateLabel();
        }
    }

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
