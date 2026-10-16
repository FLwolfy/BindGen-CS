using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace BGCS.Tool.Validation;

// Reflection Type canonicalizes function pointers and loses legacy calling-
// convention header information. Read those signatures from the actual PE.
internal sealed class ApiSnapshotSignatureProvider(MetadataReader reader) : ISignatureTypeProvider<ApiSnapshotSignatureProvider.SignatureType, object?>
{
    internal readonly record struct SignatureType(
        string text,
        ImmutableArray<string> conventions = default
    );
    private sealed record SignatureContext(
        string[] types,
        string[] methods
    );

    internal string Field(int token)
    {
        FieldDefinition field = reader.GetFieldDefinition((FieldDefinitionHandle)MetadataTokens.EntityHandle(token));
        return field.DecodeSignature(this, Context(field.GetDeclaringType(), [])).text;
    }

    internal MethodSignature<SignatureType> Method(int token)
    {
        MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(token));
        return method.DecodeSignature(this, Context(method.GetDeclaringType(), method.GetGenericParameters().ToArray()));
    }

    internal string Property(int token)
    {
        PropertyDefinition property = reader.GetPropertyDefinition((PropertyDefinitionHandle)MetadataTokens.EntityHandle(token));
        return property.DecodeSignature(this, Context(property.GetDeclaringType(), [])).ReturnType.text;
    }

    private SignatureContext Context(
        TypeDefinitionHandle owner,
        GenericParameterHandle[] methods
    )
        => new(Names(reader.GetTypeDefinition(owner).GetGenericParameters().ToArray()), Names(methods));
    private string[] Names(GenericParameterHandle[] parameters) => parameters.Select(reader.GetGenericParameter)
        .OrderBy(parameter => parameter.Index).Select(parameter => reader.GetString(parameter.Name)).ToArray();

    public SignatureType GetArrayType(
        SignatureType elementType,
        ArrayShape shape
    )
        => new(elementType.text + "[" + new string(',', shape.Rank - 1) + "]");
    public SignatureType GetByReferenceType(SignatureType elementType) => new(elementType.text + "&");
    public SignatureType GetFunctionPointerType(MethodSignature<SignatureType> signature)
    {
        string? headerConvention = signature.Header.CallingConvention switch
        {
            SignatureCallingConvention.CDecl => "System.Runtime.CompilerServices.CallConvCdecl",
            SignatureCallingConvention.StdCall => "System.Runtime.CompilerServices.CallConvStdcall",
            SignatureCallingConvention.ThisCall => "System.Runtime.CompilerServices.CallConvThiscall",
            SignatureCallingConvention.FastCall => "System.Runtime.CompilerServices.CallConvFastcall",
            _ => null
        };
        ImmutableArray<string> modifiers = signature.ReturnType.conventions.IsDefault ? [] : signature.ReturnType.conventions;
        string[] conventions = (headerConvention is null ? modifiers : modifiers.Insert(0, headerConvention)).ToArray();
        bool unmanaged = headerConvention is not null || signature.Header.CallingConvention == SignatureCallingConvention.Unmanaged || conventions.Length > 0;
        string convention = unmanaged ? " unmanaged" : " managed";
        if (conventions.Length > 0) convention += "[" + string.Join(",", conventions) + "]";
        return new("delegate*" + convention + "<" + string.Join(",", signature.ParameterTypes.Select(type => type.text).Append(signature.ReturnType.text)) + ">");
    }
    public SignatureType GetGenericInstantiation(
        SignatureType genericType,
        ImmutableArray<SignatureType> typeArguments
    )
        => new(System.Text.RegularExpressions.Regex.Replace(genericType.text, @"`\d+", string.Empty)
            + "<" + string.Join(",", typeArguments.Select(type => type.text)) + ">");
    public SignatureType GetGenericMethodParameter(
        object? genericContext,
        int index
    ) => new(((SignatureContext)genericContext!).methods[index]);
    public SignatureType GetGenericTypeParameter(
        object? genericContext,
        int index
    ) => new(((SignatureContext)genericContext!).types[index]);
    public SignatureType GetModifiedType(
        SignatureType modifier,
        SignatureType unmodifiedType,
        bool isRequired
    )
        => modifier.text.StartsWith("System.Runtime.CompilerServices.CallConv", StringComparison.Ordinal)
            ? unmodifiedType with { conventions = (unmodifiedType.conventions.IsDefault ? [] : unmodifiedType.conventions).Add(modifier.text) }
            : unmodifiedType;
    public SignatureType GetPinnedType(SignatureType elementType) => elementType;
    public SignatureType GetPointerType(SignatureType elementType) => new(elementType.text + "*");
    public SignatureType GetPrimitiveType(PrimitiveTypeCode typeCode) => new(typeCode switch
    {
        PrimitiveTypeCode.Void => "System.Void",
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => throw new NotSupportedException("The public signature contains an unrecognized primitive metadata code.")
    });
    public SignatureType GetSZArrayType(SignatureType elementType) => new(elementType.text + "[]");
    public SignatureType GetTypeFromDefinition(
        MetadataReader metadata,
        TypeDefinitionHandle handle,
        byte rawTypeKind
    ) {
        TypeDefinition type = metadata.GetTypeDefinition(handle);
        TypeDefinitionHandle parent = type.GetDeclaringType();
        string name = metadata.GetString(type.Name);
        return new(parent.IsNil ? Qualified(metadata.GetString(type.Namespace), name)
            : GetTypeFromDefinition(metadata, parent, rawTypeKind).text + "+" + name);
    }
    public SignatureType GetTypeFromReference(
        MetadataReader metadata,
        TypeReferenceHandle handle,
        byte rawTypeKind
    ) {
        TypeReference type = metadata.GetTypeReference(handle);
        string name = metadata.GetString(type.Name);
        return new(type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(metadata, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind).text + "+" + name
            : Qualified(metadata.GetString(type.Namespace), name));
    }
    public SignatureType GetTypeFromSpecification(
        MetadataReader metadata,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind
    )
        => metadata.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    private static string Qualified(
        string prefix,
        string name
    ) => prefix.Length == 0 ? name : prefix + "." + name;
}
