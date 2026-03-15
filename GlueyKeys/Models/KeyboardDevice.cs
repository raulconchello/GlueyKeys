namespace GlueyKeys.Models;

public class KeyboardDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public IntPtr DeviceHandle { get; set; }

    public string DisplayName => string.IsNullOrEmpty(DeviceName)
        ? "Unknown Keyboard"
        : DeviceName;
}
