using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using GlueyKeys.Models;
using GlueyKeys.Services;
using GlueyKeys.Views;

namespace GlueyKeys;

public partial class App : Application
{
    private const ushort VK_BACK = 0x08;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_PAUSE = 0x13;
    private const ushort VK_CAPITAL = 0x14;
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_PRIOR = 0x21;
    private const ushort VK_NEXT = 0x22;
    private const ushort VK_END = 0x23;
    private const ushort VK_HOME = 0x24;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_SNAPSHOT = 0x2C;
    private const ushort VK_INSERT = 0x2D;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_NUMPAD0 = 0x60;
    private const ushort VK_NUMPAD9 = 0x69;
    private const ushort VK_MULTIPLY = 0x6A;
    private const ushort VK_DIVIDE = 0x6F;
    private const ushort VK_F1 = 0x70;
    private const ushort VK_F24 = 0x87;
    private const ushort VK_NUMLOCK = 0x90;
    private const ushort VK_SCROLL = 0x91;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_OEM_1 = 0xBA;
    private const ushort VK_OEM_102 = 0xE2;

    private static Mutex? _mutex;
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;

    // Services
    public SettingsService SettingsService { get; } = new();
    public KeyboardEnumerationService KeyboardEnumerationService { get; } = new();
    public LanguageSwitcherService LanguageSwitcherService { get; } = new();
    public NotificationService NotificationService { get; } = new();
    public StartupService StartupService { get; } = new();
    public RawInputService RawInputService { get; } = new();
    public InstallationService InstallationService { get; } = new();
    public UpdateService UpdateService { get; } = new();

    // State
    private string? _lastKeyboardDeviceId;
    private bool _isCheckingForUpdates;
    private PressKeyPromptWindow? _pressKeyPrompt;

    // Commands
    public ICommand ShowSettingsCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand UninstallCommand { get; }

    public App()
    {
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        CheckForUpdatesCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(true));
        ExitCommand = new RelayCommand(ExitApplication);
        UninstallCommand = new RelayCommand(UninstallApplication);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Handle --uninstall argument (called from Windows "Installed Apps")
        if (e.Args.Length > 0 && e.Args[0] == "--uninstall")
        {
            PerformSilentUninstall();
            Shutdown();
            return;
        }

        // Single instance check
        const string mutexName = "GlueyKeys_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var updatedVersion = GetUpdatedVersion(e.Args);

        // Load settings
        SettingsService.Load();

        if (InstallationService.IsInstalledInProperLocation())
        {
            InstallationService.RegisterInWindows();
            InstallationService.CreateStartMenuShortcut();
        }

