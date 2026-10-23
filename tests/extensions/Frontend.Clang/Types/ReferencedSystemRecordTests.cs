using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Frontend.Clang;
using BGCS.Targets.Desktop.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ReferencedSystemRecordTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ReferencedSdkRecord_PreservesNestedFieldsAndArrayLayoutWithoutPublishingSdkApis(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-referenced-sdk-record-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk");
        Directory.CreateDirectory(sdk);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sdk, "sdk_records.h"), """
                typedef unsigned short Sample;
                struct SdkInner { double value; };
                struct SdkPacket {
                    int code;
                    struct SdkInner nested;
                    Sample samples[2];
                #ifdef __cplusplus
                    int unreferenced_method() const { return code; }
                    static int unreferenced_static_method() { return 0; }
                #endif
                };
                struct UnreferencedSdkRecord { int ignored; };
                int unreferenced_system_function(void);
                """);
            string header = Path.Combine(root, "entry.h");
            await File.WriteAllTextAsync(header, "#include <sdk_records.h>\nstruct SdkPacket transform_packet(struct SdkPacket input);");
            FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), language, [header])
            { systemIncludeDirectories = [sdk], pathBase = root, parseSystemIncludes = false, parseMacros = false };
            NativeModule module = (await new ClangFrontend(host).ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration packet = Assert.Single(module.declarations, declaration => declaration.name == "SdkPacket");
            Assert.True(packet.isSystemDeclaration);
            Assert.True(packet.isCompleteDefinition);
            NativeType storage = module.GetType(packet.type!.Value);
            Assert.Equal(24, storage.size);
            Assert.Equal(8, storage.alignment);
            NativeDeclaration[] fields = packet.children.Select(module.GetDeclaration).Where(declaration => declaration.kind == "Field").ToArray();
            Assert.Equal(new[] { "code", "nested", "samples" }, fields.Select(field => field.name));
            Assert.Equal(new long?[] { 0, 8, 16 }, fields.Select(field => field.offset));
            NativeType nested = Resolve(fields[1].type!.Value);
            NativeDeclaration inner = module.GetDeclaration(nested.declaration!.Value);
            NativeDeclaration innerField = Assert.Single(inner.children.Select(module.GetDeclaration), declaration => declaration.kind == "Field");
            Assert.Equal("value", innerField.name);
            Assert.Equal(0, innerField.offset);
            Assert.Equal(8, Resolve(innerField.type!.Value).size);
            NativeType array = Resolve(fields[2].type!.Value);
            Assert.Equal(NativeTypeCategory.Array, array.category);
            Assert.Equal(2, array.arrayLength);
            Assert.Equal(4, array.size);
            NativeType alias = module.GetType(array.elementType!.Value);
            Assert.Equal(NativeTypeCategory.Alias, alias.category);
            Assert.Equal("Sample", alias.spelling);
            Assert.False(Resolve(alias.id).isSigned);
            Assert.DoesNotContain(module.declarations, declaration => declaration.name is "UnreferencedSdkRecord" or "unreferenced_system_function"
                or "unreferenced_method" or "unreferenced_static_method");

            NativeType Resolve(StableId id)
            {
                NativeType type = module.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
                return type;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
