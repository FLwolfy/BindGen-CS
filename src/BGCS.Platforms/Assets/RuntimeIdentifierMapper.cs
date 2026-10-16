using System;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Native;

namespace BGCS.Platforms.Assets;

/// <summary>Maps exact native target identities to asset RIDs without treating a RID as an ABI proof.</summary>
public static class RuntimeIdentifierMapper
{
    /// <summary>Maps independently resolved platform, CPU and ABI facts while preserving an opaque target identity.</summary>
    /// <param name="target">The concrete platform and ABI descriptor.</param>
    /// <returns>The known .NET asset RID, without a platform certification claim.</returns>
    public static string Map(NativeTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.byteOrder == NativeByteOrder.BigEndian)
            throw new NotSupportedException("The built-in .NET asset RIDs have no supported big-endian runtime mapping; native-only construction requires a separate explicit consumer mechanism.");
        string architecture = target.architectureId;
        return (target.platformId, target.abiId) switch
        {
            ("windows", "msvc") when architecture is "x86" or "x64" or "arm64" => "win-" + architecture,
            ("linux", "gnu") when architecture is "x86" or "x64" or "arm" or "arm64" => "linux-" + architecture,
            ("linux", "musl") when architecture is "x64" or "arm64" => "linux-musl-" + architecture,
            ("macos", "darwin") when architecture is "x64" or "arm64" => "osx-" + architecture,
            ("android", "android") when architecture is "x64" or "arm64" => "android-" + architecture,
            ("ios", "darwin") when architecture == "arm64" => "ios-arm64",
            ("ios-simulator", "darwin") when architecture is "x64" or "arm64" => "iossimulator-" + architecture,
            ("freebsd", "gnu") when architecture is "x64" or "arm64" => "freebsd-" + architecture,
            ("emscripten", "emscripten") when architecture == "wasm32" => "browser-wasm",
            _ => throw new NotSupportedException("The resolved native platform, CPU and ABI have no built-in .NET asset mapping: " + target.targetId.value)
        };
    }
    /// <summary>Maps an implemented native platform/ABI identity to its consumer asset RID.</summary>
    /// <param name="targetId">The exact concrete target identity.</param>
    /// <returns>The .NET asset RID; the mapping makes no invocation or package certification claim.</returns>
    public static string Map(NativeTargetId targetId) => targetId.value switch
    {
        "windows-x86-msvc" => "win-x86",
        "windows-x64-msvc" => "win-x64",
        "windows-arm64-msvc" => "win-arm64",
        "linux-x86-gnu" => "linux-x86",
        "linux-x64-gnu" => "linux-x64",
        "linux-arm64-gnu" => "linux-arm64",
        "linux-arm-gnu" => "linux-arm",
        "linux-x64-musl" => "linux-musl-x64",
        "linux-arm64-musl" => "linux-musl-arm64",
        "macos-x64-darwin" => "osx-x64",
        "macos-arm64-darwin" => "osx-arm64",
        "android-arm64" or "android-arm64-android" => "android-arm64",
        "android-x64" or "android-x64-android" => "android-x64",
        "ios-arm64-darwin" => "ios-arm64",
        "ios-simulator-x64-darwin" => "iossimulator-x64",
        "ios-simulator-arm64-darwin" => "iossimulator-arm64",
        "freebsd-x64-gnu" => "freebsd-x64",
        "freebsd-arm64-gnu" => "freebsd-arm64",
        "emscripten-wasm32-emscripten" => "browser-wasm",
        _ => throw new NotSupportedException("The native target has no built-in .NET asset mapping: " + targetId.value)
    };
}
