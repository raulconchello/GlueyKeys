using System.Runtime.InteropServices;

namespace GlueyKeys.Interop;

public static class SetupApi
{
    public static readonly Guid GUID_DEVCLASS_KEYBOARD = new("4D36E96B-E325-11CE-BFC1-08002BE10318");

    public const int DIGCF_PRESENT = 0x00000002;
    public const int DIGCF_DEVICEINTERFACE = 0x00000010;

    public const int SPDRP_DEVICEDESC = 0x00000000;
    public const int SPDRP_FRIENDLYNAME = 0x0000000C;
    public const int SPDRP_MFG = 0x0000000B;

    // Device status flags
    public const int DN_STARTED = 0x00000008;
    public const int DN_DISABLEABLE = 0x00002000;
    public const int CR_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        IntPtr propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        IntPtr deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    // Configuration Manager API to check device status
    [DllImport("cfgmgr32.dll", SetLastError = true)]
    public static extern int CM_Get_DevNode_Status(
        out uint status,
        out uint problemNumber,
        uint devInst,
        uint flags);
}
