using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PresenterScript.Services;

public sealed class PresentationSessionService
{
    private PowerRequestHandle? _request;
    private bool _systemRequested;
    private bool _displayRequested;

    public void Start()
    {
        if (_request is not null)
        {
            return;
        }

        nint reason = Marshal.StringToHGlobalUni("PresenterScript is presenting a live-coding talk.");
        try
        {
            var context = new ReasonContext
            {
                Flags = 1,
                Reason = new ReasonUnion { SimpleReasonString = reason },
            };
            _request = PowerCreateRequest(ref context);
            if (_request.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a keep-awake request.");
            }
            if (!PowerSetRequest(_request, PowerRequestType.SystemRequired))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not prevent idle sleep.");
            }
            _systemRequested = true;
            if (!PowerSetRequest(_request, PowerRequestType.DisplayRequired))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not keep the displays on.");
            }
            _displayRequested = true;
        }
        catch (Exception startError)
        {
            try
            {
                Stop();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(startError, cleanupError);
            }
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(reason);
        }
    }

    public void Stop()
    {
        if (_request is null)
        {
            return;
        }

        var errors = new List<Exception>();
        try
        {
            if (_displayRequested && !PowerClearRequest(_request, PowerRequestType.DisplayRequired))
            {
                errors.Add(new Win32Exception(Marshal.GetLastWin32Error(), "Could not release the display request."));
            }
            if (_systemRequested && !PowerClearRequest(_request, PowerRequestType.SystemRequired))
            {
                errors.Add(new Win32Exception(Marshal.GetLastWin32Error(), "Could not release the idle-sleep request."));
            }
        }
        finally
        {
            _request.Dispose();
            _request = null;
            _systemRequested = false;
            _displayRequested = false;
        }
        if (errors.Count > 0)
        {
            throw new AggregateException("Could not clear keep-awake requests; the request handle has been closed.", errors);
        }
    }

    private enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public ReasonUnion Reason;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ReasonUnion
    {
        [FieldOffset(0)]
        public nint SimpleReasonString;
        [FieldOffset(0)]
        public DetailedReason Detailed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DetailedReason
    {
        public nint LocalizedReasonModule;
        public uint LocalizedReasonId;
        public uint ReasonStringCount;
        public nint ReasonStrings;
    }

    private sealed class PowerRequestHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern PowerRequestHandle PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(PowerRequestHandle request, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(PowerRequestHandle request, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
