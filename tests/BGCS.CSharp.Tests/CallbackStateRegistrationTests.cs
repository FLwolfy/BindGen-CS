using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class CallbackStateRegistrationTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task ExactCallbackAndUserDataAreReusedInDeclaredUnregisterOrder(BindingImportMode mode)
    {
        var original = RetainedCallbackTests.Model();
        InteropModule module = Pair(original.interop);
        var model = new DefaultManagedProjection().Project(module, new("Generated.Retained", "CallbackApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpRetainedCallbackPlan operation = Assert.Single(plan.retainedCallbacks);
        Assert.Empty(plan.callbackOmissions);
        Assert.Equal(NativeCallbackRegistrationKind.CallbackAndUserData, operation.kind);
        Assert.Equal("nint", operation.handleType);
        Assert.Equal(1, operation.unregisterCallbackIndex);
        Assert.Equal(0, operation.unregisterUserDataIndex);
        Assert.Equal("(int)(1)", operation.successExpression);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string source = sink.sources.Single(item => item.Key.Contains("Registration_", StringComparison.Ordinal)).Value;
        Assert.Contains("DangerousGetNativeUserData", source);
        Assert.Contains("KeepOpenUntilUnregistered", source);
        Assert.Contains("== (int)(1)", source);
        var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        MethodDeclarationSyntax stop = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "__BgcsStop_" + operation.methodIndex);
        InvocationExpressionSyntax invocation = Assert.IsType<InvocationExpressionSyntax>(stop.ExpressionBody!.Expression);
        Assert.Equal(2, invocation.ArgumentList.Arguments.Count);
        CastExpressionSyntax state = Assert.IsType<CastExpressionSyntax>(invocation.ArgumentList.Arguments[0].Expression);
        Assert.True(Assert.IsType<PredefinedTypeSyntax>(Assert.IsType<PointerTypeSyntax>(state.Type).ElementType).Keyword.IsKind(SyntaxKind.VoidKeyword));
        Assert.Equal("handle", state.Expression.ToString());
        CastExpressionSyntax callbackPointer = Assert.IsType<CastExpressionSyntax>(invocation.ArgumentList.Arguments[1].Expression);
        Assert.Equal("callbackPointer", callbackPointer.Expression.ToString());
        Assert.Single(syntax.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>(),
            expression => expression.IsKind(SyntaxKind.AddressOfExpression) && expression.Operand.ToString() == operation.callback.thunkName);
        string replacedState = syntax.ReplaceNode(state.Expression, SyntaxFactory.ParseExpression("(handle + 1)")).ToFullString();
        foreach (string changed in new[] { replacedState, source.Replace("== (int)(1)", "== (int)(0)", StringComparison.Ordinal) })
        {
            Assert.NotEqual(source, changed);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sink.sources.Select(item => (item.Key, item.Value == source ? changed : item.Value)), "native"));
        }
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("state")]
    [InlineData("failure")]
    [InlineData("success")]
    [InlineData("prototype")]
    [InlineData("null")]
    public void IncompletePairProtocolsKeepRawAndOmitOwningRegistration(string fault)
    {
        InteropModule module = Pair(RetainedCallbackTests.Model().interop);
        InteropFunction register = module.functions[0];
        CallbackContract callback = register.parameters[0].callback!;
        CallbackRegistrationContract registration = callback.registration!;
        registration = fault switch
        {
            "callback" => registration with { unregisterCallbackParameter = new("missing") },
            "state" => registration with { unregisterUserDataParameter = registration.unregisterCallbackParameter },
            "failure" => registration with { failureResultQuiescence = NativeQuiescenceKind.NoNewInvocations },
            "success" => registration with { successfulReturn = null },
            "null" => registration with { nullResultQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations },
            _ => registration
        };
        register = register with { parameters = register.parameters.SetItem(0, register.parameters[0] with { callback = callback with { registration = registration } }) };
        var functions = module.functions.SetItem(0, register);
        if (fault == "prototype") functions = functions.SetItem(1, functions[1] with
        { parameters = functions[1].parameters.SetItem(1, functions[1].parameters[1] with { type = new("pointer") }) });
        module = new(module.libraryId, module.targetId, module.pointerSize, module.types, functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Retained", "CallbackApi"), CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        Assert.Empty(plan.retainedCallbacks);
        Assert.Single(plan.callbackOmissions);
        Assert.Equal(2, plan.methods.Length);
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task VoidPairRegistrationHasNoInventedScalarSuccessChannel(BindingImportMode mode)
    {
        InteropModule module = Pair(RetainedCallbackTests.Model().interop);
        InteropFunction register = module.functions[0];
        CallbackContract callback = register.parameters[0].callback!;
        register = register with { returnType = new("void"), parameters = register.parameters.SetItem(0, register.parameters[0] with
        { callback = callback with { registration = callback.registration! with { successfulReturn = null, failureResultQuiescence = NativeQuiescenceKind.Unknown } } }) };
        module = new(module.libraryId, module.targetId, module.pointerSize, module.types, module.functions.SetItem(0, register), []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Retained", "CallbackApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Null(Assert.Single(plan.retainedCallbacks).successExpression);
        Assert.Empty(plan.callbackOmissions);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(item => (item.Key, item.Value)), "native");
    }

    private static InteropModule Pair(InteropModule module)
    {
        InteropFunction register = module.functions[0];
        CallbackContract callback = register.parameters[0].callback!;
        Evidence evidence = callback.evidence;
        CallbackRegistrationContract registration = new(null, NativeQuiescenceKind.Unknown, "any", evidence)
        {
            kind = NativeCallbackRegistrationKind.CallbackAndUserData,
            unregisterCallbackParameter = new("unregister-callback"), unregisterUserDataParameter = new("unregister-state"),
            successfulReturn = new(NativeScalarConstantKind.Integer, "1", 4, true, evidence),
            failureResultQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations
        };
        register = register with { returnType = new("int"), parameters = register.parameters.SetItem(0, register.parameters[0] with { callback = callback with { registration = registration } }) };
        InteropFunction stop = module.functions[1] with { parameters = [new(new("unregister-state"), "state", new("pointer")), new(new("unregister-callback"), "callback", new("callback-pointer"))] };
        return new(module.libraryId, module.targetId, module.pointerSize, module.types, [register, stop], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
    }
}
