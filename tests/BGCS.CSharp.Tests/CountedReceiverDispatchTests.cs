using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class CountedReceiverDispatchTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task DirectAndCallbackOwnedHelpersComputeCountsInsideTheirCheckedTextBoundary(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        ManagedApiModel callbacks = OwnedInstanceCallbackTests.Model(userData: true);
        InteropModule callbackModule = AddText(callbacks.interop, function => function.id.value is "run" or "spawn" or "clone");
        ManagedApiModel callbackModel = new DefaultManagedProjection().Project(callbackModule, new(callbacks.@namespace, callbacks.name), CancellationToken.None);
        await Verify(callbackModel, 3, callbacks: true);
        InteropModule direct = AddText(DirectCOwnershipTests.Module(), function => function.id.value == "get");
        await Verify(new DefaultManagedProjection().Project(direct, DirectCOwnershipTests.Request(), CancellationToken.None), 1, callbacks: false);

        async Task Verify(
            ManagedApiModel model,
            int textMethods,
            bool callbacks
        ) {
            NeutralBackendTests.MemorySink sink = new();
            EmissionRequest request = new("counted_receivers", sink)
            { importMode = mode, includeRuntimeSource = sourceRuntime, runtimeNamespace = sourceRuntime ? model.@namespace + ".Runtime" : "BGCS.Runtime" };
            CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
            Assert.Equal(textMethods, plan.methods.Count(method => !method.stringInputs.IsEmpty));
            if (callbacks) Assert.Equal(2, plan.callScopedCallbacks.Length);
            else Assert.Single(Assert.Single(plan.objects).directMethods);
            await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
            await ValueStringReceiverTests.Compile(sink, "public static class ReceiverConsumer { }", mode, sourceRuntime);
            new EmittedInteropContractReader().Verify(plan, sink.sources.Select(pair => (pair.Key, pair.Value)), request.libraryName);
            MethodDeclarationSyntax[] helpers = sink.sources.SelectMany(pair => CSharpSyntaxTree.ParseText(pair.Value).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                .Where(method => method.Identifier.ValueText.StartsWith(callbacks ? "__BgcsObjectCall" : "__BgcsDirectReceiverCall", StringComparison.Ordinal)
                    && method.ToFullString().Contains("EncodeCounted(", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(helpers);
            Assert.All(helpers, helper => Assert.DoesNotContain(helper.ParameterList.Parameters, parameter => parameter.Identifier.ValueText == "textLength"));
            foreach (string token in new[] { "EncodeCounted(", ".encodedCodeUnitCount", "m_handle.Invoke(" })
            {
                var changed = sink.sources.Select(pair => (pair.Key, pair.Value.Replace(token, "Changed" + token, StringComparison.Ordinal))).ToArray();
                Assert.Contains(changed, pair => pair.Item2 != sink.sources[pair.Key]);
                Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
            }
        }
    }

    private static InteropModule AddText(
        InteropModule module,
        Func<InteropFunction, bool> selected
    ) {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:counted-owned-receiver-text");
        InteropType unit = new(new("counted-unit"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType pointer = new(new("counted-pointer"), "unsigned char*", NativeTypeCategory.Pointer, 8, 8) { elementType = unit.id };
        InteropType count = new(new("counted-count"), "unsigned int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "UnsignedInt", isSigned = false };
        var functions = module.functions.Select(function =>
        {
            if (!selected(function)) return function;
            StableId length = new(function.id.value + "-text-length");
            InteropParameter text = new(new(function.id.value + "-text"), "text", pointer.id)
            {
                text = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits, NativeParameterDirection.Input, new(NativeLifetimeKind.Call, null, evidence), evidence)
                { lengthParameter = length, lengthDirection = NativeParameterDirection.Input, zeroTerminated = false, retainsPointer = false, allowsNull = false }
            };
            return function with { parameters = function.parameters.Add(text).Add(new(length, "textLength", count.id)) };
        }).ToImmutableArray();
        return new(module.libraryId, module.targetId, module.pointerSize, module.types.AddRange(ImmutableArray.Create(unit, pointer, count)), functions, module.diagnostics)
        { byteOrder = module.byteOrder, constants = module.constants };
    }
}
