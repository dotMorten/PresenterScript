# AGENTS.md

Guidance for AI coding agents working on **PresenterScript**. Read this first to catch up on purpose, architecture and workflow.

## Purpose

PresenterScript is a small WinUI 3 desktop app for presenting **live-coding talks** from a Markdown script. The window sits on a secondary monitor that the audience doesn't see.

- **Edit mode**: a Markdown `TextBox` on the left and a live WebView2 preview on the right.
- **Presenter mode** (F5; Esc returns to editing): only the rendered script, with large text.
- The script is split into **sections**. The current section gets a subtle background, larger text, and is scrolled to the vertical center.
- Fenced code blocks are **snippets**. Clicking anywhere on one copies it and makes its own section current. When the app detects the snippet was **pasted** in another app, the highlight advances to the next section. With "Auto-copy next" on, the next snippet is queued on the clipboard. If another app replaces the clipboard, the "On clipboard" badge clears.
- A **timer** can be off, count down to an end time, or count up. `@mm:ss` lines set section time targets. If the last line of the document is an `@` tag, it is the talk length. A **pace pill** shows ahead (blue), on pace (green) or behind (yellow → red). Untagged sections get interpolated, hidden targets.
- A task list at the top of the document becomes a **setup checklist**, rendered outside the sections and collapsed in presenter mode.
- **Unsaved edits are never lost**. They are kept in a per-document scratch file and restored on next launch, so closing doesn't prompt. New/Open prompt when the document is dirty. Revert discards edits.
- Recent files, window placement (WinUIEx `PersistenceId`), and an F1 Help window that documents all special syntax.

The user-facing syntax reference is `src\PresenterScript\HelpWindow.xaml`. **Keep it in sync** whenever the Markdown dialect or behavior changes.

## Tech stack

- C# / .NET 10 (`net10.0-windows10.0.26100.0`), SDK pinned in `global.json`.
- WinUI 3 / Windows App SDK 2.x. The app is **packaged** (MSIX, `Package.appxmanifest`) and built for x86/x64/ARM64. Never use AnyCPU.
- Packages:
  - **Markdig**: Markdown → HTML.
  - **CommunityToolkit.Mvvm**: `[ObservableProperty]`, `[RelayCommand]`.
  - **WinUIEx**: `WindowManager.PersistenceId` and `WindowMessageMonitor`.
  - **Microsoft.Windows.SDK.BuildTools.WinApp**: `winapp run` / `dotnet run` support.
  - **WinUIAnalyzer**.
- Rendering uses **WebView2** with local assets in `src\PresenterScript\Web` (no network, no CDN).

## Layout

```
AGENTS.md, README.md, LICENSE   Repo docs (MIT)
PresenterScript.slnx            Solution (ARM64/x64/x86 only)
global.json                     SDK pin
docs/sample-talk.md             Complete example script (used in the README showreel; keep it working)
src/PresenterScript/
  App.xaml(.cs)                 Startup; unhandled exceptions are appended to LocalState\crash.log
  MainWindow.xaml(.cs)          Hosts MainPage; PersistenceId "MainWindow"; OnClosing -> ViewModel.PersistScratch()
  MainPage.xaml(.cs)            CommandBar, timer bar, editor + WebView2 preview, host<->JS messaging,
                                debounced render (Text change) and scratch save (1 s), keyboard accelerators
  HelpWindow.xaml(.cs)          Syntax/help reference (F1). Update it when behavior changes
  ViewModels/
    MainViewModel.cs            Document state (Text, FilePath, IsDirty, IsPresenting, CurrentSection),
                                New/Open/Save/SaveAs/Revert, StarterText, session restore, scratch integration
    TimerViewModel.cs           Timer modes, section targets, interpolation, continuous pace model, pill colors
  Services/
    MarkdownRenderer.cs         Markdig -> HTML with sections, snippets, @targets, setup checklist (pure, static)
    SnippetClipboard.cs         Delay-rendered clipboard (paste detection), WM_CLIPBOARDUPDATE replace detection
    ScratchService.cs           LocalState\Scratch\<hash>.md per document (untitled.md), LastDocument setting
    RecentFilesService.cs       MRU in LocalSettings
    FileDialogService.cs        Open/save pickers
    DialogService.cs            Save/Don't save/Cancel, confirm and error ContentDialogs
  Web/
    index.html, presenter.css, presenter.js   Static shell; content swapped via PostWebMessage
```

