using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace GlueyKeys.Services;

public class InstallationService
{
    private const string AppName = "GlueyKeys";
    private const string AppVersion = "1.0.0";
    private const string Publisher = "GlueyKeys";
    private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName;

    public string GetInstallPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            AppName);
    }

    public string GetInstalledExePath()
    {
        return Path.Combine(GetInstallPath(), $"{AppName}.exe");
    }

    public bool IsInstalledInProperLocation()
    {
        var currentExe = Environment.ProcessPath;
        var expectedPath = GetInstalledExePath();
        return string.Equals(currentExe, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    public bool InstallApplication()
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
                return false;

            var installPath = GetInstallPath();
            var targetExe = GetInstalledExePath();

            // Create install directory
            Directory.CreateDirectory(installPath);

            // Copy exe if not already in the right place
            if (!string.Equals(currentExe, targetExe, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExe, targetExe, true);
            }

            // Register in Windows "Installed Apps"
            RegisterInWindows();

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void RegisterInWindows()
    {
        try
        {
            var exePath = GetInstalledExePath();
            var installPath = GetInstallPath();

            using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryKey);
            if (key != null)
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", AppVersion);
                key.SetValue("Publisher", Publisher);
                key.SetValue("InstallLocation", installPath);
                key.SetValue("DisplayIcon", exePath);
                key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                // Estimate size in KB
                var fileInfo = new FileInfo(exePath);
                if (fileInfo.Exists)
                {
                    key.SetValue("EstimatedSize", (int)(fileInfo.Length / 1024), RegistryValueKind.DWord);
                }

                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }
        catch
        {
            // Ignore registration errors
        }
    }

    public void UnregisterFromWindows()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKey(UninstallRegistryKey, false);
        }
        catch
        {
            // Ignore if key doesn't exist
        }
    }

    public bool CreateDesktopShortcut()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var shortcutPath = Path.Combine(desktopPath, $"{AppName}.lnk");
            var targetExe = GetInstalledExePath();

            CreateShortcut(shortcutPath, targetExe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool CreateStartMenuShortcut()
    {
        try
        {
            var startMenuPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs");
            var shortcutPath = Path.Combine(startMenuPath, $"{AppName}.lnk");
            var targetExe = GetInstalledExePath();

            CreateShortcut(shortcutPath, targetExe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RemoveDesktopShortcut()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var shortcutPath = Path.Combine(desktopPath, $"{AppName}.lnk");
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RemoveStartMenuShortcut()
    {
        try
        {
            var startMenuPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs");
            var shortcutPath = Path.Combine(startMenuPath, $"{AppName}.lnk");
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RemoveInstallation()
    {
        try
        {
            RemoveDesktopShortcut();
            RemoveStartMenuShortcut();
            UnregisterFromWindows();

            var installPath = GetInstallPath();
            if (Directory.Exists(installPath))
            {
                // Can't delete running exe, but we can try
                try
                {
                    Directory.Delete(installPath, true);
                }
                catch
                {
                    // Ignore - exe is running
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void CreateShortcut(string shortcutPath, string targetPath)
    {
        var link = (IShellLink)new ShellLink();

        link.SetPath(targetPath);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath)!);
        link.SetDescription(AppName);

        var file = (IPersistFile)link;
        file.Save(shortcutPath, false);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
