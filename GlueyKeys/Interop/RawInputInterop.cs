using System.Runtime.InteropServices;

namespace GlueyKeys.Interop;

public static class RawInputInterop
{
    public const int WM_INPUT = 0x00FF;
    public const int RID_INPUT = 0x10000003;
    public const int RIM_TYPEKEYBOARD = 1;
    public const int RIDEV_INPUTSINK = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUT
    {
        public RAWINPUTHEADER Header;
        public RAWKEYBOARD Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_KEYBOARD
    {
        public uint Type;
        public uint SubType;
        public uint KeyboardMode;
        public uint NumberOfFunctionKeys;
        public uint NumberOfIndicators;
        public uint NumberOfKeysTotal;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO
    {
        public uint Size;
        public uint Type;
        public RID_DEVICE_INFO_KEYBOARD Keyboard;
    }

    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000b;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList(
        IntPtr pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public IntPtr Device;
        public uint Type;
    }
}
