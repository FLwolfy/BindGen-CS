using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class ManagedRuntimeProfileIdentityTests
{
    [Theory]
    [InlineData("unchanged")]
    [InlineData("id")]
    [InlineData("runtime")]
    [InlineData("module")]
    [InlineData("imports")]
    [InlineData("thunks")]
    [InlineData("blocking")]
    [InlineData("target")]
    [InlineData("triple")]
    [InlineData("pointer")]
    [InlineData("order")]
    [InlineData("abi")]
    [InlineData("transport")]
    [InlineData("convention")]
    [InlineData("default-convention")]
    public void CaptureRetainsExactUtf8JsonDigestAcrossEveryExecutionProfileField(string field)
    {
        ManagedRuntimeProfile original = new("fixture-\"\u03bb\"", new("fixture.runtime"), NativeModuleMechanism.ExternalFunctionTable,
            [BindingImportMode.FunctionTable], false, true)
        {
            nativeTargetId = "fixture-target",
            nativeTriple = "fixture-triple",
            pointerSize = 8,
            byteOrder = NativeByteOrder.LittleEndian,
            callAbi = new("fixture.abi")
        };
        ManagedRuntimeProfile profile = field switch
        {
            "id" => original with { id = "other-profile" },
            "runtime" => original with { runtimeId = new("other.runtime") },
            "module" => original with { moduleMechanism = NativeModuleMechanism.StaticAdapter },
            "imports" => original with { importModes = [BindingImportMode.FunctionTable, BindingImportMode.LibraryImport] },
            "thunks" => original with { requiresStaticCallbackThunks = true },
            "blocking" => original with { allowsBlockingDisposal = false },
            "target" => original with { nativeTargetId = "other-target" },
            "triple" => original with { nativeTriple = "other-triple" },
            "pointer" => original with { pointerSize = 4 },
            "order" => original with { byteOrder = NativeByteOrder.BigEndian },
            "abi" => original with { callAbi = new("other.abi") },
            "transport" => original with { functionPointerTransport = ManagedFunctionPointerTransport.NativeInteger },
            "convention" => original with { libraryImportConvention = ManagedImportConventionStrategy.RuntimeDefault },
            "default-convention" => original with { defaultCallingConvention = "Cdecl" },
            _ => original
        };
        byte[] establishedBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profile));
        Assert.Equal(establishedBytes, JsonSerializer.SerializeToUtf8Bytes(profile));
        ContentDigest expected = new(Convert.ToHexStringLower(SHA256.HashData(establishedBytes)));
        Assert.Equal(expected, ManagedRuntimeProfileIdentity.Capture(profile));
        if (field != "unchanged") Assert.NotEqual(ManagedRuntimeProfileIdentity.Capture(original), ManagedRuntimeProfileIdentity.Capture(profile));
    }
}
