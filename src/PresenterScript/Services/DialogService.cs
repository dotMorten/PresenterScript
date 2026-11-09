using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresenterScript.ViewModels;

namespace PresenterScript.Services;

public enum SaveChoice
{
    Save,
    DontSave,
    Cancel,
}

public interface IDialogService
{
    bool IsOpen { get; }
    Task<SaveChoice> ConfirmSaveChangesAsync(string fileName);
    Task<bool> ConfirmAsync(string title, string message, string confirmText);
    Task ShowErrorAsync(string title, string message);
    Task ShowPresentationSettingsAsync(PresentationSettingsViewModel settings);
}

public sealed class DialogService(Func<XamlRoot> xamlRoot) : IDialogService
{
    private bool _isOpen;

    public bool IsOpen => _isOpen;

    public async Task ShowPresentationSettingsAsync(PresentationSettingsViewModel settings)
    {
        if (_isOpen)
        {
            return;
        }

        var dialog = new PresentationSettingsDialog(settings) { XamlRoot = xamlRoot() };
        _isOpen = true;
        try
        {
            dialog.StartMonitoring();
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                settings.SaveFrom(dialog.Draft);
            }
        }
        finally
        {
            _isOpen = false;
            dialog.StopMonitoring();
        }
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        if (_isOpen)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(),
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        _isOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _isOpen = false;
        }
    }

    public async Task<SaveChoice> ConfirmSaveChangesAsync(string fileName)
    {
        if (_isOpen)
        {
            return SaveChoice.Cancel;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(),
            Title = "Unsaved changes",
            Content = $"Do you want to save changes to {fileName}?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        _isOpen = true;
        try
        {
            return await dialog.ShowAsync() switch
            {
                ContentDialogResult.Primary => SaveChoice.Save,
                ContentDialogResult.Secondary => SaveChoice.DontSave,
                _ => SaveChoice.Cancel,
            };
        }
        finally
        {
            _isOpen = false;
        }
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        if (_isOpen)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(),
            Title = title,
            Content = message,
            CloseButtonText = "OK",
        };

        _isOpen = true;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            _isOpen = false;
        }
    }
}
