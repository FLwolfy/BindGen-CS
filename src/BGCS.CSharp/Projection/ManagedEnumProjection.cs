using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Projection;

/// <summary>Projects explicitly named integral aliases as public enums while retaining exact native raw carriers.</summary>
public sealed class ManagedEnumProjection
{
    /// <summary>Applies declared enum conversions without modifying the backing native ABI.</summary>
    /// <param name="model">The current immutable managed presentation.</param>
    /// <param name="request">Declared enum definitions and exact native type selectors.</param>
    /// <param name="cancellationToken">Cancels type and export checks.</param>
    /// <returns>A new presentation with explicit enum conversions and declarations.</returns>
    public ManagedApiModel Apply(
        ManagedApiModel model,
        ManagedProjectionRequest request,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.enumMappings);
        ImmutableArray<CSharpManagedEnumPlan> enums = new ManagedEnumPlanner().Plan(request.enums, cancellationToken);
        Dictionary<string, CSharpManagedEnumPlan> definitions = enums.ToDictionary(definition => definition.name.TrimStart('@'), StringComparer.Ordinal);
        Dictionary<StableId, ManagedRecipeProjection> conversions = [];
        foreach ((string selector, string name) in request.enumMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(selector);
            string enumName = CSharpNames.Identifier(name).TrimStart('@');
            if (!definitions.TryGetValue(enumName, out CSharpManagedEnumPlan? definition))
                throw new ArgumentException("An enum mapping references an undeclared managed enum.");
            InteropType[] candidates = model.interop.types.Where(type => type.nativeName == selector || type.sourceQualifiedName == selector || type.declarationName == selector).ToArray();
            if (candidates.Length != 1) throw new ArgumentException("A native enum mapping must select exactly one detached native type: " + selector);
            InteropType selected = candidates[0];
            InteropType scalar = Underlying(selected);
            if (scalar.category != NativeTypeCategory.Primitive
                || scalar.primitiveKind is not ("Char" or "UnsignedChar" or "Short" or "UnsignedShort" or "Int" or "UnsignedInt" or "Long" or "UnsignedLong" or "LongLong" or "UnsignedLongLong" or "WChar" or "Char8" or "Char16" or "Char32")
                || scalar.size != definition.size || scalar.isSigned != definition.isSigned)
                throw new NotSupportedException("A managed enum mapping requires the same proven native integral width and signedness; bool, pointers and existing native enum carriers require their own projection.");
            string qualified = CSharpGeneratedTypeScope.QualifiedReference(model, definition.name);
            if (!conversions.TryAdd(selected.id, new(qualified, "(" + definition.underlyingType + ")({value})", "(" + qualified + ")({value})")))
                throw new ArgumentException("Two managed enum rules selected the same native type.");
        }
        Dictionary<StableId, InteropFunction> functions = model.interop.functions.ToDictionary(function => function.id);
        ImmutableArray<ManagedMember>.Builder members = ImmutableArray.CreateBuilder<ManagedMember>();
        foreach (ManagedMember member in model.methods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!functions.TryGetValue(member.nativeExport, out InteropFunction? function)) throw new InvalidOperationException("A managed enum projection lost its native export.");
            ImmutableArray<ManagedValueProjection>.Builder values = member.valueProjections.ToBuilder();
            foreach (InteropParameter parameter in function.parameters)
            {
                ManagedRecipeProjection? projection = Conversion(parameter.type);
                if (projection is null) continue;
                if (values.Any(value => value.nativeParameter == parameter.id)) throw new ArgumentException("A managed enum mapping conflicts with an existing parameter conversion.");
                values.Add(new(parameter.id, projection));
            }
            ManagedRecipeProjection? result = Conversion(function.returnType);
            if (result is not null && member.returnProjection is not null) throw new ArgumentException("A managed enum mapping conflicts with an existing return conversion.");
            members.Add(member with { valueProjections = values.ToImmutable(), returnProjection = result ?? member.returnProjection });
        }
        return model with { enums = request.enums, methods = members.ToImmutable() };

        InteropType Underlying(InteropType type)
        {
            HashSet<StableId> visited = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("An enum mapping encountered an incomplete or cyclic native alias.");
                type = model.interop.GetType(element);
            }
            return type;
        }

        ManagedRecipeProjection? Conversion(StableId id)
        {
            HashSet<StableId> visited = [];
            while (true)
            {
                if (!visited.Add(id)) throw new InvalidOperationException("An enum conversion encountered a cyclic native alias.");
                if (conversions.TryGetValue(id, out ManagedRecipeProjection? projection)) return projection;
                InteropType type = model.interop.GetType(id);
                if (type.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified) || type.elementType is not StableId element) return null;
                id = element;
            }
        }
    }
}
