# <img src="docs/logo.svg" width="40" alt="" align="top"> PresenterScript

A simple WinUI 3 app for presenting live-coding talks from a Markdown script, shown on a secondary monitor the audience doesn't see.

https://github.com/user-attachments/assets/57b3cd97-78f5-478b-a329-4e1ebb44ef7c

- **Edit mode**: a Markdown editor with a side-by-side live preview.
- **Presenter mode** (F5): only the rendered script, with the current section highlighted and centered.
- **Click-to-copy snippets**: click anywhere on a fenced code block to copy it.
  - When you paste it in your editor, the highlight moves to the next section, and the next snippet can be put on the clipboard automatically.
  - If another app replaces the clipboard, the snippet's "On clipboard" badge clears.
- **Timer and pace**: count down or up. Add `@mm:ss` lines to set section targets; a final `@` line sets the talk length. A colored pill shows whether you're ahead, on pace or behind.
- **Setup checklist**: a task list at the top of the script.
- **No lost edits**: unsaved changes are kept in a scratch space and restored on next launch.
- Recent files and remembered window placement.

Press **F1** in the app for the full syntax reference.

## Example script

````markdown
# My Talk

## Setup
- [ ] Set resolution to 1080p

---
@0:00
## Introduction
Welcome, say name, what we'll cover.

---
@5:00
### Switch to command line
```
dotnet new console
```

@30:00
````

For a complete example, see [docs/sample-talk.md](docs/sample-talk.md). It's the script used in the showreel.

## Build and run

Requires the .NET 10 SDK, Windows App SDK tooling and the [WinApp CLI](https://github.com/microsoft/WinAppCli).

```powershell
cd src\PresenterScript
winapp run .
```

You can also open `PresenterScript.slnx` in Visual Studio and run the packaged project.

## License

[MIT](LICENSE)
