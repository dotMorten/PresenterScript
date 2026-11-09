using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresenterScript.ViewModels;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.UI.Notifications;

namespace PresenterScript;

public sealed partial class PresentationSettingsDialog : ContentDialog
{
    private ToastNotificationManagerForUser? _notifications;
    private bool _monitoring;
    private string? _initializationError;

    public PresentationSettingsViewModel Draft { get; }

    public PresentationSettingsDialog(PresentationSettingsViewModel settings)
    {
        Draft = new() { KeepAwake = settings.KeepAwake, AutoFullScreen = settings.AutoFullScreen };
        InitializeComponent();
    }

    public void StartMonitoring()
    {
        _monitoring = true;
        App.Window.Activated += OnWindowActivated;
        try
        {
            if (ApiInformation.IsPropertyPresent(
                "Windows.UI.Notifications.ToastNotificationManagerForUser", "NotificationMode") &&
                ApiInformation.IsEventPresent(
                    "Windows.UI.Notifications.ToastNotificationManagerForUser", "NotificationModeChanged"))
            {
                _notifications = ToastNotificationManager.GetDefault();
                _notifications.NotificationModeChanged += OnNotificationModeChanged;
            }
            RefreshNotificationStatus();
        }
        catch (Exception ex)
        {
            _initializationError = ex.Message;
            ShowNotificationError(ex);
        }
    }

    public void StopMonitoring()
    {
        _monitoring = false;
        App.Window.Activated -= OnWindowActivated;
        if (_notifications is not null)
        {
            _notifications.NotificationModeChanged -= OnNotificationModeChanged;
            _notifications = null;
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args) => RefreshNotificationStatus();

    private void OnNotificationModeChanged(ToastNotificationManagerForUser sender, object args)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (_monitoring)
            {
                RefreshNotificationStatus();
            }
        }))
        {
            System.Diagnostics.Debug.WriteLine("Notification status update skipped during dispatcher shutdown.");
        }
    }

    private void RefreshNotificationStatus()
    {
        try
        {
            if (_notifications is null)
            {
                NotificationStatusText.Text = _initializationError is null
                    ? "Notification status is unavailable on this Windows version."
                    : $"Could not read notification status: {_initializationError}";
                return;
            }
            NotificationStatusText.Text = _notifications.NotificationMode switch
            {
                ToastNotificationMode.Unrestricted => "Current mode: Unrestricted",
                ToastNotificationMode.PriorityOnly => "Current mode: Priority notifications only",
                ToastNotificationMode.AlarmsOnly => "Current mode: Alarms only",
                _ => "Windows reported an unknown notification mode.",
            };
        }
        catch (Exception ex)
        {
            ShowNotificationError(ex);
        }
    }

    private void ShowNotificationError(Exception exception)
    {
        App.LogError(exception);
        NotificationStatusText.Text = $"Could not read notification status: {exception.Message}";
    }

    private async void OnNotificationSettingsClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync("ms-settings:notifications");

    private async void OnDateTimeSettingsClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync("ms-settings:dateandtime");

    private async Task OpenSettingsAsync(string uri)
    {
        SettingsErrorBar.IsOpen = false;
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri(uri)))
            {
                throw new InvalidOperationException("Windows could not open this settings page. Open Settings from the Start menu instead.");
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            SettingsErrorBar.Message = ex.Message;
            SettingsErrorBar.IsOpen = true;
        }
    }
}
