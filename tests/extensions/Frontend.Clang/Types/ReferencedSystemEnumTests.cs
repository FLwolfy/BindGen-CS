using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Frontend.Clang;
using BGCS.Targets.Desktop.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ReferencedSystemEnumTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ReferencedSdkEnum_RetainsExactValuesAndSystemOriginWithoutPublishingUnreferencedSdkApi(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-referenced-sdk-enum-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk");
        Directory.CreateDirectory(sdk);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sdk, "sdk_enums.h"), """
                enum SdkChoice { SdkNegative = -7, SdkPositive = 23 };
                enum UnreferencedSdkChoice { SdkIgnored = 42 };
                int unreferenced_system_function(void);
                """);
            string header = Path.Combine(root, "entry.h");
            await File.WriteAllTextAsync(header, "#include <sdk_enums.h>\nenum SdkChoice transform_choice(enum SdkChoice input);");
            FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), language, [header])
            { systemIncludeDirectories = [sdk], pathBase = root, parseSystemIncludes = false, parseMacros = false };
            NativeModule module = (await new ClangFrontend(host).ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration declaration = Assert.Single(module.declarations, fact => fact.name == "SdkChoice");
            Assert.True(declaration.isSystemDeclaration);
            NativeType enumeration = module.GetType(declaration.type!.Value);
            Assert.Equal(NativeTypeCategory.Enumeration, enumeration.category);
            Assert.Equal(4, enumeration.size);
            Assert.True(module.GetType(enumeration.elementType!.Value).isSigned);
            NativeDeclaration[] values = declaration.children.Select(module.GetDeclaration).ToArray();
            Assert.Equal(new[] { "SdkNegative", "SdkPositive" }, values.Select(value => value.name));
            Assert.Equal(new[] { "-7", "23" }, values.Select(value => value.evaluatedIntegralValue));
            Assert.All(values, value => { Assert.Equal("EnumItem", value.kind); Assert.True(value.isSystemDeclaration); });
            Assert.DoesNotContain(module.declarations, fact => fact.name is "UnreferencedSdkChoice" or "SdkIgnored" or "unreferenced_system_function");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
