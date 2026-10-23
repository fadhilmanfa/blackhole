using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using DynamicWin.Resources;
using DynamicWin.UI;
using DynamicWin.UI.Menu;
using DynamicWin.UI.Menu.Menus;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using DynamicWin.WPFBinders;
using SkiaSharp;

namespace DynamicWin.Main
{
    public class RendererMain : SKElement
    {
        private IslandObject islandObject;
        public IslandObject MainIsland => islandObject;
        private List<UIObject> objects => MenuManager.Instance.ActiveMenu.UiObjects;

        public static Vec2 ScreenDimensions => new Vec2(MainForm.Instance.Width, MainForm.Instance.Height);
        public static Vec2 CursorPosition => new Vec2(Mouse.GetPosition(MainForm.Instance).X, Mouse.GetPosition(MainForm.Instance).Y);

        private static RendererMain instance;
        public static RendererMain Instance => instance;

        public Vec2 renderOffset = Vec2.zero;
        public Vec2 scaleOffset = Vec2.one;
        public float blurOverride = 0f;
        public float alphaOverride = 1f;

        public Action<float> onUpdate;
        public Action<SKCanvas> onDraw;

        private Stopwatch? updateStopwatch;
        private int initialScreenBrightness = 0;
        private float deltaTime = 0f;
        public float DeltaTime => deltaTime;

        private bool isInitialized = false;
        public int canvasWithoutClip;
        private GRContext Context;

        public RendererMain()
        {
            MenuManager m = new MenuManager();
            instance = this;
            islandObject = new IslandObject();
            m.Init();

            initialScreenBrightness = BrightnessAdjustMenu.GetBrightness();
            KeyHandler.onKeyDown += OnKeyRegistered;
            KeyHandler.onKeyUp += OnKeyReleased;

            MainForm.Instance.DragEnter += MainForm.Instance.MainForm_DragEnter;
            MainForm.Instance.DragLeave += MainForm.Instance.MainForm_DragLeave;
            MainForm.Instance.Drop += MainForm.Instance.OnDrop;
            MainForm.Instance.MouseWheel += MainForm.Instance.OnScroll;

            // Get refresh rate
            int refreshRate = GetRefreshRate();
            Debug.WriteLine($"Monitor Refresh Rate: {refreshRate} Hz");

            CompositionTarget.Rendering += OnRendering;

            isInitialized = true;
        }

        public void Destroy()
        {
            CompositionTarget.Rendering -= OnRendering;
            // if (fallbackTimer != null) fallbackTimer.Stop();

            KeyHandler.onKeyDown -= OnKeyRegistered;
            KeyHandler.onKeyUp -= OnKeyReleased;
            MainForm.Instance.DragEnter -= MainForm.Instance.MainForm_DragEnter;
            MainForm.Instance.DragLeave -= MainForm.Instance.MainForm_DragLeave;
            MainForm.Instance.MouseWheel -= MainForm.Instance.OnScroll;

            instance = null;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            Update();
            Render();
        }

        // Hide-toggle is armed on Ctrl+Win keydown only when those are the ONLY
        // keys held, and fires on LWin release. Any other key in between
        // (e.g. Arrow for virtual-desktop switching) disarms it.
        private bool hideToggleArmed = false;

        private void OnKeyRegistered(Keys key, KeyModifier modifier)
        {
            if (key == Keys.LWin && modifier.isCtrlDown && IsOnlyCtrlWinDown())
            {
                hideToggleArmed = true;
            }
            else if (hideToggleArmed && key != Keys.LWin
                && key != Keys.LControlKey && key != Keys.RControlKey && key != Keys.RWin)
            {
                hideToggleArmed = false;
            }

            if ((key == Keys.VolumeDown || key == Keys.VolumeMute || key == Keys.VolumeUp) && PopupOptions.saveData.volumePopup)
            {
                // Volume/mute is an activity of the home island now, not a
                // separate menu. The activity stack handles its lifetime.
                if (MenuManager.Instance.ActiveMenu is HomeMenu homeMenu)
                    homeMenu.NotifyVolumeKeys();
            }
        }

        private void OnKeyReleased(Keys key)
        {
            if (hideToggleArmed && key == Keys.LWin)
            {
                hideToggleArmed = false;
                islandObject.hidden = !islandObject.hidden;
            }
            else if (key == Keys.LControlKey || key == Keys.RControlKey)
            {
                hideToggleArmed = false;
            }
        }

        private static bool IsOnlyCtrlWinDown()
        {
            var kd = KeyHandler.keyDown;
            if (kd.Count != 2 || !kd.Contains(Keys.LWin)) return false;
            return kd.Contains(Keys.LControlKey) || kd.Contains(Keys.RControlKey);
        }

