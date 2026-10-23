using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;

namespace BGCS.CSharp.Projection;

internal static class ManagedNameResolver
{
    internal static Func<InteropType, string> CreateTypeSourceResolver(InteropModule module)
    {
        HashSet<StableId> anonymous = module.types.Where(IsAnonymous).Select(type => type.id).ToHashSet();
        Dictionary<StableId, string> aliases = [];
        foreach (InteropType candidate in module.types.Where(type => type.category == NativeTypeCategory.Alias
                && !string.IsNullOrWhiteSpace(type.declarationName))
            .OrderBy(type => type.source?.file, StringComparer.Ordinal).ThenBy(type => type.source?.line)
            .ThenBy(type => type.source?.column).ThenBy(type => type.id.value, StringComparer.Ordinal))
        {
            InteropType resolved = candidate;
            HashSet<StableId> visited = [];
            while (resolved.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(resolved.id) || resolved.elementType is not { } element) break;
                resolved = module.GetType(element);
            }
            if (anonymous.Contains(resolved.id)) aliases.TryAdd(resolved.id, candidate.sourceQualifiedName ?? candidate.declarationName!);
        }
        return type => TypeSource(type, aliases);
    }

    private static bool IsAnonymous(InteropType type) => type.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration
        && type.sourceDeclaration is not null && type.declarationName is "";

    private static string TypeSource(
        InteropType type,
        IReadOnlyDictionary<StableId, string> aliases
    ) {
        string ordinary = new[] { type.sourceQualifiedName, type.declarationName, type.nativeName }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        if (!IsAnonymous(type)) return ordinary;

        // A named typedef is the public source identity of an otherwise anonymous C declaration.
        if (aliases.TryGetValue(type.id, out string? alias)) return alias;

        // Anonymous source spellings can contain absolute parser paths. Neither they nor
        // an empty terminal component such as Parent:: are a managed declaration name.
        string identity = type.source is { } source && !string.IsNullOrWhiteSpace(source.file)
            ? source.file.Replace('\\', '/') + "\n" + source.line.ToString(CultureInfo.InvariantCulture)
                + "\n" + source.column.ToString(CultureInfo.InvariantCulture) + "\n" + type.recordKind
            : type.sourceDeclaration!.Value.value;
        string suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        string owner = (type.sourceQualifiedName ?? string.Empty).TrimEnd(':');
        string role = type.category == NativeTypeCategory.Enumeration ? "Enum" : type.recordKind == "Union" ? "Union" : "Struct";
        return (owner.Length == 0 ? string.Empty : owner + "_") + "Anonymous" + role + "_" + suffix;

    }

    internal static string Callable(
        InteropModule module,
        InteropFunction function,
        ManagedProjectionRequest request,
        FlatIdentifierWordIndex? wordIndex = null
    ) {
        if (request.names.TryGetValue(function.exportName, out string? exact)) return Required(exact);
        if (function.inputAdapterOf is { } primaryId)
        {
            InteropFunction primary = module.functions.Single(candidate => candidate.id == primaryId);
            return Callable(module, primary, request, wordIndex) + "WithSequence";
        }
        if (function.sourceSignature is string signature && request.names.TryGetValue(signature, out string? overload)) return Required(overload);
        if (function.sourceQualifiedName is string qualified && request.names.TryGetValue(qualified, out string? original)) return Required(original);
        string? source = function.sourceName;
        string? role = function.sourceRole;
        if (role is "Constructor" or "Construct" or "DefaultConstruct" || role == "StandardValue" && source == "Construct")
            return Apply("Create" + Owner(), "Create" + Owner());
        if (role is "Destructor" or "Destroy") return Apply("Destroy" + Owner(), "Destroy" + Owner());
        if (role == "BaseCast") return Apply("As" + Identifier(source ?? "Base", function.id.value), "As" + Identifier(source ?? "Base", function.id.value));
        if (role == "CheckedDerivedCast") return Apply("TryAs" + Identifier(source ?? "Derived", function.id.value), "TryAs" + Identifier(source ?? "Derived", function.id.value));
        if (source is null && function.sourceQualifiedName is string native) source = Unqualified(native);
        if (source?.StartsWith("operator", StringComparison.Ordinal) == true) return Apply(Operator(source[8..].Trim()), Operator(source[8..].Trim()));
        return Apply(source ?? function.exportName, Identifier(source ?? function.exportName, function.id.value));

        string Apply(
            string input,
            string ordinary
        ) => ManagedNamingResolver.Apply(input, ordinary, request.naming.functions, request.naming, wordIndex: wordIndex);

        string Owner()
        {
            InteropType? owner = module.types.FirstOrDefault(type => function.sourceOwnerType is not null
                && type.sourceNativeType == function.sourceOwnerType && type.category == NativeTypeCategory.Record);
            owner ??= module.types.FirstOrDefault(type => function.sourceOwnerType is not null && type.sourceNativeType == function.sourceOwnerType);
            owner ??= module.types.FirstOrDefault(type => function.sourceGeneratedOwnerType is not null
                && type.sourceGeneratedType == function.sourceGeneratedOwnerType && type.category == NativeTypeCategory.Record);
            if (owner?.sourceQualifiedName is string ownerName) return Identifier(Unqualified(ownerName), function.id.value);
            if (function.sourceQualifiedName is string callable)
            {
                string name = Unqualified(callable);
                if (role is "Constructor" or "Construct") return Identifier(name, function.id.value);
                int split = callable.Length - name.Length - 2;
                if (split > 0) return Identifier(Unqualified(callable[..split]), function.id.value);
            }
            throw new InvalidOperationException("A generated construction or destruction name requires a detached native owner identity.");
        }
    }

    internal static string Identifier(
        string source,
        string identity
    ) {
        StringBuilder result = new();
        bool capitalize = true;
        foreach (char character in source)
        {
            if (!char.IsLetterOrDigit(character)) { capitalize = true; continue; }
            if (result.Length == 0 && char.IsDigit(character)) result.Append('_');
            result.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }
        return result.Length == 0 ? "Anonymous_" + identity.Replace(':', '_') : result.ToString();
    }

    private static string Required(string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    private static string Unqualified(string name)
    {
        int depth = 0;
        for (int index = name.Length - 1; index > 0; index--)
        {
            if (name[index] == '>') depth++;
            else if (name[index] == '<') depth--;
            else if (depth == 0 && name[index] == ':' && name[index - 1] == ':') return name[(index + 1)..];
        }
        return name;
    }

    private static string Operator(string operation) => operation switch
    {
        "+" => "Add",
        "-" => "Subtract",
        "*" => "Multiply",
        "/" => "Divide",
        "%" => "Remainder",
        "++" => "Increment",
        "--" => "Decrement",
        "=" => "Assign",
        "+=" => "AddAssign",
        "-=" => "SubtractAssign",
        "*=" => "MultiplyAssign",
        "/=" => "DivideAssign",
        "%=" => "RemainderAssign",
        "[]" => "GetAt",
        "()" => "InvokeNative",
        "==" => "EqualsNative",
        "!=" => "NotEquals",
        "<" => "LessThan",
        ">" => "GreaterThan",
        "<=" => "LessThanOrEqual",
        ">=" => "GreaterThanOrEqual",
        "<=>" => "Compare",
        "!" => "LogicalNot",
        "~" => "BitwiseNot",
        "&" => "BitwiseAnd",
        "|" => "BitwiseOr",
        "^" => "BitwiseXor",
        "&&" => "LogicalAnd",
        "||" => "LogicalOr",
        "<<" => "ShiftLeft",
        ">>" => "ShiftRight",
        "&=" => "BitwiseAndAssign",
        "|=" => "BitwiseOrAssign",
        "^=" => "BitwiseXorAssign",
        "<<=" => "ShiftLeftAssign",
        ">>=" => "ShiftRightAssign",
        "->" => "GetPointer",
        "->*" => "AccessMemberPointer",
        "," => "Sequence",
        "new" => "Allocate",
        "new[]" => "AllocateArray",
        "delete" => "Deallocate",
        "delete[]" => "DeallocateArray",
        _ when operation.Length > 0 && (char.IsLetter(operation[0]) || operation.StartsWith("::", StringComparison.Ordinal)) => "To" + Identifier(operation, operation),
        _ => throw new NotSupportedException("This native operator requires an explicit managed name mapping.")
    };
}
