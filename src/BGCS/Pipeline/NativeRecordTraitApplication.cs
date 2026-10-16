using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;
using BGCS.NativeBuild.Validation;

namespace BGCS;

internal static class NativeRecordTraitApplication
{
    internal static NativeModule Apply(
        NativeModule module,
        NativeRecordTraitObservation observation
    ) {
        if (observation.facts.IsDefault)
            throw new InvalidDataException("The selected native compiler returned an uninitialized record-trait observation.");
        Dictionary<StableId, NativeRecordTraitFact> facts = [];
        foreach (NativeRecordTraitFact fact in observation.facts)
        {
            if (!facts.TryAdd(fact.declaration, fact))
                throw new InvalidDataException("The selected native compiler repeated a record-trait identity.");
            NativeDeclaration declaration = module.GetDeclaration(fact.declaration);
            if (declaration.kind != "Class" || !declaration.isCompleteDefinition || !declaration.templateParameters.IsEmpty
                || declaration.type is not StableId type || module.GetType(type).category != NativeTypeCategory.Record)
                throw new InvalidDataException("The selected native compiler returned traits for an incomplete or nonconcrete record.");
        }
        if (facts.Count == 0) return module;
        ImmutableArray<NativeDeclaration> declarations = module.declarations.Select(declaration => facts.TryGetValue(declaration.id, out NativeRecordTraitFact? fact)
            ? declaration with
            {
                isDefaultConstructible = fact.isDefaultConstructible,
                isStandardLayout = fact.isStandardLayout,
                isTriviallyCopyable = fact.isTriviallyCopyable
            } : declaration).ToImmutableArray();
        return new(module.targetId, module.triple, module.pointerSize, module.types, declarations, module.diagnostics)
        { language = module.language, byteOrder = module.byteOrder };
    }
}
