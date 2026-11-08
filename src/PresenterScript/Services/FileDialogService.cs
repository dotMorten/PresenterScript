using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace PresenterScript.Services;

public interface IFileDialogService
{
    Task<string?> PickOpenFileAsync();
    Task<string?> PickSaveFileAsync(string suggestedName);
}

public sealed class FileDialogService(Func<WindowId> windowId) : IFileDialogService
{
    public async Task<string?> PickOpenFileAsync()
    {
        var picker = new FileOpenPicker(windowId())
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SettingsIdentifier = "PresenterScriptFiles",
        };
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".markdown");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add("*");

        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName)
    {
        var picker = new FileSavePicker(windowId())
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SettingsIdentifier = "PresenterScriptFiles",
            SuggestedFileName = suggestedName,
            DefaultFileExtension = ".md",
        };
        picker.FileTypeChoices.Add("Markdown", [".md", ".markdown"]);
        picker.FileTypeChoices.Add("Text", [".txt"]);

        var result = await picker.PickSaveFileAsync();
        return result?.Path;
    }
}
