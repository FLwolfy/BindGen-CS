using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Logging;
using BGCS.Cpp2C.Metadata;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using BGCS.Intermediate;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public class Cpp2CGeneratorTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateDemo(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AddDemo(nint self, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyDemo(nint self);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetBridgeError();
    [Fact]
    public void Config_DefaultCollections_ShouldBeInitialized()
    {
        Cpp2CGeneratorConfig cfg = new();

        Assert.NotNull(cfg.IncludeFolders);
        Assert.NotNull(cfg.SystemIncludeFolders);
        Assert.NotNull(cfg.Defines);
        Assert.NotNull(cfg.AdditionalArguments);
    }

    [Fact]
    public void Config_GetCType_ShouldPreservePointerReferenceAndQualificationShape()
    {
        Cpp2CGeneratorConfig config = new();
        CppType pointer = new CppPointerType(default, CppPrimitiveType.Int);
        CppType reference = new CppReferenceType(default, new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.Float));

        Assert.Equal("int*", config.GetCType(pointer));
        Assert.Equal("const float*", config.GetCType(reference));
    }

    [Fact]
    public void Generate_MinimalClassHeader_ShouldNotReportErrors()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "class Demo { public: int Add(int a, int b); };");

        try
        {
            Cpp2CGeneratorConfig cfg = new();
            Cpp2CCodeGenerator gen = new(cfg);
            gen.Generate(header, output);

            Assert.DoesNotContain(gen.Messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
            Assert.True(gen.LastResult?.Success);
            Assert.Contains(gen.LastResult!.Module!.Types, type => type.NativeName == "Demo");
            Assert.Contains(gen.LastResult.Module.Functions, function => function.NativeName == "Add");
            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("Demo_Add", classesHeader);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void Config_LoadExistingFile_ShouldNotRewriteUnknownProperties()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string path = Path.Combine(temp, "bridge.json");
        string source = "{\n  \"NamePrefix\": \"Demo\",\n  \"ExtensionValue\": 42\n}";
        File.WriteAllText(path, source);
        try
        {
            Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load(path);

            Assert.Equal("Demo", config.NamePrefix);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void GenerateConfigured_ShouldResolvePathsRelativeToConfiguration()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-configured-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "sample.hpp"), "class Demo { public: int Add(int value); };");
        string configPath = Path.Combine(temp, "bridge.json");
        File.WriteAllText(configPath,
            "{\"EntryFiles\":[\"sample.hpp\"],\"AllowedHeaders\":[\"sample.hpp\"],\"OutputPath\":\"bridge-output\"}");
        try
        {
            Cpp2CCodeGenerator generator = new(Cpp2CGeneratorConfig.Load(configPath));

            generator.GenerateConfigured();

            Assert.True(generator.LastResult?.Success);
            Assert.True(File.Exists(Path.Combine(temp, "bridge-output", "include", "Classes.h")));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_MultipleInheritance_ShouldEmitPointerAdjustingCasts()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-inheritance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "inheritance.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "class BaseA { public: virtual ~BaseA(); }; class BaseB { public: virtual ~BaseB(); }; class Derived : public BaseA, public BaseB { }; ");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("DerivedAsBaseA", classesHeader);
            Assert.Contains("DerivedAsBaseB", classesHeader);
            Assert.Contains("DerivedFromBaseA", classesHeader);
            Assert.Contains("static_cast<BaseB*>(derived)", classesSource);
            Assert.Contains("dynamic_cast<Derived*>(base_ptr)", classesSource);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_NamespaceFreeFunctions_ShouldEmitGuardedCBridge()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-free-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "free.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "namespace Math { int Add(int left, int right); const int& Current(); }");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("Math_Add", classesHeader);
            Assert.Contains("Math_Current", classesHeader);
            Assert.Contains("Math::Add", classesSource);
            Assert.Contains("catch (const std::exception& exception)", classesSource);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_Utf8StringAdapter_ShouldLowerInputAndBorrowedReturn()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-string-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "string.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "#include <string>\nnamespace Demo { inline std::string Echo(const std::string& value){return value;} }");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("API(const char*) Demo_Echo(const char* value);", classesHeader);
            Assert.Contains("value == nullptr ? \"\" : value", classesSource);
            Assert.Contains("thread_local std::", classesSource);
            Assert.Contains("return return_value.c_str();", classesSource);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_UniquePtrAdapter_ShouldTransferOpaqueOwnership()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-unique-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "unique.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "#include <memory>\nclass Widget { public: int Value() const{return 7;} }; namespace Demo { inline std::unique_ptr<Widget> Create(){return std::unique_ptr<Widget>(new Widget());} inline int Consume(std::unique_ptr<Widget> value){return value ? value->Value() : 0;} }");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("API(Widget*) Demo_Create(void);", classesHeader);
            Assert.Contains("API(int) Demo_Consume(Widget* value);", classesHeader);
            Assert.Contains("Demo::Create().release()", classesSource);
            Assert.Contains("unique_ptr", classesSource);
            Assert.Contains("reinterpret_cast<Widget*>(value)", classesSource);
            Assert.All(generator.LastResult!.Module!.Functions.Where(function => function.NativeName is "Create" or "Consume"), function =>
            {
                MarshallingPlan plan = function.NativeName == "Create" ? function.ReturnMarshalling : function.Parameters[0].Marshalling;
                Assert.Equal(BindingOwnership.Transferred, plan.Ownership);
                Assert.True(plan.RequiresCleanup);
            });
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_SpanAdapter_ShouldExpandPointerAndCount()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-span-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "span.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "#include <span>\nnamespace Demo { inline int Sum(std::span<int> values){int result=0;for(int value:values)result+=value;return result;} }");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("Demo_Sum(int* values, size_t values_count)", classesHeader);
            Assert.Contains("reinterpret_cast<int*>(values), values_count", classesSource);
            BindingFunction sum = Assert.Single(generator.LastResult!.Module!.Functions, function => function.NativeName == "Sum");
            Assert.Equal(MarshallingStrategy.Span, sum.Parameters[0].Marshalling.Strategy);
            Assert.Equal("values_count", sum.Parameters[0].Marshalling.LengthParameter);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_VectorAndSpanReturns_ShouldPreservePointerCountViews()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-vector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "vector.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "#include <span>\n#include <vector>\nnamespace Demo { inline std::vector<int> Double(std::vector<int> values){for(int& value:values)value*=2;return values;} inline std::span<int> View(){static int values[2]={3,4};return values;} }");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("Demo_Double(int* values, size_t values_count, size_t* out_count)", classesHeader);
            Assert.Contains("Demo_View(size_t* out_count)", classesHeader);
            Assert.Contains("thread_local std::vector", classesSource);
            Assert.Contains("return_value.data()", classesSource);
            Assert.Contains("*out_count = return_value.size()", classesSource);
            BindingFunction vector = Assert.Single(generator.LastResult!.Module!.Functions, function => function.NativeName == "Double");
            Assert.Equal(MarshallingStrategy.Span, vector.ReturnMarshalling.Strategy);
            Assert.Equal("out_count", vector.ReturnMarshalling.LengthParameter);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_SharedPtrAdapter_ShouldRetainReturnAndBorrowInput()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "shared.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "#include <memory>\nclass Widget { public: int Value() const{return 9;} }; namespace Demo { inline std::shared_ptr<Widget> Make(){return std::make_shared<Widget>();} inline int Read(std::shared_ptr<Widget> value){return value?value->Value():0;} }");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());
            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("API(SharedPtr_Widget*) Demo_Make(void)", classesHeader);
            Assert.Contains("API(int) Demo_Read(SharedPtr_Widget* value)", classesHeader);
            Assert.Contains("SharedPtr_WidgetGet", classesHeader);
            Assert.Contains("SharedPtr_WidgetClone", classesHeader);
            Assert.Contains("SharedPtr_WidgetDestroy", classesHeader);
            Assert.Contains("new std::shared_ptr", classesSource);
            Assert.Contains("*reinterpret_cast<std::shared_ptr", classesSource);
            BindingFunction make = Assert.Single(generator.LastResult!.Module!.Functions, function => function.NativeName == "Make");
            Assert.Equal(BindingOwnership.Shared, make.ReturnMarshalling.Ownership);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_VirtualCallbackInterface_ShouldEmitManagedProxyThunk()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-virtual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "virtual.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "class ICompute { public: virtual ~ICompute() = default; virtual int Compute(int value) const = 0; virtual void Notify(int& value) = 0; };");
        Cpp2CGeneratorConfig config = new();
        config.VirtualCallbackInterfaces.Add("ICompute");
        try
        {
            Cpp2CCodeGenerator generator = new(config);

            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("typedef int (CALL *IComputeComputeCallback)", classesHeader);
            Assert.Contains("typedef void (CALL *IComputeNotifyCallback1)(void* user_data, int* value)", classesHeader);
            Assert.Contains("IComputeCreateProxy(IComputeCallbacks callbacks, void* user_data)", classesHeader);
            Assert.Contains("class IComputeProxy final : public ICompute", classesSource);
            Assert.Contains("int Compute(int value) const override", classesSource);
            Assert.Contains("callbacks.Notify1(user_data, &value)", classesSource);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_OptionalAdapter_ShouldPreservePresenceSemantics()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-optional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "optional.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "#include <optional>\nnamespace Demo { inline std::optional<int> Maybe(bool set,int value){return set?std::optional<int>(value):std::nullopt;} inline int Read(std::optional<int> value){return value.value_or(-1);} }");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("API(bool) Demo_Maybe(bool set, int value, int* out_value)", classesHeader);
            Assert.Contains("API(int) Demo_Read(int value, bool value_has_value)", classesHeader);
            Assert.Contains("if (!optional_result.has_value()) return false", classesSource);
            Assert.Contains("value_has_value ? std::optional<int>(value) : std::nullopt", classesSource);
            BindingFunction maybe = Assert.Single(generator.LastResult!.Module!.Functions, function => function.NativeName == "Maybe");
            Assert.Equal(MarshallingStrategy.Optional, maybe.ReturnMarshalling.Strategy);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_ExplicitTemplateInstantiation_ShouldEmitSpecializedBridge()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "template.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "namespace Demo { template<class T> class Box { public: T Get() const; }; }");
        Cpp2CGeneratorConfig config = new();
        config.TemplateInstantiations.Add("Demo::Box<int>");
        try
        {
            Cpp2CCodeGenerator generator = new(config);

            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success);
            string classes = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("Box", classes);
            Assert.Contains("Get", classes);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_UnsupportedType_ShouldReturnActionableDiagnosticAndPreserveOutput()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-unsupported-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "unsupported.hpp");
        string output = Path.Combine(temp, "out");
        Directory.CreateDirectory(output);
        string sentinel = Path.Combine(output, "last-good.txt");
        File.WriteAllText(sentinel, "last-good");
        File.WriteAllText(header, "#include <variant>\nstd::variant<int,float> GetValue();");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            Assert.False(generator.LastResult?.Success);
            BindingDiagnostic diagnostic = Assert.Single(generator.LastResult!.Diagnostics, value => value.Code == "BGCSCPP001");
            Assert.Contains("TemplateInstantiations", diagnostic.Message);
            Assert.Equal("last-good", File.ReadAllText(sentinel));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_NonBlittableOptional_ShouldUseOwnedHandleProtocol()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-optional-holder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "optional-holder.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "#include <optional>\nclass Widget { public: int value; Widget(int value):value(value){} }; namespace Demo { inline std::optional<Widget> Maybe(bool set){return set?std::optional<Widget>(Widget(3)):std::nullopt;} inline int Read(std::optional<Widget> value){return value?value->value:-1;} }");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());
            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success);
            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("Demo_Maybe(bool set, Widget** out_value)", classesHeader);
            Assert.Contains("Demo_Read(Widget* value, bool value_has_value)", classesHeader);
            Assert.Contains("new Widget(*optional_result)", classesSource);
            BindingFunction maybe = Assert.Single(generator.LastResult!.Module!.Functions, function => function.NativeName == "Maybe");
            Assert.Equal(BindingOwnership.Owned, maybe.ReturnMarshalling.Ownership);
            Assert.True(maybe.ReturnMarshalling.RequiresCleanup);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_FunctionTemplateInstantiation_ShouldEmitConcreteBridge()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-function-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "function-template.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "namespace Demo { template<class T> T Twice(T value){return value+value;} }");
        Cpp2CGeneratorConfig config = new();
        config.FunctionTemplateInstantiations.Add("int Demo::Twice<int>(int)");
        try
        {
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success, string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(value => value.Message) ?? []));
            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("BGCS_FunctionTemplate_0", classesHeader);
            Assert.True(CompileGeneratedBridge(output, temp, out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_CppBridge_ShouldLinkAndInvokeNativeDll()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "runtime.hpp");
        string output = Path.Combine(temp, "out");
        string library = Path.Combine(temp,
            OperatingSystem.IsWindows() ? "bridge.dll" : OperatingSystem.IsMacOS() ? "libbridge.dylib" : "libbridge.so");
        File.WriteAllText(header,
            "class Demo { int value; public: Demo(int value):value(value){} int Add(int other){return value+other;} };");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());
            generator.Generate(header, output);
            Assert.True(CompileGeneratedBridgeDll(output, temp, library, out string diagnostics), diagnostics);

            nint handle = global::System.Runtime.InteropServices.NativeLibrary.Load(library);
            try
            {
                CreateDemo create = Marshal.GetDelegateForFunctionPointer<CreateDemo>(
                    global::System.Runtime.InteropServices.NativeLibrary.GetExport(handle, "DemoCreate"));
                AddDemo add = Marshal.GetDelegateForFunctionPointer<AddDemo>(
                    global::System.Runtime.InteropServices.NativeLibrary.GetExport(handle, "Demo_Add"));
                DestroyDemo destroy = Marshal.GetDelegateForFunctionPointer<DestroyDemo>(
                    global::System.Runtime.InteropServices.NativeLibrary.GetExport(handle, "DemoDestroy"));
                GetBridgeError getError = Marshal.GetDelegateForFunctionPointer<GetBridgeError>(
                    global::System.Runtime.InteropServices.NativeLibrary.GetExport(handle, "BGCS_GetLastError"));
                nint instance = create(40);
                Assert.NotEqual(0, instance);
                Assert.Equal(42, add(instance, 2));
                Assert.Equal(string.Empty, Marshal.PtrToStringUTF8(getError()));
                destroy(instance);
            }
            finally
            {
                global::System.Runtime.InteropServices.NativeLibrary.Free(handle);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    private static bool CompileGeneratedBridgeDll(string output, string sourceDirectory, string library, out string diagnostics)
    {
        string? compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp);
        if (compiler == null)
        {
            diagnostics = "C++ compiler not available.";
            return false;
        }
        ProcessStartInfo start = new(compiler)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        List<string> arguments = ["-std=c++23"];
        arguments.Add(OperatingSystem.IsMacOS() ? "-dynamiclib" : "-shared");
        if (!OperatingSystem.IsWindows())
            arguments.Add("-fPIC");
        arguments.AddRange(["-I", Path.Combine(output, "include"), "-iquote", sourceDirectory,
            Path.Combine(output, "src", "Classes.cpp"), "-o", library]);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        diagnostics = stdout.Result + stderr.Result;
        return process.ExitCode == 0;
    }

    private static bool CompileGeneratedBridge(string output, string sourceDirectory, out string diagnostics)
    {
        string? compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp);
        if (compiler == null)
        {
            diagnostics = "C++ compiler not available; set BGCS_CPP2C_CXX.";
            return false;
        }
        ProcessStartInfo start = new(compiler)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-std=c++23");
        start.ArgumentList.Add("-fsyntax-only");
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add(Path.Combine(output, "include"));
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add(sourceDirectory);
        start.ArgumentList.Add(Path.Combine(output, "src", "Classes.cpp"));
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        diagnostics = stdout.Result + stderr.Result;
        return process.ExitCode == 0;
    }

    [Fact]
    public void Config_SaveAndLoad_ShouldRoundTripValues()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string path = Path.Combine(temp, "cfg.json");

        try
        {
            Cpp2CGeneratorConfig cfg = new();
            cfg.IncludeFolders.Add("include-a");
            cfg.Defines.Add("DEF_A=1");
            cfg.NamePrefix = "Prefix";

            cfg.Save(path);
            var loaded = Cpp2CGeneratorConfig.Load(path);

            Assert.Contains("include-a", loaded.IncludeFolders);
            Assert.Contains("DEF_A=1", loaded.Defines);
            Assert.Equal("Prefix", loaded.NamePrefix);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void GenerationStep_AddGetOverwrite_ShouldWork()
    {
        Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

        var a = new DummyStepA(generator, new Cpp2CGeneratorConfig());
        var b = new DummyStepB(generator, new Cpp2CGeneratorConfig());

        generator.AddGenerationStep(a);
        Assert.Same(a, generator.GetGenerationStep<DummyStepA>());

        generator.OverwriteGenerationStep<DummyStepA>(b);
        Assert.Same(b, generator.GetGenerationStep<DummyStepB>());
        Assert.Throws<InvalidOperationException>(() => generator.GetGenerationStep<DummyStepA>());
    }

    [Fact]
    public void GetGenerationStep_WhenNotFound_ShouldThrow()
    {
        Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

        Assert.Throws<InvalidOperationException>(() => generator.GetGenerationStep<DummyStepA>());
    }

    [Fact]
    public void Generate_WhenParsingFails_ShouldPreserveLastSuccessfulOutputAndCallerFilters()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "class Demo { public: int Value(); };");
        List<string> allowedHeaders = [header];
        Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

        try
        {
            generator.Generate(header, output, allowedHeaders);
            string classesPath = Path.Combine(output, "include", "Classes.h");
            string lastGood = File.ReadAllText(classesPath);
            Assert.Single(allowedHeaders);
            File.WriteAllText(header, "class Demo {");

            generator.Generate(header, output, allowedHeaders);

            Assert.Equal(lastGood, File.ReadAllText(classesPath));
            Assert.Single(allowedHeaders);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    private sealed class DummyStepA : GenerationStep
    {
        public DummyStepA(Cpp2CCodeGenerator generator, Cpp2CGeneratorConfig config) : base(generator, config)
        {
        }

        public override string Name => "DummyA";

        public override void Configure(Cpp2CGeneratorConfig config)
        {
        }

        public override void Generate(FileSet files, ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyToMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyFromMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void Reset()
        {
        }
    }

    private sealed class DummyStepB : GenerationStep
    {
        public DummyStepB(Cpp2CCodeGenerator generator, Cpp2CGeneratorConfig config) : base(generator, config)
        {
        }

        public override string Name => "DummyB";

        public override void Configure(Cpp2CGeneratorConfig config)
        {
        }

        public override void Generate(FileSet files, ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyToMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyFromMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void Reset()
        {
        }
    }
}
