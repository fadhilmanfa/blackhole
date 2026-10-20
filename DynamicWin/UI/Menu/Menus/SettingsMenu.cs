using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI.UIElements;
using DynamicWin.UI.UIElements.Custom;
using DynamicWin.Utils;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Xml.Linq;
using static DynamicWin.UI.UIElements.IslandObject;
using static System.Net.Mime.MediaTypeNames;

namespace DynamicWin.UI.Menu.Menus
{
    public class SettingsMenu : BaseMenu
    {

        public SettingsMenu()
        {
            MainForm.onScrollEvent += (MouseWheelEventArgs x) => 
            {
                yScrollOffset += x.Delta * 0.25f;
            };
        }

        bool changedTheme = false;

        void SaveAndBack()
        {
            Settings.AllowBlur = allowBlur.IsChecked;
            Settings.AllowAnimation = allowAnimation.IsChecked;
            Settings.AntiAliasing = antiAliasing.IsChecked;
            Settings.RunOnStartup = runOnStartup.IsChecked;

            DynamicWinMain.UpdateStartup();

            if (changedTheme)
                Theme.Instance.UpdateTheme(true);
            else
            {
                Res.HomeMenu = new HomeMenu();
                MenuManager.OpenMenu(Res.HomeMenu);
            }

            new PopupOptions().SaveSettings();

            Settings.Save();
        }

        Checkbox allowBlur;
        Checkbox allowAnimation;
        Checkbox antiAliasing;
        Checkbox runOnStartup;

