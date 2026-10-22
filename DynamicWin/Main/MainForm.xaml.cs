using DynamicWin.Resources;
using DynamicWin.UI.Menu;
using DynamicWin.UI.Menu.Menus;
using DynamicWin.Utils;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DynamicWin.Main
{
    public partial class MainForm : Window
    {
        private static MainForm instance;
        public static MainForm Instance { get => instance; }

        public static Action<System.Windows.Input.MouseWheelEventArgs> onScrollEvent;

        private readonly Forms.NotifyIcon _trayIcon;

        private Forms.Screen? currentScreen;
        private StripWindow? stripWindow;

        private DateTime _lastRenderTime;
        private readonly TimeSpan _targetElapsedTime = TimeSpan.FromMilliseconds(16); // ~60 FPS

        public Action onMainFormRender;

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr window, int idx, int val);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr window, int idx);

        // Define integer values
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_APPWINDOW = 0x00040000;
        const int WS_EX_TOPMOST = 0x00000008;

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        // Both overlay windows are owned by a hidden WPF window. Without
        // SWP_NOOWNERZORDER Windows also moves that owner, and the topmost
        // change silently does not stick.
        const uint SWP_NOOWNERZORDER = 0x0200;
        const int VK_LBUTTON = 0x01;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        bool draggingWindow;
        long dragStartTick;
        long lastTopmostCheck;

        public MainForm()
        {
            InitializeComponent();

            _trayIcon = new Forms.NotifyIcon();

            CompositionTarget.Rendering += OnRendering;

            instance = this;

            this.WindowStyle = WindowStyle.None;
            this.WindowState = WindowState.Maximized;
            this.ResizeMode = ResizeMode.NoResize;
            this.Topmost = true;
            this.AllowsTransparency = true;
            this.ShowInTaskbar = false;
            this.Title = "DynamicWin Overlay";

            // Loaded event to ensure that this does not show the application on the Alt+Tab switcher

            this.Loaded += (s, e) =>
            {
                IntPtr handle = new WindowInteropHelper(this).Handle; // Define Handle
                int winStyle = GetWindowLong(handle, GWL_EXSTYLE); // Fetch defined GWL_EXSTYLE

                // Apply WS_EX_TOOLWINDOW and remove WS_EX_APPWINDOW if it exists
                winStyle = (winStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
                SetWindowLong(handle, GWL_EXSTYLE, winStyle);

                stripWindow = new StripWindow();
                stripWindow.MoveTo(currentScreen ?? Forms.Screen.PrimaryScreen!, StripWindow.StripHeightDip);
                stripWindow.MainHandle = handle;
                stripWindow.Show();

                // Keep the main overlay above the strip so the island
                // paints over the blurred bar.
                ApplyNativeTopmost(true);

                TopAppBar.Register(this, currentScreen ?? Forms.Screen.PrimaryScreen!, StripReservePx());

                // Let dragged windows (and the Win11 snap-layout flyout, which
                // lives at the very top edge) render above the strip.
                WindowDragWatcher.DragStateChanged += SetDragOverlay;
                WindowDragWatcher.Start();
            };

            SetMonitor(Settings.ScreenIndex);

            AddRenderer();

            MainForm.Instance.AllowDrop = true;

            // Tray icon

            _trayIcon.Icon = new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "Resources/icons/TrayIcon.ico"));
            _trayIcon.Text = "DynamicWin";

            _trayIcon.ContextMenuStrip = new Forms.ContextMenuStrip();

            _trayIcon.ContextMenuStrip.Items.Add("Restart Control", null, (x, y) =>
            {
                if (RendererMain.Instance != null) RendererMain.Instance.Destroy();
                this.Content = new Grid();

                AddRenderer();
            });

            _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (x, y) =>
            {
                MenuManager.OpenMenu(new SettingsMenu());
            });

            _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (x, y) =>
            {
                TopAppBar.Unregister();
                SaveManager.SaveAll();
                Process.GetCurrentProcess().Kill();
            });

            _trayIcon.Visible = true;
        }


        public void SetMonitor(int monitorIndex)
        {
            var screen = System.Windows.Forms.Screen.AllScreens[Math.Clamp(monitorIndex, 0, GetMonitorCount() - 1)];
            Settings.ScreenIndex = Math.Clamp(monitorIndex, 0, GetMonitorCount() - 1);

            if (screen != null)
            {
                if (!this.IsLoaded)
                    this.WindowStartupLocation = WindowStartupLocation.Manual;

                this.WindowState = WindowState.Normal;
                this.ResizeMode = ResizeMode.CanResize;

                // Full monitor bounds (not WorkingArea): the overlay must also
                // cover the reserved AppBar strip at the top, otherwise the
                // island would sit below it after re-registration.
                var area = screen.Bounds;

                this.Left = area.Left;
                this.Top = area.Top;
                this.Width = area.Width;
                this.Height = area.Height;

                this.ResizeMode = ResizeMode.NoResize;

                currentScreen = screen;
                stripWindow?.MoveTo(screen, StripWindow.StripHeightDip);
                if (this.IsLoaded)
                    TopAppBar.Register(this, screen, StripReservePx());
            }
        }

        public static int GetMonitorCount()
        {
            return System.Windows.Forms.Screen.AllScreens.Length;
        }

        int StripReservePx()
        {
            double dpi = 1.0;
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget != null)
                    dpi = src.CompositionTarget.TransformToDevice.M11;
            }
            catch { }
            return Math.Max(1, (int)Math.Ceiling(StripWindow.StripHeightDip * dpi));
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowDragWatcher.DragStateChanged -= SetDragOverlay;
            WindowDragWatcher.Stop();

            TopAppBar.Unregister();
            try { stripWindow?.Close(); } catch { }
            stripWindow = null;
            base.OnClosed(e);
        }

        /// <summary>
        /// While a shell window move/size is running the overlay leaves the
        /// topmost band, so the dragged window and the snap-layout flyout are
        /// not hidden behind the strip. Restoring re-asserts the overlay
        /// above the strip afterwards.
        /// </summary>
        void SetDragOverlay(bool dragging)
        {
            try
            {
                draggingWindow = dragging;
                dragStartTick = Environment.TickCount64;

                ApplyNativeTopmost(!dragging);
            }
            catch { }
        }

        void ApplyNativeTopmost(bool topmost)
        {
            const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
            var after = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;

            stripWindow?.SetTopmost(topmost);

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, after, 0, 0, 0, 0, flags);
        }

        bool IsOverlayTopmost()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return true;
            return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        }

        /// <summary>
        /// Safety net: Windows 11 does not always deliver MOVESIZEEND (and WPF
        /// may silently clear topmost). Re-asserts the overlay whenever it is
        /// not dragging anymore, and ends a stale drag once the button is up.
        /// </summary>
        void MaintainTopmost()
        {
            long now = Environment.TickCount64;
            if (now - lastTopmostCheck < 500) return;
            lastTopmostCheck = now;

            if (draggingWindow)
            {
                bool buttonUp = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0;
                bool stale = now - dragStartTick > 20000;

                if ((buttonUp || stale) && now - dragStartTick > 400)
                    SetDragOverlay(false);

                return;
            }

            if (!IsOverlayTopmost())
                ApplyNativeTopmost(true);
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            MaintainTopmost();

            var currentTime = DateTime.Now;
            if (currentTime - _lastRenderTime >= _targetElapsedTime)
            {
                _lastRenderTime = currentTime;

                onMainFormRender?.Invoke();
            }
        }

        public bool isDragging = false;

        public void OnScroll(object? sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            onScrollEvent?.Invoke(e);
        }

        public void AddRenderer()
        {
            if (RendererMain.Instance != null) RendererMain.Instance.Destroy();

            var customControl = new RendererMain();
            
            var parent = new Grid();
            parent.Children.Add(customControl);

            this.Content = parent;

            stripWindow?.RefreshTint();
        }

        public void MainForm_DragEnter(object? sender, DragEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("DragEnter");

            isDragging = true;
            e.Effects = DragDropEffects.Copy;

            if (!(MenuManager.Instance.ActiveMenu is DropFileMenu))
            {
                MenuManager.OpenMenu(new DropFileMenu());
            }
        }

        public void MainForm_DragLeave(object? sender, EventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("DragLeave");

            isDragging = false;

            MenuManager.OpenMenu(Res.HomeMenu);
        }

        bool isLocalDrag = false;

        internal void StartDrag(string[] files, Action callback)
        {
            if (isLocalDrag) return;

            Array.ForEach(files, file => { System.Diagnostics.Debug.WriteLine(file); });

            if (files == null) return;
            else if (files.Length <= 0) return;

            try
            {
                isLocalDrag = true;

                DataObject dataObject = new DataObject(DataFormats.FileDrop, files);
                var effects = DragDrop.DoDragDrop((DependencyObject)this, dataObject, DragDropEffects.Move | DragDropEffects.Copy);

                if (RendererMain.Instance != null) RendererMain.Instance.Destroy();
                this.Content = new Grid();
                AddRenderer();

                callback?.Invoke();

                isLocalDrag = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred: " + ex.Message);
            }
        }

        protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
        {
            if (e.Action == DragAction.Cancel)
            {
                isLocalDrag = false;
            }
            else if (e.Action == DragAction.Continue)
            {
                isLocalDrag = true;
            }
            else if (e.Action == DragAction.Drop)
            {
                isLocalDrag = false;
            }
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            base.OnDragOver(e);
        }

        public void OnDrop(object sender, System.Windows.DragEventArgs e)
        {
            isDragging = false;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropFileMenu.Drop(e);
                MenuManager.Instance.QueueOpenMenu(Res.HomeMenu);
            }
        }

        internal void DisposeTrayIcon()
        {
            _trayIcon.Dispose();
        }
    }
}
