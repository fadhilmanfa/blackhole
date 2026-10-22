using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace DynamicWin.Main
{
    /// <summary>
    /// Reserves the top strip (behind the acrylic StripWindow) as an AppBar docked to the
    /// top edge, so maximized windows start below it — taskbar-style.
    /// Work area is restored on <see cref="Unregister"/>.
    /// </summary>
    public static class TopAppBar
    {
        const int ABM_NEW = 0;
        const int ABM_REMOVE = 1;
        const int ABM_QUERYPOS = 2;
        const int ABM_SETPOS = 3;

        const int ABE_TOP = 1;
        const int ABN_POSCHANGED = 1;
        const int WM_DISPLAYCHANGE = 0x007E;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")]
        static extern UIntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int RegisterWindowMessage(string lpString);

        static IntPtr registeredHwnd = IntPtr.Zero;
        static int callbackMsg;
        static HwndSource? source;
        static int lastHeightPx;

        public static void Register(Window window, Forms.Screen screen, int heightPx)
        {
            Unregister();

            if (window == null || screen == null || heightPx <= 0) return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            callbackMsg = RegisterWindowMessage("DynamicWinAppBarMessage");
            lastHeightPx = heightPx;

            var abd = NewData(hwnd, screen, heightPx);
            SHAppBarMessage(ABM_NEW, ref abd);
            SetPos(hwnd, screen, heightPx);

            registeredHwnd = hwnd;
            source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        public static void Unregister()
        {
            if (registeredHwnd == IntPtr.Zero) return;

            try
            {
                if (source != null)
                {
                    try { source.RemoveHook(WndProc); } catch { }
                    source = null;
                }

                var abd = new APPBARDATA
                {
                    cbSize = Marshal.SizeOf<APPBARDATA>(),
                    hWnd = registeredHwnd
                };
                SHAppBarMessage(ABM_REMOVE, ref abd);
            }
            catch { }
            finally { registeredHwnd = IntPtr.Zero; }
        }

        static APPBARDATA NewData(IntPtr hwnd, Forms.Screen screen, int heightPx)
        {
            var b = screen.Bounds;
            return new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = hwnd,
                uCallbackMessage = callbackMsg,
                uEdge = ABE_TOP,
                rc = new RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Top + heightPx }
            };
        }

        static void SetPos(IntPtr hwnd, Forms.Screen screen, int heightPx)
        {
            if (hwnd == IntPtr.Zero || screen == null || heightPx <= 0) return;

            var b = screen.Bounds;
            var abd = NewData(hwnd, screen, heightPx);
            abd.rc = new RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Top + heightPx };

            SHAppBarMessage(ABM_QUERYPOS, ref abd);

            abd.uEdge = ABE_TOP;
            SHAppBarMessage(ABM_SETPOS, ref abd);
        }

        static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == callbackMsg && wParam.ToInt32() == ABN_POSCHANGED)
            {
                try { SetPos(registeredHwnd, Forms.Screen.FromHandle(registeredHwnd), lastHeightPx); } catch { }
            }
            else if (msg == WM_DISPLAYCHANGE)
            {
                try { SetPos(registeredHwnd, Forms.Screen.FromHandle(registeredHwnd), lastHeightPx); } catch { }
            }

            return IntPtr.Zero;
        }
    }
}