        // Show setup wizard on first run (only if not installed via installer)
        if (!SettingsService.Settings.SetupCompleted)
        {
            var wizard = new SetupWizardWindow();
            if (wizard.ShowDialog() == true)
            {
                // Install the application to proper location
                if (!InstallationService.InstallApplication())
                {
                    MessageBox.Show(
                        "GlueyKeys could not install itself to your user programs folder.",
                        "Installation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown();
                    return;
                }

                // Create shortcuts if requested
                if (wizard.CreateDesktopShortcut)
                {
                    InstallationService.CreateDesktopShortcut();
                }
                InstallationService.CreateStartMenuShortcut();

                // Apply wizard settings
                SettingsService.UpdateSettings(s =>
                {
                    s.SetupCompleted = true;
                    s.FirstRunPromptShown = false;
                    s.RunAtStartup = wizard.RunAtStartup;
                    s.ShowNotifications = wizard.ShowNotifications;
                });

                // Apply startup setting (uses installed path)
                StartupService.SetStartup(wizard.RunAtStartup);

                NotificationService.Enabled = wizard.ShowNotifications;

                if (!InstallationService.IsInstalledInProperLocation())
                {
                    RestartFromInstalledLocation();
                    return;
                }

                // Show "press key" prompt after setup
                SettingsService.UpdateSettings(s => s.FirstRunPromptShown = true);
                _pressKeyPrompt = new PressKeyPromptWindow();
                _pressKeyPrompt.Show();
            }
            else
            {
                // User closed the wizard without finishing - exit
                Shutdown();
                return;
            }
        }
        else
        {
            NotificationService.Enabled = SettingsService.Settings.ShowNotifications;

            // Show "press key" prompt on first run after installer
            if (!SettingsService.Settings.FirstRunPromptShown)
            {
                SettingsService.UpdateSettings(s => s.FirstRunPromptShown = true);
                _pressKeyPrompt = new PressKeyPromptWindow();
                _pressKeyPrompt.Show();
            }
        }

        // Enumerate keyboards
        KeyboardEnumerationService.EnumerateKeyboards();

        // Create hidden window for raw input
        _mainWindow = new MainWindow();
        _mainWindow.Hide();

        // Initialize raw input
        RawInputService.Initialize(_mainWindow);
        RawInputService.KeyboardInput += OnKeyboardInput;

        // Setup tray icon
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.DataContext = this;

        if (!string.IsNullOrWhiteSpace(updatedVersion))
        {
            NotificationService.ShowUpdateInstalled(updatedVersion);
        }

        _ = CheckForUpdatesAsync(false);
    }

    private void OnKeyboardInput(object? sender, KeyboardInputEventArgs e)
    {
        var keyboard = KeyboardEnumerationService.GetDeviceByHandle(e.DeviceHandle);
        if (keyboard == null)
        {
            // Re-enumerate and try again
            KeyboardEnumerationService.EnumerateKeyboards();
            keyboard = KeyboardEnumerationService.GetDeviceByHandle(e.DeviceHandle);
        }

        if (keyboard == null)
            return;

        // Mark this keyboard as active (it's definitely connected since it sent input)
        KeyboardEnumerationService.MarkAsActive(keyboard.DeviceId);

        if (!ShouldHandleLayoutSwitch(e.VirtualKey))
            return;

        // Check if we have a mapping for this keyboard
        var mapping = SettingsService.Settings.GetMapping(keyboard.DeviceId);

        if (mapping == null || !mapping.HasLayout)
        {
            // Only show picker once per keyboard (not on every keypress)
            if (_lastKeyboardDeviceId != keyboard.DeviceId)
            {
                _lastKeyboardDeviceId = keyboard.DeviceId;
                Dispatcher.Invoke(() => ShowLayoutPicker(keyboard));
            }
        }
        else if (mapping.IsEnabled)
        {
            _lastKeyboardDeviceId = keyboard.DeviceId;

            // Always enforce the mapped layout
            var currentLayoutId = LanguageSwitcherService.GetCurrentLayoutId();
            if (!string.Equals(currentLayoutId, mapping.LayoutId, StringComparison.OrdinalIgnoreCase))
            {
                if (LanguageSwitcherService.SwitchToLayout(mapping.LayoutId!))
                {
                    if (NotificationService.Enabled)
                    {
                        var layoutInfo = LanguageSwitcherService.GetLayoutById(mapping.LayoutId!);
                        var layoutName = layoutInfo?.DisplayName ?? mapping.LayoutId!;
                        NotificationService.ShowLanguageSwitched(layoutName, keyboard.DisplayName);
                    }
                }
            }
        }
    }

    private static bool ShouldHandleLayoutSwitch(ushort virtualKey)
    {
        if (IsWindowsKeyDown())
            return false;

        if (IsNonTextKey(virtualKey))
            return false;

        return IsTypingKey(virtualKey);
    }

