using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Analysis.Bridge;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Cpp2C.Planning;

/// <summary>Generates finite native virtual adapters and raw callback ABI contracts from detached facts.</summary>
public sealed class VirtualCallbackPlanner
{
    /// <summary>Plans a concrete native proxy whose callback slots cannot propagate managed exceptions across C.</summary>
    /// <param name="module">The original detached C++ declarations.</param>
    /// <param name="request">The exact explicitly selected native interface.</param>
    /// <returns>The typed slot contracts and content-identified declarations passed through a second ordinary worker parse.</returns>
    public CppVirtualProxyPlan Plan(
        NativeModule module,
        VirtualCallbackRequest request
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.id);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.nativeInterface);
        if (request.releaseThreadDomain is not ("unknown" or "any"))
            throw new ArgumentException("A virtual callback cleanup thread must be unknown or the explicit any-thread assertion.", nameof(request));
        if (!Regex.IsMatch(request.id, "^[A-Za-z][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("A virtual callback identity must be a portable stable name.", nameof(request));
        NativeDeclaration owner = module.declarations.SingleOrDefault(declaration => declaration.kind == "Class" && declaration.qualifiedName == request.nativeInterface)
            ?? throw new ArgumentException("The explicitly selected virtual interface is absent from the native graph.", nameof(request));
        if (owner.type is not StableId ownerType || !owner.isCompleteDefinition || !owner.isAbstract || owner.templateParameters.Length != 0 || owner.visibility is "Private" or "Protected")
            throw new NotSupportedException("A virtual callback proxy needs a complete, accessible, concrete abstract interface specialization.");
        Dictionary<string, NativeDeclaration> virtuals = new(StringComparer.Ordinal);
        HashSet<StableId> visited = [];
        Collect(owner);
        NativeDeclaration[] methods = virtuals.Values.Where(method => method.isAbstract && method.role != "Destructor").OrderBy(method => method.id.value, StringComparer.Ordinal).ToArray();
        if (methods.Length == 0) throw new NotSupportedException("The virtual interface has no detached pure virtual callback slots.");
        string suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.id + "|" + owner.id.value)))[..16];
        string className = "Proxy_" + suffix, factoryName = "Create_" + suffix;
        string callMacro = "BGCS_CALLBACK_CALL_" + suffix.ToUpperInvariant();
        ImmutableArray<VirtualCallbackSlot>.Builder slots = ImmutableArray.CreateBuilder<VirtualCallbackSlot>();
        StringBuilder source = new("#include <cstdint>\n#include <memory>\n#include <stdexcept>\n#include <utility>\n");
        source.Append("#if defined(_WIN32)\n#define ").Append(callMacro).Append(" __cdecl\n#else\n#define ").Append(callMacro).Append("\n#endif\n")
            .AppendLine("namespace BGCSGeneratedCallbacks {").Append("class ").Append(className).Append(" final : public ::").Append(owner.qualifiedName).AppendLine(" {")
            .AppendLine("    void* m_userData;");
        for (int slotIndex = 0; slotIndex < methods.Length; slotIndex++)
        {
            NativeDeclaration method = methods[slotIndex];
            if (method.isVariadic || !method.hasPrototype || method.templateParameters.Length != 0 || method.name.StartsWith("operator ", StringComparison.Ordinal))
                throw new NotSupportedException($"Virtual callback '{method.qualifiedName}' needs an explicitly supported fixed native prototype.");
            if (method.isNoThrow is not false)
                throw new NotSupportedException($"Virtual callback '{method.qualifiedName}' has a nonthrowing or unresolved exception contract; the status-failure proxy requires an explicitly throwing native method.");
            if (!Enum.IsDefined(method.referenceQualifier) || method.referenceQualifier == NativeReferenceQualifier.Unknown)
                throw new NotSupportedException($"Virtual callback '{method.qualifiedName}' has an unresolved native reference qualification.");
            NativeDeclaration[] parameters = method.children.Select(module.GetDeclaration).Where(parameter => parameter.kind == "Parameter").ToArray();
            ImmutableArray<VirtualCallbackValue> arguments = parameters.Select(parameter => Value(parameter.type!.Value, false)).ToImmutableArray();
            VirtualCallbackValue? result = method.returnType is StableId resultId && Resolve(resultId).primitiveKind != "Void" ? Value(resultId, true) : null;
            string slotName = "slot" + slotIndex.ToString(CultureInfo.InvariantCulture);
            slots.Add(new(method.id, slotName, arguments, result));
            source.Append("    using Callback").Append(slotIndex).Append(" = int32_t (").Append(callMacro).Append(" *)(void*");
            foreach (VirtualCallbackValue parameter in arguments) source.Append(", ").Append(parameter.cType);
            if (result is not null) source.Append(", ").Append(result.cType).Append('*');
            source.AppendLine(");").Append("    Callback").Append(slotIndex).Append(" m_").Append(slotName).AppendLine(";");
        }
        source.AppendLine("public:").Append("    explicit ").Append(className).Append("(void* userData");
        foreach (VirtualCallbackSlot slot in slots) source.Append(", void* ").Append(slot.name);
        source.Append(") : m_userData(userData)");
        for (int index = 0; index < slots.Count; index++) source.Append(", m_").Append(slots[index].name).Append("(reinterpret_cast<Callback").Append(index).Append(">(").Append(slots[index].name).Append("))");
        source.AppendLine(" {");
        foreach (VirtualCallbackSlot slot in slots) source.Append("        if (m_").Append(slot.name).AppendLine(" == nullptr) throw std::invalid_argument(\"Missing BGCS virtual callback slot\");");
        source.AppendLine("    }").Append("    ").Append(className).Append('(').Append(className).AppendLine("&&) noexcept = default;")
            .Append("    ").Append(className).Append("(const ").Append(className).AppendLine("&) = delete;");
        for (int index = 0; index < methods.Length; index++)
        {
            NativeDeclaration method = methods[index];
            NativeDeclaration[] parameters = method.children.Select(module.GetDeclaration).Where(parameter => parameter.kind == "Parameter").ToArray();
            VirtualCallbackSlot slot = slots[index];
            string returnType = method.returnType is StableId returnId ? NativeTypeSpelling.Format(module, returnId) : "void";
            source.Append("    ").Append(returnType).Append(' ').Append(method.name).Append('(')
                .Append(string.Join(", ", parameters.Select((
                    parameter,
                    parameterIndex
                ) => NativeTypeSpelling.Format(module, parameter.type!.Value) + " bgcs_arg" + parameterIndex.ToString(CultureInfo.InvariantCulture))))
                .Append(')').Append(method.isConst ? " const" : string.Empty)
                .Append(method.isVolatile ? " volatile" : string.Empty)
                .Append(method.referenceQualifier switch { NativeReferenceQualifier.RValue => " &&", NativeReferenceQualifier.LValue => " &", _ => string.Empty })
                .AppendLine(" noexcept(false) override {");
            if (slot.result is not null) source.Append("        ").Append(slot.result.cType).AppendLine(" bgcs_result{};");
            source.Append("        int32_t bgcs_status = m_").Append(slot.name).Append("(m_userData");
            for (int argument = 0; argument < slot.parameters.Length; argument++) source.Append(", ").Append(NativeArgument(parameters[argument].type!.Value, slot.parameters[argument].cType, "bgcs_arg" + argument.ToString(CultureInfo.InvariantCulture)));
            if (slot.result is not null) source.Append(", &bgcs_result");
            source.AppendLine(");").AppendLine("        if (bgcs_status != 0) throw std::runtime_error(\"Managed virtual callback reported failure\");");
            if (slot.result is not null)
            {
                NativeType native = Resolve(slot.result.nativeType);
                source.Append("        return ").Append(native.category == NativeTypeCategory.Pointer ? "reinterpret_cast<" : "static_cast<")
                    .Append(NativeTypeSpelling.Format(module, slot.result.nativeType)).AppendLine(">(bgcs_result);");
            }
            source.AppendLine("    }");
        }
        source.AppendLine("};").Append("inline ").Append(className).Append(' ').Append(factoryName).Append("(void* userData");
        foreach (VirtualCallbackSlot slot in slots) source.Append(", void* ").Append(slot.name);
        source.Append(") { return ").Append(className).Append("(userData");
        foreach (VirtualCallbackSlot slot in slots) source.Append(", ").Append(slot.name);
        source.AppendLine("); }").AppendLine("}").Append("#undef ").AppendLine(callMacro);
        string text = source.ToString();
        return new(request.id, ownerType, "BGCSGeneratedCallbacks::" + className, "BGCSGeneratedCallbacks::" + factoryName, slots.ToImmutable(),
            new("typed-virtual-proxy:" + request.id, SourceFragmentKind.TranslationUnit, text, new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))))
            {
                declarationDependencies = methods.Select(method => method.id).Prepend(owner.id).ToImmutableArray(),
                typeDependencies = slots.SelectMany(slot => slot.parameters.Select(parameter => parameter.nativeType)
                .Concat(slot.result is null ? [] : new[] { slot.result.nativeType })).Prepend(ownerType).Distinct().ToImmutableArray(),
                evidence = new(EvidenceLevel.Declared, "typed-virtual-proxy:" + request.id)
            })
        {
            releaseThreadDomain = request.releaseThreadDomain,
            cleanupEvidence = request.releaseThreadDomain == "any" ? new(EvidenceLevel.UserAsserted, "virtual-proxy-cleanup-thread:" + request.id) : new(EvidenceLevel.Unknown, "virtual-proxy-cleanup-thread:" + request.id)
        };

        NativeType Resolve(StableId id) => StandardLibraryFactReader.Resolve(module, id);
        void Collect(NativeDeclaration declaration)
        {
            if (!visited.Add(declaration.id)) return;
            foreach (NativeDeclaration method in declaration.children.Select(module.GetDeclaration).Where(child => child.kind == "Function" && child.isVirtual))
            {
                string signature = method.name + "(" + string.Join(",", method.children.Select(module.GetDeclaration).Where(child => child.kind == "Parameter")
                    .Select(parameter => ParameterIdentity(parameter.type!.Value))) + ")" + method.isConst + method.isVolatile + method.referenceQualifier;
                virtuals.TryAdd(signature, method);
            }
            foreach (NativeBaseClass baseClass in declaration.baseClasses)
                if (Resolve(baseClass.type).declaration is StableId parent) Collect(module.GetDeclaration(parent));
        }
        string ParameterIdentity(StableId id)
        {
            // Top-level parameter cv does not distinguish an override; nested
            // pointer/reference qualifiers still belong to its exact signature.
            HashSet<StableId> visitedTypes = [];
            NativeType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visitedTypes.Add(type.id) || type.elementType is not StableId element)
                    throw new InvalidOperationException("A virtual callback parameter has a missing or cyclic canonical type.");
                type = module.GetType(element);
            }
            return new BGCS.Analysis.Types.NativeTypeFormatter().Format(module, type.id, preserveAliases: false);
        }
        VirtualCallbackValue Value(
            StableId id,
            bool result
        ) {
            NativeType native = Resolve(id);
            if (native.category == NativeTypeCategory.Pointer)
            {
                if (native.elementType is StableId element && Resolve(element).category == NativeTypeCategory.Function)
                    throw new NotSupportedException("A callback function-pointer argument needs an explicit exact callback ABI projection.");
                return new(id, IsConst(native.elementType!.Value) ? "const void*" : "void*");
            }
            if (!result && native.category is NativeTypeCategory.Record or NativeTypeCategory.Reference)
                return new(id, native.category == NativeTypeCategory.Record || IsConst(native.elementType!.Value) ? "const void*" : "void*");
            if (native.category == NativeTypeCategory.Enumeration) native = Resolve(native.elementType!.Value);
            string scalar = native.primitiveKind switch
            {
                "Bool" when native.size == 1 => "uint8_t",
                "Float" when native.size == 4 => "float",
                "Double" when native.size == 8 => "double",
                _ when native.category == NativeTypeCategory.Primitive && native.size is 1 or 2 or 4 or 8 && native.isSigned is bool signed
                    => (signed ? "int" : "uint") + (native.size.Value * 8).ToString(CultureInfo.InvariantCulture) + "_t",
                _ => throw new NotSupportedException("A virtual callback return or scalar argument needs a supported fixed native representation or an explicit custom callback lowering.")
            };
            return new(id, scalar);
        }
        bool IsConst(StableId id)
        {
            NativeType type = module.GetType(id);
            return type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
                || type.category is NativeTypeCategory.Qualified or NativeTypeCategory.Alias && type.elementType is StableId element && IsConst(element);
        }
        string NativeArgument(
            StableId typeId,
            string carrier,
            string name
        ) {
            NativeType type = Resolve(typeId);
            return type.category switch
            {
                NativeTypeCategory.Pointer => "reinterpret_cast<" + carrier + ">(" + name + ")",
                NativeTypeCategory.Record or NativeTypeCategory.Reference => "static_cast<" + carrier + ">(std::addressof(" + name + "))",
                _ => "static_cast<" + carrier + ">(" + name + ")"
            };
        }
    }
}
