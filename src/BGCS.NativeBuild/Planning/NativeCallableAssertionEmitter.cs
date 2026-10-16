using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;

namespace BGCS.NativeBuild.Planning;

internal sealed class NativeCallableAssertionEmitter(NativeModule module)
{
    private readonly HashSet<string> m_conventions = new(StringComparer.Ordinal);

    internal string RenderStorageType(
        StableId identity,
        string declarator
    ) => Render(identity, declarator);

    internal string Emit()
    {
        bool cpp = module.language == SourceLanguage.Cpp;
        StringBuilder checks = new();
        int index = 0;
        foreach (NativeDeclaration function in module.declarations.Where(declaration => declaration.kind == "Function" && declaration.role == "Function"
            && !declaration.isSystemDeclaration && !declaration.isDeleted && declaration.hasPrototype && declaration.templateParameters.IsEmpty
            && declaration.templateKind != "FunctionTemplate" && Accessible(declaration) && HasConcreteAncestors(declaration)))
        {
            NativeDeclaration? owner = function.parent is StableId parent ? module.GetDeclaration(parent) : null;
            string callableName = owner?.kind == "Class" ? owner.qualifiedName + "::" + function.name : function.qualifiedName;
            if (!ControlledCallableName(callableName)) continue;
            if (function.returnType is not StableId result) throw new InvalidOperationException("A native callable omitted its parsed return type.");
            if (function.addressability == NativeCallableAddressability.DirectCallOnly)
            {
                NativeType returned = module.GetType(result);
                HashSet<StableId> visited = [];
                while (returned.category == NativeTypeCategory.Alias && returned.elementType is StableId underlying && visited.Add(returned.id)) returned = module.GetType(underlying);
                if (cpp || !module.triple.Contains("windows-msvc", StringComparison.Ordinal) || owner?.kind == "Class"
                    || function.addressabilityEvidence?.level != EvidenceLevel.Declared || returned.category != NativeTypeCategory.Primitive || returned.primitiveKind != "Void"
                    || function.children.Select(module.GetDeclaration).Any(child => child.kind == "Parameter") || function.isVariadic
                    || function.callingConvention is not (null or "C" or "Default"))
                    throw new NotSupportedException("A direct-call-only native callable currently requires an independently declared Windows MSVC C void(void) intrinsic contract: " + function.qualifiedName);
                // Taking an intrinsic's address is invalid. A matching redeclaration
                // checks this bounded void/zero-argument shape; the storage emitter
                // promotes MSVC's intrinsic return/arity diagnostics before headers.
                checks.Append("extern ").Append(Render(result, Convention(function.callingConvention) + " " + callableName + "(void)")).AppendLine(";");
                continue;
            }
            string alias = "bgcs_verified_callable_" + (index++).ToString(CultureInfo.InvariantCulture);
            string pointer = "(" + Convention(function.callingConvention) + (cpp && owner?.kind == "Class" && !function.isStatic ? " " + owner.qualifiedName + "::*" : " *") + alias + ")";
            string parameters = string.Join(", ", function.children.Select(module.GetDeclaration).Where(child => child.kind == "Parameter")
                .Select(parameter => parameter.type is StableId type ? Render(type, string.Empty) : throw new InvalidOperationException("A native callable parameter omitted its parsed type.")));
            if (function.isVariadic) parameters += parameters.Length == 0 ? "..." : ", ...";
            else if (parameters.Length == 0) parameters = "void";
            pointer += "(" + parameters + ")";
            if (cpp && owner?.kind == "Class" && !function.isStatic)
            {
                if (function.isConst) pointer += " const";
                if (function.isVolatile) pointer += " volatile";
                pointer += function.referenceQualifier switch
                {
                    NativeReferenceQualifier.None => string.Empty,
                    NativeReferenceQualifier.LValue => " &",
                    NativeReferenceQualifier.RValue => " &&",
                    _ => throw new NotSupportedException("The native member reference qualifier has no exact assertion form.")
                };
            }
            checks.Append("typedef ").Append(Render(result, pointer)).AppendLine(";");
            if (cpp)
                checks.Append("static_assert(std::is_same<decltype(static_cast<").Append(alias).Append(">(&").Append(callableName)
                    .Append(")), ").Append(alias).AppendLine(">::value, \"BGCS native callable signature differs from parsed facts\");");
            else
                checks.Append("_Static_assert(_Generic(&").Append(function.qualifiedName).Append(", ").Append(alias)
                    .AppendLine(": 1, default: 0), \"BGCS native callable signature differs from parsed facts\");");
        }
        StringBuilder text = new();
        if (cpp && index != 0) text.AppendLine("#include <type_traits>");
        foreach (string convention in m_conventions.Order(StringComparer.Ordinal)) EmitConvention(text, convention);
        return text.Append(checks).ToString();
    }