    private static bool IsWindowsKeyDown()
    {
        return IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GlueyKeys.Interop.User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool IsNonTextKey(ushort virtualKey)
    {
        return virtualKey is
            VK_BACK or VK_TAB or VK_RETURN or
            VK_SHIFT or VK_CONTROL or VK_MENU or VK_PAUSE or VK_CAPITAL or VK_ESCAPE or
            VK_PRIOR or VK_NEXT or VK_END or VK_HOME or VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN or
            VK_SNAPSHOT or VK_INSERT or VK_DELETE or
            VK_LWIN or VK_RWIN or
            VK_F1 or >= VK_F1 and <= VK_F24 or
            VK_NUMLOCK or VK_SCROLL or
            VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU;
    }

    private static bool IsTypingKey(ushort virtualKey)
    {
        return virtualKey is
            VK_SPACE or
            >= 0x30 and <= 0x39 or
            >= 0x41 and <= 0x5A or
            >= VK_NUMPAD0 and <= VK_NUMPAD9 or
            >= VK_MULTIPLY and <= VK_DIVIDE or
            >= VK_OEM_1 and <= VK_OEM_102;
    }

    private void ShowLayoutPicker(Models.KeyboardDevice keyboard)
    {
        // Close the "press key" prompt if it's open
        if (_pressKeyPrompt != null)
        {
            _pressKeyPrompt.Close();
            _pressKeyPrompt = null;
        }

        var picker = new LayoutPickerWindow(keyboard, LanguageSwitcherService);
        picker.Owner = _mainWindow;
        picker.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedLayoutId))
        {
            SettingsService.UpdateSettings(s => s.SetMapping(
                keyboard.DeviceId,
                keyboard.DisplayName,
                picker.SelectedLayoutId,
                true));

            // Switch to the selected layout
            LanguageSwitcherService.SwitchToLayout(picker.SelectedLayoutId);

            if (NotificationService.Enabled)
            {
                var layoutInfo = LanguageSwitcherService.GetLayoutById(picker.SelectedLayoutId);
                var layoutName = layoutInfo?.DisplayName ?? picker.SelectedLayoutId;
                NotificationService.ShowLanguageSwitched(layoutName, keyboard.DisplayName);
            }
        }
    }

    private void ShowSettings()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.RefreshKeyboards();
        _mainWindow.Show();
        _mainWindow.Activate();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
    }

    private void RestartFromInstalledLocation()
    {
        var installedExePath = InstallationService.GetInstalledExePath();
        if (!File.Exists(installedExePath))
        {
            MessageBox.Show(
                "GlueyKeys was installed, but the installed executable could not be found.",
                "Installation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _trayIcon?.Dispose();
        RawInputService.Dispose();
        NotificationService.ClearNotifications();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;

        Process.Start(new ProcessStartInfo
        {
            FileName = installedExePath,
            WorkingDirectory = Path.GetDirectoryName(installedExePath) ?? string.Empty,
            UseShellExecute = true
        });

        Shutdown();
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_isCheckingForUpdates)
            return;

        if (!manual && !UpdateService.ShouldCheckForUpdates(SettingsService.Settings.LastUpdateCheckUtc))
            return;

        _isCheckingForUpdates = true;

        try
        {
            SettingsService.UpdateSettings(s => s.LastUpdateCheckUtc = DateTime.UtcNow);

            var result = await UpdateService.CheckForUpdatesAsync();
            if (!result.IsUpdateAvailable)
            {
                if (manual)
                {
                    var message = string.IsNullOrEmpty(result.ErrorMessage)
                        ? $"GlueyKeys is up to date.\n\nCurrent version: {UpdateService.CurrentVersion}"
                        : $"Could not check for updates.\n\n{result.ErrorMessage}";

                    MessageBox.Show(
                        message,
                        "GlueyKeys Updates",
                        MessageBoxButton.OK,
                        string.IsNullOrEmpty(result.ErrorMessage) ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }

                return;
            }

            var updateNow = MessageBox.Show(
                $"GlueyKeys {result.LatestVersion} is available.\n\n" +
                $"Current version: {UpdateService.CurrentVersion}\n\n" +
                "Update now? The app will close and restart automatically.",
                "GlueyKeys Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (updateNow != MessageBoxResult.Yes)
                return;

            var downloadedExePath = await UpdateService.DownloadUpdateAsync(result);
            var targetExePath = InstallationService.GetInstalledExePath();

            if (!File.Exists(targetExePath))
            {
                targetExePath = Environment.ProcessPath ?? targetExePath;
            }

            UpdateService.LaunchUpdateInstaller(downloadedExePath, targetExePath, result.LatestVersion ?? "latest");
            ExitApplication();
        }
        catch (Exception ex)
        {
            if (manual)
            {
                MessageBox.Show(
                    $"Could not update GlueyKeys.\n\n{ex.Message}",
                    "GlueyKeys Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private static string? GetUpdatedVersion(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--updated", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private void UninstallApplication()
    {
        // Use Dispatcher to show dialog after context menu closes
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var confirmWindow = new Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = true,
                Topmost = true,
                Left = -1000,
                Top = -1000
            };
            confirmWindow.Show();

            var result = MessageBox.Show(confirmWindow,
                "Are you sure you want to uninstall GlueyKeys?\n\n" +
                "This will:\n" +
                "- Remove the app from Windows startup\n" +
                "- Remove shortcuts and registry entries\n" +
                "- Delete all settings and keyboard mappings\n" +
                "- Delete the program files\n" +
                "- Close the application",
                "Uninstall GlueyKeys",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            confirmWindow.Close();

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Remove from startup
                    StartupService.DisableStartup();

                    // Remove shortcuts and registry entries
                    InstallationService.RemoveDesktopShortcut();
                    InstallationService.RemoveStartMenuShortcut();
                    InstallationService.UnregisterFromWindows();

                    // Delete settings file
                    var settingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "GlueyKeys");

                    if (Directory.Exists(settingsPath))
                    {
                        Directory.Delete(settingsPath, true);
                    }

                    // Clear notifications
                    NotificationService.ClearNotifications();

                    // Schedule deletion of the install folder after exit
                    var installPath = InstallationService.GetInstallPath();
                    if (Directory.Exists(installPath))
                    {
                        var deleteCmd = $"/C timeout /t 2 /nobreak >nul && rmdir /s /q \"{installPath}\"";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = deleteCmd,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            CreateNoWindow = true
                        });
                    }

                    MessageBox.Show(
                        "Uninstall completed!\n\n" +
                        "The application will now close.",
                        "Uninstall Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error during uninstall: {ex.Message}",
                        "Uninstall Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                // Exit the application
                ExitApplication();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        RawInputService.Dispose();
        NotificationService.ClearNotifications();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Shutdown();
    }

    private void PerformSilentUninstall()
    {
        try
        {
            // Kill any running instance
            var currentProcess = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName("GlueyKeys");
            foreach (var process in processes)
            {
                if (process.Id != currentProcess.Id)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }

            // Remove from startup
            StartupService.DisableStartup();

            // Remove shortcuts and registry entries
            InstallationService.RemoveDesktopShortcut();
            InstallationService.RemoveStartMenuShortcut();
            InstallationService.UnregisterFromWindows();

            // Delete settings file
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GlueyKeys");

            if (Directory.Exists(settingsPath))
            {
                Directory.Delete(settingsPath, true);
            }

            // Schedule deletion of the install folder after exit
            var installPath = InstallationService.GetInstallPath();
            if (Directory.Exists(installPath))
            {
                // Use cmd to delete after a delay
                var deleteCmd = $"/C timeout /t 2 /nobreak >nul && rmdir /s /q \"{installPath}\"";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = deleteCmd,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });
            }

            MessageBox.Show(
                "GlueyKeys has been uninstalled successfully.",
                "Uninstall Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error during uninstall: {ex.Message}",
                "Uninstall Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        RawInputService.Dispose();
        base.OnExit(e);
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}
