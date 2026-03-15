using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using GlueyKeys.Interop;

namespace GlueyKeys.Services;

public class LanguageSwitcherService
{
    private readonly List<KeyboardLayoutInfo> _installedLayouts = new();
    private readonly Dictionary<string, KeyboardLayoutInfo> _layoutById = new(StringComparer.OrdinalIgnoreCase);

    public LanguageSwitcherService()
    {
        RefreshLayoutList();
    }

    public void RefreshLayoutList()
    {
        _installedLayouts.Clear();
        _layoutById.Clear();

        int count = User32.GetKeyboardLayoutList(0, null!);
        if (count == 0)
            return;

        var layouts = new IntPtr[count];
        User32.GetKeyboardLayoutList(count, layouts);

        foreach (var hkl in layouts)
        {
            var layoutInfo = GetLayoutInfo(hkl);
            if (layoutInfo != null)
            {
                _installedLayouts.Add(layoutInfo);
                _layoutById[layoutInfo.LayoutId] = layoutInfo;
            }
        }
    }

    public List<KeyboardLayoutInfo> GetInstalledLayouts()
    {
        return _installedLayouts.OrderBy(l => l.DisplayName).ToList();
    }

    public bool SwitchToLayout(string layoutId)
    {
        if (_layoutById.TryGetValue(layoutId, out var layout))
        {
            return ActivateLayout(layout.Hkl);
        }

        return false;
    }

    public string? GetCurrentLayoutId()
    {
        var foreground = User32.GetForegroundWindow();
        var threadId = User32.GetWindowThreadProcessId(foreground, out _);
        var currentHkl = User32.GetKeyboardLayout(threadId);

        // Convert HKL to layout ID string
        return HklToLayoutId(currentHkl);
    }

    public KeyboardLayoutInfo? GetLayoutById(string layoutId)
    {
        return _layoutById.TryGetValue(layoutId, out var layout) ? layout : null;
    }

    private bool ActivateLayout(IntPtr hkl)
    {
        // Send to all windows
        User32.PostMessage(
            new IntPtr(User32.HWND_BROADCAST),
            User32.WM_INPUTLANGCHANGEREQUEST,
            IntPtr.Zero,
            hkl);

        // Also activate for current process
        User32.ActivateKeyboardLayout(hkl, User32.KLF_SETFORPROCESS);

        return true;
    }

    private KeyboardLayoutInfo? GetLayoutInfo(IntPtr hkl)
    {
        var layoutId = HklToLayoutId(hkl);
        if (string.IsNullOrEmpty(layoutId))
            return null;

        long hklValue = hkl.ToInt64();

        // HKL format:
        // - Low word (bits 0-15): Language ID (LANGID) - determines input language
        // - High word (bits 16-31): Keyboard layout - determines physical key mapping
        int langId = (int)(hklValue & 0xFFFF);
        int layoutDeviceId = (int)((hklValue >> 16) & 0xFFFF);

        // Get language name from LANGID
        string languageName;
        try
        {
            var culture = CultureInfo.GetCultureInfo(langId);
            languageName = culture.DisplayName;
        }
        catch
        {
            languageName = $"Language 0x{langId:X4}";
        }

        // Get keyboard layout name
        // First try the full KLID from registry
        string? keyboardLayoutName = GetLayoutNameFromRegistry(layoutId);

        // If not found and high word differs from low word, the high word IS the keyboard layout
        if (string.IsNullOrEmpty(keyboardLayoutName) && layoutDeviceId != langId)
        {
            // Try to get layout name using high word as KLID
            var highWordKlid = $"0000{layoutDeviceId:X4}";
            keyboardLayoutName = GetLayoutNameFromRegistry(highWordKlid);

            // Or try to get it as a language
            if (string.IsNullOrEmpty(keyboardLayoutName))
            {
                try
                {
                    var layoutCulture = CultureInfo.GetCultureInfo(layoutDeviceId);
                    keyboardLayoutName = layoutCulture.DisplayName;
                }
                catch
                {
                    keyboardLayoutName = $"Layout 0x{layoutDeviceId:X4}";
                }
            }
        }

        // If still no keyboard name, use standard KLID lookup
        if (string.IsNullOrEmpty(keyboardLayoutName))
        {
            var standardKlid = $"0000{langId:X4}";
            keyboardLayoutName = GetLayoutNameFromRegistry(standardKlid);
        }

        // Build display name showing both language and keyboard layout
        string displayName;
        if (!string.IsNullOrEmpty(keyboardLayoutName))
        {
            if (layoutDeviceId != langId)
            {
                // Different keyboard layout than language - show both
                displayName = $"{languageName} [{keyboardLayoutName}]";
            }
            else if (keyboardLayoutName != languageName)
            {
                // Same IDs but different names
                displayName = $"{languageName} - {keyboardLayoutName}";
            }
            else
            {
                displayName = languageName;
            }
        }
        else
        {
            displayName = $"{languageName} (0x{layoutId})";
        }

        return new KeyboardLayoutInfo
        {
            LayoutId = layoutId,
            DisplayName = displayName,
            LayoutName = keyboardLayoutName ?? "Unknown",
            LanguageName = languageName,
            Hkl = hkl
        };
    }

    private string? HklToLayoutId(IntPtr hkl)
    {
        long hklValue = hkl.ToInt64();
        // Format as 8-digit hex string (standard KLID format)
        return hklValue.ToString("X8").PadLeft(8, '0');
    }

    private string? GetLayoutNameFromRegistry(string layoutId)
    {
        // Keyboard layouts are stored in: HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layouts\<KLID>
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{layoutId}");

            return key?.GetValue("Layout Text") as string;
        }
        catch
        {
            return null;
        }
    }
}

public class KeyboardLayoutInfo
{
    public string LayoutId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LayoutName { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public IntPtr Hkl { get; set; }
}
