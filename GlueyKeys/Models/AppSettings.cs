namespace GlueyKeys.Models;

public class AppSettings
{
    public bool SetupCompleted { get; set; } = false;
    public bool FirstRunPromptShown { get; set; } = false;
    public bool RunAtStartup { get; set; } = false;
    public bool ShowNotifications { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public List<KeyboardMapping> KeyboardMappings { get; set; } = new();

    public KeyboardMapping? GetMapping(string deviceId)
    {
        return KeyboardMappings.FirstOrDefault(m =>
            m.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
    }

    public void SetMapping(string deviceId, string deviceName, string? layoutId, bool isEnabled = true)
    {
        var existing = GetMapping(deviceId);
        if (existing != null)
        {
            existing.LayoutId = layoutId;
            existing.IsEnabled = isEnabled;
            existing.DeviceName = deviceName;
        }
        else
        {
            KeyboardMappings.Add(new KeyboardMapping
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                LayoutId = layoutId,
                IsEnabled = isEnabled
            });
        }
    }
}
