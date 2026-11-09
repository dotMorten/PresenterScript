using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PresenterScript.Services;

namespace PresenterScript.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly IFileDialogService _fileDialogs;
    private readonly IDialogService _dialogs;
    private readonly RecentFilesService _recentFiles;
    private readonly ScratchService _scratch;
    private string _savedText = string.Empty;
    private string _lineEnding = "\r\n";

    /// <summary>Content of a new document (and of the first launch).</summary>
    public const string StarterText = """
        # My Talk

        ## Setup
        - [ ] Set resolution to 1080p
        - [ ] Shut down apps I don't need

        ----
        @0:00

        ## Introduction
        Welcome, say name, what we'll cover etc.

        ---
        @5:00

        ### Switch to commandline

        ```
        dotnet new console
        ```
        ```
        dotnet build
        ```
        ---
        @10:00
        ### Run the app
        Explain what happens while it is launching.
        ```
        dotnet run
        ```
        Walk through app

        ---

        @20:00
        Back to slides


        @30:00
        """;

    public MainViewModel(IFileDialogService fileDialogs, IDialogService dialogs, RecentFilesService recentFiles, ScratchService scratch)
    {
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _recentFiles = recentFiles;
        _scratch = scratch;
        RefreshRecentFiles(_recentFiles.Load());
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(DisplayTitle))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(DisplayTitle))]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(ModeButtonText), nameof(ModeButtonGlyph))]
    public partial bool IsPresenting { get; set; }

    [ObservableProperty]
    public partial int CurrentSection { get; set; }

    /// <summary>1-based position of the current section among non-empty sections.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionStatus))]
    public partial int SectionPosition { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionStatus))]
    public partial int SectionCount { get; set; }

    public ObservableCollection<string> RecentFiles { get; } = [];

    public TimerViewModel Timer { get; } = new();

    public PresentationSettingsViewModel PresentationSettings { get; } = PresentationSettingsViewModel.Load();

    partial void OnIsPresentingChanged(bool value)
    {
        if (value)
        {
            Timer.EnsureStarted();
        }
    }

    partial void OnCurrentSectionChanged(int value) => Timer.SetCurrentSection(value);

    /// <summary>When a snippet gets pasted elsewhere, put the next snippet on the clipboard.</summary>
    [ObservableProperty]
    public partial bool AutoCopyNext { get; set; } =
        Windows.Storage.ApplicationData.Current.LocalSettings.Values[nameof(AutoCopyNext)] as bool? ?? true;

    partial void OnAutoCopyNextChanged(bool value) =>
        Windows.Storage.ApplicationData.Current.LocalSettings.Values[nameof(AutoCopyNext)] = value;

    // TextBox normalizes line endings to '\r', so compare and save using '\n'-normalized text.
    public bool IsDirty => !string.Equals(NormalizeNewLines(Text), _savedText, StringComparison.Ordinal);

    public bool IsEditing => !IsPresenting;

    public string FileName => FilePath is null ? "Untitled.md" : Path.GetFileName(FilePath);

    public string DisplayTitle => $"{FileName}{(IsDirty ? " •" : string.Empty)} – PresenterScript";

    public string ModeButtonText => IsPresenting ? "Edit" : "Present";

    public string ModeButtonGlyph => IsPresenting ? "\uE70F" : "\uE7F4";

    public string SectionStatus => SectionCount == 0 ? string.Empty : $"Section {SectionPosition} / {SectionCount}";

    /// <summary>Raised when the presenter highlight should go back to the first section.</summary>
    public event EventHandler? ResetRequested;

    [RelayCommand]
    private void TogglePresent() => IsPresenting = !IsPresenting;

    [RelayCommand]
    private void ResetToStart()
    {
        CurrentSection = 0;
        ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await LeaveDocumentAsync())
        {
            return;
        }
        _scratch.Clear(null);
        LoadDocument(null, StarterText);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        var path = await _fileDialogs.PickOpenFileAsync();
        if (path is not null && await LeaveDocumentAsync())
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string path)
    {
        if (await LeaveDocumentAsync())
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task RevertAsync()
    {
        if (await _dialogs.ConfirmAsync("Revert changes", $"Discard all unsaved changes to {FileName}?", "Discard changes"))
        {
            Text = _savedText;
            PersistScratch();
        }
    }

    /// <summary>
    /// Restores the document that was open when the app last closed, including its unsaved edits. Falls back to the
    /// most recent file, then to a new document with the starter content.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        var last = _scratch.LastDocument;
        if (last == string.Empty)
        {
            LoadDocument(null, StarterText);
            return;
        }

        if (last is not null && await TryLoadAsync(last))
        {
            return;
        }
        while (RecentFiles.Count > 0)
        {
            if (await TryLoadAsync(RecentFiles[0]))
            {
                return;
            }
        }
        LoadDocument(null, StarterText);
    }

    /// <summary>Loads a document without error UI; unreadable files are dropped from the recent list.</summary>
    private async Task<bool> TryLoadAsync(string path)
    {
        try
        {
            LoadDocument(path, await File.ReadAllTextAsync(path));
            return true;
        }
        catch (Exception)
        {
            RefreshRecentFiles(_recentFiles.Remove(path));
            return false;
        }
    }

    /// <summary>Stores unsaved edits in the scratch space (or clears it once there are none).</summary>
    public void PersistScratch()
    {
        if (IsDirty)
        {
            _scratch.Save(FilePath, NormalizeNewLines(Text));
        }
        else
        {
            _scratch.Clear(FilePath);
        }
    }

    [RelayCommand]
    private Task<bool> SaveAsync() => FilePath is null ? SaveAsAsync() : WriteFileAsync(FilePath);

    [RelayCommand]
    private async Task<bool> SaveAsAsync()
    {
        var path = await _fileDialogs.PickSaveFileAsync(FileName);
        return path is not null && await WriteFileAsync(path);
    }

    /// <summary>
    /// Called before switching to another document. With unsaved changes the user decides whether to save or discard
    /// them; returns false if the switch should be cancelled.
    /// </summary>
    private async Task<bool> LeaveDocumentAsync()
    {
        if (!IsDirty)
        {
            PersistScratch();
            return true;
        }

        switch (await _dialogs.ConfirmSaveChangesAsync(FileName))
        {
            case SaveChoice.Save:
                return await SaveAsync();
            case SaveChoice.DontSave:
                Text = _savedText;
                PersistScratch();
                return true;
            default:
                return false;
        }
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path);
            LoadDocument(path, text);
            RefreshRecentFiles(_recentFiles.Add(path));
        }
        catch (Exception ex)
        {
            RefreshRecentFiles(_recentFiles.Remove(path));
            await _dialogs.ShowErrorAsync("Couldn't open file", $"{path}\n\n{ex.Message}");
        }
    }

    private async Task<bool> WriteFileAsync(string path)
    {
        try
        {
            var text = NormalizeNewLines(Text);
            await File.WriteAllTextAsync(path, text.Replace("\n", _lineEnding), Utf8NoBom);
            _scratch.Clear(FilePath);
            _savedText = text;
            FilePath = path;
            _scratch.Clear(path);
            _scratch.LastDocument = path;
            NotifyDirtyChanged();
            RefreshRecentFiles(_recentFiles.Add(path));
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Couldn't save file", $"{path}\n\n{ex.Message}");
            return false;
        }
    }

    /// <summary>Shows a document; <paramref name="text"/> is its saved content, and any unsaved edits kept in the scratch space are restored on top.</summary>
    private void LoadDocument(string? path, string text)
    {
        _lineEnding = text.Contains("\r\n") || !text.Contains('\n') ? "\r\n" : "\n";
        _savedText = NormalizeNewLines(text);
        FilePath = path;
        CurrentSection = 0;
        Text = _scratch.Load(path) ?? _savedText;
        _scratch.LastDocument = path ?? string.Empty;
        NotifyDirtyChanged();
    }

    private void NotifyDirtyChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DisplayTitle));
        RevertCommand.NotifyCanExecuteChanged();
    }

    public static string NormalizeNewLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private void RefreshRecentFiles(IReadOnlyList<string> paths)
    {
        RecentFiles.Clear();
        foreach (var p in paths)
        {
            RecentFiles.Add(p);
        }
    }
}
