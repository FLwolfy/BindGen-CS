using System;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Recognizes exact default standard values whose cleanup may remain inside one synchronous creating call.</summary>
public static class StandardValueReleaseFacts
{
    /// <summary>Preserves unknown cleanup for missing template facts, custom traits/allocators and unrelated configured families.</summary>
    /// <param name="source">The original detached native graph.</param>
    /// <param name="owner">The exact native value class or an ordinary alias.</param>
    /// <returns>A creating-call contract for default standard basic_string or filesystem::path, otherwise null.</returns>
    public static NativeValueReleaseContract? Read(
        NativeModule source,
        StableId owner
    ) {
        ArgumentNullException.ThrowIfNull(source);
        NativeType type = StandardLibraryFactReader.Resolve(source, owner);
        if (type.category != NativeTypeCategory.Record || type.declaration is not StableId declarationId) return null;
        NativeDeclaration declaration = source.GetDeclaration(declarationId);
        if (!declaration.isCompleteDefinition) return null;
        StandardValueOperation? family = new StandardLibraryFactReader().Read(source, type.id);
        if (family?.family == StandardValueKind.Path)
            return Contract("standard-filesystem-path-creating-call:");
        if (family?.family != StandardValueKind.String || declaration.templateArguments.Length != 3
            || declaration.templateArguments[0].type is not StableId unitId
            || declaration.templateArguments[1].type is not StableId traitsId
            || declaration.templateArguments[2].type is not StableId allocatorId) return null;
        NativeType unit = StandardLibraryFactReader.Resolve(source, unitId);
        if (unit.category != NativeTypeCategory.Primitive || unit.size is not (1 or 2 or 4) || unit.isSigned is null
            || unit.primitiveKind is "Void" or "Bool" or "Float" or "Double" or "LongDouble"
            || !Specialization(traitsId, "std::char_traits", unit.id) || !Specialization(allocatorId, "std::allocator", unit.id)) return null;
        return Contract("standard-basic-string-default-allocation-creating-call:");

        bool Specialization(
            StableId id,
            string name,
            StableId element
        ) {
            NativeType actual = StandardLibraryFactReader.Resolve(source, id);
            if (actual.category != NativeTypeCategory.Record || actual.declaration is not StableId declared
                || CppFamilyBindingValidator.NormalizeName(actual.spelling.Split('<', 2)[0].Trim()) != name) return false;
            NativeDeclaration facts = source.GetDeclaration(declared);
            return facts.templateArguments.Length == 1 && facts.templateArguments[0].type is StableId argument
                && StandardLibraryFactReader.Resolve(source, argument).id == element;
        }
        NativeValueReleaseContract Contract(string origin) => new(NativeValueReleaseScope.CreatingCall,
            new(EvidenceLevel.Declared, origin + type.id.value));
    }
}
