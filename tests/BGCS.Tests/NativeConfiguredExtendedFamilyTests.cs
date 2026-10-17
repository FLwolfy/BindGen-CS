using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Analysis.Bridge;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeConfiguredExtendedFamilyTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CustomOwnersVariantsPathsAndClocksUseNativeProtocolsWithoutInventedLifetimes(BindingImportMode mode, bool sourceRuntime)
    {
        string root = NewRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "extended.hpp"), Header);
            BindingConfiguration configuration = Configuration(mode, sourceRuntime);
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.generation.targetModels);
            CppBridgeModule bridge = target.bridge!;
            new CppBridgeOperationVerifier().Verify(bridge);
            foreach (NativeValueFamily family in new[] { NativeValueFamily.UniqueOwner, NativeValueFamily.SharedOwner, NativeValueFamily.Variant,
                NativeValueFamily.Path, NativeValueFamily.Duration, NativeValueFamily.TimePoint })
            {
                NativeValueProtocol[] protocols = target.interop.types.Where(type => type.valueProtocol?.family == family).Select(type => type.valueProtocol!).ToArray();
                Assert.NotEmpty(protocols);
                foreach (NativeValueProtocol protocol in protocols)
                {
                    Assert.Equal(EvidenceLevel.UserAsserted, protocol.evidence.level);
                    if (family is NativeValueFamily.Duration or NativeValueFamily.TimePoint)
                    {
                        Assert.NotNull(protocol.period);
                        Assert.Equal(1, protocol.period.numerator);
                        Assert.Equal(1000, protocol.period.denominator);
                    }
                    if (family == NativeValueFamily.TimePoint) Assert.NotNull(protocol.clock);
                }
            }
            foreach (BridgeExport owned in bridge.exports.Where(export => export.resultRelease is not null))
            {
                Assert.Null(owned.resultOwnership);
                InteropFunction actual = target.interop.functions.Single(function => function.id == owned.id);
                Assert.NotEqual("any", actual.parameters.Single(parameter => parameter.id == actual.error!.valueParameter).ownership?.releaseThreadDomain);
            }
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(target.managed!, new(configuration.library.nativeName, new NoWrites())
                { importMode = mode, runtimeNamespace = sourceRuntime ? "Generated.Extended.Runtime" : "BGCS.Runtime" });
            string context = sourceRuntime ? "ExtendedApiContext" : "ExtendedApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, configuration.library.nativeName, $$"""
                using System;
                using System.Text;
                using Generated.Extended;
                using ApiContext=Generated.Extended.{{context}};
                public static unsafe class ExtendedConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        for (int pass=0;pass<2;++pass) {
                            try { {{ConsumerBody(bridge, plan)}} }
                            catch(Exception failure) { throw new InvalidOperationException("configured protocol pass " + pass + ": " + failure.Message, failure); }
                        }
                        return 42;
                    }
                }
                """, "ExtendedConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IncompatibleOwnerVariantPathAndPeriodProofsCannotReplaceTheLastCompleteOutput()
    {
        string root = NewRoot();
        try
        {
            string header = Path.Combine(root, "extended.hpp");
            await File.WriteAllTextAsync(header, Header);
            BindingConfiguration configuration = Configuration(BindingImportMode.FunctionTable, false);
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult first = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(first.success, first.GetDiagnosticSummary());
            string[] previous = Hashes(first.outputRoot);
            foreach ((string source, string cause) in new[]
            {
                (Header.Replace("Unique(const Unique&)=delete;", "Unique(const Unique& other):m_value(other.get()?new T(*other.get()):nullptr){}", StringComparison.Ordinal), "move-only"),
                (Header.Replace("std::variant_alternative_t<I,std::variant<T...>>& get()", "float& get()", StringComparison.Ordinal)
                    .Replace("return *std::get_if<I>(&m_value);", "static float wrong=0;return wrong;", StringComparison.Ordinal), "variant alternative"),
                (Header.Replace("std::u8string generic_u8string()const{return m_name;}", "std::u16string generic_u8string()const{return u\"wrong\";}", StringComparison.Ordinal), "one-byte UTF-8"),
                (Header.Replace("using period=P;", "using period=std::ratio<1,1>;", StringComparison.Ordinal), "duration period type")
            })
            {
                Assert.NotEqual(Header, source);
                await File.WriteAllTextAsync(header, source);
                BuildResult rejected = await host.BuildAsync(new(new(configuration, root)));
                Assert.False(rejected.success);
                Assert.Contains(cause, rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(previous, Hashes(first.outputRoot));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string ConsumerBody(CppBridgeModule bridge, CSharpEmissionPlan plan)
    {
        StringBuilder source = new();
        string Call(BridgeExport export, params string[] arguments)
        {
            CSharpMethodPlan method = plan.methods.Single(method => method.nativeExport == export.id);
            Assert.Equal(method.parameterTypes.Length, arguments.Length);
            return "api.Raw." + method.name + "(" + string.Join(',', arguments.Select((argument, index) => "(" + method.parameterTypes[index] + ")(" + argument + ")")) + ")";
        }
        BridgeExport Native(string name) => Assert.Single(bridge.exports, export => export.operation.declaration is StableId declaration
            && bridge.source.GetDeclaration(declaration).qualifiedName == "extended::" + name);
        CSharpMethodPlan errorRelease = plan.methods.Single(method => method.exportName == bridge.errorReleaseExport);
        string releaseErrorCall = "api.Raw." + errorRelease.name + "((" + errorRelease.parameterTypes[0] + ")error);error=0;";
        void Success(string invocation, string condition = "", string actual = "") => source.Append("if(").Append(invocation).Append("!=0 || error!=0")
            .Append(condition.Length == 0 ? "" : " || " + condition).Append(")throw new InvalidOperationException(")
            .Append(JsonSerializer.Serialize("configured native operation: " + invocation + "; condition: " + condition))
            .Append(" + \"; error=\" + error")
            .Append(actual.Length == 0 ? "" : " + \"; actual=\" + (" + actual + ")").AppendLine(");");
        void Failure(string invocation) => source.Append("if(").Append(invocation)
            .AppendLine("==0 || error==0)throw new InvalidOperationException(\"missing configured state failure\");").AppendLine(releaseErrorCall);
        source.AppendLine("int destroyedBefore=0;{nint error=0;try{");
        Success(Call(Native("Destroyed"), "&destroyedBefore", "&error"));
        source.Append("}finally{if(error!=0){").Append(releaseErrorCall).AppendLine("}}}");
        foreach (string factory in new[] { "MakeUnique", "MakeShared", "MakeVariantInt", "MakeVariantDouble", "MakePath", "MakeDuration", "MakeMoment" })
        {
            BridgeExport make = Native(factory);
            StableId owner = StandardLibraryFactReader.Resolve(bridge.source, make.nativeResultType!.Value).id;
            BridgeExport Access(StandardValueAction action, int? alternative = null) => Assert.Single(bridge.exports, export => export.operation.ownerType == owner
                && export.operation.standardValue?.action == action && export.operation.standardValue.alternativeIndex == alternative);
            BridgeExport destroy = Assert.Single(bridge.exports, export => export.operation.kind == BridgeOperationKind.Destroy && export.operation.ownerType == owner);
            source.AppendLine("{ nint holder=0,other=0,error=0,pointer=0;byte flag=0;int integer=0;ulong count=0;double floating=0;");
            Success(Call(make, "&holder", "&error"), "holder==0");
            source.AppendLine("try {");
            if (factory is "MakeUnique" or "MakeShared")
            {
                bool unique = factory == "MakeUnique";
                Success(Call(Access(StandardValueAction.HasValue), "holder", "&flag", "&error"), "flag!=1");
                Success(Call(Access(StandardValueAction.GetPointer), "holder", "&pointer", "&error"), "pointer==0 || *(int*)pointer!=42");
                source.AppendLine("nint original=pointer;");
                Success(Call(Access(unique ? StandardValueAction.MoveUnique : StandardValueAction.CloneShared), "holder", "&other", "&error"), "other==0 || other==holder");
                Success(Call(Access(StandardValueAction.HasValue), "holder", "&flag", "&error"), unique ? "flag!=0" : "flag!=1");
                Success(Call(Access(StandardValueAction.GetPointer), "other", "&pointer", "&error"), "pointer!=original || *(int*)pointer!=42");
                if (unique)
                {
                    Success(Call(Native("ConsumeUnique"), "other", "&integer", "&error"), "integer!=42");
                    Success(Call(Access(StandardValueAction.HasValue), "other", "&flag", "&error"), "flag!=0");
                    Success(Call(Access(StandardValueAction.GetPointer), "other", "&pointer", "&error"), "pointer!=0");
                }
                else
                {
                    Success(Call(destroy, "holder", "&error"));
                    source.AppendLine("holder=0;");
                    Success(Call(Native("ReadSharedCopy"), "other", "&integer", "&error"), "integer!=42");
                    Success(Call(Access(StandardValueAction.GetPointer), "other", "&pointer", "&error"), "pointer!=original || *(int*)pointer!=42");
                }
            }
            else if (factory.StartsWith("MakeVariant", StringComparison.Ordinal))
            {
                bool integerAlternative = factory == "MakeVariantInt";
                Success(Call(Access(StandardValueAction.Discriminator), "holder", "&count", "&error"), integerAlternative ? "count!=0" : "count!=1");
                Success(Call(Access(StandardValueAction.GetAlternative, integerAlternative ? 0 : 1), "holder", integerAlternative ? "&integer" : "&floating", "&error"), integerAlternative ? "integer!=42" : "floating!=42.5");
                Failure(Call(Access(StandardValueAction.GetAlternative, integerAlternative ? 1 : 0), "holder", integerAlternative ? "&floating" : "&integer", "&error"));
                source.AppendLine(integerAlternative ? "if(floating!=0)throw new InvalidOperationException(\"variant failure output\");" : "if(integer!=0)throw new InvalidOperationException(\"variant failure output\");");
            }
            else if (factory == "MakePath")
            {
                byte[] expected = Encoding.UTF8.GetBytes("dir/😀.txt");
                Success(Call(Access(StandardValueAction.PathUtf8Count), "holder", "&count", "&error"), "count!=" + expected.Length);
                foreach ((byte value, int index) in expected.Select((value, index) => (value, index)))
                    Success(Call(Access(StandardValueAction.PathUtf8Byte), "holder", index + "UL", "&flag", "&error"), "flag!=" + value);
                Failure(Call(Access(StandardValueAction.PathUtf8Byte), "holder", "999UL", "&flag", "&error"));
            }
            else if (factory == "MakeDuration")
            {
                source.AppendLine("long ticks=0;");
                Success(Call(Access(StandardValueAction.DurationCount), "holder", "&ticks", "&error"), "ticks!=42000");
            }
            else Success(Call(Access(StandardValueAction.TimePointCount), "holder", "&integer", "&error"), "integer!=42000");
            source.AppendLine("_ = pointer;_ = flag;_ = integer;_ = count;_ = floating;");
            source.AppendLine("} finally {");
            source.Append("if(error!=0){").Append(releaseErrorCall).AppendLine("}");
            source.Append("if(holder!=0){"); Success(Call(destroy, "holder", "&error")); source.AppendLine("}");
            source.Append("if(other!=0){"); Success(Call(destroy, "other", "&error")); source.AppendLine("}");
            source.AppendLine("} }");
        }
        source.AppendLine("{int released=0;nint error=0;");
        Success(Call(Native("Destroyed"), "&released", "&error"), "released!=checked(destroyedBefore+2)", "\"before=\"+destroyedBefore+\", after=\"+released");
        source.AppendLine("}");
        return source.ToString();
    }

    private static BindingConfiguration Configuration(BindingImportMode mode, bool sourceRuntime)
    {
        CppFamilyBinding Binding(string head, StandardValueKind family) => new()
        {
            nativeTemplate = "extended::" + head, family = family, evidence = new(EvidenceLevel.UserAsserted, "fixture-explicit-protocol:" + head),
            variantAccess = family == StandardValueKind.Variant ? CppVariantAccess.MemberGet : CppVariantAccess.None,
            pathFormat = family == StandardValueKind.Path ? NativePathFormat.GenericUtf8 : null
        };
        return new()
        {
            schemaVersion = 1, library = new() { id = "extended", nativeName = "extended_fixture" },
            source = new() { language = SourceLanguage.Cpp, headers = ["extended.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false,
                selection = new() { includeDeclarations = new[] { "MakeUnique", "ConsumeUnique", "MakeShared", "ReadSharedCopy", "Destroyed", "MakeVariantInt", "MakeVariantDouble", "MakePath", "MakeDuration", "MakeMoment" }
                    .Select(name => "extended::" + name).ToImmutableArray() } },
            api = new() { @namespace = "Generated.Extended", name = "ExtendedApi", importMode = mode,
                style = sourceRuntime ? BGCS.Contracts.Managed.ManagedApiStyle.Static : BGCS.Contracts.Managed.ManagedApiStyle.Instance, nestGeneratedTypesInApi = sourceRuntime },
            cpp = new() { familyBindings = [Binding("Unique", StandardValueKind.UniquePointer), Binding("Shared", StandardValueKind.SharedPointer), Binding("Variant", StandardValueKind.Variant),
                Binding("Path", StandardValueKind.Path), Binding("Ticks", StandardValueKind.Duration), Binding("Moment", StandardValueKind.TimePoint)] },
            runtime = new() { mode = sourceRuntime ? "source" : "package" }, output = new() { layout = sourceRuntime ? "singleFile" : "split" },
            nativeBuild = new() { mode = "sources" }, cache = new() { enabled = false }
        };
    }

    private static string NewRoot() { string root = Path.Combine(Path.GetTempPath(), "bgcs-configured-extended-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private static string[] Hashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
    private sealed class NoWrites : IArtifactSink { public string WriteText(string relativePath, string content) => throw new InvalidOperationException("Planning cannot emit artifacts."); }

    private const string Header = """
        #include <memory>
        #include <variant>
        #include <string>
        #include <ratio>
        #include <utility>
        namespace extended {
        inline int destruction_count=0;
        template<class T> class Unique {
            std::unique_ptr<T> m_value;
        public:
            using element_type=T;
            Unique()=default;explicit Unique(T* value):m_value(value){}
            Unique(const Unique&)=delete;
            Unique(Unique&& other)noexcept:m_value(std::move(other.m_value)){}
            ~Unique()noexcept{if(m_value)++destruction_count;}
            T* get()const{return m_value.get();}
            explicit operator bool()const{return static_cast<bool>(m_value);}
        };
        template<class T> class Shared {
            std::shared_ptr<T> m_value;
        public:
            using element_type=T;
            Shared()=default;explicit Shared(T* value):m_value(value){}
            Shared(const Shared&)noexcept=default;
            Shared(Shared&&)noexcept=default;
            ~Shared()noexcept{if(m_value&&m_value.use_count()==1)++destruction_count;}
            T* get()const{return m_value.get();}
            explicit operator bool()const{return static_cast<bool>(m_value);}
        };
        template<class...T> class Variant {
            std::variant<T...> m_value;
        public:
            Variant()=default;explicit Variant(int value):m_value(std::in_place_index<0>,value){}
            explicit Variant(double value):m_value(std::in_place_index<1>,value){}
            size_t index()const{return m_value.index();}
            template<size_t I> const std::variant_alternative_t<I,std::variant<T...>>& get()const{return *std::get_if<I>(&m_value);}
        };
        struct Marker{};
        template<class Tag> class Path {
            std::u8string m_name=u8"dir/\U0001F600.txt";
        public:
            std::u8string generic_u8string()const{return m_name;}
        };
        template<class R,class P> class Ticks {
            R m_count{};
        public:
            using rep=R;using period=P;
            Ticks()=default;explicit Ticks(R count):m_count(count){}
            R count()const{return m_count;}
        };
        struct Clock{};
        template<class C,class D> class Moment {
            D m_count;
        public:
            using clock=C;using duration=D;
            Moment()=default;explicit Moment(D count):m_count(count){}
            D time_since_epoch()const{return m_count;}
        };
        inline Unique<int> MakeUnique(){return Unique<int>(new int(42));}
        inline int ConsumeUnique(Unique<int> value){return *value.get();}
        inline Shared<int> MakeShared(){return Shared<int>(new int(42));}
        inline int ReadSharedCopy(Shared<int> value){return *value.get();}
        inline int Destroyed(){return destruction_count;}
        inline Variant<int,double> MakeVariantInt(){return Variant<int,double>(42);}
        inline Variant<int,double> MakeVariantDouble(){return Variant<int,double>(42.5);}
        inline Path<Marker> MakePath(){return Path<Marker>();}
        inline Ticks<long long,std::ratio<1,1000>> MakeDuration(){return Ticks<long long,std::ratio<1,1000>>(42000);}
        inline Moment<Clock,Ticks<int,std::ratio<2,2000>>> MakeMoment(){return Moment<Clock,Ticks<int,std::ratio<2,2000>>>(Ticks<int,std::ratio<2,2000>>(42000));}
        }
        """;
}
