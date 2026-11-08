using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using PresenterScript.Services;
using PresenterScript.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace PresenterScript;

public sealed partial class MainPage : Page
{
    private const string AppHost = "presenter.local";
    private const string DocumentHost = "document.local";

    private readonly DispatcherQueueTimer _renderTimer;
    private readonly DispatcherQueueTimer _scratchTimer;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly SnippetClipboard _clipboard;
    private bool _webReady;
    private string? _mappedFolder;
    private IReadOnlyList<string> _snippets = [];
    private int _clipboardSnippet = -1;

    public MainViewModel ViewModel { get; }

    public TimerViewModel Timer => ViewModel.Timer;

    public MainPage()
    {
        ViewModel = new MainViewModel(
            new FileDialogService(() => App.Window.AppWindow.Id),
            new DialogService(() => XamlRoot),
            new RecentFilesService(),
            new ScratchService());

        InitializeComponent();

        _renderTimer = DispatcherQueue.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(200);
        _renderTimer.IsRepeating = false;
        _renderTimer.Tick += (_, _) => RenderPreview();

        _scratchTimer = DispatcherQueue.CreateTimer();
        _scratchTimer.Interval = TimeSpan.FromSeconds(1);
        _scratchTimer.IsRepeating = false;
        _scratchTimer.Tick += (_, _) => ViewModel.PersistScratch();

        _clockTimer = DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromMilliseconds(250);
        _clockTimer.Tick += (_, _) => ViewModel.Timer.Update(DateTime.Now);
        _clockTimer.Start();

        _clipboard = new SnippetClipboard(DispatcherQueue);
        _clipboard.SnippetPasted += OnSnippetPasted;
        _clipboard.SnippetReplaced += (_, _) =>
        {
            _clipboardSnippet = -1;
            PostMessage(new JsonObject { ["type"] = "clipboard", ["snippet"] = -1 });
        };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ResetRequested += (_, _) => PostMessage(new JsonObject { ["type"] = "reset" });

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _clipboard.Attach(App.Window);
        await ViewModel.RestoreSessionAsync();
        try
        {
            await Preview.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            await new DialogService(() => XamlRoot).ShowErrorAsync("WebView2 is not available", ex.Message);
            return;
        }

        var core = Preview.CoreWebView2;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(
            AppHost, Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.Allow);

        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.StartsWith($"https://{AppHost}/", StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                OpenExternal(args.Uri);
            }
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            OpenExternal(args.Uri);
        };

