using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using BGCS.Core.Hosting;

namespace BGCS.Hosting.Standard.Windows;

internal static class WindowsDirectorySemantics
{
    internal static HostPathSemantics Observe(string directory)
    {
        string absolute = Path.GetFullPath(directory);
        // Win32 does not apply .NET's long-path normalization. Keep this native
        // spelling local to the handle operation; public path identities stay unchanged.
        string nativePath = absolute.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? absolute : absolute.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + absolute[2..] : @"\\?\" + absolute;
        using SafeFileHandle handle = CreateFileW(nativePath, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Cannot observe directory case semantics: " + directory, new Win32Exception(Marshal.GetLastPInvokeError()));
        if (!GetFileInformationByHandleEx(handle, 23, out uint flags, sizeof(uint)))
            throw new IOException("The host cannot establish directory case semantics: " + directory, new Win32Exception(Marshal.GetLastPInvokeError()));
        return new((flags & 1) != 0);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint access,
        uint share,
        IntPtr security,
        uint creation,
        uint flags,
        IntPtr template
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle,
        int informationClass,
        out uint information,
        uint size
    );
}