        public override List<UIObject> InitializeMenu(IslandObject island)
        {
            var objects = base.InitializeMenu(island);

            new PopupOptions().LoadSettings();

            var generalTitle = new DWText(island, "General", new Vec2(25, 0), UIAlignment.TopLeft);
            generalTitle.Font = Res.InterBold;
            generalTitle.Anchor.X = 0;
            objects.Add(generalTitle);

            {
                var islandModesTitle = new DWText(island, "Island Mode", new Vec2(25, 0), UIAlignment.TopLeft);
                islandModesTitle.Font = Res.InterRegular;
                islandModesTitle.Color = Theme.TextSecond;
                islandModesTitle.TextSize = 15;
                islandModesTitle.Anchor.X = 0;
                objects.Add(islandModesTitle);

                var islandModes = new string[] { "Island", "Notch" };
                var islandMode = new MultiSelectionButton(island, islandModes, new Vec2(25, 0), new Vec2(IslandSize().X - 50, 25), UIAlignment.TopLeft);
                islandMode.SelectedIndex = (Settings.IslandMode == IslandObject.IslandMode.Island) ? 0 : 1;
                islandMode.Anchor.X = 0;
                islandMode.onClick += (index) =>
                {
                    Settings.IslandMode = (index == 0) ? IslandObject.IslandMode.Island : IslandObject.IslandMode.Notch;
                };
                objects.Add(islandMode);
            }

            allowBlur = new Checkbox(island, "Toggle blur", new Vec2(25, 0), new Vec2(25, 25), () => { }, UIAlignment.TopLeft);
            allowBlur.IsChecked = Settings.AllowBlur;
            allowBlur.Anchor.X = 0;
            objects.Add(allowBlur);

            allowAnimation = new Checkbox(island, "Toggle animations", new Vec2(25, 0), new Vec2(25, 25), () => { }, UIAlignment.TopLeft);
            allowAnimation.IsChecked = Settings.AllowAnimation;
            allowAnimation.Anchor.X = 0;
            objects.Add(allowAnimation);

            antiAliasing = new Checkbox(island, "Toggle anti-aliasing", new Vec2(25, 0), new Vec2(25, 25), () => { }, UIAlignment.TopLeft);
            antiAliasing.IsChecked = Settings.AntiAliasing;
            antiAliasing.Anchor.X = 0;
            objects.Add(antiAliasing);

            runOnStartup = new Checkbox(island, "Start application on login", new Vec2(25, 0), new Vec2(25, 25), () => { }, UIAlignment.TopLeft);
            runOnStartup.IsChecked = Settings.RunOnStartup;
            runOnStartup.Anchor.X = 0;
            objects.Add(runOnStartup);


            {
                var selectedMonitorTitle = new DWText(island, "Selected Monitor", new Vec2(25, 0), UIAlignment.TopLeft);
                selectedMonitorTitle.Font = Res.InterRegular;
                selectedMonitorTitle.TextSize = 15;
                selectedMonitorTitle.Anchor.X = 0;
                objects.Add(selectedMonitorTitle);

                var selectedMonitors = new string[MainForm.GetMonitorCount()];
                for(int i = 0; i < selectedMonitors.Length; i++)
                {
                    if (i == 0) selectedMonitors[i] = "Primary";
                    else selectedMonitors[i] = "Monitor " + i;
                }

                var selectedMonitor = new MultiSelectionButton(island, selectedMonitors, new Vec2(25, 0), new Vec2(IslandSize().X - 50, 25), UIAlignment.TopLeft);
                selectedMonitor.SelectedIndex = Math.Clamp(Settings.ScreenIndex, 0, MainForm.GetMonitorCount() - 1);
                selectedMonitor.Anchor.X = 0;
                selectedMonitor.onClick += (index) =>
                {
                    Settings.ScreenIndex = index;
                    MainForm.Instance.SetMonitor(Settings.ScreenIndex);
                };
                objects.Add(selectedMonitor);
            }

            {
                var themeTitle = new DWText(island, "Themes", new Vec2(25, 0), UIAlignment.TopLeft);
                themeTitle.Font = Res.InterRegular;
                themeTitle.TextSize = 15;
                themeTitle.Anchor.X = 0;
                objects.Add(themeTitle);

                var themeOptions = new string[] { "Custom", "Dark", "Light", "Candy", "Forest Dawn", "Sunset Glow" };
                var theme = new MultiSelectionButton(island, themeOptions, new Vec2(25, 0), new Vec2(IslandSize().X - 50, 25), UIAlignment.TopLeft);
                theme.SelectedIndex = Settings.Theme + 1;
                theme.Anchor.X = 0;
                theme.onClick += (index) =>
                {
                    Settings.Theme = index - 1;
                    changedTheme = true;
                };
                objects.Add(theme);
            }

            AddSettingSection(objects, island, "Pop-up Options", new PopupOptions().SettingsObjects());

            objects.Add(new DWText(island, "Software version: " + DynamicWinMain.Version, new Vec2(25, 0), UIAlignment.TopLeft)
            {
                Color = Theme.TextThird,
                Anchor = new Vec2(0, 0.5f),
                TextSize = 15
            });

            objects.Add(new DWText(island, "Made by Florian Butz with ♡", new Vec2(25, 0), UIAlignment.TopLeft)
            {
                Color = Theme.TextThird,
                Anchor = new Vec2(0, 0.5f),
                TextSize = 15
            });

            objects.Add(new DWText(island, "Maintained and developed by 59xa", new Vec2(25, 0), UIAlignment.TopLeft)
            {
                Color = Theme.TextThird,
                Anchor = new Vec2(0, 0.5f),
                TextSize = 12,
                Font = Resources.Res.InterBold
            });

            objects.Add(new DWText(island, "Licenced under the CC BY-SA 4.0 licence.", new Vec2(25, 0), UIAlignment.TopLeft)
            {
                Color = Theme.TextThird,
                Anchor = new Vec2(0, 0.5f),
                TextSize = 15
            });

            var backBtn = new DWTextButton(island, "Save changes", new Vec2(0, -45), new Vec2(250, 40), () => { SaveAndBack(); }, UIAlignment.BottomCenter)
            {
                roundRadius = 25
            };
            backBtn.Text.Font = Resources.Res.InterBold;

            bottomMask = new UIObject(island, Vec2.zero, new Vec2(IslandSizeBig().X - 225, 175), UIAlignment.BottomCenter)
            {
                Color = Theme.IslandBackground,
                roundRadius = 50
            };

            objects.Add(bottomMask);
            objects.Add(backBtn);

            return objects;
        }

        void AddSettingSection(List<UIObject> objects, IslandObject island, string title, List<UIObject> items)
        {
            var wTitle = new DWText(island, title, new Vec2(25, 0), UIAlignment.TopLeft);
            wTitle.Font = Res.InterRegular;
            wTitle.TextSize = 15;
            wTitle.Anchor.X = 0;
            objects.Add(wTitle);

            foreach (var optionItem in items)
            {
                optionItem.Parent = island;

                if (optionItem.alignment == UIAlignment.TopLeft)
                {
                    optionItem.Position = new Vec2(25, 0);
                    optionItem.Anchor.X = 0;
                }

                if (optionItem is DWText)
                {
                    ((DWText)optionItem).Color = Theme.TextThird;
                    ((DWText)optionItem).Font = Res.InterRegular;
                    ((DWText)optionItem).TextSize = 13;
                }
                else if (optionItem is Checkbox)
                {
                    optionItem.Size = new Vec2(25, 25);
                }

                objects.Add(optionItem);
            }
        }

