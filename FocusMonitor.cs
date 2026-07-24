using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FocusTrace;

public static class FocusMonitor
{
    private const uint GetWindowOwner = 4;
    private const int ExtendedStyleIndex = -20;
    private const long ToolWindowStyle = 0x00000080L;
    private const int ShowWindowMinimize = 6;
    private const uint WindowCloseMessage = 0x0010;

    private static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Google Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Mozilla Firefox",
        ["explorer"] = "File Explorer",
        ["devenv"] = "Visual Studio",
        ["Code"] = "Visual Studio Code",
        ["WindowsTerminal"] = "Windows Terminal",
        ["OUTLOOK"] = "Microsoft Outlook",
        ["Teams"] = "Microsoft Teams",
        ["slack"] = "Slack",
        ["WINWORD"] = "Microsoft Word",
        ["EXCEL"] = "Microsoft Excel",
        ["POWERPNT"] = "Microsoft PowerPoint",
        ["FocusTrace"] = "FocusTrace"
    };

    private static readonly Dictionary<string, string> MeetingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ms-teams"] = "Microsoft Teams",
        ["Teams"] = "Microsoft Teams",
        ["Zoom"] = "Zoom",
        ["ZoomMeeting"] = "Zoom",
        ["Webex"] = "Webex",
        ["CiscoCollabHost"] = "Webex",
        ["Skype"] = "Skype"
    };

    private static readonly Dictionary<string, string> AutoDetectedMeetingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ZoomMeeting"] = "Zoom",
        ["Webex"] = "Webex",
        ["CiscoCollabHost"] = "Webex",
        ["Skype"] = "Skype"
    };

    public static string? GetForegroundApplicationName()
    {
        nint window = GetForegroundWindow();
        if (window == nint.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string processName = process.ProcessName;
            if (FriendlyNames.TryGetValue(processName, out string? friendlyName))
            {
                return friendlyName;
            }

            return CultureName(processName);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsUserActive(TimeSpan idleThreshold)
    {
        LastInputInfo lastInput = new() { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref lastInput))
        {
            return true;
        }

        uint idleMilliseconds = unchecked((uint)Environment.TickCount - lastInput.Time);
        return idleMilliseconds < idleThreshold.TotalMilliseconds;
    }

    public static int GetVisibleWindowCount()
        => GetVisibleApplications().Sum(app => app.WindowCount);

    public static IReadOnlyList<VisibleApplicationInfo> GetVisibleApplications()
    {
        Dictionary<string, int> applications = new(StringComparer.OrdinalIgnoreCase);
        _ = EnumWindows((window, _) =>
        {
            if (!IsTrackableWindow(window))
            {
                return true;
            }

            string? appName = GetApplicationName(window, includeCurrentProcess: false);
            if (appName is not null)
            {
                applications[appName] = applications.GetValueOrDefault(appName) + 1;
            }
            return true;
        }, nint.Zero);

        return applications
            .OrderBy(pair => pair.Key)
            .Select(pair => new VisibleApplicationInfo(pair.Key, pair.Value))
            .ToList();
    }

    public static int MinimizeVisibleWindowsExcept(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return 0;
        }

        List<(nint Window, string AppName)> windows = [];
        _ = EnumWindows((window, _) =>
        {
            if (!IsTrackableWindow(window))
            {
                return true;
            }

            string? visibleAppName = GetApplicationName(window, includeCurrentProcess: true);
            if (visibleAppName is not null)
            {
                windows.Add((window, visibleAppName));
            }

            return true;
        }, nint.Zero);

        if (!windows.Any(item =>
                string.Equals(item.AppName, appName, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        int minimized = 0;
        foreach ((nint window, string visibleAppName) in windows)
        {
            if (string.Equals(visibleAppName, appName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ShowWindow(window, ShowWindowMinimize))
            {
                minimized++;
            }
        }

        return minimized;
    }

    public static int RequestCloseVisibleWindows(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return 0;
        }

        int requested = 0;
        _ = EnumWindows((window, _) =>
        {
            if (!IsTrackableWindow(window))
            {
                return true;
            }

            string? visibleAppName = GetApplicationName(window, includeCurrentProcess: false);
            if (string.Equals(visibleAppName, appName, StringComparison.OrdinalIgnoreCase) &&
                PostMessage(window, WindowCloseMessage, nint.Zero, nint.Zero))
            {
                requested++;
            }

            return true;
        }, nint.Zero);
        return requested;
    }

    public static string? GetRunningMeetingApplication()
    {
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (AutoDetectedMeetingApps.TryGetValue(process.ProcessName, out string? friendlyName) &&
                        process.MainWindowHandle != nint.Zero &&
                        IsWindowVisible(process.MainWindowHandle) &&
                        !IsIconic(process.MainWindowHandle))
                    {
                        return friendlyName;
                    }
                }
                catch
                {
                    // Processes can exit or become inaccessible during enumeration.
                }
            }
        }

        return null;
    }

    public static bool IsMeetingApplication(string appName) =>
        MeetingApps.Values.Contains(appName, StringComparer.OrdinalIgnoreCase);

    private static string CultureName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return "Unknown app";
        }

        return processName.Length == 1
            ? processName.ToUpperInvariant()
            : char.ToUpperInvariant(processName[0]) + processName[1..];
    }

    private static bool IsTrackableWindow(nint window) =>
        IsWindowVisible(window) &&
        !IsIconic(window) &&
        GetWindow(window, GetWindowOwner) == nint.Zero &&
        GetWindowTextLength(window) > 0 &&
        (GetWindowLongPtr(window, ExtendedStyleIndex).ToInt64() & ToolWindowStyle) == 0;

    private static string? GetApplicationName(nint window, bool includeCurrentProcess)
    {
        _ = GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0 || (!includeCurrentProcess && processId == Environment.ProcessId))
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return FriendlyNames.TryGetValue(process.ProcessName, out string? friendlyName)
                ? friendlyName
                : CultureName(process.ProcessName);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}

public sealed record VisibleApplicationInfo(string AppName, int WindowCount);
