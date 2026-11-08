using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using WinUIEx.Messaging;

namespace PresenterScript.Services;

/// <summary>
/// Puts code snippets on the clipboard. The text is delay-rendered, so the provider callback runs only
/// when another app actually reads (pastes) it.
/// </summary>
public sealed class SnippetClipboard(DispatcherQueue dispatcher)
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private int _generation;
    private int _pastedGeneration = -1;
    private bool _onClipboard;
    private WindowMessageMonitor? _monitor;

    /// <summary>Raised on the UI thread with the snippet index the first time a placed snippet is pasted.</summary>
    public event EventHandler<int>? SnippetPasted;

    /// <summary>Raised on the UI thread when another app replaces (or clears) the snippet on the clipboard.</summary>
    public event EventHandler? SnippetReplaced;

    /// <summary>Starts watching for clipboard changes made by other apps. Works while the window is in the background.</summary>
    public void Attach(Window window)
    {
        _monitor = new WindowMessageMonitor(window);
        _monitor.WindowMessageReceived += (_, e) =>
        {
            if (e.Message.MessageId == WM_CLIPBOARDUPDATE)
            {
                OnClipboardUpdated();
            }
        };
        AddClipboardFormatListener(WinRT.Interop.WindowNative.GetWindowHandle(window));
    }

    public void Put(string code, int snippetIndex)
    {
        var text = code.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        int generation = ++_generation;
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };

        try
        {
            package.SetDataProvider(StandardDataFormats.Text, request =>
            {
                request.SetData(text);
                // A single paste can request the data more than once; report each placement only once.
                if (generation == _generation && Interlocked.Exchange(ref _pastedGeneration, generation) != generation)
                {
                    dispatcher.TryEnqueue(() => SnippetPasted?.Invoke(this, snippetIndex));
                }
            });

            // Keep clipboard history / cloud sync from reading the data up front, which would look like a paste.
            Clipboard.SetContentWithOptions(package, new ClipboardContentOptions
            {
                IsAllowedInHistory = false,
                IsRoamable = false,
            });
            _onClipboard = true;
        }
        catch (Exception)
        {
            // The clipboard can be briefly locked by another process; clicking the snippet again retries.
        }
    }

    private void OnClipboardUpdated()
    {
        // Our own SetContent also raises an update; the clipboard owner then is this process (OLE's clipboard window).
        if (!_onClipboard)
        {
            return;
        }
        uint ownerProcess = 0;
        var owner = GetClipboardOwner();
        if (owner != 0)
        {
            GetWindowThreadProcessId(owner, out ownerProcess);
        }
        if (ownerProcess == Environment.ProcessId)
        {
            return;
        }

        _onClipboard = false;
        _generation++;
        SnippetReplaced?.Invoke(this, EventArgs.Empty);
    }

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}
