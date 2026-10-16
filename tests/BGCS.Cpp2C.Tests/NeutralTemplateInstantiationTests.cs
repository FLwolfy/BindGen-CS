using System;
using System.Collections.Immutable;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Cpp2C.Planning;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NeutralTemplateInstantiationTests
{
    [Fact]
    public void QualifiedSpecializationsCanContainPointerReferenceAndArrayTemplateArguments()
    {
        TemplateInstantiationPlan plan = new TemplateInstantiationPlanner().Plan(new()
        { types = [new("pointer", "Demo::Selector<float*>"), new("reference", "Demo::Selector<int&>"), new("array", "Demo::Selector<int[3]>")] });
        Assert.Equal(3, plan.concreteTypes.Length);
        Assert.Empty(plan.preamble);
        Assert.Contains("Demo::Selector<float*>", plan.concreteTypes);
        Assert.Throws<ArgumentException>(() => new TemplateInstantiationPlanner().Plan(new() { types = [new("outer", "Demo::Selector<int>*")] }));
    }

    [Fact]
    public void ConcreteTypeAndFunctionRequests_ProduceFixedParseDeclarations()
    {
        TemplateInstantiationPlan plan = new TemplateInstantiationPlanner().Plan(new()
        {
            types = [new("integer-box", "Demo::Box<int>")],
            functions = [new("twice", "Demo::Twice<int>", "int", ["int"]), new("write", "Demo::Write<int>", "void", ["int&"])]
        });
        Assert.Equal("#include <utility>\n", plan.preamble);
        Assert.DoesNotContain("#include", plan.postamble);
        Assert.Contains("template class Demo::Box<int>;", plan.postamble);
        Assert.Contains("inline int function_", plan.postamble);
        Assert.Contains("using bgcs_signature = int (*)(int); auto bgcs_target = static_cast<bgcs_signature>(&::Demo::Twice<int>)", plan.postamble);
        Assert.Contains("return bgcs_target(std::forward<int>(bgcs_arg0))", plan.postamble);
        Assert.Contains("using bgcs_signature = void (*)(int&); auto bgcs_target = static_cast<bgcs_signature>(&::Demo::Write<int>)", plan.postamble);
        Assert.Contains("bgcs_target(std::forward<int&>(bgcs_arg0))", plan.postamble);
        Assert.Equal(2, plan.functionWrappers.Count);
        Assert.All(plan.functionWrappers.Values, wrapper => Assert.StartsWith("BGCSGeneratedTemplates::", wrapper));
    }

    [Theory]
    [InlineData("Demo::Box<int>; void injected()")]
    [InlineData("Demo::Box<int>\n#error injected")]
    [InlineData("Demo::Box<int")]
    [InlineData("Demo::Box<int>*")]
    [InlineData("Demo::Box<int>/* injected */")]
    public void TemplateDeclarationComponents_CannotInjectExtraSource(string type)
        => Assert.Throws<ArgumentException>(() => new TemplateInstantiationPlanner().Plan(new() { types = [new("box", type)] }));

    [Fact]
    public void DuplicateRequestIdentities_AreRejectedAcrossTypeAndFunctionKinds()
        => Assert.Throws<ArgumentException>(() => new TemplateInstantiationPlanner().Plan(new()
        { types = [new("same", "Demo::Box<int>")], functions = [new("same", "Demo::Twice<int>", "int", ["int"])] }));

    [Fact]
    public void ConcreteSelection_RequiresRequestedParserFactsAndSkipsUnboundTemplates()
    {
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { isSigned = true, primitiveKind = "Int" };
        NativeDeclaration generic = new(new("generic"), "Function", "Twice", "Demo::Twice", null, location)
        { templateParameters = [integer.id], templateKind = "FunctionTemplate", returnType = integer.id };
        TemplateInstantiationPlan plan = new TemplateInstantiationPlanner().Plan(new() { functions = [new("twice", "Demo::Twice<int>", "int", ["int"])] });
        string name = plan.functionWrappers["twice"];
        NativeDeclaration wrapper = new(new("wrapper"), "Function", name[(name.LastIndexOf("::", StringComparison.Ordinal) + 2)..], name, null, location) { returnType = integer.id };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer], [generic, wrapper], []) { language = SourceLanguage.Cpp };
        Assert.Equal(new[] { wrapper.id }, new TemplateInstantiationPlanner().SelectCallables(module, plan));
        NativeModule missing = new(module.targetId, module.triple, module.pointerSize, module.types, [generic], []) { language = SourceLanguage.Cpp };
        Assert.Throws<InvalidOperationException>(() => new TemplateInstantiationPlanner().SelectCallables(missing, plan));
    }
}