    private string Render(
        StableId identity,
        string declarator,
        string qualifiers = "",
        int depth = 0
    ) {
        if (depth > 128) throw new InvalidOperationException("A native callable declarator exceeded its type graph depth limit.");
        NativeType type = module.GetType(identity);
        switch (type.category)
        {
            case NativeTypeCategory.Qualified:
                string qualifier = type.qualifier switch { "Const" => "const", "Volatile" => "volatile", _ => throw new NotSupportedException("The native callable qualifier has no exact assertion form.") };
                return Render(Element(), declarator, (qualifiers + " " + qualifier).Trim(), depth + 1);
            case NativeTypeCategory.Pointer:
            case NativeTypeCategory.Reference:
                string operatorToken = type.category == NativeTypeCategory.Pointer ? "*" : type.isRValueReference ? "&&" : "&";
                string nested = operatorToken + qualifiers + " " + declarator;
                NativeType element = module.GetType(Element());
                if (element.category is NativeTypeCategory.Array or NativeTypeCategory.Function) nested = "(" + nested + ")";
                return Render(Element(), nested, string.Empty, depth + 1);
            case NativeTypeCategory.Array:
                return Render(Element(), declarator + "[" + type.arrayLength?.ToString(CultureInfo.InvariantCulture) + "]", qualifiers, depth + 1);
            case NativeTypeCategory.Function:
                if (!type.hasPrototype || type.returnType is null) throw new NotSupportedException("An unspecified native function pointer cannot be independently verified.");
                string convention = Convention(type.callingConvention);
                string name = declarator.StartsWith('(') ? declarator.Insert(1, convention + " ") : convention + " " + declarator;
                string arguments = string.Join(", ", type.parameterTypes.Select(parameter => Render(parameter, string.Empty, depth: depth + 1)));
                if (type.isVariadic) arguments += arguments.Length == 0 ? "..." : ", ...";
                else if (arguments.Length == 0) arguments = "void";
                return Render(type.returnType.Value, name + "(" + arguments + ")", qualifiers, depth + 1);
            case NativeTypeCategory.Alias:
                // The live typedef can change under different native flags. Freeze the parsed
                // underlying graph so an int-to-float alias change cannot redefine the expected ABI.
                return Render(Element(), declarator, qualifiers, depth + 1);
            case NativeTypeCategory.Primitive:
            case NativeTypeCategory.Record:
            case NativeTypeCategory.Enumeration:
                string spelling = module.language == SourceLanguage.C && type.primitiveKind == "Bool" ? "_Bool" : type.spelling;
                if (spelling.Any(character => character is ';' or '{' or '}' or '\r' or '\n' or '\0')) throw new ArgumentException("The native callable type cannot be represented by a controlled declarator.");
                return (qualifiers + " " + spelling + " " + declarator).Trim();
            default:
                throw new NotSupportedException("The native callable type has no independent exact signature assertion: " + type.spelling);
        }
        StableId Element() => type.elementType ?? throw new InvalidOperationException("The native callable type omitted its element identity.");
    }

    private bool Accessible(NativeDeclaration declaration)
        => declaration.visibility is not ("Private" or "Protected") && (declaration.parent is null || Accessible(module.GetDeclaration(declaration.parent.Value)));