## Key mechanisms

- **Host ↔ WebView messaging**: JSON messages with a `type` field.
  - Host → JS: `render` (html, mode, current, baseUrl, clipboardSnippet), `mode`, `setCurrent`, `reset`, `step`, `clipboard` (queued snippet index, -1 = none), `pasted`.
  - JS → host: `ready`, `current`, `copy`, `openLink`, `key`.
  - Assets are served from `https://app.local/` (virtual host mapping). The document's folder is mapped to a second host so relative image paths work. Navigation to other URLs opens the system browser.
- **Rendering**: `MarkdownRenderer.Render` returns a `RenderResult`: Html, Snippets, Targets, TalkLength, SectionCount, EmptySections.
  - Prose is wrapped in `<section class="sect" data-idx>`. Code blocks become `.snippet` elements carrying the following section's index.
  - Sections split at code blocks and at `---`.
  - Target tags match `^@\d{1,3}(:\d\d){0,2}$`. A tag on the last non-blank line is the talk length.
- **Paste detection**: `SnippetClipboard.Put` uses `DataPackage.SetDataProvider` (delayed rendering) with history and roaming disabled. The provider callback firing means another app pasted the snippet, which raises `SnippetPasted`. Reading the clipboard (e.g. PowerShell `Get-Clipboard`) triggers it; `Set-Clipboard` only replaces it.
- **Replace detection**: `AddClipboardFormatListener`, then on `WM_CLIPBOARDUPDATE` check whether the owner is another process. If so, raise `SnippetReplaced`.
- **Pace**: re-evaluated on every clock tick (no arrival tracking).
  - On pace while the current section's target ≤ elapsed ≤ the next target (or talk length).
  - Ahead = target − elapsed, before that window.
  - Behind = elapsed − next target, after it.
- **Scratch/session**:
  - Edits are saved atomically to scratch (`.tmp` then move).
  - On launch: LastDocument, then the most recent file, then the starter text.
  - Saving clears the scratch. The user's file on disk is only written by Save/Save as.

## Build, run, verify

```powershell
# Build + deploy + launch (packaged, with identity). Closes any running instance first.
cd src\PresenterScript; winapp run . --detach

# Compile check only, without locking the running app's output folder
dotnet build src\PresenterScript\PresenterScript.csproj -p:Platform=ARM64 -p:BaseOutputPath="$env:TEMP\ps-bin\"
```

- Use the platform that matches the machine (`ARM64` or `x64`).
- There is no unit test project. Verify behavior by running the app and driving it with `winapp ui` (UI Automation: inspect, search, click, set-value, get-value, screenshot).
- Before restarting the app, check the window title for `•` (unsaved changes). Scratch keeps them, but close gracefully when possible (`winapp ui invoke Close`).
- WebView content coordinates from UIA are unreliable. Take `winapp ui screenshot Preview` (1:1 physical pixels) and click at `--at x,y` using the Preview element's rect plus the offset in the screenshot.
- Package LocalState is `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState` and contains `Scratch\` and `crash.log`.

## Conventions

- MVVM with CommunityToolkit.Mvvm source generators. Keep services UI-agnostic where practical.
- Comment only where it clarifies non-obvious behavior.
- The WinUI `TextBox` uses bare `\r` newlines. Normalize before parsing.
- Don't add network dependencies to the web layer.
- When adding syntax or behavior, update `HelpWindow.xaml` and this file.
- The README showreel video is a GitHub user-attachment (not stored in the repo). Don't commit large media files.

## Skills/tools that help

- **winui-dev-workflow**: building and running WinUI 3 apps with the WinApp CLI.
- **winapp-ui-automation**: driving and inspecting the running app (`winapp ui ...`).
- **winui-design / winapp-find-api**: XAML controls and API lookups.
- **winapp-package / winui-packaging / winapp-signing**: MSIX packaging and release.
- **winapp-troubleshoot**: packaging and identity errors.
