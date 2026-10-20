using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace DynamicWin.UI.Menu.Menus
{
    public class SettingsMenu : BaseMenu
    {
        const float SidePadding = 24f;
        const float ContentTop = 20f;
        const float FooterHeight = 82f;
        const float BottomPadding = 18f;

        const float SectionHeaderHeight = 22f;
        const float FieldLabelHeight = 15f;
        const float ControlHeight = 28f;
        const float CheckboxRowHeight = 30f;
        const float CheckboxSize = 24f;
        const float ColumnGap = 16f;

        const float SaveButtonWidth = 240f;
        const float SaveButtonHeight = 38f;

        readonly Action<MouseWheelEventArgs> scrollHandler;
        bool scrollSubscribed = true;

        float yScrollOffset;
        float ySmoothScroll;
        float contentHeight;
        float scrollRange;

        readonly List<SettingsRow> rows = new List<SettingsRow>();

        Checkbox allowBlur;
        Checkbox allowAnimation;
        Checkbox antiAliasing;
        Checkbox runOnStartup;
        Checkbox volumePopup;
        Checkbox brightnessPopup;

        UIObject topMask;
        UIObject footerBackground;
        UIObject footerDivider;
        UIObject scrollThumb;
        DWTextButton saveButton;

        bool changedTheme;

        public SettingsMenu()
        {
            scrollHandler = (MouseWheelEventArgs x) =>
            {
                yScrollOffset += x.Delta * 0.25f;
            };

            MainForm.onScrollEvent += scrollHandler;
        }

        void UnsubscribeScroll()
        {
            if (!scrollSubscribed) return;

            MainForm.onScrollEvent -= scrollHandler;
            scrollSubscribed = false;
        }

        void SaveAndBack()
        {
            UnsubscribeScroll();

            Settings.AllowBlur = allowBlur.IsChecked;
            Settings.AllowAnimation = allowAnimation.IsChecked;
            Settings.AntiAliasing = antiAliasing.IsChecked;
            Settings.RunOnStartup = runOnStartup.IsChecked;

            PopupOptions.saveData.volumePopup = volumePopup.IsChecked;
            PopupOptions.saveData.brightnessPopup = brightnessPopup.IsChecked;

            DynamicWinMain.UpdateStartup();

            new PopupOptions().SaveSettings();
            Settings.Save();

            if (changedTheme)
                Theme.Instance.UpdateTheme(true);
            else
            {
                Res.HomeMenu = new HomeMenu();
                MenuManager.OpenMenu(Res.HomeMenu);
            }
        }

        public override List<UIObject> InitializeMenu(IslandObject island)
        {
            var objects = base.InitializeMenu(island);

            rows.Clear();
            yScrollOffset = 0f;
            ySmoothScroll = 0f;
            changedTheme = false;

            new PopupOptions().LoadSettings();

            float contentWidth = IslandSize().X - SidePadding * 2f;
            float columnWidth = (contentWidth - ColumnGap) / 2f;
            float secondColumn = columnWidth + ColumnGap;

            // General -------------------------------------------------------

            AddSectionHeader(objects, island, "General", 0f);

            AddLabelRow(objects, island, "Selected Monitor", 12f);

            var selectedMonitors = new string[MainForm.GetMonitorCount()];
            for (int i = 0; i < selectedMonitors.Length; i++)
                selectedMonitors[i] = (i == 0) ? "Primary" : "Monitor " + i;

            int monitorColumns = Math.Max(1, Math.Min(2, selectedMonitors.Length));
            var selectedMonitor = new MultiSelectionButton(island, selectedMonitors, Vec2.zero, new Vec2(contentWidth, ControlHeight), UIAlignment.TopLeft, monitorColumns);
            selectedMonitor.Anchor = Vec2.zero;
            selectedMonitor.SelectedIndex = Math.Clamp(Settings.ScreenIndex, 0, MainForm.GetMonitorCount() - 1);
            selectedMonitor.onClick += (index) =>
            {
                Settings.ScreenIndex = index;
                MainForm.Instance.SetMonitor(Settings.ScreenIndex);
            };

            AddRow(objects, selectedMonitor.Size.Y, 6f, new SettingsRowItem(selectedMonitor, 0f, 0f));

            AddLabelRow(objects, island, "Themes", 14f);

            var themeOptions = new string[] { "Custom", "Dark", "Light", "Candy", "Forest Dawn", "Sunset Glow" };
            var theme = new MultiSelectionButton(island, themeOptions, Vec2.zero, new Vec2(contentWidth, ControlHeight), UIAlignment.TopLeft, 3);
            theme.Anchor = Vec2.zero;
            theme.SelectedIndex = Settings.Theme + 1;
            theme.onClick += (index) =>
            {
                Settings.Theme = index - 1;
                changedTheme = true;
            };

            AddRow(objects, theme.Size.Y, 6f, new SettingsRowItem(theme, 0f, 0f));

            AddDivider(objects, island, contentWidth, 20f);

            // Behavior ------------------------------------------------------

            AddSectionHeader(objects, island, "Behavior", 16f);

            allowBlur = CreateCheckbox(island, "Blur", Settings.AllowBlur, columnWidth);
            allowAnimation = CreateCheckbox(island, "Animations", Settings.AllowAnimation, columnWidth);
            antiAliasing = CreateCheckbox(island, "Anti-aliasing", Settings.AntiAliasing, columnWidth);
            runOnStartup = CreateCheckbox(island, "Start on login", Settings.RunOnStartup, columnWidth);

            AddRow(objects, CheckboxRowHeight, 12f,
                CheckboxItem(allowBlur, 0f),
                CheckboxItem(allowAnimation, secondColumn));
            AddRow(objects, CheckboxRowHeight, 8f,
                CheckboxItem(antiAliasing, 0f),
                CheckboxItem(runOnStartup, secondColumn));

            AddDivider(objects, island, contentWidth, 20f);

            // Pop-up options -----------------------------------------------

            AddSectionHeader(objects, island, "Pop-up Options", 16f);

            volumePopup = CreateCheckbox(island, "Volume pop-up", PopupOptions.saveData.volumePopup, columnWidth);
            brightnessPopup = CreateCheckbox(island, "Brightness pop-up", PopupOptions.saveData.brightnessPopup, columnWidth);

            AddRow(objects, CheckboxRowHeight, 12f,
                CheckboxItem(volumePopup, 0f),
                CheckboxItem(brightnessPopup, secondColumn));

            AddDivider(objects, island, contentWidth, 20f);

            // About ---------------------------------------------------------

            AddSectionHeader(objects, island, "About", 16f);
            AddTextRow(objects, island, "Software version: " + DynamicWinMain.Version, 8f);
            AddTextRow(objects, island, "Made by Florian Butz with ♡", 2f);
            AddTextRow(objects, island, "Maintained and developed by 59xa", 2f);
            AddTextRow(objects, island, "Licensed under the CC BY-SA 4.0 license.", 2f);

            contentHeight = BottomPadding;
            foreach (var row in rows)
                contentHeight += row.GapBefore + row.Height;

            // Fixed chrome --------------------------------------------------

            float menuWidth = IslandSize().X;

            topMask = new UIObject(island, Vec2.zero, new Vec2(menuWidth, ContentTop), UIAlignment.TopLeft)
            {
                Color = Theme.IslandBackground,
                roundRadius = 0f
            };
            topMask.Anchor = Vec2.zero;
            objects.Add(topMask);

            footerBackground = new UIObject(island, Vec2.zero, new Vec2(menuWidth, FooterHeight), UIAlignment.TopLeft)
            {
                Color = Theme.IslandBackground,
                roundRadius = 0f
            };
            footerBackground.Anchor = Vec2.zero;
            objects.Add(footerBackground);

            footerDivider = new UIObject(island, Vec2.zero, new Vec2(menuWidth, 1f), UIAlignment.TopLeft)
            {
                Color = Theme.TextThird.Override(a: 0.3f),
                roundRadius = 0f
            };
            footerDivider.Anchor = Vec2.zero;
            objects.Add(footerDivider);

            saveButton = new DWTextButton(island, "Save changes", Vec2.zero, new Vec2(SaveButtonWidth, SaveButtonHeight), SaveAndBack, UIAlignment.TopLeft)
            {
                roundRadius = 12f,
                normalColor = Theme.Primary.Override(a: 0.9f),
                hoverColor = Theme.Primary,
                clickColor = Theme.Primary.Override(a: 0.7f),
                hoverScaleMulti = new Vec2(1.02f, 1.02f)
            };
            saveButton.Anchor = Vec2.zero;
            saveButton.normalTextSize = 14f;
            saveButton.Text.Font = Res.InterBold;
            saveButton.Text.Color = Theme.TextMain;
            objects.Add(saveButton);

            scrollThumb = new UIObject(island, Vec2.zero, new Vec2(3f, 50f), UIAlignment.TopLeft)
            {
                Color = Theme.TextThird.Override(a: 0.6f),
                roundRadius = 2f
            };
            scrollThumb.Anchor = Vec2.zero;
            objects.Add(scrollThumb);

            return objects;
        }

        Checkbox CreateCheckbox(IslandObject island, string text, bool isChecked, float clickWidth)
        {
            var checkbox = new Checkbox(island, text, Vec2.zero, new Vec2(CheckboxSize, CheckboxSize), null, UIAlignment.TopLeft);
            checkbox.Anchor = Vec2.zero;
            checkbox.IsChecked = isChecked;
            checkbox.Label.TextSize = 13f;
            checkbox.Label.SetInkVerticalOffset();
            checkbox.Label.Color = Theme.TextSecond;
            checkbox.interactionWidth = clickWidth;
            checkbox.expandInteractionRect = 2;
            checkbox.roundRadius = 6f;
            checkbox.normalColor = Theme.IconColor.Override(a: 0.10f);
            checkbox.hoverColor = Theme.IconColor.Override(a: 0.18f);
            checkbox.clickColor = Theme.IconColor.Override(a: 0.30f);
            return checkbox;
        }

        SettingsRowItem CheckboxItem(Checkbox checkbox, float x)
        {
            return new SettingsRowItem(checkbox, x, (CheckboxRowHeight - CheckboxSize) / 2f);
        }

        void AddSectionHeader(List<UIObject> objects, IslandObject island, string text, float gapBefore)
        {
            var label = CreateText(island, text, 16f, Theme.TextMain, Res.InterBold);
            AddRow(objects, SectionHeaderHeight, gapBefore, new SettingsRowItem(label, 0f, InkOffset(label, SectionHeaderHeight)));
        }

        void AddLabelRow(List<UIObject> objects, IslandObject island, string text, float gapBefore)
        {
            var label = CreateText(island, text, 12f, Theme.TextSecond, Res.InterRegular);
            AddRow(objects, FieldLabelHeight, gapBefore, new SettingsRowItem(label, 0f, InkOffset(label, FieldLabelHeight)));
        }

        void AddTextRow(List<UIObject> objects, IslandObject island, string text, float gapBefore)
        {
            var label = CreateText(island, text, 12f, Theme.TextThird, Res.InterRegular);
            AddRow(objects, 16f, gapBefore, new SettingsRowItem(label, 0f, InkOffset(label, 16f)));
        }

        void AddDivider(List<UIObject> objects, IslandObject island, float width, float gapBefore)
        {
            var divider = new UIObject(island, Vec2.zero, new Vec2(width, 1f), UIAlignment.TopLeft)
            {
                Color = Theme.TextThird.Override(a: 0.25f),
                roundRadius = 0f
            };

            AddRow(objects, 1f, gapBefore, new SettingsRowItem(divider, 0f, 0f));
        }

        DWText CreateText(IslandObject island, string text, float textSize, Col color, SKTypeface font)
        {
            var label = new DWText(island, text, Vec2.zero, UIAlignment.TopLeft)
            {
                Font = font,
                TextSize = textSize,
                Color = color,
                Anchor = Vec2.zero
            };

            label.Size = label.GetBoundsForString(text);
            return label;
        }

        static float InkOffset(DWText label, float rowHeight)
        {
            float textHeight = label.GetBoundsForString(label.Text).Y;

            using var paint = new SKPaint
            {
                Typeface = label.Font,
                TextSize = label.TextSize
            };

            float ascent = -paint.FontMetrics.Ascent;
            return (rowHeight - textHeight) / 2f + ascent;
        }

        void AddRow(List<UIObject> objects, float height, float gapBefore, params SettingsRowItem[] items)
        {
            var row = new SettingsRow
            {
                Height = height,
                GapBefore = gapBefore
            };

            foreach (var item in items)
            {
                item.Object.alignment = UIAlignment.TopLeft;
                item.Object.Anchor = Vec2.zero;

                row.Items.Add(item);
                objects.Add(item.Object);
            }

            rows.Add(row);
        }

        public override void Update()
        {
            base.Update();

            float menuWidth = IslandSize().X;
            float menuHeight = IslandSize().Y;
            float contentBottom = menuHeight - FooterHeight;
            float viewportHeight = contentBottom - ContentTop;

            scrollRange = Math.Max(0f, contentHeight - viewportHeight);
            yScrollOffset = Mathf.Clamp(yScrollOffset, -scrollRange, 0f);
            ySmoothScroll = Mathf.Lerp(ySmoothScroll, yScrollOffset, 12f * RendererMain.Instance.DeltaTime);

            if (Math.Abs(ySmoothScroll - yScrollOffset) < 0.05f)
                ySmoothScroll = yScrollOffset;

            float y = ContentTop + ySmoothScroll;

            foreach (var row in rows)
            {
                y += row.GapBefore;

                bool hidden = (y + row.Height <= ContentTop) || (y >= contentBottom);

                foreach (var item in row.Items)
                {
                    item.Object.LocalPosition = new Vec2(SidePadding + item.X, y + item.YOffset);
                    item.Object.SilentSetActive(!hidden);
                }

                y += row.Height;
            }

            float footerTop = menuHeight - FooterHeight;

            topMask.LocalPosition = Vec2.zero;
            footerBackground.LocalPosition = new Vec2(0f, footerTop);
            footerDivider.LocalPosition = new Vec2(0f, footerTop);
            saveButton.LocalPosition = new Vec2((menuWidth - SaveButtonWidth) / 2f, footerTop + (FooterHeight - SaveButtonHeight) / 2f);

            if (scrollRange <= 0.5f)
            {
                scrollThumb.SilentSetActive(false);
            }
            else
            {
                scrollThumb.SilentSetActive(true);

                float trackHeight = contentBottom - ContentTop;
                float thumbHeight = Math.Max(36f, trackHeight * Mathf.Clamp(trackHeight / contentHeight, 0f, 1f));
                float progress = -ySmoothScroll / scrollRange;

                scrollThumb.Size = new Vec2(3f, thumbHeight);
                scrollThumb.LocalPosition = new Vec2(menuWidth - 12f, ContentTop + (trackHeight - thumbHeight) * progress);
            }
        }

        public override Vec2 IslandSize()
        {
            return new Vec2(525, 425);
        }

        public override Vec2 IslandSizeBig()
        {
            return IslandSize() + 5;
        }

        public override Col IslandBorderColor()
        {
            return new Col(0, 0, 0, 0);
        }

        public override void OnDeload()
        {
            UnsubscribeScroll();
            base.OnDeload();
        }

        class SettingsRow
        {
            public float Height;
            public float GapBefore;
            public readonly List<SettingsRowItem> Items = new List<SettingsRowItem>();
        }

        class SettingsRowItem
        {
            public readonly UIObject Object;
            public readonly float X;
            public readonly float YOffset;

            public SettingsRowItem(UIObject obj, float x, float yOffset)
            {
                Object = obj;
                X = x;
                YOffset = yOffset;
            }
        }
    }

    public class Checkbox : DWImageButton
    {
        bool isChecked = false;
        public bool IsChecked { get { return isChecked; } set => SetChecked(value); }

        public DWText Label { get; }

        // Optional wider hit area so the whole label row can toggle the box.
        public float interactionWidth = 0f;

        void SetChecked(bool isChecked)
        {
            this.isChecked = isChecked;
            Image.Image = isChecked ? Res.Check : null;
        }

        public Checkbox(UIObject? parent, string buttonText, Vec2 position, Vec2 size, Action clickCallback, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, Res.Check, position, size, clickCallback, alignment)
        {
            Label = new DWText(this, buttonText, new Vec2(15, 0), UIAlignment.MiddleRight);
            Label.Color = Theme.TextSecond;
            Label.Anchor.X = 0;
            Label.TextSize = size.Y / 1.5f;
            Label.SetInkVerticalOffset();
            AddLocalObject(Label);

            SetChecked(false);

            hoverScaleMulti = Vec2.one;
            clickScaleMulti = Vec2.one;
        }

        public override SKRoundRect GetInteractionRect()
        {
            float width = interactionWidth > 0f ? interactionWidth : Size.X;
            var rect = SKRect.Create((int)Position.X, (int)Position.Y, (int)width, (int)Size.Y);
            var r = new SKRoundRect(rect, roundRadius);
            r.Deflate(-expandInteractionRect, -expandInteractionRect);
            return r;
        }

        public override void OnMouseUp()
        {
            IsChecked = !IsChecked;
            base.OnMouseUp();
        }
    }

    public class MultiSelectionButton : UIObject
    {
        string[] options;
        DWTextButton[] buttons;

        public Action<int> onClick;

        int selectedIndex = 0;
        public int SelectedIndex { get => selectedIndex; set => SetSelected(value); }

        public MultiSelectionButton(UIObject? parent, string[] options, Vec2 position, Vec2 size, UIAlignment alignment = UIAlignment.TopCenter, int maxInOneRow = 4, float rowHeight = 28f, float rowGap = 8f) : base(parent, position, size, alignment)
        {
            this.options = options;
            this.buttons = new DWTextButton[options.Length];

            float itemWidth = (size.X - rowGap * (maxInOneRow - 1)) / maxInOneRow;
            int rows = Math.Max(1, (int)Math.Ceiling(options.Length / (float)maxInOneRow));

            for (int i = 0; i < options.Length; i++)
            {
                var lambdaIndex = i;
                var action = () => { OnClick(lambdaIndex); };

                int row = i / maxInOneRow;
                int column = i % maxInOneRow;

                var btn = new DWTextButton(this, options[i], new Vec2(column * (itemWidth + rowGap), row * (rowHeight + rowGap)), new Vec2(itemWidth, rowHeight), action, UIAlignment.TopLeft);
                btn.Anchor = Vec2.zero;
                btn.roundRadius = 7f;
                btn.normalTextSize = 13f;
                btn.hoverScaleMulti = new Vec2(1.02f, 1f);
                btn.clickScaleMulti = new Vec2(0.99f, 1f);
                btn.Text.Color = Theme.TextSecond;
                btn.Text.TextSize = 13f;
                buttons[i] = btn;

                AddLocalObject(btn);
            }

            Size.Y = rows * rowHeight + (rows - 1) * rowGap;

            SelectedIndex = 0;
        }

        public override void Draw(SKCanvas canvas)
        {
        }

        void SetSelected(int index)
        {
            if (buttons.Length == 0) return;

            selectedIndex = Math.Clamp(index, 0, buttons.Length - 1);

            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                bool selected = i == selectedIndex;

                button.normalColor = selected
                    ? Theme.Primary.Override(a: 0.85f)
                    : Theme.IconColor.Override(a: 0.10f);
                button.hoverColor = selected
                    ? Theme.Primary.Override(a: 0.95f)
                    : Theme.IconColor.Override(a: 0.18f);
                button.clickColor = selected
                    ? Theme.Primary.Override(a: 0.70f)
                    : Theme.IconColor.Override(a: 0.30f);

                button.Text.Color = selected ? Theme.TextMain : Theme.TextSecond;
            }
        }

        void OnClick(int index)
        {
            SelectedIndex = index;
            onClick?.Invoke(index);
        }
    }
}