    private bool HasConcreteAncestors(NativeDeclaration declaration)
    {
        NativeDeclaration? current = declaration.parent is StableId parent ? module.GetDeclaration(parent) : null;
        while (current is not null)
        {
            // An unselected primary/partial template has no callable address. Specialization identities
            // are emitted using the concrete owning declaration instead of the frontend's parent-name shorthand.
            if (!current.templateParameters.IsEmpty || current.templateKind is "TemplateClass" or "PartialTemplateClass" or "ObjCGenericClass") return false;
            current = current.parent is StableId ancestor ? module.GetDeclaration(ancestor) : null;
        }
        return true;
    }

    private static bool ControlledCallableName(string name)
    {
        int operation = name.LastIndexOf("::operator", StringComparison.Ordinal);
        if (operation >= 0)
        {
            string operationName = name[(operation + "::operator".Length)..].Trim();
            if (operationName is "+" or "-" or "*" or "/" or "%" or "^" or "&" or "|" or "~" or "!" or "=" or "<" or ">"
                or "+=" or "-=" or "*=" or "/=" or "%=" or "^=" or "&=" or "|=" or "<<" or ">>" or "<<=" or ">>="
                or "==" or "!=" or "<=" or ">=" or "<=>" or "&&" or "||" or "++" or "--" or "," or "->*" or "->" or "()" or "[]"
                or "new" or "new[]" or "delete" or "delete[]" or "co_await")
                return ControlledCallableName(name[..operation]);
            return ControlledCallableName(name[..operation]) && ControlledCallableName(operationName);
        }
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_: <>,*&]*$", RegexOptions.CultureInvariant)) return false;
        int nesting = 0;
        foreach (char character in name)
        {
            if (character == '<') nesting++;
            else if (character == '>' && --nesting < 0) return false;
        }
        return nesting == 0;
    }

    private string Convention(string? convention)
    {
        string normalized = convention is null or "Default" ? "C" : convention;
        if (normalized is not ("C" or "X86StdCall" or "X86FastCall" or "X86ThisCall" or "X86VectorCall" or "Win64" or "X86_64SysV"))
            throw new NotSupportedException("The native calling convention has no implemented independent callable assertion: " + normalized);
        m_conventions.Add(normalized);
        return "BGCS_VERIFY_CC_" + normalized;
    }

    private void EmitConvention(
        StringBuilder text,
        string convention
    ) {
        string token = "BGCS_VERIFY_CC_" + convention;
        string msvc = convention switch { "X86StdCall" => "__stdcall", "X86FastCall" => "__fastcall", "X86ThisCall" => "__thiscall", "X86VectorCall" => "__vectorcall", "X86_64SysV" => "BGCS_UNSUPPORTED_MSVC_SYSV", _ => "__cdecl" };
        string gnu = convention switch { "C" => "cdecl", "X86StdCall" => "stdcall", "X86FastCall" => "fastcall", "X86ThisCall" => "thiscall", "X86VectorCall" => "vectorcall", "Win64" => "ms_abi", "X86_64SysV" => "sysv_abi", _ => throw new InvalidOperationException() };
        text.AppendLine("#if defined(_MSC_VER)").Append("#define ").Append(token).Append(' ').AppendLine(msvc);
        text.AppendLine("#elif defined(__GNUC__) || defined(__clang__)");
        if (convention == "C")
        {
            text.AppendLine("#if defined(__i386__)").Append("#define ").Append(token).AppendLine(" __attribute__((cdecl))");
            if (module.triple.StartsWith("x86_64", StringComparison.Ordinal))
                text.AppendLine("#elif defined(__x86_64__)").Append("#define ").Append(token).Append(" __attribute__((")
                    .Append(module.triple.Contains("windows", StringComparison.Ordinal) ? "ms_abi" : "sysv_abi").AppendLine("))");
            text.AppendLine("#else").Append("#define ").AppendLine(token).AppendLine("#endif");
        }
        else text.Append("#define ").Append(token).Append(" __attribute__((").Append(gnu).AppendLine("))");
        text.AppendLine("#else\n#error BGCS native compiler cannot independently verify callable conventions\n#endif");
    }
}
