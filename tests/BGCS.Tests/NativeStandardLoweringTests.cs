using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using BGCS.Contracts.Managed;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeStandardLoweringTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Make(out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateInteger(int value, out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StaticInteger(int value, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(nint self, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(nint self, out ulong result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntegerAt(nint self, ulong index, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ByteAt(nint self, ulong index, out byte result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CopyUnits(nint self, nint destination, ulong capacity, out ulong written, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntegerValue(nint self, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LongValue(nint self, out long result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PointerValue(nint self, out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Consume(nint holder, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntegerReference(nint value, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseError(nint error);

    [Fact]
    public async Task StandardValues_ParseBridgeReparseCompileAndInvokeRealNativeOperations()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-standard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0;
        List<(BridgeExport Create, nint Value)> owned = [];
        CppBridgeModule? plan = null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.hpp"), """
                #include <array>
                #include <chrono>
                #include <expected>
                #include <filesystem>
                #include <map>
                #include <memory>
                #include <optional>
                #include <set>
                #include <span>
                #include <string>
                #include <variant>
                #include <vector>
                #include <unordered_map>
                #include <unordered_set>
                namespace Sample {
                inline std::string MakeString() { return std::string("A\0B", 3); }
                inline std::vector<int> MakeVector() { return {4, 5, 6}; }
                inline std::vector<bool> MakeBoolVector() { return {true, false, true}; }
                inline int spanValues[] = {71, 72, 73};
                inline std::span<int> MakeSpan() { return std::span<int>(spanValues); }
                inline std::array<int, 3> MakeArray() { return {7, 8, 9}; }
                inline std::map<int, int> MakeMap() { return {{1, 11}, {2, 22}}; }
                inline std::set<int> MakeSet() { return {3, 5, 7}; }
                inline std::unordered_map<int, int> MakeUnorderedMap() { return {{1, 11}, {2, 22}}; }
                inline std::unordered_set<int> MakeUnorderedSet() { return {3, 5, 7}; }
                inline std::optional<int> MakeOptional() { return 41; }
                class Object {
                    int m_value;
                public:
                    explicit Object(int value) : m_value(value) {}
                    Object(const Object& other) : m_value(other.m_value) {}
                    ~Object() {}
                    int Read() const { return m_value; }
                };
                inline std::optional<Object> MakeOptionalObject() { return Object(61); }
                inline int ReadObject(Object value) { return value.Read(); }
                inline std::variant<int, double> MakeVariant() { return 42; }
                inline std::expected<int, int> MakeExpected() { return 43; }
                inline std::filesystem::path MakePath() { return std::filesystem::path(u8"目录/😀.txt"); }
                inline std::chrono::nanoseconds MakeDuration() { return std::chrono::nanoseconds(105); }
                using Point = std::chrono::time_point<std::chrono::system_clock, std::chrono::nanoseconds>;
                inline Point MakeTimePoint() { return Point(std::chrono::nanoseconds(107)); }
                inline std::shared_ptr<int> MakeShared() { return std::make_shared<int>(51); }
                inline std::unique_ptr<int> MakeUnique() { return std::make_unique<int>(53); }
                inline int ConsumeUnique(std::unique_ptr<int> value) { return *value; }
                }
                """, new UTF8Encoding(false));
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "standard", nativeName = "standard_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["fixture.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.Standard", name = "StandardApi" },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }, nativeBuild = new() { mode = "sources" }
            };
            string worker = Path.Combine(Repository(), "src", "BGCS.Frontend.Clang.Worker", "bin", BuildConfiguration(), "net10.0");
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(worker).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            BGCS.Contracts.Interop.InteropModule interop = Assert.Single(result.generation.targetModels).interop;
            new BGCS.Analysis.Bridge.NativeValueProtocolVerifier().Verify(interop);
            var protocols = interop.types.Where(type => type.valueProtocol is not null).Select(type => type.valueProtocol!).ToArray();
            Assert.Contains(protocols, protocol => protocol.family == BGCS.Contracts.Interop.NativeValueFamily.String
                && protocol.text!.encoding == BGCS.Contracts.Interop.NativeTextEncoding.Unknown && protocol.text.codeUnitWidth == 1);
            Assert.Contains(protocols, protocol => protocol.family == BGCS.Contracts.Interop.NativeValueFamily.Vector && protocol.sequence!.isPackedBoolean);
            Assert.Contains(protocols, protocol => protocol.family == BGCS.Contracts.Interop.NativeValueFamily.Span
                && protocol.sequence!.readLifetime.kind == BGCS.Contracts.Interop.NativeLifetimeKind.Unknown);
            Assert.Contains(protocols, protocol => protocol.family == BGCS.Contracts.Interop.NativeValueFamily.Array && protocol.sequence!.fixedLength == 3);
            Assert.Contains(protocols, protocol => protocol.family == BGCS.Contracts.Interop.NativeValueFamily.Path
                && protocol.path!.format == BGCS.Contracts.Interop.NativePathFormat.GenericUtf8);
            foreach (var protocol in protocols.Where(protocol => protocol.family is BGCS.Contracts.Interop.NativeValueFamily.Duration or BGCS.Contracts.Interop.NativeValueFamily.TimePoint))
            {
                Assert.NotNull(protocol.period);
                Assert.Equal(1, protocol.period.numerator);
                Assert.Equal(1_000_000_000, protocol.period.denominator);
            }
            Assert.NotNull(Assert.Single(protocols, protocol => protocol.family == BGCS.Contracts.Interop.NativeValueFamily.TimePoint).clock);
            string target = Assert.Single(Directory.GetDirectories(Path.Combine(root, "Generated")));
            plan = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            string binary = Path.Combine(target, "native", "bin", OperatingSystem.IsWindows() ? "standard_bridge.dll" : OperatingSystem.IsMacOS() ? "libstandard_bridge.dylib" : "libstandard_bridge.so");
            library = NativeLibrary.Load(binary);
            T Function<T>(BridgeExport export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export.name));
            ReleaseError releaseError = Marshal.GetDelegateForFunctionPointer<ReleaseError>(NativeLibrary.GetExport(library, plan.errorReleaseExport));
            foreach (string name in new[] { "MakeString", "MakeVector", "MakeBoolVector", "MakeSpan", "MakeArray", "MakeMap", "MakeSet", "MakeUnorderedMap", "MakeUnorderedSet", "MakeOptional", "MakeOptionalObject", "MakeVariant", "MakeExpected", "MakePath", "MakeDuration", "MakeTimePoint", "MakeShared", "MakeUnique" })
            {
                BridgeExport create = plan.exports.Single(export => export.operation.declaration is { } declaration && plan.source.GetDeclaration(declaration).qualifiedName == "Sample::" + name);
                Assert.Equal(0, Function<Make>(create)(out nint value, out nint error));
                Assert.Equal(0, error);
                Assert.NotEqual(0, value);
                owned.Add((create, value));
                BGCS.Contracts.Common.StableId valueType = BGCS.Analysis.Bridge.StandardLibraryFactReader.Resolve(plan.source, create.nativeResultType!.Value).id;
                BridgeExport Access(StandardValueAction action) => Assert.Single(plan.exports, export => export.operation.ownerType == valueType && export.operation.standardValue?.action == action
                    && (action != StandardValueAction.GetAlternative || export.operation.standardValue.alternativeIndex == 0));
                if (name is "MakeString" or "MakePath")
                {
                    var protocol = interop.types.Single(type => type.sourceNativeType == valueType && type.valueProtocol is not null).valueProtocol!;
                    Assert.Equal(BGCS.Contracts.Interop.NativeValueReleaseScope.CreatingCall,protocol.scopedRelease!.scope);
                    Assert.Equal(BGCS.Contracts.Common.EvidenceLevel.Declared,protocol.scopedRelease.evidence.level);
                    Assert.Equal(0, Function<Count>(Access(name=="MakeString"?StandardValueAction.Count:StandardValueAction.PathUtf8Count))(value,out ulong units,out error));
                    Assert.Equal(0,error);
                    nint storage=Marshal.AllocHGlobal(checked((int)units+1));
                    try
                    {
                        for(int offset=0;offset<=checked((int)units);offset++)Marshal.WriteByte(storage,offset,0xCC);
                        CopyUnits copy=Function<CopyUnits>(Access(StandardValueAction.CopyCodeUnits));
                        Assert.Equal(1,copy(value,storage,units-1,out ulong written,out error));
                        Assert.Equal(0UL,written);Assert.NotEqual(0,error);releaseError(error);
                        Assert.Equal(0xCC,Marshal.ReadByte(storage));
                        Assert.Equal(1,copy(value,0,units,out written,out error));
                        Assert.Equal(0UL,written);Assert.NotEqual(0,error);releaseError(error);
                        Assert.Equal(0,copy(value,storage,units,out written,out error));
                        Assert.Equal(units,written);Assert.Equal(0,error);
                        byte[] copied=new byte[checked((int)units)];Marshal.Copy(storage,copied,0,copied.Length);
                        Assert.Equal(name=="MakeString"?new byte[]{65,0,66}:Encoding.UTF8.GetBytes("目录/😀.txt"),copied);
                        Assert.Equal(0xCC,Marshal.ReadByte(storage,checked((int)units)));
                        if(name=="MakeString")
                        {
                            BridgeExport construct=Access(StandardValueAction.Construct);
                            Assert.Equal(0,Function<Make>(construct)(out nint empty,out error));Assert.Equal(0,error);
                            owned.Add((construct,empty));
                            Assert.Equal(0,copy(empty,0,0,out written,out error));Assert.Equal(0UL,written);Assert.Equal(0,error);
                        }
                    }
                    finally{Marshal.FreeHGlobal(storage);}
                }
                if (name is "MakeString" or "MakeVector" or "MakeBoolVector" or "MakeSpan" or "MakeArray" or "MakeMap" or "MakeSet" or "MakeUnorderedMap" or "MakeUnorderedSet")
                {
                    Assert.Equal(0, Function<Count>(Access(StandardValueAction.Count))(value, out ulong count, out error));
                    Assert.Equal(name is "MakeMap" or "MakeUnorderedMap" ? 2UL : 3UL, count);
                    if (name is "MakeUnorderedMap" or "MakeUnorderedSet")
                    {
                        Dictionary<int, int> mapEntries = [];
                        List<int> setEntries = [];
                        for (ulong index = 0; index < count; index++)
                        {
                            StandardValueAction action = name == "MakeUnorderedMap" ? StandardValueAction.GetMappedAt : StandardValueAction.GetAt;
                            Assert.Equal(0, Function<IntegerAt>(Access(action))(value, index, out int item, out error));
                            Assert.Equal(0, error);
                            if (name == "MakeUnorderedMap")
                            {
                                Assert.Equal(0, Function<IntegerAt>(Access(StandardValueAction.GetKeyAt))(value, index, out int key, out error));
                                Assert.Equal(0, error);
                                mapEntries.Add(key, item);
                            }
                            else setEntries.Add(item);
                        }
                        if (name == "MakeUnorderedMap") Assert.Equal(new[] { "1=11", "2=22" }, mapEntries.OrderBy(entry => entry.Key).Select(entry => entry.Key + "=" + entry.Value));
                        else Assert.Equal(new[] { 3, 5, 7 }, setEntries.Order());
                        Assert.Equal(1, Function<IntegerAt>(Access(name == "MakeUnorderedMap" ? StandardValueAction.GetKeyAt : StandardValueAction.GetAt))(value, count, out int invalid, out error));
                        Assert.Equal(0, invalid);
                        Assert.NotEqual(0, error);
                        releaseError(error);
                    }
                    else if (name == "MakeBoolVector")
                    {
                        Assert.Equal(0, Function<ByteAt>(Access(StandardValueAction.GetAt))(value, 0, out byte item, out error));
                        Assert.Equal(1, item);
                        Assert.Equal(0, Function<ByteAt>(Access(StandardValueAction.GetAt))(value, 1, out item, out error));
                        Assert.Equal(0, item);
                    }
                    else if (name != "MakeString")
                    {
                        StandardValueAction action = name == "MakeMap" ? StandardValueAction.GetMappedAt : StandardValueAction.GetAt;
                        Assert.Equal(0, Function<IntegerAt>(Access(action))(value, 1, out int item, out error));
                        Assert.Equal(name switch { "MakeVector" => 5, "MakeSpan" => 72, "MakeArray" => 8, "MakeMap" => 22, _ => 5 }, item);
                        Assert.Equal(1, Function<IntegerAt>(Access(action))(value, ulong.MaxValue, out item, out error));
                        Assert.Equal(0, item);
                        Assert.NotEqual(0, error);
                        releaseError(error);
                    }
                }
                if (name is "MakeOptional" or "MakeVariant" or "MakeExpected")
                {
                    Assert.Equal(0, Function<IntegerValue>(Access(name == "MakeVariant" ? StandardValueAction.GetAlternative : StandardValueAction.GetValue))(value, out int item, out error));
                    Assert.Equal(name == "MakeOptional" ? 41 : name == "MakeVariant" ? 42 : 43, item);
                }
                if (name == "MakeOptionalObject")
                {
                    var factoryContract = interop.functions.Single(function => function.id == create.id);
                    var holderOwnership = factoryContract.parameters.Single(parameter => parameter.id == factoryContract.error!.valueParameter).ownership!;
                    Assert.Null(holderOwnership.releaseThreadDomain);
                    Assert.Equal(BGCS.Contracts.Common.EvidenceLevel.Declared, holderOwnership.evidence.level);
                    BridgeExport copy = Access(StandardValueAction.GetValue);
                    Assert.Equal(BridgeConversionKind.OwnedValue, copy.resultConversion);
                    Assert.NotNull(copy.resultRelease);
                    var copyContract = interop.functions.Single(function => function.id == copy.id);
                    var copyOwnership = copyContract.parameters.Single(parameter => parameter.id == copyContract.error!.valueParameter).ownership!;
                    Assert.Null(copyOwnership.releaseThreadDomain);
                    Assert.Equal(BGCS.Contracts.Common.EvidenceLevel.Declared, copyOwnership.evidence.level);
                    Assert.Equal(0, Function<PointerValue>(copy)(value, out nint objectCopy, out error));
                    Assert.Equal(0, error);
                    Assert.NotEqual(0, objectCopy);
                    owned.Add((copy, objectCopy));
                    BridgeExport read = Assert.Single(plan.exports, export => export.operation.declaration is { } method
                        && plan.source.GetDeclaration(method).qualifiedName == "Sample::ReadObject");
                    Assert.Equal(0, Function<Consume>(read)(objectCopy, out int actual, out error));
                    Assert.Equal(61, actual);
                    Assert.Equal(0, error);
                }
                if (name is "MakeDuration" or "MakeTimePoint")
                {
                    Assert.Equal(0, Function<LongValue>(Access(name == "MakeDuration" ? StandardValueAction.DurationCount : StandardValueAction.TimePointCount))(value, out long item, out error));
                    Assert.Equal(name == "MakeDuration" ? 105 : 107, item);
                }
                if (name == "MakePath")
                {
                    Assert.Equal(0, Function<Count>(Access(StandardValueAction.PathUtf8Count))(value, out ulong count, out error));
                    byte[] bytes = new byte[checked((int)count)];
                    for (ulong index = 0; index < count; index++)
                    {
                        Assert.Equal(0, Function<ByteAt>(Access(StandardValueAction.PathUtf8Byte))(value, index, out byte item, out error));
                        bytes[checked((int)index)] = item;
                    }
                    Assert.Equal("目录/😀.txt", Encoding.UTF8.GetString(bytes));
                }
                if (name is "MakeShared" or "MakeUnique")
                {
                    Assert.Equal(0, Function<PointerValue>(Access(StandardValueAction.GetPointer))(value, out nint item, out error));
                    Assert.Equal(name == "MakeShared" ? 51 : 53, Marshal.ReadInt32(item));
                    BridgeExport move = Access(name == "MakeShared" ? StandardValueAction.CloneShared : StandardValueAction.MoveUnique);
                    Assert.Equal(0, Function<PointerValue>(move)(value, out nint another, out error));
                    owned.Add((move, another));
                    if (name == "MakeUnique")
                    {
                        BridgeExport consume = plan.exports.Single(export => export.operation.declaration is { } declaration
                            && plan.source.GetDeclaration(declaration).qualifiedName == "Sample::ConsumeUnique");
                        Assert.Equal(BridgeConversionKind.Move, Assert.Single(consume.parameters).conversion);
                        Assert.Equal(0, Function<Consume>(consume)(another, out int consumed, out error));
                        Assert.Equal(53, consumed);
                        Assert.Equal(0, error);
                        Assert.Equal(0, Function<PointerValue>(Access(StandardValueAction.GetPointer))(another, out item, out error));
                        Assert.Equal(0, item);
                        Assert.Equal(0, error);
                    }
                }
            }
        }
        finally
        {
            if (library != 0 && plan is not null)
            {
                foreach ((BridgeExport create, nint value) in owned.AsEnumerable().Reverse())
                {
                    BridgeExport destroy = plan.exports.Single(export => export.id == create.resultRelease);
                    Destroy release = Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library, destroy.name));
                    Assert.Equal(0, release(value, out nint error));
                    Assert.Equal(0, error);
                }
                NativeLibrary.Free(library);
            }
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitClassAndFunctionTemplates_UseConcreteWorkerFactsAndRealNativeExports()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-templates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0, instance = 0;
        CppBridgeModule? plan = null;
        BridgeExport? constructor = null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "before.hpp"), """
                #ifndef BGCS_TEMPLATE_USER_PREAMBLE
                #error BGCS_TEMPLATE_USER_PREAMBLE_MISSING
                #endif
                #define BGCS_TEMPLATE_FIRST_HEADER 1
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.hpp"), """
                #ifndef BGCS_TEMPLATE_FIRST_HEADER
                #error BGCS_TEMPLATE_HEADER_ORDER_CHANGED
                #endif
                namespace Demo {
                template<typename T> class Box {
                public:
                    explicit Box(T value) : m_value(value) {}
                    T Read() const { return m_value; }
                private: T m_value;
                };
                template<class T> class Selector { public: Selector() = default; int Kind() const { return 0; } };
                template<class T> class Selector<T*> { public: Selector() = default; int Kind() const { return 1; } };
                template<> class Selector<int> { public: Selector() = default; int Kind() const { return 2; } };
                template<typename T> T Twice(T value) { return value + value; }
                template<typename T> T Take(T&& value) { return value + 3; }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "templates", nativeName = "templates_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["before.hpp", "fixture.hpp"], preamble = "#define BGCS_TEMPLATE_USER_PREAMBLE 1\n", parseMacros = false, includeTransitivelyReferencedHeaders = false },
                cpp = new() { templateInstantiations = new() { types = [new("integer-box", "Demo::Box<int>"), new("full-selector", "Demo::Selector<int>"), new("partial-selector", "Demo::Selector<float*>")], functions = [new("twice", "Demo::Twice<int>", "int", ["int"]), new("take", "Demo::Take<int>", "int", ["int&&"])] } },
                api = new() { @namespace = "Generated.Templates", name = "TemplatesApi" },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }, nativeBuild = new() { mode = "sources" }
            };
            string worker = Path.Combine(Repository(), "src", "BGCS.Frontend.Clang.Worker", "bin", BuildConfiguration(), "net10.0");
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(worker).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(Path.Combine(root, "Generated")));
            plan = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            string binary = Path.Combine(target, "native", "bin", OperatingSystem.IsWindows() ? "templates_bridge.dll" : OperatingSystem.IsMacOS() ? "libtemplates_bridge.dylib" : "libtemplates_bridge.so");
            library = NativeLibrary.Load(binary);
            T Function<T>(BridgeExport export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export.name));
            string storageAssertions = await File.ReadAllTextAsync(Path.Combine(target, "native", "src", "storage.assertions.cpp"));
            AssertInputOrder(storageAssertions);
            AssertInputOrder(await File.ReadAllTextAsync(Path.Combine(target, "native", "src", "facts.input.hpp")));
            Assert.Contains("&Demo::Box<int>::Read", storageAssertions);
            Assert.DoesNotContain("&Demo::Box::Read", storageAssertions);
            Assert.True(plan.exports.Any(export => export.operation.kind == BridgeOperationKind.Construct), string.Join(Environment.NewLine, plan.source.declarations
                .Where(declaration => declaration.kind is "Class" or "Function")
                .Select(declaration => $"{declaration.kind} {declaration.qualifiedName}, parent={declaration.parent}, templates={declaration.templateParameters.Length}, children={declaration.children.Length}, definition={declaration.isDefinition}, complete={declaration.isCompleteDefinition}, role={declaration.role}")));
            constructor = Assert.Single(plan.exports, export => export.operation.kind == BridgeOperationKind.Construct && export.parameters.Length == 1
                && BGCS.Analysis.Bridge.StandardLibraryFactReader.Resolve(plan.source, export.parameters[0].nativeType).primitiveKind == "Int");
            Assert.Equal("Demo::Box<int>", plan.source.GetType(constructor.operation.ownerType!.Value).spelling);
            Assert.Equal(0, Function<CreateInteger>(constructor)(21, out instance, out nint error));
            Assert.Equal(0, error);
            BridgeExport read = plan.exports.Single(export => export.operation.declaration is { } declaration && plan.source.GetDeclaration(declaration).name == "Read");
            Assert.Equal("Demo::Box<int>::Read() const", new BGCS.Analysis.Types.NativeTypeFormatter().CallableSignature(plan.source, plan.source.GetDeclaration(read.operation.declaration!.Value)));
            Assert.Equal(0, Function<IntegerValue>(read)(instance, out int value, out error));
            Assert.Equal(21, value);
            TemplateInstantiationPlan instantiations = new BGCS.Cpp2C.Planning.TemplateInstantiationPlanner().Plan(configuration.cpp.templateInstantiations!);
            BridgeExport twice = plan.exports.Single(export => export.operation.declaration is { } declaration
                && plan.source.GetDeclaration(declaration).qualifiedName == instantiations.functionWrappers["twice"]);
            Assert.Equal(0, Function<StaticInteger>(twice)(21, out value, out error));
            Assert.Equal(42, value);
            BridgeExport take = plan.exports.Single(export => export.operation.declaration is { } declaration
                && plan.source.GetDeclaration(declaration).qualifiedName == instantiations.functionWrappers["take"]);
            Assert.Equal(BridgeConversionKind.Move, Assert.Single(take.parameters).conversion);
            nint reference = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(reference, 39);
                Assert.Equal(0, Function<IntegerReference>(take)(reference, out value, out error));
                Assert.Equal(42, value);
                Assert.Equal(0, error);
            }
            finally { Marshal.FreeHGlobal(reference); }
            foreach ((string spelling, int expected) in new[] { ("Demo::Selector<int>", 2), ("Demo::Selector<float *>", 1) })
            {
                string normalized = string.Concat(spelling.Where(character => !char.IsWhiteSpace(character)));
                BridgeExport create = Assert.Single(plan.exports, export => export.operation.kind == BridgeOperationKind.Construct && export.parameters.IsEmpty
                    && string.Concat(plan.source.GetType(export.operation.ownerType!.Value).spelling.Where(character => !char.IsWhiteSpace(character))) == normalized);
                Assert.Equal(0, Function<Make>(create)(out nint selector, out error));
                Assert.Equal(0, error);
                try
                {
                    BridgeExport kind = Assert.Single(plan.exports, export => export.operation.ownerType == create.operation.ownerType
                        && export.operation.declaration is { } method && plan.source.GetDeclaration(method).name == "Kind");
                    Assert.Equal(0, Function<IntegerValue>(kind)(selector, out int actual, out error));
                    Assert.Equal(expected, actual);
                    Assert.Equal(0, error);
                }
                finally
                {
                    Assert.Equal(0, Function<Destroy>(plan.exports.Single(export => export.id == create.resultRelease))(selector, out nint cleanupError));
                    Assert.Equal(0, cleanupError);
                }
            }
            Assert.DoesNotContain(plan.exports, export => export.operation.declaration is { } declaration && plan.source.GetDeclaration(declaration).templateParameters.Length != 0);
        }
        finally
        {
            if (library != 0 && plan is not null && constructor is not null && instance != 0)
            {
                BridgeExport destroy = plan.exports.Single(export => export.id == constructor.resultRelease);
                Destroy release = Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library, destroy.name));
                Assert.Equal(0, release(instance, out nint error));
                Assert.Equal(0, error);
            }
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(root, recursive: true);
        }

        static void AssertInputOrder(string source)
        {
            int userPreamble = source.IndexOf("#define BGCS_TEMPLATE_USER_PREAMBLE 1", StringComparison.Ordinal);
            Assert.True(userPreamble >= 0, source);
            int support = source.IndexOf("#include <utility>", userPreamble, StringComparison.Ordinal);
            Assert.True(support > userPreamble, source);
            int firstHeader = source.IndexOf("before.hpp", support, StringComparison.Ordinal);
            Assert.True(firstHeader > support, source);
            int secondHeader = source.IndexOf("fixture.hpp", firstHeader, StringComparison.Ordinal);
            Assert.True(secondHeader > firstHeader, source);
            Assert.DoesNotContain("#include <utility>", source[(secondHeader + "fixture.hpp".Length)..]);
        }
    }

    [Fact]
    public async Task ConfiguredNativeAndManagedRecipes_CompileInvokeAndPreserveAssertionEvidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-recipes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.hpp"), """
                namespace Demo {
                class Quantity {
                public: explicit Quantity(int value) : m_value(value) {} explicit operator int() const { return m_value; }
                private: int m_value;
                };
                inline Quantity Echo(Quantity value) { return Quantity(static_cast<int>(value) + 2); }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "recipes", nativeName = "recipes_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["fixture.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                cpp = new() { typeLowerings = [new()
                {
                    id = "quantity", typePattern = "Demo::Quantity", cAbiType = "int32_t",
                    parameterExpression = "{nativeType}({value} + 1)", resultExpression = "static_cast<int>({value}) - 1",
                    managedProjection = new("long", "checked((int){value})", "(long){value} + 100")
                }] },
                api = new() { @namespace = "Generated.Recipes", name = "RecipesApi", names = ImmutableDictionary<string, string>.Empty.Add("Demo::Echo", "Echo") },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }, nativeBuild = new() { mode = "sources" }
            };
            string worker = Path.Combine(Repository(), "src", "BGCS.Frontend.Clang.Worker", "bin", BuildConfiguration(), "net10.0");
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(worker).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(Path.Combine(root, "Generated")));
            using JsonDocument abi = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(target, "metadata", "abi.manifest.json")));
            JsonElement echo = abi.RootElement.GetProperty("module").GetProperty("functions").EnumerateArray()
                .Single(function => function.GetProperty("sourceQualifiedName").ValueKind == JsonValueKind.String && function.GetProperty("sourceQualifiedName").GetString() == "Demo::Echo");
            Assert.Equal(3, echo.GetProperty("error").GetProperty("evidence").GetProperty("level").GetInt32());
            string binary = Path.Combine(target, "native", "bin", OperatingSystem.IsWindows() ? "recipes_bridge.dll" : OperatingSystem.IsMacOS() ? "librecipes_bridge.dylib" : "librecipes_bridge.so");
            string[] assemblyPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
            CSharpCompilation compilation = CSharpCompilation.Create("GeneratedRecipes_" + Guid.NewGuid().ToString("N"),
                Directory.GetFiles(Path.Combine(target, "managed"), "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path)),
                assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
                new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
            using MemoryStream stream = new();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            stream.Position = 0;
            AssemblyLoadContext context = new("BGCS.NativeRecipes.Test", isCollectible: true);
            try
            {
                Assembly assembly = context.LoadFromStream(stream);
                Type api = assembly.GetType("Generated.Recipes.RecipesApi", true)!;
                using IDisposable instance = (IDisposable)api.GetMethod("Load")!.InvokeFactory([binary])!;
                MethodInfo friendly = api.GetMethod("Echo")!;
                Assert.Equal(typeof(long), friendly.ReturnType);
                Assert.Equal(typeof(long), Assert.Single(friendly.GetParameters()).ParameterType);
                Assert.Equal(122L, friendly.Invoke(instance, [20L]));
            }
            finally { context.Unload(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OwnedNativeRecipe_UsesTypedReleaseAndRetainsCleanupAfterContextDisposal()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-owned-recipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.hpp"), """
                #include <atomic>
                #include <cstdint>
                namespace Demo {
                struct Token { uint64_t value; };
                inline std::atomic<int> live = 0;
                inline uint64_t* Allocate(uint64_t value) { auto* result = new uint64_t(value); ++live; return result; }
                inline void Release(uint64_t* value) { delete value; --live; }
                inline Token MakeToken() { return {42}; }
                inline int Live() { return live.load(); }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "owned_recipe", nativeName = "owned_recipe_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["fixture.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                cpp = new() { typeLowerings = [new()
                {
                    id = "token", typePattern = "Demo::Token", cAbiType = "uint64_t*", resultExpression = "Demo::Allocate({value}.value)",
                    ownedResultRelease = new("Demo::Release", "tokens") { releaseThreadDomain = "any" }
                }] },
                api = new() { @namespace = "Generated.OwnedRecipe", name = "OwnedRecipeApi",
                    names = ImmutableDictionary<string, string>.Empty.Add("Demo::MakeToken", "CreateToken").Add("Demo::Live", "Live") },
                runtime = new() { mode = "package" }, cache = new() { enabled = false }, nativeBuild = new() { mode = "sources" }
            };
            string worker = Path.Combine(Repository(), "src", "BGCS.Frontend.Clang.Worker", "bin", BuildConfiguration(), "net10.0");
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(worker).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(Path.Combine(root, "Generated")));
            CppBridgeModule plan = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            BridgeExport create = plan.exports.Single(export => export.operation.declaration is { } id && plan.source.GetDeclaration(id).qualifiedName == "Demo::MakeToken");
            Assert.Equal("any", create.resultOwnership!.releaseThreadDomain);
            Assert.Equal(BGCS.Contracts.Common.EvidenceLevel.UserAsserted, create.resultOwnership.evidence.level);
            Assert.Equal(create.resultOwnership.releaseExport, create.resultRelease);
            string binary = Path.Combine(target, "native", "bin", OperatingSystem.IsWindows() ? "owned_recipe_bridge.dll" : OperatingSystem.IsMacOS() ? "libowned_recipe_bridge.dylib" : "libowned_recipe_bridge.so");
            string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
            CSharpCompilation compilation = CSharpCompilation.Create("GeneratedOwnedRecipe_" + Guid.NewGuid().ToString("N"),
                Directory.GetFiles(Path.Combine(target, "managed"), "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path)),
                paths.Append(typeof(BGCS.Runtime.OwnedNativeHandle).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
                new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
            using MemoryStream stream = new();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            stream.Position = 0;
            AssemblyLoadContext context = new("BGCS.NativeOwnedRecipe.Test", isCollectible: true);
            try
            {
                Assembly assembly = context.LoadFromStream(stream);
                Type api = assembly.GetType("Generated.OwnedRecipe.OwnedRecipeApi", true)!;
                using IDisposable instance = (IDisposable)api.GetMethod("Load")!.InvokeFactory([binary])!;
                using BGCS.Runtime.OwnedNativeHandle token = (BGCS.Runtime.OwnedNativeHandle)api.GetMethod("CreateToken")!.Invoke(instance, null)!;
                Assert.Equal(42L, token.Invoke(address => Marshal.ReadInt64(address)));
                Assert.Equal(1, api.GetMethod("Live")!.Invoke(instance, null));
                Assert.Equal("native-recipe-allocator:tokens", token.releaseDomain);
                instance.Dispose();
                token.Dispose();
                Assert.True(token.IsClosed);
                Assert.Null(token.releaseFailure);
            }
            finally { context.Unload(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string BuildConfiguration() => AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
    private static string Repository()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "BindGen-CS.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("The repository root is unavailable.");
    }
}
