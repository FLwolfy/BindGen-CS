using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeCallableAddressabilityTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ActualCompilerDiagnosticsDistinguishDirectCallIntrinsicsFromAddressableBuiltins(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-addressability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "intrinsics.h");
            await File.WriteAllTextAsync(header, """
                #ifdef __cplusplus
                extern "C" {
                #endif
                void _ReadWriteBarrier(void);
                void __debugbreak(void);
                #pragma intrinsic(_ReadWriteBarrier)
                #pragma intrinsic(__debugbreak)
                void* memcpy(void* output, const void* input, __SIZE_TYPE__ length);
                void Ordinary(void);
                #ifdef __cplusplus
                }
                #endif
                """);
            NativeTargetDescriptor target = new(new("addressability-test"), "windows", "x64", "msvc", "x86_64-pc-windows-msvc", new());
            NativeModule module = (await new ClangFrontend(host).ParseAsync(new(target, language, [header])
            { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            foreach (string name in new[] { "_ReadWriteBarrier", "__debugbreak" })
            {
                NativeDeclaration function = Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == name);
                Assert.Equal(NativeCallableAddressability.DirectCallOnly, function.addressability);
                Assert.Equal(EvidenceLevel.Declared, function.addressabilityEvidence!.level);
                Assert.False(function.isDefinition);
                Assert.Equal("intrinsics.h", function.source.file);
            }
            Assert.Equal(NativeCallableAddressability.Addressable,
                Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == "memcpy").addressability);
            Assert.Equal(NativeCallableAddressability.Unknown,
                Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == "Ordinary").addressability);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
