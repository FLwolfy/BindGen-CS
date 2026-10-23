using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.NativeBridge.Planning;

/// <summary>Creates concrete template parse declarations and selects their detached native callables.</summary>
public sealed class TemplateInstantiationPlanner
{
    /// <summary>Validates explicit template requests and emits a fixed prototype for each function request.</summary>
    /// <param name="request">The immutable concrete template requests.</param>
    /// <returns>Declarations passed through the same native parser and later bridge compiler.</returns>
    public TemplateInstantiationPlan Plan(TemplateInstantiationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.types.IsDefault || request.functions.IsDefault)
            throw new ArgumentException("Template request collections must be initialized immutable arrays.", nameof(request));
        HashSet<string> identities = new(StringComparer.Ordinal);
        HashSet<string> types = new(StringComparer.Ordinal);
        ImmutableDictionary<string, string>.Builder wrappers = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        StringBuilder declarations = new();
        foreach (TypeTemplateInstantiation type in request.types)
        {
            ArgumentNullException.ThrowIfNull(type);
            Identity(type.id);
            NativeSyntax(type.nativeType, true);
            if (!type.nativeType.Contains('<') || !types.Add(type.nativeType))
                throw new ArgumentException("A class-template request must name one unique concrete specialization.", nameof(request));
            declarations.Append("template class ").Append(type.nativeType).Append(";\n");
        }
        if (request.functions.Length != 0) declarations.Append("namespace BGCSGeneratedTemplates {\n");
        foreach (FunctionTemplateInstantiation function in request.functions)
        {
            ArgumentNullException.ThrowIfNull(function);
            Identity(function.id);
            NativeSyntax(function.nativeCallable, true);
            NativeSyntax(function.returnType, false);
            if (function.parameterTypes.IsDefault) throw new ArgumentException("Function-template parameter collections must be initialized immutable arrays.", nameof(request));
            if (!function.nativeCallable.Contains('<')) throw new ArgumentException("A function-template request needs explicit concrete template arguments.", nameof(request));
            foreach (string type in function.parameterTypes) NativeSyntax(type, false);
            string name = "function_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(function.id)))[..16];
            wrappers.Add(function.id, "BGCSGeneratedTemplates::" + name);
            declarations.Append("inline ").Append(function.returnType).Append(' ').Append(name).Append('(');
            declarations.Append(string.Join(", ", function.parameterTypes.Select((
                type,
                index
            ) => type + " bgcs_arg" + index.ToString(CultureInfo.InvariantCulture))));
            declarations.Append(") { using bgcs_signature = ").Append(function.returnType).Append(" (*)(")
                .Append(string.Join(", ", function.parameterTypes)).Append("); auto bgcs_target = static_cast<bgcs_signature>(&")
                .Append(function.nativeCallable.StartsWith("::", StringComparison.Ordinal) ? function.nativeCallable : "::" + function.nativeCallable).Append("); ");
            if (function.returnType.Trim() != "void") declarations.Append("return ");
            declarations.Append("bgcs_target(");
            declarations.Append(string.Join(", ", function.parameterTypes.Select((
                type,
                index
            ) => "std::forward<" + type + ">(bgcs_arg" + index.ToString(CultureInfo.InvariantCulture) + ")")));
            declarations.Append("); }\n");
        }
        if (request.functions.Length != 0) declarations.Append("}\n");
        return new(declarations.ToString(), wrappers.ToImmutable(), request.types.Select(type => type.nativeType).ToImmutableArray())
        {
            preamble = request.functions.IsEmpty ? string.Empty : "#include <utility>\n"
        };

        void Identity(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (!Regex.IsMatch(id, "^[A-Za-z][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant) || !identities.Add(id))
                throw new ArgumentException("Template request identities must be unique portable names.", nameof(request));
        }
    }

    /// <summary>Selects concrete detached callables while leaving unbound primary templates unexported.</summary>
    /// <param name="module">The detached parse result containing explicit instantiations.</param>
    /// <param name="plan">The exact native declarations requested before parsing.</param>
    /// <returns>Concrete public and module-level callable identities, including generated function wrappers.</returns>
    public ImmutableArray<StableId> SelectCallables(
        NativeModule module,
        TemplateInstantiationPlan plan
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(plan);
        foreach (string wrapper in plan.functionWrappers.Values)
            if (!module.declarations.Any(declaration => declaration.kind == "Function" && declaration.qualifiedName == wrapper))
                throw new InvalidOperationException($"The native parser did not produce requested function-template wrapper '{wrapper}'.");
        foreach (string concrete in plan.concreteTypes)
            if (!module.types.Any(type => type.category == NativeTypeCategory.Record && Normalize(type.spelling) == Normalize(concrete) && type.declaration is StableId declaration
                && module.GetDeclaration(declaration).templateParameters.Length == 0))
                throw new InvalidOperationException($"The native parser did not produce concrete class-template specialization '{concrete}'.");
        return module.declarations.Where(declaration => declaration.kind == "Function" && !declaration.isSystemDeclaration
            && !declaration.isDeleted && declaration.visibility is not ("Private" or "Protected") && declaration.templateParameters.Length == 0
            && declaration.templateKind != "FunctionTemplate" && IsConcreteOwner(declaration)).Select(declaration => declaration.id).ToImmutableArray();

        bool IsConcreteOwner(NativeDeclaration declaration)
        {
            NativeDeclaration? current = declaration.parent is StableId parent ? module.GetDeclaration(parent) : null;
            while (current is not null)
            {
                if (current.templateParameters.Length != 0 || current.visibility is "Private" or "Protected") return false;
                current = current.parent is StableId ancestor ? module.GetDeclaration(ancestor) : null;
            }
            return true;
        }

        static string Normalize(string spelling) => string.Concat(spelling.Where(character => !char.IsWhiteSpace(character)));
    }

    private static void NativeSyntax(
        string spelling,
        bool name
    ) {
        if (string.IsNullOrWhiteSpace(spelling) || spelling.Length > 4096 || spelling.Any(character => character is '\r' or '\n' or '\0' or ';' or '{' or '}' or '#' or '"' or '\'' or '\\' or '/'))
            throw new ArgumentException("A template type or callable spelling must be one native declaration component.");
        if (!Regex.IsMatch(spelling, "^[A-Za-z0-9_:<>, *&\\[\\]()+.-]+$", RegexOptions.CultureInvariant))
            throw new ArgumentException("A template spelling contains unsupported native declaration syntax.");
        Stack<char> nesting = [];
        int templateDepth = 0;
        foreach (char character in spelling)
        {
            if (name && templateDepth == 0 && character is '*' or '&' or '[' or ']')
                throw new ArgumentException("A template callable or specialization must name a qualified declaration, not an outer pointer, reference or array.");
            if (character is '<' or '[' or '(')
            {
                nesting.Push(character);
                if (character == '<') templateDepth++;
            }
            else if (character is '>' or ']' or ')')
            {
                char expected = character == '>' ? '<' : character == ']' ? '[' : '(';
                if (nesting.Count == 0 || nesting.Pop() != expected) throw new ArgumentException("A template spelling has unbalanced native delimiters.");
                if (character == '>') templateDepth--;
            }
        }
        if (nesting.Count != 0)
            throw new ArgumentException("A template callable or specialization must have a balanced qualified native name.");
    }
}
