using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BGCS.Analysis.Bridge;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.Infrastructure.Execution;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeConfiguredExactTypeFamilyTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task NonTemplateStringAliasAndPathKeepCountedUtf8AndTheirActualNativeIdentity(BindingImportMode mode, bool sourceRuntime)
    {
        string root = NewRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "exact.hpp"), Header);
            BindingConfiguration configuration = Configuration(mode, sourceRuntime);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            NativeBridgePlan bridge = target.bridge!;
            Assert.All(bridge.familyBindings, binding => { Assert.Null(binding.nativeTemplate); Assert.NotNull(binding.nativeType); });
            NativeValueProtocol text = Assert.Single(target.interop.types, type => type.valueProtocol?.family == NativeValueFamily.String).valueProtocol!;
            Assert.Equal(NativeTextEncoding.Utf8, text.text!.encoding);
            Assert.Equal(1, text.text.codeUnitWidth);
            Assert.Equal(EvidenceLevel.UserAsserted, text.text.encodingEvidence.level);
            Assert.True(text.text.permitsEmbeddedZero);
            Assert.Equal(NativePathFormat.GenericUtf8, Assert.Single(target.interop.types, type => type.valueProtocol?.family == NativeValueFamily.Path).valueProtocol!.path!.format);
            foreach (BridgeExport owned in bridge.exports.Where(export => export.resultRelease is not null)) Assert.Null(owned.resultOwnership);
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            CSharpEmissionPlan plan = new HostTestEmissionPlanner().Plan(target.managed!, new(configuration.library.nativeName, new NoWrites())
                { importMode = mode, runtimeNamespace = sourceRuntime ? "Generated.Exact.Runtime" : "BGCS.Runtime" });
            string context = sourceRuntime ? "ExactApiContext" : "ExactApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, configuration.library.nativeName, $$"""
                using System;
                using System.Text;
                using Generated.Exact;
                using ApiContext=Generated.Exact.{{context}};
                public static unsafe class ExactConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        {{ConsumerBody(bridge, plan)}}
                        return 42;
                    }
                }
                """, "ExactConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConflictingAliasesAndEncodingPreservePublicationWhileInvalidNativePathsFailConsumerCompilation()
    {
        string root = NewRoot();
        try
        {
            string header = Path.Combine(root, "exact.hpp");
            await File.WriteAllTextAsync(header, Header);
            BindingConfiguration configuration = Configuration(BindingImportMode.FunctionTable, false);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult first = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(first.success, first.GetDiagnosticSummary());
            string[] previous = Hashes(first.outputRoot);
            foreach ((BindingConfiguration request, string source, string cause) in new[]
            {
                (configuration with { bridge = configuration.bridge with { cpp = configuration.bridge.cpp with { familyBindings = configuration.bridge.cpp.familyBindings.Add(configuration.bridge.cpp.familyBindings[0] with { nativeType = "exact::Text" }) } } }, Header, "canonical native class"),
                (configuration with { bridge = configuration.bridge with { cpp = configuration.bridge.cpp with { familyBindings = configuration.bridge.cpp.familyBindings.SetItem(0, configuration.bridge.cpp.familyBindings[0] with { stringEncoding = NativeTextEncoding.Utf16 }) } } }, Header, "code-unit storage")
            })
            {
                await File.WriteAllTextAsync(header, source);
                GenerationResult rejected = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(request, root));
                Assert.False(rejected.success);
                Assert.Contains(cause, rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(previous, Hashes(first.outputRoot));
            }
            string invalid = Header.Replace("std::u8string generic_u8string()const{return u8\"dir/\\U0001F600.txt\";}",
                "std::u16string generic_u8string()const{return u\"wrong\";}", StringComparison.Ordinal);
            Assert.NotEqual(Header, invalid);
            await File.WriteAllTextAsync(header, invalid);
            GenerationResult generated = await host.GenerateAsync(new(configuration, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            Assert.True(generated.published);
            string assertionSource = Assert.Single(generated.artifacts.Where(artifact => artifact.mediaType == "text/x-c++")
                .Select(artifact => Path.Combine(generated.outputRoot, artifact.relativePath)),
                path => File.ReadAllText(path).Contains("one-byte UTF-8 path code units", StringComparison.Ordinal));
            string includes = Path.Combine(generated.outputRoot, Assert.Single(generated.targetModels).targetId, "native", "include");
            ImmutableArray<string> arguments = ["-std=c++23", "-fsyntax-only", "-I", root, "-I", includes,
                .. NativeConsumerHarness.CompilerSdkArguments(), assertionSource];
            var compiled = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(true), arguments, root, TimeSpan.FromMinutes(1));
            Assert.False(compiled.timedOut);
            Assert.NotEqual(0, compiled.exitCode);
            Assert.Contains("one-byte UTF-8 path code units", compiled.standardOutput + compiled.standardError, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string ConsumerBody(NativeBridgePlan bridge, CSharpEmissionPlan plan)
    {
        StringBuilder source = new();
        string Call(BridgeExport export, params string[] arguments)
        {
            CSharpMethodPlan method = plan.methods.Single(method => method.nativeExport == export.id);
            Assert.Equal(method.parameterTypes.Length, arguments.Length);
            return "api.Raw." + method.name + "(" + string.Join(',', arguments.Select((argument, index) => "(" + method.parameterTypes[index] + ")(" + argument + ")")) + ")";
        }
        CSharpMethodPlan errorRelease = plan.methods.Single(method => method.exportName == bridge.errorReleaseExport);
        string releaseError = "api.Raw." + errorRelease.name + "((" + errorRelease.parameterTypes[0] + ")error);error=0;";
        foreach (string factory in new[] { "MakeText", "MakePath" })
        {
            bool text = factory == "MakeText";
            BridgeExport make = Assert.Single(bridge.exports, export => export.operation.declaration is StableId declaration
                && bridge.source.GetDeclaration(declaration).qualifiedName == "exact::" + factory);
            StableId owner = StandardLibraryFactReader.Resolve(bridge.source, make.nativeResultType!.Value).id;
            BridgeExport Access(StandardValueAction action) => Assert.Single(bridge.exports, export => export.operation.ownerType == owner && export.operation.standardValue?.action == action);
            BridgeExport destroy = Assert.Single(bridge.exports, export => export.operation.kind == BridgeOperationKind.Destroy && export.operation.ownerType == owner);
            byte[] expected = Encoding.UTF8.GetBytes(text ? "A\0B😀" : "dir/😀.txt");
            source.AppendLine("{nint holder=0,error=0;ulong count=0;sbyte unit=0;byte pathByte=0;");
            void Success(string invocation, string condition = "") => source.Append("if(").Append(invocation).Append("!=0 || error!=0")
                .Append(condition.Length == 0 ? "" : " || " + condition).AppendLine(")throw new InvalidOperationException(\"exact native operation\");");
            Success(Call(make, "&holder", "&error"), "holder==0");
            source.AppendLine("try {");
            Success(Call(Access(text ? StandardValueAction.Count : StandardValueAction.PathUtf8Count), "holder", "&count", "&error"), "count!=" + expected.Length);
            foreach ((byte value, int index) in expected.Select((value, index) => (value, index)))
                Success(Call(Access(text ? StandardValueAction.GetAt : StandardValueAction.PathUtf8Byte), "holder", index + "UL", text ? "&unit" : "&pathByte", "&error"),
                    text ? "unchecked((byte)unit)!=" + value : "pathByte!=" + value);
            if (text)
            {
                Success(Call(Access(StandardValueAction.SetAt), "holder", "0UL", "90", "&error"));
                Success(Call(Access(StandardValueAction.GetAt), "holder", "0UL", "&unit", "&error"), "unit!=90");
                Success(Call(Access(StandardValueAction.Append), "holder", "33", "&error"));
                Success(Call(Access(StandardValueAction.Count), "holder", "&count", "&error"), "count!=8");
                Success(Call(Access(StandardValueAction.GetAt), "holder", "7UL", "&unit", "&error"), "unit!=33");
            }
            source.Append("if(").Append(Call(Access(text ? StandardValueAction.GetAt : StandardValueAction.PathUtf8Byte), "holder", "999UL", text ? "&unit" : "&pathByte", "&error"))
                .AppendLine("==0 || error==0)throw new InvalidOperationException(\"exact range boundary\");").AppendLine(releaseError)
                .AppendLine("_ = unit;_ = pathByte;")
                .AppendLine("}finally{")
                .Append("if(error!=0){").Append(releaseError).AppendLine("}");
            Success(Call(destroy, "holder", "&error"));
            source.AppendLine("}}");
        }
        return source.ToString();
    }

    private static BindingConfiguration Configuration(BindingImportMode mode, bool sourceRuntime) => new()
    {
        schemaVersion = 2, library = new() { id = "exact", nativeName = "exact_fixture" },
        source = new() { language = SourceLanguage.Cpp, headers = ["exact.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false,
            selection = new() { includeDeclarations = ["exact::MakeText", "exact::MakePath"] } },
        api = new() { @namespace = "Generated.Exact", name = "ExactApi", importMode = mode,
            style = sourceRuntime ? BGCS.Core.Managed.ManagedApiStyle.Static : BGCS.Core.Managed.ManagedApiStyle.Instance, nestGeneratedTypesInApi = sourceRuntime },
        bridge = new() { cpp = new() { familyBindings = [
            new() { nativeType = "exact::TextAlias", family = StandardValueKind.String, stringEncoding = NativeTextEncoding.Utf8, evidence = new(EvidenceLevel.UserAsserted, "exact-UTF8-string-protocol") },
            new() { nativeType = "exact::Path", family = StandardValueKind.Path, pathFormat = NativePathFormat.GenericUtf8, evidence = new(EvidenceLevel.UserAsserted, "exact-generic-UTF8-path-protocol") }] } },
        runtime = new() { mode = sourceRuntime ? "source" : "package" }, output = new() { layout = sourceRuntime ? "singleFile" : "split" },
         cache = new() { enabled = false }
    };

    private static string NewRoot() { string root = Path.Combine(Path.GetTempPath(), "bgcs-configured-exact-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private static string[] Hashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
    private sealed class NoWrites : IArtifactSink { public string WriteText(string relativePath, string content) => throw new InvalidOperationException("Planning cannot emit artifacts."); }

    private const string Header = """
        #include <string>
        namespace exact {
        class Text {
            std::string m_units=std::string("A\0B\xF0\x9F\x98\x80",7);
        public:
            using value_type=char;
            size_t size()const{return m_units.size();}
            const char& operator[](size_t index)const{return m_units[index];}
            char& operator[](size_t index){return m_units[index];}
            void push_back(char value){m_units.push_back(value);}
            void reserve(size_t size){m_units.reserve(size);}
        };
        using TextAlias=Text;
        class Path {
        public:
            std::u8string generic_u8string()const{return u8"dir/\U0001F600.txt";}
        };
        inline TextAlias MakeText(){return Text();}
        inline Path MakePath(){return Path();}
        }
        """;
}
