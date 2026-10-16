using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Interop;

internal static class NativeObjectiveCFactVerifier
{
    internal static void Validate(
        NativeModule module,
        NativeDeclaration declaration,
        ISet<StableId> types,
        ISet<StableId> declarations,
        Action<string> error
    ) {
        if (declaration.objectiveCContainer is null && declaration.objectiveCMethod is null && declaration.objectiveCProperty is null) return;
        if (module.language != SourceLanguage.ObjC) Error("Objective-C facts require an Objective-C source module.");
        if (declaration.objectiveCContainer is { } container)
        {
            if (declaration.kind != "Class" || !Enum.IsDefined(container.kind) || container.kind == NativeObjectiveCContainerKind.Unknown)
                Error("A container requires an exact interface, protocol or category declaration.");
            CheckEvidence(container.evidence);
            if (container.protocols.IsDefault || container.genericParameters.IsDefault) Error("Objective-C relationship collections cannot be uninitialized.");
            else
            {
                if (container.protocols.Distinct().Count() != container.protocols.Length || container.genericParameters.Distinct().Count() != container.genericParameters.Length)
                    Error("Objective-C relationship identities cannot be repeated.");
                foreach (StableId protocol in container.protocols) Container(protocol, NativeObjectiveCContainerKind.Protocol);
                foreach (StableId parameter in container.genericParameters)
                    if (!types.Contains(parameter) || module.GetType(parameter).category != NativeTypeCategory.Template)
                        Error("An Objective-C generic parameter must reference its exact native parameter type.");
            }
            if (container.baseInterface is StableId baseInterface) Container(baseInterface, NativeObjectiveCContainerKind.Interface);
            if (container.categoryTarget is StableId target) Container(target, NativeObjectiveCContainerKind.Interface);
            if (container.kind == NativeObjectiveCContainerKind.Category)
            {
                if (container.categoryTarget is null || container.categoryName is null) Error("A category must retain its target interface and category name.");
            }
            else if (container.categoryTarget is not null || container.categoryName is not null) Error("Only a category may declare a category target or name.");
            if (container.kind == NativeObjectiveCContainerKind.Protocol && (container.baseInterface is not null || !container.genericParameters.IsDefaultOrEmpty))
                Error("A protocol does not use interface inheritance or interface generic parameter facts.");
            if (!declaration.baseClasses.IsDefaultOrEmpty || !declaration.templateParameters.IsDefaultOrEmpty || !declaration.templateArguments.IsDefaultOrEmpty)
                Error("Objective-C relationships must not be encoded as C++ inheritance or templates.");
        }
        if (declaration.objectiveCMethod is { } method)
        {
            CheckEvidence(method.evidence);
            if (declaration.kind != "Function" || string.IsNullOrWhiteSpace(method.selector) || method.selector != declaration.name
                || method.dispatch is not (NativeObjectiveCDispatchKind.Instance or NativeObjectiveCDispatchKind.Class))
                Error("A method requires its exact selector and instance/class receiver domain.");
            Parent();
            if (declaration.linkage != "Objective-C" || declaration.callingConvention is not null || declaration.addressability != NativeCallableAddressability.Unknown)
                Error("An Objective-C selector must not claim an ordinary C function linkage, calling convention or address expression.");
            if (!string.IsNullOrEmpty(method.selector) && !declaration.children.IsDefault && declaration.children.All(declarations.Contains)
                && method.selector.Count(character => character == ':') != declaration.children.Select(module.GetDeclaration).Count(child => child.kind == "Parameter"))
                Error("A method selector must retain one colon for each declared parameter.");
        }
        if (declaration.objectiveCProperty is { } property)
        {
            CheckEvidence(property.evidence);
            if (declaration.kind != "Property" || declaration.type is not StableId type || !types.Contains(type) || string.IsNullOrWhiteSpace(property.getterSelector))
                Error("A property requires its exact native value type and getter selector.");
            Parent();
            if (property.attributes is NativeObjectiveCPropertyAttributes attributes && ((int)attributes & ~8191) != 0)
                Error("A property contains unrecognized neutral attribute bits.");
            if (property.attributes is { } access && access.HasFlag(NativeObjectiveCPropertyAttributes.ReadOnly | NativeObjectiveCPropertyAttributes.ReadWrite))
                Error("A property cannot be both readonly and readwrite.");
            Accessor(property.getter, property.getterSelector, setter: false);
            Accessor(property.setter, property.setterSelector, setter: true);
            if (property.attributes is { } flags && flags.HasFlag(NativeObjectiveCPropertyAttributes.ReadOnly) && property.setter is not null)
                Error("A readonly property cannot claim a setter.");
        }

        void Error(string message) => error($"Objective-C declaration '{declaration.qualifiedName}': {message}");
        void CheckEvidence(Evidence evidence)
        {
            if (evidence is not { level: EvidenceLevel.Declared or EvidenceLevel.Verified } || string.IsNullOrWhiteSpace(evidence.origin))
                Error("A language fact requires compiler declaration provenance.");
        }
        void Container(
            StableId id,
            NativeObjectiveCContainerKind kind
        ) {
            if (id == declaration.id || !declarations.Contains(id) || module.GetDeclaration(id).objectiveCContainer?.kind != kind)
                Error("A container relationship references a missing or incompatible declaration.");
        }
        void Parent()
        {
            if (declaration.parent is not StableId parent || !declarations.Contains(parent) || module.GetDeclaration(parent).objectiveCContainer is null)
                Error("A method or property must retain its owning Objective-C container.");
        }
        void Accessor(
            StableId? identity,
            string? selector,
            bool setter
        ) {
            if (identity is not StableId id) return;
            if (!declarations.Contains(id)) { Error("A property references a missing accessor."); return; }
            NativeDeclaration accessor = module.GetDeclaration(id);
            if (accessor.parent != declaration.parent || accessor.objectiveCMethod is not { } method || method.selector != selector)
                Error("A property accessor must retain its exact parent and selector.");
            else if (declaration.objectiveCProperty!.attributes is { } flags
                && flags.HasFlag(NativeObjectiveCPropertyAttributes.Class) != (method.dispatch == NativeObjectiveCDispatchKind.Class))
                Error("A property accessor has an incompatible instance/class receiver domain.");
            if (accessor.children.IsDefault || accessor.children.Any(child => !declarations.Contains(child))) return;
            NativeDeclaration[] parameters = accessor.children.Select(module.GetDeclaration).Where(child => child.kind == "Parameter").ToArray();
            if (!setter && (parameters.Length != 0 || accessor.returnType != declaration.type)
                || setter && (parameters.Length != 1 || parameters[0].type != declaration.type))
                Error("A property accessor lost its exact native value type or parameter count.");
        }
    }
}