        Preview.Source = new Uri($"https://{AppHost}/index.html");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Text):
                _renderTimer.Stop();
                _renderTimer.Start();
                _scratchTimer.Stop();
                _scratchTimer.Start();
                break;
            case nameof(MainViewModel.FilePath):
                // A newly loaded document should render (and reset the highlight) right away.
                _renderTimer.Stop();
                RenderPreview();
                break;
            case nameof(MainViewModel.IsPresenting):
                ApplyMode();
                break;
        }
    }

    private void ApplyMode()
    {
        bool presenting = ViewModel.IsPresenting;
        EditorColumn.Width = presenting ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        PreviewHost.Margin = presenting ? new Thickness(0) : new Thickness(4, 0, 8, 8);
        PreviewHost.BorderThickness = presenting ? new Thickness(0) : new Thickness(1);

        if (presenting)
        {
            // Make sure the latest edits are shown before presenting.
            if (_renderTimer.IsRunning)
            {
                _renderTimer.Stop();
                RenderPreview();
            }
            DispatcherQueue.TryEnqueue(() => Preview.Focus(FocusState.Programmatic));
        }
        else
        {
            // Defer until the editor's visibility binding has updated; collapsed elements can't take focus.
            DispatcherQueue.TryEnqueue(() => Editor.Focus(FocusState.Programmatic));
        }

        PostMessage(new JsonObject { ["type"] = "mode", ["mode"] = presenting ? "present" : "edit" });
    }

    private void RenderPreview()
    {
        if (!_webReady)
        {
            return;
        }

        var result = MarkdownRenderer.Render(ViewModel.Text);
        _snippets = result.Snippets;
        ViewModel.Timer.SetTargets(result.Targets, result.TalkLength, result.SectionCount, result.EmptySections);
        PostMessage(new JsonObject
        {
            ["type"] = "render",
            ["html"] = result.Html,
            ["mode"] = ViewModel.IsPresenting ? "present" : "edit",
            ["current"] = ViewModel.CurrentSection,
            ["baseUrl"] = MapDocumentFolder(),
            ["clipboardSnippet"] = _clipboardSnippet,
        });
    }

    /// <summary>Lets relative image paths in the Markdown resolve against the document's folder.</summary>
    private string? MapDocumentFolder()
    {
        var core = Preview.CoreWebView2;
        var folder = ViewModel.FilePath is null ? null : Path.GetDirectoryName(ViewModel.FilePath);
        if (!string.Equals(folder, _mappedFolder, StringComparison.OrdinalIgnoreCase))
        {
            if (_mappedFolder is not null)
            {
                core.ClearVirtualHostNameToFolderMapping(DocumentHost);
            }
            if (folder is not null)
            {
                core.SetVirtualHostNameToFolderMapping(DocumentHost, folder, CoreWebView2HostResourceAccessKind.Allow);
            }
            _mappedFolder = folder;
        }
        return folder is null ? null : $"https://{DocumentHost}/";
    }

    private void PostMessage(JsonObject message)
    {
        if (_webReady)
        {
            Preview.CoreWebView2.PostWebMessageAsJson(message.ToJsonString());
        }
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (JsonNode.Parse(args.WebMessageAsJson) is not JsonObject msg)
        {
            return;
        }

        switch ((string?)msg["type"])
        {
            case "ready":
                _webReady = true;
                RenderPreview();
                break;
            case "current":
                ViewModel.CurrentSection = (int?)msg["index"] ?? 0;
                ViewModel.SectionPosition = (int?)msg["position"] ?? 0;
                ViewModel.SectionCount = (int?)msg["count"] ?? 0;
                break;
            case "copy":
                PutSnippetOnClipboard((string?)msg["code"] ?? string.Empty, (int?)msg["snippet"] ?? -1);
                break;
            case "openLink":
                OpenExternal((string?)msg["href"]);
                break;
            case "key":
                HandleForwardedKey((string?)msg["key"], (bool?)msg["ctrl"] == true, (bool?)msg["shift"] == true);
                break;
        }
    }

    private void PutSnippetOnClipboard(string code, int snippetIndex)
    {
        // Always detect pastes: pasting is what advances the highlight to the next section.
        _clipboard.Put(code, snippetIndex);
        _clipboardSnippet = snippetIndex;
        PostMessage(new JsonObject { ["type"] = "clipboard", ["snippet"] = snippetIndex });
    }

    private async void OnSnippetPasted(object? sender, int snippetIndex)
    {
        if (snippetIndex != _clipboardSnippet)
        {
            return;
        }

        PostMessage(new JsonObject { ["type"] = "pasted", ["snippet"] = snippetIndex });

        int next = snippetIndex + 1;
        if (!ViewModel.AutoCopyNext || next >= _snippets.Count)
        {
            return;
        }

        // Give the target app time to finish reading the clipboard before replacing its content.
        await Task.Delay(500);
        if (_clipboardSnippet == snippetIndex && ViewModel.AutoCopyNext && next < _snippets.Count)
        {
            PutSnippetOnClipboard(_snippets[next], next);
        }
    }

    private void HandleForwardedKey(string? key, bool ctrl, bool shift)
    {
        if (ctrl)
        {
            var command = key?.ToLowerInvariant() switch
            {
                "s" when shift => ViewModel.SaveAsCommand,
                "s" => ViewModel.SaveCommand,
                "o" => ViewModel.OpenCommand,
                "n" => ViewModel.NewCommand,
                _ => (System.Windows.Input.ICommand?)null,
            };
            if (ViewModel.IsEditing && command?.CanExecute(null) == true)
            {
                command.Execute(null);
            }
            return;
        }

        switch (key)
        {
            case "F5":
                ViewModel.TogglePresentCommand.Execute(null);
                break;
            case "Escape":
                if (ViewModel.IsPresenting)
                {
                    ViewModel.IsPresenting = false;
                }
                break;
            case "F11":
                ToggleFullScreen();
                break;
            case "F1" when ViewModel.IsEditing:
                HelpWindow.ShowOrActivate();
                break;
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e) => HelpWindow.ShowOrActivate();

    private static void OpenExternal(string? uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeMailto))
        {
            _ = Launcher.LaunchUriAsync(parsed);
        }
    }

    private static void ToggleFullScreen()
    {
        var appWindow = App.Window.AppWindow;
        appWindow.SetPresenter(appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Overlapped
            : AppWindowPresenterKind.FullScreen);
    }

    private void OnTogglePresentAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.TogglePresentCommand.Execute(null);
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsPresenting)
        {
            args.Handled = true;
            ViewModel.IsPresenting = false;
        }
    }

    private void OnFullScreenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleFullScreen();
    }

    private void OnEditorPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Tab ||
            IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.Shift) || IsKeyDown(VirtualKey.Menu))
        {
            return;
        }

        e.Handled = true;
        int start = Editor.SelectionStart;
        Editor.SelectedText = "    ";
        Editor.SelectionStart = start + 4;
        Editor.SelectionLength = 0;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnRecentFlyoutOpening(object sender, object e)
    {
        RecentFlyout.Items.Clear();
        if (ViewModel.RecentFiles.Count == 0)
        {
            RecentFlyout.Items.Add(new MenuFlyoutItem { Text = "No recent files", IsEnabled = false });
            return;
        }

        foreach (var path in ViewModel.RecentFiles)
        {
            var item = new MenuFlyoutItem
            {
                Text = Path.GetFileName(path),
                Command = ViewModel.OpenRecentCommand,
                CommandParameter = path,
            };
            ToolTipService.SetToolTip(item, path);
            RecentFlyout.Items.Add(item);
        }
    }

    // Pace color scale (seconds; positive = behind): blue (well ahead) → teal → green (on time) → yellow → orange → red.
    private static readonly (double Seconds, Windows.UI.Color Color)[] PaceStops =
    [
        (-300, Windows.UI.Color.FromArgb(255, 0x2F, 0x6F, 0xE0)),
        (-150, Windows.UI.Color.FromArgb(255, 0x1A, 0x9E, 0x9E)),
        (-60, Windows.UI.Color.FromArgb(255, 0x2E, 0x9E, 0x4A)),
        (60, Windows.UI.Color.FromArgb(255, 0x2E, 0x9E, 0x4A)),
        (120, Windows.UI.Color.FromArgb(255, 0xE8, 0xB5, 0x00)),
        (210, Windows.UI.Color.FromArgb(255, 0xF0, 0x78, 0x00)),
        (300, Windows.UI.Color.FromArgb(255, 0xD9, 0x30, 0x30)),
    ];

    internal static Windows.UI.Color PaceColor(TimeSpan? delta)
    {
        double s = Math.Clamp(delta?.TotalSeconds ?? 0, PaceStops[0].Seconds, PaceStops[^1].Seconds);
        for (int i = 1; i < PaceStops.Length; i++)
        {
            var (s1, c1) = PaceStops[i];
            if (s <= s1)
            {
                var (s0, c0) = PaceStops[i - 1];
                double f = s1 == s0 ? 0 : (s - s0) / (s1 - s0);
                byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * f);
                return Windows.UI.Color.FromArgb(255, Lerp(c0.R, c1.R), Lerp(c0.G, c1.G), Lerp(c0.B, c1.B));
            }
        }
        return PaceStops[^1].Color;
    }

    public Microsoft.UI.Xaml.Media.Brush PaceBrush(TimeSpan? delta) =>
        new Microsoft.UI.Xaml.Media.SolidColorBrush(PaceColor(delta));

    public Microsoft.UI.Xaml.Media.Brush PaceTextBrush(TimeSpan? delta) => PaceTextBrushFor(delta);

    /// <summary>Dark or light text, whichever reads better on the pace color.</summary>
    internal static Microsoft.UI.Xaml.Media.Brush PaceTextBrushFor(TimeSpan? delta)
    {
        var c = PaceColor(delta);
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(luminance > 0.55 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
    }

    public Microsoft.UI.Xaml.Media.Brush TimerBrush(TimerState state) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[state switch
        {
            TimerState.Warning => "SystemFillColorCautionBrush",
            TimerState.Overtime => "SystemFillColorCriticalBrush",
            _ => "TextFillColorPrimaryBrush",
        }];
}
