using System;
using BGCS.Core.Native;

namespace BGCS.Targets.Desktop.Targeting;

internal static class NativeTargetFacts
{
    internal static NativeByteOrder ByteOrder(
        string architecture,
        string triple
    ) {
        string cpu = triple.Split('-')[0];
        return architecture switch
        {
            "x64" when cpu == "x86_64" => NativeByteOrder.LittleEndian,
            "x86" when cpu is "i386" or "i486" or "i586" or "i686" => NativeByteOrder.LittleEndian,
            "arm64" when cpu is "aarch64" or "arm64" => NativeByteOrder.LittleEndian,
            "arm64" when cpu == "aarch64_be" => NativeByteOrder.BigEndian,
            "arm" when cpu.StartsWith("armeb", StringComparison.Ordinal) || cpu.EndsWith("eb", StringComparison.Ordinal) => NativeByteOrder.BigEndian,
            "arm" when cpu is "arm" or "thumb" || cpu.StartsWith("armv", StringComparison.Ordinal) || cpu.StartsWith("thumbv", StringComparison.Ordinal) => NativeByteOrder.LittleEndian,
            "wasm32" when cpu == "wasm32" => NativeByteOrder.LittleEndian,
            _ => NativeByteOrder.Unknown
        };
    }
}
