using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedRecordPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitRecordConveniencesCompileAndRunWithoutChangingStorage(bool singleFile)
    {
        ManagedRecordRuleRequest rule = Rule();
        ManagedApiModel model = Model(rule);
        NeutralBackendTests.MemorySink sink = new();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("unused", sink) { singleFile = singleFile });
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink) { singleFile = singleFile }, CancellationToken.None);
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        string consumer = """
            using System.Runtime.InteropServices;
            namespace Generated.Example;
            public static class CarrierProbe {
                public static int Run() {
                    RenamedRecord invalid = new(-1, ulong.MaxValue);
                    RenamedRecord valid = new(42, ulong.MaxValue);
                    if (invalid.IsValid || !valid.IsValid || valid.Id != 42 || valid.Counter != ulong.MaxValue) return 1;
                    if (RenamedRecord.NativeSize != Marshal.SizeOf<RenamedRecord>() || Marshal.OffsetOf<RenamedRecord>("Counter").ToInt32() != 8) return 2;
                    return 42;
                }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create("RecordPresentation_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            references.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.RecordPresentation.Test", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            Assert.Equal(42, assembly.GetType("Generated.Example.CarrierProbe", true)!.GetMethod("Run")!.Invoke(null, null));
        }
        finally { load.Unload(); }
        int mutations = 0;
        var changed = sink.sources.Select(source =>
        {
            SyntaxNode root = CSharpSyntaxTree.ParseText(source.Value).GetRoot();
            LiteralExpressionSyntax[] constants = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(variable => variable.Identifier.ValueText == "NativeSize")
                .Select(variable => (LiteralExpressionSyntax)variable.Initializer!.Value).ToArray();
            mutations += constants.Length;
            return (source.Key, Text: root.ReplaceNodes(constants, (
                original,
                _
            ) =>
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(15)).WithTriviaFrom(original)).ToFullString());
        }).ToArray();
        Assert.Equal(1, mutations);
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "unused"));
    }

    [Theory]
    [InlineData("unknown-field")]
    [InlineData("size-collision")]
    [InlineData("validity-collision")]
    [InlineData("sentinel-overflow")]
    [InlineData("overlap")]
    public void InvalidRecordConveniencesFailBeforeEmission(string invalid)
    {
        ManagedRecordRuleRequest rule = Rule();
        rule = invalid switch
        {
            "unknown-field" => rule with { constructorFields = ["missing"] },
            "size-collision" => rule with { fields = [new("value") { name = "NativeSize" }] },
            "validity-collision" => rule with { validity = new("value", new(ManagedDefaultValueKind.Integer, "-1"), "Counter") },
            "sentinel-overflow" => rule with { validity = new("value", new(ManagedDefaultValueKind.Integer, "2147483648")) },
            _ => rule
        };
        NeutralBackendTests.MemorySink sink = new();
        Action plan = () => new CSharpTestPlanner().Plan(Model(rule, overlap: invalid == "overlap"), new("unused", sink));
        Exception failure = invalid == "overlap" ? Assert.Throws<NotSupportedException>(plan) : Assert.Throws<ArgumentException>(plan);
        Assert.Contains(invalid switch
        {
            "unknown-field" => "exactly one native field",
            "size-collision" => "size constant conflicts",
            "validity-collision" => "validity property conflicts",
            "sentinel-overflow" => "outside its exact friendly type range",
            _ => "overlapping native storage fields"
        }, failure.Message, StringComparison.Ordinal);
        Assert.Empty(sink.sources);
    }

    private static ManagedRecordRuleRequest Rule() => new("NativeRecord")
    {
        name = "RenamedRecord",
        documentation = "Carrier <value> && data, not a native constructor.",
        fields = [new("value") { name = "Id", documentation = "The native ID < 0 sentinel." }, new("counter") { name = "Counter" }],
        constructorFields = ["value", "counter"],
        emitSizeConstant = true,
        validity = new("value", new(ManagedDefaultValueKind.Integer, "-1"))
    };

    private static ManagedApiModel Model(
        ManagedRecordRuleRequest rule,
        bool overlap = false
    ) {
        ManagedApiModel original = NeutralBackendTests.Model();
        InteropType integer = original.interop.types[0];
        InteropType count = new(new("count"), "unsigned long long", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "ULongLong", isSigned = false };
        InteropType record = new(new("native-record"), "NativeRecord", NativeTypeCategory.Record, 16, 8)
        {
            declarationName = "NativeRecord",
            recordKind = overlap ? "Union" : "Struct",
            fields = [new(new("record-value"), "value", integer.id, 0, 0, null), new(new("record-counter"), "counter", count.id, overlap ? 0 : 8, overlap ? 0 : 64, null)]
        };
        InteropModule source = original.interop;
        InteropModule module = new(source.libraryId, source.targetId, source.pointerSize, source.types.Add(count).Add(record), source.functions, source.diagnostics)
        {
            byteOrder = source.byteOrder,
            constants = source.constants,
            functionAliases = source.functionAliases,
            stringConstants = source.stringConstants,
            virtualProxies = source.virtualProxies
        };
        return new DefaultManagedProjection().Project(module, new("Generated.Example", "NativeApi") { records = [rule] }, CancellationToken.None);
    }
}
