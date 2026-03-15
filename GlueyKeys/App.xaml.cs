using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using GlueyKeys.Models;
using GlueyKeys.Services;
using GlueyKeys.Views;

namespace GlueyKeys;

public partial class App : Application, INotifyPropertyChanged
{
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

    // State
    private string? _lastKeyboardDeviceId;
    private bool _isEnabled = true;
    private PressKeyPromptWindow? _pressKeyPrompt;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                SettingsService.UpdateSettings(s => s.IsEnabled = value);
                OnPropertyChanged();
            }
        }
    }

    // Commands
    public ICommand ShowSettingsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand UninstallCommand { get; }

    public App()
    {
        ShowSettingsCommand = new RelayCommand(ShowSettings);
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
            MessageBox.Show("GlueyKeys is already running.", "Already Running",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Load settings
        SettingsService.Load();

        // Show setup wizard on first run (only if not installed via installer)
        if (!SettingsService.Settings.SetupCompleted)
        {
            var wizard = new SetupWizardWindow();
            if (wizard.ShowDialog() == true)
            {
                // Install the application to proper location
                InstallationService.InstallApplication();

                // Create shortcuts if requested
                if (wizard.CreateDesktopShortcut)
                {
                    InstallationService.CreateDesktopShortcut();
                }
                if (wizard.CreateStartMenuShortcut)
                {
                    InstallationService.CreateStartMenuShortcut();
                }

                // Apply wizard settings
                SettingsService.UpdateSettings(s =>
                {
                    s.SetupCompleted = true;
                    s.FirstRunPromptShown = true;
                    s.RunAtStartup = wizard.RunAtStartup;
                    s.ShowNotifications = wizard.ShowNotifications;
                    s.IsEnabled = wizard.EnableSwitching;
                });

                // Apply startup setting (uses installed path)
                StartupService.SetStartup(wizard.RunAtStartup);

                _isEnabled = wizard.EnableSwitching;
                NotificationService.Enabled = wizard.ShowNotifications;

                // Show "press key" prompt after setup
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
            _isEnabled = SettingsService.Settings.IsEnabled;
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
    }

    private void OnKeyboardInput(object? sender, KeyboardInputEventArgs e)
    {
        if (!_isEnabled)
            return;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
