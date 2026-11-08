using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PresenterScript;

/// <summary>Reference for the script's special Markdown syntax and keyboard shortcuts. Single instance.</summary>
public sealed partial class HelpWindow : Window
{
    private static HelpWindow? s_instance;

    private HelpWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(HelpTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(820, 900));
        WinUIEx.WindowManager.Get(this).PersistenceId = "HelpWindow";

        foreach (var (minutes, label) in new[] { (-5, "5 min ahead"), (-2.5, "2:30 ahead"), (0, "On pace"), (2, "2 min behind"), (3.5, "3:30 behind"), (5, "5 min behind") })
        {
            var delta = TimeSpan.FromMinutes(minutes);
            PaceLegend.Children.Add(new Border
            {
                Padding = new Thickness(8, 1, 8, 2),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(MainPage.PaceColor(delta)),
                Child = new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = MainPage.PaceTextBrushFor(delta) },
            });
        }

        Closed += (_, _) => s_instance = null;
    }

    public static void ShowOrActivate()
    {
        s_instance ??= new HelpWindow();
        s_instance.Activate();
    }

    public static void CloseIfOpen() => s_instance?.Close();
}
