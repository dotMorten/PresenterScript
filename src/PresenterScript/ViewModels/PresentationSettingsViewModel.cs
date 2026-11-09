using CommunityToolkit.Mvvm.ComponentModel;
using Windows.Storage;

namespace PresenterScript.ViewModels;

public partial class PresentationSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool KeepAwake { get; set; } = true;

    [ObservableProperty]
    public partial bool AutoFullScreen { get; set; }

    public static PresentationSettingsViewModel Load()
    {
        var saved = ApplicationData.Current.LocalSettings.Values["PresentationSettings"] as ApplicationDataCompositeValue;
        return new()
        {
            KeepAwake = saved?[nameof(KeepAwake)] as bool? ?? true,
            AutoFullScreen = saved?[nameof(AutoFullScreen)] as bool? ?? false,
        };
    }

    public void SaveFrom(PresentationSettingsViewModel draft)
    {
        ApplicationData.Current.LocalSettings.Values["PresentationSettings"] = new ApplicationDataCompositeValue
        {
            [nameof(KeepAwake)] = draft.KeepAwake,
            [nameof(AutoFullScreen)] = draft.AutoFullScreen,
        };
        KeepAwake = draft.KeepAwake;
        AutoFullScreen = draft.AutoFullScreen;
    }
}
