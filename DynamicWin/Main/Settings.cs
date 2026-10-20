using DynamicWin.Resources;
using DynamicWin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace DynamicWin.Main
{
    public class Settings
    {
        private static bool allowBlur;
        private static bool allowAnimation;
        private static bool antiAliasing;
        private static bool runOnStartup;
        private static int theme;
        private static int activeScreenIndex;

        public static bool AllowBlur { get => allowBlur; set => allowBlur = value; }
        public static bool AllowAnimation { get => allowAnimation; set => allowAnimation = value; }
        public static bool AntiAliasing { get => antiAliasing; set => antiAliasing = value; }
        public static bool RunOnStartup { get => runOnStartup; set => runOnStartup = value; }
        public static int Theme { get => theme; set => theme = value; }
        public static int ScreenIndex { get => activeScreenIndex; set => activeScreenIndex = value; }

        public static void InitializeSettings()
        {
            try
            {

                if (SaveManager.Contains("settings"))
                {
                    AllowBlur = (bool)SaveManager.Get("settings.allowblur");
                    AllowAnimation = (bool)SaveManager.Get("settings.allowanimtion");
                    AntiAliasing = (bool)SaveManager.Get("settings.antialiasing");
                    RunOnStartup = (bool)SaveManager.Get("settings.runonstartup");

                    Theme = (int)((Int64)SaveManager.Get("settings.theme"));
                    ScreenIndex = (int)((Int64)SaveManager.Get("settings.screenindex"));
                }
                else
                {
                    AllowBlur = true;
                    AllowAnimation = true;
                    AntiAliasing = true;

                    Settings.Theme = 0;

                    SaveManager.SaveData.Add("settings", 1);
                }


                // This must be run after loading all settings
                AfterSettingsLoaded();
            }catch(Exception e)
            {
                MessageBox.Show("An error occured trying to load the settings. Please revert back to the default settings by deleting the \"Settings.json\" file located under \"%appdata%/DynamicWin/\".");

                AfterSettingsLoaded();
            }
        }

        static void AfterSettingsLoaded()
        {
            DynamicWin.Utils.Theme.Instance.UpdateTheme();

            new PopupOptions().LoadSettings();
        }

        public static void Save()
        {
            SaveManager.Add("settings.allowblur", AllowBlur);
            SaveManager.Add("settings.allowanimtion", AllowAnimation);
            SaveManager.Add("settings.antialiasing", AntiAliasing);
            SaveManager.Add("settings.runonstartup", RunOnStartup);

            SaveManager.Add("settings.theme", Theme);
            SaveManager.Add("settings.screenindex", ScreenIndex);

            SaveManager.SaveAll();
        }
    }
}
