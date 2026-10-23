using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class FlatIdentifierNamingTests
{
    [Theory]
    [InlineData("CREATEWINDOW", "CreateWindow")]
    [InlineData("DEPTHBUFFER", "DepthBuffer")]
    [InlineData("GETMOUSEPOS", "GetMousePos")]
    public void VersionedEnglishWordsRestoreTheEstablishedFlatIdentifierExamples(
        string input,
        string expected
    )
        => Assert.Equal(expected, ManagedNamingResolver.Convert(input, ManagedNameCasing.Pascal, English()));

    [Theory]
    [InlineData("CREATEWINDOW", "Createwindow")]
    [InlineData("DEPTHBUFFER", "Depthbuffer")]
    [InlineData("GETMOUSEPOS", "Getmousepos")]
    public void NoSegmentationRemainsTheDefault(
        string input,
        string expected
    )
        => Assert.Equal(expected, ManagedNamingResolver.Convert(input, ManagedNameCasing.Pascal, new()));

    [Theory]
    [InlineData("ABCAB", "AbcAb")]
    [InlineData("QZX123ABCQZX", "Qzx123AbcQzx")]
    [InlineData("ABC_ABC", "AbcAbc")]
    [InlineData("createwindow", "Createwindow")]
    [InlineData("CreateWindow", "CreateWindow")]
    public void CustomLongestPrefixesPreserveUnknownAndNumericFragments(
        string input,
        string expected
    )
        => Assert.Equal(expected, ManagedNamingResolver.Convert(input, ManagedNameCasing.Pascal, Custom("A", "AB", "ABC")));

    [Theory]
    [InlineData(ManagedNameCasing.Preserve, "CREATEWINDOW")]
    [InlineData(ManagedNameCasing.Camel, "createWindow")]
    [InlineData(ManagedNameCasing.Snake, "create_window")]
    [InlineData(ManagedNameCasing.ScreamingSnake, "CREATE_WINDOW")]
    public void OutputCasingUsesTheSelectedWordBoundaries(
        ManagedNameCasing casing,
        string expected
    )
        => Assert.Equal(expected, ManagedNamingResolver.Convert("CREATEWINDOW", casing, English()));

    [Theory]
    [InlineData("HTTPServer2D_value")]
    [InlineData("createwindow")]
    [InlineData("getMousePOS")]
    public void LowercaseAndMixedCaseSourcesRetainTheExistingBoundaryAlgorithm(string input)
        => Assert.Equal(ManagedNamingResolver.Convert(input, ManagedNameCasing.Pascal),
            ManagedNamingResolver.Convert(input, ManagedNameCasing.Pascal, English()));

    [Theory]
    [InlineData("undefined-mode")]
    [InlineData("uninitialized")]
    [InlineData("none-with-words")]
    [InlineData("english-with-words")]
    [InlineData("empty-custom")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("separator")]
    [InlineData("digit")]
    [InlineData("long-word")]
    [InlineData("many-words")]
    public void InvalidVocabulariesNeverFallBackSilently(string fault)
    {
        ManagedNamingPolicy policy = fault switch
        {
            "undefined-mode" => new() { flatIdentifierSegmentation = (ManagedFlatIdentifierSegmentation)999 },
            "uninitialized" => new() { flatIdentifierWords = default },
            "none-with-words" => new() { flatIdentifierWords = ["word"] },
            "english-with-words" => English() with { flatIdentifierWords = ["word"] },
            "empty-custom" => Custom(),
            "null" => Custom(new string[] { null! }),
            "empty" => Custom(""),
            "duplicate" => Custom("word", "WORD"),
            "separator" => Custom("two_words"),
            "digit" => Custom("word2"),
            "long-word" => Custom(new string('a', 129)),
            _ => Custom(Enumerable.Repeat("word", 4097).ToArray())
        };
        Assert.Throws<ArgumentException>(() => ManagedNamingResolver.Validate(policy));
    }

    [Fact]
    public async Task OneDetachedModuleCanBeProjectedConcurrentlyWithDifferentImmutableVocabularies()
    {
        InteropModule module = Module();
        ManagedNamingPolicy first = Custom("create", "window");
        ManagedNamingPolicy second = Custom("createwindow");
        string[] names = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
            Project(module, index % 2 == 0 ? first : second).methods.Single().name)));
        Assert.All(names.Where((
            _,
            index
        ) => index % 2 == 0), name => Assert.Equal("CreateWindow", name));
        Assert.All(names.Where((
            _,
            index
        ) => index % 2 != 0), name => Assert.Equal("Createwindow", name));
        Assert.Equal("CREATEWINDOW", Assert.Single(module.functions).exportName);
    }

    [Fact]
    public void ExactMappingsAndUnspecifiedCategoryCasingRetainTheirExistingBehavior()
    {
        InteropModule module = Module();
        ManagedApiModel ordinary = new DefaultManagedProjection().Project(module, new("Names", "Api")
        { naming = new() { flatIdentifierSegmentation = ManagedFlatIdentifierSegmentation.English } }, CancellationToken.None);
        Assert.Equal("CREATEWINDOW", Assert.Single(ordinary.methods).name);
        ManagedApiModel exact = new DefaultManagedProjection().Project(module, new("Names", "Api")
        { naming = English(), names = ImmutableDictionary<string, string>.Empty.Add("CREATEWINDOW", "ExactSpelling") }, CancellationToken.None);
        Assert.Equal("ExactSpelling", Assert.Single(exact.methods).name);
    }

    [Fact]
    public void VeryLongUnknownTokensRetainEveryCharacterWithoutSearchingEveryDictionaryWord()
    {
        string unknown = new('Z', 100000);
        Assert.Equal("AZ" + new string('z', 99999) + "A", ManagedNamingResolver.Convert("A" + unknown + "A", ManagedNameCasing.Pascal, Custom("A")));
    }

    private static ManagedNamingPolicy English() => new()
    { flatIdentifierSegmentation = ManagedFlatIdentifierSegmentation.English, functions = new() { casing = ManagedNameCasing.Pascal } };
    private static ManagedNamingPolicy Custom(params string[] words) => new()
    { flatIdentifierSegmentation = ManagedFlatIdentifierSegmentation.Custom, flatIdentifierWords = words.ToImmutableArray(), functions = new() { casing = ManagedNameCasing.Pascal } };
    private static ManagedApiModel Project(
        InteropModule module,
        ManagedNamingPolicy policy
    )
        => new DefaultManagedProjection().Project(module, new("Names", "Api") { naming = policy }, CancellationToken.None);
    private static InteropModule Module()
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        return new("flat", "windows-x64-msvc", 8, [integer], [new(new("create"), "CREATEWINDOW", integer.id, [], "C")], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
    }
}
