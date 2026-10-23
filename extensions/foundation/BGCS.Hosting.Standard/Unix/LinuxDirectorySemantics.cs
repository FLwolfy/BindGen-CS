using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using BGCS.Core.Hosting;

namespace BGCS.Hosting.Standard.Unix;

internal static class LinuxDirectorySemantics
{
    internal static HostPathSemantics Observe(string directory)
    {
        byte[] filesystem = new byte[512];
        if (statfs(directory, filesystem) != 0)
            throw new IOException("Cannot observe filesystem comparison behavior: " + directory, new Win32Exception(Marshal.GetLastPInvokeError()));
        ulong kind = IntPtr.Size == 8 ? BitConverter.ToUInt64(filesystem) : BitConverter.ToUInt32(filesystem);
        if (kind == 0xEF53)
        {
            int descriptor = open(directory, 0x10000 | 0x80000);
            if (descriptor < 0) throw new IOException("Cannot open directory case metadata: " + directory, new Win32Exception(Marshal.GetLastPInvokeError()));
            try
            {
                if (ioctl(descriptor, (nuint)(IntPtr.Size == 8 ? 0x80086601UL : 0x80046601UL), out uint flags) != 0)
                    throw new IOException("Cannot observe directory case-folding flags: " + directory, new Win32Exception(Marshal.GetLastPInvokeError()));
                return new((flags & 0x40000000) == 0);
            }
            finally { close(descriptor); }
        }
        if (kind is 0x01021994 or 0x794c7630 or 0x9123683E or 0x58465342 or 0x2FC12FC1 or 0x9fa0 or 0x62656572)
            return new(true);
        throw new NotSupportedException("This host filesystem has no implemented comparison observer; register a host extension that establishes its actual semantics. Filesystem: " + kind.ToString("x"));
    }
    [DllImport("libc", SetLastError = true)]
    private static extern int statfs(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [Out] byte[] data
    );
    [DllImport("libc", SetLastError = true)]
    private static extern int open(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags
    );
    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(
        int descriptor,
        nuint request,
        out uint flags
    );
    [DllImport("libc", SetLastError = true)]
    private static extern int close(int descriptor);
}
