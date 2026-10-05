using System;
using System.Diagnostics.CodeAnalysis;
using BGCS.Core.Targeting;

namespace BGCS.CppAst.Targeting.Providers;

/// <summary>
/// Supplies GNU, musl, FreeBSD and Android native target definitions.
/// </summary>
public sealed class UnixNativeTargetProvider : INativeTargetProvider
{
    /// <inheritdoc/>
    public bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    ) {
        ArgumentNullException.ThrowIfNull(request);
        target = null;
        string[] parts = request.targetId.value.Split('-');
        if (parts.Length != 3)
            return false;
        string? cpu = parts[1] switch
        {
            "x86" => "i686",
            "x64" => "x86_64",
            "arm" => "armv7",
            "arm64" => "aarch64",
            _ => null
        };
        if (cpu is null)
            return false;
        string? triple = (parts[0], parts[2]) switch
        {
            ("linux", "gnu") => cpu + "-unknown-linux-gnu",
            ("linux", "musl") => cpu + "-unknown-linux-musl",
            ("freebsd", "gnu") => cpu + "-unknown-freebsd",
            ("android", "android") => cpu + "-linux-android",
            _ => null
        };
        if (triple is null)
            return false;
        target = new(request.targetId, parts[0], parts[1], parts[2], request.tripleOverride ?? triple, request.toolchain);
        return true;
    }
}
