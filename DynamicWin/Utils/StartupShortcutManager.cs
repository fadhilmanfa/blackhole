using System;
using System.Diagnostics;
using System.IO;

namespace DynamicWin.Utils
{
    public class StartupShortcutManager
    {
        private static string GetStartupFolderPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        }

        private static string GetShortcutPath(string shortcutName)
        {
            return Path.Combine(GetStartupFolderPath(), $"{shortcutName}.lnk");
        }

        public static void CreateShortcut()
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;
            string appPath = Path.GetFileName(exePath);
            string shortcutPath = GetShortcutPath(appPath);

            if (File.Exists(shortcutPath))
            {
                Console.WriteLine("Shortcut already exists.");
                return;
            }

            // Late-bound COM, no IWshRuntimeLibrary reference needed (dotnet CLI compatible)
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("WScript.Shell not available.");
            object? shell = Activator.CreateInstance(shellType);
            if (shell == null) throw new InvalidOperationException("Failed to create WScript.Shell.");
            object? shortcut = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            if (shortcut == null) throw new InvalidOperationException("Failed to create shortcut.");
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exePath) ?? "" });
            shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "Launches the app on system startup." });
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);

            Console.WriteLine("Shortcut created successfully.");
        }

        public static bool RemoveShortcut()
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;
            string appPath = Path.GetFileName(exePath);
            string shortcutPath = GetShortcutPath(appPath);

            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                Console.WriteLine("Shortcut removed successfully.");
                return true;
            }

            Console.WriteLine("Shortcut does not exist.");
            return false;
        }
    }
}
