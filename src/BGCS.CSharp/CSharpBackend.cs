using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Managed;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using BGCS.Core.Interop;
using BGCS.CSharp.Validation;
using BGCS.Core.Targets;

namespace BGCS.CSharp;

/// <summary>Emits candidate C# sources from neutral semantic models and a checked emission plan.</summary>
public sealed class CSharpBackend : IBindingBackend
{
    /// <inheritdoc/>
    public ValueTask<ImmutableArray<ArtifactDescriptor>> EmitAsync(
        ManagedApiModel model,
        EmissionRequest request,
        CancellationToken cancellationToken
    ) {
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request, cancellationToken);
        if (!Enum.IsDefined(request.importMode)) throw new ArgumentOutOfRangeException(nameof(request));
        string ns = CSharpNames.Namespace(model.@namespace);
        List<(string Path, string Text)> sources = [];
        foreach (CSharpStoragePlan storage in plan.storage)
            Add("Types/" + storage.name.TrimStart('@') + ".g.cs", CSharpGeneratedTypeScope.WrapDeclarations(model,
                AbiStorageEmitter.Emit(storage) + AbiStorageEmitter.EmitAlignmentProbe(storage)));
        foreach (CSharpManagedEnumPlan declaration in plan.managedEnums)
            Add("Types/" + declaration.name.TrimStart('@') + ".g.cs", CSharpGeneratedTypeScope.WrapDeclarations(model, ManagedEnumEmitter.Emit(declaration)));
        foreach (CSharpAbiDelegatePlan declaration in plan.abiDelegates)
            Add("Types/" + declaration.name.TrimStart('@') + ".g.cs", CSharpGeneratedTypeScope.WrapDeclarations(model, AbiDelegateEmitter.Emit(declaration)));
        foreach (CSharpPointerCarrierPlan carrier in plan.pointerCarriers)
            Add("Types/" + carrier.name.TrimStart('@') + ".g.cs", CSharpGeneratedTypeScope.WrapDeclarations(model, PointerCarrierEmitter.Emit(plan, carrier)));
        Add("Api/" + plan.contextName.TrimStart('@') + ".g.cs", ContextEmitter.Emit(plan, request));
        Add("Api/" + plan.contextName.TrimStart('@') + ".Raw.g.cs", RawApiEmitter.Emit(plan));
        Add("Api/" + plan.contextName.TrimStart('@') + ".Methods.g.cs", FriendlyApiEmitter.Emit(plan, request));
        if (!plan.pointerFunctions.IsEmpty)
            Add("Api/" + plan.contextName.TrimStart('@') + ".Addresses.g.cs", "public sealed unsafe partial class " + plan.contextName + " {\n"
                + string.Concat(plan.pointerFunctions.Select(operation => PointerFunctionEmitter.Emit(plan, operation))) + "}\n");
        if (!plan.aliases.IsEmpty) Add("Api/" + plan.contextName.TrimStart('@') + ".Aliases.g.cs", AliasEmitter.Emit(plan));
        if (!plan.functionContainers.IsEmpty) Add("Api/" + plan.contextName.TrimStart('@') + ".FunctionContainers.g.cs", FunctionContainerEmitter.Emit(plan));
        if (!plan.defaultOverloads.IsEmpty || !plan.defaultConstants.IsEmpty) Add("Api/" + plan.contextName.TrimStart('@') + ".Defaults.g.cs", DefaultArgumentEmitter.Emit(plan));
        if (!plan.inputVariations.IsEmpty) Add("Api/" + plan.contextName.TrimStart('@') + ".Variations.g.cs", InputVariationEmitter.Emit(plan));
        if (!plan.constants.IsDefaultOrEmpty || !plan.stringConstants.IsDefaultOrEmpty) Add("Api/" + plan.contextName.TrimStart('@') + ".Constants.g.cs", ConstantEmitter.Emit(plan));
        if (plan.sharedContext is not null) Add("Api/" + model.name.TrimStart('@') + ".g.cs", SharedContextEmitter.Emit(plan, request.libraryName));
        if (!plan.recipeProjections.IsDefaultOrEmpty) Add("Extensions/ConfiguredLoweringProjections.g.cs", RecipeConversionEmitter.Emit(plan));
        if (!plan.objects.IsDefaultOrEmpty) Add("Api/" + plan.contextName.TrimStart('@') + ".Objects.g.cs", OwnedObjectEmitter.Emit(plan, request));
        foreach (CSharpVirtualProxyPlan proxy in plan.virtualProxies)
            Add("Callbacks/" + plan.contextName.TrimStart('@') + "." + proxy.name + ".g.cs", VirtualProxyEmitter.Emit(plan, proxy, request));
        foreach (CSharpCallbackMethodPlan operation in plan.callScopedCallbacks)
            Add("Callbacks/" + plan.contextName.TrimStart('@') + ".Call_" + operation.methodIndex + ".g.cs", CallScopedCallbackEmitter.Emit(plan, operation));
        foreach (CSharpRetainedCallbackPlan operation in plan.retainedCallbacks)
            Add("Callbacks/" + plan.contextName.TrimStart('@') + ".Registration_" + operation.methodIndex + ".g.cs", RetainedCallbackEmitter.Emit(plan, operation));
        foreach (CSharpAsyncOperationPlan operation in plan.asyncOperations)
            Add("Async/" + plan.contextName.TrimStart('@') + ".Operation_" + operation.methodIndex + ".g.cs", AsyncOperationEmitter.Emit(plan, operation));
        if (request.importMode != BindingImportMode.FunctionTable)
            Add("Imports/" + plan.contextName.TrimStart('@') + "NativeImports.g.cs", NativeImportEmitter.Emit(plan, request));
        if (request.includeRuntimeSource)
        {
            string[] features = ["modules.session", "modules.static", .. request.additionalRuntimeSourceFeatures];
            if (model.emitNativeMetadata) features = [.. features, "metadata.native-names"];
            if (request.importMode == BindingImportMode.FunctionTable)
            {
                features = [.. features, "modules.external-table"];
                if (request.managedRuntime is null || request.managedRuntime.moduleMechanism == NativeModuleMechanism.DynamicLibrary)
                    features = [.. features, "modules.dynamic"];
            }
            foreach (NativeTextEncoding encoding in plan.methods.SelectMany(method => method.stringInputs).Select(input => input.encoding).Distinct())
                features = [.. features, encoding switch { NativeTextEncoding.Utf8 => "strings.utf8-input", NativeTextEncoding.Utf16 => "strings.utf16-input", NativeTextEncoding.Utf32 => "strings.utf32-input", _ => throw new InvalidOperationException("An unsupported codec entered runtime source planning.") }];
            if (!plan.statusMethods.IsDefaultOrEmpty || !plan.valueStringResults.IsEmpty) features = [.. features, "errors.native-status"];
            if (plan.statusMethods.Any(method => method.ownedResult is not null) || !plan.directOwnedFactories.IsEmpty) features = [.. features, "handles.owned"];
            if (!plan.directBorrowedFactories.IsEmpty) features = [.. features, "handles.module-borrowed"];
            foreach (NativeTextEncoding encoding in plan.stringResults.Select(result => result.encoding).Concat(plan.valueStringResults.Select(result => result.encoding)).Distinct())
                features = [.. features, encoding switch
                {
                    NativeTextEncoding.Utf8 => "strings.utf8-result",
                    NativeTextEncoding.Utf16 => "strings.utf16-result",
                    NativeTextEncoding.Utf32 => "strings.utf32-result",
                    _ => throw new InvalidOperationException("An unsupported native text result encoding entered runtime source planning.")
                }];
            if (!plan.virtualProxies.IsEmpty) features = [.. features, "callbacks.static-registration", "errors.native-status"];
            if (plan.callScopedCallbacks.Any(operation => operation.callbacks.Any(callback => callback.staticThunk))) features = [.. features, "callbacks.static-registration"];
            if (!plan.retainedCallbacks.IsEmpty) features = [.. features, "callbacks.static-registration"];
            if (!plan.asyncOperations.IsEmpty) features = [.. features, "callbacks.static-registration", "async.operation"];
            foreach ((string path, string content) in new RuntimeSourceCatalog().GetSources(features, request.runtimeNamespace, cancellationToken))
                AddComplete(path, "// <auto-generated/>\n#nullable enable\n" + content);
        }
        NativeMetadataSyntaxDecorator.Apply(plan, sources, cancellationToken);
        if (request.singleFile)
        {
            string text = new SingleFileComposer().ComposeSources(sources, ns, model.interop.targetId, cancellationToken);
            sources = [(request.singleFileName, text)];
        }
        new EmittedInteropContractReader().Verify(plan, sources.ToArray(), request.libraryName, cancellationToken);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, string _) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!paths.Add(path)) throw new InvalidOperationException($"Candidate source path '{path}' conflicts with another artifact.");
        }
        ImmutableArray<ArtifactDescriptor>.Builder artifacts = ImmutableArray.CreateBuilder<ArtifactDescriptor>();
        foreach ((string path, string text) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.sink.WriteText(path, text);
            artifacts.Add(new(path, "text/x-csharp", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))))));
        }
        return ValueTask.FromResult(artifacts.ToImmutable());

        void Add(
            string path,
            string content
        ) {
            string usings = string.Concat(request.additionalUsings.Concat(plan.recipeProjections.Select(projection => projection.requiredNamespace)
                .Where(value => value is not null).Cast<string>()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(value => "using global::" + CSharpNames.Namespace(value) + ";\n"));
            string text = "// <auto-generated/>\n#nullable enable\n#pragma warning disable CS1591\n" + usings + "namespace " + ns + ";\n\n" + content;
            AddComplete(path, text);
        }

        void AddComplete(
            string path,
            string text
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.singleFile)
            {
                // Composition parses and formats the complete source set before
                // contract verification; formatting each fragment first is redundant.
                sources.Add((path, text));
                return;
            }
            SyntaxTree tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp14), path, cancellationToken: cancellationToken);
            Diagnostic[] errors = tree.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("The planned C# artifact is syntactically invalid: " + string.Join("; ", errors.Select(error => error.ToString())));
            sources.Add((path, GeneratedSourceFormatter.Normalize(tree.GetRoot(cancellationToken), "\n") + "\n"));
        }
    }
}
