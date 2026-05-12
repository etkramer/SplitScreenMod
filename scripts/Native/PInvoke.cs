using System.Runtime.InteropServices;

public static class PInvoke
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualProtect(
        IntPtr lpAddress,
        nuint dwSize,
        uint flNewProtect,
        out uint lpflOldProtect
    );
}