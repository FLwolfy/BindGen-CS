using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Analysis.Bridge;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeConfiguredFamilyTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Factory(out nint value, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(nint self, out ulong value, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntegerAt(nint self, ulong index, out int value, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntegerValue(nint self, out int value, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ByteAt(nint self, ulong index, out byte value, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Release(nint self, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseError(nint error);

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ActualCustomFamiliesKeepTheirTypeAndUseCheckedNativeOperations(BindingImportMode mode, bool sourceRuntime)
    {
        string root = NewRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "custom.hpp"), Header);
            BindingConfiguration configuration = Configuration(mode, sourceRuntime);
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            var target = Assert.Single(built.generation.targetModels);
            var protocols = target.interop.types.Where(type => type.valueProtocol is not null).Select(type => type.valueProtocol!).ToArray();
            foreach (NativeValueFamily family in new[] { NativeValueFamily.String, NativeValueFamily.Vector, NativeValueFamily.Array, NativeValueFamily.Span, NativeValueFamily.Optional })
            {
                NativeValueProtocol[] matching = protocols.Where(item => item.family == family).ToArray();
                Assert.NotEmpty(matching);
                foreach (NativeValueProtocol protocol in matching)
                {
                    Assert.Equal(EvidenceLevel.UserAsserted, protocol.evidence.level);
                    Assert.True(protocol.sequence is null || protocol.sequence.readLifetime.kind == NativeLifetimeKind.Unknown);
                }
            }
            var text = Assert.Single(protocols, item => item.family == NativeValueFamily.String).text!;
            Assert.Equal(NativeTextEncoding.Utf8, text.encoding);
            Assert.Equal(EvidenceLevel.UserAsserted, text.encodingEvidence.level);
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            CppBridgeModule bridge = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(targetRoot, "native", "metadata", "bridge.module.json")))!;
            Assert.Equal(5, bridge.familyBindings.Length);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            CheckNative(bridge, binary);
            string factory = mode == BindingImportMode.FunctionTable ? "FamilyApi.Load(binary)" : "FamilyApi.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "family_fixture", $$"""
                using System;
                using Generated.Family;
                public static class FamilyConsumer {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        if (api.SumVectorWithSequence(new int[] {20,22}) != 42) return 1;
                        if (api.SumArrayWithSequence(new int[] {10,12,20}) != 42) return 2;
                        if (api.SumTextWithSequence("A\0é") != 429) return 3;
                        int[] values={20,22};
                        if(api.SumSpanWithSequence(values)!=42 || values[0]!=21 || values[1]!=23) return 4;
                        bool rejected=false;
                        api.SetBrokenSpan(1);
                        try {api.SumSpanWithSequence(values);} catch(Exception){rejected=true;}
                        finally {api.SetBrokenSpan(0);}
                        if(!rejected || values[0]!=21 || values[1]!=23) return 5;
                        return 42;
                    }
                }
                """, "FamilyConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("element")]
    [InlineData("reserve")]
    [InlineData("macro")]
    public async Task IncompatibleNativeProtocolFailsAndPreservesPreviousPublication(string mutation)
    {
        string root = NewRoot();
        try
        {
            string header = Path.Combine(root, "custom.hpp");
            await File.WriteAllTextAsync(header, Header);
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BindingConfiguration configuration = Configuration(BindingImportMode.FunctionTable, false);
            BuildResult first = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(first.success, first.GetDiagnosticSummary());
            string[] previous = Hashes(first.outputRoot);
            string incompatible = mutation switch
            {
                "element" => Header.Replace("using value_type=T; //vector-element", "using value_type=float; //vector-element", StringComparison.Ordinal),
                "reserve" => Header.Replace("void reserve(size_t n){m_values.reserve(n);}", "void incompatible_reserve(size_t n){m_values.reserve(n);}", StringComparison.Ordinal),
                "macro" => Header + "\n#define is_same(...) bgcs_corrupted_proof\n",
                _ => throw new ArgumentException(nameof(mutation))
            };
            Assert.NotEqual(Header, incompatible);
            await File.WriteAllTextAsync(header, incompatible);
            BuildResult rejected = await host.BuildAsync(new(new(configuration, root)));
            Assert.False(rejected.success);
            Assert.Equal(previous, Hashes(first.outputRoot));
            Assert.Contains(mutation switch { "element" => "element type", "reserve" => "reserve", _ => "macro" }, rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void CheckNative(CppBridgeModule bridge, string binary)
    {
        nint library = NativeLibrary.Load(binary);
        try
        {
            T Function<T>(BridgeExport export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export.name));
            ReleaseError releaseError = Marshal.GetDelegateForFunctionPointer<ReleaseError>(NativeLibrary.GetExport(library, bridge.errorReleaseExport));
            foreach (string name in new[] { "MakeVector", "MakeText", "MakeArray", "MakeBrokenArray", "MakeSpan", "MakeOptional", "MakeEmptyOptional" })
            {
                BridgeExport make = Assert.Single(bridge.exports, export => export.operation.declaration is { } declaration && bridge.source.GetDeclaration(declaration).qualifiedName == "look::" + name);
                Assert.Equal(0, Function<Factory>(make)(out nint instance, out nint error));
                Assert.Equal(0, error);
                StableId owner = StandardLibraryFactReader.Resolve(bridge.source, make.nativeResultType!.Value).id;
                BridgeExport Access(StandardValueAction action) => Assert.Single(bridge.exports, export => export.operation.ownerType == owner && export.operation.standardValue?.action == action);
                try
                {
                    if (name is "MakeOptional" or "MakeEmptyOptional")
                    {
                        int status = Function<IntegerValue>(Access(StandardValueAction.GetValue))(instance, out int value, out error);
                        Assert.Equal(name == "MakeOptional" ? 0 : 1, status);
                        Assert.Equal(name == "MakeOptional" ? 42 : 0, value);
                    }
                    else
                    {
                        int status = Function<Count>(Access(StandardValueAction.Count))(instance, out ulong count, out error);
                        Assert.Equal(name == "MakeBrokenArray" ? 1 : 0, status);
                        Assert.Equal(name == "MakeBrokenArray" ? 0UL : 3UL, count);
                        if (name == "MakeText")
                        {
                            Assert.Equal(0, Function<ByteAt>(Access(StandardValueAction.GetAt))(instance, 1, out byte unit, out nint textError));
                            Assert.Equal(0, unit); Assert.Equal(0, textError);
                        }
                        else if (name != "MakeBrokenArray")
                        {
                            Assert.Equal(0, Function<IntegerAt>(Access(StandardValueAction.GetAt))(instance, 1, out int value, out nint elementError));
                            Assert.Equal(22, value); Assert.Equal(0, elementError);
                        }
                    }
                    if (error != 0) releaseError(error);
                }
                finally
                {
                    Assert.Equal(0, Function<Release>(bridge.exports.Single(export => export.id == make.resultRelease))(instance, out nint releaseFailure));
                    Assert.Equal(0, releaseFailure);
                }
            }
        }
        finally { NativeLibrary.Free(library); }
    }

    private static BindingConfiguration Configuration(BindingImportMode mode, bool sourceRuntime)
    {
        string[] functions = ["MakeVector", "MakeText", "MakeArray", "MakeBrokenArray", "MakeSpan", "MakeOptional", "MakeEmptyOptional", "SumVector", "SumText", "SumArray", "SumSpan", "SetBrokenSpan"];
        CppFamilyBinding Binding(string native, StandardValueKind family) => new()
        {
            nativeTemplate = "look::" + native, family = family, evidence = new(EvidenceLevel.UserAsserted, "fixture-declared-protocol:" + native),
            stringEncoding = family == StandardValueKind.String ? NativeTextEncoding.Utf8 : NativeTextEncoding.Unknown
        };
        SequenceInputAdapterRequest Sequence(string name, bool borrowed = false) => new("look::" + name,
            [new("values") { retainsArgument = borrowed ? false : null, lifetime = borrowed ? NativeLifetimeKind.Call : NativeLifetimeKind.Unknown }]);
        return new()
        {
            schemaVersion = 1, library = new() { id = "family", nativeName = "family_fixture" },
            source = new() { language = SourceLanguage.Cpp, headers = ["custom.hpp"], parseMacros = false,
                includeTransitivelyReferencedHeaders = false, selection = new() { includeDeclarations = functions.Select(name => "look::" + name).ToImmutableArray() } },
            api = new() { @namespace = "Generated.Family", name = "FamilyApi", importMode = mode },
            cpp = new()
            {
                familyBindings = [Binding("Vector", StandardValueKind.Vector), Binding("Text", StandardValueKind.String), Binding("Array", StandardValueKind.Array), Binding("Span", StandardValueKind.Span), Binding("Optional", StandardValueKind.Optional)],
                sequenceInputAdapters = [Sequence("SumVector"), Sequence("SumText"), Sequence("SumArray"), Sequence("SumSpan", true)]
            },
            runtime = new() { mode = sourceRuntime ? "source" : "package" }, output = new() { layout = sourceRuntime ? "singleFile" : "split" },
            nativeBuild = new() { mode = "sources" }, cache = new() { enabled = false }
        };
    }

    private static string NewRoot() { string root = Path.Combine(Path.GetTempPath(), "bgcs-configured-family-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private static string[] Hashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }

    private const string Header = """
        #include <array>
        #include <optional>
        #include <span>
        #include <vector>
        namespace look {
        template<class T> class Vector {
            std::vector<T> m_values;
        public:
            using value_type=T; //vector-element
            size_t size() const {return m_values.size();}
            T& operator[](size_t i){return m_values[i];}
            const T& operator[](size_t i) const {return m_values[i];}
            void push_back(T v){m_values.push_back(v);}
            void reserve(size_t n){m_values.reserve(n);}
        };
        template<class T> class Text : public Vector<T> {};
        template<class T,size_t N> class Array {
            std::array<T,N> m_values{}; bool m_broken=false;
        public:
            using value_type=T;
            size_t size() const {return m_broken ? N+1 : N;}
            void break_count(){m_broken=true;}
            T& operator[](size_t i){return m_values[i];}
            const T& operator[](size_t i)const{return m_values[i];}
        };
        inline bool broken_span=false;
        template<class T,size_t N=std::dynamic_extent> class Span {
            T* m_data; size_t m_count;
        public:
            using element_type=T; using value_type=T;
            Span(T* data,size_t count):m_data(data),m_count(broken_span ? count+1 : count){}
            size_t size() const{return m_count;}
            T* data()const{return m_data;}
            T& operator[](size_t i)const{return m_data[i];}
        };
        template<class T> class Optional {
            std::optional<T> m_value;
        public:
            using value_type=T;
            Optional()=default; Optional(T value):m_value(value){}
            bool has_value()const{return m_value.has_value();}
            T& value(){return *m_value;}
            const T& value()const{return *m_value;}
        };
        inline Vector<int> MakeVector(){Vector<int> r;r.push_back(20);r.push_back(22);r.push_back(0);return r;}
        inline Text<char8_t> MakeText(){Text<char8_t> r;r.push_back(u8'A');r.push_back(0);r.push_back(u8'B');return r;}
        inline Array<int,3> MakeArray(){Array<int,3> r;r[0]=20;r[1]=22;return r;}
        inline Array<int,3> MakeBrokenArray(){auto r=MakeArray();r.break_count();return r;}
        inline int span_data[]={20,22,0};
        inline Span<int> MakeSpan(){return Span<int>(span_data,3);}
        inline Optional<int> MakeOptional(){return Optional<int>(42);}
        inline Optional<int> MakeEmptyOptional(){return Optional<int>();}
        inline int SumVector(Vector<int> values){int r=0;for(size_t i=0;i<values.size();++i)r+=values[i];return r;}
        inline int SumText(Text<char8_t> values){int r=0;for(size_t i=0;i<values.size();++i)r+=values[i];return r;}
        inline int SumArray(Array<int,3> values){int r=0;for(size_t i=0;i<values.size();++i)r+=values[i];return r;}
        inline int SumSpan(Span<int> values){int r=0;for(size_t i=0;i<values.size();++i){r+=values[i];++values[i];}return r;}
        inline void SetBrokenSpan(int value){broken_span=value!=0;}
        }
        """;
}
