using System.Runtime.InteropServices;
using GlueyKeys.Interop;
using GlueyKeys.Models;

namespace GlueyKeys.Services;

public class KeyboardEnumerationService
{
    private readonly Dictionary<IntPtr, KeyboardDevice> _deviceHandleMap = new();
    private readonly Dictionary<string, KeyboardDevice> _deviceIdMap = new();

    // Track keyboards that have actually sent input (definitely connected)
    private readonly HashSet<string> _activeDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<IntPtr, KeyboardDevice> DeviceHandleMap => _deviceHandleMap;

    // Only return keyboards that have been active (sent input)
    public IReadOnlyCollection<KeyboardDevice> AllDevices =>
        _deviceIdMap.Values.Where(d => _activeDeviceIds.Contains(d.DeviceId)).ToList();

    // Mark a keyboard as active (called when it sends input)
    public void MarkAsActive(string deviceId)
    {
        _activeDeviceIds.Add(deviceId);
    }

    public void EnumerateKeyboards()
    {
        _deviceHandleMap.Clear();
        _deviceIdMap.Clear();
        // Don't clear _activeDeviceIds - we want to keep track of seen keyboards

        // Get raw input device list for handles
        var rawDevices = GetRawInputKeyboards();

        // Get device details from SetupAPI
        var setupDevices = GetSetupApiKeyboards();

        foreach (var rawDevice in rawDevices)
        {
            var deviceName = GetDeviceName(rawDevice.Handle);
            if (string.IsNullOrEmpty(deviceName))
                continue;

            var matchedDevice = FindMatchingSetupDevice(deviceName, setupDevices);

            var keyboard = new KeyboardDevice
            {
                DeviceHandle = rawDevice.Handle,
                DeviceId = deviceName,
                DeviceName = matchedDevice?.FriendlyName ?? ExtractFriendlyName(deviceName),
                Manufacturer = matchedDevice?.Manufacturer ?? "Unknown"
            };

            _deviceHandleMap[rawDevice.Handle] = keyboard;

            if (!_deviceIdMap.ContainsKey(keyboard.DeviceId))
            {
                _deviceIdMap[keyboard.DeviceId] = keyboard;
            }
        }
    }

    public KeyboardDevice? GetDeviceByHandle(IntPtr handle)
    {
        return _deviceHandleMap.TryGetValue(handle, out var device) ? device : null;
    }

    public KeyboardDevice? GetDeviceById(string deviceId)
    {
        return _deviceIdMap.TryGetValue(deviceId, out var device) ? device : null;
    }

    private string ExtractFriendlyName(string deviceName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            deviceName,
            @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? $"Keyboard (VID:{match.Groups[1].Value} PID:{match.Groups[2].Value})" : "Unknown Keyboard";
    }

    private (string FriendlyName, string Manufacturer)? FindMatchingSetupDevice(
        string rawDeviceName,
        Dictionary<string, (string FriendlyName, string Manufacturer)> setupDevices)
    {
        foreach (var kvp in setupDevices)
        {
            if (rawDeviceName.Contains(kvp.Key.Replace("\\", "#"), StringComparison.OrdinalIgnoreCase) ||
                rawDeviceName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private List<(IntPtr Handle, uint Type)> GetRawInputKeyboards()
    {
        var result = new List<(IntPtr, uint)>();
        uint deviceCount = 0;
        uint size = (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTDEVICELIST>();

        if (RawInputInterop.GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, size) != 0)
            return result;

        if (deviceCount == 0)
            return result;

        var deviceListPtr = Marshal.AllocHGlobal((int)(size * deviceCount));
        try
        {
            if (RawInputInterop.GetRawInputDeviceList(deviceListPtr, ref deviceCount, size) == unchecked((uint)-1))
                return result;

            for (int i = 0; i < deviceCount; i++)
            {
                var device = Marshal.PtrToStructure<RawInputInterop.RAWINPUTDEVICELIST>(
                    deviceListPtr + (i * (int)size));

                if (device.Type == RawInputInterop.RIM_TYPEKEYBOARD)
                {
                    result.Add((device.Device, device.Type));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(deviceListPtr);
        }

        return result;
    }

    private string? GetDeviceName(IntPtr deviceHandle)
    {
        uint size = 0;
        RawInputInterop.GetRawInputDeviceInfo(deviceHandle, RawInputInterop.RIDI_DEVICENAME, IntPtr.Zero, ref size);

        if (size == 0)
            return null;

        var namePtr = Marshal.AllocHGlobal((int)(size * 2));
        try
        {
            if (RawInputInterop.GetRawInputDeviceInfo(deviceHandle, RawInputInterop.RIDI_DEVICENAME, namePtr, ref size) > 0)
            {
                return Marshal.PtrToStringUni(namePtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(namePtr);
        }

        return null;
    }

    private Dictionary<string, (string FriendlyName, string Manufacturer)> GetSetupApiKeyboards()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        var classGuid = SetupApi.GUID_DEVCLASS_KEYBOARD;

        var deviceInfoSet = SetupApi.SetupDiGetClassDevs(
            ref classGuid,
            null,
            IntPtr.Zero,
            SetupApi.DIGCF_PRESENT);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            return result;

        try
        {
            var deviceInfoData = new SetupApi.SP_DEVINFO_DATA
            {
                Size = (uint)Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>()
            };

            uint index = 0;
            while (SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, index++, ref deviceInfoData))
            {
                var instanceId = GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);
                if (string.IsNullOrEmpty(instanceId))
                    continue;

                var friendlyName = GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_FRIENDLYNAME)
                    ?? GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_DEVICEDESC)
                    ?? "Unknown Keyboard";

                var manufacturer = GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_MFG)
                    ?? "Unknown";

                result[instanceId] = (friendlyName, manufacturer);
            }
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return result;
    }

    private string? GetDeviceInstanceId(IntPtr deviceInfoSet, ref SetupApi.SP_DEVINFO_DATA deviceInfoData)
    {
        uint size = 0;
        SetupApi.SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, IntPtr.Zero, 0, out size);

        if (size == 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)(size * 2));
        try
        {
            if (SetupApi.SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, buffer, size, out _))
            {
                return Marshal.PtrToStringUni(buffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return null;
    }

    private string? GetDeviceProperty(IntPtr deviceInfoSet, ref SetupApi.SP_DEVINFO_DATA deviceInfoData, uint property)
    {
        uint size = 0;
        SetupApi.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, IntPtr.Zero, 0, out size);

        if (size == 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (SetupApi.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, buffer, size, out _))
            {
                return Marshal.PtrToStringUni(buffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return null;
    }
}
