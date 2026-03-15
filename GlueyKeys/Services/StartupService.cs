using System.IO;
using Microsoft.Win32;

namespace GlueyKeys.Services;

public class StartupService
{
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GlueyKeys";
    private const string OldAppName = "KeyboardActuallyWorks";

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
            var value = key?.GetValue(AppName);
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    public void EnableStartup()
    {
        try
        {
            // Use the installed path
            var installPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                AppName,
                $"{AppName}.exe");

            // Fall back to current exe if installed path doesn't exist
            var exePath = File.Exists(installPath) ? installPath : Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return;

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            key?.SetValue(AppName, $"\"{exePath}\"");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to enable startup: {ex.Message}");
        }
    }

    public void DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            key?.DeleteValue(AppName, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to disable startup: {ex.Message}");
        }
    }

    public void SetStartup(bool enabled)
    {
        if (enabled)
            EnableStartup();
        else
            DisableStartup();
    }

    /// <summary>
    /// Remove startup entries left behind by the old app name (KeyboardActuallyWorks).
    /// </summary>
    public void CleanupLegacyEntries()
    {
        try
        {
            // Remove old registry Run entry
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            key?.DeleteValue(OldAppName, false);
        }
        catch { }

        try
        {
            // Remove old startup folder shortcut
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var oldShortcut = Path.Combine(startupFolder, $"{OldAppName}.lnk");
            if (File.Exists(oldShortcut))
                File.Delete(oldShortcut);
        }
        catch { }
    }
}
