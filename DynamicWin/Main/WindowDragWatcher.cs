using System;
using System.Runtime.InteropServices;

namespace DynamicWin.Main
{
    /// <summary>
    /// Watches shell window move/size operations (including Windows 11 snap
    /// layout drags). While a window is being dragged the overlay drops out
    /// of the topmost band, so the dragged window and the snap-layout flyout
    /// render above the strip instead of being hidden behind it.
    /// </summary>
    public static class WindowDragWatcher
    {
        const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
        const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        static WinEventDelegate? callback;
        static IntPtr startHook;
        static IntPtr endHook;

        /// <summary>True while a shell move/size operation is in progress.</summary>
        public static event Action<bool>? DragStateChanged;

        public static void Start()
        {
            if (callback != null) return;

            callback = OnEvent;
            const uint flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;

            startHook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZESTART,
                IntPtr.Zero, callback, 0, 0, flags);
            endHook = SetWinEventHook(EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, callback, 0, 0, flags);
        }

        public static void Stop()
        {
            if (startHook != IntPtr.Zero)
            {
                UnhookWinEvent(startHook);
                startHook = IntPtr.Zero;
            }

            if (endHook != IntPtr.Zero)
            {
                UnhookWinEvent(endHook);
                endHook = IntPtr.Zero;
            }

            callback = null;
        }

        static void OnEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType == EVENT_SYSTEM_MOVESIZESTART)
                    DragStateChanged?.Invoke(true);
                else if (eventType == EVENT_SYSTEM_MOVESIZEEND)
                    DragStateChanged?.Invoke(false);
            }
            catch
            {
                // A watcher must never take the app down.
            }
        }
    }
}
