using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicWin.Utils;
using Forms = System.Windows.Forms;

namespace DynamicWin.Main
{
    /// <summary>
    /// Thin top-edge window with real OS backdrop blur (acrylic), replacing
    /// the Skia-painted strip. Click-through so underlying app buttons stay
    /// usable. Tint follows <see cref="Theme.IslandBackground"/>.
    ///
    /// Smart blur: the frost is only enabled while a real window is behind
    /// the strip; otherwise the window goes fully transparent.
    /// </summary>
    public class StripWindow : Window
    {
        public const float StripHeightDip = 38f;

        const int GradientAlpha = 0x20;

        enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
        }

        enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr window, int idx);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr window, int idx, int val);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        const int DWMWA_CLOAKED = 14;
        const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        private Forms.Screen? targetScreen;
        private IntPtr mainHandle = IntPtr.Zero;
        private readonly DispatcherTimer smartTimer;
        private bool blurOn = true;

        // Blur turns on only after this many consecutive positive polls, so
        // transient frames (maximize/snap animations) never trigger it.
        private int stablePolls = 0;
        private const int OnDebouncePolls = 2;

        // DWM acrylic cannot be faded: the blur itself is binary and
        // composited by the OS. So the transition is masked instead — a
        // WPF tint veil rises smoothly, the acrylic is flipped while the
        // veil is at its peak (hiding the sharp<->blurred swap), then the
        // veil sinks again. The veil is a single mutated brush, updated on
        // the composition frame, so the ramp is GPU-smooth.
        private readonly SolidColorBrush tintBrush = new SolidColorBrush(Colors.Transparent);
        private SolidColorBrush? frozenBrush;
        private int animDir = 0; // +1 = blur on, -1 = blur off
        private long animStartTick = 0;
        private bool accentFlipped = true;

        private const int FadeDurationMs = 340;
        private const double RestBrushAlpha = 0.02;
        private const double PeakBrushAlpha = 0.22;
        private const double FlipPointOn = 0.46;
        private const double FlipPointOff = 0.40;

        public IntPtr MainHandle
        {
            get => mainHandle;
            set => mainHandle = value;
        }

        public StripWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Title = "DynamicWinStrip";

            // Layered window is required: without per-pixel alpha the surface
            // is opaque and the translucent background renders as pure black,
            // hiding the acrylic blur behind it.
            AllowsTransparency = true;
            Background = tintBrush;

            SourceInitialized += (_, _) =>
            {
                MakeClickThrough();
                animDir = 0;
                accentFlipped = true;
                if (blurOn) ApplyTintedBlur();
                else ApplyTransparent();
                UpdateSmartBlur();
                smartTimer.Start();
            };

            smartTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            smartTimer.Tick += (_, _) => UpdateSmartBlur();

            CompositionTarget.Rendering += OnCompositionFrame;
            Closed += (_, _) => CompositionTarget.Rendering -= OnCompositionFrame;
        }

        public void MoveTo(Forms.Screen screen, double heightDip)
        {
            if (screen == null) return;

            var b = screen.Bounds;
            Left = b.Left;
            Top = b.Top;
            Width = Math.Max(1, b.Width);
            Height = Math.Max(1, heightDip);

            targetScreen = screen;
            UpdateSmartBlur();
        }

        public void RefreshTint()
        {
            // Re-apply whichever state is active with the new theme color.
            if (animDir != 0) return; // frame tick will pick up the new theme
            if (blurOn) ApplyTintedBlur();
            else ApplyTransparent();
        }

        void UpdateSmartBlur()
        {
            bool touch = IsAnyWindowBehindStrip();

            // Blur turns on only after consecutive positive polls, so the
            // fade starts once the window has settled — never mid-animation.
            // Turning off stays immediate (the fade-out smooths it).
            if (touch)
            {
                stablePolls++;
                if (stablePolls >= OnDebouncePolls && !blurOn)
                    StartTransition(true);
            }
            else
            {
                stablePolls = 0;
                if (blurOn) StartTransition(false);
            }
        }

        void StartTransition(bool target)
        {
            blurOn = target;

            if (!HandleReady())
            {
                accentFlipped = true;
                if (target) ApplyTintedBlur();
                else ApplyTransparent();
                animDir = 0;
                return;
            }

            // A new transition while one is running retargets from the
            // current frame's veil, so reversal never snaps.
            animDir = target ? 1 : -1;
            accentFlipped = false;
            animStartTick = Environment.TickCount64;

            // Freeze the current brush so the veil starts where it is.
            frozenBrush = new SolidColorBrush(tintBrush.Color);
            Background = frozenBrush;
        }

        void OnCompositionFrame(object? sender, EventArgs e)
        {
            if (animDir == 0) return;

            if (!HandleReady())
            {
                animDir = 0;
                accentFlipped = true;
                if (blurOn) ApplyTintedBlur();
                else ApplyTransparent();
                return;
            }

            long elapsed = Environment.TickCount64 - animStartTick;
            double t = Math.Min(1.0, (double)elapsed / FadeDurationMs);

            double flipAt = animDir > 0 ? FlipPointOn : FlipPointOff;
            double progress = t < flipAt
                ? EaseOutSine(t / flipAt)                       // veil rises
                : 1.0 - SmoothStep((t - flipAt) / (1.0 - flipAt)); // veil sinks

            if (!accentFlipped && t >= flipAt)
            {
                accentFlipped = true;
                if (animDir > 0) SetAccent(AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, GradientAlpha);
                else SetAccent(AccentState.ACCENT_DISABLED, 0);
            }

            double baseAlpha = blurOn ? RestBrushAlpha : 0.0;
            double alpha = baseAlpha + (PeakBrushAlpha - baseAlpha) * progress;
            ApplyVeil(alpha);

            if (t >= 1.0)
            {
                animDir = 0;
                accentFlipped = true;
                frozenBrush = null;
                Background = tintBrush;

                if (blurOn)
                {
                    SetAccent(AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, GradientAlpha);
                    ApplyVeil(RestBrushAlpha);
                }
                else
                {
                    SetAccent(AccentState.ACCENT_DISABLED, 0);
                    ApplyVeil(0.0);
                }
            }
        }

        static double EaseOutSine(double x) => Math.Sin(Math.Min(1.0, Math.Max(0.0, x)) * Math.PI / 2.0);

        static double SmoothStep(double x)
        {
            x = Math.Min(1.0, Math.Max(0.0, x));
            return x * x * (3.0 - 2.0 * x);
        }

        bool HandleReady()
        {
            try { return new WindowInteropHelper(this).Handle != IntPtr.Zero; }
            catch { return false; }
        }

        void ApplyVeil(double alpha)
        {
            var island = Theme.IslandBackground ?? Col.White;
            byte r = (byte)(island.r * 255);
            byte g = (byte)(island.g * 255);
            byte b = (byte)(island.b * 255);

            var color = Color.FromArgb((byte)(Math.Max(0, Math.Min(1, alpha)) * 255), r, g, b);
            if (frozenBrush != null) frozenBrush.Color = color;
            else tintBrush.Color = color;
        }

        bool IsAnyWindowBehindStrip()
        {
            if (targetScreen == null) return true;

            var b = targetScreen.Bounds;
            int stripBottom = b.Top + ReservePx();
            int sL = b.Left, sT = b.Top, sR = b.Right, sB = stripBottom;

            IntPtr self = IntPtr.Zero;
            try { self = new WindowInteropHelper(this).Handle; } catch { }

            bool found = false;

            EnumWindows((h, l) =>
            {
                if (found) return true;
                if (h == IntPtr.Zero || h == self || h == mainHandle) return true;
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                if (IsCloaked(h)) return true;
                if (IsDesktopChrome(h)) return true;
                if (!TryGetVisualBounds(h, out RECT r)) return true;

                // Touching counts: a maximized window rests exactly on the
                // strip's bottom edge (work area reservation) and must
                // trigger the blur. Windows strictly below stay transparent.
                // Bounds exclude the drop shadow, and the on-debounce
                // filters transient maximize/snap animation frames.
                if (r.Right > sL && r.Left < sR && r.Bottom > sT && r.Top <= sB)
                    found = true;

                return true;
            }, IntPtr.Zero);

            return found;
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

        static bool TryGetVisualBounds(IntPtr hWnd, out RECT r)
        {
            // Extended frame bounds exclude the drop shadow: shadows must
            // not trigger the blur. Falls back to the full window rect.
            try
            {
                if (DwmGetWindowAttributeRect(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS,
                    out r, Marshal.SizeOf(typeof(RECT))) == 0
                    && r.Right > r.Left && r.Bottom > r.Top)
                    return true;
            }
            catch { }

            try
            {
                if (GetWindowRect(hWnd, out r)) return true;
            }
            catch { }

            r = new RECT();
            return false;
        }

        static bool IsDesktopChrome(IntPtr hWnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                if (GetClassName(hWnd, sb, sb.Capacity) == 0) return false;

                string cls = sb.ToString();
                return cls == "Progman"
                    || cls == "WorkerW"
                    || cls == "Shell_TrayWnd"
                    || cls == "Shell_SecondaryTrayWnd";
            }
            catch { }
            return false;
        }

        int ReservePx()
        {
            double dpi = 1.0;
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget != null)
                    dpi = src.CompositionTarget.TransformToDevice.M11;
            }
            catch { }
            return Math.Max(1, (int)Math.Ceiling(StripHeightDip * dpi));
        }

        void ApplyTintedBlur()
        {
            Background = tintBrush;
            ApplyVeil(RestBrushAlpha);
            SetAccent(AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, GradientAlpha);
        }

        void ApplyTransparent()
        {
            Background = Brushes.Transparent;
            SetAccent(AccentState.ACCENT_DISABLED, 0);
        }

        void SetAccent(AccentState state, int alpha)
        {
            var hwnd = IntPtr.Zero;
            try { hwnd = new WindowInteropHelper(this).Handle; } catch { }
            if (hwnd == IntPtr.Zero) return;

            var island = Theme.IslandBackground ?? Col.White;
            byte r = (byte)(island.r * 255);
            byte g = (byte)(island.g * 255);
            byte b = (byte)(island.b * 255);

            // GradientColor layout: 0xAABBGGRR.
            int gradient = (alpha << 24) | (b << 16) | (g << 8) | r;

            var policy = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = 0,
                GradientColor = gradient,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(policy);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = size,
                    Data = ptr
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        void MakeClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
    }
}
