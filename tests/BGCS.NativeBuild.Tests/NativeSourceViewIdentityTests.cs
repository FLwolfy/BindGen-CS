using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeSourceViewIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OctalUtf8OwnedLineMetadataHasTheSameIdentityAcrossCandidateDirectories(bool nativeSeparators)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-source-contribution 验收 & identity");
        string first = Path.Combine(root, ".bgcs-staging-first", "bridge.h");
        string second = Path.Combine(root, ".bgcs-staging-second", "bridge.h");
        string firstSource = "# 1 \"" + Encoded(first, nativeSeparators) + "\" 1 3\r\nint original(int value);\r\n";
        string secondSource = "# 1 \"" + Encoded(second, nativeSeparators) + "\" 1 3\r\nint original(int value);\r\n";
        string expected = "# 1 \"native/include/bridge.h\" 1 3\r\nint original(int value);\r\n";
        Assert.Equal(expected, Normalize(firstSource, first));
        Assert.Equal(expected, Normalize(secondSource, second));
    }

    [Fact]
    public void EscapedFilenameLookingLanguageTokensAndUnmappedMetadataStayExact()
    {
        string mapped = Path.Combine(Path.GetTempPath(), "bgcs-owned 验收 &", "bridge.h");
        string encoded = Encoded(mapped, nativeSeparators: false);
        string language = "const char* location = \"" + encoded + "\";\n"
            + "#define FILE_TOKEN \"" + encoded + "\"\n"
            + "const char* raw = R\"delimiter(\n# 5 \"" + encoded + "\"\n)delimiter\";\n"
            + "/*\n# 6 \"" + encoded + "\"\n*/\n";
        string original = language + "# 7 \"" + encoded + "\" 2\n"
            + "# 8 \"unmapped\\351\\252\\214.h\"\n";
        Assert.Equal(language + "# 7 \"native/include/bridge.h\" 2\n"
            + "# 8 \"unmapped\\351\\252\\214.h\"\n", Normalize(original, mapped));
    }

    [Theory]
    [InlineData("\\000")]
    [InlineData("\\012")]
    [InlineData("\\015")]
    [InlineData("\\377")]
    [InlineData("\\300\\257")]
    [InlineData("\\355\\240\\200")]
    [InlineData("\\777")]
    [InlineData("\\u9a8c")]
    public void InvalidOctalUtf8OrControlMetadataCannotBeRewrittenAsAValidPath(string escape)
    {
        string system = Path.Combine(Path.GetTempPath(), "bgcs-owned", "sdk");
        string original = "#line 1 \"" + Encoded(Path.Combine(system, "bad"), nativeSeparators: false) + escape + ".h\"\nint kept;\n";
        Assert.Equal(original, NativeEffectiveSourceAdapter.PreserveSystemHeaderMarkers(original, [system]));
    }

    [Fact]
    public void EscapedBackslashesQuotesAndLiteralOctalDigitsRetainTheirExistingMeaning()
    {
        string mapped = Path.Combine(Path.GetTempPath(), "bgcs-owned", "123", "bridge.h");
        string original = "#line 9 \"" + Encoded(mapped, nativeSeparators: true) + "\"\n";
        Assert.Equal("#line 9 \"native/include/bridge.h\"\n", Normalize(original, mapped));
        string unknown = "# 4 \"relative\\\\123\\\"quoted.h\"\n";
        Assert.Equal(unknown, Normalize(unknown, mapped));
    }

    private static string Normalize(string text, string mapped)
    {
        string root = Path.GetDirectoryName(mapped)!;
        FrontendRequest request = new(new(new("identity-wasm32"), "emscripten", "wasm32", "emscripten",
            "wasm32-unknown-emscripten", new(), byteOrder: BGCS.Contracts.Native.NativeByteOrder.LittleEndian), SourceLanguage.C, [mapped])
        {
            pathBase = root,
            sourcePathMappings = ImmutableDictionary<string, string>.Empty.Add(mapped, "native/include/bridge.h")
        };
        Type type = typeof(NativeSourceViewPreprocessor).Assembly.GetType("BGCS.NativeBuild.Planning.NativeSourceViewIdentity", throwOnError: true)!;
        object identity = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [Path.Combine(root, "aggregate.c"), request], null)!;
        return (string)type.GetMethod("Normalize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(identity, [text])!;
    }

    private static string Encoded(string path, bool nativeSeparators)
    {
        string file = nativeSeparators ? path : path.Replace('\\', '/');
        StringBuilder encoded = new();
        foreach (byte value in Encoding.UTF8.GetBytes(file))
        {
            if (value >= 128) encoded.Append('\\').Append(Convert.ToString(value, 8).PadLeft(3, '0'));
            else if (value is (byte)'\\' or (byte)'"') encoded.Append('\\').Append((char)value);
            else encoded.Append((char)value);
        }
        return encoded.ToString();
    }
}

