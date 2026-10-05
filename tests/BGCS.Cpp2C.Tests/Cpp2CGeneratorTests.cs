using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BGCS.Core.Logging;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Facade;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using BGCS.Intermediate;
using BGCS.Intermediate.Bridges;
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

        Assert.NotNull(cfg.includeFolders);
        Assert.NotNull(cfg.systemIncludeFolders);
        Assert.NotNull(cfg.defines);
        Assert.NotNull(cfg.additionalArguments);
    }

    [Fact]
    public void Config_GetCType_ShouldPreservePointerReferenceAndQualificationShape()
    {
        Cpp2CGeneratorConfig config = new();
        CppType pointer = new CppPointerType(default, CppPrimitiveType.@int, System.IntPtr.Size);
        CppType reference = new CppReferenceType(default, new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.@float));

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

            Assert.DoesNotContain(gen.messages, x => x.severity is LogSeverity.Error or LogSeverity.Critical);
            Assert.True(gen.lastResult?.success);
            Assert.Contains(gen.lastResult!.module!.types, type => type.nativeName == "Demo");
            Assert.Contains(gen.lastResult.module.functions, function => function.nativeName == "Add");
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

            Assert.Equal("Demo", config.namePrefix);
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

            Assert.True(generator.lastResult?.success);
            Assert.True(File.Exists(Path.Combine(temp, "bridge-output", "include", "Classes.h")));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void GenerateConfigured_IncrementalCacheRestoresBridgeAndInvalidatesOnHeaderChange()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "sample.hpp");
        string configPath = Path.Combine(temp, "bridge.json");
        File.WriteAllText(header, "int cache_first();\n");
        File.WriteAllText(configPath,
            "{\"EntryFiles\":[\"sample.hpp\"],\"AllowedHeaders\":[\"sample.hpp\"],\"OutputPath\":\"bridge-output\"}");
        try
        {
            Cpp2CCodeGenerator first = new(Cpp2CGeneratorConfig.Load(configPath));
            first.GenerateConfigured();
            Assert.True(first.lastResult?.success);
            Assert.False(first.lastResult!.cacheHit);
            string firstKey = Assert.IsType<string>(first.lastResult.cacheKey);
            string classes = Path.Combine(temp, "bridge-output", "include", "Classes.h");
            File.WriteAllText(classes, "corrupted");

            Cpp2CCodeGenerator second = new(Cpp2CGeneratorConfig.Load(configPath));
            second.GenerateConfigured();
            Assert.True(second.lastResult!.cacheHit);
            Assert.NotNull(second.lastResult.module);
            Assert.Contains("cache_first", File.ReadAllText(classes), StringComparison.Ordinal);

            File.WriteAllText(header, "int cache_second();\n");
            Cpp2CCodeGenerator third = new(Cpp2CGeneratorConfig.Load(configPath));
            third.GenerateConfigured();
            Assert.False(third.lastResult!.cacheHit);
            Assert.NotEqual(firstKey, third.lastResult.cacheKey);
            Assert.Contains("cache_second", File.ReadAllText(classes), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void GenerateConfigured_ShouldNotChangeProcessCurrentDirectory()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp-config-cwd-" + Guid.NewGuid().ToString("N"));
        string configDirectory = Path.Combine(temp, "config");
        Directory.CreateDirectory(Path.Combine(configDirectory, "include"));
        File.WriteAllText(Path.Combine(configDirectory, "include", "sample.hpp"),
            "class Demo { public: int Add(int value); };\n");
        File.WriteAllText(Path.Combine(configDirectory, "bridge.json"),
            """
            {
              "entryFiles": [
                "include/sample.hpp"
              ],
              "includeFolders": [
                "include"
              ],
              "outputPath": "GeneratedBridge"
            }
            """);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load(Path.Combine(configDirectory, "bridge.json"));
            Cpp2CCodeGenerator generator = new(config);

            generator.GenerateConfigured();

            Assert.True(generator.lastResult?.success);
            Assert.Equal(originalDirectory, Environment.CurrentDirectory);
            Assert.True(File.Exists(Path.Combine(configDirectory, "GeneratedBridge", "bridge.manifest.json")));
        }
        finally
        {
            Assert.Equal(originalDirectory, Environment.CurrentDirectory);
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Config_Load_ShouldRejectCyclicBaseConfiguration()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp-config-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string first = Path.Combine(temp, "first.json");
        string second = Path.Combine(temp, "second.json");
        File.WriteAllText(first, "{\"baseConfig\":{\"url\":\"file://second.json\"}}");
        File.WriteAllText(second, "{\"baseConfig\":{\"url\":\"file://first.json\"}}");
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Cpp2CGeneratorConfig.Load(first));

            Assert.Contains("Circular BaseConfig reference", exception.Message, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(first), exception.Message, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(second), exception.Message, StringComparison.Ordinal);
        }
        finally
        {
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
    public void Generate_ClassMethod_ShouldPreserveConstPointerParameter()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-const-pointer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "input.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "class Text { public: void Set(const char* value) {} };\n");
        try
        {
            new Cpp2CCodeGenerator(new Cpp2CGeneratorConfig()).Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("const char* value", classesHeader, StringComparison.Ordinal);
        }
        finally
        {
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
            Assert.All(generator.lastResult!.module!.functions.Where(function => function.nativeName is "Create" or "Consume"), function =>
            {
                MarshallingPlan plan = function.nativeName == "Create" ? function.returnMarshalling : function.parameters[0].marshalling;
                Assert.Equal(BindingOwnership.Transferred, plan.ownership);
                Assert.True(plan.requiresCleanup);
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
            CppBridgeFunction sum = Assert.Single(generator.lastResult!.module!.functions, function => function.nativeName == "Sum");
            Assert.Equal(MarshallingStrategy.Span, sum.parameters[0].marshalling.strategy);
            Assert.Equal("values_count", sum.parameters[0].marshalling.lengthParameter);
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
            CppBridgeFunction vector = Assert.Single(generator.lastResult!.module!.functions, function => function.nativeName == "Double");
            Assert.Equal(MarshallingStrategy.Span, vector.returnMarshalling.strategy);
            Assert.Equal("out_count", vector.returnMarshalling.lengthParameter);
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
            CppBridgeFunction make = Assert.Single(generator.lastResult!.module!.functions, function => function.nativeName == "Make");
            Assert.Equal(BindingOwnership.Shared, make.returnMarshalling.ownership);
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
        config.virtualCallbackInterfaces.Add("ICompute");
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
            CppBridgeFunction maybe = Assert.Single(generator.lastResult!.module!.functions, function => function.nativeName == "Maybe");
            Assert.Equal(MarshallingStrategy.Optional, maybe.returnMarshalling.strategy);
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
        config.templateInstantiations.Add("Demo::Box<int>");
        try
        {
            Cpp2CCodeGenerator generator = new(config);

            generator.Generate(header, output);

            Assert.True(generator.lastResult?.success);
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
        File.WriteAllText(header, "#include <tuple>\nstd::tuple<int,float> GetValue();");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

            generator.Generate(header, output);

            Assert.False(generator.lastResult?.success);
            BindingDiagnostic diagnostic = Assert.Single(generator.lastResult!.diagnostics, value => value.code == "BGCSCPP001");
            Assert.Contains("TemplateInstantiations", diagnostic.message);
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

            Assert.True(generator.lastResult?.success);
            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("Demo_Maybe(bool set, Widget** out_value)", classesHeader);
            Assert.Contains("Demo_Read(Widget* value, bool value_has_value)", classesHeader);
            Assert.Contains("new Widget(*optional_result)", classesSource);
            CppBridgeFunction maybe = Assert.Single(generator.lastResult!.module!.functions, function => function.nativeName == "Maybe");
            Assert.Equal(BindingOwnership.Owned, maybe.returnMarshalling.ownership);
            Assert.True(maybe.returnMarshalling.requiresCleanup);
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
        config.functionTemplateInstantiations.Add("int Demo::Twice<int>(int)");
        try
        {
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.lastResult?.success, string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(value => value.message) ?? []));
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

    [Fact]
    public void Generate_PublicFacade_ExcludesPrivateImplementationAndDtoLifetimes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-facade-visibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string header = Path.Combine(directory, "facade.hpp");
            File.WriteAllText(header, """
                class Facade {
                public:
                    struct Values { int count; };
                    enum class Mode { First, Second };
                    int Read() const { return 7; }
                protected:
                    struct ProtectedState { int value; };
                private:
                    struct Impl;
                    Impl* state;
                    enum class InternalMode { Hidden };
                    struct PrivateOwner {
                    public:
                        struct NestedState { int value; };
                    };
                    Impl* ReadState();
                };
                struct PlainValue { int number; };
                """);
            string output = Path.Combine(directory, "out");
            Cpp2CCodeGenerator generator = new(new() { namePrefix = "test_" });
            generator.Generate(header, output);

            Assert.True(generator.lastResult?.success,
                string.Join(Environment.NewLine, generator.messages.Select(static value => value.message)));
            CppBridgeModule module = generator.lastResult!.module!;
            string declarations = File.ReadAllText(Path.Combine(output, "include", "Classes.h"))
                + File.ReadAllText(Path.Combine(output, "include", "enums.h"));
            foreach (string hidden in new[] { "Impl", "ProtectedState", "InternalMode", "PrivateOwner", "NestedState", "ReadState" })
            {
                Assert.DoesNotContain(hidden, declarations);
                Assert.DoesNotContain(module.types, type => type.nativeName.Contains(hidden, StringComparison.Ordinal));
                Assert.DoesNotContain(module.functions, function => function.exportName.Contains(hidden, StringComparison.Ordinal));
            }
            Assert.Contains(module.types, type => type.nativeName == "Facade::Values");
            Assert.Contains(module.types, type => type.nativeName == "Facade::Mode");
            Assert.DoesNotContain(module.functions, function => function.exportName.StartsWith("test_PlainValue", StringComparison.Ordinal)
                || function.exportName.StartsWith("test_Values", StringComparison.Ordinal));
            Assert.True(CompileGeneratedBridge(output, directory, out string diagnostics), diagnostics);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Generate_ForwardDeclaration_DoesNotInventCallableLifetimes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-forward-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string header = Path.Combine(directory, "forward.hpp");
            File.WriteAllText(header, "struct External; External* Borrow();");
            string output = Path.Combine(directory, "out");
            Cpp2CCodeGenerator generator = new(new() { namePrefix = "test_" });
            generator.Generate(header, output);

            Assert.True(generator.lastResult?.success);
            string declarations = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("typedef struct test_External test_External;", declarations);
            Assert.DoesNotContain("ExternalCreate", declarations);
            Assert.DoesNotContain(generator.lastResult!.module!.functions,
                function => function.kind is BindingFunctionKind.Constructor or BindingFunctionKind.Destructor);
            Assert.True(CompileGeneratedBridge(output, directory, out string diagnostics), diagnostics);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
            cfg.includeFolders.Add("include-a");
            cfg.defines.Add("DEF_A=1");
            cfg.namePrefix = "Prefix";

            cfg.Save(path);
            var loaded = Cpp2CGeneratorConfig.Load(path);

            Assert.Contains("include-a", loaded.includeFolders);
            Assert.Contains("DEF_A=1", loaded.defines);
            Assert.Equal("Prefix", loaded.namePrefix);
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

}
