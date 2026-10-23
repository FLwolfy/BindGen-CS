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
using Xunit;

namespace BGCS.Tests;

public sealed class NativeMinimalStringProtocolTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CStrOnlyStringAndNativePathPreserveTheirActualProtocols(BindingImportMode mode, bool sourceRuntime)
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
            Assert.False(text.text.permitsEmbeddedZero);
            Assert.True(text.text.zeroTerminated);
            Assert.False(text.text.readOnlyReceiver);
            Assert.Equal(64, text.text.maximumCodeUnits);
            Assert.Equal(NativeLifetimeKind.Call, text.sequence!.readLifetime.kind);
            BridgeExport adapter = Assert.Single(bridge.exports, export => export.inputAdapterOf is not null);
            Assert.NotNull(target.interop.functions.Single(function => function.id == adapter.id).parameters[0].text);
            Assert.Equal(NativePathFormat.NativeUtf8, Assert.Single(target.interop.types, type => type.valueProtocol?.family == NativeValueFamily.Path).valueProtocol!.path!.format);
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
    public async Task MissingCopyOrInvalidNativeGetterPreservesThePreviousOutput()
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
                (configuration with { bridge = configuration.bridge with { cpp = configuration.bridge.cpp with { familyBindings = configuration.bridge.cpp.familyBindings.SetItem(0, configuration.bridge.cpp.familyBindings[0] with { cStr = configuration.bridge.cpp.familyBindings[0].cStr! with { constructorCopiesInput = null } }) } } }, Header, "constructorCopiesInput"),
                (configuration with { bridge = configuration.bridge with { cpp = configuration.bridge.cpp with { familyBindings = configuration.bridge.cpp.familyBindings.SetItem(0, configuration.bridge.cpp.familyBindings[0] with { cStr = configuration.bridge.cpp.familyBindings[0].cStr! with { maximumCodeUnits = 0 } }) } } }, Header, "scan bound"),
                (configuration, Header.Replace("const char* c_str(){", "int c_str(){", StringComparison.Ordinal)
                    .Replace("return mode==1?nullptr:mode==2?unterminated:units.c_str();", "return 42;", StringComparison.Ordinal)
                    .Replace("return std::strcmp(value.c_str(),\"copied\\xF0\\x9F\\x98\\x80\")==0?42:-1;", "return 42;", StringComparison.Ordinal), "data pointer")
            })
            {
                await File.WriteAllTextAsync(header, source);
                if (cause == "scan bound")
                {
                    ArgumentException invalid = Assert.Throws<ArgumentException>(() => new GenerationRequest(request, root));
                    Assert.Contains(cause, invalid.Message, StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(previous, Hashes(first.outputRoot));
                    continue;
                }
                GenerationResult rejected = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(request, root));
                Assert.False(rejected.success);
                Assert.Contains(cause, rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(previous, Hashes(first.outputRoot));
            }
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
            byte[] expected = Encoding.UTF8.GetBytes(text ? "A😀" : "dir\\😀.txt");
            source.AppendLine("{nint holder=0,error=0;ulong count=0;sbyte unit=0;byte pathByte=0;");
            void Success(string invocation, string condition = "") => source.Append("if(").Append(invocation).Append("!=0 || error!=0")
                .Append(condition.Length == 0 ? "" : " || " + condition).AppendLine(")throw new InvalidOperationException(\"exact native operation\");");
            Success(Call(make, "&holder", "&error"), "holder==0");
            source.AppendLine("try {");
            Success(Call(Access(text ? StandardValueAction.Count : StandardValueAction.PathUtf8Count), "holder", "&count", "&error"), "count!=" + expected.Length);
            foreach ((byte value, int index) in expected.Select((value, index) => (value, index)))
                Success(Call(Access(text ? StandardValueAction.GetAt : StandardValueAction.PathUtf8Byte), "holder", index + "UL", text ? "&unit" : "&pathByte", "&error"),
                    text ? "unchecked((byte)unit)!=" + value : "pathByte!=" + value);
            source.Append("if(").Append(Call(Access(text ? StandardValueAction.GetAt : StandardValueAction.PathUtf8Byte), "holder", "999UL", text ? "&unit" : "&pathByte", "&error"))
                .AppendLine("==0 || error==0)throw new InvalidOperationException(\"exact range boundary\");").AppendLine(releaseError)
                .AppendLine("_ = unit;_ = pathByte;")
                .AppendLine("}finally{")
                .Append("if(error!=0){").Append(releaseError).AppendLine("}");
            Success(Call(destroy, "holder", "&error"));
            source.AppendLine("}}");
        }
        BridgeExport input = Assert.Single(bridge.exports, export => export.inputAdapterOf is not null);
        CSharpMethodPlan friendlyInput = plan.methods[Assert.Single(plan.statusMethods, status => plan.methods[status.methodIndex].nativeExport == input.id).methodIndex];
        Assert.Single(friendlyInput.stringInputs);
        source.Append("if(api.").Append(friendlyInput.name).AppendLine("(\"copied😀\")!=42)throw new InvalidOperationException(\"managed counted c_str input\");");
        source.AppendLine("{byte[] units=Encoding.UTF8.GetBytes(\"copied😀\");int result=0;nint error=0;fixed(byte* data=units){");
        source.Append("if(").Append(Call(input,"data","(ulong)units.Length","&result","&error")).AppendLine("!=0 || result!=42 || error!=0)throw new InvalidOperationException(\"copying c_str constructor\");}");
        source.AppendLine("byte[] zero=new byte[]{65,0,66};fixed(byte* data=zero){result=99;");
        source.Append("if(").Append(Call(input,"data","3UL","&result","&error")).AppendLine("==0 || result!=0 || error==0)throw new InvalidOperationException(\"embedded zero must be rejected\");}").AppendLine(releaseError);
        source.AppendLine("byte[] oversized=new byte[64];Array.Fill(oversized,(byte)65);fixed(byte* data=oversized){");
        source.Append("if(").Append(Call(input,"data","64UL","&result","&error")).AppendLine("==0 || error==0)throw new InvalidOperationException(\"terminated bound\");}").AppendLine(releaseError).AppendLine("}");
        foreach(string factory in new[]{"MakeNull","MakeUnterminated"}) {
            BridgeExport make=Assert.Single(bridge.exports, export=>export.operation.declaration is StableId declaration && bridge.source.GetDeclaration(declaration).qualifiedName=="exact::"+factory);
            StableId owner=StandardLibraryFactReader.Resolve(bridge.source,make.nativeResultType!.Value).id;
            BridgeExport count=Assert.Single(bridge.exports, export=>export.operation.ownerType==owner && export.operation.standardValue?.action==StandardValueAction.Count);
            BridgeExport destroy=Assert.Single(bridge.exports, export=>export.operation.ownerType==owner && export.operation.kind==BridgeOperationKind.Destroy);
            source.AppendLine("{nint holder=0,error=0;ulong count=99;");
            source.Append("if(").Append(Call(make,"&holder","&error")).AppendLine("!=0 || holder==0)throw new InvalidOperationException(\"invalid read fixture factory\");try{");
            source.Append("if(").Append(Call(count,"holder","&count","&error")).AppendLine("==0 || count!=0 || error==0)throw new InvalidOperationException(\"invalid c_str read\");").AppendLine(releaseError).AppendLine("}finally{");
            source.Append("if(error!=0){").Append(releaseError).AppendLine("}");
            source.Append("if(").Append(Call(destroy,"holder","&error")).AppendLine("!=0)throw new InvalidOperationException(\"invalid read cleanup\");}}");
        }
        return source.ToString();
    }

    private static BindingConfiguration Configuration(BindingImportMode mode, bool sourceRuntime) => new()
    {
        schemaVersion = 2, library = new() { id = "exact", nativeName = "exact_fixture" },
        source = new() { language = SourceLanguage.Cpp, headers = ["exact.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false,
            selection = new() { includeDeclarations = ["exact::MakeText", "exact::MakePath", "exact::MakeNull", "exact::MakeUnterminated", "exact::CheckCopy"] } },
        api = new() { @namespace = "Generated.Exact", name = "ExactApi", importMode = mode,
            style = sourceRuntime ? BGCS.Core.Managed.ManagedApiStyle.Static : BGCS.Core.Managed.ManagedApiStyle.Instance, nestGeneratedTypesInApi = sourceRuntime },
        bridge = new() { cpp = new() { familyBindings = [
            new() { nativeType = "exact::Text", family = StandardValueKind.String, stringEncoding = NativeTextEncoding.Utf8, stringAccess = CppStringAccess.CStrNullTerminated, evidence = new(EvidenceLevel.UserAsserted, "minimal-UTF8-string-protocol"),
                cStr = new() { maximumCodeUnits = 64, readableLifetime = new(NativeLifetimeKind.Call, null, new(EvidenceLevel.UserAsserted,"native-cstr-readable-during-call")), nullTerminated = new(EvidenceLevel.UserAsserted,"native-cstr-termination"), constructorCopiesInput = new(EvidenceLevel.UserAsserted,"native-Text-constructor-copies-before-return") } },
            new() { nativeType = "exact::Path", family = StandardValueKind.Path, pathFormat = NativePathFormat.NativeUtf8, evidence = new(EvidenceLevel.UserAsserted, "minimal-native-UTF8-path-protocol") }], sequenceInputAdapters = [new("exact::CheckCopy", [new("value")])] } },
        runtime = new() { mode = sourceRuntime ? "source" : "package" }, output = new() { layout = sourceRuntime ? "singleFile" : "split" },
         cache = new() { enabled = false }
    };

    private static string NewRoot() { string root = Path.Combine(Path.GetTempPath(), "bgcs-minimal-cstr-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
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
        #include <cstring>
        namespace exact {
        class Text {
            std::string units; int mode; char unterminated[64];
        public:
            explicit Text(const char* value,int kind=0):units(value),mode(kind){for(int i=0;i<64;++i)unterminated[i]='X';}
            const char* c_str(){return mode==1?nullptr:mode==2?unterminated:units.c_str();}
        };
        struct Bytes {
            const char8_t* data()const{return u8"dir\\\U0001F600.txt";}
            size_t size()const{return 12;}
        };
        class Path { public: Bytes u8string()const{return Bytes();} };
        inline Text MakeText(){return Text("A\xF0\x9F\x98\x80");}
        inline Text MakeNull(){return Text("",1);}
        inline Text MakeUnterminated(){return Text("",2);}
        inline Path MakePath(){return Path();}
        inline int CheckCopy(Text value){return std::strcmp(value.c_str(),"copied\xF0\x9F\x98\x80")==0?42:-1;}
        }
        """;
}
