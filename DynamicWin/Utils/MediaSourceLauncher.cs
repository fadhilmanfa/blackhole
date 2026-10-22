using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicWin.Utils
{
    /// <summary>
    /// Brings the application that owns the tracked media session back to
    /// the foreground when the now-playing card is clicked.
    ///
    /// Windows SMTC only exposes the source app id (not a tab or a window
    /// handle), so Win32 players are located by process/window while
    /// packaged apps are activated through their AUMID.
    /// </summary>
    public static class MediaSourceLauncher
    {
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        const int SW_SHOW = 5;
        const int SW_RESTORE = 9;

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const uint GW_OWNER = 4;

        const int DWMWA_CLOAKED = 14;

        /// <summary>
        /// Foregrounds the tracked player. Does nothing when playback comes
        /// from raw loopback audio without an SMTC session.
        /// </summary>
        public static void Activate()
        {
            string appId = MediaState.SourceAppId;
            if (string.IsNullOrWhiteSpace(appId)) return;

            try
            {
                bool packaged = appId.IndexOf('!') >= 0;

                // Packaged apps expose a full AUMID. Activation is the
                // supported way to foreground a running instance (and also
                // opens it again if it is somehow not running).
                if (packaged && TryActivatePackaged(appId))
                    return;

                if (TryActivateWindow(appId))
                    return;

                // Desktop PWAs and packaged Win32 apps can register an AUMID
                // that does not match their process name. Try shell activation
                // as a last resort.
                if (!packaged && LooksRegistered(appId))
                    TryActivatePackaged(appId);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Opening media source failed: " + e.Message);
            }
        }

        static bool TryActivateWindow(string appId)
        {
            string normalized = NormalizeAppId(appId);
            if (normalized == "") return false;

            var processIds = FindCandidateProcessIds(normalized);
            if (processIds.Count == 0) return false;

            var windows = new List<IntPtr>();
            try
            {
                EnumWindows((hWnd, _) =>
                {
                    try
                    {
                        GetWindowThreadProcessId(hWnd, out uint processId);
                        if (processIds.Contains(processId) && IsUsableWindow(hWnd))
                            windows.Add(hWnd);
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Window search failed: " + e.Message);
            }

            if (windows.Count == 0) return false;

            return ActivateWindow(ChooseBestWindow(windows));
        }

        /// <summary>
        /// Matches an AUMID such as "Spotify.exe", "Chrome.exe" or
        /// "MSEdge.exe" to running processes. Exact matches win; case and
        /// separators are ignored so "Microsoft Edge" still finds msedge.
        /// </summary>
        static HashSet<uint> FindCandidateProcessIds(string normalizedId)
        {
            var exact = new HashSet<uint>();
            var loose = new HashSet<uint>();
            string[] aliases = GetAliases(normalizedId);
            uint ownId = (uint)Environment.ProcessId;

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    uint id = (uint)process.Id;
                    if (id == ownId) continue;

                    string name = NormalizeToken(process.ProcessName);
                    if (name == "") continue;

                    bool isExact = name == normalizedId || Array.IndexOf(aliases, name) >= 0;
                    bool isLoose = normalizedId.Length >= 4 && name.Length >= 4 &&
                        (name.Contains(normalizedId, StringComparison.Ordinal) ||
                         normalizedId.Contains(name, StringComparison.Ordinal));

                    if (isExact) exact.Add(id);
                    else if (isLoose) loose.Add(id);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }

            return exact.Count > 0 ? exact : loose;
        }

        static string[] GetAliases(string normalizedId)
        {
            switch (normalizedId)
            {
                case "microsoftedge": return new[] { "msedge" };
                case "googlechrome": return new[] { "chrome" };
                case "mozillafirefox": return new[] { "firefox" };
                case "operagx": return new[] { "opera" };
                case "bravebrowser": return new[] { "brave" };
                case "windowsmediaplayer": return new[] { "wmplayer" };
                case "microsoftzunemusic":
                case "microsoftmediaplayer":
                    return new[] { "musicplayer", "mediaplayer", "microsoftmediaplayer" };
                default: return Array.Empty<string>();
            }
        }

        static bool IsUsableWindow(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd)) return false;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return false;
            if ((GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return false;
            if (IsCloaked(hWnd)) return false;
            return GetWindowTextLength(hWnd) > 0;
        }

        static bool IsCloaked(IntPtr hWnd)
        {
            try
            {
                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
                    return cloaked != 0;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Picks the window most likely to belong to the current track.
        /// Browsers expose the active tab title in the window title, so a
        /// title match selects the right browser window when several are
        /// open; z-order then picks the most recently used fallback.
        /// </summary>
        static IntPtr ChooseBestWindow(List<IntPtr> windows)
        {
            string title = MediaState.Title;
            string artist = MediaState.Artist;

            IntPtr best = IntPtr.Zero;
            int bestScore = int.MinValue;

            for (int i = 0; i < windows.Count; i++)
            {
                IntPtr hWnd = windows[i];
                string windowTitle = GetWindowTitle(hWnd);

                int score = 0;

                if (title.Length >= 3 &&
                    windowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                    score += 100;

                if (artist.Length >= 3 &&
                    windowTitle.Contains(artist, StringComparison.OrdinalIgnoreCase))
                    score += 40;

                if (!IsIconic(hWnd))
                    score += 10;

                // EnumWindows returns windows in top-to-bottom z-order.
                score += Math.Max(0, 20 - i);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = hWnd;
                }
            }

            return best;
        }

        static string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length <= 0) return "";

            var builder = new StringBuilder(length + 1);
            return GetWindowText(hWnd, builder, builder.Capacity) > 0
                ? builder.ToString()
                : "";
        }

        static bool ActivateWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
            else
                ShowWindow(hWnd, SW_SHOW);

            // Our overlay is a topmost window, so Windows' foreground lock
            // can turn SetForegroundWindow into a taskbar flash. Attach to
            // the current foreground thread first, then fall back to the
            // shell's own switch path.
            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = foreground != IntPtr.Zero
                ? GetWindowThreadProcessId(foreground, out _)
                : 0;
            uint currentThread = GetCurrentThreadId();

            bool attached = foregroundThread != 0 &&
                foregroundThread != currentThread &&
                AttachThreadInput(currentThread, foregroundThread, true);

            try
            {
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                SetFocus(hWnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }

            if (GetForegroundWindow() == hWnd) return true;

            SwitchToThisWindow(hWnd, true);
            return GetForegroundWindow() == hWnd;
        }

        static bool TryActivatePackaged(string appId)
        {
            try
            {
                var manager = (IApplicationActivationManager)new ApplicationActivationManager();
                int hr = manager.ActivateApplication(appId, null, ActivateOptions.None, out _);

                if (hr >= 0) return true;

                Debug.WriteLine($"AUMID activation failed (0x{hr:X8}): {appId}");
            }
            catch (Exception e)
            {
                Debug.WriteLine("AUMID activation failed: " + e.Message);
            }

            return false;
        }

        static bool LooksRegistered(string appId)
        {
            return appId.IndexOf('!') >= 0 ||
                (!appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                 appId.IndexOf('.') >= 0);
        }

        /// <summary>
        /// Reduces an AUMID to a comparable process name: package prefix and
        /// app part, path, extension, case and separators are stripped.
        /// </summary>
        static string NormalizeAppId(string appId)
        {
            string id = appId.Trim();
            if (id.Length == 0) return "";

            int bang = id.IndexOf('!');
            if (bang >= 0 && bang + 1 < id.Length)
                id = id.Substring(bang + 1);

            int slash = id.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0 && slash + 1 < id.Length)
                id = id.Substring(slash + 1);

            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                id = id.Substring(0, id.Length - 4);

            return NormalizeToken(id);
        }

        static string NormalizeToken(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    class ApplicationActivationManager
    {
    }

    enum ActivateOptions
    {
        None = 0
    }
}