        private void Update()
        {
            if (updateStopwatch != null)
            {
                updateStopwatch.Stop();
                deltaTime = updateStopwatch.ElapsedMilliseconds / 1000f;
            }
            else
            {
                deltaTime = 1f / 1000f;
            }

            updateStopwatch = Stopwatch.StartNew();

            onUpdate?.Invoke(DeltaTime);

            if (BrightnessAdjustMenu.GetBrightness() != initialScreenBrightness && PopupOptions.saveData.brightnessPopup)
            {
                initialScreenBrightness = BrightnessAdjustMenu.GetBrightness();
                if (MenuManager.Instance.ActiveMenu is HomeMenu)
                {
                    MenuManager.OpenOverlayMenu(new BrightnessAdjustMenu(), 100f);
                }
                else if (BrightnessAdjustMenu.timerUntilClose != null)
                {
                    BrightnessAdjustMenu.PressBK();
                    BrightnessAdjustMenu.timerUntilClose = 0f;
                }
            }

            MenuManager.Instance.Update(DeltaTime);

            if (MenuManager.Instance.ActiveMenu != null)
            {
                MenuManager.Instance.ActiveMenu.Update();

                if (MenuManager.Instance.ActiveMenu is DropFileMenu && !MainForm.Instance.isDragging)
                    MenuManager.OpenMenu(Res.HomeMenu);
            }

            islandObject.UpdateCall(DeltaTime);

            if (MainIsland.hidden) return;

            foreach (UIObject uiObject in objects)
            {
                uiObject.UpdateCall(DeltaTime);
            }
        }

        private void Render()
        {
            Dispatcher.Invoke(() => InvalidateVisual());
        }

        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            base.OnPaintSurface(e);

            if (!isInitialized) return;

            SKSurface surface = e.Surface;
            SKCanvas canvas = surface.Canvas;

            canvas.Clear(SKColors.Transparent);

            double dpiFactor = System.Windows.PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
            canvas.Scale((float)dpiFactor, (float)dpiFactor);

            canvasWithoutClip = canvas.Save();

            bool islandHidden = MainIsland.hidden;
            bool hasContextMenu = false;

            // Returning bubbles slide under the island, so they are painted
            // before the island silhouette.
            if (!islandHidden)
            {
                foreach (UIObject uiObject in objects)
                {
                    if (uiObject.drawBehindIsland)
                        hasContextMenu |= DrawMenuObject(canvas, uiObject);
                }
            }

            // Each menu object leaves the canvas in its own save slot; reset
            // to the clean state before painting the island.
            canvas.RestoreToCount(canvasWithoutClip);
            canvasWithoutClip = canvas.Save();

            if (islandObject.maskInToIsland) Mask(canvas);
            islandObject.DrawCall(canvas);

            if (islandHidden) return;

            foreach (UIObject uiObject in objects)
            {
                if (!uiObject.drawBehindIsland)
                    hasContextMenu |= DrawMenuObject(canvas, uiObject);
            }

            onDraw?.Invoke(canvas);

            if (!hasContextMenu) ContextMenu = null;

            canvas.Flush();
        }

        private bool DrawMenuObject(SKCanvas canvas, UIObject uiObject)
        {
            canvas.RestoreToCount(canvasWithoutClip);
            canvasWithoutClip = canvas.Save();

            bool hasContextMenu = false;

            if (uiObject.IsHovering && uiObject.GetContextMenu() != null)
            {
                hasContextMenu = true;
                ContextMenu = uiObject.GetContextMenu();
            }

            foreach (UIObject obj in uiObject.LocalObjects)
            {
                if (obj.IsHovering && obj.GetContextMenu() != null)
                {
                    hasContextMenu = true;
                    ContextMenu = obj.GetContextMenu();
                }
            }

            if (uiObject.maskInToIsland)
                Mask(canvas);

            canvas.Scale(
                scaleOffset.X,
                scaleOffset.Y,
                islandObject.Position.X + islandObject.Size.X / 2,
                islandObject.Position.Y + islandObject.Size.Y / 2);
            canvas.Translate(renderOffset.X, renderOffset.Y);

            uiObject.DrawCall(canvas);
            return hasContextMenu;
        }

        private void Mask(SKCanvas canvas)
        {
            var islandMask = GetMask();
            canvas.ClipRoundRect(islandMask);
        }

        public SKRoundRect GetMask()
        {
            var islandMask = islandObject.GetRect();
            islandMask.Deflate(new SKSize(1, 1));
            return islandMask;
        }

        // Native PInvoke to get monitor refresh rate
        private const int ENUM_CURRENT_SETTINGS = -1;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        private int GetRefreshRate()
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode))
                return (int)devMode.dmDisplayFrequency;
            return 60;
        }
    }
}