using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GlueyKeys.Models;
using GlueyKeys.Services;

namespace GlueyKeys.Views;

public partial class LayoutPickerWindow : Window
{
    private readonly LanguageSwitcherService _layoutService;
    private bool _acceptEnterKey = false;

    public string? SelectedLayoutId { get; private set; }

    public LayoutPickerWindow(Models.KeyboardDevice keyboard, LanguageSwitcherService layoutService)
    {
        _layoutService = layoutService;

        InitializeComponent();

        KeyboardNameText.Text = $"\"{keyboard.DisplayName}\" is typing.";

        // Load installed keyboard layouts
        LoadInstalledLayouts();

        // Focus the list and select first item when window loads
        Loaded += (s, e) =>
        {
            if (LayoutListBox.Items.Count > 0)
            {
                LayoutListBox.SelectedIndex = 0;
                LayoutListBox.Focus();
                var firstItem = LayoutListBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                firstItem?.Focus();
            }

            // Delay accepting Enter key to prevent spillover from the key that triggered this window
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) =>
            {
                _acceptEnterKey = true;
                timer.Stop();
            };
            timer.Start();
        };
    }

    private void LoadInstalledLayouts()
    {
        var layouts = _layoutService.GetInstalledLayouts();
        LayoutListBox.ItemsSource = layouts;
    }

    private void LayoutListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveButton.IsEnabled = LayoutListBox.SelectedItem != null;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (LayoutListBox.SelectedItem is KeyboardLayoutInfo selectedLayout)
        {
            SelectedLayoutId = selectedLayout.LayoutId;
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void LayoutListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LayoutListBox.SelectedItem != null)
        {
            SaveButton_Click(sender, e);
        }
    }

    private void LayoutListBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Only accept Enter after a short delay to prevent spillover from the key that triggered this window
        if (e.Key == Key.Enter && _acceptEnterKey && LayoutListBox.SelectedItem != null)
        {
            SaveButton_Click(sender, e);
            e.Handled = true;
        }
    }
}
