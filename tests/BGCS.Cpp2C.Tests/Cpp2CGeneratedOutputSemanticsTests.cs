using System;
using System.IO;
using BGCS.Cpp2C.GenerationSteps;
using BGCS.Core.Logging;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public class Cpp2CGeneratedOutputSemanticsTests
{
    [Fact]
    public void Generate_VirtualClassAndEnum_OutputShouldContainBridgeSemantics()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-semantics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            """
            namespace Demo
            {
                enum Mode
                {
                    A = 1,
                    B = 2
                };

                class Counter
                {
                public:
                    virtual int Add(int a, int b);
                };
            }
            """);

        try
        {
            Cpp2CGeneratorConfig cfg = new() { NamePrefix = "BGCS_" };
            Cpp2CCodeGenerator gen = new(cfg);
            gen.AddGenerationStep(new EnumGenerationStep(gen, cfg));
            gen.AddGenerationStep(new ClassGenerationStep(gen, cfg));

            gen.Generate(header, output);
            Assert.DoesNotContain(gen.Messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);

            string common = Path.Combine(output, "include", "common.h");
            string classesH = Path.Combine(output, "include", "Classes.h");
            string enumsH = Path.Combine(output, "include", "enums.h");
            string classesCpp = Path.Combine(output, "src", "Classes.cpp");

            Assert.True(File.Exists(common));
            Assert.True(File.Exists(classesH));
            Assert.True(File.Exists(enumsH));
            Assert.True(File.Exists(classesCpp));

            string classesHeaderText = File.ReadAllText(classesH);
            Assert.Contains("typedef struct BGCS_Counter BGCS_Counter;", classesHeaderText, StringComparison.Ordinal);
            Assert.Contains("BGCS_API(BGCS_Counter*) BGCS_CounterCreate(void);", classesHeaderText, StringComparison.Ordinal);
            Assert.Contains("BGCS_API(const char*) BGCS_GetLastError(void);", File.ReadAllText(common), StringComparison.Ordinal);
            Assert.Contains("BGCS_API(void) BGCS_CounterDestroy(BGCS_Counter* self);", classesHeaderText, StringComparison.Ordinal);
            Assert.Contains("BGCS_API(int) BGCS_Counter_Add(BGCS_Counter* self, int a, int b);", classesHeaderText, StringComparison.Ordinal);

            string classesCppText = File.ReadAllText(classesCpp);
            Assert.Contains("BGCS_API_INTERNAL(BGCS_Counter*) BGCS_CounterCreate(void)", classesCppText, StringComparison.Ordinal);
            Assert.Contains("return reinterpret_cast<BGCS_Counter*>(new Demo::Counter());", classesCppText, StringComparison.Ordinal);
            Assert.Contains("BGCS_API_INTERNAL(void) BGCS_CounterDestroy(BGCS_Counter* self)", classesCppText, StringComparison.Ordinal);
            Assert.Contains("delete ptr;", classesCppText, StringComparison.Ordinal);
            Assert.Contains("return ptr->Add(a, b);", classesCppText, StringComparison.Ordinal);
            Assert.Contains("catch (const std::exception& exception)", classesCppText, StringComparison.Ordinal);

            string enumsText = File.ReadAllText(enumsH);
            Assert.Contains("typedef enum", enumsText, StringComparison.Ordinal);
            Assert.Contains("BGCS_Demo_Mode_A = 1", enumsText, StringComparison.Ordinal);
            Assert.Contains("BGCS_Demo_Mode_B = 2", enumsText, StringComparison.Ordinal);
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
    public void Generate_ConstructorsStaticAndReferenceMethods_ShouldProduceAbiSafeSignatures()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-members-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "members.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            """
            namespace Demo
            {
                class Widget
                {
                public:
                    Widget(int value);
                    static int Twice(int value);
                    void Set(int& value);
                    int& Value();
                };
            }
            """);

        try
        {
            Cpp2CGeneratorConfig config = new() { NamePrefix = "BGCS_" };
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            string classesHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string classesSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("BGCS_API(BGCS_Widget*) BGCS_WidgetCreate(int value);", classesHeader);
            Assert.Contains("BGCS_API(int) BGCS_Widget_Twice(int value);", classesHeader);
            Assert.Contains("BGCS_API(void) BGCS_Widget_Set(BGCS_Widget* self, int* value);", classesHeader);
            Assert.Contains("BGCS_API(int*) BGCS_Widget_Value(BGCS_Widget* self);", classesHeader);
            Assert.Contains("new Demo::Widget(value)", classesSource);
            Assert.Contains("Demo::Widget::Twice(value)", classesSource);
            Assert.Contains("ptr->Set(*value);", classesSource);
            Assert.Contains("return &(ptr->Value());", classesSource);
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
