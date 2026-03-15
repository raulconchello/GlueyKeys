using System.Text.Json.Serialization;

namespace GlueyKeys.Models;

public class KeyboardMapping
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string? LayoutId { get; set; }
    public bool IsEnabled { get; set; } = true;

    [JsonIgnore]
    public bool HasLayout => !string.IsNullOrEmpty(LayoutId);
}
