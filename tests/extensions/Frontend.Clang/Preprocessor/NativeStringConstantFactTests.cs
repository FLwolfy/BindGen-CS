using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeStringConstantFactTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C, "x86_64-pc-windows-msvc", 2)]
    [InlineData(SourceLanguage.Cpp, "x86_64-pc-windows-msvc", 2)]
    [InlineData(SourceLanguage.C, "aarch64-unknown-linux-gnu", 4)]
    [InlineData(SourceLanguage.Cpp, "aarch64-unknown-linux-gnu", 4)]
    [InlineData(SourceLanguage.C, "powerpc64-unknown-linux-gnu", 4)]
    public async Task CompilerLiteralUnitsRetainEmbeddedZerosConcatenationUnicodeAndTargetWideWidth(SourceLanguage language, string triple, int wideWidth)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-literal-facts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "literals.h");
            await File.WriteAllTextAsync(header, """
                #define PLAIN "A\0B" "C"
                #define CHAIN PLAIN
                #define OPAQUE "\xFF\xC3\xA9"
                #define UTF8_TEXT u8"\U0001F600\0Z"
                #define UTF16_TEXT u"\U0001F600\0Z"
                #define UTF32_TEXT U"\U0001F600\0Z"
                #define WIDE_TEXT L"\U0001F600\0Z"
                static const char NAMED_ARRAY[] = "Q\0R";
                typedef const char ConstantText[4];
                static ConstantText TYPEDEF_ARRAY = "T\0U";
                static char MUTABLE_ARRAY[] = "changeable";
                static const volatile char VOLATILE_ARRAY[] = "observed";
                static const char PADDED_ARRAY[8] = "Q";
                #define PADDED_ALIAS PADDED_ARRAY
                #ifndef __cplusplus
                static const char UNTERMINATED_ARRAY[3] = "ABC";
                #define UNTERMINATED_ALIAS UNTERMINATED_ARRAY
                #endif
                #ifdef __cplusplus
                #define RAW_TEXT R"tag(A\nB)tag"
                #endif
                """);
            NativeTargetDescriptor target = new(new("literal-test"), "test", "test", "test", triple, new());
            NativeModule module = (await new ClangFrontend(host).ParseAsync(new(target, language, [header]) { pathBase = root }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Equal(new uint[] { 65, 0, 66, 67, 0 }, Value("PLAIN").codeUnits);
            Assert.Equal(Value("PLAIN").codeUnits.ToArray(), Value("CHAIN").codeUnits.ToArray());
            Assert.Equal(new uint[] { 255, 195, 169, 0 }, Value("OPAQUE").codeUnits);
            Assert.Equal(new uint[] { 0xF0, 0x9F, 0x98, 0x80, 0, 90, 0 }, Value("UTF8_TEXT").codeUnits);
            Assert.Equal(new uint[] { 0xD83D, 0xDE00, 0, 90, 0 }, Value("UTF16_TEXT").codeUnits);
            Assert.Equal(new uint[] { 0x1F600, 0, 90, 0 }, Value("UTF32_TEXT").codeUnits);
            Assert.Equal(wideWidth, Value("WIDE_TEXT").unitWidth);
            Assert.Equal(wideWidth == 2 ? new uint[] { 0xD83D, 0xDE00, 0, 90, 0 } : [0x1F600, 0, 90, 0], Value("WIDE_TEXT").codeUnits);
            Assert.Equal(new uint[] { 81, 0, 82, 0 }, Value("NAMED_ARRAY").codeUnits);
            Assert.Equal(new uint[] { 84, 0, 85, 0 }, Value("TYPEDEF_ARRAY").codeUnits);
            Assert.Null(Assert.Single(module.declarations, declaration => declaration.name == "MUTABLE_ARRAY").stringConstant);
            Assert.Null(Assert.Single(module.declarations, declaration => declaration.name == "VOLATILE_ARRAY").stringConstant);
            foreach (string name in new[] { "PADDED_ARRAY", "PADDED_ALIAS" })
                Assert.Null(Assert.Single(module.declarations, declaration => declaration.name == name).stringConstant);
            if (language == SourceLanguage.C)
                foreach (string name in new[] { "UNTERMINATED_ARRAY", "UNTERMINATED_ALIAS" })
                    Assert.Null(Assert.Single(module.declarations, declaration => declaration.name == name).stringConstant);
            if (language == SourceLanguage.Cpp) Assert.Equal(new uint[] { 65, 92, 110, 66, 0 }, Value("RAW_TEXT").codeUnits);

            NativeStringConstant Value(string name) => Assert.Single(module.declarations, declaration => declaration.name == name).stringConstant
                ?? throw new InvalidOperationException("No literal fact: " + name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
