using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Emission;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

/// <summary>Rechecks actual emitted import syntax against the frozen C# ABI plan.</summary>
public sealed class EmittedInteropContractReader
{
    /// <summary>Rejects missing, duplicated or structurally changed imports before candidate publication.</summary>
    /// <param name="plan">The frozen native carrier and import plan.</param>
    /// <param name="sources">The complete candidate C# source set, including standalone Runtime sources.</param>
    /// <param name="libraryName">The required native import library identity.</param>
    /// <param name="cancellationToken">Cancels source parsing and contract checks.</param>
    public void Verify(
        CSharpEmissionPlan plan,
        IEnumerable<(string Path, string Text)> sources,
        string libraryName,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sources);
        if (plan.libraryImportUsesPlatformDefault && (plan.importMode != BindingImportMode.LibraryImport
            || plan.managedRuntime?.libraryImportConvention != BGCS.Contracts.Targets.ManagedImportConventionStrategy.BrowserCdeclDefault
            || plan.managedRuntime.kind != BGCS.Contracts.Targets.ManagedRuntimeKind.BrowserWasm
            || plan.managedRuntime.moduleMechanism != BGCS.Contracts.Targets.NativeModuleMechanism.StaticAdapter || plan.api.interop.pointerSize != 4
            || plan.methods.Any(method => method.callingConvention != "Cdecl")))
            throw new InvalidOperationException("The emitted import plan has no established browser Cdecl default.");
        Dictionary<string, TypeSyntax> expectedTypes = new(StringComparer.Ordinal);
        string context = string.Join(".", plan.api.@namespace.Split('.').Select(part => part.TrimStart('@'))) + "." + plan.contextName.TrimStart('@');
        List<CompilationUnitSyntax> roots = [];
        foreach ((string path, string text) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxTree tree = CSharpSyntaxTree.ParseText(text, new(LanguageVersion.CSharp14), path, cancellationToken: cancellationToken);
            Diagnostic[] errors = tree.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("A processed C# artifact is syntactically invalid: " + string.Join("; ", errors.Select(error => error.ToString())));
            roots.Add((CompilationUnitSyntax)tree.GetRoot(cancellationToken));
        }
        if (plan.api.emitNativeMetadata) roots = NativeMetadataSyntaxDecorator.VerifyAndRemove(plan, roots);
        // Build indexes from the actual processed syntax on every verification.
        // Import attributes can occur outside methods, including local functions
        // and lambdas, so the traversal deliberately includes complete bodies.
        List<ClassDeclarationSyntax> classes = [];
        List<DelegateDeclarationSyntax> delegates = [];
        List<BaseTypeDeclarationSyntax> storageTypes = [];
        List<MethodDeclarationSyntax> collectedImports = [];
        bool hasImportAttribute = false;
        foreach (CompilationUnitSyntax root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int visited = 0;
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if ((++visited & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (node is BaseTypeDeclarationSyntax storageType) storageTypes.Add(storageType);
                if (node is ClassDeclarationSyntax declaration) classes.Add(declaration);
                else if (node is DelegateDeclarationSyntax callback) delegates.Add(callback);
                else if (node is MethodDeclarationSyntax method
                    && method.AttributeLists.SelectMany(list => list.Attributes).Any(attribute => IsImportName(attribute.Name))) collectedImports.Add(method);
                if (node is AttributeSyntax attribute)
                {
                    if (!plan.api.emitNativeMetadata) NativeMetadataSyntaxDecorator.VerifyDisabledAttribute(attribute);
                    if (IsImportName(attribute.Name)) hasImportAttribute = true;
                }
            }
        }
        ILookup<string, ClassDeclarationSyntax> classDeclarations = classes.ToLookup(FullName, StringComparer.Ordinal);
        ILookup<string, DelegateDeclarationSyntax> delegateDeclarations = delegates.ToLookup(type => FullName(type, type.Identifier.ValueText), StringComparer.Ordinal);
        ILookup<string, BaseTypeDeclarationSyntax> storageDeclarations = storageTypes.ToLookup(type => FullName(type, type.Identifier.ValueText), StringComparer.Ordinal);
        MethodDeclarationSyntax[] allImports = collectedImports.ToArray();
        ClassDeclarationSyntax[] contexts = classDeclarations[context].ToArray();
        if (contexts.Length == 0) throw new InvalidOperationException("The emitted ABI context is absent from the complete candidate source set.");
        PhysicalImportContractVerifier.Verify(plan, contexts, libraryName);
        if (plan.api.nestGeneratedTypesInApi)
        {
            string ownerName = CSharpGeneratedTypeScope.DeclarationNamespace(plan.api).Replace("@", string.Empty, StringComparison.Ordinal);
            ClassDeclarationSyntax[] owners = classDeclarations[ownerName].ToArray();
            if (owners.Length == 0 || owners.Any(owner => !owner.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
                || !owner.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword))
                || owner.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)) != (plan.api.style == BGCS.Contracts.Managed.ManagedApiStyle.Static)
                || plan.api.style == BGCS.Contracts.Managed.ManagedApiStyle.Instance && !owner.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.SealedKeyword))))
                throw new InvalidOperationException("The nested generated type owner changed its planned identity, visibility or API lifetime style.");
        }
        LibraryNameContractVerifier.Verify(plan, libraryName, roots, contexts);
        ImportLibraryNameContractVerifier.Verify(plan, libraryName, roots);
        foreach (CSharpAbiDelegatePlan declaration in plan.abiDelegates)
        {
            string name = CSharpGeneratedTypeScope.DeclarationNamespace(plan.api).Replace("@", string.Empty, StringComparison.Ordinal) + "." + declaration.name.TrimStart('@');
            DelegateDeclarationSyntax[] actual = delegateDeclarations[name].ToArray();
            MemberDeclarationSyntax expected = SyntaxFactory.ParseMemberDeclaration(AbiDelegateEmitter.Emit(declaration))!;
            if (actual.Length != 1 || !SyntaxFactory.AreEquivalent(actual[0], expected))
                throw new InvalidOperationException("A named native delegate changed its exact callback prototype, convention or public type identity: " + declaration.name);
        }
        FunctionContainerContractVerifier.Verify(plan, roots, contexts);
        DefaultArgumentContractVerifier.Verify(plan, contexts);
        NullDefaultContractVerifier.Verify(plan, contexts);
        InputVariationContractVerifier.Verify(plan, contexts);
        BufferValueContractVerifier.Verify(plan, libraryName, contexts);
        OwnedCProjectionContractVerifier.Verify(plan, libraryName, contexts);
        StringResultContractVerifier.Verify(plan, contexts);
        PointerCarrierContractVerifier.Verify(plan, roots, contexts);
        if (ContextEmitter.HasTemporaryAllocator(plan))
        {
            ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(ContextEmitter.Emit(plan, libraryName))!;
            ILookup<string, MemberDeclarationSyntax> actual = contexts.SelectMany(type => type.Members).ToLookup(SharedMemberKey, StringComparer.Ordinal);
            foreach (MemberDeclarationSyntax member in expected.Members)
            {
                MemberDeclarationSyntax[] declarations = actual[SharedMemberKey(member)].ToArray();
                if (declarations.Length != 1 || !SyntaxFactory.AreEquivalent(declarations[0], member))
                    throw new InvalidOperationException("The temporary allocator context's factory, complete table, session ownership or cleanup contract changed: " + SharedMemberKey(member));
            }
            EmissionRequest request = new(libraryName, new InspectionSink())
            {
                importMode = plan.importMode,
                runtimeNamespace = plan.runtimeNamespace,
                managedRuntime = plan.managedRuntime,
                staticExportTable = plan.staticExportTable
            };
            foreach (string projection in new[] { FriendlyApiEmitter.Emit(plan, request), OwnedObjectEmitter.Emit(plan, request) })
            {
                ClassDeclarationSyntax projected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(projection)!;
                foreach (MethodDeclarationSyntax member in projected.Members.OfType<MethodDeclarationSyntax>()
                    .Where(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "EncodeNullTerminated")))
                {
                    MemberDeclarationSyntax[] declarations = actual[SharedMemberKey(member)].ToArray();
                    if (declarations.Length != 1 || !SyntaxFactory.AreEquivalent(declarations[0], member))
                        throw new InvalidOperationException("A temporary text projection changed its checked encoding, allocator ownership, native call or matching cleanup: " + SharedMemberKey(member));
                }
            }
        }
        if (!plan.recipeProjections.IsEmpty)
        {
            ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(RecipeConversionEmitter.Emit(plan))!;
            ClassDeclarationSyntax expectedConversions = expected.Members.OfType<ClassDeclarationSyntax>().Single();
            ClassDeclarationSyntax[] actualConversions = contexts.SelectMany(type => type.Members.OfType<ClassDeclarationSyntax>())
                .Where(type => type.Identifier.ValueText == "RecipeConversions").ToArray();
            if (actualConversions.Length != 1 || !SyntaxFactory.AreEquivalent(expectedConversions, actualConversions[0]))
                throw new InvalidOperationException("An external consumer field-copy conversion changed its checked raw carrier or exact field assignments.");
        }
        if (plan.sharedContext is { } shared)
        {
            string facadeName = string.Join(".", plan.api.@namespace.Split('.').Select(part => part.TrimStart('@'))) + "." + shared.name.TrimStart('@');
            ClassDeclarationSyntax[] facades = classDeclarations[facadeName].ToArray();
            if (facades.Length == 0 || facades.Any(type => !type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
                || !type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)) || !type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword))))
                throw new InvalidOperationException("The explicitly shared static facade is absent or changed its lifetime style.");
            ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(SharedContextEmitter.Emit(plan, libraryName))!;
            ILookup<string, MemberDeclarationSyntax> actual = facades.SelectMany(type => type.Members).ToLookup(SharedMemberKey, StringComparer.Ordinal);
            foreach (MemberDeclarationSyntax member in expected.Members)
            {
                MemberDeclarationSyntax[] declarations = actual[SharedMemberKey(member)].ToArray();
                if (declarations.Length != 1 || !SyntaxFactory.AreEquivalent(declarations[0], member))
                    throw new InvalidOperationException("The shared context's actual initialization, forwarding or cleanup contract changed: " + SharedMemberKey(member));
            }
        }
        if (!plan.aliases.IsEmpty)
        {
            ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(AliasEmitter.Emit(plan))!;
            ILookup<string, MemberDeclarationSyntax> contextMembers = contexts.SelectMany(type => type.Members).ToLookup(SharedMemberKey, StringComparer.Ordinal);
            ILookup<string, MemberDeclarationSyntax> rawMembers = contexts.SelectMany(type => type.Members.OfType<ClassDeclarationSyntax>())
                .Where(type => type.Identifier.ValueText == "RawApi").SelectMany(type => type.Members).ToLookup(SharedMemberKey, StringComparer.Ordinal);
            foreach (MemberDeclarationSyntax member in expected.Members)
            {
                if (member is ClassDeclarationSyntax raw)
                    foreach (MemberDeclarationSyntax forwarding in raw.Members) Check(forwarding, rawMembers);
                else Check(member, contextMembers);
            }
            static void Check(
                MemberDeclarationSyntax member,
                ILookup<string, MemberDeclarationSyntax> actual
            ) {
                MemberDeclarationSyntax[] matches = actual[SharedMemberKey(member)].ToArray();
                if (matches.Length != 1 || !SyntaxFactory.AreEquivalent(matches[0], member))
                    throw new InvalidOperationException("A managed alias changed its original checked forwarding contract: " + SharedMemberKey(member));
            }
        }
        foreach (CSharpVirtualProxyPlan proxy in plan.virtualProxies)
        {
            ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(VirtualProxyEmitter.Emit(plan, proxy, plan.runtimeNamespace))!;
            ILookup<string, MemberDeclarationSyntax> actual = contexts.SelectMany(type => type.Members).ToLookup(SharedMemberKey, StringComparer.Ordinal);
            foreach (MemberDeclarationSyntax member in expected.Members)
            {
                MemberDeclarationSyntax[] declarations = actual[SharedMemberKey(member)].ToArray();
                if (declarations.Length != 1 || !SyntaxFactory.AreEquivalent(declarations[0], member))
                    throw new InvalidOperationException("The generated virtual callback's thunk, native lifetime or cleanup contract changed: " + SharedMemberKey(member));
            }
        }
        foreach (CSharpCallbackMethodPlan operation in plan.callScopedCallbacks)
        {
            ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(CallScopedCallbackEmitter.Emit(plan, operation))!;
            ILookup<string, MemberDeclarationSyntax> actual = contexts.SelectMany(type => type.Members).ToLookup(SharedMemberKey, StringComparer.Ordinal);
            foreach (MemberDeclarationSyntax member in expected.Members)
            {
                MemberDeclarationSyntax[] declarations = actual[SharedMemberKey(member)].ToArray();
                if (declarations.Length != 1 || !SyntaxFactory.AreEquivalent(declarations[0], member))
                    throw new InvalidOperationException("A generated managed callback changed its exact reverse-call ABI, fallback, state root, exception boundary or native call lease: " + SharedMemberKey(member));
            }
        }
        foreach (CSharpRetainedCallbackPlan operation in plan.retainedCallbacks)
        {
            ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(RetainedCallbackEmitter.Emit(plan, operation))!;
            ILookup<string, MemberDeclarationSyntax> actual = contexts.SelectMany(type => type.Members).ToLookup(SharedMemberKey, StringComparer.Ordinal);
            foreach (MemberDeclarationSyntax member in expected.Members)
            {
                MemberDeclarationSyntax[] declarations = actual[SharedMemberKey(member)].ToArray();
                if (declarations.Length != 1 || !SyntaxFactory.AreEquivalent(declarations[0], member))
                    throw new InvalidOperationException("A retained callback changed its reverse-call ABI, handle publication, native stop-and-join, module root or exception boundary: " + SharedMemberKey(member));
            }
        }
        foreach (CSharpAsyncOperationPlan operation in plan.asyncOperations)
        {
            ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(AsyncOperationEmitter.Emit(plan, operation))!;
            ILookup<string, MemberDeclarationSyntax> actual = contexts.SelectMany(type => type.Members).ToLookup(SharedMemberKey, StringComparer.Ordinal);
            foreach (MemberDeclarationSyntax member in expected.Members)
            {
                MemberDeclarationSyntax[] declarations = actual[SharedMemberKey(member)].ToArray();
                if (declarations.Length != 1 || !SyntaxFactory.AreEquivalent(declarations[0], member))
                    throw new InvalidOperationException("An async operation changed its completion/cancellation, native join, buffer pin, module lifetime or reverse-call ABI: " + SharedMemberKey(member));
            }
        }
        if (!plan.constants.IsDefaultOrEmpty || !plan.stringConstants.IsDefaultOrEmpty)
        {
            ClassDeclarationSyntax[] containers = contexts.SelectMany(type => type.Members.OfType<ClassDeclarationSyntax>())
                .Where(type => type.Identifier.ValueText == "Constants").ToArray();
            if (containers.Length != 1 || !containers[0].Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
                || !containers[0].Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword))
                || containers[0].Members.Count != plan.constants.Length + plan.stringConstants.Length)
                throw new InvalidOperationException("The planned native constant container is absent, duplicated or changed.");
            foreach (CSharpConstantPlan constant in plan.constants)
            {
                FieldDeclarationSyntax[] fields = containers[0].Members.OfType<FieldDeclarationSyntax>()
                    .Where(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == constant.name.TrimStart('@'))).ToArray();
                if (fields.Length != 1 || !SyntaxFactory.AreEquivalent(fields[0], SyntaxFactory.ParseMemberDeclaration(ConstantEmitter.Member(constant))))
                    throw new InvalidOperationException("An emitted native constant changed its planned width, value or visibility: " + constant.name);
            }
            foreach (CSharpStringConstantPlan constant in plan.stringConstants)
            {
                MemberDeclarationSyntax[] members = containers[0].Members.Where(member => member is FieldDeclarationSyntax field
                    && field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == constant.name.TrimStart('@'))
                    || member is PropertyDeclarationSyntax property && property.Identifier.ValueText == constant.name.TrimStart('@')).ToArray();
                if (members.Length != 1 || !SyntaxFactory.AreEquivalent(members[0], SyntaxFactory.ParseMemberDeclaration(ConstantEmitter.Member(constant))))
                    throw new InvalidOperationException("An emitted native literal changed its exact Unicode value or readonly code units: " + constant.name);
            }
        }
        ILookup<(string Owner, string Name), MethodDeclarationSyntax> importDeclarations = allImports
            .Where(method => method.Parent is ClassDeclarationSyntax)
            .ToLookup(method => (FullName((ClassDeclarationSyntax)method.Parent!), method.Identifier.ValueText));
        string storageNamespace = CSharpGeneratedTypeScope.DeclarationNamespace(plan.api).Replace("@", string.Empty, StringComparison.Ordinal);
        foreach (CSharpManagedEnumPlan declaration in plan.managedEnums)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BaseTypeDeclarationSyntax[] candidates = storageDeclarations[storageNamespace + "." + declaration.name.TrimStart('@')].ToArray();
            if (candidates.Length != 1 || candidates[0] is not EnumDeclarationSyntax enumeration
                || !enumeration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
                || enumeration.BaseList?.Types.Count != 1 || !Equivalent(enumeration.BaseList.Types[0].Type, Expected(declaration.underlyingType))
                || enumeration.Members.Count != declaration.values.Length
                || enumeration.Members.Select((
                    member,
                    index
                ) => member.Identifier.ValueText == declaration.values[index].name.TrimStart('@')
                    && member.EqualsValue is not null && SyntaxFactory.AreEquivalent(member.EqualsValue.Value, SyntaxFactory.ParseExpression(declaration.values[index].value))).Any(match => !match)
                || enumeration.AttributeLists.SelectMany(list => list.Attributes).Count(attribute => Compact(attribute.Name) is "global::System.Flags" or "global::System.FlagsAttribute") != (declaration.isFlags ? 1 : 0))
                throw new InvalidOperationException("An emitted managed enum changed its planned integral representation, values, visibility or flags: " + declaration.name);
        }
        foreach (CSharpStoragePlan storage in plan.storage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (storage.alignmentProbeName is string probeName)
            {
                BaseTypeDeclarationSyntax[] probes = storageDeclarations[storageNamespace + "." + probeName].ToArray();
                MemberDeclarationSyntax expected = SyntaxFactory.ParseMemberDeclaration(AbiStorageEmitter.EmitAlignmentProbe(storage))!;
                if (probes.Length != 1 || !SyntaxFactory.AreEquivalent(probes[0], expected))
                    StorageFailure(storage, "The managed alignment probe changed its complete carrier, packing or field set.");
            }
            BaseTypeDeclarationSyntax[] carriers = storageDeclarations[storageNamespace + "." + storage.name.TrimStart('@')].ToArray();
            if (carriers.Length != 1) StorageFailure(storage, "The storage declaration is missing or duplicated.");
            if (storage.enumUnderlyingType is string integral)
            {
                if (carriers[0] is not EnumDeclarationSyntax enumeration || enumeration.BaseList?.Types.Count != 1 || !Equivalent(enumeration.BaseList.Types[0].Type, Expected(integral))
                    || enumeration.Members.Count != storage.enumValues.Length || enumeration.Members.Select((
                        member,
                        index
                    ) => member.Identifier.ValueText == storage.enumValues[index].name.TrimStart('@')
                        && member.EqualsValue is not null && SyntaxFactory.AreEquivalent(member.EqualsValue.Value, SyntaxFactory.ParseExpression(storage.enumValues[index].value))).Any(match => !match))
                    StorageFailure(storage, "The enum's actual integral carrier or values changed.");
                continue;
            }
            if (carriers[0] is not StructDeclarationSyntax record) StorageFailure(storage, "The unmanaged storage declaration is not a struct.");
            record = (StructDeclarationSyntax)carriers[0];
            if (!record.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
                || !record.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
                StorageFailure(storage, "The unmanaged storage declaration must remain a public partial struct.");
            FieldDeclarationSyntax[] fields = record.Members.OfType<FieldDeclarationSyntax>().ToArray();
            if (storage.presentation is not null || !storage.valueMembers.IsEmpty)
            {
                MemberDeclarationSyntax expected = SyntaxFactory.ParseMemberDeclaration(AbiStorageEmitter.Emit(storage))!;
                if (!SyntaxFactory.AreEquivalent(record, expected))
                    StorageFailure(storage, "A customized carrier changed its exact fields, data constructor, value receiver forwarding, size constant or sentinel comparison.");
                fields = fields.Where(field => !field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword))).ToArray();
            }
            if (storage.arrayLength is int length)
            {
                if (fields.Length != 1 || fields[0].Declaration.Variables.Count != 1) StorageFailure(storage, "An inline array changed its element storage set.");
                if (storage.pointerArrayWidth is int pointerWidth)
                {
                    AttributeSyntax? layout = Attribute(record, "global::System.Runtime.InteropServices.StructLayout");
                    if (!Equivalent(fields[0].Declaration.Type, Expected(pointerWidth == 4 ? "uint" : "ulong")) || !fields[0].Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.FixedKeyword))
                        || fields[0].Declaration.Variables[0].ArgumentList?.Arguments.Count != 1 || !Number(fields[0].Declaration.Variables[0].ArgumentList!.Arguments[0].Expression, length)
                        || layout?.ArgumentList is not AttributeArgumentListSyntax arguments || !Number(Named(arguments, "Size"), storage.size!.Value))
                        StorageFailure(storage, "The pointer array's native slot width, extent or size changed.");
                }
                else
                {
                    AttributeSyntax? inline = Attribute(record, "global::System.Runtime.CompilerServices.InlineArray");
                    if (!Equivalent(fields[0].Declaration.Type, Expected(storage.arrayElementType!)) || inline?.ArgumentList?.Arguments.Count != 1 || !Number(inline.ArgumentList.Arguments[0].Expression, length))
                        StorageFailure(storage, "The inline array's element carrier or extent changed.");
                }
                continue;
            }
            if (storage.size is int size)
            {
                AttributeSyntax? layout = Attribute(record, "global::System.Runtime.InteropServices.StructLayout");
                if (layout?.ArgumentList is not AttributeArgumentListSyntax arguments || arguments.Arguments.Count(argument => argument.NameEquals is null) != 1
                    || Compact(arguments.Arguments.First(argument => argument.NameEquals is null).Expression) != "global::System.Runtime.InteropServices.LayoutKind.Explicit"
                    || !Number(Named(arguments, "Size"), size) || !Number(Named(arguments, "Pack"), storage.packing))
                    StorageFailure(storage, "The explicit native storage size or packing changed.");
            }
            if (!storage.bitfields.IsEmpty)
            {
                FieldDeclarationSyntax[] buffers = fields.Where(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "m_bitStorage")).ToArray();
                AttributeSyntax? offset = buffers.Length == 1 ? Attribute(buffers[0], "global::System.Runtime.InteropServices.FieldOffset") : null;
                if (buffers.Length != 1 || buffers[0].Declaration.Variables.Count != 1 || !buffers[0].Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword))
                    || !buffers[0].Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.FixedKeyword)) || !Equivalent(buffers[0].Declaration.Type, Expected("byte"))
                    || buffers[0].Declaration.Variables[0].ArgumentList?.Arguments.Count != 1 || !Number(buffers[0].Declaration.Variables[0].ArgumentList!.Arguments[0].Expression, storage.size!.Value)
                    || offset?.ArgumentList?.Arguments.Count != 1 || !Number(offset.ArgumentList.Arguments[0].Expression, 0))
                    StorageFailure(storage, "The native bitfield byte buffer changed its extent, type or offset.");
                foreach (CSharpBitfieldPlan bitfield in storage.bitfields)
                {
                    PropertyDeclarationSyntax[] properties = record.Members.OfType<PropertyDeclarationSyntax>().Where(property => property.Identifier.ValueText == bitfield.name.TrimStart('@')).ToArray();
                    MemberDeclarationSyntax expected = SyntaxFactory.ParseMemberDeclaration(BitfieldEmitter.Emit(bitfield))!;
                    if (properties.Length != 1 || !SyntaxFactory.AreEquivalent(properties[0], expected))
                        StorageFailure(storage, "A native bitfield accessor changed its recorded bit shape or integral conversion.");
                }
                fields = fields.Where(field => !ReferenceEquals(field, buffers[0])).ToArray();
            }
            if (fields.Length != storage.fields.Length) StorageFailure(storage, "The native storage field set changed.");
            foreach (CSharpFieldPlan field in storage.fields)
            {
                FieldDeclarationSyntax[] declarations = fields.Where(candidate => candidate.Declaration.Variables.Any(variable => variable.Identifier.ValueText == field.name.TrimStart('@'))).ToArray();
                if (declarations.Length != 1 || declarations[0].Declaration.Variables.Count != 1 || !Equivalent(declarations[0].Declaration.Type, Expected(field.type)))
                    StorageFailure(storage, "A native field carrier changed or became ambiguous.");
                AttributeSyntax? offset = Attribute(declarations[0], "global::System.Runtime.InteropServices.FieldOffset");
                if (offset?.ArgumentList?.Arguments.Count != 1 || !Number(offset.ArgumentList.Arguments[0].Expression, field.offset)) StorageFailure(storage, "A native field offset changed.");
            }
        }
        if (plan.importMode == BindingImportMode.FunctionTable)
        {
            if (plan.staticExportTable is null && hasImportAttribute)
                throw new InvalidOperationException("A function-table candidate contains undeclared runtime imports.");
            FieldDeclarationSyntax[] fields = contexts.SelectMany(type => type.Members.OfType<FieldDeclarationSyntax>()).ToArray();
            AssignmentExpressionSyntax[] assignments = contexts.SelectMany(type => type.Members.OfType<ConstructorDeclarationSyntax>())
                .SelectMany(constructor => constructor.DescendantNodes().OfType<AssignmentExpressionSyntax>()).ToArray();
            ILookup<string, FieldDeclarationSyntax> functionFields = fields.SelectMany(field => field.Declaration.Variables
                .Select(variable => (Name: variable.Identifier.ValueText, Field: field))).ToLookup(item => item.Name, item => item.Field, StringComparer.Ordinal);
            ILookup<string, AssignmentExpressionSyntax> functionInstalls = assignments.Where(assignment => assignment.Left is IdentifierNameSyntax)
                .ToLookup(assignment => ((IdentifierNameSyntax)assignment.Left).Identifier.ValueText, StringComparer.Ordinal);
            for (int index = 0; index < plan.methods.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CSharpMethodPlan method = plan.methods[index];
                CSharpPhysicalImportPlan? physical = PhysicalImportPlanner.Find(plan, index);
                string fieldName = physical?.fieldName ?? "m_function" + index;
                FieldDeclarationSyntax[] declarations = functionFields[fieldName].ToArray();
                AssignmentExpressionSyntax[] installs = functionInstalls[fieldName].ToArray();
                if (declarations.Length != 1 || declarations[0].Declaration.Variables.Count != 1 || installs.Length != 1
                    || !declarations[0].Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ReadOnlyKeyword)) || declarations[0].Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
                    Fail(method, "The function table field or installation is missing or duplicated.");
                TypeSyntax expected = Expected(PhysicalImportEmitter.FunctionPointer(method, physical));
                if (!Equivalent(declarations[0].Declaration.Type, expected) || installs[0].Right is not CastExpressionSyntax cast || !Equivalent(cast.Type, expected)
                    || cast.Expression is not ElementAccessExpressionSyntax access || access.ArgumentList.Arguments.Count != 1 || Literal(access.ArgumentList.Arguments[0].Expression) != method.exportName)
                    Fail(method, "The function table's actual carrier, calling convention or export mapping changed.");
            }
            if (fields.Count(field => field.Declaration.Type is FunctionPointerTypeSyntax) != plan.methods.Length)
                throw new InvalidOperationException("The emitted context contains undeclared function table fields.");
            MethodDeclarationSyntax[] adapterImports = allImports;
            if (plan.staticExportTable is { } table)
            {
                (string Name, string Export, string Result, string[] Parameters)[] expectedImports =
                [
                    ("GetExport", table.getExportSymbol, "nuint", ["uint"]), ("GetRevision", table.getRevisionSymbol, "uint", []),
                    ("GetCount", table.getCountSymbol, "uint", []), ("GetPointerSize", table.getPointerSizeSymbol, "uint", []),
                    ("GetAbiIdentity", table.getAbiIdentitySymbol, "byte*", [])
                ];
                if (adapterImports.Length != expectedImports.Length) throw new InvalidOperationException("The emitted static native adapter imports are incomplete or contain undeclared entry points.");
                foreach (var expectedImport in expectedImports)
                {
                    MethodDeclarationSyntax[] actual = importDeclarations[(context + ".StaticNativeAdapter", expectedImport.Name)].ToArray();
                    if (actual.Length != 1) throw new InvalidOperationException("A static native adapter import is missing or duplicated.");
                    MethodDeclarationSyntax import = actual[0];
                    AttributeSyntax[] attributes = import.AttributeLists.SelectMany(list => list.Attributes)
                        .Where(attribute => Compact(attribute.Name) == "global::System.Runtime.InteropServices.DllImport").ToArray();
                    if (import.Body is not null || import.ExpressionBody is not null || !import.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword))
                        || !import.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ExternKeyword))
                        || !Equivalent(import.ReturnType, Expected(expectedImport.Result))
                        || import.ParameterList.Parameters.Count != expectedImport.Parameters.Length
                        || import.ParameterList.Parameters.Select((
                            parameter,
                            index
                        ) => parameter.Type is null || parameter.Modifiers.Count != 0
                            || !Equivalent(parameter.Type, Expected(expectedImport.Parameters[index]))).Any(changed => changed)
                        || attributes.Length != 1 || attributes[0].ArgumentList is not AttributeArgumentListSyntax arguments
                        || arguments.Arguments.Count(argument => argument.NameEquals is null && argument.NameColon is null) != 1
                        || Literal(arguments.Arguments.First(argument => argument.NameEquals is null && argument.NameColon is null).Expression) != libraryName
                        || Named(arguments, "EntryPoint") is not ExpressionSyntax entry || Literal(entry) != expectedImport.Export
                        || Named(arguments, "CallingConvention") is not ExpressionSyntax convention || Compact(convention) != "global::System.Runtime.InteropServices.CallingConvention.Cdecl"
                        || Named(arguments, "ExactSpelling") is not LiteralExpressionSyntax exact || !exact.IsKind(SyntaxKind.TrueLiteralExpression))
                        throw new InvalidOperationException("The static native adapter library, symbol, calling convention or ABI carrier changed.");
                }
            }
            else if (adapterImports.Length != 0) throw new InvalidOperationException("The emitted function-table artifacts contain undeclared native imports.");
        }
        else
        {
            MethodDeclarationSyntax[] imports = allImports;
            if (imports.Length != plan.methods.Length) throw new InvalidOperationException("The emitted native import set is incomplete or contains undeclared imports.");
            for (int index = 0; index < plan.methods.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CSharpMethodPlan method = plan.methods[index];
                CSharpPhysicalImportPlan? physical = PhysicalImportPlanner.Find(plan, index);
                MethodDeclarationSyntax[] declarations = importDeclarations[(context + ".NativeImports", physical?.declarationName ?? "Function" + index)].ToArray();
                if (declarations.Length != 1) Fail(method, "The native import declaration is missing or duplicated.");
                MethodDeclarationSyntax declaration = declarations[0];
                if (declaration.Body is not null || declaration.ExpressionBody is not null || !declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword))
                    || !declaration.Modifiers.Any(modifier => modifier.IsKind(plan.importMode == BindingImportMode.DllImport ? SyntaxKind.ExternKeyword : SyntaxKind.PartialKeyword)))
                    Fail(method, "The actual import is no longer a bodyless static native declaration.");
                string attributeName = plan.importMode == BindingImportMode.DllImport ? "DllImport" : "LibraryImport";
                AttributeSyntax[] attributes = declaration.AttributeLists.SelectMany(list => list.Attributes).Where(attribute => Compact(attribute.Name) == "global::System.Runtime.InteropServices." + attributeName).ToArray();
                if (attributes.Length != 1 || attributes[0].ArgumentList is not AttributeArgumentListSyntax arguments
                    || arguments.Arguments.Count(argument => argument.NameEquals is null && argument.NameColon is null) != 1
                    || !ImportLibraryNameContractVerifier.Matches(plan, arguments.Arguments.First(argument => argument.NameEquals is null && argument.NameColon is null).Expression, libraryName)
                    || Named(arguments, "EntryPoint") is not ExpressionSyntax entry || Literal(entry) != method.exportName)
                    Fail(method, "The actual native library or export identity changed.");
                if (!Equivalent(declaration.ReturnType, Expected(physical?.returnType ?? method.returnType)) || declaration.ParameterList.Parameters.Count != method.parameterTypes.Length
                    || declaration.ParameterList.Parameters.Select((
                        parameter,
                        position
                    ) => parameter.Type is not null && parameter.Modifiers.Count == 0 && Equivalent(parameter.Type, Expected((physical?.parameterTypes ?? method.parameterTypes)[position]))).Any(match => !match))
                    Fail(method, "The actual import parameter or result carrier changed.");
                if (plan.importMode == BindingImportMode.DllImport)
                {
                    ExpressionSyntax? convention = Named(attributes[0].ArgumentList!, "CallingConvention");
                    ExpressionSyntax? exact = Named(attributes[0].ArgumentList!, "ExactSpelling");
                    if (convention is null || Compact(convention) != "global::System.Runtime.InteropServices.CallingConvention." + method.callingConvention || exact is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.TrueLiteralExpression))
                        Fail(method, "The actual DllImport convention or exact export spelling changed.");
                }
                else
                {
                    AttributeSyntax[] conventions = declaration.AttributeLists.SelectMany(list => list.Attributes).Where(attribute => IsConventionName(attribute.Name)).ToArray();
                    TypeOfExpressionSyntax[] types = conventions.SelectMany(attribute => attribute.DescendantNodes().OfType<TypeOfExpressionSyntax>()).ToArray();
                    if (plan.libraryImportUsesPlatformDefault ? conventions.Length != 0
                        : conventions.Length != 1 || Compact(conventions[0].Name) != "global::System.Runtime.InteropServices.UnmanagedCallConv"
                            || types.Length != 1 || Compact(types[0].Type) != "global::System.Runtime.CompilerServices.CallConv" + method.callingConvention)
                        Fail(method, "The actual LibraryImport unmanaged convention changed.");
                }
            }
        }
        TypeSyntax Expected(string syntax)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expectedTypes.TryGetValue(syntax, out TypeSyntax? type))
            {
                type = SyntaxFactory.ParseTypeName(syntax, options: new CSharpParseOptions(LanguageVersion.CSharp14));
                expectedTypes.Add(syntax, type);
            }
            return type;
        }
    }

    private static string SharedMemberKey(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => "method:" + method.Identifier.ValueText + "`" + (method.TypeParameterList?.Parameters.Count ?? 0) + "(" + string.Join(",", method.ParameterList.Parameters.Select(parameter => string.Join(" ", parameter.Modifiers.Select(modifier => modifier.ValueText)) + ":" + parameter.Type!.WithoutTrivia().NormalizeWhitespace().ToFullString())) + ")",
        PropertyDeclarationSyntax property => "property:" + property.Identifier.ValueText,
        FieldDeclarationSyntax field => "field:" + string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
        BaseTypeDeclarationSyntax type => "type:" + type.Identifier.ValueText,
        DelegateDeclarationSyntax type => "delegate:" + type.Identifier.ValueText,
        _ => "extension:" + member.Kind() + ":" + member.WithoutTrivia().NormalizeWhitespace().ToFullString()
    };

    private static bool Equivalent(
        TypeSyntax actual,
        TypeSyntax expected
    ) => SyntaxFactory.AreEquivalent(actual, expected);
    private sealed class InspectionSink : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        )
            => throw new InvalidOperationException("Inspecting frozen emission contracts must not write artifacts.");
    }
    private static string? Literal(ExpressionSyntax expression) => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) ? literal.Token.ValueText : null;
    private static ExpressionSyntax? Named(
        AttributeArgumentListSyntax arguments,
        string name
    ) {
        AttributeArgumentSyntax[] matches = arguments.Arguments.Where(argument => argument.NameEquals?.Name.Identifier.ValueText == name).ToArray();
        return matches.Length == 1 ? matches[0].Expression : null;
    }
    private static bool IsImportName(NameSyntax name) => name.ToString().EndsWith("DllImport", StringComparison.Ordinal) || name.ToString().EndsWith("LibraryImport", StringComparison.Ordinal)
        || name.ToString().EndsWith("DllImportAttribute", StringComparison.Ordinal) || name.ToString().EndsWith("LibraryImportAttribute", StringComparison.Ordinal);
    private static bool IsConventionName(NameSyntax name) => name.ToString().EndsWith("UnmanagedCallConv", StringComparison.Ordinal)
        || name.ToString().EndsWith("UnmanagedCallConvAttribute", StringComparison.Ordinal);
    private static string FullName(ClassDeclarationSyntax type)
        => FullName(type, type.Identifier.ValueText);
    private static string FullName(
        SyntaxNode type,
        string name
    )
        => string.Join(".", type.Ancestors().Reverse().Select(node => node switch { BaseNamespaceDeclarationSyntax ns => string.Join(".", ns.Name.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText)), ClassDeclarationSyntax owner => owner.Identifier.ValueText, _ => null }).Where(name => name is not null).Append(name));
    private static string Compact(SyntaxNode node) => string.Concat(node.DescendantTokens().Select(token => token.Text));
    private static bool Number(
        ExpressionSyntax? expression,
        int expected
    ) => expression is LiteralExpressionSyntax literal && literal.Token.Value is int actual && actual == expected;
    private static AttributeSyntax? Attribute(
        MemberDeclarationSyntax member,
        string name
    ) {
        AttributeSyntax[] attributes = member.AttributeLists.SelectMany(list => list.Attributes).Where(attribute => Compact(attribute.Name) == name).ToArray();
        return attributes.Length == 1 ? attributes[0] : null;
    }
    private static void StorageFailure(
        CSharpStoragePlan storage,
        string reason
    ) => throw new InvalidOperationException($"Emitted native storage '{storage.name}': {reason}");
    private static void Fail(
        CSharpMethodPlan method,
        string reason
    ) => throw new InvalidOperationException($"Emitted native import '{method.exportName}': {reason}");
}
