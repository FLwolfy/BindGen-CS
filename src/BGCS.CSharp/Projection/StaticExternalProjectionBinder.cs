using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using BGCS.Analysis.Layout;
using BGCS.Core.Common;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Projection;

/// <summary>Binds explicit scalar field copies from frozen source and metadata without compiling or executing consumer code.</summary>
public static class StaticExternalProjectionBinder
{
    /// <summary>Resolves complete unmanaged consumer field shapes while preserving the original raw native ABI.</summary>
    /// <param name="model">The managed presentation and unchanged native carriers.</param>
    /// <param name="inputs">Frozen explicit source, metadata and preprocessor inputs.</param>
    /// <param name="cancellationToken">Cancels static binding and metadata inspection.</param>
    /// <returns>A presentation with declared scalar shapes, without CLR size, offset or calling-ABI assertions.</returns>
    public static ManagedApiModel Bind(
        ManagedApiModel model,
        ManagedProjectionInputs? inputs,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();
        if (model.externalTypeProjections.IsDefault) throw new ArgumentException("External type projections must be initialized.");
        if (model.externalTypeProjections.IsEmpty) return model;
        ManagedConstantCompilerInputs prepared = ManagedConstantCompilerInputs.Create(inputs, ConstantPresentationPlanner.frameworkReferences, cancellationToken);
        var bound = ImmutableArray.CreateBuilder<ExternalTypeProjection>();
        foreach (ExternalTypeProjection projection in model.externalTypeProjections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(projection);
            string typeName = ExternalCarrierPlanner.NormalizeType(projection.managedType);
            SyntaxTree tree = CSharpSyntaxTree.ParseText("public static unsafe class __BGCSStaticProjectionBinding { public static " + typeName + " Value = default; }",
                prepared.parseOptions, "bgcs-internal:/external-field-binding.g.cs", cancellationToken: cancellationToken);
            CSharpCompilation compilation = CSharpCompilation.Create("BGCS.ExternalFieldBinding", prepared.sources.Add(tree), prepared.references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            prepared.VerifyAssemblyClosure(compilation, cancellationToken);
            Diagnostic[] failures = compilation.GetDeclarationDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Take(8).ToArray();
            if (failures.Length != 0) throw new ArgumentException("External field binding has unresolved or invalid declarations: " + string.Join("; ", failures.Select(failure => failure.ToString())));
            VariableDeclaratorSyntax variable = tree.GetRoot(cancellationToken).DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
            ITypeSymbol? resolved = (compilation.GetSemanticModel(tree).GetDeclaredSymbol(variable, cancellationToken) as IFieldSymbol)?.Type;
            if (resolved is not INamedTypeSymbol type || type.TypeKind != TypeKind.Struct || type.IsRefLikeType || type.IsRecord
                || !type.IsUnmanagedType || type.IsUnboundGenericType || type.TypeArguments.Any(argument => argument.TypeKind is TypeKind.Error or TypeKind.TypeParameter))
                throw new NotSupportedException("External copies require a resolved closed unmanaged, non-ref-like ordinary struct.");
            RejectExplicitLayout(type, inputs, cancellationToken);
            var fields = ImmutableArray.CreateBuilder<ManagedStaticFieldShape>();
            foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (field.DeclaredAccessibility != Accessibility.Public || field.IsReadOnly || field.IsConst || field.IsFixedSizeBuffer || field.RefKind != RefKind.None
                    || field.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.FieldOffsetAttribute"))
                    throw new NotSupportedException("External copies require every instance field to be public, writable, scalar and free of explicit offsets: " + field.Name);
                (ManagedScalarKind kind, int size, string identity) = Scalar(field.Type, model.interop.pointerSize);
                fields.Add(new(field.Name, identity, kind, size, true, true));
            }
            ExternalTypeProjection candidate = projection with
            {
                managedShape = new(projection.managedType, fields.ToImmutable(), new(EvidenceLevel.Declared, "bgcs:managed-static-field-binding:v1"))
            };
            new ExternalTypeContractValidator().ValidateProjection(model.interop, candidate);
            bound.Add(candidate);
        }
        return model with { externalTypeProjections = bound.ToImmutable() };
    }

    private static (ManagedScalarKind Kind, int Size, string Identity) Scalar(
        ITypeSymbol type,
        int pointerSize
    ) {
        if (type is IPointerTypeSymbol pointer && pointer.PointedAtType.TypeKind != TypeKind.FunctionPointer && pointerSize is 4 or 8)
            return (ManagedScalarKind.NativeAddress, pointerSize, pointer.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return type.SpecialType switch
        {
            SpecialType.System_SByte => (ManagedScalarKind.SignedInteger, 1, "System.SByte"),
            SpecialType.System_Byte => (ManagedScalarKind.UnsignedInteger, 1, "System.Byte"),
            SpecialType.System_Int16 => (ManagedScalarKind.SignedInteger, 2, "System.Int16"),
            SpecialType.System_UInt16 => (ManagedScalarKind.UnsignedInteger, 2, "System.UInt16"),
            SpecialType.System_Int32 => (ManagedScalarKind.SignedInteger, 4, "System.Int32"),
            SpecialType.System_UInt32 => (ManagedScalarKind.UnsignedInteger, 4, "System.UInt32"),
            SpecialType.System_Int64 => (ManagedScalarKind.SignedInteger, 8, "System.Int64"),
            SpecialType.System_UInt64 => (ManagedScalarKind.UnsignedInteger, 8, "System.UInt64"),
            SpecialType.System_Single => (ManagedScalarKind.BinaryFloat, 4, "System.Single"),
            SpecialType.System_Double => (ManagedScalarKind.BinaryFloat, 8, "System.Double"),
            SpecialType.System_IntPtr when pointerSize is 4 or 8 => (ManagedScalarKind.NativeAddress, pointerSize, "System.IntPtr"),
            SpecialType.System_UIntPtr when pointerSize is 4 or 8 => (ManagedScalarKind.NativeAddress, pointerSize, "System.UIntPtr"),
            _ => throw new NotSupportedException("The external field has no supported exact scalar kind or width: " + type.ToDisplayString())
        };
    }

    private static void RejectExplicitLayout(
        INamedTypeSymbol type,
        ManagedProjectionInputs? inputs,
        CancellationToken cancellationToken
    ) {
        foreach (AttributeData attribute in type.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute"
                && attribute.ConstructorArguments.Length != 0 && attribute.ConstructorArguments[0].Value is int layout && layout == 2)
                throw new NotSupportedException("Explicit-layout consumer structs require an explicit conversion adapter.");
        if (!type.DeclaringSyntaxReferences.IsEmpty) return;
        INamedTypeSymbol definition = type.OriginalDefinition;
        string metadataName = definition.MetadataName;
        for (INamedTypeSymbol? parent = definition.ContainingType; parent is not null; parent = parent.ContainingType)
            metadataName = parent.MetadataName + "+" + metadataName;
        if (!definition.ContainingNamespace.IsGlobalNamespace) metadataName = definition.ContainingNamespace.ToDisplayString() + "." + metadataName;
        ManagedAssemblyInput image = inputs?.assemblies.SingleOrDefault(input => input.assemblyIdentity == definition.ContainingAssembly.Identity.ToString())
            ?? throw new NotSupportedException("The external consumer type has no captured metadata image for independent layout-policy inspection.");
        using PEReader pe = new(image.image);
        MetadataReader reader = pe.GetMetadataReader();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDefinition candidate = reader.GetTypeDefinition(handle);
            if (Name(handle) != metadataName) continue;
            if ((candidate.Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout
                || candidate.GetFields().Any(field => reader.GetFieldDefinition(field).GetOffset() >= 0))
                throw new NotSupportedException("Explicit layout or field offsets in captured metadata require an explicit conversion adapter.");
            return;
        }
        throw new NotSupportedException("The exact consumer type definition is missing from its captured metadata image.");

        string Name(TypeDefinitionHandle handle)
        {
            TypeDefinition item = reader.GetTypeDefinition(handle);
            string name = reader.GetString(item.Name);
            TypeDefinitionHandle parent = item.GetDeclaringType();
            if (!parent.IsNil) return Name(parent) + "+" + name;
            string scope = reader.GetString(item.Namespace);
            return string.IsNullOrEmpty(scope) ? name : scope + "." + name;
        }
    }
}
