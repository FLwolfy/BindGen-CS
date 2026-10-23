using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using BGCS.Core.Hosting;

namespace BGCS.Hosting.Standard.Unix;

internal static class AppleDirectorySemantics
{
    internal static HostPathSemantics Observe(string directory)
    {
        AttributeList request = new() { count = 5, volume = 0x00020000 };
        byte[] response = new byte[36];
        if (getattrlist(directory, ref request, response, (nuint)response.Length, 0) != 0)
            throw new IOException("Cannot observe volume case semantics: " + directory, new Win32Exception(Marshal.GetLastPInvokeError()));
        if (BitConverter.ToUInt32(response, 0) != response.Length || (BitConverter.ToUInt32(response, 20) & 0x100) == 0)
            throw new IOException("The volume did not establish its case comparison capability: " + directory);
        return new((BitConverter.ToUInt32(response, 4) & 0x100) != 0);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeList
    {
        internal ushort count;
        internal ushort reserved;
        internal uint common;
        internal uint volume;
        internal uint directory;
        internal uint file;
        internal uint fork;
    }
    [DllImport("libc", SetLastError = true)]
    private static extern int getattrlist(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        ref AttributeList attributes,
        [Out] byte[] result,
        nuint size,
        ulong options
    );
}
