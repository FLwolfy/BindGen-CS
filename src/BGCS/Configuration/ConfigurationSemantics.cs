using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Configuration;

internal static class ConfigurationSemantics
{
    internal static InteropModule Apply(
        InteropModule module,
        BindingConfiguration configuration,
        string origin
    ) {
        ImmutableArray<InteropFunction> functions = module.functions;
        var semanticOmissions = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        foreach (BorrowedParameterConfiguration rule in configuration.semantics.borrowedParameters)
        {
            InteropFunction function = Select(rule.function);
            InteropParameter[] matches = function.parameters.Where(parameter => parameter.name == rule.parameter || parameter.sourceName == rule.parameter).ToArray();
            if (matches.Length != 1) throw new ArgumentException("A borrowed parameter must select one exact native parameter.");
            InteropParameter parameter = matches[0];
            if (Resolve(parameter.type).category != NativeTypeCategory.Pointer) throw new ArgumentException("A borrowed address contract requires an actual native pointer.");
            if (parameter.ownership is { kind: not NativeOwnershipKind.Borrowed }) throw new ArgumentException("A borrowed parameter cannot overwrite an established ownership transfer.");
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:borrowed:" + rule.function + ":" + rule.parameter);
            OwnershipContract contract = new(NativeOwnershipKind.Borrowed, new(rule.lifetime, null, evidence), evidence) { retainsPointer = rule.retainsPointer };
            functions = functions.SetItem(functions.IndexOf(function), function with { parameters = function.parameters.SetItem(function.parameters.IndexOf(parameter), parameter with { ownership = contract }) });
        }
        foreach (OwnedResultConfiguration rule in configuration.semantics.ownedResults)
        {
            InteropFunction function = Select(rule.function);
            int functionIndex = functions.IndexOf(function);
            int resultIndex = function.error?.valueParameter is StableId resultId
                ? Array.FindIndex(function.parameters.ToArray(), parameter => parameter.id == resultId) : -1;
            OwnershipContract? previous = resultIndex >= 0 ? function.parameters[resultIndex].ownership : function.returnOwnership;
            StableId release = rule.releaseFunction is not null ? Select(rule.releaseFunction).id
                : previous?.releaseFunction ?? throw new ArgumentException($"Owned result '{rule.function}' requires an explicit matching release function.");
            StableId allocator = rule.releaseDomain is not null ? new("configured-release-domain:" + rule.releaseDomain)
                : previous?.allocator ?? throw new ArgumentException($"Owned result '{rule.function}' requires an explicit allocation and release domain.");
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:owned-result:" + rule.function);
            StableId owner = resultIndex >= 0 ? function.parameters[resultIndex].id : function.id;
            OwnershipContract ownership = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, owner, evidence), evidence)
            { releaseFunction = release, allocator = allocator, releaseThreadDomain = rule.releaseThreadDomain };
            functions = functions.SetItem(functionIndex, resultIndex >= 0
                ? function with { parameters = function.parameters.SetItem(resultIndex, function.parameters[resultIndex] with { ownership = ownership }) }
                : function with { returnOwnership = ownership });
        }
        foreach (BorrowedResultConfiguration rule in configuration.semantics.borrowedResults)
        {
            InteropFunction function = Select(rule.function);
            if (function.error is not null || Resolve(function.returnType).category != NativeTypeCategory.Pointer)
                throw new ArgumentException("A borrowed result must select an actual direct native pointer return.");
            if (function.returnOwnership is { } previous && (previous.kind != NativeOwnershipKind.Borrowed
                || previous.lifetime.kind != rule.lifetime || previous.releaseFunction is not null || previous.allocator is not null))
                throw new ArgumentException("A borrowed result cannot overwrite an established ownership or lifetime protocol.");
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:borrowed-result:" + rule.function);
            functions = functions.SetItem(functions.IndexOf(function), function with
            { returnOwnership = new(NativeOwnershipKind.Borrowed, new(rule.lifetime, null, evidence), evidence) });
        }
        foreach (StringParameterConfiguration rule in configuration.semantics.stringParameters)
        {
            InteropFunction function = Select(rule.function);
            int functionIndex = functions.IndexOf(function);
            InteropParameter[] matches = function.parameters.Where(parameter => parameter.name == rule.parameter || parameter.sourceName == rule.parameter).ToArray();
            if (matches.Length != 1) throw new ArgumentException($"Text parameter '{rule.function}:{rule.parameter}' must identify exactly one native parameter.");
            int parameterIndex = function.parameters.IndexOf(matches[0]);
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:" + rule.function + ":" + rule.parameter);
            StableId? length = rule.lengthParameter is null ? null : function.parameters.Single(parameter => parameter.name == rule.lengthParameter || parameter.sourceName == rule.lengthParameter).id;
            StringContract text = new(rule.encoding, rule.codeUnitWidth, rule.lengthUnit, rule.direction, new(rule.lifetime, null, evidence), evidence)
            {
                byteOrder = rule.byteOrder,
                zeroTerminated = rule.zeroTerminated,
                retainsPointer = rule.retainsPointer,
                allowsNull = rule.allowsNull,
                lengthParameter = length,
                lengthDirection = rule.lengthDirection,
                maximumCodeUnits = rule.maximumCodeUnits
            };
            functions = functions.SetItem(functionIndex, function with { parameters = function.parameters.SetItem(parameterIndex, function.parameters[parameterIndex] with { text = text }) });
        }
        foreach (StringResultConfiguration rule in configuration.semantics.stringResults)
        {
            InteropFunction function = Select(rule.function);
            if (function.returnString is not null) throw new ArgumentException("A string result cannot overwrite an established native text contract.");
            if (function.error is not null || function.asynchronous is not null || Resolve(function.returnType).category != NativeTypeCategory.Pointer)
            {
                semanticOmissions.Add(new(BindingDiagnosticSeverity.Warning,
                    $"Friendly string result '{function.exportName}' was omitted: direct text copying requires a synchronous native pointer result; the existing raw status, async or scalar ABI remains available.",
                    "BGCS-FRIENDLY-OMITTED"));
                continue;
            }
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:string-result:" + rule.function);
            StableId? length = null;
            if (rule.lengthParameter is not null)
            {
                InteropParameter[] matches = function.parameters.Where(parameter => parameter.name == rule.lengthParameter || parameter.sourceName == rule.lengthParameter).ToArray();
                if (matches.Length != 1) throw new ArgumentException("A native string result length must identify exactly one parameter.");
                length = matches[0].id;
            }
            int width = rule.codeUnitWidth ?? (rule.encoding switch
            { NativeTextEncoding.Utf8 => 1, NativeTextEncoding.Utf16 => 2, NativeTextEncoding.Utf32 => 4, _ => 0 });
            LifetimeContract lifetime = function.returnOwnership?.lifetime ?? new(NativeLifetimeKind.Unknown, null, evidence);
            StringContract text = new(rule.encoding, width, rule.lengthUnit, NativeParameterDirection.Output, lifetime, evidence)
            {
                byteOrder = rule.byteOrder,
                zeroTerminated = rule.zeroTerminated,
                allowsNull = rule.allowsNull,
                fixedLength = rule.fixedLength,
                lengthParameter = length,
                lengthDirection = rule.lengthDirection,
                lengthOutputCallBounded = rule.lengthOutputCallBounded,
                maximumCodeUnits = rule.maximumCodeUnits,
                ownership = function.returnOwnership
            };
            functions = functions.SetItem(functions.IndexOf(function), function with { returnString = text });
        }
        foreach (ValueParameterConfiguration rule in configuration.semantics.valueParameters)
        {
            InteropFunction function = Select(rule.function);
            InteropParameter[] parameters = function.parameters.Where(parameter => parameter.name == rule.parameter || parameter.sourceName == rule.parameter).ToArray();
            if (parameters.Length != 1) throw new ArgumentException($"Value parameter '{rule.function}:{rule.parameter}' must identify exactly one native parameter.");
            InteropParameter parameter = parameters[0];
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:value:" + rule.function + ":" + rule.parameter);
            ValueParameterContract value = new(rule.direction, new(rule.lifetime, null, evidence), evidence)
            { retainsPointer = rule.retainsPointer, allowsNull = rule.allowsNull, requiredAlignment = rule.requiredAlignment };
            functions = functions.SetItem(functions.IndexOf(function), function with { parameters = function.parameters.SetItem(function.parameters.IndexOf(parameter), parameter with { singleValue = value }) });
        }
        foreach (BufferParameterConfiguration rule in configuration.semantics.bufferParameters)
        {
            InteropFunction function = Select(rule.function);
            InteropParameter[] matches = function.parameters.Where(parameter => parameter.name == rule.parameter || parameter.sourceName == rule.parameter).ToArray();
            if (matches.Length != 1) throw new ArgumentException($"Buffer parameter '{rule.function}:{rule.parameter}' must identify exactly one native parameter.");
            InteropParameter parameter = matches[0];
            InteropType pointer = Resolve(parameter.type);
            if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element)
                throw new ArgumentException("A native buffer contract requires a pointer with a known element type.");
            StableId? Related(string? name)
            {
                if (name is null) return null;
                InteropParameter[] related = function.parameters.Where(candidate => candidate.name == name || candidate.sourceName == name).ToArray();
                if (related.Length != 1) throw new ArgumentException("A native buffer extent must identify exactly one parameter.");
                return related[0].id;
            }
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:buffer:" + rule.function + ":" + rule.parameter);
            BufferContract buffer = new(element, rule.lengthUnit, rule.direction, new(rule.lifetime, null, evidence), evidence)
            {
                lengthParameter = Related(rule.lengthParameter),
                capacityParameter = Related(rule.capacityParameter),
                writtenCountParameter = Related(rule.writtenCountParameter),
                retainsPointer = rule.retainsPointer,
                allowsNull = rule.allowsNull,
                requiredAlignment = rule.requiredAlignment
            };
            functions = functions.SetItem(functions.IndexOf(function), function with { parameters = function.parameters.SetItem(function.parameters.IndexOf(parameter), parameter with { buffer = buffer }) });
        }
        functions = CallbackConfigurationBinder.Apply(module, functions, configuration.semantics.callbackParameters, origin, Select);
        functions = AsyncConfigurationBinder.Apply(module, functions, configuration.semantics.asyncOperations, origin, Select);
        InteropModule annotated = new(module.libraryId, module.targetId, module.pointerSize, module.types, functions, module.diagnostics)
        { byteOrder = module.byteOrder, constants = module.constants, virtualProxies = module.virtualProxies, functionAliases = module.functionAliases, stringConstants = module.stringConstants };
        var diagnostics = module.diagnostics.ToBuilder();
        diagnostics.AddRange(semanticOmissions);
        foreach (InteropFunction function in functions)
        {
            foreach (InteropParameter parameter in function.parameters.Where(parameter => parameter.text is not null))
            {
                string? reason = new FriendlyApiEligibilityAnalyzer().GetTextInputRestriction(annotated, function, parameter);
                if (reason is not null) diagnostics.Add(new(BindingDiagnosticSeverity.Warning, $"Friendly text input '{function.exportName}:{parameter.name}' was omitted: {reason}", "BGCS-FRIENDLY-OMITTED"));
            }
            foreach (InteropParameter parameter in function.parameters.Where(parameter => parameter.buffer is not null))
            {
                string? reason = new BufferProtocolAnalyzer().GetRestriction(annotated, function, parameter);
                if (reason is not null) diagnostics.Add(new(BindingDiagnosticSeverity.Warning, $"Friendly buffer '{function.exportName}:{parameter.name}' was omitted: {reason}", "BGCS-FRIENDLY-OMITTED"));
            }
            foreach (InteropParameter parameter in function.parameters.Where(parameter => parameter.singleValue is not null))
            {
                string? reason = new ValueParameterAnalyzer().GetRestriction(annotated, function, parameter);
                if (reason is not null) diagnostics.Add(new(BindingDiagnosticSeverity.Warning, $"Friendly value '{function.exportName}:{parameter.name}' was omitted: {reason}", "BGCS-FRIENDLY-OMITTED"));
            }
        }
        return new(module.libraryId, module.targetId, module.pointerSize, module.types, functions, diagnostics.ToImmutable())
        { byteOrder = module.byteOrder, constants = module.constants, virtualProxies = module.virtualProxies, functionAliases = module.functionAliases, stringConstants = module.stringConstants };

        InteropFunction Select(string name)
        {
            InteropFunction[] matches = functions.Where(function => function.exportName == name).ToArray();
            if (matches.Length == 0) matches = functions.Where(function => function.inputAdapterOf is null && function.sourceDeclaration?.value == name).ToArray();
            string sourceName = name.TrimStart(':');
            if (matches.Length == 0) matches = functions.Where(function => function.inputAdapterOf is null && function.sourceSignature?.TrimStart(':') == sourceName).ToArray();
            if (matches.Length == 0) matches = functions.Where(function => function.inputAdapterOf is null && function.sourceQualifiedName?.TrimStart(':') == sourceName).ToArray();
            if (matches.Length == 0 && name.StartsWith("::", StringComparison.Ordinal)) matches = functions.Where(function => function.exportName == sourceName).ToArray();
            if (matches.Length != 1) throw new ArgumentException($"Semantic callable '{name}' must identify exactly one selected native export; found {matches.Length}.");
            return matches[0];
        }
        InteropType Resolve(StableId id)
        {
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
            return type;
        }
    }
}
