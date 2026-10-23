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

public sealed class NativeConfiguredAssociativeFamilyTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExactCustomAssociativeAndExpectedProtocolsUseActualNativeValuesAndBoundaries(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string root = NewRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "associative.hpp"), Header);
            BindingConfiguration configuration = Configuration(mode, sourceRuntime);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            foreach (NativeValueFamily family in new[] { NativeValueFamily.Map, NativeValueFamily.Set, NativeValueFamily.Expected })
            {
                var values = target.interop.types.Where(type => type.valueProtocol?.family == family).Select(type => type.valueProtocol!).ToArray();
                Assert.NotEmpty(values);
                Assert.All(values, protocol => Assert.Equal(EvidenceLevel.UserAsserted, protocol.evidence.level));
                Assert.All(values.Where(protocol => protocol.sequence is not null), protocol => Assert.Equal(NativeLifetimeKind.Unknown, protocol.sequence!.readLifetime.kind));
            }
            NativeBridgePlan bridge = target.bridge!;
            Assert.Equal(3, bridge.familyBindings.Length);
            new CppBridgeOperationVerifier().Verify(bridge);
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string runtime = sourceRuntime ? "Generated.Associative.Runtime" : "BGCS.Runtime";
            CSharpEmissionPlan plan = new HostTestEmissionPlanner().Plan(target.managed!, new(configuration.library.nativeName, new NoWrites())
            { importMode = mode, runtimeNamespace = runtime });
            string context = sourceRuntime ? "AssociativeApiContext" : "AssociativeApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string body = ConsumerBody(bridge, plan);
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, configuration.library.nativeName, $$"""
                using System;
                using Generated.Associative;
                using ApiContext=Generated.Associative.{{context}};
                public static unsafe class AssociativeConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        {{body}}
                        return 42;
                    }
                }
                """, "AssociativeConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IncompatibleAliasesAccessorsAndMacroProofTokensFailConsumerCompilation()
    {
        string root = NewRoot();
        try
        {
            string header = Path.Combine(root, "associative.hpp");
            await File.WriteAllTextAsync(header, Header);
            BindingConfiguration configuration = Configuration(BindingImportMode.FunctionTable, false);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult first = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(first.success, first.GetDiagnosticSummary());
            foreach ((string source, string cause) in new[]
            {
                (Header.Replace("using mapped_type=V;", "using mapped_type=float;", StringComparison.Ordinal), "BGCS configured family requires exact native mapped type"),
                (Header.Replace("const E& error()const{return m_value.error();}", "float error()const{return 0;}", StringComparison.Ordinal), "BGCS configured family requires exact expected error access"),
                (Header + "\n#define begin(...) bgcs_changed_proof\n", "BGCS configured family proof token is macro-defined: begin")
            })
            {
                Assert.NotEqual(Header, source);
                await File.WriteAllTextAsync(header, source);
                GenerationResult generated = await host.GenerateAsync(new(configuration, root));
                Assert.True(generated.success, generated.GetDiagnosticSummary());
                Assert.True(generated.published);
                string assertionSource = Assert.Single(generated.artifacts.Where(artifact => artifact.mediaType == "text/x-c++")
                    .Select(artifact => Path.Combine(generated.outputRoot, artifact.relativePath)),
                    path => File.ReadAllText(path).Contains(cause, StringComparison.Ordinal));
                string includes = Path.Combine(generated.outputRoot, Assert.Single(generated.targetModels).targetId, "native", "include");
                ImmutableArray<string> arguments = ["-std=c++23", "-fsyntax-only", "-I", root, "-I", includes,
                    .. NativeConsumerHarness.CompilerSdkArguments(), assertionSource];
                var compiled = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(true), arguments, root, TimeSpan.FromMinutes(1));
                Assert.False(compiled.timedOut);
                Assert.NotEqual(0, compiled.exitCode);
                Assert.Contains(cause, compiled.standardOutput + compiled.standardError, StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string ConsumerBody(
        NativeBridgePlan bridge,
        CSharpEmissionPlan plan
    ) {
        StringBuilder source = new();
        CSharpMethodPlan releaseError = plan.methods.Single(method => method.exportName == bridge.errorReleaseExport);
        string ErrorRelease(string address) => "api.Raw." + releaseError.name + "((" + releaseError.parameterTypes[0] + ")(" + address + "));";
        string Call(
            BridgeExport export,
            params string[] arguments
        ) {
            CSharpMethodPlan method = plan.methods.Single(method => method.nativeExport == export.id);
            Assert.Equal(method.parameterTypes.Length, arguments.Length);
            return "api.Raw." + method.name + "(" + string.Join(",", arguments.Select((
                argument,
                index
            ) => "(" + method.parameterTypes[index] + ")(" + argument + ")")) + ")";
        }
        foreach (string name in new[] { "MakeMap", "MakeSet", "MakeBrokenSet", "MakeValue", "MakeError", "MakeVoidValue", "MakeVoidError" })
        {
            BridgeExport make = Assert.Single(bridge.exports, export => export.operation.declaration is StableId declaration
                && bridge.source.GetDeclaration(declaration).qualifiedName == "associative::" + name);
            StableId owner = StandardLibraryFactReader.Resolve(bridge.source, make.nativeResultType!.Value).id;
            BridgeExport Access(StandardValueAction action) => Assert.Single(bridge.exports, export => export.operation.ownerType == owner && export.operation.standardValue?.action == action);
            BridgeExport destroy = Assert.Single(bridge.exports, export => export.operation.kind == BridgeOperationKind.Destroy && export.operation.ownerType == owner);
            source.AppendLine("{ nint holder=0,error=0; int value=0;")
                .Append("if(").Append(Call(make, "&holder", "&error")).AppendLine("!=0 || holder==0 || error!=0)throw new InvalidOperationException(\"custom factory\");")
                .AppendLine("try {");
            if (name is "MakeMap" or "MakeSet" or "MakeBrokenSet")
            {
                source.AppendLine("ulong count=0;");
                source.Append("if(").Append(Call(Access(StandardValueAction.Count), "holder", "&count", "&error")).AppendLine("!=0 || error!=0 || count!=" + (name == "MakeBrokenSet" ? "4UL" : "2UL") + ")throw new InvalidOperationException(\"custom count\");");
                StandardValueAction valueAction = name == "MakeMap" ? StandardValueAction.GetMappedAt : StandardValueAction.GetAt;
                if (name == "MakeBrokenSet")
                {
                    Reject(Call(Access(valueAction), "holder", "2UL", "&value", "&error"));
                    Reject(Call(Access(valueAction), "holder", "3UL", "&value", "&error"));
                }
                else
                {
                    source.Append("if(").Append(Call(Access(valueAction), "holder", "0UL", "&value", "&error")).AppendLine("!=0 || error!=0 || value!=20)throw new InvalidOperationException(\"custom first value\");");
                    if (name == "MakeMap")
                    {
                        source.Append("if(").Append(Call(Access(StandardValueAction.GetKeyAt), "holder", "1UL", "&value", "&error")).AppendLine("!=0 || value!=2)throw new InvalidOperationException(\"custom key\");")
                            .Append("if(").Append(Call(Access(StandardValueAction.Insert), "holder", "1", "42", "&error")).AppendLine("!=0 || error!=0)throw new InvalidOperationException(\"custom insert\");")
                            .Append("if(").Append(Call(Access(valueAction), "holder", "0UL", "&value", "&error")).AppendLine("!=0 || value!=42)throw new InvalidOperationException(\"custom mapped replacement\");");
                    }
                    else source.Append("if(").Append(Call(Access(StandardValueAction.Append), "holder", "42", "&error")).AppendLine("!=0 || error!=0)throw new InvalidOperationException(\"custom set insert\");")
                        .Append("if(").Append(Call(Access(StandardValueAction.Count), "holder", "&count", "&error")).AppendLine("!=0 || count!=3)throw new InvalidOperationException(\"custom set mutation count\");")
                        .Append("if(").Append(Call(Access(valueAction), "holder", "2UL", "&value", "&error")).AppendLine("!=0 || value!=42)throw new InvalidOperationException(\"custom set mutated value\");");
                    Reject(Call(Access(valueAction), "holder", "999UL", "&value", "&error"));
                }
            }
            else
            {
                source.AppendLine("byte present=0;");
                bool valid = name is "MakeValue" or "MakeVoidValue";
                bool emptyValue = name.Contains("Void", StringComparison.Ordinal);
                source.Append("if(").Append(Call(Access(StandardValueAction.HasValue), "holder", "&present", "&error")).Append("!=0 || error!=0 || present!=").Append(valid ? 1 : 0).AppendLine(")throw new InvalidOperationException(\"custom expected state\");");
                if (!emptyValue)
                {
                    if (valid) source.Append("if(").Append(Call(Access(StandardValueAction.GetValue), "holder", "&value", "&error")).AppendLine("!=0 || error!=0 || value!=42)throw new InvalidOperationException(\"custom expected value\");");
                    else Reject(Call(Access(StandardValueAction.GetValue), "holder", "&value", "&error"));
                }
                if (valid) Reject(Call(Access(StandardValueAction.GetError), "holder", "&value", "&error"));
                else source.Append("if(").Append(Call(Access(StandardValueAction.GetError), "holder", "&value", "&error")).AppendLine("!=0 || error!=0 || value!=42)throw new InvalidOperationException(\"custom expected error\");");
            }
            source.AppendLine("} finally {")
                .Append("if(error!=0){").Append(ErrorRelease("error")).AppendLine("error=0;}")
                .Append("if(").Append(Call(destroy, "holder", "&error")).AppendLine("!=0){if(error!=0){" + ErrorRelease("error") + "}throw new InvalidOperationException(\"custom destroy\");}")
                .AppendLine("} }");

            void Reject(string invocation) => source.Append("value=0;if(").Append(invocation).AppendLine("==0 || error==0 || value!=0)throw new InvalidOperationException(\"missing custom boundary rejection\");")
                .Append(ErrorRelease("error")).AppendLine("error=0;");
        }
        return source.ToString();
    }

    private static BindingConfiguration Configuration(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        CppFamilyBinding Binding(
            string name,
            StandardValueKind family
        ) => new()
        {
            nativeTemplate = "associative::" + name,
            family = family,
            evidence = new(EvidenceLevel.UserAsserted, "fixture-explicit-associative-protocol:" + name)
        };
        return new()
        {
            schemaVersion = 2,
            library = new() { id = "associative", nativeName = "associative_fixture" },
            source = new()
            {
                language = SourceLanguage.Cpp,
                headers = ["associative.hpp"],
                parseMacros = false,
                includeTransitivelyReferencedHeaders = false,
                selection = new() { includeDeclarations = new[] { "MakeMap", "MakeSet", "MakeBrokenSet", "MakeValue", "MakeError", "MakeVoidValue", "MakeVoidError" }.Select(name => "associative::" + name).ToImmutableArray() }
            },
            api = new()
            {
                @namespace = "Generated.Associative",
                name = "AssociativeApi",
                importMode = mode,
                style = sourceRuntime ? BGCS.Core.Managed.ManagedApiStyle.Static : BGCS.Core.Managed.ManagedApiStyle.Instance,
                nestGeneratedTypesInApi = sourceRuntime
            },
            bridge = new() { cpp = new() { familyBindings = [Binding("Map", StandardValueKind.Map), Binding("Set", StandardValueKind.Set), Binding("Result", StandardValueKind.Expected)] } },
            runtime = new() { mode = sourceRuntime ? "source" : "package" },
            output = new() { layout = sourceRuntime ? "singleFile" : "split" },
            cache = new() { enabled = false }
        };
    }

    private static string NewRoot() { string root = Path.Combine(Path.GetTempPath(), "bgcs-configured-associative-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private static string[] Hashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
    private sealed class NoWrites : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        ) => throw new InvalidOperationException("Planning cannot emit artifacts.");
    }

    private const string Header = """
        #include <map>
        #include <set>
        #include <expected>
        namespace associative {
        template<class K,class V> class Map {
            std::map<K,V> m_values;
        public:
            using key_type=K;using mapped_type=V;
            using const_iterator=typename std::map<K,V>::const_iterator;
            size_t size()const{return m_values.size();}
            const_iterator begin()const{return m_values.begin();}
            const_iterator end()const{return m_values.end();}
            void insert_or_assign(K key,V value){m_values.insert_or_assign(key,value);}
        };
        template<class T> class Set {
            std::set<T> m_values;bool m_broken=false;
        public:
            using value_type=T;using const_iterator=typename std::set<T>::const_iterator;
            size_t size()const{return m_values.size()+(m_broken?2:0);}
            const_iterator begin()const{return m_values.begin();}
            const_iterator end()const{return m_values.end();}
            void insert(T value){m_values.insert(value);}
            void break_count(){m_broken=true;}
        };
        template<class T,class E> class Result {
            std::expected<T,E> m_value;
        public:
            using value_type=T;using error_type=E;
            Result()=default;Result(T value):m_value(value){}
            static Result failure(E error){Result result;result.m_value=std::unexpected(error);return result;}
            bool has_value()const{return m_value.has_value();}
            const T& value()const{return m_value.value();}
            const E& error()const{return m_value.error();}
        };
        template<class E> class Result<void,E> {
            std::expected<void,E> m_value;
        public:
            using value_type=void;using error_type=E;
            static Result failure(E error){Result result;result.m_value=std::unexpected(error);return result;}
            bool has_value()const{return m_value.has_value();}
            void value()const{m_value.value();}
            const E& error()const{return m_value.error();}
        };
        inline Map<int,int> MakeMap(){Map<int,int> value;value.insert_or_assign(1,20);value.insert_or_assign(2,22);return value;}
        inline Set<int> MakeSet(){Set<int> value;value.insert(20);value.insert(22);return value;}
        inline Set<int> MakeBrokenSet(){Set<int> value=MakeSet();value.break_count();return value;}
        inline Result<int,int> MakeValue(){return Result<int,int>(42);}
        inline Result<int,int> MakeError(){return Result<int,int>::failure(42);}
        inline Result<void,int> MakeVoidValue(){return Result<void,int>();}
        inline Result<void,int> MakeVoidError(){return Result<void,int>::failure(42);}
        }
        """;
}
