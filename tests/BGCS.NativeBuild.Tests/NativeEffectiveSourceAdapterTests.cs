using System;
using System.IO;
using System.Text.Json;
using BGCS.NativeBuild.Validation;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeEffectiveSourceAdapterTests
{
    [Fact]
    public void OnlyResolvedSdkLineMetadataChangesAndUserClassificationResumes()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-sdk-marker-tests");
        string system = JsonSerializer.Serialize(Path.Combine(root, "sdk", "quoted file.hpp"));
        string sibling = JsonSerializer.Serialize(Path.Combine(root, "sdk-other", "user.hpp"));
        string user = JsonSerializer.Serialize(Path.Combine(root, "user.hpp"));
        string source = "#line 4 " + system + "\r\nstruct S { int private_value; };\r\n#line 17 " + sibling
            + "\r\nconst char* literal = \"#line 4 sdk\";\r\n#line 9 " + user + "\r\nstruct U { S field; };\r\n";
        string actual = NativeEffectiveSourceAdapter.PreserveSystemHeaderMarkers(source, [Path.Combine(root, "sdk")]);
        Assert.Equal("# 4 " + system + " 3\r\nstruct S { int private_value; };\r\n# 17 " + sibling
            + "\r\nconst char* literal = \"#line 4 sdk\";\r\n# 9 " + user + "\r\nstruct U { S field; };\r\n", actual);
    }

    [Fact]
    public void LineMetadataLookingRawStringsAndCommentsRemainByteExact()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-sdk-marker-tests");
        string system = JsonSerializer.Serialize(Path.Combine(root, "sdk", "quoted file.hpp"));
        string literals = "const char* text = R\"delimiter(\n#line 17 " + system + "\n)delimiter\";\n"
            + "/*\n#line 18 " + system + "\n*/\n"
            + "// continued comment \\\n#line 19 " + system + "\n";
        string source = literals + "#line 20 " + system + "\nstruct Actual { int value; };\n";
        string actual = NativeEffectiveSourceAdapter.PreserveSystemHeaderMarkers(source, [Path.Combine(root, "sdk")]);
        Assert.Equal(literals + "# 20 " + system + " 3\nstruct Actual { int value; };\n", actual);
    }
}
