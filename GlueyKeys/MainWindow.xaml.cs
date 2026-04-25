using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GlueyKeys.Models;
using GlueyKeys.Services;

namespace GlueyKeys;

public partial class MainWindow : Window
{
    private App AppInstance => (App)Application.Current;
    private SettingsService Settings => AppInstance.SettingsService;
    private KeyboardEnumerationService KeyboardService => AppInstance.KeyboardEnumerationService;
    private LanguageSwitcherService LayoutService => AppInstance.LanguageSwitcherService;
    private StartupService StartupService => AppInstance.StartupService;
    private NotificationService NotificationService => AppInstance.NotificationService;

    private List<KeyboardLayoutInfo>? _installedLayouts;
    private bool _isInitializing;
    private bool _hasUnsavedChanges;

    // Pending settings (not saved until Save button is clicked)
    private bool _pendingRunAtStartup;
    private bool _pendingShowNotifications;
    private Dictionary<string, (string? layoutId, bool isEnabled)> _pendingKeyboardChanges = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;

        // Load current settings
        _pendingRunAtStartup = StartupService.IsStartupEnabled();
        _pendingShowNotifications = Settings.Settings.ShowNotifications;

        RunAtStartupCheckBox.IsChecked = _pendingRunAtStartup;
        ShowNotificationsCheckBox.IsChecked = _pendingShowNotifications;

        // Cache layouts
        _installedLayouts = LayoutService.GetInstalledLayouts();

        // Clear pending keyboard changes
        _pendingKeyboardChanges.Clear();
        _hasUnsavedChanges = false;
        SaveButton.IsEnabled = false;

        RefreshKeyboards();

        _isInitializing = false;
    }

    public void RefreshKeyboards()
    {
        KeyboardService.EnumerateKeyboards();
        _installedLayouts ??= LayoutService.GetInstalledLayouts();

        var viewModels = new List<KeyboardViewModel>();

        foreach (var keyboard in KeyboardService.AllDevices)
        {
            var mapping = Settings.Settings.GetMapping(keyboard.DeviceId);

            viewModels.Add(new KeyboardViewModel
            {
                DeviceId = keyboard.DeviceId,
                DisplayName = keyboard.DisplayName,
                IsEnabled = mapping?.IsEnabled ?? true,
                LayoutId = mapping?.LayoutId,
                AvailableLayouts = _installedLayouts
            });
        }

        KeyboardsList.ItemsSource = viewModels;
    }

    private void MarkAsChanged()
    {
        _hasUnsavedChanges = true;
        SaveButton.IsEnabled = true;
    }

    private void RunAtStartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _pendingRunAtStartup = RunAtStartupCheckBox.IsChecked ?? false;
        MarkAsChanged();
    }

    private void ShowNotificationsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _pendingShowNotifications = ShowNotificationsCheckBox.IsChecked ?? true;
        MarkAsChanged();
    }

    private void KeyboardEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        if (sender is CheckBox checkBox && checkBox.Tag is string deviceId)
        {
            var isEnabled = checkBox.IsChecked ?? true;

            // Get current pending state or existing mapping
            if (_pendingKeyboardChanges.TryGetValue(deviceId, out var pending))
            {
                _pendingKeyboardChanges[deviceId] = (pending.layoutId, isEnabled);
            }
            else
            {
                var mapping = Settings.Settings.GetMapping(deviceId);
                _pendingKeyboardChanges[deviceId] = (mapping?.LayoutId, isEnabled);
            }

            MarkAsChanged();
        }
    }

    private void LayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (sender is ComboBox comboBox &&
            comboBox.Tag is string deviceId &&
            comboBox.SelectedValue is string layoutId)
        {
            // Get current pending state or existing mapping
            if (_pendingKeyboardChanges.TryGetValue(deviceId, out var pending))
            {
                _pendingKeyboardChanges[deviceId] = (layoutId, pending.isEnabled);
            }
            else
            {
                var mapping = Settings.Settings.GetMapping(deviceId);
                _pendingKeyboardChanges[deviceId] = (layoutId, mapping?.IsEnabled ?? true);
            }

            MarkAsChanged();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        StartupService.SetStartup(_pendingRunAtStartup);
        Settings.UpdateSettings(s => s.RunAtStartup = _pendingRunAtStartup);

        NotificationService.Enabled = _pendingShowNotifications;
        Settings.UpdateSettings(s => s.ShowNotifications = _pendingShowNotifications);

        // Apply keyboard mappings
        foreach (var kvp in _pendingKeyboardChanges)
        {
            var deviceId = kvp.Key;
            var (layoutId, isEnabled) = kvp.Value;
            var keyboard = KeyboardService.GetDeviceById(deviceId);

            Settings.UpdateSettings(s => s.SetMapping(
                deviceId,
                keyboard?.DisplayName ?? deviceId,
                layoutId,
                isEnabled));
        }

        // Clear pending changes
        _pendingKeyboardChanges.Clear();
        _hasUnsavedChanges = false;
        SaveButton.IsEnabled = false;

        // Close the window after saving
        Hide();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Discard changes and close
        _pendingKeyboardChanges.Clear();
        _hasUnsavedChanges = false;

        // Reset UI to saved state
        _isInitializing = true;
        RunAtStartupCheckBox.IsChecked = StartupService.IsStartupEnabled();
        ShowNotificationsCheckBox.IsChecked = Settings.Settings.ShowNotifications;
        RefreshKeyboards();
        SaveButton.IsEnabled = false;
        _isInitializing = false;

        Hide();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Warn about unsaved changes
        if (_hasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Do you want to save before closing?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveButton_Click(sender, new RoutedEventArgs());
            }
            else if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            else
            {
                // Discard changes - reset to saved state
                _pendingKeyboardChanges.Clear();
                _hasUnsavedChanges = false;
            }
        }

        // Hide instead of close
        e.Cancel = true;
        Hide();
    }

    public void RemoveKeyboard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string deviceId)
        {
            // Remove from settings
            Settings.UpdateSettings(s =>
            {
                var mapping = s.KeyboardMappings.FirstOrDefault(m =>
                    m.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
                if (mapping != null)
                {
                    s.KeyboardMappings.Remove(mapping);
                }
            });

            // Remove from pending changes
            _pendingKeyboardChanges.Remove(deviceId);

            // Refresh the list
            RefreshKeyboards();
        }
    }
}

public class KeyboardViewModel : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private string? _layoutId;

    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<KeyboardLayoutInfo>? AvailableLayouts { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public string? LayoutId
    {
        get => _layoutId;
        set
        {
            if (_layoutId != value)
            {
                _layoutId = value;
                OnPropertyChanged(nameof(LayoutId));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
