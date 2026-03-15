using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GlueyKeys.Interop;

namespace GlueyKeys.Services;

public class RawInputService : IDisposable
{
    private HwndSource? _hwndSource;
    private IntPtr _hwnd;
    private bool _isRegistered;

    public event EventHandler<KeyboardInputEventArgs>? KeyboardInput;

    public bool Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        return RegisterForRawInput();
    }

    public bool Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        return RegisterForRawInput();
    }

    private bool RegisterForRawInput()
    {
        if (_isRegistered)
            return true;

        var devices = new RawInputInterop.RAWINPUTDEVICE[]
        {
            new()
            {
                UsagePage = 0x01, // Generic Desktop
                Usage = 0x06,    // Keyboard
                Flags = RawInputInterop.RIDEV_INPUTSINK,
                Target = _hwnd
            }
        };

        _isRegistered = RawInputInterop.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTDEVICE>());

        return _isRegistered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == RawInputInterop.WM_INPUT)
        {
            ProcessRawInput(lParam);
        }

        return IntPtr.Zero;
    }

    private void ProcessRawInput(IntPtr lParam)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTHEADER>();

        RawInputInterop.GetRawInputData(lParam, RawInputInterop.RID_INPUT, IntPtr.Zero, ref size, headerSize);

        if (size == 0)
            return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (RawInputInterop.GetRawInputData(lParam, RawInputInterop.RID_INPUT, buffer, ref size, headerSize) != size)
                return;

            var raw = Marshal.PtrToStructure<RawInputInterop.RAWINPUT>(buffer);

            if (raw.Header.Type == RawInputInterop.RIM_TYPEKEYBOARD)
            {
                // Only process key down events to avoid duplicates
                if ((raw.Keyboard.Flags & 0x01) == 0) // Key down (WM_KEYDOWN)
                {
                    KeyboardInput?.Invoke(this, new KeyboardInputEventArgs
                    {
                        DeviceHandle = raw.Header.Device,
                        VirtualKey = raw.Keyboard.VKey,
                        MakeCode = raw.Keyboard.MakeCode,
                        Flags = raw.Keyboard.Flags
                    });
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _hwndSource = null;
    }
}

public class KeyboardInputEventArgs : EventArgs
{
    public IntPtr DeviceHandle { get; set; }
    public ushort VirtualKey { get; set; }
    public ushort MakeCode { get; set; }
    public ushort Flags { get; set; }
}
