using System.Windows;

namespace GlueyKeys.Views;

public partial class SetupWizardWindow : Window
{
    public bool RunAtStartup => RunAtStartupCheckBox.IsChecked ?? true;
    public bool ShowNotifications => ShowNotificationsCheckBox.IsChecked ?? true;
    public bool CreateDesktopShortcut => DesktopShortcutCheckBox.IsChecked ?? false;
    public bool CreateStartMenuShortcut => StartMenuShortcutCheckBox.IsChecked ?? false;

    public SetupWizardWindow()
    {
        InitializeComponent();
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