        UIObject bottomMask;

        float yScrollOffset = 0f;
        float ySmoothScroll = 0f;

        public override void Update()
        {
            base.Update();

            ySmoothScroll = Mathf.Lerp(ySmoothScroll,
                yScrollOffset, 10f * RendererMain.Instance.DeltaTime);

            bottomMask.blurAmount = 15;

            var yScrollLim = 0f;
            var yPos = 35f;
            var spacing = 15f;

            for(int i = 0; i < UiObjects.Count - 2; i++)
            {
                var uiObject = UiObjects[i];
                if (!uiObject.IsEnabled) continue;

                uiObject.LocalPosition.Y = yPos + ySmoothScroll;
                yPos += uiObject.Size.Y + spacing;

                if (yPos > IslandSize().Y - 45) yScrollLim += uiObject.Size.Y + spacing;
            }

            yScrollOffset = Mathf.Lerp(yScrollOffset,
                Mathf.Clamp(yScrollOffset, -yScrollLim, 0f), 15f * RendererMain.Instance.DeltaTime);
        }

        public override Vec2 IslandSize()
        {
            return new Vec2(525, 425);
        }

        public override Vec2 IslandSizeBig()
        {
            return IslandSize() + 5;
        }

        // Border should only be rendered if on island mode instead of notch
        public override Col IslandBorderColor()
        {
            IslandMode mode = Settings.IslandMode; // Reads either Island or Notch as value
            if (mode == IslandMode.Island) return new Col(0.5f, 0.5f, 0.5f);
            else return new Col(0, 0, 0, 0); // Render transparent if island mode is Notch
        }
    }

    public class Checkbox : DWImageButton
    {
        bool isChecked = false;
        public bool IsChecked { get { return isChecked; } set => SetChecked(value); }

        void SetChecked(bool isChecked)
        {
            this.isChecked = isChecked;
            Image.Image = isChecked ? Res.Check : null;
        }

        public Checkbox(UIObject? parent, string buttonText, Vec2 position, Vec2 size, Action clickCallback, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, Res.Check, position, size, clickCallback, alignment)
        {
            var text = new DWText(this, buttonText, new Vec2(15, 0), UIAlignment.MiddleRight);
            text.Color = Theme.TextSecond;
            text.Anchor.X = 0;
            text.TextSize = size.Y / 1.5f;
            AddLocalObject(text);

            SetChecked(false);

            hoverScaleMulti = new Vec2(1.05f, 1f);
            clickScaleMulti = new Vec2(0.975f, 1f);
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

        public MultiSelectionButton(UIObject? parent, string[] options, Vec2 position, Vec2 size, UIAlignment alignment = UIAlignment.TopCenter, int maxInOneRow = 4) : base(parent, position, size, alignment)
        {
            this.options = options;
            this.buttons = new DWTextButton[options.Length];

            float xPos = 0;
            float yPos = 0;
            int counter = 0;

            for(int i = 0; i < options.Length; i++)
            {
                var lambdaIndex = i; // Either I'm going insane or I don't understand lambdas, but it seems like only the pointer given in to the OnClick() method. This is why this line is needed!
                var action = () => { OnClick(lambdaIndex); }; // For some it just outputs the length of options if there is no seperate variable for it.

                if (counter >= maxInOneRow)
                {
                    counter = 0;
                    yPos += 35;
                    xPos = 0;
                }

                var btn = new DWTextButton(this, options[i], new Vec2(xPos, yPos), new Vec2(Math.Max(75, options[i].Length >= 11 ? (options[i].Length * 9) : 0), 25), action, UIAlignment.MiddleLeft);
                btn.Text.Color = Theme.TextSecond;
                btn.Anchor.X = 0;
                buttons[i] = btn;
                
                AddLocalObject(btn);

                xPos += btn.Size.X + 15;
                counter++;
            }

            Size.Y = Size.Y + yPos;

            SelectedIndex = 0;
        }

        public override void Draw(SKCanvas canvas)
        {
        }

        void SetSelected(int index)
        {
            selectedIndex = index;

            foreach (var button in buttons)
            {
                button.normalColor = Theme.Secondary.Override(a: 0.9f);
            }

            buttons[index].normalColor = (Theme.Primary * 0.65f).Override(a: 0.75f);
        }

        void OnClick(int index)
        {
            SelectedIndex = index;

            onClick.Invoke(index);
        }
    }
}
