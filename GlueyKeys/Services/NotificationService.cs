using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace GlueyKeys.Services;

public class NotificationService
{
    private const string AppId = "GlueyKeys";
    private bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public void ShowLanguageSwitched(string languageName, string keyboardName)
    {
        if (!_enabled)
            return;

        try
        {
            ShowToast($"Switched to {languageName}", $"Keyboard: {keyboardName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show notification: {ex.Message}");
        }
    }

    public void ShowNewKeyboardDetected(string keyboardName)
    {
        if (!_enabled)
            return;

        try
        {
            ShowToast("New Keyboard Detected", $"{keyboardName} - Click to configure");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show notification: {ex.Message}");
        }
    }

    private void ShowToast(string title, string message)
    {
        var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
        var textNodes = template.GetElementsByTagName("text");

        textNodes[0].AppendChild(template.CreateTextNode(title));
        textNodes[1].AppendChild(template.CreateTextNode(message));

        var toast = new ToastNotification(template);
        toast.ExpirationTime = DateTimeOffset.Now.AddSeconds(5);

        var notifier = ToastNotificationManager.CreateToastNotifier(AppId);
        notifier.Show(toast);
    }

    public static void ClearNotifications()
    {
        try
        {
            ToastNotificationManager.History.Clear(AppId);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
